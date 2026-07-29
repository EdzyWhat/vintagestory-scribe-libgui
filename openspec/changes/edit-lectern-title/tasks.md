## 1. Core — ScribeDocument and codec

- [x] 1.1 Add `string Title { get; set; }` to `ScribeDocument` (`src/Core/ScribeDocument.cs`). Default value: `"Lectern"`.
- [x] 1.2 Add a `const string DefaultTitle = "Lectern"` and `const int MaxTitleLength = 80` to a suitable constants class (or inline in `ScribeDocument`).
- [x] 1.3 Bump the codec version in `ScribeDocumentCodec` (`src/Core/ScribeDocumentCodec.cs`). In the new version's write path, serialize `Title` after the existing fields. In the read path for the new version, deserialize `Title`; supply `"Lectern"` if absent or blank after trim.
- [x] 1.4 In the prior-version read path, supply `Title = "Lectern"` (no field present in old bytes).
- [x] 1.5 Write Core unit tests (`tests/Core.Tests/`): round-trip with a non-default title; deserialize prior-version bytes produces `Title == "Lectern"`; deserialize new-version bytes with blank title produces `Title == "Lectern"`.

## 2. GUI — `BuildDocumentHeader` helper

- [x] 2.1 Add `bool _isTitleEditing` field and `TextEditingController _titleController` / `FocusNode _titleFocusNode` fields to `GuiDialogScribeLecternLibGui`.
- [x] 2.2 Implement `BuildDocumentHeader(bool editable)`: returns a `Row` containing the title text (or input when `_isTitleEditing && editable`) and, when `editable`, a `TitleButton`/`IconButton` with the `"scribeedit"` icon on the right.
- [x] 2.3 When `editable = false`: render title as a plain `Text` widget using the title text style (match the dialog's title font size and `OnSurface` colour). No pencil icon.
- [x] 2.4 When `editable = true` and `_isTitleEditing = false`: render title as `Text` + pencil `TitleButton`. Pencil `onTap` sets `_isTitleEditing = true`, initializes `_titleController.Text = scratch.Title`, calls `ForceRebuild()`, then requests focus on `_titleFocusNode`.
- [x] 2.5 When `editable = true` and `_isTitleEditing = true`: render a LibGUI `TextField` bound to `_titleController` and `_titleFocusNode`. Enforce 80-char cap (via `MaxLength` or `onKeyDown` guard). Register a `_titleFocusNode` change listener (`OnTitleFocusChanged`) in the dialog's `initState` (or equivalent lifecycle hook).
- [x] 2.6 Implement `OnTitleFocusChanged`: when focus is lost, trim `_titleController.Text`; if empty, replace with `"Lectern"`; clamp to 80 chars; write to `scratch.Title`; set `_isTitleEditing = false`; call `ForceRebuild()`; call `FlushIfDirty()`.

## 3. GUI — Wire header into all three view builders

- [x] 3.1 In `BuildReadContent()`, insert `BuildDocumentHeader(editable: false)` above the existing content (above the `Divider` / scroll region).
- [x] 3.2 In `BuildEditorContent()`, insert `BuildDocumentHeader(editable: true)` above the existing content.
- [x] 3.3 In `BuildPinnedContent()`, insert `BuildDocumentHeader(editable: false)` above the existing content.
- [x] 3.4 Ensure `_isTitleEditing` is reset to `false` when the view mode changes (in `EnterReadMode`, `TryEnterEditor`, `OnClickSwitchToPinned`, and any close path), so the pencil state doesn't bleed across tab switches.

## 4. In-game verification

- [ ] 4.1 Open a fresh Lectern — confirm title shows `"Lectern"` in read, edit, and pin views.
- [ ] 4.2 In edit view, click the pencil — confirm the title becomes an editable input.
- [ ] 4.3 Type a title (e.g. "Stone Age Notes") and click away — confirm the title reverts to text and shows "Stone Age Notes".
- [ ] 4.4 Clear the title input and click away — confirm title resets to `"Lectern"`.
- [ ] 4.5 Attempt to type more than 80 characters — confirm the input stops accepting beyond the 80th.
- [ ] 4.6 Switch from edit to read while the title input is open — confirm input closes gracefully (saves current value) and read view shows the title without a pencil.
- [ ] 4.7 Save and reload the world — confirm the title persists.
- [ ] 4.8 Confirm no pencil icon appears in read view or pin view.
