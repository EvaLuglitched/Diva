"""Approved change to the locked walking rule (later on 2026-10-13, Eva): P1 stops on time. Motion is measured
against the wrist 0.2 s ago instead of the previous frame, so landmark jitter no longer counts as moving; the pause
before stopping is 0.25 s. With v1.3's per-frame check, real-camera jitter kept the elephant walking for about 1 s
after the arms stopped."""
import math
import random
import unittest

from diva_gestures import DivaGestureDetector
from test_diva_gestures import body

V13 = dict(go_stop_delay=.5, go_step=.03, go_motion_window=0.)


def stop_time(fps, jitter, seed, cfg=None, freq=1.3):
    """Seconds from the arms stopping to walking off (None = still walking 3 s later)."""
    random.seed(seed)
    d = DivaGestureDetector()
    if cfg:
        d.config.update(cfg)
    for i in range(int(6 * fps)):
        t = i / fps
        b = body(height=-.6 + .3 * math.sin(2 * math.pi * freq * min(t, 3)))
        for k in (15, 16):
            b[k]['y'] += random.gauss(0, jitter)
        go = d.update(1, b, t)['go']
        if t >= 3 and go == 0:
            return t - 3
    return None


def still_walking_frames(cfg, jitter, fps, seed, seconds=10):
    random.seed(seed)
    d = DivaGestureDetector()
    d.config.update(cfg)
    walked = 0
    for i in range(seconds * fps):
        b = body(height=-.6)
        for k in (15, 16):
            b[k]['y'] += random.gauss(0, jitter)
        walked += d.update(1, b, i / fps)['go'] > 0
    return walked


class StopOnTimeTests(unittest.TestCase):
    def test_stops_within_half_a_second_despite_jitter(self):
        for fps in (30, 15):
            for seed in range(1, 6):
                t = stop_time(fps, .002, seed)
                self.assertIsNotNone(t, (fps, seed))
                self.assertLess(t, .5, (fps, seed))

    def test_v13_kept_walking_about_a_second(self):
        times = [stop_time(30, .002, seed, V13) for seed in range(1, 6)]
        self.assertTrue(all(t is None or t > .9 for t in times), times)

    def test_jitter_alone_never_walks(self):
        # .003 is about 1.5x the jitter in Eva's live log (standing still: ~.002 per frame).
        for fps in (30, 15):
            for seed in range(1, 6):
                self.assertEqual(still_walking_frames({}, .003, fps, seed), 0, (fps, seed))

    def test_twice_the_jitter_walks_far_less_than_v13(self):
        new = sum(still_walking_frames({}, .004, fps, s) for fps in (30, 15) for s in range(1, 6))
        old = sum(still_walking_frames(V13, .004, fps, s) for fps in (30, 15) for s in range(1, 6))
        self.assertLess(new * 5, old)

    def test_starts_without_delay(self):
        for fps, limit in ((30, .06), (15, .1)):
            d = DivaGestureDetector()
            for i in range(fps):
                d.update(1, body(height=-.6), i / fps)
            first = None
            for i in range(3 * fps):
                t = 1 + i / fps
                if d.update(1, body(height=-.6 + .3 * math.sin(2 * math.pi * 1.3 * (t - 1))), t)['go'] and first is None:
                    first = t - 1
            self.assertLessEqual(first, limit, fps)


if __name__ == '__main__':
    unittest.main()
