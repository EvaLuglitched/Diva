# DigiPhant / Diva setup and validation

## Fresh checkout

Use Unity **6000.6.3f1**, URP **17.6.0**. Restore the excluded Elephant assets and Python environment using [README.md](README.md); on macOS use `bash Tools/setup_tracking_mac.sh`. Both setup paths pin starter commit `8f426f675f86d938bf3dbec23d6a7ee2479d2c1d`, which supplies the elephant, pose model, recording encoder and its tests. Keep `DigiPhantStarter/` beside Assets. Preserve metadata; do not replace the tracked DigiPhant assets with starter copies.

Windows verification used Python **3.14.7**, MediaPipe **0.10.35**, OpenCV package **4.14.0.94**. Install the pinned requirements in `DigiPhantStarter/Tracking/.venv`. The launcher synchronizes `TrackingOverrides/` gesture sources into Tracking and can download the hand model when missing.

Open **Assets/DigiPhant/Scenes/Diva.unity**. `Assets/StudentWork/DigiPhantStudent.unity` is an optional preserved student scene using the same shared scripts. Fab rocket/cloud assets are omitted under their license; follow [FAB_SETUP.md](FAB_SETUP.md). Runtime substitutes are expected. Preserve MissingPrefab references rather than deleting/resaving them before restoring optional assets.

## Controls

Play → F1 → **Try alone > All > Camera**. Keep shoulders, hips and wrists visible; stand upright through **Get ready (10 seconds)**, then START. Camera START is blocked before calibration; manual Test controls can start immediately. Reassign after changing positions and recalibrate.

- Pump both arms for GO; stop pumping for STOP. Holding hands high overhead triggers confirmation/ultimate instead.
- Hold both hands together at chest height like praying and lean the torso to turn even with GO zero. Upright, separating hands or lost tracking stops turning. Thresholds relative to valid neutral: start 0.12, release 0.07, full turn 0.35. The following camera can make scenery rotate while the elephant stays centered.
- Prayer turning requires both wrists and valid shoulders/hips. Either-hand thumbs-up refill needs only its matched wrist and shoulders; missing wrists still block GO. Calibration records a fresh hip baseline only with valid hips, otherwise retaining an earlier valid baseline or using zero. Ready alone does not prove fresh hip calibration.
- Solo refill/ultimate intentionally suppress turning; spray/refill/ultimate suppress forward motion.

See [DIVA_TRY_IT.md](DIVA_TRY_IT.md) for roles, spray/refill and group instructions. Stop Play to stop Unity's camera bridge.

## Recording and generated trial data

**Record performance** appears below the preview in F1. It requires calibrated Camera mode and live preview. Click **Stop recording and save**, then check `Recordings/<session>/performance.mp4`. It captures the Game view without audio; export holds images across capture gaps.

Started runs automatically export to `TrialData/<UTC-session-and-GUID>/`: `route.csv`, `events.json`, `summary.json`. Reasons distinguish completed tasks, timeout, manual end and interruption. Route samples use scaled simulation time, nominally 30 Hz, capped at 600 seconds during Play/ultimate; no samples are fabricated to fill gaps. Mode/calibration metadata records start/end only, not complete tracking history. Manual runs must not be presented as gesture-controlled evidence.

Failed exports retain frozen snapshots until domain reload and partial files for recovery. Fix disk/access problems and use **Retry pending trial exports** on the Diva Show director before recompiling/reloading. Successful session IDs prevent duplicate exports.

## Verification record and limitations

- **61 Python tests passed**: 58 repository tests plus three starter recording tests; dependencies and pose/hand model loading passed locally.
- Native Unity gesture, START guard, stationary-turn and trial-export validators passed. Stationary-turn checks exercise actual locomotion with both yaw directions, zero speed, unchanged xyz, upright/stale stopping and solo action gates. Export checks cover immutable snapshots, identifiers, decimal formatting, JSON escaping, retry/idempotence and component teardown. Independent review found no remaining material issues.
- Historical local live checks observed camera landmarks, solo GO/STOP and left/right steering with elephant translation/rotation. A silent 35.2-second, 1280×636 MP4 decoded all 352 frames at 10 fps; 156 captured images were held across gaps. A decoded frame visibly contained webcam landmarks and elephant.
- A historical interrupted trial produced 3,217 route samples through 174.60672 simulation seconds and 13 events. Its duration/end-mode metadata was incorrect due to teardown ordering. Those original local files remain unchanged and their affected metadata is invalid. A caching fix passed native regression; a new post-fix physical finish/shutdown export remains unverified.
- Thumbs-up refill and prayer-hand steering have automated verification, but no fresh physical test after those changes. Full three-person control, complete course interaction and cross-machine restoration remain pending. Walking is reported weak/intermittent; GO rules are unchanged. Archived low-cadence replay exposes a limitation without establishing the live cause.

[TrackingOverrides/VALIDATION.md](TrackingOverrides/VALIDATION.md) details archived reference-fit tradeoffs. Pretrained MediaPipe plus rules is used; no custom neural action classifier was trained. Calibration alters a steering baseline, not model weights.

Recordings, raw datasets, trial outputs, local logs/backups, environments, caches and licensed shared assets are excluded from Git. The historical evidence above describes local checks and is not included as reproducible test media. Keep new performance evidence separately and document its provenance.

### Latest gesture update verification

P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 requires prayer-like hands at chest height plus calibrated torso lean, with both wrists and hips visible; upright, separate hands or tracking loss stops turning. Wrist geometry approximates prayer hands, not confirmed palm contact.

The active project's 61 Python tests passed (58 override tests + three starter recording tests), and native Unity gesture validation passed. The live camera preview reopened; physical accuracy of the new thumbs-up/prayer gestures remains unverified. Newly merged upstream keyboard controls are preserved; the combined publication checkout has not received fresh native Unity or live keyboard-plus-gesture validation. Existing walking sensitivity limitations remain unresolved.
