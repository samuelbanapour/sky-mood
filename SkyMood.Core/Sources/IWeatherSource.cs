namespace SkyMood.Sources;

/// <summary>
/// A place weather can come from. The <see cref="WeatherService"/> tries sources in order and
/// uses the first that returns a reading, so each source returns <c>null</c> (rather than throwing)
/// when it simply has no data for this location right now.
/// </summary>
public interface IWeatherSource
{
    string Name { get; }
    DataChannel Channel { get; }

    /// <summary>Returns a reading for the location, or <c>null</c> if this source can't serve it.</summary>
    Task<WeatherReading?> TryGetAsync(GeoLocation location, CancellationToken ct = default);
}
