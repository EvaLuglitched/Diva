"""Approved change to the locked walking rule (2026-10-13 night, Eva): no walking with the hands hanging.

Compared with Vicky's original P1 GO/STOP clips (GitHub release mediapipe-data-2026-10-13): there, walking is a pump
with the wrists above the shoulders (highest wrist +0.16 torso lengths, swing ranges about 1.2 / 0.9 shoulder widths)
and a player holding still moves .11 / .09. v1.4's .10 / .05 sat at that still level and had no height check, so in
three-player games P1 standing with the arms down (low-visibility wrists drifting slowly) walked the elephant. Now a
hand must come up to go_reach (navel height) at least once in go_window, and the swing sizes are .14 / .08."""
import math
import unittest

from test_diva_gestures import body
from test_easier_walk import arms, walking_share

V14 = dict(go_range=.10, go_range_other=.05, go_reach=-9.)


def share(make, fps=30, old=False, seconds=6):
    from diva_gestures import DivaGestureDetector
    d = DivaGestureDetector()
    if old:
        d.config.update(V14)
    gos = [d.update(1, make(i / fps), i / fps)['go'] for i in range(seconds * fps)]
    return sum(gos[fps:]) / len(gos[fps:])   # after the first second


def low_visibility(b, q=.45):
    for k in (15, 16):
        b[k]['visibility'] = b[k]['presence'] = q
    return b


class HandsUpTests(unittest.TestCase):
    def test_hanging_hands_drifting_never_walk(self):
        # Three players, P1 with the arms down: unclear wrists wander slowly around the hips (go_debug 17:08, 34-39 s).
        make = lambda t: low_visibility(arms(-1. + .15 * math.sin(2 * math.pi * .8 * t),
                                             -1.05 + .12 * math.sin(2 * math.pi * .6 * t + 1)))
        for fps in (30, 15, 10):
            self.assertEqual(share(make, fps), 0, fps)
            self.assertGreater(share(make, fps, old=True), .3, fps)

    def test_holding_still_with_small_shifts_does_not_walk(self):
        # Vicky's clip 2 at ~21 s: arms held up, small shifts of ~.1 shoulder widths started walking in v1.4.
        def make(t):
            u = (t % 1.2) / .15
            k = min(1., u) if int(t / 1.2) % 2 == 0 else 1 - min(1., u)
            return body(height=.1 + .07 * k)
        self.assertEqual(share(make), 0)
        self.assertGreater(share(make, old=True), .05)

    def test_original_pumping_walks(self):
        # Vicky's GO clips: lowest wrist about -0.7, highest about +0.16, both arms.
        make = lambda t: arms(-.27 + .45 * math.sin(2 * math.pi * 1.2 * t), -.27 + .45 * math.sin(2 * math.pi * 1.2 * t + math.pi))
        for fps in (30, 15, 10):
            self.assertGreater(share(make, fps), .97, fps)

    def test_swing_from_the_thighs_up_past_the_navel_walks(self):
        make = lambda t: body(height=-.95 + .55 * math.sin(2 * math.pi * 1.1 * t))   # -1.5 .. -0.4
        self.assertGreater(share(make), .95)
        self.assertGreater(share(make, 15), .95)

    def test_swing_that_stays_below_the_navel_does_not_walk(self):
        # The one behaviour taken back from v1.3: a swing that never brings a hand above the navel.
        make = lambda t: body(height=-1. + .25 * math.sin(2 * math.pi * 1.2 * t))   # -1.25 .. -0.75
        self.assertEqual(share(make), 0)
        self.assertGreater(share(make, old=True), .9)

    def test_one_arm_up_to_the_navel_is_enough(self):
        # Uneven beginners: one arm comes up, the other hangs and moves a little.
        make = lambda t: arms(-.8 + .4 * math.sin(2 * math.pi * 1.2 * t), -1. + .06 * math.sin(2 * math.pi * 1.2 * t + math.pi))
        self.assertGreater(walking_share(make), .95)

    def test_debug_reason_names_hands_not_up(self):
        from diva_gestures import DivaGestureDetector
        d = DivaGestureDetector()
        for i in range(45):
            d.update(1, body(height=-1. + .25 * math.sin(2 * math.pi * 1.2 * i / 30)), i / 30)
        self.assertEqual(d.go_debug[1]['reason'], 'hands-not-up')


if __name__ == '__main__':
    unittest.main()
