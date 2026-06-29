#!/usr/bin/env bash
# Generate an Android UPLOAD keystore for signing the Play Store .aab, and print the exact
# GitHub secrets to set. Run locally — the keystore holds a PRIVATE KEY; never commit it.
#
#   ./scripts/make-android-keystore.sh
#
# Keep upload.keystore + the passwords somewhere safe (a password manager). If you lose the
# *upload* key you can ask Google to reset it; if you lose the *app signing* key (managed by
# Play App Signing) you cannot — so enrol in Play App Signing when you create the app.
set -euo pipefail

OUT="${1:-upload.keystore}"
ALIAS="${2:-skymood-upload}"

read -r -s -p "Choose a keystore password: " STOREPASS; echo
read -r -s -p "Choose a key password (can be the same): " KEYPASS; echo

keytool -genkeypair -v \
  -keystore "$OUT" -alias "$ALIAS" \
  -keyalg RSA -keysize 2048 -validity 10000 \
  -storepass "$STOREPASS" -keypass "$KEYPASS" \
  -dname "CN=Sky Mood, OU=Solo Apps Studio, O=Solo Apps Studio, C=US"

echo
echo "Created $OUT (alias: $ALIAS)."
echo
echo "Add these GitHub secrets (Settings → Secrets and variables → Actions):"
echo "  ANDROID_KEYSTORE_BASE64    = $(base64 -i "$OUT" | tr -d '\n' | cut -c1-24)…  (full value below)"
echo "  ANDROID_KEYSTORE_PASSWORD  = <the keystore password you just chose>"
echo "  ANDROID_KEY_ALIAS          = $ALIAS"
echo "  ANDROID_KEY_PASSWORD       = <the key password you just chose>"
echo
echo "Full base64 for ANDROID_KEYSTORE_BASE64 (copy the whole line):"
base64 -i "$OUT" | tr -d '\n'; echo
echo
echo "Tip: gh secret set ANDROID_KEYSTORE_BASE64 < <(base64 -i $OUT)"
