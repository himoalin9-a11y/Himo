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
public sealed record CallState(Guid ConversationId, CallMode Mode, bool IsConnected, bool IsMuted, bool IsSpeakerOn, bool IsRemoteAudioEnabled);

public interface ICallService
{
    event EventHandler<CallState>? StateChanged;
    event EventHandler<CallSignalMessage>? IncomingSignal;
    event EventHandler? CallEnded;
    CallState? Current { get; }
    Task<bool> RequestPermissionsAsync(CallMode mode, CancellationToken cancellationToken = default);
    Task StartAsync(CallRequest request, CancellationToken cancellationToken = default);
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
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            await _media.StartAsync(request.Mode, cancellationToken);
            _current = new CallState(request.ConversationId, request.Mode, false, false,
                _media.IsSpeakerEnabled, _webRtc.IsRemoteAudioEnabled);

            try
            {
                await _webRtc.StartAsync(request.Mode, cancellationToken);
                RaiseState();
                await _realtime.SendCallSignalAsync(
                    request.ConversationId,
                    CallSignalType.Invite.ToString(),
                    JsonSerializer.Serialize(new { mode = request.Mode.ToString().ToLowerInvariant() }),
                    cancellationToken);
            }
            catch
            {
                await StopCallResourcesAsync(CancellationToken.None);
                _current = null;
                throw;
            }
        }
        finally
        {
            _remoteAudioGate.Release();
        }
    }

    public async Task AcceptAsync(CancellationToken cancellationToken = default)
    {
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            if (_current is null) return;

            await _media.StartAsync(_current.Mode, cancellationToken);
            _current = _current with { IsSpeakerOn = _media.IsSpeakerEnabled };

            try
            {
                await _webRtc.StartAsync(_current.Mode, cancellationToken);
                await _realtime.SendCallSignalAsync(
                    _current.ConversationId,
                    CallSignalType.Accept.ToString(),
                    null,
                    cancellationToken);
            }
            catch
            {
                await StopCallResourcesAsync(CancellationToken.None);
                _current = null;
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
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            if (_current is null) return;
            await _realtime.SendCallSignalAsync(_current.ConversationId, CallSignalType.Reject.ToString(), null, cancellationToken);
            await StopCallResourcesAsync(cancellationToken);
            Clear();
        }
        finally
        {
            _remoteAudioGate.Release();
        }
    }

    public async Task EndAsync(CancellationToken cancellationToken = default)
    {
        await _remoteAudioGate.WaitAsync(cancellationToken);
        try
        {
            if (_current is not null)
                await _realtime.SendCallSignalAsync(_current.ConversationId, CallSignalType.End.ToString(), null, cancellationToken);
            await StopCallResourcesAsync(cancellationToken);
            Clear();
        }
        finally
        {
            _remoteAudioGate.Release();
        }
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
            _current = new CallState(signal.ConversationId, mode, false, false, _media.IsSpeakerEnabled, _webRtc.IsRemoteAudioEnabled);
            RaiseState();
        }
        else if (_current is not null && _current.ConversationId == signal.ConversationId)
        {
            if (string.Equals(signal.Type, CallSignalType.Accept.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // Accept confirms signaling only; WebRTC establishes the connected state.
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
        RaiseState();
    }

    private void Clear()
    {
        if (_current is null) return;
        _current = null;
        CallEnded?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopCallResourcesAsync(CancellationToken cancellationToken)
    {
        Exception? firstError = null;

        try
        {
            await _media.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            firstError = ex;
        }

        try
        {
            await _webRtc.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            firstError ??= ex;
        }

        if (firstError is not null)
            throw firstError;
    }

    private async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        await StopCallResourcesAsync(cancellationToken);
        Clear();
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
