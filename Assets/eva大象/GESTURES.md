# 手势和动画的触发逻辑

这份笔记说明 Diva 三人手势怎么触发机甲大象的动画和特效。手势规则在 `TrackingOverrides/diva_gestures.py`，阈值在 `TrackingOverrides/diva_gestures.json`，Unity 端在 `DivaControlState`、`DivaDemo` 和 `Assets/eva大象/Runtime`。

## 一帧里发生了什么

```
摄像头
  -> Python bridge（MediaPipe 身体 + 手部关键点）
  -> diva_gestures.py：每个玩家一份 {go, steer, aim, shoot, drink, confidence}
  -> UDP -> DigiPhantController -> DivaDemo.AcceptFrame
  -> DivaControlState.Step：按角色合成 Forward, Turn, Aim, Shoot, Drink, Water（水箱）, Bursts
       ├─ DigiPhantLocomotion：Forward / Turn -> 移动速度 -> 走路、跑步、转向动画
       ├─ DivaDemo.LateUpdate（执行顺序 200）：摆鼻子 + 打靶水滴
       ├─ DivaMechGestureLink（执行顺序 210）：Shoot -> 鼻尖水枪，Drink -> 泡泡炮
       └─ DivaBoosters、DivaMechAudio：读移动速度 -> 推进器火焰、引擎声、脚步声
```

下面的"手高"是肩线到手腕的高度除以躯干长度（肩中点到髋中点），0 表示手和肩一样高。左右都以玩家自己的左右为准。

## P1：摆臂前进

| | |
| --- | --- |
| 手势 | 双臂举起上下摆 |
| 识别条件 | 两只手的手高都高于 -0.85（不是垂在身侧）；最近 0.65 秒内每只手腕上下摆动超过 0.16（按肩宽归一）；最后一次摆动在 0.28 秒以内 |
| 输出 | `go = 1`；停止摆臂 0.28 秒后变回 0 |
| 游戏作用 | `Forward` -> `DigiPhantLocomotion` 加速前进 |
| 机甲大象 | 走路/跑步动画；推进器火焰按速度变大（走路约 35%，跑步 100%），速度突然变快时多一点爆发；引擎声变大变高；每只脚落地有脚步声 |
| 谁负责 | 自动。`DivaBoosters` 和 `DivaMechAudio` 自己读移动速度，不经过手势连接组件 |

## P2：倾身转向

| | |
| --- | --- |
| 手势 | 双手并拢，和身体一起往左或往右倾 |
| 识别条件 | 两手腕距离小于 0.8；双手中心偏离肩中点超过 0.12（相对校准时的站姿）；身体倾斜方向和手一致；两只手都低于 0.4。髋部必须看得见 |
| 输出 | `steer` 在 -1 到 1 之间，偏 0.45 达到满转 |
| 游戏作用 | `Turn` -> 转向 |
| 机甲大象 | 转向动画；有前进速度时火焰照常，原地转不喷火 |
| 谁负责 | 自动 |

## P3：举手瞄准

| | |
| --- | --- |
| 手势 | 只举起一只手 |
| 识别条件 | 恰好一只手的手高超过 0.65。两只都举或都不举时为 0 |
| 输出 | 举左手 `aim = -1`，举右手 `aim = 1` |
| 游戏作用 | `DivaDemo` 把鼻子左右偏最多 35°（分到每节鼻子骨骼），打靶水滴的方向也偏 `aim × 35°` |
| 机甲大象 | 鼻尖水枪的水柱跟着同一个方向左右转 |
| 谁负责 | `DivaMechGestureLink`（Follow Game Aim） |

## P3：双手张开往前推 = 喷水

| | |
| --- | --- |
| 手势 | 两只手都张开，手臂往前伸 |
| 识别条件 | 两只手都检测到；每只手至少 3 根手指伸直；两条手臂都往前伸（手腕比肩膀靠前 0.2 以上，或肘部角度大于 140°）；手高在 -0.55 到 0.65 之间；不是在做喝水手势 |
| 输出 | `shoot = true` |
| 游戏作用 | 水箱里还有水才算 `Shoot`。第一下马上喷一发；一直保持的话 0.4 秒后开始每 0.12 秒一发；每发用掉水箱 0.8%（满箱约 125 发）。喷水时 `DivaDemo` 把鼻子每节上抬 12° |
| 机甲大象 | 鼻尖水枪：0.12 秒内水压升起，播开喷声，然后循环水流声，喷口闪青光，水柱、水雾、落地水花。松手后 0.12 秒内停下，播停喷声 |
| 弹道 | 跟 `DivaDemo` 的打靶水滴一样：水平速度 17，往上的初速度按喷口高度瞄准大象前方 1.4 米高处，重力 2.5。所以水柱落点就是打靶判定的位置 |
| 打靶水滴 | `DivaDemo` 原来的蓝色小球默认隐藏（Hide Demo Droplets），打中靶子照样计分 |
| 谁负责 | `DivaMechGestureLink`（Spray From Gesture） |

快速张开一次手：`Shoot` 只持续几帧，水枪会喷一小段（升起和落下各 0.12 秒）。一直张着：水柱连续不断，直到水箱空了或松手。

## P3：双手放到嘴边 = 喝水

| | |
| --- | --- |
| 手势 | 双手握拳或半握，合在嘴边 |
| 识别条件 | 两只手都检测到；手高在 -0.03 到 0.65 之间；两手距离小于 1.45；双手中心离嘴小于 0.38，且不比嘴低超过 0.22；每只手最多 2 根手指伸直 |
| 输出 | `drink = true`（这时不会判成喷水） |
| 游戏作用 | 水箱每秒补 30%；`DivaDemo` 把鼻子每节上抬 18°，往嘴边卷 |
| 机甲大象 | 两门泡泡炮冒彩虹泡泡（每门每秒 22 个），开始时一声"啵啵"。泡泡会飘、会上浮，落地破掉 |
| 谁负责 | `DivaMechGestureLink`（Bubbles From Drinking） |

## 不靠手势、一直自动的

| 动画 | 触发 |
| --- | --- |
| 机甲零件 | 挂在骨骼上，跟着所有动画和鼻子、腿的动作走 |
| 脚步声 | 脚骨骼先抬高 0.08 以上、再落回 0.03 以内，算一步 |
| 推进器火焰 | 只看移动速度。测试时把 `DivaBoosters` 的 Boost Override 设成 0~1 |
| 糖果粉皮肤 | `DivaMechToggle` 的 Candy Skin |

## 不开摄像头怎么测试

`DivaDemo` 面板选 **Test controls**：滑块和勾选框（P1 movement、P2 left/right、P3 aim、Spray water、Drink / refill）走的是同一个 `DivaControlState`，所以水枪、泡泡、火焰的反应和真手势完全一样。

## 想改对应关系

- 选中 `Elephant Travel`，在 `DivaMechGestureLink` 上可以分别关掉：手势喷水、水柱跟随瞄准、隐藏打靶小球、喝水冒泡泡。
- 想换一个手势触发泡泡：在别的脚本里设置 `DivaBubbleCannons.gestureBlow = true/false`，或者调用 `Burst(秒数)`。
- 手势阈值改 `TrackingOverrides/diva_gestures.json`，规则改 `diva_gestures.py`。

## 已知限制

- 以上都还没有用真实摄像头完整玩过一遍，只在编辑器里按这些姿势预览过（`Docs~/gesture_*.png`）。
- 水箱空了 `Shoot` 不会成立，水枪也不会喷，这是游戏规则，不是故障。
- 喝水时不会喷水，因为两个手势在识别时是互斥的。
