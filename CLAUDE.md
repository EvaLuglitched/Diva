# Diva — notes for coding agents (Claude etc.)

## 🔒 Gesture controls are LOCKED — password lock since v2.0

The action detection, the way actions drive the elephant, and the camera / player lock are **password-locked**
(v2.0, 2026-10-13). The 28 locked files are listed in **`Tools/lock/lock.json`**, signed with a key derived from Eva's
password. The spec and the change procedure are in **`GESTURES_LOCKED.md`**. Main locked files:
- `TrackingOverrides/bridge.py`, `TrackingOverrides/diva_gestures.py`, `TrackingOverrides/diva_gestures.json`
- `Assets/DigiPhant/Runtime/DivaControlState.cs`, `DivaDemo.cs`, `DigiPhantController.cs`, `DigiPhantLocomotion.cs`,
  `DigiPhantCameraPreview.cs`, `DigiPhantMacCameras.cs`, `Assets/DivaShow/Runtime/DivaShowCameraCheck.cs`
- their tests and validators, and the lock itself (`Tools/lock/`, `.githooks/`, `.github/`, `DivaLockCheck.cs`)

Rules for agents:
1. **Never modify a locked file**, not even to fix something else, and never "tune" them on your own initiative.
   `python3 Tools/lock/diva_lock.py status` lists them. If a task seems to need one, stop and tell the user.
2. **Never edit `Tools/lock/lock.json`, the pinned key, `diva_lock.py`, `test_password_lock.py`, `DivaLockCheck.cs`,
   the pre-commit hook or the workflow** to make a check pass, and never ask for, type, store or guess the password.
   Only Eva runs `python3 Tools/lock/diva_lock.py sign` herself, in her own terminal.
3. If the user (Eva) asks for a change to a locked part: say it is password-locked, summarise the double-check
   procedure in `GESTURES_LOCKED.md`, work on a branch with a pull request, and ask Eva to re-sign with her password
   after the change and its tests pass. Never push such changes straight to `main`.
4. Never edit `LOCKED` / `LOCKED_LINES` in `TrackingOverrides/test_gesture_lock.py` just to make a failing test pass.
5. After an approved change: all Python tests
   (`DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s TrackingOverrides -p "test_*.py"`), the four Unity
   validators (**DigiPhant > Diva > Validate Startup / Gestures / Stationary Turn / Trial Export**) and **Verify Lock**,
   update `GESTURES_LOCKED.md`, `test_gesture_lock.py` and `docs/EVA_UPDATES.md`, and ask for a live camera test.
6. Enable the commit check once per clone: `git config core.hooksPath .githooks`.

## Other project notes
- Fab rocket / cloud files are not in git (licence); see `docs/FAB_SETUP.md`. Never commit `Assets/DivaGame/ThirdParty/Fab/`.
- Do not commit `ProjectSettings/VersionControlSettings.asset` (Unity keeps switching it) or `.vscode/`.
- The Vision Pro branch `carl/diva-vr` needs a Unity Pro licence; do not merge it into `main`.
