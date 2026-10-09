# Diva landmark gesture prototype

Uses pretrained MediaPipe pose and hand landmark models plus configurable rules.
It does not train a custom neural action classifier.

32 automated tests pass: 20 gesture tests, 8 existing bridge/preview tests,
and 4 bounded camera recovery tests.
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
