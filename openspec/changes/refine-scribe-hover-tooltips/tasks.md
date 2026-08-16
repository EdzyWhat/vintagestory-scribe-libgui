## 1. Reduced hover-strength shade (D1)

- [x] 1.1 Add a shade-scaling helper that lerps a shade toward identity by a fixed fraction (e.g.
      `ScribeGlobalTint.WithStrength(shade, keep)` or `Shade.TowardIdentity(keep)`): `Brightness→1` and
      `TintR/G/B→1` by `(1 - keep)`. Identity in → identity out (so the `IsIdentity` fast-path still
      elides the SaveLayer at full daylight).
      — Added `ScribeGlobalTint.ForHover(child, shade)` + private `TowardIdentity(v, keep) = 1 + (v-1)*keep`.
- [x] 1.2 Add a single `HoverStrength = 0.9f` constant (10% less darkening) as the one tunable knob all
      hover wraps share. Leave `BuildBodyTree` (the body wrap) on the raw 100% shade — unchanged.
      — `ScribeGlobalTint.HoverStrength = 0.9f`; body wrap untouched.

## 2. Shade the Scribe-owned tooltips (D2)

- [x] 2.1 In `ScribeDialogBase.Layout.cs:WithTooltip`, wrap the tooltip `content` in a
      `ScribeGlobalTint` built from `Widget.CurrentShade` at `HoverStrength`, mirroring
      `ScribeAddKindPicker.cs:205-212`. This covers all nav buttons + title-bar (title-edit / drag-grip
      / close) hovers at once. — Used `ScribeGlobalTint.ForHover(content, currentShade)` (base field).
- [x] 2.2 Apply the same wrap at the inline `new Tooltip(...)` sites: `ScribeEditorContent` info button
      + tablet gear, and `ScribePinnedContent` policy caption. Confirm `CurrentShade` is available at
      each build site (it is threaded to these widgets already); thread it if any site lacks it.
      — Editor sites used `Widget.CurrentShade`; `ScribePinnedContent` lacked it → threaded a `currentShade`
      ctor param from `BuildPinnedContent`.
- [x] 2.3 Confirm Scribe Settings is untouched: `ScribeSettingsContent`'s two help tooltips are built
      inside the un-wrapped Settings dialog and must NOT receive a shade wrap. Add a code comment at the
      `WithTooltip` wrap noting the exclusion is by-construction so it isn't "unified" away later.
      — Exclusion comment added at `WithTooltip`; Settings tooltips left un-wrapped.

## 3. Custom compact slot + summary card (D3, D4)

- [x] 3.1 Add a summary-card content builder that takes an `ItemStack`: `ScribeDocumentAttributes.
      TryReadFrom` → on false render item name + an explicit "never opened" line; on true render item
      name + `doc.Title` (untitled placeholder when it equals `ScribeDocument.DefaultTitle`) + per-kind
      counts from `doc.Blocks` grouped by `ScribeBlockKind` (Task / Text→notes / Tracker / Link), showing
      only present kinds. Keep it a standalone helper (pure read) so copy/paste can reuse it.
      — `ScribeDocumentSlot.BuildSummaryCard(stack, colors)` (internal static); also renders an "Empty" line
      for an opened-but-blockless document, distinct from "never opened".
- [x] 3.2 Add lang keys for the card labels ("never opened", "tasks"/"notes"/"trackers"/"links",
      untitled placeholder) — reuse existing keys where they exist (e.g. the untitled placeholder already
      used by `ScribeTooltip.FormatTitleLine`).
      — Added `hover-card-never-opened`/`-empty`/`-tasks`/`-notes`/`-trackers`/`-links`; reused
      `ScribeTooltip.FormatTitleLine` (→ `tooltip-title` / `tooltip-title-untitled`) for the title line.
- [x] 3.3 Build the custom `ScribeDocumentSlot` widget: `Container` box (match `FlatItemSlot`'s
      `FlatBackground` box style so it still reads as a slot) → `ItemSlotOverlay(slot, SlotSize)`, wrapped
      in a `GestureDetector` forwarding to the existing `SlotController` (`EnterSlot`/`LeaveSlot`,
      `ClickSlot` per button on press, `WheelSlot` on wheel — click-to-grab / click-to-place only, NO
      `BeginDrag`/`DragEnterSlot`/`EndDrag`), wrapped in our own `Tooltip` whose content is the card (3.1) inside a `HoverStrength`
      `ScribeGlobalTint`. Exactly one tooltip — do NOT embed a `FlatItemSlot`.
      — New `ScribeDocumentSlot.cs`; empty slot → SizedBox content + 1h wait so no bubble appears.
- [x] 3.4 In `GuiDialogScribeScriptorium.BuildWatermarkedSlot`, swap `FlatItemSlot` → `ScribeDocumentSlot`
      while keeping the `scribebook` watermark under-layer `Stack` intact.
      — Threaded `colors` + `CurrentShade` (new `private protected` base accessor) into the slot.

## 4. Verification

- [x] 4.1 `dotnet build` clean; `dotnet test` (Core) green — Core is untouched, regression guard only.
      — Build clean (0 warn/0 err); Core 375/375 pass.
- [ ] 4.2 Restage, then manually test the item hover in the Scriptorium inventory: confirm the card is
      compact (well under the old 350px panel), shows item name + title + per-type counts, and shows the
      "never opened" state for a freshly crafted (never-opened) Scribe item.
- [ ] 4.3 Manually test item interaction through the custom slot: left-click grab/place, right-click
      place-one/split, and wheel transfer all still work (click-to-grab / click-to-place model), and a
      non-Scribe item is still rejected (accept filter intact).
- [ ] 4.4 Manually test hover shading in low/medium light: nav-button, title-bar, editor, pinned, and
      item-card hovers are all dimmed to match the body but slightly brighter (reduced hover strength);
      in full daylight hovers show no tint (identity).
- [ ] 4.5 Confirm Scribe Settings hovers remain canonical (un-shaded) in low light.
