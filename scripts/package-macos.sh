#!/usr/bin/env bash
# Package SkyMood.Desktop (Avalonia) into a macOS .app + .dmg, signed and (optionally) notarized.
#
# With a Developer ID identity + an App Store Connect API key it produces a notarized, stapled
# .dmg that opens with a normal double-click. Without them it falls back to an ad-hoc-signed app
# and an unsigned .dmg (which still works via right-click → Open). Used by CI and runnable locally.
#
# Usage: scripts/package-macos.sh <published-dir> <out.dmg>
# Env (all optional — set in CI secrets for a notarized build):
#   MACOS_SIGN_IDENTITY   "Developer ID Application: Your Name (TEAMID)"
#   ASC_KEY_ID ASC_ISSUER_ID ASC_API_KEY_P8   (App Store Connect API key for notarization)
set -euo pipefail

PUBDIR="${1:?published dir}"; OUTDMG="${2:?output .dmg path}"
WORK="$(mktemp -d)"; APP="$WORK/SkyMood.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBDIR"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/SkyMood.Desktop"
ICNS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../SkyMood.Desktop/Assets/SkyMood.icns"
[ -f "$ICNS" ] && cp "$ICNS" "$APP/Contents/Resources/SkyMood.icns"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Sky Mood</string>
  <key>CFBundleDisplayName</key><string>Sky Mood</string>
  <key>CFBundleIdentifier</key><string>com.gamedevsolo.skymood.desktop</string>
  <key>CFBundleExecutable</key><string>SkyMood.Desktop</string>
  <key>CFBundleIconFile</key><string>SkyMood</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.weather</string>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
PLIST

# .NET's runtime needs these even under the hardened runtime that notarization requires.
ENT="$WORK/entitlements.plist"
cat > "$ENT" <<'ENTL'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENTL

if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
  echo ">> Signing with Developer ID: $MACOS_SIGN_IDENTITY (hardened runtime)"
  # Sign nested native libraries first, then the executable, then the bundle.
  find "$APP/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -print0 \
    | xargs -0 -I{} codesign --force --timestamp --options runtime -s "$MACOS_SIGN_IDENTITY" {}
  codesign --force --timestamp --options runtime --entitlements "$ENT" -s "$MACOS_SIGN_IDENTITY" "$APP/Contents/MacOS/SkyMood.Desktop"
  codesign --force --timestamp --options runtime --entitlements "$ENT" -s "$MACOS_SIGN_IDENTITY" "$APP"
  codesign --verify --deep --strict "$APP"
else
  echo ">> No MACOS_SIGN_IDENTITY — ad-hoc signing (not notarizable, but runs via right-click → Open)."
  codesign --force --deep -s - "$APP"
fi

echo ">> Building DMG: $OUTDMG"
STAGE="$WORK/stage"; mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"; ln -s /Applications "$STAGE/Applications"
rm -f "$OUTDMG"
hdiutil create -volname "Sky Mood" -srcfolder "$STAGE" -ov -format UDZO "$OUTDMG"
[ -n "${MACOS_SIGN_IDENTITY:-}" ] && codesign --force --timestamp -s "$MACOS_SIGN_IDENTITY" "$OUTDMG" || true

if [ -n "${MACOS_SIGN_IDENTITY:-}" ] && [ -n "${ASC_KEY_ID:-}" ] && [ -n "${ASC_ISSUER_ID:-}" ] && [ -n "${ASC_API_KEY_P8:-}" ]; then
  echo ">> Notarizing with App Store Connect API key…"
  xcrun notarytool submit "$OUTDMG" --key "$ASC_API_KEY_P8" --key-id "$ASC_KEY_ID" --issuer "$ASC_ISSUER_ID" --wait
  xcrun stapler staple "$OUTDMG"
  xcrun stapler validate "$OUTDMG"
  echo ">> Notarized + stapled ✓ (opens with a normal double-click)"
else
  echo ">> Notarization skipped (needs MACOS_SIGN_IDENTITY + ASC_KEY_ID/ASC_ISSUER_ID/ASC_API_KEY_P8)."
fi
echo ">> Done: $OUTDMG"
