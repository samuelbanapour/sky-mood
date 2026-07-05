using System.Globalization;

namespace SkyMood;

/// <summary>A place we can show weather for. Latitude/Longitude in degrees.</summary>
public sealed record GeoLocation(
    string Name,
    string Admin,
    string Country,
    double Latitude,
    double Longitude,
    string Timezone = "auto")
{
    /// <summary>"City, Region" or "City, Country" for display.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Admin)
            ? (string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}")
            : $"{Name}, {Admin}";

    /// <summary>Great-circle distance to another point, in kilometres (haversine).</summary>
    public double DistanceKmTo(double lat, double lon)
    {
        const double R = 6371.0;
        double dLat = ToRad(lat - Latitude), dLon = ToRad(lon - Longitude);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(Latitude)) * Math.Cos(ToRad(lat))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    static double ToRad(double deg) => deg * Math.PI / 180.0;
}

/// <summary>A single weather observation/forecast snapshot. Temperatures in °C, wind in km/h.</summary>
public sealed record WeatherReading(
    double TempC,
    double FeelsLikeC,
    int Humidity,
    double WindKph,
    int WeatherCode,
    bool IsDay,
    double HighC,
    double LowC,
    DateTimeOffset ObservedAt,
    // Added later — defaulted so old cached JSON (without these) still deserializes.
    double UvIndex = 0,
    IReadOnlyList<DailyForecast>? Daily = null,
    // IANA id (e.g. "Europe/Berlin"), resolved by whichever source served this reading — lets the
    // UI show the *location's* current local time rather than the device's own, and stays correct
    // across DST since it's looked up fresh at display time rather than stored as a fixed offset.
    string? TimezoneId = null)
{
    public WeatherVisual Visual => WeatherCodes.Describe(WeatherCode);

    /// <summary>The multi-day outlook, never null (empty when a source didn't supply one).</summary>
    public IReadOnlyList<DailyForecast> Forecast => Daily ?? Array.Empty<DailyForecast>();

    /// <summary>WHO/EPA UV exposure band for the current UV index.</summary>
    public string UvLabel => UvCategory(UvIndex);

    public static double ToFahrenheit(double c) => c * 9.0 / 5.0 + 32.0;

    /// <summary>Standard UV-index risk band: Low / Moderate / High / Very High / Extreme.</summary>
    public static string UvCategory(double uv) => uv switch
    {
        < 3  => "Low",
        < 6  => "Moderate",
        < 8  => "High",
        < 11 => "Very High",
        _    => "Extreme",
    };
}

/// <summary>One day in the multi-day outlook. Temperatures in °C.</summary>
public sealed record DailyForecast(
    DateOnly Date,
    double HighC,
    double LowC,
    int WeatherCode,
    double UvIndexMax)
{
    public WeatherVisual Visual => WeatherCodes.Describe(WeatherCode);

    /// <summary>Short weekday label for the UI, e.g. "Mon" (today reads "Today").</summary>
    public string ShortDay =>
        Date == DateOnly.FromDateTime(DateTime.Now)
            ? "Today"
            : Date.ToString("ddd", CultureInfo.InvariantCulture);
}

/// <summary>Where a reading came from. Drives the "Live / Satellite / Offline" badge in the UI.</summary>
public enum DataChannel
{
    /// <summary>Fetched live over the internet (works the same over a satellite ISP like Starlink).</summary>
    Live,
    /// <summary>Decoded from a direct weather-satellite pass picked up by a local ground station.</summary>
    Satellite,
    /// <summary>Loaded from the on-device cache because nothing else was reachable.</summary>
    Cache,
}

/// <summary>A resolved weather result: the reading plus which channel produced it.</summary>
public sealed record WeatherResult(
    GeoLocation Location,
    WeatherReading Reading,
    DataChannel Channel,
    string SourceName,
    string Note);

/// <summary>How to render a weather code: a friendly label, an emoji, and a scene category + intensities.</summary>
public readonly record struct WeatherVisual(
    string Label,
    string Emoji,
    WeatherKind Kind,
    int RainIntensity,
    int SnowIntensity);

/// <summary>The family of scene the renderer should draw.</summary>
public enum WeatherKind { Clear, FewClouds, Cloudy, Fog, Rain, Snow, Storm }

public sealed class WeatherUnavailableException : Exception
{
    public WeatherUnavailableException(string message) : base(message) { }
}
