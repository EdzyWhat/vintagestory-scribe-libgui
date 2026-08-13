## Context

The editor scrolls the focused row into view through a single seam: `NotifyTextChanged`
and the navigation handlers set `pendingEnsureVisible = true`, and `OnRenderGUI`
(`ScribeDialogBase.Lifecycle.cs:209-215`) consumes it by calling
`Scrollable.EnsureVisible(editorFocusNodes[idx].Owner)`. That `Owner` is the
`ScribeMultilineField` **element** (set at `ScribeMultilineField.cs:593`), whose render
object's height is the full wrapped-line count (`ScribeMultilineFieldRender.PerformLayout`
auto-grows to `visualLines.Count * lineHeight + PadY*2`).

`Scrollable.EnsureVisible` (`reference/vslibgui/.../Scrollable.cs:106-115`) computes the
target's content-space top/bottom and applies two guards:

```csharp
var newOffset = viewTop;
if (itemBottom > viewBottom) newOffset = itemBottom - ViewportHeight; // bottom-align
if (itemTop   < viewTop)     newOffset = itemTop;                     // top-align (wins)
```

When the row is taller than the viewport both guards are effectively always eligible, and
which one "wins" depends on the *current* offset that the previous `EnsureVisible` left
behind — so successive keystrokes ping-pong the offset between the row's top and bottom.
That is the bounce.

The caret's pixel position is already computed for painting inside the render object
(`ScribeMultilineField.cs:166-173`: `caretY = PadY + line * lineHeight`) but is a local
in `PaintInternal` — not stored, not exposed. The mod already has an internal seam for
reaching editable-text geometry, `IScribeEditableTextRender` (`ScribeCuneiformField.cs:37`),
implemented by both the plain and cuneiform field render objects, currently exposing
`OffsetAtPosition` and `CaretOffsetVertical` (both flat offsets, no pixels).

`sharedScrollController` (a `ScrollController`, `ScribeDialogBase.cs:81`) exposes public
`Offset`, `JumpTo(float)`, `AnimateTo(...)`, and `MaxScrollExtent` — enough to scroll to an
arbitrary content-Y without forking `gui`.

## Goals / Non-Goals

**Goals:**
- Editor scroll-into-view follows the caret rect, not the row element.
- A row taller than the viewport does not bounce the scroll on each keystroke.
- Typing/navigating with the caret already visible causes no scroll.
- Keyboard navigation (arrows, Tab/Shift+Tab, Enter advance/retreat) follows the caret.
- No `src/Core/` change, no new dependency, no `gui` fork.

**Non-Goals:**
- No change to horizontal scrolling (rows wrap, they don't scroll horizontally).
- No change to the six `pendingEnsureVisible = true` trigger sites' *when* — only what
  servicing the flag does.
- No new animation/easing requirement — matching the current (jump) feel is acceptable;
  optional smoothing is called out as a follow-up, not a requirement.
- Not touching the Read view, Pin Tab, or HUD scroll paths — this is the editor caret only.

## Decisions

### D1 — Expose the caret rect on `IScribeEditableTextRender`, don't reach into privates
Add one method to the internal `IScribeEditableTextRender` seam, e.g.
`bool TryGetCaretRect(out float localTop, out float height)`, returning the caret's top and
line height in the render object's **local** coordinates (the same `PadY + line*lineHeight`
/ `lineHeight` the painter uses), or `false` before layout has run. Both
`ScribeMultilineFieldRender` and `ScribeCuneiformFieldRender` implement it, so the plain and
cuneiform editors behave identically.

*Why over alternatives:* exposing a public caret-Y field on the render object would leak
paint-time state and skip the "valid only after layout" contract; a full caret `Rect` in
content space would force the render object to know about the viewport (it must not). A
local top+height keeps the render object viewport-agnostic and lets the dialog do the
content-space math it already knows how to do.

### D2 — Compute the caret's content-space Y in the dialog, mirroring `EnsureVisible`
Replace the `Scrollable.EnsureVisible(element)` call with a helper
`EnsureCaretVisible(focusedFieldElement)` that:
1. Resolves the focused field's text render object from `editorFocusNodes[idx].Owner`
   using the same proxy → `Children[0] as IScribeEditableTextRender` step the field's own
   `ResolveTextRender` uses (`ScribeMultilineField.cs:1049-1053`).
2. Gets `localTop`/`height` via D1; bails (no scroll) if unavailable.
3. Computes the field's content-space Y by summing render-object `Y` up the parent chain to
   the `RenderViewport` — the exact walk `Scrollable.EnsureVisible` performs
   (`Scrollable.cs:148-164`); factor it into a shared helper if practical, otherwise
   replicate it. `caretTop_content = fieldContentY + localTop`, `caretBottom_content =
   caretTop_content + height`.
4. Reads the viewport extent from the controller (`ScrollController.ViewportSize` /
   `MaxScrollExtent`; note the public property is `ViewportSize`, not `ViewportHeight`,
   which lives only on the internal `IScrollableContext`).
5. Applies minimal, single-outcome scroll (NOT the two-guard form that bounces):
   - if `caretBottom_content > offset + viewport` → `newOffset = caretBottom_content - viewport`
   - else if `caretTop_content < offset` → `newOffset = caretTop_content`
   - else → no change.
   Clamp to `[0, MaxScrollExtent]` and `JumpTo` only if it differs from the current offset
   by more than a small epsilon (matching `Scrollable`'s `< 0.5f` no-op guard).

*Why over alternatives:* keeping the math in the dialog reuses the existing
content-Y walk and the existing `pendingEnsureVisible` plumbing, so the six trigger sites
and the render loop are untouched. A single winning branch (bottom OR top OR neither) is
what removes the oscillation — the row's own height never enters the computation.

### D3 — Keep the flag-driven, once-per-frame servicing
`NotifyTextChanged` firing per keystroke is fine: it only *arms* the flag, and the caret
math is cheap and idempotent (a visible caret produces no scroll). Nav handlers
(`FocusEditorRow`, insert/advance/retreat) already arm the same flag, so they follow the
caret for free. A newly-added or newly-focused row's caret sits at its top, so the caret
path also brings a new row into view — preserving the current "new row scrolls into view"
feel without a special case.

## Risks / Trade-offs

- [Caret rect invalid before layout] → `TryGetCaretRect` returns `false` until
  `PerformLayout` has populated `lineHeight`/`visualLines`; the dialog treats `false` as
  "no scroll this frame." Because the flag is consumed in `OnRenderGUI` after layout, the
  first post-edit frame normally has a valid rect; a rare skipped frame self-corrects on
  the next armed frame.
- [Content-Y walk drift from `EnsureVisible`] → if the parent-chain sum diverges from how
  `Scrollable` computes it, the caret could be mis-placed. Mitigation: replicate the exact
  walk (or share it) and verify against a known row in-game.
- [Cuneiform field parity] → the cuneiform render object must implement `TryGetCaretRect`
  with the same semantics or the cuneiform editor regresses. Mitigation: implement both in
  the same task and test a cuneiform surface.
- [Jump vs. ease] → the fix keeps the current instantaneous `JumpTo`. If it reads as
  abrupt when the caret crosses the edge, swapping to `AnimateTo` is a localized follow-up
  (the controller already supports it); not required by the spec.

## Open Questions

- ~~Should the caret-follow leave a small margin below/above the caret?~~ **Resolved
  (playtest):** yes — an **8px** edge margin (`caretEdgeMargin` in `EnsureCaretVisible`),
  applied to both the trigger test and the aligned offset in each branch, so the input's
  border stays visible on the top/bottom rows instead of clipping flush. The final clamp
  to `[0, MaxScrollExtent]` absorbs the margin at the document ends.

## Playtest follow-ups (first in-game pass)

Two gaps surfaced on the first §5 run and were fixed on the same seam:

1. **Arrow keys didn't scroll** — `MoveCaret` (keyboard nav: arrows / Home / End /
   word-jump) never went through `OnChanged`, so it never armed `pendingEnsureVisible`.
   Fix: a new `OnCaretMoved` field callback fired from `MoveCaret`, threaded
   field → `ScribeEditRow` → `ScribeEditorContent` → `NotifyCaretMoved`, which arms the
   same flag. Not fired on click (click doesn't call `MoveCaret`).
2. **Click into a tall row bounced the scroll to the row's top/bottom** even though the
   caret landed at the (visible) click point, and flipped which edge on each re-click. Root
   cause (found by runtime stack trace, NOT the stale `reference/vslibgui` clone, which
   predates it): the shipped `gui@3.1.0` `FocusManager.RequestFocus` unconditionally calls
   `Scrollable.EnsureVisible(focusedElement)` on every focus change, and `EnsureVisible`'s
   two-guard form ping-pongs a taller-than-viewport row between its top and bottom depending
   on the current offset — the same oscillation D2 removed for our own path, but upstream of
   it and fired on click-focus. A click always lands on a visible pixel, so the view must NOT
   move at all. Fix: snapshot the settled scroll offset each frame
   (`lastStableScrollOffset`), and a new `OnPointerFocus` field callback (fired from the
   pointer-press path after the caret is placed, threaded to `NotifyPointerFocus`) both
   clears our `pendingEnsureVisible` AND `JumpTo`s straight back to that pre-click offset.
   The focus-scroll ran synchronously inside `focusNode.RequestFocus()` and our restore runs
   in the same input-phase call before the next render, so the bounce is never painted.
   Programmatic focus (Tab / Enter via `FocusEditorRow`) still scrolls because it doesn't
   route through the press path — keeping the two focus sources distinguishable is why the
   seam is the field, not the shared `OnRowFocusChanged` listener (which both sources share).
3. **Document top/bottom now jumps to the first/last row** (§7). Plain Up/Down moved one
   visual line, but there was no document-edge jump. Added on the platform-correct shortcuts:
   **Cmd+Up/Down** on macOS, **Ctrl+Up/Down** and **Ctrl+Home/End** on Windows. It fires
   `OnJumpToFirstRow`/`OnJumpToLastRow` (threaded field → `ScribeEditRow` →
   `ScribeEditorContent` → dialog like the advance/retreat callbacks); the dialog commits the
   current row, focuses the edge row, and — because a field's caret persists wherever it last
   sat and focus-gain never resets it — reaches the target field's State via its focus node's
   owning element (`(FocusNode.Owner as StatefulElement).State`, the same access the body
   `GlobalKey` uses) and calls a new `PlaceCaretAtEdge(atStart)` to snap the caret to the
   document edge. The placement fires the caret scroll-follow this change built, so the edge is
   brought into view for free — no new scroll path.

   **Modifier plumbing (the subtle part).** LibGUI's `KeyboardEvent` carries only Shift/Ctrl/Alt
   — `GuiBase` forwards VS's `CtrlPressed` but *drops* `CommandPressed` — so macOS Cmd can never
   reach the field handler. The field therefore gates the jump on **`e.Ctrl` alone** (deliberately
   NOT the `Ctrl || Alt` word-jump gate that Left/Right use, so Alt/Option+Up/Down stays a plain
   one-line move as macOS expects), and `ScribeDialogBase.OnKeyDown` — which *does* see the raw VS
   `KeyEvent` with `CommandPressed` before base handling strips it — remaps **Cmd+Up/Down →
   Ctrl+Up/Down**. That's the exact same seam that already remaps Cmd+Left/Right → Home/End and
   Cmd+A/C/X/V → Ctrl+…, so macOS Cmd row-nav needs no `gui` fork.
