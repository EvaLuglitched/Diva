import unittest
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body


class BodyLeanTests(unittest.TestCase):
    def test_calibration_with_unreliable_hips_cannot_create_false_turn(self):
        d = DivaGestureDetector()
        lost = body()
        for i in (23, 24):
            lost[i].update(x=.1, visibility=.1, presence=.1)
        d.update(1, lost, 0)
        d.calibrate()
        self.assertEqual(d.update(1, body(), .1)['steer'], 0)

    def test_recalibration_without_hips_preserves_previous_torso_neutral(self):
        d = DivaGestureDetector()
        d.update(1, body(lean=.2), 0)
        d.calibrate()
        lost = body(lean=.2)
        for i in (23, 24):
            lost[i].update(x=.1, visibility=.1, presence=.1)
        d.update(1, lost, .1)
        d.calibrate()
        self.assertEqual(d.update(1, body(lean=.2), .2)['steer'], 0)

    def test_relaxed_arms_turn_both_directions_without_walking(self):
        d = DivaGestureDetector()
        d.update(1, body(height=-1.2, separate=1.8), 0)
        d.calibrate()
        for i, lean in enumerate((.3, -.3), 1):
            result = d.update(1, body(lean=lean, height=-1.2, separate=1.8), i * .1)
            self.assertLess(result['steer'] * lean, 0)
            self.assertEqual(result['go'], 0)

    def test_upright_stops_and_translation_or_small_sway_does_not_start(self):
        d = DivaGestureDetector()
        self.assertLess(d.update(1, body(lean=.3), 0)['steer'], 0)
        self.assertEqual(d.update(1, body(handshift=.8), .1)['steer'], 0)
        for i, pose in enumerate((body(offset=.25), body(lean=.04), body(lean=-.04)), 2):
            self.assertEqual(d.update(1, pose, i * .1)['steer'], 0)

    def test_neutral_offset_and_sideways_translation_preserve_center(self):
        d = DivaGestureDetector()
        d.update(1, body(lean=.15), 0)
        d.calibrate()
        self.assertEqual(d.update(1, body(lean=.15, offset=.2), .1)['steer'], 0)
        self.assertLess(d.update(1, body(lean=.45), .2)['steer'], 0)

    def test_missing_wrists_allow_torso_only_turn_and_clear_hand_actions(self):
        for invalid in ('quality', 'nan'):
            with self.subTest(invalid=invalid):
                d = DivaGestureDetector()
                pose = body(lean=.3)
                for i in (15, 16):
                    if invalid == 'quality': pose[i]['visibility'] = .05
                    else: pose[i]['x'] = float('nan')
                result = d.update(1, pose, 0)
                self.assertLess(result['steer'], 0)
                self.assertGreaterEqual(result['confidence'], .3)
                for name in ('go', 'aim', 'shoot', 'drink', 'ult'):
                    self.assertFalse(result[name], name)

    def test_hidden_hips_keep_turning_and_hidden_shoulders_hold_briefly(self):
        # Eva: turning reads only shoulders + head. Hidden hips never stop it; hidden shoulders keep the current
        # turn for steer_dropout (0.3 s), then stop; a lost body or standing upright stops it.
        for i in (23, 24):
            d = DivaGestureDetector()
            self.assertLess(d.update(1, body(lean=.3), 0)['steer'], 0)
            pose = body(lean=.3); pose[i]['visibility'] = .05
            self.assertLess(d.update(1, pose, .1)['steer'], 0)
        for i in (11, 12):
            d = DivaGestureDetector()
            turning = d.update(1, body(lean=.3), 0)['steer']
            pose = body(lean=.3); pose[i]['visibility'] = .05
            self.assertEqual(d.update(1, pose, .2)['steer'], turning)      # held
            self.assertEqual(d.update(1, pose, .45)['steer'], 0)           # longer than 0.3 s: stops
        d = DivaGestureDetector()
        self.assertLess(d.update(1, body(lean=.3), 0)['steer'], 0)
        self.assertEqual(d.update(1, None, .2)['steer'], 0)
        self.assertEqual(d.update(1, body(), .3)['steer'], 0)


if __name__ == '__main__':
    unittest.main()
