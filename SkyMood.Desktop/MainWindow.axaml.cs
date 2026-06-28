using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SkyMood;

namespace SkyMood.Desktop;

public partial class MainWindow : Window
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    readonly GeocodingService _geocoder;
    readonly WeatherService _weather;
    readonly string _settingsPath;

    GeoLocation _location;
    WeatherResult? _current;
    bool _fahrenheit;
    bool _celebrate;
    DispatcherTimer? _timer;
    DispatcherTimer? _refresh;
    readonly Random _rnd = new();

    public MainWindow()
    {
        InitializeComponent();

        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyMood");
        Directory.CreateDirectory(baseDir);
        var inbox = Path.Combine(baseDir, "groundstation");
        Directory.CreateDirectory(inbox);
        _settingsPath = Path.Combine(baseDir, "settings.json");

        _geocoder = new GeocodingService(_http);
        _weather = WeatherService.CreateDefault(Path.Combine(baseDir, "cache"), inbox, _http);

        var s = LoadSettings();
        _fahrenheit = s.UnitF;
        _location = s.Loc ?? GeocodingService.Default;
        UnitBtn.Content = _fahrenheit ? "°F" : "°C";

        SearchBtn.Click += async (_, _) => await DoSearch();
        SearchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await DoSearch(); };
        LocateBtn.Click += async (_, _) => await UseMyLocation();
        RefreshBtn.Click += async (_, _) => await Refresh();
        UnitBtn.Click += (_, _) => ToggleUnit();
        Scene.PointerPressed += (_, e) => { var p = e.GetPosition(Scene); Scene.Spawn(p.X, p.Y); };
        PointerMoved += (_, e) => { var p = e.GetPosition(this); Scene.SetTargetParallax(p.X / Math.Max(1, Bounds.Width) - 0.5, p.Y / Math.Max(1, Bounds.Height) - 0.5); };

        Opened += async (_, _) =>
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => { Scene.Advance(0.033); Scene.InvalidateVisual(); };
            _timer.Start();
            BuildPlaces();
            await Refresh();
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _refresh.Tick += async (_, _) => await Refresh();
            _refresh.Start();
        };
    }

    // ---------------- data flow ----------------

    async Task Refresh()
    {
        try
        {
            var r = await _weather.GetAsync(_location);
            _current = r;
            RenderResult(r);
            BuildPlaces();
            if (_celebrate) { _celebrate = false; Celebrate(); }
        }
        catch (WeatherUnavailableException ex) { NoteText.Text = ex.Message; SetBadge(DataChannel.Cache, "No data yet"); }
        catch { NoteText.Text = "Couldn't update right now."; }
    }

    void RenderResult(WeatherResult r)
    {
        var v = r.Reading.Visual;
        EmojiText.Text = NightAwareEmoji(v, r.Reading.IsDay);
        TempText.Text = Temp(r.Reading.TempC);
        DescText.Text = r.Reading.IsDay ? v.Label : $"{v.Label} · night";
        QuipText.Text = QuipFor(v.Kind, r.Reading.TempC, r.Reading.IsDay);
        PlaceText.Text = $"{r.Location.Display}  ·  {r.Reading.ObservedAt.ToLocalTime():t}";
        FeelsText.Text = Temp(r.Reading.FeelsLikeC);
        HumText.Text = $"{r.Reading.Humidity}%";
        WindText.Text = $"{Math.Round(r.Reading.WindKph)} km/h";
        HiLoText.Text = $"{Temp(r.Reading.HighC)} / {Temp(r.Reading.LowC)}";
        SetBadge(r.Channel, r.Note);

        Scene.Kind = v.Kind;
        Scene.IsDay = r.Reading.IsDay;
        Scene.RainIntensity = v.RainIntensity;
        Scene.SnowIntensity = v.SnowIntensity;
        Scene.WindKph = r.Reading.WindKph;
    }

    void SetBadge(DataChannel ch, string note)
    {
        var (text, bg, fg) = ch switch
        {
            DataChannel.Live => ("LIVE", "#3FB36B", "#06210F"),
            DataChannel.Satellite => ("SATELLITE", "#36C5D8", "#04222A"),
            _ => ("OFFLINE", "#E0A33A", "#2A1C04"),
        };
        BadgeText.Text = text;
        BadgeText.Foreground = new SolidColorBrush(Color.Parse(fg));
        BadgeBorder.Background = new SolidColorBrush(Color.Parse(bg));
        NoteText.Text = note;
    }

    // ---------------- search & location ----------------

    async Task DoSearch()
    {
        var q = SearchBox.Text ?? "";
        if (q.Trim().Length == 0) return;
        var matches = await _geocoder.SearchAsync(q);
        ResultsPanel.Children.Clear();
        foreach (var loc in matches)
        {
            var b = new Button
            {
                Content = loc.Display, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
                Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(14, 10), FontSize = 15,
            };
            b.Click += async (_, _) =>
            {
                ResultsCard.IsVisible = false; SearchBox.Text = ""; _celebrate = true;
                await SetLocation(loc);
            };
            ResultsPanel.Children.Add(b);
        }
        ResultsCard.IsVisible = matches.Count > 0;
        if (matches.Count == 0) NoteText.Text = $"No places found for “{q}”.";
    }

    async Task UseMyLocation()
    {
        try
        {
            // desktops have no GPS — approximate by IP (free, no key).
            using var resp = await _http.GetAsync("https://ipapi.co/json/");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            double lat = root.GetProperty("latitude").GetDouble();
            double lon = root.GetProperty("longitude").GetDouble();
            string city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
            string region = root.TryGetProperty("region_code", out var rg) ? rg.GetString() ?? "" : "";
            string country = root.TryGetProperty("country_code", out var co) ? co.GetString() ?? "" : "";
            _celebrate = true;
            await SetLocation(new GeoLocation(city.Length > 0 ? city : "My location", region, country, lat, lon));
        }
        catch { NoteText.Text = "Couldn't find your location — search a city instead."; }
    }

    async Task SetLocation(GeoLocation loc) { _location = loc; SaveSettings(); await Refresh(); }

    // ---------------- saved places ----------------

    void BuildPlaces()
    {
        PlacesPanel.Children.Clear();
        foreach (var r in _weather.CachedPlaces())
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = r.Location.Name, Foreground = Brushes.White, FontSize = 14 });
            stack.Children.Add(new TextBlock { Text = $"{Temp(r.Reading.TempC)}  {r.Reading.Visual.Label}", Foreground = new SolidColorBrush(Color.Parse("#CCDCE6F5")), FontSize = 12 });
            var tile = new Button
            {
                Content = stack, Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8),
            };
            var place = r.Location;
            tile.Click += async (_, _) => { _celebrate = true; await SetLocation(place); };
            PlacesPanel.Children.Add(tile);
        }
    }

    // ---------------- units ----------------

    void ToggleUnit()
    {
        _fahrenheit = !_fahrenheit;
        UnitBtn.Content = _fahrenheit ? "°F" : "°C";
        SaveSettings();
        if (_current is not null) RenderResult(_current);
        BuildPlaces();
    }

    string Temp(double c) => $"{Math.Round(_fahrenheit ? WeatherReading.ToFahrenheit(c) : c)}°";

    // ---------------- playful ----------------

    void Celebrate() => Scene.Spawn(Bounds.Width / 2, Bounds.Height * 0.35);

    static string NightAwareEmoji(WeatherVisual v, bool day) => day ? v.Emoji : v.Kind switch
    {
        WeatherKind.Clear or WeatherKind.FewClouds => "🌙",
        WeatherKind.Rain => "🌧️",
        _ => v.Emoji,
    };

    readonly Random _quipRnd = new();
    string QuipFor(WeatherKind kind, double tempC, bool day)
    {
        string[] pool = kind switch
        {
            WeatherKind.Clear => day ? new[] { "Perfect day to be outside ☀️", "Grab your shades 😎", "Not a cloud in sight!", "Vitamin-D o'clock 🌻" }
                                     : new[] { "Clear skies, sweet dreams 🌙", "Stargazing weather ✨", "Calm & clear tonight" },
            WeatherKind.FewClouds => new[] { "Sun's playing peek-a-boo ⛅", "A few clouds just visiting ☁️", "Gentle skies today" },
            WeatherKind.Cloudy => new[] { "A cosy, cloudy one ☁️", "Soft grey skies", "Cloud-blanket mode: on" },
            WeatherKind.Fog => new[] { "Mysterious out there… 🌫️", "Mind the fog!", "Everything's a bit dreamy" },
            WeatherKind.Rain => new[] { "Umbrella weather ☂️", "Liquid sunshine 🌧️", "Puddle-jumping time!", "Don't forget your brolly" },
            WeatherKind.Snow => new[] { "Snowball ammo incoming ❄️", "Hot cocoa highly advised ☕", "Let it snow! ⛄", "Bundle up, buttercup" },
            WeatherKind.Storm => new[] { "Whoa — thunderstorm! ⛈️", "Nature's light show ⚡", "Cosy-up-inside weather" },
            _ => new[] { "Whatever the weather 🌡️" },
        };
        var list = pool.ToList();
        if (tempC <= 0) list.Add("Brrr… freezing! 🥶"); else if (tempC >= 32) list.Add("Scorcher alert! 🥵");
        return list[_quipRnd.Next(list.Count)];
    }

    // ---------------- settings ----------------

    sealed class Settings { public bool UnitF { get; set; } public GeoLocation? Loc { get; set; } }

    Settings LoadSettings()
    {
        try { return File.Exists(_settingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? new() : new(); }
        catch { return new(); }
    }

    void SaveSettings()
    {
        try { File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings { UnitF = _fahrenheit, Loc = _location })); }
        catch { }
    }
}
