using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net.Http;
using Himo.Services;
using Himo.Models;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

#if ANDROID
using Android.Views;
using AndroidX.RecyclerView.Widget;
#endif

namespace Himo.Views;

[QueryProperty(nameof(ConversationId), "id")]
public partial class ChatPage : ContentPage
{
    private const int MessagePageSize = 30;
    private const int MessageSyncPageSize = 100;
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly INotificationService _notifications;
    private readonly HimoRealtimeService _realtime;
    private readonly ICallService _calls;
    private readonly RangeObservableCollection<ChatMessage> _visibleMessages = new();
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _remoteLoadGate = new(1, 1);
    private int _conversationId;
    private int _loadingOlder;
    private bool _allowOlderPaging;
    private bool _initialPositioningLatest;
    private int _lastVisibleItemIndex = -1;
    private bool _hasMoreOlderMessages = true;
    private DateTime? _visibleOldestSentAt;
    private string? _visibleOldestRemoteId;
    private string? _remoteConversationId;
    private int _sending;
    private bool _isRecordingAudio;
    private CancellationTokenSource? _typingCts;
    private bool _typingActive;
    private bool _remoteTyping;
    private bool _initialRemoteSyncCompleted;
    private ChatMessage? _replyingTo;
    private readonly HashSet<int> _selectedMessageIds = new();
    private bool _selectionMode;
    private bool IsConversationMuted => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_muted_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationArchived => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_archived_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationBlocked => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_blocked_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
#if ANDROID
    private global::Android.Media.MediaRecorder? _audioRecorder;
    private global::Android.Media.Ringtone? _incomingCallRingtone;
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
                return;
            }

            if (Guid.TryParse(value, out var remoteId))
            {
                PrepareForConversationChange();

                _remoteConversationId = remoteId.ToString("D");

                // Fast local path: the conversation list is cached locally.
                // Resolve the remote GUID to the local integer ID without any
                // network call, so the last cached messages can render immediately.
                var cachedConversation = _chat.Conversations.FirstOrDefault(x =>
                    string.Equals(
                        x.RemoteId,
                        _remoteConversationId,
                        StringComparison.OrdinalIgnoreCase));

                _conversationId = cachedConversation?.Id ?? 0;
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
        _hasMoreOlderMessages = true;
        _allowOlderPaging = false;
        _initialPositioningLatest = true;
        Interlocked.Exchange(ref _initialScrollInProgress, 0);
        _lastVisibleItemIndex = -1;
        _initialRemoteSyncCompleted = false;
        _localFirstRenderCompleted = false;
        _visibleOldestSentAt = null;
        _visibleOldestRemoteId = null;
        _visibleMessages.Clear();
        if (MessageEntry is not null) MessageEntry.Text = string.Empty;
        if (ReplyPreview is not null) ReplyPreview.IsVisible = false;
        if (MessageSearchBar is not null) MessageSearchBar.IsVisible = false;
        if (MessageSearchEntry is not null) MessageSearchEntry.Text = string.Empty;
        if (ScrollToBottomButton is not null) ScrollToBottomButton.IsVisible = false;
    }

    public ChatPage(ChatService chat, HimoApiClient api, AccountService account, INotificationService notifications, HimoRealtimeService realtime, ICallService calls)
    {
        InitializeComponent();
        Messages.ItemsSource = _visibleMessages;
        _chat = chat; _api = api; _account = account; _notifications = notifications; _realtime = realtime; _calls = calls;
        _realtime.MessageReceived += OnRealtimeMessageReceived;
        _realtime.UserPresenceChanged += OnUserPresenceChanged;
        _realtime.UserTypingChanged += OnUserTypingChanged;
        _realtime.MessageDeliveryChanged += OnMessageDeliveryChanged;
        _realtime.MessageEdited += OnMessageEdited;
        _realtime.MessageDeleted += OnMessageDeleted;
        _realtime.Reconnected += OnRealtimeReconnected;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
        StartPollingFallback();

        // Do not execute the first message bind while Shell is still performing
        // the navigation transition. Let Android draw the ChatPage first, then
        // populate the first message page on the next UI turn.
        Dispatcher.Dispatch(() =>
        {
            _ = InitializeChatAsync();
        });
    }

    private int _initializationVersion;
    private int _initializationRunning;
    private bool _localFirstRenderCompleted;
    private int _initialScrollInProgress;

    private async Task InitializeChatAsync()
    {
        if (Interlocked.Exchange(ref _initializationRunning, 1) != 0)
            return;

        var version = Interlocked.Increment(ref _initializationVersion);

        try
        {
            if (version == Volatile.Read(ref _initializationVersion) &&
                !_localFirstRenderCompleted)
            {
                // Let the current navigation/layout frame complete first.
                // Then render the cached last page immediately; do not wait for network.
                await Task.Yield();

                if (version == Volatile.Read(ref _initializationVersion) &&
                    !_localFirstRenderCompleted)
                {
                    Load();
                }
            }

            _ = _notifications.InitializeAsync();

            // Never make the first frame wait for authentication/server/SignalR.
            // Cached messages are rendered first; network synchronization follows.
            _ = InitializeRemoteSyncAsync(version);
            _ = FlushOutboxAsync();
            _ = ClearConversationNotificationAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Initialize failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _initializationRunning, 0);
        }
    }

    private async Task InitializeRemoteSyncAsync(int version)
    {
        try
        {
            await _api.TokenInitialization.ConfigureAwait(false);
            if (!_api.HasToken || version != Volatile.Read(ref _initializationVersion))
                return;

            await ResolveRemoteConversationAsync().ConfigureAwait(false);

            if (version != Volatile.Read(ref _initializationVersion))
                return;

            if (!_localFirstRenderCompleted)
                await MainThread.InvokeOnMainThreadAsync(Load);

            if (version != Volatile.Read(ref _initializationVersion))
                return;

            _ = StartRealtimeSafelyAsync(version);
            _ = LoadRemoteSafelyAsync(version);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Remote initialization failed: {ex}");
        }
    }

    private async Task StartRealtimeSafelyAsync(int version)
    {
        try
        {
            await StartRealtimeAsync();
            if (version == Volatile.Read(ref _initializationVersion) &&
                Guid.TryParse(_remoteConversationId, out var remoteId))
            {
                var online = await _realtime.GetPresenceAsync(remoteId);
                await MainThread.InvokeOnMainThreadAsync(() => SetPresenceText(online));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Realtime startup failed: {ex}");
        }
    }

    private async Task LoadRemoteSafelyAsync(int version)
    {
        try
        {
            await LoadRemoteAsync();

            if (version != Volatile.Read(ref _initializationVersion))
                return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Remote history sync failed: {ex}");
        }
    }

    private async Task ResolveRemoteConversationAsync()
    {
        // Fast cache-only resolution. This must happen before any HTTP call.
        if (_conversationId <= 0 &&
            !string.IsNullOrWhiteSpace(_remoteConversationId))
        {
            var cached = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(
                    x.RemoteId,
                    _remoteConversationId,
                    StringComparison.OrdinalIgnoreCase));

            if (cached is not null)
            {
                _conversationId = cached.Id;
            }
        }

        if (_conversationId > 0 &&
            !string.IsNullOrWhiteSpace(_remoteConversationId))
        {
            return;
        }

        // Normal path: the navigation query already contains the remote GUID.
        if (!string.IsNullOrWhiteSpace(_remoteConversationId) && Guid.TryParse(_remoteConversationId, out var remoteId))
        {
            var existing = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(x.RemoteId, _remoteConversationId, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _conversationId = existing.Id;
                return;
            }

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
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Remote conversation resolve failed: {ex.Message}");
            }
        }

        // Recovery path: older local stores can contain a conversation with only
        // the local integer ID. Resolve that record against the server before the
        // first history request; otherwise the page has no GUID and silently skips
        // loading messages until another action refreshes the conversation list.
        if (_conversationId <= 0 || !_api.HasToken)
            return;

        try
        {
            var local = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (local is null) return;
            if (Guid.TryParse(local.RemoteId, out var alreadyBound))
            {
                _remoteConversationId = alreadyBound.ToString("D");
                return;
            }

            var remoteList = await _api.GetConversationsAsync();
            var candidates = remoteList
                .Where(x => string.Equals(x.Name?.Trim(), local.Name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return;

            HimoApiClient.ConversationDto? best = null;
            var bestScore = int.MinValue;
            var tied = false;
            foreach (var candidate in candidates)
            {
                var score = 1; // exact display-name match
                if (!string.IsNullOrWhiteSpace(local.LastMessage) &&
                    !string.Equals(local.LastMessage, "محادثة جديدة", StringComparison.Ordinal) &&
                    string.Equals(local.LastMessage.Trim(), candidate.LastMessage?.Trim(), StringComparison.Ordinal))
                    score += 4;

                var delta = Math.Abs((candidate.UpdatedAt.LocalDateTime - local.UpdatedAt).TotalMinutes);
                if (delta <= 5) score += 2;
                else if (delta <= 60) score += 1;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    tied = false;
                }
                else if (score == bestScore)
                {
                    tied = true;
                }
            }

            // Name-only matching is acceptable only when it is unique. When a
            // stronger message/time match exists, accept that unique best match.
            if (best is null || (tied && bestScore < 3))
                return;

            local.RemoteId = best.Id.ToString("D");
            local.LastMessage = best.LastMessage;
            local.Time = best.UpdatedAt.LocalDateTime.ToString("HH:mm");
            local.UpdatedAt = best.UpdatedAt.LocalDateTime;
            local.UnreadCount = best.UnreadCount;
            _remoteConversationId = local.RemoteId;

            _conversationId = local.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Local conversation hydration failed: {ex.Message}");
        }
    }

    private void Load()
    {
        if (_conversationId == 0)
            return;

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation == null)
            return;

        var displayName = string.IsNullOrWhiteSpace(conversation.Name) ? "محادثة" : conversation.Name.Trim();
        NameLabel?.SetValue(Label.TextProperty, displayName);
        InitialLabel?.SetValue(Label.TextProperty, string.IsNullOrWhiteSpace(conversation.Initial) ? displayName[..1] : conversation.Initial);

        var recent = _chat.GetRecentMessages(_conversationId, MessagePageSize);

        SetMessagesItemsSource(recent);

        // A local cache may contain more history than the first page.
        // We do not sort/copy the whole history just to determine this.
        var localCount = _chat.GetMessages(_conversationId).Count;
        _hasMoreOlderMessages = localCount > recent.Count || _hasMoreOlderMessages;
        UpdateVisiblePagingCursor();
        EmptyState?.SetValue(IsVisibleProperty, recent.Count == 0);
        _localFirstRenderCompleted = true;

        if (recent.Count == 0)
    
        if (UnreadDivider is not null)
            UnreadDivider.IsVisible = conversation.UnreadCount > 0 && recent.Count > 0;

        _chat.MarkAsRead(_conversationId);

        if (recent.Count > 0)
            ConfigureInitialChatPosition();
    }

    private List<ChatMessage> GetOrderedLocalMessages()
    {
        return _chat.GetMessages(_conversationId)
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private ChatMessage? GetVisibleOldestMessage()
    {
        return _visibleMessages.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.RemoteId))
            ?? _visibleMessages.FirstOrDefault();
    }

    private void UpdateVisiblePagingCursor()
    {
        var oldest = GetVisibleOldestMessage();
        _visibleOldestSentAt = oldest?.SentAt;
        _visibleOldestRemoteId = oldest?.RemoteId;
    }

    private void AppendNewMessagesToVisible()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(AppendNewMessagesToVisible);
            return;
        }

        if (_visibleMessages.Count == 0)
        {
            var recent = _chat.GetRecentMessages(_conversationId, MessagePageSize);
            SetMessagesItemsSource(recent);
            UpdateVisiblePagingCursor();
            return;
        }

        var all = GetOrderedLocalMessages();

        var currentKeys = _visibleMessages.Select(GetMessageUiKey).ToHashSet(StringComparer.Ordinal);
        var newestVisible = _visibleMessages[^1];
        var additions = all
            .Where(x => CompareMessageOrder(x, newestVisible) > 0 && !currentKeys.Contains(GetMessageUiKey(x)))
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();

        if (additions.Count > 0)
            _visibleMessages.InsertRange(_visibleMessages.Count, additions);

        UpdateMessageDateSeparators(_visibleMessages.ToList());
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
        StopIncomingCallRingtone();
#endif
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        StopPolling();
        StopTyping();
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
#if ANDROID
        StartIncomingCallRingtone();
#endif
        try
        {
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
        finally
        {
#if ANDROID
            StopIncomingCallRingtone();
#endif
        }
    }

#if ANDROID
    private void StartIncomingCallRingtone()
    {
        try
        {
            StopIncomingCallRingtone();
            var context = global::Android.App.Application.Context;
            var uri = global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone);
            if (uri is null) return;

            _incomingCallRingtone = global::Android.Media.RingtoneManager.GetRingtone(context, uri);
            _incomingCallRingtone?.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Incoming ringtone failed: {ex}");
            StopIncomingCallRingtone();
        }
    }

    private void StopIncomingCallRingtone()
    {
        try { _incomingCallRingtone?.Stop(); } catch { }
        try { _incomingCallRingtone?.Dispose(); } catch { }
        _incomingCallRingtone = null;
    }
#endif

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
        if (!_api.HasToken || Interlocked.Exchange(ref _flushingOutbox, 1) != 0)
            return;

        try
        {
            while (_api.HasToken)
            {
                // Take a fresh snapshot every pass. This is important when the user
                // sends message B while message A is still uploading: B must not
                // wait for the 30-second polling fallback.
                var pendingBatch = _chat.GetPendingMessages()
                    .OrderBy(x => x.SentAt)
                    .ThenBy(x => x.Id)
                    .ToList();

                if (pendingBatch.Count == 0)
                    break;

                var madeProgress = false;

                foreach (var pending in pendingBatch)
                {
                    if (!_api.HasToken)
                        break;

                    var conversation = _chat.Conversations
                        .FirstOrDefault(x => x.Id == pending.ConversationId);

                    if (conversation?.RemoteId is not string remoteId ||
                        !Guid.TryParse(remoteId, out var conversationId) ||
                        string.IsNullOrWhiteSpace(pending.ClientMessageId))
                    {
                        // Keep the message pending. Conversation resolution/auth
                        // recovery may make it sendable later.
                        continue;
                    }

                    var clientMessageId = pending.ClientMessageId;

                    _chat.MarkPendingSending(clientMessageId);

                    try
                    {
                        var replyId =
                            Guid.TryParse(
                                pending.ReplyToRemoteId,
                                out var parsedReplyId)
                                ? parsedReplyId
                                : (Guid?)null;

                        var remote = await _api.SendMessageAsync(
                            conversationId,
                            pending.Text,
                            clientMessageId,
                            replyId);

                        _chat.CompletePendingMessage(
                            pending.ConversationId,
                            clientMessageId,
                            remote.Id.ToString("D"),
                            remote.SentAt.LocalDateTime);

                        madeProgress = true;
                    }
                    catch (OperationCanceledException)
                    {
                        _chat.FailPendingMessage(
                            pending.ConversationId,
                            clientMessageId);

                        if (!_api.HasToken)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _chat.FailPendingMessage(
                            pending.ConversationId,
                            clientMessageId);

                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Text send failed for {clientMessageId}: {ex.Message}");
                    }

                    // Let another queued message get a chance without waiting for
                    // the UI event/polling loop.
                    await Task.Yield();
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                    RefreshMessagesView(scrollToEnd: false));

                // If none of the current pending messages could be processed, stop
                // here. The normal reconnect/polling path will retry without a
                // tight battery-draining loop.
                if (!madeProgress)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushingOutbox, 0);

            // A message can be queued in the tiny window between the final snapshot
            // and releasing the gate. One short follow-up pass catches that race.
            if (_api.HasToken &&
                _chat.GetPendingMessages().Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await FlushOutboxAsync();
                });
            }
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
            StatusDot.Fill = isOnline
                ? new SolidColorBrush(Color.FromArgb("#49D486"))
                : new SolidColorBrush(Color.FromArgb("#A7A0B2"));
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
            _ = MainThread.InvokeOnMainThreadAsync(() =>
            {
                var wasNearBottom = _lastVisibleItemIndex < 0 ||
                                     _lastVisibleItemIndex >= Math.Max(0, _visibleMessages.Count - 3);
                AppendNewMessagesToVisible();
                if (_visibleMessages.Count > 0 && wasNearBottom)
                    ScrollToLatestMessage(animate: false);
                return Task.CompletedTask;
            });
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
        await _api.TokenInitialization.ConfigureAwait(false);
        if (!_api.HasToken) return;
        if (!await _remoteLoadGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteConversationId))
                return;

            var wasInitialSync = !_initialRemoteSyncCompleted;
            var cacheBeforeSync = _chat.GetMessages(_conversationId);
            var messageCountBeforeSync = cacheBeforeSync.Count;

            IReadOnlyList<MessageDto> remoteMessages;
            if (wasInitialSync)
            {
                // Only the newest page is fetched when the chat opens.
                remoteMessages = await _api.GetMessagesAsync(
                    remoteConversationId,
                    limit: MessagePageSize,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                DateTimeOffset? lastSyncedAt = cacheBeforeSync
                    .Where(x => !string.IsNullOrWhiteSpace(x.RemoteId))
                    .Select(x => (DateTimeOffset?)new DateTimeOffset(x.SentAt))
                    .DefaultIfEmpty()
                    .Max();

                remoteMessages = await _api.GetMessagesAsync(
                    remoteConversationId,
                    since: lastSyncedAt,
                    limit: MessageSyncPageSize,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _initialRemoteSyncCompleted = true;

            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            foreach (var message in remoteMessages)
            {
                var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                if (!isMine) _ = _realtime.MarkMessageDeliveredAsync(message.Id);

                var added = _chat.AddRemoteMessage(
                    _conversationId, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);

                if (added && message.AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var localMessage = _chat.GetMessages(_conversationId)
                        .FirstOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase));
                    _ = PrepareImagePreviewAsync(localMessage);
                }
            }

            var allMessages = _chat.GetMessages(_conversationId);
            var receivedNewMessages = allMessages.Count > messageCountBeforeSync;

            if (receivedNewMessages)
            {
                _chat.MarkAsRead(_conversationId);
                await _notifications.ClearConversationAsync(_conversationId.ToString()).ConfigureAwait(false);
                try { await _api.MarkConversationReadAsync(remoteConversationId, cancellationToken).ConfigureAwait(false); } catch { }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (wasInitialSync)
                {
                    var recent = allMessages
                        .OrderBy(x => x.SentAt)
                        .ThenBy(x => x.Id)
                        .TakeLast(MessagePageSize)
                        .ToList();
                    SetMessagesItemsSource(recent);
                    _hasMoreOlderMessages = allMessages.Count > recent.Count || remoteMessages.Count >= MessagePageSize;
                    UpdateVisiblePagingCursor();
                    EmptyState?.SetValue(IsVisibleProperty, recent.Count == 0);
                    if (recent.Count > 0) ConfigureInitialChatPosition();
                }
                else if (receivedNewMessages)
                {
                    AppendNewMessagesToVisible();
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] LoadRemote failed: {ex}");
        }
        finally
        {
            _remoteLoadGate.Release();
        }
    }

    private async Task LoadOlderMessagesAsync()
    {
        if (_conversationId == 0 || !_api.HasToken || !_hasMoreOlderMessages)
            return;
        if (Interlocked.Exchange(ref _loadingOlder, 1) != 0)
            return;

        try
        {
            if (!_initialRemoteSyncCompleted)
            {
                await LoadRemoteAsync().ConfigureAwait(false);
                if (!_initialRemoteSyncCompleted) return;
            }

            var anchor = GetVisibleOldestMessage();
            if (anchor is null) return;

            // First consume locally cached history. This is instant and mirrors the
            // local-first behavior of mature chat apps. Only when local history is
            // exhausted do we request the next page from the server.
            var allLocal = GetOrderedLocalMessages();
            var anchorKey = GetMessageUiKey(anchor);
            var anchorIndex = allLocal.FindIndex(x => string.Equals(GetMessageUiKey(x), anchorKey, StringComparison.Ordinal));

            if (anchorIndex > 0)
            {
                var startIndex = Math.Max(0, anchorIndex - MessagePageSize);
                var cachedOlder = allLocal.Skip(startIndex).Take(anchorIndex - startIndex).ToList();
                if (cachedOlder.Count > 0)
                {
                    await PrependOlderMessagesAsync(cachedOlder, anchor).ConfigureAwait(false);
                    _hasMoreOlderMessages = startIndex > 0 || _hasMoreOlderMessages;
                    return;
                }
            }

            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteConversationId))
                return;

            var before = _visibleOldestSentAt.HasValue
                ? new DateTimeOffset(_visibleOldestSentAt.Value)
                : new DateTimeOffset(anchor.SentAt);
            Guid? beforeId = Guid.TryParse(_visibleOldestRemoteId, out var parsedBeforeId) ? parsedBeforeId : null;

            var older = await _api.GetMessagesAsync(
                remoteConversationId,
                before: before,
                beforeId: beforeId,
                limit: MessagePageSize,
                cancellationToken: default).ConfigureAwait(false);

            if (older.Count == 0)
            {
                _hasMoreOlderMessages = false;
                return;
            }

            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            foreach (var message in older)
            {
                var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                _chat.AddRemoteMessage(
                    _conversationId, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString("D"), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted);
            }

            var currentCache = _chat.GetMessages(_conversationId);
            var olderKeys = older.Select(x => x.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var olderLocal = currentCache
                .Where(x => !string.IsNullOrWhiteSpace(x.RemoteId) && olderKeys.Contains(x.RemoteId))
                .OrderBy(x => x.SentAt)
                .ThenBy(x => x.Id)
                .ToList();

            if (olderLocal.Count == 0)
            {
                _hasMoreOlderMessages = false;
                return;
            }

            await PrependOlderMessagesAsync(olderLocal, anchor).ConfigureAwait(false);
            _hasMoreOlderMessages = older.Count >= MessagePageSize;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Load older messages failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _loadingOlder, 0);
        }
    }

    private async Task PrependOlderMessagesAsync(IReadOnlyList<ChatMessage> olderMessages, ChatMessage anchor)
    {
        if (olderMessages.Count == 0) return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var existing = _visibleMessages.Select(GetMessageUiKey).ToHashSet(StringComparer.Ordinal);
            var toInsert = olderMessages
                .Where(x => existing.Add(GetMessageUiKey(x)))
                .OrderBy(x => x.SentAt)
                .ThenBy(x => x.Id)
                .ToList();

            if (toInsert.Count == 0) return;

            _visibleMessages.InsertRange(0, toInsert);
            UpdateMessageDateSeparators(_visibleMessages.ToList());
            UpdateVisiblePagingCursor();

            await Task.Delay(60);
            await SafeScrollTo(anchor, ScrollToPosition.Start, animate: false);
        });
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
            RecordButton.Source = "himo_icon_stop.png";
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
            RecordButton.Source = "himo_icon_mic.png";
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
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    try { File.Delete(path); } catch { }
                }

                path = await _api.DownloadAttachmentAsync(
                    messageId,
                    message.AttachmentFileName ?? "voice.m4a");
                message.AttachmentLocalPath = path;
            }

            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                throw new InvalidOperationException("ملف التسجيل الذي تم تنزيله فارغ.");

            StopAudioPlayback();
            var player = new global::Android.Media.MediaPlayer();
            _audioPlayer = player;

            player.Completion += (_, _) =>
            {
                MainThread.BeginInvokeOnMainThread(StopAudioPlayback);
            };

            player.Error += (_, args) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StopAudioPlayback();
                    await DisplayAlertAsync(
                        "الرسالة الصوتية",
                        $"تعذر تشغيل ملف الصوت على الجهاز. الخطأ: {args.What}/{args.Extra}",
                        "حسنًا");
                });
            };

            player.SetDataSource(path);
            player.Prepare();
            player.Start();
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
            if (RecordButton is not null) RecordButton.Source = "himo_icon_mic.png";
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
                SendButton.Source = "himo_icon_send.png";
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
                SendButton.Source = "himo_icon_send.png";
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
        if (!_api.HasToken)
        {
            await DisplayAlertAsync(
                "الإرسال",
                "جلسة الدخول غير متاحة حاليًا. سجّل الدخول مرة أخرى ثم أعد الإرسال.",
                "حسنًا");
            return;
        }

        if (conversation?.RemoteId is not string remoteId ||
            !Guid.TryParse(remoteId, out _))
        {
            await DisplayAlertAsync(
                "الإرسال",
                "هذه المحادثة غير مرتبطة بالخادم. حدّث قائمة المحادثات ثم حاول مرة أخرى.",
                "حسنًا");
            return;
        }

        var clientMessageId = Guid.NewGuid().ToString("D");

        var pending = _chat.AddPendingMessage(
            _conversationId,
            text,
            clientMessageId,
            _replyingTo?.RemoteId,
            _replyingTo?.Text);

        if (pending is null)
            return;

        // Optimistic UI: the message appears immediately as "sending". The same
        // ClientMessageId makes retries idempotent on the server.
        MessageEntry?.Text = string.Empty;
        ClearReply();
        RefreshMessagesView(scrollToEnd: true);

        _ = FlushOutboxAsync();
    }

    private static string GetMessageUiKey(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.RemoteId))
            return "r:" + message.RemoteId;

        return "l:" + message.ConversationId + ":" + message.Id;
    }

    private static int CompareMessageOrder(ChatMessage left, ChatMessage right)
    {
        var byTime = left.SentAt.CompareTo(right.SentAt);
        if (byTime != 0) return byTime;
        return left.Id.CompareTo(right.Id);
    }

    private void UpdateMessageDateSeparators(IReadOnlyList<ChatMessage> orderedMessages)
    {
        DateTime? previousDate = null;
        foreach (var message in orderedMessages)
        {
            var date = message.SentAt.Date;
            message.DateSeparatorText = previousDate != date
                ? date == DateTime.Today
                    ? "اليوم"
                    : date == DateTime.Today.AddDays(-1)
                        ? "أمس"
                        : date.ToString("dd/MM/yyyy")
                : string.Empty;

            previousDate = date;
        }
    }

    private void SetMessagesItemsSource(IReadOnlyList<ChatMessage> messages)
    {
        if (!MainThread.IsMainThread)
        {
            var snapshot = messages.ToList();
            MainThread.BeginInvokeOnMainThread(() => SetMessagesItemsSource(snapshot));
            return;
        }

        var orderedMessages = messages
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();

        UpdateMessageDateSeparators(orderedMessages);

        var currentKeys = _visibleMessages.Select(GetMessageUiKey).ToList();
        var desiredKeys = orderedMessages.Select(GetMessageUiKey).ToList();

        if (currentKeys.SequenceEqual(desiredKeys, StringComparer.Ordinal))
            return;

        // One Reset for the whole batch instead of one CollectionChanged event
        // per message. This avoids long UI stalls when a chat contains history.
        _visibleMessages.ReplaceRange(orderedMessages);
        UpdateVisiblePagingCursor();
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
        var recent = GetOrderedLocalMessages();
        var visible = recent.Count > MessagePageSize ? recent.Skip(recent.Count - MessagePageSize).ToList() : recent;
        SetMessagesItemsSource(visible);
        UpdateVisiblePagingCursor();
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

    private void ConfigureInitialChatPosition()
    {
        if (_visibleMessages.Count == 0)
        {
            _initialPositioningLatest = false;
            _allowOlderPaging = true;
            return;
        }

        _initialPositioningLatest = true;
        _allowOlderPaging = false;

#if ANDROID
        try
        {
            if (Messages?.Handler?.PlatformView is RecyclerView recycler &&
                recycler.GetLayoutManager() is LinearLayoutManager layoutManager)
            {
                // Telegram/WhatsApp-style timeline:
                // normal chronological order, but the native list is anchored
                // to its end. No delayed ScrollTo loop and no hidden CollectionView.
                layoutManager.ReverseLayout = false;
                layoutManager.StackFromEnd = true;

                recycler.Post(() =>
                {
                    try
                    {
                        var count = recycler.GetAdapter()?.ItemCount ?? 0;
                        if (count > 0)
                            layoutManager.ScrollToPositionWithOffset(count - 1, 0);
                    }
                    catch (global::Java.Lang.IllegalArgumentException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Initial native positioning ignored: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Initial native positioning failed: {ex}");
                    }
                    finally
                    {
                        _initialPositioningLatest = false;
                        _allowOlderPaging = true;
                        _lastVisibleItemIndex = Math.Max(-1, _visibleMessages.Count - 1);
                        ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
                    }
                });

                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] Native layout setup failed: {ex}");
        }
#endif

        // Portable MAUI fallback. One post only; no polling/delays.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Messages is not null && _visibleMessages.Count > 0)
                {
                    Messages.ScrollTo(
                        _visibleMessages.Count - 1,
                        group: null,
                        position: ScrollToPosition.End,
                        animate: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo ChatPage] Initial MAUI positioning failed: {ex}");
            }
            finally
            {
                _initialPositioningLatest = false;
                _allowOlderPaging = true;
                _lastVisibleItemIndex = Math.Max(-1, _visibleMessages.Count - 1);
                ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
            }
        });
    }

    private void MessagesLoaded(object? sender, EventArgs e)
    {
        if (_visibleMessages.Count > 0)
            ConfigureInitialChatPosition();
        else
        {
            _initialPositioningLatest = false;
            _allowOlderPaging = true;
        }
    }

    private void MessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        var messages = _visibleMessages;
        if (messages.Count == 0) return;

        _lastVisibleItemIndex = e.LastVisibleItemIndex;

        // The first CollectionView layout normally reports index 0 even though the
        // page has just been opened. Do not interpret that initial layout as the
        // user's request to load older messages. Older paging becomes legal only
        // after the initial scroll-to-latest pass has completed.
        if (!_initialPositioningLatest && _allowOlderPaging &&
            _initialRemoteSyncCompleted &&
            e.FirstVisibleItemIndex >= 0 &&
            e.FirstVisibleItemIndex <= 2 &&
            _hasMoreOlderMessages)
        {
            _ = LoadOlderMessagesAsync();
        }

        if (ScrollToBottomButton is null) return;
        var lastVisible = e.LastVisibleItemIndex;
        ScrollToBottomButton.IsVisible = lastVisible >= 0 && lastVisible < messages.Count - 2;
    }

    private int FindVisibleMessageIndex(ChatMessage message)
    {
        if (message is null || _visibleMessages.Count == 0)
            return -1;

        // Prefer the actual object reference when available.
        var index = _visibleMessages.IndexOf(message);
        if (index >= 0)
            return index;

        // The collection may have been refreshed with new instances. Compare by
        // the stable remote/local key instead of the object reference.
        var key = GetMessageUiKey(message);
        for (var i = 0; i < _visibleMessages.Count; i++)
        {
            if (string.Equals(GetMessageUiKey(_visibleMessages[i]), key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private async Task SafeScrollTo(
        ChatMessage? message,
        ScrollToPosition position = ScrollToPosition.End,
        bool animate = false)
    {
        if (message is null)
            return;

        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await SafeScrollTo(message, position, animate));
            return;
        }

        var index = FindVisibleMessageIndex(message);
        if (index < 0 || index >= _visibleMessages.Count)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Himo ChatPage] SafeScrollTo skipped: message is not in ItemsSource.");
            return;
        }

        await SafeScrollToIndex(index, position, animate);
    }

    private async Task SafeScrollToIndex(
        int index,
        ScrollToPosition position = ScrollToPosition.End,
        bool animate = false)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await SafeScrollToIndex(index, position, animate));
            return;
        }

        if (Messages is null || _visibleMessages.Count == 0)
            return;

        // Validate against the current MAUI ItemsSource before touching the native
        // Android RecyclerView. The collection can change between async awaits.
        if (index < 0 || index >= _visibleMessages.Count)
            return;

        try
        {
#if ANDROID
            if (Messages.Handler?.PlatformView is RecyclerView recycler &&
                recycler.IsAttachedToWindow)
            {
                var adapterCount = recycler.GetAdapter()?.ItemCount ?? 0;
                if (index < 0 || index >= adapterCount)
                    return;

                recycler.Post(() =>
                {
                    try
                    {
                        var currentCount = recycler.GetAdapter()?.ItemCount ?? 0;
                        if (index >= 0 && index < currentCount)
                            recycler.ScrollToPosition(index);
                    }
                    catch (global::Java.Lang.IllegalArgumentException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Android SafeScrollTo ignored invalid position: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Android SafeScrollTo failed: {ex}");
                    }
                });

                return;
            }
#endif

            // Portable MAUI fallback. Use the validated numeric index rather than
            // passing an arbitrary object reference to CollectionView.ScrollTo.
            var currentCount = _visibleMessages.Count;
            if (index < 0 || index >= currentCount)
                return;

            Messages.ScrollTo(index, group: null, position: position, animate: animate);
        }
        catch (global::Java.Lang.IllegalArgumentException ex)
        {
            // MAUI can surface the Android RecyclerView failure as a Java exception.
            // Treat it as a stale-scroll race instead of letting it crash the app.
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo ignored Android invalid target: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo ignored invalid target: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo failed safely: {ex}");
        }
    }

    private void ScrollToLatestMessage(bool animate)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => ScrollToLatestMessage(animate));
            return;
        }

        if (Messages is null || _visibleMessages.Count == 0)
            return;

        _initialPositioningLatest = false;
        _allowOlderPaging = true;

        _ = SafeScrollToIndex(_visibleMessages.Count - 1, ScrollToPosition.End, animate);

        ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
    }


    private void ScrollToBottomClicked(object? sender, EventArgs e)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (messages.Count > 0)
            ScrollToLatestMessage(animate: true);

        if (ScrollToBottomButton is not null)
            ScrollToBottomButton.IsVisible = false;
    }

    private void RefreshMessagesView(bool scrollToEnd)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => RefreshMessagesView(scrollToEnd));
            return;
        }

        AppendNewMessagesToVisible();
        EmptyState?.SetValue(IsVisibleProperty, _visibleMessages.Count == 0);

        if (scrollToEnd && _visibleMessages.Count > 0)
            ScrollToLatestMessage(animate: true);
    }


    private sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void InsertRange(int index, IEnumerable<T> items)
        {
            CheckReentrancy();
            var values = items.ToList();
            if (values.Count == 0) return;

            for (var i = 0; i < values.Count; i++)
                Items.Insert(index + i, values[i]);

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, values, index));
        }
    }

}