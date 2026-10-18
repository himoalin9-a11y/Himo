using Microsoft.Maui.Controls;

namespace Himo.Views;

public enum CameraCaptureMode
{
    Photo,
    Video
}

public sealed record CameraCaptureResult(string FilePath, string FileName, string ContentType);

internal interface ICameraPreviewController
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<CameraCaptureResult?> CapturePhotoAsync(CancellationToken cancellationToken = default);
    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    Task<CameraCaptureResult?> StopRecordingAsync(CancellationToken cancellationToken = default);
    Task PauseRecordingAsync();
    Task ResumeRecordingAsync();
    Task CancelRecordingAsync();
    Task SwitchCameraAsync();
    Task ToggleFlashAsync();
    Task SetZoomAsync(float factor);
    Task FocusAsync();
    Task SetFilterAsync(string filterName);
    bool IsRecording { get; }
    bool IsRecordingPaused { get; }
    float PhotoAspectRatio { get; }
    bool HasFlash { get; }
    bool IsFlashOn { get; }
    float MaxZoomFactor { get; }
}

public sealed class CameraPreview : View
{
    private float _previewAspectRatio = 0.75f;

    internal event Action<float>? PreviewAspectRatioChanged;

    internal float PreviewAspectRatio => _previewAspectRatio;

    internal void SetPreviewAspectRatio(float aspectRatio)
    {
        if (aspectRatio <= 0.1f || float.IsNaN(aspectRatio) || float.IsInfinity(aspectRatio))
            return;

        if (Math.Abs(_previewAspectRatio - aspectRatio) < 0.001f)
            return;

        _previewAspectRatio = aspectRatio;
        PreviewAspectRatioChanged?.Invoke(_previewAspectRatio);
    }

    private TaskCompletionSource<bool>? _controllerReady;

    internal ICameraPreviewController? Controller { get; private set; }

    internal void SetController(ICameraPreviewController controller)
    {
        Controller = controller;
        _controllerReady?.TrySetResult(true);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var controller = Controller;

        if (controller is null)
        {
            _controllerReady ??= new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await _controllerReady.Task.WaitAsync(cancellationToken);
            controller = Controller;
        }

        if (controller is not null)
            await controller.StartAsync(cancellationToken);
    }

    public Task StopAsync() => Controller?.StopAsync() ?? Task.CompletedTask;

    public Task<CameraCaptureResult?> CapturePhotoAsync(CancellationToken cancellationToken = default) =>
        Controller?.CapturePhotoAsync(cancellationToken) ?? Task.FromResult<CameraCaptureResult?>(null);

    public Task StartRecordingAsync(CancellationToken cancellationToken = default) =>
        Controller?.StartRecordingAsync(cancellationToken) ?? Task.CompletedTask;

    public Task<CameraCaptureResult?> StopRecordingAsync(CancellationToken cancellationToken = default) =>
        Controller?.StopRecordingAsync(cancellationToken) ?? Task.FromResult<CameraCaptureResult?>(null);

    public Task PauseRecordingAsync() =>
        Controller?.PauseRecordingAsync() ?? Task.CompletedTask;

    public Task ResumeRecordingAsync() =>
        Controller?.ResumeRecordingAsync() ?? Task.CompletedTask;

    public Task CancelRecordingAsync() => Controller?.CancelRecordingAsync() ?? Task.CompletedTask;

    public Task SwitchCameraAsync() => Controller?.SwitchCameraAsync() ?? Task.CompletedTask;

    public Task ToggleFlashAsync() => Controller?.ToggleFlashAsync() ?? Task.CompletedTask;

    public Task SetZoomAsync(float factor) => Controller?.SetZoomAsync(factor) ?? Task.CompletedTask;

    public Task FocusAsync() => Controller?.FocusAsync() ?? Task.CompletedTask;

    public Task SetFilterAsync(string filterName) =>
        Controller?.SetFilterAsync(filterName) ?? Task.CompletedTask;

    public bool IsRecording => Controller?.IsRecording == true;
    public bool IsRecordingPaused => Controller?.IsRecordingPaused == true;
    public float PhotoAspectRatio => Controller?.PhotoAspectRatio ?? _previewAspectRatio;
    public bool HasFlash => Controller?.HasFlash == true;
    public bool IsFlashOn => Controller?.IsFlashOn == true;
    public float MaxZoomFactor => Controller?.MaxZoomFactor ?? 1f;
}
