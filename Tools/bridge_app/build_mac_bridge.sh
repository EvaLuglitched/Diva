#!/usr/bin/env bash
# Build the self-contained macOS camera bridge that ships inside Diva.app (release builds).
# Needs Python 3.10+ (the locked bridge's three-player detector needs it; 3.13 is what we test with).
# Usage: bash Tools/bridge_app/build_mac_bridge.sh <work dir> [python]
# Output: <work dir>/dist/diva_bridge/ (executable + _internal with the models and gesture config), checked by --selftest.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${1:?work dir}"
PY="${2:-}"
if [ -z "$PY" ]; then
  for c in python3.14 python3.13 python3.12 python3.11 python3.10 /opt/anaconda3/bin/python3 /opt/homebrew/bin/python3 python3; do
    if command -v "$c" >/dev/null 2>&1 && "$c" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)' 2>/dev/null; then PY="$c"; break; fi
  done
fi
[ -n "$PY" ] || { echo "Need Python 3.10 or newer"; exit 1; }
mkdir -p "$WORK/src" "$WORK/models"
cd "$WORK"
[ -x venv/bin/python ] || "$PY" -m venv venv
venv/bin/python -m pip install -q --upgrade pip
venv/bin/python -m pip install -q -r "$ROOT/TrackingOverrides/requirements.txt" pyinstaller
cp "$ROOT/TrackingOverrides/bridge.py" "$ROOT/TrackingOverrides/diva_gestures.py" "$ROOT/TrackingOverrides/diva_gestures.json" "$ROOT/Tools/bridge_app/diva_bridge.py" src/
BASE=https://storage.googleapis.com/mediapipe-models
[ -f models/pose_landmarker_full.task ] || curl -fsSL -o models/pose_landmarker_full.task "$BASE/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task"
[ -f models/hand_landmarker.task ] || curl -fsSL -o models/hand_landmarker.task "$BASE/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
[ -f models/gesture_recognizer.task ] || curl -fsSL -o models/gesture_recognizer.task "$BASE/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task"
venv/bin/pyinstaller --noconfirm --clean --log-level WARN --onedir --name diva_bridge \
  --paths src --hidden-import bridge --hidden-import diva_gestures \
  --collect-all mediapipe \
  --add-data "src/diva_gestures.json:." --add-data "models/pose_landmarker_full.task:." \
  --add-data "models/hand_landmarker.task:." --add-data "models/gesture_recognizer.task:." \
  src/diva_bridge.py
dist/diva_bridge/diva_bridge --selftest
du -sh dist/diva_bridge
