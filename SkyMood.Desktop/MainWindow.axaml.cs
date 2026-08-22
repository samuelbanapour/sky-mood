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
    DispatcherTimer? _clock;
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

            // Ticks the displayed local-time-at-location clock without re-fetching weather data.
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clock.Tick += (_, _) => { if (_current is not null) UpdatePlaceText(_current); BuildPlaces(); };
            _clock.Start();
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
        UpdatePlaceText(r);
        FeelsText.Text = Temp(r.Reading.FeelsLikeC);
        HumText.Text = $"{r.Reading.Humidity}%";
        WindText.Text = $"{Math.Round(r.Reading.WindKph)} km/h";
        HiLoText.Text = $"{Temp(r.Reading.HighC)} / {Temp(r.Reading.LowC)}";
        UvText.Text = Math.Round(r.Reading.UvIndex).ToString(CultureInfo.InvariantCulture);
        UvBand.Text = $"· {r.Reading.UvLabel}";
        UvBand.Foreground = new SolidColorBrush(Color.Parse(UvColor(r.Reading.UvIndex)));
        SetBadge(r.Channel, r.Note);
        BuildForecast(r.Reading);

        Scene.Kind = v.Kind;
        Scene.IsDay = r.Reading.IsDay;
        Scene.RainIntensity = v.RainIntensity;
        Scene.SnowIntensity = v.SnowIntensity;
        Scene.WindKph = r.Reading.WindKph;
    }

    // Shows the *location's* current local time (re-computed live by _clock), not when the
    // reading was fetched. Arithmetic only, no zone-database lookup — this project builds with
    // InvariantGlobalization, which on Windows can't map an IANA id to a zone (needs ICU); a
    // plain numeric offset needs no lookup at all, so it works the same on every OS regardless.
    void UpdatePlaceText(WeatherResult r) =>
        PlaceText.Text = $"{r.Location.Display}  ·  {LocalTimeText(r.Reading)}";

    static string LocalTimeText(WeatherReading reading) => reading.UtcOffsetSeconds is { } offset
        ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(offset)).ToString("t")
        : DateTimeOffset.Now.ToString("t"); // no offset resolved — this machine's own local time

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
            stack.Children.Add(new TextBlock { Text = r.Location.Name, Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(0, 0, 14, 0) });
            stack.Children.Add(new TextBlock { Text = $"{Temp(r.Reading.TempC)}  {r.Reading.Visual.Label}  ·  {LocalTimeText(r.Reading)}", Foreground = new SolidColorBrush(Color.Parse("#CCDCE6F5")), FontSize = 12 });
            var tile = new Button
            {
                Content = stack, Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8),
            };
            var place = r.Location;
            tile.Click += async (_, _) => { _celebrate = true; await SetLocation(place); };

            // small ✕ to forget this place, overlaid top-right of the tile
            var del = new Button
            {
                Content = "✕", FontSize = 11, Foreground = Brushes.White, Padding = new Thickness(0),
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            del.Click += (_, _) => { _weather.RemovePlace(place); BuildPlaces(); };

            var cell = new Grid();
            cell.Children.Add(tile);
            cell.Children.Add(del);
            PlacesPanel.Children.Add(cell);
        }
    }

    // ---------------- forecast ----------------

    void BuildForecast(WeatherReading reading)
    {
        ForecastPanel.Children.Clear();
        // Older offline-cached readings have no daily block — hide the section rather than show an empty strip.
        bool has = reading.Forecast.Count > 0;
        ForecastHeader.IsVisible = has;
        ForecastScroller.IsVisible = has;
        foreach (var d in reading.Forecast)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
            stack.Children.Add(new TextBlock { Text = d.ShortDay, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#D8DCE6F5")), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = d.Visual.Emoji, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = Temp(d.HighC), FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = Temp(d.LowC), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#99DCE6F5")), HorizontalAlignment = HorizontalAlignment.Center });

            var uvTag = new Border
            {
                Background = new SolidColorBrush(Color.Parse(UvColor(d.UvIndexMax))), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 1), Margin = new Thickness(0, 2, 0, 0),
                Child = new TextBlock { Text = $"UV {Math.Round(d.UvIndexMax)}", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#0C1322")) },
            };
            stack.Children.Add(uvTag);

            ForecastPanel.Children.Add(new Border
            {
                Child = stack, Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 10), MinWidth = 60,
            });
        }
    }

    // WHO/EPA UV bands → colour, mirroring the web edition and WeatherReading.UvCategory.
    static string UvColor(double uv) => uv switch
    {
        < 3  => "#3FB36B",
        < 6  => "#D8B13A",
        < 8  => "#E0823A",
        < 11 => "#D9534F",
        _    => "#9A6BD0",
    };

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
