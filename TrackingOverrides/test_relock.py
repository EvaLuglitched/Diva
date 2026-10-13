"""Approved change to the locked bridge (2026-10-13, Eva): whoever stays in view keeps playing, and a Gesture
Recognizer failure with several people in view no longer ends the bridge. See GESTURES_LOCKED.md."""
import unittest

from bridge import PerformerTracker, RecoveringRecognizer


class RelockTests(unittest.TestCase):
    def test_one_player_hands_over_to_the_person_who_stays(self):
        t = PerformerTracker(1)
        self.assertEqual(t.assign([(.5, .5)], now=0), {1: 0})
        self.assertTrue(t.locked)
        # A second person walks in: ignored while the first player is in view.
        self.assertEqual(t.assign([(.5, .5), (.85, .5)], now=.5), {1: 0})
        self.assertEqual(t.assign([(.85, .5), (.5, .5)], now=3), {1: 1})
        # The first player leaves; the second stays. Not yet (less than relock_seconds) ...
        self.assertEqual(t.assign([(.85, .5)], now=3.1), {})
        self.assertEqual(t.assign([(.86, .5)], now=3.9), {})
        # ... then P1 locks onto the person still in view.
        self.assertEqual(t.assign([(.86, .5)], now=4.15), {1: 0})
        self.assertEqual(t.relocked, [1])
        # The first player comes back: the new P1 keeps playing, the returning one is ignored.
        self.assertEqual(t.assign([(.5, .5), (.86, .5)], now=5), {1: 1})
        self.assertEqual(t.relocked, [])

    def test_short_dropout_never_swaps_players(self):
        t = PerformerTracker(1)
        t.assign([(.5, .5)], now=0)
        self.assertEqual(t.assign([(.85, .5)], now=1), {})       # P1 not detected for a moment, bystander visible
        self.assertEqual(t.assign([(.85, .5)], now=1.6), {})
        self.assertEqual(t.assign([(.51, .5), (.85, .5)], now=1.7), {1: 0})   # P1 back within 1 s: still P1
        self.assertEqual(t.assign([(.85, .5)], now=2.2), {})     # the missing clock restarted
        self.assertEqual(t.assign([(.85, .5)], now=2.6), {})

    def test_three_players_replace_only_the_one_who_left(self):
        t = PerformerTracker(3)
        self.assertEqual(t.assign([(.2, .5), (.5, .5), (.8, .5)], now=0), {1: 0, 2: 1, 3: 2})
        # A bystander on the far left is ignored while all three play.
        self.assertEqual(t.assign([(.2, .5), (.5, .5), (.8, .5), (.03, .5)], now=1), {1: 0, 2: 1, 3: 2})
        # P2 leaves; a newcomer stands right of where P2 was, too far for normal tracking to pick up.
        self.assertEqual(t.assign([(.2, .5), (.8, .5), (.03, .5), (.72, .5)], now=2), {1: 0, 3: 1})
        result = t.assign([(.2, .5), (.8, .5), (.03, .5), (.72, .5)], now=3.05)
        self.assertEqual(result, {1: 0, 3: 1, 2: 3})   # the person nearest P2's place, not the far bystander
        self.assertEqual(t.relocked, [2])
        self.assertEqual(t.assign([(.2, .5), (.8, .5), (.03, .5), (.71, .5)], now=3.5), {1: 0, 2: 3, 3: 1})

    def test_ambiguous_players_are_not_handed_to_bystanders(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.4, .5), (.8, .5)], now=0)
        # P1 and P2 merge into one body for a long time (ambiguous): they pause, nobody is re-locked onto that body.
        for k in range(5):
            result = t.assign([(.3, .5), (.8, .5)], now=1 + k)
            self.assertEqual(result, {3: 1})


class FakeRecognizer:
    made = 0

    def __init__(self, fail):
        FakeRecognizer.made += 1
        self.fail, self.closed = fail, False

    def recognize_for_video(self, image, timestamp):
        if self.fail:
            raise RuntimeError("CalculatorGraph::Run() failed:\nPacket isn't the sole owner of the holder.")
        return 'hands'

    def close(self):
        self.closed = True


class RecoveringRecognizerTests(unittest.TestCase):
    def test_failure_rebuilds_instead_of_ending_the_bridge(self):
        FakeRecognizer.made = 0
        first = [True]

        def create():
            fail, first[0] = first[0], False
            return FakeRecognizer(fail)

        r = RecoveringRecognizer(create)
        broken = r.recognizer
        self.assertIsNone(r.recognize_for_video('image', 1))   # failed frame: skipped, no exception
        self.assertTrue(broken.closed)
        self.assertEqual(r.failures, 1)
        self.assertEqual(FakeRecognizer.made, 2)
        self.assertEqual(r.recognize_for_video('image', 2), 'hands')
        r.close()
        self.assertTrue(r.recognizer.closed)


if __name__ == '__main__':
    unittest.main()
