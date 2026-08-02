## 1. Core policy

- [x] 1.1 Add `src/Core/ScribeDocumentPolicy.cs`: a value type with `int? MaxBlocks`, `int? MaxPins`,
  `bool ReadOnly`; a `null` limit means uncapped. No Vintage Story API reference.
- [x] 1.2 Add a `Tablet` preset (`MaxBlocks = 10` task blocks, `MaxPins = 1`) and `CanAdd`/`CanPin`
  predicates (false once the count reaches the cap; always true when the cap is `null`).
- [x] 1.3 Add Core unit tests in `tests/Core.Tests`: uncapped policy permits any count; Tablet
  preset caps at 10 tasks (9 → allowed, 10 → refused) and 1 pin; `ScribeDocument.AddTask` itself
  stays uncapped (cap only at the boundary).

## 2. Tablet item class

- [x] 2.1 Add `src/Mod/ItemScribeTablet.cs` modeled on `ItemScribeNotebook`: `OnLoaded` sets
  `MaxStackSize = 1` and builds interactions; `OnHeldInteractStart` opens the dialog (client
  `PreventDefault`) with shift → base pass-through for ground storage; `GetHeldItemInfo` appends the
  title line via `ScribeDocumentAttributes.TryReadFrom`; `OnCreatedByCrafting` records the server-side
  `Crafted` history entry. NO stylus-in-offhand edit gate.
- [x] 2.2 In `OnHeldInteractStart`, open the tablet via a `TabletHost` and the **existing**
  `GuiDialogScribeNotebook` (interim reuse until Proposal C's bespoke dialog).

## 3. Tablet host

- [x] 3.1 Add `src/Mod/TabletHost.cs` as a thin variant of `NotebookHost` (`IScribeDocumentHost`):
  `GetLayout(px) => new ScribeLayout(px, 1160f/1024f)`, `DefaultDocumentTitle = "Tablet"`, reuse the
  server write-through + `Flush()` and the existing `ScribeNotebookSaveMessage` (NO new packet).
- [x] 3.2 Enforce the `ScribeDocumentPolicy.Tablet` preset at the mutation boundary in `TabletHost`
  (consult `CanAdd`/`CanPin` before adding tasks / pins).

## 4. Registration

- [x] 4.1 In `src/Mod/ScribeModSystem.cs` `Start()` (next to the existing `RegisterItemClass` calls,
  ~line 172), add `api.RegisterItemClass("ItemScribeTablet", typeof(ItemScribeTablet));`. Do NOT
  touch the frozen network message registration order.

## 5. Assets

- [x] 5.1 Add `src/Mod/assets/scribe/itemtypes/scribetablet.json`: `variantgroups` `material
  [clay, wax]`, `class: ItemScribeTablet`, `maxstacksize: 1`, `GroundStorable` behavior, creative
  inventory. PLACEHOLDER art: point `shape` at a vanilla `game:` clutter tablet
  (`shapes/block/clutter/tablet-clay1..9.json`) with texture remaps to `block/clay/aged-ceramic1` +
  `block/overlay/writing`. Verify held/inventory/ground rendering; fall back to a flat item texture
  if the block shape misbehaves.
- [x] 5.2 Add `src/Mod/assets/scribe/recipes/grid/scribetablet-clay.json` — clay + sticks style.
  PLACEHOLDER ingredient codes (e.g. `game:clay-blue` + `game:stick`); finalize against the
  installed game's actual codes during implementation.
- [x] 5.3 Add `src/Mod/assets/scribe/recipes/grid/scribetablet-wax.json` — beeswax + frame style.
  PLACEHOLDER ingredient codes (e.g. `game:beeswax` + a wood/frame code); finalize during
  implementation.
- [x] 5.4 Add lang keys to `src/Mod/assets/scribe/lang/en.json` for both variants: item names +
  descriptions and the `itemhelp-*-open` interaction line (mirror the notebook keys).

## 6. Verification

- [x] 6.1 `dotnet test` — the new `ScribeDocumentPolicy` tests pass alongside the existing Core suite.
- [x] 6.2 In-game: craft a clay tablet and a wax tablet from the grid recipes; confirm each opens a
  document dialog.
- [x] 6.3 In-game: confirm the title persists across close/reopen and across drop/pickup (docId on
  the stack).
- [x] 6.4 In-game: confirm the 10-task cap (the "add task" affordance disables at 10) and the 1-pin
  cap are enforced (pinning a new task at the cap seamlessly SWAPS the pin — verified across
  drop/pickup too).
- [x] 6.5 Confirm the interim `GuiDialogScribeNotebook` opens for the tablet (bespoke dialog is
  Proposal C).
- [ ] 6.6 Atlas/integration: the local pre-push gate stages the `gui` dep and exercises the item;
  keep synthetic player names ≤16 chars and ensure `ItemScribeTablet` is registered before staging.

## Implementation notes (2026-08-02)

- **Policy enforcement mechanism (resolved).** Task adds happen in the client editor's `scratch`
  document, never through a host method, so the cap could not be enforced purely inside `TabletHost`
  as the design first implied. Enforcement is at the dialog's editor mutation boundary via a new
  `IScribeDocumentHost.Policy` member (default interface member = `ScribeDocumentPolicy.Unlimited`).
  Because `NotebookHost` declares the interface, a bare `Policy` on the `TabletHost` subclass would
  NOT re-map the interface dispatch — so `Policy` is declared `virtual` on `NotebookHost`
  (`=> Unlimited`) and `override`n on `TabletHost` (`=> Tablet`). `ScribeDialogBase` gained
  `CanAddTaskUnderPolicy()` / `CanPinUnderPolicy()`, guarding `OnClickAddTask`,
  `EditorInsertTaskBelow` (Enter=new-task), and both pin-toggle paths; the footer "Add task" button
  is also dimmed + inert at the cap (a new `addTaskEnabled` flag on `ScribeEditorContent`, default
  true so Lectern/Notebook are byte-identical). `NotebookHost` was unsealed and
  `DefaultDocumentTitle` made `virtual` so `TabletHost` reuses its tested write-through/history/pickup
  code verbatim instead of duplicating ~240 lines of delicate sync.
- **Ingredient codes (finalized).** Clay Tablet = `game:clay-blue` over `game:stick` (1×2 grid);
  Wax Tablet = `game:beeswax` over `game:stick`. Verified against the installed 1.22.6 assets
  (`itemtypes/resource/clay.json` variant `type:[blue,red,fire]`; `beeswax`; `stick`).
- **Placeholder art (finalized for this round).** Both variants point at the vanilla clutter shape
  `game:block/clutter/tablet-clay1` with its `ceramic`/`writing` texture remaps — a *block* shape on
  an *item*; held/inventory/ground transforms are first-pass and to be tuned in-game (6.2). Authentic
  clay-pillow / wax-diptych art is deferred.
- **Post-testing fix — drop/pickup wiped the document (marker interface).** Five server- and
  client-side gate sites recognized document-bearing items by the brittle type list
  `is (ItemScribeNotebook or ItemClockmakerNotebook)`; a tablet matched none, so
  `OnServerReceivedNotebookSave` dropped every tablet save server-side and the authoritative stack
  stayed empty — dropping (server-authoritative) then revealed a wiped tablet. Fixed by adding an
  empty marker interface `IScribeDocumentItem` implemented by all three item classes and switching
  all five gates to it (`ScribeModSystem.Network.cs` save + pickup, `PinOperations.cs` host
  resolution, `History.cs` carried scan, `GuiDialogScribeNotebook.cs` active-slot check). The three
  inventory-scan sites now build a `TabletHost` (vs `NotebookHost`) for tablets so tier policy/title
  apply server-side.
- **Post-testing change — 1-pin cap now SWAPS instead of refusing.** Per the requested behavior, at
  the tablet's 1-pin cap, pinning a new task releases the player's older pin(s) for that document
  first, then pins the new one. Both pin-toggle paths (`TogglePinnedEditorTask`,
  `OnReadViewTogglePinned`) route through a shared `ScribeDialogBase.TogglePinWithPolicy` helper;
  `ReleasePinsToFitPolicy` is a no-op for uncapped tiers, so Lectern/Notebook are unchanged. Verified
  in-game including across drop/pickup of the same tablet.
