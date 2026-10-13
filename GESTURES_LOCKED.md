# 🔒 动作识别已锁定 / Gesture controls are LOCKED (2026-10-12)

**单人和三人模式的动作识别规则、参数、角色分配已经定稿并锁定。之后不允许随意修改。**
如果确实需要修改，必须先按下面的 **double check 流程** 做完，并得到 Eva（EvaLuglitched）同意。

The single-player and three-player gesture rules, thresholds and role assignment are final and locked.
Do not change them without completing the double-check procedure below and Eva's approval.

## 锁定范围 / What is locked

| 文件 | 锁定内容 |
| --- | --- |
| `TrackingOverrides/diva_gestures.py` | 所有动作的判断规则和 `DEFAULTS` 参数 |
| `TrackingOverrides/diva_gestures.json` | 实际使用的参数（必须和 `DEFAULTS` 一致） |
| `TrackingOverrides/bridge.py` | 角色分配（镜像画面从左到右、分不清时只暂停那个人）、多人检测、手部/手势识别 |
| `Assets/DigiPhant/Runtime/DivaControlState.cs` | 单人/三人角色合并规则、单人模式的互斥规则、键盘合并 |
| `Assets/DigiPhant/Runtime/DivaDemo.cs` | 输入入口、可见超时（≥ 0.8 秒） |

`TrackingOverrides/test_gesture_lock.py` 会检查这些参数和关键规则；只要有人改了，测试就会失败并提示先 double check。

## 单人模式（一个人控制全部）

| 动作 | 手势 | 关键参数 |
| --- | --- | --- |
| 前进 | 双臂上下摆（摆到髋部也算） | 0.8 秒内两手摆幅 > 0.14 肩宽；手不低于肩下 1.25 躯干长；停顿 0.35 秒停；手腕短暂看不清 0.3 秒内保持 |
| 转弯 | **压肩或歪头**（往自己左边 = 左转），不需要髋部 | 斜率 0.14 开始、0.08 释放、0.40 满转；肩膀短暂看不清保持 0.3 秒 |
| 瞄准 | 举起一只手 | 手高 > 0.65 躯干长 |
| 喷水 | 双手张开向前推 | 见 `shoot_*` 参数 |
| 补水 / 吃喝任务 | **竖大拇指保持 0.4 秒** | MediaPipe Gesture Recognizer `Thumb_Up` ≥ 0.5，或几何规则备用 |
| 大招 | 双手举过头顶保持 0.8 秒 | 手高 > 0.85 躯干长 |
| 互斥 | 喷水/补水/大招时不前进；补水/大招时不转弯 | `DivaControlState` |

## 三人模式

| 玩家 | 站位（镜像画面里） | 负责 |
| --- | --- | --- |
| P1 | 左 | 前进（双臂上下摆） |
| P2 | 中 | 转弯（压肩或歪头） |
| P3 | 右 | 瞄准、喷水、竖拇指补水 |
| 任何人 | — | 大招（双手举过头顶） |

- 角色按**镜像**摄像头画面从左到右分配，和摄像头识别页下面的 P1 · P2 · P3 对应；所有人到齐或保存站姿后角色锁定。
- 两个人靠得太近分不清时，**只暂停分不清的那两个人**，其他人照常。
- **谁留在镜头里就继续玩**（2026-10-13 Eva 批准的修改）：锁定后，某个玩家离开画面 **1 秒**以上、画面里又有没角色的人，
  这个角色就交给离原位置最近的那个人（单人模式：一开始是 A，B 进来后 A 离开，就锁定 B）。原来的玩家还在画面里时，其他人一律不管；
  不到 1 秒的短暂看不清不会换人；两个人分不清（暂停）时也不会把角色交给旁边的人。
- **多人在画面里不再崩溃**（同一次修改）：MediaPipe 手势识别在好几只手同时出现时偶尔内部报错（"Packet isn't the sole owner"），
  以前整个追踪程序会退出；现在重建识别器、跳过这一帧继续。Unity 那边如果追踪程序还是意外退出，也会自动重启（2 秒起，最长等 30 秒）。
- 开始流程：主页选颜色和人数 → START → 大屏摄像头识别页 → 全员绿色 5 秒自动保存站姿 → 开场。

## 修改前的 double check 流程 / Before changing anything

1. **先在群里说明要改什么、为什么改，得到 Eva 同意**（最好三个人都同意）。
2. **不要直接改 `main`**：在新分支上改，开 Pull Request，请 Eva review。
3. 跑完全部检查，全部通过：
   - `DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s TrackingOverrides -p "test_*.py"`
   - Unity：**DigiPhant > Diva > Validate Startup / Validate Gestures / Validate Stationary Turn / Validate Trial Export**
4. 有意修改参数时，同时更新 `test_gesture_lock.py` 里的 `LOCKED` 和本文件，并在提交说明里写清楚。
5. **真人测试**：单人和三人各在摄像头前完整玩一局；用 `TrackingOverrides/go_debug_report.py` 看走路记录，确认没有变差。
6. 在 `docs/EVA_UPDATES.md` / `docs/NOTE_FOR_*.md` 里记录改动。

## 已批准的修改记录

| 日期 | 改了什么 | 谁批准 | 测试 |
| --- | --- | --- | --- |
| 2026-10-13 | 角色在玩家离开 1 秒后交给仍在画面里的人；手势识别报错时重建、不退出（`bridge.py`） | Eva | `test_relock.py`，全部 Python 测试，4 个 Unity 检查；Eva 在 v1.2 Mac 版上真人测试通过（2026-10-13） |

AI 助手（Claude 等）：见根目录 `CLAUDE.md`，不要擅自修改以上文件。
