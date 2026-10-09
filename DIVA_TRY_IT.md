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
