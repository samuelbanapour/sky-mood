using System.Globalization;
using System.Text.Json;

namespace SkyMood.Sources;

/// <summary>
/// Fetches live weather from the free NOAA/National Weather Service API (api.weather.gov, no key
/// required) — a second live source, independent of Open-Meteo's infrastructure, so an Open-Meteo
/// outage doesn't mean "no live data" for anyone in the US. NWS covers the US only (including
/// territories); everywhere else this simply returns null and the chain falls through as usual.
///
/// NWS has no single "current + 7-day" endpoint, so a full fetch is 4 sequential calls:
///   1. /points/{lat},{lon}          → gridId/gridX/gridY, observationStations + forecast URLs
///   2. .../stations, then .../observations/latest → current conditions (already °C/km/h)
///   3. /gridpoints/{grid}           → raw maxTemperature/minTemperature series (already °C)
///   4. /gridpoints/{grid}/forecast  → daytime periods, used only for each day's icon/condition
/// NWS has no numeric weather code (unlike Open-Meteo's WMO codes) and no UV index at all —
/// icons are bucketed into the closest WMO code (approximate, good enough for label/emoji) and
/// UvIndex is always 0 for NOAA-sourced readings.
/// </summary>
public sealed class NoaaWeatherSource : IWeatherSource
{
    public string Name => "NOAA/NWS (US only)";
    public DataChannel Channel => DataChannel.Live;

    readonly HttpClient _http;
    static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(6);

    public NoaaWeatherSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        // NWS rejects requests without a descriptive User-Agent (app + contact) — set it once.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("(SkyMood weather app, samuel.banapour100@gmail.com)");
    }

    public async Task<WeatherReading?> TryGetAsync(GeoLocation location, CancellationToken ct = default)
    {
        try
        {
            var lat = location.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            var lon = location.Longitude.ToString("0.####", CultureInfo.InvariantCulture);

            var points = await GetJsonAsync($"https://api.weather.gov/points/{lat},{lon}", ct).ConfigureAwait(false);
            if (points is null) return null; // e.g. 404 — outside NWS/US coverage

            var props = points.Value.GetProperty("properties");
            var gridId = props.GetProperty("gridId").GetString();
            var gridX = props.GetProperty("gridX").GetInt32();
            var gridY = props.GetProperty("gridY").GetInt32();
            var stationsUrl = props.GetProperty("observationStations").GetString();
            if (gridId is null || stationsUrl is null) return null;
            var timezoneId = props.TryGetProperty("timeZone", out var tzEl) ? tzEl.GetString() : null;

            var stations = await GetJsonAsync(stationsUrl, ct).ConfigureAwait(false);
            var stationUrl = stations?.GetProperty("features").EnumerateArray().FirstOrDefault().GetProperty("id").GetString();
            if (stationUrl is null) return null;

            var obs = await GetJsonAsync($"{stationUrl}/observations/latest", ct).ConfigureAwait(false);
            if (obs is null) return null;
            var obsProps = obs.Value.GetProperty("properties");

            double? Value(JsonElement e, string p) =>
                e.TryGetProperty(p, out var v) && v.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number
                    ? val.GetDouble() : null;

            var temp = Value(obsProps, "temperature");
            if (temp is null) return null; // station reporting outage — nothing usable

            var humidity = Value(obsProps, "relativeHumidity") ?? 0;
            var windKph = Value(obsProps, "windSpeed") ?? 0;
            var feelsLike = Value(obsProps, "heatIndex") ?? Value(obsProps, "windChill") ?? temp.Value;
            var (currentSlug, isDay) = ParseIcon(obsProps.TryGetProperty("icon", out var iconEl) ? iconEl.GetString() : null);
            var currentCode = CodeFor(currentSlug);

            var grid = await GetJsonAsync($"https://api.weather.gov/gridpoints/{gridId}/{gridX},{gridY}", ct).ConfigureAwait(false);
            var forecast = await GetJsonAsync($"https://api.weather.gov/gridpoints/{gridId}/{gridX},{gridY}/forecast", ct).ConfigureAwait(false);

            var (dates, highs, lows) = ParseDailyMinMax(grid);
            var dayCodes = ParseDaytimeCodes(forecast);

            var daily = new List<DailyForecast>(dates.Count);
            for (int i = 0; i < dates.Count; i++)
            {
                var code = i < dayCodes.Count ? dayCodes[i] : currentCode;
                daily.Add(new DailyForecast(dates[i], highs[i], lows[i], code, 0));
            }

            var highC = dates.Count > 0 ? highs[0] : temp.Value;
            var lowC = dates.Count > 0 ? lows[0] : temp.Value;

            return new WeatherReading(
                TempC: temp.Value,
                FeelsLikeC: feelsLike,
                Humidity: (int)Math.Round(humidity),
                WindKph: windKph,
                WeatherCode: currentCode,
                IsDay: isDay,
                HighC: highC,
                LowC: lowC,
                ObservedAt: DateTimeOffset.Now,
                UvIndex: 0,
                Daily: daily,
                TimezoneId: timezoneId);
        }
        catch
        {
            return null; // any hiccup — let WeatherService move on to the next source
        }
    }

    async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerCallTimeout);
        try
        {
            using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // our own per-call timeout, not the caller's cancellation
        }
    }

    /// <summary>Groups the raw grid's maxTemperature/minTemperature series by calendar date.</summary>
    static (List<DateOnly> dates, List<double> highs, List<double> lows) ParseDailyMinMax(JsonElement? grid)
    {
        var highByDate = new Dictionary<DateOnly, double>();
        var lowByDate = new Dictionary<DateOnly, double>();
        var order = new List<DateOnly>();

        if (grid is null) return (order, new List<double>(), new List<double>());
        var props = grid.Value.GetProperty("properties");

        void Collect(string prop, Dictionary<DateOnly, double> into)
        {
            if (!props.TryGetProperty(prop, out var series) || !series.TryGetProperty("values", out var values)) return;
            foreach (var v in values.EnumerateArray())
            {
                var validTime = v.GetProperty("validTime").GetString();
                if (validTime is null) continue;
                var startStr = validTime.Split('/')[0];
                if (!DateTimeOffset.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) continue;
                var date = DateOnly.FromDateTime(start.DateTime);
                if (!into.ContainsKey(date))
                {
                    into[date] = v.GetProperty("value").GetDouble();
                    if (!order.Contains(date)) order.Add(date);
                }
            }
        }

        Collect("maxTemperature", highByDate);
        Collect("minTemperature", lowByDate);

        order.Sort();
        var take = order.Take(7).ToList();
        var highs = take.Select(d => highByDate.GetValueOrDefault(d)).ToList();
        var lows = take.Select(d => lowByDate.GetValueOrDefault(d)).ToList();
        return (take, highs, lows);
    }

    /// <summary>Daytime-period icons in chronological order, positionally paired with the grid's
    /// per-day highs/lows (NWS's local-time period dates and the grid's UTC dates don't line up
    /// cleanly, so index order is used rather than exact date matching).</summary>
    static List<int> ParseDaytimeCodes(JsonElement? forecast)
    {
        var codes = new List<int>();
        if (forecast is null) return codes;
        if (!forecast.Value.GetProperty("properties").TryGetProperty("periods", out var periods)) return codes;

        foreach (var p in periods.EnumerateArray())
        {
            if (p.TryGetProperty("isDaytime", out var day) && day.ValueKind == JsonValueKind.True)
            {
                var icon = p.TryGetProperty("icon", out var i) ? i.GetString() : null;
                var (slug, _) = ParseIcon(icon);
                codes.Add(CodeFor(slug));
            }
        }
        return codes;
    }

    /// <summary>NWS icon URLs look like ".../icons/land/night/bkn?size=medium" or
    /// ".../land/day/rain_showers,40?size=medium" (trailing ",NN" is a precipitation chance).</summary>
    static (string slug, bool isDay) ParseIcon(string? iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl)) return ("", true);
        var parts = iconUrl.Split('/');
        if (parts.Length < 2) return ("", true);
        var slug = parts[^1].Split('?')[0].Split(',')[0];
        var isDay = parts[^2] != "night";
        return (slug, isDay);
    }

    /// <summary>Approximate NWS icon slug → WMO weather code, so the rest of the app's
    /// label/emoji rendering (keyed off WMO codes) works unmodified for NOAA readings.</summary>
    static int CodeFor(string slug) => slug switch
    {
        "skc" => 0,
        "few" => 1,
        "sct" => 2,
        "bkn" or "ovc" or "cold" => 3,
        "fog" or "dust" or "smoke" or "haze" => 45,
        "rain" => 63,
        "rain_showers" or "rain_showers_hi" => 80,
        "snow" => 73,
        "rain_snow" => 71,
        "sleet" or "rain_sleet" or "snow_sleet" => 67,
        "fzra" or "rain_fzra" or "snow_fzra" => 66,
        "tsra" or "tsra_sct" or "tsra_hi" => 95,
        "tornado" or "hurricane" or "tropical_storm" => 99,
        "blizzard" => 75,
        "hot" or "skc_hot" => 0,
        _ when slug.StartsWith("wind_", StringComparison.Ordinal) => CodeFor(slug["wind_".Length..]),
        _ => 3,
    };
}
