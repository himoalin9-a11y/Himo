using Himo.Services;

namespace Himo.Platforms.Android.Services;

public sealed class NotificationService : INotificationService
{
    // Legacy IDs are kept for compatibility with older app versions and existing intents.
    public const string MessageChannelId = "himo_messages_v5";
    public const string CallsChannelId = "himo_calls_v4";
    private const string MessagesTonePreferenceKey = "himo_notification_messages_tone";
    private const string GroupsTonePreferenceKey = "himo_notification_groups_tone";
    private const string CallsTonePreferenceKey = "himo_notification_calls_tone";
    private const string DefaultToneId = "system";
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
    private static readonly object PreviewRingtoneGate = new();
    private static global::Android.Media.Ringtone? _previewRingtone;
    private static System.Threading.CancellationTokenSource? _previewRingtoneCts;
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
        => ShowMessageAsync(senderName, message, conversationId, isGroup: false);

    public Task ShowMessageAsync(string senderName, string message, string conversationId, bool isGroup)
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

        var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, GetCurrentMessageChannelId(isGroup));
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

    // Show a real Android call notification as well as ringing while the app is backgrounded.
    public Task ShowIncomingCallAsync(string callerName, string conversationId, string mode, string callId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return Task.CompletedTask;

        var normalizedMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        var normalizedCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D") : string.Empty;
        if (IsCallRingingSuppressed(normalizedCallId)) return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        if (context is null) return Task.CompletedTask;

        var dedupeKey = string.IsNullOrWhiteSpace(normalizedCallId) ? conversationId : normalizedCallId;
        try
        {
            EnsureChannels();
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
                var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, GetCurrentCallsChannelId());
                builder.SetSmallIcon(Resource.Drawable.himo_notification);
                builder.SetContentTitle(title);
                builder.SetContentText(body);
                builder.SetCategory(global::AndroidX.Core.App.NotificationCompat.CategoryCall);
                builder.SetPriority(global::AndroidX.Core.App.NotificationCompat.PriorityMax);
                builder.SetVisibility(global::AndroidX.Core.App.NotificationCompat.VisibilityPublic);
                builder.SetOngoing(true);
                builder.SetAutoCancel(false);
                builder.SetOnlyAlertOnce(false);
                builder.SetContentIntent(pendingIntent);
                builder.SetFullScreenIntent(pendingIntent, true);
                builder.SetVibrate(new long[] { 0, 500, 300, 500 });
                notificationManager?.Notify(notificationId, builder.Build());
                System.Diagnostics.Debug.WriteLine($"[Himo Call] Incoming-call notification posted; callId={normalizedCallId}; mode={normalizedMode}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Himo Call] POST_NOTIFICATIONS permission missing; cannot post call notification.");
            }

            // Keep a looping ringtone as a fallback because notification sounds may be limited by device settings.
            lock (FinishedCallIdsGate)
            {
                if (IsCallRingingSuppressed(normalizedCallId)) return Task.CompletedTask;
                ActiveCallRingtoneIds.Add(dedupeKey);
            }
            StartBackgroundIncomingCallRingtone(context, dedupeKey, conversationId, normalizedCallId);
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
        string callId)
    {
        lock (BackgroundCallRingtoneGate)
        {
            if (string.Equals(_backgroundCallRingtoneKey, dedupeKey, StringComparison.OrdinalIgnoreCase) &&
                _backgroundCallRingtone is not null)
                return;

            StopBackgroundIncomingCallRingtoneLocked();
            var selectedCallTone = GetSavedToneId(CallsTonePreferenceKey);
            var ringtoneUri = GetToneUri(context, selectedCallTone, isCall: true);
            if (ringtoneUri is null)
            {
                System.Diagnostics.Debug.WriteLine("[Himo Call] Incoming call ringtone is disabled by the selected tone.");
                return;
            }

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
                            if (oldCallTag || string.Equals(channelId, "himo_calls_v3", StringComparison.OrdinalIgnoreCase) ||
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

    public static string GetCurrentMessageChannelId(bool isGroup = false)
    {
        var toneId = GetSavedToneId(isGroup ? GroupsTonePreferenceKey : MessagesTonePreferenceKey);
        return $"himo_{(isGroup ? "groups" : "messages")}_{SafeChannelPart(toneId)}_v1";
    }

    public static string GetCurrentCallsChannelId()
    {
        var toneId = GetSavedToneId(CallsTonePreferenceKey);
        return $"himo_calls_{SafeChannelPart(toneId)}_v1";
    }

    public static void RefreshNotificationChannels() => EnsureChannels();

    private static string GetSavedToneId(string preferenceKey)
    {
        var value = Preferences.Default.Get(preferenceKey, DefaultToneId)?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return DefaultToneId;
        return value switch
        {
            "النغمة الافتراضية" => "system",
            "نغمة هادئة" => "himo_tone_soft",
            "نغمة كلاسيكية" => "himo_tone_classic",
            "نغمة قصيرة" => "himo_tone_digital",
            "بدون نغمة" => "silent",
            _ => value
        };
    }

    private static string SafeChannelPart(string value)
    {
        var cleaned = new string((value ?? DefaultToneId).ToLowerInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? DefaultToneId : cleaned;
    }

    private static int GetToneResourceId(string toneId) => toneId switch
    {
        "himo_tone_breeze" => Resource.Raw.himo_tone_breeze,
        "himo_tone_spark" => Resource.Raw.himo_tone_spark,
        "himo_tone_classic" => Resource.Raw.himo_tone_classic,
        "himo_tone_chime" => Resource.Raw.himo_tone_chime,
        "himo_tone_soft" => Resource.Raw.himo_tone_soft,
        "himo_tone_digital" => Resource.Raw.himo_tone_digital,
        "himo_tone_marimba" => Resource.Raw.himo_tone_marimba,
        "himo_tone_bell" => Resource.Raw.himo_tone_bell,
        "himo_tone_ripple" => Resource.Raw.himo_tone_ripple,
        "himo_tone_pulse" => Resource.Raw.himo_tone_pulse,
        "himo_tone_pluck" => Resource.Raw.himo_tone_pluck,
        "himo_tone_glass" => Resource.Raw.himo_tone_glass,
        "himo_tone_morning" => Resource.Raw.himo_tone_morning,
        "himo_tone_orbit" => Resource.Raw.himo_tone_orbit,
        "himo_tone_alert" => Resource.Raw.himo_tone_alert,
        _ => 0
    };

    private static global::Android.Net.Uri? GetToneUri(
        global::Android.Content.Context context, string toneId, bool isCall)
    {
        if (string.Equals(toneId, "silent", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(toneId, DefaultToneId, StringComparison.OrdinalIgnoreCase))
            return global::Android.Media.RingtoneManager.GetDefaultUri(
                isCall ? global::Android.Media.RingtoneType.Ringtone : global::Android.Media.RingtoneType.Notification);

        var resourceId = GetToneResourceId(toneId);
        if (resourceId == 0)
            return global::Android.Media.RingtoneManager.GetDefaultUri(
                isCall ? global::Android.Media.RingtoneType.Ringtone : global::Android.Media.RingtoneType.Notification);

        return global::Android.Net.Uri.Parse($"android.resource://{context.PackageName}/{resourceId}");
    }

    public static void PlayTonePreview(string toneId, bool isCall)
    {
        var context = global::Android.App.Application.Context;
        if (context is null) return;

        lock (PreviewRingtoneGate)
        {
            StopTonePreviewLocked();
            var uri = GetToneUri(context, toneId, isCall);
            if (uri is null) return;

            var ringtone = global::Android.Media.RingtoneManager.GetRingtone(context, uri);
            if (ringtone is null) return;
            _previewRingtone = ringtone;
            _previewRingtoneCts = new System.Threading.CancellationTokenSource();
            ringtone.Play();
            _ = StopTonePreviewAfterAsync(ringtone, _previewRingtoneCts.Token);
        }
    }

    public static void StopTonePreview()
    {
        lock (PreviewRingtoneGate)
            StopTonePreviewLocked();
    }

    private static void StopTonePreviewLocked()
    {
        try { _previewRingtoneCts?.Cancel(); } catch { }
        try { _previewRingtoneCts?.Dispose(); } catch { }
        _previewRingtoneCts = null;
        try { _previewRingtone?.Stop(); } catch { }
        try { _previewRingtone?.Dispose(); } catch { }
        _previewRingtone = null;
    }

    private static async Task StopTonePreviewAfterAsync(
        global::Android.Media.Ringtone ringtone,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(3_000, cancellationToken).ConfigureAwait(false);
            lock (PreviewRingtoneGate)
            {
                if (ReferenceEquals(_previewRingtone, ringtone))
                    StopTonePreviewLocked();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Tone] Preview cleanup failed: {ex.Message}"); }
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

            CreateToneChannel(manager, context, GetCurrentMessageChannelId(isGroup: false), ChannelName,
                ChannelDescription, GetSavedToneId(MessagesTonePreferenceKey), isCall: false,
                importance: global::Android.App.NotificationImportance.High);
            CreateToneChannel(manager, context, GetCurrentMessageChannelId(isGroup: true), "مجموعات Himo",
                "إشعارات الرسائل الجديدة في المجموعات", GetSavedToneId(GroupsTonePreferenceKey), isCall: false,
                importance: global::Android.App.NotificationImportance.High);
            CreateToneChannel(manager, context, GetCurrentCallsChannelId(), CallsChannelName,
                CallsChannelDescription, GetSavedToneId(CallsTonePreferenceKey), isCall: true,
                importance: global::Android.App.NotificationImportance.Max,
                visibility: global::Android.App.NotificationVisibility.Public);
            Plugin.Firebase.CloudMessaging.FirebaseCloudMessagingImplementation.ChannelId =
                GetCurrentMessageChannelId();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Notification channel initialization failed: {ex}");
        }
    }

    private static void CreateToneChannel(
        global::Android.App.NotificationManager manager,
        global::Android.Content.Context context,
        string channelId,
        string name,
        string description,
        string toneId,
        bool isCall,
        global::Android.App.NotificationImportance importance,
        global::Android.App.NotificationVisibility visibility = global::Android.App.NotificationVisibility.Private)
    {
        // Android freezes sound settings on first channel creation. A tone-specific ID
        // ensures a newly confirmed selection receives a channel with the new sound.
        if (manager.GetNotificationChannel(channelId) is not null)
            return;

        var channel = new global::Android.App.NotificationChannel(channelId, name, importance)
        {
            Description = description,
            LockscreenVisibility = visibility
        };
        channel.SetSound(GetToneUri(context, toneId, isCall), null);
        if (isCall)
        {
            channel.EnableVibration(true);
            channel.SetVibrationPattern(new long[] { 0, 500, 300, 500 });
        }
        manager.CreateNotificationChannel(channel);
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
