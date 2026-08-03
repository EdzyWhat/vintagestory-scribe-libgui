## 1. Confirm the toggle/close mechanism against the shipped DLLs

- [x] 1.1 Re-verify (via `ilspycmd` against `/Applications/Vintage Story.app/VintagestoryAPI.dll`) that `GuiDialog` exposes public `IsOpened()`, `TryClose()`, and the abstract `string ToggleKeyCombinationCode`, and that `IGuiAPI` (`capi.Gui`) exposes `List<GuiDialog> OpenedGuis` — the members the toggle relies on — confirmed against 1.22.3: `GuiDialog` exposes public `IsOpened()`, `TryOpen()`, `TryClose()`, `Toggle()`, abstract `ToggleKeyCombinationCode`; `IGuiAPI.OpenedGuis` is `List<GuiDialog>`.
- [x] 1.2 Re-verify (against `/Applications/Vintage Story.app/Mods/VSSurvivalMod.dll`) that `GuiDialogHandbook.ToggleKeyCombinationCode` returns `"handbook"` and that `ModSystemSurvivalHandbook` exposes no public open/close/toggle (its `dialog` is private) — confirming the `OpenedGuis` scan is the decoupled path, not the mod system — confirmed: `GuiDialogHandbook.ToggleKeyCombinationCode => "handbook"`; `ModSystemSurvivalHandbook.dialog` is a private field, only public members are the `OnInitCustomPages` event + `ShouldLoad`/`StartClientSide`/`Dispose`.
- [x] 1.3 Record any newly learned handbook/`OpenedGuis`/`ToggleKeyCombinationCode` fact in `VSAPI-NOTES.md` (survival-mod-systems section) so it is not re-derived — added a decoupled open/close bullet under "Calendar, player events, per-player storage, and survival-mod systems" (corrects the prior note's implication that `OpenDetailPageFor` lives on the mod system).

## 2. Extend the footer action into a toggle (Mod/adapter)

- [x] 2.1 In `src/Mod/ScribeDialogBase.Layout.cs`, extend `OpenEditorReferenceHandbook()` (optionally rename to `ToggleEditorReferenceHandbook()` and update the `onOpenEditorReference:` wiring ~line 524) to: (a) find the open handbook via `capi.Gui.OpenedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")`; (b) if found, call `TryClose()` on it; (c) otherwise fire the existing `"handbook"` link-protocol open path (`handbook://craftinginfo-scribe-editor-reference`) — per design D1/D2/D3 — done: renamed to `ToggleEditorReferenceHandbook()`, updated the `onOpenEditorReference:` wiring, close-if-open / open-if-closed body.
- [x] 2.2 Preserve the decoupling: use ONLY base-`GuiDialog` public members and `capi.Gui.OpenedGuis` — no reference to `GuiDialogHandbook` / `ModSystemSurvivalHandbook`, no reflection into privates — confirmed: matches on the public `ToggleKeyCombinationCode` string and closes via base `GuiDialog.TryClose()`; no survival-mod type reference, no reflection.
- [x] 2.3 Preserve graceful degradation (design D5): keep the `capi.LinkProtocols.TryGetValue("handbook", ...)` guard on the open path, and rely on the empty `OpenedGuis` match when the survival mod is absent so the close path is a safe no-op with no null-deref — done: nullable `openHandbook` with a `!= null` guard; open path still guarded by `LinkProtocols.TryGetValue`.
- [x] 2.4 Update the method's XML doc-comment to describe the toggle behavior and the `OpenedGuis`-scan-by-`ToggleKeyCombinationCode` detection, keeping the existing "graceful no-op when the survival mod isn't loaded" note — done.

## 3. Tooltip / lang (Mod/adapter)

- [x] 3.1 Update the value of `scribe-gui-editor-reference-tooltip` in `src/Mod/assets/scribe/lang/en.json` to convey the open/close toggle affordance (design D4 / D-Q2); leave the key name and `scribe:`-prefix convention unchanged — done: value now `"Show / hide Editor Features"` (D-Q2 pick), key/prefix unchanged.
- [x] 3.2 Confirm no structural change is needed in `src/Mod/ScribeEditorContent.cs` — the ⓘ `Button`/`Tooltip` keeps calling `Widget.OnOpenEditorReference()`; only the tooltip string resolves differently — confirmed: `onTap: _ => Widget.OnOpenEditorReference()` (line 466) and the tooltip's `Lang.Get("scribe:scribe-gui-editor-reference-tooltip")` (line 469) are untouched; only the delegate body + resolved string changed.

## 4. Build and manual in-game playtest

- [x] 4.1 Build the mod (0 errors) and confirm the `Core` suite still passes (no Core change expected — this is a Mod-only change; the run is a guard) — Mod build clean (0 errors; the 3 warnings are pre-existing, the transient nullable warning I introduced was fixed); Core suite 255/255 pass.
- [x] 4.2 In-game (all four footers share this button — spot-check at least the Lectern and the tablet): with the handbook CLOSED, click ⓘ → the Scribe Editor Features page opens (unchanged from today) — confirmed 2026-08-02 (playtest submission 2026-08-02T16-46-27: "works.")
- [x] 4.3 In-game: with the handbook OPEN on the Scribe Editor Features page, click ⓘ → the handbook closes — confirmed 2026-08-02 (playtest: "works.")
- [x] 4.4 In-game: with the handbook OPEN on a DIFFERENT page, click ⓘ → the handbook navigates to the Scribe Editor Features page (the "focus, don't hide" rule, D3); a further click then closes it — confirm the two-click flow feels right (D-Q1) — confirmed 2026-08-02 (playtest: two-click flow "Works.")
- [x] 4.5 In-game: confirm the ⓘ tooltip now reads the updated toggle wording, and that closing plays/omits the handbook sound acceptably (D-Q3) — confirmed 2026-08-02 (playtest: tooltip + sound "Works")
- [x] 4.6 Graceful-degradation sanity: confirm no crash/exception path exists when the survival mod / `"handbook"` protocol is absent (both branches no-op) — reason through the code if a no-survival test world isn't readily available — reasoned through: with no survival mod, `OpenedGuis` never holds a handbook dialog, so `FirstOrDefault` returns `null` (guarded by `!= null`) and control falls to the open branch; `LinkProtocols` has no `"handbook"` entry so `TryGetValue` is false → no-op. No null-deref, no exception on either path.
- [x] 4.7 Record verdicts via the `what-to-test` skill / `TESTING.md` — added an `add-info-button-handbook-toggle` group to `TESTING.md` with 4 in-game items (4.2 `9f96bb03`, 4.3 `f5ea01dd`, 4.4 `6103f6f8`, 4.5 `11c74a8d`), awaiting the playtest.

## 5. Validation

- [x] 5.1 Run `openspec validate add-info-button-handbook-toggle --strict` and reconcile any issues — "Change 'add-info-button-handbook-toggle' is valid" (no issues).
- [x] 5.2 Run `openspec list` and confirm the change registers — registers as `add-info-button-handbook-toggle`.
