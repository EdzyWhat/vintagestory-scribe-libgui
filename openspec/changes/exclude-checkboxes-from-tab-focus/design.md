## Context

Before LibGUI 3.1.0, LibGUI had no focus-traversal engine at all (the mod's own code comments say so
— `ScribeDialogBase.cs:93`, `ScribeMultilineField.cs:424`). The Scribe editor rolled its own Tab
handling: each editable field is a `ScribeMultilineField : IFocusable`, and the field itself
intercepts Tab/Shift+Tab (`ScribeMultilineField.cs:608-618`) to call the dialog's
`EditorAdvanceFrom`/`EditorRetreatFrom` (`ScribeDialogBase.Editor.cs:44-60`), which programmatically
focus the next/previous row's field node. Row checkboxes were plain, non-focusable widgets, so Tab
never touched them.

LibGUI 3.1.0 changed two things (confirmed by decompiling the vendored `src/Mod/lib/Gui.dll`; the
`reference/vslibgui/` clone is stale and predates 3.1.0, so it must NOT be trusted here):

1. `Checkbox`'s state, `CheckboxState`, now derives from `FocusableState<Checkbox>` and lazily creates
   its own `FocusNode`. Every mounted checkbox is now focusable, though the `Checkbox` **widget**
   constructor still exposes no focus/traversal parameter — the node lives on internal state the mod
   cannot reach.
2. `GuiBase.OnKeyDown` now intercepts Tab globally and runs `FocusManager.FocusNext/FocusPrevious`,
   which walks `FocusManager.TraversalPolicy.GetTraversalOrder(...)`. The default
   `ReadingOrderTraversalPolicy` collects every focus node in the tree whose
   `CanRequestFocus && !SkipTraversal` is true — including the new checkbox nodes.

Net regression: Tab/Shift+Tab in the Editor and Pinned views now stop on each row's checkbox before
its text field, doubling keystrokes and interrupting fast keyboard editing.

## Goals / Non-Goals

**Goals:**
- Tab / Shift+Tab in the Lectern Editor view and the Pinned view traverse only the editable text
  fields, in row order — never the row checkboxes.
- Preserve every existing keyboard behavior: Tab/Shift+Tab commit-and-advance/retreat, Enter/Shift+Enter,
  Esc-commits-and-closes, empty-row removal, text normalization.
- The checkbox stays fully usable by mouse click (completion toggling is unchanged).

**Non-Goals:**
- No change to `src/Core/` (no document/model/codec/network/persistence change).
- Not changing LibGUI itself or the vendored DLL — the mod cannot reach the checkbox's internal focus
  node, and vendoring a patched build is out of scope.
- Not addressing focus traversal in the Settings window, Read view (non-editable), or Guestbook beyond
  what already works — those aren't in the reported regression and don't Tab between fields.

## Decisions

**Decision: Install a mod-owned allow-list `FocusTraversalPolicy` on the dialog's `FocusManager`,
rather than trying to mark each checkbox non-traversable.**

`FocusNode` does expose settable `SkipTraversal` / `CanRequestFocus`, which would be the natural fix —
but the checkbox's node is created lazily on the internal `CheckboxState` and the public `Checkbox`
widget offers no constructor knob or accessor to reach it. So per-checkbox exclusion has no public
seam.

Instead, `FocusManager.TraversalPolicy` is a public settable property, and `FocusTraversalPolicy` is a
public abstract base with one method: `GetTraversalOrder(Element root, FocusManager)`. The dialog
already owns the complete, ordered set of field focus nodes it wants Tab to visit —
`editorFocusNodes` (a `List<FocusNode>`, in row order) and `pinFocusNodes` (a `Dictionary<Guid,
FocusNode>`). So we assign a small `FocusTraversalPolicy` subclass that returns exactly those nodes
(the ones currently mounted/live), in order. This is an **allow-list**: anything not a Scribe field
node — checkboxes included, now and for any future focusable control we don't opt in — is simply never
returned, so Tab can't land on it. That is strictly more robust than a checkbox-specific deny.

Alternatives considered:
- **Deny-list by owner type** (return the default policy's list minus nodes whose owner element's state
  is a `CheckboxState`): works, but couples the mod to a LibGUI internal type name and re-includes any
  other future focusable widget we didn't intend. Rejected in favor of the allow-list.
- **Reach the checkbox `FocusNode` and set `SkipTraversal`**: no public seam (see above). Rejected.
- **`FocusNode.TraversalOrder`**: only sorts, cannot exclude. Doesn't solve it.

**Decision: The policy sources its ordered node list from the dialog's existing `editorFocusNodes` /
`pinFocusNodes`, filtered to nodes that are currently mounted.** These stores are already kept in sync
with the live rows (created/disposed as rows appear/vanish). The policy returns the editor list when in
editor mode and the pin list (ordered to match the visible pin rows) when in the Pinned view, so
traversal order matches on-screen row order in both.

**Decision: Keep the field's own Tab handling as the commit mechanism; the policy only governs which
node Tab moves to.** In 3.1.0 `GuiBase.OnKeyDown` may now preempt the field's Tab handler, so the
commit-and-advance semantics are driven through the traversal path. The field's
`EditorAdvanceFrom`/`EditorRetreatFrom` and blur-commit remain the commit authority; task 2 verifies
commit-on-Tab still fires under the new traversal.

## Risks / Trade-offs

- **[The 3.1.0 `GuiBase.OnKeyDown` Tab interception may bypass the field's own Tab→commit call]** →
  Verify in-game that Tab still commits the current row before moving (the reported bug is only about
  *where* focus lands, and the playtester didn't report lost edits, but this must be confirmed since
  the traversal path changed). If commit no longer fires on Tab, wire the commit into the policy-driven
  focus change (blur-commit already runs on focus loss via `RequestFocus`, which blurs the old node
  first — so this likely already holds).
- **[Allow-list must track row add/remove/reorder]** → Source the list from the same live
  `editorFocusNodes`/`pinFocusNodes` the dialog already maintains, filtering to mounted nodes, so it
  stays correct as rows change without a second bookkeeping path.
- **[Policy is global to the dialog's `FocusManager`]** → That's the intended scope: this dialog only
  ever wants Tab to visit its own field nodes. The Settings window is a separate dialog with its own
  `FocusManager`, unaffected.
- **[Reference clone is stale]** → All API decisions here were verified against the decompiled vendored
  `src/Mod/lib/Gui.dll`, not `reference/vslibgui/`. Note added to `VSAPI-NOTES.md` in tasks.

## Migration Plan

Pure client-side behavior fix; no data migration, no save-format or network change. Ship in the mod
build; effect is immediate on next launch. Rollback = revert the policy assignment (traversal falls
back to the 3.1.0 default that includes checkboxes).

## Open Questions

None blocking. The one thing to confirm in-game (task 2 / playtest) is that Tab still commits the
current row's edit before advancing under the new traversal path.
