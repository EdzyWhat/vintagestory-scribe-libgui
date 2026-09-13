## Why

The pinned-task HUD's default TopRight anchor is pre-offset to clear the vanilla minimap, using a
hardcoded pixel constant (`DefaultTopRightMinimapClearanceX = 260f`) derived from the vanilla
minimap's own logical-pixel size (250×250 + 10px padding). The author reproduced, in-game, that this
clearance is wrong at any GUI Scale setting other than the game's reference value (10) — at GUI Scale
8 (a setting the author, and likely most players, actually use), the HUD sits noticeably too far from
the minimap on world load. This may be related to (but is not confirmed as the same bug as) an
independent ModDB report describing the HUD rendering "much wider" on singleplayer world load until
some interaction "corrects" it — that report is tracked separately; this proposal fixes the
minimap-clearance miscalculation on its own merits, regardless of whether it turns out to be the same
root cause.

Separately, the author wants the HUD's *default* anchor changed from top-right to top-left. This
sidesteps the entire minimap-clearance problem for every new install (nothing to clear on the left,
since the vanilla minimap itself defaults to the top-right corner) — independent of whether the
clearance-math fix below is even needed for a player who never touches the anchor setting. Existing
players on (or who later choose) top-right still need the clearance math to be correct, so this does
not replace the bug fix.

## What Changes

- Fix the TopRight anchor's minimap-clearance offset so it clears the vanilla minimap by a small,
  consistent visual gap at **any** GUI Scale setting, not only the reference value the current
  hardcoded constant happens to match. The exact mechanism (recomputing the clearance from a
  GUIScale-consistent conversion, or reading the minimap's actual rendered footprint at runtime) is a
  design decision, not a spec-level one — the spec only constrains the observable result.
- Change `ScribePlayerSettings.HudAnchor`'s default from `TopRight` to `TopLeft`, and change
  `NormalizeAnchor`'s fallback-for-unrecognized-value from `TopRight` to `TopLeft` to match (a
  malformed/legacy config value should fall back to the same anchor a fresh install gets, not to the
  anchor this change is moving away from as the default).
- No change to the anchor picker itself (still all seven positions, still player-configurable) — only
  the default value and the TopRight clearance math change.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `pinned-task-hud`: the "The HUD's screen position is configurable" requirement changes in two ways:
  (1) the default anchor becomes top-left instead of top-right, so the minimap-clearance pre-offset no
  longer applies to the out-of-box default; (2) the top-right anchor's minimap-clearance pre-offset
  (still available to any player who selects or already has top-right) SHALL clear the minimap
  consistently regardless of the player's GUI Scale setting, not only at one reference value.

## Impact

- Affected code: `src/Mod/HudScribePins.cs` (`ApplyAnchor`, `DefaultTopRightMinimapClearanceX`),
  `src/Core/ScribePlayerSettings.cs` (`HudAnchor` default, `NormalizeAnchor` fallback).
- No `src/Core` model/persistence-format changes beyond the default literal value; `HudAnchor` remains
  a client-local preference (no network/save-format impact — a player who already saved a `TopRight`
  preference, explicitly or via the old default, is unaffected by the default changing, since their
  saved value takes precedence over the default).
- Existing players currently sitting on `TopRight` only because it *was* the default (never
  explicitly chosen) will not automatically move to `TopLeft` — the default only applies to a value
  that was never written to their config file. This is called out as a known, accepted limitation
  rather than something this change attempts to migrate.
