namespace Himo.Views;

/// <summary>
/// MAUI placeholder whose Android handler creates a native WebRTC
/// SurfaceViewRenderer. Keeping this as a MAUI View lets the call page remain
/// cross-platform while the Android media engine stays platform-specific.
/// </summary>
public sealed class WebRtcRendererView : View
{
    internal event Action<object>? PlatformViewReady;
    internal event Action<object>? PlatformViewDetached;

    internal void NotifyPlatformViewReady(object renderer) => PlatformViewReady?.Invoke(renderer);
    internal void NotifyPlatformViewDetached(object renderer) => PlatformViewDetached?.Invoke(renderer);
}
