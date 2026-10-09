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
