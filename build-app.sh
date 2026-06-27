#!/usr/bin/env bash
# Build a platform head of the MAUI app (SkyMood.App).
# Build output goes to ~/.skymood-build (see Directory.Build.props — keeps generated files off this
# exFAT volume, which otherwise breaks the Android/iOS toolchains via ._* AppleDouble sidecars).
#
# Usage: ./build-app.sh [android|maccatalyst|ios|windows]   (default: maccatalyst on macOS)
set -e
cd "$(dirname "$0")"
APP="SkyMood.App/SkyMood.App.csproj"
target="${1:-maccatalyst}"

case "$target" in
  android)
    # Find a valid JDK 17: honour an existing JAVA_HOME, else probe the usual spots on this machine.
    if [ -z "$JAVA_HOME" ] || [ ! -d "$JAVA_HOME" ]; then
      JAVA_HOME=""
      for cand in \
        "$HOME"/Library/Java/JavaVirtualMachines/jbr-17*/Contents/Home \
        "/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
        "$(/usr/libexec/java_home -v 17 2>/dev/null)"; do
        if [ -n "$cand" ] && [ -x "$cand/bin/jar" ]; then JAVA_HOME="$cand"; break; fi
      done
    fi
    if [ -z "$JAVA_HOME" ]; then
      echo "No JDK 17 found. Set JAVA_HOME to a JDK 17, e.g.:"
      echo "  export JAVA_HOME=\$(/usr/libexec/java_home -v 17)"; exit 1
    fi
    export JAVA_HOME
    export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
    echo "Using JDK: $JAVA_HOME"
    # Pass JavaSdkDirectory so the Android tooling stops probing (and warning about) other paths.
    exec dotnet build "$APP" -c Debug -p:Heads=android -p:JavaSdkDirectory="$JAVA_HOME" ;;
  maccatalyst|mac)
    export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
    exec dotnet build "$APP" -c Debug -p:Heads=maccatalyst ;;
  ios)
    export DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
    # Simulator build needs no signing. For a device .ipa, drop the RID and add your signing identity.
    exec dotnet build "$APP" -c Debug -p:Heads=ios -p:RuntimeIdentifier=iossimulator-arm64 ;;
  windows)
    exec dotnet build "$APP" -c Debug -p:Heads=windows ;;
  *)
    echo "unknown target: $target  (use: android | maccatalyst | ios | windows)"; exit 1 ;;
esac
