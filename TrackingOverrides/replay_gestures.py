"""Replay a recording through the pose models and the Diva gesture rules, to tune thresholds on your own footage.

There is no trained action model to retrain: the gestures are rules on MediaPipe landmarks, and every threshold lives
in diva_gestures.json. This tool answers "with these thresholds, when does the rule fire on this video?" so you can
adjust them against real recordings instead of guessing.

    python replay_gestures.py performance.mp4 --people 1
    python replay_gestures.py walk.mp4 --people 3 --slot 1 --labels walk_labels.json
    python replay_gestures.py walk.mp4 --labels walk_labels.json --grid "go_window=0.65,0.8,1.0;go_range=0.12,0.14,0.16"

labels.json marks when each action was really being performed (seconds): {"go": [[2.0, 6.5], [9.0, 12.0]], "shoot": [[14, 16]]}
Output: a per-second timeline (GO / LEFT / RIGHT / SPRAY / DRINK / ULT), and with labels the hit rate inside each
labelled range and the false-fire rate outside it. --grid tries every combination and prints a table.

The neutral pose is taken from the first second of the video (stand upright, arms down) unless --calibrate-at is given.
"""
import argparse
import itertools
import json
import math
import sys
import time
from pathlib import Path

HERE = Path(__file__).parent


def parse_grid(text):
    grid = {}
    for part in filter(None, (p.strip() for p in text.split(';'))):
        key, values = part.split('=')
        grid[key.strip()] = [float(v) for v in values.split(',')]
    keys = list(grid)
    return [dict(zip(keys, combo)) for combo in itertools.product(*(grid[k] for k in keys))]


def run_video(path, people, slot, config_overrides, model, hand_model, calibrate_at, aspect_from_frame=True):
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    from bridge import GroupPoseDetector, PerformerTracker, extract_features
    from diva_gestures import DivaGestureDetector, associate_hands

    gestures = DivaGestureDetector(HERE / 'diva_gestures.json')
    gestures.config.update(config_overrides)
    group = people >= 2

    def pose_options(mode):
        return vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(model)),
            running_mode=vision.RunningMode.IMAGE if mode == 'image' else vision.RunningMode.VIDEO, num_poses=4,
            min_pose_detection_confidence=.35 if group else .5, min_pose_presence_confidence=.35 if group else .5,
            min_tracking_confidence=.4)
    poses = GroupPoseDetector(lambda: vision.PoseLandmarker.create_from_options(pose_options('video')),
                              lambda: vision.PoseLandmarker.create_from_options(pose_options('image')), group=group)
    hands = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(hand_model)), running_mode=vision.RunningMode.VIDEO,
        num_hands=6, min_hand_detection_confidence=.4, min_hand_presence_confidence=.4, min_tracking_confidence=.4))
    tracker = PerformerTracker(people)
    capture = cv2.VideoCapture(str(path))
    if not capture.isOpened():
        raise SystemExit(f'Cannot open {path}')
    fps = capture.get(cv2.CAP_PROP_FPS) or 30.
    timeline = []   # (seconds, dict(go, steer, aim, shoot, drink, ult)) for the chosen slot
    calibrated = False
    frame_index = 0
    p3_slot = 1 if people == 1 else 3
    while True:
        ok, frame = capture.read()
        if not ok:
            break
        seconds = frame_index / fps
        frame_index += 1
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        timestamp = int(seconds * 1000)
        result = poses.detect(image, timestamp, seconds)
        observations = []
        usable = .5 if people == 1 else .4
        for landmarks in result.pose_landmarks:
            center, values, confidence = extract_features(landmarks)
            shoulder_quality = min(min(landmarks[j].visibility, landmarks[j].presence) for j in (11, 12))
            if confidence[4] >= usable or shoulder_quality >= usable:
                observations.append((center, values, confidence, landmarks))
        assignments = tracker.assign([o[0] for o in observations])
        bodies = {s: observations[i][3] for s, i in assignments.items()}
        gestures.retain(assignments)
        aspect = frame.shape[1] / frame.shape[0]
        matched = {}
        if p3_slot in bodies:
            hand_result = hands.detect_for_video(image, timestamp)
            rows = [dict(landmarks=h) for h in hand_result.hand_landmarks]
            all_bodies = {f'observed-{i}': row[3] for i, row in enumerate(observations)}
            associated = associate_hands(rows, all_bodies, gestures.config, aspect)
            for s, i in assignments.items():
                matched[s] = associated.get(f'observed-{i}', []) if s == p3_slot else []
        out = None
        for s in assignments:
            g = gestures.update(s, bodies[s], seconds, matched.get(s, []), aspect)
            if s == slot:
                out = g
        if not calibrated and seconds >= calibrate_at and slot in assignments:
            gestures.calibrate(); calibrated = True
        timeline.append((seconds, out))
    capture.release(); poses.close(); hands.close()
    return timeline, fps


ACTIONS = ('go', 'steer_left', 'steer_right', 'shoot', 'drink', 'ult')


def flags(g):
    if g is None:
        return set()
    out = set()
    if g['go']: out.add('go')
    if g['steer'] < 0: out.add('steer_left')
    if g['steer'] > 0: out.add('steer_right')
    if g['shoot']: out.add('shoot')
    if g['drink']: out.add('drink')
    if g['ult']: out.add('ult')
    return out


def score(timeline, labels):
    """Per action: fraction of labelled frames where it fired (hit) and of unlabelled frames where it fired (false)."""
    report = {}
    for action in ACTIONS:
        ranges = labels.get(action, [])
        inside = [any(a <= t <= b for a, b in ranges) for t, _ in timeline]
        fired = [action in flags(g) for _, g in timeline]
        hits = sum(1 for i, f in zip(inside, fired) if i and f); total = sum(inside)
        false = sum(1 for i, f in zip(inside, fired) if not i and f); outside = len(inside) - total
        report[action] = (hits / total if total else None, false / outside if outside else None)
    return report


def print_timeline(timeline, fps):
    second = -1; row = []
    for t, g in timeline:
        s = int(t)
        if s != second:
            if row:
                print(f'{second:4d}s  ' + '  '.join(sorted(set(row))) if row else f'{second:4d}s  -')
            second, row = s, []
        row.extend(flags(g) or {'-'})
    if row:
        print(f'{second:4d}s  ' + '  '.join(sorted(set(row))))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('video', type=Path)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=1)
    parser.add_argument('--slot', type=int, default=1, help='Which player\'s rules to report (1 = P1 / solo)')
    parser.add_argument('--labels', type=Path, help='JSON of {action: [[start, end], ...]} in seconds')
    parser.add_argument('--grid', default='', help='e.g. "go_window=0.65,0.8;go_range=0.12,0.16"')
    parser.add_argument('--calibrate-at', type=float, default=1.0, help='Seconds into the video to take the neutral pose')
    parser.add_argument('--model', type=Path, default=HERE / 'pose_landmarker_full.task')
    parser.add_argument('--hand-model', type=Path, default=HERE / 'hand_landmarker.task')
    args = parser.parse_args()
    for m in (args.model, args.hand_model):
        if not m.exists():
            parser.error(f'{m} missing — run bridge.py --download-model once, or pass --model/--hand-model')
    labels = json.loads(args.labels.read_text()) if args.labels else None
    combos = parse_grid(args.grid) if args.grid else [{}]
    for overrides in combos:
        started = time.monotonic()
        timeline, fps = run_video(args.video, args.people, args.slot, overrides, args.model, args.hand_model, args.calibrate_at)
        label = ', '.join(f'{k}={v}' for k, v in overrides.items()) or 'diva_gestures.json as is'
        print(f'\n== {label}  ({len(timeline)} frames, {time.monotonic() - started:.1f} s)')
        if not labels:
            print_timeline(timeline, fps)
            continue
        for action, (hit, false) in score(timeline, labels).items():
            if hit is None and (false is None or false == 0):
                continue
            print(f'  {action:12s} hit {hit * 100 if hit is not None else float("nan"):5.1f}%   false {false * 100 if false is not None else 0:5.1f}%')
    return 0


if __name__ == '__main__':
    sys.exit(main())
