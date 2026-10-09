# Diva — DigiPhant Unity project

Three people control one elephant on the Savannah course using pretrained MediaPipe pose and hand landmarks plus gesture rules. This is a playable prototype; a custom neural action model has not been trained.

## Open and try the newest scene

Use Unity **6000.6.3f1** with Universal Render Pipeline **17.6.0**. Open **Assets/DigiPhant/Scenes/Diva.unity** and press Play. The original DigiPhant scene remains available.

Choose **Try alone** to test P1 Move, P2 Turn or P3 Water individually. Choose **Camera**, click **Get ready (10 seconds)** and hold a natural resting pose with arms down. Changing roles requires calibration again. **Test controls** provides manual movement, turn, aim, spray and refill controls.

- P1: pump raised arms up and down to go; stop pumping to stop.
- P2: lean both hands and body left/right to turn; stand upright for straight. Hips must be visible.
- P3: raise one arm to aim. Push both hands forward and open them for water spray. A quick opening gives one burst; holding both open hands extended continues spraying. Curl hands together near a visible mouth to drink/refill.

For group play, start left-to-right in the unmirrored preview: P1 movement, P2 turning, P3 water. Keep background people out of view and avoid crossing. Use Reassign players and recalibrate if roles are lost.

The demo includes a third-person camera, five water targets, hit counting and a water tank. Refill currently works anywhere. Aim returns to center when neither arm is raised, which may center the spray gesture. See **DIVA_TRY_IT.md** for details.

## Restore assets after a fresh clone

The shared elephant assets, pretrained model files, Python environment and Unity caches are excluded from Git. Run the following PowerShell commands from a fresh project checkout. Inspect and reuse existing starter/Elephant folders rather than copying over them.

```powershell
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
Copy-Item -LiteralPath DigiPhantStarter/Elephant -Destination Assets/Elephant -Recurse
Copy-Item -LiteralPath DigiPhantStarter/Elephant.meta -Destination Assets/Elephant.meta
Get-ChildItem -LiteralPath TrackingOverrides -File | Where-Object { $_.Extension -in '.py', '.json', '.txt' } | Copy-Item -Destination DigiPhantStarter/Tracking -Force
python -m venv DigiPhantStarter/Tracking/.venv
& DigiPhantStarter/Tracking/.venv/Scripts/python.exe -m pip install -r DigiPhantStarter/Tracking/requirements.txt
Invoke-WebRequest -Uri 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task' -OutFile DigiPhantStarter/Tracking/pose_landmarker_full.task
Invoke-WebRequest -Uri 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task' -OutFile DigiPhantStarter/Tracking/hand_landmarker.task
& DigiPhantStarter/Tracking/.venv/Scripts/python.exe -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'
```

Python **3.14.7** was used on Windows. Preserve the elephant metadata. Do not overwrite the tracked DigiPhant or SavannahCourse folders with starter copies. The starter's third-party asset terms continue to apply.

Unity starts the local camera bridge automatically in Play mode. Stop Play to stop it. Camera messages use localhost ports 5055–5057. Gesture thresholds live in **TrackingOverrides/diva_gestures.json** and detection rules in **TrackingOverrides/diva_gestures.py**.

## Verified behavior and current limitation

- 32 Python tests cover tracking, gesture rules, hand association and bounded camera recovery.
- Unity native gesture validation passed, including role routing, solo testing, neutral calibration, tracking loss, bursts, held spray and refill.
- Diva entered Play mode with the course, elephant and targets. Manual spray registered target hits and refill restored the water tank.
- Live camera preview appeared, but the integrated webcam repeatedly stopped supplying frames. Three reconnect attempts did not restore a reliable feed. Sustained gesture-driven gameplay and the three-person physical trial remain unverified.
- Recovery clears active inputs, reopens the same camera connection up to three times and requires fresh neutral calibration after reconnecting. OpenCV's native camera operations can block beyond these retry timers.
- The Diva scene uses a 40-unit travel radius. The original scene retains its settings.
- Unity rendering and asset restoration still need verification on another machine.

The labeled photos/videos remain in the local **diva data** folder and are not part of this Git repository. See **TrackingOverrides/VALIDATION.md** for reference replay limits and **DIGIPHANT_SETUP.md** for the setup record.
