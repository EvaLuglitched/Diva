import unittest
from types import SimpleNamespace
from bridge import GroupPoseDetector, PerformerTracker, extract_features, requested_count


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
    def test_search_finds_late_arrivals_and_rebuilds_the_tracker(self):
        log = []; scene = {'all': [(.5, .5)]}; made = []
        # The tracker only ever sees whoever was there when it was created; the search sees everyone.
        def make_video():
            snapshot = list(scene['all']); v = FakeLandmarker(lambda: snapshot, log, f'video{len(made)}'); made.append(v); return v
        make_image = lambda: FakeLandmarker(lambda: scene['all'], log, 'image')
        d = GroupPoseDetector(make_video, make_image, group=True, search_interval=.5)
        self.assertEqual(len(d.detect(None, 0, 0.0).pose_landmarks), 1)        # search at t=0 finds nothing new
        scene['all'] = [(.5, .5), (.2, .5), (.8, .5)]                          # two more people step in
        self.assertEqual(len(d.detect(None, 33, 0.2).pose_landmarks), 1)       # no search yet: still the old tracker
        found = d.detect(None, 66, 0.6)                                        # search runs: sees 3, rebuilds
        self.assertEqual(len(found.pose_landmarks), 3)
        self.assertEqual(d.rebuilds, 1); self.assertTrue(made[0].closed)
        self.assertEqual(len(d.detect(None, 99, 0.7).pose_landmarks), 3)       # new tracker follows all three
        self.assertEqual(d.last_search_counts, (1, 2))

    def test_solo_never_searches_and_group_switch_rebuilds(self):
        log = []
        d = GroupPoseDetector(lambda: FakeLandmarker(lambda: [(.5, .5)], log, 'v'), lambda: FakeLandmarker(lambda: [], log, 'i'), group=False)
        for t in range(5): d.detect(None, t * 33, t * .3)
        self.assertFalse(any(kind == 'image' for kind, _ in log))
        d.set_group(True); self.assertEqual(d.rebuilds, 1)
        d.detect(None, 200, 5.0); self.assertTrue(any(kind == 'image' for kind, _ in log))
        d.set_group(False); self.assertIsNone(d.image)

    def test_low_quality_bodies_do_not_trigger_rebuilds(self):
        log = []
        video = FakeLandmarker(lambda: [(.5, .5)], log, 'v')
        ghost = [SimpleNamespace(x=.9, y=.5, z=0., visibility=.1, presence=.1) for _ in range(33)]
        image = SimpleNamespace(detect=lambda img: SimpleNamespace(pose_landmarks=[fake_pose(.5, .5), ghost]), close=lambda: None)
        d = GroupPoseDetector(lambda: video, lambda: image, group=True)
        d.detect(None, 0, 0.0); d.detect(None, 33, 1.0)
        self.assertEqual(d.rebuilds, 0)


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

    def test_ambiguous_assignment_is_rejected(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.4, .5), (.8, .5)])
        self.assertEqual(t.assign([(.3, .5), (.8, .5)]), {})
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
