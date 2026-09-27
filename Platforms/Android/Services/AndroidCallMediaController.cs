using global::Android.Content;
using global::Android.Media;
using Himo.Services;

namespace Himo.Platforms.Android.Services;

/// <summary>
/// Android call-audio routing foundation.
/// WebRTC RTP/media transport remains separate from Android audio routing.
/// </summary>
public sealed class AndroidCallMediaController : ICallMediaController, IDisposable
{
    private readonly AudioManager _audioManager;
    private bool _started;
    public bool IsSpeakerEnabled { get; private set; }

    public AndroidCallMediaController()
    {
        var context = global::Android.App.Application.Context;
        _audioManager = (AudioManager?)context.GetSystemService(Context.AudioService)
            ?? throw new InvalidOperationException("Android AudioManager is unavailable.");
    }

    public Task StartAsync(CallMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _audioManager.Mode = Mode.InCommunication;
        var speaker = mode == CallMode.Video;
        SetSpeakerRoute(speaker);
        IsSpeakerEnabled = speaker;
        _audioManager.MicrophoneMute = false;
        _started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started) return Task.CompletedTask;

        _audioManager.MicrophoneMute = false;
        SetSpeakerRoute(false);
        IsSpeakerEnabled = false;
        _audioManager.Mode = Mode.Normal;
        _started = false;
        return Task.CompletedTask;
    }

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _audioManager.MicrophoneMute = muted;
        return Task.CompletedTask;
    }

    public Task SetSpeakerAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetSpeakerRoute(enabled);
        IsSpeakerEnabled = enabled;
        return Task.CompletedTask;
    }

    private void SetSpeakerRoute(bool enabled)
    {
        // Android 12+ provides the non-deprecated communication-device API.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            if (enabled)
            {
                foreach (var device in _audioManager.AvailableCommunicationDevices)
                {
                    if (device.Type == AudioDeviceType.BuiltinSpeaker)
                    {
                        _audioManager.SetCommunicationDevice(device);
                        return;
                    }
                }
            }
            else
            {
                _audioManager.ClearCommunicationDevice();
                return;
            }
        }

#pragma warning disable CA1422 // Required fallback for Android API < 31.
        _audioManager.SpeakerphoneOn = enabled;
#pragma warning restore CA1422
    }

    public void Dispose()
    {
        if (_started)
        {
            _audioManager.MicrophoneMute = false;
            SetSpeakerRoute(false);
            IsSpeakerEnabled = false;
            _audioManager.Mode = Mode.Normal;
            _started = false;
        }

        _audioManager.Dispose();
    }
}
