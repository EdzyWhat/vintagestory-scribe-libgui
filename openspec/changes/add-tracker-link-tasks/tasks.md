## 1. Core model: Tracker & Link kinds (no VS API)

- [x] 1.1 Append `Tracker = 2` and `Link = 3` to `ScribeBlockKind` (never renumber `Task`/`Text`).
- [x] 1.2 Add `TargetItemCode` (string?), `TargetQuantity` (int), `CurrentQuantity` (int), and
      `LinkTarget` (string?) to `ScribeBlock`; extend the constructor with defaulted params so
      existing callers compile unchanged.
- [x] 1.3 Implement clamping in Core: `TargetQuantity` clamped to ≥ 1 on set/create;
      `CurrentQuantity` clamped into `[0, TargetQuantity]` whenever set.
- [x] 1.4 Add `ScribeDocument.AddTracker(itemCode, targetQuantity)` and `AddLink(target)` ops
      (append a block, assign a distinct `TaskId`, mirror the existing add-op return contract).
- [x] 1.5 Add a `ScribeDocument` op to set a tracker's `CurrentQuantity` by `TaskId` (clamped,
      no-op/failure for non-tracker or missing id).
- [x] 1.6 Core.Tests: add-tracker/add-link create the right kind + fields; target-quantity and
      current-quantity clamping; ordering + distinct `TaskId` for the new kinds.

## 2. Core codec: v5 → v6 with named migration step

- [x] 2.1 Bump `ScribeDocumentCodec.Version` to 6 and `PriorVersion` to 5; append the four new
      fields to the per-block serialize/deserialize layout in field order.
- [x] 2.2 Add `ApplyV5ToV6Migrations` that defaults the new fields for v5 blobs
      (`TargetItemCode`/`LinkTarget` = null, `TargetQuantity` = 1, `CurrentQuantity` = 0); wire it
      into the read path at the version branch. Ensure v4 (two versions back) fails safely.
- [x] 2.3 Update the `ScribeDocumentCodec` class doc-comment version table (current v6, prior v5,
      fields added in the v5→v6 transition).
- [x] 2.4 Core.Tests: current-version round-trip preserves tracker/link fields; replace the v4
      older-blob test with a v5 older-blob test asserting the new fields default; assert v4/older
      bytes fail to deserialize.
- [x] 2.5 Update `docs/CODEC-MIGRATION.md` with the v5→v6 transition as the newest worked example.

## 3. Mod: Handbook "Add to Scribe" entry point (Harmony)

- [x] 3.1 Add a Harmony patch class with a postfix on
      `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` that appends "Add to Scribe"
      `LinkTextComponent`(s) (Tracker and Link paths) carrying the page's collectible code. Register/
      unregister the Harmony patch in the client mod-system lifecycle.
- [x] 3.2 Track the last-opened Scribe item: set a client-side field wherever an item-hosted Scribe
      dialog opens (Notebook/Tablet).
- [x] 3.3 Implement three-tier target resolution on click (`ScribeModSystem.AddFromHandbook`):
      (1) open `ScribeDialogBase` via
      `capi.Gui.OpenedGuis.OfType<ScribeDialogBase>().FirstOrDefault(d => d.IsOpened())`; (2) else
      open the last-opened carried Scribe item (fallback: first carried Scribe item) via the new
      `IScribeDocumentItem.OpenScribeDialog` seam; (3) else
      `TriggerIngameError("You need a Scribe item to do that.")` and create nothing.
- [x] 3.4 **(REVISED — no new packet).** REUSE the existing per-dialog save path instead of a new
      `ScribeCreateTaskFromHandbookMessage`: `ScribeDialogBase.TryAddFromHandbook(kind, itemCode)`
      appends to the resolved live dialog's `scratch` document and flushes via its existing
      (possibly-overridden) `SendFlushPacket`. Case A (already editing) appends + flushes at once;
      Case B (not editing) stashes a `pendingHandbookAppend` and calls `TryEnterEditor()` — a
      deferred-append hook at the end of `EnterEditorMode` applies it once editor access lands
      (synchronous for items, async grant for blocks). Backwards compatible: `ScribeAddKind` is never
      serialized, and the only persistence change is the v6 document bytes already shipped in Group 2.
- [x] 3.5 **(REVISED — no new server handler).** The reused `SendFlushPacket` path already routes
      through the server-authoritative `ScribeEditDocumentMessage`/`ScribeNotebookSaveMessage` edit +
      re-sync; no dedicated handbook server handler is added. Locked-by-other blocks surface the
      generic `scribe:scribe-gui-locked` error (reused, no new key) via `TryEnterEditor`; a
      read-only/refused surface clears the stale `pendingHandbookAppend`.
- [x] 3.6 Register a new Handbook explainer entry (registration JSON + lang copy) describing the
      Tracker and Link task types and pointing at the per-item "Add to Scribe" link.
- [x] 3.7 Add Tracker and Link entries to `ScribeAddKinds.Live`; extend `ScribeAddKind` /
      `OnClickAdd` so these dispatch a non-mutating guide action: Handbook closed → open the
      explainer entry; Handbook open → `TriggerIngameError` telling the player to scroll to the
      current entry's bottom and click the "Add to Scribe" link.
- [x] 3.8 Add lang keys for the button label(s), the footer guide entries + their error text, the
      "no Scribe item" error, and task-type labels.
- [x] 3.9 Add a `VSAPI-NOTES.md` entry recording the exact `GetHandbookInfo` type/signature, the
      append-only postfix approach, and the handbook open API used to jump to an entry.

## 4. Mod: Tracker count engine (carried-only)

- [x] 4.1 Build a carried-inventory matcher: construct a `CraftingRecipeIngredient` from
      `TargetItemCode` and sum matching stack sizes across hotbar + backpack via
      `SatisfiesAsIngredient(stack, checkStackSize:false)`.
- [x] 4.2 Recompute on `IInventory.SlotModified` (debounced) + a ~1s edge-case poll, active only
      while the open document contains at least one Tracker; recompute on dialog open.
- [x] 4.3 Route `CurrentQuantity` updates through the server edit path (synced like `Done`);
      server persists, viewers converge.
- [x] 4.4 On target-met, apply the per-player completion setting (completes / deletes / nothing) by
      issuing the matching edit; guard against resurrecting a deleted task on later shortfall.

## 5. Mod: row rendering & completion setting

- [x] 5.1 Render a Tracker row: target item icon + name + `have/need` counter, with a progress
      state (none / partial / satisfied); shortfall reads unsatisfied, met reads like a completed row.
- [x] 5.2 Wire the inline arrow-stepper numeric control to edit a Tracker's `TargetQuantity` on the
      row (reuse the Settings numeric / `typed-arrow-substitution` control); re-clamp on change.
- [x] 5.3 Render a Link row: item icon + name; clicking the label opens the referenced Handbook page
      (parse `LinkTarget` → `AssetLocation` → handbook open API) and does NOT change completion,
      distinct from the row's completion control.
- [x] 5.5 Wire Link-task hyperlink activation on the pinned-task HUD: a pinned Link's click opens
      its Handbook page (reuse the existing HUD row-click plumbing, gated on kind == Link).
- [x] 5.4 Add the completes/deletes/nothing completion setting to `ScribeClientConfig` +
      `ScribeSettingsContent`/`ScribeSettingsDialog` (default: completes) with a lang label.

## 6. Verification & docs

- [x] 6.1 `build/verify.sh` green (Core suite incl. new tests + Atlas suite).
- [ ] 6.2 Manually test in-game — three-tier resolution: "Add to Scribe" appears on an item page;
      (1) with a Scribe surface open (test a block AND an item surface) it creates the task there;
      (2) with none open but a Scribe item carried, it opens that item's UI and creates the task;
      (3) with no Scribe item at all, it shows "You need a Scribe item to do that."
- [ ] 6.8 Manually test in-game — footer guide: click the footer Tracker/Link entry with the
      Handbook closed → the explainer entry opens; click it with the Handbook open → the
      scroll-and-click instruction error fires; neither creates a block.
- [ ] 6.9 Manually test in-game — Link hyperlink: click a Link task in a Scribe UI and again as a
      pinned task on the HUD; both open the linked Handbook page and leave completion unchanged.
- [ ] 6.3 Manually test in-game: create a Tracker, set N via the arrow-stepper, collect/drop matching
      items and confirm the `have/need` counter tracks carried inventory only (chest items ignored).
- [ ] 6.4 Manually test in-game: verify each completion-setting mode (completes / deletes / nothing)
      behaves correctly when a Tracker reaches its target.
- [ ] 6.5 Manually test in-game: create a Link, confirm tapping it opens the item's Handbook page and
      leaves its completion state unchanged.
- [ ] 6.6 Manually test in-game: load a pre-v6 (v5) world/save and confirm existing documents open
      cleanly with the new fields defaulted.
- [x] 6.7 Update `CHANGELOG.md` (Unreleased → Added: Tracker & Link task types, Handbook entry) and
      `ROADMAP.md` (mark the v1.2 task-types cluster progress).
