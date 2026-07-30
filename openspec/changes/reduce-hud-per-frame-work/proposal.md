## Why

Two hot paths in `HudScribePins` run work every frame or every 250ms regardless of whether
anything has changed:

1. `ApplyAnchor()` runs 60 times per second and recomputes the HUD window position from
   screen dimensions, settings, and anchor — even when the window hasn't been resized and
   the player hasn't changed any settings since the last frame.
2. `OnTick()` iterates `pendingCompletions` every 250ms to find expired undo windows — even
   when no completions are in flight and the dictionary is empty.

Both are cheap individually, but both are avoidable with trivial guards.

## What Changes

- `HudScribePins.ApplyAnchor()`: cache the last computed inputs `(screenW, screenH, anchor,
  offsetX, offsetY, minimapOn)`. Skip all position math when none of those have changed since
  the last call. Invalidate the cache when settings change (already triggers `ForceRebuild`
  via `MyPinsChanged`) or when the window is resized (detected by comparing cached vs. live
  screen dimensions).
- `HudScribePins.OnTick()`: add an early-return guard `if (pendingCompletions.Count == 0)
  return;` at the top of the method. No behavior change — when no completions are in flight
  the existing body is a no-op; the guard just makes that explicit and avoids the dictionary
  iteration.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. Both changes are pure implementation optimizations with no observable behavior change
for the player.

## Impact

- `src/Mod/HudScribePins.cs` only.
- No protocol, persistence, or interface changes.
