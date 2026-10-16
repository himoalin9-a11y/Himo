namespace Himo.Services;

/// <summary>
/// Stage 9: deterministic signaling coordinator.
/// Native WebRTC callbacks are kept behind IWebRtcMediaEngine; this class only
/// coordinates Offer/Answer/ICE without depending on generated Android types.
/// </summary>
public sealed class WebRtcNegotiationCoordinator : IDisposable
{
    private readonly ICallService _calls;
    private readonly IWebRtcMediaEngine _webRtc;
    private WebRtcSession? _session;
    private bool _disposed;
    private bool _lastConnectionEstablished;

    public bool IsActive => _session is not null;
    public string? RemoteOfferSdp => _session?.RemoteOfferSdp;
    public string? RemoteAnswerSdp => _session?.RemoteAnswerSdp;
    public int PendingIceCandidates => _session?.PendingIceCandidates ?? 0;
    public string? LastIceConnectionState => _lastIceConnectionState;
    public string? LastPeerConnectionState => _lastPeerConnectionState;
    public bool IsIceConnected => string.Equals(_lastIceConnectionState, "Connected", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(_lastIceConnectionState, "Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsPeerConnectionConnected => string.Equals(_lastPeerConnectionState, "Connected", StringComparison.OrdinalIgnoreCase);
    public bool IsConnectionEstablished => _webRtc.IsConnectionEstablished;

    public event EventHandler<string>? RemoteOfferReceived;
    public event EventHandler<string>? RemoteAnswerReceived;
    public event EventHandler<string>? IceCandidateReceived;
    public event EventHandler<string>? IceConnectionStateChanged;
    public event EventHandler<string>? PeerConnectionStateChanged;
    public event EventHandler<bool>? ConnectionEstablishedChanged;

    public WebRtcNegotiationCoordinator(ICallService calls, IWebRtcMediaEngine webRtc)
    {
        _calls = calls;
        _webRtc = webRtc;
        _calls.StateChanged += OnCallStateChanged;
        _calls.IncomingSignal += OnIncomingSignal;
        _calls.CallEnded += OnCallEnded;
        if (_webRtc is Platforms.Android.Services.AndroidWebRtcMediaEngine androidWebRtc)
        {
            androidWebRtc.IceCandidateGenerated += OnIceCandidateGenerated;
            androidWebRtc.IceConnectionStateChanged += OnIceConnectionStateChanged;
            androidWebRtc.PeerConnectionStateChanged += OnPeerConnectionStateChanged;
            androidWebRtc.ConnectionEstablishedChanged += OnConnectionEstablishedChanged;
        }
    }

    public void Start(Guid conversationId, CallMode mode, Guid callId = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session?.ConversationId == conversationId &&
            _session.Mode == mode &&
            (callId == Guid.Empty || _session.CallId == callId))
        {
            return;
        }

        Stop();
        _session = new WebRtcSession(_calls, conversationId, mode, callId);
        _session.RemoteOfferReceived += ForwardOffer;
        _session.RemoteAnswerReceived += ForwardAnswer;
        _session.IceCandidateReceived += ForwardIce;
    }

    public async Task<string?> CreateOfferAsync(CancellationToken cancellationToken = default)
    {
        EnsureSession();
        var offer = await _webRtc.CreateOfferAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(offer))
            await _session!.SendOfferAsync(offer, cancellationToken);
        return offer;
    }

    public Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureSession();
        return _session!.SendOfferAsync(sdp, cancellationToken);
    }

    public Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        EnsureSession();
        return _session!.SendAnswerAsync(sdp, cancellationToken);
    }

    public Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default)
    {
        EnsureSession();
        return _session!.SendIceCandidateAsync(candidate, cancellationToken);
    }

    private void OnCallStateChanged(object? sender, CallState state)
    {
        if (state.ConversationId == Guid.Empty) return;
        var current = _calls.Current;
        if (current is null ||
            current.ConversationId != state.ConversationId ||
            current.CallId != state.CallId)
        {
            Stop();
            return;
        }

        Start(state.ConversationId, state.Mode, state.CallId);
    }

    private void OnCallEnded(object? sender, EventArgs e) => Stop();

    private async void OnIncomingSignal(object? sender, CallSignalMessage signal)
    {
        var current = _calls.Current;
        if (current is null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Ignore signal: no active call; type={signal.Type}; " +
                $"conversation={signal.ConversationId:D}; payloadLength={signal.Payload?.Length ?? 0}");
            return;
        }

        if (current.ConversationId != signal.ConversationId)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Ignore signal: conversation mismatch; type={signal.Type}; " +
                $"activeConversation={current.ConversationId:D}; receivedConversation={signal.ConversationId:D}; " +
                $"activeCallId={current.CallId:D}");
            return;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Signal delivered to coordinator; type={signal.Type}; " +
            $"conversation={signal.ConversationId:D}; activeCallId={current.CallId:D}; " +
            $"payloadLength={signal.Payload?.Length ?? 0}; engineStarted={_webRtc.IsStarted}");

        // CallService validates the envelope's CallId, but intentionally keeps the
        // original message intact for other subscribers. Unwrap it here before
        // handing SDP or ICE to the native WebRTC engine. Passing the JSON envelope
        // itself to SetRemoteDescription/AddIceCandidate prevents peer negotiation.
        var payload = signal.Payload;
        if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope))
        {
            if (envelope.CallId != Guid.Empty && envelope.CallId != current.CallId)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Drop {signal.Type}: callId mismatch; " +
                    $"current={current.CallId:D}; received={envelope.CallId:D}");
                return;
            }

            if (envelope.Mode.HasValue && envelope.Mode.Value != current.Mode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Drop {signal.Type}: mode mismatch; " +
                    $"current={current.Mode}; received={envelope.Mode.Value}");
                return;
            }

            payload = envelope.Data;
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Unwrapped {signal.Type}; callId={current.CallId:D}; " +
                $"dataLength={payload?.Length ?? 0}");
        }

        if (_session is null ||
            _session.ConversationId != signal.ConversationId ||
            _session.Mode != current.Mode ||
            (_session.CallId != Guid.Empty && _session.CallId != current.CallId))
        {
            Start(signal.ConversationId, current.Mode, current.CallId);
        }

        try
        {
            if (signal.Type.Equals(CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // The caller creates SDP only after the callee has accepted.
                // This prevents the offer from arriving before the callee has
                // started its native PeerConnection.
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Processing Accept; callId={current.CallId:D}; " +
                    $"engineStarted={_webRtc.IsStarted}; sessionActive={_session is not null}");

                if (_webRtc.IsStarted)
                {
                    var offer = await CreateOfferAsync();
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] Offer creation/send path finished; callId={current.CallId:D}; " +
                        $"sdpLength={offer?.Length ?? 0}; sessionActive={_session is not null}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] ERROR: Accept received while local PeerConnection is not started; callId={current.CallId:D}");
                }
                return;
            }

            if (signal.Type.Equals(CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(payload))
                    throw new InvalidOperationException("Received Offer has an empty SDP payload.");
                if (!payload.Contains("v=0", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Received Offer payload is not SDP after envelope parsing (length={payload.Length}).");
                if (!_webRtc.IsStarted)
                    throw new InvalidOperationException("Received Offer before local PeerConnection was started.");

                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Applying remote Offer; sdpLength={payload.Length}; callId={current.CallId:D}");
                await _webRtc.SetRemoteOfferAsync(payload);
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Remote Offer applied; sdpLength={payload.Length}; callId={current.CallId:D}");

                var answer = await _webRtc.CreateAnswerAsync();
                var session = _session;
                if (session is not null && !string.IsNullOrWhiteSpace(answer))
                {
                    await session.SendAnswerAsync(answer);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] Answer sent; sdpLength={answer.Length}; callId={current.CallId:D}");
                }
                else
                {
                    throw new InvalidOperationException("WebRTC did not create an Answer SDP.");
                }
                return;
            }

            if (signal.Type.Equals(CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(payload))
                    throw new InvalidOperationException("Received Answer has an empty SDP payload.");
                if (!payload.Contains("v=0", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Received Answer payload is not SDP after envelope parsing (length={payload.Length}).");

                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Applying remote Answer; sdpLength={payload.Length}; callId={current.CallId:D}");
                await _webRtc.SetRemoteAnswerAsync(payload);
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Remote Answer applied; sdpLength={payload.Length}; callId={current.CallId:D}");
                return;
            }

            if (signal.Type.Equals(CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Drop empty ICE candidate; callId={current.CallId:D}");
                    return;
                }

                if (_webRtc is Platforms.Android.Services.AndroidWebRtcMediaEngine androidWebRtc)
                {
                    await androidWebRtc.AddRemoteIceCandidateAsync(payload);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] Remote ICE candidate processed; payloadLength={payload.Length}; callId={current.CallId:D}");
                }
                return;
            }

            if (signal.Type.Equals(CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase) ||
                signal.Type.Equals(CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Stop();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Negotiation signal failed; type={signal.Type}; " +
                $"conversation={signal.ConversationId:D}; callId={current.CallId:D}; " +
                $"payloadLength={payload?.Length ?? 0}; error={ex}");
        }
    }

    private async void OnIceCandidateGenerated(object? sender, string value)
    {
        var session = _session;
        if (session is null || string.IsNullOrWhiteSpace(value))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local ICE candidate not sent: " +
                $"sessionActive={session is not null}; emptyPayload={string.IsNullOrWhiteSpace(value)}");
            return;
        }

        try
        {
            await session.SendIceCandidateAsync(value);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local ICE candidate signaling completed; " +
                $"conversation={session.ConversationId:D}; callId={session.CallId:D}; " +
                $"payloadLength={value.Length}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local ICE candidate signaling FAILED; " +
                $"conversation={session.ConversationId:D}; callId={session.CallId:D}; " +
                $"payloadLength={value.Length}; error={ex}");
        }
    }

    private void ForwardOffer(object? sender, string value) => RemoteOfferReceived?.Invoke(this, value);
    private void ForwardAnswer(object? sender, string value) => RemoteAnswerReceived?.Invoke(this, value);
    private void ForwardIce(object? sender, string value) => IceCandidateReceived?.Invoke(this, value);

    private string? _lastIceConnectionState;
    private string? _lastPeerConnectionState;

    private void OnIceConnectionStateChanged(object? sender, string value)
    {
        if (_session is null || string.IsNullOrWhiteSpace(value)) return;
        if (string.Equals(_lastIceConnectionState, value, StringComparison.OrdinalIgnoreCase)) return;
        _lastIceConnectionState = value;
        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Coordinator ICE state; state={value}; " +
            $"conversation={_session.ConversationId:D}; callId={_session.CallId:D}");
        IceConnectionStateChanged?.Invoke(this, value);
    }

    private void OnConnectionEstablishedChanged(object? sender, bool established)
    {
        if (_session is null) return;
        if (_lastConnectionEstablished == established) return;
        _lastConnectionEstablished = established;
        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Media connection established={established}; " +
            $"conversation={_session.ConversationId:D}; callId={_session.CallId:D}; " +
            $"ice={_lastIceConnectionState ?? "unknown"}; peer={_lastPeerConnectionState ?? "unknown"}");
        ConnectionEstablishedChanged?.Invoke(this, established);
    }

    private void OnPeerConnectionStateChanged(object? sender, string value)
    {
        if (_session is null || string.IsNullOrWhiteSpace(value)) return;
        if (string.Equals(_lastPeerConnectionState, value, StringComparison.OrdinalIgnoreCase)) return;
        _lastPeerConnectionState = value;
        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Coordinator PeerConnection state; state={value}; " +
            $"conversation={_session.ConversationId:D}; callId={_session.CallId:D}");
        PeerConnectionStateChanged?.Invoke(this, value);
    }

    private void EnsureSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session is null) throw new InvalidOperationException("WebRTC negotiation has not started.");
    }

    public void Stop()
    {
        _lastConnectionEstablished = false;
        if (_session is null) return;
        _session.RemoteOfferReceived -= ForwardOffer;
        _session.RemoteAnswerReceived -= ForwardAnswer;
        _session.IceCandidateReceived -= ForwardIce;
        _session.Dispose();
        _session = null;
        _lastIceConnectionState = null;
        _lastPeerConnectionState = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _calls.StateChanged -= OnCallStateChanged;
        _calls.IncomingSignal -= OnIncomingSignal;
        _calls.CallEnded -= OnCallEnded;
        if (_webRtc is Platforms.Android.Services.AndroidWebRtcMediaEngine androidWebRtc)
        {
            androidWebRtc.IceCandidateGenerated -= OnIceCandidateGenerated;
            androidWebRtc.IceConnectionStateChanged -= OnIceConnectionStateChanged;
            androidWebRtc.PeerConnectionStateChanged -= OnPeerConnectionStateChanged;
            androidWebRtc.ConnectionEstablishedChanged -= OnConnectionEstablishedChanged;
        }
        Stop();
    }
}
