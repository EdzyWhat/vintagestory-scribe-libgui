## Context

The Lectern is the only Scribe document host today. All ~15 message handlers in
`ScribeModSystem` resolve documents by looking up `BlockEntityScribeLectern` by block
position. Adding the Notebook item requires a routing mechanism that works for both placed
blocks and held items, since items have no `BlockPos`.

The `IScribeDocumentHost` interface already abstracts the document API; the only
Lectern-specific coupling is in how the router finds the host from an inbound packet.

## Goals / Non-Goals

**Goals:**
- Add a carried Notebook item with full Read/Edit/Pinned/Settings GUI parity (no Guestbook)
- Replace all position-based packet routing with a `DocId → IScribeDocumentHost` registry
- Remove `BlockPos Pos { get; }` from `IScribeDocumentHost` (was only needed for packet stuffing)
- Keep `src/Core/` untouched (registry and item adapter live in `src/Mod/`)

**Non-Goals:**
- Crafting recipe (Creative inventory only for now)
- Multiplayer Notebook sharing / lock (owner-only access; one holder at a time)
- Guestbook on the Notebook
- Custom Notebook art (placeholder shape from Wanderer's Sketchbook)

## Decisions

### 1. DocId → IScribeDocumentHost registry in ScribeModSystem

`ScribeModSystem` owns a `Dictionary<Guid, IScribeDocumentHost> _hostRegistry`.

- `RegisterHost(IScribeDocumentHost host)` and `UnregisterHost(Guid docId)` are public
  methods called by the Lectern BE and `NotebookHost`.
- All handler methods replace `TryGetLectern(world, x, y, z)` with
  `TryResolveHost(docId)`.
- Every packet that previously carried `PosX/PosY/PosZ` now carries `DocIdBytes` (16-byte
  `byte[]`). Existing message classes are updated in-place; no new packet for the Lectern
  edit flow.

**Alternative considered:** keep position routing for Lecterns, add a parallel DocId route
only for Notebooks. Rejected because it doubles the handler count and leaves two
competing routing schemes indefinitely.

### 2. NotebookHost : IScribeDocumentHost adapter

`NotebookHost` wraps an `IPlayer`, the held `ItemSlot`, and the mod's `ScribeModSystem`
reference. It does NOT inherit from `BlockEntityScribeLectern`.

```
NotebookHost
  ├── Document        → ScribeDocumentAttributes.TryReadFrom(slot.Itemstack) ?? empty
  ├── ApplyLocalOptimisticEdit(doc) → ScribeDocumentAttributes.WriteTo(slot.Itemstack, doc)
  ├── IsLockedByOther(_)   → always false
  ├── Guestbook            → throws NotSupportedException (never called for Notebook)
  ├── BackdropSpec         → ScribeBackdrops.LecternPage (reuses Lectern art for now)
  ├── GetLayout(w)         → ScribeLayoutProportions.Default (same as Lectern)
  └── DefaultDocumentTitle → "Notebook"
```

`IScribeDocumentHost.Pos` is removed from the interface in this change. `NotebookHost`
does not implement it. Lectern BE loses the property too (only existed for packet stuffing).

### 3. ItemScribeNotebook : Item

Key responsibilities:

- `OnHeldInteractStart`: creates a `NotebookHost`, registers it, opens
  `GuiDialogScribeNotebook`. Returns `true` (handled).
- `OnUnloaded` / dialog close callback: flushes pending edits, calls
  `modSystem.UnregisterHost(docId)`.
- Fresh stack (no `"scribeDocument"` key): writes an empty `ScribeDocument` with a new
  `Guid` on first open so subsequent opens reuse the same `DocId`.
- `MaxStackSize = 1`.

The dialog's `OnGuiClosed` callback is the authoritative unregister point (covers ESC,
death, disconnect, and inventory close). `ItemScribeNotebook.OnHeldInteractStart` passes a
`closeCallback` into the dialog constructor.

### 4. GuiDialogScribeNotebook : ScribeDialogBase

Thin subclass, exactly like `GuiDialogScribeLecternLibGui` but:

- Passes `BlockPos.Zero` to the base constructor (`ScribeDialogBase` still requires a
  `BlockPos` for the engine range-check; `BlockPos.Zero` is safe given the override below).
- Overrides `InteractionRange` → `double.MaxValue` so the engine's frame-by-frame
  distance check never auto-closes the dialog (Notebooks are not proximity-bound).
- Does not override `GetExtraNavButtons()` (no Guestbook tab).

### 5. ScribeNotebookSaveMessage

New packet: `DocIdBytes` (16-byte `byte[]`) + `DocumentBytes` (`byte[]`). Server handler
calls `TryResolveHost(docId)` → `host.ApplyEdit(...)` → reply with
`ScribeEditDocumentMessage`.

This is structurally identical to the refactored `ScribeEditDocumentMessage` (which also
moves from `PosX/Y/Z` to `DocIdBytes`), but kept separate so the Notebook save can carry
any item-specific context in the future without conflating it with the block-edit path.

**Alternative:** reuse the same `ScribeEditDocumentMessage` for both Lectern and Notebook
after the `DocIdBytes` refactor. Rejected for now to keep the packet name unambiguous and
avoid a single handler branching on "is this a block or an item?"

### 6. Packet migration (all PosX/Y/Z → DocIdBytes)

Message classes updated in-place:

| Class | Old fields | New field |
|---|---|---|
| `ScribeEditDocumentMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeLockMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeReleaseLockMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeSetTaskDoneMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribePinMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeUnpinMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeReorderPinsMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeRecordVisitorMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeGuestbookSyncMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |
| `ScribeEditGuestbookNoteMessage` | `PosX, PosY, PosZ` | `DocIdBytes byte[16]` |

`ScribeDialogBase` builds outbound messages by reading `host.Document.DocId` (already
available) instead of `host.Pos`.

### 7. Asset strategy

Shape: copy `arthursjournal/shapes/block/variants/small-normal.json` from
`~/Downloads/wanderers-sketchbook-net10-2.0.5.zip` into
`src/Mod/assets/scribe/shapes/item/notebook.json`. Rename the root node from
`arthursjournal:block/arthursjournal/...` if needed. This is acknowledged as a temporary
placeholder that will be replaced with original art before public release.

Item type: `src/Mod/assets/scribe/itemtypes/scribenotebook.json` with
`class: "ItemScribeNotebook"`, `maxStackSize: 1`, shape ref, `creativeinventory`.

## Risks / Trade-offs

**Breaking packet wire format** → Every client and server must upgrade together. VS
enforces version matching at the mod loader level, so mismatched installs are rejected
before connecting. Existing worlds are safe: `DocId` is already persisted in Lectern
tree attributes.

**`BlockPos.Zero` sentinel in GuiDialogScribeNotebook** → The engine's `IsDuplicate`
check (in `GuiDialogBlockEntity`) looks for an already-open dialog at the same
`BlockEntityPosition`. Two notebooks at `(0,0,0)` could falsely flag as duplicates if a
player somehow opened two at once. Practically impossible (only one item in hand; opening a
second would close the first). `IsDuplicate = true` in this edge case just silently skips
the open — acceptable risk.

**`double.MaxValue` for `InteractionRange`** → Prevents proximity auto-close for the
Notebook. No negative side-effects; the range check is only meaningful for placed blocks.

**`NotebookHost.Guestbook` throws** → `BuildVisitorsContent()` in `ScribeDialogBase` calls
`host.Guestbook`. Since `GuiDialogScribeNotebook` never adds the Visitors nav button, this
path is never reached. The `throw` is a safeguard against future accidental access.

## Open Questions

- **Crafting recipe** — deferred; to be designed in a future change once the item is stable.
- **Custom Notebook art** — placeholder art is used for v1; original art is a future task.
- **Multiple notebooks in different inventory slots** — each has its own `ItemStack` with
  its own `DocId`; they are independent documents. This is intentional but should be noted
  in the in-game handbook.
