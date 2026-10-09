# Trying Diva in Unity

Open Assets/DigiPhant/Scenes/Diva.unity in JIVE_Digiphant and press Play.
The scene has the Savannah track, elephant, third-person camera and water targets.
The original DigiPhant scene is preserved.

## One person
Select Try alone, then P1 Move, P2 Turn or P3 Water. Keep Camera selected.
Click Get ready (10 seconds), then stand naturally with arms down until ready.
Changing roles resets calibration, so use Get ready again after changing roles.

- P1: repeatedly pump raised elbows/arms up and down to go. Stop the movement to stop.
- P2: move both hands and lean your body left to turn left, or right to turn right.
  Stand upright for straight. Keep your hips visible.
- P3: raise one arm to aim that way. Push both hands forward and open them to shoot.
  A quick opening gives one burst; holding them extended and open keeps spraying.
  Curl your hands together near your visible mouth to drink/refill; release to stop.

Aim returns to center when neither arm is raised, so the spray gesture may center
it. Refill currently works anywhere. Water and target hits appear in the panel.

## Three people
Select 3 players. Start side by side, left-to-right in the UNMIRRORED camera preview:
P1 movement, P2 turning, P3 water. Keep other people out of the camera view.
Click Get ready and hold a natural resting pose. Avoid crossing positions.
Use Reassign players if assignments are lost or people exchange places, then calibrate.

## Other testing options
Test controls gives manual sliders and spray/refill switches.
Return elephant to start resets its position. Fill tank for testing fills the water.
Stopping Play shuts down the camera bridge owned by Unity. Retry camera restarts it.

## How detection works
This is pretrained MediaPipe pose and hand detection plus configurable landmark
rules tuned from your recordings; it is not a newly trained action network.
Rules: DigiPhantStarter/Tracking/diva_gestures.py and diva_gestures.json.
The bridge sends local messages to Unity on port 5055, preview on 5057 and receives
commands on 5056. Roles track positions; this does not identify faces or names.

Dataset:
E:\UCB Gradschool\Classes\TDF\Projects\diva data
Includes labeled raw recordings/photos, landmarks and segment annotations.
The new neutral photo is shared as P1 STOP, P2 STRAIGHT and P3 IDLE.

Live performance needs testing with the intended camera, lighting and three people.
Keep shoulders, wrists, hands and P2 hips in frame; avoid covering the mouth for refill.
