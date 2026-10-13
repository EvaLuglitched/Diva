#!/usr/bin/env bash
# Turn the Unity release builds into the zips attached to a GitHub release.
# Usage: bash Tools/release/package_release.sh <version> <frozen bridge dist dir> <models dir> <visionOS .app or -> <out dir>
#   frozen bridge: Tools/bridge_app/build_mac_bridge.sh output (…/dist/diva_bridge)
#   models: folder with pose_landmarker_full.task, hand_landmarker.task, gesture_recognizer.task
# Needs Builds/Release/macOS/Diva.app and Builds/Release/Windows/Diva/ from Diva > Release (DivaReleaseBuild.cs).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:?version}"; BRIDGE="${2:?bridge dist}"; MODELS="${3:?models dir}"; VISION="${4:--}"; OUT="${5:?out dir}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# ---------- macOS (Apple silicon): the bridge goes inside the app, so camera gestures need no Python
APP="${MAC_APP:-$ROOT/Builds/Release/macOS/Diva.app}"  # MAC_APP: repackage an app that is not in Builds/ (e.g. after a bridge-only fix)
if [ -d "$APP" ]; then
  M="$STAGE/Diva $VERSION (macOS)"; mkdir -p "$M"
  cp -R "$APP" "$M/Diva.app"
  mkdir -p "$M/Diva.app/Contents/Resources/DivaTracking"
  cp -R "$BRIDGE" "$M/Diva.app/Contents/Resources/DivaTracking/diva_bridge"
  PL="$M/Diva.app/Contents/Info.plist"
  plutil -replace NSCameraUsageDescription -string "Diva reads your body movements from the camera to drive the elephant. Everything runs on this Mac; nothing is uploaded." "$PL"
  plutil -replace CFBundleDisplayName -string "Diva" "$PL"
  plutil -replace CFBundleShortVersionString -string "${VERSION#v}" "$PL"
  # A real signing identity keeps macOS's camera permission across rebuilds (an ad-hoc signature changes every build,
  # so a switched-on camera setting stops matching). SIGN_IDENTITY overrides; else the first Apple Development / Developer ID.
  IDENT="${SIGN_IDENTITY:-$(security find-identity -v -p codesigning 2>/dev/null | grep -E 'Developer ID Application|Apple Development' | head -1 | awk '{print $2}')}"
  codesign --force --deep --sign "${IDENT:--}" "$M/Diva.app" >/dev/null 2>&1
  codesign --verify --deep "$M/Diva.app"
  echo "macOS signed with ${IDENT:-ad-hoc}"
  cp "$ROOT/Tools/release/READ ME (macOS).txt" "$M/"
  (cd "$STAGE" && ditto -c -k --keepParent "Diva $VERSION (macOS)" "$OUT/Diva-$VERSION-macOS-AppleSilicon.zip")
  echo "macOS: $(du -h "$OUT/Diva-$VERSION-macOS-AppleSilicon.zip" | cut -f1)"
fi

# ---------- Windows (64-bit): the bridge sources and models next to Diva.exe; setup_camera.bat makes the Python environment once
WIN="$ROOT/Builds/Release/Windows/Diva"
if [ -d "$WIN" ]; then
  W="$STAGE/Diva $VERSION (Windows)"; mkdir -p "$W"
  cp -R "$WIN/." "$W/"
  rm -rf "$W/Diva_BurstDebugInformation_DoNotShip"
  T="$W/DivaTracking"; mkdir -p "$T"
  cp "$ROOT/TrackingOverrides/bridge.py" "$ROOT/TrackingOverrides/diva_gestures.py" "$ROOT/TrackingOverrides/diva_gestures.json" "$ROOT/TrackingOverrides/requirements.txt" "$T/"
  cp "$MODELS/pose_landmarker_full.task" "$MODELS/hand_landmarker.task" "$MODELS/gesture_recognizer.task" "$T/"
  cp "$ROOT/Tools/bridge_app/diva_bridge.py" "$T/"   # setup_camera.bat runs its model check
  perl -pe 's/\r?\n/\r\n/' "$ROOT/Tools/release/setup_camera.bat" > "$T/setup_camera.bat"   # Windows line endings for cmd
  cp "$ROOT/Tools/release/READ ME (Windows).txt" "$W/"
  (cd "$STAGE" && ditto -c -k --keepParent "Diva $VERSION (Windows)" "$OUT/Diva-$VERSION-Windows-x64.zip")
  echo "Windows: $(du -h "$OUT/Diva-$VERSION-Windows-x64.zip" | cut -f1)"
fi

# ---------- Vision Pro: the development-signed app (installs on the team's registered headsets)
if [ "$VISION" != "-" ] && [ -d "$VISION" ]; then
  V="$STAGE/Diva VR $VERSION (visionOS)"; mkdir -p "$V"
  cp -R "$VISION" "$V/"
  cp "$ROOT/Tools/release/READ ME (visionOS).txt" "$V/"
  (cd "$STAGE" && ditto -c -k --keepParent "Diva VR $VERSION (visionOS)" "$OUT/Diva-VR-$VERSION-visionOS.zip")
  echo "visionOS: $(du -h "$OUT/Diva-VR-$VERSION-visionOS.zip" | cut -f1)"
fi
ls -la "$OUT"
