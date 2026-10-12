import unittest
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body


def prayer(**kwargs):
    kwargs.setdefault("height",-.2); kwargs.setdefault("separate",.2)
    return body(**kwargs)


class BodyLeanTests(unittest.TestCase):
    def test_calibration_with_unreliable_hips_cannot_create_false_turn(self):
        d = DivaGestureDetector()
        lost = prayer()
        for i in (23, 24):
            lost[i].update(x=.1, visibility=.1, presence=.1)
        d.update(1, lost, 0)
        d.calibrate()
        self.assertEqual(d.update(1, prayer(), .1)['steer'], 0)

    def test_recalibration_without_hips_preserves_previous_torso_neutral(self):
        d = DivaGestureDetector()
        d.update(1, prayer(lean=.2), 0)
        d.calibrate()
        lost = prayer(lean=.2)
        for i in (23, 24):
            lost[i].update(x=.1, visibility=.1, presence=.1)
        d.update(1, lost, .1)
        d.calibrate()
        self.assertEqual(d.update(1, prayer(lean=.2), .2)['steer'], 0)

    def test_relaxed_arms_cannot_turn_without_prayer_gate(self):
        d = DivaGestureDetector()
        d.update(1, prayer(height=-1.2, separate=1.8), 0)
        d.calibrate()
        for i, lean in enumerate((.3, -.3), 1):
            result = d.update(1, prayer(lean=lean, height=-1.2, separate=1.8), i * .1)
            self.assertEqual(result['steer'], 0)
            self.assertEqual(result['go'], 0)

    def test_upright_stops_and_translation_or_small_sway_does_not_start(self):
        d = DivaGestureDetector()
        self.assertLess(d.update(1, prayer(lean=.3), 0)['steer'], 0)
        self.assertEqual(d.update(1, prayer(handshift=.8), .1)['steer'], 0)
        for i, pose in enumerate((prayer(offset=.25), prayer(lean=.04), prayer(lean=-.04)), 2):
            self.assertEqual(d.update(1, pose, i * .1)['steer'], 0)

    def test_neutral_offset_and_sideways_translation_preserve_center(self):
        d = DivaGestureDetector()
        d.update(1, prayer(lean=.15), 0)
        d.calibrate()
        self.assertEqual(d.update(1, prayer(lean=.15, offset=.2), .1)['steer'], 0)
        self.assertLess(d.update(1, prayer(lean=.45), .2)['steer'], 0)

    def test_missing_wrists_stop_prayer_turn_and_clear_hand_actions(self):
        for invalid in ('quality', 'nan'):
            with self.subTest(invalid=invalid):
                d = DivaGestureDetector()
                pose = prayer(lean=.3)
                for i in (15, 16):
                    if invalid == 'quality': pose[i]['visibility'] = .05
                    else: pose[i]['x'] = float('nan')
                result = d.update(1, pose, 0)
                self.assertEqual(result['steer'], 0)
                self.assertGreaterEqual(result['confidence'], .3)
                for name in ('go', 'aim', 'shoot', 'drink', 'ult'):
                    self.assertFalse(result[name], name)

    def test_missing_torso_stops_immediately_and_cannot_hold_stale_turn(self):
        d = DivaGestureDetector()
        self.assertLess(d.update(1, prayer(lean=.3), 0)['steer'], 0)
        for i in (11, 12, 23, 24):
            pose = prayer(lean=.3)
            pose[i]['visibility'] = .05
            self.assertEqual(d.update(1, pose, .1)['steer'], 0)
        self.assertEqual(d.update(1, None, .2)['steer'], 0)
        self.assertEqual(d.update(1, prayer(), .3)['steer'], 0)


if __name__ == '__main__':
    unittest.main()
