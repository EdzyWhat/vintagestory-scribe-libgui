## 1. Base-dialog seams (keep the 3-column skeleton)

- [x] 1.1 In `src/Mod/ScribeDialogBase.Layout.cs` make `BuildRightColNav()` `protected virtual` (its
  body unchanged) so a subclass can supply a different right column; keep `GetExtraNavButtons()` as-is.
- [x] 1.2 Make `BuildCentralRegion()` `protected virtual` (body unchanged) and promote
  `BuildEditorContent()` from `private` to `protected` so a subclass can reuse the editable task list
  without forking it. Do NOT change `BuildSectionInnerBox` — the `[ SideColW | TasksColW | SideColW ]`
  three-column skeleton stays shared.
- [x] 1.3 Read the diff and confirm the Lectern and both Notebook dialogs (which override nothing)
  call the identical inherited bodies — the change is visibility widening + `virtual`, no behavior
  change.

## 2. Tablet theme

- [x] 2.1 In `src/Mod/ScribeTheme.cs` add a `Tablet` `ThemeData` (earthen/clay palette) next to
  `Light` as a **placeholder** first pass. Do NOT finalize the ink/contrast approach now — the real
  decision (one ink color / dark text + shadow-glow / per-backdrop ink) is deferred until the clay/wax
  backdrops render in-game, since VS material colors span pale-to-dark. See the
  `tablet-theme-contrast-vs-backdrops` memory note.

## 3. Tablet backdrops

- [x] 3.1 In `src/Mod/ScribeBackdrop.cs` add two named backdrop slots to `ScribeBackdrops`, keyed to
  the tablet item's `material` axis — `clay` and `wax` — both pointing at the existing
  `textures/gui/scribe-lectern.png` placeholder for now (1024×1160 ratio matches the tablet layout
  aspect). Reuse `WrapBackdrop` / `BuildOuterArtBox` unchanged. (The 7-backdrop clay-type/fired
  vision is the separate followup `add-tablet-clay-type-backdrops`, not this change.)

## 4. GuiDialogScribeTablet

- [x] 4.1 Add `src/Mod/GuiDialogScribeTablet.cs` subclassing `ScribeDialogBase`, constructed with a
  `TabletHost`; inherit title-edit, grip drag, close, autosave, and network send helpers (do NOT
  reimplement them).
- [x] 4.2 Override `BuildRightColNav()` to return an empty right column (no nav buttons; the
  `SideColW` spacer width is preserved so the side margin is unchanged), and override
  `BuildCentralRegion()` to build a column: a fixed-height cuneiform title banner on top, then the
  inherited `BuildEditorContent()` editable task list filling the remainder. Do NOT fork the editor —
  reuse the promoted `protected BuildEditorContent()` so task add/edit/check/pin keep working under
  the tablet policy. Keep `GetExtraNavButtons()` empty (inherited).
- [x] 4.3 Render the title banner as a display-only `CuneiformText` showing the document's title;
  give it an explicit pixel height derived from `fontSizeEm` (do NOT use `Expanded` inside the scroll
  view), and give the task list the remaining height.
- [x] 4.4 Compute a single `UseCuneiform` from the player's `DisableCuneiformFont` setting; when
  disabled, render the title banner through the normal path via `ScribeTaskFont.Resolve` instead of
  `CuneiformText`. Keep the branch in one place.
- [x] 4.5 Select the `ScribeTheme.Tablet` palette in the dialog's `Build()` theme wrapper and apply
  the backdrop slot matching the tablet's material (clay/wax).

## 5. Wire the item to the new dialog

- [x] 5.1 In `src/Mod/ItemScribeTablet.cs`, switch the open site (`OpenTabletDialog` /
  `OnHeldInteractStart`) from `GuiDialogScribeNotebook` to `GuiDialogScribeTablet`, passing the same
  `TabletHost`.
- [x] 5.2 Add any new `scribe:` lang keys needed for tablet-dialog demo/label strings to
  `src/Mod/assets/scribe/lang/en.json`.

## 6. Verification

- [x] 6.1 `dotnet build` clean; `dotnet test` — existing Core suite still green (no Core changes
  expected in this proposal).
- [x] 6.2 In-game: open a Lectern and both Notebooks; confirm all views (Read/Edit/Pinned/Settings,
  plus History on the notebook) still work — the seam did not disturb the incumbents.
- [x] 6.3 In-game: right-click a clay tablet and a wax tablet; confirm each opens the bespoke
  `GuiDialogScribeTablet` (always-edit, no tabs) with the earthen theme + material backdrop, that
  title edit / grip drag / close all work, and that adding/editing/checking/pinning tasks still works
  under the 10-task / 1-pin caps (no regression of Proposal B's editor).
- [x] 6.4 In-game: confirm the cuneiform title banner renders crisp filled strokes at a legible fixed
  height above the task list.
- [x] 6.5 In-game: toggle `DisableCuneiformFont` on; confirm the banner falls back to the normal
  resolved task font, and back off restores cuneiform.
- [ ] 6.6 Atlas/integration: the local pre-push gate stages the `gui` dep and exercises the tablet
  open path; keep synthetic player names ≤16 chars.
