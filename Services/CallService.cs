using System.Text.Json;

namespace Himo.Services;

public enum CallMode
{
    Audio,
    Video
}

public enum CallSignalType
{
    Invite,
    Accept,
    Reject,
    End,
    Offer,
    Answer,
    IceCandidate
}

public sealed record CallRequest(Guid ConversationId, CallMode Mode, Guid CallId = default);
public enum CallLifecycleState
{
    Idle,
    Calling,
    Connected,
    Disconnecting,
    Ended
}

public sealed record CallState(Guid ConversationId, Guid CallId, CallMode Mode, bool IsConnected, bool IsMuted, bool IsSpeakerOn, bool IsRemoteAudioEnabled);

public sealed record CallSignalEnvelope(Guid CallId, CallMode? Mode, string? Data)
{
    public static string Serialize(Guid callId, CallMode? mode = null, string? data = null)
        => JsonSerializer.Serialize(new
        {
            callId,
            mode = mode?.ToString().ToLowerInvariant(),
            data
        });

    public static bool TryParse(string? payload, out CallSignalEnvelope envelope)
    {
        envelope = new CallSignalEnvelope(Guid.Empty, null, payload);
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("callId", out var idElement) ||
                !Guid.TryParse(idElement.GetString(), out var callId) ||
                callId == Guid.Empty)
                return false;

            CallMode? mode = null;
            if (root.TryGetProperty("mode", out var modeElement) &&
                string.Equals(modeElement.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                mode = CallMode.Video;
            else if (root.TryGetProperty("mode", out modeElement) &&
                     string.Equals(modeElement.GetString(), "audio", StringComparison.OrdinalIgnoreCase))
                mode = CallMode.Audio;

            string? data = null;
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null)
                data = dataElement.GetString();

            envelope = new CallSignalEnvelope(callId, mode, data);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public interface ICallService
{
    event EventHandler<CallState>? StateChanged;
    event EventHandler<CallSignalMessage>? IncomingSignal;
    event EventHandler? CallEnded;
    CallState? Current { get; }
    Task<bool> RequestPermissionsAsync(CallMode mode, CancellationToken cancellationToken = default);
    Task StartAsync(CallRequest request, CancellationToken cancellationToken = default);
    Task PrepareIncomingAsync(CallRequest request, CancellationToken cancellationToken = default);
    Task AcceptAsync(CancellationToken cancellationToken = default);
    Task RejectAsync(CancellationToken cancellationToken = default);
    Task EndAsync(CancellationToken cancellationToken = default);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default);
    Task SetSpeakerAsync(bool enabled, CancellationToken cancellationToken = default);
    bool IsRemoteAudioEnabled { get; }
    Task SetRemoteAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default);
    Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default);
    Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default);
}

public sealed class CallService : ICallService, IDisposable
{
    private readonly HimoRealtimeService _realtime;
    private readonly ICallMediaController _media;
    private readonly IWebRtcMediaEngine _webRtc;
    private CallState? _current;
    private readonly SemaphoreSlim _remoteAudioGate = new(1, 1);
    private int _ending;
    private long _operationVersion;
    private CallLifecycleState _lifecycle = CallLifecycleState.Idle;
    private Guid _pendingIncomingCallId;
    private CancellationTokenSource? _callTimeoutCts;
    private TaskCompletionSource<bool>? _outgoingWebRtcStartup;
    private static readonly TimeSpan OutgoingTimeout = TimeSpan.FromSeconds(45);

    public event EventHandler<CallState>? StateChanged;
    public event EventHandler<CallSignalMessage>? IncomingSignal;
    public event EventHandler? CallEnded;
    public CallState? Current => _current;
    public bool IsRemoteAudioEnabled => _webRtc.IsRemoteAudioEnabled;

    public CallService(HimoRealtimeService realtime, ICallMediaController media, IWebRtcMediaEngine webRtc)
    {
        _realtime = realtime;
        _media = media;
        _webRtc = webRtc;
        _webRtc.ConnectionEstablishedChanged += OnConnectionEstablishedChanged;
        _realtime.CallSignalReceived += OnSignalReceived;
    }

    public async Task<bool> RequestPermissionsAsync(CallMode mode, CancellationToken cancellationToken = default)
    {
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();
        if (microphone != PermissionStatus.Granted) return false;
        if (mode == CallMode.Video)
        {
            var camera = await Permissions.RequestAsync<Permissions.Camera>();
            if (camera != PermissionStatus.Granted) return false;
        }
        return true;
    }

    public async Task StartAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        var operationVersion = Interlocked.Increment(ref _operationVersion);
        var callId = request.CallId == Guid.Empty ? Guid.NewGuid() : request.CallId;
        request = request with { CallId = callId };
        CancelCallTimeout();

        var inviteAttempted = false;
        CallState? outgoingState = null;

        await _remoteAudioGate.WaitAsync(cancellationToken);
        var startupGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _outgoingWebRtcStartup, startupGate)?.TrySetResult(false);
        try
        {
            try
            {
                await _media.StartAsync(request.Mode, cancellationToken);
                if (operationVersion != Volatile.Read(ref _operationVersion))
                {
                    await StopCallResourcesQuietlyAsync();
                    return;
                }

                Volatile.Write(ref _ending, 0);
                _lifecycle = CallLifecycleState.Calling;
                _current = outgoingState = new CallState(
                    request.ConversationId,
                    callId,
                    request.Mode,
                    false,
                    false,
                    _media.IsSpeakerEnabled,
                    _webRtc.IsRemoteAudioEnabled);

                // Publish the call state first: CallPage starts the earpiece ringback
                // after Android has selected the communication audio route.
                RaiseState();

                // Send Invite before PeerConnectionFactory initialization. If the
                // local WebRTC engine is slow, the recipient can still receive and
                // ring for the call instead of seeing no incoming call at all.
                inviteAttempted = true;
                await _realtime.SendCallSignalAsync(
                    request.ConversationId,
                    CallSignalType.Invite.ToString(),
                    CallSignalEnvelope.Serialize(callId, request.Mode),
                    cancellationToken);
                StartOutgoingTimeout(callId);

                await _webRtc.StartAsync(request.Mode, cancellationToken);
                if (operationVersion != Volatile.Read(ref _operationVersion) || Volatile.Read(ref _ending) != 0)
                {
                    startupGate.TrySetResult(false);
                    await StopCallResourcesQuietlyAsync();
                    Clear();
                    return;
                }

                // A remote Accept can arrive while StartAsync is building PeerConnection.
                // OnSignalReceived waits for this gate so the Accept is not dropped.
                startupGate.TrySetResult(true);
                RaiseState();
            }
            catch
            {
                startupGate.TrySetResult(false);
                CancelCallTimeout();
                if (inviteAttempted && outgoingState is not null)
                    await SendCallSignalBestEffortAsync(outgoingState, CallSignalType.End, CancellationToken.None);

                await StopCallResourcesQuietlyAsync();
                if (_current?.CallId == callId)
                    _current = null;
                _lifecycle = CallLifecycleState.Ended;
                throw;
            }
        }
        finally
        {
            startupGate.TrySetResult(false);
            _remoteAudioGate.Release();
        }
    }

    public async Task PrepareIncomingAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _operationVersion);
        Interlocked.Exchange(ref _outgoingWebRtcStartup, null)?.TrySetResult(false);

        // Preserve an existing incoming call's authoritative ID when an FCM deep link
        // omits callId but the matching SignalR Invite has already arrived. Do not
        // manufacture a new GUID before comparing with the active incoming state.
        var incomingCallId = request.CallId != Guid.Empty ? request.CallId : _pendingIncomingCallId;
        CancelCallTimeout();
        var current = _current;
        if (current is not null)
        {
            if (current.ConversationId == request.ConversationId && current.Mode == request.Mode &&
                (incomingCallId == Guid.Empty || current.CallId == incomingCallId))
                return;

            if (current.IsConnected || _lifecycle == CallLifecycleState.Calling)
                throw new InvalidOperationException("يوجد اتصال نشط أو جارٍ لمكالمة أخرى.");

            await StopCallResourcesQuietlyAsync();
        }

        if (incomingCallId == Guid.Empty) incomingCallId = Guid.NewGuid();
        request = request with { CallId = incomingCallId };
        Volatile.Write(ref _ending, 0);
        _lifecycle = CallLifecycleState.Calling;
        _current = new CallState(
            request.ConversationId,
            request.CallId,
            request.Mode,
            false,
            false,
            _media.IsSpeakerEnabled,
            _webRtc.IsRemoteAudioEnabled);
        RaiseState();
    }

    public async Task AcceptAsync(CancellationToken cancellationToken = default)
    {
        var operationVersion = Interlocked.Increment(ref _operationVersion);
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            var current = _current;
            if (current is null || Volatile.Read(ref _ending) != 0) return;

            _lifecycle = CallLifecycleState.Calling;
            await _media.StartAsync(current.Mode, cancellationToken);
            _current = current = current with { IsSpeakerOn = _media.IsSpeakerEnabled };

            try
            {
                await _webRtc.StartAsync(current.Mode, cancellationToken);
                if (operationVersion != Volatile.Read(ref _operationVersion) || Volatile.Read(ref _ending) != 0)
                {
                    await StopCallResourcesQuietlyAsync();
                    return;
                }
                RaiseState();
                await _realtime.SendCallSignalAsync(
                    current.ConversationId,
                    CallSignalType.Accept.ToString(),
                    CallSignalEnvelope.Serialize(current.CallId, current.Mode),
                    cancellationToken);
            }
            catch
            {
                var failedState = _current;
                await StopCallResourcesQuietlyAsync();
                Clear();
                if (failedState is not null)
                    await SendCallSignalBestEffortAsync(failedState, CallSignalType.Reject, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _remoteAudioGate.Release();
        }
    }

    public async Task RejectAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _operationVersion);
        if (Interlocked.Exchange(ref _ending, 1) != 0) return;
        var state = _current;
        if (state is null) return;

        _lifecycle = CallLifecycleState.Disconnecting;
        _outgoingWebRtcStartup?.TrySetResult(false);
        CancelCallTimeout();
        // The local media path is closed FIRST. Signaling is only a best-effort
        // notification after teardown, so a broken hub can never block the UI.
        await StopCallResourcesQuietlyAsync();
        Clear();
        _lifecycle = CallLifecycleState.Ended;
        await SendCallSignalBestEffortAsync(state, CallSignalType.Reject, cancellationToken);
    }

    public async Task EndAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _operationVersion);
        if (Interlocked.Exchange(ref _ending, 1) != 0) return;
        var state = _current;

        _lifecycle = CallLifecycleState.Disconnecting;
        _outgoingWebRtcStartup?.TrySetResult(false);
        CancelCallTimeout();
        // Exact teardown order: stop local media -> stop WebRTC -> clear UI state ->
        // best-effort End signal. The signal is never allowed to hold navigation.
        await StopCallResourcesQuietlyAsync();
        Clear();
        _lifecycle = CallLifecycleState.Ended;

        if (state is not null)
            await SendCallSignalBestEffortAsync(state, CallSignalType.End, cancellationToken);
    }

    public async Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        await _media.SetMutedAsync(muted, cancellationToken);
        await _webRtc.SetMicrophoneEnabledAsync(!muted, cancellationToken);
        _current = _current with { IsMuted = muted };
        RaiseState();
    }

    public async Task SetSpeakerAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        await _media.SetSpeakerAsync(enabled, cancellationToken);
        await _webRtc.SetSpeakerEnabledAsync(enabled, cancellationToken);
        _current = _current with { IsSpeakerOn = enabled };
        RaiseState();
    }

    public async Task SetRemoteAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            var state = _current;
            if (state is null || state.IsRemoteAudioEnabled == enabled) return;

            var conversationId = state.ConversationId;
            await _webRtc.SetRemoteAudioEnabledAsync(enabled, cancellationToken);

            if (_current is null || _current.ConversationId != conversationId) return;
            _current = _current with { IsRemoteAudioEnabled = enabled };
            RaiseState();
        }
        finally
        {
            _remoteAudioGate.Release();
        }
    }

    public Task SendOfferAsync(string sdp, CancellationToken cancellationToken = default) => SendMediaSignal(CallSignalType.Offer, sdp, cancellationToken);
    public Task SendAnswerAsync(string sdp, CancellationToken cancellationToken = default) => SendMediaSignal(CallSignalType.Answer, sdp, cancellationToken);
    public Task SendIceCandidateAsync(string candidate, CancellationToken cancellationToken = default) => SendMediaSignal(CallSignalType.IceCandidate, candidate, cancellationToken);

    private Task SendMediaSignal(CallSignalType type, string payload, CancellationToken cancellationToken)
    {
        if (_current is null || string.IsNullOrWhiteSpace(payload)) return Task.CompletedTask;
        return _realtime.SendCallSignalAsync(_current.ConversationId, type.ToString(), CallSignalEnvelope.Serialize(_current.CallId, _current.Mode, payload), cancellationToken);
    }

    private async void OnSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (string.Equals(signal.Type, CallSignalType.Invite.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var mode = CallMode.Audio;
            var callId = Guid.NewGuid();
            if (CallSignalEnvelope.TryParse(signal.Payload, out var inviteEnvelope))
            {
                callId = inviteEnvelope.CallId;
                if (inviteEnvelope.Mode.HasValue) mode = inviteEnvelope.Mode.Value;
            }
            else if (!string.IsNullOrWhiteSpace(signal.Payload))
            {
                try
                {
                    using var doc = JsonDocument.Parse(signal.Payload);
                    if (doc.RootElement.TryGetProperty("mode", out var m) && string.Equals(m.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                        mode = CallMode.Video;
                } catch { }
            }
            _pendingIncomingCallId = callId;
            if (_current is not null)
            {
                if (_current.ConversationId == signal.ConversationId &&
                    _current.CallId == callId)
                    return;

                if (_current.IsConnected || _lifecycle == CallLifecycleState.Calling)
                {
                    _ = SendCallSignalBestEffortAsync(
                        new CallState(signal.ConversationId, callId, mode, false, false, _media.IsSpeakerEnabled, _webRtc.IsRemoteAudioEnabled),
                        CallSignalType.Reject,
                        CancellationToken.None);
                    return;
                }

                await StopCallResourcesQuietlyAsync();
            }

            Volatile.Write(ref _ending, 0);
            _lifecycle = CallLifecycleState.Calling;
            _current = new CallState(signal.ConversationId, callId, mode, false, false, _media.IsSpeakerEnabled, _webRtc.IsRemoteAudioEnabled);
            RaiseState();
        }
        else if (_current is not null && _current.ConversationId == signal.ConversationId)
        {
            if (CallSignalEnvelope.TryParse(signal.Payload, out var envelope) &&
                envelope.CallId != Guid.Empty &&
                envelope.CallId != _current.CallId)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo CallService] Drop signal: callId mismatch; type={signal.Type}; " +
                    $"activeCallId={_current.CallId:D}; receivedCallId={envelope.CallId:D}; " +
                    $"conversation={signal.ConversationId:D}");
                return;
            }
            if (string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // The Invite is sent before local WebRTC startup so the receiver can
                // ring promptly. If their answer arrives during startup, hold it until
                // the local PeerConnection is ready instead of silently dropping it.
                var startupGate = _outgoingWebRtcStartup;
                if (startupGate is not null && !_webRtc.IsStarted)
                {
                    bool started;
                    try
                    {
                        started = await startupGate.Task.WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Himo CallService] Waiting for WebRTC startup before Accept failed: {ex.Message}");
                        return;
                    }

                    var liveCall = _current;
                    if (!started || liveCall is null || liveCall.ConversationId != signal.ConversationId ||
                        (CallSignalEnvelope.TryParse(signal.Payload, out var acceptedEnvelope) &&
                         acceptedEnvelope.CallId != Guid.Empty && acceptedEnvelope.CallId != liveCall.CallId))
                    {
                        System.Diagnostics.Debug.WriteLine("[Himo CallService] Dropped Accept because the matching WebRTC call did not become ready.");
                        return;
                    }
                }
                // Accept is handled by WebRtcNegotiationCoordinator. Keeping SDP
                // creation in one place prevents duplicate Offer messages.
            }
            else if (string.Equals(signal.Type, CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(signal.Type, CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _operationVersion);
                _outgoingWebRtcStartup?.TrySetResult(false);
                await ClearAsync();
            }
        }

        var activeCallId = _current?.CallId.ToString("D") ?? "none";
        System.Diagnostics.Debug.WriteLine(
            $"[Himo CallService] Dispatch signal; type={signal.Type}; " +
            $"conversation={signal.ConversationId:D}; " +
            $"activeCallId={activeCallId}; " +
            $"payloadLength={signal.Payload?.Length ?? 0}");
        IncomingSignal?.Invoke(this, signal);
    }

    private void OnConnectionEstablishedChanged(object? sender, bool established)
    {
        // Ignore late WebRTC notifications after the media engine has already stopped.
        if (_current is null || !_webRtc.IsStarted) return;
        SetConnected(established);
    }

    private void SetConnected(bool value)
    {
        if (_current is null) return;
        if (_current.IsConnected == value) return;

        _current = _current with { IsConnected = value };
        _lifecycle = value ? CallLifecycleState.Connected : CallLifecycleState.Calling;
        RaiseState();
    }

    private void Clear()
    {
        CancelCallTimeout();
        Interlocked.Exchange(ref _outgoingWebRtcStartup, null)?.TrySetResult(false);
        _pendingIncomingCallId = Guid.Empty;
        if (_current is null) return;
        _current = null;
        CallEnded?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        await StopCallResourcesQuietlyAsync();
        Clear();
    }

    private async Task StopCallResourcesQuietlyAsync()
    {
        try { await _media.StopAsync(CancellationToken.None); } catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallService] Media cleanup failed: {ex}");
        }

        try { await _webRtc.StopAsync(CancellationToken.None); } catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallService] WebRTC cleanup failed: {ex}");
        }
    }

    private async Task SendCallSignalBestEffortAsync(
        CallState state,
        CallSignalType type,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await _realtime.SendCallSignalAsync(state.ConversationId, type.ToString(), CallSignalEnvelope.Serialize(state.CallId, state.Mode), timeout.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallService] {type} signal failed: {ex.Message}");
        }
    }

    private void StartOutgoingTimeout(Guid callId)
    {
        CancelCallTimeout();
        var cts = new CancellationTokenSource();
        _callTimeoutCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OutgoingTimeout, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested || _current?.CallId != callId || _current.IsConnected) return;
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo CallService] Outgoing call timed out without WebRTC connection; " +
                    $"callId={callId:D}; conversation={_current.ConversationId:D}; " +
                    $"ice/peer connection was not established before timeout.");
                await EndAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo CallService] Call timeout cleanup failed: {ex}");
            }
        });
    }

    private void CancelCallTimeout()
    {
        var cts = Interlocked.Exchange(ref _callTimeoutCts, null);
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private void RaiseState()
    {
        if (_current is not null) StateChanged?.Invoke(this, _current);
    }

    public void Dispose()
    {
        CancelCallTimeout();
        _realtime.CallSignalReceived -= OnSignalReceived;
        _webRtc.ConnectionEstablishedChanged -= OnConnectionEstablishedChanged;
    }
}
