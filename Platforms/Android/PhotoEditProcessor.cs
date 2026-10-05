#pragma warning disable CA1416
using ABitmap = global::Android.Graphics.Bitmap;
using ABitmapFactory = global::Android.Graphics.BitmapFactory;
using ACanvas = global::Android.Graphics.Canvas;
using APaint = global::Android.Graphics.Paint;
using APaintFlags = global::Android.Graphics.PaintFlags;
using APath = global::Android.Graphics.Path;
using ATypeface = global::Android.Graphics.Typeface;
using ATypefaceStyle = global::Android.Graphics.TypefaceStyle;
using AColor = global::Android.Graphics.Color;

using Microsoft.Maui.Storage;

namespace Himo.Platforms.Android;

internal static class PhotoEditProcessor
{
    internal readonly record struct CropSelection(float Left, float Top, float Right, float Bottom);
    internal readonly record struct DrawStroke(
        IReadOnlyList<(float X, float Y)> Points,
        uint ColorArgb = 0xFFFFFFFF,
        float Width = 0.006f);

    internal readonly record struct TextAnnotation(
        string Text,
        float X,
        float Y,
        uint ColorArgb = 0xFFFFFFFF,
        float Size = 0.07f);

    internal static (double Width, double Height) GetImageSize(string inputPath)
    {
        using var options = new ABitmapFactory.Options { InJustDecodeBounds = true };
        _ = ABitmapFactory.DecodeFile(inputPath, options);
        if (options.OutWidth <= 0 || options.OutHeight <= 0)
            throw new IOException("تعذر قراءة أبعاد الصورة.");

        return (options.OutWidth, options.OutHeight);
    }

    /// <summary>
    /// Applies only a manual crop to the source bitmap. This intentionally bypasses
    /// the general photo-effects pipeline so a confirmed crop can never fall back to
    /// the original image. The selection is normalized to the bitmap after rotation.
    /// </summary>
    internal static Task ApplyManualCropAsync(
        string inputPath,
        string outputPath,
        int rotationDegrees,
        CropSelection selection,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => ApplyManualCropInternal(
                inputPath,
                outputPath,
                rotationDegrees,
                selection,
                cancellationToken),
            cancellationToken);

    private static void ApplyManualCropInternal(
        string inputPath,
        string outputPath,
        int rotationDegrees,
        CropSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = ABitmapFactory.DecodeFile(inputPath)
            ?? throw new IOException("تعذر قراءة الصورة الأصلية للقص.");
        using var rotated = Rotate(source, rotationDegrees);

        var left = Math.Clamp(selection.Left, 0f, 1f);
        var top = Math.Clamp(selection.Top, 0f, 1f);
        var right = Math.Clamp(selection.Right, 0f, 1f);
        var bottom = Math.Clamp(selection.Bottom, 0f, 1f);

        if (right <= left || bottom <= top)
            throw new InvalidOperationException("منطقة القص غير صالحة.");

        var cropX = Math.Clamp((int)Math.Floor(left * rotated.Width), 0, Math.Max(0, rotated.Width - 1));
        var cropY = Math.Clamp((int)Math.Floor(top * rotated.Height), 0, Math.Max(0, rotated.Height - 1));
        var cropRight = Math.Clamp((int)Math.Ceiling(right * rotated.Width), cropX + 1, rotated.Width);
        var cropBottom = Math.Clamp((int)Math.Ceiling(bottom * rotated.Height), cropY + 1, rotated.Height);
        var cropWidth = Math.Max(1, cropRight - cropX);
        var cropHeight = Math.Max(1, cropBottom - cropY);

        cancellationToken.ThrowIfCancellationRequested();
        using var cropped = ABitmap.CreateBitmap(rotated, cropX, cropY, cropWidth, cropHeight)
            ?? throw new IOException("تعذر إنشاء الصورة المقصوصة.");

        using var outputBitmap = cropped.Copy(
            ABitmap.Config.Argb8888
                ?? throw new InvalidOperationException("صيغة الصورة غير متاحة."),
            true)
            ?? throw new IOException("تعذر إنشاء نسخة الصورة المقصوصة.");

        SaveJpeg(outputBitmap, outputPath, cancellationToken);

        var actual = GetImageSize(outputPath);
        if (actual.Width != outputBitmap.Width || actual.Height != outputBitmap.Height)
            throw new IOException($"أبعاد الصورة الناتجة غير متطابقة: {actual.Width:0}×{actual.Height:0} بدلًا من {outputBitmap.Width}×{outputBitmap.Height}.");
    }

    public static Task ApplyAsync(
        string inputPath,
        string outputPath,
        HimoPhotoFilter filter,
        float cropAspectRatio,
        int rotationDegrees,
        float beautyAmount,
        float whitening,
        float brightness,
        CropSelection? cropSelection = null,
        IReadOnlyList<DrawStroke>? drawings = null,
        IReadOnlyList<TextAnnotation>? textAnnotations = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => ApplyInternal(
                inputPath,
                outputPath,
                filter,
                cropAspectRatio,
                rotationDegrees,
                beautyAmount,
                whitening,
                brightness,
                cropSelection,
                drawings,
                textAnnotations,
                cancellationToken),
            cancellationToken);

    private static void ApplyInternal(
        string inputPath,
        string outputPath,
        HimoPhotoFilter filter,
        float cropAspectRatio,
        int rotationDegrees,
        float beautyAmount,
        float whitening,
        float brightness,
        CropSelection? cropSelection,
        IReadOnlyList<DrawStroke>? drawings,
        IReadOnlyList<TextAnnotation>? textAnnotations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = ABitmapFactory.DecodeFile(inputPath)
            ?? throw new IOException("تعذر قراءة الصورة.");

        // Rotate first so automatic cropping follows the final visual orientation.
        using var rotated = Rotate(source, rotationDegrees);
        var cropResult = CropWithRegion(rotated, cropAspectRatio, cropSelection);
        using var cropped = cropResult.Bitmap;
        var cropRegion = cropResult.Region;

        using var working = cropped.Copy(
            ABitmap.Config.Argb8888
                ?? throw new InvalidOperationException("صيغة الصورة غير متاحة."),
            true)
            ?? throw new IOException("تعذر إنشاء نسخة الصورة.");

        ApplyPixels(
            working,
            filter,
            beautyAmount,
            whitening,
            brightness,
            cropSelection,
            cancellationToken);

        ApplyDrawings(working, drawings, cropRegion, cancellationToken);
        ApplyTextAnnotations(working, textAnnotations, cropRegion, cancellationToken);

        SaveJpeg(working, outputPath, cancellationToken);
    }

    private static void ApplyDrawings(
        ABitmap bitmap,
        IReadOnlyList<DrawStroke>? drawings,
        CropSelection cropRegion,
        CancellationToken cancellationToken)
    {
        if (drawings is null || drawings.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        using var canvas = new ACanvas(bitmap);

        foreach (var stroke in drawings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stroke.Points.Count == 0)
                continue;

            var color = ToAndroidColor(stroke.ColorArgb);
            using var paint = new APaint(APaintFlags.AntiAlias | APaintFlags.Dither)
            {
                Color = color,
                StrokeWidth = (float)Math.Max(5f, Math.Min(bitmap.Width, bitmap.Height) * Math.Clamp(stroke.Width, 0.002f, 0.02f)),
                StrokeCap = APaint.Cap.Round,
                StrokeJoin = APaint.Join.Round
            };
            paint.SetStyle(APaint.Style.Stroke);

            using var path = new APath();
            var first = MapNormalizedPoint(stroke.Points[0], cropRegion);
            path.MoveTo((float)(first.X * bitmap.Width), (float)(first.Y * bitmap.Height));

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var mapped = MapNormalizedPoint(stroke.Points[i], cropRegion);
                path.LineTo((float)(mapped.X * bitmap.Width), (float)(mapped.Y * bitmap.Height));
            }

            canvas.DrawPath(path, paint);
        }
    }

    private static void ApplyTextAnnotations(
        ABitmap bitmap,
        IReadOnlyList<TextAnnotation>? textAnnotations,
        CropSelection cropRegion,
        CancellationToken cancellationToken)
    {
        if (textAnnotations is null || textAnnotations.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        using var canvas = new ACanvas(bitmap);

        foreach (var annotation in textAnnotations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(annotation.Text))
                continue;

            var mapped = MapNormalizedPoint((annotation.X, annotation.Y), cropRegion);
            var x = (float)(mapped.X * bitmap.Width);
            var y = (float)(mapped.Y * bitmap.Height);
            var textSize = (float)Math.Max(18f, Math.Min(bitmap.Width, bitmap.Height) * Math.Clamp(annotation.Size, 0.03f, 0.14f));
            var color = ToAndroidColor(annotation.ColorArgb);

            using var shadow = new APaint(APaintFlags.AntiAlias | APaintFlags.Dither)
            {
                Color = AColor.Argb(180, 0, 0, 0),
                TextSize = textSize,
                TextAlign = APaint.Align.Center,
                StrokeWidth = (float)Math.Max(2f, textSize * 0.055f),
                StrokeCap = APaint.Cap.Round,
                StrokeJoin = APaint.Join.Round
            };
            shadow.SetStyle(APaint.Style.FillAndStroke);
            shadow.SetTypeface(ATypeface.Create("sans-serif", ATypefaceStyle.Bold));

            using var textPaint = new APaint(APaintFlags.AntiAlias | APaintFlags.Dither)
            {
                Color = color,
                TextSize = textSize,
                TextAlign = APaint.Align.Center
            };
            textPaint.SetTypeface(ATypeface.Create("sans-serif", ATypefaceStyle.Bold));

            var baseline = (float)(y - ((shadow.Ascent() + shadow.Descent()) / 2f));
            canvas.DrawText(annotation.Text, (float)(x + textSize * 0.045f), (float)(baseline + textSize * 0.065f), shadow);
            canvas.DrawText(annotation.Text, (float)x, (float)baseline, textPaint);
        }
    }

    private static (float X, float Y) MapNormalizedPoint((float X, float Y) point, CropSelection region)
    {
        var width = Math.Max(0.001f, region.Right - region.Left);
        var height = Math.Max(0.001f, region.Bottom - region.Top);
        return (
            Math.Clamp((point.X - region.Left) / width, 0f, 1f),
            Math.Clamp((point.Y - region.Top) / height, 0f, 1f));
    }

    private static AColor ToAndroidColor(uint argb)
        => AColor.Argb(
            (int)((argb >> 24) & 0xFF),
            (int)((argb >> 16) & 0xFF),
            (int)((argb >> 8) & 0xFF),
            (int)(argb & 0xFF));

    private static ABitmap Crop(ABitmap source, float aspect, CropSelection? selection)
        => CropWithRegion(source, aspect, selection).Bitmap;

    private static (ABitmap Bitmap, CropSelection Region) CropWithRegion(
        ABitmap source,
        float aspect,
        CropSelection? selection)
    {
        if (selection.HasValue)
        {
            var selected = selection.Value;
            var selLeft = Math.Clamp(selected.Left, 0f, 1f);
            var selTop = Math.Clamp(selected.Top, 0f, 1f);
            var selRight = Math.Clamp(selected.Right, selLeft + 0.001f, 1f);
            var selBottom = Math.Clamp(selected.Bottom, selTop + 0.001f, 1f);

            var cropX = Math.Clamp((int)Math.Round(selLeft * source.Width), 0, Math.Max(0, source.Width - 1));
            var cropY = Math.Clamp((int)Math.Round(selTop * source.Height), 0, Math.Max(0, source.Height - 1));
            var cropWidth = Math.Max(1, Math.Min(source.Width - cropX, (int)Math.Round((selRight - selLeft) * source.Width)));
            var cropHeight = Math.Max(1, Math.Min(source.Height - cropY, (int)Math.Round((selBottom - selTop) * source.Height)));

            var bitmap = ABitmap.CreateBitmap(source, cropX, cropY, cropWidth, cropHeight)
                ?? throw new IOException("تعذر إنشاء القص اليدوي.");
            return (bitmap, new CropSelection(selLeft, selTop, selRight, selBottom));
        }

        if (aspect <= 0.01)
        {
            var bitmap = source.Copy(
                ABitmap.Config.Argb8888
                    ?? throw new InvalidOperationException("صيغة الصورة غير متاحة."),
                true)
                ?? throw new IOException("تعذر قص الصورة.");
            return (bitmap, new CropSelection(0f, 0f, 1f, 1f));
        }

        var sourceAspect = source.Width / (float)Math.Max(1, source.Height);
        int aspectCropWidth;
        int aspectCropHeight;
        int aspectCropLeft;
        int aspectCropTop;

        if (sourceAspect > aspect)
        {
            aspectCropHeight = source.Height;
            aspectCropWidth = Math.Max(1, (int)Math.Round(aspectCropHeight * aspect));
            aspectCropLeft = Math.Max(0, (source.Width - aspectCropWidth) / 2);
            aspectCropTop = 0;
        }
        else
        {
            aspectCropWidth = source.Width;
            aspectCropHeight = Math.Max(1, (int)Math.Round(aspectCropWidth / aspect));
            aspectCropLeft = 0;
            aspectCropTop = Math.Max(0, (source.Height - aspectCropHeight) / 2);
        }

        var result = ABitmap.CreateBitmap(source, aspectCropLeft, aspectCropTop, aspectCropWidth, aspectCropHeight)
            ?? throw new IOException("تعذر إنشاء القص.");
        return (
            result,
            new CropSelection(
                aspectCropLeft / (float)Math.Max(1, source.Width),
                aspectCropTop / (float)Math.Max(1, source.Height),
                (aspectCropLeft + aspectCropWidth) / (float)Math.Max(1, source.Width),
                (aspectCropTop + aspectCropHeight) / (float)Math.Max(1, source.Height)));
    }

    private static ABitmap Rotate(ABitmap source, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized == 0)
        {
            return source.Copy(
                ABitmap.Config.Argb8888
                    ?? throw new InvalidOperationException("صيغة الصورة غير متاحة."),
                true)
                ?? throw new IOException("تعذر تدوير الصورة.");
        }

        using var matrix = new global::Android.Graphics.Matrix();
        matrix.PostRotate(normalized);
        return ABitmap.CreateBitmap(
            source,
            0,
            0,
            source.Width,
            source.Height,
            matrix,
            true)
            ?? throw new IOException("تعذر تدوير الصورة.");
    }

    private static void ApplyPixels(
        ABitmap bitmap,
        HimoPhotoFilter filter,
        float beautyAmount,
        float whitening,
        float brightness,
        CropSelection? cropSelection,
        CancellationToken cancellationToken)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = new int[width * height];
        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

        var guideWidth = Math.Max(1, width / 6);
        var guideHeight = Math.Max(1, height / 6);
        using var guide = ABitmap.CreateScaledBitmap(bitmap, guideWidth, guideHeight, true)
            ?? throw new IOException("تعذر إنشاء طبقة تنعيم البشرة.");
        var guidePixels = new int[guideWidth * guideHeight];
        guide.GetPixels(guidePixels, 0, guideWidth, 0, 0, guideWidth, guideHeight);

        var beauty = Math.Clamp(beautyAmount, 0f, 1f);
        var white = Math.Clamp(whitening, 0f, 1f);
        var brightnessOffset = Math.Clamp(brightness, -0.18f, 0.18f);

        var faceCx = width * 0.50f;
        var faceCy = height * 0.47f;
        var faceRx = width * 0.43f;
        var faceRy = height * 0.47f;

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gy = Math.Min(guideHeight - 1, y * guideHeight / height);

            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var pixel = pixels[index];
                var a = (byte)((pixel >> 24) & 0xFF);
                var r = (byte)((pixel >> 16) & 0xFF);
                var g = (byte)((pixel >> 8) & 0xFF);
                var b = (byte)(pixel & 0xFF);

                var skin = SkinScore(r, g, b);
                var inFace = IsInsideEllipse(x, y, faceCx, faceCy, faceRx, faceRy);
                var skinWeight = inFace ? skin : skin * 0.25f;

                if (beauty > 0.01 && skinWeight > 0.08f)
                {
                    var gx = Math.Min(guideWidth - 1, x * guideWidth / width);
                    var softPixel = guidePixels[(gy * guideWidth) + gx];
                    var sr = (byte)((softPixel >> 16) & 0xFF);
                    var sg = (byte)((softPixel >> 8) & 0xFF);
                    var sb = (byte)(softPixel & 0xFF);
                    var smooth = Math.Clamp(skinWeight * (0.36f + 0.48f * (float)beauty), 0f, 0.78f);

                    r = Blend(r, sr, smooth);
                    g = Blend(g, sg, smooth);
                    b = Blend(b, sb, smooth);

                    // Reduce small reddish/brown local deviations that often appear as blemishes.
                    var delta = Math.Abs(r - sr) + Math.Abs(g - sg) + Math.Abs(b - sb);
                    if (delta > 26)
                    {
                        var clear = Math.Clamp((delta - 26f) / 160f * (0.30f + beauty * 0.55f), 0f, 0.58f);
                        r = Blend(r, sr, clear);
                        g = Blend(g, sg, clear);
                        b = Blend(b, sb, clear);
                    }
                }

                if (skinWeight > 0.10f && white > 0.01)
                {
                    // Strong whitening, but still clipped to preserve highlights and avoid a flat white mask.
                    var lift = 36f * white * skinWeight;
                    r = ClampByte(r + lift);
                    g = ClampByte(g + lift * 0.96f);
                    b = ClampByte(b + lift * 0.90f);
                }

                var tone = FilterTone(filter);
                if (skinWeight > 0.08f && tone.Saturation != 1f)
                {
                    ApplySaturation(ref r, ref g, ref b, tone.Saturation);
                }

                if (skinWeight > 0.08f)
                {
                    var toneBrightness = tone.Brightness + brightnessOffset;
                    r = ClampByte(r + toneBrightness * 255f);
                    g = ClampByte(g + toneBrightness * 255f * 0.96f);
                    b = ClampByte(b + toneBrightness * 255f * 0.92f);

                    if (tone.Warmth > 0f)
                    {
                        r = ClampByte(r + 12f * tone.Warmth);
                        b = ClampByte(b - 5f * tone.Warmth);
                    }
                    else if (tone.Warmth < 0f)
                    {
                        r = ClampByte(r + 5f * tone.Warmth);
                        b = ClampByte(b - 10f * tone.Warmth);
                    }
                }

                if (filter is HimoPhotoFilter.BeautyMakeupSoft or HimoPhotoFilter.BeautyMakeup)
                {
                    var cheek = BlushWeight(x, y, width, height);
                    var lip = LipWeight(x, y, width, height);
                    var makeup = filter == HimoPhotoFilter.BeautyMakeup ? 1f : 0.58f;
                    var skinFactor = skinWeight * makeup;

                    if (cheek > 0.02f && skinFactor > 0.05f)
                    {
                        var amount = cheek * skinFactor;
                        r = ClampByte(r + 22f * amount);
                        g = ClampByte(g - 5f * amount);
                        b = ClampByte(b + 4f * amount);
                    }

                    if (lip > 0.02f)
                    {
                        var amount = lip * makeup;
                        r = ClampByte(r + 26f * amount);
                        g = ClampByte(g - 9f * amount);
                        b = ClampByte(b + 9f * amount);
                    }
                }

                pixels[index] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }

        bitmap.SetPixels(pixels, 0, width, 0, 0, width, height);
    }

    private static (float Brightness, float Saturation, float Warmth) FilterTone(HimoPhotoFilter filter)
        => filter switch
        {
            HimoPhotoFilter.Vivid => (0.025f, 1.18f, 0f),
            HimoPhotoFilter.Warm => (0.02f, 1.05f, 0.65f),
            HimoPhotoFilter.Cool => (0.015f, 1.03f, -0.55f),
            HimoPhotoFilter.Mono => (-0.005f, 0f, 0f),
            HimoPhotoFilter.Sepia => (0.01f, 0.84f, 0.48f),
            HimoPhotoFilter.BeautyNatural => (0.025f, 1.03f, 0.15f),
            HimoPhotoFilter.BeautySoft => (0.045f, 1.05f, 0.20f),
            HimoPhotoFilter.BeautyGlow => (0.075f, 1.08f, 0.34f),
            HimoPhotoFilter.BeautyMakeupSoft => (0.055f, 1.08f, 0.38f),
            HimoPhotoFilter.BeautyMakeup => (0.065f, 1.10f, 0.45f),
            HimoPhotoFilter.ClearSkin => (0.032f, 1.02f, 0.12f),
            _ => (0f, 1f, 0f)
        };

    private static float SkinScore(byte r, byte g, byte b)
    {
        var rf = r / 255f;
        var gf = g / 255f;
        var bf = b / 255f;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        if (r < 42 || g < 22 || b < 14 || delta < 0.035f)
            return 0f;

        var warm = (rf - bf) > 0.075f && (gf - bf) > 0.018f;
        if (!warm)
            return 0f;

        var range = Math.Clamp((delta - 0.035f) * 2.6f, 0f, 1f);
        var redBias = Math.Clamp((rf - bf - 0.075f) * 4.6f, 0f, 1f);
        return Math.Clamp((range * 0.42f) + (redBias * 0.58f), 0f, 1f);
    }

    private static bool IsInsideEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        var dx = (x - cx) / Math.Max(1f, rx);
        var dy = (y - cy) / Math.Max(1f, ry);
        return (dx * dx) + (dy * dy) <= 1f;
    }

    private static float BlushWeight(int x, int y, int width, int height)
    {
        var minDim = Math.Min(width, height);
        return Math.Clamp(
            Gaussian(x, y, width * 0.34f, height * 0.58f, minDim * 0.09f, minDim * 0.07f)
            + Gaussian(x, y, width * 0.66f, height * 0.58f, minDim * 0.09f, minDim * 0.07f),
            0f,
            1f);
    }

    private static float LipWeight(int x, int y, int width, int height)
    {
        var minDim = Math.Min(width, height);
        return Gaussian(x, y, width * 0.50f, height * 0.70f, minDim * 0.07f, minDim * 0.035f);
    }

    private static float Gaussian(float x, float y, float cx, float cy, float sx, float sy)
    {
        var dx = (x - cx) / Math.Max(1f, sx);
        var dy = (y - cy) / Math.Max(1f, sy);
        return MathF.Exp(-0.5f * ((dx * dx) + (dy * dy)));
    }

    private static void ApplySaturation(ref byte r, ref byte g, ref byte b, float saturation)
    {
        if (saturation <= 0.001f)
        {
            var gray = (byte)Math.Clamp((int)Math.Round((0.299f * r) + (0.587f * g) + (0.114f * b)), 0, 255);
            r = gray;
            g = gray;
            b = gray;
            return;
        }

        var avg = (r + g + b) / 3f;
        r = ClampByte(avg + ((r - avg) * saturation));
        g = ClampByte(avg + ((g - avg) * saturation));
        b = ClampByte(avg + ((b - avg) * saturation));
    }

    private static byte Blend(byte source, byte target, float amount)
        => ClampByte(source + ((target - source) * Math.Clamp(amount, 0f, 1f)));

    private static byte ClampByte(float value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static void SaveJpeg(ABitmap bitmap, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? FileSystem.CacheDirectory);
        var tempPath = outputPath + ".tmp";

        try
        {
            using (var stream = File.Create(tempPath))
            {
                var format = ABitmap.CompressFormat.Jpeg
                    ?? throw new InvalidOperationException("صيغة JPEG غير متاحة.");
                if (!bitmap.Compress(format, 96, stream))
                    throw new IOException("تعذر حفظ الصورة المعدلة.");
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
