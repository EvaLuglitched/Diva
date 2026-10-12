# Diva — DigiPhant Unity project

Three people control one elephant on the Savannah course using pretrained MediaPipe pose and hand landmarks plus gesture rules. This is a playable prototype; a custom neural action model has not been trained.

## Open and try the newest scene

Use Unity **6000.6.3f1** with Universal Render Pipeline **17.6.0**. Open **Assets/DigiPhant/Scenes/Diva.unity** and press Play. It now holds everything: the three-player gestures, Eva's candy-town game layer (targets, trunk shots, HUD, course tasks) and the D.Va mech elephant. The original DigiPhant scene remains available with the game layer only (no Diva gestures).

Choose **Try alone** and **All** for the single-player game (one person has every control), or P1 Move, P2 Turn or P3 Water to test one role. Choose **Camera**, click **Get ready (10 seconds)** and hold a natural resting pose with arms down. Changing roles requires calibration again. **Test controls** provides manual movement, turn, aim, spray and refill controls.

- P1: pump raised arms up and down to go; stop pumping to stop.
- P2: lean your torso left/right to turn even when GO is zero. Arms can stay relaxed. Stand upright to stop; losing torso tracking also stops turning. Keep shoulders and hips visible during upright calibration. Missing wrists still block GO and other arm actions.
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
git -C DigiPhantStarter checkout 8f426f675f86d938bf3dbec23d6a7ee2479d2c1d
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

## Verification and remaining checks

- 49 Python tests pass: 46 repository tests plus three recording tests restored from the pinned starter.
- Native Unity gesture, START/calibration, stationary-turn and trial-export validation passed. Stationary turning changed yaw both ways with zero speed and unchanged position; these are automated checks, not a new physical group trial.
- Historical local live checks showed rendered gameplay, camera landmarks and solo movement/steering. A 35.2-second MP4 decoded successfully and showed the camera and elephant together. These recordings and local trial data are excluded from Git.
- Walking remains weak/intermittent for the current user. GO rules are unchanged; archived replay demonstrates a low-cadence limitation, but the live cause is unresolved. A fresh physical trial after the torso-steering change, full three-person performance and a post-fix live trial-export check remain pending.
- Camera recovery reopens the same backend up to three times and requires recalibration. Native camera operations may block beyond retry timers. Cross-machine restoration/rendering still needs testing.

Camera-mode START waits for calibration; manual Test controls can start immediately. Keep hips visible during neutral capture: Ready alone does not guarantee a fresh hip baseline. F1 exposes **Record performance** below the preview; use **Stop recording and save** to export a silent Game-view MP4. Started runs also export route, events and summary to `TrialData/` on finish or interruption. See [setup and validation record](DIGIPHANT_SETUP.md) for limitations and recovery.

Labeled photos/videos, recordings, trial outputs and local backups are excluded from this repository. See [gesture validation](TrackingOverrides/VALIDATION.md) for reference-fit results; the supplied examples do not establish held-out accuracy.
