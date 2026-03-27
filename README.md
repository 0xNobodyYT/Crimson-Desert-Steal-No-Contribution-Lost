# Crimson Desert Steal No Contribution Lost

Standalone runtime helper for Crimson Desert that prevents contribution from actually dropping when you steal.

It works with the steal keybinds:
- `R`
- `G`

The `-5` message can still appear, but the real contribution value stays unchanged.

## Files

- `crimson_desert_contribution_injector.bat`
- `CrimsonDesertContributionInjector.exe`
- `src/Program.cs`

## How to use

1. Put the BAT and EXE in the same folder.
2. Run `crimson_desert_contribution_injector.bat`.
3. Leave the helper window open.
4. Launch Crimson Desert or switch back to it if it is already open.
5. Steal normally with `R` or `G`.

## How to stop

- Press `F8` in the helper window.
- Or just close the helper window.
- Restart the game to fully clear the runtime patch.

## Notes

- Run it through the BAT so it gets administrator rights automatically.
- This is a runtime mod. It does not patch game files on disk.
- If the game updates, the helper may stop working until the hook offsets are updated.

## Changelog

- Increased the steal protection window to `5000 ms` because some delayed chest loots were slipping through the original shorter window.
