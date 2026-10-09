# Fab 火箭和云：安装说明（给 Carl 和 Vicky，以及你们的 Claude）

天上的大火箭和云来自 Fab：**Stylized Rocket** 和 **Stylized Clouds Pack – Vol 07**。
Fab 授权不允许把原始模型文件放进公开仓库，所以它们**不在 GitHub 上**（`.gitignore` 已排除
`Assets/DivaGame/ThirdParty/Fab/`）。Eva 会私下把 **`Diva_Fab_Assets.zip`** 发给你们。

没装这些文件时，场景会自动显示我们自己的糖果火箭和 Kenney 云作为替身（`DivaModelFallback`），
Console 里会有 "Missing Prefab" 提示，这是正常的。装好后就会显示 Fab 原版。

## 给 Claude 的步骤

把 zip 解压进项目即可，**不需要**重建场景、改场景或改任何代码。请严格按下面做：

1. 先 `git pull`，确认在最新的 `main` 上。
2. 解压位置是 **`Assets/DivaGame/ThirdParty/`**。zip 顶层是 `Fab/` 文件夹和 `Fab.meta`，解压后应得到：
   ```
   Assets/DivaGame/ThirdParty/Fab.meta
   Assets/DivaGame/ThirdParty/Fab/README.md (+ .meta)
   Assets/DivaGame/ThirdParty/Fab/StylizedRocket/Stylized Rocket.fbx (+ .meta)
   Assets/DivaGame/ThirdParty/Fab/StylizedRocket/rocket_candy_albedo.png (+ .meta)
   Assets/DivaGame/ThirdParty/Fab/StylizedRocket/rocket_candy_emission.png (+ .meta)
   Assets/DivaGame/ThirdParty/Fab/StylizedRocket/rocket_norm.png (+ .meta)
   Assets/DivaGame/ThirdParty/Fab/StylizedClouds07/stylized_clouds_pack_vol_07.fbx (+ .meta)
   ```
   例如（项目根目录下运行，zip 路径按实际位置改）：
   ```sh
   unzip -o ~/Downloads/Diva_Fab_Assets.zip -d Assets/DivaGame/ThirdParty/
   ```
   不要用会多套一层文件夹的方式解压（例如得到 `Fab/Fab/...` 或 `Fab 2/`）。
3. **`.meta` 文件必须一起放进去，不能删、不能让 Unity 重新生成。** 场景靠这些 GUID 找到模型。逐个核对：
   ```sh
   cd Assets/DivaGame/ThirdParty/Fab
   grep -m1 "^guid:" "StylizedRocket/Stylized Rocket.fbx.meta"                 # dbfbb29fe97eb4bca93b74bed0e0c7ef
   grep -m1 "^guid:" StylizedClouds07/stylized_clouds_pack_vol_07.fbx.meta     # cc75c1e56200b4d7ea355acf99eb339f
   grep -m1 "^guid:" StylizedRocket/rocket_candy_albedo.png.meta               # 796d4242f4efc4e35ab7d2710453e08a
   grep -m1 "^guid:" StylizedRocket/rocket_candy_emission.png.meta             # c897385afc4d64c6a9e44fb2f72e3047
   cd -
   ```
   每行输出的 guid 应与该行末尾注释里的值相同。
   如果不一致，说明文件放错了或 `.meta` 被重新生成了，重新解压，不要继续。
4. 打开（或重新打开）Unity 项目和 **`Assets/DigiPhant/Scenes/Diva.unity`**。如果 Unity 在解压前已经开着，
   先 **Assets > Refresh**，再重新打开 Diva 场景。此时天上应该是 Fab 火箭（粉色鼻锥、糖果配色）和
   Fab 云，不再是替身；Console 里不应再有 "Missing Prefab"。
5. 检查 git：`git status` 里**不应**出现 `ThirdParty/Fab` 下的任何文件（已被忽略）。如果出现了，
   不要提交，告诉人类组员。

## 不要做的事

- 不要把 Fab 文件提交到 GitHub，不要上传或发给组外的人（授权只允许在项目组员之间私下分享）。
- 不要运行 **Diva > Game > Build Game Layer** 或 **Use Fab Rocket and Clouds** 来"修复"，不需要。
- 没装 Fab 文件时，不要删除场景里的 Missing Prefab 物体，也不要为此保存场景，否则装好后 Fab 模型也回不来。
- 不要用 Blender 重新导出、复制或仿制这两个模型。

相关说明：`Assets/DivaGame/README.md` 的 "Sky: flying rocket and clouds" 一节。
