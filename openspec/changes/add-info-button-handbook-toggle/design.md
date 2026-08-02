## Context

The editor footer's Information (ⓘ) button today calls `OpenEditorReferenceHandbook()` in
`src/Mod/ScribeDialogBase.Layout.cs` (~line 560), threaded to `ScribeEditorContent` as
`onOpenEditorReference` (~line 524) and fired from the footer button in `src/Mod/ScribeEditorContent.cs`
(~lines 443-464). The current body is:

```csharp
private void OpenEditorReferenceHandbook()
{
    if (capi.LinkProtocols.TryGetValue("handbook", out var open))
        open(new LinkTextComponent("handbook://craftinginfo-scribe-editor-reference"));
}
```

This is **deliberately decoupled** from the survival mod: it fires the game's registered `"handbook"`
link protocol rather than reaching into `ModSystemSurvivalHandbook`'s private `GuiDialogHandbook`. The
doc-comment there records why — if the survival mod isn't loaded, `LinkProtocols` has no `"handbook"`
entry and the call is a graceful no-op instead of a crash. The whole risk of this change is adding a
"close if open" behavior **without** re-coupling to that private dialog.

### What the DLLs actually expose (verified via ilspycmd against the shipped 1.22.3 DLLs)

Decompiling `VSSurvivalMod.dll` and `VintagestoryAPI.dll` established the exact available surface:

- **`ModSystemSurvivalHandbook` (in `VSSurvivalMod.dll`)** holds its `GuiDialogHandbook dialog` as a
  **`private` field** and exposes **no** public open/close/toggle method. Its only public members are
  `OnInitCustomPages` (an event), `ShouldLoad`, `StartClientSide`, and `Dispose`. So the
  `capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>()` route — even if we took the coupling — has
  **no public API** to open or close the dialog. It is a dead end without reflection.
- **The survival mod registers `"handbook"` as a link protocol** in `StartClientSide`:
  `api.RegisterLinkProtocol("handbook", onHandBookLinkClicked)`. `onHandBookLinkClicked` opens the
  dialog if closed (`if (!dialog.IsOpened()) dialog.TryOpen();`) then calls
  `dialog.OpenDetailPageFor(pageCode)`. This is exactly the path our current open code drives.
- **The survival mod's own hotkey handler is already a toggle:** `OnSurvivalHandbookHotkey` does
  `if (dialog.IsOpened()) dialog.TryClose(); else { dialog.TryOpen(); ... }`. This confirms
  `IsOpened()`/`TryClose()` are the intended open/close primitives — we just can't reach *its* private
  `dialog` instance directly.
- **`GuiDialogHandbook` extends the API base `GuiDialog`** and overrides
  `public override string ToggleKeyCombinationCode => "handbook";`. `ToggleKeyCombinationCode` is a
  **public abstract property on the base `GuiDialog`** — a stable, public identity we can match on
  without referencing the concrete `GuiDialogHandbook` type.
- **`GuiDialog` (base, in `VintagestoryAPI.dll`) exposes public** `bool IsOpened()`, `bool TryClose()`,
  `bool TryOpen()`, `void Toggle()`, and the abstract `string ToggleKeyCombinationCode`.
- **`capi.Gui` (`IGuiAPI`) exposes `List<GuiDialog> OpenedGuis { get; }`** — the live list of currently
  open dialogs. This is the reflection-free way to *discover* the handbook instance: scan
  `OpenedGuis` for the one whose `ToggleKeyCombinationCode == "handbook"`.

So the clean, decoupled mechanism is: **discover** the open handbook via
`capi.Gui.OpenedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")` and **close** it via
`TryClose()` — using only base-`GuiDialog` public members and the client GUI API. No `VSSurvivalMod`
type reference, no reflection into privates.

## Goals / Non-Goals

**Goals:**

- Make the ⓘ button a toggle: open the Scribe Editor Features page when the handbook is closed; close
  the handbook when it is already showing that page.
- Detect and close the handbook using only public VS API, preserving the existing decoupling from the
  survival mod's private dialog.
- Keep the graceful no-op when the survival mod / `"handbook"` protocol is absent.
- Keep the change entirely in `src/Mod/` (Core stays VS-API-free by construction).

**Non-Goals:**

- Coupling to `GuiDialogHandbook` / `ModSystemSurvivalHandbook` (by type reference or reflection).
- Authoring or changing handbook content, or the target page code.
- A generic dialog-toggle framework, or toggling the tablet settings gear.

## Decisions

### D1 — Toggle by extending the existing footer action, not by adding a new wire

Keep `onOpenEditorReference` as the single footer→base wire and the ⓘ button's
`onTap: _ => Widget.OnOpenEditorReference()` unchanged. Only the **method body** in
`ScribeDialogBase.Layout.cs` grows from "open" to "toggle" (optionally renamed
`ToggleEditorReferenceHandbook()` for clarity, with the field wiring updated to match). This keeps the
footer widget structural code (`ScribeEditorContent.cs`) untouched except for the tooltip string, and
avoids introducing a second action or a stateful "is-open" flag the dialog would have to track.

*Alternative considered (rejected):* have the dialog track its own "handbook open by me" boolean.
Rejected — the handbook can be opened/closed by its own hotkey or close chrome independently of our
button, so a locally cached flag would drift out of sync. Querying `OpenedGuis` live at click time is
authoritative and stateless.

### D2 — Discover the handbook via `OpenedGuis` + `ToggleKeyCombinationCode`, never the mod system

Detect the open handbook with:

```
capi.Gui.OpenedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")
```

and close it with `TryClose()`. This uses only base-`GuiDialog` public members
(`ToggleKeyCombinationCode`, `IsOpened()`, `TryClose()`) and the public `IGuiAPI.OpenedGuis` list — no
reference to `GuiDialogHandbook` or `ModSystemSurvivalHandbook`, and no reflection.

*Alternative considered (rejected):* `capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>()` and
call a public open/close on it. Rejected on the DLL evidence — the mod system exposes **no** such
public API and holds its dialog `private`; this route buys the coupling we are trying to avoid and
still can't close the dialog without reflection.

*Alternative considered (rejected):* match the dialog by its concrete `GuiDialogHandbook` type via
`OpenedGuis.OfType<...>()`. Rejected — that reintroduces the hard type dependency on the survival mod.
Matching on the public `ToggleKeyCombinationCode == "handbook"` string is the decoupled equivalent and
mirrors how VS itself keys the dialog to its hotkey.

### D3 — "Focus, don't hide" when the handbook is open on a different page

The least-surprising behavior when the handbook is open **but on a different entry** (e.g. the player
opened it to look up copper) is to **navigate to the Scribe Editor Features page**, not to close the
handbook. Closing would yank away the handbook the player was reading; navigating honors the button's
"show me the editor reference" intent. Only when the handbook is already showing *our* page does the
next ⓘ click **close** it.

Concretely, the toggle logic is:

1. Look up the open handbook dialog in `OpenedGuis` (by `ToggleKeyCombinationCode == "handbook"`).
2. **If not open** → fire the existing `"handbook"` link-protocol open path (unchanged from today).
3. **If open** → determine whether it is currently showing the Scribe reference page. If it is → call
   `TryClose()`. If it is not → re-fire the link-protocol path, which navigates it to our page.

Determining "is it showing our page" without coupling to the dialog's private page state is itself a
decoupling question — see D-Q1. The safe, low-coupling default (chosen for this proposal) is: treat
**any open handbook** as "close it" only after we have navigated it to our page. In practice this
means the first click while the handbook is open on another page navigates to our page (via the
re-fired link protocol), and a subsequent click — now that the handbook is open — closes it. This
keeps us entirely on public API (open state is observable via `OpenedGuis`; the current page is not,
without coupling) and still yields a sensible two-click "bring to my page, then dismiss" flow. The
exact page-aware refinement is deferred to D-Q1 and confirmed in the in-game playtest.

*Rationale for documenting rather than over-engineering:* the page-code the handbook is currently on
is held in `GuiDialogHandbook`'s protected `browseHistory` / `pageNumberByPageCode` — not publicly
readable. Reading it would recouple us to survival-mod internals, contradicting the whole point. So the
spec commits to the observable behavior (open ⇒ toggles closed; closed ⇒ opens to our page) and the
"navigate to our page first when it is open elsewhere" is the accepted, decoupling-preserving
compromise.

### D4 — Update the ⓘ tooltip to convey toggle behavior

The current tooltip value for `scribe:scribe-gui-editor-reference-tooltip` is `"Editor Features"`.
Update it to wording that conveys open-and-close, e.g. `"Editor Features (toggle handbook)"` or
`"Show / hide Editor Features"`. Final wording is a small copy decision to settle during
implementation (D-Q2); the requirement is only that the tooltip no longer implies open-only. Lang key
and its `scribe:`-prefixed convention are unchanged; only the string value changes.

### D5 — Graceful degradation preserved end to end

Both branches degrade safely when the survival mod is absent:

- **Open branch:** unchanged — guarded by `capi.LinkProtocols.TryGetValue("handbook", out var open)`;
  no protocol ⇒ no-op.
- **Detect/close branch:** scanning `capi.Gui.OpenedGuis` for `ToggleKeyCombinationCode == "handbook"`
  simply finds nothing when the handbook mod isn't loaded (the dialog was never registered), so the
  code falls through to the (also no-op) open branch. No null-deref, no crash, no exception.

## Risks / Trade-offs

- **[Recoupling to the survival mod]** → Mitigated by matching on the public
  `GuiDialog.ToggleKeyCombinationCode` string and closing via base `GuiDialog.TryClose()`; no
  `VSSurvivalMod` type reference and no reflection. Verified against the shipped DLLs that these
  members are public on the API base.
- **[Reading the handbook's current page needs private state]** → Not attempted; D3 commits to the
  observable open/closed behavior and a "navigate-then-dismiss" flow instead, keeping us on public API.
  D-Q1 tracks a possible page-aware refinement if a public signal is later found.
- **[Behavior differs across all four dialogs' footers]** → It does not: the footer is shared through
  `ScribeEditorContent`, so the one toggle behavior applies uniformly. This is intended (the proposal
  calls it out) and reduces per-dialog divergence rather than adding it.
- **[Tooltip wording churn]** → Low; a single lang-value change with no key rename, so no references
  break.

## Migration Plan

- No data/persistence/codec/wire migration — this is a client-side GUI interaction change only.
- Rollback: revert `OpenEditorReferenceHandbook()` to its open-only body (and the lang-value change)
  to restore today's behavior; nothing else is touched.

## Open Questions

- **D-Q1:** Is there a public, decoupling-preserving signal for *which* handbook page is currently
  shown, so a single click can close-when-on-our-page vs. navigate-when-elsewhere in one step (rather
  than the two-click navigate-then-dismiss of D3)? The page state lives in `GuiDialogHandbook`'s
  protected members today; absent a public accessor, D3's observable behavior stands. Confirm the
  two-click flow feels right in the in-game playtest.
- **D-Q2:** Final tooltip copy for `scribe-gui-editor-reference-tooltip` — "Editor Features (toggle
  handbook)", "Show / hide Editor Features", or similar. Pick during implementation; keep it short
  enough for the small footer tooltip.
- **D-Q3:** Should closing play the handbook's own close sound for consistency, or stay silent?
  `TryClose()` triggers the dialog's normal `OnGuiClosed` path, so this likely needs no extra work —
  confirm in-game.
