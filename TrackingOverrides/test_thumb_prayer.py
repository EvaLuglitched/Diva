import math
import unittest
from diva_gestures import DivaGestureDetector, associate_hands
from test_diva_gestures import body


def thumb_hand(wrist, direction='up', extended_finger=None):
    # Nondegenerate synthetic hand: palm upright, four fingers folded at PIP.
    x,y,z=(wrist[k] for k in ('x','y','z'))
    p=[dict(x=x,y=y,z=z) for _ in range(21)]
    for base,dx in ((5,-.025),(9,-.008),(13,.009),(17,.026)):
        for k,dy in enumerate((-.04,-.07,-.045,-.025)):
            p[base+k].update(x=x+dx,y=y+dy)
        if extended_finger in ('all',base):
            for k in range(4):p[base+k]['y']=y-.04-.025*k
    for i,dx,dy in ((1,-.035,-.015),(2,-.04,-.04),(3,-.04,-.075),(4,-.04,-.11)):
        p[i].update(x=x+dx,y=y+dy)
    if direction=='side':
        p[3].update(x=x-.075,y=y-.04);p[4].update(x=x-.11,y=y-.04)
    elif direction=='down':
        p[3]['y']=y-.005;p[4]['y']=y+.03
    elif direction=='fist':p[3]['y']=y-.025;p[4]['y']=y-.01
    return {'landmarks':p}

def prayer(lean=0., **kwargs):
    return body(lean=lean,height=-.2,separate=.2,**kwargs)

class ThumbPrayerTests(unittest.TestCase):
    def test_either_thumb_dwell_with_other_wrist_hidden_and_low_cadence(self):
        for wrist,other in ((15,16),(16,15)):
            d=DivaGestureDetector();p=body(height=-.2)
            p[other]['visibility']=.01;p[other]['x']=float('nan')
            h=thumb_hand(p[wrist])
            for t in (0,.2,.4,.9):
                g=d.update(3,p,t,[h])
                self.assertEqual(g['drink'],t>=.4)
                self.assertGreaterEqual(g['confidence'],.3)
                for key in ('go','shoot','aim','ult'):self.assertFalse(g[key])

    def test_thumb_direction_and_four_curled_fingers_are_required(self):
        for direction,finger in (('side',None),('down',None),('fist',None),('up',5),('up','all')):
            d=DivaGestureDetector();p=body();h=thumb_hand(p[15],direction,finger)
            for t in (0,.2,.4,.6):self.assertFalse(d.update(3,p,t,[h])['drink'],(direction,finger))

    def test_thumb_releases_and_resets_for_loss_gap_or_calibration(self):
        for reset in ('release','loss','gap','calibration'):
            d=DivaGestureDetector();p=body();h=thumb_hand(p[15])
            d.update(3,p,0,[h]);self.assertTrue(d.update(3,p,.4,[h])['drink'])
            if reset=='release':self.assertFalse(d.update(3,p,.5,[])['drink'])
            elif reset=='loss':self.assertFalse(d.update(3,None,.5,[h])['drink'])
            elif reset=='calibration':d.calibrate()
            self.assertFalse(d.update(3,p,1.1 if reset=='gap' else .6,[h])['drink'])

    def test_wrong_person_unmatched_invalid_or_invisible_hand_rejected(self):
        p=body();h=thumb_hand(body(offset=.7)[15])
        for kind in ('wrong','invalid','wrist'):
            d=DivaGestureDetector();b=body();bad=h if kind=='wrong' else thumb_hand(b[15])
            if kind=='invalid':bad['landmarks'][4]['x']=float('nan')
            if kind=='wrist':b[15]['visibility']=.01;b[16]['visibility']=.01
            for t in (0,.2,.4):self.assertFalse(d.update(3,b,t,[bad])['drink'],kind)
        other=body(offset=.7);otherhand=thumb_hand(other[15])
        self.assertEqual(associate_hands([otherhand],{1:other,3:p})[3],[])

    def test_degenerate_fingers_are_not_evidence_of_a_thumbs_up(self):
        d=DivaGestureDetector();p=body();h=thumb_hand(p[15])
        for i in range(5,21):h['landmarks'][i]=dict(h['landmarks'][0])
        for t in (0,.2,.4):self.assertFalse(d.update(3,p,t,[h])['drink'])

    def test_matching_ignores_nan_other_wrist_and_reset_retains_no_dwell(self):
        p=body();h=thumb_hand(p[15]);p[16]['x']=float('nan')
        self.assertEqual(len(associate_hands([h],{3:p})[3]),1)
        for reset in ('retain','reset','backwards'):
            d=DivaGestureDetector();d.update(3,p,1,[h]);d.update(3,p,1.2,[h])
            if reset=='retain':d.retain([])
            elif reset=='reset':d.reset()
            self.assertFalse(d.update(3,p,.9 if reset=='backwards' else 1.4,[h])['drink'])

    def test_invalid_other_body_cannot_poison_own_hand_assignment(self):
        own=body();other=body();h=thumb_hand(own[15])
        other[11]['x']=float('nan')
        matched=associate_hands([h],{1:other,3:own})
        self.assertEqual(len(matched[3]),1)
        self.assertEqual(matched[1],[])

    def test_prayer_gate_both_lean_directions_zero_go_and_upright_stop(self):
        d=DivaGestureDetector();d.update(2,prayer(),0);d.calibrate()
        for t,lean in ((.1,.3),(.2,-.3)):
            g=d.update(2,prayer(lean),t)
            self.assertLess(g['steer']*lean,0)
            for key in ('go','shoot','aim','drink','ult'):self.assertFalse(g[key])
        self.assertEqual(d.update(2,prayer(),.3)['steer'],0)

    def test_free_arms_separated_offcenter_or_missing_prayer_stops_turn(self):
        for kind in ('free','separated','offcenter','wrist','hips'):
            d=DivaGestureDetector();d.update(2,prayer(.3),0)
            p=prayer(.3)
            if kind=='free':p=body(lean=.3,height=-1.2)
            elif kind=='separated':p[15]['x']+=.2
            elif kind=='offcenter':
                for i in (15,16):p[i]['x']+=.3
            else:p[15 if kind=='wrist' else 23]['visibility']=.01
            self.assertEqual(d.update(2,p,.1)['steer'],0,kind)

if __name__=='__main__':unittest.main()
