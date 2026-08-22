using Microsoft.Maui.ApplicationModel;      // Permissions, FeatureNotSupportedException
using Microsoft.Maui.Devices.Sensors;       // Geolocation, GeolocationRequest
using Microsoft.Maui.Graphics;              // Color
using Microsoft.Maui.Storage;               // Preferences, FileSystem
using SkyMood;

namespace SkyMood.App;

public partial class MainPage : ContentPage
{
    readonly WeatherSceneDrawable _scene = new();
    readonly GeocodingService _geocoder;
    readonly WeatherService _weather;

    IDispatcherTimer? _animTimer;
    IDispatcherTimer? _refreshTimer;
    IDispatcherTimer? _clockTimer;
    GeoLocation _location;
    WeatherResult? _current;
    bool _fahrenheit;
    bool _started;
    readonly Random _rnd = new();

    public MainPage()
    {
        InitializeComponent();

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _geocoder = new GeocodingService(http);

        var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "cache");
        var inboxDir = Path.Combine(FileSystem.AppDataDirectory, "groundstation");
        Directory.CreateDirectory(inboxDir); // so a decoder pipeline has somewhere to drop passes
        _weather = WeatherService.CreateDefault(cacheDir, inboxDir, http);

        _location = RestoreLastLocation() ?? GeocodingService.Default;
        _fahrenheit = Preferences.Default.Get("unit.f", false);
        UnitBtn.Text = _fahrenheit ? "°F" : "°C";

        SceneView.Drawable = _scene;

        SearchBtn.Clicked += async (_, _) => await DoSearchAsync();
        SearchEntry.Completed += async (_, _) => await DoSearchAsync();
        LocateBtn.Clicked += async (_, _) => await UseMyLocationAsync();
        RefreshBtn.Clicked += async (_, _) => await RefreshAsync();
        UnitBtn.Clicked += (_, _) => ToggleUnit();
        ResultsList.SelectionChanged += OnResultSelected;
        PlacesList.SelectionChanged += OnPlaceSelected;

        AddTapBurst(EmojiLabel);   // tap the emoji or temperature for a confetti burst
        AddTapBurst(TempLabel);

#if IOS
        WatchConnectivityService.Instance.Configure(
            getCurrentLocation: () => _location,
            refresh: loc => _weather.GetAsync(loc),
            getPlaces: () => _weather.CachedPlaces(),
            selectLocation: loc => SetLocationAsync(loc),
            getIsFahrenheit: () => _fahrenheit);
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;

        StartAnimation();
        StartParallax();
        BuildPlaces();
        await RefreshAsync();

        // Keep the reading fresh; works the same on a satellite ISP and falls back to cache offline.
        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(5);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        // Ticks the displayed local-time-at-location clock without re-fetching weather data.
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.Tick += (_, _) => { if (_current is not null) UpdatePlaceLabel(_current); BuildPlaces(); };
        _clockTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _animTimer?.Stop();
        _refreshTimer?.Stop();
        _clockTimer?.Stop();
        StopParallax();
    }

    // ---------------- animation ----------------

    void StartAnimation()
    {
        _animTimer = Dispatcher.CreateTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
        _animTimer.Tick += (_, _) =>
        {
            _scene.Time += 0.033f;
            _scene.StepParticles(0.033f);
            _scene.OffsetX += (_targetOX - _scene.OffsetX) * 0.1f;   // ease toward the latest tilt
            _scene.OffsetY += (_targetOY - _scene.OffsetY) * 0.1f;

            // Random lightning during storms.
            if (_scene.Kind == WeatherKind.Storm)
            {
                if (_scene.Flash > 0) _scene.Flash = MathF.Max(0, _scene.Flash - 0.06f);
                else if (_rnd.NextDouble() < 0.006) _scene.Flash = 0.9f;
            }
            else _scene.Flash = 0;

            SceneView.Invalidate();
        };
        _animTimer.Start();
    }

    // ---------------- data flow ----------------

    async Task RefreshAsync()
    {
        try
        {
            var result = await _weather.GetAsync(_location);
            _current = result;
            RenderResult(result);
            BuildPlaces();
#if IOS
            WatchConnectivityService.Instance.Push(result);
#endif
        }
        catch (WeatherUnavailableException ex)
        {
            NoteLabel.Text = ex.Message;
            SetBadge(DataChannel.Cache, "No data yet");
        }
        catch (Exception)
        {
            NoteLabel.Text = "Couldn't update right now.";
        }
    }

    void RenderResult(WeatherResult r)
    {
        var v = r.Reading.Visual;

        EmojiLabel.Text = NightAwareEmoji(v, r.Reading.IsDay);
        TempLabel.Text = Temp(r.Reading.TempC);
        DescLabel.Text = r.Reading.IsDay ? v.Label : $"{v.Label} · night";
        QuipLabel.Text = QuipFor(v.Kind, r.Reading.TempC, r.Reading.IsDay);
        UpdatePlaceLabel(r);
        FeelsLabel.Text = Temp(r.Reading.FeelsLikeC);
        HumidityLabel.Text = $"{r.Reading.Humidity}%";
        WindLabel.Text = $"{Math.Round(r.Reading.WindKph)} km/h";
        HighLowLabel.Text = $"{Temp(r.Reading.HighC)} / {Temp(r.Reading.LowC)}";
        UvLabel.Text = Math.Round(r.Reading.UvIndex).ToString();
        UvBandLabel.Text = $"· {r.Reading.UvLabel}";
        UvBandLabel.TextColor = UvColor(r.Reading.UvIndex);
        BuildForecast(r.Reading);

        SetBadge(r.Channel, r.Note);

        _scene.Kind = v.Kind;
        _scene.IsDay = r.Reading.IsDay;
        _scene.RainIntensity = v.RainIntensity;
        _scene.SnowIntensity = v.SnowIntensity;
        _scene.WindKph = r.Reading.WindKph;
    }

    // Shows the *location's* current local time (re-computed live by _clockTimer), not when the
    // reading was fetched — that's what "what time is it in Germany right now" actually means.
    void UpdatePlaceLabel(WeatherResult r) =>
        PlaceLabel.Text = $"{r.Location.Display}  ·  {LocalTimeText(r.Reading)}";

    static string LocalTimeText(WeatherReading reading)
    {
        if (reading.TimezoneId is { Length: > 0 } tz)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
                return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).ToString("t");
            }
            catch (TimeZoneNotFoundException) { /* unknown id on this platform — fall through */ }
            catch (InvalidTimeZoneException) { /* corrupt tz data — fall through */ }
        }
        return DateTimeOffset.Now.ToString("t"); // no resolved zone — device's own local time
    }

    // The weather emoji is keyed to the condition (☀️ for "clear"); swap the sun-bearing ones for
    // a moon at night so we don't show a sun in the dark.
    static string NightAwareEmoji(WeatherVisual v, bool day) => day ? v.Emoji : v.Kind switch
    {
        WeatherKind.Clear or WeatherKind.FewClouds => "🌙",
        WeatherKind.Rain => "🌧️",
        _ => v.Emoji,
    };

    // ---------------- playful extras ----------------

    void AddTapBurst(View target)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(SceneView) is Point pt) _scene.Spawn((float)pt.X, (float)pt.Y);
            _ = Wiggle(target);
        };
        target.GestureRecognizers.Add(tap);
    }

    static async Task Wiggle(View v)
    {
        await v.ScaleToAsync(1.18, 90, Easing.CubicOut);
        await v.RotateToAsync(-10, 60);
        await v.RotateToAsync(8, 60);
        await v.RotateToAsync(0, 60);
        await v.ScaleToAsync(1.0, 90, Easing.CubicIn);
    }

    static readonly string[] _quipClearDay = { "Perfect day to be outside ☀️", "Grab your shades 😎", "Not a cloud in sight!", "Vitamin-D o'clock 🌻" };
    static readonly string[] _quipClearNight = { "Clear skies, sweet dreams 🌙", "Stargazing weather ✨", "Calm & clear tonight" };
    readonly Random _quipRnd = new();

    string QuipFor(WeatherKind kind, double tempC, bool day)
    {
        string[] pool = kind switch
        {
            WeatherKind.Clear     => day ? _quipClearDay : _quipClearNight,
            WeatherKind.FewClouds => new[] { "Sun's playing peek-a-boo ⛅", "A few clouds just visiting ☁️", "Gentle skies today" },
            WeatherKind.Cloudy    => new[] { "A cosy, cloudy one ☁️", "Soft grey skies", "Cloud-blanket mode: on" },
            WeatherKind.Fog       => new[] { "Mysterious out there… 🌫️", "Mind the fog!", "Everything's a bit dreamy" },
            WeatherKind.Rain      => new[] { "Umbrella weather ☂️", "Liquid sunshine 🌧️", "Puddle-jumping time!", "Don't forget your brolly" },
            WeatherKind.Snow      => new[] { "Snowball ammo incoming ❄️", "Hot cocoa highly advised ☕", "Let it snow! ⛄", "Bundle up, buttercup" },
            WeatherKind.Storm     => new[] { "Whoa — thunderstorm! ⛈️", "Nature's light show ⚡", "Cosy-up-inside weather" },
            _                     => new[] { "Whatever the weather 🌡️" },
        };
        if (tempC <= 0) pool = pool.Append("Brrr… freezing! 🥶").ToArray();
        else if (tempC >= 32) pool = pool.Append("Scorcher alert! 🥵").ToArray();
        return pool[_quipRnd.Next(pool.Length)];
    }

    // ---------------- accelerometer tilt-parallax ----------------

    const float ParallaxMax = 22f;     // DIPs the scene shifts at full tilt
    float _targetOX, _targetOY;

    void StartParallax()
    {
        try
        {
            var acc = Accelerometer.Default;
            if (acc is null || !acc.IsSupported) return;       // e.g. desktop — scene just stays centred
            acc.ReadingChanged += OnAccel;
            if (!acc.IsMonitoring) acc.Start(SensorSpeed.UI);
        }
        catch { /* sensor unavailable — ignore */ }
    }

    void StopParallax()
    {
        try
        {
            var acc = Accelerometer.Default;
            acc.ReadingChanged -= OnAccel;
            if (acc.IsMonitoring) acc.Stop();
        }
        catch { }
    }

    // Fires off the UI thread; we only set float targets that the anim timer eases toward.
    void OnAccel(object? sender, AccelerometerChangedEventArgs e)
    {
        var a = e.Reading.Acceleration;                         // G units; roll → X, pitch → Y
        _targetOX = Math.Clamp(a.X, -1f, 1f) * ParallaxMax;
        _targetOY = Math.Clamp(a.Y, -1f, 1f) * ParallaxMax;
    }

    void SetBadge(DataChannel channel, string note)
    {
        (BadgeLabel.Text, BadgeBorder.BackgroundColor, BadgeLabel.TextColor) = channel switch
        {
            DataChannel.Live      => ("LIVE",      Color.FromArgb("#3FB36B"), Color.FromArgb("#06210F")),
            DataChannel.Satellite => ("SATELLITE", Color.FromArgb("#36C5D8"), Color.FromArgb("#04222A")),
            _                     => ("OFFLINE",   Color.FromArgb("#E0A33A"), Color.FromArgb("#2A1C04")),
        };
        NoteLabel.Text = note;
    }

    // ---------------- search & location ----------------

    async Task DoSearchAsync()
    {
        var q = SearchEntry.Text ?? "";
        if (q.Trim().Length == 0) return;
        SearchBtn.IsEnabled = false;
        try
        {
            var matches = await _geocoder.SearchAsync(q);
            ResultsList.ItemsSource = matches;
            ResultsCard.IsVisible = matches.Count > 0;
            if (matches.Count == 0) NoteLabel.Text = $"No places found for “{q}”.";
        }
        finally { SearchBtn.IsEnabled = true; }
    }

    async void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not GeoLocation loc) return;
        ResultsList.SelectedItem = null;
        ResultsCard.IsVisible = false;
        SearchEntry.Text = "";
        await SetLocationAsync(loc);
    }

    async void OnPlaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaceTile tile) return;
        PlacesList.SelectedItem = null;
        await SetLocationAsync(tile.Location);
    }

    async Task UseMyLocationAsync()
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                NoteLabel.Text = "Location permission denied — search a city instead.";
                return;
            }

            var pos = await Geolocation.Default.GetLocationAsync(
                          new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)))
                      ?? await Geolocation.Default.GetLastKnownLocationAsync();

            if (pos is null) { NoteLabel.Text = "Couldn't read your location."; return; }

            await SetLocationAsync(new GeoLocation("My location", "", "", pos.Latitude, pos.Longitude));
        }
        catch (FeatureNotSupportedException) { NoteLabel.Text = "This device has no location sensor."; }
        catch (Exception) { NoteLabel.Text = "Couldn't read your location."; }
    }

    async Task SetLocationAsync(GeoLocation loc)
    {
        _location = loc;
        SaveLastLocation(loc);
        await RefreshAsync();
    }

    // ---------------- places strip ----------------

    void BuildPlaces()
    {
        var tiles = _weather.CachedPlaces()
            .Select(r => new PlaceTile(r.Location, $"{Temp(r.Reading.TempC)}  {r.Reading.Visual.Label}  ·  {LocalTimeText(r.Reading)}"))
            .ToList();
        PlacesList.ItemsSource = tiles;
    }

    void OnDeletePlaceClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: GeoLocation loc })
        {
            _weather.RemovePlace(loc);
            BuildPlaces();
        }
    }

    // ---------------- forecast strip ----------------

    void BuildForecast(WeatherReading reading)
    {
        ForecastList.ItemsSource = reading.Forecast
            .Select(d => new DayTile(
                d.ShortDay, d.Visual.Emoji, Temp(d.HighC), Temp(d.LowC),
                $"UV {Math.Round(d.UvIndexMax)}", UvColor(d.UvIndexMax)))
            .ToList();
        // Older offline-cached readings have no daily block — hide the section rather than show an empty strip.
        bool has = reading.Forecast.Count > 0;
        ForecastHeader.IsVisible = has;
        ForecastList.IsVisible = has;
    }

    // WHO/EPA UV bands → colour, mirroring the desktop + web editions.
    static Color UvColor(double uv) => Color.FromArgb(uv switch
    {
        < 3  => "#3FB36B",
        < 6  => "#D8B13A",
        < 8  => "#E0823A",
        < 11 => "#D9534F",
        _    => "#9A6BD0",
    });

    // ---------------- units ----------------

    void ToggleUnit()
    {
        _fahrenheit = !_fahrenheit;
        Preferences.Default.Set("unit.f", _fahrenheit);
        UnitBtn.Text = _fahrenheit ? "°F" : "°C";
        if (_current is not null) RenderResult(_current);
        BuildPlaces();
    }

    string Temp(double c)
    {
        double v = _fahrenheit ? WeatherReading.ToFahrenheit(c) : c;
        return $"{Math.Round(v)}°";
    }

    // ---------------- persistence ----------------

    void SaveLastLocation(GeoLocation l)
    {
        var p = Preferences.Default;
        p.Set("loc.name", l.Name);
        p.Set("loc.admin", l.Admin);
        p.Set("loc.country", l.Country);
        p.Set("loc.lat", l.Latitude);
        p.Set("loc.lon", l.Longitude);
        p.Set("loc.tz", l.Timezone);
    }

    static GeoLocation? RestoreLastLocation()
    {
        var p = Preferences.Default;
        if (!p.ContainsKey("loc.lat")) return null;
        return new GeoLocation(
            p.Get("loc.name", "My location"),
            p.Get("loc.admin", ""),
            p.Get("loc.country", ""),
            p.Get("loc.lat", 0.0),
            p.Get("loc.lon", 0.0),
            p.Get("loc.tz", "auto"));
    }

    /// <summary>Row model for the horizontal "saved places" strip.</summary>
    public sealed record PlaceTile(GeoLocation Location, string ShortLine);

    /// <summary>Cell model for the horizontal 7-day forecast strip.</summary>
    public sealed record DayTile(string Day, string Emoji, string Hi, string Lo, string Uv, Color UvColor);
}
