"""Regression: real MediaPipe hand landmarks have visibility/presence = None; the thumbs-up check must not crash."""
import unittest
from types import SimpleNamespace
import diva_gestures as dg


def hand(points, none=True):
    v = None if none else 1.
    return [SimpleNamespace(x=x, y=y, z=0., visibility=v, presence=v) for x, y in points]


# A thumbs-up shape: thumb (1-4) pointing up the image, fingers (5-20) folded near the palm.
THUMBS = [(.50, .60), (.47, .56), (.46, .50), (.46, .45), (.46, .40),
          (.53, .52), (.56, .53), (.55, .56), (.53, .56),
          (.54, .55), (.57, .56), (.56, .59), (.54, .59),
          (.54, .58), (.57, .59), (.56, .62), (.54, .62),
          (.53, .61), (.56, .62), (.55, .64), (.53, .64)]


class HandNoneQualityTests(unittest.TestCase):
    def test_val_uses_default_for_none(self):
        p = SimpleNamespace(x=.1, visibility=None)
        self.assertEqual(dg.val(p, 'visibility', 1.), 1.)
        self.assertEqual(dg.val({'presence': None}, 'presence', 1.), 1.)
        self.assertEqual(dg.quality(p), 1.)

    def test_thumbs_up_with_mediapipe_hand_landmarks_does_not_crash(self):
        cfg = dict(dg.DEFAULTS)
        with_none = dg.thumbs_up(hand(THUMBS, none=True), cfg)
        with_values = dg.thumbs_up(hand(THUMBS, none=False), cfg)
        self.assertEqual(with_none, with_values)   # None behaves like full quality

    def test_open_fingers_with_none_quality(self):
        self.assertIsInstance(dg.open_fingers(hand(THUMBS)), int)


if __name__ == '__main__':
    unittest.main()
