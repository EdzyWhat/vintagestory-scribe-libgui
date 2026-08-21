## 1. Swap the cuneiform item-name renderer to the wrapping one

- [x] 1.1 In `ScribeRowWidgets.cs`, `ScribeItemLabel.Build`'s cuneiform branch now builds a display-only
      `ScribeCuneiformFieldRenderWidget` (mirroring `ScribeReadContent.cs:458-479`): `caret/selectionAnchor: 0`,
      `hasFocus/caretVisible: false`, `caretColor/selectionColor: Vector4.Zero`, transparent box/border,
      `padX/padY` from style, `jitterSeed: label.GetHashCode()` (stable), and `singleLine` left default (false)
      so the name WRAPS.
- [x] 1.2 Non-cuneiform fallback (`new Text(label, … SoftWrap = true)`) left unchanged — it already wraps.
- [x] 1.3 Single-line title band untouched: the only `ScribeItemLabel.Build` cuneiform site was swapped. Grep of
      remaining `CuneiformText` sites confirms none is the item-name path — title band (`GuiDialogScribeTablet.cs:235`),
      add-kind picker (`ScribeAddKindPicker.cs:168`), editor hint (`ScribeEditorContent.cs:527`), and the single-line
      "N/N" counter (`ScribeRowWidgets.cs`, `ScribeTrackerCounterText`) all correctly stay single-line.
- [x] 1.4 Updated the `ScribeItemLabel` XML doc-comment to note the cuneiform strokes now wrap to width via the
      shared field renderer (was single-line).

## 2. Build + verify

- [x] 2.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [x] 2.2 `dotnet test tests/Core.Tests` — 463 pass, no new failures (the 7 failing are pre-existing
      illumination-floor tests, unrelated — no Core code was touched).
- [x] 2.3 `bash build/restage.sh Debug` — done as one combined restage after all four changes merged to main
      (client confirmed not running); 137 files staged, build 0/0.
- [x] 2.4 In-game gate: on a Tablet, add a Tracker/Link/Craft for an item with a long name (e.g. a
      wildcard cloth/shirt) → the cuneiform name WRAPS within the row instead of clipping mid-word, in
      both the wet (editor) and read views.
      — Confirmed 2026-08-21 (playtest verdict on file in TESTING.md, `3d83b0c0`).
- [x] 2.5 In-game gate: add a Craft with a long-named ingredient subtask → the indented subtask's name
      wraps within its narrower bounds; indentation still reads correctly under a wrapped name.
      — Confirmed 2026-08-21 (playtest verdict on file in TESTING.md, `f56cf446`).
- [x] 2.6 In-game gate: confirm the Tablet dialog TITLE band is still single-line, and that the
      Lectern/Notebook/Scriptorium/HUD item titles are visually unchanged.
      — Obsolete 2026-08-20 (TESTING.md `412d7bf0`): the premise flipped — the user now WANTS the title band
      to wrap to two lines, which was delivered separately in `wrap-tablet-title-band` (4.1 Confirmed). The
      "keep the band single-line" check no longer reflects the goal; closed as obsolete-and-superseded.
