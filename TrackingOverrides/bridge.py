"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import struct
import threading
import types
import time
from contextlib import ExitStack

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'
LITE_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task'
HAND_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task'
GESTURE_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task'

class CameraRecovery:
    """Bounded recovery of intermittent reads without changing camera backend."""
    def __init__(self,camera,reopen,now,timeout=3,max_reopens=3,stable_seconds=20):
        self.camera=camera;self.reopen=reopen;self.reopens=0
        self.last_frame=now;self.healthy_since=None
        self.timeout=timeout;self.max_reopens=max_reopens;self.stable_seconds=stable_seconds
    def read(self,now):
        ok,frame=self.camera.read()
        if ok:
            self.last_frame=now
            if self.healthy_since is None:self.healthy_since=now
            if now-self.healthy_since>=self.stable_seconds:self.reopens=0
            return True,frame,False
        self.healthy_since=None
        if now-self.last_frame<=self.timeout:return False,None,False
        if self.max_reopens is not None and self.reopens>=self.max_reopens:
            raise RuntimeError(f'Camera stopped delivering frames after {self.max_reopens} reconnect attempts. Restart Camera in Unity.')
        self.camera.release()
        self.camera=self.reopen()
        self.reopens+=1
        self.last_frame=now
        return False,None,True


def extract_features(landmarks, upper_body_only=False):
    """Body-relative image measurements; Unity subtracts the calibrated neutral pose."""
    def xy(i):
        p = landmarks[i]
        return p.x, p.y
    def quality(indices):
        return min(min(landmarks[i].visibility, landmarks[i].presence) for i in indices)
    if upper_body_only:
        ls, rs = xy(11), xy(12)
        shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
        scale = max(math.dist(ls, rs), .03)
        left = (shoulder[1] - xy(15)[1]) / scale
        right = (shoulder[1] - xy(16)[1]) / scale
        # Shoulder tilt replaces hip-relative lean; leg channels use hand heights.
        raw = [left, right, left, right, (ls[1] - rs[1]) / scale,
               abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
        groups = [[11, 12, 15], [11, 12, 16], [11, 12, 15],
                  [11, 12, 16], [11, 12], [11, 12, 15, 16]]
        return shoulder, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]
    ls, rs, lh, rh = (xy(i) for i in (11, 12, 23, 24))
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    scale = max(math.dist(shoulder, hip), .03)
    base = [11, 12, 23, 24]
    groups = [base + [15], base + [16], base + [27], base + [28], base, base + [15, 16]]
    raw = [(shoulder[1] - xy(15)[1]) / scale,
           (shoulder[1] - xy(16)[1]) / scale,
           (hip[1] - xy(27)[1]) / scale,
           (hip[1] - xy(28)[1]) / scale,
           (shoulder[0] - hip[0]) / scale,
           abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
    return hip, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]


class OneEuro:
    """One-Euro filter (Casiez 2012): heavy smoothing when still, light smoothing when moving. One per channel."""
    __slots__ = ('min_cutoff', 'beta', 'd_cutoff', 'x', 'dx', 't')
    def __init__(self, min_cutoff=1.5, beta=.3, d_cutoff=1.0):   # beta .3: a 2 Hz arm pump keeps ~80% of its amplitude (GO rule), stillness is filtered at 1.5 Hz
        self.min_cutoff, self.beta, self.d_cutoff = min_cutoff, beta, d_cutoff
        self.x = self.dx = None; self.t = None
    @staticmethod
    def alpha(cutoff, dt):
        tau = 1 / (2 * math.pi * cutoff); return 1 / (1 + tau / dt)
    def __call__(self, value, t):
        if self.x is None or self.t is None or t <= self.t:
            self.x, self.dx, self.t = value, 0., t; return value
        dt = t - self.t; self.t = t
        dx = (value - self.x) / dt
        a_d = self.alpha(self.d_cutoff, dt); self.dx = a_d * dx + (1 - a_d) * self.dx
        cutoff = self.min_cutoff + self.beta * abs(self.dx)
        a = self.alpha(cutoff, dt); self.x = a * value + (1 - a) * self.x
        return self.x


class LandmarkSmoother:
    """Per-player One-Euro smoothing of the 33 pose landmarks. Per-frame (IMAGE-mode) detection has no temporal
    filter of its own, so without this the skeleton and the gesture rules see frame-to-frame jitter."""
    def __init__(self, min_cutoff=1.5, beta=.3):
        self.min_cutoff, self.beta = min_cutoff, beta; self.filters = {}
    def reset(self, slot=None):
        if slot is None: self.filters.clear()
        else: self.filters.pop(slot, None)
    def retain(self, slots):
        for slot in list(self.filters):
            if slot not in slots: del self.filters[slot]
    def apply(self, slot, landmarks, t):
        bank = self.filters.setdefault(slot, [[OneEuro(self.min_cutoff, self.beta) for _ in range(3)] for _ in range(33)])
        if len(landmarks) != 33:
            return landmarks
        out = []
        for i, l in enumerate(landmarks):
            fx, fy, fz = bank[i]
            out.append(types.SimpleNamespace(x=fx(l.x, t), y=fy(l.y, t), z=fz(l.z, t), visibility=l.visibility, presence=l.presence))
        return out


def performance_profile(name='auto'):
    """'quality' on Apple Silicon (full pose model, 960 px, hands every frame), 'fast' elsewhere (lite, 640 px,
    hands every other frame). Both run group detection in the background thread."""
    if name == 'auto':
        import platform
        chip = ''
        try:
            import subprocess
            chip = subprocess.run(['sysctl', '-n', 'machdep.cpu.brand_string'], capture_output=True, text=True, timeout=2).stdout
        except Exception:
            pass
        name = 'quality' if (platform.machine() == 'arm64' and 'Apple' in chip) else 'fast'
    if name == 'quality':
        return dict(name='quality', lite=False, max_width=960, hands_every=1)
    return dict(name='fast', lite=True, max_width=640, hands_every=2)


class GroupPoseDetector:
    """Pose detection policy.

    Solo: a VIDEO-mode landmarker on the main loop (detects once, then tracks: cheap and smooth).
    Group (2+ people): an IMAGE-mode landmarker (the lite model when available) runs the full-frame detector on a
    downscaled copy of every frame it can keep up with, in its own worker thread. The camera loop never waits for it:
    it hands over the newest frame and carries on with the newest finished result, so sending to Unity, the preview
    and the hand model keep their own pace while detection runs at whatever rate the machine manages. MediaPipe's
    video tracking kept only the first body it found, and rebuilding it to catch late arrivals reloaded the model
    over and over (laggy); per-frame detection finds everyone in the picture, and the thread keeps it from stalling."""
    def __init__(self, make_video, make_image, group, max_width=640, threaded=True):
        self.make_video, self.make_image = make_video, make_image
        self.group = group; self.max_width = max_width; self.threaded = threaded
        self.video = None if group else make_video(); self.image = make_image() if group else None
        self.latest = None; self.last_cost = 0.0; self.detections = 0; self.dropped = 0
        self._pending = None; self._lock = threading.Condition(); self._worker = None; self._stop = False
        if group and threaded:
            self._start_worker()

    def _start_worker(self):
        self._stop = False
        self._worker = threading.Thread(target=self._run, name='pose-detect', daemon=True); self._worker.start()

    def _run(self):
        while True:
            with self._lock:
                while self._pending is None and not self._stop:
                    self._lock.wait()
                if self._stop:
                    return
                frame, self._pending = self._pending, None
            self._detect_now(frame)

    def _detect_now(self, rgb):
        started = time.monotonic()
        result = self.image.detect(self._mp_image(self._downscale(rgb)))
        with self._lock:
            self.latest = result; self.last_cost = time.monotonic() - started; self.detections += 1

    def _downscale(self, rgb):
        h, w = rgb.shape[:2]
        if w <= self.max_width:
            return rgb
        import cv2
        scale = self.max_width / w
        return cv2.resize(rgb, (self.max_width, max(1, round(h * scale))), interpolation=cv2.INTER_AREA)

    @staticmethod
    def _mp_image(rgb):
        import mediapipe as mp
        return mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

    def set_group(self, group):
        if group == self.group:
            return
        self.group = group
        if group:
            if self.video is not None: self.video.close(); self.video = None
            if self.image is None: self.image = self.make_image()
            if self.threaded and (self._worker is None or not self._worker.is_alive()): self._start_worker()
        else:
            self._stop_worker()
            if self.image is not None: self.image.close(); self.image = None
            if self.video is None: self.video = self.make_video()
        self.latest = None

    def _stop_worker(self):
        if self._worker is None:
            return
        with self._lock:
            self._stop = True; self._pending = None; self._lock.notify_all()
        self._worker.join(timeout=2); self._worker = None

    def close(self):
        self._stop_worker()
        if self.video is not None: self.video.close()
        if self.image is not None: self.image.close()

    def detect(self, rgb, timestamp_ms):
        """Landmarks for this frame (solo: tracked now; group: the newest finished detection)."""
        if not self.group:
            return self.video.detect_for_video(self._mp_image(rgb), timestamp_ms)
        if not self.threaded:
            self._detect_now(rgb)
            return self.latest
        with self._lock:
            if self._pending is not None:
                self.dropped += 1          # the worker is still busy: this frame is superseded by the newer one
            self._pending = rgb
            self._lock.notify()
            if self.latest is not None:
                return self.latest
        # Very first frame: wait briefly for a result so Unity gets bodies right away.
        deadline = time.monotonic() + 1.0
        while time.monotonic() < deadline:
            with self._lock:
                if self.latest is not None:
                    return self.latest
            time.sleep(.005)
        return types.SimpleNamespace(pose_landmarks=[])


class PerformerTracker:
    """Small-group nearest-position assignment with ambiguity rejection.

    This is not biometric identity. Crossings/long occlusions may need manual reset.
    """
    def __init__(self, count, max_distance=.20):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.max_distance = max_distance
        self.positions = None
        # Before the group is complete, people are assigned as they arrive (left to right) and roles may
        # re-sort when someone new steps in. Once every slot is filled, or Unity saves the neutral pose,
        # roles lock and only nearest-position tracking moves them.
        self.locked = False

    def reset(self):
        self.positions = None
        self.locked = False

    def lock(self):
        if self.positions is not None and all(p is not None for p in self.positions):
            self.locked = True

    @property
    def filled(self):
        return 0 if self.positions is None else sum(1 for p in self.positions if p is not None)

    def assign(self, centers):
        if not self.locked:
            # Gather everyone: already-known positions matched to their nearest current body, plus unknown bodies.
            known = []
            if self.positions is not None:
                taken = set()
                for p in self.positions:
                    if p is None:
                        continue
                    best = min(((math.dist(p, centers[i]), i) for i in range(len(centers)) if i not in taken), default=None)
                    if best is not None and best[0] <= self.max_distance:
                        known.append(best[1]); taken.add(best[1])
                unknown = [i for i in range(len(centers)) if i not in taken]
                present = known + unknown
            else:
                present = list(range(len(centers)))
            order = sorted(present, key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order] + [None] * (self.count - len(order))
            if len(order) >= self.count:
                self.locked = True
            return {slot + 1: index for slot, index in enumerate(order)}
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[s], centers[i]) for s, i in enumerate(assignment) if i >= 0]
            if any(d > self.max_distance for d in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        # Avoid arbitrary role swaps when two assignments are almost equally plausible: pause only the players whose
        # role differs between the near-tied options (e.g. two people standing close), the others keep playing.
        ties = [c for c in candidates[1:] if c[0] == best[0] and c[1] - best[1] < .015]
        uncertain = {s for c in ties for s in range(self.count) if c[2][s] != best[2][s]}
        result = {s + 1: i for s, i in enumerate(best[2]) if i >= 0 and s not in uncertain}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
        return result


def requested_count(data):
    """Validate Unity's local count request without changing tracking on bad input."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and 1 <= count <= 4:
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def encode_preview(frame, cv2):
    """Fit one low-latency JPEG into a local UDP datagram, preserving aspect ratio."""
    height, width = frame.shape[:2]
    scale = min(320 / width, 240 / height)
    preview = cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))))
    # Stay below macOS's commonly configured 9216-byte UDP send limit.
    # JPEG size depends on scene detail, so reduce quality and then dimensions.
    for _ in range(4):
        for quality in (65, 45, 25):
            ok, encoded = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(encoded) <= 8000:
                return encoded.tobytes()
        h, w = preview.shape[:2]
        preview = cv2.resize(preview, (max(1, w // 2), max(1, h // 2)))
    return None


PREVIEW_MAGIC = b'DVP1'
PREVIEW_CHUNK = 8000   # payload per datagram: under macOS's 9216-byte UDP limit with the 12-byte header


def encode_preview_hd(frame, cv2, max_width=960, max_height=540, quality=72):
    """A sharp preview for Unity's camera check (sent split over several datagrams, see preview_datagrams)."""
    height, width = frame.shape[:2]
    scale = min(1., max_width / width, max_height / height)
    preview = frame if scale >= 1 else cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))),
                                                  interpolation=cv2.INTER_AREA)
    ok, encoded = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, quality])
    return encoded.tobytes() if ok else None


def preview_datagrams(jpeg, frame_id, chunk=PREVIEW_CHUNK):
    """Split one JPEG into datagrams: 'DVP1' + frame id (uint32) + index (uint16) + count (uint16) + bytes.
    Unity reassembles a frame once every piece has arrived and drops incomplete ones."""
    count = max(1, -(-len(jpeg) // chunk))
    if count > 0xFFFF:
        return []
    return [PREVIEW_MAGIC + struct.pack('<IHH', frame_id & 0xFFFFFFFF, i, count) + jpeg[i * chunk:(i + 1) * chunk]
            for i in range(count)]


GO_LOG_FIELDS = ('wall', 't', 'loop_dt', 'bodies', 'assigned', 'slot', 'go', 'reason', 'left_h', 'right_h', 'left_range',
                 'right_range', 'since_motion', 'frame_gap', 'wrist_q', 'lost_for', 'shoulder_q')


class GoLog:
    """Diagnostics for walking: one CSV row per frame for P1 (the walking player) with why GO was on or off.
    Files go to Tracking/go_debug/go_<time>.csv; the newest 10 are kept. Summarise with go_debug_report.py."""
    def __init__(self, folder, keep=10):
        import csv
        from datetime import datetime
        folder.mkdir(parents=True, exist_ok=True)
        for old in sorted(folder.glob('go_*.csv'))[:-(keep - 1) or None]:
            try: old.unlink()
            except OSError: pass
        self.path = folder / f'go_{datetime.now():%Y%m%d_%H%M%S}.csv'
        self.file = open(self.path, 'w', newline='', encoding='utf-8')
        self.writer = csv.DictWriter(self.file, fieldnames=GO_LOG_FIELDS, extrasaction='ignore')
        self.writer.writeheader()
        self.last_flush = time.monotonic(); self.last_loop = None

    def write(self, t, bodies, assigned, slot, go, info):
        now = time.monotonic()
        loop_dt = 0. if self.last_loop is None else now - self.last_loop
        self.last_loop = now
        row = dict(wall=round(time.time(), 3), t=round(t, 3), loop_dt=round(loop_dt, 3), bodies=bodies, assigned=assigned,
                   slot=slot, go=go, **(info or {'reason': 'not-assigned'}))
        self.writer.writerow(row)
        if now - self.last_flush > 1:
            self.file.flush(); self.last_flush = now

    def close(self):
        try: self.file.close()
        except OSError: pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=3)
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--upper-body-only', action='store_true', help='Use shoulders and hands; feet and hips may be out of frame')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'pose_landmarker_full.task')
    parser.add_argument('--download-model', action='store_true', help='Download the official model if missing')
    parser.add_argument('--diva', action='store_true', help='Enable Diva GO/body-lean steer/open-palm shoot/thumbs-up refill gesture rules')
    parser.add_argument('--diva-config', type=Path, default=Path(__file__).parent / 'diva_gestures.json')
    parser.add_argument('--hand-model', type=Path, default=Path(__file__).parent / 'hand_landmarker.task')
    parser.add_argument('--no-go-log', action='store_true', help='Do not write the walking (GO) diagnostics CSV in go_debug/')
    parser.add_argument('--profile', choices=('auto', 'quality', 'fast'), default='auto',
                        help='quality: full pose model, 960 px, hands every frame (Apple Silicon default); fast: lite model, 640 px')
    parser.add_argument('--gesture-model', type=Path, default=Path(__file__).parent / 'gesture_recognizer.task',
                        help="MediaPipe Gesture Recognizer: hands plus pretrained 'Thumb_Up' for the refill gesture")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65533:
        parser.error('--port must be between 1024 and 65533')
    if not args.model.exists():
        if not args.download_model:
            parser.error('Model missing. Run again with --download-model (internet required).')
        import urllib.request
        args.model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.model.with_suffix('.download')
        print('Downloading the official MediaPipe pose model...')
        urllib.request.urlretrieve(MODEL_URL, temporary)
        temporary.replace(args.model)
    if args.diva and not args.hand_model.exists():
        if not args.download_model:
            parser.error('Hand model missing. Run with --download-model (internet required).')
        import urllib.request
        args.hand_model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.hand_model.with_suffix('.download')
        urllib.request.urlretrieve(HAND_MODEL_URL, temporary)
        temporary.replace(args.hand_model)
    if args.diva and not args.gesture_model.exists() and args.download_model:
        # Optional: without it the hand landmarker and the geometric thumbs-up rule are used, as before.
        try:
            import urllib.request
            temporary = args.gesture_model.with_suffix('.download')
            print('Downloading the official MediaPipe gesture recognizer model...', flush=True)
            urllib.request.urlretrieve(GESTURE_MODEL_URL, temporary)
            temporary.replace(args.gesture_model)
        except OSError as error:
            print(f'Gesture recognizer download failed ({error}); using the hand landmarker.', flush=True)
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    profile = performance_profile(args.profile)
    print(f'Performance profile: {profile["name"]} ({"lite" if profile["lite"] else "full"} pose model, {profile["max_width"]} px, hands every {profile["hands_every"]} frame(s))', flush=True)
    def group_model():
        if not profile['lite']:
            return args.model
        # The lite model is 2-3x cheaper and plenty for three bodies at 640 px; fetch it once, fall back to full.
        lite = args.model.parent / 'pose_landmarker_lite.task'
        if not lite.exists():
            try:
                import urllib.request
                temporary = lite.with_suffix('.download')
                print('Downloading the MediaPipe lite pose model for group detection...', flush=True)
                urllib.request.urlretrieve(LITE_MODEL_URL, temporary)
                temporary.replace(lite)
            except Exception as e:   # offline: the full model works too, just slower
                print(f'Lite pose model not available ({e}); using the full model.', flush=True)
                return args.model
        return lite
    def pose_options(people, mode):
        # VIDEO mode tracks between detections (smooth, cheap) but tends to keep only the first body it found;
        # for a group, GroupPoseDetector runs IMAGE-mode detection in a worker thread (see the class).
        group = people >= 2
        return vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(group_model() if mode == 'image' else args.model)),
            running_mode=vision.RunningMode.IMAGE if mode == 'image' else vision.RunningMode.VIDEO, num_poses=4,
            min_pose_detection_confidence=.35 if group else .5, min_pose_presence_confidence=.35 if group else .5,
            min_tracking_confidence=.4)
    tracker = PerformerTracker(args.people)
    last_seen, last_seen_at = None, 0.0
    hand_frame, last_hand_rows = 0, None
    gestures = None
    if args.diva:
        from diva_gestures import DivaGestureDetector, associate_hands
        gestures = DivaGestureDetector(args.diva_config)
    camera = cv2.VideoCapture(args.camera)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    # Reopen the backend OpenCV already selected. Do not switch to DirectShow.
    selected_backend=int(camera.get(cv2.CAP_PROP_BACKEND))
    reopen_camera=lambda: cv2.VideoCapture(args.camera,selected_backend) if selected_backend>0 else cv2.VideoCapture(args.camera)
    # Keep trying instead of exiting: on macOS the first frames can take several seconds (camera warm-up, the
    # permission prompt, another app such as FaceTime/Zoom holding the camera). Exiting after three quick
    # attempts left Unity with "Camera stopped" and no way to calibrate or START until Retry.
    recovery=CameraRecovery(camera,reopen_camera,time.monotonic(),timeout=4,max_reopens=None)
    needs_diva_neutral=False
    camera_gap=False
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
        try:
            sender.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 1 << 21)   # room for a split HD preview frame
        except OSError:
            pass
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close its preview with Q first.')
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15),
             (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
             (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
             (0, 7), (0, 8), (7, 11), (8, 12)]
    # Every recognised (assigned) player is drawn green, unassigned bodies grey; the P1/P2/P3 label shows the role.
    colors = [(100, 230, 100)] * 4
    previous_timestamp = -1
    last_preview = 0.0
    preview_id = 0
    print('Stand side by side, all visible. IDs start left-to-right as seen in the (mirrored) preview.')
    print('R: reset identities (then recalibrate in Unity). Q: quit.')
    go_log = None
    if args.diva and not args.no_go_log:
        try:
            go_log = GoLog(Path(__file__).parent / 'go_debug')
            print(f'Walking diagnostics: {go_log.path.name} (summary: python go_debug_report.py)', flush=True)
        except OSError as error:
            print(f'Walking diagnostics off: {error}', flush=True)
    try:
        with ExitStack() as stack:
            poses = GroupPoseDetector(lambda: vision.PoseLandmarker.create_from_options(pose_options(args.people, 'video')),
                                      lambda: vision.PoseLandmarker.create_from_options(pose_options(args.people, 'image')),
                                      group=args.people >= 2, max_width=profile['max_width'])
            smoother = LandmarkSmoother()
            stack.callback(poses.close)
            hand_detector = None
            gesture_recognizer = None
            if args.diva and args.gesture_model.exists():
                # Gesture Recognizer = hand landmarks + pretrained gestures; its 'Thumb_Up' drives the refill.
                gesture_options = vision.GestureRecognizerOptions(
                    base_options=python.BaseOptions(model_asset_path=str(args.gesture_model)),
                    running_mode=vision.RunningMode.VIDEO, num_hands=6,
                    min_hand_detection_confidence=.4, min_hand_presence_confidence=.4,
                    min_tracking_confidence=.4)
                gesture_recognizer = stack.enter_context(vision.GestureRecognizer.create_from_options(gesture_options))
                print('Hands: MediaPipe Gesture Recognizer (pretrained Thumb_Up).', flush=True)
            elif args.diva:
                hand_options = vision.HandLandmarkerOptions(
                    base_options=python.BaseOptions(model_asset_path=str(args.hand_model)),
                    running_mode=vision.RunningMode.VIDEO, num_hands=6,
                    min_hand_detection_confidence=.4, min_hand_presence_confidence=.4,
                    min_tracking_confidence=.4)
                hand_detector = stack.enter_context(vision.HandLandmarker.create_from_options(hand_options))
            while True:
                for _ in range(32):
                    try:
                        data, address = sender.recvfrom(1024)
                    except BlockingIOError:
                        break
                    if address == ('127.0.0.1', args.port):
                        try:
                            command = json.loads(data)
                            if isinstance(command, dict) and command.get('version') == 1:
                                if gestures and command.get('divaCalibrate') is True:
                                    gestures.calibrate()
                                    tracker.lock()
                                    needs_diva_neutral=False
                                    print('Diva neutral stance saved. Return upright to stop turning.')
                                upper = command.get('upperBodyOnly')
                                if type(upper) is bool and upper != args.upper_body_only:
                                    args.upper_body_only = upper
                                    tracker.reset()
                                    if gestures: gestures.reset()
                                    print('Movement mode changed. Set neutral pose in Unity.')
                                if command.get('reset') is True:
                                    tracker.reset()
                                    if gestures: gestures.reset()
                        except (ValueError, UnicodeError):
                            pass
                    count = requested_count(data) if address == ('127.0.0.1', args.port) else None
                    if count is not None and count != args.people:
                        group_before = args.people >= 2
                        args.people = count
                        tracker = PerformerTracker(count)
                        smoother.reset()
                        if gestures: gestures.reset()
                        if (count >= 2) != group_before:
                            poses.set_group(count >= 2)
                            print(f'Pose detector switched to {"per-frame group detection" if count >= 2 else "single-person tracking"}.', flush=True)
                        print(f'Unity selected {count} people. Reassigning; set neutral pose in Unity.')
                ok, frame, reopened = recovery.read(time.monotonic())
                camera = recovery.camera
                if not ok:
                    if not camera_gap or reopened:
                        empty={'version':1,'performerCount':args.people,'upperBodyOnly':args.upper_body_only,'people':[]}
                        if gestures:empty['divaGestures']=True
                        sender.sendto(json.dumps(empty).encode(),('127.0.0.1',args.port))
                        if gestures:gestures.retain([])
                    camera_gap=True
                    if reopened:
                        if gestures:
                            tracker.reset()
                            gestures.reset()
                            needs_diva_neutral=True
                        print(f'Camera reconnected (attempt {recovery.reopens}). If it stays dark: close other apps using the camera '
                              f'(FaceTime, Zoom, Photo Booth) and allow camera access. Then save neutral again in Unity.',flush=True)
                    time.sleep(.05)
                    continue
                camera_gap=False
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
                previous_timestamp = timestamp
                result = poses.detect(rgb, timestamp)
                observations = []
                usable_quality = .5 if args.people == 1 else .4
                for landmarks in result.pose_landmarks:
                    center, values, confidence = extract_features(landmarks, args.upper_body_only)
                    shoulder_quality = min(min(landmarks[j].visibility, landmarks[j].presence) for j in (11, 12))
                    if confidence[4] >= usable_quality or (gestures and shoulder_quality >= usable_quality):
                        observations.append((center, values, confidence, landmarks))
                # Roles go left to right as the players see themselves in the MIRRORED preview (P1 = the player on
                # the left of the picture), so they match the P1 · P2 · P3 order under Unity's camera check.
                assignments = tracker.assign([(1 - o[0][0], o[0][1]) for o in observations])
                # Which detected body is which player, keyed by the RAW landmarks the preview draws (smoothing below
                # replaces each assigned player's landmarks with a new object).
                raw_slots = {id(observations[i][3]): (slot, i) for slot, i in assignments.items()}
                if args.people >= 2:
                    # Per-frame detection jitters; smooth each assigned player's landmarks and recompute their features.
                    smoother.retain(assignments)
                    for slot, i in assignments.items():
                        center, values, confidence, landmarks = observations[i]
                        smoothed = smoother.apply(slot, landmarks, timestamp / 1000.)
                        c2, v2, q2 = extract_features(smoothed, args.upper_body_only)
                        observations[i] = (center, v2, q2, smoothed)
                # Diagnostics for the group: how many bodies the model sees, how many are good enough, how many have a role.
                seen = (len(result.pose_landmarks), len(observations), len(assignments), tracker.locked, poses.dropped // 100)
                if seen != last_seen and time.monotonic() - last_seen_at > 1:
                    last_seen, last_seen_at = seen, time.monotonic()
                    print(f'Camera sees {seen[0]} bodies, {seen[1]} usable, {seen[2]}/{args.people} assigned' + (' (roles locked)' if seen[3] else '')
                          + (f', pose {poses.last_cost * 1000:.0f} ms/detection in the background, {poses.detections} done' if poses.group else ''), flush=True)
                packet = {'version': 1, 'performerCount': args.people, 'upperBodyOnly': args.upper_body_only,
                          'detected': len(result.pose_landmarks), 'usable': len(observations), 'rolesLocked': tracker.locked,
                          'people': [dict(slot=slot, values=observations[i][1], confidence=observations[i][2])
                                                 for slot, i in assignments.items()]}
                if gestures:
                    packet['divaGestures'] = True
                    gestures.retain(assignments)
                    bodies = {slot: observations[index][3] for slot, index in assignments.items()}
                    p3_slot = 1 if args.people == 1 else 3
                    # Detect hands only while P3 is assigned. Match to ALL observed
                    # bodies first so another player's palm cannot fire P3's gun.
                    matched_hands = {}
                    hand_frame += 1
                    if p3_slot in bodies and (args.people == 1 or hand_frame % profile['hands_every'] == 0 or last_hand_rows is None):
                        hand_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                        if gesture_recognizer is not None:
                            hand_result = gesture_recognizer.recognize_for_video(hand_image, timestamp)
                            hand_rows = [dict(landmarks=h, gesture=g[0].category_name if g else None,
                                              gesture_score=float(g[0].score) if g else 0.)
                                         for h, g in zip(hand_result.hand_landmarks, hand_result.gestures)]
                        else:
                            hand_result = hand_detector.detect_for_video(hand_image, timestamp)
                            hand_rows = [dict(landmarks=h) for h in hand_result.hand_landmarks]
                        last_hand_rows = hand_rows
                    elif p3_slot in bodies:
                        hand_rows = last_hand_rows   # group mode, skipped frame: last hands (cheap, 15 fps is plenty for spray / refill)
                    if p3_slot in bodies:
                        all_bodies = {f'observed-{i}': row[3] for i, row in enumerate(observations)}
                        associated = associate_hands(hand_rows, all_bodies, gestures.config, frame.shape[1] / frame.shape[0])
                        for slot, index in assignments.items():
                            matched_hands[slot] = associated.get(f'observed-{index}', []) if slot == p3_slot else []
                    for person in packet['people']:
                        slot = person['slot']
                        person['gestures'] = gestures.update(slot, bodies[slot], timestamp / 1000., matched_hands.get(slot, []), frame.shape[1] / frame.shape[0])
                        if needs_diva_neutral:
                            person['gestures'].update(go=0.,steer=0.,aim=0.,shoot=False,drink=False,ult=False)
                    if go_log is not None:
                        # P1 walks in every mode (solo: the one player is slot 1).
                        walker = next((p for p in packet['people'] if p['slot'] == 1), None)
                        go_log.write(timestamp / 1000., len(result.pose_landmarks), len(assignments), 1,
                                     walker['gestures']['go'] if walker else 0.,
                                     gestures.go_debug.get(1) if walker else None)
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))
                h, w = frame.shape[:2]
                # The preview is a mirror (players see themselves like in a mirror); gestures above use the raw image.
                frame = cv2.flip(frame, 1)
                # Draw every detection, even while waiting for the full group or
                # rejecting an ambiguous identity. Only assigned bodies drive Unity.
                thickness = max(2, round(max(w / 320, h / 240) * 2))
                radius = max(3, round(thickness * 1.5))
                for landmarks in result.pose_landmarks:
                    hit = raw_slots.get(id(landmarks))
                    slot = hit[0] if hit else None
                    if hit:
                        landmarks = observations[hit[1]][3]   # assigned: draw the (smoothed) skeleton the rules use
                    color = colors[slot - 1] if slot is not None else (180, 180, 180)
                    points = {}
                    for index, landmark in enumerate(landmarks):
                        if (min(landmark.visibility, landmark.presence) >= .5
                                and math.isfinite(landmark.x) and math.isfinite(landmark.y)
                                and 0 <= landmark.x <= 1 and 0 <= landmark.y <= 1):
                            points[index] = (min(w - 1, int((1 - landmark.x) * w)),
                                             min(h - 1, int(landmark.y * h)))
                    for a, b in edges:
                        if a in points and b in points:
                            cv2.line(frame, points[a], points[b], (20, 20, 20), thickness + 2, cv2.LINE_AA)
                            cv2.line(frame, points[a], points[b], color, thickness, cv2.LINE_AA)
                    for point in points.values():
                        cv2.circle(frame, point, radius + 1, (20, 20, 20), -1, cv2.LINE_AA)
                        cv2.circle(frame, point, radius, color, -1, cv2.LINE_AA)
                    anchor = points.get(11, next(iter(points.values()), None))
                    if anchor is not None:
                        label = f'P{slot}' if slot is not None else 'Unassigned'
                        if gestures and slot is not None:
                            g = next(p['gestures'] for p in packet['people'] if p['slot'] == slot)
                            if args.people == 1:
                                state = 'SPRAY' if g['shoot'] else 'DRINK' if g['drink'] else 'GO' if g['go'] else 'LEFT' if g['steer'] < 0 else 'RIGHT' if g['steer'] > 0 else 'IDLE'
                            elif slot == 1:
                                state = 'GO' if g['go'] else 'STOP'
                            elif slot == 2:
                                state = 'LEFT' if g['steer'] < 0 else 'RIGHT' if g['steer'] > 0 else 'STRAIGHT'
                            else:
                                state = 'SPRAY' if g['shoot'] else 'DRINK' if g['drink'] else 'AIM LEFT' if g['aim'] < 0 else 'AIM RIGHT' if g['aim'] > 0 else 'IDLE'
                            label += f' {state}'
                        font_scale = max(.6, thickness * .3)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    color, thickness, cv2.LINE_AA)
                message = f'{len(assignments)}/{args.people} assigned' if args.no_window else f'{len(assignments)}/{args.people} assigned | R: reassign | Q: quit'
                if needs_diva_neutral and assignments:
                    message='Camera recovered: save neutral in Unity'
                if tracker.positions is None:
                    message = f'Waiting for {args.people} visible people' if args.no_window else f'Stand side by side: waiting for {args.people} visible people | Q: quit'
                cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                now = time.monotonic()
                if now - last_preview >= 1 / 15:
                    # HD preview split across datagrams (Unity reassembles); about 960x540 at 15 fps instead of
                    # the old single-datagram 320x240 at low quality, which looked blurry on the large camera check.
                    jpeg = encode_preview_hd(frame, cv2)
                    if jpeg is not None:
                        preview_id += 1
                        for datagram in preview_datagrams(jpeg, preview_id):
                            try:
                                sender.sendto(datagram, ('127.0.0.1', args.port + 2))
                            except OSError as error:
                                # A dropped preview must not stop performer tracking (Unity skips the incomplete frame).
                                if error.errno not in (errno.EMSGSIZE, errno.EAGAIN, errno.EWOULDBLOCK, errno.ENOBUFS):
                                    raise
                                break
                    last_preview = now
                if not args.no_window:
                    cv2.imshow('DigiPhant tracking (mirrored)', frame)
                key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
                if key == ord('q'):
                    break
                if key == ord('r'):
                    tracker.reset()
                    if gestures: gestures.reset()
    finally:
        if go_log is not None: go_log.close()
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
