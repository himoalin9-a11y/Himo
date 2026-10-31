using System.Globalization;
using System.Collections.ObjectModel;
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
    private bool _captureInProgress;
    private bool _videoNoteMode;
    private readonly ObservableCollection<RecentCameraCapture> _recentCaptures = new();

    private Color ThemeAccent => GetThemeColor("HimoAccent", "#5148FF");
    private static Color CameraIdleColor => Color.FromArgb("#66000000");
    private Color CameraChipIdleColor => GetThemeColor("HimoSoft", "#292929");
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
        RecentStrip.ItemsSource = _recentCaptures;
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
        Himo.Services.HimoAppearanceManager.ApplyThemedIconSourcesToTree(this);

        // While the photo editor is shown above this page, do not restart the
        // camera when the modal stack returns. The capture flow will either
        // close this page after an explicit Send, or restart the camera after
        // the editor is cancelled.
        if (_editorOpen || _closing)
            return;

        RefreshRecentCaptures();
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

            // Size the preview to the available viewport before opening Camera1,
            // so Android selects a preview stream that fills the screen area.
            Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
            await Camera.StartAsync();
            await Camera.SetFilterAsync(_mode == CameraCaptureMode.Photo
                ? _selectedFilter.ToString()
                : HimoPhotoFilter.Natural.ToString());
            UpdateModeUi();
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

    private async void PhotoModeClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress)
            return;

        _mode = CameraCaptureMode.Photo;
        _videoNoteMode = false;
        UpdateModeUi();
        await Camera.SetFilterAsync(_selectedFilter.ToString());
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
        UpdateFilterUi();
    }

    private async void VideoModeClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress)
            return;

        _mode = CameraCaptureMode.Video;
        _videoNoteMode = false;
        _filterPanelVisible = false;
        FilterPanel.IsVisible = false;
        UpdateModeUi();
        // Filters are photo-oriented in this screen. Keep video color natural and
        // restore the selected photo treatment if the user switches back to Photo.
        await Camera.SetFilterAsync(HimoPhotoFilter.Natural.ToString());
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
        UpdateLiveFilterOverlay();
    }

    private async void VideoNoteModeClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress)
            return;

        _mode = CameraCaptureMode.Video;
        _videoNoteMode = true;
        _filterPanelVisible = false;
        FilterPanel.IsVisible = false;
        UpdateModeUi();
        await Camera.SetFilterAsync(HimoPhotoFilter.Natural.ToString());
        Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
        UpdateLiveFilterOverlay();
    }

    private async void CaptureClicked(object? sender, EventArgs e)
    {
        if (_captureInProgress || _closing)
            return;

        _captureInProgress = true;
        CaptureButton.IsEnabled = false;
        UpdateModeUi();

        try
        {
            if (_mode == CameraCaptureMode.Photo)
            {
                await RunPhotoCountdownAsync();
                StatusBorder.IsVisible = true;
                StatusLabel.Text = "جارٍ التقاط الصورة…";

                var result = await Camera.CapturePhotoAsync();
                StatusBorder.IsVisible = false;
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

            StatusLabel.Text = "جارٍ حفظ الفيديو…";
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
            _captureInProgress = false;
            CaptureButton.IsEnabled = !_closing;
            if (!_closing)
                UpdateModeUi();
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
        if (_recording || _captureInProgress || _mode != CameraCaptureMode.Photo)
            return;

        _filterPanelVisible = !_filterPanelVisible;
        FilterPanel.IsVisible = _filterPanelVisible;
        FilterToggleButton.Text = string.Empty;
        FilterToggleButton.BackgroundColor = _filterPanelVisible || _selectedFilter != HimoPhotoFilter.Natural
            ? ThemeAccent
            : CameraIdleColor;
    }

    private async void FilterSelectedClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress || _mode != CameraCaptureMode.Photo || sender is not Button button)
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
        FilterToggleButton.Text = string.Empty;
        FilterToggleButton.BackgroundColor = _selectedFilter == HimoPhotoFilter.Natural
            ? CameraIdleColor
            : ThemeAccent;

        await Camera.SetFilterAsync(_selectedFilter.ToString());
        UpdateLiveFilterOverlay();
        FilterToggleButton.Text = string.Empty;

        UpdateFilterUi();
    }

    private void TimerClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress || _mode != CameraCaptureMode.Photo)
            return;

        _photoTimerSeconds = _photoTimerSeconds switch
        {
            0 => 3,
            3 => 5,
            _ => 0
        };

        TimerButton.Text = _photoTimerSeconds == 0 ? "0s" : $"{_photoTimerSeconds}s";
        TimerButton.BackgroundColor =
            _photoTimerSeconds > 0
                ? ThemeAccent
                : Color.FromArgb("#33000000");
    }

    private async void FlashClicked(object? sender, EventArgs e)
    {
        if (!Camera.HasFlash || _recording || _captureInProgress)
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
        if (_recording || _captureInProgress)
            return;

        try
        {
            FocusButton.Opacity = 0.55;
            await Camera.FocusAsync();
            await Task.Delay(220);
        }
        catch
        {
            // A few camera devices do not expose a focus mode; keep the UI responsive.
        }
        finally
        {
            FocusButton.Opacity = FocusButton.IsEnabled ? 1 : 0.45;
        }
    }

    private async void ZoomClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress || sender is not Button button)
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
        if (_recording || _captureInProgress)
            return;

        try
        {
            await Camera.SwitchCameraAsync();
            _flashOn = Camera.IsFlashOn;
            _zoomFactor = 1f;
            await Camera.SetZoomAsync(1f);
            Camera.SetPreviewAspectRatio(GetPreviewAspectRatio());
            await Camera.SetFilterAsync(_mode == CameraCaptureMode.Photo
                ? _selectedFilter.ToString()
                : HimoPhotoFilter.Natural.ToString());
            UpdateModeUi();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"),
                $"تعذر تبديل الكاميرا: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void GalleryClicked(object? sender, EventArgs e)
    {
        if (_recording || _captureInProgress || _closing)
            return;

        try
        {
            // PickPhotosAsync is the current MAUI API. Only the first image is used here.
            var pickedPhotos = await Microsoft.Maui.Media.MediaPicker.Default.PickPhotosAsync(
                new Microsoft.Maui.Media.MediaPickerOptions { Title = "اختيار صورة" });
            var picked = pickedPhotos?.FirstOrDefault();
            if (picked is null)
                return;

            var extension = Path.GetExtension(picked.FileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                extension = ".jpg";
            var localPath = Path.Combine(
                Microsoft.Maui.Storage.FileSystem.CacheDirectory,
                $"Himo_gallery_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");

            await using (var source = await picked.OpenReadAsync())
            await using (var destination = File.Create(localPath))
                await source.CopyToAsync(destination);

            var contentType = string.IsNullOrWhiteSpace(picked.ContentType)
                ? "image/jpeg"
                : picked.ContentType!;
            await OpenMediaInEditorAsync(new CameraCaptureResult(
                localPath,
                Path.GetFileName(picked.FileName),
                contentType));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("المعرض"),
                $"تعذر فتح الصورة: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void RecentCaptureSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentStrip is not null)
            RecentStrip.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not RecentCameraCapture selected)
            return;
        if (!File.Exists(selected.FilePath) || _recording || _captureInProgress || _closing)
            return;

        await OpenMediaInEditorAsync(new CameraCaptureResult(
            selected.FilePath,
            selected.FileName,
            selected.ContentType));
    }

    private async Task OpenMediaInEditorAsync(CameraCaptureResult capture)
    {
        if (_recording || _captureInProgress || _closing)
            return;

        _captureInProgress = true;
        UpdateModeUi();
        try
        {
            await Camera.StopAsync();
            _editorOpen = true;
            MediaEditorCaptureResult? edited;
            try
            {
                edited = await MediaEditorPage.EditCameraCaptureAsync(capture);
            }
            finally
            {
                _editorOpen = false;
            }

            _captureInProgress = false;
            if (edited is not null)
                await CloseAsync(edited);
            else
                await StartCameraAsync();
        }
        catch (Exception ex)
        {
            _captureInProgress = false;
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("المعرض"),
                $"تعذر فتح الملف المحدد: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            await StartCameraAsync();
        }
        finally
        {
            if (!_closing)
            {
                _captureInProgress = false;
                UpdateModeUi();
            }
        }
    }

    private void RefreshRecentCaptures()
    {
        try
        {
            var cache = Microsoft.Maui.Storage.FileSystem.CacheDirectory;
            var files = Directory.EnumerateFiles(cache, "Himo_*")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists &&
                    (file.Name.StartsWith("Himo_photo_", StringComparison.OrdinalIgnoreCase) &&
                     file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     file.Name.StartsWith("Himo_video_", StringComparison.OrdinalIgnoreCase) &&
                     file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(5)
                .ToList();

            _recentCaptures.Clear();
            foreach (var file in files)
            {
                var isVideo = file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
                _recentCaptures.Add(new RecentCameraCapture(
                    file.FullName,
                    file.Name,
                    isVideo ? "video/mp4" : "image/jpeg",
                    isVideo));
            }
            RecentStripHost.IsVisible = _recentCaptures.Count > 0;
        }
        catch
        {
            _recentCaptures.Clear();
            RecentStripHost.IsVisible = false;
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
        var idle = !_recording && !_captureInProgress;
        var selectedMode = GetThemeColor("HimoSoft", "#242424");
        var idleMode = Colors.Transparent;

        PhotoModeButton.BackgroundColor = photo ? selectedMode : idleMode;
        VideoModeButton.BackgroundColor = !photo && !_videoNoteMode ? selectedMode : idleMode;
        VideoNoteModeButton.BackgroundColor = !photo && _videoNoteMode ? selectedMode : idleMode;
        PhotoModeButton.IsEnabled = idle;
        VideoModeButton.IsEnabled = idle;
        VideoNoteModeButton.IsEnabled = idle;
        PhotoModeButton.Opacity = idle ? 1 : 0.55;
        VideoModeButton.Opacity = idle ? 1 : 0.55;
        VideoNoteModeButton.Opacity = idle ? 1 : 0.55;

        TimerButton.IsEnabled = photo && idle;
        TimerButton.Opacity = TimerButton.IsEnabled ? 1 : 0.45;
        SwitchCameraButton.IsVisible = !_recording;
        SwitchCameraButton.IsEnabled = idle;
        SwitchCameraButton.Opacity = idle ? 1 : 0.42;
        PauseResumeButton.IsVisible = _recording && !photo;
        PauseResumeButton.IsEnabled = _recording && !_captureInProgress;
        PauseResumeButton.Opacity = PauseResumeButton.IsEnabled ? 1 : 0.45;
        PauseResumeButton.ImageSource = ImageSource.FromFile(
            _recordingPaused ? "himo_icon_chat_play_white.png" : "himo_cam_pause_white.png");
        // ImageSource is changed dynamically as recording pauses/resumes, so reapply
        // icon-only theming after assigning it. Camera preview pixels/background are untouched.
        Himo.Services.HimoAppearanceManager.ApplyThemedIconSource(PauseResumeButton);
        PauseResumeButton.BackgroundColor = _recordingPaused ? ThemeAccent : GetThemeColor("HimoCard", "#292929");
        GridButton.IsEnabled = idle;
        GridButton.Opacity = idle ? 1 : 0.45;
        FocusButton.IsEnabled = idle;
        FocusButton.Opacity = idle ? 1 : 0.45;

        CameraHintLabel.Text = photo
            ? "صورة"
            : _videoNoteMode ? "ملاحظة فيديو" : "فيديو";

        CaptureButton.Text = _recording ? "■" : string.Empty;
        CaptureButton.ImageSource = null;
        CaptureButton.TextColor = Colors.White;
        CaptureButton.BackgroundColor = _recording
            ? RecordingColor
            : Color.FromArgb("#FFFFFF");
        CaptureButton.IsEnabled = !_captureInProgress && !_closing;

        StatusBorder.IsVisible = _recording;
        FilterToggleButton.IsEnabled = photo && idle;
        FilterToggleButton.Text = string.Empty;
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
        var normal = GetThemeColor("HimoCard", "#101827");
        var selectedText = GetThemeColor("HimoOnAccent", "#FFFFFF");
        var normalText = GetThemeColor("HimoPrimary", "#FFFFFF");

        ApplyFilterButtonStyle(FilterNaturalButton, _selectedFilter == HimoPhotoFilter.Natural, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterVividButton, _selectedFilter == HimoPhotoFilter.Vivid, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterWarmButton, _selectedFilter == HimoPhotoFilter.Warm, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterCoolButton, _selectedFilter == HimoPhotoFilter.Cool, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterMonoButton, _selectedFilter == HimoPhotoFilter.Mono, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterSepiaButton, _selectedFilter == HimoPhotoFilter.Sepia, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterBeautyNaturalButton, _selectedFilter == HimoPhotoFilter.BeautyNatural, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterBeautySoftButton, _selectedFilter == HimoPhotoFilter.BeautySoft, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterBeautyGlowButton, _selectedFilter == HimoPhotoFilter.BeautyGlow, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterBeautyMakeupSoftButton, _selectedFilter == HimoPhotoFilter.BeautyMakeupSoft, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterBeautyMakeupButton, _selectedFilter == HimoPhotoFilter.BeautyMakeup, selected, normal, selectedText, normalText);
        ApplyFilterButtonStyle(FilterClearSkinButton, _selectedFilter == HimoPhotoFilter.ClearSkin, selected, normal, selectedText, normalText);

        UpdateLiveFilterOverlay();
    }

    private static void ApplyFilterButtonStyle(
        Button button, bool selected, Color accent, Color idle, Color selectedText, Color normalText)
    {
        button.BackgroundColor = selected ? accent : idle;
        button.TextColor = selected ? selectedText : normalText;
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
        FilterPreviewOverlay.IsVisible = overlay.Item2 > 0 && !_recording && _mode == CameraCaptureMode.Photo;

        if (BeautyPreviewOverlay is not null)
        {
            BeautyPreviewOverlay.IsVisible = IsBeautyFilter(_selectedFilter) && !_recording && _mode == CameraCaptureMode.Photo;
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
        // The live preview should fill the camera viewport like the native camera UI.
        // Use the actual XAML viewport, not the still-photo sensor ratio; the latter
        // often forces a 4:3 rectangle with black space above and below.
        var viewWidth = Camera.Width;
        var viewHeight = Camera.Height;
        if (viewWidth > 1 && viewHeight > 1)
        {
            var viewportRatio = (float)(viewWidth / viewHeight);
            if (viewportRatio > 0.1f && viewportRatio < 10f)
                return viewportRatio;
        }

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var displayRatio = display.Height > 0
            ? (float)(display.Width / display.Height)
            : 9f / 16f;
        return displayRatio > 0.1f && displayRatio < 10f ? displayRatio : 9f / 16f;
    }

    private void UpdateCameraControls()
    {
        FlashButton.IsEnabled = Camera.HasFlash && !_recording && !_captureInProgress;
        FlashButton.Opacity = FlashButton.IsEnabled ? 1 : 0.45;
        _flashOn = Camera.IsFlashOn;
        FlashButton.Text = string.Empty;
        FlashButton.BackgroundColor = _flashOn
            ? ThemeAccent
            : CameraIdleColor;
        SwitchCameraButton.IsVisible = !_recording;
        SwitchCameraButton.IsEnabled = !_recording && !_captureInProgress;
        SwitchCameraButton.Opacity = SwitchCameraButton.IsEnabled ? 1 : 0.42;
    }

    private void UpdateZoomUi()
    {
        var max = Math.Max(1f, Camera.MaxZoomFactor);
        var zoomAllowed = !_recording && !_captureInProgress;
        Zoom1Button.IsEnabled = zoomAllowed && max >= 1f;
        Zoom2Button.IsEnabled = zoomAllowed && max >= 2f;
        Zoom4Button.IsEnabled = zoomAllowed && max >= 4f;
        Zoom1Button.Opacity = Zoom1Button.IsEnabled ? 1 : 0.45;
        Zoom2Button.Opacity = Zoom2Button.IsEnabled ? 1 : 0.45;
        Zoom4Button.Opacity = Zoom4Button.IsEnabled ? 1 : 0.45;

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


public sealed record RecentCameraCapture(
    string FilePath,
    string FileName,
    string ContentType,
    bool IsVideo)
{
    public ImageSource ThumbnailSource => ImageSource.FromFile(IsVideo ? "himo_cam_video_white.png" : FilePath);
}
