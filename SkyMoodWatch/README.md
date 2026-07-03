# Sky Mood — watchOS companion

A small native SwiftUI watch app, hand-written (not a MAUI head — MAUI has no
watchOS target), using the same WMO weather-code → label/emoji mapping as
`SkyMood.Core/WeatherCodes.cs` for visual consistency with the phone app.

Now a real WatchConnectivity companion of SkyMood.App (`WKCompanionAppBundleIdentifier:
com.gamedevsolo.skymood`, embedded into the phone's device build — see
`SkyMood.App.csproj`'s `_EmbedSkyMoodWatch` target). It mirrors whatever city/weather
the phone is showing, can ask the phone to refresh, and can switch cities from a
saved-places list pushed alongside the weather — all via `WatchSessionManager.swift`
+ `SkyMood.App/Platforms/iOS/WatchConnectivityService.cs`. Falls back to the
original direct-GPS-and-Open-Meteo fetch (`WeatherModel.start()`) whenever the
phone isn't reachable, so it still works standalone too.

Xcode project generated via [XcodeGen](https://github.com/yonaskolb/XcodeGen)
from `project.yml` — edit that, not `SkyMoodWatch.xcodeproj` directly, then
run `xcodegen generate`.

## Build from an APFS location, not here

Same story as Sky Mood's iOS build and Neon Pong's Capacitor wrapper: this
repo lives on an exFAT volume, which breaks Xcode/CocoaPods tooling in
assorted ways. Copy this whole folder to a plain APFS path (e.g.
`~/.skymoodwatch-src/`) and build from there.

## Gotchas hit getting this working

- **Missing app icon**: `Assets.xcassets/AppIcon.appiconset` didn't exist
  originally — Xcode happily builds and installs a watch app with no icon
  asset at all, it just shows a blank placeholder on the watch face. Fixed
  with a single 1024×1024 PNG (`ASSETCATALOG_COMPILER_APPICON_NAME: AppIcon`
  in `project.yml` — modern watchOS supports a single-size universal icon,
  no need for the old multi-size icon set). The icon reuses Sky Mood's
  sun/cloud glyph, recentered — the original MAUI foreground SVG's transform
  assumed MAUI's own icon-safe-zone cropping and isn't centered as a flat
  square icon elsewhere.
- **`WKApplication` missing**: required for a modern (Xcode 12+/watchOS 7+)
  single-target standalone watch app. Without it, install fails with a
  vague, misleading provisioning-profile error that has nothing to do with
  actual signing.
- **`WKWatchOnly` + `WKRunsIndependentlyOfCompanionApp` together**: Apple
  explicitly rejects having both keys set ("ambiguous"). Since this app has
  no companion iPhone app at all, keep `WKWatchOnly` only.
- **`xcodebuild -destination "id=<UDID>"` doesn't work for this watch model**
  — xcodebuild's own destination resolution reports "doesn't have a known
  architecture" for this Apple Watch Series 9 (arm64e), even though
  `devicectl` sees and can target it fine. Build with
  `-destination "generic/platform=watchOS"` instead, then install/launch
  via `devicectl` directly (or, if that profile doesn't yet include this
  specific device, build+run once through Xcode's own scheme/destination
  picker in the GUI — that's what actually registers a new device against
  the account for automatic signing).
- **First device registration needs Xcode's GUI, not just CLI**: the very
  first time a new device needs adding to the provisioning profile,
  `xcodebuild -allowProvisioningUpdates` alone doesn't do it even with a
  connected/paired device. Open the project in Xcode, pick the real device
  (not a simulator) in the scheme/destination dropdown top-left, and hit ▶
  once.
- **Embedded companion apps need a bundle ID prefixed by the phone's**: a
  standalone (`WKWatchOnly`) watch app can have any bundle ID, but once it
  declares `WKCompanionAppBundleIdentifier` and gets embedded in the phone's
  `.app/Watch/` folder, install fails with "the watch app... specifies a
  bundle identifier that does not start with this app's bundle identifier
  followed by a dot" unless it's literally `<phone bundle id>.something` —
  hence `com.gamedevsolo.skymood.watchkitapp`, not the old sibling ID
  `com.gamedevsolo.skymoodwatch`. Changing it forces a fresh provisioning
  profile, but `-allowProvisioningUpdates` handled that automatically.
- **`_PostProcessAppBundle` looks like an empty SDK extension point but isn't**:
  `SkyMood.App.csproj`'s embedding target originally tried to define
  `Target Name="_PostProcessAppBundle"` directly, assuming it was an unused
  hook. It's actually real — the SDK's legacy `tools/msbuild/Xamarin.Shared.targets`
  defines it for dSYM generation/stripping, imported *after* the csproj, so it
  silently won by MSBuild's last-definition-wins rule and the embed step never
  ran (no error, no warning — the Watch folder just never appeared). Fixed by
  using `AfterTargets="_PostProcessAppBundle" BeforeTargets="Codesign"` on a
  uniquely-named target instead of trying to own the name.
- **iOS didn't auto-push the embedded companion to the paired watch** in this
  free-signed dev-build setup — waited over a minute after installing/launching
  the phone app with nothing appearing on the watch. Real App Store/TestFlight
  installs are expected to auto-push; ad-hoc/dev signing apparently doesn't (or
  at least didn't within a reasonable wait here). Fallback that does work:
  `devicectl install app` the same `SkyMoodWatch.app` directly onto the watch
  too, same as the old standalone flow, just with the new companion bundle ID.

## Deploy recipe once everything above is sorted

```
xcodebuild -project SkyMoodWatch.xcodeproj -scheme SkyMoodWatch \
  -destination "generic/platform=watchOS" -derivedDataPath ~/.skymoodwatch-src/build \
  -allowProvisioningUpdates build

# Phone app (built separately, see sky-mood/SkyMood.App) embeds this automatically via
# _EmbedSkyMoodWatch when SkyMoodWatchAppPath resolves — install/launch just the phone app:
xcrun devicectl device install app --device <iPhone UDID> path/to/SkyMood.App.app
xcrun devicectl device process launch --device <iPhone UDID> com.gamedevsolo.skymood

# If the watch doesn't pick up the companion automatically within a minute or so,
# install it directly too (same signed .app used above):
xcrun devicectl device install app --device <Watch UDID> path/to/SkyMoodWatch.app
xcrun devicectl device process launch --device <Watch UDID> com.gamedevsolo.skymood.watchkitapp
```

Watch-to-Mac dev connectivity over Wi-Fi has been consistently flaky this
session (tunnel timeouts, "disconnected immediately after connecting") —
if a command times out, it's very often just transient; retry once or twice
before assuming something's actually broken. Also: launch fails outright if
the watch screen is locked/asleep — wake it first.

Free/personal-team signing (Team `K56M484768`, same account as Sky Mood iOS
and Neon Pong) expires after ~7 days and needs reinstalling.
