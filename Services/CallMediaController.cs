namespace Himo.Services;

/// <summary>
/// Stage 5: isolates Android call-audio routing from signaling and UI.
/// The WebRTC transport remains behind WebRtcSession and is not coupled to Android APIs.
/// </summary>
public interface ICallMediaController
{
    bool IsSpeakerEnabled { get; }
    Task StartAsync(CallMode mode, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default);
    Task SetSpeakerAsync(bool enabled, CancellationToken cancellationToken = default);
}

public sealed class NoOpCallMediaController : ICallMediaController
{
    public bool IsSpeakerEnabled { get; private set; }
    public Task StartAsync(CallMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetSpeakerAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSpeakerEnabled = enabled;
        return Task.CompletedTask;
    }
}
