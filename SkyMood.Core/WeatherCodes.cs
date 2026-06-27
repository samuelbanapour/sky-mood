namespace SkyMood;

/// <summary>
/// Maps WMO weather-interpretation codes (the scheme Open-Meteo and most providers use) to a
/// friendly label, an emoji, a scene category, and rain/snow intensities for the animation.
/// </summary>
public static class WeatherCodes
{
    public static WeatherVisual Describe(int code) => code switch
    {
        0       => new("Clear sky",          "☀️", WeatherKind.Clear,     0,   0),
        1       => new("Mainly clear",        "🌤️", WeatherKind.FewClouds, 0,   0),
        2       => new("Partly cloudy",       "⛅",  WeatherKind.FewClouds, 0,   0),
        3       => new("Overcast",            "☁️", WeatherKind.Cloudy,    0,   0),
        45      => new("Foggy",               "🌫️", WeatherKind.Fog,       0,   0),
        48      => new("Rime fog",            "🌫️", WeatherKind.Fog,       0,   0),
        51      => new("Light drizzle",       "🌦️", WeatherKind.Rain,      30,  0),
        53      => new("Drizzle",             "🌦️", WeatherKind.Rain,      45,  0),
        55      => new("Heavy drizzle",       "🌧️", WeatherKind.Rain,      60,  0),
        56      => new("Freezing drizzle",    "🌧️", WeatherKind.Rain,      45,  0),
        57      => new("Freezing drizzle",    "🌧️", WeatherKind.Rain,      55,  0),
        61      => new("Light rain",          "🌧️", WeatherKind.Rain,      50,  0),
        63      => new("Rain",                "🌧️", WeatherKind.Rain,      80,  0),
        65      => new("Heavy rain",          "🌧️", WeatherKind.Rain,      120, 0),
        66      => new("Freezing rain",       "🌧️", WeatherKind.Rain,      70,  0),
        67      => new("Freezing rain",       "🌧️", WeatherKind.Rain,      90,  0),
        71      => new("Light snow",          "🌨️", WeatherKind.Snow,      0,   30),
        73      => new("Snow",                "❄️", WeatherKind.Snow,      0,   55),
        75      => new("Heavy snow",          "❄️", WeatherKind.Snow,      0,   90),
        77      => new("Snow grains",         "🌨️", WeatherKind.Snow,      0,   40),
        80      => new("Light showers",       "🌦️", WeatherKind.Rain,      50,  0),
        81      => new("Showers",             "🌧️", WeatherKind.Rain,      80,  0),
        82      => new("Violent showers",     "⛈️", WeatherKind.Storm,     130, 0),
        85      => new("Snow showers",        "🌨️", WeatherKind.Snow,      0,   60),
        86      => new("Heavy snow showers",  "❄️", WeatherKind.Snow,      0,   100),
        95      => new("Thunderstorm",        "⛈️", WeatherKind.Storm,     90,  0),
        96      => new("Thunderstorm + hail", "⛈️", WeatherKind.Storm,     110, 0),
        99      => new("Severe thunderstorm", "⛈️", WeatherKind.Storm,     140, 0),
        _       => new("Unknown",             "🌡️", WeatherKind.Cloudy,    0,   0),
    };
}
