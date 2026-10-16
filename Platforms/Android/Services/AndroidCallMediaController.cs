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
        // Select the communication route explicitly; clearing it alone may leave some OEMs on speaker.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var devices = _audioManager.AvailableCommunicationDevices;
            if (enabled)
            {
                foreach (var device in devices)
                {
                    if (string.Equals(device.Type.ToString(), "BuiltinSpeaker", StringComparison.OrdinalIgnoreCase))
                    {
                        _audioManager.SetCommunicationDevice(device);
                        return;
                    }
                }
            }
            else
            {
                // Keep a connected headset/Bluetooth device preferred over the phone earpiece.
                foreach (var device in devices)
                {
                    var type = device.Type.ToString();
                    if (type.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                        type.Contains("WiredHeadset", StringComparison.OrdinalIgnoreCase) ||
                        type.Contains("UsbHeadset", StringComparison.OrdinalIgnoreCase))
                    {
                        _audioManager.SetCommunicationDevice(device);
                        return;
                    }
                }
                foreach (var device in devices)
                {
                    if (string.Equals(device.Type.ToString(), "BuiltinEarpiece", StringComparison.OrdinalIgnoreCase))
                    {
                        _audioManager.SetCommunicationDevice(device);
                        return;
                    }
                }
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
