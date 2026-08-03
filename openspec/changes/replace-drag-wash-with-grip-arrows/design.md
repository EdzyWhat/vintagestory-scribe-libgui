## Context

Editor rows (`ScribeEditorContent`) and Pin Tab rows (`ScribePinnedContent`) both reorder
by grip-drag. Each view's parent `State` owns two ints — `dragFromIndex` (grabbed row) and
`dragOverIndex` (row under the cursor) — and from them derives two per-row bools passed
into each row: `IsDragSource` (`dragFromIndex == i`) and `IsDropTarget` (`dragFromIndex is
not null && dragOverIndex == i`). Today each row turns those bools into a whole-row
background `Container` wash: source → `Primary` brightened +20, target → `Primary` darkened
−20 (both half-saturated, fill 0.4 / border 0.5).

A pinned row separately paints a `Secondary` wash (`ScribeRowConstants.PinnedTint`), now at
alpha 0.55 with a saturation boost. The darkened-`Primary` drop-target wash and the
`Secondary` pinned wash are close enough in some themes that, mid-drag over a pinned row,
"pinned" and "drops here" look the same. Both cues live on the same surface (the row
background), so the conflict is structural, not a color-picking accident.

The grip glyph is a `ScribeVsIconGlyph` (a thin `VsIcon(code, size, color)` wrapper) and is
**always mounted** during a drag — it must be, because it holds the event dispatcher's
pointer capture that keeps drag moves flowing. That makes it the ideal carrier for drag
feedback: swapping its glyph code / color is a cheap property update that can't drop
capture. The reorder itself is a move (`ScribeDocument.MoveBlock` = remove-at-from +
insert-at-to, list reflows — not a swap), commit-on-release, no-op on origin.

## Goals / Non-Goals

**Goals:**
- Remove all source/drop-target *background* washes from both drag-capable views.
- Indicate drag state on the grip glyphs instead: source grip → ◀, drop-target grip → ▶,
  all non-participant grips hidden; dim the source row to ~50% ("lifted").
- Keep the pinned-row `Secondary` wash painting during a drag (pinned ≠ dragging), now
  with nothing tinted to collide with it.
- One shared implementation across editor + pinned rows (they already share the state
  shape); no divergent copies.

**Non-Goals:**
- No change to reorder semantics (still move-not-swap, still commit-on-release, still
  origin-release-is-no-op) or to `MoveBlock` / the document / the network path.
- No change to the resting (non-drag) row appearance, including the pinned wash values
  (those were tuned in the sibling display change).
- No insertion-line-in-the-gap indicator (considered and declined — see Decisions).
- Read view untouched (non-reorderable).

## Decisions

**Feedback lives on the grip, plus a source-row dim.** Three glyph states driven by the
existing per-row bools:
- source (`IsDragSource`) → left triangle glyph (◀), and the whole row wrapped in
  `Opacity(~0.5)` so it reads as picked-up;
- drop target (`IsDropTarget` && not source) → right triangle glyph (▶);
- a drag is active but this row is neither → grip hidden.

"Drag active" is a *third* signal the rows don't get today (a row that is neither source
nor target still must hide its grip). Thread one more bool into each row —
`dragActive = dragFromIndex is not null` — computed in the same parent `.Select(...)` that
already computes `IsDragSource`/`IsDropTarget`. Idle (no drag) → grip renders normally, as
today.

**Marker semantics honor move-not-swap.** The target ▶ means "the task lands at this row's
position"; because the move extracts the source and reinserts at the target index, a drag
downward lands the task where the hovered row currently sits and that row (and any between)
shift up by one — the ▶-on-the-hovered-row reading is faithful to that result. This is why
an insertion-line "between rows" indicator was declined: it implies a gap-insertion model,
but our indices are row-granular and commit-on-release resolves to a row, so a row marker is
the more honest match and needs no gap geometry.

**Hiding the grip vs. dimming it.** Non-participant grips are fully hidden (not dimmed) so
the list visually collapses to just ◀ and ▶ — the decluttering the user asked for. The
grip's column width is unchanged (it still reserves its space; only the glyph's visibility
changes), so hiding a grip does not reflow the row — important, because the rows are
shifting position under the cursor already and a width change mid-drag would be janky.

**New triangle glyphs.** The icons dir has no arrow/triangle SVG. Add
`triangle-left.svg` + `triangle-right.svg` under
`src/Mod/assets/scribe/textures/icons/` and register them in
`ScribeModSystem.Assets.cs` as `scribetriangleleft` / `scribetriangleright`, following the
existing `RegisterSvgIcon` pattern (post-startup unload means we must use the by-code
`VsIcon` path, exactly as the grip does — not LibGUI's by-path `LoadSvg`). A single triangle
reused at two rotations was considered but two explicit assets are simpler and match how
every other Scribe glyph is registered.

**Keep the always-present `Container`.** The row `Container` stays in the tree (now only
ever carrying the pinned wash, since the drag washes are gone) so the widget type never
swaps mid-drag — preserving the structural-stability rule that keeps the field's `State`
(and any live caret) mounted. The `Opacity` wrapper for the source dim likewise must be
always-present (opacity 1.0 when not the source) rather than conditionally inserted, for the
same reason.

## Risks / Trade-offs

- **[Reflow from the `Opacity`/glyph swap]** Wrapping the source row in an always-present
  `Opacity` and swapping the grip glyph must not change row size or the grip column width
  (mid-drag reflow reads as jank). → `Opacity` is a paint-only wrapper (no layout change);
  keep the grip column width fixed and toggle only glyph visibility/code/color.
- **[Losing pointer capture by unmounting the grip]** Hiding a non-participant grip must
  not unmount the *source* grip that holds capture. → Only non-source/non-target grips hide,
  and hiding is a glyph-visibility change inside the still-mounted `GestureDetector`, not a
  removal of the grip subtree.
- **[Triangle legibility at small sizes / GUI scale]** The grip renders at `ControlSize`,
  which scales with the text-size preference; a triangle must stay readable at the smallest
  scale. → Author the SVGs as bold, simple solid triangles (like the existing grip), verify
  in-game at min text size.
- **[Pin Tab has no resting wash]** In the editor a non-dragged row can fall back to the
  pinned tint; in the Pin Tab every row is pinned, so removing the drag wash there leaves
  only the (uniform) pinned wash during a drag — the ◀/▶ glyphs become the *only*
  differentiator. That is acceptable and in fact the point (glyphs, not washes), but worth a
  specific in-game check on the Pin Tab.
- **[Direction not encoded]** ◀/▶ mark source/target as roles, not direction of travel
  (they're perpendicular to the up/down motion). Accepted per the design conversation; a
  directional-chevron variant was offered and not chosen.

## Open Questions

- Should the ▶ target glyph use an accent color (e.g. `Primary`) to pop, or the same
  muted `OnSurfaceVariant` the resting grip uses? Decide from the in-game look; default to a
  gentle accent on the target and the muted color on the source ◀, so the *destination*
  draws the eye.
