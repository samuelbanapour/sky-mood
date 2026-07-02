# Releasing Sky Mood (signed builds → TestFlight & Play Console)

The [`release.yml`](../.github/workflows/build.yml) workflow builds **signed** store packages and
uploads them. It runs on a version tag (`git tag v1.0.0 && git push --tags`) or manually from the
**Actions → Release Sky Mood → Run workflow** button.

It degrades gracefully: with **no** secrets it still builds (unsigned) artifacts so you can confirm
the pipeline; add the secrets for a platform and that platform starts producing a **signed** build
and uploading to the store. Set secrets at **Settings → Secrets and variables → Actions**
(or `gh secret set NAME`).

> You need the paid developer accounts first: **Apple Developer Program** ($99/yr) for iOS/TestFlight
> and a **Google Play Developer** account ($25 once) for Play. Windows Store submission needs a
> **Partner Center** account; signing an MSIX for sideloading just needs a code-signing cert.

---

## 1. Android → Play Console

**A. Upload keystore** — run `./scripts/make-android-keystore.sh`. It creates `upload.keystore` and
prints the secrets to set:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | `base64 -i upload.keystore` (whole string) |
| `ANDROID_KEYSTORE_PASSWORD` | keystore password you chose |
| `ANDROID_KEY_ALIAS` | `skymood-upload` (default) |
| `ANDROID_KEY_PASSWORD` | key password you chose |

**B. Create the app in [Play Console](https://play.google.com/console)** under the **Game Dev Solo**
developer account (that's the Play/Amazon publisher identity — see `Directory.Build.props` / the
per-head `Company` override in `SkyMood.App.csproj`), with package `com.gamedevsolo.skymood`, and
**enrol in Play App Signing** (recommended). Do one manual upload of a first `.aab` if Play requires
it before the API will accept uploads.

**C. Play API service account** — in Google Cloud, create a service account with the *Google Play
Android Developer API* enabled, download its JSON key, then in Play Console → *Users & permissions*
invite that service-account email and grant **Release** access.

| Secret | Value |
|---|---|
| `PLAY_SERVICE_ACCOUNT_JSON` | the full service-account JSON |

Track is chosen by the workflow input (`internal` by default).

> **New developer account? Production is gated behind closed testing.** Since Google's 2023–2024
> policy change, a *new* Play Console developer account can't jump straight to Production (or often
> even Internal, until basic requirements are met) — it must first run a **closed test with at least
> 12 opted-in testers for 14 continuous days** before Play unlocks a Production release. Budget for
> that when planning a Sky Mood launch date: create the closed track, recruit testers (a private
> Google Group or email list works), and let it run its 14 days before expecting a production listing.

---

## 2. iOS → TestFlight

**A. App record** — in [App Store Connect](https://appstoreconnect.apple.com) create an app with
bundle id `com.gamedevsolo.skymood` (register it under *Certificates, IDs & Profiles* first).

**B. Distribution certificate + provisioning profile**
- Create an **Apple Distribution** certificate; export it as a `.p12` (with a password).
- Create an **App Store** provisioning profile for the bundle id; download the `.mobileprovision`.

| Secret | Value |
|---|---|
| `IOS_DIST_CERT_P12_BASE64` | `base64 -i dist.p12` |
| `IOS_DIST_CERT_PASSWORD` | the .p12 export password |
| `IOS_PROVISIONING_PROFILE_BASE64` | `base64 -i profile.mobileprovision` |
| `IOS_SIGNING_IDENTITY` | e.g. `Apple Distribution: Your Name (TEAMID)` |
| `IOS_PROVISIONING_PROFILE_NAME` | the profile's name |

**C. App Store Connect API key** (for the TestFlight upload) — *Users and Access → Integrations →
App Store Connect API* → create a key with **App Manager** role; download the `.p8` (once only).

| Secret | Value |
|---|---|
| `ASC_KEY_ID` | the key id |
| `ASC_ISSUER_ID` | the issuer id (top of that page) |
| `ASC_API_KEY_P8_BASE64` | `base64 -i AuthKey_XXXX.p8` |

The build produces a signed `.ipa` and `xcrun altool` uploads it to TestFlight.

---

## 3. Windows → MSIX

For **sideload/Store** signing, supply a code-signing certificate (`.pfx`). For testing you can make
a self-signed one:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Solo Apps Studio" `
  -CertStoreLocation Cert:\CurrentUser\My
Export-PfxCertificate -Cert $cert -FilePath skymood.pfx -Password (ConvertTo-SecureString -String "PFXPASS" -AsPlainText -Force)
```

| Secret | Value |
|---|---|
| `WINDOWS_PFX_BASE64` | `base64 -i skymood.pfx` |
| `WINDOWS_PFX_PASSWORD` | the .pfx password |

The job emits a signed `.msix`. **Microsoft Store** submission goes through Partner Center — once your
app is registered there, add a submission step (Store REST API / `microsoft/store-submission`); it's
left manual because it needs your Partner Center Azure AD app credentials and an existing listing.

---

## 4. macOS → notarized .dmg

The `macos-dmg` job Developer-ID-signs the Avalonia desktop app, builds a `.dmg`, and **notarizes** it
with Apple so it opens with a normal double-click (no right-click → Open). Without the secrets it still
produces an ad-hoc `.dmg`. Notarization **reuses your App Store Connect API key** (the same `ASC_*`
secrets as TestFlight), so you only add the Developer ID cert.

**A. Developer ID Application certificate** — in your Apple Developer account create a **Developer ID
Application** cert (this is the *outside-the-App-Store* identity, different from the iOS Distribution
cert). Export it as a `.p12` with a password.

| Secret | Value |
|---|---|
| `MACOS_DEVID_CERT_P12_BASE64` | `base64 -i devid.p12` |
| `MACOS_DEVID_CERT_PASSWORD` | the .p12 export password |
| `MACOS_SIGN_IDENTITY` | e.g. `Developer ID Application: Your Name (TEAMID)` (`security find-identity -v -p codesigning` shows it) |

**B. Notarization** uses your existing `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_API_KEY_P8_BASE64` (the App
Store Connect API key — *App Manager* role works). Nothing extra to add if you already set those for iOS.

The result is `SkyMood-macos-arm64.dmg`, stapled so Gatekeeper accepts it offline. (Build it locally too:
`scripts/package-macos.sh <published-dir> out.dmg`, with the same env vars exported.)

---

## Cutting a release

```bash
git tag v1.0.0
git push origin v1.0.0          # triggers release.yml → signed builds + uploads
# …or run it manually from the Actions tab (toggle "Upload to stores" off for a dry run)
```

Bump the version by tagging `vX.Y.Z`; the workflow feeds `X.Y.Z` as the display version and the run
number as the build number. Without a tag (manual run) it uses `0.1.<run-number>`.
