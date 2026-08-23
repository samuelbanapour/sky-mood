using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using SkyMood;
// Mapsui.Styles, NetTopologySuite.Geometries, and Microsoft.Maui.Graphics all define types named
// Color/Brush/Font/Point — aliasing every one explicitly (no bare "using Microsoft.Maui.Graphics;"
// or "using NetTopologySuite.Geometries;"/"using Mapsui.Styles;" name left unqualified) since a
// bare reference to any of these four names would otherwise be a compile-time ambiguous-reference
// error with more than one of these namespaces in scope.
using MsColor = Mapsui.Styles.Color;
using MsBrush = Mapsui.Styles.Brush;
using MsFont = Mapsui.Styles.Font;
using NtsPoint = NetTopologySuite.Geometries.Point;
using MauiColor = Microsoft.Maui.Graphics.Color;
using MauiPoint = Microsoft.Maui.Graphics.Point;

namespace SkyMood.App;

/// <summary>
/// Native satellite/weather map — a from-scratch C# port of the web edition's map (Leaflet +
/// RainViewer + Open-Meteo), using Mapsui instead of a WebView so this is a genuinely native
/// screen rather than an embedded web page. Same free, keyless data sources throughout:
/// Esri World Imagery (satellite), CARTO Voyager (standard), RainViewer (real radar, past only —
/// radar has no future data), and Open-Meteo (grid-sampled forecast heatmap, past+future, for
/// everything else including Precipitation's forward timeline).
/// </summary>
public partial class MapPage : ContentPage
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    const string UserAgent = "SkyMoodApp/1.0 (+https://soloappsstudio.com)";
    const int GridN = 7; // 7x7 sample grid per layer, one batched Open-Meteo request

    sealed record LayerDef(
        string Label, string Icon, string? Variable, bool IsAqi,
        (byte R, byte G, byte B)[] GradientLowToHigh, string[] LegendLowToHigh,
        double HeatMax, double HeatMin = 0);

    static readonly (string Key, LayerDef Def)[] Layers =
    [
        ("precipitation", new("Precipitation", "☔", "precipitation", false,
            [(0x88,0xdd,0xee),(0x00,0x91,0xca),(0xff,0xaa,0x00),(0xc1,0x00,0x00)],
            ["Light","Moderate","Heavy","Extreme"], 8)),
        ("temperature", new("Temperature", "🌡️", "temperature_2m", false,
            [(0x32,0x88,0xbd),(0xab,0xdd,0xa4),(0xfd,0xae,0x61),(0xd5,0x3e,0x4f)],
            ["Cold","Mild","Warm","Hot"], 40, -10)),
        ("wind", new("Wind", "💨", "wind_speed_10m", false,
            [(0xe0,0xf3,0xf8),(0xab,0xd9,0xe9),(0x74,0xad,0xd1),(0x45,0x75,0xb4)],
            ["Calm","Breezy","Windy","Strong"], 60)),
        ("airquality", new("Air Quality", "🍃", "us_aqi", true,
            [(0x00,0xe4,0x00),(0xff,0xff,0x00),(0xff,0x7e,0x00),(0xff,0x00,0x00)],
            ["Good","Moderate","Sensitive","Unhealthy"], 150)),
    ];

    abstract record MapFrame(long Time);
    sealed record RadarFrame(long Time, string Host, string Path) : MapFrame(Time);
    sealed record HeatFrame(long Time, (double Lat, double Lon, double? Value)[] Points) : MapFrame(Time);

    readonly GeoLocation _loc;
    readonly string _tempDisplay;
    readonly string _emoji;
    TileLayer? _satLayer, _stdLayer, _overlayLayer;
    MemoryLayer? _pinLayer;
    string _layerKey = "precipitation";
    List<MapFrame> _frames = [];
    int _nowIdx = -1;
    IDispatcherTimer? _playTimer;
    bool _scrubbing; // guards against ScrubSlider's programmatic updates re-triggering ShowFrame

    public MapPage(GeoLocation loc, string tempDisplay, string emoji)
    {
        InitializeComponent();
        _loc = loc;
        _tempDisplay = tempDisplay;
        _emoji = emoji;
        InitMap();
        BuildLayerMenu();

        CloseBtn.Clicked += async (_, _) => { StopPlay(); await Navigation.PopModalAsync(); };
        SatBtn.Clicked += (_, _) => SetBasemap(true);
        StdBtn.Clicked += (_, _) => SetBasemap(false);
        LayerBtn.Clicked += (_, _) => LayerMenu.IsVisible = !LayerMenu.IsVisible;
        PlayBtn.Clicked += (_, _) => { if (_playTimer is not null) StopPlay(); else StartPlay(); };
        ScrubSlider.ValueChanged += (_, e) => { if (!_scrubbing) ShowFrame((int)Math.Round(e.NewValue)); };

        _ = SetActiveLayer("precipitation");
    }

    // ---------------- map setup ----------------

    void InitMap()
    {
        var (x, y) = SphericalMercator.FromLonLat(_loc.Longitude, _loc.Latitude);
        Map.Map.Navigator.CenterOn(new MPoint(x, y));
        Map.Map.Navigator.ZoomTo(ResolutionForZoom(10));

        _satLayer = new TileLayer(new HttpTileSource(new GlobalSphericalMercator(),
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            name: "Satellite", attribution: new BruTile.Attribution("Tiles © Esri"),
            configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", UserAgent)))
        { Name = "Satellite" };

        _stdLayer = new TileLayer(new HttpTileSource(new GlobalSphericalMercator(),
            "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
            serverNodes: ["a", "b", "c", "d"], name: "Standard",
            attribution: new BruTile.Attribution("© OpenStreetMap © CARTO"),
            configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", UserAgent)))
        { Name = "Standard", Enabled = false };

        Map.Map.Layers.Add(_satLayer);
        Map.Map.Layers.Add(_stdLayer);

        _pinLayer = new MemoryLayer { Name = "Pin", Features = [] };
        Map.Map.Layers.Add(_pinLayer);
        UpdatePin(null);
    }

    // Resolution (map units per pixel) at a given standard web-mercator zoom level — the formula
    // every XYZ tile pyramid is built on, used here instead of depending on Navigator.Resolutions
    // being pre-populated from a specific layer.
    static double ResolutionForZoom(int zoom) => 156543.03392804097 / Math.Pow(2, zoom);

    void SetBasemap(bool satellite)
    {
        if (_satLayer is null || _stdLayer is null) return;
        _satLayer.Enabled = satellite;
        _stdLayer.Enabled = !satellite;
        var glass = (MauiColor)Application.Current!.Resources["Glass"];
        SatBtn.BackgroundColor = satellite ? glass : Microsoft.Maui.Graphics.Colors.Transparent;
        SatBtn.Opacity = satellite ? 1 : 0.7;
        StdBtn.BackgroundColor = satellite ? Microsoft.Maui.Graphics.Colors.Transparent : glass;
        StdBtn.Opacity = satellite ? 0.7 : 1;
    }

    // ---------------- pin (temp + condition; AQI value appended when that layer is active) ----------------

    void UpdatePin(string? aqiSuffix)
    {
        if (_pinLayer is null) return;
        var (x, y) = SphericalMercator.FromLonLat(_loc.Longitude, _loc.Latitude);
        string text = $"{_tempDisplay} {_emoji}" + (aqiSuffix is not null ? $"\n{aqiSuffix}" : "");
        var feature = new GeometryFeature { Geometry = new NtsPoint(x, y) };
        feature.Styles.Add(new LabelStyle
        {
            Text = text,
            Font = new MsFont { Bold = true, Size = 15 },
            ForeColor = MsColor.White,
            BackColor = new MsBrush(new MsColor(0x2d, 0x83, 0xd6)),
            BorderColor = MsColor.White,
            BorderThickness = 3,
            CornerRounding = 24,
            Offset = new Offset(0, -20),
        });
        _pinLayer.Features = [feature];
        _pinLayer.DataHasChanged();
    }

    // ---------------- layer picker ----------------

    void BuildLayerMenu()
    {
        LayerMenuItems.Children.Clear();
        foreach (var (key, def) in Layers)
        {
            var btn = new Button
            {
                Text = $"{def.Icon}  {def.Label}",
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent,
                TextColor = Microsoft.Maui.Graphics.Colors.White,
                Padding = new Thickness(10, 8),
            };
            btn.Clicked += (_, _) => { LayerMenu.IsVisible = false; _ = SetActiveLayer(key); };
            LayerMenuItems.Children.Add(btn);
        }
    }

    async Task SetActiveLayer(string key)
    {
        var def = Layers.First(l => l.Key == key).Def;
        _layerKey = key;
        LayerBtn.Text = $"{def.Icon}  {def.Label}";
        StopPlay();
        // Cleared here too (not just in ShowFrame) so a failed fetch below doesn't leave the
        // previous layer's overlay stuck on the map with no ShowFrame call left to clear it.
        if (_overlayLayer is not null) { Map.Map.Layers.Remove(_overlayLayer); _overlayLayer = null; }
        if (_heatLayer is not null) { Map.Map.Layers.Remove(_heatLayer); _heatLayer = null; }
        UpdatePin(null);
        RenderLegend(def);
        _frames = [];
        ScrubSlider.Maximum = 1;

        try
        {
            _frames = key == "precipitation" ? await LoadPrecipitationFrames() : await LoadHeatFrames(def);
        }
        catch
        {
            LegendNote.IsVisible = true;
            LegendNote.Text = "Couldn't load data right now — try again in a moment.";
            return;
        }

        if (_frames.Count == 0) return;
        ScrubSlider.Maximum = _frames.Count - 1;
        TickStart.Text = FormatTime(_frames[0].Time);
        TickEnd.Text = FormatTime(_frames[^1].Time);
        ShowFrame(_nowIdx >= 0 ? _nowIdx : 0);
    }

    void RenderLegend(LayerDef def)
    {
        LegendTitle.Text = def.Label;
        LegendNote.IsVisible = false;
        var (r0, g0, b0) = def.GradientLowToHigh[0];
        var (r1, g1, b1) = def.GradientLowToHigh[^1];
        // Classic collection-initializer syntax (not a C# 12 collection expression) — safer bet
        // for GradientStopCollection, which I couldn't confirm supports the newer [...] syntax.
        var gradientStops = new GradientStopCollection
        {
            new GradientStop(MauiColor.FromRgb((int)r1, (int)g1, (int)b1), 0.0f),
            new GradientStop(MauiColor.FromRgb((int)r0, (int)g0, (int)b0), 1.0f),
        };
        LegendGrad.Background = new LinearGradientBrush(gradientStops, new MauiPoint(0, 0), new MauiPoint(0, 1));
        LegendLabels.Children.Clear();
        var inkSoft = (MauiColor)Application.Current!.Resources["InkSoft"];
        foreach (var label in def.LegendLowToHigh.Reverse())
            LegendLabels.Children.Add(new Label { Text = label, FontSize = 11, TextColor = inkSoft });
    }

    // ---------------- Precipitation: real radar (past) + Open-Meteo heatmap (future) ----------------

    async Task<List<MapFrame>> LoadPrecipitationFrames()
    {
        var radar = new List<MapFrame>();
        try
        {
            var json = await Http.GetStringAsync("https://api.rainviewer.com/public/weather-maps.json");
            using var doc = JsonDocument.Parse(json);
            var host = doc.RootElement.GetProperty("host").GetString() ?? "";
            if (doc.RootElement.TryGetProperty("radar", out var radarEl) &&
                radarEl.TryGetProperty("past", out var pastEl))
            {
                foreach (var f in pastEl.EnumerateArray())
                    radar.Add(new RadarFrame(f.GetProperty("time").GetInt64(), host, f.GetProperty("path").GetString() ?? ""));
            }
        }
        catch { /* radar is best-effort — Open-Meteo forecast still covers the future */ }

        _nowIdx = Math.Max(0, radar.Count - 1);
        long lastRadarTime = radar.Count > 0 ? ((RadarFrame)radar[^1]).Time : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var def = Layers.First(l => l.Key == "precipitation").Def;
        var heat = (await FetchGrid(def)).Where(f => f.Time > lastRadarTime);

        return [.. radar, .. heat];
    }

    // ---------------- Temperature / Wind / Air Quality: past+future Open-Meteo heatmap ----------------

    async Task<List<MapFrame>> LoadHeatFrames(LayerDef def)
    {
        var frames = await FetchGrid(def);
        if (frames.Count == 0) return [];
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _nowIdx = 0;
        long best = long.MaxValue;
        for (int i = 0; i < frames.Count; i++)
        {
            long diff = Math.Abs(frames[i].Time - now);
            if (diff < best) { best = diff; _nowIdx = i; }
        }
        return [.. frames];
    }

    // ---------------- shared grid sampling (Open-Meteo, free/keyless) ----------------

    async Task<List<HeatFrame>> FetchGrid(LayerDef def)
    {
        var points = GridPoints(GridN);
        string lats = string.Join(',', points.Select(p => p.lat.ToString("F3", CultureInfo.InvariantCulture)));
        string lons = string.Join(',', points.Select(p => p.lon.ToString("F3", CultureInfo.InvariantCulture)));
        string baseUrl = def.IsAqi
            ? "https://air-quality-api.open-meteo.com/v1/air-quality"
            : "https://api.open-meteo.com/v1/forecast";
        string url = $"{baseUrl}?latitude={lats}&longitude={lons}&hourly={def.Variable}&past_hours=2&forecast_hours=24";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());

        // A single-point request returns a bare object; our batched multi-point request always
        // returns an array in the same order the lat/lon lists were sent.
        var root = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToArray()
            : [doc.RootElement];

        var times = root[0].GetProperty("hourly").GetProperty("time").EnumerateArray()
            .Select(t => DateTimeOffset.Parse(t.GetString() + "Z", CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds())
            .ToArray();

        var perPoint = root.Select(loc =>
            loc.GetProperty("hourly").GetProperty(def.Variable!).EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Null ? (double?)null : v.GetDouble())
                .ToArray()
        ).ToArray();

        var frames = new List<HeatFrame>(times.Length);
        for (int ti = 0; ti < times.Length; ti++)
        {
            var pts = new (double, double, double?)[points.Count];
            for (int pi = 0; pi < points.Count; pi++)
                pts[pi] = (points[pi].lat, points[pi].lon, ti < perPoint[pi].Length ? perPoint[pi][ti] : null);
            frames.Add(new HeatFrame(times[ti], pts));
        }
        return frames;
    }

    List<(double lat, double lon)> GridPoints(int n)
    {
        var extent = Map.Map.Navigator.Viewport.ToExtent();
        var (lonMin, latMin) = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
        var (lonMax, latMax) = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);
        var pts = new List<(double, double)>(n * n);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                pts.Add((latMin + (latMax - latMin) * (i + 0.5) / n, lonMin + (lonMax - lonMin) * (j + 0.5) / n));
        return pts;
    }

    // ---------------- rendering + scrubber ----------------

    void ShowFrame(int i)
    {
        if (_frames.Count == 0) return;
        i = Math.Clamp(i, 0, _frames.Count - 1);
        var frame = _frames[i];

        if (_overlayLayer is not null) { Map.Map.Layers.Remove(_overlayLayer); _overlayLayer = null; }
        if (_heatLayer is not null) { Map.Map.Layers.Remove(_heatLayer); _heatLayer = null; }

        if (frame is RadarFrame radar)
        {
            _overlayLayer = new TileLayer(new HttpTileSource(new GlobalSphericalMercator(),
                $"{radar.Host}{radar.Path}/256/{{z}}/{{x}}/{{y}}/2/1_1.png",
                name: "Radar", attribution: new BruTile.Attribution("Radar © RainViewer"),
                configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", UserAgent)))
            { Name = "Overlay", Opacity = 0.65 };
            Map.Map.Layers.Add(_overlayLayer);
        }
        else if (frame is HeatFrame heat)
        {
            var def = Layers.First(l => l.Key == _layerKey).Def;
            var features = new List<IFeature>(heat.Points.Length);
            foreach (var (lat, lon, value) in heat.Points)
            {
                if (value is null) continue;
                double t = Math.Clamp((value.Value - def.HeatMin) / (def.HeatMax - def.HeatMin), 0, 1);
                var (r, g, b) = LerpGradient(def.GradientLowToHigh, t);
                var (x, y) = SphericalMercator.FromLonLat(lon, lat);
                var f = new GeometryFeature { Geometry = new NtsPoint(x, y) };
                f.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Ellipse,
                    SymbolScale = 6.5,
                    Fill = new MsBrush(new MsColor(r, g, b)),
                    Opacity = (float)(0.15 + t * 0.45),
                });
                features.Add(f);
            }
            var heatLayer = new MemoryLayer { Name = "Overlay", Features = features };
            Map.Map.Layers.Insert(Map.Map.Layers.Count - 1, heatLayer); // stay under the pin
            _heatLayer = heatLayer;

            if (_layerKey == "airquality")
            {
                var (plat, plon) = (_loc.Latitude, _loc.Longitude);
                var nearest = heat.Points.Where(p => p.Item3 is not null)
                    .OrderBy(p => Math.Pow(p.Item1 - plat, 2) + Math.Pow(p.Item2 - plon, 2))
                    .FirstOrDefault();
                UpdatePin(nearest.Item3 is { } v ? $"AQI {Math.Round(v)}" : null);
            }
        }

        _scrubbing = true;
        ScrubSlider.Value = i;
        _scrubbing = false;

        string label = FormatTime(frame.Time);
        string rel = i < _nowIdx ? " · past" : i > _nowIdx ? " · forecast" : "";
        var name = Layers.First(l => l.Key == _layerKey).Def.Label;
        ScrubWhen.Text = (i == _nowIdx ? $"{name} · Now" : $"{name} · {label}") + rel;
        PlayBtn.Text = _playTimer is not null ? "⏸" : "▶";
    }

    MemoryLayer? _heatLayer;

    static (byte, byte, byte) LerpGradient((byte R, byte G, byte B)[] stops, double t)
    {
        double scaled = t * (stops.Length - 1);
        int i = Math.Clamp((int)scaled, 0, stops.Length - 2);
        double f = scaled - i;
        var a = stops[i]; var b = stops[i + 1];
        return ((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
    }

    static string FormatTime(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture);

    void StartPlay()
    {
        if (_frames.Count == 0) return;
        PlayBtn.Text = "⏸";
        _playTimer = Dispatcher.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(700);
        _playTimer.Tick += (_, _) =>
        {
            int next = (int)ScrubSlider.Value + 1;
            ShowFrame(next >= _frames.Count ? 0 : next);
        };
        _playTimer.Start();
    }

    void StopPlay()
    {
        _playTimer?.Stop();
        _playTimer = null;
        PlayBtn.Text = "▶";
    }
}
