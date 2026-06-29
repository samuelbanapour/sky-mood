using System.Globalization;
using System.Text.Json;

namespace SkyMood.Sources;

/// <summary>
/// Fetches live weather from the free Open-Meteo API (no key required). This is the "online" path —
/// and because it's plain HTTPS, it works transparently over a satellite ISP (Starlink/Viasat) too.
/// Requests are tiny and time-boxed with one retry so it degrades gracefully on a high-latency or
/// flaky satellite link rather than hanging.
/// </summary>
public sealed class OnlineWeatherSource : IWeatherSource
{
    public string Name => "Open-Meteo (live)";
    public DataChannel Channel => DataChannel.Live;

    readonly HttpClient _http;

    public OnlineWeatherSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        // Keep the per-attempt budget short so a dead satellite link fails fast into the cache path.
        if (_http.Timeout == TimeSpan.FromSeconds(100)) // the .NET default → we override it
            _http.Timeout = TimeSpan.FromSeconds(12);
    }

    public async Task<WeatherReading?> TryGetAsync(GeoLocation location, CancellationToken ct = default)
    {
        var lat = location.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var url =
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
            "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,uv_index" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,uv_index_max" +
            "&forecast_days=7&timezone=auto";

        // One immediate retry smooths over the occasional dropped packet on a satellite hop.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                return Parse(doc.RootElement);
            }
            catch (Exception) when (attempt == 0 && !ct.IsCancellationRequested)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
        return null;
    }

    static WeatherReading Parse(JsonElement root)
    {
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        double Num(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.GetDouble() : 0;
        double First(JsonElement e, string p) =>
            e.TryGetProperty(p, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                ? arr[0].GetDouble() : 0;

        var forecast = ParseDaily(daily);

        return new WeatherReading(
            TempC: Num(cur, "temperature_2m"),
            FeelsLikeC: Num(cur, "apparent_temperature"),
            Humidity: (int)Math.Round(Num(cur, "relative_humidity_2m")),
            WindKph: Num(cur, "wind_speed_10m"),
            WeatherCode: (int)Num(cur, "weather_code"),
            IsDay: Num(cur, "is_day") == 1,
            HighC: First(daily, "temperature_2m_max"),
            LowC: First(daily, "temperature_2m_min"),
            ObservedAt: DateTimeOffset.Now,
            UvIndex: Num(cur, "uv_index"),
            Daily: forecast);
    }

    /// <summary>Builds the multi-day outlook from the parallel arrays Open-Meteo returns under "daily".</summary>
    static IReadOnlyList<DailyForecast> ParseDaily(JsonElement daily)
    {
        if (daily.ValueKind != JsonValueKind.Object
            || !daily.TryGetProperty("time", out var time)
            || time.ValueKind != JsonValueKind.Array)
            return Array.Empty<DailyForecast>();

        JsonElement Arr(string p) =>
            daily.TryGetProperty(p, out var a) && a.ValueKind == JsonValueKind.Array ? a : default;

        var codes = Arr("weather_code");
        var highs = Arr("temperature_2m_max");
        var lows  = Arr("temperature_2m_min");
        var uvs   = Arr("uv_index_max");

        double At(JsonElement arr, int i) =>
            arr.ValueKind == JsonValueKind.Array && i < arr.GetArrayLength() ? arr[i].GetDouble() : 0;

        var list = new List<DailyForecast>(time.GetArrayLength());
        for (int i = 0; i < time.GetArrayLength(); i++)
        {
            var date = DateOnly.TryParse(time[i].GetString(), CultureInfo.InvariantCulture, out var d)
                ? d : DateOnly.FromDateTime(DateTime.Now).AddDays(i);
            list.Add(new DailyForecast(date, At(highs, i), At(lows, i), (int)At(codes, i), At(uvs, i)));
        }
        return list;
    }
}
