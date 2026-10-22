#if ANDROID
using Himo.Views;
using Microsoft.Maui.Handlers;
using Org.Webrtc;

namespace Himo.Platforms.Android.Services;

#pragma warning disable CS0618

/// <summary>
/// Hosts SurfaceViewRenderer inside a normal Android FrameLayout.
/// Do not expose SurfaceViewRenderer itself as the MAUI PlatformView because
/// MAUI may measure a handler while its Java View is being detached, which can
/// result in PlatformInterop.measureAndGetWidthAndHeight receiving null.
/// </summary>
public sealed class WebRtcRendererViewHandler : ViewHandler<WebRtcRendererView, global::Android.Widget.FrameLayout>
{
    public static readonly IPropertyMapper<WebRtcRendererView, WebRtcRendererViewHandler> Mapper =
        new PropertyMapper<WebRtcRendererView, WebRtcRendererViewHandler>(ViewHandler.ViewMapper);

    private SurfaceViewRenderer? _renderer;

    public WebRtcRendererViewHandler() : base(Mapper) { }

    protected override global::Android.Widget.FrameLayout CreatePlatformView()
    {
        return new global::Android.Widget.FrameLayout(Context)
        {
            LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
                global::Android.Views.ViewGroup.LayoutParams.MatchParent,
                global::Android.Views.ViewGroup.LayoutParams.MatchParent)
        };
    }

    protected override void ConnectHandler(global::Android.Widget.FrameLayout platformView)
    {
        base.ConnectHandler(platformView);

        var renderer = new SurfaceViewRenderer(Context)
        {
            LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
                global::Android.Views.ViewGroup.LayoutParams.MatchParent,
                global::Android.Views.ViewGroup.LayoutParams.MatchParent)
        };

        renderer.SetEnableHardwareScaler(true);
        renderer.SetZOrderMediaOverlay(false);
        // The media engine initializes this renderer with the same EGL context
        // used by the camera capturer and WebRTC video tracks.

        _renderer = renderer;
        platformView.RemoveAllViews();
        platformView.AddView(renderer);

        VirtualView.SetNativeRenderer(renderer);

        platformView.Post(() =>
        {
            try
            {
                platformView.RequestLayout();
                renderer.RequestLayout();
                VirtualView.SetNativeRenderer(renderer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Renderer post-layout failed: {ex.Message}");
            }
        });
    }

    protected override void DisconnectHandler(global::Android.Widget.FrameLayout platformView)
    {
        // SurfaceViewRenderer.surfaceDestroyed() asserts the Android main thread.
        // MAUI teardown can race with asynchronous WebRTC cleanup, so never remove
        // or release the SurfaceView from a worker thread.
        if (!global::Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
        {
            try
            {
                global::Microsoft.Maui.ApplicationModel.MainThread
                    .InvokeOnMainThreadAsync(() => DisconnectHandlerOnMainThread(platformView))
                    .GetAwaiter()
                    .GetResult();
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo WebRTC] Renderer handler disconnect failed: {ex.GetBaseException().Message}");
                platformView.Post(() => DisconnectHandlerOnMainThread(platformView));
                return;
            }
        }

        DisconnectHandlerOnMainThread(platformView);
    }

    private void DisconnectHandlerOnMainThread(global::Android.Widget.FrameLayout platformView)
    {
        var renderer = _renderer;
        if (renderer is not null)
            VirtualView.NotifyPlatformViewDetached(renderer);

        try { platformView.RemoveAllViews(); } catch { }
        try { renderer?.ClearImage(); } catch { }
        try { renderer?.Release(); } catch { }

        _renderer = null;
        base.DisconnectHandler(platformView);
    }
}

#pragma warning restore CS0618
#endif
