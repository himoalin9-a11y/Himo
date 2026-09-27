#if ANDROID
using Himo.Views;
using Microsoft.Maui.Handlers;
using Org.Webrtc;

namespace Himo.Platforms.Android.Services;

#pragma warning disable CS0618

public sealed class WebRtcRendererViewHandler : ViewHandler<WebRtcRendererView, SurfaceViewRenderer>
{
    public static readonly IPropertyMapper<WebRtcRendererView, WebRtcRendererViewHandler> Mapper =
        new PropertyMapper<WebRtcRendererView, WebRtcRendererViewHandler>(ViewHandler.ViewMapper);

    public WebRtcRendererViewHandler() : base(Mapper) { }

    private IEglBase? _eglBase;

    protected override SurfaceViewRenderer CreatePlatformView()
    {
        var renderer = new SurfaceViewRenderer(Context);
        renderer.SetZOrderMediaOverlay(false);
        return renderer;
    }

    protected override void ConnectHandler(SurfaceViewRenderer platformView)
    {
        base.ConnectHandler(platformView);

        var eglBase = EglBase.Create();
        if (eglBase is null)
            throw new InvalidOperationException("Failed to create WebRTC EGL base.");

        _eglBase = eglBase;

        platformView.SetEnableHardwareScaler(true);
        platformView.Init(eglBase.EglBaseContext, null);

        VirtualView.NotifyPlatformViewReady(platformView);
    }

    protected override void DisconnectHandler(SurfaceViewRenderer platformView)
    {
        VirtualView.NotifyPlatformViewDetached(platformView);

        try { platformView.ClearImage(); } catch { }
        try { platformView.Release(); } catch { }

        try { _eglBase?.Dispose(); } catch { }
        _eglBase = null;

        base.DisconnectHandler(platformView);
    }
}

#pragma warning restore CS0618
#endif
