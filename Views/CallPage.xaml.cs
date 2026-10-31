using Himo.Services;
using Himo.Models;
using Microsoft.Maui.Controls.Shapes;
#if ANDROID
using Himo.Platforms.Android.Services;
#endif

namespace Himo.Views;

[QueryProperty(nameof(ConversationId), "id")]
[QueryProperty(nameof(Mode), "mode")]
[QueryProperty(nameof(Incoming), "incoming")]
[QueryProperty(nameof(CallId), "callId")]
[QueryProperty(nameof(PeerName), "peerName")]
public partial class CallPage : ContentPage
{
    private readonly ICallService _calls;
    private readonly HimoRealtimeService _realtime;
    private readonly WebRtcNegotiationCoordinator _negotiation;
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly INotificationService _notifications;
    private CancellationTokenSource? _avatarLoadCts;
    private Guid _conversationId;
    private CallMode _mode = CallMode.Audio;
    private DateTimeOffset? _connectedAt;
    private bool _cameraEnabled = true;
    private IDispatcherTimer? _timer;
    private bool _remoteAudioToggleInProgress;
    private long _callUiStateVersion;
    private bool _incoming;
    private bool _incomingAnswered;
    private bool _callSetupInProgress;
    private Guid _callId;
    private bool _navigationStarted;
    private CancellationTokenSource? _incomingRingTimeoutCts;
    private Guid _incomingRingTimeoutCallId;
    private int _remoteEndHandling;
#if ANDROID
    private global::Android.Media.ToneGenerator? _outgoingRingback;
    private global::Android.Media.Ringtone? _incomingRingtone;
    private global::Android.Media.AudioManager? _ringbackAudioManager;
    private global::Android.Media.Mode? _previousAudioMode;
    private bool? _previousSpeakerphoneOn;
#endif
    private bool _remoteAccepted;
    private bool _callChromeVisible = true;
    private bool _isPictureInPicture;
    private bool _localPreviewDocked;
    private double _localPreviewPanStartX;
    private double _localPreviewPanStartY;
    private string _peerName = "جهة اتصال";

    public string ConversationId
    {
        get => _conversationId.ToString("D");
        set => Guid.TryParse(value, out _conversationId);
    }

    public string Mode
    {
        get => _mode == CallMode.Video ? "video" : "audio";
        set
        {
            _mode = string.Equals(value, "video", StringComparison.OrdinalIgnoreCase) ? CallMode.Video : CallMode.Audio;
            UpdateCallModeIcon();
        }
    }

    private void UpdateCallModeIcon()
    {
        if (CallModeIcon is not null)
            CallModeIcon.Source = ImageSource.FromFile(_mode == CallMode.Video
                ? "himo_icon_chat_video_call.png"
                : "himo_icon_chat_voice_call.png");
    }

    public string Incoming
    {
        get => _incoming ? "true" : "false";
        set => _incoming = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public string CallId
    {
        get => _callId == Guid.Empty ? string.Empty : _callId.ToString("D");
        set => Guid.TryParse(value, out _callId);
    }

    public string PeerName
    {
        get => _peerName;
        set
        {
            _peerName = string.IsNullOrWhiteSpace(value) ? "جهة اتصال" : value.Trim();
            if (NameLabel is not null) NameLabel.Text = _peerName;
            if (VideoCallNameLabel is not null) VideoCallNameLabel.Text = _peerName;
            if (InitialLabel is not null) InitialLabel.Text = FirstTextElement(_peerName);
            if (VideoPeerInitialLabel is not null) VideoPeerInitialLabel.Text = FirstTextElement(_peerName);
        }
    }

    public bool IsForCall(string conversationId, string callId)
    {
        if (!Guid.TryParse(conversationId, out var parsedConversationId) || parsedConversationId != _conversationId)
            return false;
        if (!Guid.TryParse(callId, out var parsedCallId) || parsedCallId == Guid.Empty)
            return true;
        if (_callId == parsedCallId)
            return true;
        // A page with no bound CallId is a match only if CallService already owns
        // exactly the target call. Do not mistake an unrelated in-progress/outgoing
        // page in the same conversation for a duplicate incoming call.
        var active = _calls.Current;
        return _callId == Guid.Empty && active is not null &&
               active.ConversationId == parsedConversationId && active.CallId == parsedCallId;
    }

    private static string FirstTextElement(string value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "H" : trimmed.Substring(0, 1);
    }

    // Resolve colors from the active Himo palette so the call controls stay coherent
    // after a theme change. Fallbacks preserve functionality if a resource is absent.
    private static Color ThemeColor(string resourceKey, string fallbackHex)
    {
        if (Microsoft.Maui.Controls.Application.Current is { } app &&
            app.Resources.TryGetValue(resourceKey, out var value) && value is Color color)
            return color;

        return Color.FromArgb(fallbackHex);
    }

    // Runtime call-state changes replace ImageButton.Source, so use the active theme
    // variant directly instead of falling back to a generic-colored PNG.
    // The call controls use a dedicated, consistent white icon family on the dark IMO bar.
    // This is a presentation-only resource mapping; call state and event handlers are unchanged.
    private static string ThemedCallIcon(string fileName)
    {
        return fileName switch
        {
            "himo_call_mic.png" => "imo_call_mic.png",
            "himo_call_mic_off.png" => "imo_call_mic_off.png",
            "himo_call_speaker.png" => "imo_call_speaker.png",
            "himo_call_speaker_off.png" => "imo_call_speaker_off.png",
            "himo_call_camera.png" => "imo_call_camera.png",
            "himo_call_camera_off.png" => "imo_call_camera_off.png",
            "himo_cam_switch.png" => "imo_call_switch_camera.png",
            _ => "imo_call_more.png"
        };
    }

    public CallPage(ICallService calls, HimoRealtimeService realtime, WebRtcNegotiationCoordinator negotiation, ChatService chat, HimoApiClient api, INotificationService notifications)
    {
        InitializeComponent();
        AudioAvatarBackgroundImage.HandlerChanged += AudioAvatarBackgroundImageHandlerChanged;
        Himo.Services.AppLocalization.AttachToPage(this);
        _calls = calls;
        _realtime = realtime;
        _negotiation = negotiation;
        _chat = chat;
        _api = api;
        _notifications = notifications;
        _calls.StateChanged += CallsStateChanged;
        _calls.CallEnded += CallsEnded;
    }

    private Conversation? FindLocalCallConversation() =>
        _chat.Conversations.FirstOrDefault(c =>
            Guid.TryParse(c.RemoteId, out var remoteId) && remoteId == _conversationId);

    private void PrepareCachedPeerAvatar()
    {
        var conversation = FindLocalCallConversation();
        if (conversation is null)
        {
            ApplyPeerAvatar(null);
            return;
        }

        if (!string.IsNullOrWhiteSpace(conversation.Name))
            PeerName = conversation.Name;

        string? cachedPath = null;
        if (conversation.OtherParticipantUserId is Guid userId && conversation.HasRemoteProfilePhoto)
        {
            cachedPath = conversation.ProfilePhotoPath;
            if (string.IsNullOrWhiteSpace(cachedPath) || !File.Exists(cachedPath))
                cachedPath = ProfilePhotoCache.Find(userId, conversation.ProfilePhotoVersion);
            if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
                conversation.ProfilePhotoPath = cachedPath;
        }

        ApplyPeerAvatar(cachedPath);
    }

    private async Task LoadPeerAvatarAsync(CancellationToken cancellationToken)
    {
        var localConversation = FindLocalCallConversation();
        var fallbackUserId = localConversation?.OtherParticipantUserId;
        var fallbackHasPhoto = localConversation?.HasRemoteProfilePhoto == true;
        var fallbackVersion = localConversation?.ProfilePhotoVersion ?? 0;

        try
        {
            // During a cold-start incoming call, token restoration may still be in
            // progress. Await it without blocking the ringtone or call setup.
            await _api.TokenInitialization;
            cancellationToken.ThrowIfCancellationRequested();

            // The API's current conversation metadata supplies the peer's user id
            // and photo version without putting personal photo URLs into navigation.
            if (_api.HasToken)
            {
                var conversations = await _api.GetConversationsAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var remote = conversations.FirstOrDefault(c => c.Id == _conversationId);
                if (remote is not null)
                {
                    if (!string.IsNullOrWhiteSpace(remote.Name))
                        _peerName = remote.Name.Trim();

                    fallbackUserId = remote.OtherParticipantUserId;
                    fallbackHasPhoto = remote.HasProfilePhoto;
                    fallbackVersion = remote.ProfilePhotoVersion;

                    if (localConversation is not null)
                    {
                        localConversation.OtherParticipantUserId = remote.OtherParticipantUserId;
                        localConversation.HasRemoteProfilePhoto = remote.HasProfilePhoto;
                        localConversation.ProfilePhotoVersion = remote.ProfilePhotoVersion;
                    }
                }
            }

            string? photoPath = null;
            if (fallbackUserId is Guid peerUserId)
            {
                photoPath = await ProfilePhotoCache.GetPathAsync(
                    _api,
                    peerUserId,
                    fallbackHasPhoto,
                    fallbackVersion,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (!string.IsNullOrWhiteSpace(_peerName))
                {
                    NameLabel.Text = _peerName;
                    VideoCallNameLabel.Text = _peerName;
                    InitialLabel.Text = FirstTextElement(_peerName);
                    VideoPeerInitialLabel.Text = FirstTextElement(_peerName);
                }
                if (localConversation is not null)
                    localConversation.ProfilePhotoPath = photoPath;
                ApplyPeerAvatar(photoPath);
            });
        }
        catch (OperationCanceledException)
        {
            // The call screen has been closed; a canceled photo request is normal.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Peer avatar load failed: {ex.Message}");
            // Try the locally cached conversation information if the network lookup
            // failed; photo availability must never delay or break call setup.
            try
            {
                if (fallbackUserId is Guid peerUserId && fallbackHasPhoto)
                {
                    var path = await ProfilePhotoCache.GetPathAsync(
                        _api, peerUserId, true, fallbackVersion, cancellationToken);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            ApplyPeerAvatar(path);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception fallbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Cached peer avatar load failed: {fallbackEx.Message}");
            }
        }
    }

    private void ApplyPeerAvatar(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var source = hasPhoto ? ImageSource.FromFile(path!) : null;

        AudioAvatarBackgroundImage.Source = source;
        AudioAvatarBackgroundImage.IsVisible = hasPhoto;

        ProfilePhotoImage.Source = source;
        ProfilePhotoImage.IsVisible = hasPhoto;
        InitialLabel.IsVisible = !hasPhoto;

        VideoPeerPhotoImage.Source = source;
        VideoPeerPhotoImage.IsVisible = hasPhoto;
        VideoPeerInitialLabel.IsVisible = !hasPhoto;

        IncomingPeerPhotoImage.Source = source;
        IncomingPeerPhotoImage.IsVisible = hasPhoto;
        IncomingPeerInitialLabel.Text = FirstTextElement(_peerName);
        IncomingPeerInitialLabel.IsVisible = !hasPhoto;
    }

    private async Task LoadOwnProfileAvatarAsync()
    {
        var profileService = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
            .GetService<ProfileService>();
        if (profileService is null) return;

        var profile = profileService.Profile;
        ApplyOwnProfileAvatar(profile.PhotoPath, profile.Name);
        if (!string.IsNullOrWhiteSpace(profile.PhotoPath) && File.Exists(profile.PhotoPath))
            return;

        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken) return;
            var photo = await _api.GetMyProfilePhotoAsync();
            if (photo is null) return;
            var path = profileService.SavePhoto(photo.Bytes, photo.ContentType);
            await MainThread.InvokeOnMainThreadAsync(() => ApplyOwnProfileAvatar(path, profileService.Profile.Name));
        }
        catch (Exception ex)
        {
            // The local profile initials remain visible if the photo cannot be loaded.
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Local profile photo skipped: {ex.Message}");
        }
    }

    private void ApplyOwnProfileAvatar(string? path, string? name)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        LocalProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        LocalProfilePhotoImage.IsVisible = hasPhoto;
        LocalProfileInitialLabel.Text = FirstTextElement(string.IsNullOrWhiteSpace(name) ? "Himo" : name);
        LocalProfileInitialLabel.IsVisible = !hasPhoto;
    }

    private void SetLocalVideoContainerPlacement(bool connected)
    {
        if (_mode != CallMode.Video) return;

        if (connected)
        {
            if (!_localPreviewDocked)
            {
                LocalVideoContainer.TranslationX = 0;
                LocalVideoContainer.TranslationY = 0;
                _localPreviewDocked = true;
            }

            LocalVideoContainer.WidthRequest = 100;
            LocalVideoContainer.HeightRequest = 140;
            LocalVideoContainer.HorizontalOptions = LayoutOptions.End;
            LocalVideoContainer.VerticalOptions = LayoutOptions.Start;
            LocalVideoContainer.Margin = new Thickness(0, 84, 14, 0);
            LocalVideoContainer.StrokeThickness = 2;
            LocalVideoContainer.Stroke = new SolidColorBrush(Color.FromArgb("#FFFFFFFF"));
            LocalVideoContainer.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) };
        }
        else
        {
            _localPreviewDocked = false;
            LocalVideoContainer.TranslationX = 0;
            LocalVideoContainer.TranslationY = 0;
            LocalVideoContainer.WidthRequest = -1;
            LocalVideoContainer.HeightRequest = -1;
            LocalVideoContainer.HorizontalOptions = LayoutOptions.Fill;
            LocalVideoContainer.VerticalOptions = LayoutOptions.Fill;
            LocalVideoContainer.Margin = new Thickness(0);
            LocalVideoContainer.StrokeThickness = 0;
            LocalVideoContainer.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) };
        }
    }

    // Visual-only drag handling for the local video thumbnail. It does not touch the media pipeline.
    private void LocalPreviewPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not VisualElement preview || preview.Parent is not VisualElement canvas || !_localPreviewDocked)
            return;

        if (e.StatusType == GestureStatus.Started)
        {
            _localPreviewPanStartX = preview.TranslationX;
            _localPreviewPanStartY = preview.TranslationY;
            return;
        }

        if (e.StatusType != GestureStatus.Running || canvas.Width <= 0 || canvas.Height <= 0)
            return;

        var desiredX = _localPreviewPanStartX + e.TotalX;
        var desiredY = _localPreviewPanStartY + e.TotalY;
        var minX = 10d - preview.X;
        var maxX = canvas.Width - preview.Width - 10d - preview.X;
        var minY = 12d - preview.Y;
        var maxY = canvas.Height - preview.Height - 12d - preview.Y;

        preview.TranslationX = Math.Clamp(desiredX, minX, Math.Max(minX, maxX));
        preview.TranslationY = Math.Clamp(desiredY, minY, Math.Max(minY, maxY));
    }

    private void AudioAvatarBackgroundImageHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            if (AudioAvatarBackgroundImage.Handler?.PlatformView is global::Android.Widget.ImageView nativeImage &&
                global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
            {
                nativeImage.SetRenderEffect(
                    global::Android.Graphics.RenderEffect.CreateBlurEffect(
                        28f, 28f, global::Android.Graphics.Shader.TileMode.Clamp));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Avatar backdrop blur unavailable: {ex.Message}");
        }
#endif
    }

    private async void CallButtonPressed(object? sender, EventArgs e)
    {
        if (sender is not VisualElement button) return;
        try { await button.ScaleToAsync(0.9, 70, Easing.CubicOut); }
        catch { /* A disposed visual during navigation needs no further animation. */ }
    }

    private async void CallButtonReleased(object? sender, EventArgs e)
    {
        if (sender is not VisualElement button) return;
        try { await button.ScaleToAsync(1, 90, Easing.CubicOut); }
        catch { /* A disposed visual during navigation needs no further animation. */ }
    }

    private void UpdateCallControlLayout()
    {
        var isVideo = _mode == CallMode.Video;
        ControlButtonGrid.ColumnDefinitions.Clear();
        for (var i = 0; i < 5; i++)
            ControlButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        // Fixed IMO button order: Mute, Switch Camera, End, Speaker, More.
        Grid.SetColumn(MuteControl, 0);
        Grid.SetColumn(SwitchCameraControl, 1);
        Grid.SetColumn(EndControl, 2);
        Grid.SetColumn(SpeakerControl, 3);
        Grid.SetColumn(MoreControl, 4);

        MuteControl.IsVisible = true;
        SwitchCameraControl.IsVisible = true;
        EndControl.IsVisible = true;
        SpeakerControl.IsVisible = true;
        MoreControl.IsVisible = true;

        // The camera on/off button remains on the floating local preview, not in the five-button bar.
        CameraControl.IsVisible = false;
        CameraButton.IsEnabled = isVideo && _calls.Current is not null;
        SwitchCameraButton.IsEnabled = isVideo && _calls.Current is not null && _cameraEnabled;
        SwitchCameraButton.Opacity = isVideo ? 1 : 0.42;
    }

    private void UpdateCallPresentation(bool connected)
    {
        var isVideo = _mode == CallMode.Video;
        var incomingRinging = _incoming && !_incomingAnswered && !connected;
        var showVideoScene = isVideo && (!_incoming || _incomingAnswered || connected);

        AudioBackground.IsVisible = !isVideo;
        VideoSurface.IsVisible = showVideoScene;
        CallTapCatcher.IsVisible = !isVideo;
        IncomingVideoPanel.IsVisible = isVideo && incomingRinging;
        ProfilePanel.IsVisible = !isVideo;
        VideoWaitingOverlay.IsVisible = false;
        RemoteVideoHost.IsVisible = isVideo && connected;
        IncomingActions.IsVisible = incomingRinging;
        OutgoingActions.IsVisible = !isVideo && !connected && !incomingRinging;

        if (isVideo)
        {
            SetLocalVideoContainerPlacement(connected);
            LocalCameraOffOverlay.IsVisible = !_cameraEnabled && showVideoScene;
            CameraActionLabel.Text = _cameraEnabled ? "إغلاق الكاميرا" : "تشغيل الكاميرا";
            SwitchCameraButton.IsEnabled = _calls.Current is not null && _cameraEnabled;
        }

        NameLabel.IsVisible = !isVideo;
        StatusLabel.IsVisible = !isVideo;
        VideoCallNameLabel.IsVisible = isVideo;
        VideoStatusLabel.IsVisible = isVideo;
        ModeLabel.Text = isVideo ? "مكالمة فيديو" : "مكالمة صوتية";

        UpdateCallControlLayout();
        var hasCall = _calls.Current is not null;
        var canShowActiveControls = isVideo
            ? hasCall && !incomingRinging
            : connected;
        CallControls.IsVisible = canShowActiveControls && _callChromeVisible && !_isPictureInPicture;
        CallHeader.IsVisible = _callChromeVisible && !_isPictureInPicture;
        MinimizeCallButton.IsVisible = hasCall && !incomingRinging && _callChromeVisible && !_isPictureInPicture;
    }

    private void CallSurfaceTapped(object? sender, TappedEventArgs e)
    {
        if (_isPictureInPicture || (_incoming && !_incomingAnswered)) return;
        // Audio controls should not disappear while the recipient is still waiting for an answer.
        if (_mode == CallMode.Audio && _calls.Current?.IsConnected != true) return;
        _callChromeVisible = !_callChromeVisible;
        UpdateCallPresentation(_calls.Current?.IsConnected == true);
    }

    private async void MinimizeCallClicked(object? sender, EventArgs e)
    {
        if (_calls.Current is null) return;
#if ANDROID
        if (MainActivity.TryEnterCallPictureInPicture())
        {
            SetPictureInPictureMode(true);
            return;
        }
#endif
        await DisplayAlertAsync(
            "تصغير المكالمة",
            "تعذر فتح النافذة العائمة على هذا الجهاز. ستبقى المكالمة مستمرة في هذه الشاشة.",
            "حسنًا");
    }

    public void SetPictureInPictureMode(bool isInPictureInPicture)
    {
        _isPictureInPicture = isInPictureInPicture;
        _callChromeVisible = !isInPictureInPicture;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateCallPresentation(_calls.Current?.IsConnected == true);
            if (!isInPictureInPicture && _mode == CallMode.Video)
                _ = ResumeVideoRenderingForForegroundAsync();
        });
    }

    private void CallsStateChanged(object? sender, CallState state)
    {
        var stateVersion = Interlocked.Increment(ref _callUiStateVersion);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (stateVersion != Volatile.Read(ref _callUiStateVersion))
                return;
            if (state.IsConnected && _connectedAt is null)
            {
#if ANDROID
                StopOutgoingRingback();
                StopIncomingRingtone();
#endif
                _incomingAnswered = _incoming || _incomingAnswered;
                _connectedAt = DateTimeOffset.UtcNow;
                StartTimer();
            }
#if ANDROID
            else if (!_incoming && !_remoteAccepted)
            {
                // Start ringback as soon as the local audio route is configured,
                // before WebRTC factory setup can delay the outgoing invite.
                StartOutgoingRingback();
            }
#endif
            StatusLabel.Text = state.IsConnected
                ? FormatDuration()
                : (_incoming && !_incomingAnswered
                    ? "مكالمة واردة..."
                    : (_incomingAnswered || _remoteAccepted
                        ? "تم الرد، جارٍ الاتصال..."
                        : "بانتظار قبول المكالمة..."));
            VideoStatusLabel.Text = state.IsConnected ? FormatDuration() : StatusLabel.Text;
            IncomingActions.IsVisible = _incoming && !_incomingAnswered && !state.IsConnected;
            OutgoingActions.IsVisible = !state.IsConnected && !IncomingActions.IsVisible;
            CallControls.IsVisible = state.IsConnected;
            VideoSurface.IsVisible = _mode == CallMode.Video && state.IsConnected;
            ProfilePanel.IsVisible = !VideoSurface.IsVisible;
            CameraButton.IsEnabled = _mode == CallMode.Video && state.IsConnected;
            CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.4;
            MuteActionLabel.Text = state.IsMuted ? "إلغاء الكتم" : "كتم الصوت";
            MuteButton.BackgroundColor = state.IsMuted ? ThemeColor("HimoDangerSoft", "#41162F") : ThemeColor("HimoSurface", "#160A27");
            MuteActionLabel.TextColor = state.IsMuted ? ThemeColor("HimoDanger", "#D9485F") : ThemeColor("HimoTextSecondary", "#B7A2D2");
            CameraActionLabel.Text = _mode == CallMode.Video
                ? (_cameraEnabled ? "إيقاف الكاميرا" : "تشغيل الكاميرا")
                : "فيديو غير متاح";
            CameraButton.BackgroundColor = _mode != CallMode.Video
                ? ThemeColor("HimoCard", "#170B28")
                : (_cameraEnabled ? ThemeColor("HimoSurface", "#160A27") : ThemeColor("HimoDangerSoft", "#41162F"));
            CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.42;
            CameraActionLabel.TextColor = _mode != CallMode.Video
                ? ThemeColor("HimoMuted", "#BBA6D6")
                : (_cameraEnabled ? ThemeColor("HimoTextSecondary", "#B7A2D2") : ThemeColor("HimoDanger", "#D9485F"));
            SpeakerActionLabel.Text = state.IsSpeakerOn ? "إيقاف المكبر" : "مكبر الصوت";
            SpeakerButton.BackgroundColor = state.IsSpeakerOn ? ThemeColor("HimoSoft", "#25113D") : ThemeColor("HimoSurface", "#160A27");
            SpeakerActionLabel.TextColor = state.IsSpeakerOn ? ThemeColor("HimoAccent", "#A12AFF") : ThemeColor("HimoTextSecondary", "#B7A2D2");
            RemoteAudioActionLabel.Text = state.IsRemoteAudioEnabled ? "كتم الوارد" : "تشغيل الوارد";
            RemoteAudioButton.BackgroundColor = state.IsRemoteAudioEnabled ? ThemeColor("HimoSoft", "#25113D") : ThemeColor("HimoSurface", "#160A27");
            RemoteAudioActionLabel.TextColor = state.IsRemoteAudioEnabled ? ThemeColor("HimoAccent", "#A12AFF") : ThemeColor("HimoTextSecondary", "#B7A2D2");
#if ANDROID
            if (_mode == CallMode.Video && state.IsConnected) _ = AttachVideoWithRetryAsync();
#endif
            RemoteAudioButton.Source = state.IsRemoteAudioEnabled ? ThemedCallIcon("himo_call_speaker.png") : ThemedCallIcon("himo_call_speaker_off.png");
            RemoteAudioButton.IsEnabled = state.IsConnected && !_remoteAudioToggleInProgress;
            MuteButton.Source = state.IsMuted ? ThemedCallIcon("himo_call_mic_off.png") : ThemedCallIcon("himo_call_mic.png");
            MuteActionLabel.Text = state.IsMuted ? "المايك مغلق" : "المايك مفتوح";
            SpeakerButton.Source = state.IsSpeakerOn ? ThemedCallIcon("himo_call_speaker.png") : ThemedCallIcon("himo_call_speaker_off.png");
            UpdateCallPresentation(state.IsConnected);
        });
    }

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _callUiStateVersion);
        _calls.StateChanged -= CallsStateChanged;
        _calls.CallEnded -= CallsEnded;
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
#if ANDROID
        NotificationService.IncomingCallTerminatedByPush -= OnIncomingCallTerminatedByPush;
        NotificationService.IncomingCallAnsweredByPush -= OnIncomingCallAnsweredByPush;
#endif
        _avatarLoadCts?.Cancel();
        _avatarLoadCts?.Dispose();
        _avatarLoadCts = null;
        StopTimer();
#if ANDROID
        EnsureBackgroundRingtoneIfIncoming();
        if (_mode == CallMode.Video)
            _ = PauseVideoForBackgroundAsync();
        StopOutgoingRingback();
        StopIncomingRingtone();
#endif
        base.OnDisappearing();
    }

    public Task PauseVideoForBackgroundAsync()
    {
#if ANDROID
        if (_mode != CallMode.Video)
            return Task.CompletedTask;
        try
        {
            var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
                .GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
            if (engine is null)
                return Task.CompletedTask;

            var pauseTask = engine.PauseVideoForBackgroundAsync();
            _cameraEnabled = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CameraButton.Source = ThemedCallIcon("himo_call_camera_off.png");
                CameraActionLabel.Text = "تشغيل الكاميرا";
                CameraButton.BackgroundColor = ThemeColor("HimoDangerSoft", "#41162F");
                CameraActionLabel.TextColor = ThemeColor("HimoDanger", "#D9485F");
            });
            return pauseTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Background video pause failed: {ex.Message}");
        }
#endif
        return Task.CompletedTask;
    }

    public Task ResumeVideoRenderingForForegroundAsync()
    {
#if ANDROID
        if (_mode != CallMode.Video)
            return Task.CompletedTask;
        try
        {
            var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
                .GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
            if (engine is not null)
                return engine.ResumeVideoAfterBackgroundAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Foreground video resume failed: {ex.Message}");
        }
#endif
        return Task.CompletedTask;
    }

    public void EnsureBackgroundRingtoneIfIncoming()
    {
#if ANDROID
        if (!_incoming || _incomingAnswered || _callId == Guid.Empty || _conversationId == Guid.Empty)
            return;
        var state = _calls.Current;
        if (state is not null && state.CallId == _callId && (state.IsAccepted || state.IsConnected))
            return;
        try
        {
            StopIncomingRingtone();
            _ = _notifications.ShowIncomingCallAsync(
                string.IsNullOrWhiteSpace(_peerName) ? "مكالمة واردة" : _peerName,
                _conversationId.ToString("D"), Mode, _callId.ToString("D"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Background ringtone restart failed: {ex.Message}");
        }
#endif
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
        _ = SwipePageTransition.AnimateIncomingAsync(this, SwipeDirection.Left);
        _calls.StateChanged -= CallsStateChanged;
        _calls.StateChanged += CallsStateChanged;
        _calls.CallEnded -= CallsEnded;
        _calls.CallEnded += CallsEnded;
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
#if ANDROID
        NotificationService.IncomingCallTerminatedByPush -= OnIncomingCallTerminatedByPush;
        NotificationService.IncomingCallTerminatedByPush += OnIncomingCallTerminatedByPush;
        NotificationService.IncomingCallAnsweredByPush -= OnIncomingCallAnsweredByPush;
        NotificationService.IncomingCallAnsweredByPush += OnIncomingCallAnsweredByPush;
#endif
        var activeState = _calls.Current;
        var activeSameCall = activeState is not null && activeState.ConversationId == _conversationId &&
                             (_callId != Guid.Empty
                                 ? activeState.CallId == _callId
                                 : !_incoming);
        // Preserve a matching Answering/Accept push across activity resume. The caller's
        // CallState.IsAccepted remains false (that flag belongs to the local answerer),
        // so replacing this UI latch from CallState alone would restart ringback on resume.
        _remoteAccepted = activeSameCall && !_incoming &&
            (_remoteAccepted || activeState!.IsAccepted);
        _incomingAnswered = _incoming && activeSameCall && (activeState!.IsAccepted || activeState.IsConnected);
        _connectedAt = activeSameCall && activeState!.IsConnected ? DateTimeOffset.UtcNow : _connectedAt;
        if (!activeSameCall) _navigationStarted = false;
        _avatarLoadCts?.Cancel();
        _avatarLoadCts?.Dispose();
        _avatarLoadCts = new CancellationTokenSource();
        var avatarToken = _avatarLoadCts.Token;
        PrepareCachedPeerAvatar();
        _ = LoadPeerAvatarAsync(avatarToken);
#if ANDROID
        // Start the expensive factory initialization while this screen is preparing,
        // before the user answers (incoming) or while the outgoing screen is connecting.
        _ = WarmMediaFactoryAsync();
#endif
        VideoSurface.IsVisible = false;
        VideoWaitingOverlay.IsVisible = true;
        ProfilePanel.IsVisible = true;
#if ANDROID
        var currentVideoEngine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
            .GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
        _cameraEnabled = _mode == CallMode.Video &&
            (activeState?.IsConnected == true ? currentVideoEngine?.IsCameraEnabled == true : true);
#else
        _cameraEnabled = _mode == CallMode.Video;
#endif
        CameraButton.IsEnabled = false;
        CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.42;
        CameraButton.Source = _cameraEnabled ? ThemedCallIcon("himo_call_camera.png") : ThemedCallIcon("himo_call_camera_off.png");
        CameraButton.BackgroundColor = _mode == CallMode.Video
            ? ThemeColor("HimoSurface", "#160A27")
            : ThemeColor("HimoCard", "#170B28");
        MuteButton.Source = ThemedCallIcon("himo_call_mic.png");
        MuteActionLabel.Text = "المايك مفتوح";
        SwitchCameraButton.Source = ThemedCallIcon("himo_cam_switch.png");
        SwitchCameraActionLabel.Text = "تبديل الكاميرا";
        SpeakerButton.Source = ThemedCallIcon("himo_call_speaker_off.png");
        RemoteAudioButton.Source = ThemedCallIcon("himo_call_speaker_off.png");
        CameraActionLabel.Text = _mode == CallMode.Video
            ? (_cameraEnabled ? "إيقاف الكاميرا" : "تشغيل الكاميرا")
            : "فيديو غير متاح";
        CameraActionLabel.TextColor = _mode == CallMode.Video
            ? ThemeColor("HimoTextSecondary", "#B7A2D2")
            : ThemeColor("HimoMuted", "#BBA6D6");
        ModeLabel.Text = _mode == CallMode.Video ? "مكالمة فيديو" : "مكالمة صوتية";
        NameLabel.Text = _peerName;
        VideoCallNameLabel.Text = _peerName;
        InitialLabel.Text = FirstTextElement(_peerName);
        StatusLabel.Text = _incoming ? "مكالمة واردة..." : "جاري تجهيز الاتصال...";
        VideoStatusLabel.Text = StatusLabel.Text;
        WaitingCallTitle.Text = _incoming ? "جارٍ الاتصال..." : "جاري الاتصال بالطرف الآخر";
        WaitingCallSubtitle.Text = _incoming ? "يجري إعداد الصوت والصورة" : "بانتظار رد الطرف الآخر...";
        RemoteAudioButton.IsEnabled = false;
        IncomingActions.IsVisible = _incoming && !_incomingAnswered;
        OutgoingActions.IsVisible = !_incoming || _incomingAnswered;
        CallControls.IsVisible = activeSameCall && activeState!.IsConnected;
        _callChromeVisible = true;
        UpdateCallControlLayout();
        UpdateCallPresentation(activeSameCall && activeState!.IsConnected);
        _ = LoadOwnProfileAvatarAsync();

        // Incoming calls must ring immediately even when the app was opened from a
        // cold-start FCM notification and SignalR has not connected yet. Do not
        // auto-answer: media and permissions are initialized only after the user taps
        // the green answer button.
        if (_incoming)
        {
            if (_callId != Guid.Empty && _notifications.IsIncomingCallSuppressed(_callId.ToString("D")))
            {
                StopIncomingRingtone();
                if (_calls.Current is { } suppressedState &&
                    suppressedState.ConversationId == _conversationId && suppressedState.CallId == _callId)
                {
                    try { await _calls.RejectAsync(); } catch { }
                }
                await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
                await NavigateBackFromCallAsync();
                return;
            }

            // Once the full-screen activity owns the incoming call, remove its tray
            // notification without marking the still-ringing call as ended.
            _ = _notifications.ClearIncomingCallAsync(
                _conversationId.ToString("D"), _callId.ToString("D"), suppressFurtherRinging: false);

            // Warm SignalR while the incoming call is ringing, instead of waiting
            // until the answer button is pressed (cold starts used to cost several seconds).
            _ = WarmIncomingSignalChannelAsync();
            try
            {
                await _calls.PrepareIncomingAsync(new CallRequest(_conversationId, _mode, _callId));
                var preparedState = _calls.Current;
                if (preparedState is null || preparedState.ConversationId != _conversationId ||
                    (_callId != Guid.Empty && preparedState.CallId != _callId))
                {
#if ANDROID
                    StopIncomingRingtone();
#endif
                    await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
                    await NavigateBackFromCallAsync();
                    return;
                }
                if (_callId == Guid.Empty) _callId = preparedState.CallId;
                if (_notifications.IsIncomingCallSuppressed(_callId.ToString("D")))
                {
#if ANDROID
                    StopIncomingRingtone();
#endif
                    try { await _calls.RejectAsync(); } catch { }
                    await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
                    await NavigateBackFromCallAsync();
                    return;
                }
                _incomingAnswered = _incomingAnswered || preparedState.IsAccepted || preparedState.IsConnected;
#if ANDROID
                if (!_incomingAnswered)
                    StartIncomingRingtone();
#endif
                StatusLabel.Text = _incomingAnswered ? "تم الرد، جارٍ الاتصال..." : "مكالمة واردة — اضغط رد للبدء";
                VideoStatusLabel.Text = StatusLabel.Text;
                IncomingActions.IsVisible = !_incomingAnswered;
                OutgoingActions.IsVisible = _incomingAnswered;
                UpdateCallPresentation(preparedState.IsConnected);
            }
            catch (Exception ex)
            {
#if ANDROID
                StopIncomingRingtone();
#endif
                var callWasFinished = _callId != Guid.Empty &&
                    (_notifications.IsIncomingCallSuppressed(_callId.ToString("D")) ||
                     ex is InvalidOperationException && ex.Message.Contains("انتهت بالفعل", StringComparison.Ordinal));
                var callCollision = ex is InvalidOperationException &&
                    ex.Message.Contains("يوجد اتصال نشط أو جارٍ لمكالمة أخرى", StringComparison.Ordinal);
                if (callWasFinished || callCollision)
                {
                    // If another call is already active, reject this exact incoming CallId
                    // rather than displaying a second answer screen over the active call.
                    if (callCollision && _callId != Guid.Empty)
                    {
                        using var rejectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                        try
                        {
                            await _realtime.SendCallSignalAsync(
                                _conversationId, CallSignalType.Reject.ToString(),
                                CallSignalEnvelope.Serialize(_callId, _mode), rejectTimeout.Token);
                        }
                        catch (Exception rejectEx)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Himo CallPage] Busy-call rejection could not be signaled: {rejectEx.Message}");
                        }
                    }
                    await _notifications.ClearIncomingCallAsync(
                        _conversationId.ToString("D"), _callId.ToString("D"));
                    await NavigateBackFromCallAsync();
                    return;
                }
                StatusLabel.Text = "تعذر استقبال المكالمة.";
                VideoStatusLabel.Text = StatusLabel.Text;
                System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Incoming call setup failed: {ex}");
            }
            return;
        }

        if (!_incoming && activeSameCall)
        {
            // Navigation / activity resume can call OnAppearing again. Reuse the active
            // call instead of sending a second Invite and starting another call screen.
            _callId = activeState!.CallId;
            StatusLabel.Text = activeState.IsAccepted ? "تم الرد، جارٍ الاتصال..." : "بانتظار قبول المكالمة...";
            VideoStatusLabel.Text = StatusLabel.Text;
            if (activeState.IsAccepted)
            {
#if ANDROID
                StopOutgoingRingback();
#endif
            }
            return;
        }

        if (_callSetupInProgress) return;
        _callSetupInProgress = true;
        try
        {
            // Outgoing calls need a ready signal channel before an Invite can be sent.
            await _realtime.EnsureConnectedAsync();

            var granted = await _calls.RequestPermissionsAsync(_mode);
            if (!granted)
            {
                StatusLabel.Text = "يلزم السماح بالكاميرا والميكروفون";
                return;
            }

            await _calls.StartAsync(new CallRequest(_conversationId, _mode));
            // StartAsync creates the authoritative call ID before signaling.
            _callId = _calls.Current?.CallId ?? Guid.Empty;
            StatusLabel.Text = _remoteAccepted ? "تم الرد، جارٍ الاتصال..." : "بانتظار قبول المكالمة...";

#if ANDROID
            if (_mode == CallMode.Video)
                await AttachVideoWithRetryAsync();
#endif
        }
        catch (Exception ex)
        {
#if ANDROID
            StopOutgoingRingback();
#endif
            StatusLabel.Text = "تعذر تجهيز المكالمة. تحقق من الاتصال ثم حاول مرة أخرى.";
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Start failed: {ex}");
            // StartAsync can fail before CallService publishes a current call state,
            // so no CallEnded event is guaranteed. Explicitly leave this page to avoid
            // trapping the caller in a dead call screen.
            await NavigateBackFromCallAsync();
        }
        finally
        {
            _callSetupInProgress = false;
        }
    }

    private async Task WarmIncomingSignalChannelAsync()
    {
        try
        {
            await _api.TokenInitialization.WaitAsync(TimeSpan.FromSeconds(8));
            if (_api.HasToken)
                await _realtime.EnsureConnectedAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Incoming SignalR warm-up skipped: {ex.Message}");
        }
    }

    private async Task WarmMediaFactoryAsync()
    {
#if ANDROID
        try
        {
            var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
                .GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
            if (engine is not null)
                await engine.WarmUpFactoryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A warm-up failure must not prevent a normal call startup; StartAsync
            // still performs the regular initialization path and reports real failures.
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] WebRTC factory warm-up skipped: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private async void AcceptIncomingClicked(object? sender, EventArgs e)
    {
        if (!_incoming || _incomingAnswered || _navigationStarted) return;

        AnswerIncomingButton.IsEnabled = false;
        RejectIncomingButton.IsEnabled = false;
#if ANDROID
        StopIncomingRingtone();
#endif
        _incomingAnswered = true;
        IncomingActions.IsVisible = false;
        OutgoingActions.IsVisible = true;
        StatusLabel.Text = "جارٍ الاتصال...";
        VideoStatusLabel.Text = StatusLabel.Text;
        UpdateCallPresentation(_calls.Current?.IsConnected == true);
        WaitingCallTitle.Text = "جارٍ الاتصال...";
        WaitingCallSubtitle.Text = "يجري تجهيز الصوت";
        try
        {
            var granted = await _calls.RequestPermissionsAsync(_mode);
            if (!granted)
            {
                StatusLabel.Text = "يلزم السماح بالميكروفون لقبول المكالمة";
                await _calls.RejectAsync();
                await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
                await NavigateBackFromCallAsync();
                return;
            }

            if (_callId != Guid.Empty && _realtime.IsConnected)
            {
                // Notify the caller promptly only when SignalR is already ready;
                // never make local microphone/WebRTC startup wait on reconnect.
                using var answeringTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                try
                {
                    await _realtime.SendCallSignalAsync(
                        _conversationId, CallSignalType.Answering.ToString(),
                        CallSignalEnvelope.Serialize(_callId, _mode), answeringTimeout.Token);
                }
                catch (Exception signalEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo CallPage] Early Answering signal skipped; media startup continues: {signalEx.Message}");
                }
            }
            else
            {
                // This runs in parallel with local media initialization.
                _ = WarmIncomingSignalChannelAsync();
            }
            CallControls.IsVisible = _calls.Current?.IsConnected == true;
            WaitingCallTitle.Text = "جارٍ الاتصال...";
            WaitingCallSubtitle.Text = "يجري إعداد الصوت والصورة";
            StatusLabel.Text = "جارٍ الاتصال...";
            await _calls.AcceptAsync();

            // The remote side can hang up while Android is initializing the audio
            // route or PeerConnection. AcceptAsync intentionally returns without
            // resurrecting a terminated call; re-check identity before this page does
            // any more work (especially video renderer attachment) for that old call.
            var acceptedState = _calls.Current;
            if (acceptedState is null ||
                acceptedState.ConversationId != _conversationId ||
                acceptedState.CallId != _callId ||
                (!acceptedState.IsAccepted && !acceptedState.IsConnected))
            {
#if ANDROID
                StopIncomingRingtone();
#endif
                await _notifications.ClearIncomingCallAsync(
                    _conversationId.ToString("D"), _callId.ToString("D"));
                return;
            }

            await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
#if ANDROID
            if (_mode == CallMode.Video)
                await AttachVideoWithRetryAsync();
#endif
        }
        catch (Exception ex)
        {
#if ANDROID
            StopIncomingRingtone();
#endif
            StatusLabel.Text = "تعذر قبول المكالمة. حاول مرة أخرى.";
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Accept failed: {ex}");
            try { await _calls.RejectAsync(); } catch { }
            await NavigateBackFromCallAsync();
        }
        finally
        {
            if (!_navigationStarted && !_incomingAnswered)
            {
                AnswerIncomingButton.IsEnabled = true;
                RejectIncomingButton.IsEnabled = true;
            }
        }
    }

    private async void RejectIncomingClicked(object? sender, EventArgs e)
    {
        if (_navigationStarted) return;
#if ANDROID
        StopIncomingRingtone();
#endif
        try { await _calls.RejectAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Reject failed: {ex}"); }
        await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
        await NavigateBackFromCallAsync();
    }

#if ANDROID
    private void StartIncomingRingtone()
    {
        try
        {
            StopIncomingRingtone();
            var context = global::Android.App.Application.Context;
            if (NotificationService.IsGroupCallMutedForConversation(_conversationId.ToString("D")))
            {
                // Keep the incoming call screen available, but remain silent for this group.
                StartIncomingRingTimeout(_callId);
                return;
            }

            var uri = NotificationService.GetGroupCallToneUriForConversation(context, _conversationId.ToString("D"))
                ?? global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone);
            if (uri is null) return;

            var ringtone = global::Android.Media.RingtoneManager.GetRingtone(context, uri);
            if (ringtone is null) return;
            _incomingRingtone = ringtone;
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.P)
                ringtone.Looping = true;
            ringtone.Play();
            StartIncomingRingTimeout(_callId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Incoming ringtone failed: {ex}");
            StopIncomingRingtone();
        }
    }

    private void StartIncomingRingTimeout(Guid callId)
    {
        if (callId == Guid.Empty) return;
        if (_incomingRingTimeoutCts is not null && _incomingRingTimeoutCallId == callId &&
            !_incomingRingTimeoutCts.IsCancellationRequested)
            return;

        CancelIncomingRingTimeout();
        _incomingRingTimeoutCallId = callId;
        var cts = _incomingRingTimeoutCts = new CancellationTokenSource();
        _ = ExpireIncomingRingtoneAsync(callId, cts.Token);
    }

    private async Task ExpireIncomingRingtoneAsync(Guid callId, CancellationToken cancellationToken)
    {
        try
        {
            // Failsafe when the remote cancel push cannot be delivered. Normal End/Reject
            // signals cancel the ringtone immediately; this cap prevents infinite ringing.
            await Task.Delay(TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (cancellationToken.IsCancellationRequested || !_incoming || _incomingAnswered ||
                    _navigationStarted || _callId != callId)
                    return;
                StopIncomingRingtone();
                try
                {
                    if (_calls.Current is { } current && current.CallId == callId)
                        await _calls.RejectAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Ring timeout cleanup failed: {ex.Message}");
                }
                await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), callId.ToString("D"));
                await NavigateBackFromCallAsync();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Ring timeout handler failed: {ex.Message}");
        }
    }

    private void CancelIncomingRingTimeout()
    {
        var cts = _incomingRingTimeoutCts;
        _incomingRingTimeoutCts = null;
        _incomingRingTimeoutCallId = Guid.Empty;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private void StopIncomingRingtone()
    {
        try { _incomingRingtone?.Stop(); } catch { }
        try { _incomingRingtone?.Dispose(); } catch { }
        _incomingRingtone = null;
        CancelIncomingRingTimeout();
    }
#endif

    private async void CallsEnded(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _callUiStateVersion);
        _remoteAudioToggleInProgress = false;
        StopTimer();
#if ANDROID
        StopOutgoingRingback();
        StopIncomingRingtone();
        MainActivity.ClearIncomingCallWindowFlags();
#endif
        _ = _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "انتهت المكالمة";
            RemoteAudioButton.IsEnabled = false;
            RemoteAudioButton.Source = ThemedCallIcon("himo_call_speaker_off.png");
        });

        if (_navigationStarted) return;
        _navigationStarted = true;
        try
        {
            if (Shell.Current is null) return;
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await SwipePageTransition.GoBackForCallAsync(this));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Navigation after end failed: {ex}");
            _navigationStarted = false;
        }
    }

    private void StartTimer()
    {
        if (_timer is not null) return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            if (_calls.Current?.IsConnected == true)
            {
                StatusLabel.Text = FormatDuration();
                VideoStatusLabel.Text = FormatDuration();
            }
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private string FormatDuration()
    {
        if (_connectedAt is null) return "متصل";
        var elapsed = DateTimeOffset.UtcNow - _connectedAt.Value;
        return $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

#if ANDROID
    private void StartOutgoingRingback()
    {
        if (_incoming || _remoteAccepted || _calls.Current?.IsConnected == true || _outgoingRingback is not null) return;

        try
        {
            StopOutgoingRingback();
            var context = global::Android.App.Application.Context;
            if (context is null) return;

            // Use the voice-call audio stream so the outgoing ringback is routed
            // to the handset earpiece instead of the loudspeaker/ringtone stream.
            var audioManager = context.GetSystemService(global::Android.Content.Context.AudioService)
                as global::Android.Media.AudioManager;
            _ringbackAudioManager = audioManager;
            if (audioManager is not null)
            {
                _previousAudioMode = audioManager.Mode;
#pragma warning disable CA1422 // SpeakerphoneOn is retained for compatibility with Android API levels below 34.
                _previousSpeakerphoneOn = audioManager.SpeakerphoneOn;
                audioManager.SpeakerphoneOn = false;
#pragma warning restore CA1422
                audioManager.Mode = global::Android.Media.Mode.InCommunication;
            }

            // ToneGenerator has no Ringtone.SetStreamType/SetAudioAttributes API;
            // constructing it on VoiceCall is the supported way to select the stream.
            // Keep a non-null local reference for nullable analysis. .NET for Android
            // exposes the Java tone constants through Android.Media.ToneGeneratorTone.
            var toneGenerator = new global::Android.Media.ToneGenerator(
                global::Android.Media.Stream.VoiceCall, 80);
            _outgoingRingback = toneGenerator;

            // Use the named Android constant: numeric value 33 is SupPip, not ringback.
            toneGenerator.StartTone(global::Android.Media.Tone.SupRingtone, -1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Outgoing ringback failed: {ex}");
            StopOutgoingRingback();
        }
    }

    private void StopOutgoingRingback()
    {
        try { _outgoingRingback?.StopTone(); } catch { }
        try { _outgoingRingback?.Release(); } catch { }
        try { _outgoingRingback?.Dispose(); } catch { }
        _outgoingRingback = null;

        // Restore the previous route only when the call page is actually
        // disappearing/ending; while the call is being answered, keep the
        // normal handset route selected for the conversation.
        if (_navigationStarted || _calls.Current is null)
        {
            try
            {
                var audioManager = _ringbackAudioManager;
                if (audioManager is not null && _previousAudioMode.HasValue)
                    audioManager.Mode = _previousAudioMode.Value;
                if (audioManager is not null && _previousSpeakerphoneOn.HasValue)
                {
#pragma warning disable CA1422 // SpeakerphoneOn is retained for compatibility with Android API levels below 34.
                    audioManager.SpeakerphoneOn = _previousSpeakerphoneOn.Value;
#pragma warning restore CA1422
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Audio route restore failed: {ex.Message}");
            }
            _ringbackAudioManager = null;
            _previousAudioMode = null;
            _previousSpeakerphoneOn = null;
        }
    }

    private async Task AttachVideoWithRetryAsync()
    {
        if (_mode != CallMode.Video) return;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var attached = false;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
                    if (engine is null || !engine.IsStarted) return;
                    await engine.AttachRemoteVideoAsync(RemoteVideoHost);
                    await engine.AttachLocalVideoAsync(LocalVideoHost);
                    attached = true;
                });

                if (attached)
                {
                    VideoWaitingOverlay.IsVisible = false;
                    UpdateCallPresentation(_calls.Current?.IsConnected == true);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Video renderer attach attempt {attempt + 1} failed: {ex.Message}");
            }

            await Task.Delay(120);
        }
    }
#endif

#if ANDROID
    private void OnIncomingCallAnsweredByPush(string conversationId, string callId, string state)
    {
        // Answering/Accept is not an end signal. Use FCM as a fallback for stopping
        // caller ringback when SignalR is temporarily unavailable, but keep the media
        // session alive so the subsequent SDP/ICE exchange can still complete.
        if (_incoming || _navigationStarted ||
            !Guid.TryParse(conversationId, out var incomingConversationId) ||
            incomingConversationId != _conversationId ||
            !Guid.TryParse(callId, out var parsedCallId) || parsedCallId == Guid.Empty)
            return;

        var activeState = _calls.Current;
        var expectedCallId = _callId != Guid.Empty ? _callId : (activeState?.CallId ?? Guid.Empty);
        if (expectedCallId == Guid.Empty || parsedCallId != expectedCallId)
            return;

        if (!string.Equals(state, "answering", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(state, "accept", StringComparison.OrdinalIgnoreCase))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // The push can race the return from CallService.StartAsync, just before
            // this page copies Current.CallId into _callId. Re-check using authoritative
            // service state so a very fast answer is not ignored because the UI field lags.
            var currentCallId = _callId != Guid.Empty ? _callId : (_calls.Current?.CallId ?? Guid.Empty);
            if (_incoming || _navigationStarted || currentCallId != parsedCallId ||
                _calls.Current?.CallId != parsedCallId)
                return;

            _callId = parsedCallId;
            _remoteAccepted = true;
            StopOutgoingRingback();
            if (_calls.Current?.IsConnected != true)
            {
                StatusLabel.Text = "تم الرد، جارٍ الاتصال...";
                VideoStatusLabel.Text = StatusLabel.Text;
                WaitingCallTitle.Text = "تم الرد، جارٍ الاتصال...";
                WaitingCallSubtitle.Text = "يجري إعداد الصوت";
            }
            System.Diagnostics.Debug.WriteLine(
                $"[Himo CallPage] FCM {state} fallback received; stopped ringback for callId={parsedCallId:D}.");
        });
    }

    private void OnIncomingCallTerminatedByPush(string conversationId, string callId)
    {
        if (_navigationStarted ||
            !Guid.TryParse(conversationId, out var incomingConversationId) ||
            incomingConversationId != _conversationId)
            return;

        var parsedCallId = Guid.Empty;
        if (Guid.TryParse(callId, out var parsed) && parsed != Guid.Empty)
            parsedCallId = parsed;
        if (_callId != Guid.Empty && parsedCallId != Guid.Empty && _callId != parsedCallId)
            return;
        if (Interlocked.Exchange(ref _remoteEndHandling, 1) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_navigationStarted) return;
                StopIncomingRingtone();
                StopOutgoingRingback();
                var targetCallId = parsedCallId != Guid.Empty ? parsedCallId : _callId;
                if (targetCallId != Guid.Empty)
                {
                    try { await _calls.TerminateFromRemoteAsync(targetCallId); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Remote-push termination cleanup failed: {ex.Message}"); }
                }
                await _notifications.ClearIncomingCallAsync(
                    _conversationId.ToString("D"), targetCallId.ToString("D"), suppressFurtherRinging: true);
                await NavigateBackFromCallAsync();
            }
            finally
            {
                Volatile.Write(ref _remoteEndHandling, 0);
            }
        });
    }
#endif

    private void OnRealtimeCallSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (_incoming || signal.ConversationId != _conversationId)
            return;

        var isAnswering = string.Equals(signal.Type, CallSignalType.Answering.ToString(), StringComparison.OrdinalIgnoreCase);
        var isAccept = string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase);
        var isReject = string.Equals(signal.Type, CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase);
        var isEnd = string.Equals(signal.Type, CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase);
        if (!isAnswering && !isAccept && !isReject && !isEnd)
            return;

        // Ignore delayed signals from an earlier call in the same conversation.
        var expectedCallId = _callId != Guid.Empty ? _callId : (_calls.Current?.CallId ?? Guid.Empty);
        if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope) &&
            expectedCallId != Guid.Empty && envelope.CallId != Guid.Empty &&
            envelope.CallId != expectedCallId)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo CallPage] Ignore {signal.Type} for another call; expected={expectedCallId:D}; received={envelope.CallId:D}");
            return;
        }

        if (isAnswering || isAccept)
        {
            _remoteAccepted = true;
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Remote {(isAnswering ? "is answering" : "accepted")} callId={expectedCallId:D}; stopping ringback.");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_navigationStarted && _calls.Current?.IsConnected != true)
                    StatusLabel.Text = "تم الرد، جارٍ الاتصال...";
            });
        }

        // Ringback ends on remote Accept, Reject or End. Waiting for WebRTC ICE to
        // connect here caused the sender to keep ringing after the other party answered.
#if ANDROID
        StopOutgoingRingback();
#endif
        if (isAnswering || isAccept || isReject || isEnd)
            _ = _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), expectedCallId.ToString("D"));
    }

    private async void MoreClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_mode == CallMode.Video)
            {
                var selected = await DisplayActionSheetAsync("خيارات المكالمة", "إلغاء", null, "تبديل الكاميرا");
                if (string.Equals(selected, "تبديل الكاميرا", StringComparison.Ordinal))
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>();
                        if (engine is not null) await engine.SwitchCameraAsync();
                    });
            }
            else
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("معلومات المكالمة"), $"مكالمة صوتية\n{FormatDuration()}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] More options failed: {ex.Message}");
        }
    }

    private async void ToggleCameraClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (_mode != CallMode.Video) return;
        var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
        if (engine is null) return;
        _cameraEnabled = !_cameraEnabled;
        await engine.SetCameraEnabledAsync(_cameraEnabled);
        CameraButton.Source = _cameraEnabled ? ThemedCallIcon("himo_call_camera.png") : ThemedCallIcon("himo_call_camera_off.png");
        CameraActionLabel.Text = _cameraEnabled ? "إغلاق الكاميرا" : "تشغيل الكاميرا";
        CameraButton.BackgroundColor = _cameraEnabled ? ThemeColor("HimoSurface", "#160A27") : ThemeColor("HimoDangerSoft", "#41162F");
        CameraActionLabel.TextColor = _cameraEnabled ? ThemeColor("HimoTextSecondary", "#B7A2D2") : ThemeColor("HimoDanger", "#D9485F");
        LocalCameraOffOverlay.IsVisible = !_cameraEnabled;
        SwitchCameraButton.IsEnabled = _cameraEnabled;
#else
        await Task.CompletedTask;
#endif
    }

    private async void SwitchCameraClicked(object? sender, EventArgs e)
    {
        if (_mode != CallMode.Video || !_cameraEnabled) return;
        try
        {
            var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>();
            if (engine is not null) await engine.SwitchCameraAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Camera switch failed: {ex.Message}");
            await DisplayAlertAsync("الكاميرا", "تعذر تبديل الكاميرا. حاول مرة أخرى.", "حسنًا");
        }
    }

    private async void EndClicked(object? sender, EventArgs e)
    {
        if (_navigationStarted) return;
        try
        {
            if (_incoming && !_incomingAnswered)
                await _calls.RejectAsync();
            else
                await _calls.EndAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] End/reject failed: {ex}");
        }
        finally
        {
#if ANDROID
            StopIncomingRingtone();
            MainActivity.ClearIncomingCallWindowFlags();
#endif
            await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
            await NavigateBackFromCallAsync();
        }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (_navigationStarted) return;
        try
        {
            if (_calls.Current is not null)
            {
                if (_incoming && !_incomingAnswered)
                    await _calls.RejectAsync();
                else
                    await _calls.EndAsync();
            }
#if ANDROID
            StopIncomingRingtone();
            MainActivity.ClearIncomingCallWindowFlags();
#endif
            await _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Back failed: {ex}");
        }
        finally
        {
            await NavigateBackFromCallAsync();
        }
    }

    private async Task NavigateBackFromCallAsync()
    {
        if (_navigationStarted) return;
        _navigationStarted = true;
#if ANDROID
        MainActivity.ClearIncomingCallWindowFlags();
#endif
        _ = _notifications.ClearIncomingCallAsync(_conversationId.ToString("D"), _callId.ToString("D"));
        try
        {
            var shell = Shell.Current;
            if (shell is null) return;
            await MainThread.InvokeOnMainThreadAsync(async () => await shell.GoToAsync(".."));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Navigation failed: {ex}");
            _navigationStarted = false;
        }
    }
    private async void ToggleMuteClicked(object? sender, EventArgs e)
    {
        var muted = !(_calls.Current?.IsMuted ?? false);
        await _calls.SetMutedAsync(muted);
        MuteActionLabel.Text = muted ? "المايك مغلق" : "المايك مفتوح";
        MuteButton.BackgroundColor = muted ? ThemeColor("HimoDangerSoft", "#41162F") : ThemeColor("HimoSurface", "#160A27");
        MuteButton.Source = muted ? ThemedCallIcon("himo_call_mic_off.png") : ThemedCallIcon("himo_call_mic.png");
        MuteActionLabel.TextColor = muted ? ThemeColor("HimoDanger", "#D9485F") : ThemeColor("HimoTextSecondary", "#B7A2D2");
    }

    private async void ToggleRemoteAudioClicked(object? sender, EventArgs e)
    {
        if (_remoteAudioToggleInProgress || _calls.Current?.IsConnected != true)
            return;

        _remoteAudioToggleInProgress = true;
        RemoteAudioButton.IsEnabled = false;

        try
        {
            var enabled = !_calls.IsRemoteAudioEnabled;
            await _calls.SetRemoteAudioEnabledAsync(enabled);
            RemoteAudioButton.Source = enabled ? ThemedCallIcon("himo_call_speaker.png") : ThemedCallIcon("himo_call_speaker_off.png");
            RemoteAudioActionLabel.Text = enabled ? "كتم الوارد" : "تشغيل الوارد";
            RemoteAudioButton.BackgroundColor = enabled ? ThemeColor("HimoSoft", "#25113D") : ThemeColor("HimoSurface", "#160A27");
            RemoteAudioActionLabel.TextColor = enabled ? ThemeColor("HimoAccent", "#A12AFF") : ThemeColor("HimoTextSecondary", "#B7A2D2");
            StatusLabel.Text = enabled ? "الصوت الوارد مفعّل" : "الصوت الوارد مكتوم";
        }
        finally
        {
            _remoteAudioToggleInProgress = false;
            RemoteAudioButton.IsEnabled = _calls.Current?.IsConnected == true;
        }
    }

    private async void ToggleSpeakerClicked(object? sender, EventArgs e)
    {
        var enabled = !(_calls.Current?.IsSpeakerOn ?? false);
        await _calls.SetSpeakerAsync(enabled);
        SpeakerButton.Source = enabled ? ThemedCallIcon("himo_call_speaker.png") : ThemedCallIcon("himo_call_speaker_off.png");
        SpeakerActionLabel.Text = enabled ? "إيقاف المكبر" : "مكبر الصوت";
        SpeakerButton.BackgroundColor = enabled ? ThemeColor("HimoSoft", "#25113D") : ThemeColor("HimoSurface", "#160A27");
        SpeakerActionLabel.TextColor = enabled ? ThemeColor("HimoAccent", "#A12AFF") : ThemeColor("HimoTextSecondary", "#B7A2D2");
        StatusLabel.Text = enabled ? "مكبر الصوت مفعّل" : "مكبر الصوت متوقف";
    }
}
