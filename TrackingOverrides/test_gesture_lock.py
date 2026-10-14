"""🔒 Gesture lock (2026-10-12): single-player and three-player controls are final. See GESTURES_LOCKED.md.

If this test fails, someone changed a locked gesture parameter or rule. Do NOT just update the numbers here:
follow the double-check procedure in GESTURES_LOCKED.md first (team agreement + Eva's approval, a branch and PR,
all Python tests and the four Unity validators, a live single- and three-player test). Only then update LOCKED
below together with GESTURES_LOCKED.md, in the same reviewed change.
"""
import json
import unittest
from pathlib import Path

from diva_gestures import DEFAULTS

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

LOCKED = {
    # Walking (P1 / solo): pump the arms. 2026-10-13 approved change (Eva): one arm .10 + the other .05 (was both .14),
    # 1 s window, 0.5 s pause, slower motion counts, swings down to the thighs, 0.5 s of blurred wrists kept.
    # Later on 2026-10-13 (Eva): stop on time. Motion vs the wrist 0.2 s ago (jitter no longer counts), pause 0.25 s.
    'min_quality': .30, 'go_window': 1.0, 'go_range': .10, 'go_range_other': .05, 'go_stop_delay': .25, 'go_step': .04,
    'go_motion_window': .2,
    'go_hand_min_height': -1.8, 'go_dropout': .5,
    # Turning (P2 / solo): tilt the shoulders or the head, no hips
    'steer_tilt_start': .14, 'steer_tilt_release': .08, 'steer_tilt_full': .40, 'steer_direction': -1.,
    'steer_dropout': .3,
    # Aim / spray (P3 / solo)
    'aim_height': .65, 'aim_still_window': .5, 'hand_match_distance': .55, 'finger_angle': 145., 'finger_extension': 1.2,
    'shoot_min_height': -.55, 'shoot_max_height': .65, 'shoot_forward_z': .20, 'shoot_elbow_angle': 140.,
    'shoot_aim_full_offset': .35, 'shoot_aim_deadzone': .06,
    # Refill (P3 / solo): thumbs-up held 0.4 s (Gesture Recognizer Thumb_Up or the geometric rule)
    'thumb_hold': .4, 'thumb_max_gap': .6, 'thumb_angle_min': 150., 'thumb_up_cos': .70, 'thumb_extension': .5,
    'curled_angle_max': 135., 'thumb_model_score': .5,
    # Ultimate (anyone): both hands above the head for 0.8 s
    'ult_height': .85, 'ult_hold': .8, 'ult_walk_hold': .4,
}

# Key lines of the locked role / mode rules (file, exact text).
LOCKED_LINES = [
    ('TrackingOverrides/bridge.py', 'tracker.assign([(1 - o[0][0], o[0][1]) for o in observations])'),   # mirrored left-to-right roles
    ('TrackingOverrides/bridge.py', 'uncertain = {s for c in ties for s in range(self.count) if c[2][s] != best[2][s]}'),  # pause only ambiguous players
    ('TrackingOverrides/bridge.py', 'def __init__(self, count, max_distance=.20):'),
    # 2026-10-13 approved change (Eva): a missing player's role passes to the person still in view after 1 s, and a
    # Gesture Recognizer failure rebuilds it instead of ending the bridge.
    ('TrackingOverrides/bridge.py', 'self.relock_seconds = 1.0'),
    # 2026-10-13 approved change (Eva): after calibration roles stay in each player's spot; role colours in the preview.
    ('TrackingOverrides/bridge.py', 'self.home = list(self.positions)   # calibrated: everyone\'s spot for the rest of the game'),
    ('TrackingOverrides/bridge.py', 'colors = [(70, 70, 255), (255, 150, 40), (90, 220, 90), (60, 220, 255)]'),
    ('TrackingOverrides/bridge.py', 'gesture_recognizer = RecoveringRecognizer(lambda: vision.GestureRecognizer.create_from_options(gesture_options))'),
    # 2026-10-13 approved change (Eva, v1.5): in a group game roles are ranked by the distance to each player's spot.
    ('TrackingOverrides/bridge.py', 'anchored = self.home is not None and self.count >= 2'),
    ('TrackingOverrides/bridge.py', 'distances = [math.dist(self.home[s] if anchored else self.positions[s], centers[i]) for s, i in pairs]'),
    # 2026-10-13 approved change (Eva, v1.5): one player runs hand / gesture recognition on every other frame.
    ('TrackingOverrides/bridge.py', 'SOLO_HANDS_EVERY = 2'),
    ('TrackingOverrides/bridge.py', 'if p3_slot in bodies and hands_due(args.people, hand_frame, profile, last_hand_rows is not None):'),
    ('Assets/DigiPhant/Runtime/DivaControlState.cs', 'var p1 = count == 1 ? (soloRole == 1 || all ? Read(1, now) : null) : Read(1, now);'),
    ('Assets/DigiPhant/Runtime/DivaControlState.cs', 'var p2 = count == 1 ? (soloRole == 2 || all ? Read(1, now) : null) : Read(2, now);'),
    ('Assets/DigiPhant/Runtime/DivaControlState.cs', 'var p3 = count == 1 ? (soloRole == 3 || all ? Read(1, now) : null) : Read(3, now);'),
    ('Assets/DigiPhant/Runtime/DivaControlState.cs', 'if (all && (Shoot || Drink || Ult)) Forward = 0;'),
    ('Assets/DigiPhant/Runtime/DivaControlState.cs', 'if (all && (Drink || Ult)) Turn = 0;'),
    ('Assets/DigiPhant/Runtime/DivaDemo.cs', 'State.Timeout = Mathf.Max(trackingTimeout, .8f);'),
]

HINT = ' -> LOCKED gesture control. Read GESTURES_LOCKED.md and do the double check before changing it.'


class GestureLockTests(unittest.TestCase):
    def test_defaults_match_the_lock(self):
        for key, value in LOCKED.items():
            self.assertIn(key, DEFAULTS, key + HINT)
            self.assertAlmostEqual(DEFAULTS[key], value, msg=f'DEFAULTS[{key}] changed' + HINT)
        self.assertEqual(set(DEFAULTS) - set(LOCKED), set(), 'new gesture parameter added' + HINT)

    def test_json_config_matches_the_lock(self):
        config = json.loads((HERE / 'diva_gestures.json').read_text(encoding='utf-8'))
        for key, value in config.items():
            self.assertIn(key, LOCKED, f'unexpected key {key} in diva_gestures.json' + HINT)
            self.assertAlmostEqual(value, LOCKED[key], msg=f'diva_gestures.json {key} changed' + HINT)

    def test_role_and_mode_rules_are_unchanged(self):
        for path, line in LOCKED_LINES:
            text = (ROOT / path).read_text(encoding='utf-8')
            self.assertIn(line, text, f'{path}: locked rule changed: {line}' + HINT)


if __name__ == '__main__':
    unittest.main()
