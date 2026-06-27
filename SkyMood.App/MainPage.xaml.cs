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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;

        StartAnimation();
        BuildPlaces();
        await RefreshAsync();

        // Keep the reading fresh; works the same on a satellite ISP and falls back to cache offline.
        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(5);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _animTimer?.Stop();
        _refreshTimer?.Stop();
    }

    // ---------------- animation ----------------

    void StartAnimation()
    {
        _animTimer = Dispatcher.CreateTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
        _animTimer.Tick += (_, _) =>
        {
            _scene.Time += 0.033f;

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

        EmojiLabel.Text = v.Emoji;
        TempLabel.Text = Temp(r.Reading.TempC);
        DescLabel.Text = r.Reading.IsDay ? v.Label : $"{v.Label} · night";
        PlaceLabel.Text = $"{r.Location.Display}  ·  {r.Reading.ObservedAt.ToLocalTime():t}";
        FeelsLabel.Text = Temp(r.Reading.FeelsLikeC);
        HumidityLabel.Text = $"{r.Reading.Humidity}%";
        WindLabel.Text = $"{Math.Round(r.Reading.WindKph)} km/h";
        HighLowLabel.Text = $"{Temp(r.Reading.HighC)} / {Temp(r.Reading.LowC)}";

        SetBadge(r.Channel, r.Note);

        _scene.Kind = v.Kind;
        _scene.IsDay = r.Reading.IsDay;
        _scene.RainIntensity = v.RainIntensity;
        _scene.SnowIntensity = v.SnowIntensity;
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
            .Select(r => new PlaceTile(r.Location, $"{Temp(r.Reading.TempC)}  {r.Reading.Visual.Label}"))
            .ToList();
        PlacesList.ItemsSource = tiles;
    }

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
}
