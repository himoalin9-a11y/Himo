using Himo.Services;
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
#if ANDROID
    private global::Android.Media.ToneGenerator? _outgoingRingback;
    private global::Android.Media.Ringtone? _incomingRingtone;
    private global::Android.Media.AudioManager? _ringbackAudioManager;
    private global::Android.Media.Mode? _previousAudioMode;
    private bool? _previousSpeakerphoneOn;
#endif
    private bool _remoteAccepted;
    private string _peerName = "جهة اتصال";

    public string ConversationId
    {
        get => _conversationId.ToString("D");
        set => Guid.TryParse(value, out _conversationId);
    }

    public string Mode
    {
        get => _mode == CallMode.Video ? "video" : "audio";
        set => _mode = string.Equals(value, "video", StringComparison.OrdinalIgnoreCase) ? CallMode.Video : CallMode.Audio;
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
        }
    }

    private static string FirstTextElement(string value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "H" : trimmed.Substring(0, 1);
    }

    public CallPage(ICallService calls, HimoRealtimeService realtime, WebRtcNegotiationCoordinator negotiation)
    {
        InitializeComponent();
        _calls = calls;
        _realtime = realtime;
        _negotiation = negotiation;
        _calls.StateChanged += CallsStateChanged;
        _calls.CallEnded += CallsEnded;
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
            MuteButton.BackgroundColor = state.IsMuted ? Color.FromArgb("#702B3A") : Color.FromArgb("#24354B");
            MuteActionLabel.TextColor = state.IsMuted ? Color.FromArgb("#FFD4DB") : Color.FromArgb("#D1DCEA");
            CameraActionLabel.Text = _mode == CallMode.Video
                ? (_cameraEnabled ? "إيقاف الكاميرا" : "تشغيل الكاميرا")
                : "فيديو غير متاح";
            CameraButton.BackgroundColor = _mode != CallMode.Video
                ? Color.FromArgb("#19283A")
                : (_cameraEnabled ? Color.FromArgb("#24364C") : Color.FromArgb("#702B3A"));
            CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.42;
            CameraActionLabel.TextColor = _mode != CallMode.Video
                ? Color.FromArgb("#8396AE")
                : (_cameraEnabled ? Color.FromArgb("#DCE7F5") : Color.FromArgb("#FFD4DB"));
            SpeakerActionLabel.Text = state.IsSpeakerOn ? "إيقاف المكبر" : "مكبر الصوت";
            SpeakerButton.BackgroundColor = state.IsSpeakerOn ? Color.FromArgb("#17563E") : Color.FromArgb("#24354B");
            SpeakerActionLabel.TextColor = state.IsSpeakerOn ? Color.FromArgb("#B7F5D1") : Color.FromArgb("#D1DCEA");
            RemoteAudioActionLabel.Text = state.IsRemoteAudioEnabled ? "كتم الوارد" : "تشغيل الوارد";
            RemoteAudioButton.BackgroundColor = state.IsRemoteAudioEnabled ? Color.FromArgb("#17563E") : Color.FromArgb("#24354B");
            RemoteAudioActionLabel.TextColor = state.IsRemoteAudioEnabled ? Color.FromArgb("#B7F5D1") : Color.FromArgb("#D1DCEA");
#if ANDROID
            if (_mode == CallMode.Video && state.IsConnected) _ = AttachVideoWithRetryAsync();
#endif
            RemoteAudioButton.Source = state.IsRemoteAudioEnabled ? "call_speaker_white.png" : "call_speaker_off_white.png";
            RemoteAudioButton.IsEnabled = state.IsConnected && !_remoteAudioToggleInProgress;
            MuteButton.Source = state.IsMuted ? "call_mic_off_white.png" : "call_mic_white.png";
            SpeakerButton.Source = state.IsSpeakerOn ? "call_speaker_white.png" : "call_speaker_off_white.png";
        });
    }

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _callUiStateVersion);
        _calls.StateChanged -= CallsStateChanged;
        _calls.CallEnded -= CallsEnded;
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        StopTimer();
#if ANDROID
        StopOutgoingRingback();
        StopIncomingRingtone();
#endif
        base.OnDisappearing();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _calls.StateChanged -= CallsStateChanged;
        _calls.StateChanged += CallsStateChanged;
        _calls.CallEnded -= CallsEnded;
        _calls.CallEnded += CallsEnded;
        _realtime.CallSignalReceived -= OnRealtimeCallSignalReceived;
        _realtime.CallSignalReceived += OnRealtimeCallSignalReceived;
        _remoteAccepted = false;
        _incomingAnswered = false;
        _connectedAt = _calls.Current?.IsConnected == true ? DateTimeOffset.UtcNow : null;
        _navigationStarted = false;
        VideoSurface.IsVisible = false;
        VideoWaitingOverlay.IsVisible = true;
        ProfilePanel.IsVisible = true;
        _cameraEnabled = _mode == CallMode.Video;
        CameraButton.IsEnabled = false;
        CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.42;
        CameraButton.Source = _cameraEnabled ? "call_camera_white.png" : "call_camera_off_white.png";
        CameraButton.BackgroundColor = _mode == CallMode.Video
            ? Color.FromArgb("#24364C")
            : Color.FromArgb("#19283A");
        MuteButton.Source = "call_mic_white.png";
        SpeakerButton.Source = "call_speaker_off_white.png";
        RemoteAudioButton.Source = "call_speaker_off_white.png";
        CameraActionLabel.Text = _mode == CallMode.Video
            ? (_cameraEnabled ? "إيقاف الكاميرا" : "تشغيل الكاميرا")
            : "فيديو غير متاح";
        CameraActionLabel.TextColor = _mode == CallMode.Video
            ? Color.FromArgb("#DCE7F5")
            : Color.FromArgb("#8396AE");
        ModeLabel.Text = _mode == CallMode.Video ? "مكالمة فيديو" : "مكالمة صوتية";
        NameLabel.Text = _peerName;
        VideoCallNameLabel.Text = _peerName;
        InitialLabel.Text = FirstTextElement(_peerName);
        StatusLabel.Text = _incoming ? "مكالمة واردة..." : "جاري تجهيز الاتصال...";
        VideoStatusLabel.Text = StatusLabel.Text;
        WaitingCallTitle.Text = _incoming ? "جارٍ الاتصال..." : "جاري الاتصال بالطرف الآخر";
        WaitingCallSubtitle.Text = _incoming ? "يجري إعداد الصوت والصورة" : "بانتظار رد الطرف الآخر...";
        RemoteAudioButton.IsEnabled = false;
        IncomingActions.IsVisible = _incoming;
        OutgoingActions.IsVisible = !_incoming;
        CallControls.IsVisible = false;

        // Incoming calls must ring immediately even when the app was opened from a
        // cold-start FCM notification and SignalR has not connected yet. Do not
        // auto-answer: media and permissions are initialized only after the user taps
        // the green answer button.
        if (_incoming)
        {
            try
            {
                await _calls.PrepareIncomingAsync(new CallRequest(_conversationId, _mode, _callId));
#if ANDROID
                StartIncomingRingtone();
#endif
                StatusLabel.Text = "مكالمة واردة — اضغط رد للبدء";
                VideoStatusLabel.Text = StatusLabel.Text;
            }
            catch (Exception ex)
            {
#if ANDROID
                StopIncomingRingtone();
#endif
                StatusLabel.Text = "تعذر استقبال المكالمة.";
                VideoStatusLabel.Text = StatusLabel.Text;
                System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Incoming call setup failed: {ex}");
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
        }
        finally
        {
            _callSetupInProgress = false;
        }
    }

    private async void AcceptIncomingClicked(object? sender, EventArgs e)
    {
        if (!_incoming || _incomingAnswered || _navigationStarted) return;

        AnswerIncomingButton.IsEnabled = false;
        RejectIncomingButton.IsEnabled = false;
        try
        {
            var granted = await _calls.RequestPermissionsAsync(_mode);
            if (!granted)
            {
                StatusLabel.Text = "يلزم السماح بالميكروفون لقبول المكالمة";
                await _calls.RejectAsync();
                await NavigateBackFromCallAsync();
                return;
            }

            await _realtime.EnsureConnectedAsync();
#if ANDROID
            StopIncomingRingtone();
#endif
            _incomingAnswered = true;
            IncomingActions.IsVisible = false;
            OutgoingActions.IsVisible = true;
            CallControls.IsVisible = _calls.Current?.IsConnected == true;
            WaitingCallTitle.Text = "جارٍ الاتصال...";
            WaitingCallSubtitle.Text = "يجري إعداد الصوت والصورة";
            StatusLabel.Text = "جارٍ الاتصال...";
            await _calls.AcceptAsync();
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
        await NavigateBackFromCallAsync();
    }

#if ANDROID
    private void StartIncomingRingtone()
    {
        try
        {
            StopIncomingRingtone();
            var context = global::Android.App.Application.Context;
            var uri = global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone);
            if (uri is null) return;

            var ringtone = global::Android.Media.RingtoneManager.GetRingtone(context, uri);
            if (ringtone is null) return;
            _incomingRingtone = ringtone;
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.P)
                ringtone.Looping = true;
            ringtone.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Incoming ringtone failed: {ex}");
            StopIncomingRingtone();
        }
    }

    private void StopIncomingRingtone()
    {
        try { _incomingRingtone?.Stop(); } catch { }
        try { _incomingRingtone?.Dispose(); } catch { }
        _incomingRingtone = null;
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
#endif
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "انتهت المكالمة";
            RemoteAudioButton.IsEnabled = false;
            RemoteAudioButton.Source = "call_speaker_off_white.png";
        });

        if (_navigationStarted) return;
        _navigationStarted = true;
        try
        {
            var shell = Shell.Current;
            if (shell is null) return;
            await MainThread.InvokeOnMainThreadAsync(async () => await shell.GoToAsync(".."));
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
        return $"متصل • {elapsed.Minutes:00}:{elapsed.Seconds:00}";
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

    private void OnRealtimeCallSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (_incoming || signal.ConversationId != _conversationId)
            return;

        var isAccept = string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase);
        var isReject = string.Equals(signal.Type, CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase);
        var isEnd = string.Equals(signal.Type, CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase);
        if (!isAccept && !isReject && !isEnd)
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

        if (isAccept)
        {
            _remoteAccepted = true;
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Remote accepted callId={expectedCallId:D}; stopping ringback.");
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
                await DisplayAlertAsync("معلومات المكالمة", $"مكالمة صوتية\n{FormatDuration()}", "حسنًا");
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
        CameraButton.Source = _cameraEnabled ? "call_camera_white.png" : "call_camera_off_white.png";
        CameraActionLabel.Text = _cameraEnabled ? "إيقاف الكاميرا" : "تشغيل الكاميرا";
        CameraButton.BackgroundColor = _cameraEnabled ? Color.FromArgb("#24354B") : Color.FromArgb("#702B3A");
        CameraActionLabel.TextColor = _cameraEnabled ? Color.FromArgb("#D1DCEA") : Color.FromArgb("#FFD4DB");
#else
        await Task.CompletedTask;
#endif
    }

    private async void SwitchCameraClicked(object? sender, EventArgs e)
    {
        var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>();
        if (engine is not null) await engine.SwitchCameraAsync();
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
#endif
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
#endif
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
        MuteActionLabel.Text = muted ? "إلغاء الكتم" : "كتم الصوت";
        MuteButton.BackgroundColor = muted ? Color.FromArgb("#702B3A") : Color.FromArgb("#24354B");
        MuteButton.Source = muted ? "call_mic_off_white.png" : "call_mic_white.png";
        MuteActionLabel.TextColor = muted ? Color.FromArgb("#FFD4DB") : Color.FromArgb("#D1DCEA");
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
            RemoteAudioButton.Source = enabled ? "call_speaker_white.png" : "call_speaker_off_white.png";
            RemoteAudioActionLabel.Text = enabled ? "كتم الوارد" : "تشغيل الوارد";
            RemoteAudioButton.BackgroundColor = enabled ? Color.FromArgb("#17563E") : Color.FromArgb("#24354B");
            RemoteAudioActionLabel.TextColor = enabled ? Color.FromArgb("#B7F5D1") : Color.FromArgb("#D1DCEA");
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
        SpeakerButton.Source = enabled ? "call_speaker_white.png" : "call_speaker_off_white.png";
        SpeakerActionLabel.Text = enabled ? "إيقاف المكبر" : "مكبر الصوت";
        SpeakerButton.BackgroundColor = enabled ? Color.FromArgb("#17563E") : Color.FromArgb("#24354B");
        SpeakerActionLabel.TextColor = enabled ? Color.FromArgb("#B7F5D1") : Color.FromArgb("#D1DCEA");
        StatusLabel.Text = enabled ? "مكبر الصوت مفعّل" : "مكبر الصوت متوقف";
    }
}
