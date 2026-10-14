"""Approved changes to the locked rules (2026-10-13 night, Eva): each player's control depends only on the body parts
that control needs, and a few frames of lost tracking never stop the elephant.

- P2 turning never depends on the hips or the wrists: with both hidden (three players in a row, or out of the
  picture) the frame's confidence is the shoulders', so Unity no longer drops the turn. Players are tracked by the
  shoulder midpoint, not the hips (MediaPipe's guessed hips jumped and P2 lost the role).
  Vicky's P2 lean clips cropped at the upper chest: v1.6 delivered 0-17 % of the turn frames, now 100 %.
- A player missing from tracking for under player_dropout (0.6 s) keeps the swing history and the turn, so walking
  does not warm up again after every short dropout (P1 dropped out 7-16 times a minute in three-player logs)."""
import math
import types
import unittest

from bridge import PerformerTracker, shoulder_center
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body


def hidden(b, parts):
    for i in parts:
        b[i]['visibility'] = b[i]['presence'] = 0.
    return b


HIPS, WRISTS = (23, 24), (15, 16)


class P2NeedsOnlyShouldersAndHead(unittest.TestCase):
    def test_turn_reaches_unity_with_hips_and_wrists_hidden(self):
        d = DivaGestureDetector()
        d.update(2, hidden(body(), HIPS + WRISTS), 0.)
        d.calibrate()
        for i in range(1, 20):
            out = d.update(2, hidden(body(lean=.3), HIPS + WRISTS), i / 15)
            self.assertGreaterEqual(out['confidence'], .3, i)   # Unity's MinimumConfidence: the frame is kept
            self.assertNotEqual(out['steer'], 0, i)

    def test_hidden_wrists_never_cost_the_turn_compared_with_visible_ones(self):
        for parts in (HIPS, WRISTS, HIPS + WRISTS):
            d, ref = DivaGestureDetector(), DivaGestureDetector()
            for det, b in ((d, hidden(body(), parts)), (ref, body())):
                det.update(2, b, 0.); det.calibrate()
            for i in range(1, 10):
                a = d.update(2, hidden(body(lean=-.25), parts), i / 15)
                r = ref.update(2, body(lean=-.25), i / 15)
                self.assertAlmostEqual(a['steer'], r['steer'], places=6, msg=parts)
                self.assertGreaterEqual(a['confidence'], .3, parts)

    def test_neutral_is_saved_while_the_wrists_are_hidden(self):
        # A natural shoulder slope saved at the camera check with the hands out of view is P2's straight.
        d = DivaGestureDetector()
        d.update(2, hidden(body(lean=.1), HIPS + WRISTS), 0.)
        d.calibrate()
        self.assertEqual(d.update(2, hidden(body(lean=.1), HIPS + WRISTS), .1)['steer'], 0.)

    def test_p1_wrist_blur_hold_is_not_dropped_for_hidden_hips(self):
        # The wrists-unclear hold keeps walking; with hips hidden its confidence used to fall under Unity's 0.3.
        d = DivaGestureDetector()
        for i in range(45):
            g = d.update(1, hidden(body(height=-.3 + .45 * math.sin(2 * math.pi * 1.2 * i / 30)), HIPS), i / 30)
        self.assertEqual(g['go'], 1.)
        g = d.update(1, hidden(body(height=-.3), HIPS + WRISTS), 1.55)
        self.assertEqual(g['go'], 1.)
        self.assertGreaterEqual(g['confidence'], .3)


def landmarks(shoulder_x, shoulder_y=.3, hip_y=.6):
    p = [types.SimpleNamespace(x=shoulder_x, y=.5, z=0., visibility=1., presence=1.) for _ in range(33)]
    p[11].x, p[12].x, p[11].y, p[12].y = shoulder_x - .05, shoulder_x + .05, shoulder_y, shoulder_y
    p[23].x, p[24].x, p[23].y, p[24].y = shoulder_x - .04, shoulder_x + .04, hip_y, hip_y
    return p


class TrackedByShoulders(unittest.TestCase):
    def test_center_ignores_the_hips(self):
        self.assertEqual(shoulder_center(landmarks(.5, hip_y=.6)), shoulder_center(landmarks(.5, hip_y=1.3)))

    def test_p2_keeps_the_role_when_its_guessed_hips_jump(self):
        # P2's hips are hidden: MediaPipe's guess moves 0.3 down and back every few frames. The shoulders stay put.
        for center, keeps in ((shoulder_center, True), (lambda l: ((l[23].x + l[24].x) / 2, (l[23].y + l[24].y) / 2), False)):
            t = PerformerTracker(3)
            people = lambda k: [landmarks(.2), landmarks(.5, hip_y=.6 + (.3 if k % 4 >= 2 else 0)), landmarks(.8)]
            t.assign([(1 - center(l)[0], center(l)[1]) for l in people(0)], now=0)
            t.lock()
            kept = all(2 in t.assign([(1 - center(l)[0], center(l)[1]) for l in people(k)], now=k / 15) for k in range(1, 30))
            self.assertEqual(kept, keeps, center)


class ShortDropoutsKeepPlaying(unittest.TestCase):
    def pump(self, d, start, seconds, fps=15):
        go = []
        for i in range(int(seconds * fps)):
            t = start + i / fps
            go.append(d.update(1, body(height=-.3 + .45 * math.sin(2 * math.pi * 1.2 * t)), t)['go'])
        return go

    def test_walking_resumes_at_once_after_a_short_dropout(self):
        d = DivaGestureDetector()
        self.assertEqual(self.pump(d, 0., 2.)[-1], 1.)
        for k in range(4):                       # P1 not assigned for 0.27 s
            d.retain(set(), 2. + k / 15)
        self.assertEqual(self.pump(d, 2.27, .5)[0], 1.)

    def test_without_a_time_the_old_immediate_reset_still_applies(self):
        d = DivaGestureDetector()
        self.pump(d, 0., 2.)
        d.retain(set())
        self.assertEqual(self.pump(d, 2.07, .2)[0], 0.)   # warming up again

    def test_a_long_absence_starts_over(self):
        d = DivaGestureDetector()
        self.pump(d, 0., 2.)
        for k in range(12):                      # 0.8 s missing
            d.retain(set(), 2. + k / 15)
        self.assertNotIn(1, d.history)
        self.assertEqual(self.pump(d, 2.8, .1)[0], 0.)

    def test_turn_hysteresis_survives_a_short_dropout(self):
        d = DivaGestureDetector()
        d.update(2, body(), 0.); d.calibrate()
        self.assertNotEqual(d.update(2, body(lean=.2), .1)['steer'], 0)
        d.retain(set(), .15)
        # Back after 0.13 s with a smaller tilt: still above the release threshold, so it keeps turning.
        self.assertNotEqual(d.update(2, body(lean=.11), .23)['steer'], 0)


def thumb(b, wrist=16):
    from test_diva_gestures import hand
    h = hand(b[wrist], opened=False)
    h.update(gesture='Thumb_Up', gesture_score=.9)   # what the Gesture Recognizer said about a pumping fist
    return h


class ThumbsUpOnlyWhenHeld(unittest.TestCase):
    """Solo: pumping fists read as Thumb_Up in 26 % of the frames of Vicky's GO clip; each one stopped walking."""
    def pump(self, t):
        return body(height=-.3 + .45 * math.sin(2 * math.pi * 1.2 * t))

    def test_thumbs_up_seen_while_pumping_never_stops_walking(self):
        d = DivaGestureDetector()
        go = []
        for i in range(90):
            t = i / 30
            b = self.pump(t)
            go.append(d.update(1, b, t, [thumb(b)] if i % 3 == 0 else [])['go'])
            self.assertFalse(d.go_debug[1]['reason'] == 'thumbs-up-refill' and t > 1, t)
        self.assertGreater(sum(go[30:]) / 60, .95)

    def test_a_held_thumbs_up_still_refills_after_walking_stops(self):
        d = DivaGestureDetector()
        for i in range(60):
            d.update(1, self.pump(i / 30), i / 30)
        drink = []
        for i in range(60, 120):                 # stop with the hand at chest height, thumbs-up held
            b = body(height=-.3)
            drink.append(d.update(1, b, i / 30, [thumb(b)])['drink'])
        self.assertTrue(any(drink))
        self.assertTrue(all(drink[-10:]))

    def test_a_moving_thumbs_up_does_not_refill(self):
        d = DivaGestureDetector()
        for i in range(30):
            d.update(1, body(height=-.3), i / 30)
        for i in range(30, 60):                  # hand waving up and down with a thumbs-up, not walking yet
            t = i / 30
            b = body(height=-.6 + .3 * math.sin(2 * math.pi * 1.5 * t))
            self.assertFalse(d.update(1, b, t, [thumb(b)])['drink'], t)


if __name__ == '__main__':
    unittest.main()
