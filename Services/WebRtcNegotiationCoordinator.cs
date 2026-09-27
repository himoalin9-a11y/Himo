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

    public void Start(Guid conversationId, CallMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session?.ConversationId == conversationId && _session.Mode == mode) return;
        Stop();
        _session = new WebRtcSession(_calls, conversationId, mode);
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
        if (_calls.Current is null || _calls.Current.ConversationId != state.ConversationId)
        {
            Stop();
            return;
        }

        Start(state.ConversationId, state.Mode);
    }

    private void OnCallEnded(object? sender, EventArgs e) => Stop();

    private async void OnIncomingSignal(object? sender, CallSignalMessage signal)
    {
        if (_session?.ConversationId != signal.ConversationId) return;

        try
        {
            if (signal.Type.Equals(CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // The caller creates SDP only after the callee has accepted.
                // This prevents the offer from arriving before the callee has
                // started its native PeerConnection.
                if (_webRtc.IsStarted)
                    await CreateOfferAsync();
                return;
            }

            if (signal.Type.Equals(CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(signal.Payload))
            {
                if (!_webRtc.IsStarted) return;
                await _webRtc.SetRemoteOfferAsync(signal.Payload);
                var answer = await _webRtc.CreateAnswerAsync();
                if (!string.IsNullOrWhiteSpace(answer))
                    await _session.SendAnswerAsync(answer);
                return;
            }

            if (signal.Type.Equals(CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(signal.Payload))
            {
                await _webRtc.SetRemoteAnswerAsync(signal.Payload);
                return;
            }

            if (signal.Type.Equals(CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(signal.Payload))
            {
                if (_webRtc is Platforms.Android.Services.AndroidWebRtcMediaEngine androidWebRtc)
                    await androidWebRtc.AddRemoteIceCandidateAsync(signal.Payload);
                return;
            }

            if (signal.Type.Equals(CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase) ||
                signal.Type.Equals(CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Stop();
            }
        }
        catch
        {
            // Signaling callbacks must not tear down the realtime event loop.
        }
    }

    private async void OnIceCandidateGenerated(object? sender, string value)
    {
        if (_session is null || string.IsNullOrWhiteSpace(value)) return;
        try { await _session.SendIceCandidateAsync(value); } catch { }
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
        IceConnectionStateChanged?.Invoke(this, value);
    }

    private void OnConnectionEstablishedChanged(object? sender, bool established)
    {
        if (_session is null) return;
        if (_lastConnectionEstablished == established) return;
        _lastConnectionEstablished = established;
        ConnectionEstablishedChanged?.Invoke(this, established);
    }

    private void OnPeerConnectionStateChanged(object? sender, string value)
    {
        if (_session is null || string.IsNullOrWhiteSpace(value)) return;
        if (string.Equals(_lastPeerConnectionState, value, StringComparison.OrdinalIgnoreCase)) return;
        _lastPeerConnectionState = value;
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
