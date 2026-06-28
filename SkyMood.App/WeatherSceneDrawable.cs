using Microsoft.Maui.Graphics;

namespace SkyMood.App;

/// <summary>
/// Draws the animated sky behind the readout: a gradient background plus a pulsing sun (or moon and
/// twinkling stars at night), drifting clouds, falling rain, swaying snow, rolling fog and lightning
/// flashes — chosen to match the current <see cref="WeatherKind"/>. Playful extras mirror the web
/// edition: birds on sunny days, shooting stars at night, a daytime rainbow when it rains, a kite
/// when it's breezy, wind-reactive cloud speed, and tap-to-burst confetti. The owning page advances
/// <see cref="Time"/> on a timer and calls Invalidate() to animate.
/// </summary>
public sealed class WeatherSceneDrawable : IDrawable
{
    public WeatherKind Kind { get; set; } = WeatherKind.Clear;
    public bool IsDay { get; set; } = true;
    public float Time { get; set; }          // seconds, advanced by the page
    public float Flash { get; set; }          // 0..1 lightning overlay
    public int RainIntensity { get; set; }    // ~0..140, from the weather code
    public int SnowIntensity { get; set; }    // ~0..100
    public double WindKph { get; set; }        // drives cloud/bird drift speed + the kite
    public float OffsetX { get; set; }          // parallax offset from device tilt (accelerometer)
    public float OffsetY { get; set; }

    float WindFactor => 1f + (float)WindKph / 40f;

    readonly (float x, float y, float size, float phase)[] _stars;
    readonly (float x, float y, float scale, float speed, float seed)[] _clouds;
    readonly (float x, float len, float speed, float phase)[] _rain;
    readonly (float x, float y, float r, float speed, float sway, float phase)[] _snow;
    readonly (float y, float speed, float phase)[] _birds;
    readonly (float x, float y, float speed, float phase)[] _shoot;
    readonly List<Particle> _particles = new();
    readonly Random _r = new();

    static readonly Color[] Confetti =
    {
        Color.FromArgb("#FF5E5E"), Color.FromArgb("#FFB14E"), Color.FromArgb("#FFE14E"),
        Color.FromArgb("#74E07D"), Color.FromArgb("#5EC8FF"), Color.FromArgb("#9A7DFF"), Color.FromArgb("#FF8FD0"),
    };

    public WeatherSceneDrawable()
    {
        var rnd = new Random(20260627);
        float F() => (float)rnd.NextDouble();

        _stars = new (float, float, float, float)[80];
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = (F(), F() * 0.55f, F() * 1.6f + 0.6f, F() * 6.28f);

        _clouds = new (float, float, float, float, float)[6];
        for (int i = 0; i < _clouds.Length; i++)
            _clouds[i] = (F(), 0.05f + F() * 0.34f, 0.7f + F() * 0.9f, 0.006f + F() * 0.012f, F());

        _rain = new (float, float, float, float)[150];
        for (int i = 0; i < _rain.Length; i++)
            _rain[i] = (F(), 0.04f + F() * 0.05f, 0.9f + F() * 0.8f, F());

        _snow = new (float, float, float, float, float, float)[120];
        for (int i = 0; i < _snow.Length; i++)
            _snow[i] = (F(), F(), 1.6f + F() * 3.4f, 0.04f + F() * 0.07f, 0.01f + F() * 0.03f, F() * 6.28f);

        _birds = new (float, float, float)[4];
        for (int i = 0; i < _birds.Length; i++)
            _birds[i] = (0.12f + F() * 0.3f, 0.006f + F() * 0.01f, F());

        _shoot = new (float, float, float, float)[3];
        for (int i = 0; i < _shoot.Length; i++)
            _shoot[i] = (F() * 0.6f, F() * 0.3f, 0.05f + F() * 0.06f, F());
    }

    // ---------------- tap-burst confetti ----------------

    /// <summary>Spawn a confetti burst at a point (page coordinates). Stepped by the page timer.</summary>
    public void Spawn(float x, float y)
    {
        for (int i = 0; i < 20; i++)
        {
            float a = (float)(_r.NextDouble() * Math.PI * 2);
            float sp = 120 + (float)_r.NextDouble() * 260;
            _particles.Add(new Particle
            {
                X = x, Y = y,
                VX = MathF.Cos(a) * sp, VY = MathF.Sin(a) * sp - 130,
                Life = 0.9f + (float)_r.NextDouble() * 0.5f, Max = 1.4f,
                Size = 4 + (float)_r.NextDouble() * 5, C = Confetti[_r.Next(Confetti.Length)],
            });
        }
    }

    public void StepParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.VY += 520 * dt; p.VX *= 0.99f;
            p.X += p.VX * dt; p.Y += p.VY * dt; p.Life -= dt;
            if (p.Life <= 0) _particles.RemoveAt(i);
        }
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0) return;

        DrawSky(canvas, rect);

        // Parallax: the tilt offset is baked into each element's coordinates below (canvas.Translate
        // is a no-op on the MAUI Android GraphicsView, so we shift positions directly).
        bool clearish = Kind is WeatherKind.Clear or WeatherKind.FewClouds;
        if (IsDay && clearish) { DrawSun(canvas, w, h); DrawBirds(canvas, w, h); }
        if (!IsDay)
        {
            if (clearish) DrawMoon(canvas, w, h);
            DrawStars(canvas, w, h, Kind == WeatherKind.Clear ? 80 : 34);
            if (Kind == WeatherKind.Clear) DrawShootingStars(canvas, w, h);
        }

        int cloudCount = Kind switch
        {
            WeatherKind.FewClouds => 3,
            WeatherKind.Cloudy => 6,
            WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Storm => 5,
            _ => 0,
        };
        DrawClouds(canvas, w, h, cloudCount);

        if (RainIntensity > 0) DrawRain(canvas, w, h, Math.Min(_rain.Length, RainIntensity));
        if (SnowIntensity > 0) DrawSnow(canvas, w, h, Math.Min(_snow.Length, SnowIntensity));
        if (Kind == WeatherKind.Fog) DrawFog(canvas, w, h);

        if (IsDay && Kind == WeatherKind.Rain) DrawRainbow(canvas, w, h);          // cheery arc in daylight rain
        if (IsDay && WindKph >= 22 && Kind is not WeatherKind.Storm and not WeatherKind.Fog)
            DrawKite(canvas, w, h);                                                 // a kite when it's breezy

        if (Flash > 0.001f)                                                         // lightning: full-screen, no parallax
        {
            canvas.FillColor = Color.FromRgba(255, 255, 255, (int)(Flash * 170));
            canvas.FillRectangle(rect);
        }

        DrawParticles(canvas);                                                      // confetti on top of everything
    }

    // ---------------- sky ----------------

    void DrawSky(ICanvas canvas, RectF rect)
    {
        var (top, bottom) = Gradient(Kind, IsDay);
        var paint = new LinearGradientPaint
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new[] { new PaintGradientStop(0f, top), new PaintGradientStop(1f, bottom) },
        };
        canvas.SetFillPaint(paint, rect);
        canvas.FillRectangle(rect);
    }

    static (Color top, Color bottom) Gradient(WeatherKind kind, bool day) => kind switch
    {
        WeatherKind.Clear     => day ? (Hex("#2D83D6"), Hex("#7EC8F5")) : (Hex("#0B1D3A"), Hex("#1B3A63")),
        WeatherKind.FewClouds => day ? (Hex("#4A90D9"), Hex("#9ED0EF")) : (Hex("#10233F"), Hex("#233F63")),
        WeatherKind.Cloudy    => day ? (Hex("#6B7F96"), Hex("#A8B8C8")) : (Hex("#1A2330"), Hex("#2F3C4D")),
        WeatherKind.Fog       => day ? (Hex("#8896A3"), Hex("#C2CCD4")) : (Hex("#22272E"), Hex("#3A424C")),
        WeatherKind.Rain      => day ? (Hex("#465A6E"), Hex("#7B8FA3")) : (Hex("#11161F"), Hex("#27313D")),
        WeatherKind.Storm     => day ? (Hex("#2D333F"), Hex("#55606F")) : (Hex("#0A0C12"), Hex("#1D222C")),
        WeatherKind.Snow      => day ? (Hex("#7A93AB"), Hex("#D3E0EC")) : (Hex("#1E2735"), Hex("#3B4759")),
        _                     => day ? (Hex("#2D83D6"), Hex("#7EC8F5")) : (Hex("#0B1D3A"), Hex("#1B3A63")),
    };

    // ---------------- sky bodies ----------------

    void DrawSun(ICanvas canvas, float w, float h)
    {
        float cx = w * 0.5f + OffsetX, cy = h * 0.18f + OffsetY;
        float r = MathF.Min(w, h) * 0.12f * (1f + 0.05f * MathF.Sin(Time * 1.6f));
        for (int i = 4; i >= 1; i--)
        {
            canvas.FillColor = Color.FromRgba(255, 224, 92, 22);
            Circle(canvas, cx, cy, r * (1f + i * 0.45f));
        }
        canvas.FillColor = Hex("#FFE16B");
        Circle(canvas, cx, cy, r);
        canvas.FillColor = Color.FromRgba(255, 248, 212, 230);
        Circle(canvas, cx, cy, r * 0.7f);
    }

    void DrawMoon(ICanvas canvas, float w, float h)
    {
        float cx = w * 0.5f + OffsetX, cy = h * 0.18f + OffsetY, r = MathF.Min(w, h) * 0.1f;
        canvas.FillColor = Color.FromRgba(220, 228, 255, 60);
        Circle(canvas, cx, cy, r * 1.5f);
        canvas.FillColor = Hex("#EEF1F8");
        Circle(canvas, cx, cy, r);
        canvas.FillColor = Hex("#AEB6C9");
        Circle(canvas, cx + r * 0.5f, cy - r * 0.25f, r * 0.85f);
    }

    void DrawStars(ICanvas canvas, float w, float h, int count)
    {
        count = Math.Min(count, _stars.Length);
        for (int i = 0; i < count; i++)
        {
            var s = _stars[i];
            float a = 0.3f + 0.7f * MathF.Abs(MathF.Sin(Time * 1.4f + s.phase));
            canvas.FillColor = Color.FromRgba(1f, 1f, 1f, a);
            Circle(canvas, s.x * w + OffsetX, s.y * h + OffsetY, s.size);
        }
    }

    void DrawBirds(ICanvas canvas, float w, float h)
    {
        canvas.StrokeColor = Color.FromRgba(40, 46, 60, 170);
        canvas.StrokeSize = 2.2f;
        float s = MathF.Max(5f, w * 0.013f);
        for (int i = 0; i < _birds.Length; i++)
        {
            var b = _birds[i];
            float x = (Frac(b.phase + Time * b.speed * WindFactor) * 1.2f - 0.1f) * w + OffsetX;
            float y = b.y * h + MathF.Sin(Time * 2f + b.phase * 6f) * 4f + OffsetY;
            var p = new PathF();
            p.MoveTo(x - 2 * s, y);
            p.QuadTo(x - s, y - 1.2f * s, x, y);
            p.QuadTo(x + s, y - 1.2f * s, x + 2 * s, y);
            canvas.DrawPath(p);
        }
    }

    void DrawShootingStars(ICanvas canvas, float w, float h)
    {
        for (int i = 0; i < _shoot.Length; i++)
        {
            var s = _shoot[i];
            float prog = Frac(Time * s.speed + s.phase);
            if (prog > 0.18f) continue;                 // brief streak, long gap
            float t = prog / 0.18f, fade = 1f - t;
            float sx = s.x * w + t * w * 0.28f + OffsetX;
            float sy = s.y * h + t * h * 0.2f + OffsetY;
            float len = w * 0.06f;
            canvas.StrokeSize = 2.4f;
            canvas.StrokeColor = Color.FromRgba(1f, 1f, 1f, fade);
            canvas.DrawLine(sx, sy, sx - len, sy - len * 0.7f);
            canvas.FillColor = Color.FromRgba(1f, 1f, 1f, fade);
            Circle(canvas, sx, sy, 2.2f);
        }
    }

    // ---------------- clouds ----------------

    void DrawClouds(ICanvas canvas, float w, float h, int count)
    {
        count = Math.Min(count, _clouds.Length);
        bool dark = Kind is WeatherKind.Rain or WeatherKind.Storm;
        var col = dark ? Color.FromRgba(196, 204, 214, 235) : Color.FromRgba(255, 255, 255, 235);
        for (int i = 0; i < count; i++)
        {
            var c = _clouds[i];
            float x = (Frac(c.x + Time * c.speed * WindFactor) * 1.4f - 0.2f) * w + OffsetX;
            float y = c.y * h + OffsetY;
            float cw = (90f + c.scale * 150f);
            DrawCloud(canvas, x, y, cw, col);
        }
    }

    static void DrawCloud(ICanvas canvas, float x, float y, float cw, Color col)
    {
        float ch = cw * 0.34f;
        canvas.FillColor = col;
        canvas.FillRoundedRectangle(x, y, cw, ch, ch * 0.5f);
        canvas.FillEllipse(x + cw * 0.10f, y - ch * 0.55f, cw * 0.5f, ch * 1.3f);
        canvas.FillEllipse(x + cw * 0.42f, y - ch * 0.75f, cw * 0.52f, ch * 1.5f);
    }

    // ---------------- precipitation + extras ----------------

    void DrawRain(ICanvas canvas, float w, float h, int count)
    {
        canvas.StrokeColor = Color.FromRgba(255, 255, 255, 130);
        canvas.StrokeSize = 1.6f;
        for (int i = 0; i < count; i++)
        {
            var d = _rain[i];
            float yy = Frac(d.phase + Time * d.speed);
            float x = d.x * w;
            float y0 = yy * 1.2f * h - 0.15f * h;
            float len = d.len * h;
            canvas.DrawLine(x, y0, x - w * 0.012f, y0 + len);
        }
    }

    void DrawSnow(ICanvas canvas, float w, float h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var f = _snow[i];
            float y = Frac(f.y + Time * f.speed) * 1.1f * h - 0.05f * h;
            float x = (f.x + f.sway * MathF.Sin(Time * 1.3f + f.phase)) * w;
            canvas.FillColor = Color.FromRgba(1f, 1f, 1f, 0.85f);
            Circle(canvas, x, y, f.r);
        }
    }

    void DrawFog(ICanvas canvas, float w, float h)
    {
        for (int i = 0; i < 5; i++)
        {
            float y = (0.15f + i * 0.16f) * h;
            float x = (Frac(i * 0.2f + Time * 0.03f) * 1.6f - 0.3f) * w;
            canvas.FillColor = Color.FromRgba(255, 255, 255, 38);
            canvas.FillRoundedRectangle(x, y, w * 0.9f, h * 0.07f, h * 0.035f);
        }
    }

    void DrawRainbow(ICanvas canvas, float w, float h)
    {
        float cx = w * 0.5f + OffsetX, cy = h * 0.46f + OffsetY, r0 = MathF.Min(w, h) * 0.46f;
        Color[] cols = { Hex("#FF5E5E"), Hex("#FFB14E"), Hex("#FFE14E"), Hex("#74E07D"), Hex("#5EC8FF"), Hex("#9A7DFF") };
        canvas.StrokeSize = 6;
        for (int i = 0; i < cols.Length; i++)
        {
            float r = r0 - i * 7;
            canvas.StrokeColor = cols[i].WithAlpha(0.5f);
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, 0, 180, false, false); // top arch
        }
    }

    void DrawKite(ICanvas canvas, float w, float h)
    {
        float sway = MathF.Sin(Time * 1.1f) * w * 0.04f;
        float kx = w * 0.72f + sway + OffsetX, ky = h * 0.30f - MathF.Cos(Time * 1.1f) * h * 0.02f + OffsetY;
        float s = MathF.Min(w, h) * 0.05f;

        var p = new PathF();
        p.MoveTo(kx, ky - s); p.LineTo(kx + s * 0.7f, ky); p.LineTo(kx, ky + s); p.LineTo(kx - s * 0.7f, ky); p.Close();
        canvas.FillColor = Hex("#FF6FA5"); canvas.FillPath(p);
        canvas.StrokeColor = Color.FromRgba(1f, 1f, 1f, 0.6f); canvas.StrokeSize = 1;
        canvas.DrawLine(kx, ky - s, kx, ky + s); canvas.DrawLine(kx - s * 0.7f, ky, kx + s * 0.7f, ky);

        canvas.StrokeColor = Hex("#FFD86B"); canvas.StrokeSize = 2;
        var t = new PathF(); t.MoveTo(kx, ky + s);
        for (int i = 1; i <= 5; i++)
            t.LineTo(kx + MathF.Sin(Time * 3f + i) * s * 0.35f, ky + s + i * s * 0.5f);
        canvas.DrawPath(t);
    }

    void DrawParticles(ICanvas canvas)
    {
        foreach (var p in _particles)
        {
            float al = Math.Clamp(p.Life / p.Max, 0f, 1f);
            canvas.FillColor = p.C.WithAlpha(al);
            Circle(canvas, p.X, p.Y, p.Size);
        }
    }

    // ---------------- helpers ----------------

    static void Circle(ICanvas canvas, float cx, float cy, float r)
        => canvas.FillEllipse(cx - r, cy - r, r * 2, r * 2);

    static float Frac(float v) => v - MathF.Floor(v);
    static Color Hex(string hex) => Color.FromArgb(hex);

    sealed class Particle
    {
        public float X, Y, VX, VY, Life, Max, Size;
        public Color C = Colors.White;
    }
}
