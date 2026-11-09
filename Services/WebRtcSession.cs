namespace Himo.Services;

/// <summary>
/// Signaling state for exactly one remote participant in a Mesh call.
/// SDP and ICE are routed to RemotePeerUserId; they are never broadcast to an
/// entire group conversation.
/// </summary>
public sealed class WebRtcSession : IDisposable
{
    private readonly ICallService _calls;
    private bool _disposed;

    public Guid ConversationId { get; }
    public CallMode Mode { get; }
    public Guid CallId { get; }
    public Guid RemotePeerUserId { get; }
    public Guid LocalUserId { get; }

    public WebRtcSession(
        ICallService calls,
        Guid conversationId,
        CallMode mode,
        Guid callId,
        Guid remotePeerUserId,
        Guid localUserId)
    {
        if (remotePeerUserId == Guid.Empty)
            throw new ArgumentException("A remote participant is required for a peer session.", nameof(remotePeerUserId));

        _calls = calls;
        ConversationId = conversationId;
        Mode = mode;
        CallId = callId;
        RemotePeerUserId = remotePeerUserId;
        LocalUserId = localUserId;
    }

    public Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
        ValidateSdp(sdp);
        return _calls.SendOfferAsync(RemotePeerUserId, sdp, cancellationToken);
    }

    public Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
        ValidateSdp(sdp);
        return _calls.SendAnswerAsync(RemotePeerUserId, sdp, cancellationToken);
    }

    public Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        EnsureMatchingActiveCall();
        if (string.IsNullOrWhiteSpace(candidate)) return Task.CompletedTask;
        return _calls.SendIceCandidateAsync(RemotePeerUserId, candidate, cancellationToken);
    }

    public bool IsSignalForPeer(CallSignalMessage signal)
    {
        if (_disposed || signal.ConversationId != ConversationId || signal.SenderUserId != RemotePeerUserId)
            return false;
        if (signal.TargetUserId.HasValue)
        {
            if (LocalUserId != Guid.Empty && signal.TargetUserId.Value != LocalUserId)
                return false;
        }

        if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope))
            return envelope.CallId == Guid.Empty || envelope.CallId == CallId;
        return true;
    }


    private static void ValidateSdp(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
            throw new ArgumentException("SDP is required.", nameof(sdp));
        if (!sdp.Contains("v=0", StringComparison.Ordinal))
            throw new ArgumentException("Invalid SDP payload.", nameof(sdp));
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void EnsureMatchingActiveCall()
    {
        var current = _calls.Current;
        if (current is null || current.ConversationId != ConversationId || current.Mode != Mode || current.CallId != CallId)
            throw new InvalidOperationException(
                $"WebRTC peer session is stale; expected conversation={ConversationId:D}, callId={CallId:D}, peer={RemotePeerUserId:D}.");
    }

    public void Dispose() => _disposed = true;
}
