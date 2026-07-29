## Why

Every Lectern looks identical today. Players with multiple Lecterns in a world have no way to
distinguish them at a glance — there is no title, label, or name. Adding an editable title gives
each Lectern an identity, visible in all views.

## What Changes

- `ScribeDocument` gains a `Title` string field (max 80 chars, default `"Lectern"`).
- All Lectern views (read, edit, pin) show the title at the top of the central region.
- In edit view only, a pencil icon (`"scribeedit"`) appears to the right of the title text.
- Clicking the pencil switches the title row to an inline single-line text input (80-char limit).
- On blur (unfocus), the input saves the title to the document, reverts to display text,
  and flushes the document to the server — using the existing `FlushIfDirty()` path.
- Saving an empty/whitespace-only title resets it to `"Lectern"`.
- The title is part of the document and travels with the existing `ScribeEditDocumentMessage`
  save flow — no new packet type.

## Capabilities

### New Capabilities

- `lectern-title`: Editable per-Lectern title displayed in all views; pencil-to-edit in
  edit view; on-blur save; 80-char cap; defaults to `"Lectern"`.

### Modified Capabilities

- `task-note-document`: `ScribeDocument` gains a `Title` field; codec version bumped to
  handle the new field and provide a default for documents deserialized from older versions.
- `lectern-block`: The `BuildDocumentHeader` widget is now rendered above the central region
  in all Lectern views; the block entity `To/FromTreeAttributes` is unaffected (title travels
  in the document bytes, not as a separate tree attribute).

## Impact

- `src/Core/ScribeDocument.cs` — new `string Title` property.
- `src/Core/ScribeDocumentCodec.cs` — codec version bump; serialize/deserialize `Title`;
  older-version deserialize supplies `"Lectern"` as the default.
- `src/Mod/GuiDialogScribeLecternLibGui.cs` — `BuildDocumentHeader(bool editable)` widget;
  `_isTitleEditing` bool flag; `_titleController`/`_titleFocusNode` for the input;
  pencil click handler; blur handler (trim, default, clamp, flush).
- `tests/Core.Tests/` — new codec tests for `Title` round-trip and default fallback.
- No new mod dependencies. No new packet types. No breaking changes to the block entity's
  tree attribute keys.
