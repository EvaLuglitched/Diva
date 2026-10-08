
## Savannah course import — 2026-10-08

- Starter: https://github.com/kommanderpi/studentstarter, commit 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f.
- Windows; Unity 6000.6.3f1; existing Universal Render Pipeline 17.6.0 retained.
- Imported Assets/SavannahCourse and its original metadata; 50 copied files verified against the downloaded source.
- Added layout 4, seed 20261005, to Assets/DigiPhant/Scenes/DigiPhant.unity with the supplied Inject Course into Open Scene tool.
- Backup: Assets/DigiPhant/Scenes/DigiPhant_BeforeSavannah.unity.
- Existing elephant, camera, controls, movement settings, and tracking files preserved.
- Unity checks passed: SAVANNAH_BUNDLE_OK, SAVANNAH_VALIDATION_OK, SAVANNAH_SCENE_COPY_OK, SAVANNAH_INJECTION_OK.
- Course visible with the elephant in the Game view. Live movement and camera tracking were not tested. Python tracking dependencies remain to be installed.
- Existing travel radius is 15 units; full-course coverage may require 30. No movement boundary was changed. Discuss extending the radius before attempting the whole course.
- Open the same DigiPhant scene to test. The scenery adds no collision, sound, or runtime interaction.

## Live camera controls — 2026-10-08

- Installed an isolated Python 3.14.7 environment in DigiPhantStarter/Tracking/.venv using uv.
- Installed pinned MediaPipe 0.10.35 and OpenCV contrib 4.14.0.94; dependency consistency check passed and all 11 supplied tests passed.
- Opened Unity Play mode, maximized Game view, selected 1 person, Seated / upper body, and Camera input. Webcam index 0 displayed a live preview with P1 detection.
- Original bridge exited immediately on individual failed camera reads. DirectShow did not resolve the dropouts and that backend change was reverted. Adapted Tracking/bridge.py to tolerate dropped frames for up to 10 seconds, then report a prolonged camera failure. Original source saved as Tracking/bridge_before_windows_camera.py.bak.
- Calibration button is visible. User must keep shoulders and both hands in frame, click Set neutral pose (10 seconds), and hold a resting pose. Successful calibration and gesture-driven elephant movement have not yet been verified.
- Unity owns the camera bridge and stops it when Play mode ends. No recording was started.
