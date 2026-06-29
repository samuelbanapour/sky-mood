using SkyMood;
using Xunit;

namespace SkyMood.Tests;

public class CoreModelTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]   // the famous crossover point
    [InlineData(37, 98.6)]
    public void ToFahrenheit_converts_correctly(double celsius, double expectedF)
    {
        Assert.Equal(expectedF, WeatherReading.ToFahrenheit(celsius), precision: 4);
    }

    [Theory]
    [InlineData(0, "Low")]
    [InlineData(2.9, "Low")]
    [InlineData(3, "Moderate")]
    [InlineData(5.9, "Moderate")]
    [InlineData(6, "High")]
    [InlineData(7.9, "High")]
    [InlineData(8, "Very High")]
    [InlineData(10.9, "Very High")]
    [InlineData(11, "Extreme")]
    [InlineData(15, "Extreme")]
    public void UvCategory_maps_to_who_bands(double uv, string expected)
    {
        Assert.Equal(expected, WeatherReading.UvCategory(uv));
    }

    [Fact]
    public void DistanceKmTo_is_zero_for_same_point()
    {
        var here = new GeoLocation("Here", "", "", 40.0, -3.0);
        Assert.Equal(0, here.DistanceKmTo(40.0, -3.0), precision: 6);
    }

    [Fact]
    public void DistanceKmTo_matches_known_city_pair()
    {
        // London ↔ Paris is ~343 km great-circle.
        var london = new GeoLocation("London", "", "GB", 51.5074, -0.1278);
        var km = london.DistanceKmTo(48.8566, 2.3522);
        Assert.InRange(km, 330, 355);
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear, "Clear sky")]
    [InlineData(2, WeatherKind.FewClouds, "Partly cloudy")]
    [InlineData(63, WeatherKind.Rain, "Rain")]
    [InlineData(75, WeatherKind.Snow, "Heavy snow")]
    [InlineData(95, WeatherKind.Storm, "Thunderstorm")]
    [InlineData(123456, WeatherKind.Cloudy, "Unknown")]   // unmapped falls through
    public void WeatherCodes_describe_maps_kind_and_label(int code, WeatherKind kind, string label)
    {
        var v = WeatherCodes.Describe(code);
        Assert.Equal(kind, v.Kind);
        Assert.Equal(label, v.Label);
    }

    [Fact]
    public void Forecast_is_never_null_even_when_daily_omitted()
    {
        var r = new WeatherReading(20, 19, 50, 10, 0, true, 22, 14, DateTimeOffset.Now);
        Assert.NotNull(r.Forecast);
        Assert.Empty(r.Forecast);
        Assert.Equal("Low", r.UvLabel);
    }

    [Fact]
    public void GeoLocation_display_prefers_admin_then_country()
    {
        Assert.Equal("Lyon, Rhône", new GeoLocation("Lyon", "Rhône", "FR", 0, 0).Display);
        Assert.Equal("Tokyo, JP", new GeoLocation("Tokyo", "", "JP", 0, 0).Display);
        Assert.Equal("Nowhere", new GeoLocation("Nowhere", "", "", 0, 0).Display);
    }
}
