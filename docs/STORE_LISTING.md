# Store listing kit — Google Play & Microsoft Store

Everything you need to take Sky Mood from a built package to a live store listing. Pairs with
[`RELEASE.md`](RELEASE.md), which covers the **build/signing/CI** side; this file covers the
**store-front content** and the manual console steps only you can do (you have a Google Play
account and a paid Microsoft Partner Center account; the free Apple account can't do App Store, so
iOS/macOS stay direct-download).

---

## 0. Artifact verification (checked against `dist/`)

| Package | Identity | Version | Signing | Verdict |
|---|---|---|---|---|
| `SkyMood-0.1.6-upload.aab` (Play) | `com.gamedevsolo.skymood`, minSdk 24 / targetSdk 36, ABIs arm64-v8a + armeabi-v7a + x86_64 | versionCode **1**, versionName **1.0** | signed V3, upload key `CN=Sky Mood, O=GameDevSolo, C=US` (`jar verified`) | ✅ uploadable |
| `SkyMood.App_1.0.0.0_x64.msix` (Store) | `Name=com.gamedevsolo.skymood`, `Publisher=CN=GameDevSolo`, x64 only | `1.0.0.0` | **unsigned** | ⚠️ identity must be swapped — see §1 |

Upload-key SHA-256 (register under Play App Signing): `ee71c8d414356f020d7b364a26704e0c50de4642bfb8bb8efc8f80e224fedd5f`

Two things to know before uploading:
- **Version reads `1.0`, not `0.1.6`** — these were built before the `v0.1.6` tag. Fine for a first
  submission (Play orders by versionCode, not name). To make the store show `0.1.6`, re-run
  `release.yml` from the `v0.1.6` tag (it derives the version from the tag) and use those artifacts.
- **MSIX is unsigned** — that's correct for the Store (Partner Center re-signs with the Store cert).
  You only sign an MSIX yourself for *sideloading*.

---

## 1. ⚠️ Required fix before Microsoft Store will accept the MSIX

The Store rejects a package whose identity doesn't match the one Partner Center reserves for you.
After you **reserve the app name** in Partner Center, open
**Product → Product identity** and copy the three assigned values into
[`SkyMood.App/Platforms/Windows/Package.appxmanifest`](../SkyMood.App/Platforms/Windows/Package.appxmanifest):

| Manifest field (current placeholder) | Replace with Partner Center value |
|---|---|
| `<Identity Name="com.gamedevsolo.skymood" …>` | **Package/Identity/Name** (e.g. `12345GameDevSolo.SkyMood`) |
| `<Identity Publisher="CN=GameDevSolo" …>` | **Package/Identity/Publisher** (e.g. `CN=ABCD1234-…`) |
| `<PublisherDisplayName>GameDevSolo</…>` | **Package/Properties/PublisherDisplayName** |

Then rebuild the Windows package (Release, `-p:Heads=windows`) — or set `WindowsPackageType=Msix`
and let `release.yml` produce the `.msix`/`.msixupload` — and upload **that** to the submission.
Leave it unsigned; the Store signs it.

> Tip: a `.msixupload` (or `.msixbundle`) covering **x64 + arm64** widens device reach. The current
> package is x64-only, which is acceptable but excludes Arm Windows devices.

---

## 2. Google Play

### 2a. Listing copy (paste into Play Console → Main store listing)

- **App name:** `Sky Mood`
- **Short description** (≤80 chars):
  `Animated live weather with 7-day forecast & UV — online, satellite or offline.`
- **Full description** (≤4000 chars):

```
Sky Mood turns your local weather into a living scene. The sky, sun, moon, clouds, rain, snow and
storms animate to match real conditions right now — a calm, beautiful way to check the forecast.

WHAT YOU GET
• Real-time conditions: temperature, feels-like, humidity and wind
• 7-day forecast with daily high/low and a colour-coded UV index
• Current UV index with the standard Low → Extreme risk band
• Animated scenes that change with the weather and time of day
• Search any city, or use your location with one tap
• Saved places strip for the spots you check most — swipe and remove any time
• Works offline: your last reading is cached so it still loads with no signal
• Satellite-capable: reads locally-decoded weather-satellite passes if you have a ground station

PRIVATE BY DESIGN
• No accounts, no sign-in, no ads, no tracking, no analytics
• Your saved places and settings stay on your device
• Location is optional and used only to look up the weather

Weather data by Open-Meteo. °C/°F toggle included.
```

- **App category:** Weather · **Tags:** weather, forecast, UV index
- **Contact email:** `samuel.banapour100@gmail.com`
- **Privacy policy URL:** `https://get-skymood.vercel.app/privacy` (already live)

### 2b. Content rating (IARC questionnaire)
All content questions → **No** (no violence, sexuality, language, controlled substances, gambling,
or user-to-user interaction). Expected result: **Everyone / PEGI 3**.

### 2c. Data safety form (recommended answers — verify before final submit)
Grounded in the privacy policy. The app sends coordinates / search text to Open-Meteo (and
BigDataCloud on web) only to fetch weather; nothing is stored on a server, no account, no ads.

| Question | Answer |
|---|---|
| Does your app collect or share user data? | **Yes** (location only) |
| Data type | **Location → Approximate + Precise** |
| Collected or shared? | Collected. **Not shared** for a third party's own use. Mark **"processed ephemerally"** if you confirm Open-Meteo doesn't retain it — that exempts it from "collected". |
| Purpose | **App functionality** only |
| Linked to a user's identity? | **No** (no accounts) |
| Used for tracking / ads? | **No** |
| Is data encrypted in transit? | **Yes** (HTTPS) |
| Can users request deletion? | Data is on-device; clearing app data removes it |

> The only genuinely judgement-y box is whether sending location to Open-Meteo counts as "shared".
> Most weather apps declare it App-functionality + ephemeral and **not shared**; pick that if you're
> comfortable, otherwise declare it shared with Open-Meteo. Either is defensible.

### 2d. Graphics & screenshots (specs)
- **App icon:** 512×512 PNG (32-bit) — use the master from the icon matrix.
- **Feature graphic:** 1024×500 PNG/JPG (required).
- **Phone screenshots:** 2–8, PNG/JPG, 16:9 or 9:16, each side 320–3840 px. Capture: hero readout,
  the 7-day forecast strip, the UV tile, saved-places with the ✕, an offline/“OFFLINE” state.
- (Optional) 7-inch & 10-inch tablet screenshots widen eligibility.

### 2e. First-submission checklist
1. Create the app in [Play Console](https://play.google.com/console) → package `com.gamedevsolo.skymood`.
2. Enrol in **Play App Signing**; register the upload key (SHA-256 above).
3. Complete: Main store listing (§2a), Content rating (§2b), Data safety (§2c), Target audience,
   Privacy policy URL, Ads = No, App access (no login needed).
4. Create a release on the **Internal testing** track; upload `SkyMood-0.1.6-upload.aab`.
5. Roll out to internal testers, sanity-check, then promote to Production.
6. After the first manual upload, `release.yml` can auto-upload future tags (see §4).

---

## 3. Microsoft Store (Partner Center)

### 3a. Listing copy (Partner Center → Store listings)
- **Product name:** `Sky Mood`
- **Short description:** `Live, animated weather with a 7-day forecast and UV index — even offline.`
- **Description:** reuse the Play full description (§2a) — Partner Center allows up to 10,000 chars.
- **Category:** Weather
- **Search terms:** weather, forecast, UV index, animated weather, offline weather
- **Privacy policy URL:** `https://get-skymood.vercel.app/privacy`

### 3b. Identity & package
- Do the §1 manifest fix first, then upload the unsigned `.msix`/`.msixupload`.
- Under **Properties → App declarations**, the `location` device capability is already in the
  manifest; be ready to justify it ("used to fetch local weather; optional").

### 3c. Age rating
Partner Center uses the IARC questionnaire too — same all-**No** answers → **Everyone**.

### 3d. Screenshots
- **Desktop screenshots:** at least 1, PNG, 1366×768 or larger (3840×2160 max). Capture the Windows
  app showing the hero readout + forecast strip + UV tile. Up to 9.
- **Store logos:** Partner Center pulls tile assets from the package; ensure the §1 rebuild includes
  real logo PNGs (the manifest currently has `$placeholder$.png` tokens that MAUI fills at build).

### 3e. First-submission checklist
1. In [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games → New product →
   MSIX/PWA app**; reserve the name **Sky Mood**.
2. Copy the **Product identity** values into the manifest (§1) and rebuild the package.
3. New submission → **Packages**: upload the `.msix`/`.msixupload`.
4. Fill Pricing (Free), Properties (category, declarations), Age ratings (§3c), Store listing (§3a).
5. Submit for certification.

---

## 4. CI auto-upload & version

- **Secrets / signing:** all documented in [`RELEASE.md`](RELEASE.md) — Android keystore + Play
  service account (`PLAY_SERVICE_ACCOUNT_JSON`) auto-upload to the chosen track; `WINDOWS_PFX_*`
  signs the MSIX.
- **Versioning:** `release.yml` derives the version from the git tag (`v0.1.6` → display `0.1.6`),
  so tagged CI builds carry the right version automatically — unlike the pre-tag `dist/` artifacts.
- **Two known gaps:**
  - **Microsoft Store submission is manual** — `release.yml` produces the MSIX but doesn't push to
    Partner Center (needs your Azure AD app creds + an existing listing). Add a
    `microsoft/store-submission` step once the listing exists if you want it automated.
  - **Apple jobs stay unsigned** — with only a free Apple account, the iOS/macOS-notarize secrets
    can't be created, so those jobs degrade to unsigned artifacts (direct download only). Not a
    blocker for Play/Microsoft.

---

*Privacy policy lives at `web/privacy.html` (deployed at /privacy). Keep its "Last updated" date in
sync if data practices change.*
