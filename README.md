# Diva — DigiPhant Unity project

Three people control one elephant on the Savannah course using pretrained MediaPipe pose and hand landmarks plus gesture rules. This is a playable prototype; a custom neural action model has not been trained.

## Open and try the newest scene

Use Unity **6000.6.3f1** with Universal Render Pipeline **17.6.0**. Open **Assets/DigiPhant/Scenes/Diva.unity** and press Play. It now holds everything: the three-player gestures, Eva's candy-town game layer (targets, trunk shots, HUD, course tasks) and the D.Va mech elephant. The original DigiPhant scene remains available with the game layer only (no Diva gestures).

Choose **Try alone** and **All** for the single-player game (one person has every control), or P1 Move, P2 Turn or P3 Water to test one role. Choose **Camera**, click **Get ready (10 seconds)** and hold a natural resting pose with arms down. Changing roles requires calibration again. **Test controls** provides manual movement, turn, aim, spray and refill controls.

- P1: pump raised arms up and down to go; stop pumping to stop.
- P2: lean both hands and body left/right to turn; stand upright for straight. Hips must be visible. Once turning, a smaller lean keeps the turn going, and short tracking blips (up to 0.25 s) no longer cancel it.
- P3: raise one arm to aim. Push both hands forward and open them for water spray; while spraying, push both hands toward one side to aim that way. A quick opening gives one burst; holding both open hands extended continues spraying. Each burst fires at the best target in the aimed direction and scores in the HUD. Curl hands together near the mouth to drink/refill; covering the mouth with your hands is fine for a few seconds once your face has been seen.

For group play, start left-to-right in the unmirrored preview: P1 movement, P2 turning, P3 water. Keep background people out of view and avoid crossing. Use Reassign players and recalibrate if roles are lost.

The demo includes a third-person camera, the game layer's 16 targets, scoring, a water tank and the course tasks. Course tasks follow the Diva gestures: drink/eat by bringing hands to the mouth near the bush or basin, step over the log by pumping (P1) next to it. Refill currently works anywhere. See **DIVA_TRY_IT.md** for details.

If the camera panel says **Camera stopped: …**, the text after the colon is the tracker's own error (for example a missing model or file). **Waiting for P2, P3 …** names the players the camera cannot see; for one person choose **Try alone**.

## Game flow, skins, skills and leaderboard (DivaShow)

Press Play in **Diva.unity** and the game now runs as a full match: choose one of four mech skins on a turntable → Eva's race intro (aerial, rocket, swoop, mech power-up, 3·2·1 GO) with Carl's entrance sting when the elephant appears → play with FPS-style HUD, skill banners, picture-in-picture close-ups, combos and an ultimate (MEGA BUBBLE: hold both hands above your head) → finish cutscene → automatic 8-second highlight replay → results cards and a local leaderboard. It is a single-player game by default: one person has every control (Try alone > **All**); **3 PLAYERS** keeps the original roles. The UI is English by default; the **EN 中文** button (or L) switches to Chinese. F1 or **SETUP** shows the old setup panel. See **Assets/DivaShow/README.md** (Chinese), **PLAYTEST_TODO.md** for what still needs a real-camera test, and **NOTE_FOR_EVA.md** for how the show uses the intro.

## Eva's updates

Everything Eva added (candy-town game layer, race intro, rocket and sky, track edges, start pad, Vision Pro intro text,
Fab fallback) is listed with commit IDs in **EVA_UPDATES.md** (Chinese), including replies to NOTE_FOR_EVA.md.

## Fab rocket and clouds (not in git)

The giant rocket and clouds in the sky are Fab models whose licence does not allow a public repo. Eva shares
**Diva_Fab_Assets.zip** privately; unzip it into `Assets/DivaGame/ThirdParty/` as described in **FAB_SETUP.md**.
Without it the scene shows stand-ins (our kit rocket and Kenney clouds) and a "Missing Prefab" warning.

## D.Va-style mech elephant (eva大象)

The elephant in **Diva.unity** wears a pink D.Va-style mech suit with a candy-pink skin, a trunk water gun, bubble cannons, thruster flames that follow speed, and sound effects. Everything lives in **Assets/eva大象**; see **Assets/eva大象/README.md** (Chinese) for toggles and how to install it in another scene (**Diva > Add D.Va Mech to Elephant**). Gestures drive it: P1/P2 movement grows the thruster flames, P3's spray fires the trunk water gun along the target-hit path (aim follows P3's raised arm), and P3's drink blows bubbles. The trigger logic is written up in **Assets/eva大象/GESTURES.md**.

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

**macOS:** run `bash Tools/setup_tracking_mac.sh` from the project folder instead; it does the same steps and runs the tests. In Play mode Unity also copies TrackingOverrides into DigiPhantStarter/Tracking when they differ and downloads the hand model if it is missing.

Python **3.14.7** was used on Windows. Preserve the elephant metadata. Do not overwrite the tracked DigiPhant or SavannahCourse folders with starter copies. The starter's third-party asset terms continue to apply.

Unity starts the local camera bridge automatically in Play mode. Stop Play to stop it. Camera messages use localhost ports 5055–5057. Gesture thresholds live in **TrackingOverrides/diva_gestures.json** and detection rules in **TrackingOverrides/diva_gestures.py**.

## Verified behavior and current limitation

- 39 Python tests cover tracking, gesture rules (including steering hysteresis, aiming while spraying and the remembered mouth), hand association and bounded camera recovery.
- A simulated three-player Play-mode run in Diva.unity: calibration names missing players, P1 runs with full thruster flames, P2 turns, P3 spray fires the trunk shots at targets with the water jet, P3 drink refills the tank and blows bubbles, and the eat and log tasks complete with gestures.
- Unity native gesture validation passed, including role routing, solo testing, neutral calibration, tracking loss, bursts, held spray and refill.
- Diva entered Play mode with the course, elephant and targets. Manual spray registered target hits and refill restored the water tank.
- Live camera preview appeared, but the integrated webcam repeatedly stopped supplying frames. Three reconnect attempts did not restore a reliable feed. Sustained gesture-driven gameplay and the three-person physical trial remain unverified.
- Recovery clears active inputs, reopens the same camera connection up to three times and requires fresh neutral calibration after reconnecting. OpenCV's native camera operations can block beyond these retry timers.
- The Diva scene uses a 40-unit travel radius. The original scene retains its settings.
- Unity rendering and asset restoration still need verification on another machine.

The labeled photos/videos remain in the local **diva data** folder and are not part of this Git repository. See **TrackingOverrides/VALIDATION.md** for reference replay limits and **DIGIPHANT_SETUP.md** for the setup record.
