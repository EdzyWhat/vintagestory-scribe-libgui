## 1. Foundation — IScribeDocumentHost + ScribeLayout

- [ ] 1.1 Create `src/Mod/IScribeDocumentHost.cs` with `ScribeLayoutProportions` record (Default singleton), `ScribeLayout` struct (replaces `LecternLayout`), and `IScribeDocumentHost` interface
- [ ] 1.2 Verify `ScribeLayoutProportions.Default` reproduces the Lectern's v1 dimensions: TitleBarFrac=0.13, InnerHFrac=0.8, SideColFrac=0.1, TitleBtnsWFrac=0.8, TitleBtnsHFrac=0.065
- [ ] 1.3 Verify `ScribeLayout.TasksColW = (1 − 2·SideColFrac)·W` so the three columns sum to `InnerW` exactly

## 2. BlockEntityScribeLectern — implements IScribeDocumentHost

- [ ] 2.1 Add `IScribeDocumentHost` to `BlockEntityScribeLectern`'s interface list
- [ ] 2.2 Add explicit interface members: `BackdropSpec` → `ScribeBackdrops.LecternPage`; `GetLayout(w)` → `new ScribeLayout(w, 1160f/1024f)`; `DefaultDocumentTitle` → `"Lectern"`
- [ ] 2.3 Retype the `dialog` field from `GuiDialogScribeLecternLibGui?` to `ScribeDialogBase?`; update `OpenDialog` to `new GuiDialogScribeLecternLibGui(Pos, this, capi)`
- [ ] 2.4 Confirm `dotnet build` passes with no errors after steps 2.1–2.3

## 3. ScribeDocument.DefaultTitle

- [ ] 3.1 Change `ScribeDocument.DefaultTitle` from `"Lectern"` to `"Untitled"` in `src/Core/ScribeDocument.cs`
- [ ] 3.2 Confirm `Core.Tests` still pass (`dotnet test tests/Core.Tests`)

## 4. Extract row content widget files

- [ ] 4.1 Create `src/Mod/ScribeReadContent.cs` — move `ScribeReadRowData`, `ScribeReadRow` + State, and rename `ScribeLecternReadContent` → `ScribeReadContent` (+ State)
- [ ] 4.2 Create `src/Mod/ScribeEditorContent.cs` — move `ScribeEditRowData`, `ScribeDepartingEditorRow`, `ScribeFrozenEditorRow`, `ScribeEditRow` + State, and rename `ScribeLecternEditorContent` → `ScribeEditorContent` (+ State)
- [ ] 4.3 Create `src/Mod/ScribePinnedContent.cs` — move `ScribePinRowData`, `ScribePinRow` + State, and rename `ScribeLecternPinnedContent` → `ScribePinnedContent` (+ State)
- [ ] 4.4 Create `src/Mod/ScribeRowWidgets.cs` — move `ScribeVsIconGlyph`, `ScribeRowButton` + State, `ScribeRowButtonText` + State, `ScribeRowControlNudge` out of the monolith
- [ ] 4.5 Confirm `dotnet build` passes after all four extractions; fix any missing `using` directives

## 5. Extract ScribeDialogBase

- [ ] 5.1 Create `src/Mod/ScribeDialogBase.cs`; move the `LecternLayout`-based `CreateWindowConfig`, `OnRenderGUI`, `OnGuiClosed`, and all private fields from `GuiDialogScribeLecternLibGui` into the new base class
- [ ] 5.2 Replace all `lectern.*` accesses with `host.*` calls through `IScribeDocumentHost`; replace all `LecternLayout` usages with `ScribeLayout` (via `host.GetLayout(...)`)
- [ ] 5.3 Replace `ScribeDocument.DefaultTitle` in `CommitTitleIfEditing` with `host.DefaultDocumentTitle`
- [ ] 5.4 Add `protected virtual Widget[] GetExtraNavButtons() => Array.Empty<Widget>();` and call it in `BuildRightColNav`, appending the result after the four baseline buttons
- [ ] 5.5 Replace all `ScribeLecternReadContent` → `ScribeReadContent`, `ScribeLecternEditorContent` → `ScribeEditorContent`, `ScribeLecternPinnedContent` → `ScribePinnedContent` call sites in the base
- [ ] 5.6 Confirm `dotnet build` passes after base extraction

## 6. Slim GuiDialogScribeLecternLibGui

- [ ] 6.1 Remove all code now in `ScribeDialogBase` from `GuiDialogScribeLecternLibGui.cs`, leaving only the class declaration, constructor, and (empty) `GetExtraNavButtons` override if needed
- [ ] 6.2 Change `GuiDialogScribeLecternLibGui : GuiDialogBlockEntityBase` → `GuiDialogScribeLecternLibGui : ScribeDialogBase`; update constructor to call `base(pos, host, capi, modSystem)` (or equivalent base signature)
- [ ] 6.3 Remove `LecternLayout` from `GuiDialogScribeLecternLibGui.cs` — it is now `ScribeLayout` in `IScribeDocumentHost.cs`
- [ ] 6.4 Confirm `dotnet build` passes with the slimmed file; file should be ~80 lines

## 7. Verification

- [ ] 7.1 `dotnet build` from repo root produces zero errors and zero warnings (no regressions from renamed types)
- [ ] 7.2 `dotnet test tests/Core.Tests` passes (DefaultTitle change + no Core regressions)
- [ ] 7.3 Manual in-game: open Lectern → Read view shows tasks → switch to Edit → make a change → Done → Read view reflects change
- [ ] 7.4 Manual in-game: open Lectern → Edit view → title field → clear → Done → title resets to "Lectern"
- [ ] 7.5 Manual in-game: open Lectern → Pinned tab → pins visible → switch views → scroll position preserved across view switch
- [ ] 7.6 Manual in-game: two players — Player 1 opens editor → Player 2 sees edit button dimmed → Player 1 closes → Player 2 can edit
