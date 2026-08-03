## 0. Sequencing precondition

- [ ] 0.1 Apply/author AFTER `add-tablet-clay-type-backdrops` is archived, so the `fired` attribute + fired
  backdrops it introduced live in `openspec/specs/`. This change builds on that state (archive-order drift
  trap, MEMORY.md). New requirements are mostly ADDED; the MODIFIED-in-spirit dialog/policy deltas must
  match that change's current header text.
- [ ] 0.2 Finalize the firing temperature/duration against the shipped vanilla clay-pottery
  `combustibleProps` (decompile a vanilla clay pottery itemtype) rather than guessing.
- [ ] 0.3 Finalize the `Harden` `freshHours` (≈48 game-hours = ~2 in-game days) + `transitionHours` by
  eyeballing against a shipped `transitionableProps` example (decompile a vanilla item that hardens/dries);
  treat as a finalize-in-play detail.
- [ ] 0.4 Confirm the vanilla "item/holder exposed to water" hook by decompiling the torch-extinguish path
  (dropped-item water check + held-while-swimming), so the rehydration hook targets a real signal, not a
  guess. Record the finding in VSAPI-NOTES.md.

## 1. Declare hardening (wet → hard)

- [ ] 1.1 Add `transitionablePropsByType` to `scribetablet.json` for the CLAY variants only (`*-clay-*`, so
  wax is excluded): a `Harden` transition with `freshHours` ≈48, a short `transitionHours`, and
  `transitionedStack` → the SAME clay variant (`scribetablet-clay-red` → itself, etc.).
- [ ] 1.2 Override `ItemScribeTablet.OnTransitionNow(slot, props)`: call `base.OnTransitionNow(...)`, then on
  the resulting hard output stack copy `scribeDocument` (via `ScribeDocumentAttributes`) from the input and
  set `hard = true`. Guard nulls (a blank input yields a blank-but-hard output). Only act on the `Harden`
  transition type; pass others through.
- [ ] 1.3 Ensure an already-fired clay tablet does NOT harden (guard on `fired` in `GetTransitionableProperties`
  or `OnTransitionNow`), so a fired tablet can't accrue a `hard` flag.
- [ ] 1.4 Add `ItemScribeTablet.ReadHard(stack)` mirroring `ReadFired`, and an `IsEditable(stack)` helper
  (`!ReadHard && !ReadFired`) used by the dialog + policy.

## 2. Rehydration (hard → wet)

- [ ] 2.1 Add a rehydration behaviour on `ItemScribeTablet` that converts a `hard` stack to a wet stack of
  the same clay variant: clear `hard`, reset the `transitionstate` tree attribute so the ~2-day timer
  restarts from full, and keep the document. No-op on a `fired` or already-wet stack. Server-authoritative.
- [ ] 2.2 Wire the DROPPED-IN-WATER case: hook the entity-item / dropped-item water path (per task 0.4) so a
  hard tablet dropped into water softens.
- [ ] 2.3 Wire the HELD-WHILE-SWIMMING case: detect the holder entering water while the tablet is the active
  hotbar item and soften it. If a clean shared hook doesn't exist, a lightweight per-tick check on the
  active tablet is acceptable — but keep it cheap.
- [ ] 2.4 Confirm both paths run server-side and the softened stack rides existing stack-attribute
  persistence (no new packet); the reset timer persists via the native `transitionstate` attr.

## 3. Declare firepit firing (unfired → fired)

- [ ] 3.1 Add `combustiblePropsByType` to `scribetablet.json` for the CLAY variants only: each clay variant's
  `smeltedStack` → the SAME clay variant, `meltingPoint` ≈ vanilla clay pottery, `meltingDuration`,
  `smeltedRatio: 1`, `requiresContainer: false`, appropriate `smeltingType`.
- [ ] 3.2 Ensure an ALREADY-fired clay tablet is not itself fireable (guard in `CanSmelt`/`DoSmelt` on the
  input's `fired` attribute), so it can't be re-fired into a blank. An unfired tablet fires whether wet or hard.

## 4. Carry document through the fire + fired precedence

- [ ] 4.1 Override `ItemScribeTablet.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot)`: call
  `base.DoSmelt(...)`, then on the fired output copy `scribeDocument` from the input and set `fired = true`.
  Clay type needs no copy (same clay variant). Guard nulls.
- [ ] 4.2 Fired precedence: when a HARD tablet is fired, the output is `fired = true` and MUST count as
  read-only/permanent regardless of any residual `hard` value (fired wins in `IsEditable` and the backdrop
  resolve). Don't rely on clearing `hard` — assert precedence in the resolve.
- [ ] 4.3 Confirm the copy runs server-side and the fired output rides existing stack-attribute persistence.

## 5. Read-only dialog for hard OR fired

- [ ] 5.1 In `GuiDialogScribeTablet`, resolve `IsEditable` once (via `ItemScribeTablet`) and, when NOT
  editable (hard or fired), do NOT call `EnterEditorMode` in the ctor; render the inherited read view.
- [ ] 5.2 Audit EVERY edit affordance for the non-editable case and ensure none is offered: add-row, checkbox
  toggle, pin, reorder, title edit, the editor footer, and `RequestEditorAccess`. Covers both hard and fired.
- [ ] 5.3 When a non-editable tablet's document has no tasks AND no notes, render a small centered lang-key
  message in the central region: `scribe:tablet-fired-empty` for fired, `scribe:tablet-hard-empty` (dried —
  dunk in water to edit) for hard. Add both lang keys to `en.json`.
- [ ] 5.4 Confirm a WET tablet's always-edit behaviour is completely unchanged (the read-only branch is
  additive).

## 6. State-keyed backdrop

- [ ] 6.1 Extend `ScribeBackdrop.ForTablet` to select by clay type AND state (wet / hard / fired), fired
  taking precedence over hard. Add a NEW hard-state tint per clay type (lighter/drier than wet, distinct
  from fired), reusing the tinted-soft-art interim approach.
- [ ] 6.2 Route the dialog's backdrop pick through the state so wet/hard/fired each render distinctly.

## 7. Read-only document policy

- [ ] 7.1 Add a read-only `ScribeDocumentPolicy` preset (e.g. `UneditableTablet`) in Core whose
  `CanAdd`/`CanPin` always deny; keep it VS-API-free. Leave the wet `Tablet` preset unchanged.
- [ ] 7.2 Have `TabletHost` (or the dialog) select the read-only policy when the stack is not editable (hard
  or fired), at the same mutation boundary the `Tablet` cap uses.

## 8. Verification

- [ ] 8.1 `dotnet build` clean; `dotnet test` — add Core coverage for the new `UneditableTablet` policy
  (CanAdd/CanPin deny) since it is Core, VS-API-free.
- [ ] 8.2 In-game: let a wet clay tablet (with tasks) dry ~2 in-game days; confirm it becomes a hard tablet
  that KEEPS its tasks/notes/title, shows them read-only, and shows the dried backdrop.
- [ ] 8.3 In-game: rehydrate a hard tablet BOTH ways — drop it in water, and enter water while holding it;
  confirm each returns it to wet + editable, keeps the document, and restarts the dry-out timer.
- [ ] 8.4 In-game: fire an unfired clay tablet (try both a wet one and a hard one) in a firepit; confirm it
  becomes a fired tablet that keeps its tasks/notes/title, shows them read-only, and shows the fired backdrop
  with the correct per-type tint (unblocks add-tablet-clay-type-backdrops task 6.3 — tune tints now if flat).
- [ ] 8.5 In-game: confirm firing preserves the clay variant (blue → fired blue) and that a wax tablet does
  NOT fire and does NOT dry.
- [ ] 8.6 In-game: pull a fired clay tablet and a hard clay tablet from Creative Inventory; confirm each opens
  blank + uneditable with its own centered empty-state message (fired-without-writing vs dried-dunk-to-edit).
- [ ] 8.7 In-game: confirm a fired tablet cannot be re-fired, cannot rehydrate in water, and cannot be edited
  by any affordance; confirm a hard tablet cannot be edited until rehydrated.
- [ ] 8.8 Atlas/integration: exercise the smelt AND harden transforms (output retains document bytes) if
  feasible in the local pre-push gate; keep synthetic player names ≤16 chars.
