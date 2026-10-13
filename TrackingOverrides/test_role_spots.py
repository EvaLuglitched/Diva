"""Approved change to the locked bridge (2026-10-13, Eva): once the camera check saves the neutral pose (the game
starts), roles are tied to each player's spot (P1 left, P2 middle, P3 right). A player who leaves and comes back to
their own spot gets their role back after 1 s; nobody can take another player's role, wherever they stand."""
import unittest

from bridge import PerformerTracker


def calibrated_three(now=0):
    t = PerformerTracker(3)
    assert t.assign([(.2, .5), (.5, .5), (.8, .5)], now=now) == {1: 0, 2: 1, 3: 2}
    t.lock()   # Unity saved the neutral pose: the game starts
    return t


class RoleSpotTests(unittest.TestCase):
    def test_lock_remembers_the_spots(self):
        t = calibrated_three()
        self.assertEqual([p[0] for p in t.home], [.2, .5, .8])
        t.reset()
        self.assertIsNone(t.home)

    def test_a_person_in_another_players_spot_never_takes_the_role(self):
        t = calibrated_three()
        # P2 leaves; someone stands at the far left, next to P1 (P1's spot, not P2's).
        for k in range(10):
            result = t.assign([(.2, .5), (.8, .5), (.03, .5)], now=1 + k * .3)
            self.assertEqual(result, {1: 0, 3: 1}, k)
        self.assertEqual(t.relocked, [])

    def test_returning_close_to_your_spot_is_picked_up_at_once(self):
        t = calibrated_three()
        self.assertEqual(t.assign([(.2, .5), (.8, .5)], now=1), {1: 0, 3: 1})            # P2 out of the picture
        self.assertEqual(t.assign([(.2, .5), (.8, .5), (.42, .5)], now=1.2), {1: 0, 2: 2, 3: 1})

    def test_returning_far_into_your_own_spot_gets_your_role_back_after_a_second(self):
        t = PerformerTracker(3)
        t.assign([(.3, .5), (.5, .5), (.7, .5)], now=0)
        t.lock()
        self.assertEqual(t.assign([(.5, .5), (.7, .5)], now=1), {2: 0, 3: 1})            # P1 out
        self.assertEqual(t.assign([(.5, .5), (.7, .5), (.05, .5)], now=1.5), {2: 0, 3: 1})   # back at the far left
        result = t.assign([(.5, .5), (.7, .5), (.05, .5)], now=2.05)
        self.assertEqual(result, {1: 2, 2: 0, 3: 1})
        self.assertEqual(t.relocked, [1])

    def test_two_players_out_at_once_come_back_to_their_own_roles(self):
        t = calibrated_three()
        t.assign([(.8, .5)], now=1)                                    # P1 and P2 both out
        # They come back in each other's old order of arrival: the one in the middle first in the list.
        result = t.assign([(.8, .5), (.47, .5), (.24, .5)], now=2.2)
        self.assertEqual(result, {3: 0, 1: 2, 2: 1})                   # by spot: left = P1, middle = P2

    def test_single_player_comes_back_anywhere(self):
        t = PerformerTracker(1)
        t.assign([(.5, .5)], now=0)
        t.lock()
        self.assertEqual(t.assign([(.85, .5)], now=1), {})
        self.assertEqual(t.assign([(.85, .5)], now=2.1), {1: 0})       # the only role, back after 1 s

    def test_before_the_game_the_old_relock_still_applies(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.5, .5), (.8, .5)], now=0)                 # filled, but no neutral pose saved yet
        self.assertIsNone(t.home)
        t.assign([(.2, .5), (.8, .5), (.03, .5), (.72, .5)], now=1)
        self.assertEqual(t.assign([(.2, .5), (.8, .5), (.03, .5), (.72, .5)], now=2.1), {1: 0, 3: 1, 2: 3})


if __name__ == '__main__':
    unittest.main()
