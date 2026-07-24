## 1. De-spike the build & dependency wiring

- [x] 1.1 In `src/Mod/Mod.csproj`, remove the "SPIKE ONLY — DO NOT MERGE" banner around the `Gui` /
  `OpenTK.Mathematics` / `SkiaSharp` `ItemGroup` and rewrite the comments to justify them as a production
  hard dependency, mirroring the ConfigLib reference comment style (compile ref + `Private=false`; the
  installed `gui` mod provides them at runtime; OpenTK/Skia resolve from `$(VintageStoryPath)/Lib` because
  LibGUI's public API surfaces those types).
- [x] 1.2 Confirm `src/Mod/modinfo.json` declares `"gui": "2.0.0"` as a hard dependency (already present);
  reconcile the version string with the vendored `gui_*.zip` the DLLs came from.
- [x] 1.3 Add a `gui_2.0.0.zip` re-extraction section to `src/Mod/lib/README.md` listing the 7 vendored
  managed DLLs (`Gui.dll`, `ExCSS`, `ShimSkiaSharp`, `SkiaSharp.HarfBuzz`, `Svg.Custom`, `Svg.Model`,
  `Svg.Skia`, `HarfBuzzSharp`), mirroring the existing ConfigLib/VSImGui entries.
- [x] 1.4 Verify `build/restage.sh`, `build/restage.ps1`, and `build/package.sh` do NOT stage/ship
  `Gui.dll` (because `Private=false` keeps it out of `bin/`); record this as a one-line assertion.

## 2. Remove spike scaffolding

- [x] 2.1 Delete `src/Mod/SpikeLibGuiLecternDialog.cs`.
- [x] 2.2 Remove `RegisterLibGuiSpikeCommand`, its call site, and the SPIKE banner comment from
  `src/Mod/ScribeModSystem.cs` (the `.scribespike` chat command).
- [x] 2.3 Keep `src/Mod/SpikeScribeMultilineField.cs` but add a header comment marking it reference-only
  for the change-2 editor-view port (to be deleted once that lands).

## 3. Production read-view dialog

- [x] 3.1 Add `src/Mod/GuiDialogScribeLecternLibGui.cs` subclassing LibGUI's `GuiDialogBlockEntityBase`
  (block position + `ICoreClientAPI`), integrated into the real lectern open path — opened from the block
  interaction and populated via the existing `ScribeRequestAccessMessage` / `ScribeEditDocumentMessage`
  flow, not `.scribespike` and not a direct `Document` reference.
- [x] 3.2 Build the read-view widget tree: `WindowFrame` (title/close/drag) → `Column` (title + free-text
  section) → `ListView` of task/note rows, using the code-defined parchment `ColorScheme`/`ThemeData`
  (reference: the retired `SpikeLibGuiLecternDialog.cs` tree structure).
- [x] 3.3 Implement each read row as a self-stateful widget carrying a stable `ValueKey`, containing a
  `Row` of `[Checkbox reflecting Done, Expanded(wrapped Text)]` (LibGUI stock `Checkbox`; ruling / custom
  glyph / text-size scaling deferred). No other part of the row is interactive.
- [x] 3.4 Wire the checkbox to toggle Done via the existing lock-free `ScribeToggleTaskMessage` path
  (server-authoritative, no editor lock), and have the row reflect the re-synced state through its own
  state (not a parent rebuild).
- [x] 3.5 Add a "switch to editor" control that opens the existing native `GuiDialogScribeLectern` editor
  view (interim seam — the LibGUI dialog stays read-only this change).
- [x] 3.6 Refresh the read view when the block entity re-syncs (the `RefreshReadView` hook in
  `BlockEntityScribeLectern.FromTreeAttributes`) so a synced Done toggle updates the open dialog.

## 4. Retire superseded stub

- [x] 4.1 Delete `openspec/changes/own-lectern-element-bounds/` (empty stub superseded by this migration).

## 5. Creative-reach auto-close re-check

- [x] 5.1 Re-check Scribe's Creative-mode inflated-reach auto-close fix against LibGUI's
  `GuiDialogBlockEntityBase` `IsOutOfRange`/`InteractionRange` override point (the native dialog overrode
  `IsInRangeOfBlock`); adjust the LibGUI dialog so walk-away auto-close still fires in survival mode.

## 6. Build, test, playtest

- [x] 6.1 `dotnet build src/Mod/Mod.csproj -c Release` builds clean (0 warnings/errors).
- [x] 6.2 `dotnet test tests/Core.Tests` is green (no Core change expected).
- [x] 6.3 `bash build/restage.sh Release` and `bash build/restage.sh Debug` stage successfully; assert
  `Gui.dll` is NOT present in the staged Mods folder.
- [ ] 6.4 In-game read-view playtest (record verdicts via the testing checklist): dialog opens from the
  lectern via real interaction (not `.scribespike`); renders the live document via the packet flow;
  close/drag/minimize work; parchment theme renders on Apple Silicon; clicking a checkbox toggles Done and
  re-syncs without the editor lock; the rest of a row is inert; "switch to editor" opens the working native
  editor; survival-mode walk-away auto-close fires.

## 7. Docs

- [x] 7.1 Append any new LibGUI lesson hit in practice (e.g. the `ListView` index-caching gotcha as it bit,
  the Creative-reach override point) to the `## LibGUI` section of `VSAPI-NOTES.md`.
