"""v1.5 (extends Eva's role spots, 2026-10-13): in a group game the player in their own spot wins their role back.

Without this, someone standing near a player's spot (a person behind P2, or a second detection of someone) took the
role while the player was hidden for a moment and kept it after the player came back: P2 could not steer until that
person left. Roles are now ranked by the distance to the spot saved with the neutral pose.
"""
import unittest

from bridge import PerformerTracker

P1, P2, P3 = (.25, .55), (.50, .55), (.75, .55)
BEHIND_P2 = (.58, .40)          # a person behind P2, a little to the side (within 0.20 of P2's spot)


def started(count, *bodies):
    t = PerformerTracker(count)
    t.assign(list(bodies), now=0.)
    t.lock()                     # Unity saved the neutral pose: spots are remembered
    return t


def role(t, bodies, now, slot):
    got = t.assign(bodies, now=now)
    return bodies[got[slot]] if slot in got else None


class SpotAnchorTests(unittest.TestCase):
    def test_player_back_in_their_spot_wins_the_role_back(self):
        t = started(3, P1, P2, P3)
        self.assertEqual(role(t, [P1, P2, P3, BEHIND_P2], .1, 2), P2)
        for k in range(5):                                            # P2 hidden: the person behind holds the role
            role(t, [P1, P3, BEHIND_P2], .3 + k * .2, 2)
        self.assertEqual(role(t, [P1, P2, P3, BEHIND_P2], 1.5, 2), P2)   # P2 back: role back at once
        self.assertEqual(role(t, [P1, P2, P3, BEHIND_P2], 2.0, 2), P2)

    def test_other_roles_stay_put(self):
        t = started(3, P1, P2, P3)
        for k in range(5):
            got = t.assign([P1, P3, BEHIND_P2], now=.3 + k * .2)
            self.assertEqual(got.get(1), 0); self.assertEqual(got.get(3), 1)

    def test_a_player_who_drifts_is_still_followed(self):
        t = started(3, P1, P2, P3)
        x = P1[0]
        for k in range(12):                                           # P1 slowly steps 0.24 to the left
            x -= .02
            self.assertEqual(role(t, [(x, .55), P2, P3], .1 + k * .1, 1), (x, .55))

    def test_one_player_is_unchanged(self):
        # Solo: the role follows the player in view; someone standing where the player calibrated does not take it.
        t = started(1, P2)
        x = P2[0]
        for k in range(10):
            x += .03
            self.assertEqual(role(t, [(x, .55)], .1 + k * .1, 1), (x, .55))
        self.assertEqual(role(t, [(x, .55), P2], 1.2, 1), (x, .55))


if __name__ == '__main__':
    unittest.main()
