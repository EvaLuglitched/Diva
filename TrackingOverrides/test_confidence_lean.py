import unittest
from mediapipe.tasks.python.components.containers.landmark import NormalizedLandmark
from diva_gestures import DivaGestureDetector, associate_hands, scale_body, thumbs_up, quality, DEFAULTS
from test_diva_gestures import body
from test_thumb_prayer import thumb_hand


class ConfidenceLeanTests(unittest.TestCase):
    def test_real_sdk_optional_confidence_through_matching_and_refill(self):
        p=body(); raw=thumb_hand(p[15])
        points=[NormalizedLandmark(x=v['x'],y=v['y'],z=v['z']) for v in raw['landmarks']]
        self.assertIsNone(points[0].visibility)
        h={'landmarks':points}
        matched=associate_hands([h],{3:p})[3]
        self.assertEqual(len(matched),1)
        self.assertTrue(thumbs_up(scale_body(points,1.),DEFAULTS))
        d=DivaGestureDetector();d.update(3,p,0,matched)
        self.assertTrue(d.update(3,p,.4,matched)['drink'])
        points[4].visibility=0.
        self.assertFalse(d.update(3,p,.5,matched)['drink'])

    def test_quality_preserves_zero_and_rejects_invalid_values(self):
        for value,want in ((None,1.),(0.,0.),(.25,.25),(1.,1.),(False,0.),(True,0.),('1',0.),(float('nan'),0.),(float('inf'),0.),(-.1,0.),(1.1,0.)):
            with self.subTest(value=value):self.assertEqual(quality({'visibility':value,'presence':None}),want)
        self.assertEqual(quality({}),1.)

    def test_free_arm_lean_turns_without_go_and_upright_stops(self):
        d=DivaGestureDetector();d.update(1,body(),0);d.calibrate()
        for t,lean in ((.1,.3),(.2,-.3)):
            g=d.update(1,body(lean=lean,height=-1.2,separate=1.8),t)
            self.assertLess(g['steer']*lean,0)
            self.assertEqual(g['go'],0)
        self.assertEqual(d.update(1,body(handshift=.8),.3)['steer'],0)

    def test_hidden_wrists_allow_lean_but_not_other_actions(self):
        d=DivaGestureDetector();p=body(lean=.3)
        for i in (15,16):p[i]['visibility']=.01;p[i]['x']=float('nan')
        g=d.update(1,p,0)
        self.assertLess(g['steer'],0)
        for key in ('go','aim','shoot','drink','ult'):self.assertFalse(g[key])

if __name__=='__main__':unittest.main()
