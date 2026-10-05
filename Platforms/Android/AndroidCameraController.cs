#pragma warning disable CS0618, CA1422, CA1416, CS0108
using Android.Media;
using ABitmap = global::Android.Graphics.Bitmap;
using ABitmapFactory = global::Android.Graphics.BitmapFactory;
using AMatrix = global::Android.Graphics.Matrix;
using Himo.Views;
using Microsoft.Maui.ApplicationModel;
using ACamera = global::Android.Hardware.Camera;
using ACameraFacing = global::Android.Hardware.CameraFacing;
using ASurface = global::Android.Views.Surface;
using ASurfaceHolder = global::Android.Views.ISurfaceHolder;
using ASurfaceHolderCallback = global::Android.Views.ISurfaceHolderCallback;
using ASurfaceView = global::Android.Views.SurfaceView;
using ADisplay = global::Android.Views.Display;
using ASurfaceOrientation = global::Android.Views.SurfaceOrientation;
using IOPath = global::System.IO.Path;

namespace Himo.Platforms.Android;

/// <summary>
/// Reliable in-app Android camera preview based on SurfaceView + Android Camera API.
/// SurfaceView is used intentionally here because it provides a real SurfaceHolder,
/// which is more reliable than TextureView for this app's current Android baseline.
/// </summary>
internal sealed class AndroidCameraController : Java.Lang.Object, ICameraPreviewController, ASurfaceHolderCallback
{
    private readonly ASurfaceView _surfaceView;
    private readonly ASurfaceHolder _surfaceHolder;

    private ACamera? _camera;
    private MediaRecorder? _mediaRecorder;
    private int _cameraId = -1;
    private bool _started;
    private bool _surfaceReady;
    private bool _recording;
    private bool _recordingPaused;
    private string? _recordingPath;
    private float _photoAspectRatio = 3f / 4f;
    private bool _hasFlash;
    private bool _flashOn;
    private float _maxZoomFactor = 1f;
    private TaskCompletionSource<bool>? _startTcs;
    private string _activeFilterName = "Natural";

    public AndroidCameraController(global::Android.Content.Context context, ASurfaceView surfaceView)
    {
        ArgumentNullException.ThrowIfNull(context);
        _surfaceView = surfaceView ?? throw new ArgumentNullException(nameof(surfaceView));
        _surfaceHolder = _surfaceView.Holder ?? throw new InvalidOperationException("Camera surface holder is unavailable.");
        _surfaceHolder.AddCallback(this);

        // SurfaceView can already have a valid surface before the callback is
        // attached. Detect that case explicitly; otherwise StartAsync would
        // wait forever and the preview would remain black.
        _surfaceReady = _surfaceHolder.Surface?.IsValid == true;

        if (_surfaceReady)
        {
            _surfaceView.Post(() =>
            {
                if (_started)
                    OpenCamera();
            });
        }
    }

    public bool IsRecording => _recording;
    public bool IsRecordingPaused => _recordingPaused;
    public float PhotoAspectRatio => _photoAspectRatio;
    public bool HasFlash => _hasFlash;
    public bool IsFlashOn => _flashOn;
    public float MaxZoomFactor => _maxZoomFactor;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_started)
            return Task.CompletedTask;

        _started = true;
        _startTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Re-check the holder here because SurfaceCreated may have fired before
        // the handler/controller was fully connected.
        _surfaceReady = _surfaceHolder.Surface?.IsValid == true || _surfaceReady;

        _cameraId = FindCameraId(ACameraFacing.Back);
        if (_cameraId < 0)
            _cameraId = FindAnyCameraId();

        if (_cameraId < 0)
        {
            _started = false;
            var error = new InvalidOperationException("لم يتم العثور على كاميرا متاحة.");
            _startTcs.TrySetException(error);
            return _startTcs.Task;
        }

        if (_surfaceReady)
            OpenCamera();

        return AwaitStartAsync(cancellationToken);
    }

    private async Task AwaitStartAsync(CancellationToken cancellationToken)
    {
        if (_startTcs is null)
            return;

        using var registration = cancellationToken.Register(
            () => _startTcs.TrySetCanceled(cancellationToken));

        await _startTcs.Task.ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        StopInternal(deleteRecording: true);
        return Task.CompletedTask;
    }

    public async Task<CameraCaptureResult?> CapturePhotoAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_camera is null || !_surfaceReady || _recording)
            return null;

        var path = IOPath.Combine(
            FileSystem.CacheDirectory,
            $"Himo_photo_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");

        var tcs = new TaskCompletionSource<CameraCaptureResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var captureRotationDegrees = GetCaptureJpegRotation();

            _camera.TakePicture(
                null,
                null,
                new JpegPictureCallback(async data =>
                {
                    try
                    {
                        var normalizedData = NormalizeCapturedJpeg(
                            data,
                            captureRotationDegrees);

                        await File.WriteAllBytesAsync(path, normalizedData).ConfigureAwait(false);

                        tcs.TrySetResult(
                            new CameraCaptureResult(
                                path,
                                IOPath.GetFileName(path),
                                "image/jpeg"));
                    }
                    catch (Exception ex)
                    {
                        TryDelete(path);
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        try
                        {
                            _camera?.StartPreview();
                        }
                        catch
                        {
                        }
                    }
                }));
        }
        catch (Exception ex)
        {
            TryDelete(path);
            tcs.TrySetException(ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            TryDelete(path);
            tcs.TrySetCanceled(cancellationToken);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_recording || _camera is null || !_surfaceReady)
            return Task.CompletedTask;

        var surface = _surfaceHolder.Surface;
        if (surface is null || !surface.IsValid)
            throw new InvalidOperationException("كاميرا Himo لم تجهز سطح العرض بعد.");

        var path = IOPath.Combine(
            FileSystem.CacheDirectory,
            $"Himo_video_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");

        MediaRecorder? recorder = null;

        try
        {
            recorder = new MediaRecorder();

            // Read the camera capabilities before handing the camera to
            // MediaRecorder. This keeps the size selection deterministic and
            // avoids relying on parameter reads after Unlock().
            var parameters = _camera.GetParameters();
            var supportedVideoSizes = parameters?.SupportedVideoSizes;
            var videoSize = ChooseVideoSize(supportedVideoSizes, _photoAspectRatio)
                ?? ChooseVideoSize(parameters?.SupportedPreviewSizes, _photoAspectRatio)
                ?? parameters?.PreferredPreviewSizeForVideo;

            _camera.Unlock();

            recorder.SetCamera(_camera);
            recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetVideoSource(VideoSource.Camera);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetOutputFile(path);

            // Use a real camera-supported video size whenever available. The
            // selected size has the same aspect ratio as the still-photo output,
            // so the live preview and the saved video keep the same visual shape.
            if (videoSize is not null)
            {
                recorder.SetVideoSize(videoSize.Width, videoSize.Height);
                var pixels = videoSize.Width * (long)videoSize.Height;
                var bitrate = pixels >= 2_000_000
                    ? 10_000_000
                    : pixels >= 1_000_000
                        ? 7_000_000
                        : 5_000_000;
                recorder.SetVideoEncodingBitRate(bitrate);
            }
            else
            {
                // Final conservative fallback. Keep the same camera shape rather
                // than reverting to a hard-coded 16:9 recording.
                var sensorRatio = _photoAspectRatio < 1f
                    ? 1d / Math.Max(0.01d, _photoAspectRatio)
                    : _photoAspectRatio;
                const int fallbackWidth = 1280;
                var fallbackHeight = Math.Max(480, (int)Math.Round(fallbackWidth / sensorRatio));
                recorder.SetVideoSize(fallbackWidth, fallbackHeight);
                recorder.SetVideoEncodingBitRate(6_000_000);
            }

            recorder.SetVideoFrameRate(30);
            recorder.SetVideoEncoder(VideoEncoder.H264);
            recorder.SetAudioEncodingBitRate(128_000);
            recorder.SetAudioEncoder(AudioEncoder.Aac);

            recorder.SetOrientationHint(GetDisplayOrientation());
            recorder.SetPreviewDisplay(surface);
            recorder.Prepare();
            recorder.Start();

            _mediaRecorder = recorder;
            _recordingPath = path;
            _recording = true;
            _recordingPaused = false;
        }
        catch
        {
            try { _camera.Lock(); } catch { }
            try { recorder?.Reset(); } catch { }
            try { recorder?.Release(); } catch { }
            recorder?.Dispose();
            TryDelete(path);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task PauseRecordingAsync()
    {
        if (!_recording || _recordingPaused || _mediaRecorder is null)
            return Task.CompletedTask;

        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            throw new PlatformNotSupportedException("الإيقاف المؤقت للفيديو يحتاج Android 7.0 أو أحدث.");

        try
        {
            _mediaRecorder.Pause();
            _recordingPaused = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("تعذر إيقاف تسجيل الفيديو مؤقتًا.", ex);
        }

        return Task.CompletedTask;
    }

    public Task ResumeRecordingAsync()
    {
        if (!_recording || !_recordingPaused || _mediaRecorder is null)
            return Task.CompletedTask;

        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            throw new PlatformNotSupportedException("استئناف الفيديو يحتاج Android 7.0 أو أحدث.");

        try
        {
            _mediaRecorder.Resume();
            _recordingPaused = false;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("تعذر استئناف تسجيل الفيديو.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<CameraCaptureResult?> StopRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_recording ||
            _mediaRecorder is null ||
            string.IsNullOrWhiteSpace(_recordingPath))
        {
            return Task.FromResult<CameraCaptureResult?>(null);
        }

        var path = _recordingPath;
        var valid = false;

        try
        {
            _mediaRecorder.Stop();
            valid = File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            valid = false;
        }
        finally
        {
            try { _mediaRecorder.Reset(); } catch { }
            try { _mediaRecorder.Release(); } catch { }
            _mediaRecorder.Dispose();

            _mediaRecorder = null;
            _recordingPath = null;
            _recording = false;
            _recordingPaused = false;

            try { _camera?.Lock(); } catch { }
            try { _camera?.StartPreview(); } catch { }
        }

        if (!valid)
        {
            TryDelete(path);
            return Task.FromResult<CameraCaptureResult?>(null);
        }

        return Task.FromResult<CameraCaptureResult?>(
            new CameraCaptureResult(
                path,
                IOPath.GetFileName(path),
                "video/mp4"));
    }

    public Task CancelRecordingAsync()
    {
        if (!_recording || _mediaRecorder is null)
            return Task.CompletedTask;

        var path = _recordingPath;

        try { _mediaRecorder.Stop(); } catch { }
        try { _mediaRecorder.Reset(); } catch { }
        try { _mediaRecorder.Release(); } catch { }
        _mediaRecorder.Dispose();

        _mediaRecorder = null;
        _recordingPath = null;
        _recording = false;
        _recordingPaused = false;

        try { _camera?.Lock(); } catch { }
        try { _camera?.StartPreview(); } catch { }

        TryDelete(path);
        return Task.CompletedTask;
    }

    public Task ToggleFlashAsync()
    {
        if (_camera is null || !_hasFlash || _recording)
            return Task.CompletedTask;

        try
        {
            var parameters = _camera.GetParameters();
            if (parameters is null)
                return Task.CompletedTask;

            var modes = parameters.SupportedFlashModes;
            if (modes is null || modes.Count == 0)
                return Task.CompletedTask;

            var hasTorch = modes.Any(mode =>
                string.Equals(mode, "torch", StringComparison.OrdinalIgnoreCase));
            var hasOff = modes.Any(mode =>
                string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase));

            if (!hasTorch || !hasOff)
                return Task.CompletedTask;

            var next = !_flashOn;
            parameters.FlashMode = next ? "torch" : "off";
            _camera.SetParameters(parameters);
            _flashOn = next;
        }
        catch
        {
            // Flash is optional and some front cameras expose unsupported modes.
        }

        return Task.CompletedTask;
    }

    public Task SetZoomAsync(float factor)
    {
        if (_camera is null || _recording)
            return Task.CompletedTask;

        try
        {
            var parameters = _camera.GetParameters();
            if (parameters is null)
                return Task.CompletedTask;

            if (!parameters.IsZoomSupported)
                return Task.CompletedTask;

            var maxZoom = Math.Max(0, parameters.MaxZoom);
            if (maxZoom == 0)
                return Task.CompletedTask;

            var safeFactor = Math.Clamp(factor, 1f, 4f);
            var zoomIndex = (int)Math.Round(
                ((safeFactor - 1f) / 3f) * maxZoom);

            parameters.Zoom = Math.Clamp(zoomIndex, 0, maxZoom);
            _camera.SetParameters(parameters);
        }
        catch
        {
            // Zoom is optional.
        }

        return Task.CompletedTask;
    }

    public Task FocusAsync()
    {
        if (_camera is null || _recording)
            return Task.CompletedTask;

        try
        {
            _camera.CancelAutoFocus();
            _camera.AutoFocus(new AutoFocusCallback());
        }
        catch
        {
            // Not all devices expose manual autofocus.
        }

        return Task.CompletedTask;
    }

    public Task SetFilterAsync(string filterName)
    {
        _activeFilterName = string.IsNullOrWhiteSpace(filterName)
            ? "Natural"
            : filterName;

        if (_camera is null || _recording)
            return Task.CompletedTask;

        try
        {
            var parameters = _camera.GetParameters();
            if (parameters is null)
                return Task.CompletedTask;

            ApplyLiveFilter(parameters, _activeFilterName);
            _camera.SetParameters(parameters);
        }
        catch
        {
            // The legacy Camera API exposes filters differently across devices.
            // The captured-photo filter remains as a reliable fallback.
        }

        return Task.CompletedTask;
    }

    public async Task SwitchCameraAsync()
    {
        if (_recording || !_started || !_surfaceReady)
            return;

        var front = FindCameraId(ACameraFacing.Front);
        var back = FindCameraId(ACameraFacing.Back);

        var target = _cameraId == front ? back : front;
        if (target < 0)
            target = FindAnyCameraId();

        if (target < 0 || target == _cameraId)
            return;

        _flashOn = false;
        _hasFlash = false;
        _maxZoomFactor = 1f;

        CloseCamera();

        _cameraId = target;

        await Task.Delay(100);

        _surfaceReady = _surfaceHolder.Surface?.IsValid == true || _surfaceReady;

        if (_started && _surfaceReady)
            OpenCamera();
    }

    public void SurfaceCreated(ASurfaceHolder holder)
    {
        _surfaceReady = true;

        if (_started)
            OpenCamera();
    }

    public void SurfaceChanged(
        ASurfaceHolder holder,
        global::Android.Graphics.Format format,
        int width,
        int height)
    {
        if (_camera is not null)
        {
            try
            {
                _camera.SetDisplayOrientation(GetDisplayOrientation());
            }
            catch
            {
            }
        }
    }

    public void SurfaceDestroyed(ASurfaceHolder holder)
    {
        _surfaceReady = false;
        CloseCamera();
    }

    private void OpenCamera()
    {
        if (!_started || !_surfaceReady || _cameraId < 0)
            return;

        try
        {
            CloseCamera();

            var camera = ACamera.Open(_cameraId);
            if (camera is null)
                throw new InvalidOperationException("تعذر فتح كاميرا Himo.");

            var parameters = camera.GetParameters();
            if (parameters is null)
            {
                camera.Release();
                throw new InvalidOperationException("تعذر قراءة إعدادات كاميرا Himo.");
            }

            var previewSize = ChoosePreviewSize(parameters.SupportedPreviewSizes);
            if (previewSize is not null)
            {
                parameters.SetPreviewSize(
                    previewSize.Width,
                    previewSize.Height);
            }

            var previewRatio = previewSize is null
                ? 4d / 3d
                : previewSize.Width / (double)Math.Max(1, previewSize.Height);

            var pictureSize = ChoosePictureSize(
                parameters.SupportedPictureSizes,
                previewRatio);
            if (pictureSize is not null)
            {
                parameters.SetPictureSize(
                    pictureSize.Width,
                    pictureSize.Height);

                var sensorRatio = pictureSize.Width / (double)Math.Max(1, pictureSize.Height);
                var jpegRotation = GetCaptureJpegRotation();
                _photoAspectRatio = jpegRotation % 180 == 0
                    ? (float)sensorRatio
                    : (float)(1d / Math.Max(0.01d, sensorRatio));
            }

            try
            {
                if (parameters.SupportedWhiteBalance?.Contains(
                        ACamera.Parameters.WhiteBalanceAuto) == true)
                {
                    parameters.WhiteBalance = ACamera.Parameters.WhiteBalanceAuto;
                }
            }
            catch
            {
                // Auto white balance is optional.
            }

            try
            {
                if (parameters.SupportedSceneModes?.Contains(
                        ACamera.Parameters.SceneModeAuto) == true)
                {
                    parameters.SceneMode = ACamera.Parameters.SceneModeAuto;
                }
            }
            catch
            {
                // Scene mode is optional.
            }

            try
            {
                parameters.JpegQuality = 100;
            }
            catch
            {
                // Some devices expose a fixed JPEG quality.
            }

            try
            {
                var focusModes = parameters.SupportedFocusModes;
                if (focusModes is not null)
                {
                    if (focusModes.Contains(ACamera.Parameters.FocusModeContinuousPicture))
                    {
                        parameters.FocusMode =
                            ACamera.Parameters.FocusModeContinuousPicture;
                    }
                    else if (focusModes.Contains(ACamera.Parameters.FocusModeAuto))
                    {
                        parameters.FocusMode = ACamera.Parameters.FocusModeAuto;
                    }
                }
            }
            catch
            {
                // Focus mode is optional.
            }

            ApplyLiveFilter(parameters, _activeFilterName);
            camera.SetParameters(parameters);

            var flashModes = parameters.SupportedFlashModes;
            _hasFlash = flashModes is not null &&
                        flashModes.Any(mode =>
                            string.Equals(mode, "torch", StringComparison.OrdinalIgnoreCase)) &&
                        flashModes.Any(mode =>
                            string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase));
            _flashOn = false;
            _maxZoomFactor = parameters.IsZoomSupported && parameters.MaxZoom > 0
                ? 4f
                : 1f;

            camera.SetDisplayOrientation(GetDisplayOrientation());

            var surface = _surfaceHolder.Surface;
            if (surface is null || !surface.IsValid)
            {
                camera.Release();
                return;
            }

            camera.SetPreviewDisplay(_surfaceHolder);
            camera.StartPreview();

            _camera = camera;
            _startTcs?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            try { _camera?.StopPreview(); } catch { }
            try { _camera?.Release(); } catch { }
            _camera = null;

            _startTcs?.TrySetException(
                new InvalidOperationException(
                    $"تعذر تشغيل كاميرا Himo: {ex.Message}",
                    ex));
        }
    }

    private static void ApplyLiveFilter(ACamera.Parameters parameters, string filterName)
    {
        var supportedEffects = parameters.SupportedColorEffects;
        var supportedWhiteBalance = parameters.SupportedWhiteBalance;
        var supportedScenes = parameters.SupportedSceneModes;

        // Start from neutral values where the camera supports them. This prevents
        // switching between filters from stacking old white-balance/scene settings.
        if (supportedEffects is not null)
        {
            var neutralEffect = FindSupported(supportedEffects, "none");
            if (neutralEffect is not null)
                parameters.ColorEffect = neutralEffect;
        }

        if (supportedWhiteBalance is not null)
        {
            var auto = FindSupported(supportedWhiteBalance, "auto");
            if (auto is not null)
                parameters.WhiteBalance = auto;
        }

        if (supportedScenes is not null)
        {
            var autoScene = FindSupported(supportedScenes, "auto");
            if (autoScene is not null)
                parameters.SceneMode = autoScene;
        }

        switch (filterName)
        {
            case "Mono":
                if (!TrySetColorEffect(parameters, supportedEffects, "mono"))
                    TrySetScene(parameters, supportedScenes, "portrait");
                break;

            case "Sepia":
                if (!TrySetColorEffect(parameters, supportedEffects, "sepia"))
                    TrySetColorEffect(parameters, supportedEffects, "aqua");
                break;

            case "Vivid":
                if (!TrySetColorEffect(parameters, supportedEffects, "vivid"))
                    TrySetScene(parameters, supportedScenes, "landscape");
                break;

            case "Warm":
                if (!TrySetWhiteBalance(parameters, supportedWhiteBalance, "cloudy-daylight"))
                    TrySetWhiteBalance(parameters, supportedWhiteBalance, "daylight");
                break;

            case "Cool":
                TrySetWhiteBalance(parameters, supportedWhiteBalance, "fluorescent");
                break;

            case "BeautyNatural":
                // Portrait scene, when supported, gives a more natural skin/face
                // rendering in the live preview. The actual smoothing is also
                // applied to the captured photo by PhotoFilterProcessor.
                TrySetScene(parameters, supportedScenes, "portrait");
                break;

            case "BeautySoft":
            case "BeautyGlow":
                TrySetScene(parameters, supportedScenes, "portrait");
                if (!TrySetWhiteBalance(parameters, supportedWhiteBalance, "cloudy-daylight"))
                    TrySetWhiteBalance(parameters, supportedWhiteBalance, "daylight");
                break;

            case "Natural":
            default:
                break;
        }
    }

    private static bool TrySetColorEffect(
        ACamera.Parameters parameters,
        IList<string>? supported,
        string wanted)
    {
        var value = FindSupported(supported, wanted);
        if (value is null)
            return false;

        parameters.ColorEffect = value;
        return true;
    }

    private static bool TrySetWhiteBalance(
        ACamera.Parameters parameters,
        IList<string>? supported,
        string wanted)
    {
        var value = FindSupported(supported, wanted);
        if (value is null)
            return false;

        parameters.WhiteBalance = value;
        return true;
    }

    private static bool TrySetScene(
        ACamera.Parameters parameters,
        IList<string>? supported,
        string wanted)
    {
        var value = FindSupported(supported, wanted);
        if (value is null)
            return false;

        parameters.SceneMode = value;
        return true;
    }

    private static string? FindSupported(IList<string>? values, string wanted)
    {
        if (values is null)
            return null;

        return values.FirstOrDefault(
            value => string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private void CloseCamera()
    {
        if (_recording)
        {
            try
            {
                CancelRecordingAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        try { _camera?.StopPreview(); } catch { }
        try { _camera?.Release(); } catch { }
        _camera = null;
        _hasFlash = false;
        _flashOn = false;
        _maxZoomFactor = 1f;
    }

    private void StopInternal(bool deleteRecording)
    {
        _started = false;

        var path = _recordingPath;

        try
        {
            if (_recording)
                CancelRecordingAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        if (deleteRecording)
            TryDelete(path);

        CloseCamera();

        _surfaceReady = false;
        _startTcs?.TrySetCanceled();
        _startTcs = null;
    }

    private static int FindAnyCameraId()
    {
        var count = ACamera.NumberOfCameras;
        return count > 0 ? 0 : -1;
    }

    private static int FindCameraId(ACameraFacing facing)
    {
        var count = ACamera.NumberOfCameras;

        for (var i = 0; i < count; i++)
        {
            var info = new ACamera.CameraInfo();
            ACamera.GetCameraInfo(i, info);

            if (info.Facing == facing)
                return i;
        }

        return -1;
    }

    private ACamera.Size? ChoosePreviewSize(IList<ACamera.Size>? sizes)
    {
        if (sizes is null || sizes.Count == 0)
            return null;

        var displayOrientation = GetDisplayOrientation();
        var surfaceWidth = _surfaceView.Width;
        var surfaceHeight = _surfaceView.Height;

        // Camera1 sizes are sensor-oriented. When the preview is rotated by
        // 90/270 degrees, compare the sensor ratio against the inverse of
        // the portrait surface ratio. This prevents the common 16:9 stretched
        // selfie effect on a 3:4 phone preview.
        var surfaceRatio = surfaceWidth > 0 && surfaceHeight > 0
            ? surfaceWidth / (double)surfaceHeight
            : 3d / 4d;

        var targetRatio = displayOrientation % 180 == 0
            ? surfaceRatio
            : 1d / Math.Max(0.01d, surfaceRatio);

        var preferred = sizes
            .Where(size => size.Width >= 640 && size.Height >= 480)
            .Where(size => size.Width <= 1920 && size.Height <= 1440)
            .OrderBy(size => Math.Abs(
                size.Width / (double)Math.Max(1, size.Height) - targetRatio))
            .ThenByDescending(size => size.Width * (long)size.Height)
            .FirstOrDefault();

        if (preferred is not null)
            return preferred;

        return sizes
            .OrderBy(size => Math.Abs(
                size.Width / (double)Math.Max(1, size.Height) - targetRatio))
            .ThenByDescending(size => size.Width * (long)size.Height)
            .FirstOrDefault();
    }

    private static ACamera.Size? ChooseVideoSize(
        IList<ACamera.Size>? sizes,
        double targetRatio)
    {
        if (sizes is null || sizes.Count == 0)
            return null;

        var sensorTargetRatio = targetRatio < 1d
            ? 1d / Math.Max(0.01d, targetRatio)
            : targetRatio;

        var valid = sizes
            .Where(size => size.Width >= 640 && size.Height >= 480)
            .Where(size => size.Width <= 1920 && size.Height <= 1920)
            .ToList();

        if (valid.Count == 0)
            valid = sizes.ToList();

        var close = valid
            .OrderBy(size => Math.Abs(
                size.Width / (double)Math.Max(1, size.Height) - sensorTargetRatio))
            .ThenByDescending(size => size.Width * (long)size.Height)
            .FirstOrDefault();

        return close;
    }

    private static ACamera.Size? ChoosePictureSize(
        IList<ACamera.Size>? sizes,
        double targetRatio)
    {
        if (sizes is null || sizes.Count == 0)
            return null;

        var candidates = sizes
            .Where(size => size.Width <= 4096 && size.Height <= 4096)
            .OrderBy(size => Math.Abs(
                size.Width / (double)Math.Max(1, size.Height) - targetRatio))
            .ThenByDescending(size => size.Width * (long)size.Height)
            .ToList();

        return candidates.FirstOrDefault()
               ?? sizes
                   .OrderByDescending(size => size.Width * (long)size.Height)
                   .FirstOrDefault();
    }

    private int GetDisplayOrientation()
    {
        var info = new ACamera.CameraInfo();
        ACamera.GetCameraInfo(_cameraId, info);

        var rotation = 0;

        if (Platform.CurrentActivity?.WindowManager?.DefaultDisplay is ADisplay display)
        {
            rotation = display.Rotation switch
            {
                ASurfaceOrientation.Rotation0 => 0,
                ASurfaceOrientation.Rotation90 => 90,
                ASurfaceOrientation.Rotation180 => 180,
                ASurfaceOrientation.Rotation270 => 270,
                _ => 0
            };
        }

        if (info.Facing == ACameraFacing.Front)
        {
            // Front-facing previews are mirrored by the legacy Camera API.
            // Compensate for that mirror when calculating the display rotation
            // so the preview remains upright instead of appearing inverted.
            var frontResult = (info.Orientation + rotation) % 360;
            return (360 - frontResult) % 360;
        }

        return (info.Orientation - rotation + 360) % 360;
    }

    private int GetCaptureJpegRotation()
    {
        var info = new ACamera.CameraInfo();
        ACamera.GetCameraInfo(_cameraId, info);

        var rotation = 0;

        if (Platform.CurrentActivity?.WindowManager?.DefaultDisplay is ADisplay display)
        {
            rotation = display.Rotation switch
            {
                ASurfaceOrientation.Rotation0 => 0,
                ASurfaceOrientation.Rotation90 => 90,
                ASurfaceOrientation.Rotation180 => 180,
                ASurfaceOrientation.Rotation270 => 270,
                _ => 0
            };
        }

        // Camera1 preview orientation and JPEG orientation are related but
        // the JPEG returned by TakePicture is encoded in the sensor's native
        // orientation unless Parameters.Rotation is explicitly set. We
        // normalize the pixels here so the saved file itself is upright and
        // no later screen/image control has to interpret EXIF orientation.
        if (info.Facing == ACameraFacing.Front)
        {
            var front = (info.Orientation + rotation) % 360;
            return (360 - front) % 360;
        }

        return (info.Orientation - rotation + 360) % 360;
    }

    private static byte[] NormalizeCapturedJpeg(byte[] data, int rotationDegrees)
    {
        if (data.Length == 0 || rotationDegrees % 360 == 0)
            return data;

        try
        {
            using var source = ABitmapFactory.DecodeByteArray(data, 0, data.Length);
            if (source is null)
                return data;

            using var matrix = new AMatrix();
            matrix.PostRotate(rotationDegrees);

            using var rotated = ABitmap.CreateBitmap(
                source,
                0,
                0,
                source.Width,
                source.Height,
                matrix,
                true);

            if (rotated is null)
                return data;

            using var output = new MemoryStream();
            if (!rotated.Compress(ABitmap.CompressFormat.Jpeg!, 100, output))
                return data;

            return output.ToArray();
        }
        catch
        {
            // Keep the original JPEG if a device cannot decode/re-encode it.
            return data;
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public new void Dispose()
    {
        StopInternal(deleteRecording: true);

        try
        {
            _surfaceHolder.RemoveCallback(this);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
        base.Dispose();
    }

    private sealed class AutoFocusCallback : Java.Lang.Object, ACamera.IAutoFocusCallback
    {
        public void OnAutoFocus(bool success, ACamera? camera)
        {
            // The camera's continuous/auto focus mode continues after this callback.
        }
    }

    private sealed class JpegPictureCallback :
        Java.Lang.Object,
        ACamera.IPictureCallback
    {
        private readonly Func<byte[], Task> _callback;

        public JpegPictureCallback(Func<byte[], Task> callback)
        {
            _callback = callback;
        }

        public void OnPictureTaken(
            byte[]? data,
            ACamera? camera)
        {
            if (data is null || data.Length == 0)
                return;

            _ = _callback(data);
        }
    }
}
#pragma warning restore CS0618, CA1422, CA1416, CS0108
