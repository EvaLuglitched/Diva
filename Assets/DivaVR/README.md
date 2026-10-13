# Diva VR — Vision Pro 第一人称版（分支 `carl/diva-vr`）

Carl 做的 Apple Vision Pro 版本：坐在大象背上的驾驶舱里，第一人称开大象跑 Eva 的糖果小镇赛道。
玩法说明（怎么开始、怎么操控）在仓库根目录 **`docs/DIVA_TRY_IT.md`** 的 "Vision Pro" 一节；这份文件讲代码、构建和踩过的坑。

**注意**：这个分支加了 `com.unity.xr.visionos` 3.3.1 和 `com.unity.xr.hands` 1.9.0（`Packages/manifest.json`）。
visionOS 支持需要 Unity Pro / Enterprise 许可证，所以**先不要把这个分支并进 main**，用 Personal 许可证的人也不要在本机
切到这个分支打开工程（会报许可证错误）。PC 版的玩法在 main 上完全不受影响。

## 目录

| 位置 | 内容 |
| --- | --- |
| `Runtime/DivaVRRig.cs` | 入口：在 `Diva.unity` 的 DivaShow 基础上搭 VR 装置（相机、驾驶舱、手势、教学、道具、庆祝……都是运行时加的组件，场景文件不改）。座舱朝向紧跟大象（平滑 0.15 秒、上限 50°/秒，要高于 `DivaVRInput` 的 40°/秒转速，不然转弯时座舱落后、停下后画面还自己转——晕） |
| `Runtime/DivaVRBubbleTour.cs` | 开场：梦境里的菜单（START、EN/中文）→ 三个静止镜头（全景 → 大象背后 → 低机位英雄镜头）用暮色淡入淡出接起来 → 落进驾驶舱 |
| `Runtime/DivaVRGuide.cs` + `DivaVRGestureDemo.cs` | 驾驶舱正前方的教学牌：第 1 步自动播放的"怎么玩"小动画、第 2 步选涂装、第 3 步练动作、第 4 步出发；结算时"再来一局" |
| `Runtime/DivaVRHands.cs` / `DivaVRInput.cs` | 操控（和电脑版的手势对齐）：身体前倾 = 前进（脖子根相对坐好时往前 7–22 厘米）、歪头 = 转弯、看着靶子捏 = 象鼻水炮（看哪打哪，带 12° 辅助瞄准；捏也用来按按钮，按钮要先看 0.35 秒，主按钮捏住 0.5 秒）、竖拇指 0.4 秒 = 吃喝补水、双手举到眼前 = 大招 / 确认；看着按钮捏 = 按按钮。倒数时记一次坐姿，暂停板有 RECENTER SEAT |
| `Runtime/DivaVRCockpit.cs` | 驾驶舱：Blender 零件 + 仪表（速度、能量、水量）、按钮、开关、踏板、RGB 流光灯条（燃烧时变橙红） |
| `Runtime/DivaVRCoach.cs` / `DivaVRHowTo.cs` | 比赛里的教练提示（下一站的柱子和箭头、靠近任务点的动作提示）和起跑卡片旁的动作小人 |
| `Runtime/DivaVRStickFigure.cs` | 教学牌第 1 步的火柴人（照 Eva 识别页的画法）：前倾、歪头、看着捏、竖拇指、举手 |
| `Runtime/DivaVRMeta.cs` / `DivaVRDash.cs` | 比赛节奏在驾驶舱里的呈现：灯条燃烧、空间音效、浮字、大招就绪提示；冲刺的风线、震动、尾灯。规则本身（糖果 +15、小火箭冲刺、ON FIRE ×1.5、黄金靶子 ×3、低水量、圈速奖牌、辅助瞄准、每次喷水耗水 .022）在 PC / VR 共用的 `Assets/DivaShow/Runtime/DivaShowMeta.cs`（糖果在 `DivaShowPickups.cs`，教练触发在 `DivaShowCoach.cs`），VR 只接它的事件 |
| `Runtime/DivaVRCelebration.cs` / `DivaVRHologram.cs` | 冲线的英雄时刻（聚光灯、卡片砸进来、分数跳动、彩纸烟花、奖牌章）和仪表台上的全息大象（每种材质合成一块网格，几次绘制；换涂装按材质组换） |
| `Runtime/DivaVRFeedback.cs` / `DivaVRFloatText.cs` / `DivaVRHandGhost.cs` | 打中靶子的飘字（池子里复用）、任务完成的 DONE!、光柱；手影 = 每只手一次实例化绘制的 26 个小光点（`DIVA_HANDDOTS=spheres` 退回逐关节小球） |
| `Runtime/DivaVRGlass.cs` / `DivaVRCulling.cs` / `DivaLook.cs`（DivaShow） | 画面：玻璃柔化、分级距离剔除、冷色补光 / 低角度太阳 / 雾 / 半强度泛光 |
| `Runtime/DivaVRUiShaders.cs` + `Shaders/DivaVRText.shader` / `DivaVRUi.shader` | 文字和 UI 的 URP 替代着色器（见下面"坑"第一条） |
| `Runtime/DivaVRPerfLog.cs` / `DivaVRCapture.cs` / `DivaVRDebug.cs` | 头显上的诊断：帧率日志、控制台日志、真实 XR 左右眼截图、开关文件 |
| `Runtime/DivaVRAutopilot.cs` / `DivaVRSimulator.cs` | 自动驾驶（无人测试）和编辑器里的 VR 模拟（`-divaVR`） |
| `EditorVisionOS/DivaVRBuild.cs` | 命令行构建：URP 设置、静态合批、visionOS 插件的校验规则（ARSession、深度纹理、无启动画面、最低 visionOS 2.0） |

## 构建和安装（Carl 的机器）

```
# 1. Unity 导出 Xcode 工程（用一份干净的拷贝，不动正在开着的工程）
Unity -batchmode -nographics -quit -buildTarget VisionOS -projectPath <拷贝> \
      -executeMethod Diva.VR.EditorTools.DivaVRBuild.BuildFromCommandLine
plutil -replace CFBundleDisplayName -string "Diva VR" Builds/visionOS/Info.plist
# 2. Xcode 签名编译
cd Builds/visionOS && xcodebuild -target Unity-VisionOS -sdk xros -configuration Release \
      -allowProvisioningUpdates DEVELOPMENT_TEAM=<team> CODE_SIGN_STYLE=Automatic
# 3. 装到头显（头显要醒着；正在玩的时候装会把这局杀掉）
xcrun devicectl device install app --device <udid> build/Release-xros/JIVEDigiphant.app
```

`-divaMixed` 构建的是带透视（看得到房间）的版本；默认是全沉浸。

编辑器里测：`Diva > VR > Play in VR Simulator`，或命令行 `-divaVR`（加 `-divaAutoplay` 自动跑一整局）。
visionOS 模拟器只能查逻辑和异常，画不出 XR 画面（Unity 6 的 URP 在模拟器上报 `Unsupported XR layout: 1 render passes`）。

## 头显上的诊断

- 每次启动写 `Documents/DivaPerf/perf-*.log`（帧率、GPU 时间、阶段）和 `console-*.log`（DIVA 日志、警告、错误、场景清单）。
  拉下来：`xcrun devicectl device copy from --device <udid> --domain-type appDataContainer --domain-identifier com.jchen60.divavr --source Documents/DivaPerf --destination <目录>`
- 截图：把内容不是 `0` 的文件推到 `Documents/diva_capture`，选皮肤时会存单眼截图和真实的左右眼画面（`*_xr.jpg` / `*_xr2.jpg`）。
- 开关：`Documents/diva_flags.txt`，一行一个 `NAME=VALUE`（`DivaVRDebug.Get`；环境变量优先）。常用：`DIVA_STATS=1` 仪表盘角上显示帧率，`DIVA_HANDDOTS=spheres` 手影旧画法，`DIVA_AUTOPLAY=1` 自动驾驭。
- 崩溃报告：`xcrun devicectl device copy from --device <udid> --domain-type systemCrashLogs --source . --destination <目录>`，`.ips` 里 `faultingThread` 的帧带 IL2CPP 方法名。

## 踩过的坑（改 VR 的东西之前先读）

1. **内置管线的着色器在 visionOS 的立体渲染里不画**：`UI/Default`（所有 uGUI 图片 / 底板）和 `TextMeshPro/Distance Field`（所有文字）
   在头显上完全看不见，而普通相机（编辑器、模拟器、单眼截图）全都正常，所以用相机测永远发现不了。
   VR 里任何新的东西只能用 URP 着色器（`Universal Render Pipeline/*` 或带立体宏的自定义 HLSL），文字用 `Diva/VR Text`、UI 用 `Diva/VR UI`
   （`DivaShowDirector` 在 VR 里把文字着色器传给 `DivaUi.MakeFont`；`DivaUi` 新建的图形直接带 "DivaVR UI" 材质（`DivaUi.GraphicMaterial`），别处建的由 `DivaVRUiShaders` 在阶段切换后和每 6 秒补一次）。
   验证只认头显上的 `_xr.jpg` 截图。
2. **工程是线性色彩空间**：0.96 透明度的深色底板也会透出约 1/4 的背景亮度，小镇上面的字就看不清。世界空间的 UI 底板要完全不透明。
3. **舒适**：绝不转动玩家看得见的世界（换朝向要在全遮挡下 snap）；镜头只做直线慢推；用暮色淡入淡出而不是亮色闪白；
   头不参与操控（"前倾 = 油门"让人晕，已删）；转弯时才加暗角。
4. **帧率**：头显锁在 45 fps（22.2 ms）。像素级设置（分辨率、MSAA、阴影、后期）都不是瓶颈：渲染线程 13–15 ms、GPU 20–24 ms，
   是三角形数量（Eva 的小镇没有 LOD，每帧 2–3 M 三角形）。Forward+ / GPU Resident Drawer 试过更慢，已回退；
   现在用构建时静态合批 + 分级距离剔除（`DivaVRCulling`，小物件 45 m、中物件 75 m 以外剔除）。2026-10-13 又减了一批 CPU：全息机甲按材质合并、手影实例化绘制、
   飘字 / 闪光 / 爆开 / 分数飘字进池子、UI 材质扫描从每 0.5 秒改成按需、仪表和教练的字只在数值变了才写。桌面版的景深和动态模糊在 VR 里都关着（`DivaLook`）。
5. 运行时创建的材质 / 网格不进构建的 link.xml 剥离名单：`Assets/link.xml` 保留了 Capsule / Box / Sphere / MeshCollider。
6. 中文字体是子集：`DivaShow/Resources/DivaCJK.ttf` 由 `DivaShow/Fonts/make_cjk_subset.py` 从 `DivaText.cs` 里的中文生成，
   **加了中文字符串就要重新生成**，否则新字显示成方块。
7. 装 app 要等头显空闲（150 秒内没有写 perf 日志）：正在玩的时候装，这局会直接退出，看起来像闪退。
