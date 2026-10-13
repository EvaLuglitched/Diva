"""Walking (GO) relaxations: a full arm swing down to the hips still walks, a brief wrist dropout (<0.3 s) keeps the
walking state and swing history (0.5 s since 2026-10-13), and every frame records why GO is on or off (the bridge logs it)."""
import math
import unittest
from diva_gestures import DivaGestureDetector, DEFAULTS
from test_diva_gestures import body


def swing(i, fps=30, low=-1.1, high=.3, period=1.):
    mid, amp = (high + low) / 2, (high - low) / 2
    return body(height=mid + amp * math.sin(2 * math.pi * i / fps / period))


class RelaxedGoTests(unittest.TestCase):
    def test_full_swing_down_to_the_hips_keeps_walking(self):
        d = DivaGestureDetector(); gos = []
        for i in range(90): gos.append(d.update(1, swing(i), i / 30)['go'])
        self.assertGreater(sum(gos[30:]) / 60, .95)            # walking almost every frame after warm-up
        old = DivaGestureDetector(); old.config['go_hand_min_height'] = -.85
        old_gos = [old.update(1, swing(i), i / 30)['go'] for i in range(90)]
        self.assertLess(sum(old_gos[30:]) / 60, .8)            # the old limit dropped GO at the bottom of each swing

    def test_brief_wrist_dropout_keeps_walking_and_history(self):
        d = DivaGestureDetector()
        for i in range(45): g = d.update(1, swing(i), i / 30)
        self.assertEqual(g['go'], 1.)
        for i in range(45, 51):                                  # 0.2 s of unclear wrists
            b = swing(i); b[15]['visibility'] = b[16]['visibility'] = .01
            g = d.update(1, b, i / 30)
            self.assertEqual(g['go'], 1., i)
            self.assertEqual(d.go_debug[1]['reason'], 'wrists-unclear-hold')
        g = d.update(1, swing(51), 51 / 30)                     # back: history was kept, still walking
        self.assertEqual(g['go'], 1.)

    def test_long_wrist_dropout_stops_and_clears(self):
        d = DivaGestureDetector()
        for i in range(45): d.update(1, swing(i), i / 30)
        for i in range(45, 66):                                  # 0.7 s of unclear wrists (hold limit 0.5 s)
            b = swing(i); b[15]['visibility'] = b[16]['visibility'] = .01
            g = d.update(1, b, i / 30)
        self.assertEqual(g['go'], 0.)
        self.assertEqual(d.go_debug[1]['reason'], 'wrists-unclear')
        self.assertNotIn(1, d.history)

    def test_every_frame_has_a_reason(self):
        d = DivaGestureDetector()
        g = d.update(1, body(height=0), 0); self.assertEqual(d.go_debug[1]['reason'], 'warming-up')
        for i in range(1, 20): g = d.update(1, body(height=0), i / 30)
        self.assertEqual(d.go_debug[1]['reason'], 'swing-too-small')
        self.assertIn('left_range', d.go_debug[1])
        for i in range(20, 60): d.update(1, swing(i), i / 30)
        self.assertEqual(d.go_debug[1]['reason'], 'go')
        d.update(1, None, 2.1); self.assertEqual(d.go_debug[1]['reason'], 'body-lost')
        for i in range(70, 75): d.update(1, swing(i), i / 30)
        d.update(1, swing(95), 95 / 30)                          # 0.7 s gap between frames (limit 0.6 s)
        self.assertEqual(d.go_debug[1]['reason'], 'frame-gap-reset')

    def test_defaults(self):
        self.assertEqual(DEFAULTS['go_hand_min_height'], -1.8)   # 2026-10-13: down to the thighs (was -1.25)
        self.assertEqual(DEFAULTS['go_dropout'], .5)             # 2026-10-13: 0.5 s (was 0.3 s)


if __name__ == '__main__':
    unittest.main()
