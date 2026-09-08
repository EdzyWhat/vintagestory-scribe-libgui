## Context

See `proposal.md` for the "why." Two pieces of existing mechanism shape this design directly:

1. **All 5 physical placements already funnel through one rotation path.** Ground placement
   (`BlockScribeWritingStation.TryPlaceBlock`) and every wall-mounted Inbox direction
   (`BlockEntityInbox.WallMountAngleRad`, read in `BlockEntity.Initialize`) both end up setting
   `BlockEntityScribeWritingStation.MeshAngleRad`. That setter's current implementation reads
   `Block.CollisionBoxes[0]`/`SelectionBoxes[0]` (the static box baked from blocktype JSON at
   registration) and rotates it into two cached fields, `RotatedBox`/`RotatedSelectionBox`, which
   `BlockScribeWritingStation.GetCollisionBoxes`/`GetSelectionBoxes` (the `Block`-level override)
   already prefers over the un-rotated JSON box when non-null. So Inbox (ground), Inbox-Wall (any
   of 4 directions), Scriptorium, and Assignment Desk are all, today, already reading their box
   dimensions from exactly one place: `Block.CollisionBoxes[0]`/`SelectionBoxes[0]` at
   `MeshAngleRad`-set time.
2. **`BlockEntityInbox` already distinguishes ground vs. wall at the instance level.** Its
   `WallMountAngleRad` override reads `Block.Variant["orientation"]` (`null`/`"up"` = ground,
   any direction = wall) to decide the facing angle. The exact same check is the natural way to
   resolve which of the two Inbox tuning targets a given placed instance is.

## Goals / Non-Goals

**Goals:**
- Make the box tuning apply to a block that's already placed in the world, live, with no need to
  break/replace it or restart the client (per `block-box-tuning`'s "no rebuild or relaunch"
  requirement).
- Reuse the existing single rotation mechanism above rather than adding a second box-resolution
  path alongside it.
- Preserve every current default and the existing rotation-survives-save/reload/facing behavior
  exactly for a never-tuned config (and for the Lectern, which is out of scope and must not be
  touched at all).

**Non-Goals:**
- Multiplayer correctness on a true dedicated server. Box tuning is a client-local config file
  (same mechanism as `ScribeGearTuning`/`ScribeVisualTuning`), so it is only guaranteed correct
  where the tuning author's client and the authoritative server share the same `ScribeModSystem`
  instance — i.e. singleplayer or a locally-hosted integrated server, the same scope `.geartune`
  already operates in. A dedicated server never calls `StartClientSide`, so it never loads a
  tuning file and always enforces the untouched default box; a client with a different local
  tuning value would see/predict a box the server doesn't agree with. Not solved here — this is a
  dev/author tool, not a synced gameplay feature.
- Diverging a target's collision box from its selection box, **except for the Chalkboard**. The
  other 4 in-scope blocks ship identical collision/selection boxes; this design keeps that
  coupling for them (one tuned box per target drives both), not 12 fields per target. The
  Chalkboard is the deliberate, sole exception: it ships `collisionbox: null` today (painting-style
  walk-through) and stays that way — only its selection slab is tunable. See Decisions below for
  the `HasCollisionBox` gate that keeps this a one-target carve-out rather than a general 2-box-
  per-target scheme.
- Baking tuned values back into the blocktype JSON files as an ongoing per-change task (it happens
  as a manual, once-values-are-final follow-up — see proposal.md — not as tracked work item here).

## Decisions

**`RotatedBox`/`RotatedSelectionBox` become computed get-only properties, not fields cached at
`MeshAngleRad`-set time.** Today's fields are written once, when `MeshAngleRad` is set (at
placement, or once on load via `Initialize`/`WallMountAngleRad`). For a tuning change to reach an
*already-placed* block with no restart, something has to either (a) re-trigger that computation
for every loaded instance when a tuning value changes, or (b) stop caching and compute the box on
every `GetCollisionBoxes`/`GetSelectionBoxes` call instead. (a) needs a "find every loaded
`BlockEntityScribeWritingStation` across all loaded chunks" operation that nothing else in this
codebase does today (block entities are addressed by position, not enumerated by type). (b) is
strictly simpler: the game engine already calls `GetCollisionBoxes`/`GetSelectionBoxes` on demand
per interaction/physics-check rather than holding a long-lived reference, so removing the cache has
no functional cost — a tuning change is just visible on the very next call, which is what "live, no
restart" requires anyway.

**A new `TuningTarget` virtual hook resolves which of the 5 targets (or none) a placed instance
reads from.** Added to `BlockEntityScribeWritingStation` as
`protected virtual ScribeBoxTuningTarget? TuningTarget => null;`. Overridden to a fixed value on
`BlockEntityScriptorium` (`Scriptorium`), `BlockEntityAssignmentDesk` (`AssignmentDesk`), and
`BlockEntityScribeChalkboard` (`Chalkboard`); on `BlockEntityInbox` it reads
`Block.Variant["orientation"]` exactly like its existing `WallMountAngleRad` check (`null`/`"up"`
→ `Inbox`, any other value → `InboxWall`). Left at the default `null` only on the Lectern — out of
scope, and `null` makes the box source fall back to `Block.CollisionBoxes[0]`/`SelectionBoxes[0]`
exactly as it does today, so its behavior is untouched byte-for-byte.

**One shared `ScribeBoxTuning` POCO, flat properties, no nested per-target object.** 5 targets × 6
floats (`X1,Y1,Z1,X2,Y2,Z2`) = 30 public properties (e.g. `InboxX1`, `InboxWallY2`,
`ScriptoriumZ1`, `AssignmentDeskX2`, `ChalkboardZ2`), matching `ScribeGearTuning`'s flat,
no-serialization-attribute style (confirmed to round-trip through the engine's
`LoadModConfig<T>`/`StoreModConfig`). A `CollisionBoxFor(ScribeBoxTuningTarget)` helper builds the
`Cuboidf` for a target; the same value is used for both collision and selection for 4 of the 5
targets (see Non-Goals) — the Chalkboard's `ChalkboardX1..Z2` feed `CollisionBoxFor` too (so
`RotatedSelectionBox` can use it unconditionally, same as every other target), but a separate
`HasCollisionBox(ScribeBoxTuningTarget)` static predicate (`false` only for `Chalkboard`) is
consulted by `RotatedBox` before routing collision through tuning at all — so the Chalkboard's
`CollisionBoxFor` value is computed but never actually surfaced as a collision box. Defaults match
each block's current blocktype-JSON box exactly, including `InboxWall*` defaulting to the ground
Inbox's current shared box and `Chalkboard*` defaulting to `chalkboard.json`'s `selectionbox`
(`{0,0,0,1,1,0.2}`; its `collisionbox` is `null` and stays unrepresented in tuning). Every
setter/load path clamps through a `Normalized()` method (mirroring `ScribeGearTuning.Normalized()`)
that clamps all 30 fields to `[0, 2]`.

**`HasCollisionBox` keeps the Chalkboard's collision-exemption a one-target carve-out, not a
second box-resolution path.** `BlockEntityScribeWritingStation.RotatedBox` changes its condition
from `TuningTarget is { } t` to `TuningTarget is { } t && ScribeBoxTuning.HasCollisionBox(t)` —
when false (Chalkboard only), it falls through to the existing `Block?.CollisionBoxes[0]` fallback
branch, which is `null` for the Chalkboard per its JSON, so `RotatedBox` stays `null` (walk-through
preserved) no matter what the Chalkboard's tuned axes say. `RotatedSelectionBox` needs no change:
it already resolves `TuningTarget is { } t ? CollisionBoxFor(t) : ...` unconditionally per target,
so the Chalkboard's selection box becomes tunable simply by `TuningTarget` no longer being `null`.

**Loaded client-side only, in `StartClientSide`, alongside `GearTuning`/`VisualTuning`.** Same
`LoadModConfig<ScribeBoxTuning>(BoxTuningConfigFileName) ?? new ScribeBoxTuning()` shape, persisted
to `scribe-box-tuning.json`. The `ScribeModSystem.BoxTuning` getter lazily defaults
(`boxTuning ??= new ScribeBoxTuning()`) so a dedicated server (which never calls
`StartClientSide`) never null-refs when a block entity's `TuningTarget` isn't null — it just always
resolves the untouched default box, consistent with the Non-Goals note above.

**`.boxtune` command + `ScribeBoxTuningDialog`, a structural clone of `.geartune`/
`ScribeGearTuningDialog`.** 5 groups of 6 numeric fields (one group per target — the Chalkboard's
group is labeled to make clear it tunes selection only), each writing through
`ScribeModSystem.UpdateBoxTuning(Action<ScribeBoxTuning> mutate)` (persists + raises
`BoxTuningChanged`). Because the world hitbox is now computed live (see Decision 1), the
`BoxTuningChanged` event's only remaining job is what it already does for `.geartune`: let an
*open dialog* re-seed its own displayed fields onto the clamped/persisted value
(`OnTuningChanged => ForceRebuild()`) — it is not needed for the world's box to update.

## Risks / Trade-offs

- **[Risk] Client-only config diverges from a dedicated server's enforced box.** → Mitigation:
  documented as a Non-Goal; intended usage is the author's own singleplayer/integrated-server
  testing before baking values into JSON, identical in scope to `.geartune`.
- **[Risk] Converting a cached field to a computed property touches a code path shared by the
  Lectern, which is out of scope.** → Mitigation: `TuningTarget` defaults to `null` for every
  subclass that doesn't override it, so its box source and the resulting rotated box are identical
  to today, just computed per-call instead of cached-at-set-time; verify via the existing manual
  "hitbox rotates with facing, survives save/reload" playtest step for the Lectern as a regression
  check even though it is not being tuned.
- **[Trade-off] Selection and collision boxes can't diverge for a tuned target, except the
  Chalkboard.** → Accepted (see Non-Goals); the `HasCollisionBox` gate is a one-target carve-out,
  not a general mechanism — revisit only if a *second* future block needs the same split.
- **[Risk] A `HasCollisionBox`-gated field set (`ChalkboardX1..Z2`) computes a `CollisionBoxFor`
  value that's silently never used for collision.** → Mitigation: the dialog's Chalkboard group
  caption and `ScribeBoxTuning`'s doc comments make explicit that these 6 fields drive selection
  only; no separate "collision fields" are added because the block has none to tune.

## Migration Plan

1. Add `ScribeBoxTuningTarget` enum (`Inbox`, `InboxWall`, `Scriptorium`, `AssignmentDesk`,
   `Chalkboard`) and the `ScribeBoxTuning` POCO (30 clamped float properties +
   `CollisionBoxFor`/`HasCollisionBox`/`Normalized`).
2. Add `TuningTarget` virtual hook to `BlockEntityScribeWritingStation` (default `null`); override
   on `BlockEntityScriptorium`, `BlockEntityAssignmentDesk`, `BlockEntityScribeChalkboard`, and
   `BlockEntityInbox` (variant-based).
3. Convert `RotatedBox`/`RotatedSelectionBox` from cached fields to computed properties that
   consult `TuningTarget`/`ModSystem.BoxTuning` first (`RotatedBox` additionally gated by
   `HasCollisionBox`), falling back to `Block.CollisionBoxes[0]`/`SelectionBoxes[0]` exactly as
   today; simplify the `MeshAngleRad` setter to only store the angle and `MarkDirty`.
4. Add `BoxTuningConfigFileName` constant, `BoxTuning` field/getter, `BoxTuningChanged` event, and
   `UpdateBoxTuning`/`OpenBoxTuning` methods on `ScribeModSystem`/`ScribeModSystem.ClientPrefs.cs`,
   mirroring the existing `GearTuning` wiring; load it in `StartClientSide` and register the
   `.boxtune` command.
5. Add `ScribeBoxTuningDialog` (structural clone of `ScribeGearTuningDialog`, 5 groups × 6 fields).
6. In-game verification: confirm all 5 targets' default boxes match today's shipped values exactly
   (place one of each, compare hitbox to current build — the Chalkboard check is selection-only,
   and confirm it still has no collision at any tuned value); then tune one target's value while a
   block of that type is placed and confirm the world hitbox changes with no client restart; then
   confirm a tuned value survives a client restart.

No player-facing migration is needed: every setting keeps its existing default, and nothing is
removed from any existing surface.
