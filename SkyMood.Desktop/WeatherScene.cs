using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkyMood;

namespace SkyMood.Desktop;

/// <summary>
/// The animated sky, ported from the MAUI app's WeatherSceneDrawable to Avalonia's DrawingContext:
/// gradient background, pulsing sun (or moon + twinkling stars at night), drifting wind-reactive
/// clouds, rain, snow, fog, birds, shooting stars, a daytime rainbow, a kite, lightning, and
/// click-to-burst confetti. The window drives it via <see cref="Advance"/> + InvalidateVisual().
/// </summary>
public sealed class WeatherScene : Control
{
    public WeatherKind Kind { get; set; } = WeatherKind.Clear;
    public bool IsDay { get; set; } = true;
    public int RainIntensity { get; set; }
    public int SnowIntensity { get; set; }
    public double WindKph { get; set; }

    float _time, _flash, _ox, _oy, _tox, _toy;
    double WindFactor => 1.0 + WindKph / 40.0;

    readonly (double x, double y, double size, double phase)[] _stars;
    readonly (double x, double y, double scale, double speed, double seed)[] _clouds;
    readonly (double x, double len, double speed, double phase)[] _rain;
    readonly (double x, double y, double r, double speed, double sway, double phase)[] _snow;
    readonly (double y, double speed, double phase)[] _birds;
    readonly (double x, double y, double speed, double phase)[] _shoot;
    readonly List<Particle> _particles = new();
    readonly Random _r = new();

    static readonly Color[] Confetti =
    {
        Color.Parse("#FF5E5E"), Color.Parse("#FFB14E"), Color.Parse("#FFE14E"),
        Color.Parse("#74E07D"), Color.Parse("#5EC8FF"), Color.Parse("#9A7DFF"), Color.Parse("#FF8FD0"),
    };

    public WeatherScene()
    {
        var rnd = new Random(20260627);
        double F() => rnd.NextDouble();
        _stars = new (double, double, double, double)[80];
        for (int i = 0; i < _stars.Length; i++) _stars[i] = (F(), F() * 0.55, F() * 1.6 + 0.6, F() * 6.28);
        _clouds = new (double, double, double, double, double)[6];
        for (int i = 0; i < _clouds.Length; i++) _clouds[i] = (F(), 0.05 + F() * 0.34, 0.7 + F() * 0.9, 0.006 + F() * 0.012, F());
        _rain = new (double, double, double, double)[150];
        for (int i = 0; i < _rain.Length; i++) _rain[i] = (F(), 0.04 + F() * 0.05, 0.9 + F() * 0.8, F());
        _snow = new (double, double, double, double, double, double)[120];
        for (int i = 0; i < _snow.Length; i++) _snow[i] = (F(), F(), 1.6 + F() * 3.4, 0.04 + F() * 0.07, 0.01 + F() * 0.03, F() * 6.28);
        _birds = new (double, double, double)[4];
        for (int i = 0; i < _birds.Length; i++) _birds[i] = (0.12 + F() * 0.3, 0.006 + F() * 0.01, F());
        _shoot = new (double, double, double, double)[3];
        for (int i = 0; i < _shoot.Length; i++) _shoot[i] = (F() * 0.6, F() * 0.3, 0.05 + F() * 0.06, F());
    }

    public void SetTargetParallax(double nx, double ny) { _tox = (float)(nx * 22); _toy = (float)(ny * 22); }

    public void Spawn(double x, double y)
    {
        for (int i = 0; i < 20; i++)
        {
            double a = _r.NextDouble() * Math.PI * 2, sp = 120 + _r.NextDouble() * 260;
            _particles.Add(new Particle
            {
                X = x, Y = y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp - 130,
                Life = 0.9 + _r.NextDouble() * 0.5, Max = 1.4, Size = 4 + _r.NextDouble() * 5,
                C = Confetti[_r.Next(Confetti.Length)],
            });
        }
    }

    public void Advance(double dt)
    {
        _time += (float)dt;
        _ox += (float)((_tox - _ox) * 0.1);
        _oy += (float)((_toy - _oy) * 0.1);
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.VY += 520 * dt; p.VX *= 0.99; p.X += p.VX * dt; p.Y += p.VY * dt; p.Life -= dt;
            if (p.Life <= 0) _particles.RemoveAt(i);
        }
        if (Kind == WeatherKind.Storm)
        {
            if (_flash > 0) _flash = Math.Max(0, _flash - 0.06f);
            else if (_r.NextDouble() < 0.006) _flash = 0.9f;
        }
        else _flash = 0;
    }

    public override void Render(DrawingContext g)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var full = new Rect(0, 0, w, h);

        var (top, bot) = Gradient(Kind, IsDay);
        g.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bot, 1) },
        }, null, full);

        using (g.PushTransform(Matrix.CreateTranslation(_ox, _oy)))
        {
            bool clearish = Kind is WeatherKind.Clear or WeatherKind.FewClouds;
            if (IsDay && clearish) { DrawSun(g, w, h); DrawBirds(g, w, h); }
            if (!IsDay)
            {
                if (clearish) DrawMoon(g, w, h);
                DrawStars(g, w, h, Kind == WeatherKind.Clear ? 80 : 34);
                if (Kind == WeatherKind.Clear) DrawShoot(g, w, h);
            }
            int clouds = Kind switch
            {
                WeatherKind.FewClouds => 3, WeatherKind.Cloudy => 6,
                WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Storm => 5, _ => 0,
            };
            DrawClouds(g, w, h, clouds);
            if (RainIntensity > 0) DrawRain(g, w, h, Math.Min(_rain.Length, RainIntensity));
            if (SnowIntensity > 0) DrawSnow(g, w, h, Math.Min(_snow.Length, SnowIntensity));
            if (Kind == WeatherKind.Fog) DrawFog(g, w, h);
            if (IsDay && Kind == WeatherKind.Rain) DrawRainbow(g, w, h);
            if (IsDay && WindKph >= 22 && Kind is not WeatherKind.Storm and not WeatherKind.Fog) DrawKite(g, w, h);
        }

        if (_flash > 0.001f) g.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(_flash * 170), 255, 255, 255)), null, full);
        foreach (var p in _particles)
        {
            byte a = (byte)(Math.Clamp(p.Life / p.Max, 0, 1) * 255);
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, p.C.R, p.C.G, p.C.B)), null, new Point(p.X, p.Y), p.Size, p.Size);
        }
    }

    // ---------------- elements ----------------

    void DrawSun(DrawingContext g, double w, double h)
    {
        double cx = w * 0.5, cy = h * 0.18, r = Math.Min(w, h) * 0.12 * (1 + 0.05 * Math.Sin(_time * 1.6));
        for (int i = 4; i >= 1; i--)
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb(22, 255, 224, 92)), null, new Point(cx, cy), r * (1 + i * 0.45), r * (1 + i * 0.45));
        g.DrawEllipse(Br("#FFE16B"), null, new Point(cx, cy), r, r);
        g.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, 255, 248, 212)), null, new Point(cx, cy), r * 0.7, r * 0.7);
    }

    void DrawMoon(DrawingContext g, double w, double h)
    {
        double cx = w * 0.5, cy = h * 0.18, r = Math.Min(w, h) * 0.1;
        g.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 220, 228, 255)), null, new Point(cx, cy), r * 1.5, r * 1.5);
        g.DrawEllipse(Br("#EEF1F8"), null, new Point(cx, cy), r, r);
        g.DrawEllipse(Br("#AEB6C9"), null, new Point(cx + r * 0.5, cy - r * 0.25), r * 0.85, r * 0.85);
    }

    void DrawStars(DrawingContext g, double w, double h, int count)
    {
        for (int i = 0; i < Math.Min(count, _stars.Length); i++)
        {
            var s = _stars[i];
            byte a = (byte)((0.3 + 0.7 * Math.Abs(Math.Sin(_time * 1.4 + s.phase))) * 255);
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), null, new Point(s.x * w, s.y * h), s.size, s.size);
        }
    }

    void DrawBirds(DrawingContext g, double w, double h)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, 40, 46, 60)), 2.2);
        double s = Math.Max(5, w * 0.013);
        foreach (var b in _birds)
        {
            double x = (Frac(b.phase + _time * b.speed * WindFactor) * 1.2 - 0.1) * w;
            double y = b.y * h + Math.Sin(_time * 2 + b.phase * 6) * 4;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(x - 2 * s, y), false);
                c.QuadraticBezierTo(new Point(x - s, y - 1.2 * s), new Point(x, y));
                c.QuadraticBezierTo(new Point(x + s, y - 1.2 * s), new Point(x + 2 * s, y));
                c.EndFigure(false);
            }
            g.DrawGeometry(null, pen, geo);
        }
    }

    void DrawShoot(DrawingContext g, double w, double h)
    {
        foreach (var s in _shoot)
        {
            double prog = Frac(_time * s.speed + s.phase);
            if (prog > 0.18) continue;
            double t = prog / 0.18; byte a = (byte)((1 - t) * 255);
            double sx = s.x * w + t * w * 0.28, sy = s.y * h + t * h * 0.2, len = w * 0.06;
            var col = Color.FromArgb(a, 255, 255, 255);
            g.DrawLine(new Pen(new SolidColorBrush(col), 2.4), new Point(sx, sy), new Point(sx - len, sy - len * 0.7));
            g.DrawEllipse(new SolidColorBrush(col), null, new Point(sx, sy), 2.2, 2.2);
        }
    }

    void DrawClouds(DrawingContext g, double w, double h, int count)
    {
        bool dark = Kind is WeatherKind.Rain or WeatherKind.Storm;
        var col = dark ? Color.FromArgb(235, 196, 204, 214) : Color.FromArgb(235, 255, 255, 255);
        var br = new SolidColorBrush(col);
        for (int i = 0; i < Math.Min(count, _clouds.Length); i++)
        {
            var c = _clouds[i];
            double x = (Frac(c.x + _time * c.speed * WindFactor) * 1.4 - 0.2) * w;
            // keep clouds in the top band (this window is tall) so they don't cover the readout
            double y = c.y * h * 0.42, cw = 70 + c.scale * 110, ch = cw * 0.34;
            g.DrawRectangle(br, null, new RoundedRect(new Rect(x, y, cw, ch), ch * 0.5));
            g.DrawEllipse(br, null, new Point(x + cw * 0.35, y - ch * 0.05), cw * 0.25, ch * 0.85);
            g.DrawEllipse(br, null, new Point(x + cw * 0.68, y - ch * 0.1), cw * 0.27, ch * 1.0);
        }
    }

    void DrawRain(DrawingContext g, double w, double h, int count)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), 1.6);
        for (int i = 0; i < count; i++)
        {
            var d = _rain[i];
            double yy = Frac(d.phase + _time * d.speed), x = d.x * w, y0 = yy * 1.2 * h - 0.15 * h, len = d.len * h;
            g.DrawLine(pen, new Point(x, y0), new Point(x - w * 0.012, y0 + len));
        }
    }

    void DrawSnow(DrawingContext g, double w, double h, int count)
    {
        var br = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
        for (int i = 0; i < count; i++)
        {
            var f = _snow[i];
            double y = Frac(f.y + _time * f.speed) * 1.1 * h - 0.05 * h;
            double x = (f.x + f.sway * Math.Sin(_time * 1.3 + f.phase)) * w;
            g.DrawEllipse(br, null, new Point(x, y), f.r, f.r);
        }
    }

    void DrawFog(DrawingContext g, double w, double h)
    {
        var br = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        for (int i = 0; i < 5; i++)
        {
            double y = (0.15 + i * 0.16) * h, x = (Frac(i * 0.2 + _time * 0.03) * 1.6 - 0.3) * w;
            g.DrawRectangle(br, null, new RoundedRect(new Rect(x, y, w * 0.9, h * 0.07), h * 0.035));
        }
    }

    void DrawRainbow(DrawingContext g, double w, double h)
    {
        double cx = w * 0.5, cy = h * 0.46, r0 = Math.Min(w, h) * 0.46;
        string[] cols = { "#FF5E5E", "#FFB14E", "#FFE14E", "#74E07D", "#5EC8FF", "#9A7DFF" };
        for (int i = 0; i < cols.Length; i++)
        {
            double r = r0 - i * 7;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(cx - r, cy), false);
                c.ArcTo(new Point(cx + r, cy), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
                c.EndFigure(false);
            }
            var col = Color.Parse(cols[i]);
            g.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(128, col.R, col.G, col.B)), 6), geo);
        }
    }

    void DrawKite(DrawingContext g, double w, double h)
    {
        double sway = Math.Sin(_time * 1.1) * w * 0.04;
        double kx = w * 0.72 + sway, ky = h * 0.30 - Math.Cos(_time * 1.1) * h * 0.02, s = Math.Min(w, h) * 0.05;
        var diamond = new StreamGeometry();
        using (var c = diamond.Open())
        {
            c.BeginFigure(new Point(kx, ky - s), true);
            c.LineTo(new Point(kx + s * 0.7, ky)); c.LineTo(new Point(kx, ky + s)); c.LineTo(new Point(kx - s * 0.7, ky));
            c.EndFigure(true);
        }
        g.DrawGeometry(Br("#FF6FA5"), null, diamond);
        var tail = new StreamGeometry();
        using (var c = tail.Open())
        {
            c.BeginFigure(new Point(kx, ky + s), false);
            for (int i = 1; i <= 5; i++) c.LineTo(new Point(kx + Math.Sin(_time * 3 + i) * s * 0.35, ky + s + i * s * 0.5));
            c.EndFigure(false);
        }
        g.DrawGeometry(null, new Pen(Br("#FFD86B"), 2), tail);
    }

    // ---------------- helpers ----------------

    static (Color, Color) Gradient(WeatherKind k, bool day) => k switch
    {
        WeatherKind.Clear     => day ? (P("#2D83D6"), P("#7EC8F5")) : (P("#0B1D3A"), P("#1B3A63")),
        WeatherKind.FewClouds => day ? (P("#4A90D9"), P("#9ED0EF")) : (P("#10233F"), P("#233F63")),
        WeatherKind.Cloudy    => day ? (P("#6B7F96"), P("#A8B8C8")) : (P("#1A2330"), P("#2F3C4D")),
        WeatherKind.Fog       => day ? (P("#8896A3"), P("#C2CCD4")) : (P("#22272E"), P("#3A424C")),
        WeatherKind.Rain      => day ? (P("#465A6E"), P("#7B8FA3")) : (P("#11161F"), P("#27313D")),
        WeatherKind.Storm     => day ? (P("#2D333F"), P("#55606F")) : (P("#0A0C12"), P("#1D222C")),
        WeatherKind.Snow      => day ? (P("#7A93AB"), P("#D3E0EC")) : (P("#1E2735"), P("#3B4759")),
        _                     => day ? (P("#2D83D6"), P("#7EC8F5")) : (P("#0B1D3A"), P("#1B3A63")),
    };

    static Color P(string hex) => Color.Parse(hex);
    static SolidColorBrush Br(string hex) => new(Color.Parse(hex));
    static double Frac(double v) => v - Math.Floor(v);

    sealed class Particle { public double X, Y, VX, VY, Life, Max, Size; public Color C; }
}
