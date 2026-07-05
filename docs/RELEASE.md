# Releasing Sky Mood (signed builds → TestFlight & Play Console)

Release automation lives in **[Azure Pipelines](https://dev.azure.com/SamuelBanapour/sky-mood/_build?definitionId=1)**
(`azure-pipelines.yml`), not GitHub Actions — the GitHub Actions release workflow was removed after
repeatedly hitting GitHub's billing/spending-limit wall. `build.yml` (a fast Debug-build + unit-test
gate on every push/PR, not a release pipeline) is the only GitHub Actions workflow left in this repo.

The pipeline runs on a version tag (`git tag v1.0.0 && git push --tags`) or manually — either from
Azure DevOps (**Pipelines → sky-mood-release → Run pipeline**) or via the CLI:
```bash
az pipelines run --id 1 --parameters uploadToStores=false windowsStoreSubmit=off syncMirror=false --branch main
```

It degrades gracefully: with **no** secrets it still builds (unsigned) artifacts so you can confirm
the pipeline; add the secrets for a platform and that platform starts producing a **signed** build
and uploading to the store. Secrets live in the **skymood-secrets** variable group (Azure DevOps →
Pipelines → Library), settable via the web UI or:
```bash
az pipelines variable-group variable create --group-id 1 --name NAME --value "VALUE" --secret true
```

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

Track is chosen by the `androidTrack` pipeline parameter (`internal` by default) — Android upload
itself isn't wired into `azure-pipelines.yml` yet (see the gaps note at the bottom), but this stays
as the intended reference for whenever that's added.

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

For **sideload/Store** signing, supply a code-signing certificate (`.pfx`). The subject's `CN` must
exactly match the `Publisher` in `SkyMood.App/Platforms/Windows/Package.appxmanifest`
(`CN=AB578A7A-AB77-4581-85A4-109FDE76C7BE`).

**The PFX must be fully unencrypted** — no password. This SDK version's signing task
(`WinAppSdkSignAppxPackage`) has no password parameter anywhere in its pipeline; a password-protected
PFX always fails with a misleading `APPX0105: may be password protected` regardless of the actual
password. GitHub/Azure secret storage is the real protection here, not PFX encryption.

```bash
openssl req -x509 -newkey rsa:2048 -keyout cert.key -out cert.crt -days 3650 -nodes \
  -subj "/CN=AB578A7A-AB77-4581-85A4-109FDE76C7BE" \
  -addext "keyUsage=digitalSignature" -addext "extendedKeyUsage=codeSigning"
openssl pkcs12 -export -out skymood.pfx -inkey cert.key -in cert.crt \
  -passout pass: -certpbe NONE -keypbe NONE -nomac
```

| Secret | Value |
|---|---|
| `WINDOWS_PFX_BASE64` | `base64 -i skymood.pfx` |

Note: GitHub Actions and Azure Pipelines each have their **own separately-generated** cert under this
recipe (Azure can't read GitHub's copy) — same Publisher CN, different physical certificate/thumbprint.

The job emits a signed `.msix`. **Microsoft Store submission is automated** via the `storeSubmit`
job, using the Store submission API v1.0 directly (client-credentials auth, no `microsoft/store-submission`
action needed).

**Setup** — Partner Center → Account settings → User management → Microsoft Entra applications →
**Add Microsoft Entra application**, role **Developer** (upload/submit only — not Manager, which
also grants account/user/tenant management this credential doesn't need):

| Secret | Value |
|---|---|
| `MS_PARTNER_TENANT_ID` | the Microsoft Entra tenant ID (from Account settings → Tenants) |
| `MS_PARTNER_CLIENT_ID` | the Entra application's Client ID |
| `MS_PARTNER_CLIENT_SECRET` | the key generated for that application |

Controlled by the `windowsStoreSubmit` pipeline parameter: `off` (default — build only), `dry-run`
(creates the submission, uploads the package zip, points it at the new files, but stops **before**
committing — safe to review in Partner Center or abandon with no consequence), `submit` (commits and
kicks off real certification). Always try `dry-run` first for anything you haven't verified recently
— an uncommitted submission costs nothing, but Microsoft only allows one submission in flight at a
time, so a bad `submit` can leave the app stuck until you manually cancel it in Partner Center.

The appx package version is stamped fresh on every run (`Package.appxmanifest`'s `Identity Version`
is otherwise hardcoded and never changes) — `Major.Minor` from the tag, `Build` = the CI run number,
`Revision` always `0` (the Store hard-rejects any non-zero revision).

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
git push origin v1.0.0          # does NOT auto-trigger Azure Pipelines — see below
```

Unlike the old GitHub Actions setup, pushing a tag alone doesn't kick off a build — Azure Pipelines
here only triggers on a tag push if you run it from that ref. Actually cut a release by triggering
the pipeline manually (web UI or `az pipelines run`, above) with **Pipeline version** / `--branch`
set to the tag you just pushed. Bump the version by tagging `vX.Y.Z`; the pipeline feeds `X.Y.Z` as
the display version and the Azure build number as the build number. Without a tag it uses `0.1.<build-number>`.

**Current gaps versus the old GitHub Actions setup** (same as before it was removed — nothing lost,
just not yet ported): iOS signing (`IOS_*`/`ASC_*` secrets), macOS notarization (`MACOS_DEVID_*`),
and the Google Play upload step aren't wired into `azure-pipelines.yml` yet, since none of those
secrets were ever configured on GitHub Actions either. iOS builds unsigned (compile-check only) and
macOS builds ad-hoc-signed — add the equivalent secrets to the `skymood-secrets` variable group and
the corresponding pipeline steps whenever those are ready to wire up.
