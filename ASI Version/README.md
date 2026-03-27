# ASI Version

This is the alternate ASI-loader version of the mod.

## Files

- `xinput1_4.dll`
- `CrimsonDesertContributionGuard.asi`
- `src/CrimsonDesertContributionGuard.cpp`

## Install

Put both of these files in the same folder as `CrimsonDesert.exe`:

- `xinput1_4.dll`
- `CrimsonDesertContributionGuard.asi`

For Crimson Desert, that means the `bin64` folder.

## What each file does

- `xinput1_4.dll`
  - This is the ASI loader.
  - It loads `.asi` plugins automatically when the game starts.
  - This loader is from `Ultimate ASI Loader` by ThirteenAG.

- `CrimsonDesertContributionGuard.asi`
  - This is the actual mod.
  - It protects contribution when you steal with `R` or `G`.
  - It uses a `5000 ms` protection window so delayed chest loots are covered too.

## Notes

- This is runtime-only. It does not patch game files on disk.
- The `-5` message can still appear, but the real contribution value stays unchanged.
- If the game updates, the offsets may need to be updated too.

## Loader Attribution

Loader used here:
- `Ultimate ASI Loader`
- Repo: https://github.com/ThirteenAG/Ultimate-ASI-Loader
- Release used: `v9.7.0`
