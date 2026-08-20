## 1. Swap the cuneiform item-name renderer to the wrapping one

- [ ] 1.1 In `src/Mod/ScribeRowWidgets.cs`, in `ScribeItemLabel.Build`'s cuneiform branch
      (`style.UseCuneiform && style.CuneiformBundle is { } bundle`), replace the `CuneiformText(...)`
      construction with a display-only `ScribeCuneiformFieldRenderWidget`, mirroring the read-view note
      usage in `ScribeReadContent.cs:458-479`: `text: label`, `caret: 0`, `selectionAnchor: 0`,
      `hasFocus: false`, `caretVisible: false`, `fontSizeEm: style.FontSize`, `inkColor: color`,
      `caretColor: Vector4.Zero` (never shown), `selectionColor: Vector4.Zero`, `bundle: bundle`,
      `padX: style.FieldPadX`, `padY: style.FieldPadY`, transparent `boxColor: Vector4.Zero` /
      `borderColor: Vector4.Zero`, `borderThickness: 1f`, `cornerRadii: Vector4.One * 4f`,
      `jitterStrength: style.CuneiformJitter`, `jitterSeed:` a stable value (e.g. `label.GetHashCode()`
      or `0`), `rotationDegrees: style.CuneiformRotation`, `glow: style.CuneiformGlow`. Leave `singleLine`
      at its default (`false`) so the name WRAPS.
- [ ] 1.2 Do NOT change the non-cuneiform fallback (the `new Text(label, new TextStyle { Color = color,
      SoftWrap = true })` branch) — it already wraps.
- [ ] 1.3 Confirm the single-line title band is untouched: `CuneiformText` stays in use for the title
      chrome (and/or `ScribeCuneiformFieldRenderWidget(singleLine: true)`), and this change only touches
      `ScribeItemLabel.Build`. Grep other `CuneiformText` call sites to be sure none are the item-name path.
- [ ] 1.4 Update the `ScribeItemLabel` XML doc-comment: it currently says the cuneiform path "draws the
      name as cuneiform strokes" single-line — note it now wraps to width via the shared field renderer.

## 2. Build + verify

- [ ] 2.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [ ] 2.2 `dotnet test tests/Core.Tests` — green (no Core change expected; sanity only).
- [ ] 2.3 `bash build/restage.sh Debug` (only while the client is quit).
- [ ] 2.4 In-game gate: on a Tablet, add a Tracker/Link/Craft for an item with a long name (e.g. a
      wildcard cloth/shirt) → the cuneiform name WRAPS within the row instead of clipping mid-word, in
      both the wet (editor) and read views.
- [ ] 2.5 In-game gate: add a Craft with a long-named ingredient subtask → the indented subtask's name
      wraps within its narrower bounds; indentation still reads correctly under a wrapped name.
- [ ] 2.6 In-game gate: confirm the Tablet dialog TITLE band is still single-line, and that the
      Lectern/Notebook/Scriptorium/HUD item titles are visually unchanged.
