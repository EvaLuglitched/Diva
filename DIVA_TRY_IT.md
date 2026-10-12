# Trying Diva in Unity

Open Assets/DigiPhant/Scenes/Diva.unity in this restored Unity project and press Play.
The scene has the Savannah track inside the candy-town game layer, the D.Va mech elephant,
a third-person camera, 16 targets, the score HUD and the course tasks.
The original DigiPhant scene is preserved (game layer only, no Diva gestures).
On a Mac, run `bash Tools/setup_tracking_mac.sh` once before the first Play.

## The game (single player)
Press Play: the home page opens (Diva Show). Pick a colour and 1 or 3 players, then START. In Camera mode a large camera check follows: every player turns green when recognised; after 5 seconds all green, the neutral pose is saved automatically and the race intro starts (Enter skips to keyboard play, Esc goes back). Test controls go straight to the intro.
Raise one arm to switch skins, then hold both hands above your head to start; Eva's race intro plays
(both hands up, Enter or Esc skip to the countdown) and the run starts at GO. One person has every
control (Try alone > All). Hold both hands above your head again when MEGA BUBBLE is charged.
Keyboard: arrows switch skins, Enter starts or skips, U fires the ultimate, L switches EN/中文, F1 shows the setup panel.
Keyboard play works together with the gestures, even without a camera: W (or ↑) go, A/D (or ←/→) turn, U/I aim (U also fires a charged ultimate),
hold Space to spray, F to drink, H hides the key legend. See Assets/DivaGame/README.md.

## One person, one role (testing)
Press F1 for the setup panel. Select Try alone, then P1 Move, P2 Turn or P3 Water. Keep Camera selected.
Click Get ready (10 seconds), then stand naturally with arms down until ready.
Changing roles resets calibration, so use Get ready again after changing roles.

- P1: repeatedly pump raised elbows/arms up and down to go. Stop the movement to stop.
- P2: lean your torso left/right, stationary or moving; arms can stay relaxed.
  Upright or lost torso tracking stops turning. Keep shoulders and hips visible; wrists may be hidden.
- P3: raise one arm to aim that way. Push both hands forward and open them to shoot;
  while shooting, push both hands toward one side to aim there.
  A quick opening gives one burst; holding them extended and open keeps spraying.
  Each burst fires at the best target in that direction (water jet + score).
  Show either hand thumbs-up for 0.4 seconds to refill; release to stop.
  Keep that hand, its wrist and shoulders visible; the other wrist may be hidden.

Refill currently works anywhere. Water and target hits appear in the panel; the
score and tasks appear in the HUD (eat/drink: thumbs-up near the bush or basin;
step over the log: P1 pumping next to it).

## Three people
Select 3 players. Start side by side, left-to-right in the UNMIRRORED camera preview:
P1 movement, P2 turning, P3 water. Keep other people out of the camera view.
Click Get ready and hold a natural resting pose. Avoid crossing positions.
Use Reassign players if assignments are lost or people exchange places, then calibrate.

### If only one player turns green (3 players)

The camera bridge now detects the whole frame on every frame in group mode (MediaPipe's video tracking tended to keep
only the first body it found), players are assigned as they arrive (left to right; roles re-sort until all three are
in, then lock when the neutral pose is saved), and the status line says what the camera sees, for example
`Waiting for P3 ... (camera sees 2 bodies, 2 usable)`:
- **sees 2 bodies** — the third person is not detected at all: step back so all three are fully in frame, more light,
  no one half-hidden behind another.
- **sees 3 bodies, 2 usable** — one body is too cut off or too far: shoulders and hips must be inside the frame.
- **sees 3, 3 usable but still waiting** — that player's gesture confidence is low (wrists or hips barely visible);
  lower `gestureConfidence` on `DivaDemo` (default 0.3) if it keeps happening.
The bridge console (Unity's camera panel shows its lines) prints the same counts whenever they change.

Group tracking detects the whole frame on every frame in a background thread (the camera loop never waits for it)
and smooths each player's landmarks; solo play keeps MediaPipe's own tracking. The bridge picks a performance profile
by itself (`--profile quality` on Apple Silicon: full pose model, 960 px, hands every frame; `fast` elsewhere: lite
model, 640 px, hands every other frame). The console line `pose NN ms/detection in the background` shows the cost.

### Tuning the gesture rules on your own recordings

The gestures are rules on landmarks (there is no trained action model), and every threshold is in
`TrackingOverrides/diva_gestures.json`. `TrackingOverrides/replay_gestures.py` replays a recording through the same
models and rules and prints when each action fires, so thresholds can be tuned against real footage:

```bash
DigiPhantStarter/Tracking/.venv/bin/python TrackingOverrides/replay_gestures.py Recordings/<session>/performance.mp4 --people 1
DigiPhantStarter/Tracking/.venv/bin/python TrackingOverrides/replay_gestures.py walk.mp4 --labels walk.json --grid "go_window=0.65,0.8,1.0;go_range=0.12,0.14,0.16"
```

`walk.json` marks when the action was really performed, e.g. `{"go": [[2.0, 6.5]], "shoot": [[14, 16]]}`; with labels the
tool prints the hit rate inside each range and the false-fire rate outside it for every threshold combination.
The GO rule now accepts slower arm pumps (window 0.8 s, range 0.14, stop delay 0.35 — the old 0.65 / 0.16 / 0.28 can be
restored in the JSON if your recordings prefer them).

## If something does not respond
- Camera stopped: … — the words after the colon are the tracker's own error
  (missing model, missing file, camera not opening). Fix that, then Retry.
- Waiting for P2, P3 … — those players are not visible; for one person choose Try alone.
- Nothing moves after Ready — check the camera panel shows all players as visible.

## Other testing options
Test controls gives manual sliders and spray/refill switches.
Return elephant to start resets its position. Fill tank for testing fills the water.
Stopping Play shuts down the camera bridge owned by Unity. Retry camera restarts it.

## How detection works
This is pretrained MediaPipe pose and hand detection plus configurable landmark
rules tuned from your recordings; it is not a newly trained action network.
Rules: TrackingOverrides/diva_gestures.py and diva_gestures.json (Unity copies them into
DigiPhantStarter/Tracking on Play when they differ).
The bridge sends local messages to Unity on port 5055, preview on 5057 and receives
commands on 5056. Roles track positions; this does not identify faces or names.

The local dataset is excluded from Git. It includes labeled raw recordings/photos,
landmarks and segment annotations.
The new neutral photo is shared as P1 STOP, P2 STRAIGHT and P3 IDLE.

Live performance needs testing with the intended camera, lighting and three people.
Keep shoulders, wrists, hands and P2 hips in frame; keep the thumbs-up hand clearly visible for refill.

## Calibration, recording and trial files

Camera-mode START opens the camera check, which saves the neutral pose after 5 seconds with every player recognised; Test controls start without it. Keep hips visible while standing upright on the camera check. Without valid hips, calibration retains an earlier valid torso baseline or uses zero; Ready alone does not confirm a fresh baseline. Both wrists must be visible for GO; torso turning does not require them. For a stationary-turn check, use Try alone > All > Camera, START, pass the camera check, keep arms relaxed and lean: expect P1 STOP with P2 LEFT/RIGHT. The following camera can make the scenery rotate around an apparently stationary elephant.

In F1, **Record performance** sits below the preview. It requires calibrated Camera mode and a live preview. Click **Stop recording and save**, then inspect `Recordings/<session>/performance.mp4` (no audio). Started runs save `route.csv`, `events.json` and `summary.json` under `TrialData/<session>/` on finish or interruption. These outputs stay local. See [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) for sampling and recovery limits.

Current validation: 65 Python tests passed (62 repository + three starter recording tests). Earlier native Unity checks are historical; this restoration and the combined keyboard/finish-line checkout have not received fresh native or physical validation. Walking sensitivity and full group performance remain unresolved.

### Latest gesture update verification

P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumbs-up is recognised by MediaPipe's pretrained Gesture Recognizer (`Thumb_Up`, `gesture_recognizer.task`, downloaded on first use) or, as a fallback, the geometric rule. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 uses calibrated torso lean alone, with shoulders and hips visible; wrists may be hidden. Upright or lost torso tracking stops turning, including while stationary.

The publication Python suite passed 62 override tests; three starter recording tests also passed (65 total). Optional MediaPipe hand confidence set to `None` no longer crashes thumbs-up detection; explicit low or invalid confidence remains rejected. Fresh native Unity and physical checks of this restoration and the combined keyboard/finish-line checkout remain pending. Upstream keyboard and finish-line behavior is preserved.


## Walking diagnostics
Every Camera-mode session writes `DigiPhantStarter/Tracking/go_debug/go_<time>.csv`: one row per frame with why P1's
GO was on or off (hands too low, swing too small, paused, wrists unclear, frame gap...). After a run, summarise it with
`DigiPhantStarter/Tracking/.venv/bin/python TrackingOverrides/go_debug_report.py`. Walking now allows swings down to
the hips (`go_hand_min_height` -1.25) and keeps the swing through wrist dropouts under 0.3 s (`go_dropout`).
