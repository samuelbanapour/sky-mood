import Foundation
import CoreLocation
import Combine

struct WeatherReading {
    let tempC: Double
    let feelsLikeC: Double
    let highC: Double
    let lowC: Double
    let code: Int
    let isDay: Bool
    let uvIndex: Double
    let daily: [DailyForecastSummary]
    let timezoneId: String?

    static func display(_ celsius: Double, fahrenheit: Bool) -> Int {
        Int((fahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius).rounded())
    }

    /// Mirrors SkyMood.Core's WeatherReading.UvCategory so the watch's UV band matches the phone.
    static func uvBand(_ uv: Double) -> String {
        switch uv {
        case ..<3: return "Low"
        case ..<6: return "Moderate"
        case ..<8: return "High"
        case ..<11: return "Very High"
        default: return "Extreme"
        }
    }

    /// The location's current local time (not the watch's own) — looked up fresh so it stays
    /// correct across DST, mirroring MainPage.xaml.cs's LocalTimeText on the phone.
    func localTimeText() -> String {
        if let tz = timezoneId, let zone = TimeZone(identifier: tz) {
            let formatter = DateFormatter()
            formatter.timeZone = zone
            formatter.timeStyle = .short
            formatter.dateStyle = .none
            return formatter.string(from: Date())
        }
        let formatter = DateFormatter()
        formatter.timeStyle = .short
        formatter.dateStyle = .none
        return formatter.string(from: Date()) // no resolved zone — watch's own local time
    }
}

@MainActor
final class WeatherModel: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var reading: WeatherReading?
    @Published var placeName: String = ""
    @Published var errorText: String?
    @Published var isLoading = false
    @Published var places: [PlaceSummary] = []
    @Published var isFahrenheit = false

    private let manager = CLLocationManager()

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyKilometer
        WatchSessionManager.shared.onPayload = { [weak self] payload in
            self?.apply(payload)
        }
        WatchSessionManager.shared.activate()
    }

    /// Phone-first refresh: asks the paired SkyMood.App for its current city/weather. Falls back
    /// to the watch's own GPS + direct Open-Meteo fetch if the phone isn't reachable — that path
    /// is exactly what this app did before it had a companion at all.
    func refresh() {
        isLoading = true
        errorText = nil
        WatchSessionManager.shared.requestRefresh { [weak self] payload in
            guard let self else { return }
            if let payload {
                self.apply(payload)
            } else {
                self.start()
            }
        }
    }

    func selectPlace(_ place: PlaceSummary) {
        isLoading = true
        errorText = nil
        WatchSessionManager.shared.selectPlace(place) { [weak self] payload in
            guard let self else { return }
            if let payload {
                self.apply(payload)
            } else {
                self.isLoading = false
                self.errorText = "Phone unreachable — open Sky Mood on your phone."
            }
        }
    }

    private func apply(_ payload: PhonePayload) {
        reading = WeatherReading(
            tempC: payload.tempC,
            feelsLikeC: payload.feelsLikeC,
            highC: payload.highC,
            lowC: payload.lowC,
            code: payload.code,
            isDay: payload.isDay,
            uvIndex: payload.uvIndex,
            daily: payload.daily,
            timezoneId: payload.timezoneId
        )
        placeName = payload.placeName
        places = payload.places
        isFahrenheit = payload.isFahrenheit
        isLoading = false
        errorText = nil
    }

    /// Direct GPS + Open-Meteo fetch — the watch's original standalone behavior, kept as the
    /// fallback for whenever the phone companion isn't reachable.
    func start() {
        isLoading = true
        errorText = nil
        manager.requestWhenInUseAuthorization()
        manager.requestLocation()
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        Task { @MainActor in
            await self.fetch(lat: loc.coordinate.latitude, lon: loc.coordinate.longitude)
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        Task { @MainActor in
            self.isLoading = false
            self.errorText = "Location unavailable"
        }
    }

    /// Open-Meteo first, NOAA (US only) second — same two-source resilience as the phone's
    /// WeatherService chain, for whenever the watch is on its own (phone unreachable).
    private func fetch(lat: Double, lon: Double) async {
        if let reading = try? await fetchOpenMeteo(lat: lat, lon: lon) {
            self.reading = reading
            self.reverseGeocode(lat: lat, lon: lon)
            self.isLoading = false
            return
        }
        if let reading = try? await fetchNOAA(lat: lat, lon: lon) {
            self.reading = reading
            self.reverseGeocode(lat: lat, lon: lon)
            self.isLoading = false
            return
        }
        errorText = "Couldn't reach any weather service"
        isLoading = false
    }

    private func fetchOpenMeteo(lat: Double, lon: Double) async throws -> WeatherReading {
        // Same fields/window as SkyMood.Core's OnlineWeatherSource, so standalone mode (phone
        // unreachable) shows the same feels-like/UV/7-day data as the companion path does.
        let urlString = "https://api.open-meteo.com/v1/forecast?latitude=\(lat)&longitude=\(lon)" +
            "&current=temperature_2m,apparent_temperature,is_day,weather_code,uv_index" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,uv_index_max" +
            "&forecast_days=7&timezone=auto"

        guard let url = URL(string: urlString) else { throw WeatherFetchError.unavailable }

        let (data, _) = try await URLSession.shared.data(from: url)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        guard let current = json?["current"] as? [String: Any],
              let daily = json?["daily"] as? [String: Any] else {
            throw WeatherFetchError.unavailable
        }
        let temp = current["temperature_2m"] as? Double ?? 0
        let feels = current["apparent_temperature"] as? Double ?? temp
        let code = (current["weather_code"] as? NSNumber)?.intValue ?? 0
        let isDay = (current["is_day"] as? NSNumber)?.intValue == 1
        let uv = current["uv_index"] as? Double ?? 0
        let timezoneId = json?["timezone"] as? String
        let highs = daily["temperature_2m_max"] as? [Double] ?? []
        let lows = daily["temperature_2m_min"] as? [Double] ?? []
        let times = daily["time"] as? [String] ?? []
        let codes = daily["weather_code"] as? [NSNumber] ?? []
        let uvMaxes = daily["uv_index_max"] as? [Double] ?? []

        let dayFormatter = DateFormatter()
        dayFormatter.dateFormat = "EEE"
        let dateParser = DateFormatter()
        dateParser.dateFormat = "yyyy-MM-dd"
        let todayString = dateParser.string(from: Date())

        var forecast: [DailyForecastSummary] = []
        for i in 0..<times.count {
            let label = times[i] == todayString ? "Today"
                : dateParser.date(from: times[i]).map { dayFormatter.string(from: $0) } ?? times[i]
            forecast.append(DailyForecastSummary(
                day: label,
                code: i < codes.count ? codes[i].intValue : code,
                highC: i < highs.count ? highs[i] : temp,
                lowC: i < lows.count ? lows[i] : temp,
                uv: i < uvMaxes.count ? uvMaxes[i] : uv
            ))
        }

        return WeatherReading(
            tempC: temp,
            feelsLikeC: feels,
            highC: highs.first ?? temp,
            lowC: lows.first ?? temp,
            code: code,
            isDay: isDay,
            uvIndex: uv,
            daily: forecast,
            timezoneId: timezoneId
        )
    }

    // ---------------- NOAA/NWS fallback (US only) ----------------
    // Mirrors SkyMood.Core/Sources/NoaaWeatherSource.cs — same 4-call flow and icon→WMO-code
    // mapping, duplicated here rather than shared since this codebase already keeps the MAUI
    // (C#) and native-Swift watch sides as separate implementations throughout.

    enum WeatherFetchError: Error { case unavailable }

    private func fetchNOAA(lat: Double, lon: Double) async throws -> WeatherReading {
        func noaaRequest(_ url: URL) -> URLRequest {
            var req = URLRequest(url: url)
            req.setValue("(SkyMood weather app, samuel.banapour100@gmail.com)", forHTTPHeaderField: "User-Agent")
            req.timeoutInterval = 6
            return req
        }

        func getJSON(_ urlString: String) async throws -> [String: Any]? {
            guard let url = URL(string: urlString) else { return nil }
            let (data, response) = try await URLSession.shared.data(for: noaaRequest(url))
            guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else { return nil }
            return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        }

        guard let points = try await getJSON("https://api.weather.gov/points/\(lat),\(lon)"),
              let props = points["properties"] as? [String: Any],
              let gridId = props["gridId"] as? String,
              let gridX = props["gridX"] as? Int,
              let gridY = props["gridY"] as? Int,
              let stationsUrl = props["observationStations"] as? String
        else { throw WeatherFetchError.unavailable } // e.g. outside NWS/US coverage
        let timezoneId = props["timeZone"] as? String

        guard let stations = try await getJSON(stationsUrl),
              let features = stations["features"] as? [[String: Any]],
              let stationUrl = features.first?["id"] as? String
        else { throw WeatherFetchError.unavailable }

        guard let obs = try await getJSON("\(stationUrl)/observations/latest"),
              let obsProps = obs["properties"] as? [String: Any]
        else { throw WeatherFetchError.unavailable }

        func value(_ key: String) -> Double? {
            (obsProps[key] as? [String: Any])?["value"] as? Double
        }

        guard let temp = value("temperature") else { throw WeatherFetchError.unavailable }
        let feelsLike = value("heatIndex") ?? value("windChill") ?? temp
        let (currentSlug, isDay) = Self.parseNoaaIcon(obsProps["icon"] as? String)
        let currentCode = Self.noaaCode(for: currentSlug)

        let grid = try? await getJSON("https://api.weather.gov/gridpoints/\(gridId)/\(gridX),\(gridY)")
        let forecastJSON = try? await getJSON("https://api.weather.gov/gridpoints/\(gridId)/\(gridX),\(gridY)/forecast")

        let (dates, highs, lows) = Self.parseNoaaDailyMinMax(grid ?? nil)
        let dayCodes = Self.parseNoaaDaytimeCodes(forecastJSON ?? nil)

        let dayFormatter = DateFormatter()
        dayFormatter.dateFormat = "EEE"
        let dateParser = DateFormatter()
        dateParser.dateFormat = "yyyy-MM-dd"
        let todayString = dateParser.string(from: Date())

        var forecast: [DailyForecastSummary] = []
        for i in 0..<dates.count {
            let label = dates[i] == todayString ? "Today"
                : dateParser.date(from: dates[i]).map { dayFormatter.string(from: $0) } ?? dates[i]
            forecast.append(DailyForecastSummary(
                day: label,
                code: i < dayCodes.count ? dayCodes[i] : currentCode,
                highC: i < highs.count ? highs[i] : temp,
                lowC: i < lows.count ? lows[i] : temp,
                uv: 0
            ))
        }

        return WeatherReading(
            tempC: temp,
            feelsLikeC: feelsLike,
            highC: highs.first ?? temp,
            lowC: lows.first ?? temp,
            code: currentCode,
            isDay: isDay,
            uvIndex: 0,
            daily: forecast,
            timezoneId: timezoneId
        )
    }

    /// NWS icon URLs look like ".../icons/land/night/bkn?size=medium" or
    /// ".../land/day/rain_showers,40?size=medium" (trailing ",NN" is a precipitation chance).
    private static func parseNoaaIcon(_ iconUrl: String?) -> (slug: String, isDay: Bool) {
        guard let iconUrl, !iconUrl.isEmpty else { return ("", true) }
        let parts = iconUrl.split(separator: "/").map(String.init)
        guard parts.count >= 2 else { return ("", true) }
        let last = parts[parts.count - 1].split(separator: "?").first.map(String.init) ?? ""
        let slug = last.split(separator: ",").first.map(String.init) ?? ""
        let isDay = parts[parts.count - 2] != "night"
        return (slug, isDay)
    }

    /// Approximate NWS icon slug → WMO weather code (see NoaaWeatherSource.cs for the same table).
    private static func noaaCode(for slug: String) -> Int {
        switch slug {
        case "skc": return 0
        case "few": return 1
        case "sct": return 2
        case "bkn", "ovc", "cold": return 3
        case "fog", "dust", "smoke", "haze": return 45
        case "rain": return 63
        case "rain_showers", "rain_showers_hi": return 80
        case "snow": return 73
        case "rain_snow": return 71
        case "sleet", "rain_sleet", "snow_sleet": return 67
        case "fzra", "rain_fzra", "snow_fzra": return 66
        case "tsra", "tsra_sct", "tsra_hi": return 95
        case "tornado", "hurricane", "tropical_storm": return 99
        case "blizzard": return 75
        case "hot": return 0
        default:
            if slug.hasPrefix("wind_") { return noaaCode(for: String(slug.dropFirst(5))) }
            return 3
        }
    }

    /// Groups the raw grid's maxTemperature/minTemperature series by calendar date (UTC).
    private static func parseNoaaDailyMinMax(_ grid: [String: Any]?) -> (dates: [String], highs: [Double], lows: [Double]) {
        guard let grid, let props = grid["properties"] as? [String: Any] else { return ([], [], []) }

        func series(_ key: String) -> [(date: String, value: Double)] {
            guard let s = props[key] as? [String: Any], let values = s["values"] as? [[String: Any]] else { return [] }
            let iso = ISO8601DateFormatter()
            let dateOnly = DateFormatter()
            dateOnly.dateFormat = "yyyy-MM-dd"
            dateOnly.timeZone = TimeZone(identifier: "UTC")
            var result: [(String, Double)] = []
            for v in values {
                guard let validTime = v["validTime"] as? String,
                      let value = v["value"] as? Double,
                      let startStr = validTime.split(separator: "/").first,
                      let start = iso.date(from: String(startStr)) else { continue }
                result.append((dateOnly.string(from: start), value))
            }
            return result
        }

        var highByDate: [String: Double] = [:]
        var order: [String] = []
        for (d, v) in series("maxTemperature") where highByDate[d] == nil {
            highByDate[d] = v
            order.append(d)
        }
        var lowByDate: [String: Double] = [:]
        for (d, v) in series("minTemperature") where lowByDate[d] == nil {
            lowByDate[d] = v
        }

        order.sort()
        let take = Array(order.prefix(7))
        return (take, take.map { highByDate[$0] ?? 0 }, take.map { lowByDate[$0] ?? 0 })
    }

    /// Daytime-period icons in chronological order, positionally paired with the grid's per-day
    /// highs/lows (NWS's local-time period dates and the grid's UTC dates don't line up cleanly).
    private static func parseNoaaDaytimeCodes(_ forecast: [String: Any]?) -> [Int] {
        guard let forecast,
              let props = forecast["properties"] as? [String: Any],
              let periods = props["periods"] as? [[String: Any]] else { return [] }
        var codes: [Int] = []
        for p in periods {
            if let isDaytime = p["isDaytime"] as? Bool, isDaytime {
                let (slug, _) = parseNoaaIcon(p["icon"] as? String)
                codes.append(noaaCode(for: slug))
            }
        }
        return codes
    }

    private func reverseGeocode(lat: Double, lon: Double) {
        let geocoder = CLGeocoder()
        let location = CLLocation(latitude: lat, longitude: lon)
        geocoder.reverseGeocodeLocation(location) { [weak self] placemarks, _ in
            guard let self, let place = placemarks?.first else { return }
            Task { @MainActor in
                self.placeName = place.locality ?? place.name ?? ""
            }
        }
    }
}
