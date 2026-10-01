using Himo.Services;
#if ANDROID
using Himo.Platforms.Android.Services;
#endif

namespace Himo.Views;

[QueryProperty(nameof(ConversationId), "id")]
[QueryProperty(nameof(Mode), "mode")]
[QueryProperty(nameof(Incoming), "incoming")]
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
    private bool _navigationStarted;
#if ANDROID
    private global::Android.Media.MediaPlayer? _outgoingRingback;
#endif

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
#endif
                _connectedAt = DateTimeOffset.UtcNow;
                StartTimer();
            }
            StatusLabel.Text = state.IsConnected ? FormatDuration() : "بانتظار الطرف الآخر";
            VideoSurface.IsVisible = _mode == CallMode.Video;
        _cameraEnabled = _mode == CallMode.Video;
        CameraButton.IsEnabled = true;
        CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.55;
#if ANDROID
        if (_mode == CallMode.Video) _ = AttachVideoAsync();
#endif
            RemoteAudioButton.Source = state.IsRemoteAudioEnabled ? "himo_phase1_icon_volume.png" : "himo_phase1_icon_volume_off.png";
            RemoteAudioButton.IsEnabled = state.IsConnected && !_remoteAudioToggleInProgress;
            MuteButton.Source = state.IsMuted ? "himo_phase1_icon_mic_off.png" : "himo_phase1_icon_mic.png";
            SpeakerButton.Source = state.IsSpeakerOn ? "himo_phase1_icon_volume.png" : "himo_phase1_icon_volume_off.png";
        });
    }

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _callUiStateVersion);
        _calls.StateChanged -= CallsStateChanged;
        _calls.CallEnded -= CallsEnded;
        StopTimer();
#if ANDROID
        StopOutgoingRingback();
#endif
        base.OnDisappearing();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _connectedAt = null;
        _navigationStarted = false;
        VideoSurface.IsVisible = _mode == CallMode.Video;
        _cameraEnabled = _mode == CallMode.Video;
        CameraButton.IsEnabled = true;
        CameraButton.Opacity = _mode == CallMode.Video ? 1.0 : 0.55;
#if ANDROID
        if (_mode == CallMode.Video) _ = AttachVideoAsync();
#endif
        ModeLabel.Text = _mode == CallMode.Video ? "مكالمة فيديو" : "مكالمة صوتية";
        StatusLabel.Text = _incoming ? "مكالمة واردة..." : "جاري تجهيز الاتصال...";
        RemoteAudioButton.IsEnabled = false;

        try
        {
            // ChatPage can stop SignalR while navigation to this page is still in progress.
            // Wait for a fully connected signaling channel before touching WebRTC.
            await _realtime.EnsureConnectedAsync();

            var granted = await _calls.RequestPermissionsAsync(_mode);
            if (!granted)
            {
                StatusLabel.Text = "يلزم السماح بالكاميرا والميكروفون";
                return;
            }

            if (_incoming)
            {
                // The Invite already created the pending CallState. Accepting here
                // starts local media and sends Accept; the caller then creates SDP.
                await _calls.AcceptAsync();
                StatusLabel.Text = "جاري الاتصال...";
            }
            else
            {
                await _calls.StartAsync(new CallRequest(_conversationId, _mode));
#if ANDROID
                StartOutgoingRingback();
#endif
                StatusLabel.Text = "بانتظار قبول المكالمة...";
            }
        }
        catch (Exception ex)
        {
#if ANDROID
            StopOutgoingRingback();
#endif
            StatusLabel.Text = "تعذر تجهيز المكالمة الصوتية. حاول مرة أخرى.";
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Start failed: {ex}");
        }
    }

    private async void CallsEnded(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _callUiStateVersion);
        _remoteAudioToggleInProgress = false;
        StopTimer();
#if ANDROID
        StopOutgoingRingback();
#endif
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "انتهت المكالمة";
            RemoteAudioButton.IsEnabled = false;
            RemoteAudioButton.Source = "himo_phase1_icon_volume_off.png";
        });

        if (_navigationStarted) return;
        _navigationStarted = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(".."));
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
                StatusLabel.Text = FormatDuration();
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
        if (_incoming) return;

        try
        {
            StopOutgoingRingback();
            var context = global::Android.App.Application.Context;
            var uri = global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone);
            if (uri is null) return;

            _outgoingRingback = global::Android.Media.MediaPlayer.Create(context, uri);
            if (_outgoingRingback is null) return;

            var audioAttributesBuilder = new global::Android.Media.AudioAttributes.Builder();
            if (audioAttributesBuilder is null) return;

            audioAttributesBuilder.SetUsage(global::Android.Media.AudioUsageKind.NotificationRingtone);
            audioAttributesBuilder.SetContentType(global::Android.Media.AudioContentType.Sonification);

            var audioAttributes = audioAttributesBuilder.Build();
            if (audioAttributes is null) return;

            _outgoingRingback.SetAudioAttributes(audioAttributes);
            _outgoingRingback.Looping = true;
            _outgoingRingback.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] Outgoing ringback failed: {ex}");
            StopOutgoingRingback();
        }
    }

    private void StopOutgoingRingback()
    {
        try
        {
            if (_outgoingRingback is not null)
            {
                if (_outgoingRingback.IsPlaying) _outgoingRingback.Stop();
                _outgoingRingback.Reset();
                _outgoingRingback.Release();
            }
        }
        catch { }
        finally
        {
            _outgoingRingback?.Dispose();
            _outgoingRingback = null;
        }
    }

    private async Task AttachVideoAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_calls is not null)
                {
                    var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
                    if (engine is not null)
                    {
                        await engine.AttachRemoteVideoAsync(RemoteVideoHost);
                        await engine.AttachLocalVideoAsync(LocalVideoHost);
                    }
                }
            });
        }
        catch { }
    }
#endif

    private async void ToggleCameraClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (_mode != CallMode.Video) return;
        var engine = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<IWebRtcMediaEngine>() as AndroidWebRtcMediaEngine;
        if (engine is null) return;
        _cameraEnabled = !_cameraEnabled;
        await engine.SetCameraEnabledAsync(_cameraEnabled);
        CameraButton.Source = _cameraEnabled ? "himo_phase1_icon_camera.png" : "himo_phase1_icon_camera_off.png";
        StatusLabel.Text = _cameraEnabled ? "الكاميرا مفعّلة" : "الكاميرا متوقفة";
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
            await _calls.EndAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallPage] End failed: {ex}");
        }
        finally
        {
            await NavigateBackFromCallAsync();
        }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (_navigationStarted) return;
        try
        {
            if (_calls.Current is not null)
                await _calls.EndAsync();
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
            await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(".."));
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
            RemoteAudioButton.Source = enabled ? "himo_phase1_icon_volume.png" : "himo_phase1_icon_volume_off.png";
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
        SpeakerButton.Source = enabled ? "himo_phase1_icon_volume.png" : "himo_phase1_icon_volume_off.png";
        StatusLabel.Text = enabled ? "مكبر الصوت مفعّل" : "مكبر الصوت متوقف";
    }
}
