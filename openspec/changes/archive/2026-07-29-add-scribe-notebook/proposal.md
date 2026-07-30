## Why

The Scribe Lectern is a placed block — useful in a base, but unavailable while the player
is traveling or exploring. A Notebook item gives players access to their notes and task list
from anywhere in their inventory, using the same document model and GUI they already know
from the Lectern.

## What Changes

- Add `ItemScribeNotebook`: a carried item that opens the full Scribe dialog (Read / Editor
  / Pinned / Settings) without a Guestbook tab.
- Add `NotebookHost`: an `IScribeDocumentHost` adapter that wraps the player's held
  `ItemSlot` and persists the document in `ItemStack.Attributes["scribeDocument"]`.
- Add `GuiDialogScribeNotebook`: a thin `ScribeDialogBase` subclass (no extra nav buttons,
  auto-close range disabled).
- Add `ScribeNotebookSaveMessage`: a new network packet for saving notebook edits
  server-side (carries `DocId` + document bytes instead of a block position).
- **Refactor `ScribeModSystem` host lookup**: replace ~15 `TryGetLectern()` call sites with
  a `DocId → IScribeDocumentHost` registry. Lecterns register/unregister on
  `Initialize`/`OnBlockRemoved`; `NotebookHost` registers/unregisters on dialog open/close.
  This removes `BlockPos` packet routing for all save and pin messages — **BREAKING** for
  saved packet field names (client and server must be on the same version; VS enforces this).
- Remove `IScribeDocumentHost.Pos`: no longer needed once packet routing uses `DocId`.
- Assets: placeholder item shape (copied from Wanderer's Sketchbook mod zip), item type
  definition, lang keys. Available in the Creative inventory; no crafting recipe for v1.

## Capabilities

### New Capabilities

- `notebook-item`: carried item with full document / pin / settings GUI parity with the
  Lectern (no Guestbook); persists document in the ItemStack; owner-only access; registered
  in the Creative inventory.
- `host-registry`: `DocId → IScribeDocumentHost` registry in `ScribeModSystem` replacing
  all `TryGetLectern()` lookups; all save/pin/edit messages route by `DocId` instead of
  `BlockPos`; `IScribeDocumentHost.Pos` removed.

### Modified Capabilities

- `lectern-block`: Lectern BE registers/unregisters in the host registry on
  `Initialize`/`OnBlockRemoved`; no behavior change visible to the player.
- `player-pins`: Pin messages now carry `DocId` instead of `BlockPos`; routing is through
  the host registry; pin behaviour is unchanged.
- `task-note-document`: All edit packets now carry `DocId` + bytes; no document model changes.

## Impact

- **`src/Mod/ScribeModSystem.cs`**: replace `TryGetLectern`/`TryResolveLectern`/
  `EnumerateLoadedLecterns` with a `Dictionary<Guid, IScribeDocumentHost>` registry; all
  ~15 handler call sites updated.
- **`src/Mod/IScribeDocumentHost.cs`**: remove `BlockPos Pos { get; }`.
- **`src/Mod/BlockEntityScribeLectern.cs`**: register self on `Initialize`; unregister on
  `OnBlockRemoved`; stop sending `PosX/PosY/PosZ` in packets.
- **New files**: `ItemScribeNotebook.cs`, `NotebookHost.cs`,
  `GuiDialogScribeNotebook.cs`, `ScribeNotebookSaveMessage.cs`.
- **Assets**: `scribe/itemtypes/scribenotebook.json`, `scribe/shapes/item/notebook.json`
  (placeholder from Wanderer's Sketchbook zip), `en.json` additions.
- **No `src/Core/` changes**: all new types live in `src/Mod/`.
