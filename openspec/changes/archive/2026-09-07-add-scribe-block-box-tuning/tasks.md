## 1. ScribeBoxTuning config surface

- [x] 1.1 Add `src/Mod/ScribeBoxTuningTarget.cs`: a plain enum with 4 values (`Inbox`, `InboxWall`,
  `Scriptorium`, `AssignmentDesk`). Verify it compiles with `dotnet build`.
- [x] 1.2 Add `src/Mod/ScribeBoxTuning.cs`: a plain POCO (no serialization attributes, matching
  `ScribeGearTuning`'s style) with 24 public float properties (`InboxX1..Z2`, `InboxWallX1..Z2`,
  `ScriptoriumX1..Z2`, `AssignmentDeskX1..Z2`), each defaulting to the corresponding block's
  current `collisionbox`/`selectionbox` value read from `inbox.json` (Inbox and Inbox-Wall both
  default to `{0.3, 0, 0.3, 0.7, 1.3, 0.7}`), `scriptorium.json` (`{0.05, 0, 0.15, 0.95, 1.25,
  0.85}`), and `assignmentdesk.json` (`{0.15, 0, 0.15, 0.85, 1.0, 0.85}`). Add `MinAxis = 0f` /
  `MaxAxis = 2f` constants, a `ClampAxis(float)` helper, a `Normalized()` method clamping all 24
  fields (mirroring `ScribeGearTuning.Normalized()`), and a
  `CollisionBoxFor(ScribeBoxTuningTarget)` method returning a `Cuboidf` built from the matching 6
  properties. Verify it compiles and add a quick manual check (e.g. a debug breakpoint or console
  write) confirming `new ScribeBoxTuning().CollisionBoxFor(ScribeBoxTuningTarget.Inbox)` equals
  today's Inbox JSON box.

## 2. Wire tuning into ScribeModSystem

- [x] 2.1 Add a `BoxTuningConfigFileName = "scribe-box-tuning.json"` constant next to
  `GearTuningConfigFileName` in `ScribeModSystem.cs`, a lazily-defaulted `BoxTuning` property
  (`boxTuning ??= new ScribeBoxTuning()`), and a `BoxTuningChanged` event, mirroring the existing
  `GearTuning`/`GearTuningChanged` declarations.
- [x] 2.2 In `ScribeModSystem.ClientPrefs.cs`, add `UpdateBoxTuning(Action<ScribeBoxTuning> mutate)`
  (mutate → `Normalized()` → `capi.StoreModConfig` → raise `BoxTuningChanged`) and
  `OpenBoxTuning()` (lazily construct/toggle `ScribeBoxTuningDialog`), mirroring
  `UpdateGearTuning`/`OpenGearTuning`.
- [x] 2.3 Load `BoxTuning` in `StartClientSide` (`api.LoadModConfig<ScribeBoxTuning>(
  BoxTuningConfigFileName) ?? new ScribeBoxTuning()).Normalized()`, next to the existing
  `GearTuning` load, and register a `.boxtune` client chat command that calls `OpenBoxTuning()`,
  mirroring the existing `.geartune` command registration. Verify `dotnet build` succeeds and
  `.boxtune` opens an (empty-bodied, pre-task-3) window in-game with no error.

## 3. Live box source on the block entity

- [x] 3.1 Add `protected virtual ScribeBoxTuningTarget? TuningTarget => null;` to
  `BlockEntityScribeWritingStation`.
- [x] 3.2 Override `TuningTarget` on `BlockEntityScriptorium` (`=> ScribeBoxTuningTarget.
  Scriptorium`) and `BlockEntityAssignmentDesk` (`=> ScribeBoxTuningTarget.AssignmentDesk`).
- [x] 3.3 Override `TuningTarget` on `BlockEntityInbox`, reading `Block?.Variant["orientation"]`
  exactly like its existing `WallMountAngleRad` check (`null`/`"up"` → `ScribeBoxTuningTarget.
  Inbox`, any other value → `ScribeBoxTuningTarget.InboxWall`).
- [x] 3.4 In `BlockEntityScribeWritingStation`, replace the `RotatedBox`/`RotatedSelectionBox`
  cached fields with computed get-only properties: each resolves its source `Cuboidf?` as
  `TuningTarget is { } t ? ModSystem?.BoxTuning.CollisionBoxFor(t) : (Block?.CollisionBoxes is {
  Length: > 0 } c ? c[0] : null)` (selection uses `Block?.SelectionBoxes` in the fallback branch
  instead), then rotates it via a shared private `Rotated(Cuboidf?)` helper using the existing
  `MeshAngleRad`-to-degrees conversion. Simplify the `MeshAngleRad` setter to only store the angle
  and call `MarkDirty(true)` when changed (drop the box-computation block it currently contains).
  Verify `dotnet build` succeeds.
- [x] 3.5 In-game regression check: place a Lectern and a Chalkboard (both untouched by
  `TuningTarget`, still default `null`) and confirm their hitbox still rotates with facing and
  matches pre-change behavior — these two must be byte-for-byte unaffected.
- [x] 3.6 In-game verification: place an Inbox (ground), an Inbox (wall-mounted, any direction), a
  Scriptorium, and an Assignment Desk with an untouched `scribe-box-tuning.json` (or none at all)
  and confirm each one's hitbox exactly matches its pre-change shipped box.

## 4. Tuning dialog

- [x] 4.1 Add `src/Mod/ScribeBoxTuningDialog.cs`: a structural clone of
  `ScribeGearTuningDialog` — `DialogCode => "scribeboxtune"`, subscribes/unsubscribes to
  `BoxTuningChanged` (`OnTuningChanged` calls `ForceRebuild()` when open), and lays out 4 groups
  (Inbox, Inbox-Wall, Scriptorium, Assignment Desk) of 6 labeled `ScribeNumericField`s each (x1,
  y1, z1, x2, y2, z2), each writing through `modSystem.UpdateBoxTuning(b => b.<Field> = v)` with
  `clamp: ScribeBoxTuning.ClampAxis` and `step: 0.05f`. Wire `OpenBoxTuning()` (task 2.2) to
  construct/reuse this dialog instead of a stub. Verify `dotnet build` succeeds.
- [x] 4.2 In-game verification: run `.boxtune`, confirm all 24 fields show today's shipped
  defaults, then nudge one Inbox-Wall value while a wall-mounted Inbox is placed and confirm its
  world hitbox changes immediately with no client restart, while a separately placed ground Inbox
  is unaffected.
- [x] 4.3 In-game verification: nudge a value above 2 or below 0 directly in the field and confirm
  it clamps to 2/0 respectively (both in the dialog's displayed value and the resulting world
  hitbox).
- [x] 4.4 In-game verification: tune a value, restart the client, rejoin the same world, and
  confirm the tuned value is still in effect (both in `.boxtune`'s displayed fields and the world
  hitbox).

## 5. Chalkboard tuning target (selection-only)

- [x] 5.1 Add `Chalkboard` to `ScribeBoxTuningTarget`. Add 6 `ChalkboardX1..Z2` properties to
  `ScribeBoxTuning`, defaulting to `chalkboard.json`'s `selectionbox`
  (`{0, 0, 0, 1, 1, 0.2}`); include them in `Normalized()`'s clamp list and in
  `CollisionBoxFor(ScribeBoxTuningTarget)`'s switch. Add a
  `public static bool HasCollisionBox(ScribeBoxTuningTarget target) => target !=
  ScribeBoxTuningTarget.Chalkboard;` predicate. Verify `dotnet build` succeeds.
- [x] 5.2 In `BlockEntityScribeWritingStation.RotatedBox`, change the condition from
  `TuningTarget is { } t` to `TuningTarget is { } t && ScribeBoxTuning.HasCollisionBox(t)` so a
  Chalkboard's tuned axes are never surfaced as a collision box (falls through to the existing
  `Block?.CollisionBoxes` fallback, which is `null` for the Chalkboard). Leave
  `RotatedSelectionBox` unchanged — it already resolves per-target unconditionally. Verify
  `dotnet build` succeeds.
- [x] 5.3 Override `TuningTarget` on `BlockEntityScribeChalkboard` (`=>
  ScribeBoxTuningTarget.Chalkboard`). Verify `dotnet build` succeeds.
- [x] 5.4 Add a 5th "Chalkboard (selection only)" group to `ScribeBoxTuningDialog`, mirroring the
  existing 4 groups, writing through `modSystem.UpdateBoxTuning(b => b.Chalkboard<Field> = v)`.
  Verify `dotnet build` succeeds.
- [x] 5.5 In-game verification: place a Chalkboard and confirm its default hitbox (no collision,
  a thin selection slab) is unchanged from before this task, then run `.boxtune` and confirm the
  Chalkboard group shows the shipped defaults.
- [x] 5.6 In-game verification: nudge a Chalkboard value, including a full-cell-sized value (e.g.
  all axes to `0`/`1`), and confirm the placed Chalkboard's selection box changes live but it
  remains walk-through (no collision) at every value.
- [x] 5.7 In-game verification: tune a Chalkboard value, restart the client, rejoin the same
  world, and confirm the tuned selection box is still in effect.
