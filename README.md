# Union City Community Patch

Unofficial fan patch for **The Last Stand: Union City**, as sold on Steam in
*The Last Stand: Legacy Collection*. Not made or endorsed by Con Artist Games.

This repository contains **no game files and no Con Artist code**. Releases ship a small
binary delta that only works on top of the player's own Steam copy.

## CURRENT FEATURES

- Runs Union City in [Ruffle](https://ruffle.rs) instead of Adobe AIR, which removes the
  input lag in big hordes.
- Fixes the screen turning yellow in fullscreen or large windows.
- Fullscreen by default; **P** pauses without leaving fullscreen.
- Keeps Steam playtime, "Now playing", the overlay and achievements, through a small launcher.
- **F9** shows a performance overlay (FPS, worst frame, zombie and object counts).

## INSTALLATION GUIDE

Download the latest release zip, unzip it anywhere and run `LSUCLauncher.exe`.
`README.txt` in the zip has the details, including the Steam launch option.

## TECHNICAL DETAILS

| Part | Where |
|---|---|
| Our ActionScript (edge glow, start without AIR, overlay, Steam socket) | `mod/lscp.as` |
| Where our changes go in the game's bytecode | `mod/recipe.json`, `mod/frame_3-pausekey.pcode` |
| Steam launcher and installer (C#, built with the .NET Framework compiler in Windows) | `launcher/LSUCLauncher.cs` |
| Recipe applier, delta generator, blank SWF for compiling `lscp.as` | `scripts/` |
| Release README and helper files | `release/` |

The launcher finds the Steam install, checks `gamefiles\lsuc.swf` against the SHA-256 the
delta was made for, applies `lsuc.delta` into `game\lsuc.swf` and checks the result.
It then loads the game's own `steam_api64.dll`, registers as app 1615100, starts Ruffle and
listens on `127.0.0.1:47615` for achievement IDs sent by the game over an XMLSocket.
Only Union City's IDs (`ACH_13`-`ACH_18`, `ACH_21`) are accepted.

### CHANGS IN THE CODE

1. `frame_1` script: `mod/lscp.as` is compiled separately and appended as bytecode. It wraps
   `sendSteamMessage` to also notify the launcher, starts the game when no AIR wrapper answers,
   and defines `drawBoundsGlow` and the F9 overlay.
2. `frame_3` main script: key code 80 (P) joins the Escape case of the key handler.
3. `frame_3` main script: the level-sized `blankBlock` (solid `#FFCC00`) with a knockout glow
   filter is replaced by a call to `drawBoundsGlow`, which draws the same edge shading with
   four gradient strips. Past roughly 1700 px of window width the filter surface exceeded GPU
   limits and the raw yellow block showed.

## BUILDING

Requirements: Windows, the Steam game installed, Java 8+, Python 3 (`py` launcher),
[JPEXS FFDec](https://github.com/jindrapetrik/jpexs-decompiler) unzipped to `tools/ffdec`,
and the Ruffle desktop build unzipped to `tools/ruffle`.

```powershell
.\build.ps1 -Version 0.1.0
```

This copies `lsuc.swf` from your Steam library into `original/`, checks its hash, exports the
two scripts as P-code, compiles `mod/lscp.as`, applies the recipe, rebuilds `build/lsuc.swf`,
writes `dist/LSUC-Community-Patch/` with the delta, launcher and Ruffle, and zips it.

For development, `play.cmd` / `play-windowed.cmd` run `build/lsuc.swf` through the launcher,
and `test-achievement.cmd` enables **F8** to send `ACH_14` (pick one you already own).

## Licence

MIT for the patch's own code (see `LICENSE`). The Last Stand: Union City belongs to
Con Artist Games. Ruffle is MIT / Apache-2.0.
