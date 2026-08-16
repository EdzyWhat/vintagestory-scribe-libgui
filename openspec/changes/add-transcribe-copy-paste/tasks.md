## 1. Core: independent-clone primitive (D1)

- [ ] 1.1 Add an API-free `ScribeDocument.CloneWithNewIdentity()` in `src/Core/` that deep-copies the
      document with a fresh `DocId` and a fresh `TaskId` for every block, preserving text/kind/done/depth
      and leaving the source unmodified.
- [ ] 1.2 Add a Core task-count helper (or reuse an existing one) that returns the number of completable
      Task blocks in a document — used for the "overwrite N tasks" prompt.
- [ ] 1.3 `tests/Core.Tests`: clone produces all-new Guids (DocId + every TaskId), identical content, and
      the source is untouched; task-count returns the expected N for mixed documents. `dotnet test` green.

## 2. Server-authoritative copy operation (D2)

- [ ] 2.1 Define a `TranscribeCopy` request message (block position, source slot index, target slot index,
      `allowOverwrite` flag) and register it on the existing Scribe network channel in `ScribeModSystem`.
- [ ] 2.2 Server handler: read the source slot's document, clone it via `CloneWithNewIdentity()` (§1.1),
      write it onto the target slot's item with `ScribeDocumentAttributes.WriteTo`, mark dirty, and let the
      existing inventory sync propagate.
- [ ] 2.3 Server re-validation: if the target is non-empty and `allowOverwrite` is false, perform no copy
      (defensive gate independent of the client's confirm UX). No-op cleanly if either slot lacks a document.

## 3. Transcribe view layout + rename (D5, modifies scriptorium-inventory)

- [ ] 3.1 Add lang key `scribe-tab-transcribe` and the stamp/overwrite/import-export strings; rename the
      Scriptorium document-slot nav button + view heading from "Inventory" to "Transcribe" (this realizes
      the `scriptorium-inventory` MODIFIED tab requirement).
- [ ] 3.2 Rebuild `GuiDialogScribeScriptorium.BuildInventoryContent` into a titled `Column`: heading →
      copy section (`Row` of Original slot, seal button, Duplicate slot) → `Divider` → import/export section.
- [ ] 3.3 Import/export section is a PLACEHOLDER (D6): render a greyed placeholder slot box (no
      `SlotController` binding, no backing `ItemSlot`) + disabled Export JSON / Export CSV / Import buttons
      with a "coming soon" tooltip; the block-entity inventory stays at its two real (copy) slots.
- [ ] 3.4 Wrap the Transcribe body in the shared `Scrollbar`/`SingleChildScrollView` if it overflows the
      fixed central region at the minimum dialog size (mitigation for the overflow risk).
- [ ] 3.5 Disabled import/export controls render greyed and no-op on click.

## 4. Copy interaction + overwrite confirm (D3)

- [ ] 4.1 Wire the seal button to send `TranscribeCopy`; disable it (with an explainer) until both the
      Original and Duplicate slots hold Scribe items.
- [ ] 4.2 Two-press state: on a non-empty target, first press → `ConfirmOverwrite` label
      ("Stamp again to overwrite N tasks", N from §1.2 on the synced target document); second press sends
      the copy with `allowOverwrite = true`. Empty target sends immediately on a single press.
- [ ] 4.3 Reset the confirm state to `Idle` whenever either slot's contents change.

## 5. Reusable wax-seal stamp animation (D4)

- [ ] 5.1 Add one AI-generated 2D wax-seal PNG asset styled to the parchment/earthen palette.
- [ ] 5.2 Build a reusable `ScribeStamp`-style paint-only widget (Transform scale/tilt + Opacity tween on
      the existing `gui-row-animation-harness`) that plays a press animation and leaves a brief imprint.
- [ ] 5.3 Trigger the stamp on a successful copy, revealing the Duplicate slot's updated summary card when
      it settles; confirm the copy still completes and the card still updates if the animation is disabled
      (non-load-bearing).

## 6. Verification

- [ ] 6.1 `dotnet build` clean; `dotnet test` (Core) green. Restage before any in-game test.
- [ ] 6.2 Manually test copy onto an EMPTY target: single stamp press copies; Original unchanged; Duplicate
      card shows the copied contents.
- [ ] 6.3 Manually test copy onto a NON-EMPTY target: first press shows "overwrite N tasks"; second press
      overwrites; a slot change between presses cancels the confirm.
- [ ] 6.4 Manually verify independence: after a copy, editing one item's document does not change the other
      (fresh identity holds; pins resolve independently).
- [ ] 6.5 Manually verify the nav button + heading read "Transcribe" and the slots still reject non-Scribe items.
- [ ] 6.6 Manually verify the import/export placeholder: section is visible, its slot is an inert greyed
      placeholder, and Export JSON/CSV and Import are disabled and do nothing.
- [ ] 6.7 Manually confirm NO save-migration is needed: a Scriptorium placed before this change opens
      cleanly on the Transcribe tab with its two slots and contents intact (no resize occurred).
- [ ] 6.8 Multiplayer: two clients on one Scriptorium — a copy performed by one is reflected for the other;
      no dupe/desync.
