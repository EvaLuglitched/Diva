# Note for Vicky (from Carl): three-player camera detection, branch `carl/pc-three-player`

Hi Vicky, Carl here. Three-player mode was not working for us: during the 10-second calibration only one player ever
turned green. Single player worked. I tracked it down in `TrackingOverrides/` (your area) and pushed the fix as branch
**`carl/pc-three-player`** (based on main after your 10-11 sync). Please look it over before it goes into main.

## What was wrong

1. `bridge.py` ran MediaPipe's PoseLandmarker in VIDEO mode. In that mode the detector only runs when tracking is lost,
   so it kept tracking the first body it found and rarely picked up the other two.
2. `PerformerTracker.assign` needed all three bodies in the **same frame** before assigning any role; anything less
   returned `{}` for the whole frame, so Unity never saw P2/P3.
3. The 0.5 confidence thresholds dropped bodies at the frame edge whose shoulders/hips were half visible.

## What changed (all in `TrackingOverrides/` unless noted)

- `bridge.py`
  - `GroupPoseDetector`: VIDEO-mode landmarker for smooth tracking **plus** an IMAGE-mode landmarker that searches the
    whole frame every 0.5 s. If the search sees a body the tracker does not have, the tracker is rebuilt (a fresh VIDEO
    instance detects everyone on its first frame, then tracks). Solo mode never searches. Low-quality bodies do not
    trigger rebuilds. Console prints `tracker rebuilt Nx`.
  - `PerformerTracker`: people are assigned as they arrive, left to right; roles re-sort while the group is incomplete
    and **lock** once all slots are filled or Unity saves the neutral pose (`divaCalibrate` → `tracker.lock()`).
    After locking, behaviour is exactly the old nearest-position tracking with ambiguity rejection.
  - Group confidence: pose detection/presence 0.35, "usable body" 0.4 (solo stays 0.5).
  - Packets carry `detected`, `usable`, `rolesLocked`; the console prints `Camera sees N bodies, M usable, K/3 assigned`.
- `diva_gestures.py` / `diva_gestures.json`: GO defaults `go_window .65→.80`, `go_range .16→.14`, `go_stop_delay .28→.35`
  (your note said walking was weak at low cadence). These are my guesses — please check them against your recordings
  with the tool below and put back the old values if they fit better.
- `replay_gestures.py` (new): replays a recording through the models + rules, prints a per-second timeline, and with a
  labels JSON gives hit/false rates per action; `--grid` tries threshold combinations. See `DIVA_TRY_IT.md` → "Tuning".
- `test_bridge.py`: one expectation updated (two of three visible now returns the two leftmost roles instead of `{}`),
  three tests added (progressive fill + lock, late arrivals rebuild the tracker, solo never searches). 50 tests pass.
- Unity side (small): `DigiPhantController.PoseFrame` gains `detected/usable/rolesLocked`; `DivaDemo.WaitingMessage`
  appends `(camera sees N bodies, M usable)`. (I had also made START queue the start while calibration was pending;
  Eva's new camera-check page supersedes that, so this branch takes her START flow as is.)

## Update (same day): smooth group detection

The first group version (VIDEO tracking + periodic search + tracker rebuilds) was laggy on a real three-person test and
still missed people, so it is gone. Now: group detection is per-frame IMAGE detection in a **worker thread** (the
camera loop never waits; it always uses the newest finished result), on a downscaled frame, with a performance profile
(`--profile auto|quality|fast`: Apple Silicon → full model at 960 px with hands every frame; otherwise lite at 640 px,
hands every other frame), and **One-Euro smoothing** of each assigned player's landmarks (per-frame detection has no
temporal filter of its own; beta .3 keeps a 2 Hz arm pump at ~80 % amplitude so GO still triggers). The GO rule also
survives frame gaps up to 0.6 s and Unity's visibility timeout is at least 0.8 s, so a hiccup no longer flickers a
player grey. Console line: `Camera sees 3 bodies, 3 usable, 3/3 assigned, pose NN ms/detection in the background`.

## What I could verify, and what I could not

- Unity's three-player pipeline end to end with a fake bridge sending three people's gestures over UDP: all three
  visible, auto-calibration completes in 10 s, P1 go / P2 steer / P3 spray + drink all drive the elephant.
- `GroupPoseDetector` against the real MediaPipe models (API use, rebuilds, mode switch): fine, ~20 ms/frame.
- **Not verified**: three real people in front of the camera. Please try it; the status line tells you which half
  fails (`sees 2 bodies` = detection, `3 bodies, 2 usable` = quality, `3/3 but waiting` = gesture confidence).

Thanks — and tell me if you would rather keep VIDEO-only with a different approach; the search interval and the
`same_body` distance are constructor arguments on `GroupPoseDetector`.
