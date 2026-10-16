using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Lightweight, code-drawn chat wallpapers. They avoid large bitmap assets and can be
/// swapped instantly from the conversation menu.
/// </summary>
public sealed class ChatWallpaperDrawable : IDrawable
{
    public const string PurpleFlowers = "flowers";
    public const string SunsetMountains = "mountains";
    public const string MoonLake = "moon";
    public const string CloseFlowers = "closeflowers";
    public const string QuietLake = "quietlake";
    public const string PurpleWaves = "waves";

    public string Style { get; private set; } = PurpleFlowers;

    public void SetStyle(string? style)
    {
        Style = style switch
        {
            SunsetMountains => SunsetMountains,
            MoonLake => MoonLake,
            CloseFlowers => CloseFlowers,
            QuietLake => QuietLake,
            PurpleWaves => PurpleWaves,
            _ => PurpleFlowers
        };
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = Math.Max(1f, dirtyRect.Width);
        var h = Math.Max(1f, dirtyRect.Height);

        switch (Style)
        {
            case SunsetMountains:
                DrawSunsetMountains(canvas, w, h);
                break;
            case MoonLake:
                DrawMoonLake(canvas, w, h);
                break;
            case CloseFlowers:
                DrawCloseFlowers(canvas, w, h);
                break;
            case QuietLake:
                DrawQuietLake(canvas, w, h);
                break;
            case PurpleWaves:
                DrawPurpleWaves(canvas, w, h);
                break;
            default:
                DrawPurpleFlowers(canvas, w, h);
                break;
        }
    }

    private static void DrawPurpleFlowers(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#C5B3E8", "#A68CDA", "#7656A7", "#4D3777", "#2E1A4A", "#24163E");
        DrawSoftSunset(c, w * .62f, h * .30f, 180, "#FF86C8", 0.28f);
        DrawSoftSunset(c, w * .54f, h * .44f, 120, "#FFD0EF", 0.18f);
        DrawMountains(c, w, h, .35f, "#493A69", "#2B2048", "#211636");
        DrawLake(c, w, h, .58f, "#4C3A69", "#251938", "#9A6ED1");
        DrawFlowerBranch(c, 22, h * .18f, -1, 1.12f, "#E9B8FF", "#A86BE8");
        DrawFlowerBranch(c, w - 18, h * .76f, 1, 0.92f, "#F0C7FF", "#A95AE5");
    }

    private static void DrawSunsetMountains(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#FFD3E9", "#ECA8D2", "#9A68AB", "#64437F", "#322052", "#201535");
        DrawSoftSunset(c, w * .62f, h * .30f, 130, "#FFD46E", 0.34f);
        DrawMountains(c, w, h, .33f, "#67527F", "#46315E", "#26193C");
        DrawLake(c, w, h, .58f, "#6B517B", "#27183D", "#F0B2C7");
        DrawFlowerBranch(c, 12, h * .82f, -1, 0.55f, "#E2A2E7", "#8C54BD");
    }

    private static void DrawMoonLake(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#7A67BB", "#4B368B", "#2F2368", "#1D164E", "#120D35", "#0B0926");
        DrawStarField(c, w, h);
        c.FillColor = Color.FromArgb("#F4DAFF");
        c.FillCircle(w * .70f, h * .23f, Math.Min(w, h) * .07f);
        c.FillColor = Color.FromArgb("#B9A1E8");
        c.FillCircle(w * .70f + 8, h * .23f - 3, Math.Min(w, h) * .058f);
        DrawMountains(c, w, h, .38f, "#2C2254", "#171235", "#0F0B28");
        DrawLake(c, w, h, .60f, "#3E2E6B", "#140E30", "#8A6BCD");
        DrawFlowerBranch(c, w - 5, h * .84f, 1, 0.48f, "#C8A1FF", "#7043B7");
    }

    private static void DrawCloseFlowers(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#A98FD0", "#7557A2", "#4E3774", "#2F1B4C", "#24163E", "#1A102E");
        DrawBokeh(c, w, h);
        DrawFlower(c, w * .17f, h * .28f, 38, "#F0C8FF", "#A863DA");
        DrawFlower(c, w * .11f, h * .56f, 54, "#D6A6F4", "#8D55C8");
        DrawFlower(c, w * .84f, h * .68f, 46, "#E6BAFF", "#9658D7");
        DrawFlower(c, w * .76f, h * .18f, 28, "#CFA3F4", "#7E4BC0");
        DrawHaze(c, w * .54f, h * .44f, 210, "#E7B9FF", 0.10f);
    }

    private static void DrawQuietLake(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#B8A7D3", "#836AA9", "#57457B", "#38265A", "#24183F", "#17102B");
        DrawMountains(c, w, h, .36f, "#4F4769", "#352A4C", "#1A122E");
        DrawLake(c, w, h, .58f, "#574475", "#1C132F", "#A886D6");
        DrawDock(c, w, h);
        DrawFlowerBranch(c, 15, h * .20f, -1, .55f, "#E1B1FF", "#8C5EC1");
        DrawFlowerBranch(c, w - 20, h * .78f, 1, .48f, "#D9A6FF", "#7F50B9");
    }

    private static void DrawPurpleWaves(ICanvas c, float w, float h)
    {
        DrawBands(c, w, h,
            "#4E327A", "#34235D", "#251747", "#1B1034", "#130A27", "#0C061A");
        c.StrokeSize = 5;
        c.StrokeColor = Color.FromArgb("#A66BFF");
        for (var i = 0; i < 9; i++)
        {
            var path = new PathF();
            var y = h * (0.12f + i * .10f);
            path.MoveTo(-20, y);
            path.CurveTo(w * .22f, y - 55, w * .40f, y + 55, w * .62f, y);
            path.CurveTo(w * .79f, y - 42, w * .92f, y + 42, w + 20, y + 4);
            c.DrawPath(path);
        }
        DrawHaze(c, w * .55f, h * .45f, 240, "#D4A8FF", .13f);
        DrawHaze(c, w * .35f, h * .73f, 160, "#8D6CE0", .10f);
    }

    private static void DrawBands(ICanvas c, float w, float h, params string[] colors)
    {
        var bandH = h / Math.Max(1, colors.Length);
        for (var i = 0; i < colors.Length; i++)
        {
            c.FillColor = Color.FromArgb(colors[i]);
            c.FillRectangle(0, i * bandH, w, bandH + 2);
        }
    }

    private static void DrawSoftSunset(ICanvas c, float x, float y, float radius, string color, float alpha)
    {
        c.FillColor = Color.FromArgb(color).WithAlpha(alpha);
        c.FillCircle(x, y, radius);
    }

    private static void DrawHaze(ICanvas c, float x, float y, float radius, string color, float alpha)
    {
        c.FillColor = Color.FromArgb(color).WithAlpha(alpha);
        c.FillCircle(x, y, radius);
    }

    private static void DrawMountains(ICanvas c, float w, float h, float top, string back, string mid, string front)
    {
        var p1 = new PathF();
        p1.MoveTo(0, h * top);
        p1.LineTo(w * .16f, h * (top - .12f));
        p1.LineTo(w * .28f, h * top);
        p1.LineTo(w * .44f, h * (top - .18f));
        p1.LineTo(w * .58f, h * (top + .01f));
        p1.LineTo(w * .73f, h * (top - .13f));
        p1.LineTo(w * .89f, h * (top + .02f));
        p1.LineTo(w, h * (top - .05f));
        p1.LineTo(w, h * .62f);
        p1.LineTo(0, h * .62f);
        p1.Close();
        c.FillColor = Color.FromArgb(back);
        c.FillPath(p1);

        var p2 = new PathF();
        p2.MoveTo(0, h * (top + .09f));
        p2.LineTo(w * .20f, h * (top - .01f));
        p2.LineTo(w * .38f, h * (top + .12f));
        p2.LineTo(w * .55f, h * (top + .02f));
        p2.LineTo(w * .73f, h * (top + .10f));
        p2.LineTo(w * .91f, h * (top + .01f));
        p2.LineTo(w, h * (top + .08f));
        p2.LineTo(w, h * .67f);
        p2.LineTo(0, h * .67f);
        p2.Close();
        c.FillColor = Color.FromArgb(mid);
        c.FillPath(p2);

        var p3 = new PathF();
        p3.MoveTo(0, h * (top + .18f));
        p3.LineTo(w * .23f, h * (top + .11f));
        p3.LineTo(w * .42f, h * (top + .21f));
        p3.LineTo(w * .61f, h * (top + .11f));
        p3.LineTo(w * .80f, h * (top + .19f));
        p3.LineTo(w, h * (top + .12f));
        p3.LineTo(w, h * .72f);
        p3.LineTo(0, h * .72f);
        p3.Close();
        c.FillColor = Color.FromArgb(front);
        c.FillPath(p3);
    }

    private static void DrawLake(ICanvas c, float w, float h, float top, string topColor, string bottomColor, string glow)
    {
        c.FillColor = Color.FromArgb(topColor);
        c.FillRectangle(0, h * top, w, h * (1f - top));
        c.StrokeColor = Color.FromArgb(glow).WithAlpha(.30f);
        c.StrokeSize = 2;
        for (var i = 0; i < 16; i++)
        {
            var y = h * (top + .05f + i * .025f);
            var half = w * (0.16f + (i % 4) * .05f);
            c.DrawLine(w * .5f - half, y, w * .5f + half, y);
        }
        c.FillColor = Color.FromArgb(bottomColor).WithAlpha(.55f);
        c.FillRectangle(0, h * .82f, w, h * .18f);
    }

    private static void DrawStarField(ICanvas c, float w, float h)
    {
        c.FillColor = Color.FromArgb("#EADFFF").WithAlpha(.72f);
        for (var i = 0; i < 46; i++)
        {
            var x = (i * 83 % 1000) / 1000f * w;
            var y = (i * 47 % 580) / 580f * h;
            var r = 0.8f + (i % 3) * .45f;
            c.FillCircle(x, y, r);
        }
    }

    private static void DrawBokeh(ICanvas c, float w, float h)
    {
        var circles = new (float x, float y, float r, string color, float alpha)[]
        {
            (.16f,.22f,72,"#F0C9FF",.10f), (.78f,.19f,92,"#B88BFF",.08f),
            (.52f,.54f,118,"#E4B8FF",.06f), (.88f,.74f,65,"#D7B0FF",.09f),
            (.31f,.76f,84,"#A67DE2",.07f)
        };
        foreach (var item in circles)
        {
            c.FillColor = Color.FromArgb(item.color).WithAlpha(item.alpha);
            c.FillCircle(w * item.x, h * item.y, item.r);
        }
    }

    private static void DrawFlowerBranch(ICanvas c, float x, float y, int direction, float scale, string petal, string center)
    {
        c.StrokeColor = Color.FromArgb("#2E1B45").WithAlpha(.82f);
        c.StrokeSize = Math.Max(2f, 6f * scale);
        var path = new PathF();
        path.MoveTo(x, y + 260 * scale);
        path.CurveTo(x + 40 * direction * scale, y + 170 * scale,
                     x + 22 * direction * scale, y + 90 * scale,
                     x + 54 * direction * scale, y);
        c.DrawPath(path);

        DrawFlower(c, x + 58 * direction * scale, y + 28 * scale, 24 * scale, petal, center);
        DrawFlower(c, x + 16 * direction * scale, y + 92 * scale, 19 * scale, petal, center);
        DrawFlower(c, x + 62 * direction * scale, y + 164 * scale, 28 * scale, petal, center);
        DrawFlower(c, x - 8 * direction * scale, y + 215 * scale, 16 * scale, petal, center);
    }

    private static void DrawFlower(ICanvas c, float cx, float cy, float radius, string petalColor, string centerColor)
    {
        c.FillColor = Color.FromArgb(petalColor).WithAlpha(.82f);
        for (var i = 0; i < 5; i++)
        {
            var angle = MathF.PI * 2f * i / 5f;
            var px = cx + MathF.Cos(angle) * radius * .72f;
            var py = cy + MathF.Sin(angle) * radius * .72f;
            c.FillEllipse(px - radius * .45f, py - radius * .70f, radius * .90f, radius * 1.40f);
        }
        c.FillColor = Color.FromArgb(centerColor);
        c.FillCircle(cx, cy, radius * .27f);
    }

    private static void DrawDock(ICanvas c, float w, float h)
    {
        c.FillColor = Color.FromArgb("#24152F").WithAlpha(.88f);
        var p = new PathF();
        p.MoveTo(w * .42f, h);
        p.LineTo(w * .58f, h);
        p.LineTo(w * .66f, h * .72f);
        p.LineTo(w * .34f, h * .72f);
        p.Close();
        c.FillPath(p);
        c.StrokeColor = Color.FromArgb("#C69BFF").WithAlpha(.22f);
        c.StrokeSize = 2;
        for (var i = 0; i < 6; i++)
        {
            var y = h * (.75f + i * .04f);
            c.DrawLine(w * .40f, y, w * .60f, y);
        }
        c.FillColor = Color.FromArgb("#FFDD9C").WithAlpha(.80f);
        c.FillRoundedRectangle(w * .48f, h * .66f, 26, 34, 6);
    }
}
