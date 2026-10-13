# DigiPhant / Diva setup and validation

## Fresh checkout

**Reconstruction test, 2026-10-13** (macOS, M1 Max, done the way `STUDENT_GIT.md` asks, in a separate folder): a new clone
of `carl/race-meta` (83 MB, 836 tracked files) → `bash Tools/setup_tracking_mac.sh` restored the starter at the pinned
commit, `Assets/Elephant` with its metadata, the Python environment and the pose / hand models → all 93 repository tests and
the starter's 3 recording tests passed with Python 3.13 → the first Unity 6000.6.3f1 open imported the project with 0
compile errors → the startup, setup, stationary-turn and trial-export validators passed → `Diva.unity` has 1,597 objects,
0 missing scripts, 0 renderers without a material and 11 missing prefabs, all of them the optional Fab clouds and rocket
([docs/FAB_SETUP.md](docs/FAB_SETUP.md)) → an automatic round (keyboard path) finished all four tasks with a gold lap.
The first run of the script used the macOS system Python 3.9: three of the locked bridge's three-player detector tests
fail there, so the script now picks Python 3.10 or newer and stops with instructions if there is none. Not covered by
this test: reconstruction on Windows and live camera play.

Use Unity **6000.6.3f1**, URP **17.6.0**. Restore the excluded Elephant assets and Python environment using [README.md](README.md); on macOS use `bash Tools/setup_tracking_mac.sh`. Both setup paths pin starter commit `8f426f675f86d938bf3dbec23d6a7ee2479d2c1d`, which supplies the elephant, pose model, recording encoder and its tests. Keep `DigiPhantStarter/` beside Assets. Preserve metadata; do not replace the tracked DigiPhant assets with starter copies.

Use Python **3.10 or newer** (the three-player detector needs it; macOS's built-in 3.9 is too old). Mac checks used Python **3.13**; Windows verification used Python **3.14.7**, MediaPipe **0.10.35**, OpenCV package **4.14.0.94**. Install the pinned requirements in `DigiPhantStarter/Tracking/.venv`. The launcher synchronizes `TrackingOverrides/` gesture sources into Tracking and can download the hand model when missing.

Open **Assets/DigiPhant/Scenes/Diva.unity**. `Assets/StudentWork/DigiPhantStudent.unity` is an optional preserved student scene using the same shared scripts. Fab rocket/cloud assets are omitted under their license; follow [docs/FAB_SETUP.md](docs/FAB_SETUP.md). Runtime substitutes are expected. Preserve MissingPrefab references rather than deleting/resaving them before restoring optional assets.

## Controls

Play → F1 → **Try alone > All > Camera**. Keep shoulders, hips and wrists visible; pick colour and players, START, then stand upright on the camera check: after 5 seconds with everyone recognised (green) the neutral pose is saved and the intro starts. Manual Test controls start immediately. Reassign after changing positions and recalibrate.

- Pump both arms for GO; stop pumping for STOP. Holding hands high overhead triggers confirmation/ultimate instead.
- Tilt the shoulders or the head toward one side to turn (left = the performer's own left), even with GO zero and relaxed arms; hips are not used. Level stops turning; shoulders briefly unclear keep the turn for 0.3 s. Thresholds relative to the calibrated neutral (slope of the shoulder or ear line, the larger counts): start 0.14, release 0.08, full turn 0.40.
- Torso turning requires valid shoulders/hips but not wrists. Either-hand thumbs-up refill needs only its matched wrist and shoulders; missing wrists still block GO. Calibration records a fresh hip baseline only with valid hips, otherwise retaining an earlier valid baseline or using zero. Ready alone does not prove fresh hip calibration.
- Solo refill/ultimate intentionally suppress turning; spray/refill/ultimate suppress forward motion.

See [docs/DIVA_TRY_IT.md](docs/DIVA_TRY_IT.md) for roles, spray/refill and group instructions. Stop Play to stop Unity's camera bridge.

## Release builds (v1.0)

Downloads: https://github.com/EvaLuglitched/Diva/releases (built with **Diva > Release** in `Assets/DivaShow/Editor/DivaReleaseBuild.cs`,
packed by `Tools/release/package_release.sh`). The macOS app carries the tracking bridge itself, frozen with Python 3.13
and MediaPipe 0.10.35 by `Tools/bridge_app/build_mac_bridge.sh` (the locked `bridge.py` runs unchanged, with the editor's
options; only the walking diagnostics CSV is off because the app folder is read-only) and asks for camera permission
as soon as the game opens (one click; if it was denied, the home page shows ALLOW CAMERA, which opens the setting). The Windows build carries the bridge sources and models in `DivaTracking/`; `setup_camera.bat` creates the
Python environment once. The Vision Pro build is development-signed for the team's headsets.

## Recording and generated trial data

**Record performance** appears below the preview in F1. It requires calibrated Camera mode and live preview. Click **Stop recording and save**, then check `Recordings/<session>/performance.mp4`. It captures the Game view without audio; export holds images across capture gaps.

Started runs automatically export to `TrialData/<UTC-session-and-GUID>/`: `route.csv`, `events.json`, `summary.json`. Reasons distinguish completed tasks (`tasks_completed`), crossing the finish arch with tasks left (`finish_line`), timeout, manual end and interruption. Route samples use scaled simulation time, nominally 30 Hz, capped at 600 seconds during Play/ultimate; no samples are fabricated to fill gaps. Mode/calibration metadata records start/end only, not complete tracking history. Manual runs must not be presented as gesture-controlled evidence.

Failed exports retain frozen snapshots until domain reload and partial files for recovery. Fix disk/access problems and use **Retry pending trial exports** on the Diva Show director before recompiling/reloading. Successful session IDs prevent duplicate exports.

## Verification record and limitations

- **2026-10-13**: 93 repository Python tests and the 3 starter recording tests pass (Python 3.13); the four Unity validators pass on the current checkout and on a fresh clone; automatic single-player rounds and a three-player run through a fake camera bridge pass (see the reconstruction test above and README → Verification).
- Earlier: 65 Python tests passed (62 repository tests plus three starter recording tests); dependencies and pose/hand model loading passed locally.
- Earlier native Unity gesture, START guard, stationary-turn and trial-export validators passed. Stationary-turn checks exercise actual locomotion with both yaw directions, zero speed, unchanged xyz, upright/stale stopping and solo action gates. Export checks cover immutable snapshots, identifiers, decimal formatting, JSON escaping, retry/idempotence and component teardown. These historical checks have not been repeated on the current combined checkout.
- Historical local live checks observed camera landmarks, solo GO/STOP and left/right steering with elephant translation/rotation. A silent 35.2-second, 1280×636 MP4 decoded all 352 frames at 10 fps; 156 captured images were held across gaps. A decoded frame visibly contained webcam landmarks and elephant.
- A historical interrupted trial produced 3,217 route samples through 174.60672 simulation seconds and 13 events. Its duration/end-mode metadata was incorrect due to teardown ordering. Those original local files remain unchanged and their affected metadata is invalid. A caching fix passed native regression; a new post-fix physical finish/shutdown export remains unverified.
- Thumbs-up refill and restored torso-only steering have automated verification, but no fresh physical test after those changes. Full three-person control, complete course interaction and cross-machine restoration remain pending. Walking is reported weak/intermittent; GO rules are unchanged. Archived low-cadence replay exposes a limitation without establishing the live cause.

[TrackingOverrides/VALIDATION.md](TrackingOverrides/VALIDATION.md) details archived reference-fit tradeoffs. Pretrained MediaPipe plus rules is used; no custom neural action classifier was trained. Calibration alters a steering baseline, not model weights.

Recordings, raw datasets, trial outputs, local logs/backups, environments, caches and licensed shared assets are excluded from Git. The historical evidence above describes local checks and is not included as reproducible test media. Keep new performance evidence separately and document its provenance.

### Latest gesture update verification

P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumbs-up is recognised by MediaPipe's pretrained Gesture Recognizer (`Thumb_Up`, `gesture_recognizer.task`, downloaded on first use) or, as a fallback, the geometric rule. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 tilts the shoulders or the head toward one side (calibrated; hips are not used and may be hidden); wrists may be hidden. Level stops turning, including while stationary; shoulders briefly unclear keep the turn for 0.3 s.

The publication Python suite passed 62 override tests; three starter recording tests also passed (65 total). Optional MediaPipe hand confidence set to `None` no longer crashes thumbs-up detection; explicit low or invalid confidence remains rejected. Fresh native Unity and physical checks of this restoration and the combined keyboard/finish-line checkout remain pending. Upstream keyboard and finish-line behavior is preserved.
