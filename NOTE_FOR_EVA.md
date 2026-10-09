# Note for Eva (from Carl): how the show uses your intro

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
4. The intro's text, bars and countdown use IMGUI, which Vision Pro mixed reality does not draw. If we demo on Vision
   Pro, they need uGUI/TMP. TMP is now in the project, and `Assets/DivaShow/Runtime/DivaUi.cs` has helpers.

Real-device test list: `PLAYTEST_TODO.md`. Show details (Chinese): `Assets/DivaShow/README.md`.
