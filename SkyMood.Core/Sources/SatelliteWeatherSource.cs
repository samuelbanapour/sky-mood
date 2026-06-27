using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyMood.Sources;

/// <summary>
/// Reads weather decoded from a *direct* weather-satellite pass — i.e. data your own ground station
/// pulled down off NOAA APT / Meteor / GOES with an SDR, with no internet involved at all.
///
/// We don't talk to the radio here; instead we watch an "inbox" folder where a decoder pipeline
/// (e.g. RTL-SDR → SatDump / wxtoimg) drops a small JSON sidecar per pass. Each sidecar describes
/// the conditions and the footprint it covers (centre lat/lon + radius). When you ask for a
/// location, we return the freshest still-valid pass whose footprint contains that location.
///
/// Drop nothing in the folder and this source simply returns null (no pass available) and the app
/// falls through to the online/cache paths — so it's safe to enable even without hardware.
///
/// Expected sidecar shape (pass-*.json):
/// {
///   "satellite": "NOAA-19", "receivedAt": "2026-06-27T10:02:00Z",
///   "latitude": 51.5, "longitude": -0.12, "radiusKm": 1500,
///   "tempC": 14.2, "feelsLikeC": 13.0, "humidity": 70, "windKph": 12,
///   "weatherCode": 3, "isDay": true, "highC": 17, "lowC": 9, "imagePath": "noaa19-1002.png"
/// }
/// </summary>
public sealed class SatelliteWeatherSource : IWeatherSource
{
    public string Name => "Ground station (satellite pass)";
    public DataChannel Channel => DataChannel.Satellite;

    readonly string _inboxDir;
    readonly TimeSpan _maxAge;

    /// <param name="inboxDir">Folder the SDR decode pipeline writes pass-*.json sidecars into.</param>
    /// <param name="maxAge">How recent a pass must be to be trusted. Default 6 hours.</param>
    public SatelliteWeatherSource(string inboxDir, TimeSpan? maxAge = null)
    {
        _inboxDir = inboxDir;
        _maxAge = maxAge ?? TimeSpan.FromHours(6);
    }

    public Task<WeatherReading?> TryGetAsync(GeoLocation location, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_inboxDir) || !Directory.Exists(_inboxDir))
            return Task.FromResult<WeatherReading?>(null);

        SatellitePass? best = null;
        foreach (var file in Directory.EnumerateFiles(_inboxDir, "pass-*.json"))
        {
            ct.ThrowIfCancellationRequested();
            SatellitePass? pass;
            try { pass = JsonSerializer.Deserialize<SatellitePass>(File.ReadAllText(file)); }
            catch { continue; } // skip a half-written / malformed sidecar

            if (pass is null) continue;
            if (DateTimeOffset.Now - pass.ReceivedAt > _maxAge) continue;          // too stale
            if (location.DistanceKmTo(pass.Latitude, pass.Longitude) > pass.RadiusKm) continue; // out of footprint

            if (best is null || pass.ReceivedAt > best.ReceivedAt) best = pass;     // keep the freshest
        }

        return Task.FromResult(best?.ToReading());
    }

    sealed class SatellitePass
    {
        [JsonPropertyName("satellite")] public string Satellite { get; set; } = "satellite";
        [JsonPropertyName("receivedAt")] public DateTimeOffset ReceivedAt { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
        [JsonPropertyName("radiusKm")] public double RadiusKm { get; set; } = 1200;
        [JsonPropertyName("tempC")] public double TempC { get; set; }
        [JsonPropertyName("feelsLikeC")] public double FeelsLikeC { get; set; }
        [JsonPropertyName("humidity")] public int Humidity { get; set; }
        [JsonPropertyName("windKph")] public double WindKph { get; set; }
        [JsonPropertyName("weatherCode")] public int WeatherCode { get; set; }
        [JsonPropertyName("isDay")] public bool IsDay { get; set; } = true;
        [JsonPropertyName("highC")] public double HighC { get; set; }
        [JsonPropertyName("lowC")] public double LowC { get; set; }

        public WeatherReading ToReading() => new(
            TempC, FeelsLikeC, Humidity, WindKph, WeatherCode, IsDay, HighC, LowC, ReceivedAt);
    }
}
