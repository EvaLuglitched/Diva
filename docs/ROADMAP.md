# Diva — Elephant Shooting Game Roadmap

**Team Diva** · DesComp Project 2 (DigiPhant) · final presentation **Tue 13 Oct**

## Concept

Three performers drive **one** elephant through the instructor's savannah course with
their bodies (MediaPipe pose). The course stays exactly as supplied. We add a playful
**game layer** on top: targets along the route, a trunk "water blaster", a score, and a
task checklist, styled like a bright arena shooter (clean blocks, neon accents, bloom).

## Fixed — do not change

- Course: `Savannah Course [layout 4]`, seed `20261005`, same start line, finish arch and path (4.2 units wide).
- The four course tasks:

  | # | Landmark | Task | Elephant action |
  | --- | --- | --- | --- |
  | 01 | Feeding bush (23% of route) | eat | `eat` clip |
  | 02 | Water basin (49%) | drink | `drink` clip |
  | 03 | Fallen log (74%) | step over | leg lifts |
  | 04 | Finish arch (end) | finish | trumpet |

- Game objects live in a separate root, **`Diva Game Layer`**, never inside the course root,
  so re-injecting the course never deletes our work. Nothing may block the path.
- Elephant travel radius: `stageRadius = 30`, so the whole 60 × 60 course is reachable.

## Who does what

| Area | Owner | Folder |
| --- | --- | --- |
| Game environment, targets, shooting, HUD | Eva | `Assets/DivaGame/` |
| MediaPipe gestures / tracking | MediaPipe teammate | `DivaTracking/`, `TrackingOverrides/` |
| Elephant rig, joints, model | Rig teammate | `Assets/DigiPhant/`, elephant prefab |

To keep merges painless: work inside your own folder, commit `.meta` files, and use the same
Unity version (6000.6.3f1). The game layer is built by a menu command
(**Diva > Game > Build Game Layer in Open Scene**), so it can be rebuilt in any team scene
instead of hand-merging scene files.

## Milestones

- [x] **M0 · Setup (Thu 8 Oct)** — team repo, savannah course injected, `stageRadius` 30, body-gesture triggers (drink / eat / trumpet / lie down / get up).
- [ ] **M1 · Playful environment (Fri 9 Oct)** — arena props around the course (blocks, ramps, neon pylons, arches, floating clouds); sky, sun and post-processing (bloom, colour); all clear of the path.
- [ ] **M2 · Targets & shooting (Sat 10 Oct)** — targets along the route (static, swinging, flying drones); trunk blaster with raycast hits; hit effects; score; 4-task checklist detected at each landmark.
- [ ] **M3 · Integration (Sun 11 Oct)** — shoot gesture from the MediaPipe teammate; muzzle on the trunk-tip bone from the rig teammate; merge into the team scene.
- [ ] **M4 · Playtest & data (Mon 12 Oct)** — full three-person runs; tune gesture thresholds; log each run (task times, targets hit, path deviation) as CSV for the Generate / Interrogate sections.
- [ ] **M5 · Final (Tue 13 Oct)** — side-by-side recording, mapping table, README with Drive link.

## Open questions

- Which gesture shoots? Candidate: P3 thrusts the "trunk" arm forward.
- Which scene is canonical? The team repo uses `Assets/DigiPhant/Scenes/DigiPhant.unity`.

## Third-party assets

- [Kenney Starter-Kit-FPS](https://github.com/KenneyNL/Starter-Kit-FPS) @ `185fd23`: sprites, 3D models and sounds, **CC0**.
