# v5 — Backpack (portability) + Pinned-task HUD + Quick-capture

> **Status:** exploration/design spec (2026-07-21). NOT an OpenSpec change, NOT implemented
> code. When v5 is picked up, this becomes the input to an `openspec-propose`. Follows the
> structure in `docs/specs/README.md`.
>
> **STALE pin model (flagged 2026-07-26).** The pinned-task sections below describe the OLD
> per-block pin model (`ScribeBlock.Pinned`, `ScribeDocument.TogglePinned`, `MaxPinnedTasks = 3`).
> That model was superseded: pinning moved to a **per-player `ScribePinStore`** keyed by
> `(DocId, TaskId)` (v3→v4 codec dropped the per-block `pinned` flag — see `docs/specs/README.md`),
> the HUD shipped with a **configurable `HudMaxRows`** (default 3, not a hard ≤3 cap), and the
> editable/reorderable pins surface is the **Pin Tab** (`scribe-pin-editor`). Read the pin-model
> details here as historical; the per-player store + `HudMaxRows` + Pin Tab are authoritative. The
> backpack/quick-capture portions of this spec are unaffected.

## Summary

v5 is the **portability tier**: the player's whole note collection goes on the go, and their
top few tasks become ambient. It merges three ROADMAP items into one cluster:

1. **Note-taker's backpack** — a held/equipped artifact that opens the *entire* collection
   via a native rebindable hotkey (not just by right-clicking a block). Building on the v2
   notebook's `docId`-on-item held-document infrastructure.
2. **Pinned-task HUD** — an **always-on** on-screen overlay showing **≤3 pinned tasks**,
   drawn every frame and refreshed on a tick, persisting across sessions. The pin flag
   already exists in Core (`ScribeBlock.Pinned`, `ScribeDocument.TogglePinned`) and the
   lectern already toggles it; v5 finally *renders* it.
3. **Quick-capture hotkey** — a dedicated hotkey that jots **one line** into the held writing
   item without opening its full document. Called out in ROADMAP's UX-lessons section as
   "likely the single highest-leverage UX investment for a held writing item."

**HUD technology decision (decisive): native VS `HudElement`, not ImGui.** The no-new-hard-deps
guardrail forbids a shipping dependency on VSImGui/ImGui, and VSImGui is *excluded from Release
builds* entirely (`Mod.csproj` gates its `<Reference>` on `Configuration == 'Debug'`; confirmed
in `VSAPI-NOTES.md` "VSImGui debug overlay" section and ROADMAP's adopted-ImGui note). An ImGui
HUD would therefore not exist in the shipped mod at all. It is additionally blocked from even
*rendering* on the author's Apple-Silicon Mac (OpenGL 4.1 cap; see the same VSAPI-NOTES entry).
ToastLib was already researched and rejected (stale for 1.22.x, ImGui hard-dep, transient-toast
lifecycle with no persist-and-update primitive — ROADMAP "Parked" section). The vanilla
`HudElement` path is confirmed viable below and is the recommendation.

---

## VS API hooks

All confirmed by decompiling the installed `/Applications/Vintage Story.app/VintagestoryAPI.dll`
and `VintagestoryLib.dll` (v1.22.x) unless noted; cross-checked against `anegostudios` source
via research.

### Always-on HUD overlay

- **`Vintagestory.API.Client.HudElement : GuiDialog`** — the base for on-screen overlays.
  Its overrides are exactly what make a dialog behave as an always-on HUD (decompiled verbatim):
  - `public override EnumDialogType DialogType => EnumDialogType.HUD;`
  - `public override string ToggleKeyCombinationCode => null;` (no toggle key by default)
  - `public override bool PrefersUngrabbedMouse => false;` (does **not** grab/free the cursor,
    so gameplay input keeps flowing while it's up)
  - `OnRenderGUI` wraps `base.OnRenderGUI` in a `GlTranslate(0,0,-150)` push/pop (renders behind
    normal dialogs).
- **`EnumDialogType`** has only two values: `Dialog` and `HUD`. There is no third "GUI" value.
  In `GuiDialog`: `TryOpen(withFocus)` only calls `RequestFocus` when `DialogType == Dialog`;
  `OnEscapePressed()` returns `false` (does nothing) when `DialogType == HUD` — so **Escape
  never closes a HUD** and it never steals focus. This is the "always-on, non-intrusive" behavior.
- **Render loop:** `GuiDialog.ShouldReceiveRenderEvents() => opened;` — a HUD renders **every
  frame for as long as it is opened**. So the always-on behavior is simply: call `TryOpen()`
  once (e.g. when player data is ready) and never close it. `OnRenderGUI(float dt)` iterates the
  composers and calls `.Render(dt)`.
- **Per-tick refresh is NOT a base override** — `GuiDialog` has no `OnGameTick`. HUDs register
  their own tick listener: `capi.Event.RegisterGameTickListener(handler, intervalMs)` and update
  the composer's dynamic text inside it; unregister in `Dispose()`.
- **Auto-registration:** `TryOpen()` self-registers (`if (!capi.Gui.LoadedGuis.Contains(this))
  capi.Gui.RegisterDialog(this);`), so a manual `capi.Gui.RegisterDialog` isn't required.

**Confirmed vanilla precedent — `Vintagestory.Client.NoObf.HudElementCoordinates`** (decompiled
from `VintagestoryLib.dll`). This is the ideal template: an always-on, tick-updated, dynamic-text
info HUD anchored to a screen corner. Its exact shape:

```csharp
public class HudElementCoordinates : HudElement
{
    public override string ToggleKeyCombinationCode => "coordinateshud";

    public override void OnOwnPlayerDataReceived()          // compose once, when world/player ready
    {
        var elementBounds = ElementBounds.Fixed(EnumDialogArea.None, 0, 0, 190, 48);
        var bounds        = elementBounds.ForkBoundingParent(5, 5, 5, 5);
        var dialogBounds  = ElementStdBounds.AutosizedMainDialog
                                .WithAlignment(EnumDialogArea.RightTop)             // corner anchor
                                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding);
        SingleComposer = capi.Gui.CreateCompo("coordinateshud", dialogBounds)
            .AddGameOverlay(bounds)                          // the translucent HUD backing plate
            .AddDynamicText("", CairoFont.WhiteSmallishText().WithOrientation(EnumTextOrientation.Center), elementBounds, "text")
            .Compose();
        if (ClientSettings.ShowCoordinateHud) TryOpen();     // open == permanently visible
    }

    public override void OnBlockTexturesLoaded()
    {
        base.OnBlockTexturesLoaded();
        capi.Event.RegisterGameTickListener(Every250ms, 250);   // periodic refresh
        // ClientSettings watcher toggles TryOpen()/TryClose()
    }

    private void Every250ms(float dt)
    {
        if (!IsOpened()) return;
        SingleComposer.GetDynamicText("text").SetNewText(/* fresh string */);
        // …re-anchors below any other RightTop dialog via capi.Gui.GetDialogBoundsInArea(...)
    }
}
```

Takeaways we reuse: compose once in `OnOwnPlayerDataReceived`; `AddGameOverlay` gives the
semi-transparent HUD plate; `AddDynamicText(...,"key")` + `GetDynamicText("key").SetNewText(...)`
is the cheap per-tick update (no recompose); anchor with `EnumDialogArea` + `DialogToScreenPadding`;
open == visible. Note the open-source stand-in `HudBosshealthBars` (vssurvivalmod) additionally
overrides `Focusable => false`, `ShouldReceiveKeyboardEvents() => false`, and empty
`OnMouseDown` to make the HUD entirely non-interactive — we want the same (see Open Questions on
whether clicking a pin to complete it is desirable).

### Rebindable hotkeys

From `IInputAPI` (decompiled):

```csharp
void RegisterHotKey(string hotkeyCode, string name, GlKeys key,
    HotkeyType type = HotkeyType.CharacterControls,
    bool altPressed = false, bool ctrlPressed = false, bool shiftPressed = false);
void SetHotKeyHandler(string hotkeyCode, ActionConsumable<KeyCombination> handler);
HotKey GetHotKeyByCode(string code);
```

- The default key is a **`GlKeys`** enum value. The handler is `ActionConsumable<KeyCombination>`
  — i.e. `bool Handler(KeyCombination comb)`; return `true` to mark the key consumed.
- Register in `StartClientSide(ICoreClientAPI api)`. `HotkeyType` values include
  `GUIOrOtherControls` (appropriate for both our hotkeys). Registration is by string code, and
  the engine persists player rebindings against that code in `clientsettings.json` — so this is
  the native rebindable path the guardrail requires (same mechanism VSImGui itself uses; see
  `VSAPI-NOTES.md`).

### Held-item GUI opening (backpack + quick-capture)

The vanilla writable book (`ItemBook : Item`, vssurvivalmod `Systems/WritingSystem/ItemBook.cs`)
is the precedent for a held item opening a document GUI:

- Right-click path overrides `CollectibleObject.OnHeldInteractStart(ItemSlot slot,
  EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
  ref EnumHandHandling handling)`. **Runs on both sides** — must guard `if (api.Side ==
  EnumAppSide.Client)` before constructing/opening a GUI; set `handling =
  EnumHandHandling.PreventDefault;` to consume. Result flows back via the dialog's `OnClosed`
  callback into a server-authoritative ModSystem (`BeginEdit`/`EndEdit`).
- **Opening from a hotkey instead** (what v5's backpack + quick-capture hotkeys do): in the
  hotkey handler, resolve the active stack via
  `capi.World.Player.InventoryManager.ActiveHotbarSlot` (and/or a dedicated backpack/offhand
  slot — see Open Questions), read its `docId`, and construct + `TryOpen()` the dialog for that
  document. This is the standard combination of the hotkey API and the held-item document path.

### Single-line text-entry dialog (quick-capture)

No built-in one-liner prompt exists; you compose a `GuiDialogGeneric` (owns one `SingleComposer`).
Confirmed helper signatures (decompiled `GuiComposerHelpers`):

```csharp
GuiComposer AddTextInput(this GuiComposer c, ElementBounds bounds, Action<string> onTextChanged,
                         CairoFont font = null, string key = null);   // single-line field
GuiComposer AddButton(this GuiComposer c, string text, ActionConsumable onClick,
                      ElementBounds bounds, EnumButtonStyle style = EnumButtonStyle.Normal, string key = null);
GuiComposer AddDialogTitleBar(this GuiComposer c, string text, Action onClose = null, ...);
GuiComposer AddStaticText(this GuiComposer c, string text, CairoFont font, ElementBounds bounds, string key = null);
```

Read back with `SingleComposer.GetTextInput(key).GetText()`; seed with `.SetValue(...)`; focus
with `SingleComposer.FocusElement(SingleComposer.GetTextInput(key).TabIndex)`. Precedent:
`GuiDialogTextInput : GuiDialogGeneric` in vssurvivalmod `Gui/GuiDialogBlockEntityText.cs` (that
one uses `AddTextArea`; swap for `AddTextInput` for one line).

> **Reuse `VSAPI-NOTES.md` GUI-lifecycle facts:** the quick-capture dialog is trivially small,
> but the "seed values *after* `.Compose()`, not before" rule (composer-lifecycle entry) and the
> `Lang.Get("scribe:<key>")` domain-prefix rule (localization entry) both still apply.

---

## C# data structures

### Core (game-agnostic — MUST NOT reference the VS API)

The pin model already exists and needs only a small, well-contained extension for the ≤3 rule.

**Existing (no change to shape):**
- `ScribeBlock.Pinned : bool` (only meaningful for `Kind == Task`; already serialized by the
  v3 codec — `bool pinned` per block).
- `ScribeDocument.TogglePinned(int index) : bool` — flips the pin on a task, no-op on text/bad
  index.

**New Core additions (proposed):**

```csharp
// ScribeDocument.cs — enforce the ≤3 rule at the model level so every caller (lectern,
// backpack, HUD) shares one definition of "pinned" and cannot exceed the cap.
public const int MaxPinnedTasks = 3;

public int PinnedCount => _blocks.Count(b => b.IsTask && b.Pinned);

public IReadOnlyList<ScribeBlock> PinnedTasks =>
    _blocks.Where(b => b.IsTask && b.Pinned).ToList();   // in document order

// TogglePinned gains a cap check: pinning is rejected (returns false) when already at
// MaxPinnedTasks; unpinning is always allowed. Keep the existing no-op-on-non-task behavior.
public bool TogglePinned(int index)  // revised body, same signature
{
    if (!IsValidIndex(index)) return false;
    var block = _blocks[index];
    if (!block.IsTask) return false;
    if (!block.Pinned && PinnedCount >= MaxPinnedTasks) return false;  // cap only blocks *adding*
    block.Pinned = !block.Pinned;
    return true;
}
```

> **Why the cap lives in Core, not the GUI:** it's a document-model rule, unit-testable with
> `dotnet test` and no game install, and it must hold identically whether a pin is toggled from
> the lectern GUI or a future backpack GUI. New xUnit cases: reject the 4th pin, allow unpin at
> cap, `PinnedTasks` ordering. Codec is unaffected — pin bits already round-trip (v3, no version
> bump needed).

**HUD needs a cross-document view of pins.** A single document caps at 3, but the HUD shows the
player's *overall* top 3. Two shapes to consider (see Open Questions — this is the central
unresolved design decision):

- **(A) Global pin projection** — a lightweight Core value type the Mod assembles from whatever
  documents the player owns and hands to the HUD:
  ```csharp
  public sealed record PinnedTaskView(string SourceDocId, int BlockIndex, string Text, bool Done);
  // Core helper: given a set of (docId, ScribeDocument), return up to MaxPinnedTasks PinnedTaskViews.
  public static IReadOnlyList<PinnedTaskView> CollectPins(
      IEnumerable<(string docId, ScribeDocument doc)> sources, int max = ScribeDocument.MaxPinnedTasks);
  ```
  Pure, game-agnostic, testable. The Mod supplies the (docId, doc) pairs; Core just selects/orders.
- **(B) Per-document HUD only** — the HUD reflects only the *currently-open/held* document's pins.
  Simpler (no cross-document aggregation, no "where do unheld pins live" problem) but weaker as an
  "ambient goals" feature. Recorded as the fallback.

### Mod (VS-API adapters, packets)

**`HudScribePins : HudElement`** (new, client-side). Mirrors `HudElementCoordinates`:
- `ToggleKeyCombinationCode => "scribepinhud"` (a rebindable show/hide toggle for the HUD itself;
  see Open Questions on always-on vs toggleable).
- Composes once in `OnOwnPlayerDataReceived`: an `AddGameOverlay` plate anchored (e.g.)
  `EnumDialogArea.RightMiddle`, holding up to 3 rows, each an `AddDynamicText` line (`"pin0/1/2"`)
  — optionally prefixed with a check glyph for `Done`. Compose for the max 3 rows once; blank
  unused rows rather than recomposing (recompose is the expensive/fragile path per VSAPI-NOTES).
- Registers `capi.Event.RegisterGameTickListener(RefreshPins, 500)` in `OnBlockTexturesLoaded`;
  `RefreshPins` reads the current pin projection and calls `GetDynamicText("pinN").SetNewText(...)`.
  Unregister in `Dispose()`.
- `Focusable => false`, `ShouldReceiveKeyboardEvents() => false`, empty `OnMouseDown` (non-
  interactive), matching `HudBosshealthBars` — unless we decide to allow click-to-complete
  (Open Questions).
- Client-side source of pin data: the client already caches the open document
  (`BlockEntityScribeLectern.Document`); for the backpack/notebook the client will cache the
  held document analogously (v2 infra). The HUD reads from a small client-side registry the mod
  keeps of "documents this client currently knows about," feeding Core's `CollectPins`.

**Hotkeys (client-side, registered in `ScribeModSystem.StartClientSide`):**
- `"scribebackpack"` → handler opens the backpack/collection GUI for the equipped backpack item
  (resolve slot → docId → `TryOpen` the collection dialog). Default key TBD (e.g. `GlKeys.B`),
  `HotkeyType.GUIOrOtherControls`.
- `"scribequickadd"` → handler opens the one-line quick-capture dialog (below). Default key TBD
  (e.g. `GlKeys.N`).
- `"scribepinhud"` → optional show/hide toggle for the HUD itself (only if we make it toggleable).

**`GuiDialogScribeQuickAdd : GuiDialogGeneric`** (new, client-side). One `AddStaticText` label +
one `AddTextInput("line")` + a Save button + title bar. On Save: read
`GetTextInput("line").GetText()`, and if non-blank send a new packet to the server. Focus the
input on open so the player can type immediately and press Enter.

**`ScribeQuickAddMessage`** (new packet, client → server, ProtoContract) — mirrors the existing
`ScribeToggleTaskMessage` style but targets a **held-item document by docId**, not a block position
(the v2 held-item store is docId-keyed, unlike v1's block-position keying):

```csharp
[ProtoContract]
public sealed class ScribeQuickAddMessage
{
    [ProtoMember(1)] public string DocId { get; set; }   // held-document id (v2 store key)
    [ProtoMember(2)] public string Text  { get; set; }   // the one line to append as a Task
}
```

Server handler: look up the docId's `ScribeDocument` in the held-document store, call
`doc.AddTask(text)` (blank rejected in Core), persist + resync via the same `MarkDirty`/packet
flow v2 establishes for held documents. Register the message type in `ScribeModSystem.Start` on
both sides, appended to the existing `RegisterChannel(...).RegisterMessageType<...>()` chain (in
the same order on both sides — see the existing note in `ScribeModSystem`).

**Backpack item + collection GUI** — deferred in shape to v2's held-item infrastructure. v5 adds
the item definition (asset JSON), its hotkey-open path, and reuses the notebook's collection GUI.
The backpack differs from the notebook mainly in being an *equippable* portability tier (opens the
whole collection), not a new document renderer.

---

## Implementation spec

Ordered so each step is independently verifiable.

1. **Core: enforce ≤3 pins.** Add `MaxPinnedTasks`, `PinnedCount`, `PinnedTasks`, and the cap
   check in `TogglePinned`. Add `CollectPins` (shape A). Cover with xUnit: reject 4th pin, unpin
   at cap allowed, ordering, cross-document collection. No codec change (pin bit already in v3).
   *Verify: `dotnet test` (no game install).*
2. **Lectern respects the cap.** The lectern's existing `OnRowTogglePin` already calls
   `TogglePinned`; once Core rejects the 4th pin it silently returns false. Add a client-side
   ingame message ("You can pin at most 3 tasks", via `capi.TriggerIngameError` +
   `Lang.Get("scribe:...")`) when the toggle is refused so the player isn't confused. *Verify:
   in-game, try to pin a 4th task.*
3. **HUD element.** Implement `HudScribePins` per the `HudElementCoordinates` template. Wire it
   into `StartClientSide` (construct, let it self-open on `OnOwnPlayerDataReceived`). Feed it from
   the client's known-documents registry via `CollectPins`. Register the 500 ms tick refresh.
   *Verify: pin a task at a lectern, confirm it appears on-screen and updates live when toggled
   done/undone; survives closing the lectern GUI; survives relog.*
4. **HUD ↔ pin sync.** Pins are server-authoritative already (they live in the document, synced by
   the Sign pattern). The HUD is a pure *read* of synced state — it needs no new sync path for the
   lectern case. For held documents, the HUD reads the client-cached held document (v2). Confirm a
   pin toggled by *another* player on a shared/lectern document propagates to this client's HUD via
   the normal `FromTreeAttributes`/`MarkDirty` resync (it should, since the HUD re-reads each tick).
5. **Quick-capture flow.** Register `"scribequickadd"` hotkey → open `GuiDialogScribeQuickAdd`
   (focus the input) → on Save send `ScribeQuickAddMessage(docId, text)` → server appends via
   `AddTask` and resyncs. Requires the held-item docId resolution from v2. *Verify: with a
   notebook/backpack held, press the hotkey, type a line, Enter; confirm it appears in the full
   document and (if pinned) never — quick-add creates an unpinned task by default.*
6. **Backpack item + hotkey-open.** Define the backpack item asset; register `"scribebackpack"`
   hotkey → resolve the equipped backpack's docId → open the (v2) collection GUI. *Verify: equip
   backpack, press hotkey, collection opens without right-clicking any block.*

**Persistence/sync:** all synced state (pins, quick-added tasks) rides existing document
persistence — `ScribeDocumentCodec` (unchanged) through the vanilla Sign pattern
(`ToTreeAttributes`/`FromTreeAttributes` + `MarkDirty`) for blocks, and v2's docId-keyed held-item
store for items. The HUD introduces **no new persisted state** (it's a live projection); the only
new wire format is `ScribeQuickAddMessage`.

---

## Dependencies & sequencing

- **Hard prerequisite: v2 (Notebook / held-item `docId` store).** The backpack, quick-capture,
  and the held-document side of the HUD all target a docId-keyed held document. v5 cannot land
  before v2 establishes that store and the held-item GUI-open path. (v1 lectern is block-position
  keyed; v5's new packet is docId-keyed precisely because of this.)
- **Soft prerequisite: row-list rework / shared renderer.** The backpack's collection GUI should
  reuse the shared row renderer the row-list-rework produces (same as the v4 desk), not fork the
  lectern dialog.
- **Independent of ConfigLib/ImGui.** The HUD deliberately avoids both. (ConfigLib could *later*
  expose HUD options — anchor corner, max rows, always-on vs toggle — as optional soft-dep
  settings, but that's not required for v5.)
- **Position in the plan:** v5 is the fifth tier (after v4 writing desk). The Core pin-cap work
  (step 1–2) is the one piece with no v2 dependency and could be pulled forward independently if
  desired, since the lectern already toggles pins today.

---

## Open questions (for the user)

1. **HUD always-on vs toggleable?** `HudElementCoordinates` is gated by a client setting +
   rebindable toggle (`coordinateshud`). Do we want the pinned-task HUD permanently visible once
   any task is pinned, hidden automatically when zero tasks are pinned, or manually toggleable via
   a hotkey (and defaulting to which)?
2. **Where do pins live when the source document is on an item not currently held/equipped?**
   This is the central design question (shape A vs B in *C# data structures*). Options: (A) a
   global cross-document pin projection so the HUD shows your top 3 from *any* of your documents
   (requires the client to know about unheld documents — a server-pushed "my pins" summary, since
   the client won't have those documents cached); or (B) the HUD reflects only the currently-held
   document's pins (simpler, no server summary, but weaker as an ambient feature). Which model?
3. **Is the ≤3 cap per-document or global?** Core naturally enforces ≤3 *per document*. If the HUD
   is global (option 2A), a player with several documents could have >3 pins total but the HUD only
   shows 3 — do we also want a global cap, and if so, where is it enforced (the server must, since
   no single document sees the others)?
4. **Should HUD pins be interactive (click to complete/unpin) or read-only?** Vanilla HUDs
   (`HudBosshealthBars`) are strictly non-interactive. A clickable pin (tick-off from the HUD)
   would be a nice capture-speed win but means the HUD grabs mouse events and needs a click→packet
   path. Read-only for v5, or worth the extra wiring?
5. **Confirm native-HUD over ImGui.** This spec commits to `HudElement` (vanilla API) because
   VSImGui is Release-excluded and the guardrail forbids a new hard dep — do you agree, or is there
   any appetite to reconsider ImGui despite it being unavailable in shipped/Apple-Silicon builds?
