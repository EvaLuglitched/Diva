# Diva Game — town & shooting layer

A stylised town wrapped around the instructor's savannah course, with tech touches for a
shooting game. The course keeps its start line, path, finish arch, trees and four landmark
tasks at their exact positions; only its colours change. All game objects live under one
separate root in the scene: **`Diva Game Layer`**.

## Art direction

Candy fantasy town under a starry space sky: macaron pastels (strawberry milk, mint, peach, lavender, baby blue),
bubblegum and coral roofs, raspberry timber, cherry-blossom trees, pink screens, with neon
cyan/magenta light strips. Value still steps up from back to front, so the course reads first:

| Layer | Look |
| --- | --- |
| Sky | Night in space: a generated star panorama (`Editor/DivaSpaceSky.cs` → `Textures/Diva Space Sky.png`) with an indigo-to-violet gradient, a candy-pink glow on the horizon, a pink/cyan nebula band, thousands of stars, four-point sparkle stars, a ringed candy planet and a small moon. It turns slowly in Play mode (`DivaSkyRotate`). Delete the PNG and rebuild to regenerate; the old pink day sky is still the `Diva Sky` material |
| Background | Two rows of pastel buildings: half-timbered townhouses, then taller lilac tech blocks with pink screens, plus clock towers; violet haze matching the sky's horizon (fog 85–300 m) |
| Middle | Lavender ring street, pink paving, icing-white kerbs, tech street lamps, poles with pink string lights, "WELCOME / DIVA SAFARI" banners, stone railing |
| Course | Light grey blue-violet plaza; holo billboards, kiosks with benches, blossom planters, butter-yellow crates, candy-cane barriers in the same spots as before |
| Foreground | Bright cream path with cyan neon edges that run parallel to both sides and meet in mitred corners (a glowing bead on each joint), candy gumdrops every 3 m and a lollipop outside every bend; a round candy start pad under the elephant (pink rim, cream disc, lilac centre, a slowly turning dashed neon ring, gumdrops and two lollipops); arches with rainbow neon bands; classic red/white targets with orange neon halo rings and a point light each (it fades while a target is down), the strongest accent in the scene; a soft light on the elephant |

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

## Playable scene and gestures

The layer is also built into **`Assets/DigiPhant/Scenes/Diva.unity`**, which has the Diva three-player
gestures and the D.Va mech elephant; `TeamScene` now points there, so the command-line `BuildInTeamScene`
rebuilds the playable scene (or open Diva.unity and use **Diva > Game > Build Game Layer in Open Scene**). In Diva mode:

- P3's spray gesture calls `DivaLaserBlaster.Fire(aim)` on every water burst, aimed by P3; the trunk water
  jet shows the shot and the beam is hidden (`showBeam`). Space and the HUD button still work.
- Course tasks use the gestures (the body sliders are not driven in Diva mode): hands to mouth (P3 drink)
  counts as curling the trunk for the eat/drink tasks, P1 pumping counts as lifting the legs at the log.
- DivaDemo no longer spawns its own five placeholder spheres when these targets exist.

## What changed in `Assets/DigiPhant/Scenes/DigiPhant.unity`

| Change | Where |
| --- | --- |
| New root **Diva Game Layer**: streets, town, street dressing, course perimeter, plaza props, arches, targets, path lights, clouds, post-processing volume, elephant light, blaster + game manager | Hierarchy |
| Course recoloured from the shipped prefab's materials (geometry and positions untouched). **Diva > Game > Restore Course Colours** undoes it | `Savannah Course [layout 4]` renderers |
| Elephant travel radius 15 → **30** | DigiPhant Controls → Locomotion → Stage Radius |
| Purple-blue procedural sky, trilight ambient, linear fog, warm soft-shadow sun (colour 1/.9/.78, intensity 1.5) | Lighting settings, Key Light |
| Post-processing on the main camera; far clip ≥ 400 | Main Camera |

## Race intro (`Runtime/DivaIntro.cs`)

Plays when entering Play (about 20 s), kart-racer style:

| Time | Shot | On screen / sound |
| --- | --- | --- |
| 0 – 3.5 s | Aerial, slowly circling the town (no haze) | "DIVA SAFARI · Candy Town Course" banner, chime |
| 3.5 – 6.5 s | Camera beside the rocket's path; it crosses the frame side-on, with the sky turned so the ringed planet is in the upper left | whoosh |
| 6.5 – 9 s | Swoop down over the course to the start line | lower whoosh |
| 9 – 13 s | One full orbit of the mech elephant | parts power up one by one (dark → flash → lit) with rising chirps |
| 13 – 15.5 s | Low hero close-up, pushing in | thrusters ignite: smoke billows out of both nozzles, a dust ring rolls out along the ground, sparks, a pink-orange flash, flames twice as big for a moment, a short camera shake |
| 15.5 – 20 s | Settles into the game camera, looking up a little more during 3-2-1 | 3 · 2 · 1 · GO! with beeps and a fanfare; the rocket flies across the sky straight ahead of the elephant (its orbit is timed at the start of the intro) |

The elephant is held on the start line until GO; panels and HUD are hidden until then. **Esc** skips
to the countdown, **I** replays (ignored when Diva Show is in the scene, since Diva Show starts the intro after
its skin select). All sounds are synthesised in code.

The bars, banner, skip hint and countdown are a uGUI canvas with TextMesh Pro (not IMGUI), so they also draw
on Vision Pro; they use Diva Show's font when it is in the scene. **Diva > Game > Render Intro Frames** saves
keyframes, text included, to `Recordings/preview/intro-*.png`. **Diva > Game > Play Intro Now** starts the
intro straight away in Play mode (skipping the skin select), for testing.

Diva Show calls `Begin()`, `Skip()`, `Playing`, `Time`, `playOnStart`, `SwoopEnd`, `BootStart`, `OrbitEnd` and
`Go`: timings can change, names and meaning must stay.

Untick *Play On Start* on the Diva Intro component
(on `Diva Game Layer`) to turn it off.

## Sky: flying rocket and clouds

A giant rocket (70 m, `rocketLength` in the model slots) hovers above the centre of the map, flying
diagonally with its nose 45° up, slowly turning and bobbing (moves in Play mode), with a glowing exhaust trailing behind. It is the Fab **Stylized Rocket**, recoloured to the candy palette
(`Art/Blender/recolor_rocket.py`: pink nose and fins, pink-white body, mint rivets, glowing pink portholes).
The 8 clouds come from the Fab **Stylized Clouds Pack – Vol 07** with a candy cloud material.

**These Fab files are not in git** (licence: no redistributing raw files in a public repo). Each teammate
downloads them into `Assets/DivaGame/ThirdParty/Fab/` as described in that folder's README, then runs
**Diva > Game > Use Fab Rocket and Clouds**. Without them the scene shows the kit rocket and Kenney clouds
automatically (each Fab model has a stand-in beside it, switched by `DivaModelFallback`).

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
