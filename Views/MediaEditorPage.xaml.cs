using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;
using Himo.Platforms.Android;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Himo.Views;

public sealed record MediaEditorCaptureResult(CameraCaptureResult Capture, string Caption);

public partial class MediaEditorPage : ContentPage
{
    private readonly TaskCompletionSource<CameraCaptureResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<MediaEditorCaptureResult?> _cameraCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly string _sourcePath;
    private string _workingImagePath;
    private readonly string _sourceFileName;
    private readonly string _sourceContentType;
    private readonly bool _isVideo;
    private readonly bool _cameraFlow;
    private HimoPhotoFilter _selectedFilter;
    private float _cropAspectRatio;
    private int _rotationDegrees;
    private float _beautyAmount;
    private float _whitening = 0.25f;
    private float _brightness = 0f;
    private long _videoDurationMs;
    private bool _closing;
    private bool _manualCropMode;
    private PhotoEditProcessor.CropSelection? _manualCrop;
    private (double Width, double Height) _sourceImageSize;

    private readonly List<PhotoEditProcessor.DrawStroke> _drawStrokes = new();
    private readonly List<EditorTextItem> _textItems = new();
    private List<(float X, float Y)>? _activeStroke;
    private (float X, float Y)? _annotationStartPoint;
    private uint _annotationColor = 0xFFFFFFFF;
    private float _annotationStrokeWidth = 0.006f;
    private bool _penMode;
    private bool _textMode;
    private readonly AnnotationDrawable _annotationDrawable;
    private readonly EmptyDrawable _textTouchDrawable = new();

    private CropTouchMode _cropTouchMode;
    private (double Left, double Top, double Right, double Bottom) _cropTouchStartBounds;
    private Rect _cropTouchStartImageRect;
    private Point _cropTouchStartPoint;
    private double _cropLeft;
    private double _cropTop;
    private double _cropRight;
    private double _cropBottom;

    private EditorTextItem? _activeTextItem;
    private Point _textTouchStart;
    private double _textStartTranslationX;
    private double _textStartTranslationY;

    private bool _drawFramePending;

    private enum CropTouchMode
    {
        None,
        Move,
        Top,
        Right,
        Bottom,
        Left,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }


    private MediaEditorPage(
        string sourcePath,
        string sourceFileName,
        string sourceContentType,
        HimoPhotoFilter initialFilter,
        bool cameraFlow = false)
    {
        InitializeComponent();
        CropVisual.Drawable = new CropOverlayDrawable(this);
        CropVisual.InputTransparent = false;
        CropVisual.IsEnabled = true;

        _sourcePath = sourcePath;
        _workingImagePath = sourcePath;
        _sourceFileName = string.IsNullOrWhiteSpace(sourceFileName)
            ? Path.GetFileName(sourcePath)
            : sourceFileName;
        _cameraFlow = cameraFlow;
        _sourceContentType = string.IsNullOrWhiteSpace(sourceContentType)
            ? GuessContentType(_sourceFileName)
            : sourceContentType;
        _isVideo = _sourceContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || IsVideoExtension(Path.GetExtension(_sourceFileName));
        _selectedFilter = _isVideo ? HimoPhotoFilter.Natural : initialFilter;
        _beautyAmount = _selectedFilter switch
        {
            HimoPhotoFilter.BeautyMakeup => 1f,
            HimoPhotoFilter.BeautySoft or HimoPhotoFilter.BeautyMakeupSoft => 0.65f,
            HimoPhotoFilter.BeautyNatural or HimoPhotoFilter.BeautyGlow or HimoPhotoFilter.ClearSkin => 0.35f,
            _ => 0f
        };
        _whitening = _beautyAmount > 0f ? 0.25f : 0f;
        _annotationDrawable = new AnnotationDrawable(this);
        DrawingView.Drawable = _annotationDrawable;
        TextTouchView.Drawable = _textTouchDrawable;

        ApplyInitialUi();
        UpdateBrushSizeUi();
    }

    internal static Task<CameraCaptureResult?> EditAsync(
        CameraCaptureResult capture,
        HimoPhotoFilter initialFilter = HimoPhotoFilter.Natural)
        => EditAsync(capture.FilePath, capture.FileName, capture.ContentType, initialFilter);

    internal static async Task<MediaEditorCaptureResult?> EditCameraCaptureAsync(CameraCaptureResult capture)
    {
        if (capture is null || string.IsNullOrWhiteSpace(capture.FilePath) || !File.Exists(capture.FilePath))
            return null;

        var page = new MediaEditorPage(
            capture.FilePath,
            capture.FileName,
            capture.ContentType,
            HimoPhotoFilter.Natural,
            cameraFlow: true);

        var navigation = Shell.Current?.Navigation
            ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

        if (navigation is null)
            return null;

        await navigation.PushModalAsync(page, animated: true);
        return await page._cameraCompletion.Task;
    }

    internal static async Task<CameraCaptureResult?> EditAsync(
        string filePath,
        string fileName,
        string contentType,
        HimoPhotoFilter initialFilter = HimoPhotoFilter.Natural)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var page = new MediaEditorPage(filePath, fileName, contentType, initialFilter);
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

        try
        {
            if (_isVideo)
            {
                PhotoControls.IsVisible = false;
                VideoEditorPanel.IsVisible = true;
                VideoPreviewBadge.IsVisible = true;
                _videoDurationMs = await VideoEditProcessor.GetDurationMsAsync(_sourcePath);
                _videoDurationMs = Math.Max(1000, _videoDurationMs);
                StartSlider.Maximum = _videoDurationMs;
                EndSlider.Maximum = _videoDurationMs;
                StartSlider.Value = 0;
                EndSlider.Value = _videoDurationMs;
                UpdateVideoLabels();
                SubtitleLabel.Text = "قص الفيديو وكتم الصوت";
            }
            else
            {
                PhotoControls.IsVisible = true;
                VideoEditorPanel.IsVisible = false;
                VideoPreviewBadge.IsVisible = false;
                _sourceImageSize = PhotoEditProcessor.GetImageSize(_workingImagePath);
                PreviewImage.Source = ImageSource.FromFile(_workingImagePath);
                PreviewImage.Aspect = Aspect.AspectFit;
                _rotationDegrees = 0;
                PreviewImage.Rotation = 0;
                _cropAspectRatio = 0f;
                SetAnnotationMode(null);
                SubtitleLabel.Text = "القص والتدوير والنص والقلم قبل الإرسال";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المحرر", $"تعذر تجهيز الملف للتحرير: {ex.Message}", "حسنًا");
            await CancelAndCloseAsync();
        }
    }

    protected override void OnDisappearing()
    {
        if (!_closing)
        {
            _completion.TrySetResult(null);
            _cameraCompletion.TrySetResult(null);
        }

        base.OnDisappearing();
    }

    private void ApplyInitialUi()
    {
        StartSlider.ValueChanged += StartChanged;
        EndSlider.ValueChanged += EndChanged;

        // The text drag surface is stable while the text label moves underneath it.
        // This prevents PanGestureRecognizer feedback/jitter when the label itself moves.
        TextTouchView.StartInteraction += TextTouchViewStartInteraction;
        TextTouchView.DragInteraction += TextTouchViewDragInteraction;
        TextTouchView.EndInteraction += TextTouchViewEndInteraction;
        TextTouchView.CancelInteraction += TextTouchViewCancelInteraction;

        // Use one fixed GraphicsView as the crop touch surface.
        // The touch surface never moves while the crop rectangle moves,
        // which removes the jitter caused by moving gesture targets.
        CropVisual.StartInteraction += CropVisualStartInteraction;
        CropVisual.DragInteraction += CropVisualDragInteraction;
        CropVisual.EndInteraction += CropVisualEndInteraction;
        CropVisual.CancelInteraction += CropVisualCancelInteraction;

        // GraphicsView gives us a stable touch surface for freehand drawing.
        DrawingView.StartInteraction += DrawingViewStartInteraction;
        DrawingView.DragInteraction += DrawingViewDragInteraction;
        DrawingView.EndInteraction += DrawingViewEndInteraction;
        DrawingView.CancelInteraction += DrawingViewCancelInteraction;
        DrawingView.SizeChanged += (_, _) => RequestDrawingInvalidate(immediate: true);
    }

    private void BeginCropPan(CropTouchMode mode, Point startPoint)
    {
        if (!_manualCropMode || mode == CropTouchMode.None)
            return;

        _cropTouchMode = mode;
        _cropTouchStartPoint = startPoint;
        _cropTouchStartBounds = (_cropLeft, _cropTop, _cropRight, _cropBottom);
        _cropTouchStartImageRect = GetDisplayedImageRect();
    }

    private void EndCropPan()
    {
        if (!_manualCropMode)
            return;

        _cropTouchMode = CropTouchMode.None;
        CaptureManualCrop();
    }

    private void CropVisualStartInteraction(object? sender, TouchEventArgs e)
    {
        if (!_manualCropMode || e.Touches is null || e.Touches.Length == 0)
            return;

        var touch = e.Touches[0];
        var startPoint = new Point(touch.X, touch.Y);
        var mode = HitTestCropMode(startPoint);
        BeginCropPan(mode, startPoint);
    }

    private void CropVisualDragInteraction(object? sender, TouchEventArgs e)
    {
        if (!_manualCropMode || _cropTouchMode == CropTouchMode.None ||
            e.Touches is null || e.Touches.Length == 0)
            return;

        var touch = e.Touches[0];
        var dx = touch.X - _cropTouchStartPoint.X;
        var dy = touch.Y - _cropTouchStartPoint.Y;
        ApplyCropPanDelta(
            _cropTouchStartBounds,
            _cropTouchStartImageRect,
            _cropTouchMode,
            dx,
            dy);
    }

    private void CropVisualEndInteraction(object? sender, TouchEventArgs e)
    {
        EndCropPan();
    }

    private void CropVisualCancelInteraction(object? sender, EventArgs e)
    {
        EndCropPan();
    }

    private CropTouchMode HitTestCropMode(Point point)
    {
        if (!_manualCropMode)
            return CropTouchMode.None;

        var imageRect = GetDisplayedImageRect();
        if (!imageRect.Contains(point))
            return CropTouchMode.None;

        var left = _cropLeft;
        var top = _cropTop;
        var right = _cropRight;
        var bottom = _cropBottom;
        var width = Math.Max(1d, right - left);
        var height = Math.Max(1d, bottom - top);

        // Fixed hit radii are used only for hit testing; the crop rectangle
        // itself never changes size while selecting the interaction mode.
        const double cornerRadius = 34d;
        const double edgeRadius = 24d;

        static double Distance(double x1, double y1, double x2, double y2)
            => Math.Sqrt(((x1 - x2) * (x1 - x2)) + ((y1 - y2) * (y1 - y2)));

        var dTL = Distance(point.X, point.Y, left, top);
        var dTR = Distance(point.X, point.Y, right, top);
        var dBL = Distance(point.X, point.Y, left, bottom);
        var dBR = Distance(point.X, point.Y, right, bottom);

        if (dTL <= cornerRadius) return CropTouchMode.TopLeft;
        if (dTR <= cornerRadius) return CropTouchMode.TopRight;
        if (dBL <= cornerRadius) return CropTouchMode.BottomLeft;
        if (dBR <= cornerRadius) return CropTouchMode.BottomRight;

        var insideHorizontal = point.X >= left + cornerRadius && point.X <= right - cornerRadius;
        var insideVertical = point.Y >= top + cornerRadius && point.Y <= bottom - cornerRadius;

        if (insideHorizontal && Math.Abs(point.Y - top) <= edgeRadius) return CropTouchMode.Top;
        if (insideHorizontal && Math.Abs(point.Y - bottom) <= edgeRadius) return CropTouchMode.Bottom;
        if (insideVertical && Math.Abs(point.X - left) <= edgeRadius) return CropTouchMode.Left;
        if (insideVertical && Math.Abs(point.X - right) <= edgeRadius) return CropTouchMode.Right;

        if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
            return CropTouchMode.Move;

        return CropTouchMode.None;
    }

    private void ApplyCropPanDelta(
        (double Left, double Top, double Right, double Bottom) start,
        Rect imageRect,
        CropTouchMode mode,
        double dx,
        double dy)
    {
        if (imageRect.Width <= 1 || imageRect.Height <= 1 || mode == CropTouchMode.None)
            return;

        const double minSize = 8d;
        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        switch (mode)
        {
            case CropTouchMode.Move:
            {
                var width = start.Right - start.Left;
                var height = start.Bottom - start.Top;
                left = Math.Clamp(start.Left + dx, imageRect.Left, imageRect.Right - width);
                top = Math.Clamp(start.Top + dy, imageRect.Top, imageRect.Bottom - height);
                right = left + width;
                bottom = top + height;
                break;
            }
            case CropTouchMode.Top:
                top = Math.Clamp(start.Top + dy, imageRect.Top, start.Bottom - minSize);
                break;
            case CropTouchMode.Right:
                right = Math.Clamp(start.Right + dx, start.Left + minSize, imageRect.Right);
                break;
            case CropTouchMode.Bottom:
                bottom = Math.Clamp(start.Bottom + dy, start.Top + minSize, imageRect.Bottom);
                break;
            case CropTouchMode.Left:
                left = Math.Clamp(start.Left + dx, imageRect.Left, start.Right - minSize);
                break;
            case CropTouchMode.TopLeft:
                left = Math.Clamp(start.Left + dx, imageRect.Left, start.Right - minSize);
                top = Math.Clamp(start.Top + dy, imageRect.Top, start.Bottom - minSize);
                break;
            case CropTouchMode.TopRight:
                right = Math.Clamp(start.Right + dx, start.Left + minSize, imageRect.Right);
                top = Math.Clamp(start.Top + dy, imageRect.Top, start.Bottom - minSize);
                break;
            case CropTouchMode.BottomLeft:
                left = Math.Clamp(start.Left + dx, imageRect.Left, start.Right - minSize);
                bottom = Math.Clamp(start.Bottom + dy, start.Top + minSize, imageRect.Bottom);
                break;
            case CropTouchMode.BottomRight:
                right = Math.Clamp(start.Right + dx, start.Left + minSize, imageRect.Right);
                bottom = Math.Clamp(start.Bottom + dy, start.Top + minSize, imageRect.Bottom);
                break;
        }

        SetCropBounds(left, top, right, bottom, imageRect);
    }

    private void SetCropBounds(double left, double top, double right, double bottom, Rect imageRect)
    {
        var minDimension = Math.Min(imageRect.Width, imageRect.Height);
        var minSize = Math.Min(8d, Math.Max(2d, minDimension));
        var width = Math.Clamp(right - left, minSize, imageRect.Width);
        var height = Math.Clamp(bottom - top, minSize, imageRect.Height);
        left = Math.Clamp(left, imageRect.Left, imageRect.Right - width);
        top = Math.Clamp(top, imageRect.Top, imageRect.Bottom - height);
        right = left + width;
        bottom = top + height;

        _cropLeft = left;
        _cropTop = top;
        _cropRight = right;
        _cropBottom = bottom;
        UpdateCropHitTargets();
        CropVisual.Invalidate();
    }

    private void UpdateCropHitTargets()
    {
        // No moving gesture targets are used. CropVisual is one fixed touch surface
        // covering the editor, so the crop frame can move without stealing/restarting touch.
    }

    private void TextTouchViewStartInteraction(object? sender, TouchEventArgs e)
    {
        if (_manualCropMode || _penMode || _textItems.Count == 0 ||
            e.Touches is null || e.Touches.Length == 0)
            return;

        var touch = e.Touches[0];
        _textTouchStart = new Point(touch.X, touch.Y);
        _activeTextItem = FindTextItemAt(_textTouchStart);
        if (_activeTextItem?.Label is not Label label)
            return;

        _textStartTranslationX = label.TranslationX;
        _textStartTranslationY = label.TranslationY;
    }

    private void TextTouchViewDragInteraction(object? sender, TouchEventArgs e)
    {
        if (_activeTextItem?.Label is not Label label ||
            e.Touches is null || e.Touches.Length == 0 || _manualCropMode || _penMode)
            return;

        var touch = e.Touches[0];
        var dx = touch.X - _textTouchStart.X;
        var dy = touch.Y - _textTouchStart.Y;
        var width = Math.Max(1d, AnnotationLayer.Width);
        var height = Math.Max(1d, AnnotationLayer.Height);
        var leftLimit = -label.Bounds.Left;
        var topLimit = -label.Bounds.Top;
        var rightLimit = width - label.Bounds.Left - Math.Max(1d, label.Width);
        var bottomLimit = height - label.Bounds.Top - Math.Max(1d, label.Height);

        label.TranslationX = Math.Clamp(_textStartTranslationX + dx, leftLimit, rightLimit);
        label.TranslationY = Math.Clamp(_textStartTranslationY + dy, topLimit, bottomLimit);
    }

    private void TextTouchViewEndInteraction(object? sender, TouchEventArgs e)
    {
        if (_activeTextItem?.Label is Label label)
            UpdateTextItemFromLabel(_activeTextItem, label);

        _activeTextItem = null;
    }

    private void TextTouchViewCancelInteraction(object? sender, EventArgs e)
    {
        _activeTextItem = null;
    }

    private EditorTextItem? FindTextItemAt(Point point)
    {
        for (var i = _textItems.Count - 1; i >= 0; i--)
        {
            var item = _textItems[i];
            if (item.Label is not Label label || label.Width <= 0 || label.Height <= 0)
                continue;

            var left = label.Bounds.Left + label.TranslationX;
            var top = label.Bounds.Top + label.TranslationY;
            var rect = new Rect(left, top, Math.Max(1d, label.Width), Math.Max(1d, label.Height));
            if (rect.Contains(point))
                return item;
        }

        return null;
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (dx * dx) + (dy * dy);
    }

    private async void TextModeClicked(object? sender, EventArgs e)
    {
        if (_isVideo || _manualCropMode)
            return;

        SetAnnotationMode(_textMode ? null : "text");
        if (!_textMode)
            return;

        var text = await DisplayPromptAsync(
            "إضافة نص",
            "اكتب النص بالعربية أو الإنجليزية",
            "إضافة",
            "إلغاء",
            "مثال: Himo أو مرحباً");

        if (string.IsNullOrWhiteSpace(text))
        {
            SetAnnotationMode(null);
            return;
        }

        AddTextItem(text.Trim());
        SubtitleLabel.Text = "اسحب النص وضعه في المكان الذي تريده";
    }

    private void PenModeClicked(object? sender, EventArgs e)
    {
        if (_isVideo || _manualCropMode)
            return;

        if (_penMode)
        {
            SetAnnotationMode(null);
            SubtitleLabel.Text = "القص والتدوير والنص والقلم قبل الإرسال";
            return;
        }

        SetAnnotationMode("pen");
        SubtitleLabel.Text = "قلم حر — ارسم بإصبعك في أي مكان على الصورة";
    }

    private void UndoDrawingClicked(object? sender, EventArgs e)
    {
        if (_isVideo || !_penMode)
            return;

        if (_activeStroke is not null)
        {
            _activeStroke = null;
            _annotationStartPoint = null;
            RequestDrawingInvalidate(immediate: true);
            SubtitleLabel.Text = "تم التراجع عن الرسم الحالي";
            return;
        }

        if (_drawStrokes.Count == 0)
            return;

        _drawStrokes.RemoveAt(_drawStrokes.Count - 1);
        RequestDrawingInvalidate(immediate: true);
        SubtitleLabel.Text = "تم التراجع عن آخر رسم";
    }

    private void DecreaseBrushSizeClicked(object? sender, EventArgs e)
    {
        if (!_penMode)
            return;

        _annotationStrokeWidth = Math.Clamp(_annotationStrokeWidth - 0.0015f, 0.002f, 0.018f);
        UpdateBrushSizeUi();
    }

    private void IncreaseBrushSizeClicked(object? sender, EventArgs e)
    {
        if (!_penMode)
            return;

        _annotationStrokeWidth = Math.Clamp(_annotationStrokeWidth + 0.0015f, 0.002f, 0.018f);
        UpdateBrushSizeUi();
    }

    private void UpdateBrushSizeUi()
    {
        if (BrushSizeLabel is null)
            return;

        var percent = (int)Math.Round((_annotationStrokeWidth / 0.018f) * 100f);
        BrushSizeLabel.Text = $"{percent}%";
    }

    private void FreehandToolClicked(object? sender, EventArgs e)
    {
        if (_isVideo || _manualCropMode)
            return;

        SetAnnotationMode("pen");
        SubtitleLabel.Text = "قلم حر — ارسم بإصبعك في أي مكان على الصورة";
    }


    private void AnnotationColorClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string value)
            return;

        if (!TryParseArgb(value, out var argb))
            return;

        _annotationColor = argb;
        if (!_penMode && !_isVideo && !_manualCropMode)
            SetAnnotationMode("pen");
        UpdateAnnotationButtons();
        RequestDrawingInvalidate(immediate: true);
    }

    private void SetAnnotationMode(string? mode)
    {
        _penMode = string.Equals(mode, "pen", StringComparison.Ordinal);
        _textMode = string.Equals(mode, "text", StringComparison.Ordinal);

        var penActive = _penMode && !_manualCropMode;
        var textActive = _textMode && !_manualCropMode;

        AnnotationLayer.IsVisible = penActive || textActive || _drawStrokes.Count > 0 || _textItems.Count > 0;
        AnnotationLayer.InputTransparent = false;
        AnnotationLayer.ZIndex = 200;

        // The drawing surface is a real GraphicsView touch surface.
        // It must be enabled explicitly on Android, but must remain below the
        // native toolbar/palette buttons so those controls can still be pressed.
        DrawingView.IsVisible = penActive || _drawStrokes.Count > 0;
        DrawingView.IsEnabled = true;
        DrawingView.InputTransparent = !penActive;
        DrawingView.ZIndex = 220;

        PreviewImage.InputTransparent = penActive;
        TextOverlay.InputTransparent = true;

        // Keep the text-drag surface active after a label is created, even though
        // text-entry mode itself ends. This lets the user freely drag existing
        // text without needing to tap the Aa button again.
        var textDragActive = !_manualCropMode && !_penMode && _textItems.Count > 0;
        var textInteractionActive = textActive || textDragActive;
        TextTouchView.IsVisible = textInteractionActive;
        TextTouchView.InputTransparent = !textInteractionActive;
        TextTouchView.ZIndex = 230;

        AnnotationToolsPanel.IsVisible = penActive;
        AnnotationPalette.IsVisible = penActive;
        BrushSizePanel.IsVisible = penActive;
        AnnotationToolsPanel.ZIndex = 520;
        AnnotationPalette.ZIndex = 520;

        CropOverlay.IsVisible = _manualCropMode;
        CropOverlay.ZIndex = _manualCropMode ? 400 : 50;

        CropButton.BackgroundColor = _manualCropMode ? Color.FromArgb("#6C2BD9") : Color.FromArgb("#2B2B35");
        TextButton.BackgroundColor = _textMode ? Color.FromArgb("#6C2BD9") : Color.FromArgb("#2B2B35");
        PenButton.BackgroundColor = _penMode ? Color.FromArgb("#6C2BD9") : Color.FromArgb("#2B2B35");
        RotateButton.BackgroundColor = Color.FromArgb("#2B2B35");

        UpdateAnnotationButtons();
        RequestDrawingInvalidate(immediate: true);
    }

    private void UpdateAnnotationButtons()
    {
        for (var i = 0; i < AnnotationPalette.Children.Count; i++)
        {
            if (AnnotationPalette.Children[i] is Button button && button.CommandParameter is string value && TryParseArgb(value, out var argb))
            {
                var selected = argb == _annotationColor;
                button.BorderColor = selected ? Colors.White : Colors.Transparent;
                button.BorderWidth = selected ? 3 : 0;
            }
        }
    }

    private void DrawingViewStartInteraction(object? sender, TouchEventArgs e)
    {
        if (!_penMode || !DrawingView.IsEnabled ||
            DrawingView.Width <= 1 || DrawingView.Height <= 1 ||
            e.Touches is null || e.Touches.Length == 0)
            return;

        var touch = e.Touches[0];
        var normalized = NormalizeDrawingPoint(new Point(touch.X, touch.Y));

        _activeStroke = new List<(float X, float Y)>(64) { normalized };
        _annotationStartPoint = normalized;
        RequestDrawingInvalidate(immediate: true);
    }

    private void DrawingViewDragInteraction(object? sender, TouchEventArgs e)
    {
        if (!_penMode || _activeStroke is null ||
            e.Touches is null || e.Touches.Length == 0)
            return;

        var touch = e.Touches[0];
        var current = NormalizeDrawingPoint(new Point(touch.X, touch.Y));
        var last = _activeStroke[^1];
        var dx = current.X - last.X;
        var dy = current.Y - last.Y;

        // Ignore sub-pixel motion to eliminate jitter while preserving deliberate movement.
        if ((dx * dx) + (dy * dy) < 0.0000007f)
            return;

        _activeStroke.Add(current);
        RequestDrawingInvalidate();
    }

    private void DrawingViewEndInteraction(object? sender, TouchEventArgs e)
    {
        FinishActiveStroke(e);
    }

    private void DrawingViewCancelInteraction(object? sender, EventArgs e)
    {
        FinishActiveStroke(null);
    }

    private void FinishActiveStroke(TouchEventArgs? e)
    {
        if (!_penMode || _activeStroke is null)
            return;

        if (e?.Touches is { Length: > 0 })
        {
            var touch = e.Touches[0];
            var current = NormalizeDrawingPoint(new Point(touch.X, touch.Y));
            var last = _activeStroke[^1];
            var dx = current.X - last.X;
            var dy = current.Y - last.Y;
            if ((dx * dx) + (dy * dy) >= 0.0000007f)
                _activeStroke.Add(current);
        }

        // A tap is rendered as a small dot instead of disappearing.
        if (_activeStroke.Count == 1)
        {
            var p = _activeStroke[0];
            const float dot = 0.0018f;
            _activeStroke.Add((Math.Clamp(p.X + dot, 0f, 1f), p.Y));
        }

        if (_activeStroke.Count > 1)
        {
            _drawStrokes.Add(new PhotoEditProcessor.DrawStroke(
                _activeStroke.ToArray(), _annotationColor, _annotationStrokeWidth));
        }

        _activeStroke = null;
        _annotationStartPoint = null;
        RequestDrawingInvalidate(immediate: true);
    }

    private void RequestDrawingInvalidate(bool immediate = false)
    {
        if (immediate)
        {
            _annotationDrawable.Invalidate();
            return;
        }

        if (_drawFramePending)
            return;

        _drawFramePending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _drawFramePending = false;
            if (_penMode || _activeStroke is null)
                _annotationDrawable.Invalidate();
        });
    }

    private (float X, float Y) NormalizeDrawingPoint(Point point)
    {
        var width = Math.Max(1d, DrawingView.Width);
        var height = Math.Max(1d, DrawingView.Height);
        var overlayX = (float)Math.Clamp(point.X / width, 0d, 1d);
        var overlayY = (float)Math.Clamp(point.Y / height, 0d, 1d);
        return MapOverlayPointToImage(overlayX, overlayY);
    }

    private (float X, float Y) MapOverlayPointToImage(float overlayX, float overlayY)
    {
        var rect = GetDisplayedImageRect();
        var px = overlayX * (float)Math.Max(1d, PreviewHost.Width);
        var py = overlayY * (float)Math.Max(1d, PreviewHost.Height);
        return (
            (float)Math.Clamp((px - rect.Left) / Math.Max(1d, rect.Width), 0d, 1d),
            (float)Math.Clamp((py - rect.Top) / Math.Max(1d, rect.Height), 0d, 1d));
    }

    private void AddTextItem(string text)
    {
        var item = new EditorTextItem(text, 0.5f, 0.5f, _annotationColor, 0.07f);
        var label = new Label
        {
            Text = text,
            TextColor = Color.FromArgb($"#{_annotationColor:X8}"),
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(12, 6),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        item.Label = label;
        _textItems.Add(item);
        TextOverlay.Children.Add(label);

        _textMode = false;
        SetAnnotationMode(null);
        TextOverlay.InputTransparent = true;
        TextTouchView.IsVisible = true;
        TextTouchView.InputTransparent = false;
        TextTouchView.ZIndex = 230;
        AnnotationLayer.IsVisible = true;
    }

    private void UpdateTextItemFromLabel(EditorTextItem item, Label label)
    {
        var width = Math.Max(1d, AnnotationLayer.Width);
        var height = Math.Max(1d, AnnotationLayer.Height);
        item.X = Math.Clamp((float)(0.5d + (label.TranslationX / width)), 0.02f, 0.98f);
        item.Y = Math.Clamp((float)(0.5d + (label.TranslationY / height)), 0.02f, 0.98f);
    }

    private static bool TryParseArgb(string value, out uint argb)
    {
        argb = 0xFFFFFFFF;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb);
    }

    private async void CropModeClicked(object? sender, EventArgs e)
    {
        if (_isVideo)
            return;

        if (_manualCropMode)
        {
            // Commit the selection to a real bitmap immediately.
            // The editor preview is replaced with the cropped image so the user
            // can see that the crop has actually been applied before sending.
            CaptureManualCrop();
            if (!_manualCrop.HasValue)
            {
                await DisplayAlertAsync("القص", "حدد منطقة صالحة داخل الصورة أولًا.", "حسنًا");
                return;
            }

            try
            {
                BusyOverlay.IsVisible = true;
                BusyLabel.Text = "جارٍ تطبيق القص…";
                var applied = await ApplyManualCropToWorkingImageAsync();
                if (!applied)
                    return;

                _manualCropMode = false;
                _manualCrop = null;
                _cropTouchMode = CropTouchMode.None;
                CropOverlay.IsVisible = false;
                AnnotationLayer.InputTransparent = false;
                CropButton.Text = "⌗";
                PreviewImage.Aspect = Aspect.AspectFit;
                SubtitleLabel.Text = "تم تطبيق القص. يمكنك الآن التعديل ثم الإرسال.";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("القص", $"تعذر تطبيق القص فعليًا: {ex.Message}", "حسنًا");
            }
            finally
            {
                BusyOverlay.IsVisible = false;
            }
            return;
        }

        SetAnnotationMode(null);
        _manualCropMode = true;
        _manualCrop = null;
        _cropTouchMode = CropTouchMode.None;
        AnnotationLayer.InputTransparent = true;
        PreviewImage.Aspect = Aspect.AspectFit;
        CropOverlay.IsVisible = true;
        CropButton.Text = "✓";
        SubtitleLabel.Text = "اسحب الإطار أو زواياه ثم اضغط ✓ لتطبيق القص.";

        await InitializeCropFrameAsync();
    }

    private async Task<bool> ApplyManualCropToWorkingImageAsync()
    {
        if (!_manualCrop.HasValue)
            return false;

        var selection = _manualCrop.Value;
        var width = Math.Clamp(selection.Right - selection.Left, 0.001f, 1f);
        var height = Math.Clamp(selection.Bottom - selection.Top, 0.001f, 1f);
        if (width >= 0.999f && height >= 0.999f)
            throw new InvalidOperationException("يجب تحديد مساحة أصغر من الصورة لتطبيق القص.");

        var outputPath = Path.Combine(
            FileSystem.CacheDirectory,
            $"Himo_crop_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(Path.GetFileNameWithoutExtension(_sourceFileName))}.jpg");

        // IMPORTANT: use the dedicated crop-only pipeline. It performs a real bitmap
        // crop and does not rely on the general-effects pipeline to interpret the
        // selection. This prevents the previous fallback to the full source image.
        await PhotoEditProcessor.ApplyManualCropAsync(
            _workingImagePath,
            outputPath,
            _rotationDegrees,
            selection,
            cancellationToken: default);

        if (!File.Exists(outputPath))
            throw new IOException("لم يتم إنشاء الصورة المقصوصة.");

        var info = new FileInfo(outputPath);
        if (info.Length <= 0)
            throw new IOException("تم إنشاء ملف قص فارغ.");

        var actual = PhotoEditProcessor.GetImageSize(outputPath);
        var sourceSize = PhotoEditProcessor.GetImageSize(_workingImagePath);
        var rotatedWidth = ((_rotationDegrees % 180) != 0) ? sourceSize.Height : sourceSize.Width;
        var rotatedHeight = ((_rotationDegrees % 180) != 0) ? sourceSize.Width : sourceSize.Height;

        // Mirror the crop processor's pixel math exactly: floor for the origin and
        // ceil for the far edge. This avoids rejecting a valid crop because of a
        // one-pixel rounding difference.
        var cropX = Math.Clamp((int)Math.Floor(selection.Left * rotatedWidth), 0, Math.Max(0, (int)rotatedWidth - 1));
        var cropY = Math.Clamp((int)Math.Floor(selection.Top * rotatedHeight), 0, Math.Max(0, (int)rotatedHeight - 1));
        var cropRight = Math.Clamp((int)Math.Ceiling(selection.Right * rotatedWidth), cropX + 1, (int)rotatedWidth);
        var cropBottom = Math.Clamp((int)Math.Ceiling(selection.Bottom * rotatedHeight), cropY + 1, (int)rotatedHeight);
        var rawExpectedWidth = Math.Max(1, cropRight - cropX);
        var rawExpectedHeight = Math.Max(1, cropBottom - cropY);
        var expectedWidth = rawExpectedWidth;
        var expectedHeight = rawExpectedHeight;

        if (actual.Width != expectedWidth || actual.Height != expectedHeight)
        {
            throw new IOException(
                $"فشل تطبيق القص فعليًا: الناتج {actual.Width:0}×{actual.Height:0} بدلًا من {expectedWidth}×{expectedHeight}.");
        }

        _workingImagePath = outputPath;
        _sourceImageSize = actual;
        _rotationDegrees = 0;
        PreviewImage.Rotation = 0;
        PreviewImage.Source = ImageSource.FromFile(outputPath);
        PreviewImage.Aspect = Aspect.AspectFit;

        _drawStrokes.Clear();
        _textItems.Clear();
        _activeStroke = null;
        _activeTextItem = null;
        TextOverlay.Children.Clear();
        AnnotationLayer.IsVisible = false;

        return true;
    }

    private async Task InitializeCropFrameAsync()
    {
        // Wait briefly for MAUI to finish laying out the preview before calculating
        // the crop frame. This avoids the first-frame jump that used to cause
        // visible jitter when entering crop mode or rotating the image.
        for (var i = 0; i < 30; i++)
        {
            if (PreviewHost.Width > 1 && PreviewHost.Height > 1)
                break;

            await Task.Delay(16);
        }

        if (!_manualCropMode || PreviewHost.Width <= 1 || PreviewHost.Height <= 1)
            return;

        var imageRect = GetDisplayedImageRect();
        if (imageRect.Width <= 1 || imageRect.Height <= 1)
            return;

        // Start with a comfortable, freely movable selection in the middle.
        // No fixed aspect ratio is imposed.
        var frameWidth = Math.Clamp(imageRect.Width * 0.78, 120d, imageRect.Width);
        var frameHeight = Math.Clamp(imageRect.Height * 0.72, 120d, imageRect.Height);

        var left = imageRect.Left + ((imageRect.Width - frameWidth) / 2d);
        var top = imageRect.Top + ((imageRect.Height - frameHeight) / 2d);
        SetCropBounds(left, top, left + frameWidth, top + frameHeight, imageRect);
        CaptureManualCrop();
    }

    private (double Left, double Top, double Right, double Bottom) GetCropFrameBounds()
        => (_cropLeft, _cropTop, _cropRight, _cropBottom);

    private void RotateClicked(object? sender, EventArgs e)
    {
        if (_isVideo)
            return;

        SetAnnotationMode(null);
        _rotationDegrees = (_rotationDegrees + 90) % 360;
        PreviewImage.Rotation = _rotationDegrees;
        _manualCrop = null;

        if (_manualCropMode)
            MainThread.BeginInvokeOnMainThread(() => _ = InitializeCropFrameAsync());

        SubtitleLabel.Text = $"الدوران: {_rotationDegrees}°  •  اضغط قص للتحديد اليدوي.";
    }

    private void CaptureManualCrop()
    {
        if (!_manualCropMode || _sourceImageSize.Width <= 1 || _sourceImageSize.Height <= 1)
            return;

        var imageRect = GetDisplayedImageRect();
        var frame = GetCropFrameBounds();
        if (imageRect.Width <= 1 || imageRect.Height <= 1)
            return;

        var left = Math.Clamp((frame.Left - imageRect.Left) / imageRect.Width, 0d, 1d);
        var top = Math.Clamp((frame.Top - imageRect.Top) / imageRect.Height, 0d, 1d);
        var right = Math.Clamp((frame.Right - imageRect.Left) / imageRect.Width, 0d, 1d);
        var bottom = Math.Clamp((frame.Bottom - imageRect.Top) / imageRect.Height, 0d, 1d);

        // Keep the selection genuinely free-form. A very small but valid area is allowed.
        if (right - left < 0.001f || bottom - top < 0.001f)
            return;

        _manualCrop = new PhotoEditProcessor.CropSelection(
            (float)left, (float)top, (float)right, (float)bottom);
    }

    private Rect GetDisplayedImageRect()
    {
        var viewWidth = Math.Max(1d, PreviewHost.Width);
        var viewHeight = Math.Max(1d, PreviewHost.Height);
        var sourceWidth = Math.Max(1d, _sourceImageSize.Width);
        var sourceHeight = Math.Max(1d, _sourceImageSize.Height);
        if ((_rotationDegrees % 180) != 0)
            (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

        var sourceRatio = sourceWidth / sourceHeight;
        var viewRatio = viewWidth / viewHeight;

        if (sourceRatio > viewRatio)
        {
            var displayedHeight = viewWidth / sourceRatio;
            return new Rect(0, (viewHeight - displayedHeight) / 2d, viewWidth, displayedHeight);
        }

        var displayedWidth = viewHeight * sourceRatio;
        return new Rect((viewWidth - displayedWidth) / 2d, 0, displayedWidth, viewHeight);
    }

    private void ApplyAutomaticCropRatio()
    {
        var width = Math.Max(1d, PreviewHost.Width);
        var height = Math.Max(1d, PreviewHost.Height);
        if (width <= 1 || height <= 1)
        {
            var display = DeviceDisplay.MainDisplayInfo;
            width = Math.Max(1d, display.Width);
            height = Math.Max(1d, display.Height);
        }

        _cropAspectRatio = (float)(width / height);
        if (_cropAspectRatio < 0.55f)
            _cropAspectRatio = 0.5625f;
        else if (_cropAspectRatio > 1.80f)
            _cropAspectRatio = 1.7777778f;
    }

    private void StartChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isVideo)
            return;

        var value = Math.Clamp(e.NewValue, 0d, Math.Max(0, _videoDurationMs - 500));
        if (value >= EndSlider.Value - 250)
            value = Math.Max(0, EndSlider.Value - 250);
        if (Math.Abs(StartSlider.Value - value) > 0.1)
            StartSlider.Value = value;
        UpdateVideoLabels();
    }

    private void EndChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isVideo)
            return;

        var minimum = Math.Min(_videoDurationMs, StartSlider.Value + 250);
        var value = Math.Clamp(e.NewValue, minimum, _videoDurationMs);
        if (Math.Abs(EndSlider.Value - value) > 0.1)
            EndSlider.Value = value;
        UpdateVideoLabels();
    }

    private void UpdateVideoLabels()
    {
        StartValueLabel.Text = FormatDuration((long)StartSlider.Value);
        EndValueLabel.Text = FormatDuration((long)EndSlider.Value);
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_closing)
            return;

        if (_manualCropMode)
        {
            CaptureManualCrop();
            _manualCropMode = false;
            CropOverlay.IsVisible = false;
            AnnotationLayer.InputTransparent = false;
            CropButton.Text = "⌗";
            PreviewImage.Aspect = Aspect.AspectFit;
        }

        BusyOverlay.IsVisible = true;
        BusyLabel.Text = _isVideo ? "جارٍ تجهيز الفيديو…" : "جارٍ تجهيز الصورة…";

        CameraCaptureResult? result = null;
        try
        {
            if (_isVideo)
            {
                result = await SaveVideoAsync();
                if (result is null)
                    throw new IOException("تعذر إنشاء الفيديو المعدل.");
            }
            else
            {
                result = await SavePhotoAsync();
                if (result is null)
                    throw new IOException("تعذر إنشاء الصورة المعدلة.");
            }

            // Never let the caller continue while this modal is still on top.
            // Popping first prevents the caller from racing the navigation stack.
            _closing = true;
            var completedResult = result ?? throw new InvalidOperationException("لم يتم إنشاء النتيجة النهائية.");
            var navigation = Navigation;
            if (navigation.ModalStack.Count > 0 && ReferenceEquals(navigation.ModalStack[^1], this))
                await navigation.PopModalAsync(animated: true);

            if (_cameraFlow && !_isVideo)
            {
                var caption = CaptionEntry?.Text?.Trim() ?? string.Empty;
                _cameraCompletion.TrySetResult(new MediaEditorCaptureResult(completedResult, caption));
            }
            else
            {
                _completion.TrySetResult(completedResult);
            }
        }
        catch (Exception ex)
        {
            _closing = false;
            await DisplayAlertAsync("التعديل", $"تعذر حفظ التعديلات: {ex.Message}", "حسنًا");
        }
        finally
        {
            if (!_closing)
                BusyOverlay.IsVisible = false;
        }
    }

    private async void SaveToGalleryClicked(object? sender, EventArgs e)
    {
        if (_isVideo)
            return;

        try
        {
            BusyOverlay.IsVisible = true;
            BusyLabel.Text = "جارٍ حفظ الصورة في مكتبة الصور…";
            var photo = await SavePhotoAsync();
            if (photo is null)
                return;

#if ANDROID
            var values = new global::Android.Content.ContentValues();
            values.Put("display_name", photo.FileName);
            values.Put("mime_type", "image/jpeg");
            values.Put("relative_path", "Pictures/Himo");
            values.Put("is_pending", 1);

            var resolver = global::Android.App.Application.Context.ContentResolver
                ?? throw new InvalidOperationException("مخزن الوسائط غير متاح.");
            var uri = resolver.Insert(
                global::Android.Provider.MediaStore.Images.Media.ExternalContentUri!,
                values)
                ?? throw new IOException("تعذر إنشاء ملف الصورة في مكتبة الصور.");

            try
            {
                await using var input = File.OpenRead(photo.FilePath);
                using var output = resolver.OpenOutputStream(uri)
                    ?? throw new IOException("تعذر فتح ملف الصورة للكتابة.");
                await input.CopyToAsync(output);

                var published = new global::Android.Content.ContentValues();
                published.Put("is_pending", 0);
                resolver.Update(uri, published, null, null);
            }
            catch
            {
                resolver.Delete(uri, null, null);
                throw;
            }
#else
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            Directory.CreateDirectory(pictures);
            File.Copy(photo.FilePath, Path.Combine(pictures, photo.FileName), overwrite: true);
#endif

            await DisplayAlertAsync("تم الحفظ", "تم حفظ الصورة في مكتبة الصور.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("حفظ الصورة", $"تعذر حفظ الصورة: {ex.Message}", "حسنًا");
        }
        finally
        {
            BusyOverlay.IsVisible = false;
        }
    }

    private async Task<CameraCaptureResult?> SavePhotoAsync()
    {
        var outputDirectory = FileSystem.CacheDirectory;
        var name = Path.GetFileNameWithoutExtension(_sourceFileName);
        var outputPath = Path.Combine(
            outputDirectory,
            $"Himo_edited_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(name)}.jpg");

        var textAnnotations = _textItems.Select(t =>
        {
            var imagePoint = MapOverlayPointToImage(t.X, t.Y);
            return new PhotoEditProcessor.TextAnnotation(
                t.Text, imagePoint.X, imagePoint.Y, t.ColorArgb, t.Size);
        }).ToList();

        await PhotoEditProcessor.ApplyAsync(
            _workingImagePath,
            outputPath,
            _selectedFilter,
            _cropAspectRatio,
            _rotationDegrees,
            _beautyAmount,
            _whitening,
            _brightness,
            _manualCrop,
            _drawStrokes,
            textAnnotations,
            cancellationToken: default);

        if (!File.Exists(outputPath))
            throw new IOException("لم يتم إنشاء ملف الصورة المعدلة.");

        var info = new FileInfo(outputPath);
        if (info.Length <= 0)
            throw new IOException("ملف الصورة المعدلة فارغ.");

        await VerifyCropOutputAsync(outputPath);
        return new CameraCaptureResult(outputPath, Path.GetFileName(outputPath), "image/jpeg");
    }

    private async Task VerifyCropOutputAsync(string outputPath)
    {
        if (!_manualCrop.HasValue)
            return;

        var selection = _manualCrop.Value;
        var widthNorm = Math.Max(0.001f, selection.Right - selection.Left);
        var heightNorm = Math.Max(0.001f, selection.Bottom - selection.Top);

        var sourceWidth = _sourceImageSize.Width;
        var sourceHeight = _sourceImageSize.Height;
        if ((_rotationDegrees % 180) != 0)
            (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

        var selectedWidth = Math.Max(1d, Math.Round(sourceWidth * widthNorm));
        var selectedHeight = Math.Max(1d, Math.Round(sourceHeight * heightNorm));

        const double maxDimension = 2400d;
        var scale = Math.Max(selectedWidth, selectedHeight) > maxDimension
            ? maxDimension / Math.Max(selectedWidth, selectedHeight)
            : 1d;

        var expectedWidth = Math.Max(1d, Math.Round(selectedWidth * scale));
        var expectedHeight = Math.Max(1d, Math.Round(selectedHeight * scale));
        var outputSize = await Task.Run(() => PhotoEditProcessor.GetImageSize(outputPath));

        // The processor crops before its optional 2400px resize. Therefore the
        // output pixel dimensions are deterministic for the selected region.
        // This catches the exact failure we are guarding against: sending the
        // original image while the crop UI only moved on screen.
        var widthError = Math.Abs(outputSize.Width - expectedWidth) / expectedWidth;
        var heightError = Math.Abs(outputSize.Height - expectedHeight) / expectedHeight;
        if (widthError > 0.01d || heightError > 0.01d)
            throw new IOException("لم يتم تطبيق القص فعليًا على الصورة الناتجة.");
    }

    private async Task<CameraCaptureResult?> SaveVideoAsync()
    {
        var start = (long)Math.Round(StartSlider.Value);
        var end = (long)Math.Round(EndSlider.Value);
        var needsEdit = start > 250 || end < _videoDurationMs - 250 || MuteVideoSwitch.IsToggled;

        if (!needsEdit)
        {
            return new CameraCaptureResult(
                _sourcePath,
                _sourceFileName,
                string.IsNullOrWhiteSpace(_sourceContentType) ? "video/mp4" : _sourceContentType);
        }

        var name = Path.GetFileNameWithoutExtension(_sourceFileName);
        var outputPath = Path.Combine(
            FileSystem.CacheDirectory,
            $"Himo_edited_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(name)}.mp4");

        await VideoEditProcessor.TrimAsync(
            _sourcePath,
            outputPath,
            start,
            end,
            MuteVideoSwitch.IsToggled,
            cancellationToken: default);

        return new CameraCaptureResult(outputPath, Path.GetFileName(outputPath), "video/mp4");
    }

    private async void CancelClicked(object? sender, EventArgs e) => await CancelAndCloseAsync();

    private async Task CancelAndCloseAsync()
    {
        if (_closing)
            return;

        _closing = true;
        try
        {
            var navigation = Navigation;
            if (navigation.ModalStack.Count > 0 && ReferenceEquals(navigation.ModalStack[^1], this))
                await navigation.PopModalAsync(animated: true);
        }
        finally
        {
            _completion.TrySetResult(null);
            _cameraCompletion.TrySetResult(null);
        }
    }

    private static string FormatDuration(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string GuessContentType(string fileName)
        => IsVideoExtension(Path.GetExtension(fileName)) ? "video/mp4" : "image/jpeg";

    private static bool IsVideoExtension(string? extension)
        => extension is not null && extension.ToLowerInvariant() is
            ".mp4" or ".m4v" or ".mov" or ".webm" or ".3gp" or ".3g2" or ".mkv" or ".avi" or ".mpeg" or ".mpg" or ".ogv";

    private sealed class EditorTextItem
    {
        public EditorTextItem(string text, float x, float y, uint colorArgb, float size)
        {
            Text = text;
            X = x;
            Y = y;
            ColorArgb = colorArgb;
            Size = size;
        }

        public string Text { get; }
        public float X { get; set; }
        public float Y { get; set; }
        public uint ColorArgb { get; }
        public float Size { get; }
        public Label? Label { get; set; }
    }

    private sealed class CropOverlayDrawable : IDrawable
    {
        private readonly MediaEditorPage _page;
        public CropOverlayDrawable(MediaEditorPage page) => _page = page;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var left = (float)_page._cropLeft;
            var top = (float)_page._cropTop;
            var right = (float)_page._cropRight;
            var bottom = (float)_page._cropBottom;

            if (right <= left || bottom <= top)
            {
                canvas.FillColor = Color.FromArgb("#99000000");
                canvas.FillRectangle(dirtyRect);
                return;
            }

            // Darken only the area outside the crop. The image itself remains completely stable.
            canvas.FillColor = Color.FromArgb("#99000000");
            canvas.FillRectangle(new RectF(dirtyRect.Left, dirtyRect.Top, dirtyRect.Width, Math.Max(0f, top - dirtyRect.Top)));
            canvas.FillRectangle(new RectF(dirtyRect.Left, bottom, dirtyRect.Width, Math.Max(0f, dirtyRect.Bottom - bottom)));
            canvas.FillRectangle(new RectF(dirtyRect.Left, top, Math.Max(0f, left - dirtyRect.Left), Math.Max(0f, bottom - top)));
            canvas.FillRectangle(new RectF(right, top, Math.Max(0f, dirtyRect.Right - right), Math.Max(0f, bottom - top)));

            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2f;
            canvas.DrawRectangle(new RectF(left, top, right - left, bottom - top));

            canvas.StrokeColor = Color.FromArgb("#88FFFFFF");
            canvas.StrokeSize = 1f;
            var w = right - left;
            var h = bottom - top;
            canvas.DrawLine(left + w / 3f, top, left + w / 3f, bottom);
            canvas.DrawLine(left + (w * 2f) / 3f, top, left + (w * 2f) / 3f, bottom);
            canvas.DrawLine(left, top + h / 3f, right, top + h / 3f);
            canvas.DrawLine(left, top + (h * 2f) / 3f, right, top + (h * 2f) / 3f);

            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 4f;
            const float handle = 22f;
            canvas.DrawLine(left, top + handle, left, top);
            canvas.DrawLine(left, top, left + handle, top);
            canvas.DrawLine(right - handle, top, right, top);
            canvas.DrawLine(right, top, right, top + handle);
            canvas.DrawLine(left, bottom - handle, left, bottom);
            canvas.DrawLine(left, bottom, left + handle, bottom);
            canvas.DrawLine(right - handle, bottom, right, bottom);
            canvas.DrawLine(right, bottom - handle, right, bottom);
        }
    }

    private sealed class AnnotationDrawable : IDrawable
    {
        private readonly MediaEditorPage _page;

        public AnnotationDrawable(MediaEditorPage page) => _page = page;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var imageRect = _page.GetDisplayedImageRect();
            if (imageRect.Width <= 1 || imageRect.Height <= 1)
                return;

            var minDimension = (float)Math.Max(1d, Math.Min(imageRect.Width, imageRect.Height));

            foreach (var stroke in _page._drawStrokes)
            {
                DrawStroke(canvas, stroke.Points, stroke.ColorArgb, stroke.Width, imageRect, minDimension);
            }

            if (_page._activeStroke is { Count: > 0 })
            {
                DrawStroke(canvas, _page._activeStroke, _page._annotationColor,
                    _page._annotationStrokeWidth, imageRect, minDimension);
            }
        }

        public void Invalidate() => _page.DrawingView.Invalidate();

        private static void DrawStroke(
            ICanvas canvas,
            IReadOnlyList<(float X, float Y)> points,
            uint argb,
            float width,
            Rect imageRect,
            float minDimension)
        {
            if (points.Count == 0)
                return;

            var path = new PathF();
            var first = points[0];
            var firstX = (float)(imageRect.Left + first.X * imageRect.Width);
            var firstY = (float)(imageRect.Top + first.Y * imageRect.Height);
            path.MoveTo(firstX, firstY);

            if (points.Count == 1)
            {
                path.LineTo(firstX + 0.5f, firstY + 0.5f);
            }
            else
            {
                for (var i = 1; i < points.Count; i++)
                {
                    var current = points[i];
                    var currentX = (float)(imageRect.Left + current.X * imageRect.Width);
                    var currentY = (float)(imageRect.Top + current.Y * imageRect.Height);
                    var next = i + 1 < points.Count ? points[i + 1] : current;
                    var nextX = (float)(imageRect.Left + next.X * imageRect.Width);
                    var nextY = (float)(imageRect.Top + next.Y * imageRect.Height);
                    var endX = (currentX + nextX) * 0.5f;
                    var endY = (currentY + nextY) * 0.5f;

                    if (i == points.Count - 1)
                    {
                        endX = currentX;
                        endY = currentY;
                    }

                    path.QuadTo(currentX, currentY, endX, endY);
                }
            }

            canvas.StrokeColor = Color.FromArgb($"#{argb:X8}");
            var strokeSize = Math.Clamp(width * minDimension, 2.5f, Math.Max(3f, minDimension * 0.018f));
            canvas.StrokeSize = strokeSize;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(path);

            // Draw a tiny filled round cap for tap-only marks so a single touch is visible.
            if (points.Count == 2 &&
                Math.Abs(points[0].X - points[1].X) < 0.002f &&
                Math.Abs(points[0].Y - points[1].Y) < 0.002f)
            {
                canvas.FillColor = Color.FromArgb($"#{argb:X8}");
                var radius = Math.Max(1.5f, strokeSize / 2f);
                canvas.FillCircle(firstX, firstY, radius);
            }
        }
    }

    private sealed class EmptyDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            // Intentionally empty: the GraphicsView is used only as a stable touch surface.
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrWhiteSpace(cleaned) ? "media" : cleaned;
    }
}
