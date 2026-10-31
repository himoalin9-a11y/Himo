using System.Reflection;
using System.Text.Json;
using Android.Runtime;
using Himo.Services;
using Microsoft.Maui.Controls;

namespace Himo.Platforms.Android.Services;

/// <summary>
/// Android WebRTC media engine.
///
/// All WebRTC binding calls are resolved through the generated binding.
/// The PeerConnectionFactory initialization method is invoked through
/// Java reflection only when the generated binding does not expose it.
/// No raw JNIEnv local/global reference management is used here.
/// </summary>
public sealed class AndroidWebRtcMediaEngine : IWebRtcMediaEngine
{
    private const string NativeAssemblyName = "FsWebRTC.Bindings.Maui.Android";

    private Assembly? _assembly;

    // Serialize PeerConnectionFactory creation between pre-warm and call startup.
    private readonly SemaphoreSlim _factoryGate = new(1, 1);

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
    private readonly HashSet<object> _initializedHostRenderers =
        new(ReferenceEqualityComparer.Instance);

    private object? _remoteVideoTrack;
    private object? _remoteAudioTrack;

    private string? _cameraDeviceName;

    private bool _started;
    private bool _cameraEnabled;
    private bool _videoPausedForBackground;
    private bool _microphoneEnabled;
    private bool _speakerEnabled;
    private bool _remoteAudioEnabled = true;

    private bool _remoteDescriptionSet;

    private readonly Queue<string> _pendingRemoteIceCandidates = new();

    private readonly HashSet<string> _remoteIceCandidateKeys =
        new(StringComparer.Ordinal);

    private CallMode? _mode;

    private string? _lastIceConnectionState;
    private string? _lastPeerConnectionState;

    private bool _lastConnectionEstablished;

    private long _sessionGeneration;

    private static readonly object FactoryInitializationGate = new();

    private static bool FactoryInitialized;

    public event EventHandler<string>? IceCandidateGenerated;

    public event EventHandler<string>? IceConnectionStateChanged;

    public event EventHandler<string>? PeerConnectionStateChanged;

    public event EventHandler<bool>? ConnectionEstablishedChanged;

    public bool IsStarted => _started;

    public CallMode? Mode => _mode;

    public bool IsCameraEnabled => _cameraEnabled;

    public bool IsMicrophoneEnabled => _microphoneEnabled;

    public bool IsSpeakerEnabled => _speakerEnabled;

    public bool IsRemoteAudioEnabled => _remoteAudioEnabled;

    public string? LastIceConnectionState =>
        _lastIceConnectionState;

    public string? LastPeerConnectionState =>
        _lastPeerConnectionState;

    public bool IsIceConnected =>
        string.Equals(
            _lastIceConnectionState,
            "Connected",
            StringComparison.OrdinalIgnoreCase)
        ||
        string.Equals(
            _lastIceConnectionState,
            "Completed",
            StringComparison.OrdinalIgnoreCase);

    public bool IsPeerConnectionConnected =>
        string.Equals(
            _lastPeerConnectionState,
            "Connected",
            StringComparison.OrdinalIgnoreCase);

    public bool IsConnectionEstablished =>
        IsIceConnected && IsPeerConnectionConnected;

    /// <summary>
    /// Loads the WebRTC binding and initializes only PeerConnectionFactory.
    /// This intentionally does not create audio tracks, request microphone capture,
    /// or create a PeerConnection. Running it while the incoming call is ringing
    /// removes the expensive one-time factory creation from the answer critical path.
    /// </summary>
    public async Task WarmUpFactoryAsync(CancellationToken cancellationToken = default)
    {
        if (_started || _factory is not null)
            return;

        await _factoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started || _factory is not null)
                return;

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                WebRtcJavaBindingKeepAlive.Ensure();
                _assembly = LoadBinding();
                InitializeFactory(RequiredType("Org.Webrtc.PeerConnectionFactory"));
            }, cancellationToken).ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine("[Himo WebRTC] PeerConnectionFactory warmed before answer.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Factory warm-up skipped: {ex.Message}");
            throw;
        }
        finally
        {
            _factoryGate.Release();
        }
    }

    public async Task StartAsync(
        CallMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_started)
        {
            if (_mode == mode)
                return;

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

        await _factoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // StopAsync can invalidate this start while it waits for the same gate.
            if (sessionGeneration != _sessionGeneration)
                return;

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                WebRtcJavaBindingKeepAlive.Ensure();
                _assembly = LoadBinding();
                try
                {
                    InitializeAudio();
                    if (mode == CallMode.Video)
                        InitializeVideo();
                    cancellationToken.ThrowIfCancellationRequested();
                    InitializePeerConnection(sessionGeneration, mode);
                }
                catch
                {
                    DisposeNativeObjects();
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (sessionGeneration != _sessionGeneration)
            {
                DisposeNativeObjects();
                return;
            }

            _mode = mode;
            _started = true;
            _cameraEnabled = mode == CallMode.Video;
            _microphoneEnabled = true;
            _speakerEnabled = false;
        }
        finally
        {
            _factoryGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Invalidate an in-progress startup before waiting for the initialization gate.
        ++_sessionGeneration;
        await _factoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        }
        finally
        {
            _factoryGate.Release();
        }
    }

    public Task SetMicrophoneEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_audioTrack is null)
            return Task.CompletedTask;

        if (_microphoneEnabled == enabled)
            return Task.CompletedTask;

        InvokeOptional(
            _audioTrack,
            "SetEnabled",
            enabled);

        _microphoneEnabled = enabled;

        return Task.CompletedTask;
    }

    public Task SetCameraEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started ||
            _mode != CallMode.Video ||
            _capturer is null)
        {
            return Task.CompletedTask;
        }

        if (enabled && !_cameraEnabled)
        {
            InvokeRequired(
                _capturer,
                "StartCapture",
                1280,
                720,
                30);

            InvokeOptional(
                _videoTrack,
                "SetEnabled",
                true);

            _cameraEnabled = true;
        }
        else if (!enabled && _cameraEnabled)
        {
            InvokeOptional(
                _capturer,
                "StopCapture");

            InvokeOptional(
                _videoTrack,
                "SetEnabled",
                false);

            _cameraEnabled = false;
        }

        return Task.CompletedTask;
    }

    public Task PauseVideoForBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode != CallMode.Video)
            return Task.CompletedTask;

        try
        {
            if (_capturer is not null && _cameraEnabled)
                InvokeOptional(_capturer, "StopCapture");
            if (_videoTrack is not null)
                InvokeOptional(_videoTrack, "SetEnabled", false);
            _cameraEnabled = false;

            // Disable received video decoding/rendering too. Audio tracks remain active.
            if (_remoteVideoTrack is not null)
                InvokeOptional(_remoteVideoTrack, "SetEnabled", false);
            _videoPausedForBackground = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Could not pause background video: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task ResumeVideoAfterBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode != CallMode.Video || !_videoPausedForBackground)
            return Task.CompletedTask;

        try
        {
            // Resume incoming video in the foreground, but leave the local camera off
            // until the user explicitly taps the camera control.
            if (_remoteVideoTrack is not null)
                InvokeOptional(_remoteVideoTrack, "SetEnabled", true);
            _videoPausedForBackground = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] Could not resume foreground video: {ex.Message}");
        }

        return Task.CompletedTask;
    }
    public Task SwitchCameraAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started ||
            _mode != CallMode.Video ||
            _capturer is null ||
            _videoSource is null ||
            _surfaceTextureHelper is null)
        {
            return Task.CompletedTask;
        }

        var context =
            global::Android.App.Application.Context;

        var enumeratorType =
            RequiredType(
                "Org.Webrtc.Camera2Enumerator");

        var enumerator =
            Activator.CreateInstance(
                enumeratorType,
                context)
            ?? throw new InvalidOperationException(
                "Camera2Enumerator could not be created.");

        try
        {
            var names =
                (InvokeRequired(
                    enumerator,
                    "GetDeviceNames") as Array)
                ?.Cast<object>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray()
                ?? Array.Empty<string>();

            if (names.Length == 0)
                return Task.CompletedTask;

            var target =
                names.FirstOrDefault(
                    n => !string.Equals(
                        n,
                        _cameraDeviceName,
                        StringComparison.Ordinal));

            if (target is null)
                return Task.CompletedTask;

            var wasEnabled = _cameraEnabled;

            InvokeOptional(
                _capturer,
                "StopCapture");

            TryDispose(_capturer);

            _capturer = null;

            var newCapturer =
                InvokeRequired(
                    enumerator,
                    "CreateCapturer",
                    target,
                    null);

            var observer =
                GetRequiredPropertyValue(
                    _videoSource,
                    "CapturerObserver",
                    "GetCapturerObserver");

            InvokeRequired(
                newCapturer,
                "Initialize",
                _surfaceTextureHelper,
                context,
                observer);

            _capturer = newCapturer;

            _cameraDeviceName = target;

            if (wasEnabled)
            {
                InvokeRequired(
                    _capturer,
                    "StartCapture",
                    1280,
                    720,
                    30);

                InvokeOptional(
                    _videoTrack,
                    "SetEnabled",
                    true);
            }
            else
            {
                InvokeOptional(
                    _videoTrack,
                    "SetEnabled",
                    false);
            }

            _cameraEnabled = wasEnabled;

            if (_videoTrack is not null &&
                _localRenderer is not null)
            {
                InvokeCompatibleOptional(
                    _videoTrack,
                    "RemoveSink",
                    _localRenderer);

                var reboundSink = InvokeCompatibleOptional(
                    _videoTrack,
                    "AddSink",
                    _localRenderer);
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Local preview sink rebound after camera switch: methodFound={reboundSink.MethodFound}; success={reboundSink.Success}.");
            }

            return Task.CompletedTask;
        }
        finally
        {
            TryDispose(enumerator);
        }
    }

    public Task AttachLocalVideoAsync(
        VisualElement host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_mode != CallMode.Video)
            return Task.CompletedTask;

        _localRenderer =
            AttachRenderer(
                host,
                true,
                _localRenderer);

        if (_videoTrack is not null)
        {
            InvokeCompatibleOptional(
                _videoTrack,
                "RemoveSink",
                _localRenderer);

            var localSink = InvokeCompatibleOptional(
                _videoTrack,
                "AddSink",
                _localRenderer);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local preview sink binding: methodFound={localSink.MethodFound}; success={localSink.Success}.");
        }

        return Task.CompletedTask;
    }

    public Task AttachRemoteVideoAsync(
        VisualElement host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_mode != CallMode.Video)
            return Task.CompletedTask;

        if (_videoPausedForBackground && _remoteVideoTrack is not null)
        {
            InvokeOptional(_remoteVideoTrack, "SetEnabled", true);
            _videoPausedForBackground = false;
        }

        _remoteRenderer = AttachRenderer(host, false, _remoteRenderer);
        BindRemoteVideoTrack();
        return Task.CompletedTask;
    }
    public void SetRemoteAudioTrack(
        object? track)
    {
        if (ReferenceEquals(
            _remoteAudioTrack,
            track))
        {
            if (_remoteAudioTrack is not null)
            {
                InvokeOptional(
                    _remoteAudioTrack,
                    "SetEnabled",
                    _remoteAudioEnabled);
            }

            return;
        }

        _remoteAudioTrack = track;

        if (_remoteAudioTrack is not null)
        {
            InvokeOptional(
                _remoteAudioTrack,
                "SetEnabled",
                _remoteAudioEnabled);
        }
    }

    public void SetRemoteVideoTrack(
        object? track)
    {
        if (ReferenceEquals(_remoteVideoTrack, track))
            return;

        if (_remoteVideoTrack is not null && _remoteRenderer is not null)
        {
            var removed = InvokeCompatibleOptional(
                _remoteVideoTrack,
                "RemoveSink",
                _remoteRenderer);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Previous remote video sink detached: found={removed.MethodFound}; success={removed.Success}.");
        }

        _remoteVideoTrack = track;

        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Remote video track changed: {(track is null ? "null" : track.GetType().FullName)}; rendererReady={_remoteRenderer is not null}; paused={_videoPausedForBackground}");

        if (_remoteVideoTrack is not null && !_videoPausedForBackground)
            InvokeOptional(_remoteVideoTrack, "SetEnabled", true);

        BindRemoteVideoTrack();
    }

    private void BindRemoteVideoTrack()
    {
        if (_remoteRenderer is null)
            return;

        if (_remoteVideoTrack is null)
        {
            InvokeOptional(
                _remoteRenderer,
                "ClearImage");

            return;
        }

        var removed = InvokeCompatibleOptional(
            _remoteVideoTrack,
            "RemoveSink",
            _remoteRenderer);
        var added = InvokeCompatibleOptional(
            _remoteVideoTrack,
            "AddSink",
            _remoteRenderer);

        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Remote video sink binding: track={_remoteVideoTrack.GetType().Name}; renderer={_remoteRenderer.GetType().Name}; addMethodFound={added.MethodFound}; addSucceeded={added.Success}; previousSinkRemoved={removed.Success}; paused={_videoPausedForBackground}");
    }

    public async Task<string?> CreateOfferAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsurePeerConnection();

        var constraints =
            CreateSdpConstraints();

        var description =
            await CreateSessionDescriptionAsync(
                "CreateOffer",
                constraints,
                cancellationToken)
            .ConfigureAwait(false);

        await SetLocalDescriptionAsync(
                description,
                cancellationToken)
            .ConfigureAwait(false);

        var sdp = GetSdpText(description);
        LogSdpMediaSummary("LOCAL OFFER", sdp);
        return sdp;
    }

    public async Task<string?> CreateAnswerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsurePeerConnection();

        var constraints =
            CreateSdpConstraints();

        var description =
            await CreateSessionDescriptionAsync(
                "CreateAnswer",
                constraints,
                cancellationToken)
            .ConfigureAwait(false);

        await SetLocalDescriptionAsync(
                description,
                cancellationToken)
            .ConfigureAwait(false);

        var sdp = GetSdpText(description);
        LogSdpMediaSummary("LOCAL ANSWER", sdp);
        return sdp;
    }

    public Task SetRemoteOfferAsync(
        string sdp,
        CancellationToken cancellationToken = default)
    {
        return SetRemoteDescriptionAsync(
            "Offer",
            sdp,
            cancellationToken);
    }

    public Task SetRemoteAnswerAsync(
        string sdp,
        CancellationToken cancellationToken = default)
    {
        return SetRemoteDescriptionAsync(
            "Answer",
            sdp,
            cancellationToken);
    }

    public async Task AddRemoteIceCandidateAsync(
        string candidatePayload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsurePeerConnection();

        if (string.IsNullOrWhiteSpace(candidatePayload))
            return;

        if (!_remoteDescriptionSet)
        {
            var queuedCandidate =
                GetIceCandidateTextFromPayload(candidatePayload);

            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Remote ICE candidate queued before remote description: " +
                $"type={GetIceCandidateType(queuedCandidate)}; protocol={GetIceCandidateProtocol(queuedCandidate)}");

            _pendingRemoteIceCandidates.Enqueue(
                candidatePayload);

            return;
        }

        AddIceCandidatePayload(
            candidatePayload);

        await Task.CompletedTask;
    }

    public Task SetRemoteAudioEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _remoteAudioEnabled = enabled;

        if (_remoteAudioTrack is not null)
        {
            InvokeOptional(
                _remoteAudioTrack,
                "SetEnabled",
                enabled);
        }

        return Task.CompletedTask;
    }

    public Task SetSpeakerEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _speakerEnabled = enabled;

        return Task.CompletedTask;
    }

    private void EnsurePeerConnection()
    {
        if (!_started ||
            _peerConnection is null)
        {
            throw new InvalidOperationException(
                "WebRTC PeerConnection is not started.");
        }
    }

    private object CreateSdpConstraints()
    {
        var constraintsType =
            RequiredType(
                "Org.Webrtc.MediaConstraints");

        var constraints =
            Activator.CreateInstance(
                constraintsType)
            ?? throw new InvalidOperationException(
                "MediaConstraints could not be created.");

        if (_mode == CallMode.Video)
        {
            var pairType =
                RequiredType(
                    "Org.Webrtc.MediaConstraints+KeyValuePair");

            var receiveVideo =
                Activator.CreateInstance(
                    pairType,
                    "OfferToReceiveVideo",
                    "true");

            var receiveAudio =
                Activator.CreateInstance(
                    pairType,
                    "OfferToReceiveAudio",
                    "true");

            if (receiveVideo is not null)
            {
                InvokeOptionalCollectionAdd(
                    constraints,
                    "Mandatory",
                    receiveVideo);
            }

            if (receiveAudio is not null)
            {
                InvokeOptionalCollectionAdd(
                    constraints,
                    "Mandatory",
                    receiveAudio);
            }
        }

        return constraints;
    }

    private static void InvokeOptionalCollectionAdd(
        object target,
        string propertyName,
        object value)
    {
        var property =
            target.GetType().GetProperty(
                propertyName,
                BindingFlags.Public |
                BindingFlags.Instance);

        var collection =
            property?.GetValue(target);

        if (collection is null)
            return;

        InvokeOptional(
            collection,
            "Add",
            value);
    }

    private Task<object> CreateSessionDescriptionAsync(
        string operation,
        object constraints,
        CancellationToken cancellationToken)
    {
        var tcs =
            new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var observer =
            new SdpObserverBridge(
                created =>
                {
                    tcs.TrySetResult(created);
                },
                error =>
                {
                    tcs.TrySetException(
                        new InvalidOperationException(
                            $"WebRTC {operation} failed: {error}"));
                });

        var method =
            FindMethod(
                _peerConnection!.GetType(),
                operation,
                2,
                false)
            ?? throw new MissingMethodException(
                _peerConnection.GetType().FullName ??
                "PeerConnection",
                operation);

        method.Invoke(
            _peerConnection,
            new object?[]
            {
                observer,
                constraints
            });

        return AwaitWithCancellationAsync(
            tcs.Task,
            cancellationToken);
    }

    private Task SetLocalDescriptionAsync(
        object description,
        CancellationToken cancellationToken)
    {
        var tcs =
            new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var observer =
            new SdpObserverBridge(
                _ =>
                {
                    tcs.TrySetResult(
                        description);
                },
                error =>
                {
                    tcs.TrySetException(
                        new InvalidOperationException(
                            $"WebRTC set local description failed: {error}"));
                });

        InvokeRequired(
            _peerConnection!,
            "SetLocalDescription",
            observer,
            description);

        return AwaitWithCancellationAsync(
            tcs.Task,
            cancellationToken);
    }

    private Task SetRemoteDescriptionAsync(
        string typeName,
        string sdp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sdp))
        {
            throw new ArgumentException(
                "SDP is required.",
                nameof(sdp));
        }

        LogSdpMediaSummary($"REMOTE {typeName.ToUpperInvariant()}", sdp);

        var sessionType =
            RequiredType(
                "Org.Webrtc.SessionDescription");

        var enumType =
            sessionType.GetNestedType(
                "Type",
                BindingFlags.Public |
                BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "SessionDescription.Type was not found.");

        // FsWebRTC's Android binding exposes SessionDescription.Type as a
        // Java enum wrapper class, not necessarily a CLR System.Enum. Calling
        // Enum.Parse unconditionally throws "Type provided must be an Enum"
        // on that binding. Resolve the actual generated enum value instead.
        var typeValue =
            ResolveSessionDescriptionType(
                enumType,
                typeName);

        var description =
            Activator.CreateInstance(
                sessionType,
                typeValue,
                sdp)
            ?? throw new InvalidOperationException(
                "SessionDescription could not be created.");

        var tcs =
            new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var observer =
            new SdpObserverBridge(
                _ =>
                {
                    _remoteDescriptionSet = true;

                    while (
                        _pendingRemoteIceCandidates.Count > 0)
                    {
                        AddIceCandidatePayload(
                            _pendingRemoteIceCandidates.Dequeue());
                    }

                    tcs.TrySetResult(
                        description);
                },
                error =>
                {
                    tcs.TrySetException(
                        new InvalidOperationException(
                            $"WebRTC set remote description failed: {error}"));
                });

        InvokeRequired(
            _peerConnection!,
            "SetRemoteDescription",
            observer,
            description);

        return AwaitWithCancellationAsync(
            tcs.Task,
            cancellationToken);
    }

    private static object ResolveSessionDescriptionType(
        Type typeClass,
        string typeName)
    {
        if (typeClass.IsEnum)
        {
            return Enum.Parse(
                typeClass,
                typeName,
                ignoreCase: true);
        }

        const BindingFlags staticFlags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static |
            BindingFlags.FlattenHierarchy;

        // Java enum constants in Xamarin/.NET Android bindings are commonly
        // projected as static fields or static properties on a Java.Lang.Enum
        // subclass. They are objects, not CLR enum members.
        var field = typeClass.GetFields(staticFlags).FirstOrDefault(
            member => string.Equals(
                member.Name,
                typeName,
                StringComparison.OrdinalIgnoreCase));

        if (field is not null)
        {
            var value = field.GetValue(null);
            if (value is not null && typeClass.IsInstanceOfType(value))
                return value;
        }

        var property = typeClass.GetProperties(staticFlags).FirstOrDefault(
            member =>
                string.Equals(
                    member.Name,
                    typeName,
                    StringComparison.OrdinalIgnoreCase)
                && member.GetIndexParameters().Length == 0
                && member.GetGetMethod(true)?.IsStatic == true);

        if (property is not null)
        {
            var value = property.GetValue(null);
            if (value is not null && typeClass.IsInstanceOfType(value))
                return value;
        }

        // Some generated bindings expose Java's valueOf(String) factory
        // instead of directly exposing enum constants.
        var valueOf = typeClass.GetMethods(staticFlags).FirstOrDefault(
            method =>
                (string.Equals(method.Name, "ValueOf", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(method.Name, "valueOf", StringComparison.OrdinalIgnoreCase))
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string));

        if (valueOf is not null)
        {
            var value = valueOf.Invoke(null, new object?[] { typeName });
            if (value is not null && typeClass.IsInstanceOfType(value))
                return value;
        }

        var members = typeClass.GetFields(staticFlags)
            .Select(member => member.Name)
            .Concat(typeClass.GetProperties(staticFlags)
                .Where(member => member.GetGetMethod(true)?.IsStatic == true)
                .Select(member => member.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        throw new InvalidOperationException(
            $"Could not resolve SessionDescription.Type '{typeName}' from binding type " +
            $"'{typeClass.FullName}'. CLR enum={typeClass.IsEnum}. " +
            $"Available static members: {string.Join(", ", members)}");
    }

    private void AddIceCandidatePayload(
        string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException(
                "ICE candidate payload is required.",
                nameof(payload));
        }

        string? sdpMid;

        int sdpMLineIndex;

        string candidateSdp;

        if (payload
            .TrimStart()
            .StartsWith(
                "{",
                StringComparison.Ordinal))
        {
            var dto =
                JsonSerializer.Deserialize<IceCandidatePayload>(
                    payload)
                ?? throw new ArgumentException(
                    "ICE candidate payload is invalid.",
                    nameof(payload));

            sdpMid = dto.SdpMid;

            sdpMLineIndex =
                dto.SdpMLineIndex;

            candidateSdp =
                dto.Candidate;
        }
        else
        {
            var parts =
                payload.Split(
                    '|',
                    3);

            if (parts.Length != 3 ||
                !int.TryParse(
                    parts[1],
                    out sdpMLineIndex))
            {
                throw new ArgumentException(
                    "ICE candidate payload is invalid.",
                    nameof(payload));
            }

            sdpMid = parts[0];

            candidateSdp = parts[2];
        }

        if (string.IsNullOrWhiteSpace(candidateSdp))
        {
            throw new ArgumentException(
                "ICE candidate SDP is required.",
                nameof(payload));
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Remote ICE candidate received: type={GetIceCandidateType(candidateSdp)}; " +
            $"protocol={GetIceCandidateProtocol(candidateSdp)}; mid={sdpMid ?? "(null)"}; mline={sdpMLineIndex}");

        var key =
            $"{sdpMid}|{sdpMLineIndex}|{candidateSdp}";

        if (!_remoteIceCandidateKeys.Add(key))
        {
            System.Diagnostics.Debug.WriteLine(
                "[Himo WebRTC] Duplicate remote ICE candidate ignored.");
            return;
        }

        var candidateType =
            RequiredType(
                "Org.Webrtc.IceCandidate");

        var candidate =
            Activator.CreateInstance(
                candidateType,
                sdpMid ?? string.Empty,
                sdpMLineIndex,
                candidateSdp)
            ?? throw new InvalidOperationException(
                "ICE candidate could not be created.");

        var addResult =
            InvokeRequired(
                _peerConnection!,
                "AddIceCandidate",
                candidate);

        bool accepted;
        bool hasAcceptanceResult;
        if (addResult is bool nativeBool)
        {
            accepted = nativeBool;
            hasAcceptanceResult = true;
        }
        else if (bool.TryParse(addResult?.ToString(), out var parsedBool))
        {
            accepted = parsedBool;
            hasAcceptanceResult = true;
        }
        else
        {
            accepted = false;
            hasAcceptanceResult = false;
        }

        if (hasAcceptanceResult)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Native AddIceCandidate result: accepted={accepted}; " +
                $"type={GetIceCandidateType(candidateSdp)}; protocol={GetIceCandidateProtocol(candidateSdp)}");

            if (!accepted)
            {
                throw new InvalidOperationException(
                    $"Native WebRTC rejected remote ICE candidate (type={GetIceCandidateType(candidateSdp)}, protocol={GetIceCandidateProtocol(candidateSdp)}).");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Native AddIceCandidate returned {addResult?.GetType().Name ?? "null/void"}; " +
                $"type={GetIceCandidateType(candidateSdp)}; protocol={GetIceCandidateProtocol(candidateSdp)}");
        }
    }

    private static string GetIceCandidateTextFromPayload(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<IceCandidatePayload>(payload)?.Candidate
                ?? payload;
        }
        catch
        {
            return payload;
        }
    }

    private static string GetIceCandidateType(string? candidateSdp)
    {
        if (string.IsNullOrWhiteSpace(candidateSdp))
            return "unknown";

        var parts = candidateSdp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "typ", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1].ToLowerInvariant();
        }

        return "unknown";
    }

    private static string GetIceCandidateProtocol(string? candidateSdp)
    {
        if (string.IsNullOrWhiteSpace(candidateSdp))
            return "unknown";

        var parts = candidateSdp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2].ToLowerInvariant() : "unknown";
    }

    private sealed record IceCandidatePayload(
        string? SdpMid,
        int SdpMLineIndex,
        string Candidate);

    private static async Task<T> AwaitWithCancellationAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return await task.ConfigureAwait(false);

        return await task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AddIceServer(
        Type iceServerType,
        object iceServers,
        string url,
        string? username = null,
        string? password = null)
    {
        var context =
            global::Android.App.Application.Context
            ?? throw new InvalidOperationException(
                "Android application context is unavailable for ICE server creation.");

        var classLoader =
            context.ClassLoader
            ?? throw new InvalidOperationException(
                "Android application ClassLoader is unavailable for ICE server creation.");

        // The Java AAR exposes PeerConnection.IceServer.builder(String), but
        // FsWebRTC 0.9.3.15 does not project that static method through the
        // generated managed binding. Use the exact Java API through the
        // application's ClassLoader, then wrap the returned IceServer in the
        // generated managed binding type.
        var iceServerClass =
            Java.Lang.Class.ForName(
                "org.webrtc.PeerConnection$IceServer",
                false,
                classLoader)
            ?? throw new InvalidOperationException(
                "Java PeerConnection.IceServer class could not be loaded.");

        var stringClass =
            Java.Lang.Class.ForName(
                "java.lang.String",
                false,
                classLoader)
            ?? throw new InvalidOperationException(
                "Java String class could not be loaded.");

        var builderMethod =
            iceServerClass.GetMethod(
                "builder",
                new Java.Lang.Class[]
                {
                    stringClass
                })
            ?? throw new MissingMethodException(
                "org.webrtc.PeerConnection$IceServer",
                "builder");

        var javaBuilder =
            builderMethod.Invoke(
                null,
                new Java.Lang.Object[]
                {
                    new Java.Lang.String(url)
                })
            as Java.Lang.Object
            ?? throw new InvalidOperationException(
                $"Java ICE server builder could not be created for {url}.");

        if (!string.IsNullOrWhiteSpace(username))
        {
            var setUsernameMethod =
                javaBuilder.Class.GetMethod(
                    "setUsername",
                    new Java.Lang.Class[]
                    {
                        stringClass
                    })
                ?? throw new MissingMethodException(
                    "org.webrtc.PeerConnection$IceServer$Builder",
                    "setUsername");

            _ = setUsernameMethod.Invoke(
                javaBuilder,
                new Java.Lang.Object[]
                {
                    new Java.Lang.String(username)
                });
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            var setPasswordMethod =
                javaBuilder.Class.GetMethod(
                    "setPassword",
                    new Java.Lang.Class[]
                    {
                        stringClass
                    })
                ?? throw new MissingMethodException(
                    "org.webrtc.PeerConnection$IceServer$Builder",
                    "setPassword");

            _ = setPasswordMethod.Invoke(
                javaBuilder,
                new Java.Lang.Object[]
                {
                    new Java.Lang.String(password)
                });
        }

        var createIceServerMethod =
            javaBuilder.Class.GetMethod(
                "createIceServer",
                Array.Empty<Java.Lang.Class>())
            ?? throw new MissingMethodException(
                "org.webrtc.PeerConnection$IceServer$Builder",
                "createIceServer");

        var javaIceServer =
            createIceServerMethod.Invoke(
                javaBuilder,
                Array.Empty<Java.Lang.Object>())
            as Java.Lang.Object
            ?? throw new InvalidOperationException(
                $"Java ICE server could not be created for {url}.");

        var managedIceServer =
            Java.Lang.Object.GetObject<Org.Webrtc.PeerConnection.IceServer>(
                javaIceServer.Handle,
                JniHandleOwnership.DoNotTransfer)
            ?? throw new InvalidOperationException(
                $"The Java ICE server could not be wrapped by the FsWebRTC managed binding for {url}.");

        var addMethod =
            iceServers.GetType().GetMethod(
                "Add",
                new[]
                {
                    iceServerType
                })
            ?? throw new MissingMethodException(
                iceServers.GetType().FullName ?? "List<PeerConnection.IceServer>",
                "Add");

        _ = addMethod.Invoke(
            iceServers,
            new object?[]
            {
                managedIceServer
            });
    }

    private static string? GetSdpText(
        object description)
    {
        return description
            .GetType()
            .GetProperty(
                "Description",
                BindingFlags.Public |
                BindingFlags.Instance)
            ?.GetValue(description)
            ?.ToString();
    }

    // Log only SDP media lines and directions. ICE addresses and credentials are omitted.
    private static void LogSdpMediaSummary(string label, string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            System.Diagnostics.Debug.WriteLine($"[Himo WebRTC] {label} SDP is empty.");
            return;
        }

        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var summaries = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("m=audio ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var media = parts[0].Length > 2 ? parts[0][2..] : "unknown";
            var port = parts.Length > 1 ? parts[1] : "?";
            var direction = "sendrecv";
            for (var j = i + 1; j < lines.Length && !lines[j].StartsWith("m=", StringComparison.OrdinalIgnoreCase); j++)
            {
                var attribute = lines[j].Trim();
                if (attribute is "a=sendrecv" or "a=sendonly" or "a=recvonly" or "a=inactive")
                {
                    direction = attribute[2..];
                    break;
                }
            }
            summaries.Add($"{media}:port={port},direction={direction}");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] {label} media: {(summaries.Count == 0 ? "no audio/video m-lines" : string.Join("; ", summaries))}");
    }

    private sealed class SdpObserverBridge :
        Java.Lang.Object,
        Org.Webrtc.ISdpObserver
    {
        private readonly Action<object> _onSuccess;

        private readonly Action<string> _onFailure;

        public SdpObserverBridge(
            Action<object> onSuccess,
            Action<string> onFailure)
        {
            _onSuccess = onSuccess;
            _onFailure = onFailure;
        }

        public void OnCreateSuccess(
            Org.Webrtc.SessionDescription? sdp)
        {
            if (sdp is not null)
            {
                _onSuccess(sdp);
            }
            else
            {
                _onFailure(
                    "SDP creation returned null.");
            }
        }

        public void OnSetSuccess()
        {
            _onSuccess(new object());
        }

        public void OnCreateFailure(
            string? error)
        {
            _onFailure(
                error ??
                "Unknown create failure.");
        }

        public void OnSetFailure(
            string? error)
        {
            _onFailure(
                error ??
                "Unknown set failure.");
        }
    }

    private void InitializeAudio()
    {
        var factoryType =
            RequiredType(
                "Org.Webrtc.PeerConnectionFactory");

        InitializeFactory(
            factoryType);

        var constraintsType =
            RequiredType(
                "Org.Webrtc.MediaConstraints");

        var constraints =
            Activator.CreateInstance(
                constraintsType)
            ?? throw new InvalidOperationException(
                "MediaConstraints could not be created.");

        // Explicitly request WebRTC acoustic echo cancellation and speech processing.
        var audioConstraintPairType = RequiredType("Org.Webrtc.MediaConstraints+KeyValuePair");
        var audioProcessingConstraints = new (string Key, string Value)[]
        {
            ("googEchoCancellation", "true"),
            ("googAutoGainControl", "true"),
            ("googNoiseSuppression", "true"),
            ("googHighpassFilter", "true")
        };
        foreach (var constraint in audioProcessingConstraints)
        {
            var pair = Activator.CreateInstance(audioConstraintPairType, constraint.Key, constraint.Value);
            if (pair is not null) InvokeOptionalCollectionAdd(constraints, "Mandatory", pair);
        }

        _audioSource =
            InvokeRequired(
                _factory!,
                "CreateAudioSource",
                constraints);

        _audioTrack =
            InvokeRequired(
                _factory!,
                "CreateAudioTrack",
                "HimoAudioTrack",
                _audioSource);

        InvokeOptional(
            _audioTrack,
            "SetEnabled",
            true);
    }

    private void InitializeVideo()
    {
        var context =
            global::Android.App.Application.Context;

        var eglType =
            RequiredType(
                "Org.Webrtc.EglBase");

        // InitializeFactory creates this EGL context before constructing the
        // video codec factories. Reuse it here instead of creating a second,
        // unrelated context for the camera's texture pipeline.
        _eglBase ??=
            InvokeRequiredStatic(
                eglType,
                "Create");

        // Android Java bindings map Java's getEglBaseContext() method to the
        // managed EglBaseContext property. Calling InvokeRequired("GetEglBaseContext")
        // fails because that CLR method does not exist in this binding, even though
        // the underlying Java API does expose getEglBaseContext().
        _eglContext ??=
            GetRequiredPropertyValue(
                _eglBase,
                "EglBaseContext",
                "GetEglBaseContext");

        var helperType =
            RequiredType(
                "Org.Webrtc.SurfaceTextureHelper");

        _surfaceTextureHelper =
            InvokeRequiredStatic(
                helperType,
                "Create",
                "HimoCamera",
                _eglContext);

        var enumeratorType =
            RequiredType(
                "Org.Webrtc.Camera2Enumerator");

        var enumerator =
            Activator.CreateInstance(
                enumeratorType,
                context)
            ?? throw new InvalidOperationException(
                "Camera2Enumerator could not be created.");

        try
        {
            var names =
                (InvokeRequired(
                    enumerator,
                    "GetDeviceNames") as Array)
                ?.Cast<object>()
                .Select(x => x?.ToString())
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray()
                ?? Array.Empty<string>();

            var front =
                names.FirstOrDefault(
                    n =>
                    Convert.ToBoolean(
                        InvokeRequired(
                            enumerator,
                            "IsFrontFacing",
                            n)));

            _cameraDeviceName =
                front ??
                names.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "No Android camera was found.");

            _capturer =
                InvokeRequired(
                    enumerator,
                    "CreateCapturer",
                    _cameraDeviceName,
                    null);
        }
        finally
        {
            TryDispose(enumerator);
        }

        _videoSource =
            InvokeRequired(
                _factory!,
                "CreateVideoSource",
                false);

        var observer =
            GetRequiredPropertyValue(
                _videoSource,
                "CapturerObserver",
                "GetCapturerObserver");

        InvokeRequired(
            _capturer!,
            "Initialize",
            _surfaceTextureHelper,
            context,
            observer);

        _videoTrack =
            InvokeRequired(
                _factory!,
                "CreateVideoTrack",
                "HimoVideoTrack",
                _videoSource);

        InvokeOptional(
            _videoTrack,
            "SetEnabled",
            true);

        // Start the camera only after the VideoTrack is fully connected to the source.
        // This avoids losing the first frames on Android WebRTC bindings that require
        // the source observer/track pipeline to be ready before capture begins.
        System.Diagnostics.Debug.WriteLine(
            $"[Himo WebRTC] Starting camera capture: {_cameraDeviceName}, 1280x720@30");
        InvokeRequired(
            _capturer,
            "StartCapture",
            1280,
            720,
            30);
        System.Diagnostics.Debug.WriteLine(
            "[Himo WebRTC] Camera capture started successfully.");
    }

    private void InitializePeerConnection(
        long sessionGeneration,
        CallMode mode)
    {
        var peerConnectionType =
            RequiredType(
                "Org.Webrtc.PeerConnection");

        var rtcConfigurationType =
            RequiredTypeAny(
                "Org.Webrtc.PeerConnection+RTCConfiguration",
                "Org.Webrtc.PeerConnection+RtcConfiguration");

        var iceServerType =
            RequiredType(
                "Org.Webrtc.PeerConnection+IceServer");

        var iceServers =
            Activator.CreateInstance(
                typeof(List<>)
                    .MakeGenericType(
                        iceServerType))
            ?? throw new InvalidOperationException(
                "ICE server list could not be created.");

        AddIceServer(
            iceServerType,
            iceServers,
            "stun:stun.l.google.com:19302");

        const string turnUsername =
            "65b1a764775f41fdf4890e7c";

        const string turnPassword =
            "rg2auX6WI2YrXkzi";

        AddIceServer(
            iceServerType,
            iceServers,
            "turn:global.relay.metered.ca:80",
            turnUsername,
            turnPassword);

        AddIceServer(
            iceServerType,
            iceServers,
            "turn:global.relay.metered.ca:80?transport=tcp",
            turnUsername,
            turnPassword);

        AddIceServer(
            iceServerType,
            iceServers,
            "turn:global.relay.metered.ca:443",
            turnUsername,
            turnPassword);

        AddIceServer(
            iceServerType,
            iceServers,
            "turns:global.relay.metered.ca:443?transport=tcp",
            turnUsername,
            turnPassword);

        var configuration =
            Activator.CreateInstance(
                rtcConfigurationType,
                iceServers)
            ?? throw new InvalidOperationException(
                "RTC configuration could not be created.");

        var factoryType =
            _factory!.GetType();

        _peerConnectionObserver =
            new PeerConnectionObserverBridge(
                candidate =>
                {
                    if (!IsCurrentSession(
                        sessionGeneration))
                    {
                        return;
                    }

                    var candidateText =
                        GetIceCandidateTextFromPayload(candidate);

                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] Local ICE candidate gathered: type={GetIceCandidateType(candidateText)}; " +
                        $"protocol={GetIceCandidateProtocol(candidateText)}");

                    IceCandidateGenerated?.Invoke(
                        this,
                        candidate);
                },
                track =>
                {
                    if (!IsCurrentSession(
                        sessionGeneration) ||
                        track is null)
                    {
                        return;
                    }

                    if (string.Equals(
                        track.GetType().Name,
                        "AudioTrack",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        SetRemoteAudioTrack(track);
                    }
                    else
                    {
                        SetRemoteVideoTrack(track);
                    }
                },
                state =>
                {
                    if (!IsCurrentSession(
                        sessionGeneration))
                    {
                        return;
                    }

                    _lastIceConnectionState =
                        state;

                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] ICE connection state changed: {state}");

                    IceConnectionStateChanged?.Invoke(
                        this,
                        state);

                    NotifyConnectionEstablishedChanged();
                },
                state =>
                {
                    if (!IsCurrentSession(
                        sessionGeneration))
                    {
                        return;
                    }

                    _lastPeerConnectionState =
                        state;

                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo WebRTC] PeerConnection state changed: {state}");

                    PeerConnectionStateChanged?.Invoke(
                        this,
                        state);

                    NotifyConnectionEstablishedChanged();
                });

        var observerType =
            _peerConnectionObserver.GetType();

        // WebRTC exposes several CreatePeerConnection overloads:
        //   (RTCConfiguration, Observer)
        //   (List<IceServer>, Observer)
        //   and 3-argument variants with MediaConstraints.
        // The previous reflection lookup selected the first 2-argument
        // overload, which on this binding was the List<IceServer> overload.
        // Select the overload whose first parameter accepts the actual
        // RTCConfiguration instance and whose second parameter accepts the
        // actual observer instance.
        var method =
            factoryType
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(
                    m =>
                        string.Equals(
                            m.Name,
                            "CreatePeerConnection",
                            StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(
                    m =>
                    {
                        var parameters =
                            m.GetParameters();

                        return parameters.Length == 2 &&
                               parameters[0].ParameterType.IsAssignableFrom(
                                   configuration.GetType()) &&
                               parameters[1].ParameterType.IsAssignableFrom(
                                   observerType);
                    });

        if (method is null)
        {
            var available =
                string.Join(
                    " | ",
                    factoryType
                        .GetMethods(
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        .Where(
                            m =>
                                string.Equals(
                                    m.Name,
                                    "CreatePeerConnection",
                                    StringComparison.OrdinalIgnoreCase))
                        .Select(
                            m =>
                                m.ToString()));

            throw new MissingMethodException(
                factoryType.FullName ??
                "PeerConnectionFactory",
                $"CreatePeerConnection(RTCConfiguration, Observer). Available overloads: {available}");
        }

        _peerConnection =
            method.Invoke(
                _factory,
                new object?[]
                {
                    configuration,
                    _peerConnectionObserver
                })
            ?? throw new InvalidOperationException(
                "Native PeerConnection could not be created.");

        // Use PeerConnection.AddTrack(MediaStreamTrack), a stable one-argument
        // overload present in the bundled libwebrtc AAR. The older two-argument
        // reflection call (track + List<string>) swallowed invocation failures,
        // which allowed local camera preview without actually sending that track.
        if (_audioTrack is not null)
        {
            var audioSender = InvokeRequired(_peerConnection, "AddTrack", _audioTrack);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local audio sender registered: sender={audioSender.GetType().Name}; track={_audioTrack.GetType().Name}.");
        }
        else
        {
            throw new InvalidOperationException("Local audio track was not initialized; cannot create a media PeerConnection.");
        }

        if (_videoTrack is not null)
        {
            var videoSender = InvokeRequired(_peerConnection, "AddTrack", _videoTrack);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Local video sender registered: sender={videoSender.GetType().Name}; track={_videoTrack.GetType().Name}; mode={mode}.");
            ConfigureVideoSenderQuality(videoSender);
        }
        else if (mode == CallMode.Video)
        {
            throw new InvalidOperationException("Video call mode is active, but no local VideoTrack exists to send.");
        }
    }

    // The local preview can look sharp while the remote peer receives a heavily
    // quantized stream. Apply an explicit sender bitrate ceiling to the video
    // encoding, without changing capture, audio, signaling, or renderer behavior.
    private static void ConfigureVideoSenderQuality(object videoSender)
    {
        const int maxVideoBitrateBps = 2_500_000;

        try
        {
            var parameters = InvokeRequired(videoSender, "GetParameters");
            var encodings = GetRequiredPropertyValue(
                parameters,
                "Encodings",
                "GetEncodings");

            if (encodings is not System.Collections.IEnumerable encodingItems)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Himo WebRTC] Video sender quality: RTP encodings collection is unavailable.");
                return;
            }

            var configured = 0;
            foreach (var encoding in encodingItems)
            {
                if (encoding is null)
                    continue;

                var encodingType = encoding.GetType();
                var bitrateProperty = encodingType.GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(p =>
                        string.Equals(p.Name, "MaxBitrateBps", StringComparison.OrdinalIgnoreCase) &&
                        p.SetMethod is not null);

                if (bitrateProperty is not null)
                {
                    var targetType = Nullable.GetUnderlyingType(bitrateProperty.PropertyType)
                        ?? bitrateProperty.PropertyType;
                    bitrateProperty.SetValue(encoding, Convert.ChangeType(maxVideoBitrateBps, targetType));
                    configured++;
                    continue;
                }

                var bitrateField = encodingType.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f =>
                        string.Equals(f.Name, "MaxBitrateBps", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.Name, "maxBitrateBps", StringComparison.OrdinalIgnoreCase));

                if (bitrateField is not null && !bitrateField.IsInitOnly)
                {
                    var targetType = Nullable.GetUnderlyingType(bitrateField.FieldType)
                        ?? bitrateField.FieldType;
                    bitrateField.SetValue(encoding, Convert.ChangeType(maxVideoBitrateBps, targetType));
                    configured++;
                }
            }

            if (configured == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Himo WebRTC] Video sender quality: MaxBitrateBps is not writable in this binding.");
                return;
            }

            var applied = InvokeRequired(videoSender, "SetParameters", parameters);
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Video sender quality: configured {configured} encoding(s), max bitrate={maxVideoBitrateBps} bps; SetParameters={applied}.");
        }
        catch (Exception ex)
        {
            // Quality tuning must never prevent a call from connecting.
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Video sender quality configuration was skipped: {ex.GetBaseException().Message}");
        }
    }

    private void InitializeFactory(
        Type factoryType)
    {
        if (_factory is not null)
            return;

        EnsurePeerConnectionFactoryInitialized(
            factoryType);

        var context =
            global::Android.App.Application.Context
            ?? throw new InvalidOperationException(
                "Android application context is unavailable for PeerConnectionFactory creation.");

        var classLoader =
            context.ClassLoader
            ?? throw new InvalidOperationException(
                "Android application ClassLoader is unavailable for PeerConnectionFactory creation.");

        // FsWebRTC.Bindings.Maui.Android 0.9.3.15 does not expose the Java
        // PeerConnectionFactory.builder() method as a usable managed static
        // method. The AAR itself exposes the Java API, so create the factory
        // through the application ClassLoader and then wrap the returned JNI
        // object in the generated managed binding. This avoids depending on
        // a managed Builder method that is not present in this binding.
        var factoryClass =
            Java.Lang.Class.ForName(
                "org.webrtc.PeerConnectionFactory",
                false,
                classLoader)
            ?? throw new InvalidOperationException(
                "Java PeerConnectionFactory class could not be loaded.");

        var builderMethod =
            factoryClass.GetMethod(
                "builder",
                Array.Empty<Java.Lang.Class>())
            ?? throw new MissingMethodException(
                "org.webrtc.PeerConnectionFactory",
                "builder");

        var javaBuilder =
            builderMethod.Invoke(
                null,
                Array.Empty<Java.Lang.Object>())
            as Java.Lang.Object
            ?? throw new InvalidOperationException(
                "Java PeerConnectionFactory.builder() returned null.");

        // The bundled WebRTC Builder leaves videoEncoderFactory and
        // videoDecoderFactory null unless they are explicitly supplied. That
        // allows the local camera preview to render, but creates/rejects the
        // negotiated video m-line (m=video 0), so no remote video can flow.
        // Use one EGL context for the encoder/decoder factories and the camera
        // source/renderer; WebRTC texture frames must share this context.
        var eglType = RequiredType("Org.Webrtc.EglBase");
        _eglBase ??= InvokeRequiredStatic(eglType, "Create");
        _eglContext ??= GetRequiredPropertyValue(
            _eglBase,
            "EglBaseContext",
            "GetEglBaseContext");

        var encoderFactory = CreateRequiredInstance(
            RequiredType("Org.Webrtc.DefaultVideoEncoderFactory"),
            _eglContext,
            true,
            true) as Java.Lang.Object
            ?? throw new InvalidOperationException(
                "DefaultVideoEncoderFactory could not be created.");

        var decoderFactory = CreateRequiredInstance(
            RequiredType("Org.Webrtc.DefaultVideoDecoderFactory"),
            _eglContext) as Java.Lang.Object
            ?? throw new InvalidOperationException(
                "DefaultVideoDecoderFactory could not be created.");

        var videoEncoderFactoryContract =
            Java.Lang.Class.ForName(
                "org.webrtc.VideoEncoderFactory",
                false,
                classLoader)
            ?? throw new InvalidOperationException(
                "WebRTC VideoEncoderFactory interface could not be loaded.");

        var videoDecoderFactoryContract =
            Java.Lang.Class.ForName(
                "org.webrtc.VideoDecoderFactory",
                false,
                classLoader)
            ?? throw new InvalidOperationException(
                "WebRTC VideoDecoderFactory interface could not be loaded.");

        InvokeJavaMethod(
            javaBuilder,
            "setVideoEncoderFactory",
            videoEncoderFactoryContract,
            encoderFactory);
        InvokeJavaMethod(
            javaBuilder,
            "setVideoDecoderFactory",
            videoDecoderFactoryContract,
            decoderFactory);

        System.Diagnostics.Debug.WriteLine(
            "[Himo WebRTC] Video codec factories configured: " +
            $"encoder={encoderFactory.GetType().Name}; decoder={decoderFactory.GetType().Name}; sharedEglContext=True.");

        var createFactoryMethod =
            javaBuilder.Class.GetMethod(
                "createPeerConnectionFactory",
                Array.Empty<Java.Lang.Class>())
            ?? throw new MissingMethodException(
                "org.webrtc.PeerConnectionFactory.Builder",
                "createPeerConnectionFactory");

        var javaFactory =
            createFactoryMethod.Invoke(
                javaBuilder,
                Array.Empty<Java.Lang.Object>())
            as Java.Lang.Object
            ?? throw new InvalidOperationException(
                "Java PeerConnectionFactory.Builder.createPeerConnectionFactory() returned null.");

        var managedFactory =
            Java.Lang.Object.GetObject<Org.Webrtc.PeerConnectionFactory>(
                javaFactory.Handle,
                JniHandleOwnership.DoNotTransfer)
            ?? throw new InvalidOperationException(
                "The Java PeerConnectionFactory could not be wrapped by the FsWebRTC managed binding.");

        _factory = managedFactory;
    }

    private static object CreateRequiredInstance(
        Type type,
        params object?[] args)
    {
        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.Instance);

        var constructor = constructors.FirstOrDefault(candidate =>
        {
            var parameters = candidate.GetParameters();
            if (parameters.Length != args.Length)
                return false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var argument = args[i];
                var parameterType = parameters[i].ParameterType;
                if (argument is null)
                {
                    if (parameterType.IsValueType &&
                        Nullable.GetUnderlyingType(parameterType) is null)
                        return false;
                }
                else if (!parameterType.IsInstanceOfType(argument))
                {
                    return false;
                }
            }

            return true;
        });

        if (constructor is null)
        {
            var available = string.Join(
                " | ",
                constructors.Select(candidate => candidate.ToString()));
            throw new MissingMethodException(
                type.FullName ?? type.Name,
                $"Compatible constructor not found. Available: {available}");
        }

        return constructor.Invoke(args)
            ?? throw new InvalidOperationException(
                $"Constructor for '{type.FullName}' returned null.");
    }

    private static void InvokeJavaMethod(
        Java.Lang.Object target,
        string methodName,
        Java.Lang.Class parameterType,
        Java.Lang.Object argument)
    {
        var method = target.Class.GetMethod(
            methodName,
            new[] { parameterType })
            ?? throw new MissingMethodException(
                target.Class.Name,
                methodName);

        method.Invoke(
            target,
            new Java.Lang.Object[] { argument });
    }

    private static void EnsurePeerConnectionFactoryInitialized(
        Type factoryType)
    {
        lock (FactoryInitializationGate)
        {
            if (FactoryInitialized)
                return;

            var context =
                global::Android.App.Application.Context;

            if (context is null)
            {
                throw new InvalidOperationException(
                    "Android application context is unavailable for WebRTC initialization.");
            }

            /*
             * First try the generated .NET binding.
             *
             * Some WebRTC bindings expose:
             *
             *   Initialize(InitializationOptions)
             *
             * while older bindings expose:
             *
             *   InitializeAndroidGlobals(...)
             *
             * If neither exists, we use Java reflection below.
             */

            /*
             * Prefer the Java API from the exact AAR that is packaged into the APK.
             * The generated FsWebRTC binding may not expose a usable managed
             * InitializationOptions.Builder on the installed 0.9.3.15 surface;
             * invoking that path first can fail before native call setup begins.
             * Java reflection keeps InitializationOptions and PeerConnectionFactory
             * on the same WebRTC revision and avoids raw JNIEnv reference handling.
             */
            if (TryInitializeUsingJavaReflection(
                factoryType,
                context))
            {
                FactoryInitialized = true;
                return;
            }

            // Fallback only when the Java API is not present; never prefer the
            // potentially mismatched managed Builder method over the packaged AAR.
            if (TryInitializeUsingManagedBinding(
                factoryType,
                context))
            {
                FactoryInitialized = true;
                return;
            }

            throw new MissingMethodException(
                factoryType.FullName ??
                "Org.Webrtc.PeerConnectionFactory",
                "Initialize");
        }
    }

    private static bool TryInitializeUsingManagedBinding(
        Type factoryType,
        global::Android.Content.Context context)
    {
        var methods =
            factoryType.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static);

        /*
         * Legacy:
         * InitializeAndroidGlobals(Context, bool, bool, bool)
         */
        var legacy =
            methods
                .Where(
                    m =>
                        string.Equals(
                            m.Name,
                            "InitializeAndroidGlobals",
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    m =>
                        m.GetParameters().Length)
                .FirstOrDefault();

        if (legacy is not null)
        {
            var parameters =
                legacy.GetParameters();

            try
            {
                if (parameters.Length == 0)
                {
                    legacy.Invoke(
                        null,
                        null);

                    return true;
                }

                if (parameters.Length == 1)
                {
                    legacy.Invoke(
                        null,
                        new object?[]
                        {
                            context
                        });

                    return true;
                }

                if (parameters.Length == 2 &&
                    parameters[0].ParameterType.IsAssignableFrom(
                        context.GetType()) &&
                    parameters[1].ParameterType == typeof(bool))
                {
                    legacy.Invoke(
                        null,
                        new object?[]
                        {
                            context,
                            true
                        });

                    return true;
                }

                if (parameters.Length == 4 &&
                    parameters[0].ParameterType.IsAssignableFrom(
                        context.GetType()) &&
                    parameters.Skip(1).All(
                        p =>
                            p.ParameterType == typeof(bool)))
                {
                    legacy.Invoke(
                        null,
                        new object?[]
                        {
                            context,
                            true,
                            true,
                            true
                        });

                    return true;
                }
            }
            catch (TargetInvocationException)
            {
                throw;
            }
        }

        /*
         * Modern:
         * Initialize(InitializationOptions)
         */
        var initialize =
            methods.FirstOrDefault(
                m =>
                    string.Equals(
                        m.Name,
                        "Initialize",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    m.GetParameters().Length == 1);

        if (initialize is null)
            return false;

        var optionsType =
            factoryType.GetNestedType(
                "InitializationOptions",
                BindingFlags.Public |
                BindingFlags.NonPublic);

        if (optionsType is null)
            return false;

        var options =
            TryCreateInitializationOptions(
                optionsType,
                context);

        if (options is null)
            return false;

        initialize.Invoke(
            null,
            new object?[]
            {
                options
            });

        return true;
    }

    private static object? TryCreateInitializationOptions(
        Type optionsType,
        global::Android.Content.Context context)
    {
        var builderMethod =
            optionsType
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static)
                .FirstOrDefault(
                    m =>
                        string.Equals(
                            m.Name,
                            "Builder",
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        m.GetParameters().Length == 1);

        if (builderMethod is null)
            return null;

        var builder =
            builderMethod.Invoke(
                null,
                new object?[]
                {
                    context
                });

        if (builder is null)
            return null;

        InvokeOptional(
            builder,
            "SetEnableInternalTracer",
            false);

        InvokeOptional(
            builder,
            "SetFieldTrials",
            string.Empty);

        return
            InvokeOptional(
                builder,
                "CreateInitializationOptions");
    }

    private static bool TryInitializeUsingJavaReflection(
        Type managedFactoryType,
        global::Android.Content.Context context)
    {
        try
        {
            var classLoader =
                context.ClassLoader
                ?? throw new InvalidOperationException(
                    "Android application ClassLoader is unavailable for WebRTC initialization.");

            // Resolve all Java classes through the application ClassLoader.
            // Do not use the boot ClassLoader and do not depend on the generated
            // managed nested types for InitializationOptions.
            var factoryClass =
                Java.Lang.Class.ForName(
                    "org.webrtc.PeerConnectionFactory",
                    false,
                    classLoader);

            var optionsClass =
                Java.Lang.Class.ForName(
                    "org.webrtc.PeerConnectionFactory$InitializationOptions",
                    false,
                    classLoader);

            var contextClass =
                Java.Lang.Class.ForName(
                    "android.content.Context",
                    false,
                    classLoader);

            var builderMethod =
                optionsClass.GetMethod(
                    "builder",
                    new Java.Lang.Class[]
                    {
                        contextClass
                    });

            if (builderMethod is null)
                return false;

            var builder =
                builderMethod.Invoke(
                    null,
                    new Java.Lang.Object[]
                    {
                        context
                    });

            if (builder is null)
                return false;

            var createOptionsMethod =
                builder.Class.GetMethod(
                    "createInitializationOptions",
                    Array.Empty<Java.Lang.Class>());

            if (createOptionsMethod is null)
                return false;

            var options =
                createOptionsMethod.Invoke(
                    builder,
                    Array.Empty<Java.Lang.Object>());

            if (options is null)
                return false;

            var initializeMethod =
                factoryClass.GetMethod(
                    "initialize",
                    new Java.Lang.Class[]
                    {
                        options.Class
                    });

            if (initializeMethod is null)
                return false;

            initializeMethod.Invoke(
                null,
                new Java.Lang.Object[]
                {
                    options
                });

            return true;
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                "WebRTC Java PeerConnectionFactory.initialize failed.",
                ex.InnerException ?? ex);
        }
        catch (Java.Lang.Exception ex)
        {
            throw new InvalidOperationException(
                $"WebRTC Java initialization failed: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "WebRTC Java PeerConnectionFactory initialization could not be completed.",
                ex);
        }
    }

    

    private static object? InvokeRequiredStaticFactory(
        Type type,
        string name,
        params object?[] args)
    {
        var method =
            FindMethod(
                type,
                name,
                args.Length,
                true);

        if (method is null)
        {
            method =
                FindMethod(
                    type,
                    name.ToLowerInvariant(),
                    args.Length,
                    true);
        }

        if (method is null)
        {
            throw new MissingMethodException(
                type.FullName ??
                "Unknown WebRTC type",
                name);
        }

        return method.Invoke(
            null,
            args);
    }

    // Read a Java bean getter that the Xamarin/.NET Android binding exposes as
    // a C# property. The final argument is retained as a compatibility fallback
    // for binding versions that expose the getter as a method instead.
    private static object GetRequiredPropertyValue(
        object target,
        string propertyName,
        string fallbackMethodName)
    {
        var targetType = target.GetType();
        var candidates = new[] { targetType }
            .Concat(targetType.GetInterfaces())
            .Distinct();

        foreach (var candidate in candidates)
        {
            var property = candidate.GetProperties(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .FirstOrDefault(p =>
                    string.Equals(
                        p.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    p.GetIndexParameters().Length == 0 &&
                    p.GetMethod is not null);

            if (property is null)
                continue;

            var value = property.GetValue(target);
            if (value is not null)
                return value;

            throw new InvalidOperationException(
                $"WebRTC property '{propertyName}' on '{targetType.FullName}' returned null.");
        }

        // Some bindings preserve the Java getter as a conventional C# method.
        return InvokeRequired(target, fallbackMethodName);
    }

    // Reflection returns null for a successfully invoked void method. Keep a
    // non-null sentinel so callers that intentionally ignore a void result do
    // not mistake successful WebRTC calls (for example SetLocalDescription)
    // for failures. For non-void methods, null remains an error.
    private static readonly object VoidInvocationResult = new();

    private static object InvokeRequired(
        object target,
        string name,
        params object?[] args)
    {
        var method =
            FindMethod(
                target.GetType(),
                name,
                args.Length,
                false)
            ?? throw new MissingMethodException(
                target.GetType().FullName ??
                target.GetType().Name,
                name);

        var result = method.Invoke(target, args);

        if (method.ReturnType == typeof(void))
            return VoidInvocationResult;

        return result
            ?? throw new InvalidOperationException(
                $"WebRTC method '{name}' returned null.");
    }

    private static object InvokeRequiredStatic(
        Type type,
        string name,
        params object?[] args)
    {
        var method =
            FindMethod(
                type,
                name,
                args.Length,
                true)
            ?? throw new MissingMethodException(
                type.FullName ??
                type.Name,
                name);

        return method.Invoke(
                null,
                args)
            ?? throw new InvalidOperationException(
                $"WebRTC static method '{name}' returned null.");
    }

    private readonly record struct OptionalInvocationResult(
        bool MethodFound,
        bool Success);

    // Resolve overloads by runtime parameter compatibility. The older helper
    // chooses only by argument count, which can select an incompatible overload
    // and silently swallow AddSink/RemoveSink failures.
    private static OptionalInvocationResult InvokeCompatibleOptional(
        object? target,
        string name,
        params object?[] args)
    {
        if (target is null)
            return new(false, false);

        var method = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    return false;
                var parameters = m.GetParameters();
                if (parameters.Length != args.Length)
                    return false;
                for (var i = 0; i < parameters.Length; i++)
                {
                    var arg = args[i];
                    if (arg is null)
                    {
                        if (parameters[i].ParameterType.IsValueType &&
                            Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                            return false;
                    }
                    else if (!parameters[i].ParameterType.IsInstanceOfType(arg))
                    {
                        return false;
                    }
                }
                return true;
            });

        if (method is null)
            return new(false, false);

        try
        {
            method.Invoke(target, args);
            return new(true, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] {name} invocation failed on {target.GetType().Name}: {ex.GetBaseException().Message}");
            return new(true, false);
        }
    }

    private static object? InvokeOptional(
        object? target,
        string name,
        params object?[] args)
    {
        if (target is null)
            return null;

        var method =
            FindMethod(
                target.GetType(),
                name,
                args.Length,
                false);

        if (method is null)
            return null;

        try
        {
            return method.Invoke(
                target,
                args);
        }
        catch
        {
            return null;
        }
    }

    private static MethodInfo? FindMethod(
        Type type,
        string name,
        int count,
        bool isStatic)
    {
        return type
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static)
            .FirstOrDefault(
                m =>
                    string.Equals(
                        m.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    m.IsStatic == isStatic
                    &&
                    m.GetParameters().Length == count);
    }

    private Assembly LoadBinding()
    {
        return
            AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(
                    a =>
                        string.Equals(
                            a.GetName().Name,
                            NativeAssemblyName,
                            StringComparison.OrdinalIgnoreCase))
            ??
            Assembly.Load(
                NativeAssemblyName);
    }

    private Type RequiredType(
        string name)
    {
        return
            _assembly?.GetType(
                name,
                false,
                false)
            ??
            throw new InvalidOperationException(
                $"WebRTC managed binding type '{name}' was not found. " +
                "The FsWebRTC Android binding is not loaded into the APK.");
    }

    private Type RequiredTypeAny(
        params string[] names)
    {
        if (_assembly is not null)
        {
            foreach (var name in names)
            {
                var type =
                    _assembly.GetType(
                        name,
                        false,
                        false);

                if (type is not null)
                    return type;
            }
        }

        throw new InvalidOperationException(
            "WebRTC managed RTCConfiguration type was not found in the FsWebRTC Android binding. " +
            string.Join(", ", names));
    }

    private object AttachRenderer(
        VisualElement host,
        bool mirror,
        object? existing)
    {
        // The call page owns a real WebRtcRendererView. Its Android handler
        // creates exactly one SurfaceViewRenderer in the native view hierarchy;
        // initialize it here with the media engine's shared EGL context.
        // The dedicated MAUI host creates exactly one renderer and keeps it
        // outside MAUI's measurement surface. Reuse that renderer instead of
        // creating another SurfaceViewRenderer.
        if (host is Himo.Views.WebRtcRendererView rendererHost &&
            rendererHost.NativeRenderer is global::Org.Webrtc.SurfaceViewRenderer readyRenderer)
        {
            if (_eglContext is null)
                throw new InvalidOperationException(
                    "The shared WebRTC EGL context is not ready for video rendering.");

            // Use the same EGL context as the camera capturer/video source. A
            // separately-created EGL context can receive frames but fail to render
            // texture-backed frames on the SurfaceViewRenderer.
            if (!_initializedHostRenderers.Contains(readyRenderer))
            {
                InvokeRequired(readyRenderer, "Init", _eglContext, null);
                _initializedHostRenderers.Add(readyRenderer);
            }

            InvokeOptional(readyRenderer, "SetEnableHardwareScaler", true);
            InvokeOptional(readyRenderer, "SetMirror", mirror);
            InvokeOptional(readyRenderer, "SetZOrderMediaOverlay", mirror);
            return readyRenderer;
        }

        var platformObject = host.Handler?.PlatformView
            ?? throw new InvalidOperationException(
                "Video renderer host is not ready on Android.");

        // Compatibility fallback for older layouts that still pass a ViewGroup.
        var platformHost =
            platformObject as global::Android.Views.ViewGroup
            ?? throw new InvalidOperationException(
                "Video host is not a compatible Android ViewGroup.");

        if (existing is global::Android.Views.View existingView)
        {
            if (existingView.Parent is global::Android.Views.ViewGroup currentParent &&
                !ReferenceEquals(currentParent, platformHost))
            {
                currentParent.RemoveView(existingView);
            }

            if (!ReferenceEquals(existingView.Parent, platformHost))
            {
                existingView.LayoutParameters =
                    new global::Android.Views.ViewGroup.MarginLayoutParams(-1, -1);

                platformHost.AddView(existingView);
            }

            return existing;
        }

        var rendererType =
            RequiredType("Org.Webrtc.SurfaceViewRenderer");

        var renderer =
            Activator.CreateInstance(
                rendererType,
                global::Android.App.Application.Context)
            ?? throw new InvalidOperationException(
                "SurfaceViewRenderer could not be created.");

        InvokeOptional(renderer, "SetEnableHardwareScaler", true);

        if (_eglContext is not null)
        {
            InvokeOptional(
                renderer,
                "Init",
                _eglContext,
                null);
        }

        InvokeOptional(renderer, "SetMirror", mirror);
        InvokeOptional(renderer, "SetZOrderMediaOverlay", mirror);

        if (renderer is global::Android.Views.View nativeView)
        {
            nativeView.LayoutParameters =
                new global::Android.Views.ViewGroup.MarginLayoutParams(-1, -1);

            platformHost.AddView(nativeView);
        }

        return renderer;
    }

    private void DisposeNativeObjects()
    {
        InvokeOptional(
            _capturer,
            "StopCapture");

        if (_videoTrack is not null &&
            _localRenderer is not null)
        {
            InvokeCompatibleOptional(
                _videoTrack,
                "RemoveSink",
                _localRenderer);
        }

        InvokeOptional(
            _localRenderer,
            "ClearImage");

        if (_remoteVideoTrack is not null &&
            _remoteRenderer is not null)
        {
            InvokeCompatibleOptional(
                _remoteVideoTrack,
                "RemoveSink",
                _remoteRenderer);
        }

        if (_remoteAudioTrack is not null)
        {
            InvokeOptional(
                _remoteAudioTrack,
                "SetEnabled",
                false);
        }

        InvokeOptional(
            _remoteRenderer,
            "ClearImage");

        RemoveRenderer(
            _localRenderer);

        RemoveRenderer(
            _remoteRenderer);

        TryDispose(_capturer);
        TryDispose(_videoTrack);
        TryDispose(_videoSource);
        TryDispose(_surfaceTextureHelper);

        TryDispose(_peerConnection);
        TryDispose(_peerConnectionObserver);

        TryDispose(_audioTrack);
        TryDispose(_audioSource);

        TryDispose(_factory);

        TryDispose(_eglBase);

        _capturer = null;
        _videoTrack = null;
        _videoSource = null;
        _surfaceTextureHelper = null;

        _audioTrack = null;
        _audioSource = null;

        _factory = null;
        _peerConnection = null;

        _eglBase = null;
        _eglContext = null;

        _localRenderer = null;
        _remoteRenderer = null;
        _initializedHostRenderers.Clear();

        _remoteVideoTrack = null;
        _remoteAudioTrack = null;

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
        if (renderer is null)
            return;

        void RemoveOnMainThread()
        {
            if (renderer is global::Android.Views.View view &&
                view.Parent is global::Android.Views.ViewGroup parent)
            {
                parent.RemoveView(view);
            }
            TryDispose(renderer);
        }

        if (global::Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
        {
            RemoveOnMainThread();
            return;
        }

        try
        {
            global::Microsoft.Maui.ApplicationModel.MainThread
                .InvokeOnMainThreadAsync(RemoveOnMainThread)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Renderer cleanup on main thread failed: {ex.GetBaseException().Message}");
        }
    }

    private static void TryDispose(
        object? value)
    {
        if (value is null)
            return;

        try
        {
            value.GetType()
                .GetMethod(
                    "Dispose",
                    BindingFlags.Public |
                    BindingFlags.Instance)
                ?.Invoke(
                    value,
                    null);
        }
        catch
        {
        }
    }

    private void ResetConnectionState()
    {
        _lastIceConnectionState = null;

        _lastPeerConnectionState = null;

        _lastConnectionEstablished = false;
    }

    private bool IsCurrentSession(
        long sessionGeneration)
    {
        return
            _started &&
            sessionGeneration == _sessionGeneration;
    }

    private void NotifyConnectionEstablishedChanged()
    {
        var established =
            IsConnectionEstablished;

        if (_lastConnectionEstablished ==
            established)
        {
            return;
        }

        _lastConnectionEstablished =
            established;

        ConnectionEstablishedChanged?.Invoke(
            this,
            established);
    }

    private sealed class PeerConnectionObserverBridge :
        Java.Lang.Object,
        Org.Webrtc.PeerConnection.IObserver
    {
        private readonly Action<string>
            _onIceCandidate;

        private readonly Action<object?>
            _onRemoteTrack;

        private readonly Action<string>
            _onIceConnectionStateChanged;

        private readonly Action<string>
            _onPeerConnectionStateChanged;

        public PeerConnectionObserverBridge(
            Action<string> onIceCandidate,
            Action<object?> onRemoteTrack,
            Action<string> onIceConnectionStateChanged,
            Action<string> onPeerConnectionStateChanged)
        {
            _onIceCandidate =
                onIceCandidate;

            _onRemoteTrack =
                onRemoteTrack;

            _onIceConnectionStateChanged =
                onIceConnectionStateChanged;

            _onPeerConnectionStateChanged =
                onPeerConnectionStateChanged;
        }

        public void OnIceCandidate(
            Org.Webrtc.IceCandidate? candidate)
        {
            if (candidate is null)
                return;

            var payload =
                new IceCandidatePayload(
                    candidate.SdpMid,
                    candidate.SdpMLineIndex,
                    candidate.Sdp ?? string.Empty);

            _onIceCandidate(
                JsonSerializer.Serialize(
                    payload));
        }

        public void OnAddStream(
            Org.Webrtc.MediaStream? stream)
        {
        }

        public void OnRemoveStream(
            Org.Webrtc.MediaStream? stream)
        {
        }

        public void OnDataChannel(
            Org.Webrtc.DataChannel? channel)
        {
        }

        public void OnRenegotiationNeeded()
        {
        }

        public void OnIceConnectionChange(
            Org.Webrtc.PeerConnection.IceConnectionState? state)
        {
            if (state is not null)
            {
                _onIceConnectionStateChanged(
                    state.ToString() ??
                    string.Empty);
            }
        }

        public void OnIceConnectionReceivingChange(
            bool receiving)
        {
        }

        public void OnIceGatheringChange(
            Org.Webrtc.PeerConnection.IceGatheringState? state)
        {
            if (state is not null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] ICE gathering state changed: {state}");
            }
        }

        public void OnIceCandidatesRemoved(
            Org.Webrtc.IceCandidate[]? candidates)
        {
        }

        public void OnSignalingChange(
            Org.Webrtc.PeerConnection.SignalingState? state)
        {
        }

        public void OnConnectionChange(
            Org.Webrtc.PeerConnection.PeerConnectionState? state)
        {
            if (state is not null)
            {
                _onPeerConnectionStateChanged(
                    state.ToString() ??
                    string.Empty);
            }
        }

        public void OnSelectedCandidatePairChanged(
            Org.Webrtc.CandidatePairChangeEvent? eventData)
        {
        }

        public void OnStandardizedIceConnectionChange(
            Org.Webrtc.PeerConnection.IceConnectionState? state)
        {
        }

        public void OnIceCandidateError(
            Org.Webrtc.IceCandidateErrorEvent? error)
        {
            if (error is not null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] ICE candidate gathering error: {error}");
            }
        }

        public void OnAddTrack(
            Org.Webrtc.RtpReceiver? receiver,
            Org.Webrtc.MediaStream[]? mediaStreams)
        {
            var track =
                receiver?.Track();

            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] OnAddTrack: receiverTrack={(track is null ? "null" : track.GetType().FullName)}; streams={mediaStreams?.Length ?? 0}");

            if (track is not null)
                _onRemoteTrack(track);
        }

        public void OnRemoveTrack(
            Org.Webrtc.RtpReceiver? receiver)
        {
        }

        public void OnTrack(
            Org.Webrtc.RtpTransceiver? transceiver)
        {
            var track =
                transceiver?.Receiver?.Track();

            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] OnTrack: receiverTrack={(track is null ? "null" : track.GetType().FullName)}");

            if (track is not null)
                _onRemoteTrack(track);
        }
    }

    public ValueTask DisposeAsync()
    {
        ++_sessionGeneration;

        DisposeNativeObjects();

        _started = false;

        _cameraEnabled = false;

        _microphoneEnabled = false;

        _speakerEnabled = false;

        _mode = null;

        return ValueTask.CompletedTask;
    }
}