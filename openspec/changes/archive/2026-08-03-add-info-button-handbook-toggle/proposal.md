## Why

The editor footer's Information (ⓘ) button opens the "Scribe Editor Features" handbook page via the
game's registered `"handbook"` link protocol (`OpenEditorReferenceHandbook()` in
`ScribeDialogBase.Layout.cs`). A 2026-08-02 playtester asked for the button to also *close* the
handbook: today, clicking ⓘ while the handbook is already open does nothing visible (the reference
page is already showing), so a player who opened the reference to check a shortcut has to reach for
the handbook's own close chrome (or its hotkey) to dismiss it. Making ⓘ a **toggle** — open if the
handbook is closed, close if it is open — matches the mental model of a single button that shows and
hides the same panel, and it keeps the player's hands on the one affordance the editor already
surfaces.

The design constraint is that the current implementation is **deliberately decoupled** from the
survival mod's private handbook dialog: it fires the `"handbook"` link protocol rather than reaching
into `ModSystemSurvivalHandbook`'s private `GuiDialogHandbook`, so that when the survival mod is not
loaded the call is a graceful no-op instead of a crash. Adding a "close if open" behavior must
**preserve that decoupling** — detect and close the handbook without taking a hard reference to the
survival mod's private dialog type or reflecting into its privates.

## What Changes

- **Make the ⓘ Information button a toggle.** When the handbook dialog is **not open**, clicking ⓘ
  opens the "Scribe Editor Features" reference page exactly as today. When the handbook dialog **is
  open**, clicking ⓘ closes it. This affects the editor footer of **every** dialog that uses
  `ScribeEditorContent` (Lectern, both Notebooks, and the always-edit tablet), since they share the
  one footer.
- **Detect and close the handbook through public API only, without coupling to the survival mod's
  private dialog.** The handbook dialog is discovered by scanning `capi.Gui.OpenedGuis` for the
  `GuiDialog` whose public `ToggleKeyCombinationCode == "handbook"` (the survival handbook's stable,
  public identity), then calling its public `TryClose()`. No reference to `GuiDialogHandbook` /
  `ModSystemSurvivalHandbook` and no reflection — the decoupling philosophy is preserved. (See
  `design.md` D2 for why the mod-system route is a dead end.)
- **When the handbook is open on a *different* page, navigate to our reference page rather than close
  it** (a "focus, don't hide" rule). Toggling to close is reserved for when the handbook is already
  showing — a plain re-fire of the open path re-selects/reloads the Scribe reference page, so a player
  who opened the handbook to a different entry gets taken to the Scribe reference instead of losing
  their handbook entirely. Close only fires on the *next* click, once our page is showing. See D3.
- **Preserve graceful degradation:** if the survival mod (and thus the `"handbook"` link protocol) is
  not loaded, both the open and the detect-open paths are safe no-ops — the button never crashes.
- **Update the ⓘ tooltip text** to reflect the toggle affordance, replacing the current static
  "Editor Features" label with wording that conveys open-and-close (lang key
  `scribe:scribe-gui-editor-reference-tooltip`). See D4.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities

- `scribe-dialog-base`: add a requirement that the editor footer's Information (ⓘ) button toggles the
  Scribe Editor Features handbook page — opening it when the handbook is closed, navigating to it when
  the handbook is open on another page, and closing the handbook when that page is already showing —
  detected and closed through public VS API only (`capi.Gui.OpenedGuis` +
  `GuiDialog.ToggleKeyCombinationCode == "handbook"` + `GuiDialog.TryClose()`), with a graceful no-op
  when the survival handbook is not loaded.

## Impact

- **Modified code (Mod/adapter):** `src/Mod/ScribeDialogBase.Layout.cs` — extend
  `OpenEditorReferenceHandbook()` (or replace it with a `ToggleEditorReferenceHandbook()` seam wired
  through `onOpenEditorReference`) to (a) find any open handbook dialog by scanning
  `capi.Gui.OpenedGuis` for `ToggleKeyCombinationCode == "handbook"`, (b) if found and already showing
  our reference page, call its `TryClose()`, else (c) fire the existing `"handbook"` link-protocol
  open/navigate path. The `onOpenEditorReference: OpenEditorReferenceHandbook` wiring (~line 524) is
  unchanged in shape; only the method body grows.
- **Modified code (Mod/adapter):** `src/Mod/ScribeEditorContent.cs` — no structural change required;
  the ⓘ `Button`/`Tooltip` (~lines 443-464) keeps calling `Widget.OnOpenEditorReference()`. Only the
  tooltip's lang key value changes.
- **Lang** (`src/Mod/assets/scribe/lang/en.json`): update the value of
  `scribe-gui-editor-reference-tooltip` to convey the toggle (open/close) behavior.
- **No `Core` changes.** This is entirely a Mod-layer input-routing/lookup change; the `src/Core/`
  no-VS-API invariant is untouched (Core has no knowledge of the handbook or GUI).
- **No new dependencies, no new network packets, no persistence/codec change.** Purely a client-side
  GUI interaction change.
- **Affects all editor footers** (Lectern, plain Notebook, Clockmaker's Notebook, tablet) because they
  share `ScribeEditorContent`'s footer — the toggle is a single shared behavior, not a per-dialog one.

### Non-Goals (explicitly out of scope)

- Changing what the handbook opens to (still the `craftinginfo-scribe-editor-reference` page) or
  authoring new handbook content.
- Any change to the survival handbook mod itself, or taking a hard/reflected dependency on
  `GuiDialogHandbook` / `ModSystemSurvivalHandbook` internals.
- Toggle behavior for the tablet's separate settings-gear button (that is a distinct affordance).
- A generic "toggle any dialog" framework — this change scopes strictly to the ⓘ ↔ handbook pair.
