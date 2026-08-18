## Context

The Scriptorium already has a two-slot, Scribe-items-only `InventoryGeneric` on `BlockEntityScriptorium`
(`SlotCount = 2`), rendered in `GuiDialogScribeScriptorium.BuildInventoryContent` as a centered `Row`
of `ScribeDocumentSlot`s driven by a `SlotController` (server-authoritative, `WatchInventory`-synced).
That view is currently framed as "Inventory" but exists solely to enable copy/paste. This change turns
it into the working "Transcribe" surface and lays out an unwired import/export section.

Documents are `ScribeDocument`s carrying a `DocId` (Guid) and per-block `TaskId` (Guid), serialized by
`ScribeDocumentCodec` and stored on an item via `ScribeDocumentAttributes` (same key/codec the block
entity uses). Persistence/sync follows the vanilla Sign pattern; `src/Core/` is API-free and unit-tested.

## Goals / Non-Goals

**Goals:**
- Rename the Inventory view to "Transcribe" (nav tooltip + heading).
- Copy a document from an Original slot onto a Duplicate slot, server-authoritatively, without touching
  the Original, producing an **independent** copy.
- Empty target → silent copy; non-empty target → two-press "overwrite N tasks" confirm.
- A reusable, non-load-bearing wax-seal press animation (one 2D asset + paint-transform code).
- An import/export section with a placeholder slot and disabled placeholder JSON/CSV/Import controls (no
  persisted slot / no save-migration this change).

**Non-Goals:**
- Wiring JSON/CSV import/export (placeholders only this change).
- Reusing the stamp animation for checkbox completion in read/edit/pinned (own follow-up change).
- Merge/append copy semantics or a drag-to-copy gesture (replace-or-empty only).
- A physical stamp *item* or ink resource.

## Decisions

### D1. Copy = deep clone with FRESH identity (not a verbatim byte-copy)

`ScribeDocumentAttributes` bytes embed the `DocId` and every `TaskId`. Copying them verbatim would give
two items the same identity, colliding on per-player pins and block-doc resolution (exactly why the
`ScribeDocumentAttributes` doc comment notes ids "ride inside the bytes"). The copy therefore produces a
deep clone with a **new `DocId` and new `TaskId` for every block**, preserving text/kind/done/depth.

- Implement as an API-free `src/Core/` method (e.g. `ScribeDocument.CloneWithNewIdentity()`), unit-tested:
  same content, all-new Guids, source unmutated. Keeps the identity rule in Core where it's testable.
- *Alternative rejected:* copy bytes as-is (simplest) — creates duplicate-identity documents and silent
  pin/resolution collisions. Not acceptable.

### D2. Server-authoritative copy via a new network message

The client sends a `TranscribeCopy` request (which block, source slot index, target slot index,
`allowOverwrite` flag) on the existing Scribe network channel (`ScribeModSystem`). The server reads the
source slot's item document, clones it (D1), writes it onto the target slot's item via
`ScribeDocumentAttributes.WriteTo`, marks the slot/inventory dirty, and lets the existing inventory sync
propagate. The client never writes item attributes directly.

- Mirrors the existing GUI → channel → server-mutate → sync flow and the Sign pattern.
- Server re-validates: if the target is non-empty and `allowOverwrite` is false, it performs no copy
  (defensive — the two-press confirm is a client UX, but the server is the gate).
- *Alternative rejected:* client-side attribute write — not authoritative; dupe/desync risk in MP.

### D3. Overwrite confirm is a two-press button STATE (client), gated server-side

The seal button holds a small local state: `Idle → ConfirmOverwrite` when pressed on a non-empty target.
The confirm label reads "Stamp again to overwrite N tasks", where **N is computed in Core** from the
target item's already-synced document (a game-agnostic Task-block count — reuse/extend existing Core
counting). A second press sends the copy with `allowOverwrite = true`. Any slot-content change resets the
state to `Idle`. Empty target skips the state entirely (single press copies).

### D4. Stamp animation = reusable paint-only widget on the existing harness

**Metaphor revised (playtest of the concept, 2026-08-16):** the wax-*seal* idea (press a matrix *into*
wax) was conflated with copying; the clearer, more legible read is a classic **wooden ink rubber stamp**
that presses a **"COPY" imprint** onto the Duplicate. The copy *button* itself stays a plain thematic
LibGUI button (no art on it); the flourish is the stamp descending onto the Duplicate slot.

`ScribeStamp` is a self-ticking paint-only `StatefulWidget` on the `gui-row-animation-harness` registry
(same survival discipline as `ScribeSlideIn` — the controller is host-owned and resumes across the
per-frame body reconcile; a generation-keyed `ValueKey`+id remounts it to replay on a re-copy). Timeline:
the wooden stamp **fades in + descends**, a brief **squash/tilt press**, then **lifts + fades out**,
leaving a **tilted "COPY" block-text imprint** that pops, holds, then fades — revealing the copied
`ScribeDocumentSlot` summary underneath. Composed of nested `Transform` (scale/rotate about centre) +
`Opacity`; the wooden PNG is drawn nearest-neighbour via `ScribePixelArtBackdrop`. No FBO, no 3D,
macOS-safe.

- The copy result is applied by the server sync regardless of the animation; the animation only *reveals*
  it. If the widget is absent, the card still updates — the flourish is not load-bearing (spec requirement).
  A missing PNG asset drops only the wooden image; the procedural "COPY" imprint still plays.
- One new pixel-art wooden-stamp PNG at `assets/scribe/textures/gui/scribe-copy-stamp.png`, baked by a
  re-runnable, swappable generator (`build/gen-copy-stamp.py`, "I bake it, you refine"). The "COPY" imprint
  needs no asset — it is rendered from text + a bordered box in the earthen/ink palette.

### D5. Layout: reflow `BuildInventoryContent` into a titled two-section Column

Replace the bare centered `Row` with a `Column`: heading ("Transcribe") → copy section (a `Row` of
Original `ScribeDocumentSlot`, the seal button between/below, Duplicate `ScribeDocumentSlot`) → `Divider`
→ import/export section (its own `ScribeDocumentSlot` + a `Row` of disabled Export JSON / Export CSV /
Import buttons with a "coming soon" `WithTooltip`). Reuse `ScribeRowButton`/`TitleButton` (icon+tooltip,
active/disabled states) and stock `Button`+`ButtonStyle`, following the editor's header/`Expanded`/footer
`Column` idiom. Disabled buttons render greyed and no-op on click.

### D6. The import/export slot is a rendered PLACEHOLDER this change (no persisted third slot)

Because import/export is unwired this change, the import/export section renders a placeholder slot box
that does not yet accept or store an item — it reserves the layout position. `BlockEntityScriptorium`
`SlotCount` stays at **2** (the copy pair), so there is **no `InventoryGeneric` resize and no
save-migration**. The real, persisted import/export slot is added by the future change that wires the
JSON/CSV logic (that change grows the inventory and handles its own migration).

- *Why not grow to 3 now:* the archived `scriptorium-inventory` spec requires exactly two slots and scopes
  import/export to a later capability; adding a functional slot for inert buttons would contradict that
  requirement and take on a resize-migration risk for no working feature.
- *Rendering:* reuse the `ScribeDocumentSlot`/watermark visual as a disabled placeholder (greyed, no
  `SlotController` binding), so it reads as "a slot will go here" without a backing `ItemSlot`.

## Risks / Trade-offs

- **[Central region overflow at small dialog sizes]** → Two sections + heading may exceed the fixed
  `TasksColW × InnerH` box the Inventory view currently fills without scrolling. Mitigation: wrap the
  Transcribe body in the dialog's shared `SingleChildScrollView`/`Scrollbar` (same components the editor
  uses) if it doesn't fit at the minimum dialog size.
- **[Duplicate-identity regression]** → If D1 is not honored (or a future refactor byte-copies), pins and
  block-doc resolution silently collide. Mitigation: Core unit test asserting clone produces all-new Guids
  and leaves the source unchanged.
- **[Seal asset style match]** → AI-generated seal may clash with the parchment palette. Mitigation: iterate
  on the PNG; the animation is non-blocking so this can't stall the feature.
- **[Confirm-state staleness in MP]** → Another viewer changes the target between a player's two presses.
  Mitigation: D3 resets on slot change; D2 server re-check makes an overwrite-without-intent impossible.

## Migration Plan

- **No persistence change at all.** No document-codec version bump (the on-item format is unchanged; only
  fresh ids are generated at copy time), and no block-entity inventory resize (D6 keeps 2 slots). Existing
  saves are unaffected.
- Modifies the archived `scriptorium-inventory` spec: the "surfaced as its own tab" requirement is
  re-labeled "Transcribe." Purely a display/label change.
- Rollback: the change is additive to the Scriptorium view; reverting restores the "Inventory"-labeled
  view. No save format changes either direction.

## Open Questions

- Exact stamp visual — RESOLVED 2026-08-16: a wooden **ink** rubber stamp leaving a "COPY" imprint (not a
  wax seal). First-pass pixel-art PNG baked by `build/gen-copy-stamp.py`; swappable/refine later.
- Whether the seal button sits *between* the two slots or *below* the pair (layout polish; decide in-game).
- Final disabled-control affordance for import/export (greyed buttons vs. a "(soon)" chip) — cosmetic.
