using MessageDto = Himo.Services.HimoApiClient.MessageDto;
#if ANDROID
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif

namespace Himo.Services;

/// <summary>
/// Single owner for push-notification lifecycle.
/// Foreground messages use the already-connected SignalR path (with FCM as a backup
/// through NotificationReceived), while background/killed delivery is handled by FCM.
/// </summary>
public sealed class PushNotificationManager
{
    private const string RegisteredTokenKey = "himo_registered_fcm_token";
    private const string PendingCleanupTokenKey = "himo_pending_fcm_token_cleanup";
    private const int MaxRememberedMessageIds = 250;

    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private readonly ChatService _chat;
    private readonly HimoRealtimeService _realtime;
    private readonly INotificationService _notifications;
    private readonly object _dedupeGate = new();
    private readonly HashSet<string> _shownMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _shownMessageOrder = new();

    private int _fcmHandlersRegistered;
    private int _tokenRegistrationInProgress;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private bool _appInForeground = true;

    public PushNotificationManager(
        AccountService account,
        HimoApiClient api,
        ChatService chat,
        HimoRealtimeService realtime,
        INotificationService notifications)
    {
        _account = account;
        _api = api;
        _chat = chat;
        _realtime = realtime;
        _notifications = notifications;

        _realtime.MessageReceived += OnRealtimeMessageReceived;
    }

    public void SetAppForeground(bool foreground)
    {
        _appInForeground = foreground;
    }

    public async Task InitializeAsync()
    {
        await _notifications.InitializeAsync();

#if ANDROID
        EnsureFirebaseHandlers();
        MainActivity.RequestNotificationPermissionIfNeeded();
#endif

        if (!_account.IsSignedIn || !_api.HasToken)
            return;

        if (_notifications.IsEnabled)
            await RegisterTokenAsync();
        else
            await UnregisterCurrentTokenAsync();

        try
        {
            await _realtime.StartAsync();
        }
        catch (Exception ex)
        {
            // FCM remains the background delivery path even when SignalR is unavailable.
            System.Diagnostics.Debug.WriteLine($"[Himo Push] SignalR startup skipped: {ex}");
        }
    }

#if ANDROID
    private void EnsureFirebaseHandlers()
    {
        if (Interlocked.Exchange(ref _fcmHandlersRegistered, 1) != 0)
            return;

        try
        {
            CrossFirebaseCloudMessaging.Current.TokenChanged += OnFcmTokenChanged;
            CrossFirebaseCloudMessaging.Current.NotificationReceived += OnFcmNotificationReceived;
            CrossFirebaseCloudMessaging.Current.NotificationTapped += OnFcmNotificationTapped;
            System.Diagnostics.Debug.WriteLine("[Himo Push] Firebase handlers registered.");
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _fcmHandlersRegistered, 0);
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Firebase handler registration failed: {ex}");
        }
    }

    private async void OnFcmTokenChanged(object? sender, FCMTokenChangedEventArgs e)
    {
        try
        {
            if (!_account.IsSignedIn || !_api.HasToken || string.IsNullOrWhiteSpace(e.Token))
                return;

            await RegisterTokenCoreAsync(e.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] TokenChanged failed: {ex}");
        }
    }

    private async void OnFcmNotificationReceived(object? sender, FCMNotificationReceivedEventArgs e)
    {
        try
        {
            // Background/killed notifications are displayed by the FCM plugin itself.
            // We only create a local notification while the app is visible.
            if (!_appInForeground)
                return;

            var notification = e?.Notification;
            if (notification is null || notification.Data is null)
                return;

            if (notification.Data.TryGetValue("call_type", out var callType) &&
                string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase))
                return;

            if (!notification.Data.TryGetValue("conversation_id", out var conversationId) ||
                string.IsNullOrWhiteSpace(conversationId))
                return;

            if (IsConversationMutedOrBlocked(conversationId))
                return;

            var messageId = notification.Data.TryGetValue("message_id", out var id)
                ? id
                : string.Empty;
            var dedupeKey = BuildDedupeKey(messageId, conversationId, notification.Body);
            if (!TryRememberNotification(dedupeKey))
                return;

            var title = string.IsNullOrWhiteSpace(notification.Title)
                ? "رسالة جديدة"
                : notification.Title;
            var body = string.IsNullOrWhiteSpace(notification.Body)
                ? "لديك رسالة جديدة"
                : notification.Body;

            await _notifications.ShowMessageAsync(title, body, conversationId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Foreground FCM handling failed: {ex}");
        }
    }

    private void OnFcmNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        try
        {
            var data = e?.Notification?.Data;
            if (data is not null)
            {
                if (data.TryGetValue("call_type", out var callType) &&
                    string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) &&
                    data.TryGetValue("conversation_id", out var callConversationId) &&
                    !string.IsNullOrWhiteSpace(callConversationId))
                {
                    data.TryGetValue("call_mode", out var callMode);
                    MainActivity.SetPendingCall(callConversationId, callMode ?? "audio");
                    MainActivity.TryNavigateToPendingCall();
                    return;
                }

                if (data.TryGetValue("conversation_id", out var conversationId) &&
                    !string.IsNullOrWhiteSpace(conversationId))
                {
                    MainActivity.SetPendingConversation(conversationId);
                    MainActivity.TryNavigateToPendingConversation();
                    return;
                }
            }

            MainActivity.TryNavigateToPendingConversation();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Notification tap handling failed: {ex}");
        }
    }

    public async Task RegisterTokenAsync()
    {
        if (!_account.IsSignedIn || !_api.HasToken || !_notifications.IsEnabled)
            return;

        if (Interlocked.Exchange(ref _tokenRegistrationInProgress, 1) != 0)
            return;

        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                await RegisterTokenCoreAsync(token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] FCM token retrieval failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _tokenRegistrationInProgress, 0);
        }
    }

    public async Task UnregisterCurrentTokenAsync()
    {
        if (!_api.HasToken)
            return;

#if ANDROID
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var registeredToken = Preferences.Default.Get(RegisteredTokenKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(registeredToken))
            tokens.Add(registeredToken);

        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var currentToken = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(currentToken))
                tokens.Add(currentToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Current FCM token lookup during unregister failed: {ex}");
        }

        foreach (var token in tokens)
        {
            try { await _api.RemovePushTokenAsync(token); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Push] Token removal failed: {ex}"); }
        }

        Preferences.Default.Remove(RegisteredTokenKey);
        Preferences.Default.Remove(PendingCleanupTokenKey);
#endif
    }

    private async Task RegisterTokenCoreAsync(string token)
    {
        if (!_account.IsSignedIn || !_api.HasToken || !_notifications.IsEnabled || string.IsNullOrWhiteSpace(token))
            return;

        await _tokenGate.WaitAsync();
        try
        {
            var registeredToken = Preferences.Default.Get(RegisteredTokenKey, string.Empty);
            var pendingCleanup = Preferences.Default.Get(PendingCleanupTokenKey, string.Empty);

            if (string.Equals(registeredToken, token, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(pendingCleanup))
            {
                return;
            }

            var registered = false;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    await _api.RegisterPushTokenAsync(token);
                    registered = true;
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Push] Token registration attempt {attempt} failed: {ex}");
                    if (attempt < 4)
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }

            if (!registered)
                return;

            var cleanupTokens = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(registeredToken) &&
                !string.Equals(registeredToken, token, StringComparison.Ordinal))
                cleanupTokens.Add(registeredToken);

            if (!string.IsNullOrWhiteSpace(pendingCleanup) &&
                !string.Equals(pendingCleanup, token, StringComparison.Ordinal) &&
                !cleanupTokens.Contains(pendingCleanup, StringComparer.Ordinal))
                cleanupTokens.Add(pendingCleanup);

            string? stillPending = null;
            foreach (var oldToken in cleanupTokens)
            {
                try
                {
                    await _api.RemovePushTokenAsync(oldToken);
                }
                catch (Exception ex)
                {
                    stillPending = oldToken;
                    System.Diagnostics.Debug.WriteLine($"[Himo Push] Old token cleanup failed: {ex}");
                }
            }

            if (stillPending is null)
                Preferences.Default.Remove(PendingCleanupTokenKey);
            else
                Preferences.Default.Set(PendingCleanupTokenKey, stillPending);

            Preferences.Default.Set(RegisteredTokenKey, token);
            System.Diagnostics.Debug.WriteLine("[Himo Push] Current FCM token is registered with Himo API.");
        }
        finally
        {
            _tokenGate.Release();
        }
    }
#endif

    private void OnRealtimeMessageReceived(object? sender, MessageDto message)
    {
        if (!_appInForeground || !_account.IsSignedIn)
            return;

        if (_account.CurrentAccount?.UserId == message.SenderUserId)
            return;

        var remoteConversationId = message.ConversationId.ToString("D");
        if (IsConversationMutedOrBlocked(remoteConversationId))
            return;

        var conversationName = _chat.Conversations.FirstOrDefault(c =>
            string.Equals(c.RemoteId, remoteConversationId, StringComparison.OrdinalIgnoreCase))?.Name;

        var title = string.IsNullOrWhiteSpace(conversationName)
            ? message.SenderPhoneNumber
            : conversationName;
        var body = string.IsNullOrWhiteSpace(message.Text) ? "لديك رسالة جديدة" : message.Text;
        var dedupeKey = BuildDedupeKey(message.Id.ToString("D"), remoteConversationId, body);

        if (!TryRememberNotification(dedupeKey))
            return;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await _notifications.ShowMessageAsync(title, body, remoteConversationId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Push] SignalR notification failed: {ex}");
            }
        });
    }

    private bool IsConversationMutedOrBlocked(string remoteConversationId)
    {
        if (string.IsNullOrWhiteSpace(remoteConversationId))
            return false;

        var muted = Preferences.Default.Get("himo_muted_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(remoteConversationId, StringComparer.OrdinalIgnoreCase);

        if (muted)
            return true;

        return Preferences.Default.Get("himo_blocked_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(remoteConversationId, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildDedupeKey(string? messageId, string conversationId, string? body)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
            return $"message:{messageId}";

        return $"fallback:{conversationId}:{body ?? string.Empty}";
    }

    private bool TryRememberNotification(string key)
    {
        lock (_dedupeGate)
        {
            if (!_shownMessageIds.Add(key))
                return false;

            _shownMessageOrder.Enqueue(key);
            while (_shownMessageOrder.Count > MaxRememberedMessageIds)
            {
                var oldest = _shownMessageOrder.Dequeue();
                _shownMessageIds.Remove(oldest);
            }

            return true;
        }
    }
}
