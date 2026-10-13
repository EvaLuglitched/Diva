#!/bin/zsh
# 把三层图标塞进 Unity 导出的 visionOS Xcode 工程（AppIcon.solidimagestack：Back / Middle / Front 三层，各 1024×1024 @2x）。
# 用法：inject_visionos_icon.sh <Builds/visionOS 目录>
set -e
X=${1:?xcode project dir}/Unity-VisionOS/Images.xcassets/AppIcon.solidimagestack
I=$(cd "$(dirname "$0")" && pwd)   # 三层 PNG 和这个脚本放在一起（Assets/DivaShow/Icons/Source~）
for layer in Back Middle Front; do
  case $layer in Back) src=vr_back;; Middle) src=vr_middle;; Front) src=vr_front;; esac
  d=$X/$layer.solidimagestacklayer/Content.imageset
  mkdir -p "$d"
  find "$d" -name "*.png" -delete
  cp "$I/$src.png" "$d/diva_$src.png"
  cat > "$d/Contents.json" <<EOF
{
  "images" : [
    {
      "filename" : "diva_$src.png",
      "idiom" : "vision",
      "scale" : "2x"
    }
  ],
  "info" : {
    "author" : "xcode",
    "version" : 1
  }
}
EOF
done
echo "visionOS icon layers injected into $X"
