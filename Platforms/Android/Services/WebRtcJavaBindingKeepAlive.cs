#if ANDROID
using System;
using System.Diagnostics.CodeAnalysis;

namespace Himo.Platforms.Android.Services;

/// <summary>
/// Roots WebRTC binding types that are accessed through reflection.
/// Uses the .NET 10 DynamicDependency constructors supported by the linker.
/// </summary>
internal static class WebRtcJavaBindingKeepAlive
{
    private const string BindingAssembly = "FsWebRTC.Bindings.Maui.Android";

    // These top-level binding types are referenced as managed Types by the engine.
    // The 2-argument DynamicDependency overload is the correct overload for a Type.
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.PeerConnectionFactory))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.PeerConnection))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.MediaConstraints))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.SurfaceTextureHelper))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.Camera2Enumerator))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.SessionDescription))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.IceCandidate))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.AudioTrack))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.VideoTrack))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Org.Webrtc.SurfaceViewRenderer))]

    // Reflection-only nested types. These use the documented 3-string
    // DynamicDependency overload: member types, full type name, assembly name.
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnection+RTCConfiguration",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnection+RtcConfiguration",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnection+IceServer",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnection+IceServer+Builder",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnectionFactory+Builder",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnectionFactory+InitializationOptions",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.PeerConnectionFactory+InitializationOptions+Builder",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.MediaConstraints+KeyValuePair",
        BindingAssembly)]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        "Org.Webrtc.SessionDescription+Type",
        BindingAssembly)]
    public static void Ensure()
    {
        // The attributes are intentionally the mechanism here; there is no
        // runtime reflection or obsolete PreserveAttribute involved.
    }
}
#endif
