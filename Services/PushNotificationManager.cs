using MessageDto = Himo.Services.HimoApiClient.MessageDto;
using Himo.Models;
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
    private const string ActiveConversationPreferenceKey = "himo_active_conversation_id";
    private const int MaxRememberedMessageIds = 250;
    private const int MaxRememberedCallIds = 64;

    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private readonly ChatService _chat;
    private readonly HimoRealtimeService _realtime;
    private readonly ICallService _calls;
    private readonly INotificationService _notifications;
    private readonly object _dedupeGate = new();
    private readonly HashSet<string> _shownMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _shownMessageOrder = new();
    private readonly object _callDedupeGate = new();
    private readonly HashSet<string> _handledIncomingCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _handledIncomingCallOrder = new();
    private readonly HashSet<string> _finishedIncomingCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _finishedIncomingCallOrder = new();

    private int _fcmHandlersRegistered;
    private int _tokenRegistrationInProgress;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _notificationInfrastructureInitialized;
    private int _notificationPermissionRequestQueued;
    // Default to background; App.OnStart/OnResume explicitly marks the UI active.
    // This matters when Firebase starts the process solely to handle a data-only call push.
    private bool _appInForeground;

    public PushNotificationManager(
        AccountService account,
        HimoApiClient api,
        ChatService chat,
        HimoRealtimeService realtime,
        ICallService calls,
        INotificationService notifications)
    {
        _account = account;
        _api = api;
        _chat = chat;
        _realtime = realtime;
        _calls = calls;
        _notifications = notifications;

        _realtime.MessageReceived += OnRealtimeMessageReceived;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
    }

    public void SetAppForeground(bool foreground)
    {
        _appInForeground = foreground;
    }

    private async Task StartRealtimeInBackgroundAsync()
    {
        // AutomaticReconnect does not retry a connection that failed on its first start.
        var delays = new[] { 0, 2, 5, 10, 15 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(delays[attempt])).ConfigureAwait(false);

            try
            {
                await _realtime.StartAsync().ConfigureAwait(false);
                if (_realtime.IsConnected) return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Push] SignalR startup attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        System.Diagnostics.Debug.WriteLine("[Himo Push] SignalR remains offline after retries; API polling and FCM remain available.");
    }

    public async Task InitializeAsync()
    {
        // Create notification channels and attach FCM callbacks once. App startup
        // may call InitializeAsync from CreateWindow, OnStart and OnResume; those
        // parallel calls must not repeat plugin setup or permission prompts.
        await _initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_notificationInfrastructureInitialized)
            {
                await _notifications.InitializeAsync().ConfigureAwait(false);
#if ANDROID
                await MauiProgram.WaitForFirebaseStartupAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await MainThread.InvokeOnMainThreadAsync(EnsureFirebaseHandlers).ConfigureAwait(false);
                if (Interlocked.Exchange(ref _notificationPermissionRequestQueued, 1) == 0)
                    MainThread.BeginInvokeOnMainThread(MainActivity.RequestNotificationPermissionIfNeeded);
#endif
                _notificationInfrastructureInitialized = true;
            }
#if ANDROID
            else
            {
                // Retry handler registration if Firebase initialization failed on
                // the first attempt and the app is later resumed/recreated.
                await MainThread.InvokeOnMainThreadAsync(EnsureFirebaseHandlers).ConfigureAwait(false);
            }
#endif
        }
        finally
        {
            _initializationGate.Release();
        }

        if (!_account.IsSignedIn || !_api.HasToken)
            return;

        if (_notifications.IsEnabled)
            await RegisterTokenAsync();
        else
            await UnregisterCurrentTokenAsync();

        // SignalR is not required to display push notifications and must never
        // hold application/page startup while a hub connection negotiates.
        // Run it in the background; HimoRealtimeService already serializes
        // duplicate starts through its internal gate.
        _ = StartRealtimeInBackgroundAsync();
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
            // Call push must be handled even when the app is backgrounded. The
            // MainApplication FCM handler starts ring-only background behavior without a visible notification.
            // notification from the plugin service when no UI is running.
            var notification = e?.Notification;
            if (notification is null || notification.Data is null)
                return;

            if (notification.Data.TryGetValue("call_type", out var callType))
            {
                notification.Data.TryGetValue("conversation_id", out var callConversationId);
                notification.Data.TryGetValue("call_mode", out var callMode);
                notification.Data.TryGetValue("call_id", out var callId);
                notification.Data.TryGetValue("caller_name", out var callerName);

                if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(callConversationId))
                    {
                        if (_appInForeground)
                            RouteIncomingCall(callConversationId, callMode, callId);
                        else
                            _ = _notifications.ShowIncomingCallAsync(
                                string.IsNullOrWhiteSpace(callerName) ? notification.Title ?? "مكالمة واردة" : callerName,
                                callConversationId, callMode ?? "audio", callId ?? string.Empty);
                    }
                    return;
                }

                if (string.Equals(callType, "answering", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(callType, "accept", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(callType, "end", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedCallId = Guid.TryParse(callId, out var parsedStateCallId) && parsedStateCallId != Guid.Empty
                        ? parsedStateCallId.ToString("D") : string.Empty;
                    if (normalizedCallId.Length > 0)
                        RememberFinishedIncomingCall(normalizedCallId);
                    _ = _notifications.ClearIncomingCallAsync(
                        callConversationId ?? string.Empty, normalizedCallId, suppressFurtherRinging: true);
                    if (string.Equals(callType, "answering", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(callType, "accept", StringComparison.OrdinalIgnoreCase))
                    {
#if ANDROID
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallAnsweredByPush(
                            callConversationId ?? string.Empty, normalizedCallId, callType);
#endif
                    }
                    else if (string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Guid.TryParse(normalizedCallId, out var remoteEndedCallId) && remoteEndedCallId != Guid.Empty)
                        {
                            try { await _calls.TerminateFromRemoteAsync(remoteEndedCallId); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Push] Remote call teardown failed: {ex.Message}"); }
                        }
#if ANDROID
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallTerminatedByPush(
                            callConversationId ?? string.Empty, normalizedCallId);
#endif
                    }
                    return;
                }
            }

            if (!_appInForeground)
                return;

            if (!notification.Data.TryGetValue("conversation_id", out var conversationId) ||
                string.IsNullOrWhiteSpace(conversationId) ||
                !Guid.TryParse(conversationId, out var parsedConversationId) ||
                parsedConversationId == Guid.Empty)
                return;

            var title = string.IsNullOrWhiteSpace(notification.Title)
                ? "رسالة جديدة"
                : notification.Title;
            var body = string.IsNullOrWhiteSpace(notification.Body)
                ? "لديك رسالة جديدة"
                : notification.Body;
            var isActiveConversation = IsActiveConversation(conversationId);

            // FCM can be the only delivery path if SignalR is reconnecting. Do not
            // merely toast a notification: fetch and persist the actual server message
            // so the open chat/list updates as well.
            await SyncForegroundConversationFromServerAsync(
                parsedConversationId, title, markRead: isActiveConversation);

            if (isActiveConversation)
                return; // The user is already reading this chat; do not show an OS notification over it.

            if (IsConversationMutedOrBlocked(conversationId))
                return;

            var messageId = notification.Data.TryGetValue("message_id", out var id)
                ? id
                : string.Empty;
            var dedupeKey = BuildDedupeKey(messageId, conversationId, body);
            if (!TryRememberNotification(dedupeKey))
                return;

            await _notifications.ShowMessageAsync(title, body, conversationId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Foreground FCM handling failed: {ex}");
        }
    }

    private void OnRealtimeCallSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (!_account.IsSignedIn) return;

        var callId = string.Empty;
        var mode = "audio";
        if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope))
        {
            callId = envelope.CallId == Guid.Empty ? string.Empty : envelope.CallId.ToString("D");
            if (envelope.Mode == CallMode.Video) mode = "video";
        }

        if (string.Equals(signal.Type, CallSignalType.Invite.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (!_appInForeground) return; // background call UI is delivered by the full-screen FCM notification.
            RouteIncomingCall(signal.ConversationId.ToString("D"), mode, callId);
            return;
        }

        if (string.Equals(signal.Type, "Answering", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(signal.Type, CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(signal.Type, CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(callId))
                RememberFinishedIncomingCall(callId);
            _ = _notifications.ClearIncomingCallAsync(
                signal.ConversationId.ToString("D"), callId, suppressFurtherRinging: true);
        }
    }

    private void RememberFinishedIncomingCall(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId)) return;
        lock (_callDedupeGate)
        {
            if (!_finishedIncomingCalls.Add(callId)) return;
            _finishedIncomingCallOrder.Enqueue(callId);
            while (_finishedIncomingCallOrder.Count > MaxRememberedCallIds)
                _finishedIncomingCalls.Remove(_finishedIncomingCallOrder.Dequeue());
        }
    }

    private void RouteIncomingCall(string conversationId, string? mode, string? callId)
    {
        if (!_appInForeground || !_account.IsSignedIn || !Guid.TryParse(conversationId, out var parsedConversationId))
            return;

        var normalizedMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        var normalizedCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D")
            : string.Empty;
        var dedupeKey = normalizedCallId.Length > 0
            ? $"{parsedConversationId:D}:{normalizedCallId}"
            : $"{parsedConversationId:D}:{normalizedMode}:unknown";

        lock (_callDedupeGate)
        {
            if (normalizedCallId.Length > 0 && _finishedIncomingCalls.Contains(normalizedCallId))
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Push] Ignored stale invite for completed callId={normalizedCallId}");
                return;
            }
            if (!_handledIncomingCalls.Add(dedupeKey))
                return;
            _handledIncomingCallOrder.Enqueue(dedupeKey);
            while (_handledIncomingCallOrder.Count > MaxRememberedCallIds)
                _handledIncomingCalls.Remove(_handledIncomingCallOrder.Dequeue());
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_appInForeground) return;
            MainActivity.SetPendingCall(parsedConversationId.ToString("D"), normalizedMode, normalizedCallId);
            MainActivity.TryNavigateToPendingCall();
        });
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
                    data.TryGetValue("call_id", out var callId);
                    MainActivity.SetPendingCall(callConversationId, callMode ?? "audio", callId);
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
        if (!_appInForeground || !_account.IsSignedIn ||
            _account.CurrentAccount?.UserId == message.SenderUserId)
            return;

        _ = HandleRealtimeIncomingMessageAsync(message);
    }

    private async Task HandleRealtimeIncomingMessageAsync(MessageDto message)
    {
        try
        {
            var remoteConversationId = message.ConversationId.ToString("D");
            var conversation = await EnsureLocalConversationAsync(message.ConversationId, message.SenderPhoneNumber);
            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;

            await MainThread.InvokeOnMainThreadAsync(() =>
                _chat.AddRemoteMessage(conversation.Id, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString("D"), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted));

            // Persist first regardless of mute state. Muting suppresses notifications;
            // it must never stop the message from reaching the local conversation cache.
            if (IsActiveConversation(remoteConversationId))
            {
                await MainThread.InvokeOnMainThreadAsync(() => _chat.MarkAsRead(conversation.Id));
                try { await _api.MarkConversationReadAsync(message.ConversationId); } catch { }
                try { await _notifications.ClearConversationAsync(remoteConversationId); } catch { }
                return;
            }

            if (IsConversationMutedOrBlocked(remoteConversationId))
                return;

            var body = string.IsNullOrWhiteSpace(message.Text) ? "لديك رسالة جديدة" : message.Text;
            var dedupeKey = BuildDedupeKey(message.Id.ToString("D"), remoteConversationId, body);
            if (!TryRememberNotification(dedupeKey))
                return;

            var title = string.IsNullOrWhiteSpace(conversation.Name)
                ? message.SenderPhoneNumber
                : conversation.Name;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try { await _notifications.ShowMessageAsync(title, body, remoteConversationId); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Push] SignalR notification failed: {ex}"); }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Incoming SignalR message sync failed: {ex}");
        }
    }

    private bool IsActiveConversation(string remoteConversationId)
    {
        if (!_appInForeground || string.IsNullOrWhiteSpace(remoteConversationId))
            return false;

        var activeId = Preferences.Default.Get(ActiveConversationPreferenceKey, string.Empty);
        return string.Equals(activeId, remoteConversationId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Conversation> EnsureLocalConversationAsync(Guid remoteConversationId, string? fallbackName)
    {
        Conversation? local = _chat.Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, remoteConversationId.ToString("D"), StringComparison.OrdinalIgnoreCase));

        HimoApiClient.ConversationDto? remote = null;
        // Hydrate a missing/placeholder row from the server so the conversation name,
        // participant and archived state are authoritative, but never block display
        // on this lookup if the API is temporarily unavailable.
        if (local is null || string.IsNullOrWhiteSpace(local.Name) || string.Equals(local.Name, "محادثة", StringComparison.Ordinal))
        {
            try
            {
                var conversations = await _api.GetConversationsAsync();
                remote = conversations.FirstOrDefault(x => x.Id == remoteConversationId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Push] Conversation metadata refresh failed: {ex.Message}");
            }
        }

        return await MainThread.InvokeOnMainThreadAsync(() =>
            remote is not null
                ? _chat.UpsertRemoteConversation(remote, fallbackName)
                : _chat.EnsureRemoteConversation(remoteConversationId, fallbackName));
    }

    private async Task SyncForegroundConversationFromServerAsync(Guid remoteConversationId, string? fallbackName, bool markRead)
    {
        try
        {
            var localConversation = await EnsureLocalConversationAsync(remoteConversationId, fallbackName);
            var messages = await _api.GetMessagesAsync(remoteConversationId, limit: 100);
            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            var newlyAddedIncomingIds = new List<Guid>();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var message in messages.OrderBy(x => x.SentAt))
                {
                    var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                    var added = _chat.AddRemoteMessage(
                        localConversation.Id, message.Text, message.SentAt.LocalDateTime, isMine,
                        message.Id.ToString("D"), message.AttachmentFileName, message.AttachmentContentType,
                        message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                        message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
                    if (added && !isMine)
                        newlyAddedIncomingIds.Add(message.Id);
                }

                if (markRead)
                    _chat.MarkAsRead(localConversation.Id);
            });

            foreach (var messageId in newlyAddedIncomingIds)
                await _realtime.MarkMessageDeliveredAsync(messageId);

            if (markRead)
            {
                await _api.MarkConversationReadAsync(remoteConversationId);
                await _notifications.ClearConversationAsync(remoteConversationId.ToString("D"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Foreground FCM message sync failed for {remoteConversationId:D}: {ex}");
        }
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
