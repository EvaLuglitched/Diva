# Diva Game — town & shooting layer

A stylised town wrapped around the instructor's savannah course, with tech touches for a
shooting game. The course keeps its start line, path, finish arch, trees and four landmark
tasks at their exact positions; only its colours change. All game objects live under one
separate root in the scene: **`Diva Game Layer`**.

## Art direction

Candy fantasy town: macaron pastels (strawberry milk, mint, peach, lavender, baby blue),
bubblegum and coral roofs, raspberry timber, cherry-blossom trees, pink screens, with neon
cyan/magenta light strips. Value still steps up from back to front, so the course reads first:

| Layer | Look |
| --- | --- |
| Background | Two rows of pastel buildings: half-timbered townhouses, then taller lilac tech blocks with pink screens, plus clock towers; soft pink haze (fog 85–300 m) |
| Middle | Lavender ring street, pink paving, icing-white kerbs, tech street lamps, poles with pink string lights, "WELCOME / DIVA SAFARI" banners, stone railing |
| Course | Light grey blue-violet plaza; holo billboards, kiosks with benches, blossom planters, butter-yellow crates, candy-cane barriers in the same spots as before |
| Foreground | Bright cream path with cyan neon edges and pink runway lights; arches with rainbow neon bands; classic red/white targets with orange neon halo rings and a point light each (it fades while a target is down), the strongest accent in the scene; a soft light on the elephant |

All colours live in the `Looks` table and `RecolourCourse` / `BuildLook` in `Editor/DivaGameBuilder.cs`;
changing them and rebuilding never moves anything.

## Pipeline

1. **Blender** — `Art/Blender/build_town_kit.py` builds 26 detailed, bevelled models (6 townhouses,
   2 tech blocks, clock tower, props, targets, arch) with metre-scale UVs and exports them to
   `Models/Town/*.fbx`. The editable source is `Art/Blender/diva_town_kit.blend`. Re-run:
   ```sh
   /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python Art/Blender/build_town_kit.py -- "$PWD/Assets/DivaGame/Models/Town" "$PWD/Art/Blender/diva_town_kit.blend"
   ```
2. **Textures** — `Editor/DivaTextures.cs` generates tileable colour + normal maps (plaster, brick,
   roof tiles, paving, asphalt, metal panels, wood, concrete, hazard stripes) into `Textures/`.
3. **Unity** — **Diva > Game > Build Game Layer in Open Scene** maps every kit material name to a
   textured URP material (`Looks` table in `Editor/DivaGameBuilder.cs`), places the town and props,
   recolours the course and sets lighting. It replaces only `Diva Game Layer`, then reloads the scene.

## What changed in `Assets/DigiPhant/Scenes/DigiPhant.unity`

| Change | Where |
| --- | --- |
| New root **Diva Game Layer**: streets, town, street dressing, course perimeter, plaza props, arches, targets, path lights, clouds, post-processing volume, elephant light, blaster + game manager | Hierarchy |
| Course recoloured from the shipped prefab's materials (geometry and positions untouched). **Diva > Game > Restore Course Colours** undoes it | `Savannah Course [layout 4]` renderers |
| Elephant travel radius 15 → **30** | DigiPhant Controls → Locomotion → Stage Radius |
| Purple-blue procedural sky, trilight ambient, linear fog, warm soft-shadow sun (colour 1/.9/.78, intensity 1.5) | Lighting settings, Key Light |
| Post-processing on the main camera; far clip ≥ 400 | Main Camera |

## Race intro (`Runtime/DivaIntro.cs`)

Plays when entering Play (about 16 s), kart-racer style:

| Time | Shot | On screen / sound |
| --- | --- | --- |
| 0 – 3.5 s | Aerial, slowly circling the town (no haze) | "DIVA SAFARI · Candy Town Course" banner, chime |
| 3.5 – 6 s | Fixed camera as the rocket flies past | whoosh |
| 6 – 8.5 s | Swoop down over the course to the start line | lower whoosh |
| 8.5 – 12.5 s | One full orbit of the mech elephant | parts power up one by one (dark → flash → lit) with rising chirps; thrusters ignite |
| 12.5 – 16 s | Settles into the game camera | 3 · 2 · 1 · GO! with beeps and a fanfare |

The elephant is held on the start line until GO; panels and HUD are hidden until then. **Esc** skips
to the countdown, **I** replays. All sounds are synthesised in code. **Diva > Game > Render Intro Frames**
saves keyframes to `Recordings/preview/intro-*.png`. Untick *Play On Start* on the Diva Intro component
(on `Diva Game Layer`) to turn it off.

## Sky: flying rocket and clouds

A giant rocket (70 m, `rocketLength` in the model slots) hovers above the centre of the map, flying
diagonally with its nose 45° up, slowly turning and bobbing (moves in Play mode), with a glowing exhaust trailing behind. It is the Fab **Stylized Rocket**, recoloured to the candy palette
(`Art/Blender/recolor_rocket.py`: pink nose and fins, pink-white body, mint rivets, glowing pink portholes).
The 8 clouds come from the Fab **Stylized Clouds Pack – Vol 07** with a candy cloud material.

**These Fab files are not in git** (licence: no redistributing raw files in a public repo). Each teammate
downloads them into `Assets/DivaGame/ThirdParty/Fab/` as described in that folder's README, then runs
**Diva > Game > Use Fab Rocket and Clouds**. Without them the scene uses the kit rocket and Kenney clouds.

## Swap in your own models

Select **`Assets/DivaGame/Settings/Diva Model Slots`** and drag prefabs into slots (houses, tech
building, landmark, billboard, kiosk, planter, crates, arch, target board, drone, course trees),
then rebuild. Each replacement goes to the same position, scaled to the same height. Check each
pack's licence first; Fab packs marked *"Allows usage with AI: No"* should be chosen and placed
by you, not analysed or imitated by an AI tool.

## Notes

- This project's render pipeline uses the **GPU Resident Drawer**. After large asset changes the
  editor can keep showing deleted objects or magenta materials; reopen the scene or restart Unity.
- **Diva > Game > Render Preview Shots** saves stills to `Recordings/preview/` (git-ignored).
- Press Play: HUD at the top right (score, time, four tasks); **Space** fires the trunk laser at the
  nearest standing target ahead.

## Files

| Path | Purpose |
| --- | --- |
| `Runtime/DivaTarget.cs`, `DivaLaserBlaster.cs`, `DivaGameManager.cs` | Targets, auto-aimed laser, score/tasks/HUD |
| `Runtime/DivaPop.cs`, `DivaFloat.cs`, `DivaFollow.cs` | Hit burst, bobbing props, elephant light follow |
| `Editor/DivaGameBuilder.cs` | Builds the layer, material looks, course recolour, previews |
| `Editor/DivaTextures.cs`, `Editor/DivaMeshes.cs` | Procedural textures; metre-UV boxes |
| `Editor/DivaModelSlots.cs` | Replacement-model slots |
| `Models/Town/` | Blender kit FBX (source: `Art/Blender/`) |
| `ThirdParty/Kenney/` | CC0 cloud model, sprites and sounds (see `CREDITS.md`) |
