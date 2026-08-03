## Context

`add-tablet-clay-type-backdrops` (archived pending) added a `fired` stack attribute and per-type fired
backdrops, but recorded the firing mechanic itself as a Non-Goal — so `fired` is never set true in play.
This change un-defers it: a firepit bakes a soft clay tablet into a fired one, carrying the document and
clay type through, and a fired tablet becomes read-only.

Findings from reading the shipped API/lib (ground truth, decompiled):

- **Firepit firing runs through `CollectibleObject.DoSmelt`, which is `virtual`.** It builds the output by
  cloning the fixed `combustibleProps.SmeltedStack.ResolvedItemstack` — it does **not** copy the input
  stack's attributes. So a plain JSON `smeltedStack` would fire the tablet into a *blank* fired tablet,
  losing the document. `CanSmelt`/`OnSmeltAttempt` are also virtual. The clean hook to preserve data is to
  **override `DoSmelt`** on `ItemScribeTablet`: let the base build the fired output, then copy
  `scribeDocument` + `clayType` from the input onto the output and ensure `fired = true`.
- **`CombustibleProperties` fields:** `MeltingPoint` (required with `SmeltedStack`), `MeltingDuration`,
  `SmeltedRatio` (=1 so one tablet → one tablet), `SmeltingType`, `RequiresContainer` (vanilla clay
  pottery fires WITHOUT a crucible — set false). Declared in `scribetablet.json` under `combustibleProps`.
- **The document already persists on the stack** via `ScribeDocumentAttributes` (`scribeDocument` bytes),
  and `clayType`/`fired` via `ItemScribeTablet.ReadClayType/ReadFired`. No new packet — the fired output is
  just another stack carrying the same attributes.
- **The dialog** (`GuiDialogScribeTablet`) is currently always-edit: its ctor calls `EnterEditorMode(...)`
  before `Build()`. Read-only mode means NOT entering editor mode and rendering the existing read view
  (the base `ScribeDialogBase` has a non-editor render path the Lectern uses).

## Goals / Non-Goals

**Goals:**
- Fire a soft clay tablet in a firepit into a fired clay tablet (`fired = true`), like vanilla clay
  pottery — no crucible, at the clay melting point.
- Carry the document (tasks/notes/title) and `clayType` through the fire (like Notebook → Clockmaker's
  Notebook data transfer), by overriding the smelt hook.
- Make a fired tablet read-only: its dialog opens view-only — readable but no task add/check/pin/edit and
  no title edit.
- A fired tablet with no document (e.g. from Creative) opens blank + uneditable, showing a small centered
  message that it was fired without any tasks.
- Make the fired backdrops + per-type tints (already built) reachable so they can be verified/tuned.

**Non-Goals:**
- Un-firing / re-softening a fired tablet, water damage, wax-wipe, stylus edit gate — still deferred.
- Wax tablet firing (wax melts, it does not fire; out of scope — only clay tablets fire).
- Any new fired art (the tinted-soft-art interim from the prior change stands).
- Changing the soft tablet's editable behavior or its 10-task / 1-pin cap.
- Any `src/Core/` change or new network packet.

## Decisions

### 1. Fire via `combustibleProps.smeltedStack` + an overridden `DoSmelt`
Declare `combustibleProps` on `scribetablet.json` for the clay material only: `smeltedStack` → the same
`scribetablet-clay` item, `meltingPoint` ≈ vanilla clay pottery (~650 °C, finalize against the shipped
pottery value), `smeltedRatio: 1`, `requiresContainer: false`. Override `ItemScribeTablet.DoSmelt` to call
`base.DoSmelt(...)` then, on the resulting fired output stack, copy `scribeDocument` and `clayType` from
the input and set `fired = true`. This reuses the entire vanilla firepit flow (temperature, duration,
progress bar) and only adds the attribute carry-through.

*Alternative — a bespoke firing interaction (right-click fired-clay block, custom recipe).* Rejected:
firepit smelting is the idiom players expect for clay, is server-authoritative already, and needs no new
UI.

### 2. `fired` is the read-only switch, resolved once
The dialog and policy both key off `ItemScribeTablet.ReadFired(stack)` (already exists). A fired tablet:
selects an immutable `ScribeDocumentPolicy` (new read-only policy, distinct from `Tablet`), and its
dialog does NOT enter editor mode — it renders the inherited read view. One resolve point, mirroring how
the backdrop selection is centralized in `ForTablet`.

### 3. Empty-state message for a blank fired tablet
When a fired tablet's document has no tasks/notes (the Creative case), the read view shows a small
centered line (a new lang key, e.g. `scribe:tablet-fired-empty`) instead of an empty list, so it reads as
intentional ("Fired without any writing") rather than broken. A fired tablet WITH content shows that
content read-only as normal.

### 4. Immutable document policy
Add a read-only `ScribeDocumentPolicy` (e.g. `FiredTablet`) that reports no editing — the dialog consults
it at the same add/pin mutation boundary the `Tablet` cap uses, and the editor entry points are simply
not offered. Keeps the soft-tablet policy untouched.

## Risks / Trade-offs

- **`DoSmelt` override drift:** we depend on the base building the output then us copying attributes; if a
  future engine version changes `DoSmelt`'s shape the carry-through could silently break. Mitigate: copy
  defensively (guard nulls), and cover the transform with an integration/Atlas check that a fired output
  retains the document bytes.
- **Melting point / firepit fuel tuning:** the exact `meltingPoint`/`meltingDuration` are eyeballed
  against vanilla pottery and finalized in-game (like Proposal B's ingredient codes) — flagged in tasks,
  not blocking.
- **Read-only completeness:** must ensure EVERY edit affordance is gone for a fired tablet (add row, check,
  pin, title edit, the always-edit ctor path), not just the obvious ones — a missed path would let a baked
  tablet be edited. Mitigate with an explicit read-only audit task.
- **Firing a stacked/again-fired tablet:** MaxStackSize is 1 and a fired tablet should not itself be
  combustible (guard `CanSmelt`/omit combustibleProps for the already-fired state, or no-op the copy) so it
  can't be re-fired into a blank. Resolve in tasks.
- **Sequencing / archive-order drift:** modifies `clay-wax-tablet-item` / `tablet-dialog` /
  `scribe-document-policy`, last touched by `add-tablet-clay-type-backdrops`; author deltas against current
  spec text and apply after that change archives, matching exact requirement headers (MEMORY.md trap).
