# Diva app icon

- `diva_icon.svg`: the design (1024×1024). `../DivaIcon.png` is the macOS / default icon rendered from it with transparent
  corners (Apple's rounded square). To re-render on a Mac without extra tools: `qlmanage -t -s 1024 -o . diva_icon.svg`
  renders on white, so render once on white and once on black and recover the alpha (see the notes in Carl's build tooling),
  or export from any SVG editor with a transparent background.
- `vr_back.png`, `vr_middle.png`, `vr_front.png`: the same design as the three parallax layers of the visionOS icon
  (background, ears + bubbles, head). `inject_visionos_icon.sh <Builds/visionOS>` copies them into the Xcode project
  Unity exports (AppIcon.solidimagestack) before `xcodebuild`.
- Unity's Player Settings point at `DivaIcon.png` for the default and macOS icons (set once, saved in ProjectSettings).
