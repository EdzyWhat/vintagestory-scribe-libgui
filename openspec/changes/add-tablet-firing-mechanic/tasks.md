## 0. Sequencing precondition

- [ ] 0.1 Apply/author AFTER `add-tablet-clay-type-backdrops` is archived, so the `fired` attribute + fired
  backdrops it introduced live in `openspec/specs/`. This change's requirements build on that state (the
  archive-order drift trap, MEMORY.md). These deltas are all ADDED requirements (no MODIFIED headers), so
  they don't target that change's exact header text — but the CODE they describe (ReadFired, ForTablet
  fired specs) must exist first.
- [ ] 0.2 Finalize the firing temperature/duration against the shipped vanilla clay-pottery
  `combustibleProps` (decompile a vanilla clay pottery itemtype) rather than guessing — treat as a
  finalize-during-implementation detail.

## 1. Declare firepit firing on the clay tablet

- [ ] 1.1 Add `combustibleProps` to `scribetablet.json` for the CLAY material only (via `combustiblePropsByType`
  or equivalent so wax is excluded): `smeltedStack` → `scribe:scribetablet-clay`, `meltingPoint` ≈ vanilla
  clay pottery, `meltingDuration`, `smeltedRatio: 1`, `requiresContainer: false`, appropriate `smeltingType`.
- [ ] 1.2 Ensure an ALREADY-fired clay tablet is not itself fireable (so it can't be re-fired into a blank):
  guard in `CanSmelt`/`DoSmelt` on the input's `fired` attribute (skip/deny when already fired), since the
  JSON combustibleProps can't condition on a stack attribute.

## 2. Carry document + clayType through the fire

- [ ] 2.1 Override `ItemScribeTablet.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot)`: call
  `base.DoSmelt(...)`, then on the resulting fired output stack copy `scribeDocument` (via
  `ScribeDocumentAttributes`) and `clayType` from the input stack, and set `fired = true`. Guard nulls (a
  blank input yields a blank-but-fired output).
- [ ] 2.2 Confirm the copy runs server-side (smelting is server-authoritative) and the fired output rides
  the existing stack-attribute persistence — no new packet.
- [ ] 2.3 Add `ItemScribeTablet` helper if needed to WRITE `fired`/document onto a stack, mirroring the
  existing `ReadClayType`/`ReadFired` read helpers.

## 3. Read-only fired tablet dialog

- [ ] 3.1 In `GuiDialogScribeTablet`, resolve `fired` once (via `ItemScribeTablet.ReadFired`) and, when
  fired, do NOT call `EnterEditorMode` in the ctor; render the inherited read view instead of the always-edit
  central region.
- [ ] 3.2 Audit EVERY edit affordance for the fired case and ensure none is offered: add-row, checkbox
  toggle, pin, reorder, title edit, the editor footer, and `RequestEditorAccess`. A fired tablet must be
  fully immutable.
- [ ] 3.3 When a fired tablet's document has no tasks AND no notes, render a small centered lang-key message
  (`scribe:tablet-fired-empty` or similar) in the central region instead of an empty list. Add the lang key
  to `en.json`.
- [ ] 3.4 Confirm a SOFT tablet's always-edit behavior is completely unchanged (the fired branch is additive).

## 4. Read-only document policy

- [ ] 4.1 Add a read-only `ScribeDocumentPolicy` preset (e.g. `FiredTablet`) in Core whose `CanAdd`/`CanPin`
  always deny; keep it VS-API-free. Leave the `Tablet` preset unchanged.
- [ ] 4.2 Have `TabletHost` (or the dialog) select the fired policy when the stack is fired, at the same
  mutation boundary the `Tablet` cap uses.

## 5. Verification

- [ ] 5.1 `dotnet build` clean; `dotnet test` — add Core coverage for the new `FiredTablet` policy
  (CanAdd/CanPin deny) since it is Core, VS-API-free.
- [ ] 5.2 In-game: fire a soft clay tablet (with tasks) in a firepit; confirm it becomes a fired tablet that
  KEEPS its tasks/notes/title and shows them read-only.
- [ ] 5.3 In-game: confirm the fired tablet shows the fired-ceramic backdrop with the correct per-type tint
  (this is the reachability that unblocks add-tablet-clay-type-backdrops task 6.3 — tune tints now if flat).
- [ ] 5.4 In-game: confirm firing preserves clayType (fire a blue tablet → fired blue tablet backdrop) and
  that a wax tablet does NOT fire.
- [ ] 5.5 In-game: pull a fired clay tablet from Creative Inventory; confirm it opens blank + uneditable with
  the centered "fired without any tasks" message.
- [ ] 5.6 In-game: confirm a fired tablet cannot be re-fired, and cannot be edited by any affordance.
- [ ] 5.7 Atlas/integration: exercise the smelt transform (fired output retains document bytes) if feasible
  in the local pre-push gate; keep synthetic player names ≤16 chars.
