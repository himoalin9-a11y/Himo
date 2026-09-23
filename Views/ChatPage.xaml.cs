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
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _remoteLoadGate = new(1, 1);
    private int _conversationId;
    private string? _remoteConversationId;
    private int _sending;
    private bool _isRecordingAudio;
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
                _conversationId = id;
                _remoteConversationId = null;
                Load();
                return;
            }

            if (Guid.TryParse(value, out var remoteId))
            {
                _remoteConversationId = remoteId.ToString("D");
                _ = ResolveRemoteConversationAsync();
            }
        }
    }

    public ChatPage(ChatService chat, HimoApiClient api, AccountService account, INotificationService notifications, HimoRealtimeService realtime)
    {
        InitializeComponent();
        _chat = chat; _api = api; _account = account; _notifications = notifications; _realtime = realtime;
        _realtime.MessageReceived += OnRealtimeMessageReceived;
        BindingContext = _chat.GetMessages(0);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Load();
        _ = _notifications.InitializeAsync();
        _ = ResolveRemoteConversationAsync();
        _ = ClearConversationNotificationAsync();
        _ = LoadRemoteAsync();
        _ = _realtime.StartAsync();
        StartPollingFallback();
        MessageEntry?.Focus();
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
        if (_conversationId <= 0 || !IsLoaded)
            return;

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation == null)
            return;

        NameLabel?.Text = conversation.Name;
        InitialLabel?.Text = conversation.Initial;
        var messages = _chat.GetMessages(_conversationId);
        BindingContext = messages;
        Messages?.ItemsSource = messages;
        EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);
        _chat.MarkAsRead(_conversationId);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (messages.Count > 0)
                Messages?.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false);
        });
    }

    protected override void OnDisappearing()
    {
#if ANDROID
        if (_isRecordingAudio) CleanupAudioRecorder();
        StopAudioPlayback();
#endif
        StopPolling();
        _ = _realtime.StopAsync();
        base.OnDisappearing();
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("..");
    }

    private async Task ClearConversationNotificationAsync()
    {
        try
        {
            if (_conversationId <= 0) return;
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
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                await LoadRemoteAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void OnRealtimeMessageReceived(object? sender, MessageDto message)
    {
        var conversation = _chat.Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, message.ConversationId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        if (conversation is null) return;

        var myEmail = _account.CurrentAccount?.Email ?? string.Empty;
        var isMine = string.Equals(message.SenderPhoneNumber?.Trim(), myEmail.Trim(), StringComparison.OrdinalIgnoreCase);
        var added = _chat.AddRemoteMessage(conversation.Id, message.Text, message.SentAt.LocalDateTime, isMine, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize);
        if (!added) return;

        if (message.AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            var localMessage = _chat.GetMessages(conversation.Id).LastOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase));
            _ = PrepareImagePreviewAsync(localMessage);
        }

        if (conversation.Id == _conversationId)
        {
            _chat.MarkAsRead(conversation.Id);
            Messages?.ItemsSource = _chat.GetMessages(conversation.Id);
            if (_chat.GetMessages(conversation.Id).Count > 0)
                Messages?.ScrollTo(_chat.GetMessages(conversation.Id)[^1], position: ScrollToPosition.End, animate: true);
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
                var myEmail = _account.CurrentAccount?.Email ?? string.Empty;
                foreach (var message in remoteMessages)
                {
                    var isMine = string.Equals(message.SenderPhoneNumber?.Trim(), myEmail.Trim(), StringComparison.OrdinalIgnoreCase);
                    // The conversation is currently visible, so do not raise a system
                    // notification for a message the user can already see. The home
                    // screen handles notifications for unread messages.
                    var added = _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, isMine, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize);
                    if (added && message.AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var localMessage = _chat.GetMessages(_conversationId).LastOrDefault(x => string.Equals(x.RemoteId, message.Id.ToString(), StringComparison.OrdinalIgnoreCase));
                        _ = PrepareImagePreviewAsync(localMessage);
                    }
                }

                var messages = _chat.GetMessages(_conversationId);
                // The conversation is currently visible, so incoming messages are immediately read.
                _chat.MarkAsRead(_conversationId);
                await _notifications.ClearConversationAsync(_conversationId.ToString());
                try
                {
                    await _api.MarkConversationReadAsync(remoteIdGuid, cancellationToken);
                }
                catch { /* Local read state remains available if the server is temporarily offline. */ }

                Messages?.ItemsSource = messages;
                EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);
                // Keep the current scroll position during polling when nothing new arrived.
                // This prevents the conversation from jumping to the bottom every two seconds.
                if (messages.Count > messageCountBeforeSync && messages.Count > 0)
                    Messages?.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false);
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
            if (_conversationId <= 0 || !_api.HasToken) return;
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
            RecordButton.Text = "⏹️";
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
            RecordButton.Text = "🎙️";
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
            _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, true, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize);
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
            if (RecordButton is not null) RecordButton.Text = "🎙️";
            if (MessageEntry is not null) MessageEntry.IsEnabled = true;
            if (SendButton is not null) SendButton.IsEnabled = true;
        });
    }
#endif

    private async void AttachmentButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_conversationId <= 0 || !_api.HasToken) return;
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

            var info = new FileInfo(file.FullPath);
            if (info.Exists && info.Length > 25L * 1024 * 1024)
            {
                await DisplayAlertAsync("المرفقات", "الحد الأقصى لحجم الملف 25 ميجابايت.", "حسنًا");
                return;
            }

            SendButton.IsEnabled = false;
            var message = await _api.UploadAttachmentAsync(remoteIdGuid, file);
            _chat.AddRemoteMessage(_conversationId, message.Text, message.SentAt.LocalDateTime, true, message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType, message.AttachmentSize);
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

    private async void SendClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _sending, 1) != 0)
            return;

        try
        {
            if (SendButton is not null)
            {
                SendButton.IsEnabled = false;
                SendButton.Text = "جارٍ الإرسال...";
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
                SendButton.Text = "إرسال";
            }

            Interlocked.Exchange(ref _sending, 0);
        }
    }

    private async Task SendMessageAsync()
    {
        var text = MessageEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _conversationId <= 0)
            return;
        if (text.Length > 4000)
        {
            await DisplayAlertAsync("الإرسال", "الرسالة طويلة جدًا. الحد الأقصى 4000 حرف.", "حسنًا");
            return;
        }

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (_api.HasToken)
        {
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteIdGuid))
            {
                await DisplayAlertAsync("الإرسال", "هذه المحادثة غير مرتبطة بالخادم. حدّث قائمة المحادثات ثم حاول مرة أخرى.", "حسنًا");
                return;
            }

            var clientMessageId = Guid.NewGuid().ToString();
            try
            {
                var remote = await _api.SendMessageAsync(remoteIdGuid, text, clientMessageId);
                _chat.AddRemoteMessage(_conversationId, remote.Text, remote.SentAt.LocalDateTime, true, remote.Id.ToString());
                MessageEntry?.Text = string.Empty;
                RefreshMessagesView(scrollToEnd: true);
                return;
            }
            catch (HttpRequestException)
            {
                // A 401 clears the token and raises SessionExpired. Do not retry
                // an authenticated request after that transition.
                if (!_api.HasToken)
                    return;

                try
                {
                    // Retry once with the same client ID. The server treats the
                    // client ID as an idempotency key, so even if the first
                    // request reached the server before the connection failed,
                    // this retry returns the existing message instead of creating
                    // a duplicate.
                    var remote = await _api.SendMessageAsync(remoteIdGuid, text, clientMessageId);
                    _chat.AddRemoteMessage(_conversationId, remote.Text, remote.SentAt.LocalDateTime, true, remote.Id.ToString());
                    MessageEntry?.Text = string.Empty;
                    RefreshMessagesView(scrollToEnd: true);
                    return;
                }
                catch (HttpRequestException)
                {
                    await DisplayAlertAsync("الإرسال", "تعذر الاتصال بالخادم. تحقق من الاتصال ثم حاول مرة أخرى.", "حسنًا");
                    return;
                }
                catch (TaskCanceledException)
                {
                    await DisplayAlertAsync("الإرسال", "انتهت مهلة الاتصال بالخادم. حاول مرة أخرى.", "حسنًا");
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                try
                {
                    // Retry once with the same client ID. The server treats the
                    // client ID as an idempotency key, so a request that reached
                    // the server before the timeout cannot create a duplicate.
                    var remote = await _api.SendMessageAsync(remoteIdGuid, text, clientMessageId);
                    _chat.AddRemoteMessage(_conversationId, remote.Text, remote.SentAt.LocalDateTime, true, remote.Id.ToString());
                    MessageEntry?.Text = string.Empty;
                    RefreshMessagesView(scrollToEnd: true);
                    return;
                }
                catch (HttpRequestException)
                {
                    await DisplayAlertAsync("الإرسال", "تعذر الاتصال بالخادم. تحقق من الاتصال ثم حاول مرة أخرى.", "حسنًا");
                    return;
                }
                catch (TaskCanceledException)
                {
                    await DisplayAlertAsync("الإرسال", "انتهت مهلة الاتصال بالخادم. حاول مرة أخرى.", "حسنًا");
                    return;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("الإرسال", ex.Message, "حسنًا");
                return;
            }
        }

        var message = _chat.Send(_conversationId, text);
        if (message == null) return;
        MessageEntry?.Text = string.Empty;
        RefreshMessagesView(scrollToEnd: true);
    }

    private void RefreshMessagesView(bool scrollToEnd)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (Messages is not null)
            Messages.ItemsSource = messages;

        EmptyState?.SetValue(IsVisibleProperty, messages.Count == 0);

        if (scrollToEnd && Messages is not null && messages.Count > 0)
            Messages.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: true);
    }

}