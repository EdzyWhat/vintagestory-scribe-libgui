## Context

Editable task rows draw two visual affordances that can co-occur: the **pinned-row wash**
(a resting `Secondary` tint at ~0.33 alpha, from `ScribeRowConstants.PinnedTint`) and the
**focus chrome** (a `Primary` border + `SurfaceHigh` fill shown while a row's text input is
focused).

There are two editor render paths:

- **Normal (Lectern / Notebook)** — the focus chrome is drawn by the field's own render
  widget (`ScribeMultilineField.cs`, ~1169–1172: `borderColor = focusNode.HasFocus ? Primary
  : Border`). It is already scoped to the input, satisfying the existing `lectern-gui-shell`
  requirement "Focus ring is scoped to the active field."
- **Tablet (cuneiform)** — the field deliberately draws NO box (`ScribeMultilineField.cs`
  ~1140–1143, all `Vector4.Zero`), and the enclosing whole-row `Container` in
  `ScribeEditorContent.cs` (~763–782) becomes the appearance driver. Its `rowFill` is a single
  ternary where focus and pinned are mutually exclusive (focus replaces the pinned wash), and
  its border is the `Primary` focus border drawn around the entire row.

The playtest (`f640f9ab` / `add-tablet-clay-type-themes` 6.8) found that on a pinned cuneiform
row the whole-row focus border and the whole-row pinned wash are the same shape and can't be
told apart. The tablet path violates a requirement the normal path already meets.

## Goals / Non-Goals

**Goals:**
- On the cuneiform path, scope the focus border/fill to the input element, matching the normal
  path — so a focused input on a pinned row reads as a small bordered input inside the pinned
  wash (two distinct shapes).
- Leave the row `Container` carrying only the pinned tint and the drag-source/drop-target
  washes.

**Non-Goals:**
- No change to the normal (non-cuneiform) path — it already conforms.
- No change to the pinned tint color/alpha, the `Secondary`-vs-`Primary` role split, or the
  drag washes.
- No re-theming; this is purely about *where* the existing focus chrome is drawn.

## Decisions

**Move the cuneiform focus chrome into the field's render widget, don't just recolor the row.**
The obvious cheap fix would be to keep drawing on the row but change the focus color so it
contrasts with the pinned wash. Rejected: it still draws focus as a whole-row shape, which is
exactly what the spec forbids and what makes the two affordances ambiguous. Instead the
`focusedCuneiform` border/fill/corner logic moves from the row `Container`
(`ScribeEditorContent.cs`) into the cuneiform field's currently-zeroed box
(`ScribeMultilineField.cs`), so the mechanism matches the normal path exactly — one place
per path, both scoping focus to the input. The state needed (`focusNode.HasFocus`, the
cuneiform flag) already reaches the field, so no new plumbing is required.

**The row `Container` keeps the pinned tint and drag washes.** Those are genuinely whole-row
affordances (a pinned row / a drag source-or-target is a row-level state), so they stay on the
`Container`. Only the focus branch leaves it.

## Risks / Trade-offs

- **[Row-height / alignment parity]** The cuneiform field currently draws no box; giving it a
  1px border + corner radius could nudge its content box and desync read/edit row heights (a
  parity the project has fought before). → Mirror the normal path's exact box metrics (1px
  border, radius 4, `SurfaceHigh` fill) which already coexists with height parity there;
  verify a single-line cuneiform row's height in-game after the move.
- **[Border clipping inside a tight row]** An input-scoped border sits inside the row padding;
  on a narrow tablet it must not clip against the pinned wash edge. → The normal path already
  renders this box inside the same padding; confirm visually on a pinned tablet row.
- **[Resting title / label surfaces]** Only editable rows have focus; the resting cuneiform
  title and footer labels never focus, so they're unaffected — but confirm no shared field
  code path forces the box on a non-editable surface.

## Open Questions

- Should the focused input's `SurfaceHigh` fill be slightly translucent so the pinned
  `Secondary` wash still shows through behind it (reinforcing "input inside a pinned row"),
  or fully opaque like the normal path? Decide from the in-game look; default to matching the
  normal path (opaque) unless it reads worse over the clay backdrop.
