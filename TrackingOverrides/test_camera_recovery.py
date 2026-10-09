import unittest
from bridge import CameraRecovery

class FakeCamera:
    def __init__(self,reads=None):self.reads=list(reads or []);self.released=False
    def read(self):return self.reads.pop(0) if self.reads else (False,None)
    def release(self):self.released=True

class CameraRecoveryTests(unittest.TestCase):
    def test_short_gap_does_not_reopen_and_long_gap_does(self):
        original=FakeCamera();fresh=FakeCamera([(True,'frame')]);calls=[]
        def reopen():calls.append(1);return fresh
        r=CameraRecovery(original,reopen,0,timeout=3)
        self.assertEqual(r.read(1),(False,None,False));self.assertEqual(calls,[])
        self.assertEqual(r.read(3.1),(False,None,True));self.assertTrue(original.released)
        self.assertEqual(r.read(3.2),(True,'frame',False));self.assertEqual(calls,[1])
    def test_three_attempts_are_finite_and_release_each_camera(self):
        cameras=[FakeCamera()];calls=[]
        def reopen():calls.append(1);cameras.append(FakeCamera());return cameras[-1]
        r=CameraRecovery(cameras[0],reopen,0,timeout=3,max_reopens=3)
        for t in [3.1,6.2,9.3]:self.assertTrue(r.read(t)[2])
        with self.assertRaisesRegex(RuntimeError,'3 reconnect'):r.read(12.4)
        self.assertEqual(len(calls),3)
        self.assertTrue(all(c.released for c in cameras[:-1]))
    def test_brief_healthy_frame_does_not_reset_attempt_budget(self):
        camera=FakeCamera();fresh=FakeCamera([(True,'a'),(False,None)])
        r=CameraRecovery(camera,lambda:fresh,0,timeout=3)
        r.read(3.1);r.read(3.2);r.read(3.3)
        self.assertEqual(r.reopens,1)
    def test_stable_healthy_period_resets_attempt_budget(self):
        r=CameraRecovery(FakeCamera(),lambda:FakeCamera([(True,'a'),(True,'b')]),0,timeout=3,stable_seconds=10)
        r.read(3.1);r.read(3.2);r.read(13.3)
        self.assertEqual(r.reopens,0)

if __name__=='__main__':unittest.main()
