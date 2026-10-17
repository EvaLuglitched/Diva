# DigiPhant / Diva setup and validation

The tested setup, mappings, recording instructions, verification record and known limitations for the Diva Unity
project. Links to the demo video, the mapping document and the Drive folder are in [README.md](README.md#course-submission).

## Fresh checkout

Use Unity **6000.6.3f1**, URP **17.6.0**. Restore the excluded Elephant assets and Python environment using [README.md](README.md#reconstruct-the-project-from-a-fresh-clone); on macOS use `bash Tools/setup_tracking_mac.sh`. Both setup paths pin starter commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` (the commit that added the savannah course, which our `Assets/SavannahCourse` matches; its elephant, tracking code and pose model are the same as at the earlier `8f426f6`), which supplies the elephant, pose model, recording encoder and its tests. The candy skin texture `Assets/eva大象/Textures/Elephant_D_Candy.jpg` is our recolour of the starter's `Elephant_D.png`, so it is not in git either: the macOS script rebuilds it byte-identically, the Windows steps download the team's copy from the Drive folder (README), and both must run before Unity first opens the project so the tracked `.meta` (GUID `3e003aaedaef24be6b55f57fe3cee1b3`) is kept. Keep `DigiPhantStarter/` beside Assets. Preserve metadata; do not replace the tracked DigiPhant assets with starter copies.

Use Python **3.10 or newer** (the three-player detector needs it; macOS's built-in 3.9 is too old). Mac checks used Python **3.13**; Windows verification used Python **3.14.7**, MediaPipe **0.10.35**, OpenCV package **4.14.0.94**. Install the pinned requirements in `DigiPhantStarter/Tracking/.venv`. The launcher synchronizes `TrackingOverrides/` gesture sources into Tracking and can download the hand model when missing.

Open **Assets/DigiPhant/Scenes/Diva.unity**. `Assets/StudentWork/DigiPhantStudent.unity` is an optional preserved student scene using the same shared scripts. Fab rocket/cloud assets are omitted under their license; follow [docs/FAB_SETUP.md](docs/FAB_SETUP.md). Runtime substitutes are expected. Preserve MissingPrefab references rather than deleting/resaving them before restoring optional assets.

**Reconstruction test, 2026-10-17** (after the candy texture left git and the starter pin moved to `4e6dab5`), on a Mac in a separate folder: a fresh clone + `bash Tools/setup_tracking_mac.sh` restored the starter at `4e6dab5`, the elephant, the models and the candy texture (rebuilt; its SHA-256 matches the Drive copy); 161 + 3 Python tests and the lock check passed; the first Unity 6000.6.3f1 import had 0 compile errors; the texture kept GUID `3e003aaedaef24be6b55f57fe3cee1b3` and `Elephant Candy.mat` uses it; `Diva.unity` has 1,597 objects, 0 missing scripts, 0 renderers without a material and 11 missing prefabs (the optional Fab clouds and rocket); the four validators passed. Opening Unity before the texture was restored deleted its `.meta`, as expected; running the script again restored both. Not covered: the Windows steps (Drive download), live camera play and an automatic race round.

**Earlier reconstruction test, 2026-10-13** (macOS, M1 Max, done the way `STUDENT_GIT.md` asks, in a separate folder): a new clone
of `carl/race-meta` (83 MB, 836 tracked files) → `bash Tools/setup_tracking_mac.sh` restored the starter at the then pinned
commit `8f426f6`, `Assets/Elephant` with its metadata, the Python environment and the pose / hand models → all 93 repository tests and
the starter's 3 recording tests passed with Python 3.13 → the first Unity 6000.6.3f1 open imported the project with 0
compile errors → the startup, setup, stationary-turn and trial-export validators passed → `Diva.unity` has 1,597 objects,
0 missing scripts, 0 renderers without a material and 11 missing prefabs, all of them the optional Fab clouds and rocket
([docs/FAB_SETUP.md](docs/FAB_SETUP.md)) → an automatic round (keyboard path) finished all four tasks with a gold lap.
The first run of the script used the macOS system Python 3.9: three of the locked bridge's three-player detector tests
fail there, so the script now picks Python 3.10 or newer and stops with instructions if there is none. Not covered by
this test: reconstruction on Windows and live camera play.

## Mappings and controls

The team mapping (performer, gesture, elephant part, neutral pose and tuning, why each role suits its performer, formation,
camera, timing and how shared inputs combine) is in [README.md](README.md#team-control-mapping) and the team's
[Google Doc](https://docs.google.com/document/d/1urKmUug0rV43_86DuJK7KGsfQCot1wsBSexF_cAui0s/edit). How to run it:

Play → F1 → **Try alone > All > Camera**. Keep the joints needed for your role visible: P1 needs the walking arm motion, P2 needs shoulders/head, and P3 needs the relevant arms/hands. P2 does not require visible hips or wrists in v2.0. Pick colour and players, START, then stand upright on the camera check: after 5 seconds with everyone recognised (green) the neutral pose is saved and the intro starts. Manual Test controls start immediately. Reassign after changing positions and recalibrate.

- Pump both arms for GO; stop pumping for STOP. Holding hands high overhead triggers confirmation/ultimate instead.
- Tilt the shoulders or the head toward one side to turn (left = the performer's own left), even with GO zero and relaxed arms; hips are not used. Level stops turning; shoulders briefly unclear keep the turn for 0.3 s. Thresholds relative to the calibrated neutral (slope of the shoulder or ear line, the larger counts): start 0.14, release 0.08, full turn 0.40.
- v2.0 steering confidence uses shoulders when wrists are missing, and role assignment uses shoulder centres rather than hips. Either-hand thumbs-up refill needs its matched wrist and shoulders; the opposite wrist may be hidden. The earlier requirement for visible hips during torso turning is historical and superseded; see [current verification](#current-v20-verification-recorded-2026-10-13).
- Solo refill/ultimate intentionally suppress turning; spray/refill/ultimate suppress forward motion.

See [docs/DIVA_TRY_IT.md](docs/DIVA_TRY_IT.md) for roles, spray/refill and group instructions. Stop Play to stop Unity's camera bridge.

## Recording and generated trial data

**Record performance** appears below the preview in F1. It requires calibrated Camera mode and live preview. Click **Stop recording and save**, then check `Recordings/<session>/performance.mp4`. It captures the Game view without audio; export holds images across capture gaps. Recordings stay out of git; put them in the team Drive folder.

Started runs automatically export to `TrialData/<UTC-session-and-GUID>/`: `route.csv`, `events.json`, `summary.json`. Reasons distinguish completed tasks (`tasks_completed`), crossing the finish arch with tasks left (`finish_line`), timeout, manual end and interruption. Route samples use scaled simulation time, nominally 30 Hz, capped at 600 seconds during Play/ultimate; no samples are fabricated to fill gaps. Mode/calibration metadata records start/end only, not complete tracking history. Manual runs must not be presented as gesture-controlled evidence.

Failed exports retain frozen snapshots until domain reload and partial files for recovery. Fix disk/access problems and use **Retry pending trial exports** on the Diva Show director before recompiling/reloading. Successful session IDs prevent duplicate exports.

## Release builds (desktop v2.0; Vision Pro v1.1)

Use the [current download links in README](README.md#downloads). The packaging notes below describe the release
pipeline; the frame-rate comparison records the earlier v1.0/v1.1 investigation.

Downloads: https://github.com/EvaLuglitched/Diva/releases (built with **Diva > Release** in `Assets/DivaShow/Editor/DivaReleaseBuild.cs`,
packed by `Tools/release/package_release.sh`). Both desktop builds carry the tracking bridge themselves, so players need
no Python: the locked `bridge.py`, frozen with MediaPipe 0.10.35 and Python 3.13 by `Tools/bridge_app/build_mac_bridge.sh`
(macOS) and `Tools/bridge_app/build_windows_bridge.ps1` (Windows; built on GitHub's Windows machines). The game starts it
with the editor's options; only the walking diagnostics CSV is off because the app folder can be read-only. macOS asks
for camera permission as soon as the game opens (one click; if it was denied, the home page shows ALLOW CAMERA, which
opens the setting). The frozen bridge keeps Matplotlib's font cache in `~/Library/Caches/Diva` (Windows:
`%LOCALAPPDATA%\Diva`), so only the first camera start on a computer takes longer (Mac about 17 s, Windows about 6 s);
later starts take 1–3 s. The macOS app is signed with a development certificate but not notarised (no Developer ID),
so a downloaded copy needs **Open Anyway** once in System Settings > Privacy & Security; tested: once Diva itself is
allowed, its camera bridge runs too. The Vision Pro build is development-signed for the team's headsets.

Frame-rate guard (`DivaShowPerf`): with the camera running, the M1 Max is CPU-bound (about 9 ms per frame at any
resolution). v1.0 still lowered the resolution to 0.6 and then locked the race to 60 fps in Carl's live round; v1.1
undoes a step that brings no gain, keeps full resolution (about 110 fps on a 120 Hz screen) and locks to 60 only below
96 fps. `Tools/perf_sim` runs the real `DivaShowPerf.cs` outside Unity on four simulated loads.

## Verification record and limitations

### Current v2.0 verification (recorded 2026-10-13)

The [README verification record](README.md#verification), [GESTURES_LOCKED.md](GESTURES_LOCKED.md) and
[Eva's update log, items 30–32](docs/EVA_UPDATES.md) report 161 Python tests, four Unity validators, Verify Lock and
zero compile errors. The real tracking bridge replayed Vicky's recorded clips in single- and three-player modes;
Eva also reported successful live single- and three-player tests in Unity. The 2026-10-17 fresh-clone reconstruction
above repeated the Python tests, the lock check and the four validators; it did not repeat the live tests.

For P2, v2.0 removes the hip/wrist dependency: shoulder confidence and shoulder-based role assignment preserve steering
when the lower body and hands are hidden. The recorded player-parts regression suite has 13 tests (eight fail with the
old code); replaying a P2 clip cropped below the chest improved steering delivery from 0–17% to 100%. The same update
retains a player's last Unity control for 0.3 s and gesture history for 0.6 s across brief detection gaps.

### Known limitations

- **Detection conditions.** Crowding, occlusion and lighting can still affect MediaPipe's detections. The evidence is
  recorded-clip replays, regression tests and Eva's live single- and three-player runs, not tests across different
  rooms, cameras or groups of performers.
- **Staying in place.** Each performer must keep the joints for their role in view (P1 the arm swing, P2 the shoulders
  and head, P3 the hands) and stay near the position saved by the camera check; after moving, run the check again.
- **Single player.** Spraying, refilling and the ultimate pause walking, and refill / ultimate pause turning, so one
  person cannot do everything at once.
- **Windows.** Live camera play and the Windows reconstruction steps are not verified (GitHub's Windows machines have no
  webcam). In the v1.1 Windows test, a game folder whose path had non-ASCII characters (for example
  `C:\Users\张三\Downloads`) stopped MediaPipe from opening its model, so camera gestures never started; this is not
  fixed, so keep the game in a folder path with only ASCII characters.
- **Signing.** The macOS app is development-signed, not notarised (**Open Anyway** once). The Vision Pro build stays on
  v1.1, installs only on the team's registered headsets, and its branch needs a Unity Pro licence.
- **Setup.** Python 3.10 or newer is required. In a fresh clone, restore the candy texture before Unity first opens the
  project, or Unity deletes its `.meta` (fix: `git checkout -- "Assets/eva大象/Textures/Elephant_D_Candy.jpg.meta"`).
- **Optional assets.** The Fab rocket and clouds are not in the repository (licence); without them the scene shows
  stand-ins and 11 missing prefab references are expected.
- **Trial data.** Keyboard and manual runs are logged the same way as gesture runs and must not be presented as
  gesture-controlled evidence; calibration metadata records only the start and end of a run.

### Historical checks

The entries below preserve earlier evidence and open questions at the time. Their test counts, weak-walking report,
hip-based steering requirement and pending three-person/native checks are not the current v2.0 status; use the
[current verification above](#current-v20-verification-recorded-2026-10-13) and its linked records for later evidence.

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
- v1.1 gaps at the time: live camera play on Windows, a full three-person live performance and a fresh recording
  ([docs/PLAYTEST_TODO.md](docs/PLAYTEST_TODO.md)).

Earlier stages:

- Earlier: 65 Python tests passed (62 repository tests plus three starter recording tests); dependencies and pose/hand model loading passed locally.
- Earlier native Unity gesture, START guard, stationary-turn and trial-export validators passed. Stationary-turn checks exercise actual locomotion with both yaw directions, zero speed, unchanged xyz, upright/stale stopping and solo action gates. Export checks cover immutable snapshots, identifiers, decimal formatting, JSON escaping, retry/idempotence and component teardown. The original note said these had not been repeated on the then-combined checkout; later v2.0 validation is recorded above.
- Historical local live checks observed camera landmarks, solo GO/STOP and left/right steering with elephant translation/rotation. A silent 35.2-second, 1280×636 MP4 decoded all 352 frames at 10 fps; 156 captured images were held across gaps. A decoded frame visibly contained webcam landmarks and elephant.
- A historical interrupted trial produced 3,217 route samples through 174.60672 simulation seconds and 13 events. Its duration/end-mode metadata was incorrect due to teardown ordering. Those original local files remain unchanged and their affected metadata is invalid. A caching fix passed native regression; a new post-fix physical finish/shutdown export remains unverified.
- Historical status before the later v2.0 updates: thumbs-up refill and restored torso-only steering had automated verification but no fresh physical test; full three-person control, complete course interaction and cross-machine restoration were pending. Walking was reported weak/intermittent, with GO rules unchanged at that point. Archived low-cadence replay exposed a limitation without establishing the live cause. These observations must not be read as a new test of v2.0.
- Gesture-restoration stage (65 tests): P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumbs-up is recognised by MediaPipe's pretrained Gesture Recognizer (`Thumb_Up`, `gesture_recognizer.task`, downloaded on first use) or, as a fallback, the geometric rule. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 tilts the shoulders or the head toward one side (calibrated; hips are not used and may be hidden); wrists may be hidden. Level stops turning, including while stationary; shoulders briefly unclear keep the turn for 0.3 s. The publication Python suite passed 62 override tests; three starter recording tests also passed (65 total). Optional MediaPipe hand confidence set to `None` no longer crashes thumbs-up detection; explicit low or invalid confidence remains rejected. Upstream keyboard and finish-line behavior is preserved.

[TrackingOverrides/VALIDATION.md](TrackingOverrides/VALIDATION.md) details archived reference-fit tradeoffs. Pretrained MediaPipe plus rules is used; no custom neural action classifier was trained. Calibration alters a steering baseline, not model weights.

Recordings, raw datasets, trial outputs, local logs/backups, environments, caches and licensed shared assets are excluded from Git. The historical evidence above describes local checks and is not included as reproducible test media. Keep new performance evidence separately and document its provenance.
