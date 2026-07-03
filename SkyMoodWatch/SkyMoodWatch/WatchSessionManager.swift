import Foundation
import WatchConnectivity

struct PlaceSummary: Identifiable {
    var id: String { name }
    let name: String
    let lat: Double
    let lon: Double
    let tempC: Double
    let code: Int
}

struct DailyForecastSummary: Identifiable {
    var id: String { day }
    let day: String
    let code: Int
    let highC: Double
    let lowC: Double
    let uv: Double
}

struct PhonePayload {
    let lat: Double
    let lon: Double
    let placeName: String
    let tempC: Double
    let feelsLikeC: Double
    let highC: Double
    let lowC: Double
    let code: Int
    let isDay: Bool
    let isFahrenheit: Bool
    let uvIndex: Double
    let daily: [DailyForecastSummary]
    let places: [PlaceSummary]
}

/// Talks to the SkyMood.App companion over WatchConnectivity. Every call degrades gracefully —
/// if the phone isn't reachable (not installed, out of range, or just not paired this way in a
/// dev/free-signed build), callers fall back to the watch's own direct Open-Meteo fetch.
@MainActor
final class WatchSessionManager: NSObject, WCSessionDelegate {
    static let shared = WatchSessionManager()

    var onPayload: ((PhonePayload) -> Void)?

    private override init() {
        super.init()
    }

    func activate() {
        guard WCSession.isSupported() else { return }
        WCSession.default.delegate = self
        WCSession.default.activate()
    }

    nonisolated func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: Error?) {
        // A context may already be sitting there from before this launch (e.g. app was killed
        // and relaunched) — apply it immediately instead of waiting for a fresh push.
        let context = session.receivedApplicationContext
        guard let payload = Self.decode(context) else { return }
        Task { @MainActor in self.onPayload?(payload) }
    }

    nonisolated func session(_ session: WCSession, didReceiveApplicationContext applicationContext: [String: Any]) {
        guard let payload = Self.decode(applicationContext) else { return }
        Task { @MainActor in self.onPayload?(payload) }
    }

    func requestRefresh(completion: @escaping (PhonePayload?) -> Void) {
        sendMessage(["action": "refresh"], completion: completion)
    }

    func selectPlace(_ place: PlaceSummary, completion: @escaping (PhonePayload?) -> Void) {
        sendMessage(["action": "selectPlace", "lat": place.lat, "lon": place.lon, "name": place.name], completion: completion)
    }

    private func sendMessage(_ message: [String: Any], completion: @escaping (PhonePayload?) -> Void) {
        guard WCSession.default.activationState == .activated, WCSession.default.isReachable else {
            completion(nil)
            return
        }
        WCSession.default.sendMessage(message, replyHandler: { reply in
            let payload = Self.decode(reply)
            Task { @MainActor in completion(payload) }
        }, errorHandler: { _ in
            Task { @MainActor in completion(nil) }
        })
    }

    private nonisolated static func decode(_ dict: [String: Any]) -> PhonePayload? {
        guard !dict.isEmpty,
              let lat = dict["lat"] as? Double,
              let lon = dict["lon"] as? Double,
              let tempC = dict["tempC"] as? Double,
              let code = dict["code"] as? Int else { return nil }

        let placesArr = dict["places"] as? [[String: Any]] ?? []
        let places = placesArr.compactMap { p -> PlaceSummary? in
            guard let n = p["name"] as? String,
                  let pl = p["lat"] as? Double,
                  let po = p["lon"] as? Double,
                  let pt = p["tempC"] as? Double,
                  let pc = p["code"] as? Int else { return nil }
            return PlaceSummary(name: n, lat: pl, lon: po, tempC: pt, code: pc)
        }

        let dailyArr = dict["daily"] as? [[String: Any]] ?? []
        let daily = dailyArr.compactMap { d -> DailyForecastSummary? in
            guard let day = d["day"] as? String,
                  let dc = d["code"] as? Int,
                  let dh = d["highC"] as? Double,
                  let dl = d["lowC"] as? Double else { return nil }
            return DailyForecastSummary(day: day, code: dc, highC: dh, lowC: dl, uv: d["uv"] as? Double ?? 0)
        }

        return PhonePayload(
            lat: lat,
            lon: lon,
            placeName: dict["placeName"] as? String ?? "",
            tempC: tempC,
            feelsLikeC: dict["feelsLikeC"] as? Double ?? tempC,
            highC: dict["highC"] as? Double ?? tempC,
            lowC: dict["lowC"] as? Double ?? tempC,
            code: code,
            isDay: dict["isDay"] as? Bool ?? true,
            isFahrenheit: dict["fahrenheit"] as? Bool ?? false,
            uvIndex: dict["uvIndex"] as? Double ?? 0,
            daily: daily,
            places: places
        )
    }
}
