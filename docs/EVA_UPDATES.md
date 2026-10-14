# Eva 的更新记录（Diva 游戏层）

这里汇总 Eva（EvaLuglitched）在仓库里做的所有更新，按主题排列，括号里是提交号。
Eva 的代码都在 **`Assets/DivaGame/`**，场景物体都在 `Diva.unity` 的根物体 **`Diva Game Layer`** 下；
用 **Diva > Game > Build Game Layer in Open Scene** 重新生成，只替换这一个根物体，不动别人的东西
（Diva Show、机甲、DigiPhant 控制、赛道位置）。详细说明见 `Assets/DivaGame/README.md`。

## 一览

| # | 更新 | 提交 |
| --- | --- | --- |
| 1 | 游戏路线图 | `029748d` |
| 2 | 糖果小镇游戏层：小镇、靶子、火箭、云、HUD 和任务 | `76ed5d5` |
| 3 | 赛车式开场动画、绕圈飞行的火箭、漂浮的云、抬高的游戏镜头 | `7228a59` `8f60ad4` |
| 4 | 开场：大象特写、火箭侧面飞过、更明显的推进器点火 | `ce7445a` |
| 5 | 赛道边线整齐对齐、糖果装饰、圆形起点台 | `f2f9651` |
| 6 | 合并 Carl 的 Diva Show | `ef2556f`（及之后的合并） |
| 7 | 开场文字改为 uGUI/TextMesh Pro（Vision Pro 可显示）；Diva Show 下 I 键不再重播 | `cbe4183` |
| 8 | 缺少 Fab 文件时显示替身火箭和云；Fab 安装说明 | `c451181` `aa9dcf2` `f7fc1fb` `9d9d5fd` |
| 9 | 太空星空夜景；主摄像机显示天空盒 | `91a52e7` `0d4aa3f` |
| 10 | 倒数时火箭从大象正前方飞过；火箭镜头里保留带光环的星球 | `c6fb9cd` `26a125f` |
| 11 | 键盘操作玩法（和手势同时可用，不需要摄像头） | 见下方第 11 条 |
| 12 | 终点：拱门移到终点线、冲线即结束、结束画面显示完成任务数、终点圆台 | 见下方第 12 条 |
| 13 | 摄像头修复、竖拇指用 Gesture Recognizer、新开始流程（主页 → 大屏摄像头识别 → 自动开始） | 见下方第 13 条 |
| 14 | 走路（摆臂 GO）放宽 + 每帧诊断记录 | 见下方第 14 条 |
| 15 | 镜像摄像头画面、转弯改为压肩/歪头（不需要髋部）、遮挡不再全员中断 | 见下方第 15 条 |
| 16 | 🔒 **单人和三人动作识别锁定**（`GESTURES_LOCKED.md`，修改前必须 double check） | 见下方第 16 条 |
| 17 | 赛道上倒下的木头往路中间挪 1 米（离中心线 4 → 3 米），一端压到路边；"跨过木头"任务点跟着移动 | `DivaGameBuilder.PlaceCourseLog`；Restore Course Colours 会放回原处 |
| 18 | 木头任务改成"绕过树干"（顺着赛道走过木头就算完成，不需要手势）；接近未完成的任务时，任务点上方出现悬浮发光箭头 | `DivaGameManager`（`TaskCheck.Pass`）、`Runtime/DivaTaskMarkers.cs`、`DivaText.Task` 中文名 |
| 19 | 摄像头识别页加角色介绍卡：P1 走路 / P2 方向 / P3 攻击 + 吃喝，每张卡下面一句怎么做（和代码一致）；和原来的 P1/P2/P3 按钮一样，识别到这个玩家整张卡变绿并显示"已识别"，没识别到是深色"等待中"；下面一行写大招（任何人双手举过头顶）。单人模式三张卡不写 P 号。只改了界面，没动锁定的手势文件 | `DivaShowCameraCheck.cs`、`DivaText.cs`（`check.role*` / `check.how*` / `check.ult*`） |
| 20 | 摄像头识别页的火柴人动画（按 Eva 的手绘稿）：进入识别页时在大屏画面中央放 8 秒，P1 · P2 · P3 从左到右（和站位一致；P3 有两个：喷水和喝水），白色线条、没有背景（摄像头画面一直看得到），然后淡出消失。P1 双臂交替上下摆（摆臂）；P2 压肩 + 歪头左右来回，头上有弯箭头（向左 / 向右）；P3 一个双手握拳到胸前再张开往前推（喷水），旁边一个举手竖大拇指（喝水 + 吃东西，同一个手势也补水）。演示的 8 秒里不开始"全员绿色保持 5 秒"的计时，演示结束后才开始，免得大家跟着做动作时被存成站姿。下面一行的角色卡不变 | `Runtime/DivaStickFigure.cs`（uGUI 网格画线，不用贴图）、`DivaShowCameraCheck.cs`、`DivaText.cs`（`check.cap.*`） |
| 21 | 同步 Carl 的 v1.0（PR #6）后：三人模式教练提示里 P2 的转弯说明从"身体倾斜转向 / lean to steer"改成"压肩或歪头转向 / tilt your shoulders or head to steer"，和锁定的转弯规则一致；GitHub 仓库简介（About）和 topics 按 Carl 的 NOTE 填好；README 的 Drive 链接填上了 Eva 新建的文件夹"Diva — DesComp Project 2 (recordings & video)"（录像和对比视频放这里） | `DivaText.cs`（`coach.run.b3`）、GitHub 仓库设置 |
| 22 | 摄像头总是连到 iPhone 的修复：追踪程序（OpenCV）按设备的内部 ID 排序给摄像头编号，iPhone 的连续互通相机（Continuity Camera）在附近时 ID 排在 Mac 自带摄像头前面，变成 0 号，而游戏一直用 0 号，所以会打开手机、手机断开后又没画面。现在 Mac 上开摄像头前，游戏按同样的顺序读出摄像头列表，自动用 Mac 自带摄像头的编号（日志里有 `DIGIPHANT cameras (bridge order)`）。没改锁定的 bridge.py。想用外接摄像头时在 DigiPhantCameraPreview 上关掉 Prefer Built In Camera，改用 Camera Index | `Runtime/DigiPhantMacCameras.cs`（新）、`DigiPhantCameraPreview.cs` |
| 23 | 🔒 锁定修改（Eva 批准，分支 + PR）：① 多人在画面里时手势识别偶尔报错导致追踪程序退出、之后手势全部失灵（下载版和 Unity 里都出现过）→ 现在重建识别器、跳过这一帧继续；② 锁定后玩家离开 1 秒以上、画面里还有别人时，角色交给离原位置最近的那个人（单人：A 走了 B 留着就锁 B；三人：只换走掉的那个）；③ Unity 在追踪程序意外退出时自动重启（2 秒起，越失败等越久，最长 30 秒）。手势规则和阈值都没动 | `bridge.py`（`RecoveringRecognizer`、`PerformerTracker` 重新锁定）、`test_relock.py`、`test_gesture_lock.py`、`GESTURES_LOCKED.md`、`DigiPhantCameraPreview.cs`（自动重启） |
| 24 | **v1.2（macOS）发布**：包含第 22、23 条（自带摄像头、自动重启、谁留在镜头里就继续玩、多人不崩溃）。Eva 在本机重新打包：追踪程序用 `Tools/bridge_app/build_mac_bridge.sh`（Python 3.13，自检通过），Unity 用 Diva > Release > Build macOS，签名用 Eva 有效的 Apple Development 证书（`347744DC…`；钥匙串里另一张 `EA05274B…` 已被吊销，用它签的 app 会被 macOS 当成有害软件移到废纸篓）。Windows 和 Vision Pro 这次没有重新打包，下载仍是 v1.1。README、产品网页 `docs/index.html` 的 Mac 下载链接改成 v1.2 | GitHub Release v1.2、`README.md`、`docs/index.html` |
| 25 | 🔒 走路对新手更友好（Eva 批准，分支 + PR）：原来要求两只手都摆够 0.14 肩宽、节奏匀速、高度刚好，摆太大太小太高太低都会断。录下的数据里手臂明显在摆时只有 59% 被判成在走，大多是因为一只手摆得大、另一只手摆得小；新规则在同样的数据上约 89%，站着不动的帧没有一帧误判。改法：一只手 > 0.10、另一只手 > 0.05 就算；看 1 秒内的摆动；最高点、最低点停顿 0.5 秒内不停；慢一点的动作也算在动；摆到大腿也算；手腕糊了 0.5 秒内保持；双手在头顶停 0.4 秒才停走路。代价：停手后大象多走约 0.15 秒 | `diva_gestures.py/json`、`test_easier_walk.py`、`test_go_relaxed.py`、`test_gesture_lock.py`、`GESTURES_LOCKED.md` |
| 26 | **v1.3（macOS）发布**：第 25 条的走路规则 + 追踪帧率修复。原因：游戏跑满 120 Hz 时，追踪程序和游戏抢 CPU，从 30 帧掉到约 15 帧（10/12 的日志 33 ms 一帧，今天 67 ms），走路断断续续。现在开着摄像头时游戏锁 60 帧（`DivaShowPerf.cameraFrameCap`），实测游戏 59 帧、追踪 29.5 帧；F2 和日志里都会显示 tracking fps。顺便修了打包：增量构建会把 app 名字、包名打回项目默认（JIVE_Digiphant、DefaultCompany、模板包名），日志和排行榜位置都变了，摄像头权限也要重新给；现在发布版每次都是干净构建（`DivaReleaseBuild`，`CleanBuildCache`）。Windows 和 Vision Pro 仍是 v1.1 | GitHub Release v1.3、`DivaShowPerf.cs`、`DivaReleaseBuild.cs`、`README.md`、`docs/index.html` |
| 27 | 🔒 P1 停手更快、识别更准（Eva 批准）+ 关掉 BOOSTER RUSH：用真实日志和带抖动的模拟查出来，v1.3 停手后其实要约 1 秒才停——逐帧判断"在动"时，手腕的轻微抖动一直被当成在动，只能等 1 秒的摆动记录过期。现在"在动"和 0.2 秒前的位置比（抖动不累加就不算），阈值 0.04，停顿 0.25 秒：停手约 0.4 秒停下（30 帧和 15 帧都是），开始走仍然 0.03–0.07 秒，停顿 0.35 秒以内的慢摆照样走；抖动加倍时站着被误判走路的帧比 v1.3 少 90%。BOOSTER RUSH（接近最高速 1 秒后的横幅、画中画、镜头拉宽、速度线，不加速）新加开关 `boosterRush`，默认关；大招后的冲锋保留（冰墙要靠它）。大象的速度和加速度没动 | `diva_gestures.py/json`、`test_stop_on_time.py`、`test_easier_walk.py`、`test_gesture_lock.py`、`GESTURES_LOCKED.md`、`DivaShowMeta.cs`、`DivaShowPlay.cs` |
| 28 | **v1.4（macOS）发布**：第 27 条（P1 停手约 0.4 秒停下、BOOSTER RUSH 关闭），Eva 在 Unity 里实测成功后原样打包，代码和测试版本完全一样（PR #16 合并后 main 只多了一行文档）。干净构建、追踪程序重新打包并自检通过、Eva 有效证书签名。Windows 和 Vision Pro 仍是 v1.1 | GitHub Release v1.4、`README.md`、`docs/index.html` |
| 29 | 🔒 游戏开始后角色固定 + 角色颜色（Eva 批准）：v1.4 的"谁留在镜头里就继续玩"在三人模式下，两个人同时短暂丢失再回来时角色可能互换（站中间的人变成 P1，歪头就转不了弯）。现在识别页保存站姿时记下每个人的站位（P1 左、P2 中、P3 右），游戏中玩家出画面再回来只能拿回自己的角色，站到别人那一块拿不到别人的角色；单人模式照旧，玩家回来就接上。摄像头画面里骨架和标签按角色上色：P1 红、P2 蓝、P3 绿，游戏中右上角小画面也一样，随时能看出谁是谁；识别页卡片和火柴人演示的 P1 / P2 / P3 用同样的颜色 | `bridge.py`、`test_role_spots.py`、`test_gesture_lock.py`、`GESTURES_LOCKED.md`、`DivaShowCameraCheck.cs` |
| 30 | 🔒 手垂着不再走路（Eva 批准，分支 `eva/walk-hands-up`）：对照 Vicky 上传到 GitHub 的原始 MediaPipe 走/停数据（release `mediapipe-data-2026-10-13`：3 段 P1 视频和标注，用我们自己的姿势模型按 30 / 15 / 10 帧重跑），原始走路是双手摆过肩膀、摆幅约 1.2 / 0.9 肩宽，站着不动也有 0.11 / 0.09 的晃动。v1.3 放宽后的 0.10 / 0.05 和"站着不动"差不多，又不看手有多高，所以三人模式里 P1 手垂着、手腕看不清慢慢漂时大象会自己往前（17:08 那局 34–39 秒）。现在 1 秒内至少一只手要抬到肚脐以上（`go_reach` -0.6），摆幅 0.14 / 0.08。慢摆、不匀、从大腿摆上来、摆过头顶照样走；只在腰下面晃的不再算。`test_hands_up.py` | `eva/walk-hands-up` |

## 1. 路线图（`029748d`）
`ROADMAP.md`：大象射击游戏的玩法和分工计划。

## 2. 糖果小镇游戏层（`76ed5d5`）
- **小镇**：Blender 脚本 `Art/Blender/build_town_kit.py` 做的二十多个精细模型（联排房、科技楼、钟楼、
  拱门、靶子、道具等），导出到 `Models/Town/`；程序生成的贴图（`Editor/DivaTextures.cs`）。
- **配色**：糖果粉彩（草莓牛奶、薄荷、蜜桃、薰衣草、宝宝蓝）+ 青色/洋红霓虹；赛道周围地面浅灰蓝紫。
  颜色都在 `DivaGameBuilder.cs` 的 `Looks` 表里，改颜色重建不会移动任何东西。
- **赛道**：老师的赛道起点、终点、路线和 4 个任务位置不变，只换颜色（**Restore Course Colours** 可还原）。
- **靶子**：16 个红白靶子，带橙色霓虹光环和点光源（被打倒时灯光变暗）。
- **玩法**：`DivaLaserBlaster`（自动瞄准，空格键或 P3 喷水手势开火）、`DivaGameManager`（分数、计时、4 个任务的 HUD）。
- **大象活动范围** 15 → 30。
- **Fab 火箭和云**：不在 git 里（授权原因），见第 8 条。

## 3. 开场动画、绕圈火箭、漂浮云、天空镜头（`7228a59` `8f60ad4`）
- `Runtime/DivaIntro.cs`：类似马里奥赛车的开场，约 20 秒：航拍小镇 → 火箭飞过 → 俯冲到起点 →
  绕机甲大象一圈（零件逐个通电亮起）→ 3·2·1·GO。音效全部代码合成。GO 之前大象被锁在起点。**Esc** 跳过。
- `Runtime/DivaOrbit.cs`：70 米长的大火箭绕地图中心飞（半径 95 m，高 42 m）。
- 云在屋顶上方一圈，缓慢漂浮（`DivaFloat`）。
- `Runtime/DivaSkyCamera.cs`：游戏镜头往上抬 16°，让玩家看得到天空里的火箭和云。
- 修复霓虹发光：URP 17 只在材质标记为 RealtimeEmissive 时保留 `_EMISSION`；发光强度降到 30%，避免泛白。

## 4. 开场改进（`ce7445a`）
- 新增 2.5 秒低角度大象特写，推进器在这时点火。
- 火箭镜头 3 秒，镜头和火箭航线垂直，火箭从侧面横穿画面。
- 点火效果：两个喷口冒烟、地面一圈尘环、火花、粉橙色闪光、火焰短暂变成两倍大、镜头轻微震动。

## 5. 赛道边线和起点台（`f2f9651`）
- 两侧青色霓虹边线和赛道平行，转角像赛道本身一样斜接，不再交叉；每个接缝有一颗发光小珠。
- 边线外侧每 3 米一颗糖豆（粉、薄荷、柠檬、淡紫、天蓝），每个弯道外侧一根会转的棒棒糖。
- 大象起点：圆形糖果起点台（粉色外圈、奶油圆盘、淡紫中心、缓慢旋转的霓虹虚线圈、一圈糖豆、两根棒棒糖）。

## 6. 合并 Carl 的 Diva Show（`ef2556f`）
`Diva.unity` 冲突是因为游戏层重建后物体编号全变了。做法：采用 Diva Show 版本的场景（保留 Diva Show、
DivaLook、机甲皮肤），再在上面重建 `Diva Game Layer`。之后每次合并都检查 `DivaShowDirector`、
`DivaMechSkins` 仍在场景里。

## 7. 开场文字支持 Vision Pro（`cbe4183`）
- 黑边、标题横幅、"Esc skip"、3·2·1·GO 从 IMGUI 改为 uGUI 画布 + TextMesh Pro（排序 60，在 Diva Show 的 50 之上）；
  有 Diva Show 时用 Carl 的字体。
- 场景里有 `DivaShowDirector` 时 **I 键不再重播开场**。
- Diva Show 依赖的接口保持不变：`Begin()`、`Skip()`、`Playing`、`Time`、`playOnStart`、`SwoopEnd`、`BootStart`、`OrbitEnd`、`Go`。
- 新菜单 **Diva > Game > Play Intro Now**：Play 模式下跳过选皮肤直接播开场（测试用）。
- **Render Intro Frames** 生成的关键帧里包含开场文字。

## 8. Fab 火箭和云（`c451181` `aa9dcf2` `f7fc1fb` `9d9d5fd`）
- Fab 授权不允许放进公开仓库，所以模型文件不在 GitHub 上。
- 场景里每个 Fab 模型旁边都建了一个替身（自制糖果火箭、Kenney 云），`DivaModelFallback` 在 Fab 文件缺失时自动显示替身，
  不会再出现空天空。
- Eva 私下分享 `Diva_Fab_Assets.zip`，安装步骤见根目录 **`FAB_SETUP.md`**（可以让 Claude 按步骤操作）。

## 9. 太空星空（`91a52e7` `0d4aa3f`）
- `Editor/DivaSpaceSky.cs` 生成 4096×2048 的星空全景图（`Textures/Diva Space Sky.png`）：靛蓝到紫色渐变、
  地平线糖果粉光晕、粉青色星云带、上万颗星星、46 颗四角闪光星、带光环的粉紫糖果星球、小月亮。
- `Runtime/DivaSkyRotate.cs`：Play 时星空缓慢转动（0.6°/秒，用材质副本，不改资源）。
- 雾的颜色和地平线一致，远处房子融进夜空；城镇保持糖果色亮度，灯光略偏淡紫"月光"。
- **主摄像机背景改为 Skybox**：原来是纯色深蓝，Play 时看不到天空（生成器现在会设置它）。
- 原来的粉色白天天空材质 `Diva Sky` 还在，可以换回。

## 10. 倒数火箭和星球（`c6fb9cd` `26a125f`）
- 开场开始时计算火箭的轨道起点，让它在 3·2·1 时从游戏镜头正前方（大象上方）飞过；倒数期间镜头多抬 7°，GO 时恢复。
- 开场时转动星空，让带光环的星球固定出现在火箭特写镜头的左上角，月亮在右上角。
- Esc 跳过时火箭和星空都会对齐到相同的时间点。
- **Render Intro Frames** 会在 Console 输出火箭和星球在画面中的真实位置（`DIVA_INTRO_ROCKET` / `DIVA_INTRO_PLANET`）。

## 11. 键盘操作玩法
- `Runtime/DivaKeyboardControls.cs`：键盘相当于多一个玩家，合并进 DivaDemo 的控制状态，和手势**同时**可用，
  没有摄像头、没校准也能玩。移动、喷水（水柱 + 激光打靶）、喝水、Diva Show 的 HUD/技能、赛道任务都和手势一样响应。
- 按键：**W/↑** 前进、**A/D 或 ←/→** 转向、**U/I** 瞄准（U 左、I 右）、**按住空格** 喷水、**F（或 R）** 喝水、**U** 同时也是 Diva Show 的大招键（大招充满时按 U 会放出大招）、
  **H** 显示/隐藏右下角的按键提示（中英文跟随 Diva Show 的 L 键）。
- 菜单和过场里键盘和手势一样被锁住；激光原来的"空格单发"在这时关掉，避免空格打两次。
- **给 Carl / Vicky：** 这次改了 `Assets/DigiPhant/Runtime/DivaControlState.cs`（你们的文件），只加了一个
  `Keys` 字段，并把 `Step` 拆成 `StepGestures` + 合并键盘 + 原来的连发逻辑。**没按键时（`Keys == null`）行为和原来完全一样**；
  用 11 个逻辑测试核对过（只有键盘、键盘+手势、锁定菜单、连发节奏、喝水补水、松开按键）。

## 12. 终点和游戏结束
- 原来终点前约 14 米的那座彩虹拱门移到了终点线上，代替老师赛道的小终点门（只隐藏了它的模型，位置不变；
  **Restore Course Colours** 可恢复）。拱门下面有粉白棋盘格终点线，过线后是一个和起点一样的圆形**终点台**（柠檬黄外圈、薄荷绿中心、粉色虚线圈）。
- **大象从拱门穿过终点线就结束这一局，不管其他任务有没有完成**（从侧面走进终点区不算）。
- 结束动画的横幅显示：用时 · 分数 · **完成任务 x/4**；结算卡片原来就有任务数。
- **给 Carl / Vicky：** 改了你们的三个文件，各只有一两行：
  - `DivaShowPlay.cs`：结束原因在没做完所有任务时记为新的 `finish_line`，全部完成仍是 `tasks_completed`（TrialData 的 reason 会出现这个新值）。
  - `DivaShowEnd.cs`：结束横幅后面加了"完成任务 x/4"。
  - `DivaText.cs`：新增 `finish.tasks` 中英文文字。
  - Carl 的 VR 分支里的 `ForceFinish()` 也是"到拱门就结束"，以后合并时两边逻辑一致，注意 `DivaGameManager.cs` 的冲突。

## 13. 摄像头和开始流程
- **修了"摄像头自动关闭"**：手部关键点的 visibility/presence 是 `None`，竖拇指判断一比较就报错，整个追踪程序退出。
  `diva_gestures.val()` 现在把 `None` 当默认值（`test_hand_none_quality.py` 用 MediaPipe 格式的数据复现并验证）。
- **摄像头没画面时不再放弃**：每 4 秒重连一次直到恢复（原来 12 秒内 3 次失败就永久退出）。
- **合并了 Carl 的 `carl/pc-three-player`**：多人模式每 0.5 秒全画面找新人、按进入顺序从左到右分配角色、门槛放宽。
- **竖拇指改用 MediaPipe Gesture Recognizer**（预训练 `Thumb_Up`，这是我们的"额外 MediaPipe 视觉任务"），原来的几何规则作为备用；
  第一次运行自动下载模型，下载失败就退回原来的方法。`test_thumb_model.py` 测试。
- 摄像头画面里**识别到的玩家骨架都是绿色**，没分配的是灰色，P1/P2/P3 标签表示角色。
- 摄像头预览改成约 960×540、15 帧/秒（拆成多个 UDP 包，Unity 拼回），识别页清晰很多；主页不再显示小摄像头窗口；
  游戏中摄像头画面移到右上角按钮下方并放大到 0.8 倍（原来左下角 0.6 倍），技能提示条移到它左边（`DivaShowUi.cs` / `DivaShowHud.cs`）。
- **新开始流程**（`Assets/DivaShow/Runtime/DivaShowCameraCheck.cs`）：主页选颜色和人数 → START（不再需要先校准）→
  大屏摄像头识别页（每个玩家识别到就变绿）→ 全员绿色保持 5 秒自动保存站姿 → Eva 的开场。回车跳过（键盘玩），Esc 返回；Test controls 直接进开场。
- **给 Carl / Vicky：** 改了 `DivaShowDirector.cs`（新阶段 `CameraCheck`，各加一行）、`DivaShowSelect.cs`（START 不再拦、主页不再自动校准）、
  `DivaShowUi.cs`（建页面和显示）、`DivaText.cs`（新文字）、`DigiPhantCameraPreview.cs`（缺手势模型时自动下载）、
  `bridge.py`、`diva_gestures.py/json`，以及 Vicky 的 `DivaStartupValidation.cs`（新规则：摄像头模式 START → 识别页，测试模式 → 开场）。

## 14. 走路（摆臂前进）
- 问题：TrialData 显示一局里大象起步/停下上百次，每段只走约 0.3 秒，约每秒一次，和摆臂节奏一致。
- **放宽**（`diva_gestures.py/json`，Vicky 的阈值）：`go_hand_min_height` -0.85 → **-1.25**（摆到髋部也算）；
  新增 `go_dropout` 0.3 秒：手腕短暂看不清（模糊、出画面边缘）不清空摆臂记录，保持原来的走/停状态。
  同一个"摆到髋部"的模拟动作：改前 73% 的帧在走、4 秒断 8 次；改后 100%、不断。`test_go_relaxed.py`。
- **诊断**：追踪程序每帧把 P1"为什么走/不走"写到 `DigiPhantStarter/Tracking/go_debug/go_<时间>.csv`（保留最近 10 个，
  `--no-go-log` 可关）。玩一局后运行 `python TrackingOverrides/go_debug_report.py` 看统计：走/停切换次数、
  每段走多久、追踪帧率、以及"手臂在摆但判为不走"时是哪条规则（手太低 / 幅度不够 / 停顿 / 手腕看不清 / 两帧间隔太长）。
- 没改 Unity 的移动代码（急停还在，按 Eva 的决定先不改）。

## 15. 三人遮挡和转弯
- **摄像头画面改成镜像**（像照镜子）：P1/P2/P3 按镜像画面从左到右分配，和识别页下面的 P1 · P2 · P3 一一对应。
  动作判断仍用原始画面，左右含义不变（往自己左边 = 左）。`bridge.py`。
- **转弯改成压肩或歪头**（`diva_gestures.py`）：肩线或耳线（看不到耳朵用眼睛）相对校准姿势的斜率，取较大者；
  0.14 开始、0.08 释放、0.40 满转向；**完全不用髋部**；肩膀短暂看不清（被旁边的人挡）保持当前转向 0.3 秒（`steer_dropout`）。
  原来的大幅度"倾斜躯干"需要髋部可见，三人一排时很容易被挡。
- **分不清时只暂停那个人**（`PerformerTracker`）：两个人靠得太近分不清时，只暂停这两个人，其他人照常（原来是这一帧三个人全部取消）。
- **给 Vicky：** 你的倾身转弯测试改成了压肩/歪头：测试里的 `body(lean=…)` 现在也会让肩线和头倾斜；
  "看不到髋部就停止转弯"的 3 个测试改成了新规则（髋部不影响、肩膀短暂遮挡保持 0.3 秒）；新增 `test_tilt_steer.py`。
  Carl 的分配歧义测试改成"只暂停有歧义的人"。

## 16. 🔒 动作识别锁定
- 单人和三人模式的动作、参数、角色分配从 2026-10-12 起**锁定，不允许随意修改**。完整规格和修改流程见根目录 **`GESTURES_LOCKED.md`**。
- 要改必须：群里说明并得到 Eva 同意 → 新分支 + Pull Request → 全部 Python 测试和 4 个 Unity 检查通过 → 单人和三人真人测试 → 更新文档。
- 保护措施：`TrackingOverrides/test_gesture_lock.py`（参数或关键规则一改就失败）、根目录 `CLAUDE.md`（组员的 Claude 会读到，
  不会擅自修改）、锁定文件开头的注释、`.github/CODEOWNERS`（这些文件的 PR 需要 Eva review；需要在 GitHub 分支保护里开启才强制）。

## 回复 Carl 的 NOTE_FOR_EVA.md

| Carl 提到的问题 | 状态 |
| --- | --- |
| 1. Fab 火箭和云不在仓库，别人电脑上天空是空的 | 已处理：替身自动显示（第 8 条）+ `FAB_SETUP.md` |
| 2. 没有 Fab 时保存场景会改一个云的引用编号 | 提交前检查 diff；装好 Fab 文件后就不会再发生 |
| 3. 后台保存时材质丢失 `_EMISSION` | 运行时 `RestoreEmission()` 会补回，Play 不受影响 |
| 4. 主摄像机背景是纯色，看不到天空 | 已处理：生成器把 Main Camera 设成 Skybox（`0d4aa3f`），和 DivaLook 的运行时修正一致 |
| 5. 开场文字用 IMGUI，Vision Pro 不显示 | 已处理：改为 uGUI + TextMesh Pro（`cbe4183`） |
| I 键重播 | 已处理：有 Diva Show 时忽略 I 键 |

## 已知注意事项

- **离线渲染的残影**：项目启用了 GPU Resident Drawer，Render Intro Frames 在同一帧里多次移动火箭时，
  图片里可能出现"两艘火箭"或火箭位置不对；以 Console 里的 `DIVA_INTRO_ROCKET` 坐标为准，游戏里正常。
- `ProjectSettings/VersionControlSettings.asset` 会被 Unity 自动改成 Unity VCS，提交时不要带上。
- Unity 偶尔会微调 `Assets/eva大象/Generated/Skins/` 里的材质颜色数值（四舍五入），不是真正的修改，提交前还原。
