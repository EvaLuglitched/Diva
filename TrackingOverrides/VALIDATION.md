# Diva landmark gesture prototype

Uses pretrained MediaPipe pose and hand landmark models plus configurable rules.
It does not train a custom neural action classifier.

The current combined Python suite passes 49 tests after the October 11 body-lean steering update. Earlier counts and replay figures below describe historical stages of the prototype.

## Body-lean steering update — October 11, 2026

Steering now uses torso lean relative to a valid neutral baseline, independently of GO and wrist pose. Start threshold is 0.12, release threshold 0.07, and full steering 0.35. Visible shoulders and hips are required; upright or lost torso tracking stops turning immediately. Missing/invalid wrists allow torso steering but suppress GO, aim, spray, refill and ultimate. Prior hand-position steering gates were removed.

Calibration records a fresh torso baseline only with valid hips, otherwise preserving an existing valid baseline or using zero when none exists. The UI's Ready state alone does not guarantee a fresh hip baseline: keep shoulders and hips visible while calibrating upright.

Native `DIVA_STATIONARY_TURN_VALIDATION_OK` passed with a Unity fixture using the real control state and locomotion: yaw changed in both directions with zero speed and unchanged position; upright/stale inputs stopped turning, and solo refill/ultimate gates remained intact. Both installed Python copies match and independent review found no remaining material issues. A new live physical test after this change remains pending. Backup: `UpdateBackups/BodyLean-20261011-224352/`.

Archived P2 replay, old → new correct-direction samples in approximate active intervals: strong RIGHT 27/27 → 27/27; weak LEFT 9/10 → 0/10; weaker RIGHT 10/10 → 6/10; directional photos 4/4 → 2/4. Neutral remained zero and no opposite-direction output appeared. The removed hand-driven poses and torso leans below 0.12 now produce zero. Clips lack paired neutral recordings, so replay used a zero baseline. These are reference-fit checks, not held-out accuracy. P1 GO rules and their low-cadence limitation are unchanged.

## Historical tuning and replay

Rule changes after the first group test (2026-10-08), each covered by a new test:
- Earlier steering hysteresis (superseded by torso steering above): a turn starts at a hand offset of 0.12 but continues down to 0.07
  without the lean check, and survives tracking blips of up to 0.25 s (`steer_release_offset`,
  `steer_hold`). Returning the hands to the middle still stops the turn at once.
- Aiming while spraying: raising one arm (aim) and spraying exclude each other, so while
  shooting the aim comes from both hands pushed toward one side (`shoot_aim_full_offset`,
  `shoot_aim_deadzone`; negative = left, as for steering).
- Remembered mouth: the mouth position relative to the shoulders is stored whenever the face
  is visible and reused for `mouth_memory` (3 s) when the hands hide it. Without a recent face
  sighting drinking is still blocked.
These are synthetic-landmark checks; the replay numbers below predate them and live
three-player testing is still needed.
Tests cover temporal pumping and freeze, 30 fps pumping, lowered arms, upright
neutral, corrected LEFT/RIGHT directions, opposing hand/body cues, open versus
curled fingers, continuous held shooting and release, missing tracking, other
players' hand exclusion, single-arm aiming, image aspect ratio, neutral capture,
and preserving neutral calibration across temporary tracking loss. Missing face
visibility blocks live drinking. Invalid hand coordinates and missing elbows
block shooting. Shoulder-estimated mouth fallback is only available through an
explicit archived-replay argument and is not used by the live bridge.

Replay checks use the same supplied examples used to tune thresholds, with
approximately 0.4 seconds after and 0.15 seconds before each P1 boundary excluded.
These are reference-fit checks, not unseen-person or three-player accuracy.

- P1: 330/337 interior GO samples positive; 57/59 interior STOP samples zero.
  Seven missed GO frames remain and two gray-shirt STOP frames falsely activate.
  Hands held motionless pass the separate synthetic freeze test.
- P2: all four static direction photos produce the correct direction; the new
  neutral photo produces zero turn. LEFT video produces LEFT on 10/46 total
  samples; RIGHT videos produce RIGHT on 12/46 and 31/35 samples respectively.
  No opposite direction outputs occurred. Setup/release, poor quality and weak
  leaning cause zero output, so some video intervals still need live tuning.
- P3: all three shoot repetitions produce shoot events, and all three drinking
  holds produce drink events. Shoot replay has 14 positive frames; drinking has
  38 positive frames. No drink events on the shoot clip and no shoot events on
  the drink clip. Fresh pose face landmarks were extracted for the drinking
  clip so its replay tests the live mouth proximity gate.

There is no multi-person hand recording in the examples. Hand association uses
the nearest wrist across all detected people and rejects ambiguous matches;
live three-person testing remains necessary. Lower resolution and occlusion
may suppress finger actions. Clear visible hands work best. Lost or ambiguous
tracking clears current inputs. Explicit Reset clears neutral baselines;
temporary body loss preserves them.

When camera reads stop, the bridge immediately sends an empty input packet.
After three seconds without a frame it releases and reopens the same selected
backend, at most three times. Twenty stable seconds restore that retry budget.
A real reopen resets player assignment and neutral capture; actions stay zero
until Save neutral is used again. Detector timestamps remain monotonic.
This recovery is tested with deterministic fake capture devices; the actual
hardware/backend issue still needs a live retry. OpenCV device opening itself
may block inside its native backend beyond the bounded read-gap timers.

Camera coordinates are unmirrored. Reference LEFT reaches camera-right; RIGHT
reaches camera-left. P1/P2/P3 begin left-to-right in that preview. Crossings may
require reassignment. Unity sends `divaCalibrate:true` after an upright neutral
capture. It only changes the steering baseline, not the learned landmark model.

Files to install alongside Tracking/bridge.py:
diva_gestures.py, diva_gestures.json, and the official hand_landmarker.task.
Launch bridge with `--diva`; `--people 1` supports solo role testing in Unity.
PoseFrame version stays 1 with all six legacy feature values intact. Each
assigned person has gestures {go, steer, aim, shoot, drink, confidence} and the
packet has divaGestures=true. Only P3's associated hands are used, except solo
mode where slot 1 supplies hands.
