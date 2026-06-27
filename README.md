# Sky Mood 🌤️

A fun, animated weather app built in **C# / .NET 10 + .NET MAUI** — see your local sky come alive,
look up any place in the world, and keep working whether you're on home Wi-Fi, a satellite ISP, a
direct weather-satellite ground station, or fully offline.

Same shape as the *Echoes of the Returning* solution next door: a shared **Core** library with two
front-ends (a graphical **MAUI App** and a **Console** head for testing).

```
sky-mood/
├─ SkyMood.Core/        net10.0 — models, data sources, geocoding, orchestration (no UI)
├─ SkyMood.Console/     net10.0 — terminal head; verifies the whole data layer fast
└─ SkyMood.App/         MAUI head (Android / iOS / Mac Catalyst / Windows) — the animated app
```

## What it does

- **Live local weather** from the free [Open-Meteo](https://open-meteo.com) API (no API key, no signup).
- **Animated sky** drawn on a MAUI `GraphicsView`: pulsing sun, moon + twinkling stars at night,
  drifting clouds, falling rain, swaying snow, rolling fog, and lightning during thunderstorms — the
  scene and background gradient change to match the real conditions and day/night.
- **See other places** — search any city (`Tokyo`, `Reykjavik`, …) and pin favourites. Searched and
  viewed places are saved for offline browsing.
- **°C / °F** toggle, auto-refresh every 5 minutes, manual refresh, and a status badge:
  **LIVE**, **SATELLITE**, or **OFFLINE**.

## The three connectivity modes (and how each is handled)

`WeatherService` tries data sources in priority order and serves the first that answers, always
saving a copy to the on-device cache:

| Priority | Source | When it's used |
|---|---|---|
| 1 | **Satellite** (`SatelliteWeatherSource`) | A fresh direct pass decoded by your own ground station — **zero internet needed**. |
| 2 | **Live** (`OnlineWeatherSource`) | Open-Meteo over HTTPS. **Works transparently over a Starlink / Viasat satellite ISP** — that's just IP. Requests are tiny and time-boxed with a retry so a high-latency/flaky link fails fast instead of hanging. |
| 3 | **Cache** (`OfflineCache`) | Last reading saved on the device, so the app still shows something with **no connection at all**. |

### 1. Satellite internet (Starlink / Viasat)
Nothing special to configure — a satellite ISP is ordinary IP, so the **Live** path works over it as-is.
The networking is just built to tolerate the latency/jitter (short timeout + one retry).

### 2. Off-grid / no internet
The app keeps working: cached readings stay viewable, the city search falls back to a built-in
gazetteer of major cities, and a manual location still renders a scene. Reconnect once and everything
refreshes.

### 3. Receive weather-satellite data **directly** (NOAA / Meteor / GOES via SDR)
This is real, off-internet weather pulled straight out of the sky. The app doesn't drive the radio
itself — instead it **watches an inbox folder** where your decode pipeline drops one small JSON
sidecar per pass. Wire up the usual chain:

```
RTL-SDR dongle + antenna  →  SatDump / wxtoimg (decode)  →  your script writes pass-*.json
                                                            into the app's groundstation/ inbox
```

Inbox location (created automatically on first run):
`<AppDataDirectory>/groundstation/` on device, or pass `--inbox <dir>` to the console head.

Sidecar format — drop a file named `pass-*.json`:

```json
{
  "satellite": "NOAA-19",
  "receivedAt": "2026-06-27T10:02:00Z",
  "latitude": 51.5, "longitude": -0.12, "radiusKm": 1500,
  "tempC": 8.4, "feelsLikeC": 6.0, "humidity": 88, "windKph": 34,
  "weatherCode": 95, "isDay": false, "highC": 11, "lowC": 5
}
```

When you ask for a location, the freshest still-valid pass (within `radiusKm` of the point and newer
than 6 h) wins and the badge flips to **SATELLITE**. No pass available → it silently falls through to
Live/Cache, so it's safe to leave enabled even without hardware.

## Run it

### Console head (fast, no mobile toolchain — great for testing the data layer)
```bash
cd sky-mood
dotnet run --project SkyMood.Console/SkyMood.Console.csproj -- "Tokyo"
dotnet run --project SkyMood.Console/SkyMood.Console.csproj -- --inbox /path/to/groundstation "London"
dotnet run --project SkyMood.Console/SkyMood.Console.csproj -- --list      # everything cached offline
```

### MAUI app
```bash
./build-app.sh maccatalyst     # or: android | ios | windows  (default: maccatalyst)
```
`build-app.sh` sets the per-platform env (JDK17/Android SDK, or `DEVELOPER_DIR` for Apple) and uses
the `Heads` property so a single platform builds without the others' workloads installed.

**Build notes (this machine / volume):**
- Build output is redirected to `~/.skymood-build` by `Directory.Build.props` — this exFAT volume
  writes `._*` AppleDouble sidecars that break the Android/iOS toolchains and codesign. Keep it there.
- Workloads needed per head: `maui-android`, `maui-ios`, `maui-maccatalyst`. Apple heads need full
  **Xcode** (Command Line Tools alone aren't enough); Android needs **JDK 17** + the Android SDK.
- Run heads with the explicit `.csproj` path (not the folder) so the `._*.csproj` sidecar isn't
  picked up as a second project.

## Permissions
- **Android:** `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_COARSE/FINE_LOCATION` (in the manifest).
- **iOS / Mac Catalyst:** `NSLocationWhenInUseUsageDescription`.
- **Windows:** `location` device capability.

Location is optional — deny it and you can still search cities by name.
