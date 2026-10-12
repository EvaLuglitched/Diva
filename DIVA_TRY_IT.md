# Trying Diva in Unity

Open Assets/DigiPhant/Scenes/Diva.unity in JIVE_Digiphant and press Play.
The scene has the Savannah track inside the candy-town game layer, the D.Va mech elephant,
a third-person camera, 16 targets, the score HUD and the course tasks.
The original DigiPhant scene is preserved (game layer only, no Diva gestures).
On a Mac, run `bash Tools/setup_tracking_mac.sh` once before the first Play.

## The game (single player)
Press Play: the skin select opens (Diva Show). Stand in view; the 10-second calibration starts by itself.
Raise one arm to switch skins, then hold both hands above your head to start; Eva's race intro plays
(both hands up, Enter or Esc skip to the countdown) and the run starts at GO. One person has every
control (Try alone > All). Hold both hands above your head again when MEGA BUBBLE is charged.
Keyboard: arrows switch skins, Enter starts or skips, U fires the ultimate, L switches EN/中文, F1 shows the setup panel.

## One person, one role (testing)
Press F1 for the setup panel. Select Try alone, then P1 Move, P2 Turn or P3 Water. Keep Camera selected.
Click Get ready (10 seconds), then stand naturally with arms down until ready.
Changing roles resets calibration, so use Get ready again after changing roles.

- P1: repeatedly pump raised elbows/arms up and down to go. Stop the movement to stop.
- P2: move both hands and lean your body left to turn left, or right to turn right.
  Stand upright for straight. Keep your hips visible. Once turning, a smaller lean keeps it going.
- P3: raise one arm to aim that way. Push both hands forward and open them to shoot;
  while shooting, push both hands toward one side to aim there.
  A quick opening gives one burst; holding them extended and open keeps spraying.
  Each burst fires at the best target in that direction (water jet + score).
  Curl your hands together near your mouth to drink/refill; release to stop.
  Hands may cover the mouth for a few seconds once your face has been seen.

Refill currently works anywhere. Water and target hits appear in the panel; the
score and tasks appear in the HUD (eat/drink: hands to mouth near the bush or basin;
step over the log: P1 pumping next to it).

## Three people
Select 3 players. Start side by side, left-to-right in the UNMIRRORED camera preview:
P1 movement, P2 turning, P3 water. Keep other people out of the camera view.
Click Get ready and hold a natural resting pose. Avoid crossing positions.
Use Reassign players if assignments are lost or people exchange places, then calibrate.

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

Dataset:
E:\UCB Gradschool\Classes\TDF\Projects\diva data
Includes labeled raw recordings/photos, landmarks and segment annotations.
The new neutral photo is shared as P1 STOP, P2 STRAIGHT and P3 IDLE.

Live performance needs testing with the intended camera, lighting and three people.
Keep shoulders, wrists, hands and P2 hips in frame; avoid covering the mouth for refill.

## Vision Pro version (branch `carl/diva-vr`)

A first-person VR version of the same match: you sit in a cockpit on the elephant's back and drive it round the
candy-town course. It is on branch `carl/diva-vr` only (it adds `com.unity.xr.visionos`, which needs a Unity Pro
licence — see `Assets/DivaVR/README.md`). The PC game on `main` is unchanged.

### Starting

1. The app opens fully immersive (no passthrough) in a dreamy sky with a menu: look at **START** and pinch.
   **EN / 中文** next to it switches the language.
2. The opening flies you over the town in three still shots (overview with the task beacons → behind the elephant →
   low hero shot) and lands you in the cockpit.
3. A guide board in front of the cockpit takes you through four steps; every step has buttons you press by
   **looking at them and pinching**:
   - **1/4 HOW TO PLAY** — just watch: six short animated clips (3D figure + one big caption) show every control
     below. 17 seconds, **SKIP** at the bottom right. Plays once per launch.
   - **2/4 PICK YOUR PAINT** — pinch anywhere = next paint; **<** / **>** / **CHOOSE THIS PAINT**.
   - **3/4 LEARN TO DRIVE** — do each move once (push the lever, push it sideways, look + pinch, hand to mouth);
     the board shows a live meter per move and stamps NICE! — or **SKIP**. First round per launch only.
   - **4/4 READY** — raise **both hands to eye level** and hold until the bar fills (in the race this is the
     MEGA BUBBLE gesture), or look at **START** and pinch.

### Controls in the race

| Control | How | Notes |
| --- | --- | --- |
| **Run** | Grab the joystick on the console (right of your knee, 20 cm right / 40 cm below the eyes / 34 cm forward) by **pinching or making a fist** on the glowing knob, then **push it forward** | 3 cm = walk, 12 cm = run; the knob glows when a hand is near; let go = stop. At the countdown the stick moves to your hand if it is within 25 cm |
| **Steer** | Push the joystick **left / right** | 3 cm dead zone, 10 cm = full turn; no reverse |
| **Trunk cannon** | **Look at a target and pinch** with the free hand | Aim assist 12° / 35 m; each burst uses water (the water gauge is on the console; below 25 % it beeps and the coach tells you to refill) |
| **Eat / drink / refill** | **Free hand to the mouth** (within 19 cm, below the eyes, in front) | The elephant stops and curls its trunk; at the feeding bush and the water basin this completes the task; refill works anywhere |
| **Step over the log** | Keep driving over the log | same as PC |
| **MEGA BUBBLE (ult)** | When the charge gauge reads 100 % ("MEGA BUBBLE READY" banner + chime): **raise both hands to eye level in front of you** and hold 0.4 s | Bubble burst at the targets ahead, then a 2.5 s dash |
| **Pickups** | Drive over them | Candies every 3 m (+15, combo pitch rises), rockets at ⅓ and ⅔ of the long straights = 2.5 s dash |
| **ON FIRE** | Hits, candies and tasks fill a streak; when it is full you burn for 12 s | Score ×1.5, the cockpit strips turn orange, the music lifts |
| **Golden target** | Every ~24 s one target turns gold for 10 s (gold disc behind it, banner) | 3× points |
| **Finish** | Drive through the finish arch | Counts even with tasks left (they just score less): hero-moment card, lap medal (gold ≤ 70 s, silver ≤ 100 s, bronze), results board, then **PLAY AGAIN** (both hands up, or look + pinch) |

Your hands are drawn as glowing joint dots (cyan when gripping the stick, yellow when pinching, blue at the mouth).
Nothing ever rotates the world around you; turning adds a soft vignette; letting go of the stick stops immediately.
