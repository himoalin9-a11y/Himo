using Himo.Platforms.Android.Services;
using Microsoft.Maui.Controls;

namespace Himo.Services;

/// <summary>
/// Coordinates one independent WebRTC PeerConnection per accepted group participant.
/// The lower user id is the deterministic offerer for each pair, preventing SDP glare.
/// </summary>
public sealed class WebRtcNegotiationCoordinator : IDisposable
{
    private sealed class PeerContext
    {
        public required Guid UserId { get; init; }
        public required AndroidWebRtcMediaEngine Engine { get; init; }
        public required WebRtcSession Session { get; init; }
        public readonly SemaphoreSlim SignalGate = new(1, 1);
        public bool OfferStarted;
        public EventHandler<string>? IceHandler;
        public EventHandler<bool>? ConnectionHandler;
        public EventHandler<string>? IceStateHandler;
        public EventHandler<string>? PeerStateHandler;
    }

    private readonly ICallService _calls;
    private readonly IWebRtcMediaEngine _webRtc;
    private readonly AndroidWebRtcMediaEngine? _rootEngine;
    private readonly AccountService _account;
    private readonly SemaphoreSlim _peerGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, PeerContext> _peers = new();
    private readonly HashSet<Guid> _acceptedRemotePeers = new();
    private readonly Dictionary<Guid, List<CallSignalMessage>> _pendingMediaSignals = new();
    private CallState? _activeCall;
    private Guid _rootPeerUserId;
    private bool _disposed;

    public bool IsActive => _activeCall is not null;
    public bool IsConnectionEstablished => _webRtc.IsConnectionEstablished;
    public IReadOnlyList<Guid> RemotePeerUserIds
    {
        get { lock (_sync) return _peers.Keys.ToArray(); }
    }

    public event EventHandler? PeerConnectionsChanged;
    public event EventHandler<bool>? ConnectionEstablishedChanged;
    public event EventHandler<(Guid PeerUserId, string State)>? PeerStateChanged;

    public WebRtcNegotiationCoordinator(ICallService calls, IWebRtcMediaEngine webRtc, AccountService account)
    {
        _calls = calls;
        _webRtc = webRtc;
        _rootEngine = webRtc as AndroidWebRtcMediaEngine;
        _account = account;
        _calls.StateChanged += OnCallStateChanged;
        _calls.IncomingSignal += OnIncomingSignal;
        _calls.LocalAccepted += OnLocalAccepted;
        _calls.CallEnded += OnCallEnded;
    }

    private void OnCallStateChanged(object? sender, CallState state)
    {
        if (_calls.Current is not { } current || current.CallId != state.CallId || current.ConversationId != state.ConversationId)
        {
            Stop();
            return;
        }

        var previous = _activeCall;
        if (previous is not null && (previous.CallId != state.CallId || previous.ConversationId != state.ConversationId))
            Stop();

        _activeCall = state;
        if (state.InitiatorUserId != Guid.Empty && state.InitiatorUserId != LocalUserId)
        {
            lock (_sync) _acceptedRemotePeers.Add(state.InitiatorUserId);
        }
    }

    private async void OnLocalAccepted(object? sender, CallState state)
    {
        var liveCall = _calls.Current;
        if (liveCall is null || liveCall.CallId != state.CallId || liveCall.ConversationId != state.ConversationId)
            return;
        _activeCall = state;
        var peers = new HashSet<Guid>();
        lock (_sync)
        {
            if (state.InitiatorUserId != Guid.Empty && state.InitiatorUserId != LocalUserId)
                _acceptedRemotePeers.Add(state.InitiatorUserId);
            peers.UnionWith(_acceptedRemotePeers);
        }

        foreach (var peerId in peers)
        {
            try
            {
                await EnsurePeerAndNegotiateAsync(peerId).ConfigureAwait(false);
                List<CallSignalMessage>? pending = null;
                lock (_sync)
                {
                    if (_pendingMediaSignals.Remove(peerId, out var queued))
                        pending = queued;
                }

                if (pending is null) continue;
                var peer = await EnsurePeerAsync(peerId).ConfigureAwait(false);
                if (peer is null)
                {
                    lock (_sync) _pendingMediaSignals[peerId] = pending;
                    continue;
                }

                foreach (var signal in pending)
                    await ProcessMediaSignalAsync(peer, signal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Deferred group signaling failed; peer={peerId:D}; error={ex}");
            }
        }
    }

    private async void OnIncomingSignal(object? sender, CallSignalMessage signal)
    {
        try
        {
            var current = _calls.Current;
            if (current is null || current.ConversationId != signal.ConversationId)
                return;
            if (signal.TargetUserId.HasValue && LocalUserId != Guid.Empty && signal.TargetUserId.Value != LocalUserId)
                return;
            if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope))
            {
                if (envelope.CallId != Guid.Empty && envelope.CallId != current.CallId)
                    return;
                if (envelope.Mode.HasValue && envelope.Mode.Value != current.Mode)
                    return;
            }

            var type = signal.Type;
            if (type.Equals(CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (signal.SenderUserId == Guid.Empty || signal.SenderUserId == LocalUserId)
                    return;
                lock (_sync) _acceptedRemotePeers.Add(signal.SenderUserId);
                await EnsurePeerAndNegotiateAsync(signal.SenderUserId).ConfigureAwait(false);
                return;
            }

            if (type.Equals(CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase) ||
                type.Equals(CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase) ||
                type.Equals(CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (signal.SenderUserId == Guid.Empty || signal.SenderUserId == LocalUserId)
                    return;

                // Accept is deliberately signaled before native media startup so all
                // members ring quickly. The caller may send SDP/ICE while this device
                // is still creating its PeerConnection; queue rather than dropping it.
                if (!_webRtc.IsStarted)
                {
                    QueuePendingMediaSignal(signal.SenderUserId, signal);
                    return;
                }

                var peer = await EnsurePeerAsync(signal.SenderUserId).ConfigureAwait(false);
                if (peer is null)
                {
                    QueuePendingMediaSignal(signal.SenderUserId, signal);
                    return;
                }

                await ProcessMediaSignalAsync(peer, signal).ConfigureAwait(false);
                return;
            }

            if (type.Equals(CallSignalType.Leave.ToString(), StringComparison.OrdinalIgnoreCase) ||
                type.Equals(CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase) ||
                (type.Equals(CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase) &&
                 current.IsGroup && signal.SenderUserId != current.InitiatorUserId))
            {
                await RemovePeerAsync(signal.SenderUserId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Group signaling handler failed; type={signal.Type}; sender={signal.SenderUserId:D}; error={ex}");
        }
    }

    private void QueuePendingMediaSignal(Guid remoteUserId, CallSignalMessage signal)
    {
        lock (_sync)
        {
            if (!_pendingMediaSignals.TryGetValue(remoteUserId, out var queued))
                _pendingMediaSignals[remoteUserId] = queued = new List<CallSignalMessage>();
            // A candidate burst is normal, but an unaccepted/slow participant must
            // not allow an unbounded queue to accumulate in memory.
            if (queued.Count < 128)
                queued.Add(signal);
        }
    }

    private async Task ProcessMediaSignalAsync(PeerContext peer, CallSignalMessage signal)
    {
        if (!IsPeerCurrent(peer) || _calls.Current?.CallId != peer.Session.CallId ||
            !peer.Session.IsSignalForPeer(signal)) return;
        var type = signal.Type;
        var payload = signal.Payload;
        if (CallSignalEnvelope.TryParse(signal.Payload, out var mediaEnvelope))
            payload = mediaEnvelope.Data;
        if (string.IsNullOrWhiteSpace(payload)) return;

        await peer.SignalGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsPeerCurrent(peer) || _calls.Current?.CallId != peer.Session.CallId)
                return;
            if (type.Equals(CallSignalType.Offer.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (!payload.Contains("v=0", StringComparison.Ordinal))
                    throw new InvalidOperationException("Received Offer payload is not SDP.");
                await peer.Engine.SetRemoteOfferAsync(payload).ConfigureAwait(false);
                var answer = await peer.Engine.CreateAnswerAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(answer))
                    await peer.Session.SendAnswerAsync(answer).ConfigureAwait(false);
            }
            else if (type.Equals(CallSignalType.Answer.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (!payload.Contains("v=0", StringComparison.Ordinal))
                    throw new InvalidOperationException("Received Answer payload is not SDP.");
                await peer.Engine.SetRemoteAnswerAsync(payload).ConfigureAwait(false);
            }
            else if (type.Equals(CallSignalType.IceCandidate.ToString(), StringComparison.OrdinalIgnoreCase) &&
                     peer.Engine is AndroidWebRtcMediaEngine androidPeer)
            {
                await androidPeer.AddRemoteIceCandidateAsync(payload).ConfigureAwait(false);
            }
        }
        finally
        {
            peer.SignalGate.Release();
        }
    }

    private async Task EnsurePeerAndNegotiateAsync(Guid remoteUserId)
    {
        var peer = await EnsurePeerAsync(remoteUserId).ConfigureAwait(false);
        if (peer is null || !ShouldCreateOffer(remoteUserId))
            return;

        await peer.SignalGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsPeerCurrent(peer) || _calls.Current?.CallId != peer.Session.CallId || peer.OfferStarted) return;
            peer.OfferStarted = true;
            var offer = await peer.Engine.CreateOfferAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(offer))
                throw new InvalidOperationException("WebRTC returned an empty Offer SDP.");
            await peer.Session.SendOfferAsync(offer).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Mesh Offer sent; peer={remoteUserId:D}; callId={_activeCall?.CallId:D}; sdpLength={offer.Length}.");
        }
        catch
        {
            peer.OfferStarted = false;
            throw;
        }
        finally
        {
            peer.SignalGate.Release();
        }
    }

    private bool ShouldCreateOffer(Guid remoteUserId)
    {
        var localId = LocalUserId;
        if (localId == Guid.Empty) return false;

        // The group initiator always offers to each participant. That makes cold-start
        // notification joins reliable even when SignalR's original Invite (which carries
        // InitiatorUserId) was missed. For participant-to-participant links, order by ID.
        var initiatorId = _activeCall?.InitiatorUserId ?? Guid.Empty;
        if (initiatorId != Guid.Empty)
        {
            if (localId == initiatorId) return true;
            if (remoteUserId == initiatorId) return false;
        }

        return localId.CompareTo(remoteUserId) < 0;
    }

    private async Task<PeerContext?> EnsurePeerAsync(Guid remoteUserId)
    {
        if (remoteUserId == Guid.Empty || remoteUserId == LocalUserId)
            return null;

        var current = _calls.Current;
        if (current is null || !_webRtc.IsStarted || _rootEngine is null)
            return null;
        var expectedCallId = current.CallId;
        var expectedConversationId = current.ConversationId;

        lock (_sync)
            if (_peers.TryGetValue(remoteUserId, out var existing)) return existing;

        await _peerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
                if (_peers.TryGetValue(remoteUserId, out var existing)) return existing;

            current = _calls.Current;
            if (current is null || current.CallId != expectedCallId ||
                current.ConversationId != expectedConversationId || !_webRtc.IsStarted)
                return null;

            AndroidWebRtcMediaEngine peerEngine;
            if (_rootPeerUserId == Guid.Empty && _rootEngine.IsStarted)
            {
                // Reuse the initial transport. When its previous participant has left,
                // rebuild it without destroying the shared local capture tracks.
                if (!_rootEngine.HasPeerConnection)
                    await _rootEngine.RecreatePeerConnectionOnlyAsync(current.Mode).ConfigureAwait(false);
                peerEngine = _rootEngine;
                _rootPeerUserId = remoteUserId;
            }
            else
            {
                peerEngine = await _rootEngine.CreatePeerConnectionEngineAsync(current.Mode).ConfigureAwait(false);
            }

            var context = new PeerContext
            {
                UserId = remoteUserId,
                Engine = peerEngine,
                Session = new WebRtcSession(_calls, current.ConversationId, current.Mode, current.CallId, remoteUserId, LocalUserId)
            };
            context.IceHandler = (_, candidate) =>
            {
                _ = SendIceCandidateSafelyAsync(context, candidate);
            };
            context.ConnectionHandler = (_, established) => ConnectionEstablishedChanged?.Invoke(this, _webRtc.IsConnectionEstablished);
            context.IceStateHandler = (_, state) => PeerStateChanged?.Invoke(this, (remoteUserId, "ICE: " + state));
            context.PeerStateHandler = (_, state) => PeerStateChanged?.Invoke(this, (remoteUserId, state));
            peerEngine.IceCandidateGenerated += context.IceHandler;
            peerEngine.ConnectionEstablishedChanged += context.ConnectionHandler;
            peerEngine.IceConnectionStateChanged += context.IceStateHandler;
            peerEngine.PeerConnectionStateChanged += context.PeerStateHandler;

            lock (_sync) _peers[remoteUserId] = context;
            PeerConnectionsChanged?.Invoke(this, EventArgs.Empty);
            ConnectionEstablishedChanged?.Invoke(this, _webRtc.IsConnectionEstablished);
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Mesh peer created; remote={remoteUserId:D}; peerCount={_peers.Count}; root={ReferenceEquals(peerEngine, _rootEngine)}.");
            return context;
        }
        finally
        {
            _peerGate.Release();
        }
    }

    private async Task SendIceCandidateSafelyAsync(PeerContext context, string candidate)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(candidate) || !IsPeerCurrent(context)) return;
            await context.Session.SendIceCandidateAsync(candidate).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Mesh ICE send failed; peer={context.UserId:D}; error={ex.Message}");
        }
    }

    private bool IsPeerCurrent(PeerContext peer)
    {
        lock (_sync)
            return _peers.TryGetValue(peer.UserId, out var current) && ReferenceEquals(current, peer);
    }

    private async Task RemovePeerAsync(Guid remoteUserId)
    {
        await _peerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            PeerContext? peer;
            lock (_sync)
            {
                if (!_peers.Remove(remoteUserId, out peer)) return;
                _acceptedRemotePeers.Remove(remoteUserId);
                _pendingMediaSignals.Remove(remoteUserId);
            }

            await peer.SignalGate.WaitAsync().ConfigureAwait(false);
            try
            {
                DetachPeerHandlers(peer);
                peer.Session.Dispose();
                if (ReferenceEquals(peer.Engine, _rootEngine))
                {
                    _rootPeerUserId = Guid.Empty;
                    await _rootEngine!.ClosePeerConnectionOnlyAsync().ConfigureAwait(false);
                }
                else
                {
                    await peer.Engine.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                peer.SignalGate.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Mesh peer cleanup failed; peer={remoteUserId:D}; error={ex.Message}");
        }
        finally
        {
            _peerGate.Release();
        }

        PeerConnectionsChanged?.Invoke(this, EventArgs.Empty);
        ConnectionEstablishedChanged?.Invoke(this, _webRtc.IsConnectionEstablished);
    }

    private static void DetachPeerHandlers(PeerContext peer)
    {
        if (peer.IceHandler is not null) peer.Engine.IceCandidateGenerated -= peer.IceHandler;
        if (peer.ConnectionHandler is not null) peer.Engine.ConnectionEstablishedChanged -= peer.ConnectionHandler;
        if (peer.IceStateHandler is not null) peer.Engine.IceConnectionStateChanged -= peer.IceStateHandler;
        if (peer.PeerStateHandler is not null) peer.Engine.PeerConnectionStateChanged -= peer.PeerStateHandler;
    }

    public Task RemoveRemoteParticipantAsync(Guid callId, Guid remoteUserId)
    {
        var current = _calls.Current;
        if (callId == Guid.Empty || remoteUserId == Guid.Empty || current is null ||
            !current.IsGroup || current.CallId != callId)
            return Task.CompletedTask;
        return RemovePeerAsync(remoteUserId);
    }

    public AndroidWebRtcMediaEngine? GetPeerEngine(Guid remoteUserId)
    {
        lock (_sync)
            return _peers.TryGetValue(remoteUserId, out var peer) ? peer.Engine : null;
    }

    public async Task AttachRemoteVideoAsync(Guid remoteUserId, VisualElement host, CancellationToken cancellationToken = default)
    {
        var engine = GetPeerEngine(remoteUserId);
        if (engine is not null)
            await engine.AttachRemoteVideoAsync(host, cancellationToken).ConfigureAwait(false);
    }

    public Task AttachLocalVideoAsync(VisualElement host, CancellationToken cancellationToken = default)
    {
        if (_rootEngine is null) return Task.CompletedTask;
        return _rootEngine.AttachLocalVideoAsync(host, cancellationToken);
    }

    private Guid LocalUserId => _account.CurrentAccount?.UserId ?? Guid.Empty;

    private void OnCallEnded(object? sender, EventArgs e) => Stop();

    public void Stop()
    {
        PeerContext[] peers;
        lock (_sync)
        {
            peers = _peers.Values.ToArray();
            _peers.Clear();
            _acceptedRemotePeers.Clear();
            _pendingMediaSignals.Clear();
            _rootPeerUserId = Guid.Empty;
            _activeCall = null;
        }

        // CallService owns transport teardown and stops the complete media engine.
        // The coordinator only releases its signaling contexts here; starting native
        // disposal from this event could race a just-started subsequent call.
        foreach (var peer in peers)
        {
            DetachPeerHandlers(peer);
            peer.Session.Dispose();
        }

        PeerConnectionsChanged?.Invoke(this, EventArgs.Empty);
        ConnectionEstablishedChanged?.Invoke(this, _webRtc.IsConnectionEstablished);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _calls.StateChanged -= OnCallStateChanged;
        _calls.IncomingSignal -= OnIncomingSignal;
        _calls.LocalAccepted -= OnLocalAccepted;
        _calls.CallEnded -= OnCallEnded;
        Stop();
        _peerGate.Dispose();
    }
}
