using Himo.Services;

namespace Himo.Platforms.Android.Services;

public sealed class NotificationService : INotificationService
{
    public const string MessageChannelId = "himo_messages_v5";
    // Versioned because Android does not allow changing sound/audio attributes on an existing channel.
    public const string CallsChannelId = "himo_calls_v3";
    private const string ChannelName = "رسائل Himo";
    private const string ChannelDescription = "إشعارات الرسائل الجديدة في Himo";
    private const int NotificationIdBase = 12000;
    private const int CallNotificationIdBase = 240000;
    private const string EnabledPreferenceKey = "himo_notifications_enabled";
    private const string FinishedCallIdsPreferenceKey = "himo_finished_call_ids";
    private static readonly object FinishedCallIdsGate = new();
    private static readonly HashSet<string> ActiveCallRingtoneIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object BackgroundCallRingtoneGate = new();
    private static global::Android.Media.Ringtone? _backgroundCallRingtone;
    private static System.Threading.CancellationTokenSource? _backgroundCallRingtoneCts;
    private static string? _backgroundCallRingtoneKey;
    private static string? _backgroundCallRingtoneCallId;
    private const int BackgroundCallRingTimeoutMs = 45_000;
    public static event Action<string, string>? IncomingCallTerminatedByPush;
    public static event Action<string, string, string>? IncomingCallAnsweredByPush;

    public static void NotifyIncomingCallTerminatedByPush(string conversationId, string callId)
    {
        try { IncomingCallTerminatedByPush?.Invoke(conversationId ?? string.Empty, callId ?? string.Empty); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Push] Call termination listener failed: {ex.Message}"); }
    }

    public static void NotifyIncomingCallAnsweredByPush(string conversationId, string callId, string state)
    {
        try { IncomingCallAnsweredByPush?.Invoke(conversationId ?? string.Empty, callId ?? string.Empty, state ?? string.Empty); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Push] Call-answer listener failed: {ex.Message}"); }
    }

    public bool IsEnabled => Preferences.Default.Get(EnabledPreferenceKey, true);

    public Task InitializeAsync()
    {
        EnsureChannels();
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        Preferences.Set(EnabledPreferenceKey, enabled);
        if (!enabled)
            return ClearAllAsync();

        EnsureChannels();
        return Task.CompletedTask;
    }

    public Task ShowMessageAsync(string senderName, string message, string conversationId)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(conversationId))
            return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        if (context is null)
            return Task.CompletedTask;

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                context,
                global::Android.Manifest.Permission.PostNotifications) != global::Android.Content.PM.Permission.Granted)
        {
            System.Diagnostics.Debug.WriteLine("[Himo Push] Notification permission is not granted.");
            return Task.CompletedTask;
        }

        EnsureChannels();

        var title = string.IsNullOrWhiteSpace(senderName) ? "رسالة جديدة" : senderName.Trim();
        var text = string.IsNullOrWhiteSpace(message) ? "لديك رسالة جديدة" : message.Trim();
        var notificationManager = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
        if (notificationManager is null)
            return Task.CompletedTask;

        if (!notificationManager.AreNotificationsEnabled())
        {
            System.Diagnostics.Debug.WriteLine("[Himo Push] Android notifications are disabled for Himo.");
            return Task.CompletedTask;
        }

        var notificationId = GetNotificationId(conversationId);

        var intent = new global::Android.Content.Intent(context, typeof(MainActivity));
        intent.SetAction(global::Android.Content.Intent.ActionView);
        intent.SetFlags(global::Android.Content.ActivityFlags.SingleTop | global::Android.Content.ActivityFlags.ClearTop);
        intent.PutExtra("conversation_id", conversationId);

        var pendingIntentFlags = global::Android.App.PendingIntentFlags.UpdateCurrent;
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.M)
            pendingIntentFlags |= global::Android.App.PendingIntentFlags.Immutable;

        var pendingIntent = global::Android.App.PendingIntent.GetActivity(
            context,
            notificationId,
            intent,
            pendingIntentFlags);

        if (pendingIntent is null)
            return Task.CompletedTask;

        var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, MessageChannelId);
        builder.SetSmallIcon(Resource.Drawable.himo_notification);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetStyle(new global::AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(text));
        builder.SetPriority(global::AndroidX.Core.App.NotificationCompat.PriorityHigh);
        builder.SetCategory(global::AndroidX.Core.App.NotificationCompat.CategoryMessage);
        builder.SetAutoCancel(true);
        builder.SetOnlyAlertOnce(false);
        builder.SetContentIntent(pendingIntent);
        builder.SetVisibility(global::AndroidX.Core.App.NotificationCompat.VisibilityPrivate);

        var notification = builder.Build();
        if (notification is null)
            return Task.CompletedTask;

        notificationManager.Notify(notificationId, notification);

        return Task.CompletedTask;
    }

    // Incoming calls deliberately do not post a visible notification or launch an Activity.
    // Save the invite for MainActivity to open CallPage when the user opens Himo, and play
    // the normal ringtone while the app is backgrounded.
    public Task ShowIncomingCallAsync(string callerName, string conversationId, string mode, string callId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Task.CompletedTask;

        var normalizedMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        var normalizedCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D")
            : string.Empty;
        if (IsCallRingingSuppressed(normalizedCallId))
            return Task.CompletedTask;

        var dedupeKey = string.IsNullOrWhiteSpace(normalizedCallId) ? conversationId : normalizedCallId;
        lock (FinishedCallIdsGate)
        {
            if (IsCallRingingSuppressed(normalizedCallId))
                return Task.CompletedTask;
            if (!ActiveCallRingtoneIds.Add(dedupeKey))
            {
                lock (BackgroundCallRingtoneGate)
                {
                    if (string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase) &&
                        _backgroundCallRingtone is not null)
                        return Task.CompletedTask;
                }
            }
        }

        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            lock (FinishedCallIdsGate) ActiveCallRingtoneIds.Remove(dedupeKey);
            return Task.CompletedTask;
        }

        try
        {
            global::Himo.MainActivity.SetPendingCall(conversationId, normalizedMode, normalizedCallId);
            StartBackgroundIncomingCallRingtone(context, dedupeKey, conversationId, normalizedCallId);
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Ring-only invite stored; no call notification posted. callId={normalizedCallId}");
        }
        catch (Exception ex)
        {
            StopBackgroundIncomingCallRingtone(normalizedCallId, dedupeKey);
            lock (FinishedCallIdsGate) ActiveCallRingtoneIds.Remove(dedupeKey);
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Could not start background ringtone: {ex}");
        }

        return Task.CompletedTask;
    }

    public static void StopBackgroundIncomingCallRingtone()
        => StopBackgroundIncomingCallRingtone(null, null);

    private static void StartBackgroundIncomingCallRingtone(
        global::Android.Content.Context context,
        string dedupeKey,
        string conversationId,
        string callId)
    {
        lock (BackgroundCallRingtoneGate)
        {
            if (string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase) &&
                _backgroundCallRingtone is not null)
                return;

            StopBackgroundIncomingCallRingtoneLocked();
            var ringtoneUri = global::Android.Media.RingtoneManager.GetDefaultUri(
                global::Android.Media.RingtoneType.Ringtone);
            if (ringtoneUri is null)
                throw new InvalidOperationException("Android did not provide a default ringtone URI.");

            var ringtone = global::Android.Media.RingtoneManager.GetRingtone(context, ringtoneUri);
            if (ringtone is null)
                throw new InvalidOperationException("Android could not create a ringtone player.");

            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(28))
                    typeof(global::Android.Media.Ringtone).GetProperty("IsLooping")?.SetValue(ringtone, true);
            }
            catch { }

            _backgroundCallRingtone = ringtone;
            _backgroundCallRingtoneKey = dedupeKey;
            _backgroundCallRingtoneCallId = callId;
            _backgroundCallRingtoneCts = new System.Threading.CancellationTokenSource();
            ringtone.Play();
            _ = MaintainBackgroundIncomingCallRingtoneAsync(
                dedupeKey, conversationId, callId, _backgroundCallRingtoneCts.Token);
        }
    }

    private static async Task MaintainBackgroundIncomingCallRingtoneAsync(
        string dedupeKey,
        string conversationId,
        string callId,
        System.Threading.CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(BackgroundCallRingTimeoutMs);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                lock (BackgroundCallRingtoneGate)
                {
                    if (!string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase))
                        return;
                    if (_backgroundCallRingtone is not null && !_backgroundCallRingtone.IsPlaying)
                        _backgroundCallRingtone.Play();
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Background ringtone monitor failed: {ex.Message}");
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        lock (FinishedCallIdsGate)
        {
            if (!ActiveCallRingtoneIds.Remove(dedupeKey))
                return;
        }
        RememberCallAsFinished(callId);
        StopBackgroundIncomingCallRingtone(callId, dedupeKey);
        global::Himo.MainActivity.ClearPendingCallIfMatches(conversationId, callId);
        System.Diagnostics.Debug.WriteLine($"[Himo Call] Background invite expired after ringtone timeout. callId={callId}");
    }

    private static void StopBackgroundIncomingCallRingtone(string? callId, string? dedupeKey)
    {
        lock (BackgroundCallRingtoneGate)
        {
            if (!string.IsNullOrWhiteSpace(callId) &&
                !string.IsNullOrWhiteSpace(_backgroundCallRingtoneCallId) &&
                !string.Equals(_backgroundCallRingtoneCallId, callId, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.IsNullOrWhiteSpace(dedupeKey) &&
                !string.IsNullOrWhiteSpace(_backgroundCallRingtoneKey) &&
                !string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase))
                return;
            StopBackgroundIncomingCallRingtoneLocked();
        }
    }

    private static void StopBackgroundIncomingCallRingtoneLocked()
    {
        try { _backgroundCallRingtoneCts?.Cancel(); } catch { }
        try { _backgroundCallRingtoneCts?.Dispose(); } catch { }
        _backgroundCallRingtoneCts = null;
        try { _backgroundCallRingtone?.Stop(); } catch { }
        try { _backgroundCallRingtone?.Dispose(); } catch { }
        _backgroundCallRingtone = null;
        _backgroundCallRingtoneKey = null;
        _backgroundCallRingtoneCallId = null;
    }

    public static void ClearExistingCallNotifications()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (context is null) return;
            var compat = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
            var platformManager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;

            // Remove prior-build call alerts, without touching message notifications.
            // Some .NET Android workload versions do not expose ActiveNotifications
            // as a compile-time property, so inspect the binding at runtime instead.
            if (OperatingSystem.IsAndroidVersionAtLeast(23) && platformManager is not null)
            {
                try
                {
                    object? activeNotificationsObject = null;
                    var managerType = platformManager.GetType();
                    var activeProperty = managerType.GetProperty(
                        "ActiveNotifications",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    if (activeProperty is not null)
                        activeNotificationsObject = activeProperty.GetValue(platformManager);

                    if (activeNotificationsObject is null)
                    {
                        var activeMethod = managerType.GetMethod("GetActiveNotifications", Type.EmptyTypes);
                        if (activeMethod is not null)
                            activeNotificationsObject = activeMethod.Invoke(platformManager, null);
                    }

                    if (activeNotificationsObject is System.Collections.IEnumerable activeNotifications)
                    {
                        foreach (var item in activeNotifications)
                        {
                            if (item is null) continue;
                            var itemType = item.GetType();
                            var tag = itemType.GetProperty("Tag")?.GetValue(item) as string;
                            var idObject = itemType.GetProperty("Id")?.GetValue(item);
                            if (idObject is null || !int.TryParse(Convert.ToString(idObject), out var id))
                                continue;

                            var notification = itemType.GetProperty("Notification")?.GetValue(item);
                            var channelId = OperatingSystem.IsAndroidVersionAtLeast(26)
                                ? notification?.GetType().GetProperty("ChannelId")?.GetValue(notification) as string
                                : null;
                            var oldCallTag = (tag ?? string.Empty).StartsWith("himo-call-", StringComparison.OrdinalIgnoreCase);
                            if (oldCallTag || string.Equals(channelId, CallsChannelId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(channelId, "himo_calls_v2", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(channelId, "himo_calls_v1", StringComparison.OrdinalIgnoreCase))
                                compat?.Cancel(tag, id);
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Himo Call] Runtime lookup for old call notifications was unavailable: {cleanupEx.Message}");
                }
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(26) && platformManager is not null)
            {
                try { platformManager.DeleteNotificationChannel(CallsChannelId); } catch { }
                try { platformManager.DeleteNotificationChannel("himo_calls_v2"); } catch { }
                try { platformManager.DeleteNotificationChannel("himo_calls_v1"); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Could not clear legacy call notifications: {ex.Message}");
        }
    }    public Task ClearIncomingCallAsync(string conversationId, string callId, bool suppressFurtherRinging = true)
    {
        var normalizedCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D")
            : string.Empty;
        var dedupeKey = string.IsNullOrWhiteSpace(normalizedCallId) ? conversationId : normalizedCallId;

        try
        {
            var context = global::Android.App.Application.Context;
            var manager = context is null
                ? null
                : global::AndroidX.Core.App.NotificationManagerCompat.From(context);

            lock (FinishedCallIdsGate)
            {
                if (suppressFurtherRinging && !string.IsNullOrWhiteSpace(normalizedCallId))
                    RememberCallAsFinished(normalizedCallId);

                ActiveCallRingtoneIds.Remove(dedupeKey);
                if (manager is not null)
                {
                    manager.Cancel(GetCallNotificationId(normalizedCallId, conversationId));
                    if (!string.IsNullOrWhiteSpace(conversationId))
                        manager.Cancel($"himo-call-{conversationId}", 0);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Clearing incoming call state failed: {ex.Message}");
        }

        StopBackgroundIncomingCallRingtone(normalizedCallId, dedupeKey);
        global::Himo.MainActivity.ClearPendingCallIfMatches(conversationId, normalizedCallId);
        return Task.CompletedTask;
    }
    public bool IsIncomingCallSuppressed(string callId) => IsCallRingingSuppressed(callId);

    private static bool IsCallRingingSuppressed(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId)) return false;
        lock (FinishedCallIdsGate)
        {
            return Preferences.Default.Get(FinishedCallIdsPreferenceKey, string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(callId, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void RememberCallAsFinished(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId)) return;
        lock (FinishedCallIdsGate)
        {
            var values = Preferences.Default.Get(FinishedCallIdsPreferenceKey, string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.Equals(x, callId, StringComparison.OrdinalIgnoreCase))
                .Append(callId)
                .TakeLast(64);
            Preferences.Default.Set(FinishedCallIdsPreferenceKey, string.Join('|', values));
        }
    }

    private static int GetCallNotificationId(string callId, string conversationId)
    {
        unchecked
        {
            var source = !string.IsNullOrWhiteSpace(callId) ? callId : conversationId;
            var hash = 23;
            foreach (var ch in source) hash = (hash * 31) + ch;
            return CallNotificationIdBase + (hash & 0x3fffffff);
        }
    }

    public Task ClearConversationAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        if (context is null)
            return Task.CompletedTask;

        var notificationManager = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
        if (notificationManager is null)
            return Task.CompletedTask;

        notificationManager.Cancel(GetNotificationId(conversationId));
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
            return Task.CompletedTask;

        var notificationManager = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
        if (notificationManager is null)
            return Task.CompletedTask;

        notificationManager.CancelAll();
        lock (FinishedCallIdsGate) ActiveCallRingtoneIds.Clear();
        return Task.CompletedTask;
    }

    private static void EnsureChannels()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        try
        {
            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null)
                return;

            var channel = new global::Android.App.NotificationChannel(
                MessageChannelId,
                ChannelName,
                global::Android.App.NotificationImportance.High)
            {
                Description = ChannelDescription
            };
            channel.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification),
                null);
            manager.CreateNotificationChannel(channel);

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Notification channel initialization failed: {ex}");
        }
    }

    private static int GetNotificationId(string conversationId)
    {
        unchecked
        {
            if (Guid.TryParse(conversationId, out var remoteId))
            {
                var bytes = remoteId.ToByteArray();
                var hash = 17;
                foreach (var b in bytes)
                    hash = (hash * 31) + b;
                return NotificationIdBase + (hash & 0x3fffffff);
            }

            var fallback = 17;
            foreach (var ch in conversationId)
                fallback = (fallback * 31) + ch;
            return NotificationIdBase + (fallback & 0x3fffffff);
        }
    }
}
