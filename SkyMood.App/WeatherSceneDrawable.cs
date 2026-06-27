using Microsoft.Maui.Graphics;

namespace SkyMood.App;

/// <summary>
/// Draws the animated sky behind the readout: a gradient background plus a pulsing sun (or moon and
/// twinkling stars at night), drifting clouds, falling rain, swaying snow, rolling fog and lightning
/// flashes — chosen to match the current <see cref="WeatherKind"/>. The owning page advances
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

    readonly (float x, float y, float size, float phase)[] _stars;
    readonly (float x, float y, float scale, float speed, float seed)[] _clouds;
    readonly (float x, float len, float speed, float phase)[] _rain;
    readonly (float x, float y, float r, float speed, float sway, float phase)[] _snow;

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
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0) return;

        DrawSky(canvas, rect);

        bool clearish = Kind is WeatherKind.Clear or WeatherKind.FewClouds;
        if (IsDay && clearish) DrawSun(canvas, w, h);
        if (!IsDay)
        {
            if (clearish) DrawMoon(canvas, w, h);
            DrawStars(canvas, w, h, Kind == WeatherKind.Clear ? 80 : 34);
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

        if (Flash > 0.001f)
        {
            canvas.FillColor = Color.FromRgba(255, 255, 255, (int)(Flash * 170));
            canvas.FillRectangle(rect);
        }
    }

    // ---------------- sky ----------------

    void DrawSky(ICanvas canvas, RectF rect)
    {
        var (top, bottom) = Gradient(Kind, IsDay);
        var paint = new LinearGradientPaint
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new[]
            {
                new PaintGradientStop(0f, top),
                new PaintGradientStop(1f, bottom),
            },
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
        float cx = w * 0.5f, cy = h * 0.18f;
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
        float cx = w * 0.5f, cy = h * 0.18f, r = MathF.Min(w, h) * 0.1f;
        canvas.FillColor = Color.FromRgba(220, 228, 255, 60);
        Circle(canvas, cx, cy, r * 1.5f);
        canvas.FillColor = Hex("#EEF1F8");
        Circle(canvas, cx, cy, r);
        canvas.FillColor = Hex("#AEB6C9");           // crescent shadow
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
            Circle(canvas, s.x * w, s.y * h, s.size);
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
            float x = (Frac(c.x + Time * c.speed) * 1.4f - 0.2f) * w;
            float y = c.y * h;
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

    // ---------------- precipitation ----------------

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

    // ---------------- helpers ----------------

    static void Circle(ICanvas canvas, float cx, float cy, float r)
        => canvas.FillEllipse(cx - r, cy - r, r * 2, r * 2);

    static float Frac(float v) => v - MathF.Floor(v);
    static Color Hex(string hex) => Color.FromArgb(hex);
}
