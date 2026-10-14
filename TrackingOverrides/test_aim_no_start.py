"""v1.6 (approved by Eva, 2026-10-13): one arm up to aim never starts walking while the other arm is still.

Within go_window (1 s) of a stop, the still arm's old swing was still in the swing history, so lifting the other arm to
aim made the elephant walk another ~0.45 s (one player does everything, so stop-and-aim is common). Walking that is
already on is unchanged, and a high one-arm swing still starts walking when the other arm moves.
"""
import math
import unittest

from diva_gestures import DivaGestureDetector
from test_diva_gestures import body


def frame(t, walk=False, aim=False, other=None):
    b = body(height=(-.4 + .7 * math.sin(2 * math.pi * t)) if walk else -.8)
    if aim:
        b[15].update(y=.3 - .9 * .3)                    # the performer's left wrist well above the shoulder
    if other is not None:
        b[16].update(y=.3 - other * .3)                 # the other wrist at a chosen height (torso units, roughly)
    return b


class AimNeverStartsWalkingTests(unittest.TestCase):
    def run_go(self, fps, script, seconds):
        d = DivaGestureDetector(); gos = []
        for f in range(int(seconds * fps)):
            t = f / fps
            g = d.update(1, script(t), t)
            gos.append((t, g['go'], g['aim']))
        return gos

    def test_aim_right_after_stopping_does_not_walk_again(self):
        for fps in (17, 30):
            gos = self.run_go(fps, lambda t: frame(t, walk=t < 3, aim=3.5 <= t < 4.5), 5)
            self.assertTrue(any(g for t, g, a in gos if 2 < t < 3), fps)               # walked
            self.assertFalse(any(g for t, g, a in gos if 3.5 <= t < 4.5), fps)        # no walking while aiming
            self.assertTrue(all(a for t, g, a in gos if 3.6 <= t < 4.5), fps)         # but it aims

    def test_aim_while_standing_still_does_not_walk(self):
        gos = self.run_go(30, lambda t: frame(t, aim=t >= 1.5), 3)
        self.assertFalse(any(g for t, g, a in gos))

    def test_high_swings_while_walking_keep_walking(self):
        # Walking, then one arm swings high (through aim height every stroke) while the other swings as before.
        def script(t):
            if t < 2: return frame(t, walk=True)
            b = frame(t, walk=True)
            b[15].update(y=.3 - (.5 + .7 * math.sin(2 * math.pi * t)) * .3)
            return b
        gos = self.run_go(30, script, 4)
        rows = [g for t, g, a in gos if 2.2 <= t < 4]
        self.assertGreater(sum(rows) / len(rows), .9)

    def test_high_one_arm_pump_with_the_other_moving_starts_walking(self):
        # Start from standing: one arm pumps high (through aim height), the other moves a little.
        def script(t):
            if t < 1: return frame(t)
            b = frame(t, other=-.8 + .15 * math.sin(2 * math.pi * t))
            b[15].update(y=.3 - (.2 + .7 * math.sin(2 * math.pi * t)) * .3)
            return b
        gos = self.run_go(30, script, 3.5)
        self.assertGreater(sum(g for t, g, a in gos if 2 <= t < 3.5) / sum(1 for t, g, a in gos if 2 <= t < 3.5), .6)


if __name__ == '__main__':
    unittest.main()
