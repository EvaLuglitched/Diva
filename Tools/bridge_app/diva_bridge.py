"""Entry point for the packaged camera bridge (Diva release builds).

The team's bridge (TrackingOverrides/bridge.py) is locked and is not changed here: this wrapper runs it unmodified as
`__main__`, so a frozen copy (PyInstaller) behaves exactly like `python bridge.py ...`. The game passes absolute paths
for the models and the gesture config, so nothing depends on where the bridge files ended up inside the bundle.

`diva_bridge --selftest` loads the pose, hand and gesture models from the given folder (default: next to this file)
and runs them once on a blank image, without opening a camera. Release builds are checked with it.
"""
import os
import runpy
import sys


def _here():
    if getattr(sys, 'frozen', False):
        return getattr(sys, '_MEIPASS', os.path.dirname(sys.executable))
    return os.path.dirname(os.path.abspath(__file__))


def selftest(folder):
    import numpy as np
    import cv2  # noqa: F401  (the bridge needs it; make sure it loads)
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    import bridge  # noqa: F401
    import diva_gestures  # noqa: F401

    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.zeros((480, 640, 3), dtype=np.uint8))
    checks = []
    pose = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=os.path.join(folder, 'pose_landmarker_full.task')), num_poses=3))
    checks.append(('pose', len(pose.detect(image).pose_landmarks)))
    hand = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=os.path.join(folder, 'hand_landmarker.task')), num_hands=2))
    checks.append(('hand', len(hand.detect(image).hand_landmarks)))
    gesture_path = os.path.join(folder, 'gesture_recognizer.task')
    if os.path.exists(gesture_path):
        gesture = vision.GestureRecognizer.create_from_options(vision.GestureRecognizerOptions(
            base_options=python.BaseOptions(model_asset_path=gesture_path)))
        checks.append(('gesture', len(gesture.recognize(image).gestures)))
    print('DIVA_BRIDGE_SELFTEST_OK mediapipe %s python %s %s' % (
        mp.__version__, sys.version.split()[0], ' '.join('%s=%d' % c for c in checks)), flush=True)


def main():
    folder = _here()
    if folder not in sys.path:
        sys.path.insert(0, folder)
    if '--selftest' in sys.argv:
        args = [a for a in sys.argv[1:] if a != '--selftest']
        selftest(args[0] if args else folder)
        return
    runpy.run_module('bridge', run_name='__main__', alter_sys=True)


if __name__ == '__main__':
    main()
