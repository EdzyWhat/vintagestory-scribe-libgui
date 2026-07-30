## 1. Packet migration — replace PosX/Y/Z with DocIdBytes

Messages that still carry `PosX/PosY/PosZ`: `ScribeEditDocumentMessage`,
`ScribeRequestAccessMessage`, `ScribeReleaseLockMessage`, `ScribeRecordVisitorMessage`,
`ScribeGuestbookSyncMessage`, `ScribeEditGuestbookNoteMessage`.

- [x] 1.1 In each of the 6 message classes above, remove the three `PosX/Y/Z` int
  properties and add `public byte[]? DocIdBytes { get; set; }` (protobuf field 1; renumber
  existing fields if needed so DocIdBytes is member 1).
- [x] 1.2 In `ScribeDialogBase`, update all 9+ outbound packet-build sites (lines 631–632,
  1152–1154, 1363–1365, 1918, etc.) to use
  `DocIdBytes = host.Document.DocId.ToByteArray()` instead of `PosX/Y/Z = host.Pos.*`.
- [x] 1.3 Remove `BlockPos Pos { get; }` from `IScribeDocumentHost` and remove its
  implementation from `BlockEntityScribeLectern`. Confirm the project builds with no
  `host.Pos` references remaining.

## 2. Host registry in ScribeModSystem

- [x] 2.1 Add `private readonly Dictionary<Guid, IScribeDocumentHost> _hostRegistry = new()`
  to `ScribeModSystem`. Add `public void RegisterHost(IScribeDocumentHost host)` (keyed on
  `host.Document.DocId`) and `public void UnregisterHost(Guid docId)`.
- [x] 2.2 Add `private IScribeDocumentHost? TryResolveHost(byte[]? docIdBytes)` using the
  existing `TryReadGuid` helper. This replaces `TryGetLectern`.
- [x] 2.3 Update all server-side handlers in `ScribeModSystem` that call `TryGetLectern` to
  call `TryResolveHost` instead (handlers: `OnServerReceivedEdit`,
  `OnServerReceivedReleaseLock`, `OnServerReceivedRequestAccess`,
  `OnServerReceivedRecordVisitor`, `OnServerReceivedEditGuestbookNote`).
- [x] 2.4 Update client-side handlers that reconstruct a `BlockPos` from message fields
  (`OnClientReceivedEditReply`, `OnClientReceivedGuestbookSync`) to use
  `TryResolveHost` instead.
- [x] 2.5 Remove `TryGetLectern`, `TryResolveLectern`, and `EnumerateLoadedLecterns`
  from `ScribeModSystem` (they become dead code after the above).

## 3. Lectern BE registers in the host registry

- [x] 3.1 In `BlockEntityScribeLectern.Initialize()`, call
  `modSystem.RegisterHost(this)` after the document is loaded. In `OnBlockRemoved()`,
  call `modSystem.UnregisterHost(Document.DocId)`.
- [x] 3.2 Remove `BlockPos Pos { get; }` from `BlockEntityScribeLectern` (already covered
  by task 1.3 — confirm removal is complete and the BE still compiles).

## 4. NotebookHost adapter

- [x] 4.1 Create `src/Mod/NotebookHost.cs` implementing `IScribeDocumentHost`:
  - Constructor: `NotebookHost(IPlayer player, ItemSlot slot, ScribeModSystem modSystem)`
  - `Document`: reads via `ScribeDocumentAttributes.TryReadFrom(slot.Itemstack)` — creates
    an empty doc with a new `DocId` if absent, then writes it back (first-access init)
  - `ApplyLocalOptimisticEdit(doc)`: calls `ScribeDocumentAttributes.WriteTo(slot.Itemstack, doc)`
  - `IsLockedByOther(_)`: returns `false`
  - `BackdropSpec`: returns `ScribeBackdrops.LecternPage`
  - `GetLayout(w)`: returns `new ScribeLayout(w, 1160f/1024f)` (same as Lectern)
  - `DefaultDocumentTitle`: returns `"Notebook"`
  - `Guestbook`: throws `NotSupportedException`

## 5. ItemScribeNotebook item class

- [x] 5.1 Create `src/Mod/ItemScribeNotebook.cs` as `public class ItemScribeNotebook : Item`.
  - `OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)`:
    - Only act on `firstEvent && byEntity is EntityPlayer ep && ep.World.Side == EnumAppSide.Client`
    - Create `NotebookHost(player, slot, modSystem)`, call `modSystem.RegisterHost(host)`
    - Open `new GuiDialogScribeNotebook(host, capi)`, subscribe to its `OnClosed` event to
      call `modSystem.UnregisterHost(host.Document.DocId)`
    - Set `handling = EnumHandHandling.PreventDefault`
  - Override `MaxStackSize` property → `1`

## 6. ScribeNotebookSaveMessage

- [x] 6.1 Create `src/Mod/ScribeNotebookSaveMessage.cs`:
  ```csharp
  [ProtoContract]
  public sealed class ScribeNotebookSaveMessage
  {
      [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }
      [ProtoMember(2)] public byte[]? DocumentBytes { get; set; }
  }
  ```
- [x] 6.2 Register `ScribeNotebookSaveMessage` in `ScribeModSystem.Start()` alongside the
  other message types. Add server handler `OnServerReceivedNotebookSave`: resolve host via
  `TryResolveHost`, call `host.ApplyEdit(...)`, send `ScribeEditDocumentMessage` reply.

## 7. GuiDialogScribeNotebook

- [x] 7.1 Create `src/Mod/GuiDialogScribeNotebook.cs` as a sealed subclass of `ScribeDialogBase`:
  - Constructor: `(IScribeDocumentHost host, ICoreClientAPI capi)` — passes `BlockPos.Zero`
    to base (`base(BlockPos.Zero, host, capi)`)
  - Override `protected override double InteractionRange => double.MaxValue`
  - Do NOT override `GetExtraNavButtons()` (no Guestbook)
- [x] 7.2 In `ScribeDialogBase`, update the autosave flush to use
  `ScribeNotebookSaveMessage` when `host` is a `NotebookHost`, OR make
  `ScribeEditDocumentMessage` (now DocId-keyed) work for both paths. Decision: check if
  `ScribeEditDocumentMessage` can be used directly after the packet migration in task 1 — if
  yes, no separate save path is needed for the Notebook and this task is just a confirmation
  check. If a separate path is needed, implement it here.

## 8. Assets

- [x] 8.1 Extract `arthursjournal/shapes/block/variants/small-normal.json` from
  `~/Downloads/wanderers-sketchbook-net10-2.0.5.zip` and copy to
  `src/Mod/assets/scribe/shapes/item/notebook.json`. Inspect the JSON and confirm the shape
  node references are valid (update any hardcoded modid prefixes from `arthursjournal:` to
  `scribe:` where needed).
- [x] 8.2 Create `src/Mod/assets/scribe/itemtypes/scribenotebook.json`:
  - `"code": "scribenotebook"`, `"class": "ItemScribeNotebook"`, `"maxStackSize": 1`
  - `"shape": { "base": "scribe:item/notebook" }`
  - `"creativeinventory": { "scribe-*": ["*"] }` (or equivalent creative tab registration)
- [x] 8.3 Add lang keys to `src/Mod/assets/scribe/lang/en.json`:
  - `"item-scribenotebook"` → `"Notebook"`
  - `"item-scribenotebook-desc"` → `"A personal notebook. Write tasks and notes anywhere."`

## 9. Post-implementation fixes (discovered during in-game testing)

- [x] 9.1 Run `dotnet build` from `src/Mod/` — confirm zero errors and zero new warnings.
- [x] 9.2 Run `dotnet test` from `tests/Core.Tests/` — confirm all tests still pass.
- [x] 9.F1 Fix: Lectern GUI no longer opened after packet migration. Root cause: `RegisterHost`
  was server-only, so the client-side `_hostRegistry` was always empty and
  `OnClientReceivedEditReply` could not find the BE. Fix: call `RegisterHost` on both
  sides in `BlockEntityScribeLectern.Initialize`.
- [x] 9.F2 Fix: Notebook task-wipe on drop/pickup. Root cause: `OnServerReceivedNotebookSave`
  guarded with `existing?.DocId != docId`; a fresh stack has no stored doc so `existing`
  is null, the comparison trips, and every autosave is silently dropped. Fix: only reject
  when an existing doc is present and its DocId mismatches.
- [x] 9.F3 Fix: Notebook dialog does not close when the item leaves the active hand slot.
  Fix: subscribe to `capi.Event.AfterActiveSlotChanged` in `GuiDialogScribeNotebook`
  constructor; call `TryClose()` when the active slot no longer holds an
  `ItemScribeNotebook`. Unsubscribe in `OnGuiClosed`.
- [x] 9.F4 Fix: `gui@3.1.0` crash on close-button click (`ButtonState.PlaySound` calls
  `Element.Owner.GetSoundPlayer()` after the element is unmounted). Workaround: install
  `SilentSoundPlayer` on `BuildOwner` at the top of `OnGuiClosed` so any deferred
  `SetState` callbacks get a non-null player and complete harmlessly.
- [x] 9.F5 Fix: Notebook texture used a placeholder wood-plank tile. Fix: extract
  `leather1.png` from Wanderer's Sketchbook zip into `scribe/textures/items/notebook-cover.png`;
  update `scribenotebook.json` texture reference.
- [x] 9.F6 Add `groundTransform` and `tpHandTransform` to `scribenotebook.json` (item had
  no ground-drop or third-person transforms — appeared tiny on the ground).
- [x] 9.F7 Add `/scripttf <target> <prop> <value>` dev command for tuning item transforms
  in-game without restarting (registered client-side in `ScribeModSystem`).
- [ ] 9.3 In-game: obtain the Notebook from the Creative inventory, open it, write a task,
  close and reopen — confirm the task persists.
- [ ] 9.4 In-game: confirm the Lectern still opens, saves, and pins correctly (regression
  check after the packet migration).
- [ ] 9.5 In-game: confirm pin/unpin from a Notebook works (task appears in HUD / Pin Tab).
- [ ] 9.6 In-game: confirm the Notebook dialog does NOT auto-close when walking away from
  the point where it was opened.
