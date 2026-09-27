using Microsoft.Maui.Graphics;
using System.Net.Http;
using Himo.Services;
using Himo.Models;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

namespace Himo.Views;

[QueryProperty(nameof(ConversationId), "id")]
public partial class ChatPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly INotificationService _notifications;
    private readonly HimoRealtimeService _realtime;
    private readonly ICallService _calls;
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _remoteLoadGate = new(1, 1);
    private int _conversationId;
    private string? _remoteConversationId;
    private int _sending;
    private bool _isRecordingAudio;
    private CancellationTokenSource? _typingCts;
    private bool _typingActive;
    private bool _remoteTyping;
    private ChatMessage? _replyingTo;
    private readonly HashSet<int> _selectedMessageIds = new();
    private bool _selectionMode;
    private bool IsConversationMuted => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_muted_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationArchived => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_archived_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationBlocked => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_blocked_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
#if ANDROID
    private global::Android.Media.MediaRecorder? _audioRecorder;
#endif
    private string? _audioRecordingPath;
#if ANDROID
    private global::Android.Media.MediaPlayer? _audioPlayer;
#endif

    public string ConversationId
    {
        get => _conversationId.ToString();
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
            {
                PrepareForConversationChange();
                _conversationId = id;
                _remoteConversationId = _chat.Conversations.FirstOrDefault(x => x.Id == id)?.RemoteId;
                Load();
                return;
            }

            if (Guid.TryParse(value, out var remoteId))
            {
                PrepareForConversationChange();
                _conversationId = 0;
                _remoteConversationId = remoteId.ToString("D");
                _ = ResolveRemoteConversationAsync();
            }
        }
    }

    private void PrepareForConversationChange()
    {
        StopTyping();
        _replyingTo = null;
        _selectionMode = false;
        _selectedMessageIds.Clear();
        _remoteTyping = false;
        if (MessageEntry is not null) MessageEntry.Text = string.Empty;
        if (ReplyPreview is not null) ReplyPreview.IsVisible = false;
        if (MessageSearchBar is not null) MessageSearchBar.IsVisible = false;
        if (MessageSearchEntry is not null) MessageSearchEntry.Text = string.Empty;
        if (ScrollToBottomButton is not null) ScrollToBottomButton.IsVisible = false;
    }

    public ChatPage(ChatService chat, HimoApiClient api, AccountService account, INotificationService notifications, HimoRealtimeService realtime, ICallService calls)
    {
        InitializeComponent();
        _chat = chat; _api = api; _account = account; _notifications = notifications; _realtime = realtime; _calls = calls;
        _realtime.MessageReceived += OnRealtimeMessageReceived;
        _realtime.UserPresenceChanged += OnUserPresenceChanged;
        _realtime.UserTypingChanged += OnUserTypingChanged;
        _realtime.MessageDeliveryChanged += OnMessageDeliveryChanged;
        _realtime.MessageEdited += OnMessageEdited;
        _realtime.MessageDeleted += OnMessageDeleted;
        _realtime.Reconnected += OnRealtimeReconnected;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
        BindingContext = _chat.GetMessages(0);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
        StartPollingFallback();
        _ = InitializeChatAsync();
    }

    private async Task InitializeChatAsync()
    {
        await Task.Yield();
        try
        {
            Load();
            _ = _notifications.InitializeAsync();
            _ = ResolveRemoteConversationAsync();
            _ = ClearConversationNotificationAsync();
            _ = LoadRemoteAsync();
            _ = StartRealtimeAsync();
            _ = FlushOutboxAsync();
        }
        catch { }
    }

    private async Task ResolveRemoteConversationAsync()
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId) || !Guid.TryParse(_remoteConversationId, out var remoteId))
            return;

        var existing = _chat.Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, _remoteConversationId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _conversationId = existing.Id;
            Load();
            return;
        }

        if (!_api.HasToken)
            return;

        try
        {
            var conversations = await _api.GetConversationsAsync();
            var remote = conversations.FirstOrDefault(x => x.Id == remoteId);
            if (remote is null) return;

            var local = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(x.RemoteId, remote.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));

            if (local is null)
            {
                local = _chat.AddConversation(remote.Name);
                local.RemoteId = remote.Id.ToString("D");
            }

            local.LastMessage = remote.LastMessage;
            local.Time = remote.UpdatedAt.LocalDateTime.ToString("HH:mm");
            local.UpdatedAt = remote.UpdatedAt.LocalDateTime;
            local.UnreadCount = remote.UnreadCount;

            _conversationId = local.Id;
            Load();
        }
        catch
        {
            // The normal home refresh can populate the conversation later.
        }
    }

    private void Load()
    {
        if (_conversationId == 0 || !IsLoaded)
            return;

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation == null)
            return;

        var displayName = string.IsNullOrWhiteSpace(conversation.Name) ? "محادثة" : conversation.Name.Trim();
        NameLabel?.Text = displayName;
        InitialLabel?.Text = string.IsNullOrWhiteSpace(conversation.Initial)
            ? displayName[..1]
            : conversation.Initial;
        var messages = _chat.GetMessages(_conversationId);
        var unreadCount = conversation.UnreadCount;
        BindingContext = messages;
        SetMessagesItemsSource(messages);
        EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);
        if (UnreadDivider is not null)
            UnreadDivider.IsVisible = unreadCount > 0 && messages.Count > 0;
        _chat.MarkAsRead(_conversationId);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (messages.Count > 0)
                Messages?.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false);
        });
    }




    private async void ConversationOptionsClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;

        var mute = IsConversationMuted ? "إلغاء كتم الإشعارات" : "كتم الإشعارات";
        var archive = IsConversationArchived ? "إلغاء الأرشفة" : "أرشفة المحادثة";
        var block = IsConversationBlocked ? "إلغاء الحظر" : "حظر المحادثة";
        var selected = await DisplayActionSheetAsync("خيارات المحادثة", "إلغاء", null,
            mute, archive, block, "الإبلاغ عن المحادثة");

        if (string.Equals(selected, mute, StringComparison.Ordinal))
            MuteConversationClicked(null, EventArgs.Empty);
        else if (string.Equals(selected, archive, StringComparison.Ordinal))
            ArchiveConversationClicked(null, EventArgs.Empty);
        else if (string.Equals(selected, block, StringComparison.Ordinal))
            BlockConversationClicked(null, EventArgs.Empty);
        else if (string.Equals(selected, "الإبلاغ عن المحادثة", StringComparison.Ordinal))
            ReportConversationClicked(null, EventArgs.Empty);
    }

    private async void ReportConversationClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        var reason = await DisplayPromptAsync("الإبلاغ عن المحادثة", "اكتب سبب البلاغ:", "إرسال", "إلغاء", "السبب", 200, Keyboard.Text);
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            await _api.ReportConversationAsync(conversationId, reason);
            await DisplayAlertAsync("الإبلاغ", "تم إرسال البلاغ.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الإبلاغ", ex.Message, "حسنًا");
        }
    }


    private async void BlockConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var blocked = IsConversationBlocked;
        var values = Preferences.Default.Get("himo_blocked_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!blocked) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_blocked_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync("المحادثة", blocked ? "تم إلغاء حظر المحادثة." : "تم حظر المحادثة.", "حسنًا");
    }

    private async void ArchiveConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var archived = IsConversationArchived;
        var values = Preferences.Default.Get("himo_archived_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!archived) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_archived_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync("المحادثة", archived ? "تم إلغاء أرشفة المحادثة." : "تمت أرشفة المحادثة.", "حسنًا");
    }

    private async void AudioCallClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId))
        {
            await DisplayAlertAsync("المكالمة", "المحادثة غير متصلة بالخادم بعد.", "حسنًا");
            return;
        }

        try
        {
            await Shell.Current.GoToAsync($"///CallPage?id={conversationId:D}&mode=audio");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المكالمة", $"تعذر فتح شاشة المكالمة: {ex.Message}", "حسنًا");
        }
    }

    private async void VideoCallClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId))
        {
            await DisplayAlertAsync("المكالمة", "المحادثة غير متصلة بالخادم بعد.", "حسنًا");
            return;
        }

        try
        {
            await Shell.Current.GoToAsync($"///CallPage?id={conversationId:D}&mode=video");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المكالمة", $"تعذر فتح شاشة المكالمة: {ex.Message}", "حسنًا");
        }
    }

    private async void MuteConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var muted = IsConversationMuted;
        var values = Preferences.Default.Get("himo_muted_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!muted) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_muted_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync("الإشعارات", muted ? "تم إلغاء كتم إشعارات المحادثة." : "تم كتم إشعارات المحادثة.", "حسنًا");
    }

    protected override void OnDisappearing()
    {
#if ANDROID
        if (_isRecordingAudio) CleanupAudioRecorder();
        StopAudioPlayback();
#endif
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        StopPolling();
        StopTyping();
        _ = _realtime.StopAsync();
        base.OnDisappearing();
    }

    private async void OnRealtimeCallSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (!string.Equals(signal.Type, CallSignalType.Invite.ToString(), StringComparison.OrdinalIgnoreCase))
            return;

        if (_calls.Current is not null &&
            (_calls.Current.ConversationId != signal.ConversationId || _calls.Current.IsConnected))
        {
            // A second invite cannot safely replace an active call.
            await _calls.RejectAsync();
            return;
        }

        var mode = CallMode.Audio;
        if (!string.IsNullOrWhiteSpace(signal.Payload))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(signal.Payload);
                if (doc.RootElement.TryGetProperty("mode", out var value) &&
                    string.Equals(value.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                    mode = CallMode.Video;
            }
            catch { }
        }

        var conversation = _chat.Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, signal.ConversationId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        var name = string.IsNullOrWhiteSpace(conversation?.Name) ? "جهة اتصال" : conversation!.Name.Trim();
        var title = mode == CallMode.Video ? $"مكالمة فيديو واردة من {name}" : $"مكالمة صوتية واردة من {name}";
        var action = await DisplayActionSheetAsync(title, "رفض", null, "رد");
        if (string.Equals(action, "رد", StringComparison.Ordinal))
        {
            await Shell.Current.GoToAsync($"///CallPage?id={signal.ConversationId:D}&mode={(mode == CallMode.Video ? "video" : "audio")}&incoming=true");
        }
        else
        {
            await _calls.RejectAsync();
        }
    }

    private async Task StartRealtimeAsync()
    {
        await _realtime.StartAsync();
        if (Guid.TryParse(_remoteConversationId, out var remoteId))
        {
            var online = await _realtime.GetPresenceAsync(remoteId);
            SetPresenceText(online);
        }
    }

    private void OnRealtimeReconnected(object? sender, EventArgs e)
    {
        // SignalR may reconnect after a network interruption while the page is still
        // visible. Pull missed messages and flush the durable outbox with the same
        // ClientMessageId values so a lost HTTP response cannot create duplicates.
        _ = LoadRemoteAsync();
        _ = FlushOutboxAsync();
        if (Guid.TryParse(_remoteConversationId, out var remoteId))
            _ = RefreshPresenceAfterReconnectAsync(remoteId);
    }

    private int _flushingOutbox;

    private async Task FlushOutboxAsync()
    {
        if (!_api.HasToken || Interlocked.Exchange(ref _flushingOutbox, 1) != 0) return;
        try
        {
            foreach (var pending in _chat.GetPendingMessages())
            {
                if (!Guid.TryParse(_chat.Conversations.FirstOrDefault(x => x.Id == pending.ConversationId)?.RemoteId, out var conversationId))
                    continue;
                if (string.IsNullOrWhiteSpace(pending.ClientMessageId)) continue;

                var clientMessageId = pending.ClientMessageId;
                _chat.MarkPendingSending(clientMessageId);
                try
                {
                    var replyId = Guid.TryParse(pending.ReplyToRemoteId, out var parsedReplyId) ? parsedReplyId : (Guid?)null;
                    var remote = await _api.SendMessageAsync(conversationId, pending.Text, clientMessageId, replyId);
                    _chat.CompletePendingMessage(pending.ConversationId, clientMessageId, remote.Id.ToString(), remote.SentAt.LocalDateTime);
                }
                catch
                {
                    _chat.FailPendingMessage(pending.ConversationId, clientMessageId);
                }
            }
            if (_conversationId != 0)
                RefreshMessagesView(scrollToEnd: false);
        }
        finally
        {
            Interlocked.Exchange(ref _flushingOutbox, 0);
        }
    }

    private async Task RefreshPresenceAfterReconnectAsync(Guid conversationId)
    {
        var online = await _realtime.GetPresenceAsync(conversationId);
        SetPresenceText(online);
    }

    private void OnMessageEdited(object? sender, MessageDto message)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || message.ConversationId != conversationId) return;
        if (_chat.UpdateMessageText(message.Id.ToString("D"), message.Text, DateTime.TryParse(message.EditedAt, out var edited) ? edited : null))
            RefreshMessagesView(scrollToEnd: false);
    }

    private void OnMessageDeleted(object? sender, MessageDto message)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || message.ConversationId != conversationId) return;
        if (_chat.UpdateMessageDeleted(message.Id.ToString("D")))
            RefreshMessagesView(scrollToEnd: false);
    }

    private void OnMessageDeliveryChanged(object? sender, (Guid MessageId, Guid UserId, string Status) e)
    {
        // ChatMessage.DeliveryStatus raises PropertyChanged, so rebuilding the
        // entire CollectionView for every receipt only creates unnecessary UI work.
        _chat.UpdateDeliveryStatus(e.MessageId.ToString("D"), e.Status);
    }

    private void OnUserPresenceChanged(object? sender, (Guid UserId, bool IsOnline) e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        if (conversationId == Guid.Empty) return;
        SetPresenceText(e.IsOnline);
    }

    private void OnUserTypingChanged(object? sender, (Guid UserId, Guid ConversationId, bool IsTyping) e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || e.ConversationId != conversationId) return;
        _remoteTyping = e.IsTyping;
        if (StatusLabel is not null)
            StatusLabel.Text = e.IsTyping ? "يكتب الآن..." : "متصل الآن";
    }

    private void SetPresenceText(bool isOnline)
    {
        if (_remoteTyping) return;
        if (StatusLabel is not null) StatusLabel.Text = isOnline ? "متصل الآن" : "غير متصل";
        if (StatusDot is not null)
        {
            StatusDot.Fill = new SolidColorBrush(
                isOnline
                    ? Color.FromArgb("#49D486")
                    : Color.FromArgb("#A7A0B2"));
        }
    }

    private void MessageEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _conversationId == 0 || !Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        var hasText = !string.IsNullOrWhiteSpace(e.NewTextValue);
        _typingCts?.Cancel();
        if (!hasText)
        {
            if (_typingActive)
            {
                _typingActive = false;
                _ = _realtime.SetTypingAsync(conversationId, false);
            }
            return;
        }

        if (!_typingActive)
        {
            _typingActive = true;
            _ = _realtime.SetTypingAsync(conversationId, true);
        }

        _typingCts = new CancellationTokenSource();
        var token = _typingCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                _typingActive = false;
                await _realtime.SetTypingAsync(conversationId, false, token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void StopTyping()
    {
        _typingCts?.Cancel();
        _typingCts?.Dispose();
        _typingCts = null;
        if (_typingActive)
        {
            _typingActive = false;
            if (Guid.TryParse(_remoteConversationId, out var conversationId))
                _ = _realtime.SetTypingAsync(conversationId, false);
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("..", false);
    }

    private async Task ClearConversationNotificationAsync()
    {
        try
        {
            if (_conversationId == 0) return;
            await _notifications.ClearConversationAsync(_conversationId.ToString());
        }
        catch
        {
            // Notification cleanup must never prevent opening a chat.
        }
    }

    private void StartPollingFallback()
    {
        StopPolling();
        if (!_api.HasToken) return;
        _pollCts = new CancellationTokenSource();
        _ = PollMessagesAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                await LoadRemoteAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void OnRealtimeMessageReceived(object? sender, MessageDto message)
    {
        // MessageReceived is sent only to recipients, so acknowledge delivery as soon as
        // the device receives the persisted message. Read state is handled separately
        // when the conversation is visible.
        _ = _realtime.MarkMessageDeliveredAsync(message.Id);

        var conversation = _chat.Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, message.ConversationId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        if (conversation is null) return;

        var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
        var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
        var added = _chat.AddRemoteMessage(conversation.Id, message.Text, message.SentAt.LocalDateTime, isMine, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"), message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
        if (!added) return;

        if (message.AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            var localMessage = _chat.GetMessages(conversation.Id).LastOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase));
            _ = PrepareImagePreviewAsync(localMessage);
        }

        if (conversation.Id == _conversationId)
        {
            _chat.MarkAsRead(conversation.Id);
            var visibleMessages = _chat.GetMessages(conversation.Id);
            SetMessagesItemsSource(visibleMessages);
            if (visibleMessages.Count > 0)
                Messages?.ScrollTo(visibleMessages[^1], position: ScrollToPosition.End, animate: false);
            _ = ClearConversationNotificationAsync();
            _ = MarkRemoteConversationReadAsync(message.ConversationId);
        }
    }

    private async Task MarkRemoteConversationReadAsync(Guid conversationId)
    {
        try { await _api.MarkConversationReadAsync(conversationId); } catch { }
    }

    private async Task LoadRemoteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _remoteLoadGate.WaitAsync(0, cancellationToken)) return;

        try
        {
            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteIdGuid) || !_api.HasToken) return;

            try
            {
                var messagesBeforeSync = _chat.GetMessages(_conversationId);
                var messageCountBeforeSync = messagesBeforeSync.Count;
                var lastSyncedAt = messagesBeforeSync
                    .Where(x => !string.IsNullOrWhiteSpace(x.RemoteId))
                    .Select(x => (DateTimeOffset?)new DateTimeOffset(x.SentAt))
                    .Max();
                var remoteMessages = await _api.GetMessagesAsync(remoteIdGuid, lastSyncedAt, cancellationToken);
                var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
                foreach (var message in remoteMessages)
                {
                    var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                    // The conversation is currently visible, so do not raise a system
                    // notification for a message the user can already see. The home
                    // screen handles notifications for unread messages.
                    if (!isMine) _ = _realtime.MarkMessageDeliveredAsync(message.Id);
                    var added = _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, isMine, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"), message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
                    if (added && message.AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var localMessage = _chat.GetMessages(_conversationId).LastOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase));
                        _ = PrepareImagePreviewAsync(localMessage);
                    }
                }

                var messages = _chat.GetMessages(_conversationId);
                var receivedNewMessages = messages.Count > messageCountBeforeSync;
                if (receivedNewMessages)
                {
                    // Only notify the server that the conversation was read when this
                    // sync actually brought new messages. Polling an unchanged chat
                    // should not generate a write request every cycle.
                    _chat.MarkAsRead(_conversationId);
                    await _notifications.ClearConversationAsync(_conversationId.ToString());
                    try
                    {
                        await _api.MarkConversationReadAsync(remoteIdGuid, cancellationToken);
                    }
                    catch { /* Local read state remains available if the server is temporarily offline. */ }
                }

                // Do not replace the CollectionView ItemsSource when polling found
                // nothing new. Rebinding the entire message list is expensive on Android
                // and can cause visible freezes even though the data did not change.
                if (receivedNewMessages)
                {
                    SetMessagesItemsSource(messages);
                    EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);
                    if (messages.Count > 0)
                        Messages?.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false);
                }
            }
            catch { }
        }
        finally
        {
            _remoteLoadGate.Release();
        }
    }

    private async void RecordAudioClicked(object? sender, EventArgs e)
    {
        if (_isRecordingAudio)
        {
            await StopAndSendAudioAsync();
            return;
        }

#if ANDROID
        try
        {
            if (_conversationId == 0 || !_api.HasToken) return;
            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out _))
            {
                await DisplayAlertAsync("الرسائل الصوتية", "حدّث المحادثة ثم حاول مرة أخرى.", "حسنًا");
                return;
            }

            var permission = await Permissions.RequestAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("الرسائل الصوتية", "يلزم السماح باستخدام الميكروفون لتسجيل رسالة صوتية.", "حسنًا");
                return;
            }

            _audioRecordingPath = Path.Combine(FileSystem.Current.CacheDirectory, $"himo_voice_{Guid.NewGuid():N}.m4a");
#pragma warning disable CA1422 // MediaRecorder is the available Android API for this recording path; no supported .NET replacement is exposed here.
            _audioRecorder = new global::Android.Media.MediaRecorder();
#pragma warning restore CA1422
            _audioRecorder.SetAudioSource(global::Android.Media.AudioSource.Mic);
            _audioRecorder.SetOutputFormat(global::Android.Media.OutputFormat.Mpeg4);
            _audioRecorder.SetAudioEncoder(global::Android.Media.AudioEncoder.Aac);
            _audioRecorder.SetAudioEncodingBitRate(64000);
            _audioRecorder.SetAudioSamplingRate(44100);
            _audioRecorder.SetOutputFile(_audioRecordingPath);
            _audioRecorder.Prepare();
            _audioRecorder.Start();
            _isRecordingAudio = true;
            RecordButton.Source = "icon_stop.svg";
            RecordingBar.IsVisible = true;
            MessageEntry.IsEnabled = false;
            SendButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            CleanupAudioRecorder();
            await DisplayAlertAsync("الرسائل الصوتية", $"تعذر بدء التسجيل: {ex.Message}", "حسنًا");
        }
#else
        await DisplayAlertAsync("الرسائل الصوتية", "تسجيل الصوت متاح حاليًا على Android.", "حسنًا");
#endif
    }

    private async Task StopAndSendAudioAsync()
    {
#if ANDROID
        var path = _audioRecordingPath;
        try
        {
            _audioRecorder?.Stop();
            _audioRecorder?.Reset();
            _audioRecorder?.Release();
            _audioRecorder = null;
            _isRecordingAudio = false;
            RecordButton.Source = "icon_mic.svg";
            RecordingBar.IsVisible = false;
            MessageEntry.IsEnabled = true;
            SendButton.IsEnabled = true;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > 25L * 1024 * 1024)
            {
                await DisplayAlertAsync("الرسائل الصوتية", "تعذر إرسال التسجيل أو تجاوز حجمه 25 ميجابايت.", "حسنًا");
                return;
            }

            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteIdGuid)) return;

            SendButton.IsEnabled = false;
            await using var stream = File.OpenRead(path);
            var message = await _api.UploadAttachmentAsync(remoteIdGuid, stream, Path.GetFileName(path), "audio/mp4");
            _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, true, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"), message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
            RefreshMessagesView(scrollToEnd: true);
        }
        catch (Exception ex)
        {
            CleanupAudioRecorder();
            await DisplayAlertAsync("الرسائل الصوتية", $"تعذر إرسال التسجيل: {ex.Message}", "حسنًا");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            _audioRecordingPath = null;
            MessageEntry.IsEnabled = true;
            SendButton.IsEnabled = true;
        }
#else
        await Task.CompletedTask;
#endif
    }

    private async void AudioClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsAudioAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        try
        {
            var path = message.AttachmentLocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "voice.m4a");
                message.AttachmentLocalPath = path;
            }
            StopAudioPlayback();
            _audioPlayer = new global::Android.Media.MediaPlayer();
            _audioPlayer.SetDataSource(path);
            _audioPlayer.Prepared += (_, _) => _audioPlayer?.Start();
            _audioPlayer.Completion += (_, _) => StopAudioPlayback();
            _audioPlayer.PrepareAsync();
        }
        catch (Exception ex)
        {
            StopAudioPlayback();
            await DisplayAlertAsync("الرسالة الصوتية", $"تعذر تشغيل التسجيل: {ex.Message}", "حسنًا");
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private void StopAudioPlayback()
    {
        try { _audioPlayer?.Stop(); } catch { }
        try { _audioPlayer?.Release(); } catch { }
        _audioPlayer = null;
    }

    private void CleanupAudioRecorder()
    {
        try { _audioRecorder?.Reset(); } catch { }
        try { _audioRecorder?.Release(); } catch { }
        _audioRecorder = null;
        _isRecordingAudio = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (RecordButton is not null) RecordButton.Source = "icon_mic.svg";
            if (RecordingBar is not null) RecordingBar.IsVisible = false;
            if (MessageEntry is not null) MessageEntry.IsEnabled = true;
            if (SendButton is not null) SendButton.IsEnabled = true;
        });
    }
#endif

    private async void AttachmentButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_conversationId == 0 || !_api.HasToken) return;
            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteIdGuid))
            {
                await DisplayAlertAsync("المرفقات", "حدّث المحادثة ثم حاول مرة أخرى.", "حسنًا");
                return;
            }

            var choice = await DisplayActionSheetAsync("إضافة مرفق", "إلغاء", null, "صورة", "ملف");
            if (string.Equals(choice, "إلغاء", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(choice)) return;

            FileResult? file;
            if (string.Equals(choice, "صورة", StringComparison.Ordinal))
            {
                file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "اختر صورة",
                    FileTypes = FilePickerFileType.Images
                });
            }
            else
            {
                file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "اختر ملفًا"
                });
            }
            if (file is null) return;

            if (file.FileName.Length > 180)
            {
                await DisplayAlertAsync("المرفقات", "اسم الملف طويل جدًا.", "حسنًا");
                return;
            }

            await using (var selectedStream = await file.OpenReadAsync())
            {
                if (selectedStream.CanSeek && selectedStream.Length > 25L * 1024 * 1024)
                {
                    await DisplayAlertAsync("المرفقات", "الحد الأقصى لحجم الملف 25 ميجابايت.", "حسنًا");
                    return;
                }
            }

            SendButton.IsEnabled = false;
            var message = await _api.UploadAttachmentAsync(remoteIdGuid, file);
            _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, true, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"), message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
            RefreshMessagesView(scrollToEnd: true);
            await PrepareImagePreviewAsync(_chat.GetMessages(_conversationId).LastOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase)));
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("المرفقات", ex.Message, "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المرفقات", $"تعذر إرسال الملف: {ex.Message}", "حسنًا");
        }
        finally
        {
            if (SendButton is not null) SendButton.IsEnabled = true;
        }
    }

    private async Task PrepareImagePreviewAsync(ChatMessage? message)
    {
        if (message is null || !message.IsImageAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        if (!string.IsNullOrWhiteSpace(message.AttachmentLocalPath) && File.Exists(message.AttachmentLocalPath)) return;

        try
        {
            var path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "image");
            message.AttachmentLocalPath = path;
        }
        catch
        {
            // Inline preview is optional; the attachment can still be opened manually.
        }
    }

    private async void AttachmentClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        try
        {
            var path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "file");
            await Launcher.Default.OpenAsync(new OpenFileRequest(message.AttachmentFileName ?? "file", new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المرفق", $"تعذر فتح الملف: {ex.Message}", "حسنًا");
        }
    }

    private async void EditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsMine || !Guid.TryParse(message.RemoteId, out var messageId) || message.IsAttachment) return;
        var edited = await DisplayPromptAsync("تعديل الرسالة", "عدّل النص:", initialValue: message.Text, maxLength: 4000, keyboard: Keyboard.Text);
        if (edited is null) return;
        edited = edited.Trim();
        if (string.IsNullOrWhiteSpace(edited) || string.Equals(edited, message.Text, StringComparison.Ordinal)) return;
        try
        {
            var remote = await _api.EditMessageAsync(messageId, edited);
            _chat.UpdateMessageText(remote.Id.ToString("D"), remote.Text, DateTime.TryParse(remote.EditedAt, out var at) ? at : null);
            RefreshMessagesView(scrollToEnd: false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعديل الرسالة", ex.Message, "حسنًا");
        }
    }

    private async void DeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsMine || message.IsDeleted || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        var confirm = await DisplayAlertAsync("حذف الرسالة", "هل تريد حذف هذه الرسالة؟", "حذف", "إلغاء");
        if (!confirm) return;
        try
        {
            var deleted = await _api.DeleteMessageAsync(messageId);
            _chat.UpdateMessageDeleted(deleted.Id.ToString("D"));
            RefreshMessagesView(scrollToEnd: false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("حذف الرسالة", ex.Message, "حسنًا");
        }
    }

    private async void ForwardClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || message.IsDeleted || message.IsAttachment || string.IsNullOrWhiteSpace(message.Text)) return;

        try
        {
            var conversations = await _api.GetConversationsAsync();
            var targets = conversations
                .Where(x => !string.Equals(x.Id.ToString("D"), _remoteConversationId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedAt)
                .ToList();

            if (targets.Count == 0)
            {
                await DisplayAlertAsync("تحويل الرسالة", "لا توجد محادثات أخرى متاحة.", "حسنًا");
                return;
            }

            var options = targets.Select(x => string.IsNullOrWhiteSpace(x.Name) ? "محادثة" : x.Name).ToArray();
            var selected = await DisplayActionSheetAsync("تحويل الرسالة إلى", "إلغاء", null, options);
            if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "إلغاء", StringComparison.Ordinal)) return;

            var target = targets.FirstOrDefault(x => string.Equals(x.Name, selected, StringComparison.Ordinal));
            if (target is null) return;

            await _api.SendMessageAsync(target.Id, message.Text);
            await DisplayAlertAsync("تحويل الرسالة", "تم تحويل الرسالة.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تحويل الرسالة", ex.Message, "حسنًا");
        }
    }

    private void ReplyClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message) return;
        _replyingTo = message;
        ReplyPreviewLabel?.Text = string.IsNullOrWhiteSpace(message.Text) ? "رسالة مرفقة" : message.Text;
        ReplyPreview?.SetValue(IsVisibleProperty, true);
        MessageEntry?.Focus();
    }

    private void ClearReplyClicked(object? sender, EventArgs e) => ClearReply();

    private async void MessageActionsClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || message.IsDeleted) return;

        var actions = new List<string> { "تحديد" };
        if (!message.IsAttachment)
        {
            actions.Add("رد");
            actions.Add("تحويل");
        }
        if (message.IsMine)
        {
            if (!message.IsAttachment) actions.Add("تعديل");
            actions.Add("حذف");
        }

        var selected = await DisplayActionSheetAsync("خيارات الرسالة", "إلغاء", null, actions.ToArray());
        if (string.Equals(selected, "تحديد", StringComparison.Ordinal))
            SelectMessageClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "رد", StringComparison.Ordinal))
            ReplyClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "تحويل", StringComparison.Ordinal))
            ForwardClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "تعديل", StringComparison.Ordinal))
            EditClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "حذف", StringComparison.Ordinal))
            DeleteClicked(button, EventArgs.Empty);
    }

    private void ToggleMessageSelection(ChatMessage message)
    {
        if (message.IsDeleted) return;
        if (!_selectionMode) _selectionMode = true;

        if (!_selectedMessageIds.Add(message.Id))
        {
            _selectedMessageIds.Remove(message.Id);
            message.IsSelected = false;
        }
        else
        {
            message.IsSelected = true;
        }

        if (_selectedMessageIds.Count == 0) ExitSelectionMode();
        else UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        SelectionBar?.SetValue(IsVisibleProperty, _selectionMode);
        SelectionCountLabel?.SetValue(Label.TextProperty, $"{_selectedMessageIds.Count} محددة");
    }

    private void ExitSelectionMode()
    {
        foreach (var message in _chat.GetMessages(_conversationId))
            message.IsSelected = false;
        _selectedMessageIds.Clear();
        _selectionMode = false;
        SelectionBar?.SetValue(IsVisibleProperty, false);
    }

    private void SelectMessageClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message) return;
        ToggleMessageSelection(message);
    }

    private void CancelSelectionClicked(object? sender, EventArgs e) => ExitSelectionMode();

    private async void DeleteSelectedClicked(object? sender, EventArgs e)
    {
        var selected = _chat.GetMessages(_conversationId)
            .Where(x => _selectedMessageIds.Contains(x.Id) && x.IsMine && !x.IsDeleted)
            .ToList();
        if (selected.Count == 0)
        {
            ExitSelectionMode();
            return;
        }

        var confirm = await DisplayAlertAsync("حذف الرسائل", $"حذف {selected.Count} رسالة؟", "حذف", "إلغاء");
        if (!confirm) return;

        foreach (var message in selected)
        {
            if (!Guid.TryParse(message.RemoteId, out var messageId)) continue;
            try
            {
                var deleted = await _api.DeleteMessageAsync(messageId);
                _chat.UpdateMessageDeleted(deleted.Id.ToString("D"));
            }
            catch
            {
                // Keep processing the remaining selected messages.
            }
        }

        ExitSelectionMode();
        RefreshMessagesView(scrollToEnd: false);
    }

    private void ClearReply()
    {
        _replyingTo = null;
        if (ReplyPreview is not null) ReplyPreview.IsVisible = false;
        if (ReplyPreviewLabel is not null) ReplyPreviewLabel.Text = string.Empty;
    }

    private async void SendClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _sending, 1) != 0)
            return;

        try
        {
            if (SendButton is not null)
            {
                SendButton.IsEnabled = false;
                SendButton.Source = "icon_send.svg";
            }

            if (MessageEntry is not null)
                MessageEntry.IsEnabled = false;

            await SendMessageAsync();
        }
        finally
        {
            if (MessageEntry is not null)
                MessageEntry.IsEnabled = true;

            if (SendButton is not null)
            {
                SendButton.IsEnabled = true;
                SendButton.Source = "icon_send.svg";
            }

            Interlocked.Exchange(ref _sending, 0);
        }
    }

    private async Task SendMessageAsync()
    {
        StopTyping();
        var text = MessageEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _conversationId == 0)
            return;
        if (text.Length > 4000)
        {
            await DisplayAlertAsync("الإرسال", "الرسالة طويلة جدًا. الحد الأقصى 4000 حرف.", "حسنًا");
            return;
        }

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (_api.HasToken)
        {
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out _))
            {
                await DisplayAlertAsync("الإرسال", "هذه المحادثة غير مرتبطة بالخادم. حدّث قائمة المحادثات ثم حاول مرة أخرى.", "حسنًا");
                return;
            }

            var clientMessageId = Guid.NewGuid().ToString("D");
            var pending = _chat.AddPendingMessage(_conversationId, text, clientMessageId, _replyingTo?.RemoteId, _replyingTo?.Text);
            if (pending is null) return;

            // Durable outbox: the message is persisted before network I/O. The UI
            // clears immediately and the request continues in the background.
            MessageEntry?.Text = string.Empty;
            ClearReply();
            RefreshMessagesView(scrollToEnd: true);
            _ = FlushOutboxAsync();
            return;
        }

        var message = _chat.Send(_conversationId, text);
        if (message == null) return;
        MessageEntry?.Text = string.Empty;
        RefreshMessagesView(scrollToEnd: true);
    }

    private void SetMessagesItemsSource(IReadOnlyList<ChatMessage> messages)
    {
        DateTime? previousDate = null;
        foreach (var message in messages)
        {
            var date = message.SentAt.Date;
            if (previousDate != date)
            {
                var today = DateTime.Today;
                message.DateSeparatorText = date == today
                    ? "اليوم"
                    : date == today.AddDays(-1)
                        ? "أمس"
                        : date.ToString("dd/MM/yyyy");
            }
            else
            {
                message.DateSeparatorText = string.Empty;
            }
            previousDate = date;
        }

        if (Messages is not null)
            Messages.ItemsSource = messages;
    }

    private void SearchMessagesClicked(object? sender, EventArgs e)
    {
        MessageSearchBar?.SetValue(IsVisibleProperty, true);
        MessageSearchEntry?.Focus();
    }

    private void CloseMessageSearchClicked(object? sender, EventArgs e)
    {
        if (MessageSearchEntry is not null)
            MessageSearchEntry.Text = string.Empty;
        MessageSearchBar?.SetValue(IsVisibleProperty, false);
        RefreshMessagesView(false);
    }

    private void MessageSearchChanged(object? sender, EventArgs e)
    {
        ApplyMessageSearch();
    }

    private void ApplyMessageSearch()
    {
        var all = _chat.GetMessages(_conversationId);
        var query = MessageSearchEntry?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            SetMessagesItemsSource(all);
            return;
        }

        var filtered = all.Where(x =>
            (!string.IsNullOrWhiteSpace(x.Text) && x.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(x.ReplyToText) && x.ReplyToText.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(x.AttachmentFileName) && x.AttachmentFileName.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        SetMessagesItemsSource(filtered);
        EmptyState?.SetValue(IsVisibleProperty, filtered.Count == 0);
    }

    private void MessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (ScrollToBottomButton is null || messages.Count == 0) return;

        var lastVisible = e.LastVisibleItemIndex;
        ScrollToBottomButton.IsVisible = lastVisible >= 0 && lastVisible < messages.Count - 2;
    }

    private void ScrollToBottomClicked(object? sender, EventArgs e)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (Messages is not null && messages.Count > 0)
            Messages.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: true);

        if (ScrollToBottomButton is not null)
            ScrollToBottomButton.IsVisible = false;
    }

    private void RefreshMessagesView(bool scrollToEnd)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (Messages is not null)
            SetMessagesItemsSource(messages);

        EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);

        if (scrollToEnd && Messages is not null && messages.Count > 0)
            Messages.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: true);
    }

}