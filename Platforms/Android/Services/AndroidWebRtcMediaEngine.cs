using System.Reflection;
using System.Text.Json;
using Himo.Services;
using Microsoft.Maui.Controls;

namespace Himo.Platforms.Android.Services;

/// <summary>Android WebRTC media engine. Binding calls stay reflective because the
/// generated names in the libwebrtc binding vary between releases.</summary>
public sealed class AndroidWebRtcMediaEngine : IWebRtcMediaEngine
{
    private const string NativeAssemblyName = "FsWebRTC.Bindings.Maui.Android";
    private Assembly? _assembly;
    private object? _factory;
    private object? _peerConnection;
    private PeerConnectionObserverBridge? _peerConnectionObserver;
    private object? _audioSource;
    private object? _audioTrack;
    private object? _eglBase;
    private object? _eglContext;
    private object? _surfaceTextureHelper;
    private object? _videoSource;
    private object? _videoTrack;
    private object? _capturer;
    private object? _localRenderer;
    private object? _remoteRenderer;
    private string? _cameraDeviceName;
    private bool _started;
    private bool _cameraEnabled;
    private bool _microphoneEnabled;
    private bool _speakerEnabled;
    private bool _remoteAudioEnabled = true;
    private bool _remoteDescriptionSet;
    private readonly Queue<string> _pendingRemoteIceCandidates = new();
    private readonly HashSet<string> _remoteIceCandidateKeys = new(StringComparer.Ordinal);

    public event EventHandler<string>? IceCandidateGenerated;
    public event EventHandler<string>? IceConnectionStateChanged;
    public event EventHandler<string>? PeerConnectionStateChanged;
    public event EventHandler<bool>? ConnectionEstablishedChanged;
    private CallMode? _mode;
    private string? _lastIceConnectionState;
    private string? _lastPeerConnectionState;
    private bool _lastConnectionEstablished;
    private long _sessionGeneration;

    public bool IsStarted => _started;
    public CallMode? Mode => _mode;
    public bool IsCameraEnabled => _cameraEnabled;
    public bool IsMicrophoneEnabled => _microphoneEnabled;
    public bool IsSpeakerEnabled => _speakerEnabled;
    public bool IsRemoteAudioEnabled => _remoteAudioEnabled;
    public string? LastIceConnectionState => _lastIceConnectionState;
    public string? LastPeerConnectionState => _lastPeerConnectionState;
    public bool IsIceConnected => string.Equals(_lastIceConnectionState, "Connected", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(_lastIceConnectionState, "Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsPeerConnectionConnected => string.Equals(_lastPeerConnectionState, "Connected", StringComparison.OrdinalIgnoreCase);
    public bool IsConnectionEstablished => IsIceConnected && IsPeerConnectionConnected;

    public Task StartAsync(CallMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_started)
        {
            if (_mode == mode) return Task.CompletedTask;
            _started = false;
            DisposeNativeObjects();
            _cameraEnabled = false;
            _microphoneEnabled = false;
            _speakerEnabled = false;
            _mode = null;
        }

        var hadConnectionState = _lastConnectionEstablished;
        var sessionGeneration = ++_sessionGeneration;
        _remoteAudioEnabled = true;
        ResetConnectionState();
        if (hadConnectionState)
            ConnectionEstablishedChanged?.Invoke(this, false);

        _assembly = LoadBinding();
        try
        {
            InitializeAudio();
            if (mode == CallMode.Video)
                InitializeVideo();

            InitializePeerConnection(sessionGeneration);

            _mode = mode;
            _started = true;
            _cameraEnabled = mode == CallMode.Video;
            _microphoneEnabled = true;
            _speakerEnabled = false;
            return Task.CompletedTask;
        }
        catch
        {
            DisposeNativeObjects();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ++_sessionGeneration;
        _started = false;
        var wasConnectionEstablished = _lastConnectionEstablished;
        _lastIceConnectionState = null;
        _lastPeerConnectionState = null;
        _lastConnectionEstablished = false;
        if (wasConnectionEstablished)
            ConnectionEstablishedChanged?.Invoke(this, false);
        DisposeNativeObjects();
        _cameraEnabled = false;
        _microphoneEnabled = false;
        _speakerEnabled = false;
        _mode = null;
        return Task.CompletedTask;
    }

    public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_audioTrack is null) return Task.CompletedTask;
        if (_microphoneEnabled == enabled) return Task.CompletedTask;

        InvokeOptional(_audioTrack, "SetEnabled", enabled);
        _microphoneEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started || _mode != CallMode.Video || _capturer is null) return Task.CompletedTask;

        if (enabled && !_cameraEnabled)
        {
            InvokeRequired(_capturer, "StartCapture", 1280, 720, 30);
            InvokeOptional(_videoTrack, "SetEnabled", true);
            _cameraEnabled = true;
        }
        else if (!enabled && _cameraEnabled)
        {
            InvokeOptional(_capturer, "StopCapture");
            InvokeOptional(_videoTrack, "SetEnabled", false);
            _cameraEnabled = false;
        }
        return Task.CompletedTask;
    }

    public Task SwitchCameraAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started || _mode != CallMode.Video || _capturer is null || _videoSource is null || _surfaceTextureHelper is null)
            return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        var enumeratorType = RequiredType("Org.Webrtc.Camera2Enumerator");
        var enumerator = Activator.CreateInstance(enumeratorType, context)
            ?? throw new InvalidOperationException("Camera2Enumerator could not be created.");

        try
        {
            var names = (InvokeRequired(enumerator, "GetDeviceNames") as Array)?.Cast<object>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>();

            if (names.Length == 0) return Task.CompletedTask;

            var target = names.FirstOrDefault(n => !string.Equals(n, _cameraDeviceName, StringComparison.Ordinal));
            if (target is null) return Task.CompletedTask;

            var wasEnabled = _cameraEnabled;
            if (_capturer is not null) InvokeOptional(_capturer, "StopCapture");
            TryDispose(_capturer);
            _capturer = null;

            var newCapturer = InvokeRequired(enumerator, "CreateCapturer", target, null);
            var observer = InvokeRequired(_videoSource!, "GetCapturerObserver");
            InvokeRequired(newCapturer, "Initialize", _surfaceTextureHelper!, context, observer);
            _capturer = newCapturer;
            _cameraDeviceName = target;

            if (wasEnabled)
            {
                InvokeRequired(_capturer, "StartCapture", 1280, 720, 30);
                InvokeOptional(_videoTrack, "SetEnabled", true);
            }
            else
            {
                InvokeOptional(_videoTrack, "SetEnabled", false);
            }

            // Preserve the public camera state across the native capturer swap.
            _cameraEnabled = wasEnabled;

            // Rebind the existing local track after capturer replacement so a
            // renderer recreated during the switch cannot retain a stale sink.
            if (_videoTrack is not null && _localRenderer is not null)
            {
                InvokeOptional(_videoTrack, "RemoveSink", _localRenderer);
                InvokeOptional(_videoTrack, "AddSink", _localRenderer);
            }

            return Task.CompletedTask;
        }
        finally
        {
            TryDispose(enumerator);
        }
    }

    public Task AttachLocalVideoAsync(VisualElement host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode != CallMode.Video) return Task.CompletedTask;

        _localRenderer = AttachRenderer(host, mirror: true, _localRenderer);
        if (_videoTrack is not null)
        {
            // Re-attach safely: avoid duplicate sink registration after a view lifecycle restart.
            InvokeOptional(_videoTrack, "RemoveSink", _localRenderer);
            InvokeOptional(_videoTrack, "AddSink", _localRenderer);
        }

        return Task.CompletedTask;
    }

    public Task AttachRemoteVideoAsync(VisualElement host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode != CallMode.Video) return Task.CompletedTask;

        // Create the renderer even when the remote track has not arrived yet.
        // SetRemoteVideoTrack will bind the track later when signaling completes.
        _remoteRenderer = AttachRenderer(host, mirror: false, _remoteRenderer);
        BindRemoteVideoTrack();

        return Task.CompletedTask;
    }

    private object? _remoteVideoTrack;
    private object? _remoteAudioTrack;

    /// <summary>Called by the PeerConnection layer when a remote AudioTrack is received.</summary>
    public void SetRemoteAudioTrack(object? track)
    {
        if (ReferenceEquals(_remoteAudioTrack, track))
        {
            if (_remoteAudioTrack is not null)
                InvokeOptional(_remoteAudioTrack, "SetEnabled", _remoteAudioEnabled);
            return;
        }

        _remoteAudioTrack = track;
        if (_remoteAudioTrack is not null)
            InvokeOptional(_remoteAudioTrack, "SetEnabled", _remoteAudioEnabled);
    }

    public void SetRemoteVideoTrack(object? track)
    {
        if (ReferenceEquals(_remoteVideoTrack, track))
        {
            // The track may be unchanged while the renderer was recreated by the view lifecycle.
            BindRemoteVideoTrack();
            return;
        }

        if (_remoteVideoTrack is not null && _remoteRenderer is not null)
            InvokeOptional(_remoteVideoTrack, "RemoveSink", _remoteRenderer);

        _remoteVideoTrack = track;
        BindRemoteVideoTrack();
    }

    private void BindRemoteVideoTrack()
    {
        if (_remoteRenderer is null) return;

        if (_remoteVideoTrack is null)
        {
            InvokeOptional(_remoteRenderer, "ClearImage");
            return;
        }

        // Rebind deterministically so renderer recreation cannot leave a stale sink.
        InvokeOptional(_remoteVideoTrack, "RemoveSink", _remoteRenderer);
        InvokeOptional(_remoteVideoTrack, "AddSink", _remoteRenderer);
    }

    public async Task<string?> CreateOfferAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePeerConnection();
        var constraints = CreateSdpConstraints();
        var description = await CreateSessionDescriptionAsync("CreateOffer", constraints, cancellationToken);
        await SetLocalDescriptionAsync(description, cancellationToken);
        return GetSdpText(description);
    }

    public async Task<string?> CreateAnswerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePeerConnection();
        var constraints = CreateSdpConstraints();
        var description = await CreateSessionDescriptionAsync("CreateAnswer", constraints, cancellationToken);
        await SetLocalDescriptionAsync(description, cancellationToken);
        return GetSdpText(description);
    }

    public async Task SetRemoteOfferAsync(string sdp, CancellationToken cancellationToken = default)
    {
        await SetRemoteDescriptionAsync("Offer", sdp, cancellationToken);
    }

    public async Task SetRemoteAnswerAsync(string sdp, CancellationToken cancellationToken = default)
    {
        await SetRemoteDescriptionAsync("Answer", sdp, cancellationToken);
    }

    public async Task AddRemoteIceCandidateAsync(string candidatePayload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePeerConnection();
        if (string.IsNullOrWhiteSpace(candidatePayload)) return;

        if (!_remoteDescriptionSet)
        {
            _pendingRemoteIceCandidates.Enqueue(candidatePayload);
            return;
        }

        AddIceCandidatePayload(candidatePayload);
        await Task.CompletedTask;
    }

    public Task SetRemoteAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _remoteAudioEnabled = enabled;
        if (_remoteAudioTrack is not null)
            InvokeOptional(_remoteAudioTrack, "SetEnabled", enabled);
        return Task.CompletedTask;
    }

    public Task SetSpeakerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _speakerEnabled = enabled;
        return Task.CompletedTask;
    }

    private void EnsurePeerConnection()
    {
        if (!_started || _peerConnection is null)
            throw new InvalidOperationException("WebRTC PeerConnection is not started.");
    }

    private object CreateSdpConstraints()
    {
        var constraintsType = RequiredType("Org.Webrtc.MediaConstraints");
        var constraints = Activator.CreateInstance(constraintsType)
            ?? throw new InvalidOperationException("MediaConstraints could not be created.");

        if (_mode == CallMode.Video)
        {
            var pairType = RequiredType("Org.Webrtc.MediaConstraints+KeyValuePair");
            var receiveVideo = Activator.CreateInstance(pairType, "OfferToReceiveVideo", "true");
            var receiveAudio = Activator.CreateInstance(pairType, "OfferToReceiveAudio", "true");
            if (receiveVideo is not null) InvokeOptionalCollectionAdd(constraints, "Mandatory", receiveVideo);
            if (receiveAudio is not null) InvokeOptionalCollectionAdd(constraints, "Mandatory", receiveAudio);
        }
        return constraints;
    }

    private static void InvokeOptionalCollectionAdd(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var collection = property?.GetValue(target);
        if (collection is null) return;
        InvokeOptional(collection, "Add", value);
    }

    private Task<object> CreateSessionDescriptionAsync(string operation, object constraints, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new SdpObserverBridge(
            created => tcs.TrySetResult(created),
            error => tcs.TrySetException(new InvalidOperationException($"WebRTC {operation} failed: {error}")));

        var method = FindMethod(_peerConnection!.GetType(), operation, 2, false)
            ?? throw new MissingMethodException(_peerConnection.GetType().FullName, operation);
        method.Invoke(_peerConnection, new[] { (object)observer, constraints });
        return AwaitWithCancellationAsync(tcs.Task, cancellationToken);
    }

    private Task SetLocalDescriptionAsync(object description, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new SdpObserverBridge(
            _ => tcs.TrySetResult(description),
            error => tcs.TrySetException(new InvalidOperationException($"WebRTC set local description failed: {error}")));
        InvokeRequired(_peerConnection!, "SetLocalDescription", observer, description);
        return AwaitWithCancellationAsync(tcs.Task, cancellationToken);
    }

    private Task SetRemoteDescriptionAsync(string typeName, string sdp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sdp)) throw new ArgumentException("SDP is required.", nameof(sdp));

        var sessionType = RequiredType("Org.Webrtc.SessionDescription");
        var enumType = sessionType.GetNestedType("Type", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SessionDescription.Type was not found.");
        var typeValue = Enum.Parse(enumType, typeName, ignoreCase: true);
        var description = Activator.CreateInstance(sessionType, typeValue, sdp)
            ?? throw new InvalidOperationException("SessionDescription could not be created.");

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new SdpObserverBridge(
            _ =>
            {
                _remoteDescriptionSet = true;
                while (_pendingRemoteIceCandidates.Count > 0)
                    AddIceCandidatePayload(_pendingRemoteIceCandidates.Dequeue());
                tcs.TrySetResult(description);
            },
            error => tcs.TrySetException(new InvalidOperationException($"WebRTC set remote description failed: {error}")));
        InvokeRequired(_peerConnection!, "SetRemoteDescription", observer, description);
        return AwaitWithCancellationAsync(tcs.Task, cancellationToken);
    }

    private void AddIceCandidatePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("ICE candidate payload is required.", nameof(payload));

        string? sdpMid;
        int sdpMLineIndex;
        string candidateSdp;

        if (payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            var dto = JsonSerializer.Deserialize<IceCandidatePayload>(payload)
                ?? throw new ArgumentException("ICE candidate payload is invalid.", nameof(payload));
            sdpMid = dto.SdpMid;
            sdpMLineIndex = dto.SdpMLineIndex;
            candidateSdp = dto.Candidate;
        }
        else
        {
            var parts = payload.Split('|', 3);
            if (parts.Length != 3 || !int.TryParse(parts[1], out sdpMLineIndex))
                throw new ArgumentException("ICE candidate payload is invalid.", nameof(payload));
            sdpMid = parts[0];
            candidateSdp = parts[2];
        }

        if (string.IsNullOrWhiteSpace(candidateSdp))
            throw new ArgumentException("ICE candidate SDP is required.", nameof(payload));

        var key = $"{sdpMid}|{sdpMLineIndex}|{candidateSdp}";
        if (!_remoteIceCandidateKeys.Add(key))
            return;

        var candidateType = RequiredType("Org.Webrtc.IceCandidate");
        var candidate = Activator.CreateInstance(candidateType, sdpMid ?? string.Empty, sdpMLineIndex, candidateSdp)
            ?? throw new InvalidOperationException("ICE candidate could not be created.");
        InvokeRequired(_peerConnection!, "AddIceCandidate", candidate);
    }

    private sealed record IceCandidatePayload(string? SdpMid, int SdpMLineIndex, string Candidate);

    private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) return await task.ConfigureAwait(false);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? GetSdpText(object description) =>
        description.GetType().GetProperty("Description", BindingFlags.Public | BindingFlags.Instance)?.GetValue(description)?.ToString();

    private sealed class SdpObserverBridge : Java.Lang.Object, Org.Webrtc.ISdpObserver
    {
        private readonly Action<object> _onSuccess;
        private readonly Action<string> _onFailure;

        public SdpObserverBridge(Action<object> onSuccess, Action<string> onFailure)
        {
            _onSuccess = onSuccess;
            _onFailure = onFailure;
        }

        public void OnCreateSuccess(Org.Webrtc.SessionDescription? sdp)
        {
            if (sdp is not null) _onSuccess(sdp);
            else _onFailure("SDP creation returned null.");
        }

        public void OnSetSuccess() => _onSuccess(new object());
        public void OnCreateFailure(string? error) => _onFailure(error ?? "Unknown create failure.");
        public void OnSetFailure(string? error) => _onFailure(error ?? "Unknown set failure.");
    }

    private void InitializeAudio()
    {
        var factoryType = RequiredType("Org.Webrtc.PeerConnectionFactory");
        InitializeFactory(factoryType);
        var constraintsType = RequiredType("Org.Webrtc.MediaConstraints");
        var constraints = Activator.CreateInstance(constraintsType) ?? throw new InvalidOperationException("MediaConstraints could not be created.");
        _audioSource = InvokeRequired(_factory!, "CreateAudioSource", constraints);
        _audioTrack = InvokeRequired(_factory!, "CreateAudioTrack", "HimoAudioTrack", _audioSource);
        InvokeOptional(_audioTrack, "SetEnabled", true);
    }

    private void InitializeVideo()
    {
        var context = global::Android.App.Application.Context;
        var eglType = RequiredType("Org.Webrtc.EglBase");
        _eglBase = InvokeRequiredStatic(eglType, "Create");
        _eglContext = InvokeRequired(_eglBase, "GetEglBaseContext");

        var helperType = RequiredType("Org.Webrtc.SurfaceTextureHelper");
        _surfaceTextureHelper = InvokeRequiredStatic(helperType, "Create", "HimoCamera", _eglContext);

        var enumeratorType = RequiredType("Org.Webrtc.Camera2Enumerator");
        var enumerator = Activator.CreateInstance(enumeratorType, context)
            ?? throw new InvalidOperationException("Camera2Enumerator could not be created.");
        var names = (InvokeRequired(enumerator, "GetDeviceNames") as Array)?.Cast<object>().Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
                    ?? Array.Empty<string>();
        var front = names.FirstOrDefault(n => Convert.ToBoolean(InvokeRequired(enumerator, "IsFrontFacing", n)));
        _cameraDeviceName = front ?? names.FirstOrDefault() ?? throw new InvalidOperationException("No Android camera was found.");
        _capturer = InvokeRequired(enumerator, "CreateCapturer", _cameraDeviceName, null);

        _videoSource = InvokeRequired(_factory!, "CreateVideoSource", false);
        var observer = InvokeRequired(_videoSource, "GetCapturerObserver");
        InvokeRequired(_capturer, "Initialize", _surfaceTextureHelper, context, observer);
        InvokeRequired(_capturer, "StartCapture", 1280, 720, 30);
        _videoTrack = InvokeRequired(_factory!, "CreateVideoTrack", "HimoVideoTrack", _videoSource);
        InvokeOptional(_videoTrack, "SetEnabled", true);
    }

    private void InitializePeerConnection(long sessionGeneration)
    {
        var peerConnectionType = RequiredType("Org.Webrtc.PeerConnection");
        var rtcConfigurationType = RequiredType("Org.Webrtc.PeerConnection+RtcConfiguration");
        var iceServerType = RequiredType("Org.Webrtc.PeerConnection+IceServer");
        var iceServers = Activator.CreateInstance(typeof(List<>).MakeGenericType(iceServerType))
            ?? throw new InvalidOperationException("ICE server list could not be created.");
        var builderMethod = iceServerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "Builder", StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == 1);
        if (builderMethod is not null)
        {
            var builder = builderMethod.Invoke(null, new object?[] { "stun:stun.l.google.com:19302" });
            var builtStun = InvokeOptional(builder, "CreateIceServer");
            if (builtStun is not null)
                InvokeOptional(iceServers, "Add", builtStun);
        }
        var configuration = Activator.CreateInstance(rtcConfigurationType, iceServers)
            ?? throw new InvalidOperationException("RTC configuration could not be created.");

        // Stage 43: connect the native PeerConnection observer so locally
        // gathered ICE candidates can be forwarded through the existing signal path.
        // CreatePeerConnection is a PeerConnectionFactory method, not a PeerConnection instance method.
        var factoryType = _factory!.GetType();
        var method = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => string.Equals(m.Name, "CreatePeerConnection", StringComparison.OrdinalIgnoreCase)
                && m.GetParameters().Length == 2);
        if (method is null)
            throw new MissingMethodException(factoryType.FullName, "CreatePeerConnection");

        _peerConnectionObserver = new PeerConnectionObserverBridge(
            candidate =>
            {
                if (!IsCurrentSession(sessionGeneration)) return;
                IceCandidateGenerated?.Invoke(this, candidate);
            },
            track =>
            {
                if (!IsCurrentSession(sessionGeneration) || track is null) return;
                if (string.Equals(track.GetType().Name, "AudioTrack", StringComparison.OrdinalIgnoreCase))
                    SetRemoteAudioTrack(track);
                else
                    SetRemoteVideoTrack(track);
            },
            state =>
            {
                if (!IsCurrentSession(sessionGeneration)) return;

                _lastIceConnectionState = state;
                IceConnectionStateChanged?.Invoke(this, state);
                NotifyConnectionEstablishedChanged();
            },
            state =>
            {
                if (!IsCurrentSession(sessionGeneration)) return;

                _lastPeerConnectionState = state;
                PeerConnectionStateChanged?.Invoke(this, state);
                NotifyConnectionEstablishedChanged();
            });

        _peerConnection = method.Invoke(_factory, new[] { configuration, (object?)_peerConnectionObserver })
            ?? throw new InvalidOperationException("Native PeerConnection could not be created.");

        var streamIds = new List<string> { "HimoStream" };
        if (_audioTrack is not null)
            InvokeOptional(_peerConnection, "AddTrack", _audioTrack, streamIds);
        if (_videoTrack is not null)
            InvokeOptional(_peerConnection, "AddTrack", _videoTrack, streamIds);
    }

    private static readonly object FactoryInitializationGate = new();
    private static bool FactoryInitialized;

    private void InitializeFactory(Type factoryType)
    {
        EnsurePeerConnectionFactoryInitialized(factoryType);

        var builderType = RequiredType("Org.Webrtc.PeerConnectionFactory+Builder");
        var builder = Activator.CreateInstance(builderType)
            ?? throw new InvalidOperationException("WebRTC PeerConnectionFactory.Builder could not be created.");

        _factory = InvokeRequired(builder, "CreatePeerConnectionFactory");
    }

    private static void EnsurePeerConnectionFactoryInitialized(Type factoryType)
    {
        lock (FactoryInitializationGate)
        {
            if (FactoryInitialized) return;

            var context = global::Android.App.Application.Context;
            if (context is null)
                throw new InvalidOperationException("Android application context is unavailable for WebRTC initialization.");

            // Modern libwebrtc requires PeerConnectionFactory.initialize(...) before
            // the first factory is created. Older bindings exposed only
            // initializeAndroidGlobals(...), so keep a compatible fallback.
            var initialize = factoryType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, "Initialize", StringComparison.OrdinalIgnoreCase)
                    && m.GetParameters().Length == 1);

            if (initialize is not null)
            {
                var options = CreateInitializationOptions(factoryType, context);
                initialize.Invoke(null, new[] { options });
                FactoryInitialized = true;
                return;
            }

            var legacy = factoryType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, "InitializeAndroidGlobals", StringComparison.OrdinalIgnoreCase));
            if (legacy is not null)
            {
                var parameters = legacy.GetParameters();
                if (parameters.Length == 0)
                    legacy.Invoke(null, null);
                else if (parameters.Length == 1)
                    legacy.Invoke(null, new object?[] { context });
                else
                    throw new MissingMethodException(factoryType.FullName, "InitializeAndroidGlobals");

                FactoryInitialized = true;
                return;
            }

            throw new MissingMethodException(factoryType.FullName, "Initialize");
        }
    }

    private static object CreateInitializationOptions(Type factoryType, global::Android.Content.Context context)
    {
        var optionsType = factoryType.GetNestedType("InitializationOptions", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WebRTC InitializationOptions type was not found in the Android binding.");

        var builderType = optionsType.GetNestedType("Builder", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WebRTC InitializationOptions.Builder type was not found in the Android binding.");

        object? builder = null;
        try { builder = Activator.CreateInstance(builderType, context); } catch { }
        builder ??= Activator.CreateInstance(builderType);
        if (builder is null)
            throw new InvalidOperationException("WebRTC InitializationOptions.Builder could not be created.");

        // These setters are optional across WebRTC releases. The application context
        // constructor is the stable part of the Android API.
        InvokeOptional(builder, "SetEnableInternalTracer", false);
        InvokeOptional(builder, "SetFieldTrials", string.Empty);

        return InvokeRequired(builder, "CreateInitializationOptions");
    }

    private object AttachRenderer(VisualElement host, bool mirror, object? existing)
    {
        var platformHost = host.Handler?.PlatformView as global::Android.Views.ViewGroup
            ?? throw new InvalidOperationException("Video host is not ready on Android.");

        if (existing is global::Android.Views.View existingView)
        {
            if (existingView.Parent is global::Android.Views.ViewGroup currentParent && !ReferenceEquals(currentParent, platformHost))
                currentParent.RemoveView(existingView);

            if (!ReferenceEquals(existingView.Parent, platformHost))
            {
                existingView.LayoutParameters = new global::Android.Views.ViewGroup.LayoutParams(-1, -1);
                platformHost.AddView(existingView);
            }

            return existing;
        }

        var rendererType = RequiredType("Org.Webrtc.SurfaceViewRenderer");
        var renderer = Activator.CreateInstance(rendererType, global::Android.App.Application.Context)
            ?? throw new InvalidOperationException("SurfaceViewRenderer could not be created.");
        InvokeOptional(renderer, "SetEnableHardwareScaler", true);
        InvokeRequired(renderer, "Init", _eglContext!, null);
        InvokeOptional(renderer, "SetMirror", mirror);
        if (renderer is global::Android.Views.View nativeView)
        {
            nativeView.LayoutParameters = new global::Android.Views.ViewGroup.LayoutParams(-1, -1);
            platformHost.AddView(nativeView);
        }
        return renderer;
    }


    private Assembly LoadBinding() => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, NativeAssemblyName, StringComparison.OrdinalIgnoreCase))
        ?? Assembly.Load(NativeAssemblyName);

    private Type RequiredType(string name) => _assembly?.GetType(name, false, false)
        ?? throw new InvalidOperationException($"WebRTC binding type '{name}' was not found.");

    private static object InvokeRequired(object target, string name, params object?[] args)
    {
        var method = FindMethod(target.GetType(), name, args.Length, false) ?? throw new MissingMethodException(target.GetType().FullName, name);
        return method.Invoke(target, args) ?? throw new InvalidOperationException($"WebRTC method '{name}' returned null.");
    }

    private static object InvokeRequiredStatic(Type type, string name, params object?[] args)
    {
        var method = FindMethod(type, name, args.Length, true) ?? throw new MissingMethodException(type.FullName, name);
        return method.Invoke(null, args) ?? throw new InvalidOperationException($"WebRTC static method '{name}' returned null.");
    }

    private static object? InvokeOptional(object? target, string name, params object?[] args)
    {
        if (target is null) return null;
        var method = FindMethod(target.GetType(), name, args.Length, false);
        return method?.Invoke(target, args);
    }

    private static MethodInfo? FindMethod(Type type, string name, int count, bool isStatic) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && m.IsStatic == isStatic && m.GetParameters().Length == count);

    private void DisposeNativeObjects()
    {
        if (_capturer is not null) InvokeOptional(_capturer, "StopCapture");
        if (_videoTrack is not null && _localRenderer is not null)
            InvokeOptional(_videoTrack, "RemoveSink", _localRenderer);
        if (_localRenderer is not null)
            InvokeOptional(_localRenderer, "ClearImage");

        if (_remoteVideoTrack is not null && _remoteRenderer is not null)
            InvokeOptional(_remoteVideoTrack, "RemoveSink", _remoteRenderer);
        if (_remoteAudioTrack is not null)
            InvokeOptional(_remoteAudioTrack, "SetEnabled", false);
        if (_remoteRenderer is not null)
            InvokeOptional(_remoteRenderer, "ClearImage");

        RemoveRenderer(_localRenderer);
        RemoveRenderer(_remoteRenderer);
        TryDispose(_capturer); TryDispose(_videoTrack); TryDispose(_videoSource); TryDispose(_surfaceTextureHelper);
        TryDispose(_peerConnection);
        TryDispose(_peerConnectionObserver);
        TryDispose(_audioTrack); TryDispose(_audioSource); TryDispose(_factory); TryDispose(_eglBase);
        _capturer = _videoTrack = _videoSource = _surfaceTextureHelper = _audioTrack = _audioSource = _factory = _peerConnection = _eglBase = _eglContext = null;
        _localRenderer = _remoteRenderer = _remoteVideoTrack = _remoteAudioTrack = null;
        _peerConnectionObserver = null;
        _pendingRemoteIceCandidates.Clear();
        _remoteIceCandidateKeys.Clear();
        _remoteDescriptionSet = false;
        _cameraDeviceName = null;
        _microphoneEnabled = false;
        _remoteAudioEnabled = false;
    }

    private static void RemoveRenderer(object? renderer)
    {
        if (renderer is global::Android.Views.View view && view.Parent is global::Android.Views.ViewGroup parent)
            parent.RemoveView(view);
        TryDispose(renderer);
    }

    private static void TryDispose(object? value)
    {
        if (value is null) return;
        try { value.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)?.Invoke(value, null); } catch { }
    }

    private void ResetConnectionState()
    {
        _lastIceConnectionState = null;
        _lastPeerConnectionState = null;
        _lastConnectionEstablished = false;
    }

    private bool IsCurrentSession(long sessionGeneration) =>
        _started && sessionGeneration == _sessionGeneration;

    private void NotifyConnectionEstablishedChanged()
    {
        var established = IsConnectionEstablished;
        if (_lastConnectionEstablished == established) return;
        _lastConnectionEstablished = established;
        ConnectionEstablishedChanged?.Invoke(this, established);
    }

    private sealed class PeerConnectionObserverBridge : Java.Lang.Object, Org.Webrtc.PeerConnection.IObserver
    {
        private readonly Action<string> _onIceCandidate;
        private readonly Action<object?> _onRemoteTrack;
        private readonly Action<string> _onIceConnectionStateChanged;
        private readonly Action<string> _onPeerConnectionStateChanged;

        public PeerConnectionObserverBridge(
            Action<string> onIceCandidate,
            Action<object?> onRemoteTrack,
            Action<string> onIceConnectionStateChanged,
            Action<string> onPeerConnectionStateChanged)
        {
            _onIceCandidate = onIceCandidate;
            _onRemoteTrack = onRemoteTrack;
            _onIceConnectionStateChanged = onIceConnectionStateChanged;
            _onPeerConnectionStateChanged = onPeerConnectionStateChanged;
        }

        public void OnIceCandidate(Org.Webrtc.IceCandidate? candidate)
        {
            if (candidate is null) return;
            var payload = new IceCandidatePayload(
                candidate.SdpMid,
                candidate.SdpMLineIndex,
                candidate.Sdp ?? string.Empty);
            _onIceCandidate(JsonSerializer.Serialize(payload));
        }

        public void OnAddStream(Org.Webrtc.MediaStream? stream) { }
        public void OnRemoveStream(Org.Webrtc.MediaStream? stream) { }
        public void OnDataChannel(Org.Webrtc.DataChannel? channel) { }
        public void OnRenegotiationNeeded() { }
        public void OnIceConnectionChange(Org.Webrtc.PeerConnection.IceConnectionState? state)
        {
            if (state is not null) _onIceConnectionStateChanged(state.ToString() ?? string.Empty);
        }
        public void OnIceConnectionReceivingChange(bool receiving) { }
        public void OnIceGatheringChange(Org.Webrtc.PeerConnection.IceGatheringState? state) { }
        public void OnIceCandidatesRemoved(Org.Webrtc.IceCandidate[]? candidates) { }
        public void OnSignalingChange(Org.Webrtc.PeerConnection.SignalingState? state) { }
        public void OnConnectionChange(Org.Webrtc.PeerConnection.PeerConnectionState? state)
        {
            if (state is not null) _onPeerConnectionStateChanged(state.ToString() ?? string.Empty);
        }
        public void OnSelectedCandidatePairChanged(Org.Webrtc.CandidatePairChangeEvent? eventData) { }
        public void OnStandardizedIceConnectionChange(Org.Webrtc.PeerConnection.IceConnectionState? state) { }
        public void OnIceCandidateError(Org.Webrtc.IceCandidateErrorEvent? error) { }
        public void OnAddTrack(Org.Webrtc.RtpReceiver? receiver, Org.Webrtc.MediaStream[]? mediaStreams)
        {
            var track = receiver?.Track();
            if (track is not null) _onRemoteTrack(track);
        }
        public void OnRemoveTrack(Org.Webrtc.RtpReceiver? receiver) { }
        public void OnTrack(Org.Webrtc.RtpTransceiver? transceiver)
        {
            var track = transceiver?.Receiver?.Track();
            if (track is not null) _onRemoteTrack(track);
        }
    }

    public ValueTask DisposeAsync() { DisposeNativeObjects(); _started = false; _cameraEnabled = false; _microphoneEnabled = false; _mode = null; return ValueTask.CompletedTask; }
}
