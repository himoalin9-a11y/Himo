
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
    public Guid CallId { get; }
    public string? RemoteOfferSdp { get; private set; }
    public string? RemoteAnswerSdp { get; private set; }
    public bool HasRemoteOffer => !string.IsNullOrWhiteSpace(RemoteOfferSdp);
    public bool HasRemoteAnswer => !string.IsNullOrWhiteSpace(RemoteAnswerSdp);
    public int PendingIceCandidates => _pendingIceCandidates.Count;

    public event EventHandler<string>? RemoteOfferReceived;
    public event EventHandler<string>? RemoteAnswerReceived;
    public event EventHandler<string>? IceCandidateReceived;

    public WebRtcSession(ICallService calls, Guid conversationId, CallMode mode, Guid callId = default)
    {
        _calls = calls;
        ConversationId = conversationId;
        Mode = mode;
        CallId = callId;
        _calls.IncomingSignal += OnSignal;
    }

    public async Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
        ValidateSdp(sdp);
        await _calls.SendOfferAsync(sdp, cancellationToken);
    }

    public async Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
        ValidateSdp(sdp);
        await _calls.SendAnswerAsync(sdp, cancellationToken);
    }

    public async Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
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

        var payload = signal.Payload;
        if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope))
        {
            if (CallId != Guid.Empty && envelope.CallId != Guid.Empty && envelope.CallId != CallId)
                return;
            payload = envelope.Data;
        }

        if (string.Equals(signal.Type, CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            RemoteOfferSdp = payload;
            RemoteOfferReceived?.Invoke(this, payload);
            return;
        }

        if (string.Equals(signal.Type, CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            RemoteAnswerSdp = payload;
            RemoteAnswerReceived?.Invoke(this, payload);
            return;
        }

        if (string.Equals(signal.Type, CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            _pendingIceCandidates.Enqueue(payload);
            IceCandidateReceived?.Invoke(this, payload);
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

    private void EnsureMatchingActiveCall()
    {
        var current = _calls.Current;
        if (current is null || current.ConversationId != ConversationId || current.Mode != Mode ||
            (CallId != Guid.Empty && current.CallId != CallId))
        {
            throw new InvalidOperationException(
                $"WebRTC session is stale; expected conversation={ConversationId:D}, callId={CallId:D}.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _calls.IncomingSignal -= OnSignal;
        _pendingIceCandidates.Clear();
    }
}
