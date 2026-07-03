import Foundation

/// Mirrors SkyMood.Core/WeatherCodes.cs so the watch shows the same label + emoji as the phone app.
enum WeatherCodes {
    static func describe(_ code: Int) -> (label: String, emoji: String) {
        switch code {
        case 0: return ("Clear sky", "☀️")
        case 1: return ("Mainly clear", "🌤️")
        case 2: return ("Partly cloudy", "⛅")
        case 3: return ("Overcast", "☁️")
        case 45: return ("Foggy", "🌫️")
        case 48: return ("Rime fog", "🌫️")
        case 51: return ("Light drizzle", "🌦️")
        case 53: return ("Drizzle", "🌦️")
        case 55: return ("Heavy drizzle", "🌧️")
        case 56, 57: return ("Freezing drizzle", "🌧️")
        case 61: return ("Light rain", "🌧️")
        case 63: return ("Rain", "🌧️")
        case 65: return ("Heavy rain", "🌧️")
        case 66, 67: return ("Freezing rain", "🌧️")
        case 71: return ("Light snow", "🌨️")
        case 73: return ("Snow", "❄️")
        case 75: return ("Heavy snow", "❄️")
        case 77: return ("Snow grains", "🌨️")
        case 80: return ("Light showers", "🌦️")
        case 81: return ("Showers", "🌧️")
        case 82: return ("Violent showers", "⛈️")
        case 85: return ("Snow showers", "🌨️")
        case 86: return ("Heavy snow showers", "❄️")
        case 95: return ("Thunderstorm", "⛈️")
        case 96: return ("Thunderstorm + hail", "⛈️")
        case 99: return ("Severe thunderstorm", "⛈️")
        default: return ("Unknown", "🌡️")
        }
    }
}
