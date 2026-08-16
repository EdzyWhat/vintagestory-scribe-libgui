# Design — refine-scribe-hover-tooltips

## Context

The `respect-local-illumination` change shades each Scribe dialog by the live light reaching the
player. Ground-truth findings for this change:

- **The tint is applied once, to the whole body.** `ScribeDialogBase.Layout.cs:BuildBodyTree` wraps
  the composed body in `ScribeGlobalTint`; `GlobalTintRender.Paint` (`ScribeGlobalTint.cs:113-127`)
  does `Canvas.SaveLayer(colorFilter)` → paint → `Restore`, where the filter is an
  `SKColorFilter.CreateColorMatrix` multiplying each channel by `brightness * tintChannel`
  (`ScribeGlobalTint.cs:76-96`). The shade comes from `ScribeAmbientLightSampler.Sample` each frame
  (a `Shade` struct: `Brightness`, `TintR/G/B`), refreshed in `ScribeDialogBase.Lifecycle.cs:49`.
- **Overlays render outside that wrap.** LibGUI `Tooltip` uses the global Overlay layer
  (`useGlobalOverlay: true` at every Scribe call site), which paints above — i.e. outside — the body's
  `ScribeGlobalTint` subtree, so tooltips are currently un-shaded. This is a solved problem: the drop-up
  menu (`ScribeAddKindPicker.cs:205-212`) already re-wraps its Overlay content in its own
  `ScribeGlobalTint` built from `Widget.CurrentShade`.
- **Scribe-owned tooltips route through `WithTooltip`** (`ScribeDialogBase.Layout.cs:466-477`) for the
  nav buttons and title-bar affordances; a few inline `new Tooltip(...)` sites exist in
  `ScribeEditorContent` (info/gear) and `ScribePinnedContent` (policy caption). We own the `content`
  widget in all of these.
- **The item-slot tooltip is LibGUI-owned and not injectable.** `FlatItemSlot.Build` →
  `new ItemSlotGestureLayer(...)`, whose `State.Build` hard-codes
  `new Tooltip(gesture, new ItemTooltipContent(slot, world), 350ms, …)`. `ItemTooltipContent.MaxWidth`
  is a `const 350f`; `Tooltip` has no scale/max-width parameter. So neither the size nor the content of
  the item tooltip is reachable while using `FlatItemSlot`.
- **`SlotController` exposes clean public gesture methods** — `EnterSlot`/`LeaveSlot`,
  `ClickSlot(slot, button, outer)`, `WheelSlot(slot, dir, outer)`, `BeginDrag`/`DragEnterSlot`/`EndDrag`,
  and a `CanClickSlot` predicate — and `ItemSlotOverlay(slot, size, …)` renders an item stack
  standalone. Together these make a custom slot feasible without touching LibGUI internals.
- **Scribe Settings is already un-shaded** (`ScribeSettingsDialog.Build` returns a bare `WindowFrame`
  on the global theme, no `ScribeGlobalTint`), so excluding it costs nothing.

Constraints: no `src/Core` change (counts read from the existing `ScribeDocument` model), no new
dependencies (reuse LibGUI's public API), presentation-only (no persistence/sync).

## Goals / Non-Goals

**Goals:**
- Shade every Scribe-owned hover surface via the illumination pass, at a reduced hover strength
  (10% less darkening than the body), excluding Scribe Settings.
- Replace the Scriptorium inventory item-slot hover with a compact document-summary card (name, title,
  per-type counts, never-opened state), sized well under the stock 350px tooltip.
- Preserve all item interaction and the Scribe-only accept filter through the custom slot.

**Non-Goals:**
- Dialing the dialog **body** strength back — the 90% applies only to hover surfaces (user decision).
- Copy/paste transfer semantics — the card is the *preview* the copy/paste change will build on, not
  the transfer itself.
- Restyling or resizing the non-item tooltips' content — only their shading changes.
- Any change to Scribe Settings hovers.

## Decisions

### D1 — Reduced hover strength = blend the live shade toward identity by a fixed fraction
Add a shade-scaling helper (e.g. `Shade.TowardIdentity(float keep)` or a `strength` arg threaded into
`ScribeGlobalTint`) that lerps `Brightness→1` and `TintR/G/B→1` by `(1 - keep)`. Hover surfaces use
`keep = 0.9` (90% of the shade delta; 10% less darkening). Identity in → identity out, so full daylight
is untouched and the existing `IsIdentity` fast-path still elides the SaveLayer. The body continues to
use the raw (100%) shade — no change to `BuildBodyTree`.
- **Why lerp-toward-identity, not a brightness-only bump:** the user asked for "90% of the color/light
  value change," i.e. the whole shade delta (hue + brightness) at 90%, which a single toward-identity
  lerp expresses exactly. `TintStrength` (hue-only, in the sampler) is the structural precedent but is
  the wrong axis here.
- **Where:** a small static helper next to `ScribeGlobalTint`, so every hover wrap uses one definition
  of "hover strength" (a single tunable constant, e.g. `ScribeGlobalTint.HoverStrength = 0.9f`).

### D2 — Shade the owned tooltips by wrapping their `content` in `ScribeGlobalTint`
Route the shade wrap through the single `WithTooltip` helper so all nav-button/title-bar tooltips get
it for free, and apply the same wrap at the inline `Tooltip` sites in `ScribeEditorContent` and
`ScribePinnedContent`. The wrap reads the live shade from `Widget.CurrentShade` (already threaded to
these widgets), exactly like `ScribeAddKindPicker`. Content is wrapped, not the `Tooltip` itself, so
the overlay positioning/fade is unchanged.
- **Settings exclusion is automatic:** `ScribeSettingsContent`'s tooltips are built inside the
  un-wrapped Settings dialog and simply won't get a shade wrap (we only touch `WithTooltip` + the two
  document-dialog inline sites). No conditional needed; note it explicitly so it isn't "fixed" later.

### D3 — Compose a custom compact slot for the Scriptorium inventory (replace `FlatItemSlot`)
Build a small `ScribeDocumentSlot` widget: a `Container` box (matching `FlatItemSlot.FlatBackground`'s
style so it still reads as a slot, keeping the D7 book watermark under it) → `ItemSlotOverlay(slot,
size)` for item rendering, wrapped in a `GestureDetector` that forwards to the existing `SlotController`
(`EnterSlot`/`LeaveSlot` on enter/exit, `ClickSlot` on press per button, `WheelSlot` on wheel), all
wrapped in our **own** `Tooltip` whose content is the summary card (D4) wrapped in the reduced-strength
`ScribeGlobalTint`.
- **Interaction model = click-to-grab / click-to-place only.** `ClickSlot(slot, button)` already
  implements the vanilla model (move between the slot and the mouse-held cursor stack; right-click
  places-one/splits). We deliberately do NOT reimplement click-hold-drag distribution —
  `BeginDrag`/`DragEnterSlot`/`EndDrag` are omitted — because drag-distribute is not the inventory
  mechanic here (user decision); click grabs, a second click places.
- **Why not keep `FlatItemSlot`:** its tooltip is un-injectable and fixed at 350px (Context). A custom
  slot is the only way to control both size and shading from mod code without a LibGUI change.
- **Cost/risk:** we reimplement only the thin click/wheel forwarding via `SlotController`'s public
  methods — no LibGUI internals, no drag state. We drop the cosmetic hover-bounce/punch animation
  (acceptable; not load-bearing). The Scribe-only accept filter is unaffected (it lives on
  `ItemSlotScribeDocument.CanHold`, server-side).
- **Watermark:** keep the existing `scribebook` under-layer (`BuildWatermarkedSlot`) by composing the
  same `Stack` around the new slot body.

### D4 — Summary-card content read from the stack's document
The card builder takes an `ItemStack` and does `ScribeDocumentAttributes.TryReadFrom(stack, out doc)`:
- **False → crafted-but-never-opened:** render the item name + an explicit "never opened" line. No
  title placeholder, no zero counts (they'd imply an opened-but-empty doc).
- **True → summary:** item name; `doc.Title` (or the untitled placeholder when it equals
  `ScribeDocument.DefaultTitle`); and counts grouped from `doc.Blocks` by `ScribeBlockKind`
  (`Task`, `Text`, `Tracker`, `Link`). Show only present kinds, labelled (tasks / notes / trackers /
  links). Item name via the collectible's display name; block/item vs. writing-station handled the same
  (both carry a document).
- **Reused, not duplicated, by copy/paste:** this builder is the canonical "what does this stack
  contain" preview; the copy/paste change will call it for its import/export preview rather than
  re-deriving counts. Keep it a standalone, testable-ish helper (pure read of the document model).
- Font size / line spacing sized to be clearly smaller than 350px — a narrow bubble, a few short lines.

## Risks / Trade-offs

- **[Reimplemented gestures diverge from `FlatItemSlot`]** → Forward only through `SlotController`'s
  public methods (the same ones `ItemSlotGestureLayer` calls); manually verify left/right-click +
  wheel + the accept filter in-game (this is the main test surface). Drag-distribute is intentionally
  not implemented (not the inventory mechanic — click grabs, click places).
- **[Two tooltips firing]** → The custom slot must NOT also embed a `FlatItemSlot` (which brings its own
  tooltip); it composes `ItemSlotOverlay` directly, so there is exactly one tooltip (ours).
- **[Overlay shade staleness]** → `Widget.CurrentShade` must be current when the tooltip content
  builds; mirror the `ScribeAddKindPicker` threading so hovers track light live (spec scenario).
- **[Settings accidentally shaded / accidentally un-excluded later]** → We touch only `WithTooltip` and
  the two document-dialog inline sites; Settings builds its own tooltips outside that path. Documented
  so it isn't "unified" into the shared helper without preserving the exclusion.
- **[Reading a malformed/old document from a stack]** → `TryReadFrom` already guards; treat any
  read failure as the never-opened state rather than throwing in a hover path.
- **[macOS tooltip-close JIT crash]** → Unrelated to content, but tooltips exercise the LibGUI overlay
  close path; if a `Bad IL range` recurs it's the known platform bug (VSAPI-NOTES), not this change.
