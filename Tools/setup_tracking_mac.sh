#!/usr/bin/env bash
# Set up Diva camera tracking on macOS: the same steps as the README's PowerShell block.
# Run from anywhere:  bash Tools/setup_tracking_mac.sh
# Safe to run again: existing Assets/Elephant and downloaded models are kept.
set -euo pipefail
cd "$(dirname "$0")/.."

STARTER_COMMIT=4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
POSE_URL=https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task
HAND_URL=https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task
PYTHON=${PYTHON:-python3}

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
[ -x .venv/bin/python ] || "$PYTHON" -m venv .venv
.venv/bin/python -m pip install -q -r requirements.txt
[ -f pose_landmarker_full.task ] || curl -fsSL -o pose_landmarker_full.task "$POSE_URL"
[ -f hand_landmarker.task ] || curl -fsSL -o hand_landmarker.task "$HAND_URL"
.venv/bin/python -m unittest discover -s . -p 'test_*.py'

echo
echo "Tracking is ready. In Unity open Assets/DigiPhant/Scenes/Diva.unity and press Play."
echo "The first time, macOS asks whether Unity may use the camera: allow it"
echo "(System Settings > Privacy & Security > Camera if you missed the prompt)."
