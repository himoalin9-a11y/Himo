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

public sealed record CallRequest(Guid ConversationId, CallMode Mode);
public enum CallLifecycleState
{
    Idle,
    Calling,
    Connected,
    Disconnecting,
    Ended
}

public sealed record CallState(Guid ConversationId, CallMode Mode, bool IsConnected, bool IsMuted, bool IsSpeakerOn, bool IsRemoteAudioEnabled);

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
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            await _media.StartAsync(request.Mode, cancellationToken);
            Volatile.Write(ref _ending, 0);
            _lifecycle = CallLifecycleState.Calling;
            _current = new CallState(
                request.ConversationId,
                request.Mode,
                false,
                false,
                _media.IsSpeakerEnabled,
                _webRtc.IsRemoteAudioEnabled);

            try
            {
                await _webRtc.StartAsync(request.Mode, cancellationToken);
                if (operationVersion != Volatile.Read(ref _operationVersion) || Volatile.Read(ref _ending) != 0)
                {
                    await StopCallResourcesQuietlyAsync();
                    Clear();
                    return;
                }
                RaiseState();
                await _realtime.SendCallSignalAsync(
                    request.ConversationId,
                    CallSignalType.Invite.ToString(),
                    JsonSerializer.Serialize(new { mode = request.Mode.ToString().ToLowerInvariant() }),
                    cancellationToken);
            }
            catch
            {
                await StopCallResourcesQuietlyAsync();
                Clear();
                throw;
            }
        }
        finally
        {
            _remoteAudioGate.Release();
        }
    }

    public async Task PrepareIncomingAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _operationVersion);

        var current = _current;
        if (current is not null)
        {
            if (current.ConversationId == request.ConversationId && current.Mode == request.Mode)
                return;

            if (current.IsConnected || _lifecycle == CallLifecycleState.Calling)
                throw new InvalidOperationException("يوجد اتصال نشط أو جارٍ لمكالمة أخرى.");

            await StopCallResourcesQuietlyAsync();
        }

        Volatile.Write(ref _ending, 0);
        _lifecycle = CallLifecycleState.Calling;
        _current = new CallState(
            request.ConversationId,
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
                    null,
                    cancellationToken);
            }
            catch
            {
                await StopCallResourcesQuietlyAsync();
                Clear();
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
        // The local media path is closed FIRST. Signaling is only a best-effort
        // notification after teardown, so a broken hub can never block the UI.
        await StopCallResourcesQuietlyAsync();
        Clear();
        _lifecycle = CallLifecycleState.Ended;
        await SendCallSignalBestEffortAsync(state.ConversationId, CallSignalType.Reject, cancellationToken);
    }

    public async Task EndAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _operationVersion);
        if (Interlocked.Exchange(ref _ending, 1) != 0) return;
        var state = _current;

        _lifecycle = CallLifecycleState.Disconnecting;
        // Exact teardown order: stop local media -> stop WebRTC -> clear UI state ->
        // best-effort End signal. The signal is never allowed to hold navigation.
        await StopCallResourcesQuietlyAsync();
        Clear();
        _lifecycle = CallLifecycleState.Ended;

        if (state is not null)
            await SendCallSignalBestEffortAsync(state.ConversationId, CallSignalType.End, cancellationToken);
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
        return _realtime.SendCallSignalAsync(_current.ConversationId, type.ToString(), payload, cancellationToken);
    }

    private async void OnSignalReceived(object? sender, CallSignalMessage signal)
    {
        if (string.Equals(signal.Type, CallSignalType.Invite.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var mode = CallMode.Audio;
            if (!string.IsNullOrWhiteSpace(signal.Payload))
            {
                try
                {
                    using var doc = JsonDocument.Parse(signal.Payload);
                    if (doc.RootElement.TryGetProperty("mode", out var m) && string.Equals(m.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                        mode = CallMode.Video;
                }
                catch { }
            }
            if (_current is not null && _current.ConversationId != signal.ConversationId)
            {
                if (_current.IsConnected || _lifecycle == CallLifecycleState.Calling)
                {
                    _ = SendCallSignalBestEffortAsync(signal.ConversationId, CallSignalType.Reject, CancellationToken.None);
                    return;
                }

                await StopCallResourcesQuietlyAsync();
            }

            Volatile.Write(ref _ending, 0);
            _lifecycle = CallLifecycleState.Calling;
            _current = new CallState(signal.ConversationId, mode, false, false, _media.IsSpeakerEnabled, _webRtc.IsRemoteAudioEnabled);
            RaiseState();
        }
        else if (_current is not null && _current.ConversationId == signal.ConversationId)
        {
            if (string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // Accept is handled by WebRtcNegotiationCoordinator. Keeping SDP
                // creation in one place prevents duplicate Offer messages.
            }
            else if (string.Equals(signal.Type, CallSignalType.End.ToString(), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(signal.Type, CallSignalType.Reject.ToString(), StringComparison.OrdinalIgnoreCase)) await ClearAsync();
        }

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
        Guid conversationId,
        CallSignalType type,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await _realtime.SendCallSignalAsync(conversationId, type.ToString(), null, timeout.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo CallService] {type} signal failed: {ex.Message}");
        }
    }

    private void RaiseState()
    {
        if (_current is not null) StateChanged?.Invoke(this, _current);
    }

    public void Dispose()
    {
        _realtime.CallSignalReceived -= OnSignalReceived;
        _webRtc.ConnectionEstablishedChanged -= OnConnectionEstablishedChanged;
    }
}
