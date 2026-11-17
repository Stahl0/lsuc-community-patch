UNION CITY COMMUNITY PATCH
Unofficial fan patch for The Last Stand: Union City (The Last Stand: Legacy Collection on Steam).
Not made or endorsed by Con Artist Games.

You need the Steam version of The Last Stand: Legacy Collection installed.
This download contains no game files. It patches your own copy.

WHAT IT DOES
- Runs Union City in Ruffle instead of Adobe AIR. Big hordes no longer cause input lag.
- Fixes the screen turning yellow in fullscreen or large windows.
- Fullscreen by default. Press P to pause (Escape also pauses, but leaves fullscreen).
- Steam playtime, "Now playing", the Steam overlay and achievements still work.
- Press F9 in game for a performance overlay (handy for bug reports).

INSTALL
1. Unzip this folder anywhere (for example Documents\LSUC Community Patch).
2. Double-click LSUCLauncher.exe.
   The first time, it finds your Steam copy, builds the patched game in the "game" folder,
   and offers to copy your Steam save slots over. Your Steam saves are never changed.
3. Use "Play windowed.cmd" if you prefer a window.

PLAY FROM YOUR STEAM LIBRARY (optional)
In Steam: right-click The Last Stand: Legacy Collection > Properties > Launch Options, and enter:
  "C:\full\path\to\LSUCLauncher.exe" %command%
Pressing Play then starts the patched Union City. Clear the box to go back to the original.

SAVES
- The patched game keeps its own saves. Progress does not sync back to the original version.
- Saves are tied to where this folder is. If you move the folder, run
  LSUCLauncher.exe --import-saves again, or copy them from
  %LOCALAPPDATA%\ruffle\SharedObjects\localhost\...

PROBLEMS
- "different version" message: Steam updated the game. Look for a newer patch release.
- launcher.log in this folder records what the launcher did.

CREDITS
The Last Stand: Union City by Con Artist Games.
Ruffle (ruffle.rs), MIT / Apache-2.0, licence in the ruffle folder.
