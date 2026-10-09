import math, unittest
from diva_gestures import DivaGestureDetector, associate_hands, open_fingers

def body(offset=0., lean=0., handshift=0., height=-.8, separate=.2, visibility=1.):
    p=[dict(x=.5+offset,y=.3,z=0.,visibility=visibility,presence=visibility) for _ in range(33)]
    for i,x,y in [(11,.4+lean*.3,.3),(12,.6+lean*.3,.3),(23,.42,.6),(24,.58,.6),(13,.38,.4),(14,.62,.4),(15,.5+lean*.3+handshift*.3+separate*.15,.3-height*.3),(16,.5+lean*.3+handshift*.3-separate*.15,.3-height*.3),(0,.5,.18),(9,.48,.22),(10,.52,.22)]:
        p[i].update(x=x+offset,y=y)
    return p

def hand(wrist, opened=True):
    x,y= wrist['x'],wrist['y']; h=[dict(x=x,y=y,z=0.) for _ in range(21)]
    for base in [5,9,13,17]:
        dx=(base-11)*.003
        for k in range(4):
            h[base+k].update(x=x+dx,y=y-.015*(k+1) if opened else y-.015*(1 if k==0 else .6))
    return {'landmarks':h}

class GestureTests(unittest.TestCase):
    def test_held_arms_never_go(self):
        d=DivaGestureDetector()
        for i in range(25): self.assertEqual(d.update(1,body(height=0),i*.1)['go'],0.)
    def test_pumping_go_then_stops(self):
        d=DivaGestureDetector()
        for i in range(18): g=d.update(1,body(height=.1+math.sin(i*.9)*.3),i*.1)
        self.assertGreater(g['go'],.5)
        for i in range(18,30): g=d.update(1,body(height=0),i*.1)
        self.assertEqual(g['go'],0.)
    def test_lowered_arm_body_movement_is_not_go(self):
        d=DivaGestureDetector()
        for i in range(20): g=d.update(1,body(offset=.05*math.sin(i),height=-1.),i*.1)
        self.assertEqual(g['go'],0.)
    def test_left_and_right_use_confirmed_camera_direction(self):
        d=DivaGestureDetector()
        self.assertLess(d.update(1,body(lean=.2,handshift=.7),0)['steer'],0)
        self.assertGreater(d.update(1,body(lean=-.2,handshift=-.7),.1)['steer'],0)
    def test_neutral_and_spread_hands_straight(self):
        d=DivaGestureDetector()
        self.assertEqual(d.update(1,body(),0)['steer'],0)
        self.assertEqual(d.update(1,body(lean=.4,handshift=.6,separate=1.8),.1)['steer'],0)
    def test_missing_and_bad_tracking_stop_everything(self):
        d=DivaGestureDetector()
        d.update(1,body(lean=.3,handshift=.7),0)
        self.assertEqual(d.update(1,None,.1)['confidence'],0.)
        self.assertEqual(d.update(1,body(visibility=.1),.2)['steer'],0.)
    def test_open_fingers_versus_curled(self):
        self.assertEqual(open_fingers(hand(dict(x=.5,y=.4))['landmarks']),4)
        self.assertEqual(open_fingers(hand(dict(x=.5,y=.4),False)['landmarks']),0)
    def test_both_open_extended_shoot_until_release(self):
        d=DivaGestureDetector(); b=body(height=.05,separate=1.)
        for i in [15,16]: b[i]['z']=-.3
        h=[hand(b[15]),hand(b[16])]
        for t in [0,.1,.5]: self.assertTrue(d.update(3,b,t,h)['shoot'])
        self.assertFalse(d.update(3,b,.6,h[:1])['shoot'])
        self.assertFalse(d.update(3,b,.7,[hand(b[15],False),h[1]])['shoot'])
    def test_open_hands_lowered_cannot_shoot(self):
        d=DivaGestureDetector(); b=body(height=-1.,separate=1.)
        self.assertFalse(d.update(3,b,0,[hand(b[15]),hand(b[16])])['shoot'])
    def test_drink_curled_near_mouth_not_shoot(self):
        d=DivaGestureDetector(); b=body(height=.26,separate=.5)
        g=d.update(3,b,0,[hand(b[15],False),hand(b[16],False)])
        self.assertTrue(g['drink']); self.assertFalse(g['shoot'])
    def test_hand_association_excludes_other_player(self):
        a=body(offset=-.28,separate=.5); b=body(offset=.28,separate=.5)
        hands=[hand(a[15]),hand(a[16]),hand(b[15]),hand(b[16])]
        assigned=associate_hands(hands,{1:a,3:b})
        self.assertEqual(len(assigned[3]),2)
        self.assertGreater(assigned[3][0]['landmarks'][0]['x'],.5)
    def test_aim_single_raised_arm(self):
        d=DivaGestureDetector(); b=body(); b[15]['y']=0.
        self.assertEqual(d.update(3,b,0)['aim'],-1.)
        b[16]['y']=0.
        self.assertEqual(d.update(3,b,.1)['aim'],0.)
    def test_pumping_at_live_30_fps(self):
        d=DivaGestureDetector()
        for i in range(40):g=d.update(1,body(height=.1+math.sin(i*.22)*.2),i/30)
        self.assertGreater(g['go'],.5)
    def test_neutral_calibration_and_reset(self):
        d=DivaGestureDetector(); b=body(lean=.2,handshift=.6)
        self.assertLess(d.update(1,b,0)['steer'],0)
        d.calibrate()
        self.assertEqual(d.update(1,b,.1)['steer'],0)
        d.reset()
        self.assertLess(d.update(1,b,.2)['steer'],0)
    def test_aspect_correction_preserves_actions(self):
        d=DivaGestureDetector(); p=body(lean=.2,handshift=.7)
        expected=d.update(1,p,0)['steer']
        portrait=[dict(j,x=j['x']/ .6,z=j['z']/ .6) for j in p]
        self.assertAlmostEqual(d.update(2,portrait,.1,aspect=.6)['steer'],expected)
    def test_calibration_survives_missing_body(self):
        d=DivaGestureDetector(); b=body(lean=.2,handshift=.6)
        d.update(1,b,0);d.calibrate();d.update(1,None,.1)
        self.assertEqual(d.update(1,b,1.)['steer'],0.)
    def test_opposed_body_and_hands_do_not_turn(self):
        d=DivaGestureDetector()
        self.assertEqual(d.update(1,body(lean=-.2,handshift=.9),0)['steer'],0.)
    def test_curled_fists_at_shoulders_below_face_do_not_drink(self):
        d=DivaGestureDetector(); b=body(height=0,separate=.5)
        b[9]['y']=b[10]['y']=.13
        self.assertFalse(d.update(3,b,0,[hand(b[15],False),hand(b[16],False)])['drink'])
    def test_no_visible_face_cannot_drink_live(self):
        d=DivaGestureDetector();b=body(height=.26,separate=.5)
        for i in [0,9,10]:b[i]['visibility']=b[i]['presence']=0.
        self.assertFalse(d.update(3,b,0,[hand(b[15],False),hand(b[16],False)])['drink'])
    def test_invalid_hand_and_missing_elbow_do_not_shoot(self):
        d=DivaGestureDetector();b=body(height=.05,separate=1.)
        for i in [15,16]:b[i]['z']=-.3
        hands=[hand(b[15]),hand(b[16])];hands[0]['landmarks'][5]['x']=float('nan')
        self.assertFalse(d.update(3,b,0,hands)['shoot'])
        hands=[hand(b[15]),hand(b[16])];b[13]['visibility']=.1
        self.assertFalse(d.update(3,b,.1,hands)['shoot'])

    def test_steering_hysteresis_keeps_turning_until_hands_return(self):
        d=DivaGestureDetector()
        self.assertLess(d.update(2,body(lean=.2,handshift=.7),0)['steer'],0)
        # Smaller offset, no lean: too little to start a turn, enough to keep one going.
        self.assertLess(d.update(2,body(handshift=.1),.1)['steer'],0)
        self.assertEqual(DivaGestureDetector().update(2,body(handshift=.1),0)['steer'],0)
        # Hands back in the middle: stop at once.
        self.assertEqual(d.update(2,body(handshift=.02),.2)['steer'],0)
    def test_steering_rides_through_a_brief_tracking_blip(self):
        d=DivaGestureDetector()
        turning=d.update(2,body(lean=.2,handshift=.7),0)['steer']
        b=body(lean=.2,handshift=.7)
        for i in [23,24]: b[i]['visibility']=b[i]['presence']=.1
        self.assertEqual(d.update(2,b,.15)['steer'],turning)
        self.assertEqual(d.update(2,b,.4)['steer'],0)
    def test_aim_while_spraying_by_pushing_both_hands_sideways(self):
        d=DivaGestureDetector()
        for t,shift,sign in [(0,.25,-1),(.1,-.25,1),(.2,0.,0)]:
            b=body(height=.05,separate=1.,handshift=shift)
            for i in [15,16]: b[i]['z']=-.3
            g=d.update(3,b,t,[hand(b[15]),hand(b[16])])
            self.assertTrue(g['shoot'])
            if sign==0: self.assertEqual(g['aim'],0)
            else: self.assertEqual(math.copysign(1,g['aim']),sign)
    def test_ult_both_hands_above_head_held(self):
        d=DivaGestureDetector()
        for i in range(7): g=d.update(1,body(height=1.2,separate=.8),i*.1)
        self.assertFalse(g['ult'])                       # 0.6 s: not yet
        for i in range(7,12): g=d.update(1,body(height=1.2,separate=.8),i*.1)
        self.assertTrue(g['ult']); self.assertEqual(g['go'],0.); self.assertEqual(g['aim'],0.)
        g=d.update(1,body(height=0),1.2)
        self.assertFalse(g['ult'])                       # hands down releases it
    def test_pumping_through_the_top_is_not_ult(self):
        d=DivaGestureDetector()
        for i in range(30): g=d.update(1,body(height=.5+math.sin(i*.9)*.6),i*.1); self.assertFalse(g['ult'])
    def test_one_raised_arm_aims_not_ult(self):
        d=DivaGestureDetector(); b=body(height=0)
        b[15]['y']=.3-1.2*.3
        for i in range(12): g=d.update(1,b,i*.1)
        self.assertFalse(g['ult']); self.assertNotEqual(g['aim'],0.)
    def test_drink_uses_remembered_mouth_when_hands_hide_the_face(self):
        d=DivaGestureDetector()
        d.update(3,body(),0)
        b=body(height=.26,separate=.5)
        for i in [0,9,10]: b[i]['visibility']=b[i]['presence']=0.
        hands=[hand(b[15],False),hand(b[16],False)]
        self.assertTrue(d.update(3,b,1.,hands)['drink'])
        self.assertFalse(d.update(3,b,4.5,hands)['drink'])

if __name__=='__main__':unittest.main()
