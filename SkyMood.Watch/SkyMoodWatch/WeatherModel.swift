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
}

@MainActor
final class WeatherModel: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var reading: WeatherReading?
    @Published var placeName: String = ""
    @Published var errorText: String?
    @Published var isLoading = false

    private let manager = CLLocationManager()

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyKilometer
    }

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

    private func fetch(lat: Double, lon: Double) async {
        let urlString = "https://api.open-meteo.com/v1/forecast?latitude=\(lat)&longitude=\(lon)" +
            "&current=temperature_2m,apparent_temperature,is_day,weather_code" +
            "&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto"

        guard let url = URL(string: urlString) else {
            isLoading = false
            errorText = "Bad request"
            return
        }

        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            guard let current = json?["current"] as? [String: Any],
                  let daily = json?["daily"] as? [String: Any] else {
                errorText = "No data"
                isLoading = false
                return
            }
            let temp = current["temperature_2m"] as? Double ?? 0
            let feels = current["apparent_temperature"] as? Double ?? temp
            let code = (current["weather_code"] as? NSNumber)?.intValue ?? 0
            let isDay = (current["is_day"] as? NSNumber)?.intValue == 1
            let highs = daily["temperature_2m_max"] as? [Double] ?? []
            let lows = daily["temperature_2m_min"] as? [Double] ?? []

            self.reading = WeatherReading(
                tempC: temp,
                feelsLikeC: feels,
                highC: highs.first ?? temp,
                lowC: lows.first ?? temp,
                code: code,
                isDay: isDay
            )
            self.reverseGeocode(lat: lat, lon: lon)
            self.isLoading = false
        } catch {
            errorText = "Couldn't reach Open-Meteo"
            isLoading = false
        }
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
