using System.Globalization;
using System.Net;
using System.Text.Json;

namespace SkyMood;

/// <summary>
/// Turns a typed place name ("Tokyo", "Reykjavik") into coordinates so the user can look up other
/// places, not just where they are. Uses Open-Meteo's free geocoding API when online; falls back to
/// a small built-in gazetteer of major cities so search still returns something offline.
/// </summary>
public sealed class GeocodingService
{
    readonly HttpClient _http;

    public GeocodingService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (_http.Timeout == TimeSpan.FromSeconds(100)) _http.Timeout = TimeSpan.FromSeconds(12);
    }

    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return Array.Empty<GeoLocation>();

        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={WebUtility.UrlEncode(query)}" +
                      "&count=8&language=en&format=json";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
            {
                var list = new List<GeoLocation>();
                foreach (var r in results.EnumerateArray())
                {
                    string Str(string p) => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    double Dbl(string p) => r.TryGetProperty(p, out var v) ? v.GetDouble() : 0;
                    list.Add(new GeoLocation(
                        Name: Str("name"),
                        Admin: Str("admin1"),
                        Country: Str("country"),
                        Latitude: Dbl("latitude"),
                        Longitude: Dbl("longitude"),
                        Timezone: Str("timezone") is { Length: > 0 } tz ? tz : "auto"));
                }
                return list;
            }
        }
        catch { /* fall through to the offline gazetteer */ }

        return SearchGazetteer(query);
    }

    static IReadOnlyList<GeoLocation> SearchGazetteer(string query) =>
        Gazetteer
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || c.Country.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(8).ToList();

    /// <summary>A small set of well-known cities so search and a sensible default work without a network.</summary>
    public static readonly IReadOnlyList<GeoLocation> Gazetteer = new[]
    {
        new GeoLocation("London", "England", "United Kingdom", 51.5074, -0.1278),
        new GeoLocation("New York", "New York", "United States", 40.7128, -74.0060),
        new GeoLocation("San Francisco", "California", "United States", 37.7749, -122.4194),
        new GeoLocation("Tokyo", "Tokyo", "Japan", 35.6895, 139.6917),
        new GeoLocation("Paris", "Île-de-France", "France", 48.8566, 2.3522),
        new GeoLocation("Sydney", "New South Wales", "Australia", -33.8688, 151.2093),
        new GeoLocation("Reykjavik", "Capital Region", "Iceland", 64.1466, -21.9426),
        new GeoLocation("Cape Town", "Western Cape", "South Africa", -33.9249, 18.4241),
        new GeoLocation("Singapore", "", "Singapore", 1.3521, 103.8198),
        new GeoLocation("Dubai", "Dubai", "United Arab Emirates", 25.2048, 55.2708),
        new GeoLocation("Rio de Janeiro", "Rio de Janeiro", "Brazil", -22.9068, -43.1729),
        new GeoLocation("Moscow", "Moscow", "Russia", 55.7558, 37.6173),
        new GeoLocation("Mumbai", "Maharashtra", "India", 19.0760, 72.8777),
        new GeoLocation("Toronto", "Ontario", "Canada", 43.6532, -79.3832),
        new GeoLocation("Anchorage", "Alaska", "United States", 61.2181, -149.9003),
        new GeoLocation("Nuuk", "Sermersooq", "Greenland", 64.1814, -51.6941),
    };

    public static GeoLocation Default => Gazetteer[0];
}
