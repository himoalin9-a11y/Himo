using System.Globalization;
using Himo.Platforms.Android;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Himo.Views;

public partial class CameraPage : ContentPage
{
    internal readonly TaskCompletionSource<MediaEditorCaptureResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CameraCaptureMode _mode;
    private bool _recording;
    private bool _closing;
    private bool _gridEnabled;
    private bool _flashOn;
    private bool _filterPanelVisible;
    private int _photoTimerSeconds;
    private float _zoomFactor = 1f;
    private HimoPhotoFilter _selectedFilter = HimoPhotoFilter.Natural;
    private DateTimeOffset _recordingStartedAt;
    private DateTimeOffset _pauseStartedAt;
    private TimeSpan _pausedDuration;
    private bool _recordingPaused;
    private bool _editorOpen;

    private Color ThemeAccent => GetThemeColor("HimoAccent", "#6C2BD9");
    private static Color CameraIdleColor => Color.FromArgb("#3DFFFFFF");
    private static Color CameraChipIdleColor => Color.FromArgb("#333333");
    private static Color RecordingColor => Color.FromArgb("#D92D20");

    private Color GetThemeColor(string key, string fallback)
    {
        if (Resources.TryGetValue(key, out var pageValue) && pageValue is Color pageColor)
            return pageColor;

        var appResources = Application.Current?.Resources;
        if (appResources is not null && appResources.TryGetValue(key, out var appValue) && appValue is Color appColor)
            return appColor;

        return Color.FromArgb(fallback);
    }

    public CameraPage(CameraCaptureMode initialMode = CameraCaptureMode.Photo)
    {
        InitializeComponent();
        Himo.Services.AppLocalization.AttachToPage(this);
        _mode = initialMode;
        UpdateModeUi();
    }

    public static async Task<MediaEditorCaptureResult?> OpenAsync(
        CameraCaptureMode initialMode = CameraCaptureMode.Photo)
    {
        var page = new CameraPage(initialMode);
        var navigation = Shell.Current?.Navigation
            ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

        if (navigation is null)
            return null;

        await navigation.PushModalAsync(page, animated: true);
        return await page._completion.Task;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // While the photo editor is shown above this page, do not restart the
        // camera when the modal stack returns. The capture flow will either
        // close this page after an explicit Send, or restart the camera after
        // the editor is cancelled.
        if (_editorOpen || _closing)
            return;

        await StartCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        if (_closing || _editorOpen)
            return;

        try
        {
            var cameraPermission = await Permissions.RequestAsync<Permissions.Camera>();
            if (cameraPermission != PermissionStatus.Granted)
            {
                await DisplayAlertAsync(
                    Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"),
                    Himo.Services.AppLocalization.TranslateSourceText("اسمح لـ Himo باستخدام الكاميرا ثم حاول مرة أخرى."),
                    Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                await CloseAsync(null);
                return;
            }

            await Camera.StartAsync();
            await Camera.SetFilterAsync(_selectedFilter.ToString());
            UpdateCameraControls();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"),
                $"تعذر تشغيل كاميرا Himo: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            await CloseAsync(null);
        }
    }

    protected override async void OnDisappearing()
    {
        if (!_closing)
        {
            try
            {
                if (_recording)
                    await Camera.CancelRecordingAsync();

                await Camera.StopAsync();
            }
            catch
            {
            }

            // Navigating to the editor is an internal step of the camera flow;
            // do not complete OpenAsync until the user explicitly sends or
            // cancels the editor.
            if (!_editorOpen)
                _completion.TrySetResult(null);
        }

        base.OnDisappearing();
    }

    private void PhotoModeClicked(object? sender, EventArgs e)
    {
        if (_recording)
            return;

        _mode = CameraCaptureMode.Photo;
        _photoTimerSeconds = 0;
        UpdateModeUi();
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
    }

    private void VideoModeClicked(object? sender, EventArgs e)
    {
        if (_recording)
            return;

        _mode = CameraCaptureMode.Video;
        _filterPanelVisible = false;
        UpdateModeUi();
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
    }

    private async void CaptureClicked(object? sender, EventArgs e)
    {
        CaptureButton.IsEnabled = false;

        try
        {
            if (_mode == CameraCaptureMode.Photo)
            {
                await RunPhotoCountdownAsync();

                var result = await Camera.CapturePhotoAsync();
                if (result is not null && File.Exists(result.FilePath))
                {
                    if (_selectedFilter != HimoPhotoFilter.Natural)
                    {
                        StatusBorder.IsVisible = true;
                        StatusLabel.Text = IsBeautyFilter(_selectedFilter)
                            ? "جارٍ تطبيق التجميل…"
                            : "جارٍ تحسين الصورة…";
                        await PhotoFilterProcessor.ApplyAsync(
                            result.FilePath,
                            _selectedFilter);
                        StatusBorder.IsVisible = false;
                    }

                    // The camera never sends the raw capture. It opens the editor
                    // while this page remains underneath. OpenAsync completes only
                    // after the editor's explicit Send button is pressed.
                    _editorOpen = true;
                    MediaEditorCaptureResult? edited = null;
                    try
                    {
                        edited = await MediaEditorPage.EditCameraCaptureAsync(result);
                    }
                    finally
                    {
                        _editorOpen = false;
                    }

                    if (edited is not null)
                    {
                        await CloseAsync(edited);
                    }
                    else
                    {
                        await StartCameraAsync();
                    }
                }

                return;
            }

            if (!_recording)
            {
                var microphone = await Permissions.RequestAsync<Permissions.Microphone>();
                if (microphone != PermissionStatus.Granted)
                {
                    await DisplayAlertAsync(
                        Himo.Services.AppLocalization.TranslateSourceText("الفيديو"),
                        Himo.Services.AppLocalization.TranslateSourceText("اسمح لـ Himo باستخدام الميكروفون لتسجيل صوت الفيديو."),
                        Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                    return;
                }

                await Camera.StartRecordingAsync();
                _recording = true;
                _recordingPaused = false;
                _pausedDuration = TimeSpan.Zero;
                _recordingStartedAt = DateTimeOffset.UtcNow;
                StatusBorder.IsVisible = true;
                UpdateModeUi();
                StartRecordingTimer();
                return;
            }

            if ((DateTimeOffset.UtcNow - _recordingStartedAt).TotalMilliseconds < 800)
                await Task.Delay(250);

            var videoResult = await Camera.StopRecordingAsync();
            _recording = false;
            _recordingPaused = false;
            _pausedDuration = TimeSpan.Zero;
            StatusBorder.IsVisible = false;
            UpdateModeUi();

            if (videoResult is not null)
                await CloseAsync(new MediaEditorCaptureResult(videoResult, string.Empty));
        }
        catch (Exception ex)
        {
            _recording = false;
            _recordingPaused = false;
            _pausedDuration = TimeSpan.Zero;
            StatusBorder.IsVisible = false;
            UpdateModeUi();

            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"),
                $"تعذر التقاط الوسائط: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            CaptureButton.IsEnabled = true;
        }
    }

    private async void PauseResumeClicked(object? sender, EventArgs e)
    {
        if (!_recording)
            return;

        try
        {
            if (!_recordingPaused)
            {
                await Camera.PauseRecordingAsync();
                _recordingPaused = true;
                _pauseStartedAt = DateTimeOffset.UtcNow;
                StatusLabel.Text = "Ⅱ  متوقف مؤقتًا";
            }
            else
            {
                await Camera.ResumeRecordingAsync();
                _pausedDuration += DateTimeOffset.UtcNow - _pauseStartedAt;
                _recordingPaused = false;
                StatusLabel.Text = "●  00:00";
            }

            UpdateModeUi();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الفيديو"),
                ex.Message,
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task RunPhotoCountdownAsync()
    {
        if (_photoTimerSeconds <= 0)
            return;

        StatusBorder.IsVisible = true;
        for (var remaining = _photoTimerSeconds; remaining > 0; remaining--)
        {
            StatusLabel.Text = remaining.ToString(CultureInfo.InvariantCulture);
            await Task.Delay(1000);
        }

        StatusBorder.IsVisible = false;
    }

    private void StartRecordingTimer()
    {
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(250), () =>
        {
            if (!_recording || _closing)
                return false;

            if (_recordingPaused)
            {
                StatusLabel.Text = "Ⅱ  متوقف مؤقتًا";
                return true;
            }

            var elapsed = DateTimeOffset.UtcNow - _recordingStartedAt - _pausedDuration;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            StatusLabel.Text = $"●  {elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture)}";
            return true;
        });
    }

    private void FilterToggleClicked(object? sender, EventArgs e)
    {
        if (_recording || _mode != CameraCaptureMode.Photo)
            return;

        _filterPanelVisible = !_filterPanelVisible;
        FilterPanel.IsVisible = _filterPanelVisible;
        FilterToggleButton.Text = "فلاتر";
        FilterToggleButton.BackgroundColor = _filterPanelVisible || _selectedFilter != HimoPhotoFilter.Natural
            ? ThemeAccent
            : CameraIdleColor;
    }

    private async void FilterSelectedClicked(object? sender, EventArgs e)
    {
        if (_recording || _mode != CameraCaptureMode.Photo || sender is not Button button)
            return;

        if (!Enum.TryParse(
                button.CommandParameter?.ToString(),
                ignoreCase: true,
                out HimoPhotoFilter filter))
        {
            return;
        }

        _selectedFilter = filter;
        _filterPanelVisible = false;
        FilterPanel.IsVisible = false;
        FilterToggleButton.Text = "فلاتر";
        FilterToggleButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Natural
            ? CameraIdleColor
            : ThemeAccent;

        await Camera.SetFilterAsync(_selectedFilter.ToString());
        UpdateLiveFilterOverlay();
        FilterToggleButton.Text = "فلاتر";

        UpdateFilterUi();
    }

    private void TimerClicked(object? sender, EventArgs e)
    {
        if (_recording || _mode != CameraCaptureMode.Photo)
            return;

        _photoTimerSeconds = _photoTimerSeconds switch
        {
            0 => 3,
            3 => 5,
            _ => 0
        };

        TimerButton.Text = $"مؤقت {_photoTimerSeconds}s";
        TimerButton.BackgroundColor =
            _photoTimerSeconds > 0
                ? ThemeAccent
                : Color.FromArgb("#33000000");
    }

    private async void FlashClicked(object? sender, EventArgs e)
    {
        if (!Camera.HasFlash || _recording)
            return;

        try
        {
            await Camera.ToggleFlashAsync();
            _flashOn = Camera.IsFlashOn;
            UpdateCameraControls();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفلاش"), $"تعذر تغيير الفلاش: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void GridClicked(object? sender, EventArgs e)
    {
        _gridEnabled = !_gridEnabled;
        GridOverlay.IsVisible = _gridEnabled;
        GridButton.BackgroundColor = _gridEnabled
            ? ThemeAccent
            : CameraIdleColor;
    }

    private async void FocusClicked(object? sender, EventArgs e)
    {
        if (_recording)
            return;

        try
        {
            await Camera.FocusAsync();
            FocusButton.Text = "تم";
            await Task.Delay(450);
            FocusButton.Text = "تركيز";
        }
        catch
        {
            FocusButton.Text = "تركيز";
        }
    }

    private async void ZoomClicked(object? sender, EventArgs e)
    {
        if (_recording || sender is not Button button)
            return;

        if (!float.TryParse(
                button.CommandParameter?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var requestedZoom))
        {
            return;
        }

        requestedZoom = Math.Clamp(requestedZoom, 1f, Math.Max(1f, Camera.MaxZoomFactor));
        _zoomFactor = requestedZoom;

        await Camera.SetZoomAsync(_zoomFactor);
        UpdateZoomUi();
    }

    private async void SwitchCameraClicked(object? sender, EventArgs e)
    {
        if (_recording)
            return;

        try
        {
            await Camera.SwitchCameraAsync();
            _flashOn = Camera.IsFlashOn;
            _zoomFactor = 1f;
            await Camera.SetZoomAsync(1f);
            UpdateCameraControls();
            UpdateZoomUi();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"),
                $"تعذر تبديل الكاميرا: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void CloseClicked(object? sender, EventArgs e)
    {
        await CloseAsync(null);
    }

    private async Task CloseAsync(MediaEditorCaptureResult? result)
    {
        if (_closing)
            return;

        _closing = true;
        try
        {
            if (_recording)
            {
                await Camera.CancelRecordingAsync();
                _recording = false;
                _recordingPaused = false;
                _pausedDuration = TimeSpan.Zero;
            }

            await Camera.StopAsync();

            // IMPORTANT: remove the camera modal before completing OpenAsync.
            // Completing first lets the caller push the photo editor while the
            // camera page is still the top modal; the pending PopModalAsync can
            // then pop the editor instead of the camera, which was the root cause
            // of the editor disappearing and the black-camera state returning.
            var navigation = Navigation;
            if (navigation.ModalStack.Count > 0 && ReferenceEquals(navigation.ModalStack[^1], this))
                await navigation.PopModalAsync(animated: true);

            _completion.TrySetResult(result);
        }
        catch
        {
            _completion.TrySetResult(result);
        }
    }

    private void UpdateModeUi()
    {
        var photo = _mode == CameraCaptureMode.Photo;

        PhotoModeButton.BackgroundColor = photo
            ? ThemeAccent
            : CameraChipIdleColor;

        VideoModeButton.BackgroundColor = photo
            ? CameraChipIdleColor
            : ThemeAccent;

        TimerButton.IsEnabled = photo && !_recording;
        TimerButton.Opacity = TimerButton.IsEnabled ? 1 : 0.45;

        CameraHintLabel.Text = photo
            ? "صورة عالية الجودة"
            : "فيديو + صوت";

        CaptureButton.Text = _recording ? "■" : string.Empty;
        CaptureButton.ImageSource = _recording
            ? null
            : ImageSource.FromFile(photo ? "himo_cam_shutter.png" : "himo_cam_video.png");

        CaptureButton.BackgroundColor = _recording
            ? RecordingColor
            : ThemeAccent;

        StatusBorder.IsVisible = _recording;
        PauseResumeButton.IsVisible = _recording && !photo;
        PauseResumeButton.IsEnabled = _recording;
        PauseResumeButton.Text = string.Empty;
        PauseResumeButton.ImageSource = ImageSource.FromFile(
            _recordingPaused ? "himo_cam_play.png" : "himo_cam_pause.png");
        PauseResumeButton.BackgroundColor = _recordingPaused
            ? ThemeAccent
            : CameraChipIdleColor;
        FilterToggleButton.IsEnabled = photo && !_recording;
        FilterToggleButton.Text = "فلاتر";
        FilterToggleButton.Opacity = FilterToggleButton.IsEnabled ? 1 : 0.45;
        if (!FilterToggleButton.IsEnabled)
        {
            _filterPanelVisible = false;
            FilterPanel.IsVisible = false;
        }

        FilterPanel.IsVisible = _filterPanelVisible && photo && !_recording;
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
        UpdateFilterUi();
        UpdateCameraControls();
        UpdateZoomUi();
    }

    private static bool IsBeautyFilter(HimoPhotoFilter filter)
        => filter is HimoPhotoFilter.BeautyNatural
            or HimoPhotoFilter.BeautySoft
            or HimoPhotoFilter.BeautyGlow
            or HimoPhotoFilter.BeautyMakeupSoft
            or HimoPhotoFilter.BeautyMakeup
            or HimoPhotoFilter.ClearSkin;

    private void UpdateFilterUi()
    {
        var selected = ThemeAccent;
        var normal = Color.FromArgb("#252525");

        FilterNaturalButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Natural ? selected : normal;
        FilterVividButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Vivid ? selected : normal;
        FilterWarmButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Warm ? selected : normal;
        FilterCoolButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Cool ? selected : normal;
        FilterMonoButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Mono ? selected : normal;
        FilterSepiaButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Sepia ? selected : normal;
        FilterBeautyNaturalButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.BeautyNatural ? selected : normal;
        FilterBeautySoftButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.BeautySoft ? selected : normal;
        FilterBeautyGlowButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.BeautyGlow ? selected : normal;
        FilterBeautyMakeupSoftButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.BeautyMakeupSoft ? selected : normal;
        FilterBeautyMakeupButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.BeautyMakeup ? selected : normal;
        FilterClearSkinButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.ClearSkin ? selected : normal;

        UpdateLiveFilterOverlay();
    }

    private void UpdateLiveFilterOverlay()
    {
        if (FilterPreviewOverlay is null)
            return;

        var overlay = _selectedFilter switch
        {
            HimoPhotoFilter.Vivid => (Color.FromArgb("#FFF3C43E"), 0.10),
            HimoPhotoFilter.Warm => (Color.FromArgb("#FFFF8A65"), 0.16),
            HimoPhotoFilter.Cool => (Color.FromArgb("#FF64B5F6"), 0.14),
            HimoPhotoFilter.Mono => (Color.FromArgb("#FFB0B0B0"), 0.30),
            HimoPhotoFilter.Sepia => (Color.FromArgb("#FF8D6E63"), 0.22),
            HimoPhotoFilter.BeautyNatural => (Color.FromArgb("#FFFFD7C7"), 0.10),
            HimoPhotoFilter.BeautySoft => (Color.FromArgb("#FFFFC9B8"), 0.14),
            HimoPhotoFilter.BeautyGlow => (Color.FromArgb("#FFFFE0B2"), 0.17),
            HimoPhotoFilter.BeautyMakeupSoft => (Color.FromArgb("#FFFFB7C8"), 0.16),
            HimoPhotoFilter.BeautyMakeup => (Color.FromArgb("#FFFF9EB9"), 0.20),
            HimoPhotoFilter.ClearSkin => (Color.FromArgb("#FFFFE4D6"), 0.09),
            _ => (Color.FromArgb("#00000000"), 0d)
        };

        FilterPreviewOverlay.BackgroundColor = overlay.Item1;
        FilterPreviewOverlay.Opacity = overlay.Item2;
        FilterPreviewOverlay.IsVisible = overlay.Item2 > 0 && !_recording;

        if (BeautyPreviewOverlay is not null)
        {
            BeautyPreviewOverlay.IsVisible = IsBeautyFilter(_selectedFilter) && !_recording;
            BeautyPreviewOverlay.Opacity = _selectedFilter switch
            {
                HimoPhotoFilter.BeautyNatural => 0.025,
                HimoPhotoFilter.BeautySoft => 0.04,
                HimoPhotoFilter.BeautyGlow => 0.055,
                HimoPhotoFilter.BeautyMakeupSoft => 0.05,
                HimoPhotoFilter.BeautyMakeup => 0.08,
                HimoPhotoFilter.ClearSkin => 0.035,
                _ => 0d
            };
        }
    }

    private float GetPreviewAspectRatio()
    {
        var ratio = Camera.PhotoAspectRatio;
        if (ratio > 0.1f && ratio < 10f)
            return ratio;

        var display = DeviceDisplay.Current.MainDisplayInfo;
        return display.Height >= display.Width ? 3f / 4f : 4f / 3f;
    }

    private void UpdateCameraControls()
    {
        FlashButton.IsEnabled = Camera.HasFlash && !_recording;
        FlashButton.Opacity = FlashButton.IsEnabled ? 1 : 0.45;
        _flashOn = Camera.IsFlashOn;
        FlashButton.Text = "فلاش";
        FlashButton.BackgroundColor = _flashOn
            ? ThemeAccent
            : CameraIdleColor;
    }

    private void UpdateZoomUi()
    {
        var max = Math.Max(1f, Camera.MaxZoomFactor);
        Zoom1Button.IsEnabled = max >= 1f;
        Zoom2Button.IsEnabled = max >= 2f;
        Zoom4Button.IsEnabled = max >= 4f;

        Zoom1Button.BackgroundColor = Math.Abs(_zoomFactor - 1f) < 0.05f
            ? ThemeAccent
            : Color.FromArgb("#66000000");

        Zoom2Button.BackgroundColor = Math.Abs(_zoomFactor - 2f) < 0.05f
            ? ThemeAccent
            : Color.FromArgb("#66000000");

        Zoom4Button.BackgroundColor = Math.Abs(_zoomFactor - 4f) < 0.05f
            ? ThemeAccent
            : Color.FromArgb("#66000000");
    }
}
