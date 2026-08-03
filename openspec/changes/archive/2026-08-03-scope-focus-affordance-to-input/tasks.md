## 1. Move focus chrome onto the cuneiform field box

- [x] 1.1 In `src/Mod/ScribeMultilineField.cs`, at the cuneiform render-widget box (currently
  hard-zeroed, ~lines 1140–1143), draw the focus box when the field is focused on the cuneiform
  path: `boxColor = focusNode.HasFocus ? colors.SurfaceHigh : (transparent)`,
  `borderColor = focusNode.HasFocus ? colors.Primary : (transparent)`, `borderThickness = 1f`
  when focused, `cornerRadii = Vector4.One * 4f` when focused — mirroring the normal path's field
  box (~lines 1169–1172). Use the `focusNode.HasFocus` and cuneiform flag already available there.
  — Done: kept thickness/corner CONSTANT (only colors toggle) so the content box never reflows
  focus↔unfocus, preserving read/edit row-height parity.
- [x] 1.2 Keep the field box fully transparent (no border/fill/corner) when NOT focused on the
  cuneiform path, so resting cuneiform rows look exactly as they do today.

## 2. Strip the focus branch off the row Container

- [x] 2.1 In `src/Mod/ScribeEditorContent.cs` (~lines 763–782), remove the `focusedCuneiform`
  term from the row `Container`'s fill/border/corner/thickness so the `Container` only ever
  carries the pinned tint and the drag-source/drop-target washes. Delete the now-unused
  `focusedCuneiform` local.
- [x] 2.2 Confirm the pinned tint (`PinnedTint(colors)`), the drag washes (`dragShift`), and the
  read-view path are otherwise unchanged — this task removes only the focus branch.

## 3. Verification

- [x] 3.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean; `dotnet test tests/Core.Tests` green
  (no Core change expected). — Done: build clean (3 pre-existing unrelated warnings), 280/280 tests pass.
- [x] 3.2 Restage (`bash build/restage.sh Debug`); in-game on a tablet (red/blue/fire), focus an
  input on a PINNED task row and confirm the focus highlight wraps just the input element and is
  clearly distinct from the whole-row pinned wash (two shapes, not one) — the retest for
  `add-tablet-clay-type-themes` 6.8 / playtest fail `f640f9ab`. — Confirmed 2026-08-03 playtest
  (`8e7526ee`/`f640f9ab`): focused input's border sits inside the pinned wash without clipping; two distinct shapes.
- [x] 3.3 In-game: confirm the normal Lectern/Notebook path focus box is visually unchanged, and
  that resting (unfocused) cuneiform rows show no input border/fill. — Confirmed 2026-08-03 playtest (`57aa784e`).
- [x] 3.4 In-game: confirm single-line cuneiform row heights are unchanged (read vs. edit parity)
  now that the field draws a 1px bordered box when focused, and that the border doesn't clip
  against the row's pinned-wash edge. — Confirmed 2026-08-03 playtest (`8e7526ee`).
