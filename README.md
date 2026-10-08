# Diva — DigiPhant Unity project

A rigged elephant controlled by webcam body tracking, with the static Savannah course.

## Open the project

Use Unity **6000.6.3f1** with Universal Render Pipeline **17.6.0**. Open `Assets/DigiPhant/Scenes/DigiPhant.unity`. This scene includes the elephant controls, camera preview, locomotion, recording interface, and Savannah environment.

## Restore the shared starter assets

The large third-party elephant assets, pose model, Python environment, and Unity caches are excluded from this repository. Restore them from the exact starter commit below. Run these commands in PowerShell from this project root after a fresh clone:

```powershell
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
Copy-Item -LiteralPath DigiPhantStarter/Elephant -Destination Assets/Elephant -Recurse
Copy-Item -LiteralPath DigiPhantStarter/Elephant.meta -Destination Assets/Elephant.meta
Copy-Item -LiteralPath TrackingOverrides/bridge.py -Destination DigiPhantStarter/Tracking/bridge.py
python -m venv DigiPhantStarter/Tracking/.venv
& DigiPhantStarter/Tracking/.venv/Scripts/python.exe -m pip install -r DigiPhantStarter/Tracking/requirements.txt
& DigiPhantStarter/Tracking/.venv/Scripts/python.exe -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'
```

Python **3.14.7** was used on Windows. Preserve the elephant metadata. Do not overwrite the tracked DigiPhant or SavannahCourse folders with starter copies. These restore commands assume a fresh checkout without an existing starter or Elephant folder. Reuse and inspect existing copies before copying again.

`TrackingOverrides/bridge.py` preserves this project's adaptation: individual missed webcam frames are retried for up to 10 seconds before the bridge reports a prolonged camera failure.

## Camera controls

Press Play in Unity. The scene starts the local Python bridge automatically and shows the camera preview. Choose **1 person**, **Seated / upper body**, and **Camera** for a solo seated test. Keep shoulders and both hands visible. Click **Set neutral pose (10 seconds)** and hold a comfortable resting pose. Then test one gesture at a time. Stop Play to stop the camera bridge.

The controls guide is `Assets/controls.md`. Full-body mode needs hips and feet visible. Recording is local; place checked videos on Drive rather than in Git.

## Verification and remaining checks

- Supplied tracking tests: 11 passed; dependency consistency check passed.
- Savannah bundle, deterministic geometry, scene-copy, and injection checks passed.
- Live camera preview and P1 detection were observed. Sustained reliability, successful neutral calibration, gesture-driven movement, and recording export still need live testing.
- The movement radius remains 15 units. Full Savannah course coverage may require 30; it has not been changed.
- Unity rendering and all asset references must be checked on another machine after restoration.

See `DIGIPHANT_SETUP.md` for the setup record. The starter's third-party asset terms continue to apply.
