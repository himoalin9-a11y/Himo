#if ANDROID
using System;
using Android.Runtime;

namespace Himo.Platforms.Android.Services;

/// <summary>
/// Keeps WebRTC Java peers that are resolved by reflection from being removed
/// from the Android application. The call engine intentionally uses reflection
/// because FsWebRTC generated member names vary between releases.
/// </summary>
[Preserve(AllMembers = true)]
internal static class WebRtcJavaBindingKeepAlive
{
    // These are the Java-bound types resolved by name in AndroidWebRtcMediaEngine.
    // The explicit managed references give the Android linker a real root so the
    // corresponding org.webrtc classes are packaged in the final APK.
    private static readonly Type[] RequiredTypes =
    {
        typeof(Org.Webrtc.PeerConnectionFactory),
        typeof(Org.Webrtc.PeerConnection),
        typeof(Org.Webrtc.MediaConstraints),
        typeof(Org.Webrtc.EglBase),
        typeof(Org.Webrtc.SurfaceTextureHelper),
        typeof(Org.Webrtc.Camera2Enumerator),
        typeof(Org.Webrtc.SessionDescription),
        typeof(Org.Webrtc.IceCandidate),
        typeof(Org.Webrtc.AudioTrack),
        typeof(Org.Webrtc.VideoTrack),
        typeof(Org.Webrtc.SurfaceViewRenderer)
    };

    // Intentionally public to make it easy for the media engine to touch this
    // type later without changing the binding contract. The Preserve attribute
    // is what matters for linker rooting.
    public static void Ensure()
    {
        // Touch every generated peer before WebRTC starts. This is intentionally
        // executed from the media engine; merely having the type in this file is
        // not sufficient protection when Android trimming is enabled by a host
        // configuration.
        _ = RequiredTypes.Length;
        foreach (var type in RequiredTypes)
            GC.KeepAlive(type);
    }
}
#endif
