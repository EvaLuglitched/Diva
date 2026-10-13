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
| `TrackingOverrides/bridge.py` | 角色分配（镜像画面从左到右、分不清时只暂停那个人）、多人检测、手部/手势识别（含单人模式隔帧做手势识别的频率） |
| `Assets/DigiPhant/Runtime/DivaControlState.cs` | 单人/三人角色合并规则、单人模式的互斥规则、键盘合并 |
| `Assets/DigiPhant/Runtime/DivaDemo.cs` | 输入入口、可见超时（≥ 0.8 秒） |

`TrackingOverrides/test_gesture_lock.py` 会检查这些参数和关键规则；只要有人改了，测试就会失败并提示先 double check。

## 单人模式（一个人控制全部）

| 动作 | 手势 | 关键参数 |
| --- | --- | --- |
| 前进 | 双臂上下摆（一只手摆够、另一只手也在动就算；摆到大腿、摆过头顶、摆得慢或不匀都行；停手约 0.4 秒内停下） | 1 秒内一只手摆幅 > 0.10 肩宽且另一只手 > 0.05；"在动"= 手腕和 0.2 秒前比移动超过 0.04 肩宽 / 100 ms（抖动不算）；停顿 0.25 秒就停；手不低于肩下 1.8 躯干长；手腕看不清 0.5 秒内保持；双手在头顶停留 0.4 秒才停走路 |
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
- **游戏开始后角色固定在各自的站位**（2026-10-13 Eva 批准的修改）：识别页保存站姿（全员绿 5 秒）那一刻记下每个人的位置
  （P1 左、P2 中、P3 右）。之后整局游戏，**谁的角色都不会被别人拿走**：玩家在画面里时其他人一律不管；玩家出画面再回来，
  回到原位附近马上认回，回到自己那一块但离原位较远时 1 秒后认回；站到别人那一块拿不到别人的角色。单人模式只有一个角色，
  玩家离开 1 秒以上、画面里有人时由这个人接上。保存站姿之前（识别页选人时）仍按原来的规则从左到右分配。
- **原位优先**（2026-10-13 Eva 批准，v1.5）：三人模式开始后，每一帧给角色配人时按**离这个角色原位的距离**排，所以玩家回到自己原位时，一定能从临时占着这个角色的人（站在他背后的观众、同一个人被多认出的一个）手里拿回角色；以前 P2 被挡一下，背后的人就拿走角色，P2 回来也拿不回，直到那个人离开。玩家慢慢挪位置照样跟得住（离原位或离上一帧位置 0.20 以内都算）。单人模式不变。
- **角色颜色**：摄像头画面里的骨架和标签按角色上色，P1 红、P2 蓝、P3 绿（没分配的是灰色）；识别页卡片和火柴人演示上的 P1 / P2 / P3 用同样的颜色。
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
| 2026-10-13 | 角色在玩家离开 1 秒后交给仍在画面里的人；手势识别报错时重建、不退出（`bridge.py`） | Eva | `test_relock.py`，全部 Python 测试，4 个 Unity 检查；Eva 在 v1.2 Mac 版上真人测试成功（2026-10-13）；三人真人测试待补 |
| 2026-10-13 | 走路对新手更友好：一只手 0.10 + 另一只手 0.05（原来两只手都要 0.14），看 1 秒内的摆动，停顿 0.5 秒才停，慢动作也算在动（go_step 0.03），最低到肩下 1.8 躯干长，手腕看不清保持 0.5 秒，摆过头顶不再立刻停走（`diva_gestures.py/json`） | Eva | `test_easier_walk.py`（10 个，含"站着、身体晃、举一只手瞄准都不会走"），全部 Python 测试，4 个 Unity 检查；Eva 单人真人测试（Unity，2026-10-13）：手在摆却因"幅度不够"不走从 92% 降到 5%；三人待测 |
| 2026-10-13 | P1 停手更快、更准：判断"在动"改成和 0.2 秒前的手腕位置比（`go_motion_window`），阈值 0.04，停顿 0.5 → 0.25 秒。以前逐帧比，手腕抖动一直算"在动"，停手后要 1 秒才停；现在约 0.4 秒，开始走仍是 0.03–0.07 秒（`diva_gestures.py/json`） | Eva | `test_stop_on_time.py`（带手腕抖动的停止时间、站着不动、开始延迟），全部 Python 测试，4 个 Unity 检查；Eva 真人测试成功（Unity，2026-10-13），并打包成 v1.4 |
| 2026-10-13 | 游戏开始后角色固定在各自站位（保存站姿时记下位置，玩家回来只能拿回自己的角色，别人拿不走）；画面里角色上色 P1 红 / P2 蓝 / P3 绿（`bridge.py`，识别页 `DivaShowCameraCheck.cs`） | Eva | `test_role_spots.py`（7 个），全部 Python 测试，4 个 Unity 检查；真人测试待做 |
| 2026-10-13 | 原位优先：三人模式开始后按离原位的距离给角色配人，玩家回到原位就能从临时占位的人手里拿回角色（补 Eva 的站位修改：观众站在 P2 背后时，P2 被挡一下角色就被拿走、回来也拿不回）；单人不变（`bridge.py`） | Eva（Carl 转达） | `test_spot_anchor.py`（4 个：背后有人时 P2 拿回角色——不打补丁时这个测试失败；其他角色不动；慢慢挪位置跟得住；单人不变），全部 Python 测试，4 个 Unity 检查；真人测试待做 |
| 2026-10-13 | 单人模式追踪变快：手部/手势识别从每帧做改成隔一帧做（`SOLO_HANDS_EVERY = 2`，`hands_due()`），中间那帧沿用上一帧的手，和三人模式在 'fast' 档一样。原因：单人时每帧先做完整姿势识别、再在整张图上找 6 只手，一帧 64 ms，只有约 15 帧（M1 Max 实测；Windows 用 CPU 更慢），走路判断拿到的样本太少。隔帧后约 45 ms、约 22 帧；喷水和 0.4 秒竖拇指每秒仍有约 11–15 次手部更新。走路、转弯、喷水、补水、大招的规则和参数都没改；三人模式不变（`bridge.py`） | Eva（Carl 转达） | `test_solo_hands.py`（隔帧、首帧不跳、三人不变），`test_gesture_lock.py` 新增两行锁定，全部 Python 测试（116 个），4 个 Unity 检查；Carl 单人真人测试（v1.5 Mac 版）待补 |

AI 助手（Claude 等）：见根目录 `CLAUDE.md`，不要擅自修改以上文件。
