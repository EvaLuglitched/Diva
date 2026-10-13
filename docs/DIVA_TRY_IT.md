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
Keyboard: arrows switch skins, Enter starts or skips, E fires the ultimate, F1 shows the setup panel (the EN/中文 switch is off for now).
Keyboard play works together with the gestures, even without a camera: W (or ↑) go, A/D (or ←/→) turn, U/I aim, E fires a charged ultimate,
hold Space to spray, F to drink, H hides the key legend. See Assets/DivaGame/README.md.

## Race extras (same rules on PC and Vision Pro)
These run on top of Eva's scoring and tasks (`Assets/DivaShow/Runtime/DivaShowMeta.cs`); the scene is unchanged.
- **Candy +15**: candy floats along the route between the stops; drive through it. Eating several in a row raises the
  "ding" pitch. **Small rockets** (two per straight) give +15 and a **ROCKET DASH**: 2.5 s at full speed, boosters on.
  The dash also fires after every MEGA BUBBLE ("CHARGE!").
- **ON FIRE**: hits (+.22), candy (+.08) and tasks (+.3) fill the orange meter at the bottom right; full = 12 s of
  score x1.5 with an orange screen edge. It cools down when nothing happens.
- **GOLDEN TARGET**: every ~24 s one target ahead turns gold for 10 s (3x points, gold disc behind it, "x3" on the
  lock marker). The cannon locks it first.
- **Aim assist**: while you spray, the locked target stays locked (no flicking between two targets); the lock marker
  always shows what the next burst hits.
- **Water**: a burst now costs .022 (was .008), so the tank empties in ~45 bursts. Under 25% the water bar flashes red,
  a beep plays every 6 s and the coach says to refill (thumbs-up 0.4 s / F).
- **Coach**: a pink beacon stands on the next stop with a waypoint label (name and metres; it sticks to the screen
  edge when the stop is off screen). Tips pop up at the bottom when you have not started running, when a target is
  ahead, when you reach the bush / basin / log, and when water is low. Three-player mode names the player (P1/P2/P3).
- **Lap medals**: GOLD LAP at or under 50 s, SILVER LAP under 80 s, else BRONZE LAP (Eva's run timer). Time-outs get
  no medal.
- **ICE WALL**: 14 m before the finish arch a wall of ice blocks the road (like Mei's wall). The elephant stops at it;
  only the **MEGA BUBBLE** smashes it (+50): the impact freezes for a moment, then slow motion while the shards fly and
  the camera pushes in from behind; the charge afterwards carries you through. No charge? Push against the wall (10%/s),
  shoot targets or eat candy; the coach tells you which. Columns are placed only where no prop stands.
- **PLAY OF THE RACE**: the finish shows a hero card instead of the old banner: time, tasks, skin, the score counting
  up, then the medal stamp. The highlight replay and results follow as before; the results line shows the medal too.
All numbers are Inspector fields on the Diva Show object ("Race meta").

## Picture and frame rate on a MacBook Pro (M1 / M2 Max)
- **Smooth first**: `DivaShowPerf` (added at runtime by Diva Show) turns vSync on so the game runs at the display's rate
  (120 fps on a ProMotion screen) and, only when a frame misses that budget, lowers the URP render scale in 0.1 steps
  (never below 0.6), raising it back when there is headroom; if even 0.6 cannot hold the display's rate it locks to a steady
  half rate (60 on a 120 Hz screen) instead of juddering between the two. Smoothness first, then detail. It logs `DIVA_SHOW perf:` lines and **F2** shows a small fps / frame-time / scale
  readout at the top right; untick *Adaptive* on the component to keep native resolution.
- **3A look** (`DivaLook` on the Diva Show object): a light far-field depth of field during play (Gaussian, from 35 m: the elephant, the
  targets and the road stay sharp, far houses soften) and f/4 cinematic bokeh focus in cutscenes; camera motion blur during play, stronger
  while boosting; 4096 shadow map over 60 m; SSAO at half resolution; the town reflection probe now really renders
  (the quality setting that disabled realtime probes is on). Fields: *Gameplay Depth Of Field*, *Gameplay Blur Start / End / Radius*,
  *Cutscene Aperture*, *Motion Blur*.
- The ice-wall refraction (a full-screen opaque copy) is only requested while the wall is within 70 m in front of the
  camera; `PC_RPAsset` no longer forces the opaque texture every frame.
- Vision Pro: no depth of field, no motion blur (they hurt in a headset); the cockpit tracks the elephant's heading
  closely (0.15 s, 50°/s) so the view never keeps turning after you level your head.

## One person, one role (testing)
Press F1 for the setup panel. Select Try alone, then P1 Move, P2 Turn or P3 Water. Keep Camera selected.
Click Get ready (10 seconds), then stand naturally with arms down until ready.
Changing roles resets calibration, so use Get ready again after changing roles.

- P1: repeatedly pump raised elbows/arms up and down to go. Stop the movement to stop.
- P2: tilt your shoulders or your head left/right (your own left/right), stationary or moving; arms can stay relaxed and hips may be hidden.
  Upright or lost torso tracking stops turning. Keep shoulders and hips visible; wrists may be hidden.
- P3: raise one arm to aim that way. Push both hands forward and open them to shoot;
  while shooting, push both hands toward one side to aim there.
  A quick opening gives one burst; holding them extended and open keeps spraying.
  Each burst fires at the best target in that direction (water jet + score).
  Show either hand thumbs-up for 0.4 seconds to refill; release to stop.
  Keep that hand, its wrist and shoulders visible; the other wrist may be hidden.

Refill currently works anywhere. Water and target hits appear in the panel; the
score and tasks appear in the HUD (eat/drink: thumbs-up near the bush or basin;
go around the log: just walk past it along the path). A floating arrow points at each unfinished task when
the elephant comes near it.

## Three people
Select 3 players. Start side by side, left-to-right as you see yourselves in the MIRRORED camera preview (like a mirror):
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

P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumbs-up is recognised by MediaPipe's pretrained Gesture Recognizer (`Thumb_Up`, `gesture_recognizer.task`, downloaded on first use) or, as a fallback, the geometric rule. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 tilts the shoulders or the head toward one side (calibrated; hips are not used and may be hidden); wrists may be hidden. Level stops turning, including while stationary; shoulders briefly unclear keep the turn for 0.3 s.

The publication Python suite passed 62 override tests; three starter recording tests also passed (65 total). Optional MediaPipe hand confidence set to `None` no longer crashes thumbs-up detection; explicit low or invalid confidence remains rejected. Fresh native Unity and physical checks of this restoration and the combined keyboard/finish-line checkout remain pending. Upstream keyboard and finish-line behavior is preserved.


## Walking diagnostics
Every Camera-mode session writes `DigiPhantStarter/Tracking/go_debug/go_<time>.csv`: one row per frame with why P1's
GO was on or off (hands too low, swing too small, paused, wrists unclear, frame gap...). After a run, summarise it with
`DigiPhantStarter/Tracking/.venv/bin/python TrackingOverrides/go_debug_report.py`. Walking now allows swings down to
the hips (`go_hand_min_height` -1.25) and keeps the swing through wrist dropouts under 0.3 s (`go_dropout`).

## Vision Pro version (branch `carl/diva-vr`)

A first-person VR version of the same match: you sit in a cockpit on the elephant's back and drive it round the
candy-town course. The race extras above (candy, rockets, ON FIRE, golden target, coach, medals, hero card, aim assist,
water) are the same rules; only their presentation is the cockpit's (lights, spatial sound, floating text). It is on branch `carl/diva-vr` only (it adds `com.unity.xr.visionos`, which needs a Unity Pro
licence — see `Assets/DivaVR/README.md`). The PC game on `main` is unchanged.

### Starting

1. The app opens fully immersive (no passthrough) in a dreamy sky with a menu: look at **START** and pinch.
   **EN / 中文** next to it switches the language.
2. The opening flies you over the town in three still shots (overview with the task beacons → behind the elephant →
   low hero shot) and lands you in the cockpit.
3. A guide board in front of the cockpit takes you through four steps; every step has buttons you press by
   **looking at them and pinching**:
   - **1/4 HOW TO PLAY** — just watch: a row of five stick figures (Eva's camera-check style) take turns showing lean,
     head tilt, look + pinch, thumbs-up and hands up, with one big caption each. 14 seconds, **SKIP** at the bottom right.
     Plays once per launch.
   - **2/4 PICK YOUR PAINT** — pinch anywhere = next paint; **<** / **>** / **CHOOSE THIS PAINT**.
   - **3/4 LEARN TO DRIVE** — do each move once (lean forward, tilt your head, look + pinch, thumbs-up);
     the board shows a live meter per move and stamps NICE! — or **SKIP**. First round per launch only.
   - **4/4 READY** — raise **both hands to eye level** and hold until the bar fills (in the race this is the
     MEGA BUBBLE gesture), or look at **START** and pinch.

### Controls in the race

| Control | How | Notes |
| --- | --- | --- |
| **Run** | **Lean forward** (same idea as walking on the PC: your body is the throttle) | 7 cm forward = walk, 22 cm = run, sit back = stop; measured from where you sat at the countdown (RECENTER SEAT on the pause board if you moved). Deliberately low sensitivity with a slow throttle so small head movements do not surge; a gentle vignette while accelerating |
| **Steer** | **Tilt your head** (same as the PC game's shoulder / head tilt) | starts at 5°, full at 15°, measured from how you sat down; the elephant turns at most 40°/s in VR (60 on PC) so the picture never whips round; no reverse |
| **Trunk cannon** | **Look at a target and pinch** (hold the pinch to keep spraying) | Aim = where your eyes / head point, aim assist 12° / 35 m; each burst uses water (below 25 % it beeps and the coach tells you to refill). Pinching also presses buttons on the boards; buttons need a 0.35 s look first, the big ones a 0.5 s hold |
| **Eat / drink / refill** | Hold a **thumbs-up** for 0.4 s (same as the PC game); hand to the mouth still works | The elephant stops and curls its trunk; at the feeding bush and the water basin this completes the task; refill works anywhere |
| **Step over the log** | Keep driving over the log | same as PC |
| **MEGA BUBBLE (ult)** | When the charge gauge reads 100 % ("MEGA BUBBLE READY" banner + chime): **raise both hands to eye level in front of you** and hold 0.4 s | Bubble burst at the targets ahead, then a 2.5 s dash |
| **Pickups** | Drive over them | Candies every 3 m (+15, combo pitch rises), rockets at ⅓ and ⅔ of the long straights = 2.5 s dash |
| **ON FIRE** | Hits, candies and tasks fill a streak; when it is full you burn for 12 s | Score ×1.5, the cockpit strips turn orange, the music lifts |
| **Golden target** | Every ~24 s one target turns gold for 10 s (gold disc behind it, banner) | 3× points |
| **Finish** | Drive through the finish arch | Counts even with tasks left (they just score less): hero-moment card, lap medal (gold ≤ 70 s, silver ≤ 100 s, bronze), results board, then **PLAY AGAIN** (both hands up, or look + pinch) |

Your hands are drawn as glowing joint dots (cyan when gripping the stick, yellow when pinching, blue at the mouth).
Nothing ever rotates the world around you; turning adds a soft vignette; letting go of the stick stops immediately.
