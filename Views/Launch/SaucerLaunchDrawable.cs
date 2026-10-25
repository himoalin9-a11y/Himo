using Microsoft.Maui.Graphics;

namespace Himo.Views.Launch;

/// <summary>
/// Native MAUI cinematic lunar launch scene. It intentionally avoids WebView/WebGL,
/// allocates no per-frame particle collections, and keeps all coordinates resolution-independent.
/// </summary>
internal sealed class SaucerLaunchDrawable : IDrawable
{
    private readonly Star[] _stars;
    private readonly Rock[] _rocks;
    private readonly Crater[] _craters;

    public double IdleSeconds { get; set; }
    public double LaunchSeconds { get; set; }
    public bool IsLaunching { get; set; }

    public SaucerLaunchDrawable()
    {
        var random = new Random(28471);
        _stars = new Star[150];
        for (var i = 0; i < _stars.Length; i++)
        {
            _stars[i] = new Star(
                (float)random.NextDouble(),
                (float)random.NextDouble() * .70f,
                .45f + (float)random.NextDouble() * 1.65f,
                .22f + (float)random.NextDouble() * .72f,
                (float)random.NextDouble() * 6.28f,
                i % 17 == 0);
        }

        _rocks = new Rock[38];
        for (var i = 0; i < _rocks.Length; i++)
        {
            _rocks[i] = new Rock(
                (float)random.NextDouble(),
                .665f + (float)random.NextDouble() * .19f,
                2.0f + (float)random.NextDouble() * 9.0f,
                1.5f + (float)random.NextDouble() * 5.5f,
                i % 3);
        }

        _craters = new Crater[13];
        for (var i = 0; i < _craters.Length; i++)
        {
            _craters[i] = new Crater(
                .04f + (float)random.NextDouble() * .92f,
                .70f + (float)random.NextDouble() * .21f,
                .025f + (float)random.NextDouble() * .11f,
                .007f + (float)random.NextDouble() * .025f,
                (float)random.NextDouble() * .5f);
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        if (w <= 0 || h <= 0)
            return;

        var t = (float)LaunchSeconds;
        DrawDeepSpace(canvas, w, h, t);
        DrawCelestialBodies(canvas, w, h, t);
        DrawLunarHorizon(canvas, w, h);
        DrawLunarSurface(canvas, w, h, t);

        var cx = w * .5f;
        var padY = h * .695f;
        var scale = Math.Clamp(w / 390f, .79f, 1.12f);

        DrawLandingSite(canvas, cx, padY, w, h, scale, t);
        if (IsLaunching)
            DrawLunarDust(canvas, cx, padY, w, scale, t);

        var ignition = IsLaunching ? EaseOut(Clamp01(t / .82f)) : 0f;
        var shake = IsLaunching && t < 1.18f
            ? (float)Math.Sin(t * 43f) * (1.8f + ignition * 1.9f)
            : (float)Math.Sin(IdleSeconds * 1.15) * 1.25f;
        var liftProgress = IsLaunching ? EaseInOut(Clamp01((t - 1.18f) / 2.35f)) : 0f;
        var lift = liftProgress * h * 1.22f;
        var saucerY = h * .548f + shake - lift;

        if (IsLaunching && t > .32f)
            DrawEngineColumn(canvas, cx, saucerY, padY, scale, t);

        DrawSaucer(canvas, cx, saucerY, scale, t, ignition);
        if (IsLaunching && t > 3.28f)
        {
            var shimmer = Clamp01((t - 3.28f) / .70f) * .18f;
            canvas.FillColor = WithAlpha(Color.FromArgb("#B7DFFF"), shimmer);
            canvas.FillRectangle(0, 0, w, h);
        }
    }

    private void DrawDeepSpace(ICanvas canvas, float w, float h, float t)
    {
        canvas.FillColor = Color.FromArgb("#02040D");
        canvas.FillRectangle(0, 0, w, h);

        // Broad atmospheric-looking nebulae add depth, not a flat purple backdrop.
        Glow(canvas, w * .17f, h * .20f, Math.Min(w, h) * .47f, Color.FromArgb("#15396A"), .17f);
        Glow(canvas, w * .78f, h * .35f, Math.Min(w, h) * .38f, Color.FromArgb("#33245F"), .13f);
        Glow(canvas, w * .44f, h * .62f, Math.Min(w, h) * .45f, Color.FromArgb("#1C2D53"), .095f);
        Glow(canvas, w * .53f, h * .09f, Math.Min(w, h) * .23f, Color.FromArgb("#16364C"), .07f);

        foreach (var star in _stars)
        {
            var twinkle = .70f + .30f * (float)Math.Sin(IdleSeconds * (star.Bright ? 2.1 : 1.25) + star.Phase);
            var x = star.X * w;
            var y = star.Y * h;
            var alpha = star.Alpha * twinkle;
            if (IsLaunching && t > 1.2f && star.Y > .2f)
            {
                var streak = Clamp01((t - 1.2f) / 1.2f);
                canvas.StrokeColor = WithAlpha(Color.FromArgb("#9FC8FF"), alpha * streak * .33f);
                canvas.StrokeSize = star.Size * .65f;
                canvas.DrawLine(x, y, x - (x - w * .5f) * .035f * streak, y + 12f * streak + star.Size * 2);
            }
            canvas.FillColor = WithAlpha(star.Bright ? Color.FromArgb("#E4F5FF") : Color.FromArgb("#B8C8F4"), alpha);
            canvas.FillEllipse(x, y, star.Size, star.Size);
            if (star.Bright)
            {
                canvas.StrokeColor = WithAlpha(Color.FromArgb("#D5EDFF"), alpha * .48f);
                canvas.StrokeSize = .65f;
                canvas.DrawLine(x - 3.4f, y + star.Size * .5f, x + 4.5f, y + star.Size * .5f);
                canvas.DrawLine(x + star.Size * .5f, y - 3, x + star.Size * .5f, y + 4);
            }
        }
    }

    private void DrawCelestialBodies(ICanvas canvas, float w, float h, float t)
    {
        // Earth: shaded blue limb, ocean, continental silhouettes and cloud bands.
        var earthR = Math.Min(w, h) * .112f;
        var ex = w * .185f;
        var ey = h * .215f;
        Glow(canvas, ex, ey, earthR * 2.15f, Color.FromArgb("#318CFF"), .23f);
        canvas.FillColor = Color.FromArgb("#315FBB");
        canvas.FillEllipse(ex - earthR, ey - earthR, earthR * 2, earthR * 2);
        canvas.FillColor = Color.FromArgb("#0A234A");
        canvas.FillEllipse(ex - earthR * .90f, ey - earthR * .95f, earthR * 1.82f, earthR * 1.89f);
        canvas.FillColor = Color.FromArgb("#14539A");
        canvas.FillEllipse(ex - earthR * .75f, ey - earthR * .84f, earthR * 1.52f, earthR * 1.68f);

        FillPolygon(canvas, Color.FromArgb("#3D997F"),
            ex - earthR * .73f, ey - earthR * .25f,
            ex - earthR * .48f, ey - earthR * .48f,
            ex - earthR * .24f, ey - earthR * .43f,
            ex - earthR * .18f, ey - earthR * .17f,
            ex - earthR * .35f, ey + earthR * .02f,
            ex - earthR * .44f, ey + earthR * .37f,
            ex - earthR * .64f, ey + earthR * .26f);
        FillPolygon(canvas, Color.FromArgb("#6AAB8B"),
            ex + earthR * .05f, ey - earthR * .67f,
            ex + earthR * .39f, ey - earthR * .57f,
            ex + earthR * .56f, ey - earthR * .32f,
            ex + earthR * .38f, ey - earthR * .10f,
            ex + earthR * .44f, ey + earthR * .14f,
            ex + earthR * .20f, ey + earthR * .33f,
            ex + earthR * .06f, ey + earthR * .09f,
            ex - earthR * .04f, ey - earthR * .19f);
        FillPolygon(canvas, Color.FromArgb("#397C73"),
            ex - earthR * .10f, ey + earthR * .40f,
            ex + earthR * .13f, ey + earthR * .49f,
            ex + earthR * .18f, ey + earthR * .69f,
            ex + earthR * .02f, ey + earthR * .78f,
            ex - earthR * .12f, ey + earthR * .58f);

        // Clouds are short curved-looking strokes that stay within the globe's silhouette.
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#DCEBFF"), .58f);
        canvas.StrokeSize = Math.Max(1.1f, earthR * .055f);
        canvas.DrawLine(ex - earthR * .72f, ey - earthR * .11f, ex - earthR * .31f, ey - earthR * .20f);
        canvas.DrawLine(ex - earthR * .25f, ey - earthR * .51f, ex + earthR * .08f, ey - earthR * .43f);
        canvas.DrawLine(ex + earthR * .20f, ey - earthR * .05f, ex + earthR * .65f, ey - earthR * .17f);
        canvas.DrawLine(ex - earthR * .27f, ey + earthR * .29f, ex + earthR * .17f, ey + earthR * .20f);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#A5D8FF"), .85f);
        canvas.StrokeSize = 1.4f;
        canvas.DrawEllipse(ex - earthR, ey - earthR, earthR * 2, earthR * 2);
        // Terminator shadow on the far edge.
        canvas.FillColor = WithAlpha(Color.FromArgb("#010715"), .38f);
        canvas.FillEllipse(ex + earthR * .28f, ey - earthR * .91f, earthR * .66f, earthR * 1.82f);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#E5F7FF"), .30f);
        canvas.StrokeSize = 1.0f;
        canvas.DrawEllipse(ex - earthR * 1.04f, ey - earthR * 1.04f, earthR * 2.08f, earthR * 2.08f);

        // A small ringed gas giant on the opposite side of the sky.
        var pR = Math.Min(w, h) * .066f;
        var px = w * .82f;
        var py = h * .205f;
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#D8C1FF"), .36f);
        canvas.StrokeSize = Math.Max(1.3f, pR * .065f);
        canvas.DrawEllipse(px - pR * 1.75f, py - pR * .42f, pR * 3.5f, pR * .82f);
        canvas.FillColor = Color.FromArgb("#322A58");
        canvas.FillEllipse(px - pR, py - pR, pR * 2, pR * 2);
        canvas.FillColor = Color.FromArgb("#62578D");
        canvas.FillEllipse(px - pR * .85f, py - pR * .77f, pR * 1.7f, pR * 1.5f);
        canvas.FillColor = Color.FromArgb("#29254B");
        canvas.FillEllipse(px - pR * .58f, py - pR * .77f, pR * .76f, pR * 1.54f);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#C8A9FF"), .31f);
        canvas.StrokeSize = 1;
        canvas.DrawLine(px - pR * .78f, py - pR * .30f, px + pR * .78f, py - pR * .30f);
        canvas.DrawLine(px - pR * .88f, py + pR * .18f, px + pR * .79f, py + pR * .18f);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#A68DDB"), .64f);
        canvas.StrokeSize = 1.1f;
        canvas.DrawEllipse(px - pR, py - pR, pR * 2, pR * 2);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#E0D3FF"), .52f);
        canvas.DrawEllipse(px - pR * 1.84f, py - pR * .31f, pR * 3.68f, pR * .62f);

        // A distant rust-coloured moon/planet close to the horizon.
        var marsR = Math.Min(w, h) * .028f;
        var mx = w * .70f;
        var my = h * .405f;
        Glow(canvas, mx, my, marsR * 3, Color.FromArgb("#C65C4E"), .08f);
        canvas.FillColor = Color.FromArgb("#6D393A");
        canvas.FillEllipse(mx - marsR, my - marsR, marsR * 2, marsR * 2);
        canvas.FillColor = Color.FromArgb("#B66A59");
        canvas.FillEllipse(mx - marsR * .77f, my - marsR * .79f, marsR * 1.52f, marsR * 1.48f);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#E5A193"), .45f);
        canvas.StrokeSize = .75f;
        canvas.DrawEllipse(mx - marsR, my - marsR, marsR * 2, marsR * 2);
    }

    private void DrawLunarHorizon(ICanvas canvas, float w, float h)
    {
        var horizon = h * .637f;
        var far = new PathF();
        far.MoveTo(0, horizon + h * .022f);
        far.LineTo(w * .09f, horizon - h * .047f);
        far.LineTo(w * .18f, horizon - h * .015f);
        far.LineTo(w * .29f, horizon - h * .091f);
        far.LineTo(w * .38f, horizon - h * .033f);
        far.LineTo(w * .48f, horizon - h * .072f);
        far.LineTo(w * .59f, horizon - h * .016f);
        far.LineTo(w * .70f, horizon - h * .084f);
        far.LineTo(w * .80f, horizon - h * .026f);
        far.LineTo(w * .91f, horizon - h * .060f);
        far.LineTo(w, horizon - h * .006f);
        far.LineTo(w, h);
        far.LineTo(0, h);
        far.Close();
        canvas.FillColor = Color.FromArgb("#121A2C");
        canvas.FillPath(far);

        var near = new PathF();
        near.MoveTo(0, horizon + h * .045f);
        near.LineTo(w * .12f, horizon + h * .014f);
        near.LineTo(w * .22f, horizon + h * .033f);
        near.LineTo(w * .33f, horizon - h * .006f);
        near.LineTo(w * .44f, horizon + h * .038f);
        near.LineTo(w * .58f, horizon + h * .018f);
        near.LineTo(w * .69f, horizon + h * .043f);
        near.LineTo(w * .83f, horizon + h * .009f);
        near.LineTo(w, horizon + h * .039f);
        near.LineTo(w, h);
        near.LineTo(0, h);
        near.Close();
        canvas.FillColor = Color.FromArgb("#252C3C");
        canvas.FillPath(near);

        canvas.StrokeColor = WithAlpha(Color.FromArgb("#B9C7E1"), .18f);
        canvas.StrokeSize = 1.2f;
        canvas.DrawLine(0, horizon + h * .045f, w * .12f, horizon + h * .014f);
        canvas.DrawLine(w * .22f, horizon + h * .033f, w * .33f, horizon - h * .006f);
        canvas.DrawLine(w * .58f, horizon + h * .018f, w * .69f, horizon + h * .043f);
        canvas.DrawLine(w * .83f, horizon + h * .009f, w, horizon + h * .039f);
    }

    private void DrawLunarSurface(ICanvas canvas, float w, float h, float t)
    {
        var groundTop = h * .665f;
        canvas.FillColor = Color.FromArgb("#323846");
        canvas.FillRectangle(0, groundTop, w, h - groundTop);
        Glow(canvas, w * .54f, groundTop + h * .07f, w * .62f, Color.FromArgb("#6F7E9A"), .065f);

        // Broad regolith ridges catch the cold light from Earth and the launch vehicle.
        var ridge = new PathF();
        ridge.MoveTo(0, groundTop + h * .038f);
        ridge.LineTo(w * .13f, groundTop + h * .008f);
        ridge.LineTo(w * .27f, groundTop + h * .050f);
        ridge.LineTo(w * .41f, groundTop + h * .025f);
        ridge.LineTo(w * .56f, groundTop + h * .061f);
        ridge.LineTo(w * .72f, groundTop + h * .015f);
        ridge.LineTo(w * .87f, groundTop + h * .040f);
        ridge.LineTo(w, groundTop + h * .020f);
        ridge.LineTo(w, h);
        ridge.LineTo(0, h);
        ridge.Close();
        canvas.FillColor = Color.FromArgb("#454B5B");
        canvas.FillPath(ridge);

        var lowerRidge = new PathF();
        lowerRidge.MoveTo(0, groundTop + h * .14f);
        lowerRidge.LineTo(w * .18f, groundTop + h * .105f);
        lowerRidge.LineTo(w * .35f, groundTop + h * .15f);
        lowerRidge.LineTo(w * .52f, groundTop + h * .116f);
        lowerRidge.LineTo(w * .73f, groundTop + h * .16f);
        lowerRidge.LineTo(w * .91f, groundTop + h * .12f);
        lowerRidge.LineTo(w, groundTop + h * .15f);
        lowerRidge.LineTo(w, h);
        lowerRidge.LineTo(0, h);
        lowerRidge.Close();
        canvas.FillColor = Color.FromArgb("#393F4D");
        canvas.FillPath(lowerRidge);

        foreach (var crater in _craters)
        {
            var x = crater.X * w;
            var y = crater.Y * h;
            var rw = crater.Width * w;
            var rh = crater.Height * h;
            canvas.FillColor = WithAlpha(Color.FromArgb("#171D2A"), .46f);
            canvas.FillEllipse(x - rw * .54f, y - rh * .48f, rw * 1.08f, rh * 1.12f);
            canvas.StrokeColor = WithAlpha(Color.FromArgb("#A7B0C1"), .23f);
            canvas.StrokeSize = Math.Max(.65f, rw * .045f);
            canvas.DrawEllipse(x - rw * .5f, y - rh * .5f, rw, rh);
            canvas.StrokeColor = WithAlpha(Color.FromArgb("#0C1220"), .40f);
            canvas.StrokeSize = Math.Max(.8f, rw * .06f);
            canvas.DrawArc(x - rw * .44f, y - rh * .42f, rw * .88f, rh * .82f, 195, 345, true, false);
        }

        foreach (var rock in _rocks)
        {
            var x = rock.X * w;
            var y = rock.Y * h;
            var rw = rock.Width;
            var rh = rock.Height;
            var rockColor = rock.Tone switch
            {
                0 => Color.FromArgb("#242B39"),
                1 => Color.FromArgb("#505767"),
                _ => Color.FromArgb("#313849")
            };
            FillPolygon(canvas, rockColor,
                x - rw, y,
                x - rw * .72f, y - rh * .68f,
                x - rw * .20f, y - rh,
                x + rw * .55f, y - rh * .72f,
                x + rw, y - rh * .12f,
                x + rw * .54f, y + rh * .13f);
            canvas.StrokeColor = WithAlpha(Color.FromArgb("#ADB8CC"), .18f);
            canvas.StrokeSize = .75f;
            canvas.DrawLine(x - rw * .63f, y - rh * .53f, x - rw * .17f, y - rh * .87f);
        }

        // Fine scattered regolith glints; deliberately fixed in place while idle.
        for (var i = 0; i < 52; i++)
        {
            var x = ((i * 73) % 997) / 997f * w;
            var y = h * (.675f + ((i * 43) % 281) / 1000f);
            var size = .5f + (i % 4) * .28f;
            var alpha = .07f + .06f * (float)(.5 + .5 * Math.Sin(IdleSeconds * .7 + i));
            canvas.FillColor = WithAlpha(i % 4 == 0 ? Color.FromArgb("#D5DCE8") : Color.FromArgb("#A4AFC3"), alpha);
            canvas.FillEllipse(x, y, size, size);
        }
    }

    private void DrawLandingSite(ICanvas canvas, float cx, float y, float w, float h, float s, float t)
    {
        var siteW = Math.Min(w * .76f, 390f);
        Glow(canvas, cx, y + 1, siteW * .56f, Color.FromArgb("#61B7FF"), IsLaunching ? .22f : .075f);

        // Recessed landing area in the lunar regolith, with illuminated guidance insets.
        canvas.FillColor = Color.FromArgb("#202939");
        canvas.FillEllipse(cx - siteW * .53f, y - 18 * s, siteW * 1.06f, 42 * s);
        canvas.FillColor = Color.FromArgb("#707C91");
        canvas.FillEllipse(cx - siteW * .49f, y - 18 * s, siteW * .98f, 36 * s);
        canvas.FillColor = Color.FromArgb("#343F53");
        canvas.FillEllipse(cx - siteW * .46f, y - 16 * s, siteW * .92f, 31 * s);
        canvas.FillColor = Color.FromArgb("#171F31");
        canvas.FillEllipse(cx - siteW * .39f, y - 13 * s, siteW * .78f, 25 * s);

        canvas.StrokeColor = WithAlpha(Color.FromArgb("#D7E6FF"), .51f);
        canvas.StrokeSize = 1.1f * s;
        canvas.DrawEllipse(cx - siteW * .49f, y - 18 * s, siteW * .98f, 36 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#5BE7FF"), IsLaunching ? .70f : .28f);
        canvas.StrokeSize = 1.1f * s;
        canvas.DrawEllipse(cx - siteW * .37f, y - 12 * s, siteW * .74f, 23 * s);

        for (var i = -4; i <= 4; i++)
        {
            var x = cx + i * siteW * .082f;
            var lamp = i % 2 == 0 ? Color.FromArgb("#7AE8FF") : Color.FromArgb("#B9A6FF");
            var pulse = .65f + .35f * (float)Math.Sin(IdleSeconds * 2.7 + i * .83f);
            var intensity = IsLaunching ? .65f + .35f * Clamp01(t / .55f) : .6f;
            Glow(canvas, x, y - 1 * s, 10 * s, lamp, .20f * intensity * pulse);
            canvas.FillColor = WithAlpha(lamp, .48f + .48f * intensity * pulse);
            canvas.FillEllipse(x - 1.9f * s, y - 2f * s, 3.8f * s, 4f * s);
        }

        // Tiny direction marks make the pad read as an engineered landing site, not a cartoon ring.
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#B6C6DF"), .28f);
        canvas.StrokeSize = .8f * s;
        for (var i = -2; i <= 2; i++)
        {
            var x = cx + i * siteW * .105f;
            canvas.DrawLine(x, y - 6 * s, x + i * 2.4f * s, y + 4 * s);
        }
    }

    private void DrawLunarDust(ICanvas canvas, float cx, float padY, float w, float s, float t)
    {
        if (t < .22f)
            return;
        var energy = EaseOut(Clamp01((t - .22f) / .95f));
        const int count = 44;
        for (var i = 0; i < count; i++)
        {
            var birth = i * .045f;
            var age = t - birth;
            if (age < 0 || age > 2.7f)
                continue;

            var phase = i * 2.399f;
            var side = (i % 2 == 0) ? -1f : 1f;
            var spread = (15f + age * 37f + (i % 5) * 4f) * s;
            var x = cx + (float)Math.Sin(phase) * spread + side * age * 18f * s;
            var y = padY - (float)Math.Abs(Math.Cos(phase)) * age * 13f * s - age * 20f * s;
            var radius = (4.5f + age * 8.8f + (i % 4) * 1.8f) * s;
            var alpha = .21f * (1f - age / 2.7f) * energy;
            var dust = i % 3 == 0 ? Color.FromArgb("#C8CEDC") : Color.FromArgb("#8994A9");
            canvas.FillColor = WithAlpha(dust, alpha);
            canvas.FillEllipse(x - radius, y - radius * .50f, radius * 2.0f, radius * 1.05f);
            canvas.FillColor = WithAlpha(Color.FromArgb("#E0E5EF"), alpha * .34f);
            canvas.FillEllipse(x - radius * .48f, y - radius * .76f, radius * .95f, radius * .82f);
        }

        // Ground-hugging dust sheet rolls outward from the engine wash.
        var sheet = Clamp01((t - .38f) / .52f) * (1f - Clamp01((t - 2.15f) / 1.0f));
        Glow(canvas, cx, padY - 8 * s, w * (.22f + .27f * energy), Color.FromArgb("#B4C7E4"), .13f * sheet);
    }

    private void DrawEngineColumn(ICanvas canvas, float cx, float shipY, float padY, float s, float t)
    {
        var power = EaseOut(Clamp01((t - .28f) / .75f));
        var bottom = Math.Min(padY - 5 * s, shipY + 44 * s);
        if (bottom <= shipY)
            return;
        var length = bottom - shipY;
        var flicker = .94f + .06f * (float)Math.Sin(t * 25f);
        Glow(canvas, cx, shipY + length * .54f, 34 * s + length * .13f, Color.FromArgb("#387FFF"), .29f * power);
        FillTriangle(canvas, cx - 22 * s, shipY + 6 * s, cx + 22 * s, shipY + 6 * s, cx + (float)Math.Sin(t * 3.7f) * 8 * s, bottom,
            WithAlpha(Color.FromArgb("#3B6DFF"), .38f * power * flicker));
        FillTriangle(canvas, cx - 13 * s, shipY + 7 * s, cx + 13 * s, shipY + 7 * s, cx, bottom - 4 * s,
            WithAlpha(Color.FromArgb("#53DAFF"), .60f * power * flicker));
        FillTriangle(canvas, cx - 5 * s, shipY + 7 * s, cx + 5 * s, shipY + 7 * s, cx, bottom - 18 * s,
            WithAlpha(Color.FromArgb("#F1FCFF"), .76f * power * flicker));

        // Four ion vents become visible under the hull during ignition.
        for (var i = -1; i <= 1; i++)
        {
            var ventX = cx + i * 37 * s;
            var ventY = shipY + 25 * s;
            var ventPower = power * (.75f + .25f * (float)Math.Sin(t * 21f + i * 1.6f));
            FillTriangle(canvas, ventX - 4 * s, ventY, ventX + 4 * s, ventY, ventX, ventY + (23 + power * 45) * s,
                WithAlpha(Color.FromArgb("#64B7FF"), .52f * ventPower));
        }
    }

    private void DrawSaucer(ICanvas canvas, float cx, float cy, float s, float t, float ignition)
    {
        var pulse = .78f + .22f * (float)Math.Sin(IdleSeconds * 3.1f);
        var flightPower = IsLaunching ? ignition : .20f + .08f * pulse;
        var hullW = 148 * s;

        // Diffuse reflected light under the titanium hull.
        Glow(canvas, cx, cy + 4 * s, hullW * 1.32f, Color.FromArgb("#4F99FF"), .11f + flightPower * .20f);

        // Cockpit canopy: concentric glass layers, cool reflections and a dark far side.
        Glow(canvas, cx, cy - 42 * s, 73 * s, Color.FromArgb("#3ABFFF"), .11f + flightPower * .12f);
        canvas.FillColor = Color.FromArgb("#718CA9");
        canvas.FillEllipse(cx - 60 * s, cy - 85 * s, 120 * s, 83 * s);
        canvas.FillColor = Color.FromArgb("#D0E6F5");
        canvas.FillEllipse(cx - 55 * s, cy - 82 * s, 110 * s, 76 * s);
        canvas.FillColor = Color.FromArgb("#284C6B");
        canvas.FillEllipse(cx - 49 * s, cy - 79 * s, 98 * s, 72 * s);
        canvas.FillColor = Color.FromArgb("#0C1E35");
        canvas.FillEllipse(cx - 42 * s, cy - 75 * s, 84 * s, 65 * s);
        canvas.FillColor = WithAlpha(Color.FromArgb("#367FA8"), .66f);
        canvas.FillEllipse(cx - 35 * s, cy - 70 * s, 70 * s, 51 * s);
        canvas.FillColor = Color.FromArgb("#132840");
        canvas.FillEllipse(cx - 29 * s, cy - 65 * s, 58 * s, 43 * s);
        canvas.FillColor = WithAlpha(Color.FromArgb("#E2F8FF"), .72f);
        canvas.FillEllipse(cx - 30 * s, cy - 70 * s, 30 * s, 7 * s);
        canvas.FillColor = WithAlpha(Color.FromArgb("#80C9F7"), .35f);
        canvas.FillEllipse(cx + 7 * s, cy - 58 * s, 17 * s, 27 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#D1F3FF"), .79f);
        canvas.StrokeSize = 1.35f * s;
        canvas.DrawEllipse(cx - 55 * s, cy - 82 * s, 110 * s, 76 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#5592BB"), .62f);
        canvas.StrokeSize = .85f * s;
        canvas.DrawEllipse(cx - 44 * s, cy - 74 * s, 88 * s, 63 * s);

        // Outer forged alloy silhouette and bevels.
        canvas.FillColor = Color.FromArgb("#0B111D");
        canvas.FillEllipse(cx - 157 * s, cy - 31 * s, 314 * s, 65 * s);
        canvas.FillColor = Color.FromArgb("#9CAEC5");
        canvas.FillEllipse(cx - 153 * s, cy - 34 * s, 306 * s, 63 * s);
        canvas.FillColor = Color.FromArgb("#E2EAF3");
        canvas.FillEllipse(cx - 148 * s, cy - 34 * s, 296 * s, 57 * s);
        canvas.FillColor = Color.FromArgb("#35465F");
        canvas.FillEllipse(cx - 143 * s, cy - 31 * s, 286 * s, 55 * s);
        canvas.FillColor = Color.FromArgb("#778BA4");
        canvas.FillEllipse(cx - 136 * s, cy - 29 * s, 272 * s, 49 * s);
        canvas.FillColor = Color.FromArgb("#1D2B42");
        canvas.FillEllipse(cx - 128 * s, cy - 27 * s, 256 * s, 46 * s);
        canvas.FillColor = Color.FromArgb("#4D5D76");
        canvas.FillEllipse(cx - 114 * s, cy - 25 * s, 228 * s, 40 * s);
        canvas.FillColor = Color.FromArgb("#172438");
        canvas.FillEllipse(cx - 107 * s, cy - 21 * s, 214 * s, 34 * s);

        // Metallic reflections across the upper deck, shaded as segmented armour panels.
        FillPolygon(canvas, Color.FromArgb("#AABAD0"),
            cx - 118 * s, cy - 22 * s, cx - 78 * s, cy - 28 * s,
            cx - 55 * s, cy - 20 * s, cx - 88 * s, cy - 13 * s);
        FillPolygon(canvas, Color.FromArgb("#647B98"),
            cx + 63 * s, cy - 27 * s, cx + 111 * s, cy - 21 * s,
            cx + 85 * s, cy - 13 * s, cx + 54 * s, cy - 18 * s);
        FillPolygon(canvas, Color.FromArgb("#8B9FB9"),
            cx - 37 * s, cy - 26 * s, cx - 5 * s, cy - 24 * s,
            cx - 15 * s, cy - 13 * s, cx - 51 * s, cy - 16 * s);
        FillPolygon(canvas, Color.FromArgb("#526781"),
            cx + 7 * s, cy - 25 * s, cx + 43 * s, cy - 26 * s,
            cx + 57 * s, cy - 17 * s, cx + 22 * s, cy - 13 * s);

        // Armour seams converge slightly toward the rear, like radial panels on a machined hull.
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#D8E3F3"), .57f);
        canvas.StrokeSize = .85f * s;
        canvas.DrawEllipse(cx - 149 * s, cy - 33 * s, 298 * s, 58 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#91A6C2"), .63f);
        canvas.StrokeSize = .8f * s;
        canvas.DrawEllipse(cx - 127 * s, cy - 27 * s, 254 * s, 46 * s);
        for (var seam = -3; seam <= 3; seam++)
        {
            var x = cx + seam * 25 * s;
            var x2 = cx + seam * 33 * s;
            canvas.StrokeColor = WithAlpha(seam % 2 == 0 ? Color.FromArgb("#C7D5E8") : Color.FromArgb("#657994"), .56f);
            canvas.StrokeSize = .75f * s;
            canvas.DrawLine(x, cy - (23 - Math.Abs(seam) * 1.5f) * s, x2, cy + 3 * s);
        }

        // Dark titanium underside with a luminous magnetic ring and separated ion nozzles.
        canvas.FillColor = Color.FromArgb("#0C1422");
        canvas.FillEllipse(cx - 108 * s, cy + 2 * s, 216 * s, 34 * s);
        canvas.FillColor = Color.FromArgb("#52627C");
        canvas.FillEllipse(cx - 97 * s, cy + 3 * s, 194 * s, 26 * s);
        canvas.FillColor = Color.FromArgb("#121F32");
        canvas.FillEllipse(cx - 91 * s, cy + 5 * s, 182 * s, 22 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#5BE4FF"), .46f + flightPower * .5f);
        canvas.StrokeSize = 2.1f * s;
        canvas.DrawEllipse(cx - 82 * s, cy + 7 * s, 164 * s, 19 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#C3A7FF"), .36f + flightPower * .30f);
        canvas.StrokeSize = 1.1f * s;
        canvas.DrawEllipse(cx - 68 * s, cy + 9 * s, 136 * s, 14 * s);

        for (var i = -2; i <= 2; i++)
        {
            var vx = cx + i * 34 * s;
            var vy = cy + 17 * s;
            canvas.FillColor = Color.FromArgb("#080D18");
            canvas.FillEllipse(vx - 7 * s, vy - 5 * s, 14 * s, 10 * s);
            Glow(canvas, vx, vy, 12 * s, i % 2 == 0 ? Color.FromArgb("#51D7FF") : Color.FromArgb("#9970FF"), .19f + flightPower * .55f);
            canvas.FillColor = WithAlpha(i % 2 == 0 ? Color.FromArgb("#93F4FF") : Color.FromArgb("#C5B1FF"), .30f + flightPower * .68f);
            canvas.FillEllipse(vx - 3.8f * s, vy - 2.4f * s, 7.6f * s, 4.8f * s);
        }

        // Individual perimeter lights, glassy lenses, small fasteners and navigation lamps.
        for (var i = 0; i < 17; i++)
        {
            var normalized = (i - 8) / 8f;
            var lx = cx + normalized * 128 * s;
            var ly = cy - 1.7f * s + normalized * normalized * 16 * s;
            var light = i % 4 == 0 ? Color.FromArgb("#D4B6FF") : Color.FromArgb("#79E8FF");
            var shimmer = .76f + .24f * (float)Math.Sin(IdleSeconds * 3.0 + i * .77f);
            Glow(canvas, lx, ly, 11 * s, light, (.12f + flightPower * .22f) * shimmer);
            canvas.FillColor = WithAlpha(light, (.68f + flightPower * .30f) * shimmer);
            canvas.FillEllipse(lx - 2.7f * s, ly - 2.0f * s, 5.4f * s, 4.0f * s);
            canvas.FillColor = WithAlpha(Color.FromArgb("#FFFFFF"), .62f * shimmer);
            canvas.FillEllipse(lx - .9f * s, ly - .8f * s, 1.8f * s, 1.6f * s);
        }

        for (var i = -5; i <= 5; i++)
        {
            var bx = cx + i * 20 * s;
            var by = cy - 21 * s + Math.Abs(i) * .55f * s;
            canvas.FillColor = WithAlpha(Color.FromArgb("#E4EBF4"), .46f);
            canvas.FillEllipse(bx - 1.1f * s, by, 2.2f * s, 2.2f * s);
        }

        // A restrained HIMO reactor insignia on the forward lip.
        canvas.FillColor = Color.FromArgb("#8698B1");
        canvas.FillEllipse(cx - 22 * s, cy + 8 * s, 44 * s, 19 * s);
        canvas.FillColor = Color.FromArgb("#15243A");
        canvas.FillEllipse(cx - 16 * s, cy + 10 * s, 32 * s, 14 * s);
        Glow(canvas, cx, cy + 17 * s, 23 * s, Color.FromArgb("#6BE9FF"), .22f + flightPower * .35f);
        canvas.FillColor = WithAlpha(Color.FromArgb("#B9F7FF"), .40f + flightPower * .54f);
        canvas.FillEllipse(cx - 7 * s, cy + 12 * s, 14 * s, 8 * s);

        // Directional rim reflections add polished metal read on the left and right edges.
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#F4F8FF"), .70f);
        canvas.StrokeSize = 1.1f * s;
        canvas.DrawLine(cx - 129 * s, cy - 13 * s, cx - 97 * s, cy - 23 * s);
        canvas.DrawLine(cx + 98 * s, cy - 23 * s, cx + 130 * s, cy - 12 * s);
        canvas.StrokeColor = WithAlpha(Color.FromArgb("#7DEBFF"), .52f + flightPower * .28f);
        canvas.StrokeSize = 1.3f * s;
        canvas.DrawLine(cx - 130 * s, cy + 4 * s, cx - 106 * s, cy + 10 * s);
        canvas.DrawLine(cx + 106 * s, cy + 10 * s, cx + 130 * s, cy + 4 * s);
    }

    private static void Glow(ICanvas canvas, float cx, float cy, float radius, Color color, float intensity)
    {
        if (radius <= 0 || intensity <= .001f)
            return;
        for (var layer = 8; layer >= 1; layer--)
        {
            var f = layer / 8f;
            var alpha = intensity * (1f - f) * .105f + intensity * .008f;
            canvas.FillColor = WithAlpha(color, alpha);
            var r = radius * f;
            canvas.FillEllipse(cx - r, cy - r * .63f, r * 2, r * 1.26f);
        }
    }

    private static void FillTriangle(ICanvas canvas, float x1, float y1, float x2, float y2, float x3, float y3, Color color)
    {
        var path = new PathF();
        path.MoveTo(x1, y1);
        path.LineTo(x2, y2);
        path.LineTo(x3, y3);
        path.Close();
        canvas.FillColor = color;
        canvas.FillPath(path);
    }

    private static void FillPolygon(ICanvas canvas, Color color, params float[] coordinates)
    {
        if (coordinates.Length < 6 || coordinates.Length % 2 != 0)
            return;
        var path = new PathF();
        path.MoveTo(coordinates[0], coordinates[1]);
        for (var i = 2; i < coordinates.Length; i += 2)
            path.LineTo(coordinates[i], coordinates[i + 1]);
        path.Close();
        canvas.FillColor = color;
        canvas.FillPath(path);
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.FromRgba(color.Red, color.Green, color.Blue, Math.Clamp(alpha, 0f, 1f));

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
    private static float EaseInOut(float value) => value * value * (3f - 2f * value);

    private readonly record struct Star(float X, float Y, float Size, float Alpha, float Phase, bool Bright);
    private readonly record struct Rock(float X, float Y, float Width, float Height, int Tone);
    private readonly record struct Crater(float X, float Y, float Width, float Height, float Shade);
}
