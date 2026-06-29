using System.Net;
using System.Text;
using SkyMood;
using SkyMood.Sources;
using Xunit;

namespace SkyMood.Tests;

public class OfflineCacheTests : IDisposable
{
    readonly string _dir;
    readonly OfflineCache _cache;

    public OfflineCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "skymood-tests-" + Guid.NewGuid().ToString("N"));
        _cache = new OfflineCache(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    static GeoLocation Place(double lat = 51.51, double lon = -0.13) =>
        new("London", "", "GB", lat, lon);

    static WeatherReading Reading() =>
        new(18, 17, 60, 12, 3, true, 21, 13, DateTimeOffset.Now, UvIndex: 4.2,
            Daily: new[] { new DailyForecast(DateOnly.FromDateTime(DateTime.Now), 21, 13, 3, 4.5) });

    [Fact]
    public void Save_then_TryGet_roundtrips_including_uv_and_daily()
    {
        var place = Place();
        _cache.Save(place, Reading());

        var got = _cache.TryGet(place);
        Assert.NotNull(got);
        Assert.Equal(18, got!.TempC);
        Assert.Equal(4.2, got.UvIndex, precision: 3);
        Assert.Single(got.Forecast);
        Assert.Equal(3, got.Forecast[0].WeatherCode);
    }

    [Fact]
    public void TryGet_returns_null_for_unknown_place()
    {
        Assert.Null(_cache.TryGet(Place(10, 10)));
    }

    [Fact]
    public void Remove_deletes_entry_and_reports_existence()
    {
        var place = Place();
        _cache.Save(place, Reading());

        Assert.True(_cache.Remove(place));   // existed → removed
        Assert.Null(_cache.TryGet(place));
        Assert.False(_cache.Remove(place));  // already gone → false
    }

    [Fact]
    public void All_lists_every_saved_place()
    {
        _cache.Save(Place(51.51, -0.13), Reading());
        _cache.Save(Place(48.85, 2.35), Reading());

        Assert.Equal(2, _cache.All().Count);
    }

    [Fact]
    public void Old_cache_json_without_uv_or_daily_still_deserializes()
    {
        // Simulate a file written by a pre-forecast build: no UvIndex / Daily fields.
        var place = Place();
        var legacy = """
            {"Location":{"Name":"London","Admin":"","Country":"GB","Latitude":51.51,"Longitude":-0.13,"Timezone":"auto"},
             "Reading":{"TempC":15,"FeelsLikeC":14,"Humidity":70,"WindKph":9,"WeatherCode":2,"IsDay":true,"HighC":17,"LowC":11,"ObservedAt":"2026-01-01T00:00:00+00:00"}}
            """;
        var key = $"{place.Latitude:0.00}_{place.Longitude:0.00}".Replace('.', 'p').Replace('-', 'm');
        File.WriteAllText(Path.Combine(_dir, key + ".json"), legacy);

        var got = _cache.TryGet(place);
        Assert.NotNull(got);
        Assert.Equal(15, got!.TempC);
        Assert.Equal(0, got.UvIndex);        // defaulted
        Assert.Empty(got.Forecast);          // defaulted to empty, not null
    }
}

public class OnlineWeatherSourceTests
{
    sealed class StubHandler : HttpMessageHandler
    {
        readonly string _json;
        public StubHandler(string json) => _json = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }

    const string SampleJson = """
        {
          "current": {
            "temperature_2m": 12.3, "relative_humidity_2m": 81, "apparent_temperature": 10.1,
            "is_day": 1, "weather_code": 61, "wind_speed_10m": 14.0, "uv_index": 5.5
          },
          "daily": {
            "time": ["2026-06-28","2026-06-29","2026-06-30"],
            "weather_code": [61, 3, 0],
            "temperature_2m_max": [16.0, 19.0, 22.5],
            "temperature_2m_min": [9.0, 10.5, 12.0],
            "uv_index_max": [5.5, 6.2, 7.8]
          }
        }
        """;

    [Fact]
    public async Task TryGetAsync_parses_current_uv_and_multi_day_forecast()
    {
        var http = new HttpClient(new StubHandler(SampleJson));
        var source = new OnlineWeatherSource(http);

        var r = await source.TryGetAsync(new GeoLocation("London", "", "GB", 51.51, -0.13));

        Assert.NotNull(r);
        Assert.Equal(12.3, r!.TempC, precision: 3);
        Assert.Equal(5.5, r.UvIndex, precision: 3);
        Assert.Equal(61, r.WeatherCode);
        Assert.True(r.IsDay);

        Assert.Equal(3, r.Forecast.Count);
        Assert.Equal(new DateOnly(2026, 6, 28), r.Forecast[0].Date);
        Assert.Equal(22.5, r.Forecast[2].HighC, precision: 3);
        Assert.Equal(7.8, r.Forecast[2].UvIndexMax, precision: 3);
        Assert.Equal(WeatherKind.Clear, r.Forecast[2].Visual.Kind);
    }
}
