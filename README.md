# Diva — DigiPhant mech-elephant race

**Team Diva** (Eva [@EvaLuglitched](https://github.com/EvaLuglitched), Carl [@jchen60-bot](https://github.com/jchen60-bot),
Vicky [@vickyyan-rgb](https://github.com/vickyyan-rgb)) · DesComp Project 2 (DigiPhant)

One elephant, driven by bodies. A webcam and pretrained MediaPipe models read one or three performers; gesture rules turn
their movements into walking, turning, aiming, spraying water, eating / drinking and an ultimate. The elephant wears a
D.Va-style mech suit and races through Eva's candy town: targets, four course tasks, candy, an ice wall before the
finish, a highlight replay and a leaderboard. A first-person Apple Vision Pro version lives on a separate branch.
No custom neural action model was trained: pretrained MediaPipe plus calibrated rules.

| | |
| --- | --- |
| **Git** | https://github.com/EvaLuglitched/Diva (default branch `main`; Carl's PC work on `carl/race-meta`, Vision Pro on `carl/diva-vr`) |
| **Drive** (team submission folder: demo video, control mapping, changed elephant texture) | https://drive.google.com/drive/folders/16mkenRzNIFoxs1Y-Xad8nl92Xh22DkiC |
| **Scene** | `Assets/DigiPhant/Scenes/Diva.unity` |
| **Unity** | 6000.6.3f1, Universal Render Pipeline 17.6.0 |
| **Instructor starter** | https://github.com/kommanderpi/studentstarter.git at commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` (the commit that added the savannah course; its `Elephant/`, `DigiPhant/` and `Tracking/` are the same as at the earlier `8f426f6`) |
| **Tested setup, mappings, recording, limitations** | [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) |
| **Product page** (downloads, how to play, how we made it) | [docs/index.html](docs/index.html), published at https://diva-elephant.github.io |

> 🔒 **Gesture controls are locked** (single-player and three-player, 2026-10-12). Read [GESTURES_LOCKED.md](GESTURES_LOCKED.md)
> before changing anything in `TrackingOverrides/diva_gestures.*`, `bridge.py`, `DivaControlState.cs` or `DivaDemo.cs`.

## Assignment submission

The three-person submission roles are **P1 Vicky** (walking), **P2 Eva** (turning), and **P3 Jiayu**
(aiming, spraying and refill). Jiayu is the team's confirmed most reliable P3 performer.

| Submission item | Where to find it |
| --- | --- |
| Written movement-to-control mapping | [Diva: Team Control Mapping](https://docs.google.com/document/d/1urKmUug0rV43_86DuJK7KGsfQCot1wsBSexF_cAui0s/edit) (Google Doc in the team Drive folder); [mapping table below](#movement--control-mapping) |
| Playable downloads and source setup | [Release downloads](#download-v20-macos-windows--v11-vision-pro); [fresh-clone restoration](#reconstruct-the-project-from-a-fresh-clone) |
| Pink candy-town scene | `Assets/DigiPhant/Scenes/Diva.unity` |
| Demo video (performers and elephant side by side) | [diva demo video](https://drive.google.com/file/d/1CX1NiwhmPOBNsl2U8RO0b-O5kjZPcWaQ/view) in the [team Drive folder](https://drive.google.com/drive/folders/16mkenRzNIFoxs1Y-Xad8nl92Xh22DkiC) |
| Existing recording collection | [Drive video folder](https://drive.google.com/drive/folders/1NAZGp2xYGU9ZVkIdl9RajQ89eXVgnDBF) (earlier recordings) |
| Changed elephant texture (not in git) | [Elephant_D_Candy.jpg](https://drive.google.com/file/d/1pGykqq3OCjHfO_ycppod0eQB_CUkr98n/view) and its English [restore note](https://drive.google.com/file/d/16gKlvuz9Wy6ZqizBjQG246WxpmfvKkBX/view) in the team Drive folder; see [fresh-clone restoration](#reconstruct-the-project-from-a-fresh-clone) |
| Testing and group reflection | [Verification](#verification), [group reflection](#group-reflection), and [setup / validation record](DIGIPHANT_SETUP.md#verification-record-and-limitations) |

## Group reflection

One tested improvement was making P2 steering depend on the shoulders and head without requiring visible hips or
wrists. The existing v2.0 regression record reports 13 player-parts tests (eight failed against the old code), and a
replay of Vicky's P2 clip cropped below the chest increased steering delivery to Unity from 0–17% to 100%. This
addresses the three-person occlusion problem while keeping the steering gesture simple. Evidence and implementation
history: [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and
[Eva's update log, items 31–32](docs/EVA_UPDATES.md).

A remaining limitation is that crowding, occlusion and lighting can still affect MediaPipe's detections. The recorded
clips and regression scenarios do not establish reliability for every room, camera or group of performers. These are
existing test/replay results; this documentation update did not run a fresh native Unity or live-camera test.

## Download v2.0 (macOS, Windows) · v1.1 (Vision Pro)

Ready-to-play builds: macOS and Windows **https://github.com/EvaLuglitched/Diva/releases/tag/v2.0**, Vision Pro
**https://github.com/EvaLuglitched/Diva/releases/tag/v1.1** (all releases: https://github.com/EvaLuglitched/Diva/releases)

v2.0 (macOS, Windows): every player controls reliably. Walking follows Vicky's original data (bring a hand up to the navel
once a second; hands hanging down never walk), P2 turns with only the shoulders and head (hips and hands may be hidden or
out of the picture), a player missing from a few camera frames no longer stops the elephant, and in single player a
pumping fist is no longer taken for a thumbs-up. The action detection, controls and camera / player lock are
password-locked. The downloads were checked against the v2.0 source: same tracking code, settings, models and game code.

v1.6 (macOS, Windows): lifting one arm to aim right after stopping no longer makes the elephant walk on (one player).

v1.5 (macOS, Windows): single player tracks much faster (about 22 instead of 15 frames a second: the hand / thumbs-up
recognition now runs on every other frame, as with three players), so one player can walk again. Mac and Windows are the
same version again, and both get Next camera (C on the camera check, or F1 > Next camera), a camera message players can act
on, and a 60 fps camera cap that still lowers the resolution when a 120 Hz+ screen needs it.

v1.4 (macOS): the elephant stops on time, about 0.4 s after P1 stops swinging (was about 1 s: wrist jitter kept
counting as movement), and the BOOSTER RUSH effect is off.

v1.3 (macOS): walking is much easier for beginners (one arm swinging well and the other moving a little is enough;
slow, uneven, low or high swings all count), and the camera tracking is back to 30 fps (the game draws at 60 fps while
the camera is on, so the two no longer fight over the processor).

v1.2 (macOS): whoever stays in front of the camera keeps playing (a player who leaves is replaced by the person still in
view after 1 s), no tracking crash with several people in view, the Mac always uses its own camera (not a nearby iPhone),
and the tracking restarts by itself if it ever stops.

Windows v1.3 and v1.4 had all of the above plus Next camera. Vision Pro has not been rebuilt yet and stays on v1.1.

| Platform | File | First start | Camera gestures |
| --- | --- | --- | --- |
| macOS, Apple silicon (M1 and newer) | [Diva-v2.0-macOS-AppleSilicon.zip](https://github.com/EvaLuglitched/Diva/releases/download/v2.0/Diva-v2.0-macOS-AppleSilicon.zip) | drag `Diva.app` to Applications and open it; it is signed but not notarised, so the first time: System Settings > Privacy & Security > **Open Anyway** (macOS 14 and older: right-click > Open) | built in: macOS asks for the camera when the game opens, one click on Allow |
| Windows 10 / 11, 64-bit | [Diva-v2.0-Windows-x64.zip](https://github.com/EvaLuglitched/Diva/releases/download/v2.0/Diva-v2.0-Windows-x64.zip) | unzip, run `Diva.exe`; if SmartScreen appears: More info > Run anyway | built in, no Python: allow the camera if Windows asks; wrong camera: C on the camera check or F1 > Next camera |
| Apple Vision Pro | [Diva-VR-v1.1-visionOS.zip](https://github.com/EvaLuglitched/Diva/releases/download/v1.1/Diva-VR-v1.1-visionOS.zip) | development-signed: installs on the team's registered headsets | headset hand tracking |

The keyboard works in every desktop build without a camera. Each zip has a short READ ME. The very first camera start
on a computer takes longer while the tracking prepares itself once (Mac about 17 s, Windows about 6 s); later starts
take a few seconds.

## Movement → control mapping

Camera → `bridge.py` (MediaPipe pose, hand and gesture models) → `diva_gestures.py` rules → UDP JSON on localhost
5055 → `DivaDemo` / `DivaControlState` → locomotion, trunk water gun, tasks and ultimate.

| Control | Gesture | Who (three-player mode) | Keyboard |
| --- | --- | --- | --- |
| Walk / run | Pump both arms up and down; stop pumping to stop | P1 (left) | W or ↑ |
| Turn | Tilt your shoulders or your head to one side (your own left = left), works standing still | P2 (middle) | A / D or ← / → |
| Aim the trunk | Raise one arm | P3 (right) | U / I |
| Spray water | Push both open hands forward; hold to keep spraying | P3 | Space (hold) |
| Eat, drink, refill water | Thumbs-up held 0.4 s (near the bush / basin for the tasks) | P3 | F |
| MEGA BUBBLE (ultimate) | Both hands above your head for 0.8 s, when charged | anyone | E |
| Menus | Raise one arm = next skin; both hands up = start / skip / play again | anyone | ← → and Enter |

In single-player mode (the default, **Try alone > All**) one person has every control; spraying, refilling and the ultimate
pause walking so the arm gestures do not fight each other. Exact thresholds: [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and
`TrackingOverrides/diva_gestures.json`. The course tasks: eat at the feeding bush, drink at the water basin (thumbs-up near
each), go around the fallen log (just walk past it), cross the finish arch.

On the Vision Pro branch the same controls come from the headset: lean forward = walk, tilt your head = turn,
look at a target and pinch = spray, thumbs-up = drink, both hands to eye level = ultimate.

## Play

1. Open `Assets/DigiPhant/Scenes/Diva.unity` and press Play. Unity starts the local camera bridge itself.
2. Pick a skin and 1 or 3 players, then **START**. In Camera mode a large camera check follows: stand naturally with
   your arms down; when everyone has been green for 5 seconds the neutral pose is saved and Eva's race intro plays.
   Keyboard / Test controls skip the check.
3. Race: candy (+15), golden targets (×3), ON FIRE (×1.5), four tasks, an ice wall that only the MEGA BUBBLE breaks,
   the finish arch, then a hero card, a highlight replay, results and the local leaderboard.

F1 or **SETUP** shows the setup panel (camera preview, Try alone / roles, calibration, **Record performance**).
F2 shows the frame rate. Everything a player sees is described in [docs/DIVA_TRY_IT.md](docs/DIVA_TRY_IT.md).

## Reconstruct the project from a fresh clone

The repository tracks our scenes, scripts, mappings, materials, `.meta` files, `Packages/` and `ProjectSettings/`.
The instructor's shared elephant, our candy recolour of its texture, the pose model, the Python environment, Unity caches,
builds and recordings are not in git (see `.gitignore`); these steps bring them back. Run them from the project folder (the one with `Assets/`).

**macOS** (one script, safe to run again):

```bash
bash Tools/setup_tracking_mac.sh
```

**Windows** (PowerShell):

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
Invoke-WebRequest -Uri 'https://drive.google.com/uc?export=download&id=1pGykqq3OCjHfO_ycppod0eQB_CUkr98n' -OutFile 'Assets/eva大象/Textures/Elephant_D_Candy.jpg'
Get-FileHash -LiteralPath 'Assets/eva大象/Textures/Elephant_D_Candy.jpg'  # SHA256 must be 1457E2376B2FE1245E2247624F2C414939C99286E2ABBCDAAB7F29470AFA4C5C
git config core.hooksPath .githooks
```

Both paths: clone the starter **beside** `Assets` as `DigiPhantStarter` at the pinned commit, copy `Elephant/` and
`Elephant.meta` into `Assets` (keeping their metadata), copy our modified tracking code from `TrackingOverrides/` over the
starter's `Tracking/`, create the Python environment from `requirements.txt` and download the pose and hand models.
Restore the candy skin texture `Assets/eva大象/Textures/Elephant_D_Candy.jpg`, our recolour of the starter's
`Elephant/Textures/Elephant_D.png`: the macOS script rebuilds it byte-identically with `Assets/eva大象/Source~/make_candy_texture.py`,
the Windows steps download the team's copy from [Drive](https://drive.google.com/file/d/1pGykqq3OCjHfO_ycppod0eQB_CUkr98n/view) (OpenCV cannot write to the non-ASCII folder name on
Windows). Do this **before** Unity first opens the project: the texture's `.meta` is tracked (GUID
`3e003aaedaef24be6b55f57fe3cee1b3`, used by `Elephant Candy.mat`) and Unity deletes a `.meta` whose file is missing. If that
happened, run `git checkout -- "Assets/eva大象/Textures/Elephant_D_Candy.jpg.meta"` and reopen the project.
Do not copy the starter's `DigiPhant/` or `SavannahCourse/` over ours: ours are adapted and tracked. Then open the scene
in Unity 6000.6.3f1. The first open imports everything and takes a while.

**Password lock (v2.0):** the action detection, the controls and the camera / player lock are signed with Eva's
password (`Tools/lock/`, see [GESTURES_LOCKED.md](GESTURES_LOCKED.md)). After cloning, turn on the commit check once:
`git config core.hooksPath .githooks`. `python3 Tools/lock/diva_lock.py verify` checks the locked files; GitHub and
Unity (**DigiPhant > Diva > Verify Lock**, and every release build) check them too.

Optional: the sky rocket and clouds are Fab models whose licence does not allow a public repository. Eva shares
`Diva_Fab_Assets.zip` privately ([docs/FAB_SETUP.md](docs/FAB_SETUP.md)); without it the scene shows our stand-ins.

**Reconstruction test, 2026-10-17** (after the candy texture left git and the starter pin moved to `4e6dab5`), on a Mac in a separate folder: a fresh clone + `bash Tools/setup_tracking_mac.sh` restored the starter at `4e6dab5`, the elephant, the models and the candy texture (rebuilt; its SHA-256 matches the Drive copy); 161 + 3 Python tests and the lock check passed; the first Unity 6000.6.3f1 import had 0 compile errors; the texture kept GUID `3e003aaedaef24be6b55f57fe3cee1b3` and `Elephant Candy.mat` uses it; `Diva.unity` has 1,597 objects, 0 missing scripts, 0 renderers without a material and 11 missing prefabs (the optional Fab clouds and rocket); the four validators passed. Opening Unity before the texture was restored deleted its `.meta`, as expected; running the script again restored both. Not covered: the Windows steps (Drive download), live camera play and an automatic race round.

**Earlier reconstruction test** (as `STUDENT_GIT.md` asks), 2026-10-13 on a Mac in a separate folder: a fresh clone + the script restored the starter at the pinned commit, the elephant and the models; 93 + 3 Python tests passed; Unity opened it with 0 compile errors; all four validators passed; the scene had 0 missing scripts (the 11 missing prefabs are the optional Fab clouds and rocket); an automatic round finished with a gold lap. It also showed that macOS's built-in Python 3.9 is too old for the three-player detector, so the script now picks Python 3.10+. Details in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md). Windows reconstruction and live camera play were not part of this test.

## Repository map

| Folder | What is in it |
| --- | --- |
| `Assets/DigiPhant/` | The adapted DigiPhant layer: camera-bridge receiver, calibration, locomotion, the Diva gesture state (locked) and the scenes |
| `Assets/DivaGame/` | Eva's candy-town game layer: town models, targets, course tasks, race intro, keyboard controls |
| `Assets/DivaShow/` | The match flow: skins, HUD, skills, race extras, ice wall, highlight replay, results, look, frame-rate guard, app icon |
| `Assets/eva大象/` | The D.Va-style mech elephant (skins, water gun, boosters, sounds) |
| `Assets/SavannahCourse/`, `Assets/StudentWork/` | The instructor's savannah course package and an earlier student scene, kept for reference |
| `Assets/Settings/` | URP pipeline assets (`PC_*` desktop, `Mobile_*` headset) |
| `TrackingOverrides/` | Our modified Python bridge and gesture rules (locked), `requirements.txt` and 93 tests |
| `Tools/` | `setup_tracking_mac.sh` (the reconstruction script above), `release/` (packing the downloads), `bridge_app/` (the self-contained camera bridge for the macOS app and the Windows download), `perf_sim/` (checks the frame-rate guard outside Unity) |
| `Art/` | Blender script that builds the town kit (the `.blend` itself is not tracked) |
| `docs/` | The product page (`index.html`, `img/`) and notes for people: how to play ([DIVA_TRY_IT](docs/DIVA_TRY_IT.md)), Eva's log ([EVA_UPDATES](docs/EVA_UPDATES.md)), team notes ([NOTE_FOR_EVA](docs/NOTE_FOR_EVA.md), [NOTE_FOR_VICKY](docs/NOTE_FOR_VICKY.md)), [ROADMAP](docs/ROADMAP.md), [PLAYTEST_TODO](docs/PLAYTEST_TODO.md), [FAB_SETUP](docs/FAB_SETUP.md), the starter's [DIGIPHANT_CONTROLS](docs/DIGIPHANT_CONTROLS.md) |

At the root: this README, [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) (tested setup, mappings, recording, verification record
and limitations), [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and `CLAUDE.md` (instructions for the coding agents).

## Recording and data

F1 → **Record performance** saves a silent Game-view MP4 to `Recordings/`; every started run also writes `route.csv`,
`events.json` and `summary.json` to `TrialData/`. Both folders stay out of git: recordings go to the Drive folder above.
Details in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md).

## Verification

v2.0 checks (2026-10-13): 161 Python tests (including the password lock and the RFC 8032 signature vectors), the four
Unity validators and **Verify Lock** pass with 0 compile errors; the real tracking bridge ran Vicky's recorded P1 / P3
clips in one- and three-player mode; Eva played single-player and three-player live in Unity. What changed is in
[GESTURES_LOCKED.md](GESTURES_LOCKED.md) and [docs/EVA_UPDATES.md](docs/EVA_UPDATES.md) (items 30–32).

v1.1 checks (2026-10-13), automated unless stated:

- 93 Python tests in `TrackingOverrides/` pass, including the gesture-lock test, and the starter's 3 recording tests.
- Unity validators pass with 0 compile errors: startup / START flow, gesture scene setup, stationary turning, trial export.
- A full automatic round in batch mode (keyboard path, single player): candy, golden target, ON FIRE, ultimate,
  ice wall, all four tasks, finish line, gold lap (33.8 s), highlight, results, play again. A three-player run through
  the camera check with a fake camera bridge: calibration, P1 walks, P2 turns, P3 sprays and drinks.
- Frame-rate guard: `Tools/perf_sim` runs the real `DivaShowPerf.cs` on four simulated loads (CPU-bound by the camera
  tracking, GPU-bound, very heavy, light) and all checks pass; the v1.0 guard reproduces the 60 % resolution and 60 fps
  lock of Carl's live round.
- macOS (M1 Max, 3456×2168): Carl played a live single-player round with the camera on the release build: camera
  permission, camera check, calibration, gold lap. About 113 fps at full resolution with the camera tracking running.
  A copy marked as downloaded is stopped by Gatekeeper until it is allowed; once allowed, its camera bridge loads all
  three models.
- Windows, on GitHub's Windows Server 2025 machines (no webcam, software graphics): the v1.3 to v1.6 zips unpacked into
  Downloads; the bridge self-test passes (first start 5 s, later 1.3 s); a keyboard race from the home page to the
  race with no errors; with no camera the tracking restarts by itself (2, 4, 8, 16, 30 s); C on the camera check and
  F1 > Next camera restart it and the choice is saved.
- Historical v1.1 gaps included live camera play on Windows, a full three-person live performance and a fresh recording.
  See the v2.0 record above for later reported Unity live tests. Windows live-camera play and a new submission recording
  are not verified by this documentation update; see [docs/PLAYTEST_TODO.md](docs/PLAYTEST_TODO.md) and the limitations in
  [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md).

## Credits and licences

Kenney Starter-Kit-FPS (CC0) sprites, models and sounds; Saira and Liberation Sans fonts (SIL OFL); MediaPipe models
(Apache 2.0), downloaded at setup; the instructor's elephant and starter assets under the starter's terms, not
redistributed here; Fab rocket and clouds not included. Everything else was made by the team.
