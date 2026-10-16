namespace Himo.Services;

/// <summary>
/// Stable boundary for the native WebRTC media implementation.
/// The interface intentionally contains no platform-specific types.
/// </summary>
public interface IWebRtcMediaEngine : IAsyncDisposable
{
    bool IsStarted { get; }
    CallMode? Mode { get; }
    bool IsConnectionEstablished { get; }
    event EventHandler<bool>? ConnectionEstablishedChanged;

    Task StartAsync(CallMode mode, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsMicrophoneEnabled { get; }
    bool IsSpeakerEnabled { get; }
    bool IsRemoteAudioEnabled { get; }
    Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SetSpeakerEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SetRemoteAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    bool IsCameraEnabled { get; }
    Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SwitchCameraAsync(CancellationToken cancellationToken = default);
    Task<string?> CreateOfferAsync(CancellationToken cancellationToken = default);
    Task<string?> CreateAnswerAsync(CancellationToken cancellationToken = default);
    Task SetRemoteOfferAsync(string sdp, CancellationToken cancellationToken = default);
    Task SetRemoteAnswerAsync(string sdp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe fallback until the native Android WebRTC engine is device-validated.
/// This keeps the app buildable without pretending that media transport is active.
/// </summary>
public sealed class NoOpWebRtcMediaEngine : IWebRtcMediaEngine
{
    public event EventHandler<bool>? ConnectionEstablishedChanged
    {
        add { }
        remove { }
    }
    public bool IsStarted { get; private set; }
    public CallMode? Mode { get; private set; }
    public bool IsConnectionEstablished => false;
    public bool IsCameraEnabled { get; private set; }
    public bool IsMicrophoneEnabled { get; private set; }
    public bool IsSpeakerEnabled { get; private set; }
    public bool IsRemoteAudioEnabled { get; private set; }

    public Task StartAsync(CallMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Mode = mode;
        IsStarted = true;
        IsMicrophoneEnabled = true;
        IsSpeakerEnabled = false;
        IsRemoteAudioEnabled = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsStarted = false;
        Mode = null;
        IsMicrophoneEnabled = false;
        IsSpeakerEnabled = false;
        IsRemoteAudioEnabled = false;
        return Task.CompletedTask;
    }

    public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsMicrophoneEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetSpeakerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSpeakerEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetRemoteAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRemoteAudioEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsCameraEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SwitchCameraAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string?> CreateOfferAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public Task<string?> CreateAnswerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public Task SetRemoteOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetRemoteAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsStarted = false;
        Mode = null;
        IsCameraEnabled = false;
        IsMicrophoneEnabled = false;
        IsSpeakerEnabled = false;
        return ValueTask.CompletedTask;
    }
}
