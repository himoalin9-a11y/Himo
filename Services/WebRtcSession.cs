
namespace Himo.Services;

/// <summary>
/// Stage 4: WebRTC negotiation state is isolated from the UI and call lifecycle.
/// The native media engine is intentionally plugged in at the next stage.
/// </summary>
public sealed class WebRtcSession : IDisposable
{
    private readonly ICallService _calls;
    private readonly Queue<string> _pendingIceCandidates = new();
    private bool _disposed;

    public Guid ConversationId { get; }
    public CallMode Mode { get; }
    public string? RemoteOfferSdp { get; private set; }
    public string? RemoteAnswerSdp { get; private set; }
    public bool HasRemoteOffer => !string.IsNullOrWhiteSpace(RemoteOfferSdp);
    public bool HasRemoteAnswer => !string.IsNullOrWhiteSpace(RemoteAnswerSdp);
    public int PendingIceCandidates => _pendingIceCandidates.Count;

    public event EventHandler<string>? RemoteOfferReceived;
    public event EventHandler<string>? RemoteAnswerReceived;
    public event EventHandler<string>? IceCandidateReceived;

    public WebRtcSession(ICallService calls, Guid conversationId, CallMode mode)
    {
        _calls = calls;
        ConversationId = conversationId;
        Mode = mode;
        _calls.IncomingSignal += OnSignal;
    }

    public async Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        ValidateSdp(sdp);
        await _calls.SendOfferAsync(sdp, cancellationToken);
    }

    public async Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        ValidateSdp(sdp);
        await _calls.SendAnswerAsync(sdp, cancellationToken);
    }

    public async Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(candidate)) return;
        await _calls.SendIceCandidateAsync(candidate, cancellationToken);
    }

    public IReadOnlyList<string> DrainPendingIceCandidates()
    {
        var result = _pendingIceCandidates.ToArray();
        _pendingIceCandidates.Clear();
        return result;
    }

    private void OnSignal(object? sender, CallSignalMessage signal)
    {
        if (_disposed || signal.ConversationId != ConversationId) return;

        if (string.Equals(signal.Type, CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(signal.Payload)) return;
            RemoteOfferSdp = signal.Payload;
            RemoteOfferReceived?.Invoke(this, signal.Payload);
            return;
        }

        if (string.Equals(signal.Type, CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(signal.Payload)) return;
            RemoteAnswerSdp = signal.Payload;
            RemoteAnswerReceived?.Invoke(this, signal.Payload);
            return;
        }

        if (string.Equals(signal.Type, CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(signal.Payload)) return;
            _pendingIceCandidates.Enqueue(signal.Payload);
            IceCandidateReceived?.Invoke(this, signal.Payload);
        }
    }

    private static void ValidateSdp(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
            throw new ArgumentException("SDP is required.", nameof(sdp));

        if (!sdp.Contains("v=0", StringComparison.Ordinal))
            throw new ArgumentException("Invalid SDP payload.", nameof(sdp));
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _calls.IncomingSignal -= OnSignal;
        _pendingIceCandidates.Clear();
    }
}
