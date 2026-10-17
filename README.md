# Diva — DigiPhant mech-elephant race

**Team Diva**: Vicky (P1, [@vickyyan-rgb](https://github.com/vickyyan-rgb)), Eva (P2, [@EvaLuglitched](https://github.com/EvaLuglitched)),
Jiayu / Carl (P3, [@jchen60-bot](https://github.com/jchen60-bot)) · DesComp Project 2: Digital Twins (DigiPhant)

One elephant, driven by three bodies. A webcam and pretrained MediaPipe models read the performers; gesture rules turn
their movements into walking, turning, aiming, spraying water, eating / drinking and an ultimate. The elephant wears a
D.Va-style mech suit and runs the supplied obstacle course, dressed as Eva's candy town: targets, the course tasks, candy,
an ice wall before the finish, a highlight replay and a leaderboard. No custom neural action model was trained:
pretrained MediaPipe plus calibrated rules.

## Course submission

| Item | Where |
| --- | --- |
| **Git repository** | https://github.com/EvaLuglitched/Diva (branch `main`) |
| **Drive folder** (demo video, mapping document, supporting files) | https://drive.google.com/drive/folders/16mkenRzNIFoxs1Y-Xad8nl92Xh22DkiC |
| **Demo video** (performers and elephant side by side) | [diva demo video](https://drive.google.com/file/d/1CX1NiwhmPOBNsl2U8RO0b-O5kjZPcWaQ/view) |
| **Mapping document** | [Diva: Team Control Mapping](https://docs.google.com/document/d/1urKmUug0rV43_86DuJK7KGsfQCot1wsBSexF_cAui0s/edit) (Google Doc); the same mapping is [below](#team-control-mapping) |
| **Group reflection** | [below](#group-reflection) |
| **Scene used in the performance** | `Assets/DigiPhant/Scenes/Diva.unity` (the only scene in the build list) |
| **Setup, mappings, recording, known limitations** | [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) |
| **Unity** | 6000.6.3f1, Universal Render Pipeline 17.6.0 |
| **Instructor starter** (not in git: elephant, pose model) | https://github.com/kommanderpi/studentstarter.git at commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f`; see [reconstruct](#reconstruct-the-project-from-a-fresh-clone) |
| **Changed elephant texture** (not in git) | [Elephant_D_Candy.jpg](https://drive.google.com/file/d/1pGykqq3OCjHfO_ycppod0eQB_CUkr98n/view) and its [restore note](https://drive.google.com/file/d/16gKlvuz9Wy6ZqizBjQG246WxpmfvKkBX/view) in the Drive folder |

Not in git, by design: Unity `Library/`, `Temp/`, `Logs/`, builds, Python `.venv`, recordings and trial data (they go to
Drive), the starter's elephant models / textures and MediaPipe models, and the Fab rocket / clouds (licence). See
`.gitignore`.

## Team control mapping

Camera → `TrackingOverrides/bridge.py` (MediaPipe Pose Landmarker + Gesture Recognizer) → `diva_gestures.py` rules →
UDP JSON on localhost 5055 → `DivaDemo` / `DivaControlState` → locomotion, trunk water gun, tasks and ultimate.

| Name / ID | Movement or gesture | Elephant part or behaviour | Neutral pose, sensitivity, inversion, smoothing | Why this assignment |
| --- | --- | --- | --- | --- |
| Vicky, **P1** | Pump both arms; bring one hand to navel height at least once a second | Forward walking (the elephant's pace) | Still, relaxed arms = stop; walking stops 0.25 s after the motion ends; unequal arm swings still count | Sets the team's pace, leaving Eva free to steer and Jiayu free to aim |
| Eva, **P2** | Tilt the head or shoulders left / right | Whole-elephant steering, also while standing still | Level shoulders and centred head = straight; tilt start / release / full turn 0.14 / 0.08 / 0.40 of the calibrated neutral; direction −1 corrects the mirrored image; hips and wrists are not needed | A dedicated direction cue, so walking and steering are coordinated independently |
| Jiayu, **P3** | Raise one hand; push both open palms forward; hold a still thumbs-up 0.4 s | Trunk aim; water spray; refill (and the eat / drink course tasks) | Relaxed hands = no spray or refill; hands must be clearly visible; refill takes priority over spraying | Jiayu performs P3's hand poses most consistently, so the detailed water controls stay reliable |

**Formation and camera.** A fixed frontal webcam at about chest height, far enough back to show P1's arm path and P3's
hands. One row: Vicky / P1 on the left of the mirrored preview, Eva / P2 in the centre, Jiayu / P3 on the right, with
space between bodies. The camera check saves everyone's neutral pose and role position after 5 s of stable detection;
performers keep those positions for the run.

**Timing.** A spoken "ready" before starting. Vicky sets the pace, Eva calls a turn before each obstacle, Jiayu calls
refill pauses. Everyone returns to neutral between trials so each action has a clear start and end.

**Inputs that share a behaviour or a part.** In team mode walking comes only from P1, steering only from P2 and water
controls only from P3. Walking and turning combine; Eva can also turn while Vicky is stopped. Aim and spray share the
trunk; refill takes priority over spray, and an empty tank blocks spray. Anyone can trigger the shared ultimate (both
hands above the head for 0.8 s), so the team uses one agreed cue. Group landmarks are smoothed with a One-Euro filter;
a briefly missing player keeps their last control for up to 0.3 s before it drops.

**Additional MediaPipe vision task.** MediaPipe **Gesture Recognizer** (with its hand landmarks), next to the starter's
Pose Landmarker. Its pretrained `Thumb_Up` label drives refill; open-hand geometry drives spray.

**Retained from the starter:** P1 moves the elephant, P3 controls the trunk, body-relative hand / lean measurements and
neutral calibration. **Changed:** P1's raised-hand travel trigger became arm pumping; steering moved from P1's lean to
P2's head / shoulder tilt; the separate front / rear leg and tail / head / ear pose mappings became coordinated
locomotion plus dedicated water controls; our earlier hand-to-mouth refill became a still thumbs-up.

Quick reference (all controls, with the keyboard fallback):

| Control | Gesture | Who (three players) | Keyboard |
| --- | --- | --- | --- |
| Walk / run | Pump both arms up and down; stop pumping to stop | P1 (left) | W or ↑ |
| Turn | Tilt your shoulders or your head to one side (your own left = left), works standing still | P2 (middle) | A / D or ← / → |
| Aim the trunk | Raise one arm | P3 (right) | U / I |
| Spray water | Push both open hands forward; hold to keep spraying | P3 | Space (hold) |
| Eat, drink, refill water | Thumbs-up held 0.4 s (near the bush / basin for the tasks) | P3 | F |
| MEGA BUBBLE (ultimate) | Both hands above your head for 0.8 s, when charged | anyone | E |
| Menus | Raise one arm = next skin; both hands up = start / skip / play again | anyone | ← → and Enter |

In single-player mode (**Try alone > All**) one person has every control; spraying, refilling and the ultimate pause
walking so the arm gestures do not fight each other. Exact thresholds: [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and
`TrackingOverrides/diva_gestures.json`. Course tasks: eat at the feeding bush, drink at the water basin (thumbs-up near
each), go around the fallen log, cross the finish arch.

> 🔒 **The gesture controls are password-locked** (v2.0). Read [GESTURES_LOCKED.md](GESTURES_LOCKED.md) before changing
> anything in `TrackingOverrides/diva_gestures.*`, `bridge.py`, `DivaControlState.cs` or `DivaDemo.cs`.

## Group reflection

One tested improvement was making P2 steering depend on the shoulders and head without requiring visible hips or
wrists. The v2.0 regression record reports 13 player-parts tests (eight failed against the old code), and a replay of
Vicky's P2 clip cropped below the chest increased steering delivery to Unity from 0–17% to 100%. This addresses the
three-person occlusion problem while keeping the steering gesture simple. Evidence and implementation history:
[GESTURES_LOCKED.md](GESTURES_LOCKED.md) and [Eva's update log, items 31–32](docs/EVA_UPDATES.md).

A remaining limitation is that crowding, occlusion and lighting can still affect MediaPipe's detections. The recorded
clips and regression scenarios do not establish reliability for every room, camera or group of performers; they are
replay and regression results, not a new live-camera test.

## Reconstruct the project from a fresh clone

The repository tracks our scenes, scripts, mappings, materials, `.meta` files, `Packages/` and `ProjectSettings/`.
The instructor's shared elephant, our candy recolour of its texture, the pose model, the Python environment, Unity caches,
builds and recordings are not in git (see `.gitignore`); these steps bring them back. Run them from the project folder
(the one with `Assets/`).

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

Both paths: clone the starter **beside** `Assets` as `DigiPhantStarter` at the pinned commit (the commit that added the
savannah course; its `Elephant/`, `DigiPhant/` and `Tracking/` are the same as at the earlier `8f426f6`), copy `Elephant/`
and `Elephant.meta` into `Assets` (keeping their metadata), copy our modified tracking code from `TrackingOverrides/` over
the starter's `Tracking/`, create the Python environment from `requirements.txt` and download the pose and hand models.
Restore the candy skin texture `Assets/eva大象/Textures/Elephant_D_Candy.jpg`, our recolour of the starter's
`Elephant/Textures/Elephant_D.png`: the macOS script rebuilds it byte-identically with `Assets/eva大象/Source~/make_candy_texture.py`,
the Windows steps download the team's copy from [Drive](https://drive.google.com/file/d/1pGykqq3OCjHfO_ycppod0eQB_CUkr98n/view)
(OpenCV cannot write to the non-ASCII folder name on Windows). Do this **before** Unity first opens the project: the
texture's `.meta` is tracked (GUID `3e003aaedaef24be6b55f57fe3cee1b3`, used by `Elephant Candy.mat`) and Unity deletes a
`.meta` whose file is missing. If that happened, run `git checkout -- "Assets/eva大象/Textures/Elephant_D_Candy.jpg.meta"`
and reopen the project. Do not copy the starter's `DigiPhant/` or `SavannahCourse/` over ours: ours are adapted and
tracked. Then open the scene in Unity 6000.6.3f1. The first open imports everything and takes a while.

**Password lock (v2.0):** the action detection, the controls and the camera / player lock are signed with Eva's
password (`Tools/lock/`, see [GESTURES_LOCKED.md](GESTURES_LOCKED.md)). After cloning, turn on the commit check once:
`git config core.hooksPath .githooks`. `python3 Tools/lock/diva_lock.py verify` checks the locked files; GitHub and
Unity (**DigiPhant > Diva > Verify Lock**, and every release build) check them too.

Optional: the sky rocket and clouds are Fab models whose licence does not allow a public repository. Eva shares
`Diva_Fab_Assets.zip` privately ([docs/FAB_SETUP.md](docs/FAB_SETUP.md)); without it the scene shows our stand-ins.

**Reconstruction test, 2026-10-17** (as `STUDENT_GIT.md` asks; Mac, separate folder): a fresh clone +
`bash Tools/setup_tracking_mac.sh` restored the starter at `4e6dab5`, the elephant, the models and the candy texture
(rebuilt; its SHA-256 matches the Drive copy); 161 + 3 Python tests and the lock check passed; the first Unity 6000.6.3f1
import had 0 compile errors; the texture kept its GUID and `Elephant Candy.mat` uses it; `Diva.unity` has 1,597 objects,
0 missing scripts, 0 renderers without a material and 11 missing prefabs (the optional Fab clouds and rocket); the four
validators passed. Opening Unity before the texture was restored deleted its `.meta`, as expected; running the script
again restored both. Not covered: the Windows steps, live camera play and an automatic race round. The earlier
2026-10-13 test is in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md#fresh-checkout).

## Play

1. Open `Assets/DigiPhant/Scenes/Diva.unity` and press Play. Unity starts the local camera bridge itself.
2. Pick a skin and 1 or 3 players, then **START**. In Camera mode a large camera check follows: stand naturally with
   your arms down; when everyone has been green for 5 seconds the neutral pose is saved and Eva's race intro plays.
   Keyboard / Test controls skip the check.
3. Race: candy (+15), golden targets (×3), ON FIRE (×1.5), four tasks, an ice wall that only the MEGA BUBBLE breaks,
   the finish arch, then a hero card, a highlight replay, results and the local leaderboard.

F1 or **SETUP** shows the setup panel (camera preview, Try alone / roles, calibration, **Record performance**).
F2 shows the frame rate. Everything a player sees is described in [docs/DIVA_TRY_IT.md](docs/DIVA_TRY_IT.md).

## Recording and data

F1 → **Record performance** saves a silent Game-view MP4 to `Recordings/`; every started run also writes `route.csv`,
`events.json` and `summary.json` to `TrialData/`. Both folders stay out of git: recordings go to the Drive folder above.
Details in [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md#recording-and-generated-trial-data).

## Downloads

Ready-to-play builds (no Unity or Python needed): macOS and Windows **v2.0**
https://github.com/EvaLuglitched/Diva/releases/tag/v2.0, Apple Vision Pro **v1.1**
https://github.com/EvaLuglitched/Diva/releases/tag/v1.1. What changed in each version is on its release page.

| Platform | File | First start | Camera gestures |
| --- | --- | --- | --- |
| macOS, Apple silicon (M1 and newer) | [Diva-v2.0-macOS-AppleSilicon.zip](https://github.com/EvaLuglitched/Diva/releases/download/v2.0/Diva-v2.0-macOS-AppleSilicon.zip) | drag `Diva.app` to Applications and open it; it is signed but not notarised, so the first time: System Settings > Privacy & Security > **Open Anyway** (macOS 14 and older: right-click > Open) | built in: macOS asks for the camera when the game opens, one click on Allow |
| Windows 10 / 11, 64-bit | [Diva-v2.0-Windows-x64.zip](https://github.com/EvaLuglitched/Diva/releases/download/v2.0/Diva-v2.0-Windows-x64.zip) | unzip, run `Diva.exe`; if SmartScreen appears: More info > Run anyway | built in, no Python: allow the camera if Windows asks; wrong camera: C on the camera check or F1 > Next camera |
| Apple Vision Pro | [Diva-VR-v1.1-visionOS.zip](https://github.com/EvaLuglitched/Diva/releases/download/v1.1/Diva-VR-v1.1-visionOS.zip) | development-signed: installs on the team's registered headsets | headset hand tracking |

The keyboard works in every desktop build without a camera. The very first camera start on a computer takes longer
while the tracking prepares itself once (Mac about 17 s, Windows about 6 s). The Vision Pro version (first person, lean
forward = walk, tilt your head = turn, look and pinch = spray) is built from branch `carl/diva-vr`, which needs a Unity
Pro licence and is not merged into `main`. Product page: https://diva-elephant.github.io (source copy in
[docs/index.html](docs/index.html)).

## Verification

v2.0 (2026-10-13): 161 Python tests (including the password lock and the RFC 8032 signature vectors), the four Unity
validators and **Verify Lock** pass with 0 compile errors; the real tracking bridge ran Vicky's recorded P1 / P3 clips in
one- and three-player mode; Eva played single-player and three-player live in Unity. What changed is in
[GESTURES_LOCKED.md](GESTURES_LOCKED.md) and [docs/EVA_UPDATES.md](docs/EVA_UPDATES.md) (items 30–32). Fresh-clone
reconstruction: [above](#reconstruct-the-project-from-a-fresh-clone). Earlier checks (v1.1 automatic rounds, the
frame-rate guard, Windows on GitHub's machines) and known limitations: [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md#verification-record-and-limitations).

## Repository map

| Folder | What is in it |
| --- | --- |
| `Assets/DigiPhant/` | The adapted DigiPhant layer: camera-bridge receiver, calibration, locomotion, the Diva gesture state (locked) and the scenes (`Diva.unity` is the performance scene; `DigiPhant.unity` is the adapted starter scene it was built from) |
| `Assets/DivaGame/` | Eva's candy-town game layer: town models, targets, course tasks, race intro, keyboard controls |
| `Assets/DivaShow/` | The match flow: skins, HUD, skills, race extras, ice wall, highlight replay, results, look, frame-rate guard, app icon |
| `Assets/eva大象/` | The D.Va-style mech elephant (skins, water gun, boosters, sounds); `Source~/` holds the scripts that generate its mesh data, sounds and candy texture |
| `Assets/SavannahCourse/` | The instructor's savannah obstacle course, unchanged from the pinned starter; `Diva.unity` uses its generated layout 4 |
| `Assets/StudentWork/` | An earlier student scene using the same scripts, kept for reference |
| `Assets/Settings/` | URP pipeline assets (`PC_*` desktop, `Mobile_*` headset) |
| `TrackingOverrides/` | Our modified Python bridge and gesture rules (locked), `requirements.txt` and their tests |
| `Tools/` | `setup_tracking_mac.sh` (the reconstruction script above), `lock/` (the v2.0 password lock), `release/` (packing the downloads), `bridge_app/` (the self-contained camera bridge for the macOS app and the Windows download), `perf_sim/` (checks the frame-rate guard outside Unity) |
| `Art/` | Blender script that builds the town kit (the `.blend` itself is not tracked) |
| `docs/` | How to play ([DIVA_TRY_IT](docs/DIVA_TRY_IT.md)), [FAB_SETUP](docs/FAB_SETUP.md), the starter's [DIGIPHANT_CONTROLS](docs/DIGIPHANT_CONTROLS.md), the product page (`index.html`, `img/`) and the team's working notes: Eva's log ([EVA_UPDATES](docs/EVA_UPDATES.md)), [NOTE_FOR_EVA](docs/NOTE_FOR_EVA.md), [NOTE_FOR_VICKY](docs/NOTE_FOR_VICKY.md), [ROADMAP](docs/ROADMAP.md), [PLAYTEST_TODO](docs/PLAYTEST_TODO.md) |
| `process/` | How we worked: timeline, releases, pull requests and branches ([process/README.md](process/README.md)) |

At the root: this README, [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) (tested setup, mappings, recording, verification record
and known limitations), [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and `CLAUDE.md` (instructions for the coding agents).

## Credits and licences

Kenney Starter-Kit-FPS (CC0) sprites, models and sounds; Saira and Liberation Sans fonts (SIL OFL); MediaPipe models
(Apache 2.0), downloaded at setup; the instructor's elephant and starter assets under the starter's terms, not
redistributed here; Fab rocket and clouds not included. Everything else was made by the team.
