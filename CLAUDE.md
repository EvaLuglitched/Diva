# Diva — notes for coding agents (Claude etc.)

## 🔒 Gesture controls are LOCKED

The single-player and three-player gesture controls (rules, thresholds, role assignment) were finalised on
2026-10-12 and are **locked**. The spec and the change procedure are in **`GESTURES_LOCKED.md`**.

Locked files:
- `TrackingOverrides/diva_gestures.py`, `TrackingOverrides/diva_gestures.json`
- `TrackingOverrides/bridge.py` (role assignment, group detection, hand / gesture recognition)
- `Assets/DigiPhant/Runtime/DivaControlState.cs`, `Assets/DigiPhant/Runtime/DivaDemo.cs`

Rules for agents:
1. **Do not modify these files** (gesture rules, thresholds, role/mode logic) on your own initiative, and do not
   "tune" them while fixing something else.
2. If the user asks for a change there, first tell them the controls are locked, summarise the double-check
   procedure from `GESTURES_LOCKED.md`, and only continue after they confirm the team (including Eva,
   EvaLuglitched) agreed. Work on a branch and open a pull request; do not push such changes straight to `main`.
3. Never edit `LOCKED` / `LOCKED_LINES` in `TrackingOverrides/test_gesture_lock.py` just to make a failing test pass.
   A failure there means a locked control changed.
4. After an approved change, run all Python tests
   (`DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s TrackingOverrides -p "test_*.py"`) and the
   four Unity validators (**DigiPhant > Diva > Validate Startup / Gestures / Stationary Turn / Trial Export**),
   update `GESTURES_LOCKED.md`, `test_gesture_lock.py` and `EVA_UPDATES.md`, and ask for a live camera test.

## Other project notes
- Fab rocket / cloud files are not in git (licence); see `FAB_SETUP.md`. Never commit `Assets/DivaGame/ThirdParty/Fab/`.
- Do not commit `ProjectSettings/VersionControlSettings.asset` (Unity keeps switching it) or `.vscode/`.
- The Vision Pro branch `carl/diva-vr` needs a Unity Pro licence; do not merge it into `main`.
