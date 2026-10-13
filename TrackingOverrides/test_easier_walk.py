"""Approved change to the locked walking rule (2026-10-13, Eva): easier for beginners. Each case also runs the old
(v1.2) settings to show what used to fail, and the guards that keep standing still, body sway and aiming from walking."""
import math
import unittest

from diva_gestures import DivaGestureDetector
from test_diva_gestures import body

OLD = dict(go_window=.8, go_range=.14, go_range_other=.14, go_stop_delay=.35, go_step=.045, go_hand_min_height=-1.25,
           go_dropout=.3, ult_walk_hold=0.)


def arms(left, right):
    """Both wrists at their own heights (torso lengths above the shoulders; negative = below)."""
    b = body()
    b[15]['y'] = .3 - left * .3
    b[16]['y'] = .3 - right * .3
    return b


def walking_share(make, frames=150, fps=30, old=False, start=30):
    d = DivaGestureDetector()
    if old:
        d.config.update(OLD)
    gos = [d.update(1, make(i / fps), i / fps)['go'] for i in range(frames)]
    return sum(gos[start:]) / len(gos[start:])


class EasierWalkTests(unittest.TestCase):
    def test_one_big_arm_and_one_small_arm_walk(self):
        # Beginners often swing one arm well and the other only a little.
        make = lambda t: arms(-.5 + .25 * math.sin(2 * math.pi * 1.2 * t), -.6 + .04 * math.sin(2 * math.pi * 1.2 * t + math.pi))
        self.assertGreater(walking_share(make), .95)
        self.assertLess(walking_share(make, old=True), .05)

    def test_small_even_swing_walks(self):
        make = lambda t: body(height=-.5 + .05 * math.sin(2 * math.pi * 1.3 * t))   # both arms, small (0.15 shoulder widths)
        self.assertGreater(walking_share(make), .95)

    def test_slow_swing_with_pauses_walks(self):
        # Later on 2026-10-13 the pause before stopping became 0.25 s (stop on time). Measured against the wrist
        # 0.2 s ago, a turnaround pause of up to about 0.35 s still keeps walking.
        def make(t):   # up in 0.55 s, hold 0.35 s at the top, down in 0.55 s, hold 0.35 s at the bottom
            u = t % 1.8
            k = u / .55 if u < .55 else 1 if u < .9 else 1 - (u - .9) / .55 if u < 1.45 else 0
            return body(height=-.7 + .8 * k)
        self.assertGreater(walking_share(make, frames=300), .99)

    def test_swing_down_to_the_thighs_walks(self):
        make = lambda t: body(height=-.75 + .85 * math.sin(2 * math.pi * t))   # lowest -1.6 torso lengths
        self.assertGreater(walking_share(make), .95)
        self.assertLess(walking_share(make, old=True), .8)

    def test_pumping_through_the_top_keeps_walking(self):
        make = lambda t: body(height=.5 + .6 * math.sin(2 * math.pi * 1.2 * t), separate=.8)   # above the head ~0.3 s
        self.assertGreater(walking_share(make), .95)
        self.assertLess(walking_share(make, old=True), .85)

    def test_blurred_fast_swing_keeps_walking(self):
        def make(t):
            b = body(height=-.3 + .6 * math.sin(2 * math.pi * 1.5 * t))
            if t % 1.0 > .6:   # 0.4 s of blurred wrists every second
                b[15]['visibility'] = b[16]['visibility'] = .01
            return b
        self.assertGreater(walking_share(make), .95)
        self.assertLess(walking_share(make, old=True), .85)

    def test_hands_held_above_the_head_stop_walking_and_fire_the_ultimate(self):
        d = DivaGestureDetector()
        for i in range(30):
            d.update(1, body(height=-.3 + .6 * math.sin(i / 30 * 2 * math.pi * 1.5)), i / 30)
        for i in range(30, 60):   # both hands up and held
            g = d.update(1, body(height=1.2, separate=.8), i / 30)
            t = (i - 30) / 30
            if t >= .45:
                self.assertEqual(g['go'], 0., t)
        self.assertTrue(g['ult'])

    # ---- guards that must not change
    def test_raising_one_arm_to_aim_never_walks(self):
        make = lambda t: arms(min(1.2, -.8 + 2 * t / .3), -.8)   # one arm up in 0.3 s and held; the other still
        self.assertEqual(walking_share(make, start=0), 0.)

    def test_standing_still_and_body_sway_never_walk(self):
        self.assertEqual(walking_share(lambda t: body(height=-.8), start=0), 0.)
        self.assertEqual(walking_share(lambda t: body(offset=.05 * math.sin(3 * t), height=-1.), start=0), 0.)

    def test_stops_when_the_arms_stop(self):
        d = DivaGestureDetector()
        for i in range(60):
            g = d.update(1, body(height=-.3 + .6 * math.sin(i / 30 * 2 * math.pi * 1.5)), i / 30)
        self.assertEqual(g['go'], 1.)
        for i in range(60, 90):
            g = d.update(1, body(height=-.8), i / 30)
            if (i - 60) / 30 > .55:
                self.assertEqual(g['go'], 0., i)


if __name__ == '__main__':
    unittest.main()
