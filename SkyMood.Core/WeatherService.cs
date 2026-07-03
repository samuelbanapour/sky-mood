using SkyMood.Sources;

namespace SkyMood;

/// <summary>
/// The brain of the app. Given a location, it tries each data source in priority order and returns
/// the first reading it gets, always saving a copy to the offline cache. If every live source is
/// unreachable it serves the last cached reading, so the app keeps working off-grid.
///
/// Priority by default:
///   1. Satellite  — a fresh direct pass from your ground station (works with zero internet)
///   2. Live       — Open-Meteo over HTTPS (also the path used over a Starlink/Viasat ISP)
///   3. Live (NOAA)— NOAA/NWS, US-only, independent infrastructure from Open-Meteo — kicks in
///                   if Open-Meteo itself is down (rather than just your own connection)
///   4. Cache      — last reading saved on this device (final offline fallback)
/// </summary>
public sealed class WeatherService
{
    readonly IReadOnlyList<IWeatherSource> _sources;
    readonly OfflineCache _cache;

    public WeatherService(OfflineCache cache, IReadOnlyList<IWeatherSource> sources)
    {
        _cache = cache;
        _sources = sources;
    }

    /// <summary>Convenience factory wiring the standard satellite → online → NOAA → cache chain.</summary>
    public static WeatherService CreateDefault(string cacheDir, string satelliteInboxDir, HttpClient? http = null)
    {
        var cache = new OfflineCache(cacheDir);
        var sources = new List<IWeatherSource>
        {
            new SatelliteWeatherSource(satelliteInboxDir),
            new OnlineWeatherSource(http),
            new NoaaWeatherSource(http),
        };
        return new WeatherService(cache, sources);
    }

    public async Task<WeatherResult> GetAsync(GeoLocation location, CancellationToken ct = default)
    {
        foreach (var source in _sources)
        {
            WeatherReading? reading;
            try { reading = await source.TryGetAsync(location, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { continue; } // this source failed — try the next

            if (reading is not null)
            {
                _cache.Save(location, reading);
                return new WeatherResult(location, reading, source.Channel, source.Name,
                    NoteFor(source.Channel, reading));
            }
        }

        // Everything live failed — fall back to the last thing we saved.
        var cached = _cache.TryGet(location);
        if (cached is not null)
        {
            var age = DateTimeOffset.Now - cached.ObservedAt;
            return new WeatherResult(location, cached, DataChannel.Cache, "Offline cache",
                $"Offline — saved {Humanize(age)} ago");
        }

        throw new WeatherUnavailableException(
            "No connection and nothing cached for this place yet. Connect once to save it for offline use.");
    }

    public IReadOnlyList<WeatherResult> CachedPlaces() => _cache.All();

    /// <summary>Forget a saved place so it drops off the offline list. True if it existed.</summary>
    public bool RemovePlace(GeoLocation location) => _cache.Remove(location);

    static string NoteFor(DataChannel channel, WeatherReading r) => channel switch
    {
        DataChannel.Satellite => $"Direct satellite pass · {Humanize(DateTimeOffset.Now - r.ObservedAt)} ago",
        DataChannel.Live      => "Live",
        _                     => "Cached",
    };

    static string Humanize(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return "moments";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h";
        return $"{(int)age.TotalDays} d";
    }
}
