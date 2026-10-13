# Note for Eva (from Carl): how the show uses your intro

## 2026-10-13 (evening) — v1.1: downloads that run directly, smoother with the camera, lighter opening blur

Carl played a live round on the release build with the camera and asked for downloads that run directly on Mac and
Windows. What changed on shared things:

| Where | What | Why |
| --- | --- | --- |
| `DivaShowPerf.cs` | Keeps full resolution when lowering it does not make frames faster | with the camera tracking running, v1.0 dropped to 60 % resolution and a 60 fps lock for the whole race, although the CPU, not the GPU, was the limit |
| `DivaLook.cs`, `DivaShowDirector.cs` | Your intro flight no longer gets the gameplay far-field blur (it fades in at GO); cutscene bokeh f/4 → f/5.6 | Carl found the opening a bit too blurred: your aerial shots see the whole town past 35 m, so all of it went soft. Play is unchanged |
| `DivaShowMute.cs` (new) | `DIVA_MUTE=1` or `-divaMute` mutes the desktop game | silent test runs |
| `Tools/bridge_app/` | The Windows download carries the camera bridge as `diva_bridge.exe` (no Python); the Mac bridge keeps its font cache | Windows players needed Python before; the Mac's first camera frame came 15 s late on every launch |
| `README.md`, `Tools/release/READ ME*` | v1.1 links and first-start steps (macOS "Open Anyway", Windows SmartScreen) | the Mac app is signed but not notarised (no Developer ID) |

Nothing in the gesture rules, the three-player detection or your intro code changed. The About-box text below still
needs you (owner only).

**Product page:** `docs/index.html` is a game page with the v1.1 downloads, how to play, the Vision Pro version and how the
three of us built it (a dated timeline written as one team, from the git history). To put it online, please turn on GitHub Pages
(owner only): Settings > Pages > Deploy from a branch > `main`, folder `/docs`. It then appears at
https://evaluglitched.github.io/Diva/ and the README already links there.

## 2026-10-13 (afternoon) — v1.0: README per the course, fresh-clone test, downloads for macOS / Windows / Vision Pro

Carl asked to tidy the repository the way the instructor's `STUDENT_GIT.md` asks and to publish v1.0 with download
links. What changed on shared things:

| Where | What | Why |
| --- | --- | --- |
| `README.md` | Rewritten as the course front page: Git + **Drive** links, starter URL and pinned commit, movement → control table, reconstruction steps and their tested result, repository map, downloads | `STUDENT_GIT.md` and ROADMAP M5 ("README with Drive link"). The Drive link is still a TODO: please paste the folder link if you have it |
| `DIGIPHANT_SETUP.md` | Back at the root (it was in `docs/` for a few hours); 2026-10-13 fresh-clone test recorded | the starter expects it at the project root |
| `Tools/setup_tracking_mac.sh` | Picks Python 3.10+ and runs our tests in `TrackingOverrides` | a fresh clone with macOS's own Python 3.9 failed three of the locked bridge's three-player tests; with 3.13 all 93 pass. Nothing in the bridge or the gesture rules changed |
| `DigiPhantCameraPreview.cs` | Release builds start the bridge themselves (editor path unchanged) | the downloads should work with the camera, not only in the editor |
| `Tools/bridge_app`, `Tools/release`, `DivaReleaseBuild.cs` | Freeze the bridge for the Mac app, build and pack the downloads (menu **Diva > Release**) | v1.0 |

**Please change the repository's About box** (only the owner can; GitHub refused it for Carl's write access):
description *"Team Diva (Eva, Carl, Vicky) · DesComp Project 2 DigiPhant: a mech-elephant race through Candy Town,
driven by body gestures (Unity 6 URP + MediaPipe). Play builds for macOS, Windows and Vision Pro under Releases."*,
topics `unity`, `mediapipe`, `gesture-recognition`, `digital-twin`, `visionos`. The GitHub front page also shows `main`'s
README, so the new README appears there once PR #6 is merged.

## 2026-10-13 — bug fixes, frame rate and a 3A look on the MacBooks (branch `carl/race-meta`)

Carl asked for a full review, then for the bugs fixed and the picture / frame rate tuned for M1 / M2 Max MacBook Pros
(and Vision Pro). Nothing in the locked gesture files changed; `DivaGameManager` is identical to `main` again on both
branches. Shared things we touched, so you know what to expect after pulling:

| Where | What | Why |
| --- | --- | --- |
| `ProjectSettings/QualitySettings.asset` (PC level) | vSync on, realtime reflection probes on | 120 fps on ProMotion with even frame pacing; the town reflection probe never rendered with probes off |
| `Assets/Settings/PC_RPAsset.asset` | opaque texture off, shadow map 4096, shadow distance 60 | the opaque copy was a full-screen copy every frame and only the ice wall's refraction needs it (requested per camera on demand now); sharper shadows |
| `Assets/Settings/PC_Renderer.asset` | SSAO downsample on | half-resolution AO, visually the same, much cheaper on a 3456×2234 screen |
| `DivaKeyboardControls` (your file) | legend text only: the ultimate key is **E** | **U** was both "aim left" and "fire the ultimate", so aiming left at full charge fired it |
| `DigiPhantLocomotion.Evaluate` | caches the `DivaDemo` reference | it ran `GetComponent` every frame |

Gameplay fixes: ON FIRE's x1.5 now also counts for targets the MEGA BUBBLE hits (the popup already showed it); the golden
target is only picked among targets ahead (±60°, 6–45 m) instead of ones you already passed; the snow banks span the
whole blocked width so a skipped ice column never feels like an invisible wall.

Performance (Diva Show layer only): pooled particle flashes / pops / score popups / feed rows, HUD texts rebuilt only when
their value changes, sound synthesis on a background thread, candy materials shared, cached lookups. New runtime
component `DivaShowPerf` (vSync + adaptive URP render scale, never below 0.6, steady half rate as the last resort) and `DivaLook` gained a light far-field gameplay
depth of field (Gaussian from 35 m) plus camera motion blur; both are off in VR. Details: `DIVA_TRY_IT.md` → "Picture and frame rate".

## 2026-10-12 — race extras, shared between PC and Vision Pro (branch `carl/race-meta`)

Carl wanted the Vision Pro extras in the PC game too, so the rules now live in the shared Diva Show layer and the VR
branch will only add its cockpit presentation on top. Nothing in the scene changed; nothing in the locked gesture
files changed. What players see (details in `DIVA_TRY_IT.md` → "Race extras", Chinese in `Assets/DivaShow/README.md`):

- candy (+15, rising "ding" pitch) and small rockets on the route (rocket = 2.5 s dash; a dash also follows every
  MEGA BUBBLE), ON FIRE (score x1.5 for 12 s), GOLDEN TARGET (3x for 10 s), low-water warning, coach tips + a beacon
  and waypoint on the next stop, lap medals (gold ≤50 s / silver ≤80 s / bronze), a PLAY OF THE RACE hero card at the
  finish (replaces the old finish banner), aim assist (the locked target stays locked while spraying), water .022 per
  burst instead of .008.
- an **ice wall** 14 m before your finish arch (Mei-style; `DivaShowWall.cs` + `Resources/DivaIce.shader`): procedural
  crystal columns with a custom URP ice shader (refraction on PC, cracks, frost, reflections, shadows). The elephant is
  held at it (position clamp in Diva Show's LateUpdate, no collider, your locomotion untouched); only the MEGA BUBBLE
  smashes it, with a short slow-motion from the ultimate's side-rear camera; pushing against it charges the ultimate so
  nobody gets stuck. Width is measured from the buildings on each side at start, and a column is only placed where no
  prop stands (umbrellas, signs, targets), so it never clips through your props. If you move the arch, the wall follows.

Files to read: `Assets/DivaShow/Runtime/DivaShowMeta.cs` (rules), `DivaShowCoach.cs`, `DivaShowHero.cs`,
`DivaShowPickups.cs`; small hooks in `DivaShowPlay.cs`, `DivaShowHud.cs`, `DivaShowEnd.cs`, `DivaShowDirector.cs`,
`DivaShowUi.cs`, `DivaText.cs`; `DivaShowSound.cs` is the VR branch's richer version (same API plus pitch, 3D, loops).

Two tiny additions to your files, both optional and off by default:
- `DigiPhantLocomotion.externalForward` (not serialised): a minimum forward input Diva Show sets to 1 during a dash,
  so the elephant charges even if P1 stops pumping. Everything else in `Evaluate` is as before.
- `DivaLaserBlaster.targetOverride` (not serialised): if set, picks the target for each shot instead of `FindTarget`.
  Diva Show uses it for the sticky lock; without Diva Show the blaster behaves exactly as before.

After pulling: nothing to rebuild. The new numbers are Inspector fields on *Diva Show* ("Race meta") with defaults, so
your saved scene needs no edit. Tested: a full automatic round in batch mode (keyboard path, test mode) plus the four
validators. Not tested: how it feels with the real camera (is ON FIRE too easy? are the medal times right?). Please
tell me if the hero card should keep your "COURSE COMPLETE · tasks" wording instead of PLAY OF THE RACE.


Hi Eva, Carl here. This push adds the full match flow (`Assets/DivaShow`) and connects it to your race intro.
We changed none of your files. Read this before your next push so our next merge stays easy.

## The flow now

```
Play → skin select (Diva Show) → your DivaIntro (aerial, rocket, swoop, orbit + power-up, hero close-up, 3·2·1·GO)
     → game (Diva Show HUD, skills, ultimate) → finish → highlight replay → results → skin select again
```

- The whole opening is yours. We only added Carl's entrance sting: its big hit lands at `DivaIntro.SwoopEnd`, so it
  starts at `SwoopEnd - 1.24 s`. That is 9.0 s with your current timeline.
- Your intro's GO starts our run (timer, score reset, HUD).
- Our own power-up and countdown are only used in scenes without a `DivaIntro`.
- The skin select no longer shows the DIVA SAFARI title. Your banner is the title.

## What Diva Show relies on in `DivaIntro`

Please keep these public and with the same meaning (timings can change freely):
`Begin()`, `Skip()`, `Playing`, `Time`, `playOnStart`, `SwoopEnd`, `BootStart`, `OrbitEnd`, `Go`.

- At runtime Diva Show sets `playOnStart = false` and calls `Begin()` after the skin is chosen. Your component in the
  scene is not edited.
- Both hands up, Enter or Esc call `Skip()`.
- Pressing your **I** key replays the intro and Diva Show follows it back to the intro (it works). You may still want to
  ignore I while a `DivaShowDirector` is in the scene.
- `DivaSkyCamera` stays as it is during gameplay. Diva Show runs at execution order 1500 (after your sky camera at
  1000, before your intro at 2000), so only our cutscene cameras skip the tilt.
- Diva Show hides the setup panel and your IMGUI HUD (it has its own HUD). Your intro saving and restoring those flags
  still works.

## What we added to shared things

| Where | What |
| --- | --- |
| `Diva.unity` | Only additions: `DivaMechSkins` on *Elephant Travel*, and a root object *Diva Show* (`DivaShowDirector`, `DivaLook`) |
| `Assets/TextMesh Pro` | TMP essential resources (needed by the new UI) |
| `Assets/Settings/PC_Renderer.asset` | SSAO intensity 0.4 → 0.6, radius 0.3 → 0.4 |
| `DivaDemo`, `DivaControlState` | Ultimate gesture, one-player "All" mode, `inputLocked`, `cameraHeld` |
| `TrackingOverrides/` | The `ult` gesture (both hands above the head for 0.8 s) |

`DivaLook` adds bloom, grading, a rim light on the elephant, town reflections, SMAA, and depth of field in cutscenes.
It works on top of your Diva Post FX and lighting without changing them. Untick it to compare.

**If your builder rebuilds `Diva.unity` and our two objects disappear**, or you get a merge conflict on the scene:
keep your scene, then run **Diva > Rebuild D.Va Mech Skins** and **Diva > Show > Install Show in Open Scene** and save.

## Things we noticed on our side

1. **Fab rocket and clouds are not in the repo.** On other machines the scene shows *Missing Prefab* for
   `Stylized Rocket` and `stylized_clouds_pack_vol_07`, so the rocket fly-by shows empty sky. Options: commit them if the
   license allows, or make the builder fall back to the kit rocket and Kenney clouds when the Fab files are missing.
2. On a machine without those Fab files, saving the scene rewrites one cloud prefab reference (`fileID`). We put yours
   back before committing. Watch for it in diffs.
3. The `.mat` files in `Assets/DivaGame/Materials` still lose `_EMISSION` whenever Unity saves them in batch mode. Your
   runtime `RestoreEmission()` covers Play mode.
4. **The Main Camera clears to a solid colour** (dark navy `0.055, 0.085, 0.11`), so your procedural purple-blue sky is
   never visible in Play (your Render Intro Frames uses its own camera, which shows the sky). `DivaLook` switches the
   camera to Skybox at runtime. If you agree, set *Background Type: Skybox* on the Main Camera in your builder.
5. The intro's text, bars and countdown use IMGUI, which Vision Pro mixed reality does not draw. If we demo on Vision
   Pro, they need uGUI/TMP. TMP is now in the project, and `Assets/DivaShow/Runtime/DivaUi.cs` has helpers.

Real-device test list: `PLAYTEST_TODO.md`. Show details (Chinese): `Assets/DivaShow/README.md`.

## 2026-10-11 — the Vision Pro branch (`carl/diva-vr`)

Hi Eva, Carl here again. I pushed the Vision Pro version as its own branch. **Please don't merge it into `main`**:
it adds `com.unity.xr.visionos` 3.3.1 + `com.unity.xr.hands` 1.9.0 to `Packages/manifest.json`, and visionOS
support needs a Unity Pro licence (a Personal licence refuses to open the project with that package). Nothing on
`main` changes; you don't have to do anything after pulling unless you want to look at the branch.

How it plays (controls, menu, tutorial) is in `DIVA_TRY_IT.md` → "Vision Pro version"; the code map, build chain and
the gotchas are in `Assets/DivaVR/README.md`.

**It borrows your intro idea.** The VR opening is a first-person version of your lap around the town: three still
shots (overview with the task beacons → behind the elephant → low hero shot) joined by dusk fades, then you land in
the cockpit. Your `DivaIntro` itself is not used in VR (a moving camera makes people dizzy in a headset), and your
scene objects are untouched.

**Shared files the branch edits** (all small, all behind VR-only checks, so the PC game behaves the same):

- `Assets/DivaGame/Runtime/DivaGameManager.cs` — new `ForceFinish()` (VR counts the run as finished at the finish
  arch even with tasks left).
- `Assets/DivaGame/Models/Town/*.fbx.meta` (18 files) — `generateMeshLods: 1` (Unity 6 mesh LODs for the
  headset's triangle budget). Unity will reimport those 18 models once after you pull; the town looks the same.
- `Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs` — the animation blend is critically damped (the old exponential
  blend jolted when the elephant started/stopped right in front of a VR player) and the input dead-zone snap only
  applies when stopping (at 90 fps the tiny per-frame steps never got past the snap).
- `Assets/DigiPhant/Runtime/DivaControlState.cs` — `IndependentHands` (in VR spraying does not stop the run);
  `Assets/DigiPhant/Runtime/DivaDemo.cs` — `externalInput` so the VR rig can feed the control state.
- `Assets/DivaShow/*` — my own show code: VR branches in the director/HUD/UI, a VR text shader hook, synthesized
  sounds (race loop, candy, boom, cheer, …), a Chinese font subset in `DivaShow/Resources/DivaCJK.ttf`.
- `Assets/link.xml`, `ProjectSettings/ProjectSettings.asset` (a visionOS icon entry), `Packages/*`.

**Known / untested:** the headset runs at a locked 45 fps (your town has no LODs; see the README); the latest build
with the demo reel and the console joystick is still waiting for a headset test. The scene also references
`stylized_clouds_pack_vol_07` (10 instances) and `Stylized Rocket` prefabs that are not in the repo (not on `main`
either) — if those are from a Fab pack you have locally, the fallbacks you added cover it; just flagging it.
