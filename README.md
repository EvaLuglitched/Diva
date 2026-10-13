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
| **Drive** (recordings, side-by-side video) | **TODO: paste the shared folder link here** |
| **Scene** | `Assets/DigiPhant/Scenes/Diva.unity` |
| **Unity** | 6000.6.3f1, Universal Render Pipeline 17.6.0 |
| **Instructor starter** | https://github.com/kommanderpi/studentstarter.git at commit `8f426f675f86d938bf3dbec23d6a7ee2479d2c1d` |
| **Tested setup, mappings, recording, limitations** | [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) |

> 🔒 **Gesture controls are locked** (single-player and three-player, 2026-10-12). Read [GESTURES_LOCKED.md](GESTURES_LOCKED.md)
> before changing anything in `TrackingOverrides/diva_gestures.*`, `bridge.py`, `DivaControlState.cs` or `DivaDemo.cs`.

## Download v1.0

Ready-to-play builds: **https://github.com/EvaLuglitched/Diva/releases/tag/v1.0**

| Platform | File | Camera gestures |
| --- | --- | --- |
| macOS, Apple silicon (M1 and newer) | [Diva-v1.0-macOS-AppleSilicon.zip](https://github.com/EvaLuglitched/Diva/releases/download/v1.0/Diva-v1.0-macOS-AppleSilicon.zip) | built in: allow the camera when macOS asks |
| Windows 10 / 11, 64-bit | [Diva-v1.0-Windows-x64.zip](https://github.com/EvaLuglitched/Diva/releases/download/v1.0/Diva-v1.0-Windows-x64.zip) | run `DivaTracking\setup_camera.bat` once (needs Python 3.10+) |
| Apple Vision Pro | [Diva-VR-v1.0-visionOS.zip](https://github.com/EvaLuglitched/Diva/releases/download/v1.0/Diva-VR-v1.0-visionOS.zip) | headset hand tracking; installs on the team's registered headsets |

The keyboard works in every desktop build without a camera. Each zip has a short READ ME.

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
The instructor's shared elephant, the pose model, the Python environment, Unity caches, builds and recordings are not in
git (see `.gitignore`); these steps bring them back. Run them from the project folder (the one with `Assets/`).

**macOS** (one script, safe to run again):

```bash
bash Tools/setup_tracking_mac.sh
```

**Windows** (PowerShell):

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

Both paths: clone the starter **beside** `Assets` as `DigiPhantStarter` at the pinned commit, copy `Elephant/` and
`Elephant.meta` into `Assets` (keeping their metadata), copy our modified tracking code from `TrackingOverrides/` over the
starter's `Tracking/`, create the Python environment from `requirements.txt` and download the pose and hand models.
Do not copy the starter's `DigiPhant/` or `SavannahCourse/` over ours: ours are adapted and tracked. Then open the scene
in Unity 6000.6.3f1. The first open imports everything and takes a while.

Optional: the sky rocket and clouds are Fab models whose licence does not allow a public repository. Eva shares
`Diva_Fab_Assets.zip` privately ([docs/FAB_SETUP.md](docs/FAB_SETUP.md)); without it the scene shows our stand-ins.

**Reconstruction test** (as `STUDENT_GIT.md` asks), 2026-10-13 on a Mac in a separate folder: a fresh clone + the script restored the starter at the pinned commit, the elephant and the models; 93 + 3 Python tests passed; Unity opened it with 0 compile errors; all four validators passed; the scene had 0 missing scripts (the 11 missing prefabs are the optional Fab clouds and rocket); an automatic round finished with a gold lap. It also showed that macOS's built-in Python 3.9 is too old for the three-player detector, so the script now picks Python 3.10+. Details in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md). Windows reconstruction and live camera play were not part of this test.

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
| `Tools/` | `setup_tracking_mac.sh` (the reconstruction script above), `release/` (packing the downloads), `bridge_app/` (the camera bridge inside the macOS app) |
| `Art/` | Blender script that builds the town kit (the `.blend` itself is not tracked) |
| `docs/` | Notes for people: how to play ([DIVA_TRY_IT](docs/DIVA_TRY_IT.md)), Eva's log ([EVA_UPDATES](docs/EVA_UPDATES.md)), team notes ([NOTE_FOR_EVA](docs/NOTE_FOR_EVA.md), [NOTE_FOR_VICKY](docs/NOTE_FOR_VICKY.md)), [ROADMAP](docs/ROADMAP.md), [PLAYTEST_TODO](docs/PLAYTEST_TODO.md), [FAB_SETUP](docs/FAB_SETUP.md), the starter's [DIGIPHANT_CONTROLS](docs/DIGIPHANT_CONTROLS.md) |

At the root: this README, [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) (tested setup, mappings, recording, verification record
and limitations), [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and `CLAUDE.md` (instructions for the coding agents).

## Recording and data

F1 → **Record performance** saves a silent Game-view MP4 to `Recordings/`; every started run also writes `route.csv`,
`events.json` and `summary.json` to `TrialData/`. Both folders stay out of git: recordings go to the Drive folder above.
Details in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md).

## Verification

Latest checks (2026-10-13, macOS, M1 Max), all automated unless stated:

- 93 Python tests in `TrackingOverrides/` pass, including the gesture-lock test.
- Unity validators pass: startup / START flow, scene setup, stationary turning, trial export.
- A full automatic round in batch mode (keyboard path, single player): candy, golden target, ON FIRE, ultimate, ice wall,
  all four tasks, finish line, gold lap, hero card, highlight, results, play again. A three-player run through the camera
  check with a fake camera bridge: calibration, P1 walks, P2 turns, P3 sprays and drinks.
- macOS standalone build at 3456×2168: 106 fps at native resolution, a steady 120 fps with the frame-rate guard
  (checked by a person at the screen).
- Not automated: live camera play. Full three-person performance, the latest thumbs-up refill and head-tilt steering
  on a real camera, and a fresh recording still need a physical test; see [docs/PLAYTEST_TODO.md](docs/PLAYTEST_TODO.md)
  and the limitations in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md).

## Credits and licences

Kenney Starter-Kit-FPS (CC0) sprites, models and sounds; Saira and Liberation Sans fonts (SIL OFL); MediaPipe models
(Apache 2.0), downloaded at setup; the instructor's elephant and starter assets under the starter's terms, not
redistributed here; Fab rocket and clouds not included. Everything else was made by the team.
