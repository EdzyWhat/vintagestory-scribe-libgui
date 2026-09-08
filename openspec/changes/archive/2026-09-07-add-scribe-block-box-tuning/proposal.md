## Why

The Inbox, Inbox-Wall, Scriptorium, and Assignment Desk blocks each have a hand-picked
`collisionbox`/`selectionbox` in their blocktype JSON, and the author needs to keep fitting these
against updated art. Today, changing one of those six numbers means edit JSON → rebuild → relaunch
→ stand next to the block to see the new hitbox — expensive per nudge. Worse, the Inbox-Wall
variant currently can't be tuned independently of the ground Inbox at all: `inbox.json` explicitly
shares one box across every orientation ("the wall shape is still a placeholder... a separate
wall-specific box isn't worth tuning until that art is final"). This mirrors a problem already
solved once in this codebase for the Timer-tab gearworks layout via the `.geartune` live-tuning
window (`ScribeGearTuning`/`ScribeGearTuningDialog`) — the same pattern applies directly here.

## What Changes

- Add a `ScribeBoxTuning` POCO (mirrors `ScribeGearTuning`'s style: plain public properties, no
  serialization attributes, persisted via `LoadModConfig<T>`/`StoreModConfig` to
  `scribe-box-tuning.json`) holding 6 floats (`X1,Y1,Z1,X2,Y2,Z2`) per tunable target — **5
  independent targets**: Inbox (ground), Inbox-Wall, Scriptorium, Assignment Desk, and Chalkboard.
  Defaults match each block's current blocktype-JSON box exactly (Inbox-Wall's default equals
  today's shared Inbox box), so an untouched config renders identically to today. Every field
  clamps to `[0, 2]` per axis. Chalkboard is the one exception to "one box drives both
  collision and selection" (see below): only its selection box is tunable, since it ships with
  no collision box at all (painting-style, walk-through) and stays that way.
- `BlockScribeWritingStation.GetCollisionBoxes`/`GetSelectionBoxes` and
  `BlockEntityScribeWritingStation.MeshAngleRad`'s rotation source stop reading the static,
  registration-time-baked `Block.CollisionBoxes[0]`/`SelectionBoxes[0]` (parsed once from JSON) and
  instead read the live `ScribeBoxTuning` value for that block instance's target, then rotate it
  the same way as today. This makes both the ground-placed (rotated) path and the wall-mounted
  (never-rotated, base-fallback) path live-tunable, with no other change to the existing rotation
  behavior.
- Add a `.boxtune` dev client command opening a new `ScribeBoxTuningDialog` (a direct structural
  clone of `ScribeGearTuningDialog`) with 6 numeric fields per target (24 fields total), writing
  straight through an `UpdateBoxTuning`-style method on `ScribeModSystem` that persists the change
  and raises a `BoxTuningChanged` event, so an open dialog's hitbox nudges live with no
  rebuild/relaunch — matching `.geartune`'s existing UX exactly.
- Inbox-Wall becomes an independently tunable target (a decoupling from the ground Inbox's box),
  but its starting default equals today's shared value, so nothing changes visibly until the
  author actually moves its sliders.
- Chalkboard becomes a tunable target for its selection box only. `BlockEntityScribeChalkboard`
  overrides `TuningTarget => ScribeBoxTuningTarget.Chalkboard`; a new
  `ScribeBoxTuning.HasCollisionBox(target)` check (false only for Chalkboard) keeps
  `RotatedBox` falling back to the untouched `Block.CollisionBoxes` (`null` for the Chalkboard,
  per its JSON) instead of routing collision through tuning, while `RotatedSelectionBox` is
  already resolved per-target unconditionally, so it becomes tunable with no change to that
  getter. The board stays walk-through no matter how its selection slab is tuned.
- DEV/author-only tooling, like `.geartune` — not documented in the handbook, not surfaced in
  Scribe Settings dialog, no player-facing effect of its mere presence.
- Baking the final tuned numbers back into the blocktype JSON files as the shipping defaults has
  already happened once for Inbox/Inbox-Wall/Scriptorium/Assignment Desk (2026-09-07, from a
  `.boxtune` session) and will happen again for Chalkboard once its selection slab is dialed in —
  same lifecycle `.geartune`'s knobs went through (see `ScribeGearTuning.cs`'s "Defaults = the
  values the author dialed in... and locked as the shipping layout" comment). Out of scope: the
  Lectern block (not requested, and it has no wall-mount/selection-only precedent to extend); any
  ConfigKit/ConfigLib integration (rejected — that path is load-once-at-startup/restart-required
  per `add-configkit-visual-tuning`'s own design decision, which defeats the point of live hitbox
  preview).

## Capabilities

### New Capabilities
- `block-box-tuning`: the `ScribeBoxTuning` config surface itself — the 5 tunable box targets × 6
  clamped floats (Chalkboard's collision half unused, see above), the load/persist/default
  behavior, the live override that sources `GetCollisionBoxes`/`GetSelectionBoxes` from it instead
  of the static JSON box, and the `.boxtune` dev window that edits it.

### Modified Capabilities
None. The shipped collision/selection box **values** and the existing rotation behavior
(`scriptorium-block`'s "collision/selection box... SHALL survive save/reload and world-edit
rotation") are unchanged — only the dev-only mechanism supplying the box's source numbers changes,
and every current default is preserved exactly.

## Impact

- New: `src/Mod/ScribeBoxTuning.cs` (POCO), `src/Mod/ScribeBoxTuningDialog.cs` (tuning window).
- Modified: `src/Mod/BlockScribeWritingStation.cs` (box-getter source),
  `src/Mod/BlockEntityScribeWritingStation.cs` (`MeshAngleRad`'s rotation source), `ScribeModSystem.cs`
  / `ScribeModSystem.ClientPrefs.cs` (box-tuning field/event/load + `.boxtune` command
  registration, mirroring the existing `GearTuning` wiring), and `BlockInbox.cs`/`BlockScriptorium.cs`/
  `BlockAssignmentDesk.cs`/`BlockEntityScribeChalkboard.cs` for the per-subclass `TuningTarget`
  override that resolves which of the 5 targets a given instance reads (exact mechanism decided in
  design.md).
- No changes to `src/Core/`, `worldconfig.json`, `Mod.csproj`, or any new dependency — same
  zero-dependency shape as `.geartune`.
