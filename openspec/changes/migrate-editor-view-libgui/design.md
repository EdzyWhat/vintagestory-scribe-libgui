## Context

`adopt-libgui-foundation` (change 1, archived 2026-07-23) migrated the lectern **read** view to a LibGUI
dialog (`GuiDialogScribeLecternLibGui` : `GuiDialogBlockEntityBase`) and established the patterns this
change inherits: self-stateful `ValueKey`-keyed rows in a `ListView`, a code-defined parchment
`ColorScheme`/`ThemeData`, and `ForceRebuild()` to reflect an external re-sync. Per design D2 it left the
**editor** view on the native `GuiDialogScribeLectern`, with the LibGUI read view's "switch to editor"
control re-opening that native dialog. That seam has a backlogged defect: the native editor's "Done
Editing" returns to the *native* read view, not the LibGUI one.

The go/no-go gate for this change was cleared during the spike: `SpikeScribeMultilineField.cs` (retained
reference-only) proves a wrapping, auto-growing, focus-holding multi-line editable field can be built on
LibGUI's **public** render/widget bases (LibGUI's stock `TextField` is single-line and `internal`, so the
field reimplements rather than subclasses — mirroring LibGUI's own `TextField`/`RenderTextField`
architecture). The full desktop keyboard model (Enter=commit / Shift+Enter=break, Tab/Shift+Tab focus
movement, caret navigation, selection, clipboard, macOS Cmd/Alt caret translation) is already solved in
the native `ScribeRowTextInput.cs` and is portable.

`src/Core/` (document model, codec), the `scribe` network channel, its packets, and persistence are
untouched — this is a Mod-layer view swap. The single-editor lock and server-authoritative edit flow
(`ScribeEditDocumentMessage`) carry over unchanged.

## Goals / Non-Goals

**Goals:**
- Render the lectern **editor** view on LibGUI, inside the existing `GuiDialogScribeLecternLibGui` dialog,
  as an internal read↔editor view swap (no native dialog in the loop).
- Promote `SpikeScribeMultilineField.cs` to a production multi-line editable widget on LibGUI's public API:
  width-wrapping, height auto-growing, focus-holding, keep-focused-row-in-view.
- Port the full editor keyboard model and caret conventions (incl. macOS Cmd/Alt) from
  `ScribeRowTextInput.cs`.
- Commit edits through the existing lock-gated server path (`ScribeEditDocumentMessage`), unchanged.
- Apply the keypress-leak fix deferred from change 1 (capture inputs while a field is focused).
- Fix the backlogged switch-to-editor return path by unifying both views in one dialog.
- Retire the native editor: remove the editor path from `GuiDialogScribeLectern`, delete
  `SpikeScribeMultilineField.cs`, and remove native editor helpers that fall dead.

**Non-Goals:**
- Any `src/Core/`, network-packet, codec, or persistence change (semantics unchanged).
- Skeuomorphic visuals: custom checkbox glyph, text-size-proportional scaling, drag-reorder affordances,
  per-row icon controls — deferred to the later theme/affordance change.
- The lined-paper ruling — dropped for good (change 1 decision 2026-07-23), not revisited here.
- Theme-JSON hot-reload — still gated to the later theme-extraction change; this change stays on the
  code-defined theme from change 1.
- Multi-selection across rows, rich text, or any editing capability the native editor did not already have.

## Decisions

**D1 — One dialog owns both views; read↔editor is an internal view swap.** The existing
`GuiDialogScribeLecternLibGui` gains an editor mode selected by dialog state; `Build()` branches to a read
content tree or an editor content tree. This is what fixes the backlogged return path: there is no native
dialog to hand back to, so "done editing" is just a state flip back to the read tree.
*Alternative considered:* a second LibGUI dialog for the editor — rejected; two block-entity dialogs
re-introduces the same open/close/lifecycle coordination the seam suffered from, for no benefit.

**D2 — Editor rows are self-stateful + keyed, in a NON-virtualized scroll container.** Each editor row is a
`StatefulWidget` keyed by block index, containing a checkbox + the multi-line field, and the field owns its
live editing state (text, caret, selection, focus). The read view uses `ListView`, but the editor view must
NOT: LibGUI's `ListView` **virtualizes** — its `ListViewContentElement.UpdateVisibleItemsVariable` mounts
only rows in `[firstVisible-1, lastVisible+1]` and unmounts the rest, destroying their `Element`/`State`/
`FocusNode` (confirmed by reading `reference/vslibgui/.../Scroll/ListView.cs`). That breaks two editor
requirements: (a) cross-row keyboard nav (Enter→next row) can't `RequestFocus` an off-screen row whose
`FocusNode` doesn't exist yet, and (b) a focused row that grows past the viewport would unmount and lose
focus/caret mid-type. So the editor view renders as a `SingleChildScrollView` + `Column` of ALL rows (every
row stays mounted, `FocusNode`s persist), with `Scrollable.EnsureVisible(focusedRow.Element)` for keep-in-view.
A lectern document is a small checklist (dozens of rows at most), so non-virtualized has no practical cost.
*Alternative considered:* virtualized `ListView` like the read view — rejected; virtualization silently
destroys off-screen focus state, which the editor's focus-coordination and keep-in-view logic depend on.
*Note:* this revises the proposal's "editable `ListView`" wording; the spec delta is mechanism-agnostic on
the scroll container (it requires continuous scroll within a viewport, no page-turn), so both comply.

**D3 — Promote `SpikeScribeMultilineField.cs` to a production widget rather than adopt LibGUI's `TextField`.**
LibGUI's stock `TextField` is single-line and `internal`. The spike field already reimplements the needed
architecture on public bases (`RenderBox` for wrap/measure/paint, `RenderObjectWidget` bridge,
`StatefulWidget` + `IFocusable`/`IKeyCharHandler`/`IKeyDownHandler` for input). Production-harden it:
promote the prototype subset (insert/backspace/caret/Enter) to the full model, wire it to a
`TextEditingController`-style model or keep the plain `(text, caret, selection)` model if sufficient, and
rename out of the `Spike` namespace.
*Alternative considered:* wait for / patch LibGUI to expose `TextField` publicly with multi-line — rejected;
out of our control, and the reimplementation is already proven.

**D4 — Port the keyboard model from `ScribeRowTextInput.cs`, expressed against the field's own key
handlers.** Row commit/navigation: **Tab** = commit-and-advance (no tab glyph), **Shift+Tab** =
commit-and-retreat, **Enter** = commit-then-insert-a-new-task-below-and-focus-it, **Shift+Enter** = hard
line break (grows the row), **Esc** = commit-and-close (panic close, not revert). Caret: Left/Right/Home/End,
word-skip (Ctrl / Alt-Option), line-end (Ctrl / Cmd), Shift extends selection; clipboard cut/copy/paste.
macOS Cmd/Alt combinations map onto the same movement logic (the native code solved that the engine
otherwise ignores Alt and only honors Ctrl).
*Revision (2026-07-23, post-playtest):* the model originally shipped as Enter=commit-advance / Tab=no-op
(native parity). Per user request it was swapped to the above (Tab=advance, Enter=new-task-below) — a
todo-app-idiomatic model where Enter rapidly builds a list. This added `ScribeDocument.InsertTask(index,
text)` to Core (with unit tests). Shift+Enter (hard break) and Esc (close) are unchanged.
*Alternative considered:* a reduced keyboard model — rejected; parity/idiom coverage is a hard requirement
(the whole point is no regression on the swap).

**D5 — Commit through the existing lock-gated `ScribeEditDocumentMessage` path, unchanged.** Editing still
requires the single-editor lock (acquired on entering editor mode, released on leaving), and commits flow
GUI → `scribe` channel → server mutates store → syncs back → `FromTreeAttributes` → view refresh. No
Core/packet/persistence change.
*Alternative considered:* a new autosave packet or debounce protocol — rejected; the native editor's
commit-on-Enter/blur semantics are sufficient and keep the packet surface frozen.

**D6 — Apply the keypress-leak fix here (deferred from change 1).** While a field holds focus, capture all
key inputs (`CaptureAllInputs()`-equivalent on the LibGUI dialog / focus node) so typed keys do not fall
through to the game (e.g. movement, hotbar). Change 1 explicitly deferred this because the read view has no
typing; the editor view is where it matters.
*Alternative considered:* per-key swallowing — rejected (the native `default:`-swallow approach was the
leak; capture-while-focused is the correct fix).

**D7 — Retire native editor pieces as they fall dead, don't preemptively gut.** Remove the editor code path
from `GuiDialogScribeLectern` and delete `SpikeScribeMultilineField.cs` once the LibGUI editor lands. Native
helpers (`ScribeRowElement`, `ScribeRowTextInput`, `RowTextLayout`, `ScribeRowListScrollbar`,
`ScribeBlockRowCell`) are deleted **only if** nothing else references them after the swap — verified by
build, not assumed.
*Alternative considered:* keep the native dialog as a hidden fallback — rejected; the untouched original
repo `vintagestory-scribe` is already the fallback, and dead code on the fork is just rot.

## Risks / Trade-offs

- **Multi-line editable field on public API is the riskiest surface** → The spike already proved the
  architecture; production-hardening (selection, clipboard, macOS carets) is porting known-good logic from
  `ScribeRowTextInput.cs`, not inventing. Mitigate: port incrementally with an in-game test per capability
  (insert/caret/wrap-grow/selection/clipboard/commit).
- **Variable-height rows + a growing focused row + keep-in-view scrolling** interact (a row grows → list
  re-measures → scroll offset must track the caret) → This is the hardest layout interaction. Mitigate:
  lean on the `variableHeight` `ListView` (proven in change 1) for measurement and add explicit
  keep-focused-row-visible logic; cover with the "growing focused row stays in view" scenario.
- **Focus/keystroke capture vs. the block-entity dialog lifecycle** → walk-away auto-close (change 1's
  `InteractionRange` override) and Esc-to-close must still work while a field holds focus. Mitigate: ensure
  Esc commits-then-closes via the dialog, and re-test walk-away auto-close in editor mode.
- **LibGUI's global Harmony `VanillaDialogCleanup` patches** (flagged in change 1) → still a
  compatibility vector; unchanged by this work but re-verify no new interaction with focused input.
- **Deleting native helpers** → risk of removing something still referenced (e.g. by the read view or
  inspect overlay). Mitigate: delete only on a clean build with no references; D7 makes this build-verified.

## Migration Plan

1. Promote `SpikeScribeMultilineField.cs` → production multi-line field widget (public API), full keyboard
   model + caret conventions ported from `ScribeRowTextInput.cs`; capture-while-focused.
2. Add the editor content tree to `GuiDialogScribeLecternLibGui` (editable `ListView` rows: checkbox +
   field), keyed/self-stateful; wire read↔editor as an internal view swap driven by dialog state and the
   editor lock.
3. Route commits through the existing `ScribeEditDocumentMessage` lock-gated path; leaving editor mode
   releases the lock and returns to the LibGUI read view (fixing the backlogged return path).
4. Remove the native editor path from `GuiDialogScribeLectern`; delete `SpikeScribeMultilineField.cs`;
   delete native editor helpers that are now unreferenced (build-verified).
5. Build (`-c Release`) clean; `dotnet test tests/Core.Tests` green; `restage.sh`/`.ps1` stage without
   `Gui.dll`; in-game editor playtest (typing, wrap/grow, Enter/Shift+Enter/Shift+Tab/Esc, macOS carets,
   commit+sync, walk-away auto-close, read↔editor round-trip).
6. Append editor-port LibGUI lessons to `VSAPI-NOTES.md`; add editor items to `TESTING.md`.

**Rollback:** the untouched original repo `vintagestory-scribe` is the fallback. On this fork, reverting
this change restores the native editor path (change 1's D2 seam) — but note that once native pieces are
deleted in step 4, rollback means reverting the commit, not toggling a flag.

## Open Questions

- Does the promoted field keep the spike's plain `(text, caret, selection)` model, or move to a
  `TextEditingController`-style abstraction? (Decide during step 1; the plain model may suffice for a
  single-field-per-row editor.)
- Should the editor lock be acquired eagerly on opening the dialog in editor mode, or lazily on first row
  focus? (Native behavior is the reference; confirm during step 2.)
- Once the native editor is gone, is `GuiDialogScribeLectern.cs` fully deletable, or does anything
  (inspect overlay, tests) still reference it? (Resolved by build in step 4.)
