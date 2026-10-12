"""Retired refill gestures must remain inactive after the thumbs-up change."""
import unittest
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body, hand

def refill_pose():
    p=body(height=-.5,separate=.5)
    p[13].update(x=.35,y=.53);p[14].update(x=.65,y=.53)
    return p

class RetiredRefillTests(unittest.TestCase):
    def test_old_chest_hold_without_hand_tracking_no_longer_refills(self):
        d=DivaGestureDetector();p=refill_pose()
        for i in range(10):self.assertFalse(d.update(3,p,i*.5)['drink'])
    def test_old_chest_hold_with_open_or_curled_fingers_no_longer_refills(self):
        for opened in (True,False):
            d=DivaGestureDetector();p=refill_pose();hands=[hand(p[15],opened),hand(p[16],opened)]
            for i in range(10):self.assertFalse(d.update(3,p,i*.2,hands)['drink'])
    def test_old_mouth_fists_no_longer_refill(self):
        d=DivaGestureDetector();p=body(height=.26,separate=.5)
        for i in range(10):self.assertFalse(d.update(3,p,i*.2,[hand(p[15],False),hand(p[16],False)])['drink'])

if __name__=='__main__':unittest.main()
