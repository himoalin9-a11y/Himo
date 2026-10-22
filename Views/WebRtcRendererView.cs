using System;

namespace Himo.Views;

/// <summary>
/// Stable MAUI host for an Android WebRTC SurfaceViewRenderer.
/// The MAUI handler owns a normal FrameLayout and places exactly one
/// SurfaceViewRenderer inside it. This avoids exposing the WebRTC Java View
/// directly to MAUI's measurement pipeline.
/// </summary>
public sealed class WebRtcRendererView : View
{
    internal event Action<object>? PlatformViewReady;
    internal event Action<object>? PlatformViewDetached;

#if ANDROID
    internal global::Org.Webrtc.SurfaceViewRenderer? NativeRenderer { get; private set; }
#endif

    public WebRtcRendererView()
    {
        SizeChanged += OnSizeChanged;
        HandlerChanged += OnHandlerChanged;
    }

#if ANDROID
    internal void SetNativeRenderer(global::Org.Webrtc.SurfaceViewRenderer? renderer)
    {
        NativeRenderer = renderer;
        if (renderer is not null)
            PlatformViewReady?.Invoke(renderer);
        SyncNativeRendererSize();
    }
#endif

    internal void NotifyPlatformViewDetached(object renderer)
    {
#if ANDROID
        if (ReferenceEquals(NativeRenderer, renderer))
            NativeRenderer = null;
#endif
        PlatformViewDetached?.Invoke(renderer);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        SyncNativeRendererSize();
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        SyncNativeRendererSize();
    }

    private void SyncNativeRendererSize()
    {
#if ANDROID
        var renderer = NativeRenderer;
        if (renderer is null)
            return;

        try
        {
            // Keep the native renderer sized by its FrameLayout parent. Manually
            // calling Layout with MAUI device-independent units can leave the
            // SurfaceView at 0x0 and cause WebRTC to drop every frame.
            renderer.LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
                global::Android.Views.ViewGroup.LayoutParams.MatchParent,
                global::Android.Views.ViewGroup.LayoutParams.MatchParent);
            renderer.RequestLayout();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo WebRTC] Renderer size setup failed: {ex.Message}");
        }
#endif
    }
}
