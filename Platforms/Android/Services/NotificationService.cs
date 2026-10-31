using Himo.Services;
using Microsoft.Maui.Storage;

namespace Himo.Platforms.Android.Services;

public sealed class NotificationService : INotificationService
{
    public const string MessageChannelId = "himo_messages_v5";
    // Versioned because Android does not allow changing sound/audio attributes on an existing channel.
    public const string CallsChannelId = "himo_calls_v5";
    private const string ChannelName = "رسائل Himo";
    private const string ChannelDescription = "إشعارات الرسائل الجديدة في Himo";
    private const string CallsChannelName = "المكالمات الواردة";
    private const string CallsChannelDescription = "تنبيهات صوتية ومرئية للمكالمات الواردة";
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
        if (IsGroupNotificationMuted(conversationId, "message-muted"))
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

        var messageChannelId = GetGroupNotificationChannel(context, conversationId, isCall: false);
        var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, messageChannelId);
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
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var tone = GetGroupToneUri(context, conversationId, isCall: false);
            if (tone is not null) builder.SetSound(tone);
            var vibration = GetGroupVibrationPattern(conversationId, isCall: false);
            if (vibration is not null) builder.SetVibrate(vibration);
        }

        var notification = builder.Build();
        if (notification is null)
            return Task.CompletedTask;

        notificationManager.Notify(notificationId, notification);

        return Task.CompletedTask;
    }

    // Show a real Android call notification as well as ringing while the app is backgrounded.
    public Task ShowIncomingCallAsync(string callerName, string conversationId, string mode, string callId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return Task.CompletedTask;
        // Muting a group call silences its alert, but must not hide the incoming call
        // notification or prevent the user from opening the call screen.
        var callMuted = IsGroupNotificationMuted(conversationId, "call-muted");

        var normalizedMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        var normalizedCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D") : string.Empty;
        if (IsCallRingingSuppressed(normalizedCallId)) return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        if (context is null) return Task.CompletedTask;

        var dedupeKey = string.IsNullOrWhiteSpace(normalizedCallId) ? conversationId : normalizedCallId;
        var callToneUri = callMuted ? null : GetGroupToneUri(context, conversationId, isCall: true);
        var callVibration = callMuted ? Array.Empty<long>() : GetGroupVibrationPattern(conversationId, isCall: true);
        try
        {
            EnsureChannels();
            var callChannelId = GetGroupNotificationChannel(context, conversationId, isCall: true);
            global::Himo.MainActivity.SetPendingCall(conversationId, normalizedMode, normalizedCallId);

            var notificationManager = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
            if (notificationManager is null || !notificationManager.AreNotificationsEnabled())
                System.Diagnostics.Debug.WriteLine("[Himo Call] Android notifications are disabled; ringtone fallback will still run.");
            else if (!OperatingSystem.IsAndroidVersionAtLeast(33) ||
                global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.PostNotifications) == global::Android.Content.PM.Permission.Granted)
            {
                var notificationId = GetCallNotificationId(normalizedCallId, conversationId);
                var intent = new global::Android.Content.Intent(context, typeof(MainActivity));
                intent.SetAction("com.companyname.himo.INCOMING_CALL");
                intent.SetFlags(global::Android.Content.ActivityFlags.SingleTop | global::Android.Content.ActivityFlags.ClearTop | global::Android.Content.ActivityFlags.NewTask);
                intent.PutExtra("call_type", "invite");
                intent.PutExtra("conversation_id", conversationId);
                intent.PutExtra("call_mode", normalizedMode);
                intent.PutExtra("call_id", normalizedCallId);
                var flags = global::Android.App.PendingIntentFlags.UpdateCurrent;
                if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.M)
                    flags |= global::Android.App.PendingIntentFlags.Immutable;
                var pendingIntent = global::Android.App.PendingIntent.GetActivity(context, notificationId, intent, flags);

                var title = string.IsNullOrWhiteSpace(callerName) ? "مكالمة واردة" : callerName.Trim();
                var body = normalizedMode == "video" ? "مكالمة فيديو واردة — اضغط للرد" : "مكالمة صوتية واردة — اضغط للرد";
                var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, callChannelId);
                builder.SetSmallIcon(Resource.Drawable.himo_notification);
                builder.SetContentTitle(title);
                builder.SetContentText(body);
                builder.SetStyle(new global::AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(body));
                builder.SetSubText("Himo • مكالمة واردة");
                builder.SetWhen(Java.Lang.JavaSystem.CurrentTimeMillis());
                builder.SetShowWhen(true);
                builder.SetCategory(global::AndroidX.Core.App.NotificationCompat.CategoryCall);
                builder.SetPriority(global::AndroidX.Core.App.NotificationCompat.PriorityMax);
                builder.SetVisibility(global::AndroidX.Core.App.NotificationCompat.VisibilityPublic);
                builder.SetOngoing(true);
                builder.SetAutoCancel(false);
                // FCM may redeliver the same high-priority data push. Alert only once
                // for this CallId, and let the same 45-second call timeout remove stale alerts.
                builder.SetOnlyAlertOnce(true);
                builder.SetTimeoutAfter(45_000);
                builder.SetContentIntent(pendingIntent);
                // With full-screen access, show the incoming call surface over the lock screen;
                // otherwise Android retains this as a high-priority heads-up/tray notification.
                builder.SetFullScreenIntent(pendingIntent, true);
                if (callVibration is { Length: > 0 }) builder.SetVibrate(callVibration);
                if (!OperatingSystem.IsAndroidVersionAtLeast(26) && callToneUri is not null)
                    builder.SetSound(callToneUri);
                notificationManager?.Notify(notificationId, builder.Build());
                System.Diagnostics.Debug.WriteLine($"[Himo Call] Incoming-call notification posted; callId={normalizedCallId}; mode={normalizedMode}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Himo Call] POST_NOTIFICATIONS permission missing; cannot post call notification.");
            }

            // Keep a looping ringtone as a fallback unless this group is muted on this device.
            if (!callMuted)
            {
                lock (FinishedCallIdsGate)
                {
                    if (IsCallRingingSuppressed(normalizedCallId)) return Task.CompletedTask;
                    ActiveCallRingtoneIds.Add(dedupeKey);
                }
                StartBackgroundIncomingCallRingtone(context, dedupeKey, conversationId, normalizedCallId, callToneUri);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Call] Incoming call notification shown silently for muted group {conversationId}.");
            }
        }
        catch (Exception ex)
        {
            StopBackgroundIncomingCallRingtone(normalizedCallId, dedupeKey);
            lock (FinishedCallIdsGate) ActiveCallRingtoneIds.Remove(dedupeKey);
            System.Diagnostics.Debug.WriteLine($"[Himo Call] Could not post/ring incoming call: {ex}");
        }
        return Task.CompletedTask;
    }

    public static void StopBackgroundIncomingCallRingtone()
        => StopBackgroundIncomingCallRingtone(null, null);

    private static void StartBackgroundIncomingCallRingtone(
        global::Android.Content.Context context,
        string dedupeKey,
        string conversationId,
        string callId,
        global::Android.Net.Uri? ringtoneUri)
    {
        lock (BackgroundCallRingtoneGate)
        {
            if (string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase) &&
                _backgroundCallRingtone is not null)
                return;

            StopBackgroundIncomingCallRingtoneLocked();
            ringtoneUri ??= global::Android.Media.RingtoneManager.GetDefaultUri(
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
                            if (oldCallTag || string.Equals(channelId, "himo_calls_v4", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(channelId, "himo_calls_v3", StringComparison.OrdinalIgnoreCase) ||
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
                try { platformManager.DeleteNotificationChannel("himo_calls_v4"); } catch { }
                try { platformManager.DeleteNotificationChannel("himo_calls_v3"); } catch { }
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

    private static bool IsGroupNotificationMuted(string conversationId, string setting)
    {
        if (!Guid.TryParse(conversationId, out var groupId) || groupId == Guid.Empty) return false;
        return Preferences.Default.Get($"himo_group_{groupId:N}_{setting}", false);
    }

    public static bool IsGroupCallMutedForConversation(string conversationId) =>
        IsGroupNotificationMuted(conversationId, "call-muted");

    public static global::Android.Net.Uri? GetGroupCallToneUriForConversation(
        global::Android.Content.Context context, string conversationId) =>
        GetGroupToneUri(context, conversationId, isCall: true);

    private static string GetGroupNotificationChannel(
        global::Android.Content.Context context,
        string conversationId,
        bool isCall)
    {
        var defaultChannel = isCall ? CallsChannelId : MessageChannelId;
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) ||
            !Guid.TryParse(conversationId, out var groupId) || groupId == Guid.Empty)
            return defaultChannel;

        var prefix = $"himo_group_{groupId:N}_";
        var toneKey = prefix + (isCall ? "call-tone" : "message-tone");
        var vibrationKey = prefix + (isCall ? "call-vibration" : "message-vibration");
        var tone = Preferences.Default.Get(toneKey, "الافتراضية");
        var vibration = Preferences.Default.Get(vibrationKey, "تلقائي");
        var muted = isCall && IsGroupNotificationMuted(conversationId, "call-muted");
        if (tone == "الافتراضية" && vibration == "تلقائي" && !muted)
            return defaultChannel;

        var toneCode = ToneCode(tone);
        var vibrationCode = VibrationCode(vibration);
        var channelId = $"himo_{(isCall ? "gc" : "gm")}_{groupId:N}_{toneCode}_{vibrationCode}_{(muted ? 1 : 0)}";
        try
        {
            var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null) return defaultChannel;
            if (manager.GetNotificationChannel(channelId) is not null) return channelId;

            var channel = new global::Android.App.NotificationChannel(
                channelId,
                isCall ? "مكالمات المجموعة" : "رسائل المجموعة",
                isCall ? global::Android.App.NotificationImportance.Max : global::Android.App.NotificationImportance.High);
            channel.Description = isCall ? "تنبيهات مكالمات هذه المجموعة" : "تنبيهات رسائل هذه المجموعة";
            if (isCall)
                channel.LockscreenVisibility = global::Android.App.NotificationVisibility.Public;

            var sound = muted ? null : GetGroupToneUri(context, conversationId, isCall);
            channel.SetSound(sound, null);

            var pattern = muted ? Array.Empty<long>() : GetGroupVibrationPattern(conversationId, isCall);
            channel.EnableVibration(pattern is { Length: > 0 });
            if (pattern is { Length: > 0 }) channel.SetVibrationPattern(pattern);
            manager.CreateNotificationChannel(channel);
            return channelId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Group notification channel setup failed: {ex.Message}");
            return defaultChannel;
        }
    }

    private static int ToneCode(string tone) => tone switch
    {
        "هادئة" => 1,
        "نغمة 1" or "رنين 1" => 2,
        "نغمة 2" or "رنين 2" => 3,
        "نغمة 3" or "رنين 3" => 4,
        "رنين 4" => 5,
        _ => 0
    };

    private static int VibrationCode(string vibration) => vibration switch
    {
        "إيقاف" => 1,
        "قصير" => 2,
        "طويل" => 3,
        _ => 0
    };

    private static global::Android.Net.Uri? GetGroupToneUri(
        global::Android.Content.Context context,
        string conversationId,
        bool isCall)
    {
        var defaultType = isCall
            ? global::Android.Media.RingtoneType.Ringtone
            : global::Android.Media.RingtoneType.Notification;
        var tone = "الافتراضية";
        if (Guid.TryParse(conversationId, out var groupId) && groupId != Guid.Empty)
        {
            var key = $"himo_group_{groupId:N}_{(isCall ? "call-tone" : "message-tone")}";
            tone = Preferences.Default.Get(key, "الافتراضية");
        }

        var resourceId = isCall
            ? tone switch
            {
                "رنين 1" => Resource.Raw.himo_call_tone_1,
                "رنين 2" => Resource.Raw.himo_call_tone_2,
                "رنين 3" => Resource.Raw.himo_call_tone_3,
                "رنين 4" => Resource.Raw.himo_call_tone_4,
                _ => 0
            }
            : tone switch
            {
                "هادئة" => Resource.Raw.himo_message_tone_soft,
                "نغمة 1" => Resource.Raw.himo_message_tone_1,
                "نغمة 2" => Resource.Raw.himo_message_tone_2,
                "نغمة 3" => Resource.Raw.himo_message_tone_3,
                _ => 0
            };

        if (resourceId != 0)
            return global::Android.Net.Uri.Parse($"android.resource://{context.PackageName}/{resourceId}");
        return global::Android.Media.RingtoneManager.GetDefaultUri(defaultType);
    }

    private static long[]? GetGroupVibrationPattern(string conversationId, bool isCall)
    {
        if (!Guid.TryParse(conversationId, out var groupId) || groupId == Guid.Empty)
            return isCall ? new long[] { 0, 500, 300, 500 } : null;

        var key = $"himo_group_{groupId:N}_{(isCall ? "call-vibration" : "message-vibration")}";
        if (!Preferences.Default.ContainsKey(key))
            return isCall ? new long[] { 0, 500, 300, 500 } : null;

        return Preferences.Default.Get(key, "تلقائي") switch
        {
            "إيقاف" => Array.Empty<long>(),
            "قصير" => new long[] { 0, 140 },
            "طويل" => new long[] { 0, 750, 220, 750 },
            _ => isCall ? new long[] { 0, 500, 300, 500 } : new long[] { 0, 220, 140, 220 }
        };
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

            var callsChannel = new global::Android.App.NotificationChannel(
                CallsChannelId,
                CallsChannelName,
                global::Android.App.NotificationImportance.Max)
            {
                Description = CallsChannelDescription,
                LockscreenVisibility = global::Android.App.NotificationVisibility.Public
            };
            callsChannel.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone),
                null);
            callsChannel.EnableVibration(true);
            callsChannel.SetVibrationPattern(new long[] { 0, 500, 300, 500 });
            manager.CreateNotificationChannel(callsChannel);

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
