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
    # Walking (P1 / solo): pump both arms
    'min_quality': .30, 'go_window': .80, 'go_range': .14, 'go_stop_delay': .35, 'go_step': .045,
    'go_hand_min_height': -1.25, 'go_dropout': .3,
    # Turning (P2 / solo): tilt the shoulders or the head, no hips
    'steer_tilt_start': .14, 'steer_tilt_release': .08, 'steer_tilt_full': .40, 'steer_direction': -1.,
    'steer_dropout': .3,
    # Aim / spray (P3 / solo)
    'aim_height': .65, 'hand_match_distance': .55, 'finger_angle': 145., 'finger_extension': 1.2,
    'shoot_min_height': -.55, 'shoot_max_height': .65, 'shoot_forward_z': .20, 'shoot_elbow_angle': 140.,
    'shoot_aim_full_offset': .35, 'shoot_aim_deadzone': .06,
    # Refill (P3 / solo): thumbs-up held 0.4 s (Gesture Recognizer Thumb_Up or the geometric rule)
    'thumb_hold': .4, 'thumb_max_gap': .6, 'thumb_angle_min': 150., 'thumb_up_cos': .70, 'thumb_extension': .5,
    'curled_angle_max': 135., 'thumb_model_score': .5,
    # Ultimate (anyone): both hands above the head for 0.8 s
    'ult_height': .85, 'ult_hold': .8,
}

# Key lines of the locked role / mode rules (file, exact text).
LOCKED_LINES = [
    ('TrackingOverrides/bridge.py', 'tracker.assign([(1 - o[0][0], o[0][1]) for o in observations])'),   # mirrored left-to-right roles
    ('TrackingOverrides/bridge.py', 'uncertain = {s for c in ties for s in range(self.count) if c[2][s] != best[2][s]}'),  # pause only ambiguous players
    ('TrackingOverrides/bridge.py', 'def __init__(self, count, max_distance=.20):'),
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
