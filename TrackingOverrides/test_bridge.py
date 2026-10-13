import time
import unittest
from types import SimpleNamespace
from bridge import GroupPoseDetector, LandmarkSmoother, OneEuro, PerformerTracker, extract_features, performance_profile, requested_count


def fake_pose(x, y, q=.9):
    return [SimpleNamespace(x=x, y=y, z=0., visibility=q, presence=q) for _ in range(33)]


class FakeLandmarker:
    """Returns the bodies it is told to see; a fresh VIDEO instance 'detects' everything on its first frame."""
    def __init__(self, bodies, log, name):
        self.bodies, self.log, self.name, self.closed = bodies, log, name, False
    def detect_for_video(self, image, ts):
        self.log.append(('video', self.name)); return SimpleNamespace(pose_landmarks=[fake_pose(*b) for b in self.bodies()])
    def detect(self, image):
        self.log.append(('image', self.name)); return SimpleNamespace(pose_landmarks=[fake_pose(*b) for b in self.bodies()])
    def close(self): self.closed = True


class GroupDetectorTests(unittest.TestCase):
    def test_group_detects_every_frame_inline(self):
        log = []; scene = {'all': [(.5, .5), (.2, .5), (.8, .5)]}
        d = GroupPoseDetector(lambda: FakeLandmarker(lambda: [], log, 'v'), lambda: FakeLandmarker(lambda: scene['all'], log, 'i'), group=True, threaded=False)
        d._mp_image = staticmethod(lambda rgb: rgb); d._downscale = lambda rgb: rgb
        self.assertIsNone(d.video)
        self.assertEqual(len(d.detect(None, 0).pose_landmarks), 3)
        scene['all'] = scene['all'][:2]
        self.assertEqual(len(d.detect(None, 33).pose_landmarks), 2)      # a fresh detection every frame
        self.assertFalse(any(kind == 'video' for kind, _ in log))

    def test_group_worker_never_blocks_the_camera_loop(self):
        log = []; scene = {'all': [(.5, .5), (.2, .5), (.8, .5)]}
        class SlowImage(FakeLandmarker):
            def detect(self, image):
                time.sleep(.05); return super().detect(image)
        d = GroupPoseDetector(lambda: FakeLandmarker(lambda: [], log, 'v'), lambda: SlowImage(lambda: scene['all'], log, 'i'), group=True, threaded=True)
        d._mp_image = staticmethod(lambda rgb: rgb); d._downscale = lambda rgb: rgb
        self.assertEqual(len(d.detect(object(), 0).pose_landmarks), 3)   # first frame waits for a result
        started = time.monotonic()
        for i in range(1, 20): d.detect(object(), i * 10)                  # 19 frames in ~0 ms: the loop is not blocked
        self.assertLess(time.monotonic() - started, .03)
        self.assertGreater(d.dropped, 5)                                   # superseded frames were skipped, not queued
        time.sleep(.15)
        self.assertGreaterEqual(d.detections, 2)
        d.close()

    def test_solo_tracks_and_group_switch_changes_detectors(self):
        log = []
        d = GroupPoseDetector(lambda: FakeLandmarker(lambda: [(.5, .5)], log, 'v'), lambda: FakeLandmarker(lambda: [(.5, .5), (.2, .5)], log, 'i'), group=False, threaded=False)
        d._mp_image = staticmethod(lambda rgb: rgb); d._downscale = lambda rgb: rgb
        for t in range(3): d.detect(None, t * 33)
        self.assertFalse(any(kind == 'image' for kind, _ in log))
        d.set_group(True); self.assertIsNone(d.video); self.assertIsNotNone(d.image)
        self.assertEqual(len(d.detect(None, 200).pose_landmarks), 2)
        d.set_group(False); self.assertIsNone(d.image); self.assertIsNotNone(d.video)
        self.assertEqual(len(d.detect(None, 233).pose_landmarks), 1)


class SmoothingTests(unittest.TestCase):
    def test_one_euro_removes_jitter_but_follows_motion(self):
        import random
        rng = random.Random(1); f = OneEuro()
        still = [f(.5 + rng.uniform(-.01, .01), i / 30) for i in range(60)]
        self.assertLess(max(still[30:]) - min(still[30:]), .011)            # jitter of ±0.01 (spread .02) at least halves when still
        g = OneEuro(); moving = [g(.2 + i * .01, i / 30) for i in range(30)]  # 0.3 per second: a real move
        self.assertLess(abs(moving[-1] - (.2 + 29 * .01)), .03)              # lags the motion by less than 3 frames

    def test_landmark_smoother_keeps_slots_apart_and_forgets_dropped_ones(self):
        sm = LandmarkSmoother()
        a = sm.apply(1, fake_pose(.2, .5), 0.); b = sm.apply(2, fake_pose(.8, .5), 0.)
        self.assertAlmostEqual(a[11].x, .2); self.assertAlmostEqual(b[11].x, .8)
        sm.retain({1: 0}); self.assertNotIn(2, sm.filters); self.assertIn(1, sm.filters)

    def test_profiles(self):
        self.assertEqual(performance_profile('fast')['lite'], True)
        self.assertEqual(performance_profile('quality')['hands_every'], 1)
        self.assertIn(performance_profile('auto')['name'], ('fast', 'quality'))


class TrackingTests(unittest.TestCase):
    def test_roles_survive_detection_reordering_and_dropout(self):
        t = PerformerTracker(3)
        # Two of three visible: they get the leftmost roles for now (not locked yet).
        self.assertEqual(t.assign([(.8, .5), (.2, .5)]), {1: 1, 2: 0})
        self.assertFalse(t.locked)
        # The third arrives in the middle: roles re-sort left to right and lock.
        self.assertEqual(t.assign([(.8, .5), (.2, .5), (.5, .5)]), {1: 1, 2: 2, 3: 0})
        self.assertTrue(t.locked)
        self.assertEqual(t.assign([(.51, .5), (.81, .5), (.21, .5)]), {1: 2, 2: 0, 3: 1})
        self.assertEqual(t.assign([(.52, .5), (.22, .5)]), {1: 1, 2: 0})
        self.assertEqual(t.assign([(.82, .5)]), {3: 0})

    def test_group_fills_in_as_people_arrive_and_locks_on_calibration(self):
        t = PerformerTracker(3)
        self.assertEqual(t.assign([(.5, .5)]), {1: 0})
        self.assertEqual(t.filled, 1)
        # Someone steps in on the left before the group is complete: they become P1, the first person moves to P2.
        self.assertEqual(t.assign([(.51, .5), (.2, .5)]), {1: 1, 2: 0})
        # Unity saves the neutral pose with only two people: roles lock as they are, slot 3 stays open.
        t.lock()
        self.assertFalse(t.locked)   # cannot lock an incomplete group
        self.assertEqual(t.assign([(.52, .5), (.21, .5), (.85, .5)]), {1: 1, 2: 0, 3: 2})
        self.assertTrue(t.locked)
        # Locked: a newcomer far from every known position gets no role.
        self.assertEqual(t.assign([(.52, .5), (.21, .5), (.85, .5), (.05, .5)]), {1: 1, 2: 0, 3: 2})
        t.reset()
        self.assertFalse(t.locked)

    def test_ambiguous_assignment_pauses_only_the_ambiguous_players(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.4, .5), (.8, .5)])
        # (.3) could be P1 or P2: those two pause; P3 (clearly at .8) keeps its role (was: everyone dropped).
        self.assertEqual(t.assign([(.3, .5), (.8, .5)]), {3: 1})
        t.reset()
        self.assertIsNone(t.positions)

    def test_one_and_two_people_with_extra_bystanders(self):
        for count in (1, 2):
            t = PerformerTracker(count)
            assigned = t.assign([(.8, .5), (.2, .5), (.5, .5)])
            self.assertEqual(len(assigned), count)
            self.assertEqual(assigned[1], 1)
            self.assertEqual(t.assign([(.21, .5)]), {1: 0})

    def test_count_request_validation(self):
        for count in (1, 2, 3, 4):
            self.assertEqual(requested_count('{"version":1,"performerCount":%d}' % count), count)
        for data in ('null', '[]', 'bad', '{"version":1,"performerCount":0}',
                     '{"version":1,"performerCount":5}', '{"version":1,"performerCount":true}',
                     '{"version":1,"performerCount":"2"}', '{"version":2,"performerCount":2}'):
            self.assertIsNone(requested_count(data))

    def test_four_people(self):
        t = PerformerTracker(4)
        self.assertEqual(len(t.assign([(.1, .5), (.35, .5), (.6, .5), (.85, .5)])), 4)
        self.assertEqual(t.assign([]), {})

    def test_upper_body_works_with_no_visible_lower_body(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=0., visibility=0.) for _ in range(33)]
        for i, xy in {11:(.4,.3), 12:(.6,.3), 15:(.3,.5), 16:(.7,.5)}.items():
            points[i] = SimpleNamespace(x=xy[0], y=xy[1], presence=1., visibility=1.)
        center, baseline, confidence = extract_features(points, True)
        self.assertEqual(center, (.5, .3))
        self.assertEqual(confidence, [1.] * 6)
        self.assertEqual(extract_features(points)[2][4], 0.)
        points[15].y -= .1
        _, raised, _ = extract_features(points, True)
        self.assertGreater(raised[0], baseline[0])
        self.assertEqual(raised[0], raised[2])
        self.assertEqual(raised[1], raised[3])
        points[11].y -= .05
        self.assertLess(extract_features(points, True)[1][4], baseline[4])
        points[15].visibility = 0.
        _, _, confidence = extract_features(points, True)
        self.assertEqual(confidence, [0., 1., 0., 1., 1., 0.])

    def test_features_are_translation_and_scale_invariant(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=1., visibility=1.) for _ in range(33)]
        for i, xy in {11:(.4,.3),12:(.6,.3),23:(.4,.6),24:(.6,.6),15:(.2,.1),16:(.8,.2),27:(.4,.9),28:(.6,.9)}.items():
            points[i].x, points[i].y = xy
        _, first, _ = extract_features(points)
        for p in points:
            p.x = p.x * .5 + .1
            p.y = p.y * .5 + .2
        _, second, _ = extract_features(points)
        for a, b in zip(first, second):
            self.assertAlmostEqual(a, b)
        points[15].visibility = .1
        _, _, confidence = extract_features(points)
        self.assertEqual(confidence[0], .1)
        self.assertEqual(confidence[1], 1.)


if __name__ == '__main__':
    unittest.main()
