# ArtGen

The penguin, outfit and igloo art is drawn from code: Python writes SVG, ImageMagick renders it to PNG.
These scripts regenerate every sprite in `Assets/Sprites/Characters` and `Assets/Sprites/Props`
exactly (pixel for pixel), so change the art here, never by hand-editing the PNGs.

Unity does not import this folder (it lives outside `Assets`).

## Requirements

- Python 3.12 with Pillow
- ImageMagick 7 (`magick` on `PATH`) with SVG support

Run the scripts with `python -I <script> ...`. Each script adds its own folder to the import path. Outputs go to the
folders you pass, so write to a scratch folder and copy the results into `Assets` yourself.

## Penguin (`penguin/`)

| Script | Purpose |
| --- | --- |
| `penguin.py` | Base penguin parts (feet, flippers, body, belly, eyes, beak) as separate SVG groups |
| `outfit.py` | Beanie and sweater (white, tinted per player in game), plus the flipper clip paths for dressing |
| `run.py` | Run cycle rig: kinematics, springs, and a per-point deformation field. Also the cloth skinning at the near armhole |
| `idle.py` | Idle clips on the same rig: the shared breath plus fidgets (blink, double blink, flap, stomp, look) |
| `export_penguin.py <out>` | `Penguin.png`, `PenguinOutfit.png` (1024 canvas), and the `PenguinRun.png` / `PenguinOutfitRun.png` sheets (4x4 of 256 px) |
| `export_idle.py <work> <out>` | Renders the idle frames into `<work>/tmp` and packs `PenguinIdle0..3.png` / `PenguinOutfitIdle0..3.png` (8x8 of 256 px) |
| `preview_idle.py <work>` | After `export_idle.py`: one GIF per clip, a contact sheet, and an 8-penguin group GIF with random phases |
| `faint.py` | Knocked-out clips on the idle rig: dizzy walk loop (spiral eyes, stars), the fall backwards, lying down. Timings must match `PenguinRunner.cs` |
| `export_faint.py <work> <out>` | Renders the knocked-out frames into `<work>/tmp` and packs `PenguinDizzy/Faint/Down.png` and their `PenguinOutfit…` sheets (cropped cells). Prints each sheet's cell size and feet pivot, and the timings |
| `preview_faint.py <work> [loops] [color]` | After `export_faint.py`: the dizzy walk, fall and lying as one GIF plus a contact sheet |
| `check_run_cover.py <work> [frames]` | Run frames: counts body pixels showing where the sweater should cover |
| `check_face.py <work>` | Run frames: the outfit never covers the beak or the eyes |
| `check_idle_cover.py <work>` | After `export_idle.py`: counts body pixels showing below the collar in every idle frame |

Every animated path uses absolute `M`/`C`/`L`/`Z` commands only, because the deformation moves path points one by one.

Every body and outfit layer shares one canvas and pivot. Unity imports them with:

- Still sprites: 1250 pixels per unit, custom pivot (0.5, 0.0156), which is the feet.
- Sheets: sliced into 256 px cells with the same pivot, at 312.5 pixels per unit.
  - Run sheets: `PenguinRun_00..15`.
  - Idle sheets: `PenguinIdle_000..251`, numbered across sheets.
- Clip order and frame counts must match `PenguinIdle.cs`; frame timings must match `PenguinRunner.cs`.
- Knocked-out sheets: cells cropped per clip (sizes and pivots printed by `export_faint.py`), sliced
  `PenguinDizzy_00..23`, `PenguinFaint_00..28`, `PenguinDown_00..35` (outfit sheets alike), same 312.5 pixels per unit
  and feet pivot.

## Igloo (`igloo/`)

| Script | Purpose |
| --- | --- |
| `igloo.py <out.svg> [door deg]` | Igloo from a small 3D model with an orthographic camera (tunnel turned 25°, camera 23.6° up) |
| `export_igloo.py <out>` | `Igloo.png` (doorless body, 1254 canvas), `IglooDoorFront.png` / `IglooDoorBack.png` (cropped), `IglooWindowDark.png` (lamp-off window pane, cropped, centre pivot), `IglooWindowHeal.png` (mint heal pane, same crop) and the heal particles `HealPlus.png` / `HealSparkle.png` (128 px, centre pivot). Prints the door pivot, hinge, window, pane centre, dome apex and axes |
| `export_doorway.py <out>` | `IglooDoorway.png`, the doorway mask with its pivot on the hinge. Prints the ground-plane numbers used by `IglooGround.cs` |
| `igloo_hull.py <Igloo.png> 0.8` | Prints `GameLayout.IglooHull` (convex hull, alpha >= 64) as C# |
| `preview_door.py <out>` | Door open/close flipbook SVG and frames |
| `preview_action.py <work> [seconds]` | Night action seen from outside (lamp flicker and shake) as day/night GIFs plus a timeline; its curves match `IglooAction.cs` |
| `preview_heal.py <work> [seconds]` | Night heal seen from outside (lamp dims, heartbeat swell with a mint window glow, hop, rising plus signs and sparkles) as day/night GIFs, key frames and a timeline; its curves and particles match `IglooAction.cs` (`Kind.Heal`) |

Igloo sprites (heal particles too) are 300 pixels per unit. The body's pivot is the base center. The door and doorway pivots are on the hinge. When the igloo art changes:

1. Re-export with `export_igloo.py` and `export_doorway.py`.
2. Update the printed fractions in `GameLayout.cs` (`IglooWindow`, `IglooWindowDark`, `IglooDoorHinge`, `IglooApex`), the door sprites' pivot, and `IglooGround.cs`.
3. Regenerate `IglooHull` with `igloo_hull.py`.
4. Run `GameLayoutChecks.Run()` in the Editor.
