# Trying Diva in Unity

Open Assets/DigiPhant/Scenes/Diva.unity in this restored Unity project and press Play.
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
Keyboard play works together with the gestures, even without a camera: W (or ↑) go, A/D (or ←/→) turn, U/I aim (U also fires a charged ultimate),
hold Space to spray, F to drink, H hides the key legend. See Assets/DivaGame/README.md.

## One person, one role (testing)
Press F1 for the setup panel. Select Try alone, then P1 Move, P2 Turn or P3 Water. Keep Camera selected.
Click Get ready (10 seconds), then stand naturally with arms down until ready.
Changing roles resets calibration, so use Get ready again after changing roles.

- P1: repeatedly pump raised elbows/arms up and down to go. Stop the movement to stop.
- P2: hold hands together at chest height like praying and lean your torso left/right, stationary or moving.
  Upright, separated hands or lost tracking stops turning. Keep both wrists, shoulders and hips visible.
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

Camera-mode START waits until calibration completes; Test controls can start without it. Keep hips visible while upright during the ten-second countdown. Without valid hips, calibration retains an earlier valid torso baseline or uses zero; Ready alone does not confirm a fresh baseline. Both wrists must be visible for GO and prayer turning. For a stationary-turn check, use Try alone > All > Camera, calibrate, START, keep hands together at chest height and lean: expect P1 STOP with P2 LEFT/RIGHT. The following camera can make the scenery rotate around an apparently stationary elephant.

In F1, **Record performance** sits below the preview. It requires calibrated Camera mode and a live preview. Click **Stop recording and save**, then inspect `Recordings/<session>/performance.mp4` (no audio). Started runs save `route.csv`, `events.json` and `summary.json` under `TrialData/<session>/` on finish or interruption. These outputs stay local. See [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) for sampling and recovery limits.

Current validation: 61 Python tests (58 repository + three starter recording tests) and native Unity validators passed. Weak/intermittent walking remains unresolved; a fresh physical trial after the steering update and full group performance remain unverified. Historical successful recordings do not establish those later checks.

### Latest gesture update verification

P3 refills with either-hand thumbs-up held 0.4 seconds; release stops. The thumb hand, its associated wrist and shoulders must be visible, but the opposite wrist may be hidden. This also supplies the existing `drink` signal for course eating/drinking. P2 requires prayer-like hands at chest height plus calibrated torso lean, with both wrists and hips visible; upright, separate hands or tracking loss stops turning. Wrist geometry approximates prayer hands, not confirmed palm contact.

The active project's 61 Python tests passed (58 override tests + three starter recording tests), and native Unity gesture validation passed. The live camera preview reopened; physical accuracy of the new thumbs-up/prayer gestures remains unverified. Newly merged upstream keyboard controls are preserved; the combined publication checkout has not received fresh native Unity or live keyboard-plus-gesture validation. Existing walking sensitivity limitations remain unresolved.
