"""v1.5 (approved by Eva, 2026-10-13): one player runs hand / gesture recognition on every other frame.

Solo used to run the full pose model and then the Gesture Recognizer (6 hands, full frame) on every camera frame:
64 ms a frame, about 15 fps on an M1 Max, so walking got too few samples. These tests pin the new cadence and check
that the group cadence and the first frame are unchanged.
"""
import unittest

from bridge import SOLO_HANDS_EVERY, hands_due, performance_profile


class SoloHandCadenceTests(unittest.TestCase):
    def test_solo_runs_hands_on_every_other_frame(self):
        for profile in (performance_profile('quality'), performance_profile('fast')):
            due = [hands_due(1, frame, profile, have_last=True) for frame in range(1, 11)]
            self.assertEqual(due.count(True), 5, profile['name'])
            self.assertEqual(due, [frame % SOLO_HANDS_EVERY == 0 for frame in range(1, 11)])

    def test_first_hands_are_never_skipped(self):
        # No hands yet (start, or after a reset): run now instead of waiting for the next even frame.
        self.assertTrue(hands_due(1, 1, performance_profile('quality'), have_last=False))
        self.assertTrue(hands_due(3, 1, performance_profile('fast'), have_last=False))

    def test_group_cadence_is_unchanged(self):
        quality, fast = performance_profile('quality'), performance_profile('fast')
        self.assertTrue(all(hands_due(3, frame, quality, have_last=True) for frame in range(1, 11)))   # every frame
        self.assertEqual([hands_due(3, frame, fast, have_last=True) for frame in range(1, 7)],
                         [False, True, False, True, False, True])                                      # every other frame


if __name__ == '__main__':
    unittest.main()
