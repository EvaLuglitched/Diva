"""Steering by tilting the shoulders or the head (no hips): either one alone turns, standing upright stops,
calibration absorbs a naturally tilted posture."""
import unittest
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body


def head_only(tilt):
    p = body()                                   # level shoulders
    p[7].update(y=.2 + tilt * .035); p[8].update(y=.2 - tilt * .035)
    return p


def shoulders_only(tilt):
    p = body(lean=tilt)
    p[7].update(y=.2); p[8].update(y=.2)         # level head
    return p


class TiltSteerTests(unittest.TestCase):
    def test_head_tilt_alone_turns_both_ways(self):
        d = DivaGestureDetector()
        self.assertLess(d.update(2, head_only(.3), 0)['steer'], 0)
        self.assertGreater(d.update(2, head_only(-.3), .1)['steer'], 0)
        self.assertEqual(d.update(2, head_only(0), .2)['steer'], 0)

    def test_shoulder_tilt_alone_turns_without_hips(self):
        d = DivaGestureDetector(); p = shoulders_only(.3)
        for i in (23, 24): p[i]['visibility'] = p[i]['presence'] = .01
        self.assertLess(d.update(2, p, 0)['steer'], 0)

    def test_small_tilt_does_not_turn(self):
        d = DivaGestureDetector()
        for t, tilt in enumerate((.05, -.05, .1, -.1)):
            self.assertEqual(d.update(2, head_only(tilt), t * .1)['steer'], 0)

    def test_calibration_absorbs_natural_tilt(self):
        d = DivaGestureDetector(); d.update(2, head_only(.2), 0); d.calibrate()
        self.assertEqual(d.update(2, head_only(.2), .1)['steer'], 0)
        self.assertLess(d.update(2, head_only(.55), .2)['steer'], 0)

    def test_hidden_head_still_turns_with_shoulders(self):
        d = DivaGestureDetector(); p = shoulders_only(.3)
        for i in (7, 8, 2, 5): p[i]['visibility'] = .01
        self.assertLess(d.update(2, p, 0)['steer'], 0)


if __name__ == '__main__':
    unittest.main()
