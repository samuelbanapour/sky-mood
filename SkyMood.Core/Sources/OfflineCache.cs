using System.Globalization;
using System.Text.Json;

namespace SkyMood.Sources;

/// <summary>
/// On-device store of the last reading we saw for each location, so the app keeps working with no
/// connection at all. Every successful fetch is written here; when nothing is reachable we read it
/// back. One small JSON file per location, keyed by rounded coordinates.
/// </summary>
public sealed class OfflineCache
{
    readonly string _dir;

    public OfflineCache(string cacheDir)
    {
        _dir = cacheDir;
        Directory.CreateDirectory(_dir);
    }

    public void Save(GeoLocation location, WeatherReading reading)
    {
        try
        {
            var entry = new CacheEntry { Location = location, Reading = reading };
            File.WriteAllText(PathFor(location),
                JsonSerializer.Serialize(entry, JsonOpts));
        }
        catch { /* a cache write failure must never break the live path */ }
    }

    /// <summary>Last saved reading for this location, or null if we've never stored one.</summary>
    public WeatherReading? TryGet(GeoLocation location)
    {
        try
        {
            var path = PathFor(location);
            if (!File.Exists(path)) return null;
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), JsonOpts);
            return entry?.Reading;
        }
        catch { return null; }
    }

    /// <summary>Forget a saved place. Returns true if a cache entry existed and was deleted.</summary>
    public bool Remove(GeoLocation location)
    {
        try
        {
            var path = PathFor(location);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>All cached places, newest reading first — used to populate the offline places list.</summary>
    public IReadOnlyList<WeatherResult> All()
    {
        var results = new List<WeatherResult>();
        if (!Directory.Exists(_dir)) return results;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file), JsonOpts);
                if (entry?.Location is not null && entry.Reading is not null)
                    results.Add(new WeatherResult(entry.Location, entry.Reading, DataChannel.Cache,
                        "Cache", "Saved reading"));
            }
            catch { /* skip unreadable entry */ }
        }
        return results.OrderByDescending(r => r.Reading.ObservedAt).ToList();
    }

    string PathFor(GeoLocation l)
    {
        // 2 decimal places ≈ 1 km — fine for keying a city.
        var key = string.Create(CultureInfo.InvariantCulture, $"{l.Latitude:0.00}_{l.Longitude:0.00}");
        return Path.Combine(_dir, $"{key.Replace('.', 'p').Replace('-', 'm')}.json");
    }

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    sealed class CacheEntry
    {
        public GeoLocation? Location { get; set; }
        public WeatherReading? Reading { get; set; }
    }
}
