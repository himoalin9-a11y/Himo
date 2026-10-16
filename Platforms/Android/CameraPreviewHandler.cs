#pragma warning disable CA1416
using Himo.Views;
using Microsoft.Maui.Handlers;

using AFrameLayout = global::Android.Widget.FrameLayout;
using AColor = global::Android.Graphics.Color;
using AGravity = global::Android.Views.GravityFlags;
using ASurfaceView = global::Android.Views.SurfaceView;
using AViewGroup = global::Android.Views.ViewGroup;
using AView = global::Android.Views.View;

namespace Himo.Platforms.Android;

public sealed class CameraPreviewHandler :
    ViewHandler<CameraPreview, AFrameLayout>
{
    public static readonly IPropertyMapper<CameraPreview, CameraPreviewHandler> Mapper =
        new PropertyMapper<CameraPreview, CameraPreviewHandler>(
            ViewHandler.ViewMapper);

    private ASurfaceView? _surfaceView;
    private AndroidCameraController? _controller;

    public CameraPreviewHandler() : base(Mapper)
    {
    }

    protected override AFrameLayout CreatePlatformView()
    {
        var context = Context
            ?? throw new InvalidOperationException(
                "Android context is not available.");

        var layout = new AFrameLayout(context);
        layout.SetBackgroundColor(AColor.Black);

        layout.LayoutParameters =
            new AViewGroup.LayoutParams(
                AViewGroup.LayoutParams.MatchParent,
                AViewGroup.LayoutParams.MatchParent);

        return layout;
    }

    protected override void ConnectHandler(AFrameLayout platformView)
    {
        base.ConnectHandler(platformView);

        var context = Context
            ?? throw new InvalidOperationException(
                "Android context is not available.");

        _surfaceView = new ASurfaceView(context);

        _surfaceView.LayoutParameters =
            new AFrameLayout.LayoutParams(
                AViewGroup.LayoutParams.MatchParent,
                AViewGroup.LayoutParams.MatchParent,
                AGravity.Center);

        platformView.AddView(_surfaceView);
        platformView.LayoutChange += PlatformViewLayoutChange;
        VirtualView.PreviewAspectRatioChanged += OnPreviewAspectRatioChanged;

        _controller =
            new AndroidCameraController(
                context,
                _surfaceView);

        VirtualView.SetController(_controller);

        // Give the SurfaceView one UI pass to finish creating/attaching its
        // native surface, and size it without stretching the camera buffer.
        platformView.Post(ApplySurfaceLayout);
    }

    protected override void DisconnectHandler(
        AFrameLayout platformView)
    {
        try
        {
            VirtualView.PreviewAspectRatioChanged -= OnPreviewAspectRatioChanged;
            platformView.LayoutChange -= PlatformViewLayoutChange;
            _controller?.Dispose();
        }
        finally
        {
            _controller = null;
            _surfaceView = null;

            VirtualView.SetController(
                new DisconnectedCameraController());

            platformView.RemoveAllViews();
            base.DisconnectHandler(platformView);
        }
    }

    private void OnPreviewAspectRatioChanged(float aspectRatio)
    {
        var platformView = PlatformView;
        if (platformView is null)
            return;

        platformView.Post(ApplySurfaceLayout);
    }

    private void PlatformViewLayoutChange(object? sender, AView.LayoutChangeEventArgs e)
    {
        ApplySurfaceLayout();
    }

    private void ApplySurfaceLayout()
    {
        var platformView = PlatformView;
        var surfaceView = _surfaceView;
        if (platformView is null || surfaceView is null)
            return;

        var containerWidth = platformView.Width;
        var containerHeight = platformView.Height;
        if (containerWidth <= 0 || containerHeight <= 0)
            return;

        var aspectRatio = Math.Max(0.1f, VirtualView.PreviewAspectRatio);
        var width = containerWidth;
        var height = (int)Math.Round(width / aspectRatio);

        if (height > containerHeight)
        {
            height = containerHeight;
            width = (int)Math.Round(height * aspectRatio);
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var layoutParams = new AFrameLayout.LayoutParams(
            width,
            height,
            AGravity.Center);

        surfaceView.LayoutParameters = layoutParams;
        surfaceView.RequestLayout();
    }

    private sealed class DisconnectedCameraController :
        ICameraPreviewController
    {
        public bool IsRecording => false;
        public bool IsRecordingPaused => false;
        public float PhotoAspectRatio => 0.75f;
        public bool HasFlash => false;
        public bool IsFlashOn => false;
        public float MaxZoomFactor => 1f;

        public Task StartAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync() =>
            Task.CompletedTask;

        public Task<CameraCaptureResult?> CapturePhotoAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraCaptureResult?>(null);

        public Task StartRecordingAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CameraCaptureResult?> StopRecordingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraCaptureResult?>(null);

        public Task PauseRecordingAsync() =>
            Task.CompletedTask;

        public Task ResumeRecordingAsync() =>
            Task.CompletedTask;

        public Task CancelRecordingAsync() =>
            Task.CompletedTask;

        public Task SwitchCameraAsync() =>
            Task.CompletedTask;

        public Task ToggleFlashAsync() =>
            Task.CompletedTask;

        public Task SetZoomAsync(float factor) =>
            Task.CompletedTask;

        public Task FocusAsync() =>
            Task.CompletedTask;

        public Task SetFilterAsync(string filterName) =>
            Task.CompletedTask;
    }
}
#pragma warning restore CA1416
