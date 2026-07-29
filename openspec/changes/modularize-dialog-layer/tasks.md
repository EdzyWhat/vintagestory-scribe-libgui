## 1. Foundation — IScribeDocumentHost + ScribeLayout

- [x] 1.1 Create `src/Mod/IScribeDocumentHost.cs` with `ScribeLayoutProportions` record (Default singleton), `ScribeLayout` struct (replaces `LecternLayout`), and `IScribeDocumentHost` interface
- [x] 1.2 Verify `ScribeLayoutProportions.Default` reproduces the Lectern's v1 dimensions: TitleBarFrac=0.13, InnerHFrac=0.8, SideColFrac=0.1, TitleBtnsWFrac=0.8, TitleBtnsHFrac=0.065
- [x] 1.3 Verify `ScribeLayout.TasksColW = (1 − 2·SideColFrac)·W` so the three columns sum to `InnerW` exactly

## 2. BlockEntityScribeLectern — implements IScribeDocumentHost

- [x] 2.1 Add `IScribeDocumentHost` to `BlockEntityScribeLectern`'s interface list
- [x] 2.2 Add explicit interface members: `BackdropSpec` → `ScribeBackdrops.LecternPage`; `GetLayout(w)` → `new ScribeLayout(w, 1160f/1024f)`; `DefaultDocumentTitle` → `"Lectern"`
- [x] 2.3 Retype the `dialog` field from `GuiDialogScribeLecternLibGui?` to `ScribeDialogBase?`; update `OpenDialog` to `new GuiDialogScribeLecternLibGui(Pos, this, capi)`
- [x] 2.4 Confirm `dotnet build` passes with no errors after steps 2.1–2.3

## 3. ScribeDocument.DefaultTitle

- [x] 3.1 Change `ScribeDocument.DefaultTitle` from `"Lectern"` to `"Untitled"` in `src/Core/ScribeDocument.cs`
- [x] 3.2 Confirm `Core.Tests` still pass (`dotnet test tests/Core.Tests`)

## 4. Extract row content widget files

- [x] 4.1 Create `src/Mod/ScribeReadContent.cs` — move `ScribeReadRowData`, `ScribeReadRow` + State, and rename `ScribeLecternReadContent` → `ScribeReadContent` (+ State)
- [x] 4.2 Create `src/Mod/ScribeEditorContent.cs` — move `ScribeEditRowData`, `ScribeDepartingEditorRow`, `ScribeFrozenEditorRow`, `ScribeEditRow` + State, and rename `ScribeLecternEditorContent` → `ScribeEditorContent` (+ State)
- [x] 4.3 Create `src/Mod/ScribePinnedContent.cs` — move `ScribePinRowData`, `ScribePinRow` + State, and rename `ScribeLecternPinnedContent` → `ScribePinnedContent` (+ State)
- [x] 4.4 Create `src/Mod/ScribeRowWidgets.cs` — move `ScribeVsIconGlyph`, `ScribeRowButton` + State, `ScribeRowButtonText` + State, `ScribeRowControlNudge` out of the monolith
- [x] 4.5 Confirm `dotnet build` passes after all four extractions; fix any missing `using` directives

## 5. Extract ScribeDialogBase

- [x] 5.1 Create `src/Mod/ScribeDialogBase.cs`; move the `LecternLayout`-based `CreateWindowConfig`, `OnRenderGUI`, `OnGuiClosed`, and all private fields from `GuiDialogScribeLecternLibGui` into the new base class
- [x] 5.2 Replace all `lectern.*` accesses with `host.*` calls through `IScribeDocumentHost`; replace all `LecternLayout` usages with `ScribeLayout` (via `host.GetLayout(...)`)
- [x] 5.3 Replace `ScribeDocument.DefaultTitle` in `CommitTitleIfEditing` with `host.DefaultDocumentTitle`
- [x] 5.4 Add `protected virtual IEnumerable<Widget> GetExtraNavButtons() => Array.Empty<Widget>();` and call it in `BuildRightColNav`, inserting between Pins and Settings (Settings always last)
- [x] 5.5 Replace all `ScribeLecternReadContent` → `ScribeReadContent`, `ScribeLecternEditorContent` → `ScribeEditorContent`, `ScribeLecternPinnedContent` → `ScribePinnedContent` call sites in the base
- [x] 5.6 Confirm `dotnet build` passes after base extraction

## 6. Slim GuiDialogScribeLecternLibGui

- [x] 6.1 Remove all code now in `ScribeDialogBase` from `GuiDialogScribeLecternLibGui.cs`, leaving only the class declaration and constructor
- [x] 6.2 Change `GuiDialogScribeLecternLibGui : GuiDialogBlockEntityBase` → `GuiDialogScribeLecternLibGui : ScribeDialogBase`
- [x] 6.3 Remove `LecternLayout` from `GuiDialogScribeLecternLibGui.cs` — it is now `ScribeLayout` in `IScribeDocumentHost.cs`
- [x] 6.4 Confirm `dotnet build` passes; file is 17 lines

## 7. Verification

- [x] 7.1 `dotnet build` — zero errors, zero warnings
- [x] 7.2 `dotnet test tests/Core.Tests` — 144/144 pass
- [x] 7.3 Manual in-game: Read / Edit / Pinned views confirmed working 2026-07-29
- [x] 7.4 Manual in-game: clear title → confirmed resets to "Lectern" 2026-07-29
- [x] 7.5 Manual in-game: scroll position preserved across view switches — confirmed 2026-07-29
- [ ] 7.6 Manual in-game: multiplayer editor lock
