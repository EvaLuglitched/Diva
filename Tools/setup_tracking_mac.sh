#!/usr/bin/env bash
# Set up Diva camera tracking on macOS: the same steps as the README's PowerShell block.
# Run from anywhere:  bash Tools/setup_tracking_mac.sh
# Safe to run again: existing Assets/Elephant, the candy skin texture and downloaded models are kept.
set -euo pipefail
cd "$(dirname "$0")/.."

# The starter commit that added the savannah course (our Assets/SavannahCourse is identical to it). Elephant/, DigiPhant/
# and Tracking/ are the same as at the earlier 8f426f6 that older notes mention.
STARTER_COMMIT=4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
POSE_URL=https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task
HAND_URL=https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task
# Python 3.10 or newer: on the macOS system Python 3.9 the locked bridge's three-player detector fails three tests
# (found by the fresh-checkout test on 2026-10-13). Use $PYTHON if set, else the newest suitable python3.x on this Mac.
if [ -z "${PYTHON:-}" ]; then
  for c in python3.14 python3.13 python3.12 python3.11 python3.10 /opt/homebrew/bin/python3 /usr/local/bin/python3 /opt/anaconda3/bin/python3 python3; do
    if command -v "$c" >/dev/null 2>&1 && "$c" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)' 2>/dev/null; then PYTHON="$c"; break; fi
  done
fi
if [ -z "${PYTHON:-}" ]; then
  echo "Python 3.10 or newer is needed (macOS ships 3.9). Install it from https://www.python.org/downloads/ or with"
  echo "'brew install python@3.13', then run this script again (or: PYTHON=/path/to/python3 bash Tools/setup_tracking_mac.sh)."
  exit 1
fi
echo "Using $("$PYTHON" --version) at $(command -v "$PYTHON")"

if [ ! -d DigiPhantStarter/.git ]; then
  git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
fi
git -C DigiPhantStarter fetch -q origin
git -C DigiPhantStarter checkout -q "$STARTER_COMMIT"

if [ -d Assets/Elephant ]; then
  echo "Assets/Elephant already present (kept)"
else
  cp -R DigiPhantStarter/Elephant Assets/Elephant
  cp DigiPhantStarter/Elephant.meta Assets/Elephant.meta
fi

# The team's bridge and gesture rules replace the starter copies (Unity also syncs them on Play).
cp TrackingOverrides/*.py TrackingOverrides/*.json TrackingOverrides/*.txt DigiPhantStarter/Tracking/

cd DigiPhantStarter/Tracking
# An old environment made with Python 3.9 is replaced
if [ -x .venv/bin/python ] && ! .venv/bin/python -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)'; then rm -rf .venv; fi
[ -x .venv/bin/python ] || "$PYTHON" -m venv .venv
.venv/bin/python -m pip install -q -r requirements.txt
[ -f pose_landmarker_full.task ] || curl -fsSL -o pose_landmarker_full.task "$POSE_URL"
[ -f hand_landmarker.task ] || curl -fsSL -o hand_landmarker.task "$HAND_URL"
cd ../..

# The candy skin texture is a recolour of the starter's Elephant_D.png, so it is not in git (exact copy on the team Drive,
# see README). Rebuild it with the pinned OpenCV before Unity first opens the project: Unity deletes a .meta whose asset
# is missing, and the tracked .meta holds the GUID that Elephant Candy.mat uses.
CANDY="Assets/eva大象/Textures/Elephant_D_Candy.jpg"
CANDY_SHA256=1457e2376b2fe1245e2247624f2c414939c99286e2abbcdaab7f29470afa4c5c
[ -f "$CANDY.meta" ] || git checkout -- "$CANDY.meta"
if [ ! -f "$CANDY" ]; then
  DigiPhantStarter/Tracking/.venv/bin/python "Assets/eva大象/Source~/make_candy_texture.py" Assets/Elephant/Textures/Elephant_D.png "$CANDY"
fi
if [ "$(shasum -a 256 "$CANDY" | cut -d' ' -f1)" = "$CANDY_SHA256" ]; then
  echo "Candy skin texture OK ($CANDY)"
else
  echo "Warning: $CANDY differs from the team's copy; download Elephant_D_Candy.jpg from the Drive folder in README."
fi
# v2.0 password lock: refuse commits that change a locked file without Eva's signature (see GESTURES_LOCKED.md)
git config core.hooksPath .githooks 2>/dev/null || true
# Our tests run where they live (the gesture-lock test checks the tracked files); the starter's recording test runs in Tracking
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s TrackingOverrides -p 'test_*.py'
(cd DigiPhantStarter/Tracking && .venv/bin/python -m unittest test_recording)

echo
echo "Tracking is ready. In Unity open Assets/DigiPhant/Scenes/Diva.unity and press Play."
echo "The first time, macOS asks whether Unity may use the camera: allow it"
echo "(System Settings > Privacy & Security > Camera if you missed the prompt)."
