#pragma warning disable CA1416
using ABitmap = global::Android.Graphics.Bitmap;
using ABitmapFactory = global::Android.Graphics.BitmapFactory;
using ACanvas = global::Android.Graphics.Canvas;
using AColorMatrix = global::Android.Graphics.ColorMatrix;
using AColorMatrixColorFilter = global::Android.Graphics.ColorMatrixColorFilter;
using APaint = global::Android.Graphics.Paint;
using APaintFlags = global::Android.Graphics.PaintFlags;

namespace Himo.Platforms.Android;

internal enum HimoPhotoFilter
{
    Natural,
    Vivid,
    Warm,
    Cool,
    Mono,
    Sepia,
    BeautyNatural,
    BeautySoft,
    BeautyGlow,
    BeautyMakeupSoft,
    BeautyMakeup,
    ClearSkin
}

internal static class PhotoFilterProcessor
{
    private static bool IsBeautyFilter(HimoPhotoFilter filter)
        => filter is HimoPhotoFilter.BeautyNatural
            or HimoPhotoFilter.BeautySoft
            or HimoPhotoFilter.BeautyGlow
            or HimoPhotoFilter.BeautyMakeupSoft
            or HimoPhotoFilter.BeautyMakeup
            or HimoPhotoFilter.ClearSkin;

    public static async Task ApplyAsync(
        string filePath,
        HimoPhotoFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (filter == HimoPhotoFilter.Natural || string.IsNullOrWhiteSpace(filePath))
            return;

        await Task.Run(
            () => ApplyInternal(filePath, filter, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyInternal(
        string filePath,
        HimoPhotoFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = ABitmapFactory.DecodeFile(filePath)
            ?? throw new IOException("تعذر قراءة الصورة لتطبيق الفلتر.");

        if (IsBeautyFilter(filter))
        {
            ApplyBeautyFilter(filePath, source, filter, cancellationToken);
            return;
        }

        var config = ABitmap.Config.Argb8888
            ?? throw new InvalidOperationException("صيغة الصورة غير متاحة.");

        using var filtered = ABitmap.CreateBitmap(source.Width, source.Height, config)
            ?? throw new IOException("تعذر إنشاء الصورة المعدلة.");

        using (var canvas = new ACanvas(filtered))
        using (var paint = new APaint(APaintFlags.AntiAlias | APaintFlags.FilterBitmap))
        using (var colorFilter = new AColorMatrixColorFilter(CreateMatrix(filter)))
        {
            paint.SetColorFilter(colorFilter);
            canvas.DrawBitmap(source, 0f, 0f, paint);
        }

        SaveBitmap(filePath, filtered, cancellationToken);
    }

    private static void ApplyBeautyFilter(
        string filePath,
        ABitmap source,
        HimoPhotoFilter filter,
        CancellationToken cancellationToken)
    {
        var preset = filter switch
        {
            HimoPhotoFilter.BeautyNatural => new BeautyPreset(0.28f, 5f, 1.03f, 0.00f, 0.30f, 0.00f, 0.00f),
            HimoPhotoFilter.BeautySoft => new BeautyPreset(0.42f, 8f, 1.05f, 0.02f, 0.55f, 0.00f, 0.00f),
            HimoPhotoFilter.BeautyGlow => new BeautyPreset(0.38f, 13f, 1.08f, 0.03f, 0.50f, 0.06f, 0.00f),
            HimoPhotoFilter.BeautyMakeupSoft => new BeautyPreset(0.48f, 9f, 1.08f, 0.16f, 0.62f, 0.10f, 0.04f),
            HimoPhotoFilter.BeautyMakeup => new BeautyPreset(0.58f, 12f, 1.12f, 0.24f, 0.76f, 0.14f, 0.06f),
            HimoPhotoFilter.ClearSkin => new BeautyPreset(0.64f, 6f, 1.03f, 0.00f, 0.92f, 0.00f, 0.00f),
            _ => new BeautyPreset(0f, 0f, 1f, 0f, 0f, 0f, 0f)
        };

        const int maxDimension = 2400;
        var scale = Math.Max(source.Width, source.Height) > maxDimension
            ? maxDimension / (float)Math.Max(source.Width, source.Height)
            : 1f;

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        ABitmap? working = null;
        if (width == source.Width && height == source.Height)
        {
            var config = ABitmap.Config.Argb8888
                ?? throw new InvalidOperationException("صيغة الصورة غير متاحة.");
            working = source.Copy(config, true);
        }
        else
        {
            working = ABitmap.CreateScaledBitmap(source, width, height, true);
        }

        if (working is null)
            throw new IOException("تعذر تجهيز الصورة للتجميل.");

        using (working)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A reduced copy acts as a soft local-tone guide. It is blended only into
            // pixels that resemble skin, which keeps hair, eyes and the background sharper.
            var guideWidth = Math.Max(1, width / 5);
            var guideHeight = Math.Max(1, height / 5);
            using var guide = ABitmap.CreateScaledBitmap(working, guideWidth, guideHeight, true)
                ?? throw new IOException("تعذر إنشاء طبقة تنعيم البشرة.");

            var pixels = new int[width * height];
            var guidePixels = new int[guideWidth * guideHeight];
            working.GetPixels(pixels, 0, width, 0, 0, width, height);
            guide.GetPixels(guidePixels, 0, guideWidth, 0, 0, guideWidth, guideHeight);

            var minDim = Math.Min(width, height);
            var faceCenterX = width * 0.50f;
            var faceCenterY = height * 0.48f;
            var faceRadiusX = width * 0.40f;
            var faceRadiusY = height * 0.46f;

            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var guideY = Math.Min(guideHeight - 1, y * guideHeight / height);

                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var argb = pixels[index];
                    var a = (byte)((argb >> 24) & 0xFF);
                    var r = (byte)((argb >> 16) & 0xFF);
                    var g = (byte)((argb >> 8) & 0xFF);
                    var b = (byte)(argb & 0xFF);

                    var skin = SkinScore(r, g, b);
                    var inFace = IsInsideEllipse(x, y, faceCenterX, faceCenterY, faceRadiusX, faceRadiusY);
                    var smoothWeight = inFace ? Math.Clamp(skin * preset.Smoothing, 0f, 0.85f) : 0f;

                    if (smoothWeight > 0.01f)
                    {
                        var guideX = Math.Min(guideWidth - 1, x * guideWidth / width);
                        var soft = guidePixels[guideY * guideWidth + guideX];
                        var sr = (byte)((soft >> 16) & 0xFF);
                        var sg = (byte)((soft >> 8) & 0xFF);
                        var sb = (byte)(soft & 0xFF);

                        r = Blend(r, sr, smoothWeight);
                        g = Blend(g, sg, smoothWeight);
                        b = Blend(b, sb, smoothWeight);
                    }

                    // Clean small local deviations that are likely to be blemishes.
                    if (inFace && skin > 0.35f && preset.ClearStrength > 0f)
                    {
                        var guideX = Math.Min(guideWidth - 1, x * guideWidth / width);
                        var soft = guidePixels[guideY * guideWidth + guideX];
                        var sr = (byte)((soft >> 16) & 0xFF);
                        var sg = (byte)((soft >> 8) & 0xFF);
                        var sb = (byte)(soft & 0xFF);
                        var delta = Math.Abs(r - sr) + Math.Abs(g - sg) + Math.Abs(b - sb);
                        var blemishWeight = Math.Clamp((delta - 22f) / 110f * preset.ClearStrength, 0f, 0.50f);
                        r = Blend(r, sr, blemishWeight);
                        g = Blend(g, sg, blemishWeight);
                        b = Blend(b, sb, blemishWeight);
                    }

                    // Tone and color are applied only to likely skin so the background remains natural.
                    if (inFace && skin > 0.20f)
                    {
                        r = ClampByte(r + preset.Brightness);
                        g = ClampByte(g + preset.Brightness * 0.92f);
                        b = ClampByte(b + preset.Brightness * 0.68f);
                        ApplySaturation(ref r, ref g, ref b, preset.Saturation);

                        if (preset.Warmth > 0f)
                        {
                            r = ClampByte(r + 7f * preset.Warmth);
                            b = ClampByte(b - 4f * preset.Warmth);
                        }

                        if (preset.Blush > 0f)
                        {
                            var blush = BlushWeight(x, y, width, height, minDim) * skin * preset.Blush;
                            r = ClampByte(r + 34f * blush);
                            g = ClampByte(g - 8f * blush);
                            b = ClampByte(b + 8f * blush);
                        }

                        if (preset.LipTint > 0f)
                        {
                            var lip = LipWeight(x, y, width, height, minDim) * preset.LipTint;
                            r = ClampByte(r + 30f * lip);
                            g = ClampByte(g - 8f * lip);
                            b = ClampByte(b + 8f * lip);
                        }
                    }

                    pixels[index] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }

            working.SetPixels(pixels, 0, width, 0, 0, width, height);
            SaveBitmap(filePath, working, cancellationToken);
        }
    }

    private readonly record struct BeautyPreset(
        float Smoothing,
        float Brightness,
        float Saturation,
        float Blush,
        float ClearStrength,
        float Warmth,
        float LipTint);

    private static bool IsInsideEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        var dx = (x - cx) / Math.Max(1f, rx);
        var dy = (y - cy) / Math.Max(1f, ry);
        return (dx * dx) + (dy * dy) <= 1f;
    }

    private static float SkinScore(byte r, byte g, byte b)
    {
        var rf = r / 255f;
        var gf = g / 255f;
        var bf = b / 255f;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        if (r < 45 || g < 25 || b < 15 || delta < 0.04f)
            return 0f;

        var warm = (rf - bf) > 0.09f && (gf - bf) > 0.03f;
        var range = Math.Clamp((delta - 0.04f) * 2.4f, 0f, 1f);
        var redBias = Math.Clamp((rf - bf - 0.09f) * 4.5f, 0f, 1f);
        return warm ? Math.Clamp((range * 0.45f) + (redBias * 0.55f), 0f, 1f) : 0f;
    }

    private static float BlushWeight(int x, int y, int width, int height, int minDim)
    {
        var left = Gaussian(x, y, width * 0.34f, height * 0.58f, minDim * 0.095f, minDim * 0.070f);
        var right = Gaussian(x, y, width * 0.66f, height * 0.58f, minDim * 0.095f, minDim * 0.070f);
        return Math.Clamp(left + right, 0f, 1f);
    }

    private static float LipWeight(int x, int y, int width, int height, int minDim)
        => Gaussian(x, y, width * 0.50f, height * 0.68f, minDim * 0.065f, minDim * 0.035f);

    private static float Gaussian(float x, float y, float cx, float cy, float sx, float sy)
    {
        var dx = (x - cx) / Math.Max(1f, sx);
        var dy = (y - cy) / Math.Max(1f, sy);
        return MathF.Exp(-0.5f * ((dx * dx) + (dy * dy)));
    }

    private static void ApplySaturation(ref byte r, ref byte g, ref byte b, float saturation)
    {
        var avg = (r + g + b) / 3f;
        r = ClampByte(avg + ((r - avg) * saturation));
        g = ClampByte(avg + ((g - avg) * saturation));
        b = ClampByte(avg + ((b - avg) * saturation));
    }

    private static byte Blend(byte source, byte target, float amount)
        => ClampByte(source + ((target - source) * amount));

    private static byte ClampByte(float value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static void SaveBitmap(string filePath, ABitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = filePath + ".beauty.jpg";
        try
        {
            using var stream = File.Create(tempPath);
            var format = ABitmap.CompressFormat.Jpeg
                ?? throw new InvalidOperationException("صيغة JPEG غير متاحة.");
            if (!bitmap.Compress(format, 96, stream))
                throw new IOException("تعذر حفظ الصورة بعد تطبيق الفلتر.");
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
    }

    private static AColorMatrix CreateMatrix(HimoPhotoFilter filter)
    {
        return filter switch
        {
            HimoPhotoFilter.Vivid => new AColorMatrix(new[]
            {
                1.28f, 0f, 0f, 0f, -18f,
                0f, 1.28f, 0f, 0f, -18f,
                0f, 0f, 1.28f, 0f, -18f,
                0f, 0f, 0f, 1f, 0f
            }),
            HimoPhotoFilter.Warm => new AColorMatrix(new[]
            {
                1.12f, 0f, 0f, 0f, 8f,
                0f, 1.04f, 0f, 0f, 2f,
                0f, 0f, 0.88f, 0f, -3f,
                0f, 0f, 0f, 1f, 0f
            }),
            HimoPhotoFilter.Cool => new AColorMatrix(new[]
            {
                0.88f, 0f, 0f, 0f, -3f,
                0f, 1.04f, 0f, 0f, 2f,
                0f, 0f, 1.12f, 0f, 8f,
                0f, 0f, 0f, 1f, 0f
            }),
            HimoPhotoFilter.Mono => CreateMonoMatrix(),
            HimoPhotoFilter.Sepia => new AColorMatrix(new[]
            {
                0.393f, 0.769f, 0.189f, 0f, 0f,
                0.349f, 0.686f, 0.168f, 0f, 0f,
                0.272f, 0.534f, 0.131f, 0f, 0f,
                0f, 0f, 0f, 1f, 0f
            }),
            _ => CreateIdentityMatrix()
        };
    }

    private static AColorMatrix CreateMonoMatrix()
    {
        var matrix = new AColorMatrix();
        matrix.SetSaturation(0f);
        return matrix;
    }

    private static AColorMatrix CreateIdentityMatrix() => new AColorMatrix();
}
#pragma warning restore CA1416
