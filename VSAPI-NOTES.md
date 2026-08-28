# VintagestoryAPI notes

Facts about `VintagestoryAPI`/vanilla-mod internals learned by decompiling, kept here so
later tiers don't re-derive them or waste a round misdiagnosing a known failure mode as
something else (e.g. staging). **Check this file before decompiling anything** — if the
symptom isn't listed, decompiling is still fair game, just add the finding here once you
have it (see the entry template at the bottom).

Cross-reference: `openspec/changes/add-lectern-block/tasks.md` group 8 has the full
incident writeups this file distills.

## GUI composer / element lifecycle

**Symptom: a crash or corrupted layout right after `SetValue`/`SetPlaceHolderText`-style
calls on a text element, especially right after adding it to a composer.**

`GuiComposer.Compose()` doesn't calculate real `Bounds` (e.g. `InnerWidth`) until it runs
`CalcWorldBounds()` on the whole tree. Calling `SetValue` on a text input/area *before*
that point runs the auto-height/line-wrap math against `InnerWidth == 0`, corrupting the
baked-in value and, transitively, the dialog's outer size. `GuiComposer.Compose()` also
swallows any exception `CalcWorldBounds()` throws (log-only, no rethrow), so the actual
crash surfaces somewhere unrelated downstream (for us: a Cairo `BlurPartial` "surface
width/height must be above 0" exception).

**Fix pattern:** split element-adding code from value-seeding code. Add all elements, call
the composer's own `.Compose()`, *then* seed values in a second pass. See
`ScribeBlockRowCell.Compose` vs. `ApplyValues` in `src/Mod/ScribeBlockRowCell.cs`.

---

**Symptom: after any recompose, typing focus/caret jumps to element 0 (or a slider's drag
resets after one step).**

The composer's default `.Compose()` call uses `focusFirstElement: true`. Any full rebuild
of `SingleComposer` (e.g. from an add/delete/toggle button) is a *brand-new* element tree —
old element references (and their in-progress interaction state, like a slider mid-drag or
a text area's caret position) are gone. There is no way to "keep" an old element across a
recompose; you must snapshot state before and restore it onto the new instance after.

**Fix pattern:** capture (focused element key, caret position) before recomposing, then
call `composer.FocusElement(tabIndex)` (not `OnFocusGained` directly — that leaves two
elements marked `HasFocus`) and restore the caret after. See
`GuiDialogScribeLectern.RecomposeEditorViewPreservingFocus`. For a slider specifically,
`GuiElementSlider.TriggerOnlyOnMouseUp` (the API's own fix) is `internal` and unusable
from mod code — defer the recompose yourself to the dialog's own `OnMouseUp` instead. See
`textSizePendingRecompose` in `GuiDialogScribeLectern.cs`.

---

**Symptom: a focused text input silently loses focus (caret vanishes, typing stops) when you
click it again, and only clicking a *different* element restores it.**

This bites when a non-focusable element is registered on the composer *before* an overlapping
focusable input and consumes the click. `GuiComposer.OnMouseDown` (decompiled) iterates
`interactiveElements` in insertion order; the first element whose `OnMouseDownOnElement` sets
`args.Handled = true` becomes "the handler," and the loop then calls `OnFocusLost()` on **every
other** focusable element that currently `HasFocus`. The default `GuiElement.OnMouseDownOnElement`
*unconditionally* sets `args.Handled = true`. So a plain overlapping element (added earlier) eats
the mouse-down and blurs the input behind it; focus is never re-granted because the composer only
grants focus to the element that *handled* the down (the non-focusable one, `Focusable == false`).
`OnMouseUp` has no focus logic at all, so a mouse-up handler can't fix it. (This is a distinct
failure mode from the recompose-focus one above — no recompose is involved.)

**Fix pattern:** the earlier/overlapping element must NOT consume the mouse-down where the input
should own it — override its `OnMouseDownOnElement` to `return` without calling base (leaving
`args.Handled` false) for the region the input covers. The down then reaches the input, whose
`GuiElementEditableTextBase.OnMouseDownOnElement` keeps focus AND places the caret (`SetCaretPos`)
for free. See `ScribeRowElement.OnMouseDownOnElement` (yields the text column of the focused editor
row to the floating `ScribeRowTextInput`).

---

**Symptom: `GetTextInput(key)` (or `GetTextArea`) throws `InvalidCastException` on some
rows but not others.**

`AddTextInput` registers a `GuiElementTextInput`; `AddTextArea` registers a
`GuiElementTextArea`. `Get*` helpers cast to the specific type and throw if you call the
wrong one. Any code that doesn't know a row's kind ahead of time (e.g. hit-testing during
drag-reorder) must not assume which accessor applies.

**Fix pattern:** use `composer.GetElement(key)?.Bounds` (base `GuiElement`, no kind-specific
cast) when you only need bounds/position, not text-editing behavior.

---

**Symptom: a row/element's rendered content overlaps the element below it once text gets
long.**

`GuiElementTextInput` (single-line) never wraps — long text scrolls horizontally instead.
`GuiElementTextArea` (multi-line) *does* wrap and grows past whatever fixed height you laid
it out at. A fixed row-height constant is fine until content wraps past it; nothing warns
you when that happens, it just visually overlaps the next row.

**Fix pattern:** measure first with the engine's own wrap-aware sizing —
`ICoreClientAPI.Gui.Text.GetMultilineTextHeight(font, text, width)` (the same mechanism
`GuiElementTextArea.TextChanged()` uses internally) — and lay out using the max of that and
your minimum height. See `ScribeBlockRowCell.MeasureWrappedHeight`. Only text areas need
this; text inputs never wrap so their fixed height is already correct.

**Symptom: a dialog's close (X) button only registers a click on a small sliver of the
visible icon, not the whole glyph.**

**Not a bug in our mod — confirmed against vanilla.** `GuiElementDialogTitleBar`'s
`closeIconRect` hit-test math is internally consistent (confirmed via decompile and a
live hover-position diagnostic: logged mouse coordinates matched the computed hit-rect
exactly). The visible X glyph (plus its drop-shadow/hover-glow padding) simply reads
larger to the eye than the tight ~17x17 logical-pixel rectangle that actually registers
clicks. Reproduced identically on a plain vanilla dialog (e.g. a chest) — same tight
hitbox, same visual-vs-clickable mismatch. Likely a general engine/Retina-display
interaction (untested at 100% GUIScale / non-Retina), not specific to `GuiDialogBlockEntity`
or any of our composer setup.

**Fix pattern:** none needed — don't spend a round re-investigating this if it resurfaces
on a different dialog. If it's ever worth truly fixing (e.g. accessibility), the lead
would be `GuiElementDialogTitleBar.unscaledCloseIconSize` / its hit-rect math, but that's
vanilla engine code we can't patch from mod code — not actionable from here.

**Recurred under LibGUI (2026-07-26, scribe-notebook-frame).** The LibGUI Lectern's custom
title-bar close button (a `ScribeRowButton` inside `Align(BottomCenter)`/`Row`/`SizedBox`, wrapped
in a `Tooltip`) showed the SAME symptom, reported as the clickable area sitting slightly above/left
of the drawn glyph on a Retina Mac. Traced every layer in `reference/vslibgui/`: hit-testing applies
each child offset via `Element.HitTest → RenderObject.GlobalToChild`, which mirrors paint's
`PaintChildren` translate, and the `Tooltip` wrapper's `CompositedTransformTarget → RenderTarget :
RenderProxyBox` is a clean 0,0 passthrough — so there is NO layout-math offset in the LibGUI tree
either. Same conclusion as the native case: a Retina/`GUIScale` rendering-vs-hitbox artifact, not our
bug. Same settling diagnostic (hover-coordinate log at 100% GUIScale on a non-Retina display); same
"don't guess-patch it" verdict.

**Diagnostic technique that worked, for next time:** a click-based test is ambiguous once
a successful click closes the dialog mid-test (you lose the ability to compare "before"
state). Prefer a throttled hover-position log (`OnMouseMove`, logged via
`ICoreClientAPI.ShowChatMessage`) plus one screenshot with the cursor visibly on the
target and the chat log visible in the same frame — gives an unambiguous side-by-side
without repeated clicking.

**Symptom: a `GuiDialogBlockEntity` doesn't auto-close when the player walks away — but only
in Creative mode (works in Survival).**

The base `GuiDialogBlockEntity.OnFinalizeFrame` closes the dialog when `IsInRangeOfBlock`
returns false — this is the "walk-away auto-close" you get "for free" by subclassing it. But
`GuiDialogGeneric.IsInRangeOfBlock` measures the eye-to-nearest-selection-box distance against
**`capi.World.Player.WorldData.PickingRange + 0.5`**, and the engine **inflates PickingRange to
~100 blocks in Creative** (confirmed via decompile: the game-mode switch does
`PickingRange = PreviousPickingRange` (default `100f`) when leaving Survival/Guest;
`GlobalConstants.DefaultPickingRange` is `4.5`). So a creative player — e.g. anyone testing a
block they just placed from the creative inventory — can walk ~100 blocks before the dialog
closes, which reads as "never closes." Also note `IsInRangeOfBlock` starts `nearest = 99` and
only lowers it if the block returns selection boxes; a block with no selection box would *always*
read as out of range instead.

**Fix pattern:** override `IsInRangeOfBlock(BlockPos)` on your dialog to reuse the base's exact
selection-box distance math but gate on a fixed distance (`GlobalConstants.DefaultPickingRange`)
instead of the mode-dependent `WorldData.PickingRange`, so walk-away close fires consistently in
every game mode. See `GuiDialogScribeLectern.IsInRangeOfBlock`. (This is also the seam where
walk-away edit-flush + lock-release happen, via the dialog's `OnGuiClosed`.)

**Symptom: a row list needs to scroll instead of running off the bottom of a fixed-height
dialog, or growing the dialog itself without bound.**

`GuiComposer` has a built-in clip+scroll idiom, confirmed against the real
`anegostudios/vsapi`/`vssurvivalmod` source (`GuiDialogTrader.cs`,
`GuiDialogBlockEntityInventory.cs`), not just decompiled: `BeginClip(clipBounds)` pushes a
`GuiElementClip` that calls `api.Render.PushScissor(Bounds)` and sets
`composer.InsideClipBounds = clipBounds`; every element added afterward
(`AddInteractiveElement`/`AddStaticElement`) inherits that as its own `InsideClipBounds`.
`EndClip()` pops the scissor and clears it. A `AddVerticalScrollbar(onNewValue, bounds,
key)` + `.GetScrollbar(key).SetHeights(visibleHeight, totalHeight)` (called *after* the
composer's own `.Compose()`, once the real content height is known) drives a callback that
sets the *content* bounds' `fixedY = 0 - value; fixedY.CalcWorldBounds()` — shifting every
child's `absY` in one call, since `CalcWorldBounds()` recurses into `ChildBounds`.

Mouse hit-testing is scroll-aware **for free**: `GuiElement.IsPositionInside` ANDs
`Bounds.PointInside` with `InsideClipBounds.PointInside`, and any hit-test that reads a live
`Bounds.absY` (rather than recomputing layout math independently) picks up the scroll shift
automatically, since `absY` is recalculated by the same `CalcWorldBounds()` call the scroll
callback triggers. No manual scroll-offset arithmetic needed in hit-test code.

**Correction (this entry originally overclaimed): `BeginClip`/`PushScissor` alone does
NOT visually clip a mixed static+interactive row list's rendering — it only sets up the
plumbing `IsPositionInside` reads for hit-testing.** Confirmed live during
`skeuomorphic-lectern-gui` playtesting (a document with enough rows to overflow visibly
bled its dividers/text through the controls below the clip region —
`screenshots/debug/2026-07-18_20-43-11_hover-hide-behavior.png`), then confirmed the
mechanism against real vsapi source: `GuiComposer.Render()` draws every *static* element
(e.g. `AddInset` dividers, `AddStaticText` rows) in one single always-unclipped texture
blit, generated at the very top of `Render()` before any `GuiElementClip`'s
`RenderInteractiveElements` (which is where the scissor push actually happens) ever runs.
Separately, `GuiElementTextInput.RenderInteractiveElements` (a task row's own text box)
issues its own `api.Render.GlScissor(...)` scoped to its own bounds, then unconditionally
calls `GlScissorFlag(false)` afterward — which cancels scissoring outright rather than
restoring whatever outer scissor `BeginClip` had pushed. Vanilla's own reference usages
(`GuiDialogTrader`'s item-slot-grid scrollbar, `GuiDialogBlockEntityInventory`'s) get away
with this because a slot grid is a single well-behaved interactive element with no static
children and no scissor-canceling side effects — they never hit either failure mode.

**Fix pattern:** don't trust `BeginClip`/`PushScissor` to hide overflow for a row list that
mixes static elements (dividers, read-view text) with `GuiElementTextInput`/
`GuiElementTextArea` rows. Viewport-cull instead: measure every row's position/height in a
first pass, then only actually add/compose (`AddStaticText`/`ScribeBlockRowCell.Compose`)
the rows whose measured range overlaps the current scrolled viewport (plus a small buffer,
so minor scroll movement doesn't force a recompose on every tick) in a second pass. Still
use `BeginClip`/`AddVerticalScrollbar` for the scrollbar control itself and for hit-testing
scroll-awareness (both of those parts of this entry's original finding hold) — just don't
rely on the clip to hide rows outside the buffered window; visibility comes from never
composing them, not from the engine hiding them after the fact. See
`GuiDialogScribeLectern.ComposeReadView`/`ComposeEditorView`'s two-pass measure/cull
structure, `RowListCullBuffer`, and `OnRowListScroll`.

---

**Symptom: with the row-list culling fix above already in place, a row's tail still
renders past the dialog's bottom edge once scrolled to a specific position (not
necessarily at the very top of the scroll range).**

The cull test above must require *full containment* of a row within the visible window,
not mere *overlap*. An overlap test (`rowBottom < windowTop || rowTop > windowBottom` →
skip) still composes a row that only partially intersects the window — and since nothing
here visually clips a composed row's rendering (see the entry above), that row renders at
its full, unclipped height, with the portion outside the window bleeding straight past the
dialog's drawn frame. Confirmed live via the playtest-checklist app: scrolling to a
position where a row straddled `windowBottom` made its tail (up to a full row's height,
here ~30px, coincidentally close to but unrelated to the title bar's height) render below
the dialog.

**Fix pattern:** require full containment, not overlap: `rowTop < windowTop || rowBottom >
windowBottom` → skip. A row now only composes once entirely inside the visible window,
popping in/out cleanly at the scroll boundary instead of rendering a partial tail.
Tradeoff: a single row taller than the visible window itself can never be fully contained
at any scroll position and will never render — inherent to cull-don't-clip; would need real
clipping (confirmed unavailable, see the entry above) to fix. See
`GuiDialogScribeLectern.cs`'s pass-2 comments in `ComposeReadView`/`ComposeEditorView`.

---

**Symptom: scrolling a hand-stacked row list (parent `fixedY = 0 - scrollValue` +
`CalcWorldBounds()`) moves some parts of a row but not others. An all-static list (read
view) doesn't visually move at all on scroll — rows just cull in/out in place. A mixed
static+interactive list (editor view) scrolls the interactive parts but leaves the static
parts frozen: text-input content moves, but its border stays; the checkbox's check +
highlight move, but the box outline stays; a static drag glyph stays. The frozen widgets
are still fully clickable/typable where they landed after scroll.**

VS renders GUI elements in TWO passes with TWO different Y coordinates, and a parent
`fixedY` shift only reaches ONE of them. Confirmed via `ElementBounds` decompile:
- **Static pass** — `GuiElement.ComposeElements(Context ctxStatic, ...)`, baked ONCE into
  a cached texture at compose time — draws at **`bgDrawY`/`drawY`**:
  `bgDrawY = absFixedY + absMarginY + absOffsetY + ParentBounds.drawY`. No scroll term; the
  texture is not re-baked on scroll.
- **Interactive pass** — `RenderInteractiveElements(float dt)`, redrawn EVERY frame —
  draws at **`renderY`**: `renderY = absFixedY + ... + ParentBounds.renderY +
  renderOffsetY`. This DOES pick up the shifted parent.

So shifting the content parent's `fixedY` moves `renderY` (live pass) but not the
already-baked static texture (`drawY`). Which elements sit in which pass:
`AddStaticText`/`AddInset` dividers are wholly static (→ read view rows don't move at all).
`GuiElementTextInput`/`GuiElementTextArea` draw their *text content* in the interactive
pass but their *border/background* in `ComposeElements`; `GuiElementSwitch` draws its box
outline in `ComposeElements` (`RoundRectangle`/`EmbossRoundRectangleElement`) but the
check + hover highlight in `RenderInteractiveElements` (→ editor view: text/check move,
box/border don't).

This is the same underlying static/interactive split as the "BeginClip doesn't visually
clip" entry above — that one is the *clip* half, this is the *scroll-shift* half.

**Fix pattern:** don't rely on shifting the parent `fixedY` to scroll a hand-stacked
static+interactive list. Position each row at a **viewport-relative Y** at compose time
(`rowY - scrollValue`) so BOTH passes bake at the already-scrolled coordinate. Combine
with viewport culling (rows outside the window aren't composed at all) exactly as the
entries above require. See `GuiDialogScribeLectern.ComposeReadView`/`ComposeEditorView`.

---

**Symptom: a `GuiElementTextInput` (or `TextArea`) composed inside a `BeginClip` region
renders its own text fine, but everything drawn AFTER it that frame — sibling rows,
rulings, elements below the clip — bleeds out unclipped, past the dialog frame and over
controls outside the box.**

The engine's clip stack and the text input's own clipping use two DIFFERENT, non-composing
mechanisms, confirmed by decompiling `VintagestoryLib.dll` (`RenderAPIGame.PushScissor`/
`PopScissor`) and `ClientPlatformWindows`:
- **The clip STACK (correct, what `BeginClip` uses).** `IRenderAPI.PushScissor(ElementBounds,
  stacking=false)` computes the GL scissor rect from the bounds and pushes onto
  `ScissorStack`; `PopScissor()` pops and **restores the previous stack entry's scissor**
  (re-issuing `GlScissor` + `GlScissorFlag(true)` for whatever is now on top, or disabling if
  the stack is empty). `GuiElementClip` (from `BeginClip`/`EndClip`) drives this. The
  `IRenderAPI` doc comment says exactly this: *"Any previously applied scissor will be restored
  after calling PopScissor()."*
- **The raw flags (what `GuiElementTextInput` uses).** Its `RenderInteractiveElements` calls
  `api.Render.GlScissor(...)` (its own tight text rect) → `GlScissorFlag(true)` → draw text →
  **`GlScissorFlag(false)`**. In `ClientPlatformWindows`, `GlScissorFlag(false)` is a *global*
  `GL.Disable(GL_SCISSOR_TEST)` — it does NOT consult or restore `ScissorStack`. So the instant
  the input finishes, scissor testing is OFF for the rest of the frame, and the outer
  `BeginClip` scissor is silently defeated for every element rendered afterward.

This is why floating a real `GuiElementTextInput` into a natively-clipped row list (the S2
edit-in-place editor) reintroduced overflow bleed even though the clip itself works: the input's
`GlScissorFlag(false)` clobbers the dialog's clip. (Vanilla dodges this because its clipped
inputs are the last/only interactive element in the region, so nothing renders after the
clobber.)

**Fix pattern:** after the base input renders (which leaves scissor disabled), re-assert the
enclosing clip. Override the input's `RenderInteractiveElements` to call `base(...)` then
`api.Render.PushScissor(InsideClipBounds); api.Render.PopScissor();` — the `PopScissor`
immediately restores the clip that was on the stack top before the input ran, re-enabling
`GL_SCISSOR_TEST` with the dialog's clip rect so later elements clip again. (Push-then-pop
because the stack still holds the `BeginClip` entry; pop re-issues it.) `InsideClipBounds` is
set on every element added inside `BeginClip`. Belt-and-suspenders: also skip composing the
input entirely when its row is outside the visible window (an off-screen focused input would
otherwise draw unclipped down the screen). See `ScribeRowTextInput.RenderInteractiveElements`
and `GuiDialogScribeLectern`'s editor compose.

---

**Symptom: dragging a `GuiElementScrollbar` (or `AddSlider`) thumb moves it one step/pixel
then the drag dies; mouse-wheel and track-clicks work fine.**

A sustained drag gesture is being interrupted by a mid-gesture recompose. If the value-
change callback (`OnRowListScroll` / a slider's `onChanged`) rebuilds `SingleComposer`,
the freshly composed scrollbar/slider is a BRAND-NEW element that never received the
mouse-down, so the drag is orphaned after one step. One-shot inputs (wheel, track-click)
survive because they don't rely on a held gesture spanning frames.

**Fix pattern (two options — this codebase uses the second):**

*Option A — defer the recompose to mouse-up.* Set a "pending recompose" flag and rebuild in
`OnMouseUp` instead of inside the change callback (this dialog does exactly this for its
text-size slider: `textSizePendingRecompose`, drained in `OnMouseUp`). Simple, but the content
can't move until release. Fine for a slider whose value applies on release anyway; **not** fine
for a scrollbar, where the whole point is the content tracking the thumb continuously.

*Option B — recompose every frame but hand the drag off to the new element (used for the row
list).* When rows are composed at a viewport-relative Y (see the entry above), the ONLY way to
move them on scroll is a recompose — so deferring it (Option A) leaves the rows frozen until
release, which playtesting rejected (2026-07-20: "the thumb moves smoothly but the text stays
still until I let go"). Instead, recompose on the normal next-frame path so rows track the
thumb, and carry the drag across the rebuild: the freshly composed `GuiElementScrollbar` is a
new element that never saw the mouse-down, so copy the OLD element's public
`mouseDownOnScrollbarHandle` (true) and `mouseDownStartY` (the grab offset) onto it right after
Compose. The physical mouse button is still down, so the engine keeps dispatching `OnMouseMove`
to the composer's elements; the new scrollbar, now believing it's mid-drag, keeps responding
and the gesture survives seamlessly. Clear the captured handoff in `OnMouseUp` so a recompose
still queued from the drag's final frame can't re-grab a scrollbar after the button is up. See
`GuiDialogScribeLectern.OnRowListScroll`/`SetupRowListScrollbar`/`OnMouseUp` and
`ScribeRowListScrollbar`.

**Mouse-wheel step is hardcoded in the engine (`scaled(102)` content px/notch), overridable.**
`GuiElementScrollbar.OnMouseWheel` scrolls a fixed `scaled(102)` pixels per notch regardless of
row height — for this list that's ~2 task rows, which playtesting found too coarse to land on a
specific row. Subclass `GuiElementScrollbar` and override `OnMouseWheel` to scroll a caller-set
number of content pixels per notch (`ScribeRowListScrollbar.RowStep`, set to one task-row
height each compose). Work in content units via the public `CurrentYPosition` getter/setter
(`= currentHandlePosition * ScrollConversionFactor`) rather than the base's handle-space math,
and keep the base's sign convention (`- delta`, wheel-up scrolls toward the top) and its
"content fits, ignore wheel" guard. Add it with `AddInteractiveElement(new Subclass(...), key)`
since `AddVerticalScrollbar` hardcodes the base type; `GetScrollbar(key)` still returns it (cast
to the subclass to reach `RowStep`).

---

**Gotcha (engine inconsistency, not yet hit but worth flagging): `GuiElementTextArea`'s own
wrap-height write skips a GUIScale division that `GuiElementDynamicText`/
`GuiElementTextBase` both apply for the same operation.** `GuiElementTextArea.TextChanged()`
assigns the wrap-height straight to `Bounds.fixedHeight` (no `/ RuntimeEnv.GUIScale`), but
`GuiElementDynamicText.AutoHeight()` / `GuiElementTextBase.GetMultilineTextHeight()` both
divide by `RuntimeEnv.GUIScale` for the equivalent calculation.
`ScribeBlockRowCell.MeasureWrappedHeight` correctly mirrors the `TextArea` convention (no
division) since our text-section rows use `GuiElementTextArea` — but a future "fix" to make
it consistent with the other convention would silently double effective row height at any
non-1.0 GUIScale. If a similar height-measurement helper is ever added for a
`GuiElementDynamicText`-backed element, don't copy `MeasureWrappedHeight`'s no-division
convention without checking which base class is actually involved.

**Symptom: a toggle/icon-button's `On` state, seeded to reflect persisted model state,
silently reverts right after any mouse-up elsewhere in the dialog -- not just clicks on
the button itself.**

`GuiElementToggleButton.OnMouseUp` (the base of `AddIconButton`'s icon-button widget)
unconditionally runs `if (!Toggleable) On = false;` -- and this override fires on *every*
`OnMouseUp` dispatched to the dialog, not gated by whether the click landed on this
specific button. `Toggleable` defaults to `false` in the constructor if not explicitly
passed `true`. So any icon button meant to visually persist an on/off model state (not
just a momentary fire-once action like a delete button) needs `toggleable: true` at
construction, or its seeded `On` value gets wiped on the very next unrelated click.

**Fix pattern:** pass `toggleable: true` for any icon button whose `On` represents real
persisted state; leave it `false` only for momentary actions with no state to preserve.
See `ScribeHoverIconButton`'s constructor doc comment in `ScribeBlockRowCell.cs`.

**Symptom: you want to restyle a `GuiElementToggleButton`'s chrome (drop the brown pill,
enlarge the icon) and can't find a color/inset seam.**

There isn't one. `GuiElementToggleButton.ComposeElements` bakes its whole look in two
PRIVATE methods, `ComposeReleasedButton`/`ComposePressedButton` (confirmed via `ilspycmd`
against `VintagestoryAPI.dll`): brown fill = `GuiStyle.DialogDefaultBgColor` +
`EmbossRoundRectangleElement`, and the icon is drawn *small* by a hardcoded inset —
`DrawIcon(ctx, icon, absPad + scaled(4), absPad + scaled(4), InnerWidth - scaled(9),
InnerHeight - scaled(9), Font.Color)`. That fixed `scaled(4)`/`scaled(9)` inset is exactly
why the glyph looks tiny, and none of it is overridable in place.

**Fix pattern:** the only public virtual seam is `ComposeElements` itself, so override it
*without calling base* and bake your own texture(s). Keep the base for its `On`/`Toggleable`
hit-test + mouse plumbing. Because `On` is typically seeded AFTER compose (the pin's
`block.Pinned` is applied post-`.Compose()`), bake TWO textures (off/on) like the base does
and pick between them at render time in `RenderInteractiveElements` — a single baked texture
can't reflect a state set later. `ScribeHoverIconButton` (`ScribeBlockRowCell.cs`) is the
worked example: opaque rounded-rect fill (occludes overlaid text) + thin outline + a
near-full-bounds `DrawIcon` (pass `size ≈ InnerWidth` to get a large glyph, since the SVG
rasterizes to exactly the w/h you pass — see the icon-rendering section below).

**Symptom: per-row buttons composed in a `for (int i = ...)` loop all fire their click handler
with the SAME (wrong) index -- every pin/delete button acts on the last row, or on none (an
out-of-range `blocks.Count`), and the action silently no-ops.**

Not a VS API quirk -- a C# closure-capture trap that bites hard here because the dialog composes
one interactive element per row in an index loop. A `for (int i = 0; i < n; i++)` declares ONE
shared `i`; a lambda `_ => Handler(i)` closes over that *variable*, not its per-iteration value, so
after the loop finishes every captured lambda sees `i == n`. (This is the one thing `for` does that
`foreach` doesn't -- `foreach` captures a fresh loop variable per iteration since C# 5.) In
`GuiDialogScribeLectern` the pin/delete handlers `_ => OnEditViewTogglePin(i)` did exactly this →
`TogglePinned(blocks.Count)` → `IsValidIndex` fail → no-op, which read as "the buttons do nothing."

**Fix pattern:** snapshot the index into a per-iteration local inside the loop body
(`int rowIndex = i;`) and capture THAT in the lambdas. Alternatively route the click through the
element's own stored-field index the way the row checkbox does (`ScribeRowElement.blockIndex` +
a method-group handler) -- that path is immune because there's no closure over the loop variable.
Seeding loops that use `i` *immediately* (e.g. `GetToggleButton(PinKey(i)).On = ...`) are fine --
the trap is only deferred execution (a stored lambda) closing over the shared variable.

## Localization (`Lang`)

**Symptom: every player-facing string renders as its own raw lang key (e.g.
`scribe-gui-title` shown literally), even right after confirming `en.json` is present,
correctly formatted, and freshly staged.**

**This is not a staging bug — don't spend a round re-checking staging first.** Every lang
entry loaded from a mod's `assets/<modid>/lang/en.json` is registered keyed by its owning
domain: `TranslationService.LoadEntry` stores it as `"<modid>:<key>"`, not bare `"<key>"`.
`Lang.Get(key)` resolves via `KeyWithDomain(key)`, which defaults to the `"game"` domain
when `key` contains no `:` — it does **not** infer "the calling mod's own domain" from
context. So `Lang.Get("scribe-gui-title")` actually looks up `"game:scribe-gui-title"`,
which never exists, and `Lang.Get` silently falls back to printing the raw key (its
documented behavior on a missing key — no exception, no log line pointing at the mistake).

Independently corroborated: a real third-party mod (`xlib:levelup`) prefixes every one of
its own `Lang.Get` calls the same way, confirming this isn't a quirk of how our lang file
was authored.

**Fix pattern:** every `Lang.Get` call site (including string literals passed over the
network, like a `RefusalReason`, since the *receiving* client is the one that resolves it)
must use `"<modid>:<key>"`, e.g. `Lang.Get("scribe:scribe-gui-title")`. Don't forget
`WorldInteraction.ActionLangCode` — same resolution path.

**Diagnostic shortcut for next time:** if strings render as raw keys, grep the call sites
for a `"<modid>:"` prefix before touching staging/build output at all.

## VSImGui debug overlay

**Question: what key toggles the VSImGui debug overlay in-game (for the Debug-only
`RegisterDebugSliders` layout tuning)?**

**Ctrl + P.** Confirmed by decompiling the vendored `src/Mod/lib/VSImGui.dll` (v1.2.7):
`RegisterHotKey("imguitoggle", ..., (GlKeys)98, (HotkeyType)2, false, true, false)`. The
signature is `RegisterHotKey(code, name, key, type, altPressed, ctrlPressed, shiftPressed)`,
so it's `ctrlPressed: true` + `GlKeys 98`, and `GlKeys 98 == P` in
`VintagestoryAPI.dll`'s enum. Same file also registers `imguiincfont` = **Ctrl+F9** and
`imguidecfont` = **Ctrl+F8** for overlay font size (useful if the slider labels render too
small to read).

These are the code-registered defaults; a rebind would live in
`VintagestoryData/clientsettings.json`'s `keyMapping` (empty by default = defaults in
effect). If Ctrl+P doesn't open the overlay, check `keyMapping` for an override or a
conflicting bind before assuming the mod failed to load.

**Symptom: VSImGui loads and Ctrl+P registers, but pressing it shows NOTHING on screen
(no overlay, no error dialog) -- specifically on Apple Silicon.**

The overlay cannot render on macOS Apple Silicon. macOS caps OpenGL at **4.1** (Apple
deprecated OpenGL; it's emulated over Metal), but ImGui.NET's GL renderer -- which VSImGui
wraps -- issues calls the 4.1/Metal path rejects. Confirmed from `client-main.log`: a
startup `GLFW Exception: VersionUnavailable Requested OpenGL version 4.3, got version 4.1`
(`Graphics Card Renderer: Apple M4`), then **thousands of per-frame**
`[Error] after final compo - OpenGL threw an error: InvalidOperation` (8000+ in one
session), where "after final compo" == `EnumRenderStage.AfterFinalComposition` (value 10),
the exact stage VSImGui's `OffWindowRenderer` registers into (`RegisterRenderer(..., (EnumRenderStage)10, ...)`
in the decompiled `VSImGui.dll`). So Ctrl+P *does* toggle overlay state and the `Draw`
event *does* fire -- the draw call just errors out every frame, drawing nothing.

This is a platform incompatibility, NOT a mod bug, hotkey problem, or staging error --
don't chase it as one. The mod's `#if DEBUG` `RegisterDebugSliders` tuning path is
therefore unavailable on this Mac; run it on a machine with OpenGL >= 4.3 (a Windows box
with a normal GPU) via a **Debug**-configuration stage (`build/restage.ps1 -Configuration
Debug`, or `build/restage.sh Debug`). Note a plain restage builds Release, which excludes
VSImGui entirely (Mod.csproj `Configuration == 'Debug'` Condition) -- so even on capable
hardware the sliders only exist in a Debug stage. ConfigLib's own settings panel is pure
VS GUI (no ImGui) and works on any platform as an alternative live-ish editing path.

**Question: how do you draw a debug/inspector overlay (outlines, tinted bands, labels) over a
dialog on this Mac, given VSImGui is dead here?**

Use the engine's own primitives — all macOS-safe (plain GUI-shader draws, no OpenGL 4.3):

- **Outline a box:** `capi.Render.RenderRectangle(float x, float y, float z, float w, float h, int color)`
  strokes a rectangle outline (a `whiteRectangleRef` `LineStrip` mesh; confirmed in
  `VintagestoryLib.dll` `RenderAPIGame`). It decodes `color` via `ColorUtil.ToRGBAVec4f`, so pack it
  with `ColorUtil.ColorFromRgba(r,g,b,a)` (0–255 ints). It only STROKES — there is no fill variant.
- **Fill a band** (RenderRectangle can't): bake a 1×1 opaque-white texture once
  (`capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref tex)`) and blit it stretched with a tint via
  `capi.Render.Render2DTexture(tex.TextureId, x, y, w, h, z, new Vec4f(r,g,b,a))`.
- **Labels:** `capi.Gui.TextTexture.GenTextTexture(text, CairoFont, TextBackground)` → cache the
  `LoadedTexture` by string → `capi.Render.Render2DLoadedTexture(tex, x, y, z)`. **Dispose** every
  `LoadedTexture` (and the white pixel) on `OnGuiClosed` — they're GL textures and leak otherwise
  (`LoadedTexture` warns about a missing Dispose in its finalizer; guard with `TextureId != 0`, not a
  `Loaded` property — there isn't one).

Draw the overlay from the END of `OnRenderGUI` (after `base.OnRenderGUI`) at `z ≈ 600` (above the
dialog's ~500). Do it as a **screen-space draw pass, not composed child elements** — a child gets torn
down on every recompose AND clipped by any `BeginClip` scissor, so it couldn't label the viewport or
chrome. Read `SingleComposer.GetElement(key)?.Bounds` (base getter — see the `InvalidCastException`
note above) and structural bounds LIVE each frame so the overlay self-heals after a recompose.
`ScribeInspectOverlay` + `GuiDialogScribeLectern.BuildInspectBoxes` are the worked example
(add-gui-inspect-overlay). This is the native substitute for the dead VSImGui path on this Mac.

**Symptom: adding a setting to `configlib-patches.json` makes ConfigLib's ENTIRE "Mod Settings"
window fail to open — and it stays broken across a full game relaunch until the setting's on-disk
value is reset.**

Observed when `InspectOverlayMode` was added as the scribe manifest's FIRST `"type": "integer"`
setting (the other four are `"float"`), flat-array form, with a `"range": {min,max}`
(add-gui-inspect-overlay, playtest 2026-07-22T17-45-13). ConfigLib *parsed* it without a logged error
(`[Config lib] Configs loaded: 1`; only a cosmetic `Lang key not found: scribe:<code>` for the missing
`ingui` label), so the fault is NOT parsing — it's ConfigLib's **ImGui-based** `ConfigWindow` throwing
while it builds/draws the integer control (`DrawIntegerMinMaxSetting` → `ImGui.SliderInt`/`DragInt`),
which aborts drawing the whole window. It persists across relaunch because the window rebuilds from the
same stored setting every time. Note the config window is the SAME ImGui tech that can't render on Apple
Silicon (OpenGL 4.1 vs 4.3 — see the VSImGui section) — a float setting happening to work is not proof
an int one will.

**Fix pattern:** don't rely on the ConfigLib panel for a setting you can't verify renders. For a
client-only knob, toggle it by editing the mod's config JSON directly and reopening the dialog if the
dialog re-reads config on open (the scribe lectern does, at `GuiDialogScribeLectern` ctor). That path
needs no manifest entry and doesn't touch ConfigLib's ImGui window at all. Confirmed `"float"` +
`"range"` entries in the flat-array form DO work in the panel; the `"integer"` combination is what broke
it — treat a new ConfigLib setting type as unproven until seen rendering in-game. (ConfigLib supports
both a nested-by-type `settings.integer.CODE` object form and the flat `[{code,type,...}]` array form —
`Config.FromJsonDefinition` branches on `settings.IsArray()`; ours is the array form.)

## Text-input caret / selection conventions

**Symptom: before building a custom in-place editor, need to know whether the built-in
`GuiElementTextInput`/`GuiElementTextArea` already handle desktop caret conventions
(word-skip, jump-to-line-end, shift-extend-select, copy/paste) or must be subclassed.**

Almost all of it is already in `GuiElementEditableTextBase` (the shared base of both
`GuiElementTextInput` — single-line — and `GuiElementTextArea` — `multilineMode=true`).
Confirmed by decompile of `OnKeyDownInternal` / `MoveCursor` / `OnControlAction`
(`KeyCode` ints are `GlKeys`: Left=47, Right=48, Up=45, Down=46, Home=58, End=59,
Enter=49, Tab=52, BackSpace=53, Delete=55). What ships for free:

- **Word-skip:** `MoveCursor(dir, wholeWord: args.CtrlPressed)` — Ctrl+Left/Right jumps by
  word (whitespace-then-word-run scan via `IsWordChar`). Ctrl+BackSpace/Delete deletes a
  word (`OnDeleteWord`).
- **Line ends:** Home/End go to start/end of the *current wrapped line*; **Ctrl+Home/End**
  go to start/end of the *whole text*.
- **Shift-extend-select:** any Shift+arrow/Home/End sets/extends `selectedTextStart`; typing
  or a bare arrow collapses it. Double-click selects the word (`SelectWordAtCursor`).
- **Clipboard / select-all:** `OnControlAction` handles Ctrl **a/c/x/v** — and it fires on
  `args.CtrlPressed || args.CommandPressed`, so on **macOS Cmd+A/C/X/V already work**.

**The two real gaps (this is what a subclass/wrapper must add), both matter for us:**

1. **The base treats Ctrl and Cmd differently.** `OnControlAction` (copy/paste/select-all)
   accepts *either* Ctrl or Cmd — but **caret navigation** (`MoveCursor`'s word-skip,
   Ctrl+Home/End) is gated on `args.CtrlPressed` *only*, never `CommandPressed`. So on a
   Mac, Cmd+Arrow does **not** word-skip or jump to line ends. Worse: **`AltPressed` is a
   hard early-out** — `OnKeyDownInternal` begins `if (args.AltPressed) { args.Handled = true;
   return; }`, so Option/Alt+Arrow (the Mac word-skip idiom) is swallowed and does nothing.
   The user explicitly wanted Cmd+Right→line-end and Alt/Option→word-skip (their S2 answer
   5.B), so on macOS **neither works out of the box** — the base is Windows-keyed. Modifiers
   themselves are populated correctly per-OS (Lib maps Cmd→`CommandPressed`, Option→
   `AltPressed`); the base class just doesn't route the Mac ones to navigation.
2. **No row-to-row nav.** In single-line mode Tab is left unhandled (`handled = KeyCode !=
   52`) and Enter defers to the caller (`handled = false`); in multiline mode Enter inserts
   a newline (`OnKeyEnter`). There is no built-in Shift+Tab / Enter-moves-to-next-row — that
   is inherently our concern (it's cross-element), to be wired at the dialog level via the
   `OnKeyDown`/focus handoff, not inside the element.

**Implication for S2:** we do NOT need to reimplement selection/caret/clipboard — subclass
`GuiElementTextInput` (or `TextArea`) and override `OnKeyDown` to (a) re-route Mac Cmd/
Option arrow combos to the existing `MoveCursor(..., wholeWord)` / Home-End logic before
`base.OnKeyDown` swallows Alt, and (b) intercept Tab/Shift+Tab/Enter for row navigation and
hand focus to the sibling row. Everything else is inherited. `OnCaretPositionChanged` is a
public hook if the floating field needs to report caret pos back to the dialog.

**Symptom: subclassed `GuiElementTextArea` crashes with `NullReferenceException` in
`Render2DTexturePremultipliedAlpha` the moment a row gains focus, if your
`ComposeTextElements` override skipped the base to drop the border.** (Hit for real —
crash 2026-07-21T23-25, when a reopened editor auto-focused a row on world load.)
`GuiElementTextArea.RenderInteractiveElements` does, unconditionally when focused,
`Render2DTexturePremultipliedAlpha(highlightTexture.TextureId, highlightBounds)` — and
`highlightBounds` is **private to `GuiElementTextArea`** and set **only** inside its private
`GenerateHighlight()`, which runs only from its `ComposeTextElements`. So an override that
doesn't call the base (to avoid baking the emboss+dark-fill border) leaves `highlightBounds`
null → NRE. NOTE this differs from `GuiElementTextInput`, where the equivalent fields are
`protected` (a subclass could set them itself) — on `TextArea` they're private, and the text
`textTexture` is `internal`, so you can reproduce **neither** the highlight nor the text
build yourself. **Fix: call `base.ComposeTextElements` but pass a THROWAWAY
`ImageSurface`/`Context`.** Decompile shows the emboss + dark fill draw onto the *passed*
`ctx` (→ discarded), while `GenerateHighlight()` and `RecomposeText()` ignore that ctx and
build their own textures + set `highlightBounds` off `Bounds` regardless. Net: borderless
look, no crash, text + faint focus highlight both work. See `ScribeRowTextInput`.

**Symptom: a multi-line editable row doesn't grow when you add a TRAILING newline (Shift+Enter
at line end), but interior newlines work fine.** Two distinct facts, both confirmed by in-game
measurement 2026-07-22 (took three wrong fixes before instrumenting — measure, don't reason):
- **`TextDrawUtil.GetMultilineTextHeight` (→ `GetQuantityTextLines` → `Lineize`) does NOT count a
  trailing newline's empty line.** `"a"` and `"a\n"` measure the *same* height; `"a\nb"` measures
  2. `GuiElementTextArea.Autoheight` uses this same measure, so the element won't self-size for a
  dangling `\n` either. To count empty/trailing lines, measure **per `\n`-segment** and sum
  `max(1, GetQuantityTextLines(segment))` (`"a\n"` → `["a",""]` → 1+1 = 2). See
  `ScribeRowElement.MeasureWrappedTextHeightScaled`.
- **The real trap, though, was upstream:** if your model setter trims text (Scribe's
  `ScribeDocument.SetBlockText` did `text.Trim()` on every live keystroke for task rows), a
  just-typed trailing `\n` is **deleted before any height code sees it** — so no height fix
  downstream can ever work, and the symptom is byte-identical across every "fix." The tell was
  "interior newline works, trailing doesn't": `Trim()` only removes the *trailing* one. Fix:
  don't trim on the live edit path (Scribe added a `trimTask:false` overload); trim only at
  commit. **Lesson: when a text edit doesn't take effect, `grep` the data path (setter/normalizer)
  before touching the renderer.**

## Block placement orientation — facing the player

**Question: how do you make a placed block face the placing player?** (For the lectern
face-the-player fix; decompiled `VintagestoryAPI.dll` + `VSSurvivalMod.dll` 2026-07-26.)

- **The base `Block.TryPlaceBlock`/`DoPlaceBlock` does NOT orient anything** — it just
  `SetBlock`s the exact `BlockId`. **A `horizontalorientation` variant group alone does nothing
  at placement.** (This corrects `docs/specs/lectern-gui-polish.md` item 1, which wrongly claims
  the engine auto-picks the facing variant with no code — it does not.) You need EITHER a behavior
  or a code override.
- **`Block.SuggestedHVOrientation(byPlayer, blockSel)`** returns `BlockFacing[]` pointing from the
  block toward the player (`Atan2` internally) — the shared building block both idioms use.
- **Idiom A — JSON only (`BlockBehaviorHorizontalOrientable`, in `VSSurvivalMod.dll`).** Add the
  `HorizontalOrientable` behavior + a `side` variant group (`loadFromProperties:
  "abstract/horizontalorientation"` → north/east/south/west) + `shapeByType` per-facing `rotateY`
  + `selectionbox.rotateYByType` + `notcreativeinventory` (else 4 dup creative entries). Canonical
  free-standing template: `assets/survival/blocktypes/wood/churn.json`. Cost: block code gains a
  `-north/…` suffix (variant explosion; ripples into recipes / any literal-code references), and
  only 4 cardinal facings.
- **Idiom B — BlockEntity `MeshAngleRad` in code (the Sign idiom).** `BlockSign.TryPlaceBlock`
  computes `Math.Atan2` from player pos, snaps to `π/4` (45°; chests use 22.5°), sets
  `BlockEntitySign.MeshAngleRad`. The BE persists it as `"meshAngle"` in `To/FromTreeAttributes`
  (defaulting to `Block.Shape.rotateY` when absent), rotates its collision box in the setter, and
  `OnTesselation` tesselates with `new Vec3f(0, MeshAngleRad*180/π, 0)` cached by angle
  (`ObjectCacheUtil.GetOrCreate("…"+MeshAngleRad, …)`). Keeps the block code stable (no variant
  suffix) and gives smooth snapped facing — the lower-friction fit for a block that already has a
  BE mirroring Sign.
- **Gotchas:** (1) setting `MeshAngleRad` does nothing visually without an `OnTesselation` rotate;
  (2) a non-cubic selection/collision box must be rotated too, and becomes diagonal at 45°/22.5° —
  90°-only snapping keeps the box clean; (3) `IRotatable.OnTransformed` on the BE is optional polish
  for `/we` / schematic-rotation parity.
- **A BE `OnTesselation` custom-mesh path IGNORES the block shape's `rotateYByType` — so Idiom A
  and Idiom B do NOT compose** (add-chalkboard-block, 2026-08-18). The Sign idiom's `OnTesselation`
  loads the shape by `capi.TesselatorManager.GetCachedShape(Block.Shape.Base)` — **`.Base` only** —
  then applies `MeshAngleRad` as the sole rotation. So if you wall-mount a BE-rendered block with
  `HorizontalAttachable` + `shapebytype.rotateYByType` (Idiom A), the placed mesh rotates by
  `MeshAngleRad` (0 for a wall block that never runs the player-facing code) and the JSON
  `rotateYByType` is dead — every board faces one fixed cardinal. Fix: drive `MeshAngleRad` from the
  `side` variant yourself. `HorizontalAttachable.TryAttachTo` places `CodeWithParts(blockSel.Face
  .Opposite.Code)`, i.e. the variant is named for the ATTACH direction (into the wall); the front
  faces the OPPOSITE (outward). With our +Z-front shape and `front(r)=(sin r, cos r)`: north→0,
  east→3π/2, south→π, west→π/2 (the mirror of vanilla painting's `rotateYByType` north:180/east:90/
  south:0/west:270, because vanilla painting art faces −Z). Set it in `Initialize` (runs on both
  fresh-place and load, and — per the API doc-comment — always AFTER `FromTreeAttributes` on load),
  NOT `TryPlaceBlock`, since `ToTreeAttributes` persists `meshAngle=0` and the FromTree fallback to
  `Shape.rotateY` would otherwise be clobbered. Also: a `Cuboidf` `selectionbox` has NO
  `rotateYByType` (it's a shape-only field). But you don't need `selectionboxbytype`: the Sign idiom's
  BE already rotates its hitbox by `MeshAngleRad` via `GetSelectionBoxes`/`GetCollisionBoxes`
  (`box.RotatedCopy(0, deg, 0, (0.5,0.5,0.5))`). Author ONE thin slab for the angle-0 facing (painting
  uses `x2:1,y2:1,z2:0.0625`, board-at-−Z-wall) and let the BE rotate it — set NO `rotateYByType` on the
  box (that would double-rotate). For a walk-through painting keep `collisionbox:null` and rotate only the
  SELECTION box, so track them separately (a shared "rotate CollisionBoxes[0]" won't build a slab when
  collision is null).
- **A BE resets `MeshAngleRad` from the tree on EVERY interaction, not just load** (add-chalkboard-block,
  2026-08-19). The Sign idiom's `FromTreeAttributes` does `MeshAngleRad = tree.GetFloat("meshAngle", 0)`,
  and every block-entity packet round-trip (any `MarkDirty` from an edit/open/sync) calls
  `FromTreeAttributes` on the client. So a wall block whose angle is set only in `Initialize` renders
  correctly when placed, then **snaps back to angle 0 the first time the player interacts with it** (the
  persisted `meshAngle` is 0 — `Initialize` set the angle client-side only, so the server serialized 0).
  Fix: honor the variant-derived angle FIRST in `FromTreeAttributes` (`MeshAngleRad = WallMountAngleRad ??
  <persisted/shape fallback>`), not only in `Initialize`. Then every path — place, load, and each
  interaction sync — lands the same angle.
- **VS shape `.json` rotation is SCALAR `rotationX`/`rotationY`/`rotationZ` (degrees) — NOT a Blockbench
  `rotation:[x,y,z]` array** (add-chalkboard-block, 2026-08-19; confirmed by decompiling
  `Vintagestory.API.Common.ShapeElement` + `Vintagestory.Client.NoObf.ShapeTesselator.TesselateShapeElements`).
  Newtonsoft SILENTLY DROPS the unknown `rotation` property, so a hand-added `"rotation":[-45,0,0]` sits in
  the file and never rotates anything (the trap that burned a whole session here). Per element the tesselator
  does, on a pushed matrix: `Translate(rotationOrigin/16)` → `Rotate X` → `Rotate Y` → `Rotate Z` → `Scale` →
  `Translate((from−rotationOrigin)/16)` → tesselate own faces → **recurse into `children` inside the same
  matrix** → pop. So (a) rotation works on BOTH leaf cubes and group/parent elements, and children fully
  inherit every ancestor transform (referential rotation is real — no "must be a group" rule); (b) a child's
  `rotationOrigin` is in the PARENT's local 0–16 space, not global; (c) if `rotationOrigin` is null the pivot
  is the parent-local CORNER (0,0,0), which usually looks like "no rotation" or swings geometry out of view —
  always set the pivot to the element's own centroid for an in-place spin. Block-level
  `CompositeShape.rotateX/Y/Z` is separate (rotates the whole baked mesh about its centre) and COMPOSES with
  element rotation.
- **In Blockbench, apply rotation to the GROUP (outliner bone), NOT the cube ELEMENT — else the VS exporter
  drops it** (add-chalkboard-block, 2026-08-19; CONFIRMED in-tool by the author). This is the durable macOS
  workflow answer (no VS Model Creator). A cube's own `rotation` array set in Blockbench survives in the
  `.bbmodel` but produces **no** `rotationX/Y/Z` in the exported `.json` (chalk's `[-45,0,0]` vanished on
  export); moving that rotation onto the enclosing group exports correctly. **Why:** VS's model is a tree of
  transform nodes (`ShapeElement` = pivot + `rotationX/Y/Z` + `scale` + `children`), and Blockbench's
  **bone/group** is the 1:1 analogue of that node (free 3-axis rotation about a settable pivot), so the VS
  codec maps group→element and writes the rotation. A Blockbench **cube** is leaf geometry (`from`/`to`/faces);
  its rotation is a constrained, MC-derived per-cube concept the VS codec does not translate. Rule of thumb:
  cube = geometry, group = transform — put every rotation on a group. Two fast checks: (1) you can **re-open
  the exported `.json` in Blockbench** to preview EXACTLY what the game renders (Blockbench reads
  `rotationX/Y/Z` correctly on import — the author's tightest iteration loop); (2) grep the `.json` for
  `rotationX`/`Y`/`Z` on the element you expect to move.
- **A Blockbench element rotation set in the `.bbmodel` is DEAD until you re-export the VS shape `.json`**
  (add-chalkboard-block, 2026-08-19). The game loads the exported `shapes/.../*.json`, not the `.bbmodel`;
  a rotation added in Blockbench and saved only to the `.bbmodel` never reaches the game (symptom: "the model
  is 95% there but element X isn't rotated"). Either re-export cleanly (with the rotation on a GROUP per the
  note above), or — the reliable fallback — surgically hand-add the SCALAR `"rotationX"/"rotationY"/"rotationZ"`
  (NOT a `rotation` array) plus a centroid `"rotationOrigin"` to that element in the `.json` and treat the
  `.json` as source of truth (the working scriptorium does exactly this — its `feather` is
  `rotationX:-2,rotationY:-45` in the `.json` but `[0,5,0]` in the `.bbmodel`; they're intentionally decoupled).
  Diff the two files by element `from`/`to` first — nested groups store child cubes in group-relative coords in
  the `.json` but absolute in the `.bbmodel`, so only compare leaves whose extents match.
- **`lecturn-book-open` authored front = SOUTH (+Z) at `rotateY=0`** (decompiled the shape +
  `Mat4f` 2026-07-26). The `rest` board (`rotationZ=-45`) + pages (`rotationZ=-56`) make the reading
  surface face +X pre-rotation, then the shape's own root `rotationY=-90` turns +X→+Z. So the front
  is +Z, and the mesh's `front(r) = (sin r, cos r)` means **`MeshAngleRad = atan2(playerX−blockX,
  playerZ−blockZ)` points the reading face straight at the player with ZERO per-piece offset** —
  exactly what vanilla `BlockClutter` (`BlockShapeFromAttributes.DoPlaceBlock`, 22.5°-snapped) does,
  and why `clutter.json` sets no `rotation` for this piece. Only if you instead drive off a
  NORTH-at-0 cardinal assumption (e.g. raw `SuggestedHVOrientation`) do you need +180°: SOUTH→0,
  EAST→π/2, NORTH→π, WEST→3π/2. **Trap:** two same-named shapes exist — use the `bookshelves/`
  copy (root `-90`), NOT plain `clutter/lecturn-book-open.json` (root `-45`, lands diagonal/SE).

## Held-item writing (books / notebooks / tablets)

> Facts gathered during the 2026-07-21 roadmap-exploration pass (see `docs/specs/`), from
> decompiles + `anegostudios/vssurvivalmod` `Systems/WritingSystem/`. Not yet exercised by
> shipped code — verify live when the notebook (v2) / clay tablet (v3) tiers are built.

**Question: how does a HELD item open a GUI, store custom data, and persist
server-authoritatively (the held-item analogue of the Sign block pattern)?**

- **Open GUI:** override `CollectibleObject.OnHeldInteractStart(slot, byEntity, blockSel,
  entitySel, firstEvent, ref handling)`; set `handling = EnumHandHandling.PreventDefault` to
  consume the right-click. Construct/`TryOpen` the dialog **client-side only**
  (`if (api.Side == EnumAppSide.Client)`) — held interactions fire on both sides. Shift modifier
  via `byEntity.Controls.ShiftKey` (same as the lectern block path).
- **Custom data:** `ItemStack.Attributes` (`ITreeAttribute`) is saved AND synchronized with the
  stack (`SetString/GetString/SetBytes/GetBytes`). `ItemStack.TempAttributes` is NOT saved/synced
  — never put persistent data there. `ItemSlot.MarkDirty()` is the held-item analogue of
  `BlockEntity.MarkDirty()`.
- **Server-side keyed store:** `ICoreServerAPI.WorldManager.SaveGame` (`ISaveGame`) exposes
  `byte[] GetData(string key)` / `StoreData(string key, byte[])` (+ generic overloads). ~1 GB
  budget for all savegame data combined. Scribe plan: key documents `"scribe:doc:" + docId`,
  serialized with the existing `ScribeDocumentCodec`.
- **Vanilla precedent = no lock.** `ItemBook` stores text directly on the stack
  (`text`/`title`/`signedby`/`signedbyuid` attrs) with NO lock — `ModSystemEditableBook` keeps
  only a transient `nowEditing` (playerUID→ItemSlot) map to route the save. A held stack has one
  holder, so the lectern's position-based single-editor lock does not carry over to held items.
- **Offhand tool gating (stylus):** offhand slot is `EntityAgent.LeftHandItemSlot`; vanilla
  gates writable-book editing on `ItemBook.isWritingTool(LeftHandItemSlot)` →
  `Collectible.Attributes.IsTrue("writingTool")`. A stylus is just an item with
  `writingTool: true`.
- **Vertical-rack storability:** `scrollrackable: true` collectible attribute (checked in
  `BlockEntityScrollRack.OnInteract`) + an `onscrollrackTransform`.
- **Dropped-in-water destruction is free:** the `dissolveInWater: true` collectible attribute
  (`CollectibleObject.OnGroundIdle`, server-only, ~1%/tick destroy). Liquid state while held:
  `Entity.Swimming` / `Entity.FeetInLiquid` public fields (VintagestoryLib).

**Water-exposure detection for an item (rehydration hook, confirmed for the hard→wet tablet, task
0.4).** There is NO single "item touched water" event — the two exposure cases are two different
per-tick virtuals, and there is no torch-specific extinguish callback to piggyback on (a lit torch
is a *block* whose `BlockEntityTorch` isn't the model here; a carried item uses the two hooks
below). Both are `CollectibleObject` virtuals the engine already ticks, so no scheduler/registration
is needed:
- **Dropped stack floating in water** → `OnGroundIdle(EntityItem entityItem)`, gated on
  `entityItem.Swimming`. Runs server-side already but is NOT hard-gated to server, so gate it
  yourself (`api.Side == Server`) and call `entityItem.WatchedAttributes.MarkPathDirty("itemstack")`
  after mutating the stack so the change syncs.
- **Active held item while the holder swims** → `OnHeldIdle(ItemSlot slot, EntityAgent byEntity)`,
  gated on `byEntity.Swimming`; gate on `byEntity.World.Side == Server` and `slot.MarkDirty()` to
  sync the swap. This is the same pair vanilla uses for held/dropped idle ticks (the spin-in-hand
  items write `OnHeldIdle`, above).
The rehydration itself is a plain in-place attribute edit — clear our `hard` flag and
`RemoveAttribute("transitionstate")` so the engine re-seeds the `Harden` clock from "now" on its
next tick (restarting the ~2-day dry-out), keeping the document. No item-code swap (clay type is the
registered item variant), no new packet — it rides existing stack-attribute persistence.

**Symptom: an item with custom `docId`/attributes on its ItemStack loses them after being fired
in a kiln (blank/orphaned archive).** `BlockEntityPitKiln.OnFired()` (VSSurvivalMod) does
`slot.Itemstack = combustibleProps.SmeltedStack.ResolvedItemstack.Clone()` and only copies
`StackSize` — the source stack's `Attributes` are discarded (the beehive-kiln path too).
**Fix pattern:** don't rely on the vanilla combustible/kiln transform to carry stack attributes;
use a grid recipe with `GridRecipeIngredient.CopyAttributesFrom`, or a Scribe-owned firing
interaction that copies the attributes onto the output stack explicitly.

**Beeswax & wax-item facts (v1.22.x, confirmed from shipped assets — for the wax tablet tier).**
`game:beeswax` is a plain inert item (`itemtypes/resource/beeswax.json`): `maxstacksize: 32`, **no
`combustibleProps`, no temperature behavior** (nothing to melt). Supply chain: honeycomb
(`itemtypes/resource/honeycomb.json`) has a `Squeezable` behavior (`returnStacks: [beeswax]`,
`liquidItemCode: honeyportion`) and `juiceableProperties` (`returnStack: beeswax x5` via fruit
press) — beehive → honeycomb → squeeze/press out honey, keep the wax. Vanilla only ever *consumes*
beeswax as a recipe ingredient (never a mold): `recipes/cooking/candle.json` (3 beeswax + 1
flaxfibers → candle) and `recipes/grid/waxedcheese.json`. The candle's `combustibleProps
{ burnTemperature: 700 }` is it burning *as fuel*, not wax melting.

**Nothing destroys held/inventory items when the player catches fire.** `Entity.ApplyFireDamage`
(VintagestoryLib) only deals 0.5 HP/s `EnumDamageType.Fire` to the entity; the combust-destroy
path `DieInLava` → `Die(Combusted)` is entity-level and explicitly excludes `EntityPlayer`. Item
heat/fire state fields on `Entity`: `public bool InLava;`, `InLavaBeginTotalMs`/
`OnFireBeginTotalMs`, `bool IsOnFire` (backed by `WatchedAttributes.GetBool("onFire")`). Item
temperature API exists (`CollectibleObject.GetTemperature`/`HasTemperature`,
`GlobalConstants.TooHotToTouchTemperature == 250`, `CollectibleDefaultTemperature == 20`) but the
only *confirmed* way to raise a held/dropped item's temperature is a firepit/kiln smelt slot —
**open-air proximity heating a hotbar item is NOT confirmed** (verify before any proximity-heat
mechanic). Consequence: a "player on fire ruins your wax tablet" mechanic has no vanilla
precedent — the wax tablet is instead balanced by material cost + no path to a fired archive.

**Channeled "hold to complete" held-item gesture (for the wax-tablet wipe, or any Scribe
hold-to-act interaction).** Vanilla ships a first-class channeled-use pattern (confirmed against
`CollectibleObject`, the same mechanism `tryEatBegin` uses to channel eating):
`OnHeldInteractStart(...)` sets `handling = EnumHandHandling.PreventDefault` and begins the
gesture; `OnHeldInteractStep(float secondsUsed, ...)` is **called every 20ms** while the button is
held and **returns `false` to end** the channel (true to keep going); `OnHeldInteractStop(float
secondsUsed, ...)` fires on release. Releasing early simply ends the channel — a natural "cancel"
with no confirm dialog needed. Third-person animation via the `HeldTpUseAnimation` field (default
`"interactstatic"`) / `GetHeldTpUseAnimation`; first-person via
`byEntity.AnimManager?.StartAnimation(name)`. (The specific animation clip for a Scribe gesture is
a placeholder until art exists — verify the clip name.)

## `fpHandTransform` is a DEAD field — the held item renders through `tpHandTransform` in BOTH first- and third-person (2026-08-02)

**The finding (verified by decompiling the render path):** there is no separate first-person hand
transform. `fpHandTransform` in an itemtype JSON is loaded onto the runtime `CollectibleObject`
(`ItemType`/`BlockType` copy it) and then **never read at render time**. Tune `tpHandTransform`
instead — it is what shows in first person too.

The proof chain, all in the shipped DLLs:
- `EnumItemRenderTarget.HandFp` (index 1) is marked `[Obsolete("Use HandTp instead")]`
  (`VintagestoryAPI.dll`). The enum is `{ Gui=0, HandFp=1, HandTp=2, HandTpOff=3, Ground=4 }`.
- `InventoryItemRenderer.GetItemStackRenderInfo` (`VintagestoryLib.dll`) builds `renderinfo.Transform`
  with a switch over the target that has cases for **Ground / Gui / HandTp / HandTpOff only — no HandFp
  case**. So no render target ever selects `fpHandTransform`.
- The held-item renderer is `EntityShapeRenderer.RenderHeldItem` / `EntityPlayerShapeRenderer.RenderHeldItem`
  (`VSEssentials.dll`). In first-person mode the FP override falls through to
  `base.RenderHeldItem`, which calls `GetItemStackRenderInfo(slot, right ? HandTp : HandTpOff, dt)` — i.e.
  target **2/3 (Tp), never 1 (Fp)**, in every camera mode. FP only differs by shader + a near-camera
  projection (`HandRenderFov`, `fpModeItemShader`), not by transform.
- Vanilla confirms it: items that spin in-hand (e.g. the temporal-gear-style `OnHeldIdle`) set
  **both** `FpHandTransform.Rotation.Y` and `TpHandTransform.Rotation.Y` each frame — the Fp write is
  vestigial; only the Tp one is rendered.

**The built-in editors already cover this — there is nothing for a mod tool to add.**
`GuiDialogTransformEditor` (`.tfedit`, client-side — leading dot, not `/`) has target list
`[Gui=0, Fp=1, Tp=2, TpOff=3, Ground=4]`, but its `TargetTransform` **setter has no case 1** —
writing the Fp target is a silent no-op by design, because the field is dead. Its "Main Hand" target
edits `tpHandTransform` and previews live in first person. Hugo Cortell's Transform Designer
(`.tfdesign`) likewise exposes only `{ Gui, MainHand, OffHand, Ground }`. Use `.tfedit` → Main Hand.

**History:** Scribe once shipped a `.scribetfpanel` slider panel + `/scripttf` "fp" target + a
`ScribeTransformTuning` helper (the `add-fp-transform-panel` change) built on the false premise that
`fpHandTransform` was reachable-but-unexposed. That whole stack was scrapped 2026-08-02 once the
decompile above showed the field is dead; the notebook itemtypes now carry only `tpHandTransform`
(the fp block was removed to avoid implying it does anything). `/scripttf` reverted to its original
inline form.

**Related, still-true note — the FP arm lowers a held item after ~2.3 s.** The first-person arm
holds a freshly-selected item up briefly, then eases to a rest pose at the player's side. This is the
`helditemready` clip (`quantityframes:70, onAnimationEnd:EaseOut`; defaulted onto every collectible by
`CollectibleType` in VSEssentials): `EntityPlayer.HandleSeraphHandAnimations` calls `StartHeldReadyAnim`
once per raise and never re-raises an idle item, so `RunningAnimation.Progress` reaches the `EaseOut`
branch and the arm drops. `immersiveFpMode false` does NOT prevent it (tested on macOS). Crouching +
looking down keeps a held item in view. This only matters if you ever need to *see* a held item's
resting pose for a screenshot — it has nothing to do with transform tuning, which previews fine in the
inventory/GUI render or third-person.

## Always-on HUD overlays and hotkeys

**Question: how do you draw an always-on, per-tick-updated HUD overlay, and register a
rebindable hotkey?** (For the v5 pinned-task HUD — decompiled, not yet exercised.)

- `HudElement : GuiDialog` overrides `DialogType => EnumDialogType.HUD` (enum is only
  `{Dialog, HUD}`), `ToggleKeyCombinationCode => null`, `PrefersUngrabbedMouse => false`.
  `TryOpen(withFocus)` requests focus only when `DialogType == Dialog`; `OnEscapePressed()`
  returns false for a HUD (Escape can't close it). "Always-on" = call `TryOpen()` once and never
  close (`ShouldReceiveRenderEvents() => opened`).
- No base `OnGameTick`: use `capi.Event.RegisterGameTickListener(handler, ms)` and update text
  cheaply via `SingleComposer.GetDynamicText(key).SetNewText(...)` (no recompose); unregister in
  `Dispose()`. Canonical template: `Vintagestory.Client.NoObf.HudElementCoordinates` (composes in
  `OnOwnPlayerDataReceived`, `AddGameOverlay` plate + `AddDynamicText`, anchored via
  `EnumDialogArea` + `GuiStyle.DialogToScreenPadding`). To make it non-interactive (per
  `HudBosshealthBars`): `Focusable => false`, `ShouldReceiveKeyboardEvents() => false`, empty
  `OnMouseDown`.
- **Hotkeys:** `IInputAPI.RegisterHotKey(code, name, GlKeys key, HotkeyType type, alt, ctrl,
  shift)` + `SetHotKeyHandler(code, ActionConsumable<KeyCombination>)` (handler returns bool =
  consumed); register in `StartClientSide`; rebindings persist by code in `clientsettings.json`.

**Question: can a client hotkey OPEN a dialog while another GUI (inventory/Handbook) is already
open and focused, and will they coexist?** (For the backlog "open held Scribe item without closing
other windows" idea — decompiled `VintagestoryLib.dll`/`VintagestoryAPI.dll`, not yet exercised.)

- **Yes to both.** `ClientMain.OnKeyDown` dispatches every keydown in a fixed order: (1) mod
  `IClientEventAPI` keydown hook, (2) **global** hotkeys (`IsGlobalHotkey == true`), (3) the client
  systems — **`GuiManager` is here, forwarding to open dialogs**, (4) **normal** hotkeys. Each stops
  the chain if it sets `args.Handled`. So the GUI system gets the key BEFORE normal hotkeys.
- A normal (non-global) hotkey therefore fires while a dialog is open **as long as the focused
  dialog doesn't consume that exact key**. `GuiManager.OnKeyDown` only forwards to dialogs where
  `ShouldReceiveKeyboardEvents()` (base: `=> focused`) — i.e. only the ONE focused dialog — and a
  `GuiComposer` only marks `Handled` when an interactive element eats the key. **The real hazard is
  a focused text input** (Handbook search box, Scribe editor rows) swallowing plain letter/number
  keys. Avoid it by binding a **modifier combo** (Ctrl/Alt/Shift+key) or a function key, OR set the
  hotkey `IsGlobalHotkey = true` (`capi.Input.GetHotKeyByCode(code).IsGlobalHotkey = true`) so it
  fires in step 2 **before any dialog sees the key** — vanilla does this for screenshot/fullscreen.
  Use a non-`CharacterControls` `HotkeyType` (e.g. `GUIOrOtherControls`) or the key is gated on
  `allowCharacterControls`, false while a dialog is focused.
- **Opening a dialog does NOT close others.** `GuiDialog.TryOpen(withFocus)` registers into
  `capi.Gui.LoadedGuis`, sets `opened`, and (for `DialogType == Dialog` with focus) calls
  `Gui.RequestFocus(this)` — which reorders to front and calls `UnFocus()` on every OTHER dialog.
  It **un-focuses, never closes** them; they stay `opened` and rendering. So a hotkey-opened Scribe
  dialog **coexists** with the inventory/Handbook. Focus model: exactly one focused dialog gets
  keyboard; mouse goes to any opened dialog and clicking a background one refocuses it
  (`RequestFocus`). On close, `GuiManager.OnGuiClosed` auto-refocuses the first remaining focusable.
- **`RequestFocus` only reorders within a `DrawOrder` band — and LibGUI paint does not follow that
  band.** `GuiDialog.DrawOrder` default is **0.1**; Handbook, player inventory, and chests override
  to **0.2**; Escape is **0.89**. `RequestFocus` only moves among peers with the *same* DrawOrder,
  so a 0.1 LibGUI window (`GuiBase` does not override it) can shuffle over other Scribe windows
  but never joins the Handbook/Inventory *hit-test* band. Raising Scribe to 0.2 **does** put
  clicks in that band — then pixels and hits diverge: LibGUI paints every window into one Skia
  surface that `PostSkiaPipeline` flushes at Ortho RenderOrder 1.0 *before* `GuiManager` (equal-
  order insert-before). Vanilla Cairo/GL draws after that blit, so Handbook always covers Scribe
  even when Scribe is focused. Spikes that flushed/Ended Skia during the GuiManager pass hid
  vanilla GUI or dropped the opaque terrain pass (sky through the ground). **Do not override
  DrawOrder to 0.2 until LibGUI can composite per-window in the `OpenedGuis` loop.** (2026-08-27
  spike; decompiled `GuiDialog` / `GuiManager` / `PostSkiaPipeline`.)
- **Alt mouse-mode is unaffected by stacking.** Alt = hotkey `"togglemousecontrol"`. Any open
  `Dialog`-type with `PrefersUngrabbedMouse` (default true) already frees the cursor;
  `ClientMain.UpdateFreeMouse()` XORs that with Alt-held. Inventory/Handbook already ungrab, so
  opening another `PrefersUngrabbedMouse` Scribe dialog on top changes nothing — Alt still toggles
  camera-look for the whole open set.

## World config (`worldconfig.json`) — staging + the `playStyles` main-menu crash

**Symptom: main menu crashes with `NullReferenceException` in
`GuiScreenSingleplayer.getHoverText` (line ~309) the instant you click Singleplayer — before
any world loads — right after adding a mod `worldconfig.json`.** Also: `/worldconfig <key>`
reports "No such config found" for a key your `worldconfig.json` clearly declares.

- **The file lives at the MOD ROOT**, a sibling of `modinfo.json` (`ModContainer` reads
  `Path.Combine(FolderPath, "worldconfig.json")`, or the same-named zip entry). It is NOT under
  `assets/`. Our `build/restage.sh` only copied `modinfo.json` + DLLs + `assets/`, so the file
  was silently dropped and the staged mod had a `null` `Mod.WorldConfig` — which is why
  `/worldconfig` (`CmdWorldConfig`, which iterates every `mod.WorldConfig.WorldConfigAttributes`)
  couldn't find the key. Fix was to stage the root file explicitly.
- **`ModWorldConfiguration` is a plain JSON POCO with bare public fields and NO defaults:**
  `PlayStyle[] PlayStyles; WorldConfigurationAttribute[] WorldConfigAttributes;`. If your JSON
  omits `playStyles`, the field deserializes to **`null`**.
- **The engine landmine:** `GuiScreenSingleplayer.getHoverText` builds a hover tooltip for
  EVERY save in the singleplayer list and does `foreach (PlayStyle ps in verifiedMod.WorldConfig
  .PlayStyles)` with **no null guard**. A mod that ships `worldConfigAttributes` but no
  `playStyles` therefore crashes the main menu for all saves. (Genuine vanilla missing-null-check
  bug, but the practical fix is ours.)
- **Fix: always include `"playStyles": []`** alongside `worldConfigAttributes` — an empty array
  makes the unguarded loop iterate zero times. `WorldConfigurationAttribute` fields are
  `Category, Code, DataType, Default, Values, Names, Min/Max/Step, ...` (JSON keys are
  lower-cased: `category`, `code`, `dataType`, `default`).
- Note it stayed hidden until the file was actually staged: before that, `WorldConfig` was null
  so the whole `foreach (verifiedMod ...)` outer loop skipped scribe entirely.
- **The staging gap is THREE scripts, not one.** `restage.sh` was fixed first, then `restage.ps1`
  (Windows dev), then — the one that actually reaches players — **`build/package.sh`** (the release
  zip). All three independently copy `modinfo.json` + DLLs + `assets/` and each needed its own
  `worldconfig.json` copy added. A fix to the dev restagers does NOT fix the shipped mod; if
  `/worldconfig` says "No such config found" for a *released* build, suspect `package.sh` first.

### No existing-world "migration" is needed for a new worldconfig key (2026-07-31)

Worry: "worlds created before we added the key won't have it, so upgraders can't toggle it."
Decompiling `CmdWorldConfig` + `GuiScreenWorldCustomize` shows this is a **non-issue** — the engine
already handles an absent key everywhere that matters:
- **Key discovery is from the LOADED mod, not the save.** `/worldconfig` (and the Customize GUI)
  enumerate `modLoader.Mods` → `mod.WorldConfig.WorldConfigAttributes`. A save that never had the
  key still lists/accepts it as long as the *mod* ships `worldconfig.json`.
- **Read falls back to the registered default's… no — to YOUR explicit default.** `CmdWorldConfig`
  does `if (WorldConfiguration.HasAttribute(key)) …read… else "(default:) " + TypedDefault`. But
  note `sapi.World.Config.GetBool(key, fallback)` does NOT consult `TypedDefault` — so always pass
  the same explicit fallback in code (we pass `true`). An unseeded key then behaves as the default.
- **Write is unconditional.** `SetBool`/`SetInt`/etc. write into the save's `WorldConfiguration`
  regardless of whether the key was seeded at creation. So `/worldconfig <key> <val>` persists on
  any existing world. → **Do not write StartServerSide backfill code; it's redundant.**

### Hiding a worldconfig key from the world-creation GUI — two independent flags

`WorldConfigurationAttribute` (in `VintagestoryAPI.dll`) has two bool fields that control GUI
presence, verified in `GuiScreenWorldCustomize`:
- **`onCustomizeScreen`** (default **`true`**): `false` → the attribute is skipped entirely in the
  Customize screen loop (`if (!attribute.OnCustomizeScreen) continue;`). It exists ONLY via the
  `/worldconfig` chat command (needs `controlserver`). Use this for operator-only settings that
  shouldn't clutter the worldgen screen. **We set this `false` for `scribeClockmakerRequiresTrait`.**
- **`onlyDuringWorldCreate`** (default **`false`**): `true` → the control still renders but is
  `Enabled = (wcu.IsNewWorld || !OnlyDuringWorldCreate)`, i.e. **greyed-out/read-only** once the
  world exists (editable only at creation). Does not hide it.
- The Customize screen is reachable for EXISTING worlds too: Singleplayer → pick world → **Modify →
  Customize** (`GuiScreenSingleplayerModify` constructs `GuiScreenWorldCustomize` with
  `IsNewWorld = false`). So "world config" ≠ "worldgen-only"; it's just per-save config.
- GUI label/tooltip lang keys are `worldattribute-<code>` and `worldattribute-<code>-desc`
  (via `Lang.Get` / `Lang.GetIfExists`). With `onCustomizeScreen: false` these are never rendered,
  so they're not needed — don't ship dead keys.

## BlockEntity tree sync vs. save, and transient session state (editor lock)

**Symptom that sent us here: a "transient" single-editor lock on the Scribe lectern behaved as a
PERMANENT lockout** — once player 1 opened the editor, player 2 could never edit again, even after
P1 left, relogged, or (we assumed) a server restart. First hypothesis was "the lock is baked into
the save via `ToTreeAttributes`." Decompiling `VintagestoryLib.dll` disproved that and pinned the
real cause; the facts are worth keeping:

- **`ToTreeAttributes`/`FromTreeAttributes` are double-duty: disk save AND network packet.** The
  same tree is written to the region file and used to build the per-client BE packet. So a value you
  put in the tree reaches clients — but whether it survives a *server restart* depends on whether the
  server-side field is re-read from the tree in `Initialize`, which it usually is NOT for fields you
  only mirror for the client.
- **`FromTreeAttributes` is always called before `Initialize()`** (DLL doc-comment, confirmed).
  On a fresh chunk load the engine calls `Initialize` on every disk-loaded BE (ServerMain
  `LoadBlockEntities`). If `Initialize` doesn't re-read a field, that field starts at its C# default
  after a restart regardless of what was on disk.
- **The server rebuilds every client BE packet LIVE** via `SendBlockEntity`/`SendDirtyBlockEntities`,
  re-serializing from `ToTreeAttributes` at send time. `MarkDirty()` enqueues the BE to
  `DirtyBlockEntities`; the dirty flush re-serializes current in-memory state. So a client mirror is
  only ever as correct as the server's live field at flush time.
- **Conclusion for a "transient" lock:** a server restart already self-heals it (the field defaults
  back), so the reported permanent lockout could ONLY be a *live in-memory leak* — the server's
  `lockHolderUid` never getting cleared when the holder left. It was never a persistence bug. The fix
  is defence-in-depth on the release side, not the save side: clear on load (`Initialize`), release
  on EVERY dialog-close path (not just editor-mode exits — a switch-to-read via a nav button was the
  leaking path), and on `OnPlayerDisconnect`. No heartbeat/timer needed.
- **Design pattern that fell out of this:** keep server-authoritative state as two fields — the
  authoritative one written only by grant/release (`lockHolderUid`), and a client mirror
  (`syncedLockHolderUid`) that `FromTreeAttributes` sets and the affordance reads. If you ALSO want a
  value that genuinely persists (e.g. a durable per-block permission), re-read it in `Initialize` from
  the tree and default it when the key is absent (pre-existing saves) — that's the difference between
  "transient session state" and "persisted state" in this API, and it's entirely up to whether
  `Initialize` reads the key back.

### `Initialize` vs `FromTreeAttributes` ordering is NOT universal — it flips for a freshly-placed BE (2026-07-31)

The bullet above says "`FromTreeAttributes` is always called before `Initialize()`." That is only
true for a BE that **already existed** (chunk-loaded from disk / arriving on the client via the
first BE sync). The `VintagestoryAPI` `BlockEntity.Initialize` doc-comment states it explicitly:

> "called right after the block entity was spawned or right after it was loaded from a newly loaded
> chunk. However **if this block entity already existed then `FromTreeAttributes` is called first!**"

So the real ordering is:
- **Freshly placed** (brand-new BE): `Initialize` runs **first**, then `FromTreeAttributes` (once the
  first sync/save round-trip carries state in).
- **Loaded / already existed**: `FromTreeAttributes` runs **first**, then `Initialize`.

**Symptom that sent us here: a newly-placed Scribe lectern would not open, while lecterns already in
the world opened fine.** Root cause was this ordering asymmetry crossed with a registry keyed by a
value that only `FromTreeAttributes` fills in:

- The client dialog only opens when the server's open-reply is routed back to the BE via
  `ScribeModSystem.TryResolveHost`, a lookup in `_hostRegistry` **keyed by `Document.DocId`**.
- Each side builds its `ScribeDocument` with its OWN random `DocId` (`ScribeDocument` ctor →
  `Guid.NewGuid()`). The authoritative DocId only arrives in `FromTreeAttributes`.
- `RegisterHost` was called in `Initialize` (and, per `ab702d1`, re-called in the server-side
  `ApplyEdit`/`OnBlockPlaced`) — but NOT in `FromTreeAttributes`.
- For a freshly-placed lectern the client runs `Initialize` first → registers under the **throwaway**
  random DocId → then `FromTreeAttributes` swaps `Document` to the real DocId but leaves the registry
  keyed under the dead id → the open-reply lookup misses → **the dialog silently never opens.** A
  loaded lectern works because `FromTreeAttributes` ran first, so `Initialize` already registers under
  the correct DocId.
- **Fix:** call `ModSystem?.RegisterHost(this)` at the end of `FromTreeAttributes` too (no-op before
  `Api` is set, i.e. the load-path ordering — `Initialize` registers moments later). Same bug class as
  `ab702d1`, on the one path that fix didn't cover.

**General lesson:** any per-BE index keyed by a field that `FromTreeAttributes` populates (a DocId, an
owner UID, anything not known at construction) MUST be (re-)keyed in `FromTreeAttributes`, not only in
`Initialize` — otherwise it works for loaded blocks and silently breaks for freshly-placed ones.

## Calendar, player events, per-player storage, and survival-mod systems

**Question: how do you read the in-game date, subscribe to player death, persist per-player
data, detect crafting milestones, and reach temporal-storm / Handbook systems?** (For the
chronicle/integration features — decompiled, not yet exercised.)

- **Calendar:** `api.World.Calendar` is `IGameCalendar`. Server-side it is NULL until run stage
  `LoadGamePre`, non-null after. Reads: `Year` (starts 1386), `DayOfYear`, `Month`/`MonthName`
  (`EnumMonth`), `GetSeason(BlockPos)` (`EnumSeason`), `TotalDays`/`TotalHours` (double, monotonic
  — good stable sort/dedup key), `HourOfDay`, and `PrettyDate()` for an engine-formatted string.
  For a game-agnostic Core model, store the numeric stamp and format in the Mod layer — don't call
  `PrettyDate()` in Core.
- **Player death:** `IServerEventAPI.PlayerDeath` → `PlayerDeathDelegate(IServerPlayer byPlayer,
  DamageSource damageSource)`. Server-side, once per death, gives identity + cause.
- **Resolving the attacker — use `DamageSource.GetCauseEntity()`, NOT `SourceEntity`.** The API
  doc-comment: `SourceEntity` is **null for non-projectile (melee) damage**; `GetCauseEntity()`
  returns `CauseEntity ?? SourceEntity` and is the documented way to get the attacker "for both
  melee and projectile damage" — it's what vanilla's own server-side `GetDeathMessage` uses.
  Reading `SourceEntity` alone silently drops every melee PvP kill (this was the
  `fix-pvp-death-kill-attribution` root cause). No `deathmsg-player-*`/`deathmsg-pvp-*` lang key
  ships in vanilla, so a PvP death has to build its own message string.
- **Naming the killing weapon:** damage *type* does NOT distinguish vanilla melee weapons —
  `EntityAgent.OnInteract` hardcodes melee `DamageSource.Type = BluntAttack` (sword/spear/knife
  don't override it); only projectiles carry a meaningful type. The accurate signal is the killer's
  held item: `killerEntity.RightHandItemSlot?.Itemstack?.Collectible?.Tool` (`EnumTool?` — Sword,
  Bow, Spear, Club, Firearm, Crossbow, …; the exotic members exist for mods, no vanilla item uses
  them). `RightHandItemSlot` is the killer's active hotbar slot, so read it in the death event as a
  best-effort heuristic (vanilla's death-audit log reads the same field).
- **Reconstructing a vanilla-style death message — vanilla ships almost no `deathmsg-<creature>`
  keys.** Vanilla's `GetDeathMessage` (decompiled from `VintagestoryLib.dll`) builds the key as
  `"deathmsg-" + causeEntity.Code.Path.Replace("-", "")` (hyphens **stripped**, so `drifter-nightmare`
  → `deathmsgdrifternightmare`) and looks it up in a cache pre-split on the trailing `-N`. But
  `game/lang/en.json` only defines `deathmsg-drifter-normal-1..3` (plus environmental `deathmsg-fall`,
  `-hunger`, `-fire-block`, `-electricity-block`, `-explosion`) — there is **no** key for
  nightmare/tainted/corrupt drifters, bears, wolves, etc. On a miss vanilla falls back to
  `Lang.Get("Player {0} got killed by {1}", name, causeEntity.GetPrefixAndCreatureName())`.
- **`Entity.GetPrefixAndCreatureName()` is the correct, variant-aware creature name** ("a nightmare
  drifter", "a brown bear") — it reads `game:prefixandcreature-<code>` (falling back to the
  hyphen-stripped key, then `generic-wildanimal` = "a wild animal"). Note there is **no "grizzly"**
  bear in vanilla (brown/black/panda/polar/sun; codes like `bear-brown-adult-male`) and **no bare
  "drifter"** entity — always a `-normal/-deep/-tainted/-corrupt/-nightmare/-double-headed` variant.
  Scribe's `BuildDeathMessage` uses `GetPrefixAndCreatureName()` + its own `scribe:scribe-mob-death-N`
  flavor pool rather than vanilla's sparse `deathmsg-` keys, precisely because those keys don't exist
  for most creatures.
- **Per-player persistent store:** `IServerPlayer.SetModData<T>(key, data)` / `GetModData<T>(key,
  default)` — permanent, per-player, NOT client-synced (also raw-byte `SetModdata`/`GetModdata`/
  `RemoveModdata`). This is where a "milestones seen" set lives.
- **`InventoryManager.InventoriesOrdered` includes the CREATIVE inventory (and ground/mouse/crafting)
  — do NOT treat it as "the player's carried items".** It walks EVERY inventory the player owns; the
  `GlobalConstants` class names are `hotbar`, `backpack`, `character`, `creative`, `ground`, `mouse`,
  `craftinggrid`, and `InventoryPlayerCreative : InventoryBasePlayer` is in the set. A
  creative-listed item (`"creativeinventory": {…}`) is enumerated from the creative inventory as an
  infinite **template** stack — so a naive "find the first held item of type X in `InventoriesOrdered`
  and write to it" can (a) mutate a creative template (its ItemStack attributes then ride along on
  every future spawned copy — looked like "a fresh notebook auto-populates past history"), and
  (b) resolve a different stack than the one the player thinks they're holding. Fix pattern: filter
  `inv.ClassName` against an allow-list of the real carried inventories (`hotbar`/`backpack`/
  `character`/`mouse`; `mouse` is the live cursor-drag stack, a real held item) and iterate ALL
  matches, not just the first, if the state should live on every copy. (Scribe's `FindCarriedNotebooks`
  — the `fix-pvp-death-kill-attribution` follow-up — does exactly this after the first-match walk
  caused all three of those symptoms.)
- **No global craft/smelt event:** the only crafting hook is the instance override
  `Collectible.OnCreatedByCrafting(...)`; `MatchGridRecipeDelegate` is a match filter, not a
  completion signal. Milestone/achievement-style detection must poll inventory (slow
  `RegisterGameTickListener` scan against a milestone `AssetLocation` table) or hook `DidUseBlock`.
- **HTTP:** no HTTP type ships in `VintagestoryAPI.dll`; the mod targets `net10.0`, so use BCL
  `System.Net.Http.HttpClient` directly (static long-lived instance) — zero new dependency.
- **Survival-mod-coupled (NOT in the API DLL — `GetModSystem<T>()`-guard and degrade if absent):**
  temporal storms = `SystemTemporalStability` (broadcasts `TemporalStormRunTimeData` on channel
  `"temporalstability"`; `StormData.nowStormActive` flips true on start). Handbook =
  `ModSystemSurvivalHandbook.OpenDetailPageFor(pageCode)`; item page codes via
  `GuiHandbookItemStackPage.PageCodeForStack(ItemStack)`.
- **Opening / closing the handbook WITHOUT coupling to the survival mod's privates (verified against
  1.22.3 DLLs, 2026-08-02):** `ModSystemSurvivalHandbook` holds its `GuiDialogHandbook dialog` as a
  **private** field and exposes **no** public open/close/toggle (only the `OnInitCustomPages` event +
  `ShouldLoad`/`StartClientSide`/`Dispose`), so `GetModSystem<ModSystemSurvivalHandbook>()` is a dead
  end for driving the dialog — and `OpenDetailPageFor` is a method on `GuiDialogHandbook`, not on the
  mod system. To **open/navigate**, fire the registered `"handbook"` link protocol
  (`capi.LinkProtocols.TryGetValue("handbook", out var open); open(new LinkTextComponent("handbook://<pagecode>"))`)
  — its handler opens the dialog if closed then calls `OpenDetailPageFor`; absent survival mod ⇒ no
  `"handbook"` entry ⇒ graceful no-op. To **discover/close** it decoupled, scan the live open-dialog
  list `capi.Gui.OpenedGuis` (`IGuiAPI.OpenedGuis : List<GuiDialog>`) for the dialog whose **public**
  `ToggleKeyCombinationCode == "handbook"` (`GuiDialogHandbook` overrides it to that stable string) and
  call base-`GuiDialog.TryClose()`. The base `GuiDialog` publicly exposes `IsOpened()`, `TryOpen()`,
  `TryClose()`, `Toggle()`, and the abstract `ToggleKeyCombinationCode` — matching on that string is the
  reflection-free equivalent of `OfType<GuiDialogHandbook>()` and takes NO `VSSurvivalMod` type reference.
  The handbook's own hotkey handler is itself `if (dialog.IsOpened()) TryClose(); else { TryOpen(); … }`,
  confirming those are the intended primitives. The current handbook PAGE is held in `GuiDialogHandbook`'s
  protected `browseHistory`/`pageNumberByPageCode` — NOT publicly readable, so a page-aware "close only
  when on my page" needs the observable open/closed state plus a navigate-then-dismiss flow, not private
  page state. (Used by `add-info-button-handbook-toggle` — the editor ⓘ button toggle.)
- **In-game time speed (how many in-game seconds pass per real second):**
  `Calendar.SpeedOfTime * Calendar.CalendarSpeedMul`. `SpeedOfTime` defaults to 60,
  `CalendarSpeedMul` to 0.5, so the default is **30 in-game seconds per real second → a 48-minute
  day** (the world's "days last X real minutes" setting drives these). `GameCalendar` proves the
  product: its `secondsPerRealSecond(seconds)` returns `seconds * currentSpeedOfTime * CalendarSpeedMul`,
  and `DayLengthInRealLifeSeconds = 3600 * HoursPerDay / SpeedOfTime / CalendarSpeedMul`. So to convert
  a player-entered in-game duration to the real-time it should take: `realSeconds = inGameSeconds /
  (SpeedOfTime * CalendarSpeedMul)`. `SpeedOfTime` already includes any active
  `SetTimeSpeedModifier` contributions ("sum of all modifiers"), so read it live rather than caching.

## Handbook variant identity + the three page classes (recipe / meal-link work, 2026-08-19)

- **`GuiHandbookItemStackPage.PageCodeForStack(ItemStack)` is THE attribute-qualified variant-identity
  primitive** — `public static` in `VSSurvivalMod.dll` / `Vintagestory.GameContent`, callable directly
  (no reflection, ships with the base game). It clones the stack's attributes, strips every
  `GlobalConstants.IgnoredStackAttributes` key + `durability`, takes a deterministic `SortedCopy(true)`,
  and `TreeAttribute.ToJsonToken`s the rest, yielding e.g.
  `block-lantern-large-up-{"glass":"quartz","lining":"plain","material":"gold"}`; an attribute-less
  stack returns just `class-shortcode`. This is what VS keys each Handbook PAGE on, and what Tallybook
  keys recipe groups on. **Use it — not `Output.ResolvedItemStack.Satisfies(pageStack)` — to bind a
  grid recipe to the viewed variant.** `Satisfies` is an attribute-SUBSET test: every metal lantern
  satisfies every other, so a bare-code-keyed signature collapsed all 13 metal fan-outs to the first
  (the `fix-recipe-variant-identity` 6.1 bug). `ScribeCraftRecipeProbe` now keys both its signature and
  its output-matching on `PageCodeForStack` equality. (Note: VS fans variant/wildcard grid recipes into
  one CONCRETE `GridRecipe` per resolved output at load via
  `GenerateRecipesForAllIngredientCombinations` → `FillPlaceHolder`, substituting `{var}` into both
  ingredient codes AND the output's `attributes` JSON — so each metal lantern really is its own recipe
  with its own attributed output stack; the probe just has to identify it correctly.)
- **`handbook: { groupBy: [...] }` only dedups the Handbook LIST, not the pages.** Each variant still
  gets its own `GuiHandbookItemStackPage` with a concrete attributed `dummySlot.Itemstack`, and THAT
  exact on-screen stack (attributes intact) is what VS hands
  `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo(inSlot, …)` (the method
  `ScribeHandbookPatch` postfixes). So a grouped page (e.g. `lantern-{size}-*`) does NOT hand a fuzzy
  representative stack — the probe already receives the right variant; earlier "grouped-page ambiguity"
  theories were wrong.
- **Three distinct Handbook page classes, only two of which route through patched methods:**
  `GuiHandbookItemStackPage` (ordinary item/block — text via `GetHandbookInfo`, patched by
  `ScribeHandbookPatch`); `GuiHandbookTextPage` (guide/explainer, `CategoryCode == "guide"` — text via
  `Init` rebuilding `comps`, patched by `ScribeGuidePageHandbookPatch`); and
  **`GuiHandbookMealRecipePage`** (cooked meals + pies — text via its OWN
  `protected virtual RichTextComponentBase[] GetPageText(ICoreClientAPI, ItemStack[], ActionConsumable<string>)`,
  which calls NEITHER patched method). A meal page therefore had zero Scribe presence until
  `ScribeMealPageHandbookPatch` postfixed that `GetPageText` (the 6.4 bug). Gotchas for that patch:
  (1) `GetPageText` is **overloaded** on the class (also a no-arg `PageText GetPageText()` summary), so
  the `[HarmonyPatch]` MUST name the 3-arg parameter types to disambiguate;
  (2) `GuiHandbookMealRecipePage.Title` is **already `Lang.Get`-resolved in the constructor**
  (`Lang.Get("mealrecipe-name-" + recipe.Code)` / pie variant) — store it VERBATIM. Contrast
  `GuiHandbookTextPage.Title`, which is a RAW lang key the guide patch must feed through `Lang.Get`. If a
  meal Link row ever shows a raw `mealrecipe-name-…` key, this divergence is the trip-wire.
  `PageCode` is `handbook-mealrecipe-<recipe.Code>` (+`-pie`); a meal has no stable countable item
  (bowl contents are per-instance random) and is not a grid recipe, so meals get a Link only — no
  Tracker, no Craft.
- **A containerized-liquid grid ingredient is NOT a grid cell — it's declared on the RECIPE
  (`attributes.liquidContainerProps`).** For ink-and-quill / poultice / bandage / oillamp / beenade the
  ingredient cell is a SOLID container (`bowl-*-fired`), and the liquid it must hold lives on the recipe:
  `recipe.Attributes["liquidContainerProps"]` → `{ requiresContent: { type, code }, requiresLitres,
  consumeContainer }` (e.g. ink → `item/dye-black`, poultice → `item/honeyportion`). There is a
  per-ingredient fallback: `recipe.ResolvedIngredients[i].RecipeAttributes["requiresContent"]`.
  Consequence: a `cell.ResolvedItemStack.Collectible.MatterState == EnumMatterState.Liquid` check NEVER
  fires for these — the cell's matter state is Solid (the bowl). `CraftingRecipeIngredient` has no
  liquid/content field at all. The authoritative "given a recipe, what liquid does it need" logic is
  vanilla **`BlockLiquidContainerBase.OnHandbookRecipeRender`** (VSSurvivalMod): read
  `requiresContent.{type,code}`, build the stack, `stack.GetName()` for the liquid's display name.
  `ScribeCraftRecipeProbe.TryAddLiquid` mirrors this (recipe-level first, cell `RecipeAttributes`
  fallback). As of add-liquid-ingredient-tracker it emits the liquid as a **counting litre Tracker**
  (reading the sibling `requiresLitres` float, target = `ceil(litresPerCraft × craftsNeeded)`), degrading
  to the old `scribe-gui-craft-liquid-note` only when the liquid can't be resolved (wildcard code, missing
  `requiresLitres`, unresolvable stack). Counting happens in `ScribeTrackerCounter.CountCarried`: a target
  whose resolved collectible is `EnumMatterState.Liquid` sums litres across carried
  `BlockLiquidContainerBase`s via `container.GetContent(stack)` +
  `BlockLiquidContainerBase.GetContainableProps(content).ItemsPerLitre` (both confirmed by decompile), not
  loose stack sizes. The container bowl stays a counted ingredient.
  **Tallybook does NOT solve this** — its `RecipeProbe` never reads `liquidContainerProps`, so it has
  the same blind spot (would just count the bowl). 1.22 caveat: `GridRecipe.Ingredients`/`IngredientPattern`
  are null client-side (use `ResolvedIngredients`), but `RecipeBase.Attributes` survives client-side.
- **`CraftingRecipeIngredient.Code` defaults to `new AssetLocation("*","*")` (`*:*`).** `Consume` is
  documented as equal to `!IsTool` and is the preferred flag. `MatchingType == TagsOnly` is a tag matcher
  with no usable item code. Encoding that default `*:*` via `ScribeItemRef.EncodeWildcard` produced a
  "Pocketsun (any variant)" child on recipes whose tool slot is tags-only (debarked oak log). Skip
  `IsTool`, `!Consume`, `TagsOnly`, and `Domain=="*" && Path=="*"` in `DeriveIngredients` /
  `IngredientCode`. (refine-crafting-tasks-1-3-2)

## Custom TTF fonts in the GUI

**This is now the LibGUI/Skia path.** The old Cairo/`FreeTypeFontFace` note here described the
retired native GUI (`ScribeRowElement.ComposeElements` on a private Cairo surface) — gone since the
LibGUI migration. Scribe's text now renders through LibGUI/SkiaSharp, which has a proper
cross-platform font-registration API, so the FreeType-direct hack is unnecessary.

**Register a bundled `.ttf`:** load it to an `SKTypeface` with `new SkiaAssetLoader(capi).LoadFont(
domain, path)` (reads the asset bytes via `SKTypeface.FromStream` — no filesystem path, no temp file,
works packed or unpacked), then `Gui.Rendering.Text.FontRegistry.RegisterCustomFont(family, weight,
typeface)`. Do it once at `StartClientSide`. `TextLayoutHelper` resolves a `TextStyle.FontFamily` via
`GetCustomTypeface(family, weight)` BEFORE any `SKTypeface.FromFamilyName` system fallback, so naming
the registered family in a `TextStyle` is sufficient for both measure and draw — no per-surface
override. This mirrors LibGUI's own `GuiModSystem.LoadFonts`. (Scribe: `ScribeModSystem.RegisterCustomFonts`.)

**Gotcha 1 — asset path: use `textures/fonts/`, NOT a bare `fonts/`.** `fonts` is not one of VS's
scanned `AssetCategory` codes (decompiled `AssetCategory` — 16 categories, no `fonts`), so a bare
`assets/scribe/fonts/x.ttf` is never scanned → `TryGet` returns null. LibGUI only loads its own
`assets/gui/fonts/` by doing `api.Assets.AddModOrigin("gui","fonts")` + `Assets.Reload` first. File
under the already-scanned `textures` category (like the SVG icons) to skip that dance. `LoadFont`
lowercases the path, so the asset filename must be lowercase.

**Gotcha 2 — register the face under EVERY `FontWeight` you'll request; the registry is keyed by
`(family, weight)` and MISSES silently.** `GetCustomTypeface(family, weight)` returns null if the
exact weight wasn't registered, and `TextLayoutHelper` then falls through to `SKTypeface.FromFamilyName`
— which for a non-OS-installed family resolves to the system default. Symptom: text renders in the
default font with no error logged. This bit us: the lectern title is `FontWeight.Bold` but Caudex was
registered only under `Normal`, so the Bold lookup missed and the title stayed sans-serif. Fix:
register the (single) TTF under Normal/SemiBold/Bold/Italic — Skia synthesizes bold/italic from a
regular face. No size caveat like the old Cairo path — Skia sizing is handled by `TextLayoutHelper`.

## Player groups (for multi-owner / faction-style block gating)

**Question: does Vintage Story have any first-party faction/group concept for gating a block on
more than one player?** Yes — a persisted **player-group** system (backs in-game chat groups),
NOT factions/territory. `ICoreServerAPI.Groups` → `IGroupManager` (`PlayerGroupsById`,
`GetPlayerGroupByName`, `AddPlayerGroup`/`RemovePlayerGroup`). A `PlayerGroup`
(`Vintagestory.API.Server`) has `int Uid`, `string Name`, `string OwnerUID`, `JoinPolicy`,
`List<IPlayer> OnlinePlayers`. Per-player membership: `IPlayer.Groups`/`GetGroup(int)` →
`PlayerGroupMembership { EnumPlayerGroupMemberShip Level; string GroupName; int GroupUid }`;
`EnumPlayerGroupMemberShip = { None, Member, Op, Owner }` gives leader/member roles for free. For
*block-ownership* precedent, `LandClaim` gates via `PermittedPlayerUids`
(`Dictionary<string,EnumBlockAccessFlags>`) and `PermittedPlayerGroupIds`
(`Dictionary<int,EnumBlockAccessFlags>`) — the engine's own "owner UID + permitted-UID set +
permitted-group-id set" shape. `BESign` gates editing on `World.Claims.TryAccess(player, Pos,
EnumBlockAccessFlags.BuildOrBreak)`, not a per-block owner field — Scribe's owner/lock gate layers
on top of land claims, not instead of them. So group-gated blocks need no third-party mod.

## Icon-button glyphs — custom SVG icons and tinting

**Symptom: an `AddIconButton`/`GuiElementToggleButton` with a made-up icon code (e.g.
`"scribe:pin"`) renders as an empty button — no glyph, no error, no crash.**

`Vintagestory.API.Client.IconUtil.DrawIconInt(cr, type, …)` (confirmed via `ilspycmd` against
`/Applications/Vintage Story.app/VintagestoryAPI.dll`, 2026-07-21) does **NOT** fall through to
`Gui.DrawSvg` for unknown codes — a widely-assumed "escape hatch" that does not exist. It (1)
looks `type` up in `Dictionary<string, IconRendererDelegate> CustomIcons`, then (2) runs a
`switch` over the hardcoded built-in glyph names (`plus`, `eraser`, `wpBee`, …). **There is no
`default` case** — an unrecognized code matches nothing and the method returns having drawn
nothing, silently.

`GuiElementToggleButton` (the base of Scribe's `ScribeHoverIconButton`) draws its icon by calling
`api.Gui.Icons.DrawIcon(ctx, icon, …, Font.Color)` each render pass — so it goes through exactly
this path; there is no separate SVG route for buttons.

**Fix pattern (to use a custom SVG icon):** register it into `CustomIcons` at client init — but
**NOT** with the obvious `CustomIcons[code] = capi.Gui.Icons.SvgIconSource(assetLocation)`. That
one-liner is a **trap that crashes the client** (see the two gotchas below). The working pattern
re-resolves the asset on every draw, capturing the `AssetLocation`, not the asset (Scribe's
`ScribeModSystem.RegisterSvgIcon`):
```csharp
capi.Gui.Icons.CustomIcons[code] = (ctx, x, y, w, h, rgba) =>
{
    var asset = capi.Assets.TryGet(loc, loadAsset: true);   // re-fetch each draw; reloads if unloaded
    if (asset?.Data is null) return;                        // never throw — draw nothing if missing
    capi.Gui.Icons.SvgIconSource(asset)(ctx, x, y, w, h, rgba);
};
```
`IconUtil.SvgIconSource(AssetLocation)` internally does `capi.Assets.TryGet(loc)` once then
`capi.Gui.DrawSvg(asset, …)`. After registration, any button using that code string renders the SVG.

**Gotcha 1 — the asset MUST live under a real `AssetCategory`, i.e. `textures/icons/…`, not a bare
`icons/…`.** VS only scans assets under its 16 hardcoded `AssetCategory` codes (`AssetCategory.categories`:
blocktypes, config, dialog, entities, itemtypes, lang, patches, recipes, shaders, shaderincludes,
shapes, sounds, textures, music, worldgen, worldproperties — there is **no `icons` category**). A
file under `assets/scribe/icons/pin.svg` is never loaded → `TryGet` returns null → silent empty
button. Vanilla stores every SVG icon at `textures/icons/` (e.g. `game:textures/icons/copy.svg`);
match that: `assets/scribe/textures/icons/pin.svg`, resolved as
`new AssetLocation("scribe", "textures/icons/pin.svg")`.

**Gotcha 2 — do NOT capture the `IAsset`; it gets unloaded and the delegate then CRASHES.** The
naive `SvgIconSource(asset)` captures the asset object and re-reads `asset.Data` at *draw* time.
But `AssetManager.UnloadAssets()` runs after startup and sets `Data = null` on every non-patched
asset (decompiled 2026-07-21; only `IsPatched` assets are spared). So an icon registered at
`StartClientSide` has real bytes then, but by the first compose (seconds later) `.Data` is null and
`SvgLoader.rasterizeSvg` throws `ArgumentNullException("Asset Data is null. Is the asset loaded?")`,
crashing the client mid-compose (not a catchable silent failure — a hard crash to desktop). Fix:
re-resolve via `TryGet(loc, loadAsset: true)` inside the delegate, which reloads an unloaded asset
on demand (`if (!value.IsLoaded() && loadAsset) value.Origin.TryLoadAsset(value)`). Compose is
infrequent (open/recompose, not per-frame) so the re-fetch is cheap. **Diagnosing tip:** log
`TryGet(...).Data?.Length` at register time — if it prints bytes at register but you still crash on
draw, it's this unload race, not a path problem.

**Tinting:** `DrawIcon` forwards the button's color (`Font.Color` for a toggle button) and the
interface method is `DrawSvg(IAsset, ImageSurface, int posx, int posy, int width, int height,
int? color)` — `SvgIconSource` passes `ColorUtil.FromRGBADoubles(rgba)` as that `color`, i.e.
**the SVG is flood-recolored to the button's single color.** So author custom icon SVGs in one
flat neutral color (the ink color comes from code, not the file), multi-color glyphs are not
supported through this path, and per-state hover recolor is free (pass a different `Font.Color`).

**Bonus:** `wpCross` is itself a `CustomIcons` entry (registered in the `IconUtil` ctor) that
vector-draws a cross via `capi.Gui.Icons.DrawCross(ctx, x, y, 4.0, w)` — a clean X with zero art.

See `docs/specs/scribe-icon-svgs.md` (art + wiring) and `docs/specs/lectern-gui-polish.md` item 8.

## Custom button pressed-state and stateful toggles (`GuiElementToggleButton`)

**Symptom: you want a transient "pressed/depressed" look on a custom icon button while it's held,
but overriding `OnMouseDownOnElement`/`OnMouseUpOnElement` to track a `pressed` bool fights the base
class and/or the row's click-yielding.**

`GuiElementToggleButton.OnMouseUp` (decompiled against `VintagestoryAPI.dll`, 2026-07-22)
unconditionally resets `On = false` whenever `Toggleable == false` — on ANY dialog mouse-up, not just
one on this button (this is why Scribe's pin passes `toggleable: true`; see `ScribeHoverIconButton`
ctor doc). Adding your own mouse overrides on top of that, plus the fact that Scribe rows deliberately
*yield* the mouse-down to the overlapping button (`ScribeRowElement.OnMouseDownOnElement` returns
without setting `args.Handled`), makes event-driven press tracking fragile.

**Fix pattern:** compute the pressed look **statelessly at render time** instead of tracking events.
In `RenderInteractiveElements`, draw a translucent overlay when `api.Input.MouseButton.Left` is true
AND `Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)`. `IInputAPI.MouseButton` is a
`MouseButtonState { bool Left, Middle, Right }` (live, polled each frame). This needs no override, and
self-clears the instant the button is released or the pointer leaves the bounds — matching "clears on
release or leave" for free. Bake the overlay as its own `LoadedTexture` (clipped to the same rounded
rect as the button) and blit it over the off/on texture. See `ScribeHoverIconButton`
(`src/Mod/ScribeBlockRowCell.cs`).

**Related — persisting a stateful toggle across recompose:** a custom button's `On` is re-seeded from
the model after each `Compose()` (Scribe seeds `pinButton.On = block.Pinned`). So the toggle only
"sticks" if the click handler mutates the backing model, not just the widget. For an editor-view
toggle that already runs through an autosave path (Scribe's `scratchDocument` + `isDirty` +
`MarkDirty`), you do **not** need a dedicated network message — the whole-document autosave serializes
the flag (codec) and the server's `MarkDirty(redrawOnClient: true)` re-syncs it to other clients'
read view, exactly like the done-toggle. A separate `Toggle*Message` is only needed for a *lock-free*
read-view action that has no editor/autosave to ride (Scribe's `ScribeToggleTaskMessage`). See
`GuiDialogScribeLectern.OnEditViewTogglePin` vs. `OnReadViewToggleTask`.

## Editor row vertical box model (why a gap persists under the input at `RulingPadding = 0`)

**Symptom: you set `RulingPadding = 0` expecting the focused input to sit flush above the ruling, but
a visible gap remains between the input's bottom and the ruling line.** (Playtest 2026-07-22T15-27-35,
item `3b7d714d`.) This is a box-model question, not a single knob — here is the whole vertical stack.

A row's height and the pieces inside it are all in FIXED (unscaled layout) units until draw time. The
bands, top to bottom, for one editor row (`src/Mod/ScribeRowElement.cs` unless noted):

1. **Top pad** — `TopPadFixed = RulingPadding * TextSizeScale` (L~91). Space above the text.
2. **Text** — measured height of the wrapped text (`MeasureWrappedTextHeightFixed`).
3. **Bottom overhead** — `BottomOverheadFixed = RulingPadding*scale + RulingThickness*scale` (L~94):
   the bottom pad PLUS the ruling line's own thickness.

`RowHeightFixed` (L~119) = `max(MinRowHeight, TopPad + textHeight + BottomOverhead)`. A short/floored
row has leftover **slack**; `ContentTopScaled` (L~218) pushes content down by `TopPad + slack/2` so a
single line centers in the row rather than top-anchoring (this is why the checkbox/text sit where they
do, and what `CheckboxGlyphMetricsFixed` mirrors for the grip).

The ruling itself is drawn by `DrawRuling` (L~258) as a Cairo stroke at `y = height - thickness` — i.e.
flush to the row's **bottom edge**, inside the bottom-overhead band.

**The floating edit input** (`GuiDialogScribeLectern.cs` ~L671): its height is set to
`rowHeight - BottomOverheadBandFixed(config)`, where `BottomOverheadBandFixed == BottomOverheadFixed`
(bottom pad + ruling thickness). So the input deliberately stops a whole `BottomOverhead` band ABOVE
the row bottom — that band is the gap you see. **At `RulingPadding = 0` the band is not zero**: it
still contains `RulingThickness * TextSizeScale` (plus the input's own internal text centering within
its bounds). So zeroing `RulingPadding` removes the *pad* but not the *ruling-thickness* slice or the
input's internal vertical centering.

**Levers, if the goal is "input hugs the ruling":** (a) change the input-height subtraction from the
full `BottomOverheadFixed` to just the ruling thickness (keeps the line visible, drops the pad slice);
(b) the input is a single-line box that vertically centers its text, so even a flush box shows padding
above/below the glyph — shrinking the box or top-aligning its text is a separate lever; (c) reintroduce
a small deliberate margin if flush looks cramped. Decide the target look before changing, since these
trade off against the symmetric top/bottom margin the earlier pass added on purpose. No code change was
made for this in the round-3 pass — this note is the writeup to decide against.

## LibGUI (vslibgui) — if/when adopted

LibGUI is a third-party, Flutter-style reactive UI framework (SkiaSharp-rendered) that **has been
adopted** (modid `gui`, production hard dep) as the replacement for our native `GuiComposer` GUI — the
decision was spike-gated in the archived `explore-libgui-adoption` and is GO. The lectern read view
migrated in `adopt-libgui-foundation` and the editor view in `migrate-editor-view-libgui`; the native
`GuiComposer` lectern dialog and its helpers have been deleted. Its model is documented in
`docs/libgui-reference.md`, and the Scribe→LibGUI rebuild plan in `docs/libgui-migration-guide.md`.
Local, gitignored clones exist for lookups: **wiki at `./.wiki/`**, **source at `./reference/vslibgui/`**
— `ripgrep` them before assuming a top-level summary is complete (the wiki and source already
disagree on one Scribe-critical point — `ListView` variable-height rows).

When we resolve a complex LibGUI layout bug or correct a LibGUI misconception, append a note here
(same symptom-indexed style as the rest of this file), so it isn't re-derived. Known facts so far:

**Fact: `GestureDetector.OnTap` fires on `OnPointerClick` regardless of movement.** Decompiled
`Gui.dll`: `OnPointerClick` invokes `OnTap` and marks Handled whenever `OnTap` is set — there is no
built-in drag threshold. `OnPress` similarly marks Handled (which is what captures the pointer). A
grip that must distinguish tap-to-nest from drag-to-reorder therefore cannot trust `onTap` to stay
silent after a drag: start drag only after pointer movement, and suppress `OnGripTap` in our own
state if a drag started that gesture, including a from==to cancel. (refine-crafting-tasks-1-3-2 D11)

**Fact: `PaintingContext.DrawText` does NO font fallback; the `CanvasDrawExtensions.DrawText`
extension does.** A glyph the chosen font lacks (e.g. `←`/`→` in the subsetted Noto Sans/Serif/La
Belle Aurore we bundle) renders as tofu (□) when drawn through the *instance* method
`PaintingContext.DrawText` — decompiled (`Gui.dll`), it resolves one `SKFont` via
`TextLayoutHelper.GetFont` and calls the raw `Canvas.DrawText(text, x, y, font, paint)` overload,
which has no shaping/fallback. The *extension* `context.Canvas.DrawText(...)` (`Gui.Rendering.CanvasDrawExtensions`,
the overload taking `sharedPaint`+`blurFilterCache`) instead goes through `TextShaper.Shape` →
`FontRunSplitter.Split`, which per-code-point falls back via `primary.GetGlyph(cp)==0 ?
SKFontManager.Default.MatchCharacter(...)`. `TextLayoutHelper.MeasureText` also shapes (so *measures*
already reflect fallback even where the *draw* shows tofu — a draw/measure mismatch). The stock
`Text`/`RenderText` read view uses the extension, so it falls back automatically; our custom
`ScribeMultilineFieldRender` used the instance method, so it didn't. **Fix pattern:** for a
mod-controlled draw path that needs a specific/deterministic fallback (not whatever `MatchCharacter`
picks from the OS), split the string into runs and draw the missing-glyph run in an explicitly chosen
family — LibGUI bundles **Cormorant Unicase** (has both arrows), always present via the `gui` dep. See
`src/Mod/ScribeGlyphFallback.cs` (redirects only an unrenderable `←`/`→` to Cormorant, measures each
run in its draw family so the caret stays aligned, single-draw fast path when no redirect is needed).

**Fact: `ListView` supports variable-height rows despite the wiki saying otherwise.** The wiki's
*Scrolling* page shows only uniform `itemHeight` ("all items must have the same height"). The source
(`reference/vslibgui/Gui/Gui/Widgets/Scroll/ListView.cs:44` and `:88`) has `estimatedItemHeight` +
`variableHeight: true` constructors backed by an `ItemHeightCache`. Scribe's *display* rows can use
this; but see the two facts below for why editable rows are a different story.

**Fact (spike, 2026-07-23): `TextField` is SINGLE-LINE. LibGUI has no multi-line text input.**
`RenderTextField` (`reference/vslibgui/Gui/Gui/Core/Input/RenderTextField.cs`) measures a single line
(one `MeasureText` + one `lineHeight`), does no newline/soft-wrap handling, and exposes no
`maxLines`/`multiline` flag. Its caret is hardcoded `Vector4.One` (white) — `TextFieldStyle` and
`TextFieldOverrides` have no caret-color field. Scribe task rows paint `OnSurface` carets via
`ScribeMultilineField`; a stock `TextField` (the `ScribeNumericField` typing box) cannot match that
without an overlay or a custom one-line renderer. Left content inset is also hardcoded 10px.
`MaxLines` exists only on the *read-only* display widgets (`Text`,
`VtmlText`, `RichText`), not the editable `TextField`. So Scribe's core interaction — a wrapping,
growing, editable task/note row — is NOT achievable with the stock LibGUI input widget. Adopting
LibGUI would require **building a custom multi-line editable RenderObject** (the same
from-scratch text-editing work the native GUI already solved in `ScribeRowTextInput`), which
materially changes the "LibGUI gives us editable rows for free" premise. This is the biggest single
finding of the spike; it does not fail gate A/B/C/E but it reframes the migration cost.

**CORRECTION (spike, 2026-07-23): interactive widgets DO work inside a `ListView`.** An earlier
note here claimed they didn't (theory: the list's scroll `GestureDetector` swallows the press).
An in-game probe disproved it — a `TextField` inside a `ListView` took focus, typed, and supported
mouse text-selection. Do not repeat the wrong claim.

**Fact (spike, 2026-07-23): a `ListView` caches its child widgets by index and does NOT rebuild
them on a parent `SetState`.** `ListViewContentElement` only clears `_cachedWidgets` when the data
identity or item count changes (`ListView.cs` `Update`, ~`:462`). A probe with a `Checkbox`/`Button`
using the parent-owned *controlled-component* pattern (value/onChanged from parent state) showed the
child never updating — the button's click counter stayed at 0 and the checkbox wouldn't toggle,
even though the taps registered (animation + sound fired). **Fix pattern:** interactive children of a
`ListView` must own their own state (be `StatefulWidget`s that `SetState` themselves), OR the list's
data identity/count must change to force a rebuild. This matters directly for Scribe's editable row
list, where each row is both scrollable content and an interactive field.

**Fix pattern: a `TextField` with a `BoxStyle` that sets no `Height` collapses to a thin line.**
`RenderTextField : RenderConstrainedBox`; with no child and no explicit `Height`, `PerformLayout`
sizes to 0 (`RenderConstrainedBox.cs:104-117`). Always give a `TextField`'s `BoxStyle` an explicit
`Height` (the showcase uses 35). The same collapse happens on **width** when the parent loosens
constraints: `RenderBox.PerformLayout` (every `Container`/`BoxWidget`) and `RenderStack` both lay
out children with `LayoutConstraints.Loose`. `TextFieldStyle.Width` is not copied onto the inner
`BoxStyle`, so a `TextField` inside `Expanded(Container(...))` becomes 0px wide — empty chrome,
clipped text, nearly unclickable — while the Container still fills the Expanded slot. Keep the
`TextField` under a parent that forwards tight width (`Expanded` directly, or a `RenderProxyBox`
wrapper). Do not put a `Container` between `Expanded` and the field.

**Fix pattern (custom editable widget): set `FocusNode.Owner = Element` in `InitState`, or the field
never focuses.** `FocusNode.RequestFocus()` resolves its `FocusManager` via `Owner?.Owner?.FocusManager`
(`Focus.cs`). A custom `StatefulWidget` field that creates its own `FocusNode` but doesn't assign
`Owner` silently fails to focus — and since key handlers gate on `HasFocus`, nothing types. LibGUI's
own `TextFieldState.InitState` sets `FocusNode.Owner = Element`; mirror that.

**Fact (spike, 2026-07-23): LibGUI text fields LEAK keypresses to the game (WASD moves the player,
E opens inventory while typing).** `GuiBase.OnKeyDown` only stops a key reaching the game if the
focused widget marks the `KeyboardEvent` `Handled` (`GuiBase` does `args.Handled |= e.Handled`). But
`TextFieldState.OnKeyDown` has **no `default:` catch-all** — letter/movement keys are inserted via the
separate `OnKeyChar`/KeyPress path, so their *KeyDown* passes through unhandled and the game consumes
it. This affects LibGUI's *own* `TextField` too, not just our custom field. **Two fixes:** (a) blunt —
override `CaptureAllInputs() => true` on the dialog (VS default false; not overridden by `GuiBase`) so
the dialog swallows all input while open — simplest for a text-editing dialog; (b) precise — in the
field's `OnKeyDown`, when focused, mark `e.Handled = true` for any key that should be "consumed by the
editor" (i.e. a `default:` that swallows). Scribe's native `GuiDialogScribeLectern`/`ScribeRowTextInput`
already solved this class of problem; the real migration must reproduce it.

**Gotcha (fix-settings-numeric-arrow-focus-leak, 2026-08-02): `CaptureAllInputs() => true` makes a
dialog steal keys even when it is NOT the focused dialog — gate it on `Focused`.** `GuiManager.OnKeyDown`
(VintagestoryLib) dispatches a keydown in TWO passes: **first** over every open dialog whose
`CaptureAllInputs()` is true (breaking on the first that marks the key `Handled`), and **only then** the
normal `ShouldReceiveKeyboardEvents()` (`=> Focused`) pass. So a `CaptureAllInputs`-returning dialog
pre-empts keyboard input ahead of whatever dialog the player is actually focused on. This bit us with two
open dialogs: the standalone Scribe Settings window on top, and a document editor behind it. Each LibGUI
`GuiBase` owns its **own** `FocusManager` (instance field), and `capi.Gui.RequestFocus(settingsDialog)`
(fired when the settings field is clicked) `UnFocus()`es the editor at the VS-dialog level but never
touches the editor's LibGUI focus — so the editor's row still reports `HasFocus == true` in the editor's
private manager. With the editor's `CaptureAllInputs()` keyed only on that `HasFocus`, the editor grabbed
the settings window's Up/Down arrows in the first pass and drove its row caret. Fix: gate
`CaptureAllInputs()` on `Focused` so a non-active dialog never pre-empts input. This does NOT weaken the
capture's real job (blocking movement/hotbar keys from the game while typing), which only matters when the
editor IS the focused dialog. General rule: `CaptureAllInputs()` should mean "capture input **while I am
the active dialog**," never unconditionally.

**Watch: macOS is a single `osx` RID in LibGUI's native loader** (`NativeLibraryLoader.cs`) — no
`osx-arm64` split for the bundled HarfBuzz `.dylib`. Same class of native-render risk that makes
VSImGui dead on Apple Silicon (see the VSImGui section above). The spike's primary gate is "does it
render on this Mac at all."

**Fact (adopt-libgui-foundation): a `ListView`'s child cache only clears on item-count (or data-
identity) change — a plain parent rebuild does NOT rebuild the rows.** `ListViewContent.Update`
(`reference/vslibgui/Gui/Gui/Widgets/Scroll/ListView.cs:456`) clears `_cachedWidgets` only when
`DataIdentity` changes by reference OR `ItemCount` differs; the stock `ListView` constructors never
set `DataIdentity`, so it's item-count-only in practice. Consequence for an already-open read view
that must reflect an EXTERNAL state change (another viewer toggled a task, an editor autosaved):
`SetState` on the parent is not enough — the same-count row at that index keeps its cached widget.
Two options that DO work: (a) make each row a self-stateful `StatefulWidget` keyed by `ValueKey` so
it owns and flips its own state (what Scribe's rows do, for the local-click case); (b) for a full
external resync, call `GuiBase.ForceRebuild()` — it unmounts the whole tree and rebuilds from
scratch, so every row is recreated from current data. Scribe's `RefreshReadView` uses `ForceRebuild`.

**Fact (adopt-libgui-foundation): `Gui.Widgets.Framework.Key` collides by simple name with a VS
`Key` type in scope.** In a Scribe dialog file that `using`s the VS client namespaces, a bare `Key?`
parameter resolves to the wrong `Key` and fails to convert to/from `ValueKey<int>`. Fully-qualify
the widget key type as `Gui.Widgets.Framework.Key?` on any ctor that forwards a key to a widget base.

**Fix pattern (adopt-libgui-foundation): XML-comment `--` breaks `.csproj`.** MSBuild rejects `--`
(and a trailing `-`) inside an XML `<!-- -->` comment (`error MSB4025: An XML comment cannot contain
'--'`). The C# double-dash-as-em-dash habit from our `.cs` doc comments does not carry over to
`Mod.csproj` — use `:` / `;` / a single `-` there instead.

**Fact (adopt-libgui-foundation): `Private=false` keeps a referenced DLL out of `bin/`, so the
blanket `*.dll` stage/package copy never ships it.** Verified for the `gui` hard dep: with
`<Private>false</Private>` on the `Gui` reference, `Gui.dll` is absent from
`src/Mod/bin/{Debug,Release}/net10.0/` and therefore from the staged Mods folder — the installed
`gui` mod provides it at runtime, exactly like the game DLLs and ConfigLib. Don't add the LibGUI
DLLs to any explicit ship list.

**Symptom (migrate-editor-view-libgui): a focused editable row loses focus/caret when it scrolls
(or grows) off-screen inside a `ListView`.** LibGUI's `ListView` **virtualizes** — its private
`ListViewContentElement.UpdateVisibleItemsVariable` mounts only rows in `[firstVisible-1,
lastVisible+1]` and calls `Unmount()` on the rest, which destroys their `Element`/`State` and thus
their `FocusNode`. Fine for the display/read view; fatal for an editor, where (a) cross-row keyboard
nav (Enter→next row) needs to `RequestFocus` a row that may be off-screen, and (b) a focused row that
grows past the viewport would be unmounted mid-type. **Fix:** render the editor as a NON-virtualized
`SingleChildScrollView` + `Column` of ALL rows (every row stays mounted, `FocusNode`s persist). A
lectern doc is a small checklist, so non-virtualized costs nothing. The read view keeps `ListView`.

**Fact (migrate-editor-view-libgui): LibGUI's `KeyboardEvent` drops the Command (⌘) modifier — only
Shift/Ctrl/Alt survive.** `GuiBase.OnKeyDown/OnKeyPress` build the LibGUI `KeyboardEvent` from VS's
`KeyEvent` passing only `shift/ctrl/alt` (decompile `src/Mod/lib/Gui.dll` → `Gui.GuiBase.OnKeyDown`
with `ilspycmd -t Gui.GuiBase src/Mod/lib/Gui.dll`: `_inputRouter.KeyDown(args.KeyCode,
args.ShiftPressed, args.CtrlPressed, args.AltPressed)` — `CommandPressed` never passed); VS's own
`KeyEvent.CommandPressed` is never propagated. So a LibGUI widget cannot see Cmd, and the macOS caret
idioms (Cmd+←/→ = line ends, Cmd+A/C/X/V) can't be handled inside the field. **Fix:** translate Cmd
one layer up, in the dialog's `public override void OnKeyDown(KeyEvent args)` — the VS `KeyEvent` is
**mutable** (`KeyCode`/`CtrlPressed`/`CommandPressed` all have setters), so rewrite Cmd+←/→ →
Home/End and Cmd+{A,C,X,V} → Ctrl+{A,C,X,V}, clear `CommandPressed`, THEN call `base.OnKeyDown(args)`
(which does the mapping). Alt/Option *is* delivered as `Alt`, so Alt+Arrow word-skip works in the
field directly. (Mirrors the native `ScribeRowTextInput.TranslateMacCaretModifiers`, moved up a level.)

**Extension (scroll-follow-caret-in-editor §7, 2026-08-13): the same seam gives macOS Cmd+Up/Down =
document top/bottom.** `ScribeDialogBase.OnKeyDown` now also rewrites **Cmd+Up/Down → Ctrl+Up/Down**
(keeping the Up/Down key code, setting `CtrlPressed`, clearing `CommandPressed`). The field's Up/Down
(and Home/End) handler gates the first/last-row jump on **`e.Ctrl` alone** — NOT the `Ctrl || Alt`
word-jump gate that Left/Right use — so Alt/Option+Up/Down stays a plain one-line move (macOS
paragraph-nav is a line move here), while Windows Ctrl+Up/Down / Ctrl+Home/End and macOS
Cmd-remapped-to-Ctrl all jump. Confirms the general rule: **any Cmd-based gesture is reachable at the
dialog's raw-`KeyEvent` layer even though the field can't see Cmd — no `gui` fork needed.** (Note the
old `reference/vslibgui/` clone was deleted 2026-08-12; decompile the vendored `src/Mod/lib/Gui.dll`
for ground truth, not that path — several older citations in this file still name it.)

**Fact (migrate-editor-view-libgui): no focus-traversal API — a parent coordinates focus manually.**
`FocusManager` tracks a single `PrimaryFocus` and offers only `RequestFocus(node)` / `RequestFocus(null)`;
there is no next/previous traversal, `FocusScope`, or sibling/parent links on `FocusNode`. To move
focus across editor rows (Enter/Shift+Tab), the dialog owns one `FocusNode` per row and calls
`RequestFocus` on the target. `FocusNode.RequestFocus()` resolves its manager via `Owner?.Owner?.FocusManager`,
so a node must have its `Owner` set (to the widget's `Element`) before focus takes — a node whose
`Owner` is unset silently never focuses (this also gives you `node.Owner` as the row's `Element` for
`Scrollable.EnsureVisible`).

**Fact (migrate-editor-view-libgui): keep-a-row-in-view is `Scrollable.EnsureVisible(Element)`, called
AFTER layout.** `Gui.Widgets.Scroll.Scrollable.EnsureVisible(Element target, ...)` (public static)
walks up to the nearest scrollable ancestor and jumps/animates its `ScrollController` so the target is
fully visible. It reads the target's live post-layout geometry, so call it once layout has run for the
new size — deferring to the dialog's `OnRenderGUI(deltaTime)` (after `base.OnRenderGUI`) works. Reach
the row's `Element` via its owned `FocusNode.Owner`.

**Fact (migrate-editor-view-libgui): clipboard + selection are public; use `context.GetClipboard()` and
`PaintingContext.DrawBox`.** A widget reads/writes the system clipboard via
`BuildContext.GetClipboard()` / `Element.Owner!.GetClipboard()` → `IClipboard.GetText()/SetText()`
(the concrete `GameClipboard` is `internal`, but the accessor is public). There is no dedicated
selection-rect draw call — draw the selection highlight as a filled `PaintingContext.DrawBox` behind
the text (same as the internal `RenderTextField`). There is also no public word-wrap helper
(`TextLayoutHelper.BreakIntoLines` is `internal`); wrap by splitting on `\n` and greedily measuring
words with the public `TextLayoutHelper.MeasureText`.

**Fact (add-lectern-row-affordances-libgui): `IconButton`/`Icon` load SVGs by PATH and will fail to
draw our custom icons; use `VsIcon` (icon-by-CODE) instead.** LibGUI's `Icon` (and therefore
`IconButton`, which only accepts an `Icon`) resolves its SVG via `SkiaAssetLoader.LoadSvg(domain, path)`
(`reference/vslibgui/.../Rendering/SkiaAssetLoader.cs:82`), which calls `Assets.TryGet(loc)` WITHOUT
`loadAsset: true`. VS nulls out every non-patched asset's `.Data` after startup (`AssetManager.UnloadAssets()`
— see the "Icon-button glyphs" note above), so that lookup returns null `Data` and the icon silently
fails to render. `VsIcon(iconName, size, color)` (`Widgets/Basic/VsIcon.cs`) instead routes through
`IconUtil.DrawIconInt` → the `CustomIcons[code]` delegate, i.e. the mod's own self-healing registration
(`ScribeModSystem.RegisterSvgIcon`, which re-resolves the asset on every draw). **Fix pattern:** for a
registered `scribe*` glyph, use `VsIcon` by code; for a clickable one build `GestureDetector + VsIcon`
yourself (see `ScribeHoverVsIcon`/`ScribeVsIconGlyph` in `GuiDialogScribeLecternLibGui.cs`) rather than
reaching for `IconButton`.

**Fact (add-lectern-row-affordances-libgui): a row-level `MouseRegion` is the right hover primitive —
it neither steals inner clicks nor breaks during a drag capture.** `Element.HitTest`
(`Widgets/Framework/Element.cs:243`) builds the hit path innermost-first and `EventDispatcher.FindTarget`
returns the FIRST (innermost) active target, so an inner field's `GestureDetector` still wins a click even
though an outer row `MouseRegion` is also "active" — the outer region only receives enter/exit (dispatched
UP the hierarchy via `DispatchToHierarchy`). Crucially, `DispatchPointerMove` keeps running normal
enter/exit hit-testing on the pointer's location even while another element holds pointer capture
(`EventDispatcher.cs:141`, the `_dragHoveredElement` block) — so during a grip-drag the row under the
cursor still fires `onEnter`, which is a robust, scroll-correct drop-target signal needing NO
`GlobalToLocal`/content-space-Y math. **Fix pattern:** hover-reveal AND drag-over drop targeting can share
one row-level `MouseRegion.onEnter`; keep the drag-handle itself always-mounted (don't hover-gate it) so
the captured element isn't unmounted mid-drag. See `ScribeEditRowState` + `ScribeLecternEditorContentState`.

**Symptom (add-lectern-row-affordances-libgui): moving the mouse over an editor row reverts its
in-progress (unsaved) text to the last-committed value — yet switching to read view shows the NEW text.**
Reconciliation is by (runtime type + `Key` + sibling position): `Widget.CanUpdate`
(`Widgets/Framework/Widget.cs:75`) returns true only when `oldWidget.GetType() == newWidget.GetType()`
and keys are equal; `Element.UpdateChild` (`Widgets/Framework/Element.cs:205`) otherwise **unmounts the
old subtree and mounts a new one**. So if a `SetState` (e.g. hover toggling `hovered`) makes a build swap
a child's widget TYPE at a given position — here the row flipped `MouseRegion.child` between a bare
`Padding` (rowBody) and a `Stack` — the whole subtree under it is torn down and rebuilt, destroying any
`State` in it. For an editable field that means its `ScribeMultilineFieldState` (which holds the live
`text`/caret) is disposed and a fresh field mounts, re-seeded from the STALE `InitialText`/`Data.Text`
snapshot — the field writes through per keystroke via `OnChanged` but deliberately does NOT rebuild the
editor, so the scratch document is correct (read view is right) while the remounted field shows old text.
**Fix pattern:** keep the widget tree STRUCTURALLY STABLE across state changes that shouldn't remount a
stateful descendant — a stable child must keep the same type AND sibling index every build. Make wrappers
unconditional (always `Stack`, always `Container` even with a transparent fill) and let only leaf/trailing
children (the hover buttons) mount/unmount. Give a stateful widget a stable `Key` if its position can
shift. See `ScribeEditRowState.Build` (the "STRUCTURAL STABILITY" comment).

**Symptom (add-lectern-row-affordances-libgui): a single-line row is ~2 logical px taller in the editor
view than in the read view, and a scroll offset restored across a view switch lands a bit off.** The read
view draws text with LibGUI's stock `Text` widget, whose `TextStyle` DEFAULTS to `FontFamily = "sans-serif"`
(`Rendering/Text/TextStyle.cs` ctor). Our custom `ScribeMultilineFieldRender` measured and drew with
`fontFamily: ""`. Both compute single-line height with the SAME formula — `metrics.Descent - metrics.Ascent
+ metrics.Leading` (`RenderText.PerformLayout:58` and `TextLayoutHelper.MeasureText:165`) — so the height
difference wasn't the formula; it was that `""` and `"sans-serif"` resolve (via `FontRegistry.ResolveFontFamily`,
which falls back to the literal name as a system-font lookup) to DIFFERENT typefaces with different metrics.
A couple-px per-row delta compounds down a list, and because the cross-view scroll restore preserves a raw
PIXEL offset (`ScrollController.JumpTo`), mismatched row heights also make the restored offset land on a
slightly different row. **Fix pattern:** any custom text RenderObject that must visually match a stock `Text`
MUST use the same `FontFamily` string for both `MeasureText` and `DrawText` — mirror `TextStyle`'s default
(`"sans-serif"`), never `""`. See `ScribeMultilineFieldRender.FontFamily`.

**Fact (peg-task-fonts-to-caudex, 2026-08-27): selectable task fonts do not share a Skia line-box at the
same nominal point size.** `TextLayoutHelper.MeasureText("Ag").Y` is `metrics.Descent − metrics.Ascent +
metrics.Leading`, so Scapholene / La Belle Aurore / Noto / Playfair / Default `sans-serif` produce
different input-row heights than Caudex (the face the Read/Edit geometry was locked against).
**Fix pattern:** layout height is always Caudex's line-box at the *nominal* window size
(`ScribeTaskFont.LineHeight`). Stock `Text` must use `LayoutSize` (auto `caudexY / familyY` only) in
`TextStyle.FontSize` — never `OpticalScale`. Optical size is a paint-only `Transform.Scale` inside
`OffsetWrap` (plus `OffsetEm` translate). Custom painters (`ScribeMultilineField`) draw at
`EffectiveSize` (= layout × optical) but still reserve `LineHeight` per line. Putting optical into
`FontSize` makes LibGUI report a taller box (La Belle Aurore at 2× grew Edit craft rows).
`ScribeRowControlNudge.TextLineHeight` must NOT measure the selected family. Default/`sans-serif` is
included. Tablet cuneiform stays on `CuneiformMetrics`; titles/buttons stay unscaled Caudex
(`ButtonFamily` / `TitleFontFamily`). The pinned HUD keeps its own face and is not pegged. Settings
chrome uses LibGUI `sans-serif` at 100% (`WrapSettingsChrome`) and does not follow Task Text Font or
Window Text Size. Call `BuildMetrics` once after `RegisterCustomFonts`. Tune `OpticalScaleOf` /
`OffsetEmOf` with `tools/task-font-optical-scale/index.html`.

**Symptom (a05caret1): clicking a per-row control (delete / pin / drag grip) while a text field is focused
makes the caret vanish — focus is lost and nothing re-homes it.** LibGUI clears focus on EVERY pointer
press whose hit path contains no focusable element: `EventDispatcher.DispatchPointerDown`
(`reference/vslibgui/Gui/Gui/Widgets/Gestures/EventDispatcher.cs:249`) does
`if (!hasFocusTarget) root.Owner?.FocusManager?.RequestFocus(null);`, where `hasFocusTarget` is true only
when some element on the path is `IFocusable` (or its `State` is). A control built from `GestureDetector`
+ `VsIcon` is NOT `IFocusable`, so pressing it blurs the focused field on pointer-DOWN, before its
`onTap`/`onRelease` (which fire on pointer-UP) ever run. The blur happens regardless of what the tap then
does — so any handler that doesn't explicitly re-grant focus leaves the caret gone. Three faces of the one
bug: delete rebuilt without re-arming focus; pin didn't rebuild locally at all (its repaint arrives later,
async, via the server pin-set push → `OnMyPinsChanged`); reorder's release-in-place (`from == to`)
early-returned. Note `DispatchPointerUp` has no focus logic, so an `onRelease` handler CAN safely re-grant.
**Fix pattern:** any control that sits over a focusable field and isn't itself `IFocusable` must re-home
focus in its handler — either `FocusNode.RequestFocus()` directly (no rebuild) or, across a `ForceRebuild`,
set the dialog's one-shot `autoFocusRowOnRebuild` to the row that should keep/receive the caret. A blur does
NOT clear our `focusedEditIndex` (its listener fires only on focus GAINED), so that field still names the
row to restore. See `DeleteEditorBlock`, `TogglePinnedEditorTask`/`OnMyPinsChanged`, and `ReorderEditorBlock`
in `GuiDialogScribeLecternLibGui.cs`.

**Symptom (94c447c8, "mass-delete dead first-click"): tapping a delete/pin control on a row that is
sliding under a stationary cursor mid-collapse does nothing; the click only registers once the animation
finishes.** A LibGUI tap fires only when the element re-hit-tested at pointer-**up** is the SAME element
captured at pointer-**down** — `EventDispatcher.DispatchPointerUp` gates `OnPointerClick` on
`if (hit == target)`. During a collapse the target row moves upward every frame, so between mouse-down and
mouse-up the control slides out from under the stationary cursor, `hit != target`, and the tap is silently
discarded; a second click after geometry settles hits the same element down/up and works. This is a
moving-target hit-test race, NOT the departing ghost-snapshot intercepting the click (the frozen ghost has
no gestures) — an earlier hypothesis the source disproved. **Resolution:** the
`reconcile-animating-surfaces` conversion fixed it as a side-effect — reconcile keeps the row list stable
(no per-frame remount that the old `ForceRebuild` did), so the row under the cursor holds its identity
across the down→up and `hit == target` holds. Confirmed in-game 2026-08-10 (playtest 2026-08-10T09-02-17).
The parked narrow fallback change (`fix-mass-delete-click-target`, which would have made the control
activate on a moving target directly) was retired unused when reconcile shipped.

**Symptom (0.2.0 title-pencil): clicking a button crashes with `NullReferenceException` in
`ButtonState.PlaySound` (`Button.cs:109`, shipped `gui@3.1.0`), reached from
`GestureDetector.OnPointerDown` → `SetState` → `PlaySound` → `base.Element.Owner.GetSoundPlayer()`.** The
null is `Element`/`Owner`, NOT the sound player (`GetSoundPlayer()` *throws `InvalidOperationException`*
when the player is unset). Cause: a handler called `ForceRebuild()` **and then a follow-up mutation
(`FocusNode.RequestFocus()`) synchronously, from inside the pointer-down dispatch.** `GuiBase.ForceRebuild`
does `RootElement.Unmount()` on the WHOLE tree and remounts a fresh one; doing that mid-dispatch orphans
the sibling buttons still queued in `EventDispatcher.DispatchToHierarchy`'s pointer-down walk, so when the
walk reaches one its `_isPressed=true; PlaySound()` runs against a now-null `Owner`. Note none of your own
code appears on the crash stack — it's entirely inside `gui.dll` input dispatch — but your rebuild-from-a-
tap is the trigger. A plain `ForceRebuild()` from a tap (e.g. a tab switch) is USUALLY survivable; the
crash showed up when the tap ALSO re-homed focus onto the freshly-remounted node in the same handler.
**Fix pattern:** never mutate the tree from inside a pointer handler at all — **the `ForceRebuild()` itself
is the hazard, not just the focus call.** Defer BOTH out of the tap: arm one-shot pending flags, and from the
next `OnRenderGUI` (a safe post-dispatch point) first `ForceRebuild()` and then, on a later frame,
`RequestFocus()` once the rebuilt field has mounted (`FocusNode.Owner is not null`). See
`_pendingTitleEditRebuild` + `_pendingTitleFocus` in `ScribeDialogBase.cs` — mirrors `pendingEmptyRowRemoval`
and the collapse sweeps that defer tree edits out of animation/notification callbacks for the same
re-entrancy reason.
**First-pass mistake (2026-07-31, do not repeat):** an earlier fix deferred ONLY the `RequestFocus()` and
still called `ForceRebuild()` synchronously in the tap, reasoning the rebuild alone was survivable. It is
NOT when the tap is on a widget whose rebuild changes the sibling layout: `ForceRebuild` does
`RootElement.Unmount()` on the WHOLE tree, so any sibling button still queued in the in-flight pointer-down
walk is orphaned (`Element.Owner` → null) and NPEs in `PlaySound`. This reproduced reliably on a Clockmaker's
Notebook crafted FROM a Notebook (the carried-over title changes the title-row build → the orphaned-sibling
timing hits every time), where a blank notebook had masked it. Note a `ForceRebuild()` from a tap that does
NOT re-home focus or reshape siblings (e.g. a plain tab switch) is usually survivable — the crash needs the
rebuild to disturb a sibling that's still mid-dispatch — but the safe rule is simply: don't touch the tree in
a pointer handler; defer it.
**Second-pass mistake (2026-07-31, same crash on a THIRD path — do not repeat):** after the tap was
fixed, the identical `PlaySound` NPE reappeared on **UN**focusing the title. Cause: the title's
`FocusNode` listener (`OnTitleFocusChanged`) committed on blur and then called `ForceRebuild()`
synchronously. A blur listener fires as a *side-effect* of the pointer dispatch of whatever button
STOLE the focus — i.e. from inside that button's in-flight pointer-down walk — so the rebuild orphaned
the clicked button before its own `PlaySound` ran. Fix: arm the SAME `_pendingTitleEditRebuild` flag from
the listener (commit inline, defer only the rebuild). **General rule this nails down: it is not just
`onTap` handlers that are unsafe — ANY callback reachable synchronously from pointer dispatch is, including
`FocusNode`/`onFocusChange`/`onBlur` listeners that a click triggers indirectly.** Contrast with a
button's OWN `onTap`: LibGUI's `Button` plays its click sound BEFORE invoking your `OnTap` (`Button.cs:71`),
and dispatch stops on that handled tap, so a `ForceRebuild()` in a nav-button handler (our `EnterReadMode`
etc. do `CommitTitleIfEditing()`+`ForceRebuild()`) is safe — the crash needs a rebuild that disturbs a
sibling STILL queued in the walk, which is exactly the indirect-blur case.
Related: our own custom fields self-focus safely from their `InitState` via an `autoFocus` param
(`ScribeMultilineField`), which is post-mount; the stock `TextField` has no such param, so a dialog-owned
node must be focused this deferred way instead.

**Fact (scribe-themed-toggle): a per-dialog theme = wrap the dialog's `Build()` output in
`new Theme(themeData, child: …)`; `GuiBase` gives no theme override hook.** `GuiBase.BuildRootTree`
always wraps content in the global `Theme(ThemeData.Default…)`, and exposes no way to override it. The
supported switch is to wrap a dialog's OWN `Build()` output in `new Theme(chosenThemeData, child)`: every
descendant that reads `Theme.Of(context)` (rows, fields, buttons, the settings form) recolors, because
`Theme.UpdateShouldNotify` compares `ThemeData` **by reference** (`Theme.cs:645`) — so passing a
*different instance* plus a rebuild recolors with no teardown. Read the theme flag fresh each `Build()`
(like `WindowFontScale`) so a `ForceRebuild` on the settings-changed event relights live. See
`ScribeTheme.For(bool)` and the wraps in `GuiDialogScribeLecternLibGui`, `ScribeSettingsDialog`,
`HudScribePins`.

**Fact (scribe-themed-toggle): `ColorScheme.Default()` is the ONLY preset LibGUI ships, and it is DARK
(parchment) — but `ThemeData.Default` is the PLAYER'S GLOBAL theme, not that constant.** LibGUI's
`GuiModSystem.LoadThemeConfig` sets `ThemeData.Default` from the player's `libgui.json`
(`reference/vslibgui/.../GuiModSystem.cs:277`), falling back to `ColorScheme.Default()` only when the
player authored no theme. So returning `ThemeData.Default` for an "off" toggle means "follow my global
game theme," NOT "force stock dark." A light theme is net-new — you author all 17 `ColorScheme` roles
yourself (`reference/vslibgui/.../Framework/Theme.cs:83`). The per-widget style structs (`ButtonStyle`,
`CheckboxStyle`, `DropdownStyle`, …) cascade from the scheme via their `Default(colors, …)` factories in
the `ThemeData` ctor, so you only author the scheme, not the structs. Two roles need *semantic*, not
mechanical, inversion when going light: `StateHover`/`StateSelected` are translucent overlays that must
DARKEN a light surface (dark ink tint at low alpha) where the dark theme lightens; and keep
`SurfaceHigh` lighter than `SurfaceLow` (raised vs recessed) rather than blindly swapping the dark
values. See `ScribeTheme.Light`.

**Fact (scribe-themed-toggle): two things do NOT follow the `Theme` wrap and must be set explicitly.**
(1) `WindowFrame`/`WindowTitleBar` read `ThemeData.Default.ColorScheme` at CONSTRUCTION
(`WindowTitleBar.cs:231-233`), not from context — so a themed dialog must pass explicit
`titleBarColor:`/`textColor:` (`Vector4?` params) computed from the active scheme, or the title bar
stays stuck on the dark default while the body goes light. (2) A bare `new Text(...)` defaults to white
(`TextStyle` default `Color = Vector4.One`) and would vanish on a light surface — every Scribe text
widget already passes an explicit theme color, so only NEW bare text is at risk.

**Scoping note (scribe-themed-toggle, 2026-07-25): the toggle themes the Lectern ONLY.** Only the Lectern
dialog is wrapped in `ScribeTheme.For(pixelArt)`; the pinned-task HUD and the standalone settings window
are deliberately left UNWRAPPED so they always follow the player's global theme. An early build wrapped
the HUD too (and inverted its glow halo per the flag) — that was wrong and was removed; the HUD keeps its
original dark glow halo constant since it always renders on the (light-text) global theme. There is one
settings window (`ScribeSettingsDialog`, owned by `ScribeModSystem.OpenSettings()`), opened by both the
Lectern gear and the HUD gear.

**In-game legibility verdict: CONFIRMED 2026-07-25** (playtest submissions 2026-07-25T20-12-27 and
21-06-50): the light Lectern reads as dark ink on light parchment with a legible light title bar and no
white-on-light text; OFF falls back to the global theme; the toggle relights the open Lectern live and
persists across a relog; the HUD and settings window correctly do NOT change with the toggle; both gears
open the one settings window.

**Fact (scribe-gui-backdrops): draw a dialog backdrop with `Container` + `BoxStyle.Texture`, and SELF-LOAD
the bitmap.** A `Container`/box paints its fill + texture BEFORE its child (`RenderBox.PaintInternal` →
`DrawMaskedBox`, then `PaintChildren`), so wrapping a view's body in `new Container(style: new BoxStyle {
Texture = bmp }, child: body)` puts the art behind the content automatically — no `Stack` layer needed.
The child stays fully interactive over it. **Do NOT use the `Image` widget or `SkiaAssetLoader.LoadBitmap`
for this:** both call `TryGet(loc)` WITHOUT `loadAsset: true`, so the bytes are null after VS unloads
assets post-startup (the same trap the SVG icon loader hit) and the backdrop silently vanishes in normal
play. Self-load exactly like `ScribeModSystem.RegisterSvgIcon`: `capi.Assets.TryGet(loc, loadAsset: true)`
then `SKBitmap.Decode(asset.Data)`. **Cache the decoded `SKBitmap` (AND a null miss) on the mod system,
not the dialog** (`ScribeModSystem.GetBackdropBitmap` / `backdropCache`): the bitmap is immutable and
shared across every open, so caching per-dialog would re-decode each open and risk one dialog disposing a
bitmap another open still references. Dispose all cached bitmaps in `ModSystem.Dispose()`; a dialog must
NEVER dispose a backdrop bitmap. Caching the null miss makes an unloadable asset warn exactly once (not
per frame/open) and fall back to a flat placeholder `BoxStyle { Color = … }` — so the whole dialog
structure is testable in-game before any PNG exists (flat-color-first). **Filtering:** `BoxStyle.Texture`
(and `Image`) filter BILINEAR (`SKFilterQuality.Medium`) — smooth downsample (crisp for ink art authored ≥
on-screen size), but BLURS upscaled hard pixel art. `NineSliceBox` is the only nearest-neighbor (crisp)
path; use it for framed pixel chrome, not full-spread backdrops. Gate the wrap on the `PixelArtDisplay`
preference read fresh each `Build()` (OFF = body bare, no wrap); the `UpdateMySettings` → `MyPinsChanged` →
`ForceRebuild` chain relights it live for free.

**Fact (scribe-pin-editor): add a new central-region view to the Lectern dialog as a PEER of read/editor —
a view-mode switch in `BuildCentralRegion`, not an overlay.** The dialog already chose its body from a
`bool isEditorMode`; a third view (the Pin Tab) is cleanest as a small enum (`Read`/`Editor`/`Pinned`) with
`isEditorMode` kept as a bool *property* over it (`get => view == Editor; set => view = value ? Editor :
Read`) so all the editor-lifecycle code that flipped the bool keeps working untouched, and
`BuildCentralRegion` becomes a `switch`. Route the nav button through a real entry method
(`OnClickSwitchToPinned`) that tears down the editor first (flush + release lock, like
`OnClickSwitchToRead`) — never an inline flag flip — matching the `RequestEditorAccess`/`EnterReadMode`
discipline. The new view reuses the editor's `[grip][checkbox][field]` + hover delete/unpin row shape but
feeds it from an alternate row-data source (`ScribePinnedRef` → `ScribePinRowData`) instead of the scratch
document.

**Fact (scribe-pin-editor): `ForceRebuild` FULLY unmounts + remounts the tree, so keying a row by
`ValueKey<Guid>(TaskId)` does NOT by itself preserve its field's live text — you also need a write-through
buffer to re-seed from.** `GuiBase.ForceRebuild` (`GuiBase.cs:1397`) calls `RootElement.Unmount()` and
builds a brand-new tree — there is no reconciliation across it, so a `ValueKey` only stabilizes *element
identity/ordering within one build*, not `State` across the teardown. The editor view survives a rebuild
mid-typing only because its field writes through to the `scratch` document on every keystroke and re-seeds
`initialText` from it. A pin-sourced view has no scratch doc, so it needs the SAME shape: a per-row
`Dictionary<Guid,string> pinEditBuffer` written on `onChanged`, seeded into each field's `initialText` in
`Build` (buffer if present, else the server snapshot), and cleared on commit (blur/Enter) or when the pin
leaves the set. Restore the caret across the async `MyPinsChanged` rebuild the same way the editor does:
track the focused row's TaskId (a blur does NOT clear it — the focus listener fires only on focus GAINED),
re-arm a one-shot `autoFocusPinTaskId` in `OnMyPinsChanged` (only if that pin still exists), and pass it to
the field's `autoFocus`. Own the focus nodes on the DIALOG keyed by TaskId (not index — pin order changes),
syncing add/remove against the live set each build. See `BuildPinnedContent` / `SyncPinFocusNodes` /
`pinEditBuffer` in `GuiDialogScribeLecternLibGui.cs`.

_In-game legibility verdicts (scribe-pin-editor): pending first playtest — the Pin Tab renders under the
same theme/backdrop/size as read/editor by construction (shared `RowStyle` + `LecternLayout` +
`ScribeTheme.For`), but the no-cap row list, the commit-on-blur edit flow, and drag-reorder feel are to be
confirmed in-game (tasks 7.1–7.11)._

**Symptom (refine-settings-and-window-chrome §8.1): a passive affordance inside the title-bar drag band
(`DragHandleHeight`) with a hover tooltip SWALLOWS the drag — pressing ON it doesn't move the window, but
pressing elsewhere in the band does.** `GuiBase.OnMouseDown` (`GuiBase.cs`) runs
`EventDispatcher.DispatchPointerDown` FIRST and, if it returns `capturedByWidget == true`, returns before
its `IsInDragZone(local)` band check ever executes. `DispatchPointerDown` captures whenever the hit path
has any *active* target (`FindTarget` → `IsAnyActiveTarget` → `EventCheckHelper.HandlesAnyPointerEvent`).
A `Tooltip` wraps its child in a `MouseRegion` for hover (`Tooltip.Build`), and `MouseRegion` is an active
target (it handles enter/exit) — so the tooltip alone makes the whole affordance capture the press and
defeat the band drag. **Click-through can't coexist with the tooltip:** `IgnorePointer.HitTest` returns
`false` for the ENTIRE subtree, which would also stop the `MouseRegion` from ever receiving enter/exit,
killing the tooltip. There is no per-widget "receive hover but not press" opt-out. **Fix pattern:** give
the affordance its OWN window-drag gesture instead of trying to fall through — a `GestureDetector`
(`onPress`/`onMove`/`onRelease`) nested INSIDE the tooltip (so the outer `MouseRegion` still fires hover).
On press capture `capi.Input.MouseX/Y` + the protected `WindowPos`; on move set `WindowPos = start + (mouse
delta / RuntimeEnv.GUIScale)` (raw pixels → logical, matching `WindowPos`'s units and `ToLogicalScreen`);
on release persist via `capi.Gui.SetDialogPosition(DialogCode, …)` — the same things `GuiBase`'s own band
drag does. `OnRenderGUI` syncs `rootRo.ScreenOffset` from `WindowPos` and clamps it on-screen every frame,
so no manual relayout/hit-bounds sync is needed. `GestureDetector` holds the pointer capture across the
move (`EventDispatcher._capturedElement`), so `OnMouseMove` keeps dispatching to it as the cursor leaves
the glyph. See `OnGripDragStart`/`Move`/`End` + `BuildTitleBar` in `GuiDialogScribeLecternLibGui.cs`.

**Symptom (refine-settings-and-window-chrome §8.2): a `NumericField`'s +/- step button loses the field's
focus on every click, so repeated stepping needs a re-click.** The step button is a bare `GestureDetector`
(not `IFocusable`), so pressing it blurs the focused field on pointer-DOWN — `DispatchPointerDown` does
`RequestFocus(null)` when the hit path has no focusable (the `a05caret1` note). That blur runs the field's
focus-lost COMMIT, which fired `onChanged` → the host's SYNCHRONOUS `ForceRebuild` (settings write-through)
→ the step button is unmounted mid-press, so its pointer-UP tap (which would re-`RequestFocus`) never runs.
The button's own arm-autofocus path was correct but unreachable because the rebuild happened on DOWN,
before UP. **Fix pattern:** don't rebuild on a no-op blur — in the field's focus-lost handler, only fire
`onChanged` when the committed value actually DIFFERS from the value the widget was mounted with. A step
press blurs with the value unchanged (the player didn't retype), so `onChanged` is skipped, no rebuild
happens, the button survives its own click, and its tap re-homes focus. A real retype still differs →
`onChanged` fires → the write-through remount settles the field. See `ScribeNumericFieldState.OnFocusChanged`.

**Symptom (v1-playtest-fixes scroll pass): pressing Enter to make a new task creates a row that
self-destructs a few frames later — INTERMITTENTLY (a race).** The delete does NOT come from the
empty-row self-destruct sweep (that path is guarded against sweeping a still-focused row); it comes
from `RefreshReadView`, the resync fired by the authoritative document changing
(`BlockEntityScribeLectern.FromTreeAttributes`). Mechanism: `EditorInsertTaskBelow` calls
`FlushIfDirty()` BEFORE `scratch.InsertTask(...)`, so the flushed document does not contain the new
empty task; that flush round-trips (server applies → pushes back), and when the push lands,
`RefreshReadView` builds `serverTaskIds` from the fresh authoritative doc — which lacks the brand-new
task — and calls `DeleteEditorBlock` on every scratch task "missing from the server," yanking the row
the player just created. It's intermittent because it only fires when the server round-trip lands
after the insert (a slow-enough round trip, or an autosave tick, races it). Empty tasks are NEVER
persisted by design (autosave skips a focused empty row; `PurgeEmptyTasksFromScratch` drops the rest),
so a resync will ALWAYS see a locally-new empty task as "server-missing" — the resync-drop had no
business acting on it. **Fix pattern:** the resync-drop must distinguish a task the server genuinely
DELETED (a real, typed task gone from the authoritative doc — should disappear locally too) from one
the local editor just CREATED and hasn't successfully persisted yet. Two cheap discriminators, both
needed: never drop the row currently being edited (`focusedEditIndex == i`), and never drop an EMPTY
task (its absence from the server is always expected, never a deletion). See the guard in
`RefreshReadView` (`GuiDialogScribeLecternLibGui.cs`). General rule: an async server-resync path that
prunes local rows against a server snapshot must special-case rows that are legitimately local-only
in-flight, or it will race optimistic local inserts.

**Symptom (v1-playtest-fixes scroll pass): UN-checking a task (toggle back to not-done) under the
Keep or Sink completion policy jumps the viewport — INTERMITTENTLY (only when the focused row is
off-screen).** On an uncheck (`done=False`), `ToggleEditorTask` skips the policy switch (policies only
act on a transition INTO done) and falls to the caret re-home line, which called `FocusEditorRow(held)`.
`FocusEditorRow` sets `pendingEnsureVisible = true`, so `OnRenderGUI` then runs
`Scrollable.EnsureVisible` on the focused row and scrolls it into view — even though the player clicked
a checkbox on a DIFFERENT, possibly off-screen row. It "jumps" only when the focused edit row is
outside the viewport, hence intermittent. Re-homing the caret IS intentional (spec 8.5: toggling a
checkbox must not disturb the caret in another focused row — and the checkbox isn't `IFocusable`, so
its press blurred the field via `DispatchPointerDown`, the a05caret1 note), but SCROLLING to it is not.
**Fix pattern:** to re-grant focus without moving the viewport, call `FocusNode.RequestFocus()`
DIRECTLY rather than the dialog's `FocusEditorRow` helper — the helper couples focus with
`pendingEnsureVisible`, which is right for a deliberate cross-row nav (Enter/Shift+Tab) but wrong for a
"the caret was already here, just re-arm the token" re-home. Watch for this coupling anywhere a
same-row focus restore reuses a nav helper: separate "put focus here AND scroll to it" from "the caret
is already here, only re-grant the focus token." See the Keep/Unpin/uncheck tail of `ToggleEditorTask`.

**Fact (3.1.0): `DefaultTextStyle` + `TextStyle.Merge` — how text-style inheritance actually resolves,
and the ONE landmine.** 3.1.0 adds `Gui.Widgets.Basic.Theming.DefaultTextStyle : InheritedWidget`
(`TextStyle Style`, ctor `(TextStyle style, Widget child, Key? key = null)`, static `TextStyle
Of(BuildContext)`). `Text.Build` resolves its style as `StyleOverride?.Merge(DefaultTextStyle.Of(context))
?? DefaultTextStyle.Of(context)` — so an ancestor `DefaultTextStyle` supplies defaults and each `Text`
merges its partial override on top (the Flutter mechanism). Adopted by Scribe in
`adopt-libgui-31-improvements` to set the player's Task Text Font once per tab (via `ScribeTextDefaults`)
instead of threading `FontFamily = ScribeTaskFont.Resolve(...)` at every `Text`.

**The landmine — and the CORRECTION to what we first believed.** `override.Merge(base)` decides each
field with `result.X = (X != new TextStyle().X) ? X : base.X`. The sentinel it compares against is
**`new TextStyle()` — the PARAMETERLESS CTOR, which runs the property initializers** (`FontFamily =
"sans-serif"`, `FontSize = 14f`, `Color = Vector4.One` (white), `SoftWrap = true`, `Align = Left`,
`Weight = Normal`) — **NOT `default(TextStyle)` (the all-zero struct).** Verified field-by-field in the
decompiled `Merge` body in the shipped `Gui.dll`. (The `adopt-libgui-31-improvements` design.md initially
had this inverted — said the sentinel was `default(TextStyle)` and listed `SoftWrap = false` / `Align =
zero` / `FontSize = 0` as the "silently inherits" cases. That is backwards; this note is the correct
version.) Consequences that actually shape the code:
- A child that sets a field to one of those **initializer defaults cannot override the ancestor** — the
  Merge reads it as "unset" and inherits. The real cases: **you cannot force `FontFamily = "sans-serif"`
  under a non-sans ancestor** (sans-serif IS the sentinel), cannot force `FontSize = 14`, cannot force
  white, cannot force `SoftWrap = true` back on if the ancestor turned it off, cannot force `Align =
  Left`. So a per-tab ancestor is wrap-everything-or-nothing for the family: descendants that were
  deliberately neutral sans-serif (HUD, settings chrome, History/Timer/Guestbook metadata) **flip to the
  ancestor's family** and can't opt back out. This drove real product decisions (leave HUD + Settings
  unwrapped; the user explicitly OK'd the metadata flip on the wrapped tabs).
- A field at a **non-initializer value overrides fine** (`Color = OnSurface`, `FontSize = 13*scale`,
  `Weight = Bold`, `Align = Center`, `SoftWrap = false`) — the common overrides all work.
- **Rule Scribe follows:** the ancestor (`ScribeTextDefaults.Style`) carries ONLY `FontFamily` (+ the
  scaled base `FontSize`) and leaves everything else at initializer defaults; tabs strip only the
  redundant `FontFamily`/base-`FontSize` from descendant `Text` styles. Custom RenderBoxes
  (`ScribeMultilineField`) and non-`Text` widgets (`TextField`/`TextFieldStyle`, `Dropdown`) do NOT read
  `DefaultTextStyle`, so they keep an explicit family; so does anything in a `useGlobalOverlay: true`
  subtree (renders outside the ancestor). See `ScribeTextDefaults.cs`.

**Fact (gui@3.1.0): every `Checkbox` is now focusable, and Tab traversal is engine-driven — excluding a
widget from Tab needs a custom `FocusTraversalPolicy`, not a per-widget flag.** Two 3.1.0 changes combine
into a regression (symptom: Tab / Shift+Tab in the Lectern editor + Pin Tab began stopping on each row's
completion **checkbox** before its text field, doubling keystrokes). (1) `CheckboxState` now derives from
`FocusableState<Checkbox>` and lazily creates its own `FocusNode` (`_focusNode ??= new FocusNode()`), so
every mounted checkbox is focusable — but the `Checkbox` **widget** ctor exposes NO focus/traversal
parameter, so that node lives on internal state a mod can't reach. (2) `GuiBase.OnKeyDown` now intercepts
Tab globally (`IsTabKey` == GlKey code **52**) and runs `FocusManager.FocusNext/FocusPrevious`, which walk
`FocusManager.TraversalPolicy.GetTraversalOrder(root, manager)`; the default `ReadingOrderTraversalPolicy`
(sealed) collects EVERY node in the tree where `CanRequestFocus && !SkipTraversal`, checkboxes included.
This Tab handling runs **before** `_inputRouter.KeyDown`, so a field's own Tab handler (e.g. our old
`ScribeMultilineField` Tab→advance) is now dead code — commit-on-Tab must ride the blur path instead
(`FocusManager.RequestFocus` blurs the outgoing node first, firing its focus-lost/gained listeners).
Fix seams that actually work: `FocusManager.TraversalPolicy` is a public settable prop (defaults to
`new ReadingOrderTraversalPolicy()`); `FocusTraversalPolicy` is `public abstract` with one method
`IReadOnlyList<FocusNode> GetTraversalOrder(Element root, FocusManager)`. `FocusNode` has public settable
`CanRequestFocus`/`SkipTraversal`/`TraversalOrder` — but you can't reach the checkbox's node to set them,
so per-widget exclusion is impossible. The robust fix is an **allow-list policy**: return only the nodes
you want Tab to visit (Scribe's `ScribeFieldOnlyTraversalPolicy` returns just the active view's ordered
field nodes), so anything not opted in — checkboxes now, any future focusable control — can never be
Tab-focused. Installed per-dialog on `GuiBase.FocusManager` (a field-initialized instance available from
construction), so it scopes to that dialog only; a separate dialog (e.g. the Settings window) keeps its
own default policy. **Ground truth for all of this is the decompiled vendored `src/Mod/lib/Gui.dll`
(`ilspycmd -t <FullTypeName>`) — the `reference/vslibgui/` clone is STALE (single commit 2026-06-06,
pre-3.1.0) and has NO traversal system at all, so it must NOT be trusted for focus/traversal questions.**

**Fact (2026-08-04): `PaintingContext.SharedPaint` is ONE `SKPaint` reused across every draw op AND
across frames — and `DrawMaskedBox` (the textured-`Container`/`BoxStyle.Texture` draw) is the one draw
that reuses `SharedPaint.Color` WITHOUT re-setting it.** Symptom that led here: a clay-texture tablet
backdrop rendered fully transparent, but ONLY on a read-only tablet that had rows of content — an empty
tablet, or the editable (wet) tablet, showed the backdrop opaque. Root cause, confirmed against the
vendored `src/Mod/lib/Gui.dll` (3.1.0 ground truth, NOT the stale `reference/vslibgui/` clone):
`PaintingContext.SharedPaint` is created once (`new() { IsAntialias = true }`) and mutated in place by
every draw. `DrawBox`/`DrawImage`/`DrawNineSlice` all assign `SharedPaint.Color = …` before painting, so
they're self-contained. But `DrawMaskedBox` sets only `FilterQuality` before `Canvas.DrawBitmap(texture,
rect, SharedPaint)` — and `DrawBitmap` MODULATES the bitmap by `paint.Color`'s alpha. So whatever alpha
the previous draw op left on `SharedPaint.Color` scales the whole backdrop texture. A widget that draws
a resting/transparent box (`boxColor` alpha 0) via `base.PaintInternal`, captures that color as
"previous", then RESTORES it on teardown, leaves the shared paint at alpha 0 → the next frame's backdrop
`DrawBitmap` renders at 0 opacity. Two things made it intermittent-looking: (1) the whole root re-records
into an `SKPicture` only when something is dirty (`GuiBase._rootPaintCache` / `RenderRepaintBoundary`),
so it bites when animated glyph rows force a re-record every frame; (2) it needs the leaking widget to be
the LAST painter — on a normal read view a footer Divider/Button repaints after the rows and re-sets the
color, masking it; drop the footer (read-only tablet) and the rows are last. **Takeaway for our render
objects: never RESTORE an inherited `SharedPaint.Color` on teardown — leave it OPAQUE (`SKColors.White`),
the neutral every framework draw except `DrawMaskedBox` sets before painting.** Fixed in
`ScribeCuneiformField.cs` + `CuneiformText.cs` (both cuneiform stroke render objects).

### Hover is never re-evaluated when geometry moves under a STILL cursor — no MouseTracker (2026-08-08)

LibGUI computes hover enter/exit in exactly ONE place — `EventDispatcher.DispatchPointerMove`
(`Gestures/EventDispatcher.cs`, hit-test → compare to `_hoveredElement` → fire exit/enter) — and that
is called from exactly ONE non-test site: `GuiBase.OnMouseMove` (real cursor motion). There is **no
post-layout / post-frame hover re-check** (no equivalent of Flutter's `MouseTracker`). Consequence:
when the widget tree relayouts and a *different* element slides under a *stationary* cursor (a Scribe
list row collapsing after delete, or expanding on insert), the framework never notices — the element
under the cursor keeps its stale `hovered=false`, so hover-gated affordances (the row's delete/pin
buttons) stay hidden until the user physically moves the mouse. This is the root cause of the
"delete-button-doesn't-reappear-until-you-wiggle" bug (docs §4.1).
- **The fix is a mod-side synthetic re-dispatch, and that's an ESTABLISHED LibGUI idiom, not a kludge**
  — `GuiBase.OnMouseMove` itself fabricates `new PointerEvent(-1, -1)` (off-screen) to force a
  `PointerLeave` when another dialog handled the move (GuiBase.cs:689). Re-dispatching
  `EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(localX, localY))` at the CURRENT
  cursor pos re-runs the hit-test and self-heals hover.
- **All the plumbing is reachable from a `GuiBase` subclass** (no gui-dep change, no private access):
  `EventDispatcher` (public), `DispatchPointerMove` (public), `RootElement` (public), `WindowPos`
  (protected), `GetUiScale()` (protected). Reconstruct window-local coords exactly as the private
  `ToWindowLocal(ToLogicalScreen(...))` does: `local = capi.Input.Mouse{X,Y} / GetUiScale() - WindowPos`
  — the SAME math the drag-grip already uses (`ScribeDialogBase.Layout.cs:293-305`). `_lastMouseLocal`
  is private, so source the cursor from `capi.Input`, not from LibGUI's cache.
- **Conclusion-only re-hover under-solves it; continuous is nearly free.** Firing the re-dispatch once
  when a collapse *finishes* leaves hover stale for the whole 200ms while geometry slides, so a fast
  mass-delete still stutters. Firing it EVERY frame *while any collapse controller is animating* fixes
  fluid mass-delete. Continuous costs almost nothing extra because the frame loop is ALREADY spinning
  during the animation (`ScribeCollapsible`'s `OnValueChanged → MarkNeedsBuild`) and the host registry
  ALREADY tracks in-flight controllers — the added trigger is one `AnyAnimating` predicate on
  `ScribeCollapseRegistry`, sibling to its existing `IsComplete`.
- **But gating PURELY on `AnyAnimating` stops ONE FRAME TOO EARLY — the hover drops exactly when the
  collapse *ends* (2026-08-08 playtest).** The collapse-completion callback (fired inside the ticker pump
  within `base.OnRenderGUI`) flips its controller to `Completed` (so `AnyAnimating` is ALREADY false) AND
  arms the deferred `needsCollapseCleanup` → `ForceRebuild()`. `ForceRebuild` (`GuiBase.cs:1404-1416`)
  `Unmount()`s the tree and mounts a **brand-new one where every element is `hovered=false`**, and that
  fresh tree is **not laid out until a later frame** (layout runs in `base.OnRenderGUI` via
  `rootRo.Layout`). So on the cleanup frame the re-hover is skipped (`AnyAnimating` false) AND a synthetic
  move would hit-test nothing anyway (new tree has zero geometry) — no pointer-move ever lands on the
  rebuilt+laid-out tree, and the row's delete/pin button vanishes right as the collapse finishes. Fix: a
  small **frame latch** (`ScribeHoverRefreshLatch`, ~3 frames) re-armed both while animating and on the
  cleanup-rebuild frame, so at least one refresh lands *after* the new tree lays out. This mirrors the
  general LibGUI rule that anything triggered by a `ForceRebuild` must account for the fresh tree needing a
  later frame to lay out (see the `ForceRebuild`/settling notes).
- **The stale hover is a property of `ForceRebuild` ITSELF, not of collapse — gate the refresh on ANY
  rebuild, detected by `RootElement` identity (2026-08-08 playtest).** HUD *unpin* and *new-row creation*
  drop hover the same way but aren't collapse-animated, so an `AnyAnimating`-only trigger never fires for
  them. `GuiBase.ForceRebuild` assigns a brand-new `RootElement` instance (`GuiBase.cs:1414`) and that is
  the ONLY post-mount replacement, so "`RootElement` differs from last frame" is an exact,
  zero-false-positive rebuild signal. `ScribeHoverRefreshLatch.ArmIfRebuilt(RootElement)` called once per
  frame from `OnRenderGUI` arms the linger on every rebuild path with no per-call-site wiring — subsumes
  the collapse-cleanup arm. This is the reusable pattern for "keep hover correct after a rebuild."
- **A ONE-FRAME hover flicker after such a rebuild is UNAVOIDABLE from mod code and is inherent LibGUI
  behavior (seen in other LibGUI mods) — accepted, don't chase it.** The fresh tree paints once with
  `hovered=false` before the next-frame synthetic refresh can hit-test it, because LibGUI runs
  build→layout→paint as one sealed sequence and a `GuiBase` subclass can't inject a hover dispatch in the
  layout↔paint gap (would need a `gui`-dep hook, or re-implementing LibGUI's layout — both rejected).
  Fixable only if LibGUI ever exposes a post-layout/pre-paint hook.

### `ScribeCollapsible` is DIRECTION-AGNOSTIC — row-EXPAND-into-view is ~80% already built (2026-08-08)

Explored while scoping §4.1 (the "new tasks expand into view" wish). The collapse primitive generalizes
to expansion with no new animation type, and the exploration de-risks that future feature:
- **`ScribeHeightFactorRender` never hard-codes a height** — `PerformLayout` lays the child out at FULL
  constraints, measures the child's real `Size.Y`, and reports `Size.Y * Factor` (clipping paint to the
  shrinking box). So the SAME render object drives an expand by running the factor 0→1
  (`factor = curve(value)`) instead of collapse's `1 - curve(value)`. `AnimationController` already has
  `Forward()`/`Reverse()`/settable `Value` and a `Reverse` status the tick loop honors, so no new
  controller work. **A "wildly different task size" is a non-issue for the primitive** — it re-measures
  the child every layout, so a 40px Standard row and a 300px Tracked-with-picker row both expand
  correctly with zero config.
- **No permanent invisible 0-height placeholder row is needed.** Collapse needs a ghost *snapshot* only
  because the data is already gone from `scratch`; expansion is the EASIER case — the row's data has
  just been ADDED, so real freshly-mounted content animates in. Wrap the new row in the height-factor
  widget with factor 0→1.
- **The host-owned `ScribeCollapseRegistry` resume-across-`ForceRebuild` pattern is reusable as-is** —
  a controller keyed by the new row's stable id, resumed on remount, is exactly what an expand needs
  (same reason collapse needs it: `ForceRebuild` remounts the widget every frame).
- **The ONE genuinely new problem is content that changes height MID-animation.** Collapse animates
  frozen departing content, so its measured target is stable. An expanding EDITABLE row (auto-focused,
  user types, or a task-type picker swaps its body) has a child whose natural height moves DURING the
  0→1 expand; since the render re-measures each frame and scales, a growing target can read as jitter
  (the row races its own growth). Two sub-risks fall out: (1) auto-focusing into a near-0-height clipped
  row — layout is full-size so caret SHOULD work while visually clipped, but that's unverified; (2) the
  final content is the task-type-DESIGN problem (docs §2.x), not an animation problem — the animation is
  ready; what mounts inside it is blocked on the picker/kind decisions. **Takeaway: build the §4.1
  continuous re-hover now and it covers expand's identical stale-hover bug for free (direction-agnostic
  trigger); the expand animation itself is a small follow-up once task-type content is decided.**

### Row-CREATION animation — SLIDE (paint-only), not height-expand, for an editable row (2026-08-08)

Two independent LibGUI-source explorations (for the "make row creation visceral" goal behind docs §1.2)
converged on the same verdict. This SUPERSEDES the naive "just run the collapse primitive backwards"
idea for an editable/auto-focused row — height-expand is the WRONG tool there.
- **Slide-in via a paint-only `Transform` translate is the pick.** `Transform`/`RenderTransform` leave
  layout constraints untouched ("purely visual"; `RenderTransform.PerformLayout` just calls `base` then
  updates the matrix) — so the child is laid out at FULL natural size every frame. An auto-focused input
  can grow/wrap/swap its body mid-animation without ever fighting the animation. This structurally avoids
  the mid-animation height-change JITTER that a height-expand (`ScribeHeightFactorRender`, which
  re-measures the live child and scales it) suffers with live editable content.
- **Auto-focus safety differs by approach — this is the decider.** Focus + keyboard route through
  `FocusManager.PrimaryFocus` (`EventDispatcher.DispatchKeyDown/Char` → `focus.Owner`), INDEPENDENT of
  paint-clip and hit-test geometry — so programmatic auto-focus-on-create always works. BUT: (a) the
  visible caret paints only under a full-size box — height-expand's `ScribeHeightFactorRender.Paint`
  `ClipRect`s to the near-0 box, so the caret is CLIPPED INVISIBLE near t=0; (b) MOUSE hit-testing uses
  layout `Size` (`Element.HitTest`, `RenderObject.HitTest` test `pos ≤ Size.Y`), so during a height-expand
  a click on the un-revealed part MISSES — and per the pointer-down-clears-focus rule it would BLUR the
  row. Slide/fade never clip and stay full-size → caret visible, clicks land. So height-expand is unsafe
  for an immediately-focused editable row; slide/fade are safe.
- **Fade is OPTIONAL, on the SAME controller via `Curves.Interval`** (slide `Interval(0,0.7,EaseOutCubic)`,
  fade `Interval(0,0.5)`) — one controller, two tween reads, no second registry entry. Fade ALONE reads
  too subtle/"computery"; it softens the slide's arrival. Gotcha: `RenderOpacity` early-returns without
  painting below α≈0.001, so a fade literally starting at 0 shows one frame of an invisible-but-focused
  row — start at a small non-zero α, or let the slide carry t=0.
- **Needs the collapse registry pattern; stock `Animated*` widgets DON'T work.** Confirmed every
  `ImplicitlyAnimatedWidget` (`AnimatedSlide`/`AnimatedOpacity`/`AnimatedScale`/`AnimatedSize`/…) SNAPS
  under `ForceRebuild`: first `InitState`/`TweenVisitor.Visit` seeds Begin==End==target, and motion only
  happens in `UpdateWidget` on a RECONCILED element — but `ForceRebuild` unmounts+recreates, so it always
  hits fresh `InitState` and snaps (`AnimatedSize`/`RenderAnimatedSize` snaps for the analogous
  `_hasLayoutOnce==false` reason). The fix is the SAME as `ScribeCollapsible`: a host-owned persisted
  `AnimationController` in a registry (`ScribeCreateRegistry` sibling to `ScribeCollapseRegistry`), keyed
  by the new row's stable id, `Forward()` on first mount / `Resume()` on remount, driving stock
  `Transform`(+`Opacity`) render primitives (NOT the `Animated*` wrappers) via a self-ticking
  `StatefulWidget` mirroring `ScribeCollapsibleState`. Stock `AnimatedBuilder` is the idiomatic
  external-controller bridge if not hand-rolling. `AnimationController` already exposes
  `Forward`/`Reverse`/`Resume`/settable `Value` — no new controller work.
- **4 things NOT source-answerable — need in-game feel tests:** (1) paint-only slide reserves the row's
  full-height slot on frame 1 (rows below snap to final positions, then the row slides into the open slot)
  — it CANNOT open the gap progressively without reintroducing the jitter/clip; whether "instant gap then
  glide" reads as concrete vs. "rows jump, one glides" is a taste call; (2) slide direction vs. the list
  container's clip rect (does an off-edge slide emerge cleanly or overlap the neighbor?); (3) duration
  (~180–220ms to match the collapse `DefaultDurationMs`) + curve (`EaseOutCubic` vs. slight `EaseOutBack`
  overshoot); (4) confirm no fade lower-bound flash. Key file refs: `Widgets/Painting/Transform.cs` +
  `Core/Painting/RenderTransform.cs`; `Widgets/Painting/Opacity.cs` + `Core/Painting/RenderOpacity.cs`;
  `Widgets/Animations/AnimationController.cs`, `AnimatedBuilder.cs`, `Curves.cs` (`Interval`); focus path
  `Widgets/Input/Focus.cs` + `Gestures/EventDispatcher.cs`; caret gate `Core/Input/RenderTextField.cs`.

### `SetState` / `MarkNeedsBuild` is DEFERRED — a State can safely schedule its OWN rebuild from an animation `onEnd` (2026-08-10)

Confirmed against `Element.MarkNeedsBuild` + `BuildOwner.ScheduleBuildFor`/`BuildDirtyElements`
(`Widgets/Framework/`). `SetState`/`MarkNeedsBuild` does NOT rebuild synchronously — it just adds the
element to `BuildOwner`'s `_dirtyElements` set (idempotent: a second `MarkNeedsBuild` on an
already-dirty element is a no-op), drained on the next `BuildDirtyElements` pass. That drain loops
`while (_dirtyElements.Count > 0)` and its doc-comment explicitly says it "handles cascaded rebuilds
from animation controllers or state changes triggered inside `Build()`."

**Consequence for host reentrancy guards:** a `try/finally` lock around `RebuildHudBody()` /
`body.Rebuild()` (a `SetState`) does **not** cover `BuildHudTree` — the flag is already cleared
when LibGUI later drains `_dirtyElements`. The lock must also be held inside the `Build()` that
constructs the tree, so a side-effect during that pass cannot call back into `RecomputeHudTrackers`
or `RebuildHudBody`. `BuildDirtyElements` itself is an **uncapped** `while (_dirtyElements.Count > 0)`
loop: if `Build()` marks any element dirty, that drain never finishes (hang). Recursive
`StatefulElement.Rebuild()`/`Mount` (not the while loop) is what would stack-overflow (`c00000fd`).

**Consequence:** a callback firing from inside the animation tick pump (e.g. a collapse's `onEnd`) MAY
call `SetState` to schedule *its own* subtree's rebuild — there is no re-entrancy, because the rebuild
is queued, not run inline. This is why `ScribeAnimatedList` retires its completed ghosts with a plain
`SetState` from `onEnd` and needs **no** host-visible `needsCleanup` flag. The editor's hand-wired path
DOES use such a flag (`needsEditorCollapseCleanup`) — but only because its `onEnd` rebuilds a
*different, ancestor* subtree (`RebuildBody` on the dialog body), which would be re-entrant to walk from
inside a descendant's tick. Rule of thumb: **scheduling a rebuild of your own element from a tick
callback is safe; synchronously rebuilding an ancestor is not** — defer the latter to `OnRenderGUI`.
(`BuildDirtyElements` also sorts dirty elements shallow-first and skips any element a parent rebuild
already cleaned, so a parent+child both going dirty in one frame rebuilds the child once, not twice.)

### A self-owned `AnimationController` on a reconcile-reused State must sync in BOTH directions — start-only is a latent bug ForceRebuild masks (2026-08-10)

Root-caused the long-standing "HUD task loses its text, leaving a bare checkbox" bug (see memory
[[hud-fade-text-stale-controller-bug]]). `ScribeFadeText` (`HudScribePins.cs`) drives its own
`AnimationController` to ramp text opacity 1→0 during the destructive-pending window, and `Build`
computes `opacity = controller == null ? 1 : 1 - controller.Value`. The `EnsureFading` helper only ever
STARTED a controller and never cleared one. On a LATE undo (fade ≈ complete, `Value ≈ 1` → opacity ≈ 0)
the row reverts to `Fading: false` but keeps the stale completed controller — so the text stays
invisible forever.

**Why it went from intermittent to reliable:** the reconcile HUD conversion (§ below / commit `ec4864a`)
replaced the HUD's `ForceRebuild` repaint with a reconciling `SetState`. `ForceRebuild` unmounts +
recreates the tree, so the undo landed on a FRESH `controller == null` state → text visible (the bug
self-healed). Reconcile REUSES the row element (its State survives), so `InitState` doesn't re-run and
the stale controller persists. **Element reuse turns a start-only controller into a permanent bug.**

**Fix / rule:** make the sync bidirectional — dispose the controller when the driving flag goes false
(`SyncFadeController` runs from BOTH `InitState` and `UpdateWidget`; disposing there is safe because both
run during the build phase, not from inside the ticker callback). General lesson: **any State that owns
an `AnimationController` and can be reconcile-reused must reconcile the controller in BOTH directions in
`UpdateWidget` — create/start on the on-transition AND dispose/clear on the off-transition.** Don't rely
on a remount to reset it; under reconcile there is no remount.

### Reconciling-rebuild discipline: persistent body + `SetState`, `ForceRebuild` reserved for genuinely-new trees (2026-08-10)

The `reconcile-animating-surfaces` change replaced the "every update is a `ForceRebuild()`" habit with a
disciplined split. The rule for any Scribe dialog surface:

- **In-place update (same tree, changed data) → reconcile via `SetState`.** Each dialog owns ONE
  persistent-root `ScribeDialogBody` (allocated once via a `GlobalKey`/`bodyKey`, NEVER in `Build()`); every
  in-place mutation — add/delete/reorder a row, a pin push, a completion toggle, a tick repaint, an external
  resync of the SAME item-count-or-keyed set — routes through a `RebuildBody()`/`RebuildHudBody()` that does
  a reconciling `SetState` on that body. Reconcile REUSES each row's live element + State, so caret, unsaved
  text, scroll offset, hover, and in-flight animations all survive. This is what killed the flicker /
  lost-hover / dead-mass-delete-first-click / caret-loss class of bugs — they were all symptoms of the tree
  being torn down and rebuilt under the user.
- **Genuinely-new tree → keep `ForceRebuild()`.** Reserved for: read⇄editor⇄settings VIEW SWITCHES, a fresh
  editor seed, lost-lock recovery, and the still-`ForceRebuild` non-reconciled views (History/Timer/Visitors).
  These legitimately want a from-scratch remount, and they pair with `CaptureScrollForRestore()` because the
  remount re-derives content height and clamps the offset toward 0.
- **Row identity is load-bearing.** Rows must be keyed by a STABLE `ValueKey<Guid>(TaskId)`, not
  `ValueKey<int>(index)` — a positional key makes reconcile mis-associate a surviving row with a departed
  one's element (wrong caret, wrong animation). A departing/collapsing row is spliced back at its held display
  index under its OWN TaskId key (wrapped in `ScribeRowSizeAnimation`), so no slot swaps widget TYPE at a key.
- **A reconcile-reused State must reconcile ALL its self-owned resources in BOTH directions** (see the
  `AnimationController` note above) — start-only / arm-only logic that relied on a remount to reset is a
  latent bug once the surface reconciles.

### An entry/exit wrapper that appears-then-disappears at a slot REMOUNTS the child (type-swap) — keep it on a caret-bearing row for life (animate-row-insertion, 2026-08-12)

The reconciler matches a slot's widget frame-to-frame by `Widget.CanUpdate` = `GetType() == GetType() &&
Equals(Key, Key)`. So *adding or removing a wrapper widget around a row across a reconcile changes the slot's
type and remounts the whole child subtree* — even though the row's own key never changed. For the
**auto-focused new editor row** that is fatal: remounting rebuilds its `GuiElementTextInput` and the
caret/selection is lost mid-keystroke.

Baked into `ScribeAnimatedList` + `ScribeSlideIn`: the entry wrapper **stays on the row for the row's entire
live lifetime** (every appearance is kept-for-life; `entering` is a plain `HashSet<Guid>`, no per-mode retire
logic). Once the slide completes it renders an inert `Opacity(1) > Transform(identity)` pass-through, never
removed. `ScribeSlideIn.Build` therefore ALWAYS returns the same `Opacity > Transform > child` shape (even
not-animating: `Opacity(1f, Transform.Translate(child, Vector2.Zero))`), so the subtree shape is identical
whether sliding, settled, or pass-through — no type-swap ever occurs at that slot. This is why the shipped
entry is a paint-only translate (`Transform` passes layout through unchanged → full height in-slot from frame
one) rather than the earlier height-grow, which changed the row's height every frame under the caret; see
`docs/animation-lessons-learned.md` "Row ENTRY animation."

Corollary to the "row identity is load-bearing" bullet above: keying by a stable `Guid` is necessary but not
sufficient — the *type* at the slot must also stay stable across the frames where you care about identity.

### A "hold the row before collapsing it" undo affordance does NOT belong in the animation container — a frozen ghost can't be interactive (migrate-hud-onto-animated-list, 2026-08-12)

When the pinned HUD was migrated onto `ScribeAnimatedList`, the plan was to give the container a `Delayed`
removal policy: hold a *faded ghost* of the row at full height for an undo window, then collapse it. That was
a misconception, and the general rule is worth keeping: **any undo/grace affordance on a departing row has to
act on the LIVE, still-interactive row — the container's collapse ghost is a frozen snapshot with no gestures,
focus node, or clickable controls, so it cannot host one.** The HUD's undo is literally "uncheck the row," so
it *must* stay on the live widget.

The resolution split the two concerns that had been conflated: (1) the **undo window** is a deferred-send
phase that lives entirely in the host (`HudScribePins`) *before* the row ever leaves the item set — the pin
stays live in `MyPins`, the `ScribeFadeText` countdown runs on the live row, and undo = removing the unsent
pending packet; (2) the **collapse** is the container's plain `Immediate` policy, triggered when the host
drops the id from the item set (adds it to `awaitingRemoval`) at send-time. The container never needed a new
policy; the stubbed `ScribeListRemovalPolicy.Delayed` member was deleted. The ghost the container renders must
match the row as the window left it — here a **zero-opacity-text** frozen twin, so the collapse closes empty
space rather than flashing the faded text back at full opacity for a frame. See
`docs/animation-lessons-learned.md` "The HUD migration, and why the 'Delayed removal policy' was a
misconception."

### CORRECTION to the `ListView` child-cache notes above — the read view no longer uses `ListView` (D4, 2026-08-10)

The two facts above (~line 1394 "Scribe's `RefreshReadView` uses `ForceRebuild`"; ~line 1421 "The read view
keeps `ListView`") were true when written but are now SUPERSEDED by `reconcile-animating-surfaces` §5 (design
D4, Tier 2). The `ListView` child-cache trap — a same-count parent `SetState` keeps the cached row widget
because `ListViewContent.DataIdentity` is hardwired to the `ScrollController` and has no public seam to feed a
document-identity token (Tier 1 is UNREACHABLE without forking `gui`) — was resolved not by fighting the cache
but by **dropping `ListView` from the read view entirely.** The read view now uses the SAME non-virtualized
`Scrollbar > SingleChildScrollView > Column` of ALL rows the editor already used, re-keyed
`ValueKey<Guid>(TaskId)`, and routes `RefreshReadView` through `RebuildBody()` (reconcile) instead of
`ForceRebuild()`. So a same-count external resync now reconciles and reuses surviving rows; the child-cache
caveat simply no longer applies to any Scribe surface (a lectern doc is a small checklist, so non-virtualized
costs nothing, and read/editor content heights now match by construction instead of via `estimatedItemHeight`).

**Fact (add-timer-gearworks, 2026-08-11): rotating a self-loaded raster in the widget tree, three ways to render a "gear," and the ForceRebuild-snap trap.** Building the Timer-tab clockwork established the working pattern for an animated textured widget:

- **Rotate a self-loaded PNG:** decode it once with `capi.Assets.TryGet(loc, loadAsset:true)` → `SKBitmap.Decode` (NOT `Image`/`SkiaAssetLoader`, which silently no-op after startup — see the self-load fact elsewhere in this section), cache the `SKBitmap`, put it in a `Container { BoxStyle.Texture = bmp }`, and wrap that in `AnimatedRotation` (or a raw `Transform.Rotate` about center). `AnimatedRotation` is the crash-safe choice — a raw rotate on a zero-size box produces a NaN Skia matrix / GPU crash, so guard for non-zero size if you use `Transform.Rotate` directly.
- **`ForceRebuild` SNAPS every stock `Animated*` widget.** This is the single biggest gotcha. An `Animated*` widget only tweens across a *reconcile*; on a fresh **mount** it seeds `Begin == End` and jumps. Any host that calls `ForceRebuild()` on a state change (the Timer tab does, in `RefreshTimerView`) remounts the subtree, so an `AnimatedRotation`/`AnimatedSlide` whose target changed across that rebuild snaps instead of animating (this caused the "wheel disappears then re-slides + half-tick on fire" glitch). **Fix pattern:** derive the animated value from a **monotonic clock / host-stamped timestamp** that survives the remount, not from widget `State`. Rotation angle = `floor(elapsedMs/period) × stepAngle`; a slide's progress = `(nowMs − hostStampedStartMs) / durationMs` eased manually. The widget is still a self-ticking `StatefulWidget` (a game-tick listener calls `SetState`/`MarkNeedsBuild` to repaint), but the *value* is a pure function of the clock, so it's rebuild-stable.
- **Three ways to draw a "gear," and which we picked:** (1) render the REAL 3D `game:gear-temporal` item via `ItemStackDisplay` + `ItemStackRenderer` (an `IPreSkiaRenderer` in the `gui` dep, composites a real `ItemStack` into the Skia canvas through an offscreen FBO, macOS-safe) — **evaluated and rejected**: it works but the real item is an irregular *lattice* with a continuous spin, so it can neither mesh nor tick per tooth. (2) Raw GL quads rotated with `GlPushMatrix/GlRotate` in `OnRenderGUI` (the Gearlock Firearms technique) — recorded only as a **documented fallback** (the mod otherwise makes zero raw-`capi.Render` calls). (3) **Chosen:** authored/procedural 2D raster in the Skia widget tree per the first bullet. Faked mesh = one monotonic driver × per-gear `(sign, ratio)` constants (`ratio = referenceTeeth/thisGearTeeth`), positions hand-tuned so painted teeth interlock — no physics.
- **Procedural raster sizing to avoid blur:** generate at a size LARGER than the displayed physical px (we used 512² for a ~212 logical-px wheel) so `DrawMaskedBox`'s bilinear resample only ever *downsamples* (crisp) rather than upscales (blurry). Cache + dispose the bitmap on the same path as loaded PNGs.
- **`DrawMaskedBox` reuses `SharedPaint.Color` without setting it** (the textured-`Container` path) — so a gear is modulated by whatever the previous draw op left on the one shared `SKPaint`. A single top-level reset can't help when many ops paint between it and each gear; reset opaque-white + clear `ColorFilter`/`ImageFilter` immediately before EACH textured draw. (This is the same `SharedPaint` leak the dialog backdrops hit; see the tablet-backdrop note.)

### Row-height-neutral oversized child, and NO strikethrough in `TextStyle` (add-tracker-link-tasks 7.11, 2026-08-15)

Two LibGUI facts from tuning the Tracker/Link row icons + counter:

- **`TextStyle.Decoration` (`TextDecoration`) has only `None` and `Underline` — there is NO strikethrough.**
  To strike text, overlay a thin line yourself. Pattern used for the satisfied Tracker counter
  (`ScribeTrackerCounterText`): wrap the counter `Text` in a `Stack` and add a `Positioned(left:0, right:0,
  top: lineHeight/2 − t/2, height: t)` child holding a `Container { BoxStyle.Color = faint }`. `left`+`right`
  both set → the line spans the Stack's width, which is the (non-positioned) `Text`'s width — so it strikes
  ONLY that text, not its Row siblings. Center it on the text's single line via a measured line height
  (`ScribeRowControlNudge.TextLineHeight`).
- **A child can render LARGER than its layout footprint via `Stack` + `Positioned`, because `RenderStack.Paint`
  does NOT clip.** To make an oversized icon contribute only ONE text-line of row height (so a Tracker/Link row
  equals a single-line Task row while the item icon still reads ~10% bigger): `Stack` children = `[ SizedBox(w:
  visual, h: lineHeight)  // non-positioned → sizes the stack, Positioned(left:0, top:(lineHeight−visual)/2,
  width:visual, height:visual, child: icon) ]`. Key mechanic (confirmed by decompiling `RenderStack`): a
  `Positioned` with BOTH `Width` and `Height` set gets `min == max` for that axis, so the child is forced to
  exactly `visual×visual` regardless of the (smaller) stack size; the negative `top` centers it so it overflows
  equally above/below. Non-positioned children lay out under `LayoutConstraints.Loose`, and the stack sizes to
  their max. There is **no `OverflowBox`/`UnconstrainedBox`** in this LibGUI build — this Stack trick is the way.
  Caveat: the overflow paints into neighbors' space, so keep the excess modest and vertically centered.

**Fact: `GuiBase` does NOT override `DrawOrder`, and matching vanilla's 0.2 band is not enough
to stack above Handbook/Inventory.** Hit-testing follows `OpenedGuis`/`LoadedGuis`; LibGUI pixels
do not. `PostSkiaPipeline` (RenderOrder 1.0) inserts *before* `GuiManager` (also 1.0) and flushes
the shared wrapped-FBO Skia surface first, so vanilla dialogs always paint on top. Overriding
Scribe `DrawOrder => 0.2` makes clicks hit Scribe while Handbook still covers it — worse than
leaving the default 0.1. Per-window `SkiaRenderer.End`/`Flush` during `OnRenderGUI` hid vanilla
GUI or leaked GL into the next opaque-terrain pass. Leave DrawOrder at the `GuiBase` default
until LibGUI composites each window in the GuiManager loop. See the DrawOrder-band bullet in
the HUD/hotkeys section above.

**Fact: `GuiBase` layout+paint is NOT gated on `Focused`/`IsActiveWindow` — every OPEN dialog re-lays-out
and repaints each frame.** Decompiling `Gui.dll` (`GuiBase.OnRenderGUI` + `FramePipeline.Run`): render is
gated only on `IsOpened() && RootElement != null`; `FramePipeline.Run` performs layout whenever
`renderObject.NeedsLayout || renderObject.ChildNeedsLayout` and paints unconditionally. `IsActiveWindow` is
passed through but used ONLY for debug painting. So a `ForceRebuild()`/`SetState` on an UNFOCUSED Scribe
dialog (e.g. while the vanilla Handbook is the focused/topmost dialog) DOES visually update on the next
frame — "it's not repainting because another window has focus" is a false lead. (This killed a focus-gating
theory for the "Handbook add doesn't show live" bug; the real cause was a rebuild-ordering bug — see below.)

**Fact: `GlobalKey.CurrentState<T>()` is NOT resolvable in the same synchronous call right after
`ForceRebuild()` mounts the tree — so a `SetState`-style in-place reconcile that runs immediately after a
rebuild silently no-ops.** Scribe's `RebuildBody()` is `bodyKey.CurrentState<BodyState>()?.Rebuild()`; the
`?.` swallows a null state. When code did `ForceRebuild(); ...mutate scratch...; RebuildBody();` in one call
stack (the Handbook deferred-append path in `EnterEditorMode`), the `ForceRebuild` built the tree from the
PRE-mutation state and the follow-up `RebuildBody` found `CurrentState == null` (the freshly-mounted body's
GlobalKey isn't registered/resolvable yet within that synchronous frame), so the mutation only appeared on
the NEXT full rebuild — symptom: "new row invisible until a manual view swap." **Fix pattern: mutate state
BEFORE the `ForceRebuild`, so the single rebuild renders the final state** — don't rely on a reconcile
chained after a rebuild in the same call. (add-tracker-link-tasks 7.13; `ScribeDialogBase.EnterEditorMode`.)

### LibGUI CAN host real VS item-slot drag/drop — it reimplements the vanilla slot protocol in `Gui.Widgets.Inventory` (2026-08-16)

**Question:** can a LibGUI dialog host real `ItemSlot` drag/drop (the vanilla cursor-stack
pickup/place), or does LibGUI bypass it? **Answer: YES it can.** LibGUI does NOT reuse vanilla's
Cairo `GuiElementItemSlotGrid` (incompatible with its Skia widget tree); instead the
`Gui.Widgets.Inventory` namespace faithfully **reimplements the identical vanilla slot protocol**, so
from the server's/inventory's point of view it is indistinguishable from a native slot grid.

- **Vanilla mechanism** (`GuiElementItemSlotGridBase.SlotClick`, VintagestoryAPI.dll): reads the
  cursor stack from `IPlayerInventoryManager.MouseItemSlot`, builds an `ItemStackMoveOperation`, calls
  `inventory.ActivateSlot(slotId, slot, ref op)` (returns a packet), sends via
  `api.Network.SendPacketClient(packet)` — over the **block-entity packet channel**, not a custom one.
- **LibGUI equivalent:** `Gui.Widgets.Inventory.SlotController` (`ClickSlot`, `BeginDrag`/`EndDrag`,
  `WheelSlot`, `WatchInventory` → subscribes `IInventory.SlotModified` to rebuild the widget),
  `SlotGrid`, `FlatItemSlot`, `ItemSlotGestureLayer`. `ItemSlotOverlay → ItemStackDisplay` renders the
  3D stack into the Skia canvas (no custom `IPreSkiaRenderer` needed for slots). Working example:
  LibGUI's `Gui.Example.Pages/InventoryPage.cs`.
- **`ScribeDialogBase` already extends LibGUI's `GuiDialogBlockEntityBase`** — today via the
  inventory-less `(pos, capi)` ctor. Switch to the inventory-carrying ctor and pass the BE's
  `InventoryBase` to get `OpenInventory`/`CloseInventoryAndSync` (packets `1000`/`1001`) for free.
- **Block-entity wiring** (mirror `BlockEntityOpenableContainer`): `InventoryGeneric(n, id, api)`;
  in `Initialize` call `Inventory.LateInitialize(id, api)` + set `Inventory.Pos` (**mandatory** — this
  binds `InvNetworkUtil`; without it `SlotController.CanActivate` silently drops every click, logging
  `[gui] Skipped slot activation … not network-ready` — the #1 failure mode). Server round-trip:
  override `OnReceivedClientPacket`, forward `packetid < 1000` to
  `Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data)`. Persist via
  `Inventory.ToTreeAttributes`/`FromTreeAttributes`; drop-on-break via
  `Inventory.DropAll(Pos.ToVec3d().Add(0.5,0.5,0.5))` guarded by `Api is ICoreServerAPI`.
- **Slot accept filter** = a custom `ItemSlot` subclass overriding `CanHold`/`CanTakeFrom` (server-
  authoritative gate against hostile/automation/shift-click moves), NOT dialog-side validation.
- **Always** `UnwatchInventory` + `SlotController.Dispose()` in the view's `Dispose`, or the
  `SlotModified` handler leaks. Let the base handle open/close (duplicate-open guard); don't manually
  `OpenInventory`. (Research for add-scriptorium-inventory; no game-code change yet.)
- **Doc staleness found:** the `./reference/vslibgui/` clone this section points to is absent locally.

### `UpdateRenderObject`/`Configure` does NOT auto-invalidate paint — a `RenderProxyBox` must call `MarkNeedsPaint()` itself when a visual field changes (2026-08-16)

**Symptom: the Hardened (read-only) clay tablet never darkened under the illumination shade — but the
wet/editable tablet and the Lectern/Notebook did. Most visible on a tablet pulled empty from Creative
(no rows at all).** Root-caused from the decompiled `Gui.dll`, not guessed:

- LibGUI's `RenderObjectElement.Update(newWidget)` (in `Gui.Widgets.Framework`) calls `base.Update` then
  `UpdateRenderObject()` → `Widget.UpdateRenderObject(_renderObject)` — which for our wrappers lands in a
  `Configure(...)` that assigns fields. **That path does NOT mark the render object needing paint.** The
  SKPicture cache re-records ONLY when `NeedsPaint || ChildNeedsPaint` (both public on `RenderObject`, as
  is `MarkNeedsPaint()`).
- So a render object that changes its *visual output* purely from reconciled field values (here
  `ScribeGlobalTint.GlobalTintRender` — brightness/tint color-matrix) will keep painting the OLD cached
  picture until *something else* dirties it. Surfaces with live animation (a wet tablet's cuneiform
  caret, the Lectern's row slide-ins) mark needs-paint every frame and pick up the new value "for free"
  — which is exactly why they darkened and a **static** read-only surface did not.
- **Fix:** make `Configure` compare against the stored values and call `MarkNeedsPaint()` when any
  actually changed (the equality guard preserves paint-cache stability — unrelated `RebuildBody`s re-run
  `Configure` with the same shade and must NOT force a re-record). Same class of bug lurks in
  `ScribeGearEffect.Configure` (also no `MarkNeedsPaint`) but is masked there by the gears' per-frame
  `AnimatedRotation`; left as-is (not reported broken, and animation re-records it every frame anyway).
- **General rule:** any `RenderProxyBox`/`RenderObject` subclass whose paint depends on
  reconcile-supplied fields must self-invalidate in its update/Configure method; don't assume the
  framework does it. (Paired with the ctor-prime fix for the one-frame open FLASH: priming
  `currentShade = lightSampler.Sample(0f)` in the dialog ctor makes the FIRST recorded picture already
  dark; this note is the separate reason a static surface never re-recorded AFTER that.)

### An UNCONTROLLED StatefulWidget seeded only in `InitState` will NOT reflect a bound-value change delivered by an in-place reconcile — it needs a focus-gated re-seed in `UpdateWidget` (fix-craft-subtask-live-rescale, 2026-08-19)

**Symptom: a Craft parent's target stepper rescaled its ingredient subtask counts correctly IN THE DATA,
but the ingredient ROWS kept painting their OLD counts in the editor — the new numbers only appeared
after a view swap (edit↔read) or other forced redraw.** Root-caused by reading our own widget, not guessed:

- LibGUI reconcile reuses an Element+State when the widget's `(type, key)` matches at its tree position;
  reuse calls `State.UpdateWidget(oldWidget)`, NOT `InitState`. Only a genuinely-new element (new key, or
  a `ValueKey` remount) runs `InitState`.
- The editor keys its rows by `TaskId` (`new ValueKey<Guid>(b.TaskId)`), so a parent target step REUSES
  every ingredient row and its inner `ScribeNumericField`. `ScribeNumericField` is **uncontrolled**: it
  seeds `_currentValue` + its controller text from `Widget.Value` in `InitState` ONLY, on the documented
  assumption "the caller remounts it via a `ValueKey` when the value changes." The rescale path does a
  `RebuildBody()` reconcile (no remount) → the reused field never re-reads the new `Widget.Value` → stale
  count until a `ForceRebuild` (view swap) finally remounts it.
- **Fix:** give the uncontrolled state an `UpdateWidget` that re-seeds from `Widget.Value` when it changed
  from `oldWidget.Value` AND `!focusNode.HasFocus`. The `!HasFocus` gate is load-bearing: it live-updates
  the OTHER (unfocused) rows while never stomping the one field the player is actively editing (the focused
  parent stepper — which `RequestFocus`'d itself in `Adjust` before triggering the rebuild). This is the
  same shape as `ScribeEditRowState.UpdateWidget`'s optimistic-`done` resync (reuse → re-seed from the
  authoritative value, gated on it actually changing).
- **General rule:** "how do I live-update ANOTHER row in the editor while the user is there?" → resync that
  row's state from the reconcile-supplied widget value in `UpdateWidget`, gated on `!HasFocus` (or the
  relevant "not being edited" signal). Don't `ValueKey`-remount a field that might be focused (drops caret),
  and don't assume an uncontrolled `InitState`-seeded widget picks up a reconcile value on its own.

### Wrapping a title into a fixed band without a metric bump — bottom-anchor + two-line clip budget (2026-08-21)

To let a title wrap to N lines *inside an existing single-line band* (tablet title), don't grow the shared
band metric. Instead: size the title slot's `contentBoxH` to `TitleBtnsH + (N-1)·titleLineH`, set
`titleCrossAlign` to bottom so **line 1 grows UP into the band's existing headroom** (the band already has
slack — `TitleBarH = 0.13·H` exceeds the one content row `0.065·H`), and keep the enclosing `Clip` capped at
exactly N line-heights so a title longer than N lines clips cleanly at the end of line N (no partial line).
A tablet-only band-height override (`private protected virtual int TitleMaxLines`) was scaffolded as a
fallback but proved unnecessary — the two-line box fits within `TitleBarH` untouched, so base
(Lectern/Notebook) bands stay byte-identical. See `ScribeDialogBase.Layout.cs` `BuildTitleBar`.

## Held-item dialog flickers closed on FIRST open of a not-yet-crafted item (2026-08-06)

**Symptom: the first time a player opens a Scribe item they did NOT craft (notebook, clockmaker's
notebook, tablet), the dialog opens and immediately flickers closed; a second right-click makes it
stay. Reproduces for every item-hosted dialog; a self-crafted item does NOT flicker.**

Root cause is a collision between three correct-in-isolation behaviors, traced end-to-end in source
(not guessed — this is the "measure, don't theorize" class):

1. Opening a brand-new item generates a fresh `ScribeDocument`/`DocId` **client-side** in the host
   ctor (`NotebookHost`/`TabletHost`) and writes it to the local stack.
2. The open path fires `NotifyServerNotebookOpened`. Server-side, `OnServerReceivedNotebookOpened`
   adds the one-time "Picked up" `HistoryEntry`, `slot.MarkDirty()`s, and re-syncs the stack to the
   client — but **deliberately without the document** (`TryRecordPickedUpOnSlot` touches only
   `scribeHistory`, because the server doesn't know the client-generated DocId; stamping a
   server-random doc would make the owner's later edits get rejected on DocId mismatch).
3. That re-sync fires `IInventory.SlotModified` on the active hotbar slot. The dialog's
   `OnHotbarSlotModified` forwards to the close-guard `ActiveHandItemHostsThisDocument()`, which
   compares the re-synced stack's DocId to the open dialog's — they no longer match (the re-sync
   dropped/replaced the client doc), so it reads as "switched away" and calls `TryClose()`.

Second open doesn't flicker because the `PickedUp` entry now already exists →
`TryRecordPickedUpOnSlot` returns null → no `MarkDirty` → no `SlotModified` → guard never fires. And
a self-crafted item never flickers because the crafter is suppressed from the PickedUp entry.

**Fix pattern (see change `fix-item-dialog-first-open-flicker`):** the DocId-strict identity check is
right for `AfterActiveSlotChanged` (a real hotbar-slot-number change — "am I still holding the item
this dialog is for?") but WRONG for `SlotModified` (an in-place content rewrite of the slot I'm still
holding — "did the thing in my hand stop being a Scribe item?"). Split the two: keep
`OnActiveSlotChanged` on the DocId-strict guard; make `OnHotbarSlotModified` close only when the
active hand no longer holds ANY `IScribeDocumentItem` (a presence check, not identity). Avoid
frame-count/grace-period hacks — this project has moved away from timing-based GUI workarounds.
Note the tablet's legit wet→hard/fired transition ALSO rides `SlotModified`, so don't break it.

## "White flash" behind a Scribe dialog is a one-frame WORLD-TERRAIN dropout bound to dialog OPEN — INTERMITTENT, root cause STILL UNKNOWN as of 2026-08-13 (the "backdrop paint confirmed 2026-08-11" conclusion has since been downgraded; see the 2026-08-13 UPDATE at the end of this section). NOT a GUI white-clear, NOT a reconcile regression (2026-08-10)

> **READ THE 2026-08-13 UPDATE AT THE END OF THIS SECTION FIRST.** The header line and the "DISCRIMINATOR
> RESOLVED (2026-08-11)" + "Fix direction (§2)" blocks below record conclusions that were later revised:
> the flash is intermittent (NOT "every open"), the Pixel-Art-OFF discriminator is now doubted as ordering
> luck, the Route-1 pre-upload fix was tried and REVERTED as a regression, and the root cause is not
> confirmed. The measured facts (frames show an opaque-terrain dropout; bisect-pre-existing; localized to
> the backdropped surfaces) still stand — the interpretation of the *cause* is what changed.

**Symptom: opening a Scribe surface flashes WHITE for one frame before the dialog resolves.**
Originally reported as first-open-only and suspected as a `reconcile-animating-surfaces` regression.
BOTH of those first guesses turned out wrong; corrected below. This is the misdiagnosis-prone
render-state class — every claim here is either an in-game measurement or a source read, not a theory.

**What the frames actually show (OpenCV extract of the tester's capture, looked at directly):**
- The flash frame's **dialog is pixel-identical to its resolved state** — the GUI is NOT painting white.
- What's missing is the **opaque chunk-terrain pass**: near room geometry gone → the pale sky-dome
  gradient shows through (reads as "white"). Everything else survives ON that empty sky — distant
  entities (entity pass), the leaded-glass window's *glass* panes but not its opaque wooden frame (OIT/
  transparent pass), the block-selection wireframe, and the composited dialog. So exactly ONE render
  pass (opaque terrain) drops for one frame while all others render normally.

**Two first guesses, both DISPROVEN by measurement:**
1. *"First-open-per-session only" (cold GPU/asset hitch).* WRONG. In-game: it flashes on EVERY open of
   every Scribe item and block, same magnitude each time (confirmed by the tester + a 3-flash OpenCV
   scan, one flash per open, identical brightness). A once-per-session lazy cost (GRContext shader
   compile, backdrop `SKBitmap.Decode`/upload) would hit open #1 only — so those are NOT the cause.
2. *"Regression of the reconcile change."* WRONG — but ruled out the right way, by BISECT: built the
   pre-reconcile commit `5f6022a` (verified `ScribeDialogBody` absent) in an isolated worktree, staged
   it, tester confirmed the flash STILL happens. It is pre-existing, present before any of this branch.
   (The `git diff main...HEAD` also touches ZERO render/GL/Skia/stage code, consistent with this.)

**The discriminator that localizes it (in-game, tester-run):** the flash fires for the Lectern,
Notebook, and Tablet — but NOT for `.ui showcase` LibGUI windows, NOT when clicking inside an open
Scribe window, and NOT for the Scribe Settings window opened by the HUD gear. The Settings window is a
plain `ScribeSettingsDialog : GuiBase` that is *deliberately NOT wrapped in the pixel-art parchment
backdrop* (`ScribeSettingsDialog.Build`, comment lines 78-83); the three that DO flash all go through
`ScribeDialogBase`/`GuiDialogBlockEntityBase` AND paint the 1024×1160 parchment backdrop
(`WrapBackdrop`, `ScribeDialogBase.Layout.cs:88` — pixel-art ON → `BoxStyle { Texture = bmp }`; OFF →
plain `SizedBox`, no texture). So the isolated variable is **painting the parchment backdrop bitmap on
open** — NOT generic LibGUI (showcase is clean), NOT the Skia flush / shared framebuffer (same renderer,
clean for showcase/Settings), NOT block interaction (Notebook/Tablet are held items — `TryOpen()` only,
no `MarkDirty`/chunk touch).

**What it is NOT, from source (first-resort DLL/vendored reads):**
- `SystemRenderTerrain.OnRenderOpaque` has NO dialog gate — it always draws every chunk pool; the
  terrain-blank is the pools being momentarily EMPTY (a re-tesselation), not the engine choosing to hide
  terrain. `ClientMain.RedrawAllBlocks` (requeue every chunk) is the only "all pools empty" path, but its
  ONLY triggers are the `.redrawall` command + the `smoothShadows`/`instancedGrass` settings watchers —
  NONE fire on dialog open, so a full requeue is not the trigger either.
- `GuiManager.OnGuiOpened` only reorders the GUI list — touches zero render/framebuffer/chunk state.
- `SkiaRenderer.Begin/End` snapshots + restores GL state around its flush; it's shared with the clean
  `.ui showcase` path, so the renderer itself is exonerated.

**DISCRIMINATOR RESOLVED (2026-08-11, playtest `f79c21bf`, fix-dialog-open-white-flash §1.1):** opened a
flashing surface with Pixel Art Display toggled OFF (backdrop → plain `SizedBox`, no texture) and the
flash was **GONE** ("The flash is gone!"). So **painting the parchment backdrop bitmap on open is the
confirmed mechanism** — this is the last isolated variable, now nailed down by measurement, not theory.
The working hypothesis above is confirmed; the fix work moves to §2.

**Fix direction (§2):** pre-decode/pre-upload the backdrop as a persistent GPU texture at mod load so no
per-open cold upload lands on a live frame, and investigate why Skia's texture for it looks evicted
between closes → re-uploaded (the per-open re-upload is what stalls the opaque-terrain pass for a frame).
Do NOT add render-path/GL code to Scribe blindly; verify any fix with the DEBUG frame-trace method.
Related prior first-open work: `fix-item-dialog-first-open-flicker` (a DIFFERENT bug — dialog flicker-close
from a DocId guard, not this terrain dropout).

---

### 2026-08-13 UPDATE — the "backdrop paint / cold upload" root cause is FALSIFIED; still no confirmed cause. Change PARKED.

Everything above the divider is the 2026-08-10→11 investigation. This session tried the §2 fix and it
failed, and new tests revised the diagnosis. **Net: the flash is INTERMITTENT, bound to the dialog OPEN
transition, and we have no reliable repro and no confirmed root cause. Do not treat any theory below the
"measured facts" list as settled.**

**What was tried and FALSIFIED (don't re-run these as fixes):**
1. **Cold GPU texture UPLOAD** (the §2 "Route 1" fix: pre-upload every backdrop to a resident
   `LoadedTexture` at `BlockTexturesLoaded`, draw it GPU-resident via `SKImage.FromTexture`). Implemented
   fully. The flash **still occurred on first open**. A channel-swap bug rendered the backdrops blue,
   which usefully *proved* the `FromTexture` path was live (not the fallback) — so the upload really was
   pre-warmed, and pre-warming did nothing. Combined with the already-measured **size-independence** (a
   72 KB backdrop flashed as hard as a 4.75 MB one — far too small an upload to stall a frame), the
   cold-upload theory is dead.
2. **Cost of the first backdrop DRAW.** Falsified in-game: toggling Pixel Art **ON while a dialog is
   already open** — i.e. the session's *first* backdrop image draw — does **NOT** flash. Only *opening* a
   dialog flashes. So the cost is in the OPEN transition, not in drawing/uploading the backdrop bitmap.
3. **GPU driver shader-cache warmup across process launches** (my hypothesis after a 3-relaunch test where
   launch 1 flashed and launches 2–3 were clean). **Rejected by the tester**, who has repeatedly seen the
   flash reproduce across a full open→flash→**quit**→relaunch→flash-again cycle — which a persisted
   driver cache would prevent. Unsupported; do not assert it.

**The Route-1 warm was a REGRESSION — reverted.** Holding 13 resident GL textures made the flash appear on
the **Pixel-Art-OFF** path (which §1.1 had shown was flash-free) and made it fire on *every* open per
art-config, not just the first — almost certainly by perturbing the shared `GrContext` state each open.
Reverted §2.3–2.6; **kept §2.2** (the harmless polish: `ScribePixelArtBackdrop` drawing via
`SKImage.FromBitmap` with NEAREST sampling for crisp pixel-art upscale, `SetImmutable()` on decoded/
tinted/procedural backdrop bitmaps for upload caching, and the native 128×145 notebook re-export).
**Guardrail learned: do NOT re-introduce resident-texture pre-warming for this bug.**

**§1.1's "Pixel Art OFF removes the flash" is now DOUBTED** — likely ordering luck (the cold open-cost was
already paid on an earlier art-ON open, so the art-OFF *reopen* was warm), not a genuine causal
discriminator. It was a single observation; the intermittency + fact #2 above undercut it.

**Measured facts that STILL hold (safe to rely on):** (a) the flash frame is a one-frame **opaque-terrain
pass dropout**, GUI pixel-identical (OpenCV); (b) **bisect-pre-existing** on `5f6022a`, orthogonal to all
Scribe render code; (c) localized to the three backdropped surfaces (Lectern/Notebook/Tablet), never the
`.ui` showcase or the Settings window; (d) bound to dialog **OPEN**, not to in-dialog art toggling; (e)
**intermittent** — appears and vanishes across sessions with no code change.

**RESUME PLAN (when it next reproduces):** do these IN ORDER, and do NOT write a fix before step 2.
1. **Pin a reliable repro.** Record the exact sequence: cold boot vs. warm? which surface opened first?
   Pixel Art on or off? single-player vs. multiplayer? does a full quit→relaunch reflash? — the thing we
   have never had is a deterministic trigger.
2. **Frame-trace the offending OPEN frame** (DEBUG frame-trace method,
   [[libgui-settling-loops-and-race-diagnosis]]) to see WHAT on the main render thread stalls long enough
   to drop the opaque-terrain pass. Still-open candidate mechanisms: a Skia GL program/pipeline compile on
   first draw of a given config; LibGUI surface (re)alloc + `GrContext.ResetContext` on open; something in
   VS's world renderer reacting to a new dialog registering. Measure — we have falsified 3+ theories by
   guessing.
Full narrative + code state: memory [[white-flash-is-world-render-stall]] and
`openspec/changes/fix-dialog-open-white-flash/tasks.md` §4.1.

## Sampling the light reaching the player (brightness + color) — the engine uses TWO inputs, not one (2026-08-12)

**Task: shade a GUI (or anything client-side) by the real light around the player — how bright AND what
color.** The mistake is to reach for a single scalar. The engine's own recipe (`IRenderAPI
.PreparedStandardShader(posX,posY,posZ)`, which is what standard surfaces are lit by) combines TWO
sources, so a faithful mod-side approximation must too:

- **`IBlockAccessor.GetLightRGBs(pos)` → `Vec4f`** (also `GetLightRGBs(x,y,z)`). **XYZ = block-light RGB**
  — a torch/lantern's warm hue is baked in here via each block's `LightHsv`, so this is where torch
  *warmth* comes from. **W = the sun-brightness SCALAR (0..1)**, NOT a color. So `GetLightRGBs` ALONE makes
  daylight look colorless (W has no hue) and misses weather. Fed to the shader as `RgbaLightIn`.
- **`ICoreClientAPI.Ambient` (`IAmbientManager`)** supplies what the block grid lacks: **`BlendedAmbientColor`
  (`Vec3f`)** = the sky/daylight HUE, and **`BlendedSceneBrightness` (`float`)** = weather/rain darkening.
  Fed to the shader as `RgbaAmbientIn`. These vary SMOOTHLY frame-to-frame (unlike the grid).

Combine them yourself: brightness ≈ `max(blockLightLuma, sunW * sceneBrightness)`; hue ≈ block-light RGB
blended toward `BlendedAmbientColor` weighted by which is actually lighting the player. `GetLightLevel(pos,
EnumLightLevelType.MaxTimeOfDayLight)` is a 0..32 scalar with NO color — not enough on its own.

**Thread safety:** read `GetLightRGBs` on the RENDER/MAIN thread only. Relighting runs on a background
thread; an off-thread block-accessor read races it. `OnRenderGUI` is a safe point.

**Quantize before it drives a cached paint.** LibGUI caches each dialog's tree in an `SKPicture` and only
re-records on `MarkNeedsPaint`; a continuously-varying light value would re-record EVERY frame and defeat
the cache (measurable hitch on the pixel-art parchment backdrops). Snap brightness+hue to coarse buckets and
only propagate a CHANGED bucket. The grid's own sun-brightness is already a 0..32 lookup, so this loses
little. (Scribe: `ScribeAmbientLightSampler` + `ScribeGlobalTint`, respect-local-illumination.)

**Flickering / dynamic PLACED point lights are UNREADABLE via public API.** VS dynamic lights are the
`IPointLight` system — SHADER-ONLY: `IRenderAPI.AddPointLight/RemovePointLight` have no getter, and the
active list is `internal List<IPointLight> pointlights` on `ClientMain`. A *steady placed* torch/lantern
still registers (it's baked into the block-light grid `GetLightRGBs` reads); only the per-frame flicker of a
*placed* source is missed, and reading it would mean reflecting into a private engine field — deferred.

**BUT a HELD light's flicker (Immersive Lanterns) IS readable — for free.** IL (decompiled 0.4.1) is NOT a
private point-light system: `ModSystemImmersiveLanterns.SetupFlickerPatching()` Harmony-**Postfixes
`CollectibleObject.GetLightHsv(IBlockAccessor, BlockPos, ItemStack)`** (+ `BlockLantern`'s override). That
patch (a) **early-returns when `pos != null`** — so it flickers HELD/inventory items only, never placed
blocks (a placed-grid query passes a real pos); (b) modifies **V (brightness index) ONLY**, never H/S — pure
brightness flicker, no hue shift; (c) sine-flickers between a min/max factor (torch `0.75..1.0` over
100–300ms; lantern/candle/lamp `0.75..1.0` over 500–1000ms), all amplitudes/cadences read from VS
`ClientSettings` `flickeringlights-*` keys, tunable in IL's own config dialog. So if you already sample a
held item's light via `GetLightHsv(blockAccessor, pos: null, stack)` (as `TryHeldLight` does), **you receive
IL's flickered V every frame with no dependency, reflection, or flicker-matching code** — the only thing that
can erase it is your OWN temporal smoothing. Detect IL with `capi.ModLoader.IsModEnabled("immersivelanterns")`
and, when present, stop low-pass-filtering the held-brightness term so the flicker survives (Scribe:
unify-held-light-flicker splits the held term out of `ScribeAmbientLightSampler`'s ~400ms ease). Depends only
on the *observable* result (a flickering `GetLightHsv` for held items) + the stable modid, not IL internals,
so it degrades gracefully if IL changes its patch shape.

## Item state-transition (`Harden`/`Dry`) and firepit smelt both DROP stack attributes on transform (2026-08-02)

**Symptom: a tablet/food item that "becomes" another item over time (dries, hardens, fires) loses its
custom stack attributes (our `scribeDocument`, `fired`, etc.) at the moment of transformation.**

VS has a native time-based transition system separate from firepit smelting: `EnumTransitionType` includes
`Perish, Dry, Burn, Cure, Convert, Ripen, Melt, Harden, None`, and `TransitionableProperties` (declared in
JSON as `transitionableProps` / `transitionablePropsByType`) carries `FreshHours` (stays in current state),
`TransitionHours` (how long the transition takes once it starts), and `TransitionedStack` (the resulting
item). This is the engine-native way to model "dries out over N hours into another item" — e.g. clay tablet
`Harden`: `FreshHours ≈ 48` game-hours wet, then `TransitionHours` to become the hard variant. The engine
ticks it via `UpdateAndGetTransitionStatesNative` (server-side; skipped for `ItemSlotCreative` and when the
stack attr `timeFrozen` is set), tracking `createdTotalHours`/`transitionedHours` under the stack's
`transitionstate` tree attribute against `world.Calendar.TotalHours`.

BUT the conversion has the SAME gotcha as firepit `DoSmelt`: `CollectibleObject.OnTransitionNow(slot, props)`
(virtual) does `props.TransitionedStack.ResolvedItemstack.Clone()` and the caller `SetFrom`s it — it clones
the FIXED output stack and does NOT copy the input's custom attributes. So a plain-JSON `Harden` would dry a
tablet into a *blank* hard tablet, dropping the document. (`DoSmelt` clones `combustibleProps.SmeltedStack`
the same way — confirmed separately for the firing mechanic.)

**Fix pattern:** override `OnTransitionNow` (for drying) and `DoSmelt` (for firing) on the item; let base
build the output, then copy `scribeDocument` (+ set/clear our state flags) from the input onto the output,
guarding nulls. Note transition is NOT inherently reversible — rehydration (hard→wet) is a separate action
you implement yourself (e.g. an interaction that swaps the stack back to the wet variant and re-copies the
document), not an engine feature.

## A near-opaque body texture (stray alpha 252–254) demotes a mesh into the WBOIT transparent pass, so an overlaid semi-transparent layer bleeds THROUGH it (2026-08-07)

**Symptom: the wax tablet's semi-transparent "writing" layer (dark etched marks, alpha ~124) applied its
transparency to the wax BODY underneath it — you could see through the whole wax slab to the ground/item it
rested on, and the show-through shimmered as the camera moved. The IDENTICAL writing layer on the clay
tablet rendered correctly (opaque body, marks etched on top).**

Root cause was NOT the model (clay and wax share the same thin `#writing`-textured element floating just above
the body). It was the BODY texture's alpha channel. VS routes a mesh to a render pass based on its texture
alpha (`EnumChunkRenderPass`): a fully-opaque texture → `Opaque` pass (depth-writing, occludes); ANY sub-255
alpha → the `Transparent` pass, which is **Weighted Blended Order-Independent Transparency (WBOIT)** — it does
not occlude the same way, so a translucent layer in front blends against whatever is BEHIND the slab instead
of against the slab. `scribe-wax-32.png` had **92 stray pixels at alpha 252/254** (an imperceptible art-export
artifact, scattered edge-to-edge across the 32×32) — enough to push the entire wax body into the transparent
pass. `ff.png` (clay body) was 100% alpha-255, so clay stayed opaque and looked right.

**Fix:** flatten the body texture's alpha to a single value of 255 (any pixel with `0 < a < 255` → `a = 255`).
The 252→255 shift is visually undetectable but moves the slab back to the opaque pass, so the writing layer
blends against solid wax again. Diagnose this class of bug by measuring the texture's alpha histogram, not by
theorizing about the model — a "why is only ONE of two near-identical items translucent?" question is almost
always a stray-alpha / render-pass split, not geometry. (Kept the writing texture semi-transparent — that
layer is *supposed* to be in the transparent pass; only the opaque body must be truly opaque.)

## `*ByType` resolution DEEP-MERGES onto the base block; arrays CONCATENATE (2026-08-07)

**Symptom: a per-variant `handbookByType`-style list (or any array) declared in BOTH a base block and
a matching `attributesByType` branch shows up DOUBLED in-game (e.g. a hardened tablet listed 6 handbook
sections — the base's 3 + the branch's 3, with entries repeated).** Also relevant when deciding whether
per-branch property duplication is even necessary.

The `*ByType` suffix (`attributesByType`, `texturesByType`, `handbookByType`, `shapeByType`, …) is
resolved by `RegistryObjectType.solveByType` in **VSEssentials.dll**
(`Vintagestory.ServerMods.NoObf.RegistryObjectType`; dump with
`ilspycmd -t Vintagestory.ServerMods.NoObf.RegistryObjectType "/Applications/Vintage Story.app/Mods/VSEssentials.dll"`).
For each key `"<x>ByType"`, it wildcard-matches the item's full variant code against the branch patterns,
takes the FIRST match, then:

```csharp
JToken obj = val["<x>"];                    // the sibling base block, if any
if (obj is JObject existing)
    existing.Merge(matchedBranchValue);     // <-- DEEP MERGE, no JsonMergeSettings
else
    val["<x>"] = matchedBranchValue;        // replace only when there is NO base block
```

So it does **NOT replace** a sibling base block — it deep-merges the matched branch *onto* it. Two
consequences, both verified empirically against the game's own `Lib/Newtonsoft.Json.dll`:

- **Object keys OVERWRITE.** A branch property whose value equals the base value is a pure no-op —
  duplicating an identical transform object across branches buys nothing. Put the shared value in the
  base block once; only branches that genuinely DIFFER need their own copy (e.g. our `*-wax` tablet keeps
  its own `onshelfTransform`/`displayable` because its mesh is smaller than the clay mesh).
- **Arrays CONCATENATE.** `JObject.Merge` with no settings uses `MergeArrayHandling.Concat` by default, so
  a branch array *appends* to the base array rather than replacing it. This is the doubling bug above.
  **Fix: don't keep a base array AND a per-branch array of the same key.** Either (a) put the whole thing
  in one place, or (b) move it entirely into a `<x>ByType` map with a `"*"` catch-all branch for the
  otherwise-unmatched variants — because `...ByType` sets the key from the matched branch (replace),
  there's no base sibling to concat with, so each variant gets exactly its own list.

Note the wildcard match takes the FIRST matching branch, so order matters and a broad `"*"` must come
LAST. Test the resolution by faithfully porting `solveByType` and running it against the real JSON (a
~20-line C# harness referencing the game's Newtonsoft DLL) rather than guessing — that's how the merge
semantics above were confirmed after an earlier note wrongly assumed `attributesByType` replaces the base.

## Two different `BadImageFormatException` crashes — don't conflate them

There are TWO distinct `BadImageFormatException` crashes on this project; they have different error
text, different trigger points, and only one is unfixable. Check the message and the triggering frame
before assuming it's the engine bug:

| Message | Trigger | Cause | Fix |
|---|---|---|---|
| `...: Bad IL range` | Closing a LibGUI dialog (Esc / title-bar close) — but ALSO seen on `OnBlockBroken` / `OnBlockPlaced` and any other first-time lazy-JIT of a VS method | VS engine bug under Rosetta (see below) | None from mod code |
| `An attempt was made to load a program with an incorrect format. (0x8007000B)` | Any lazy-JIT of a mod method (e.g. `CollectibleObject.OnHeldUseStart` on right-click) | **Restage-while-running** — `restage.sh` overwrote the memory-mapped `Scribe.dll` under the live process, so the CLR JIT read a torn/inconsistent assembly | **Fully quit + relaunch the client.** Self-inflicted by the dev loop, not a code bug |

**The `0x8007000B` one is the common false alarm.** It looks alarming and shares the exception type
with the Rosetta crash below, but it only means you restaged while the game was open (exactly what
`restage.sh`'s "fully quit and relaunch" reminder warns against). The DLL loads cleanly on the next
launch; nothing in the mod or the committed build is wrong. It fires on the first JIT of a mod method
after the swap — often a held-item right-click (`OnHeldUseStart`), a block interaction, or opening a
dialog — so the frame varies. Distinguish it from the engine bug purely by the message string.

## Platform: `BadImageFormatException: Bad IL range` on Apple Silicon

**Symptom: the game crashes with `System.BadImageFormatException: Bad IL range` when closing a
LibGUI dialog (Escape key or clicking the title-bar close button). Stack trace contains frames in
`GestureDetector`, `WindowTitleBar`, `EventDispatcher`, and/or `GuiDialog.TryClose` / `GuiManager`.
Garbled/binary symbols appear alongside readable frame names. Crashes are consistent and reproducible
on an M-series Mac; the dialog worked fine on Intel.**

This is a Vintage Story engine bug, not a Scribe or LibGUI bug. Root causes confirmed via 97-agent
deep-research (2026-07-27):

1. **VS ships x86_64 Mach-O, runs under Rosetta 2 on Apple Silicon.** `lfile "/Applications/Vintage
   Story.app/Vintagestory"` → `Mach-O x86_64`. GitHub issue #8905 (anegostudios/VintageStory-Issues,
   July 2026, M3 MacBook Air, v1.22.3) confirms this; `.ips` crash reports show `translated=true`
   (Rosetta flag). The VS team acknowledged the issue and pointed to a native ARM64 build as the fix.
   No ARM64 build had shipped as of this writing; check the VS changelog for any version after 1.22.3.

2. **The exception is a pre-JIT PE loader validation failure, not an ARM64 JIT bug.**
   `PEAssembly::GetIL()` in CoreCLR calls `CheckILMethod()` (in `cordecoderhelpers.h`), which
   validates IL method header bounds against the mapped PE image before passing IL to the JIT.
   Obfuscated VS DLLs (`Vintagestory.Client.NoObf` namespace — the obfuscation is evident in the
   garbled frame names) produce method headers whose size fields create arithmetic overflow in the
   bounds check (`S_UINT32` overflow-safe arithmetic), triggering `COR_E_BADIMAGEFORMAT /
   BFA_BAD_IL_RANGE`. This happens before RyuJIT ever sees the IL.

3. **Not the .NET PR #102799 fix.** That PR is scoped entirely to NativeAOT's compile-time
   `ILCompiler.Compiler` — not CoreCLR's runtime JIT. Unrelated.

4. **Why the crash surfaces on dialog close:** the closing code path (LibGUI event chain dispatching
   into `GuiDialog.TryClose`) triggers JIT compilation of a VS obfuscated method whose IL fails the
   bounds check. The LibGUI frames are in the stack because LibGUI dispatched the close event; the
   actual failure is in VS's own code. No fix is possible from Scribe's or LibGUI's side.

5. **No confirmed workaround.** `DOTNET_TieredCompilation=0` did NOT survive adversarial
   verification (0-3 refuted). The only fix is a native ARM64 VS binary.

**Not always dialog-close.** Observed 2026-08-16 firing on `BlockEntityScriptorium.OnBlockBroken`
(server: `[Error] Exception: Bad IL range`) and, on the client, `Block.OnBlockPlaced → ClassRegistry.
CreateBlockEntity` throwing `MissingMethodException: No parameterless constructor defined for type
'<garbled-lambda-closure-name>'`. The garbled type is a compiler-generated closure (`b__NNN_0`), NOT a
real BE class — the class token resolved to garbage, the same PE-loader metadata corruption as the
dialog-close case, just on a different first-JIT frame. Any not-yet-JIT'd VS method can trip it; break /
place are just methods that hadn't compiled yet in a long, heavily-modded session. **A restage-while-
running tear (the `0x8007000B` row above) presents almost identically** — if the staged `Scribe.dll`
mtime falls INSIDE the live session's runtime (game launched, then `restage.sh` ran under it), suspect
the torn-assembly variant first; the fix is the same (fully quit + relaunch), but that one is
self-inflicted, not the Rosetta bug.

**Fix pattern:** none actionable from mod code. Don't re-investigate if this crash resurfaces —
check whether a post-1.22.3 VS release ships a native ARM64 build. Mention this in any LibGUI
author outreach (task 9.4 of `v1-release-checklist`) — they may want to flag it in LibGUI docs as
a known platform limitation while the VS ARM64 build is pending.

---

## Sound playback volume — `SoundParams.Volume` clamps to `[0,1]`, `SetVolume` does not (2026-08-21)

Two engine facts (decompiled) constrain GUI/one-shot sound volume:

- `Vintagestory.API.Client.SoundParams.Volume`'s **setter hard-clamps to `[0f, 1f]`** — assigning >1 stores
  exactly 1. A "5× via `Volume`" is impossible.
- `LoadedSoundNative.SetVolume(float val)` stores the clamped value into `soundParams.Volume` but passes the
  **raw** `val` to OpenAL as `AL_GAIN = val * GlobalVolume`. OpenAL honors gain >1, but amplifying past unity
  risks implementation-dependent clipping/distortion against the source's headroom.

**Takeaway:** to make a quiet source louder, **bake the gain into the audio file** and keep runtime gain in
`[0,1]` — don't rely on `SetVolume(>1)`. Precedent: `ScribeAlarmSound` / the transcribe stamp cue both load
via `capi.World.LoadSound(new SoundParams{...})` and drive volume with `SetVolume` reading a `/100f` setting.

---

## Dev-diagnosis toolkit

The local iterate/diagnose loop for this project (Apple Silicon, where VSImGui sliders and the
ConfigLib panel are both dead — see "VSImGui debug overlay" and the ConfigLib-freeze memory).
This section is the standing reference for the tools; the change that established them is
`openspec/changes/improve-testing-and-diagnosis`.

**Log helper — watch the `[scribe]` trace live.** `build/scribe-log.sh` follows the game logs and
filters to Scribe-relevant lines: the mod's `[scribe]`-prefixed **server** trace (emitted at
Notification level by `ScribeModSystem.Trace`) plus asset/mod-load errors. Use it when
diagnosing whether a server-authoritative round-trip actually landed — e.g. the pin/complete
network flow. `--client` also follows `client-main.log`; `--all` drops the filter. Logs live at
`VintagestoryData/Logs/` (`VINTAGESTORY_DATA` overrides). Note the current game writes
`*-main.log`; older docs say `*-main.txt` — the helper resolves whichever exists.

**Dev-world launch profile.** For a consistent diagnostic baseline, start playtests from a flat
creative superflat world with developer mode on, rather than hand-toggling each session:

- **World:** New world → world type **Superflat** (or preset "Flat"), game mode **Creative**.
  Flat + creative = no terrain/survival noise around the lectern under test, instant block
  access, and free break/replace for the pin break→replace checks.
- **Developer mode:** enable it so dev-only commands and extended diagnostics are available.
  In `VintagestoryData/clientsettings.json` set `"developerMode": true` (or toggle it in
  Settings → once on, it persists). This is the gate for the finer debug toggles below.
- **Extended debug info:** `.debug wireframe`, the F3-style overlays, and `.clientconfig
  extendedDebugInfo true` surface render/selection state; the **error reporter** dialog
  (`.clientconfig ` toggles / Settings → "Show error reporter") makes a mid-session exception
  visible instead of silently spamming the log.
- Pair with `build/scribe-log.sh` in a side terminal so the server trace and the in-game world
  are visible together.

**Fast in-game reload (avoid a full relaunch).** These reload assets in the *running* client —
they do NOT reload mod C#:

- `.reload textures` / `.reload shapes` / `.reload shaders` / `.reload lang` — re-read that asset
  category from disk. Good for icon SVGs, block shapes, and lang-file edits (our custom icons live
  at `assets/scribe/textures/icons/`; lang at `assets/scribe/lang/`). Asset-only edits then need
  no relaunch — but a restage copies assets into the Mods folder, so run `build/restage.sh`
  (or the assets step of it) first, then `.reload`.
- `CTRL+F1` — reload the current world client-side (re-runs chunk/world load) without dropping to
  the main menu; faster than a relaunch when you only need the world rebuilt.

**C# Hot Reload does NOT work for this mod setup — do not sink time into it.** Confirmed from the
game's own runtime config, not guesswork: `/Applications/Vintage Story.app/Vintagestory.runtimeconfig.json`
sets `"System.Reflection.Metadata.MetadataUpdater.IsSupported": false`. That flag is exactly what
.NET Hot Reload / Edit-and-Continue requires; with it false the runtime refuses metadata updates,
so no `dotnet watch`/IDE Hot Reload session can apply a code change into the running game —
independent of how the `ModLoader` loads the mod DLL. Any C# change therefore requires a rebuild +
restage + **full client relaunch** (mod assemblies and lang/assets load once at boot). The
`.reload` commands above are the only in-session shortcut, and only for assets. If a future VS
build flips that flag to `true`, revisit — until then, the loop is: `build/verify.sh` (or
`restage.sh`) → relaunch → `build/scribe-log.sh`.

## `RegisterCallback` while paused throws in developer mode (2026-08-23)

**Symptom: `System.Exception: Call to RegisterCallback while game is paused` with developer mode
+ extended debug info on, stack pointing at a Scribe `RegisterCallback` from a `SlotModified`
handler (e.g. a LibGUI inventory click while a Scribe dialog is open).**

`IEventAPI.RegisterCallback(Action<float>, int)` forwards to
`ClientMain.RegisterCallback(..., permittedWhilePaused: false)`. If `ClientMain.IsPaused` is
true, that logs a Notification and, when `ClientSettings.DeveloperMode && extendedDebugInfo`,
**throws**. The 3-arg overload
`RegisterCallback(Action<float> OnTimePassed, int millisecondDelay, bool permittedWhilePaused)`
skips the throw when the third argument is `true`. Delayed callbacks are scheduled against
`EventManager.InWorldEllapsedMs`, so even a permitted-while-paused callback will not fire until
the world clock advances (unpause). For a UI refresh that must happen during a paused inventory
click, recompute immediately when `capi.IsGamePaused` instead of waiting on the delay.

**Fix pattern:** UI-only delayed work should register with `permittedWhilePaused: true`, and if
the handler is running *already paused*, do the work now rather than queueing. See
`ScribeDialogBase.TrackerCount.OnTrackerSlotModified`.

## Minimap on/off setting key

**Reading whether the minimap is currently shown:** `capi.Settings.Bool["showMinimapHud"]`. This key
is written by `Vintagestory.GameContent.GuiDialogWorldMap` (in `VSEssentials.dll`) when the player
toggles the minimap — it is NOT set in `ClientSettings`'s constructor defaults, so `Exists()` returns
false on a fresh install. The `SettingsClass<bool>` indexer returns `defaultValue` (= `false`) for a
missing key, so a bare `Bool["showMinimapHud"]` would incorrectly read as "off" on first launch.
**Safe pattern:** `!capi.Settings.Bool.Exists("showMinimapHud") || capi.Settings.Bool["showMinimapHud"]`
(absent = on by default). `ISettingsClass<T>` exposes `.Exists(key)` backed by `values.ContainsKey`.

## Atlas integration harness — next-adoption notes

We already depend on Atlas (the headless-server integration suite, `tests/Integration.Tests`);
these are 0.11.0 capabilities we do NOT yet use but should adopt as the suite grows (source +
wiki cloned to `reference/atlas` / `reference/atlas-wiki`, gitignored):

- **`ExecuteCommand` result assertions** — drive and assert on in-game chat/server commands from a
  scenario (not just block/entity state). Worth adopting to test the completion/pin flow through a
  command surface, and any future admin/debug command the mod adds, end-to-end.
- **`atlas diff` differential regression** — snapshot a world/state and assert the *delta* across a
  run, rather than hand-asserting each field. Worth adopting for persistence/migration scenarios
  (e.g. v3→v4) where "nothing else changed" is the real invariant and is tedious to assert field by
  field. See `reference/atlas-wiki/` (`CLI.md`, `Writing-Scenarios.md`).

**Symptom: `ButtonState.PlaySound()` NullReferenceException when a `Button` widget is tapped inside a dialog that calls `ForceRebuild()` from a tick listener.**

`ButtonState` (shipped `Gui.dll 3.1.0`) caches the `ISoundPlayer` from `Element.Owner` during mount/build. When `GuiBase.ForceRebuild()` is called at high frequency from a tick listener (e.g. every 250 ms to update a countdown), the button is repeatedly unmounted and remounted. During one of those remounts, `Element.Owner` may be transiently null, leaving the cached sound player null. The next tap calls `PlaySound()` on the null reference.

**Fix pattern:** Never call `ForceRebuild()` from a high-frequency tick listener when the rebuilt tree contains `Button` widgets. Instead, drive countdown display with a self-owned `StatefulWidget` (like `ScribeFadeText`) that calls `Element.MarkNeedsBuild()` directly — this diffs against the existing element tree without full unmount/remount. For server-pushed updates (1 s cadence), `ForceRebuild` is safe because it's infrequent enough. See `GuiDialogClockmakerNotebook.cs`.

## Injecting content into ANY item's handbook page — patch `GetHandbookInfo`, not the dialog (2026-08-08)

**Goal: add a line/link (e.g. "→ Add to task") to the vanilla handbook page for every item, in a
way that survives handbook-*replacement* mods.** There is **no vanilla event/hook** for "append to a
handbook page" — the only seam is a Harmony patch. Confirmed by decompiling **Tallybook 0.3.6**
(`~/Downloads/tallybook_0.3.6.zip` → `Tallybook.dll`, class `HandbookPin`).

- **Patch target: `Vintagestory.GameContent.CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo`**
  (in `VSSurvivalMod.dll`), a **`Postfix`**. This is the per-*item* method that builds the page body
  as a `RichTextComponentBase[]`. Signature seen in the wild:
  `Postfix(ItemSlot inSlot, ICoreClientAPI capi, ref RichTextComponentBase[] __result)`. The postfix
  reads `inSlot.Itemstack`, then appends to `__result` (rebuild the list, `list.Add(...)`, reassign).
- **Append a `LinkTextComponent`** for a clickable action:
  `new LinkTextComponent(capi, "→ Add to task", CairoFont.WhiteSmallText(), onClickAction)`; a
  `ClearFloatTextComponent(capi, 12f)` before it gives spacing. The click delegate captures a
  **cloned** `ItemStack` (`stack.Clone()`) — this is how you get the **exact variant** the page is
  showing (a search-picker's `new ItemStack(collectible)` only yields the *base* variant).
- **Why this is robust to handbook-overwrite mods:** it patches the *collectible's content-builder*,
  not `GuiDialogHandbook`. Any dialog (vanilla `GuiDialogSurvivalHandbook`, or a third-party
  replacement) that shows item pages still calls `GetHandbookInfo` for the body, so the injected
  content appears regardless of which dialog "won." And a `Postfix` *appends* — multiple mods can
  postfix the same method without excluding each other (unlike dialog-replacement mods that stomp one
  another). Only fails if a mod rewrites/bypasses `GetHandbookInfo` itself.
- **Harmony is already in the game — not a new dependency.** It ships at
  `/Applications/Vintage Story.app/Lib/0Harmony.dll`; `using HarmonyLib;` needs zero download and no
  `modinfo.json` dependency entry. Guard the whole `Apply()` in try/catch and log a warning on
  failure so a missing/renamed method just disables the button instead of crashing (Tallybook does
  exactly this). `harmony.UnpatchAll("<your-id>")` on dispose.
- **Finding the open handbook dialog** (to programmatically open/return to it):
  `capi.Gui.LoadedGuis.OfType<GuiDialogHandbook>().FirstOrDefault()`, or reflect the private `dialog`
  field off `ModLoader.GetModSystem<ModSystemSurvivalHandbook>()`. To open it "like the player would,"
  invoke the `handbook` / `survivalhandbook` / `guihandbook` hotkey handler.
- **You CANNOT put a real button inline in a handbook page.** The page body is rich-text
  (`RichTextComponentBase[]`); the only inline-clickable component is `LinkTextComponent` (clickable
  text). A genuine button is only possible as a **separate floating `GuiDialog` overlay** that anchors
  itself to the handbook window each tick — this is how Tallybook draws "← Back to Tallybook"
  (`HandbookReturnButton`: a `GuiDialog` with `AddShadedDialogBG` + `AddSmallButton`, a tick listener
  that finds the handbook's largest composer bounds and re-`Compose`s at that position). Caveats:
  such an overlay floats *detached* from any specific item (it reflects the currently-open page, can't
  sit on an item's line or scroll with the page), and a *vanilla* `AddSmallButton` overlay hits the
  macOS top-left-quadrant hit-test bug (a LibGUI overlay avoids that but is still detached). For an
  affordance that belongs to a specific item, prefer the inline `LinkTextComponent` + `IconComponent`.
- **Embedding a CUSTOM SVG icon inline in the appended text** — use `IconComponent` (a
  `RichTextComponentBase`, `VintagestoryAPI.dll`): `new IconComponent(capi, iconName, iconPath, font)`.
  When `iconPath` is set it loads that asset via `capi.Assets.TryGet(new AssetLocation(iconPath)
  .WithPathPrefixOnce("textures/"))` and draws it with `capi.Gui.DrawSvg(asset, …, ColorFromRgba(font.Color))`
  — i.e. a mod's own SVG, tinted to the font color, sized to `font.UnscaledFontsize * sizeMulSvg`
  (default 0.7). This is the *same* `DrawSvg` path Scribe already uses for its icon-buttons, so a
  Scribe glyph can prefix a handbook link. (If `iconPath` is null it falls back to a named vanilla
  icon via `capi.Gui.Icons.DrawIcon`.) Add it to the component list right before the `LinkTextComponent`.
- **"Is this item craftable?" — one-time index over `capi.World.GridRecipes`.** No direct
  "recipes-for-output" API; build it yourself (Tallybook's `RecipeProbe` does exactly this): walk
  `((IWorldAccessor)capi.World).GridRecipes` once into a `Dictionary<outputShortCode, List<GridRecipe>>`
  (key = `recipe.Output.ResolvedItemStack.Collectible.Code.ToShortString()`), cache it, invalidate on
  recipe reload. Craftability = key lookup. Tallybook also skips recipes that *consume their own
  output* (self-cycle guard). **⚠️ `GridRecipes` is GRID CRAFTING ONLY** — it excludes smelting,
  cooking, knapping, clayforming, barrel, and firepit recipes (each is a separate
  `capi.World.*Recipes`-style registry). A gate built on `GridRecipes` alone will report "not
  craftable" for a smelted ingot or knapped tool head. Same limit bounds any recursive
  ingredient-graph walk.

**Confirmed SHIPPED 2026-08-15 (add-tracker-link-tasks, `ScribeHandbookPatch`).** The above was
exercised for real by the Tracker/Link "Add to Scribe" links, and everything held:
- **Exact signature (this game version):**
  `public virtual RichTextComponentBase[] GetHandbookInfo(ItemSlot inSlot, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)`.
  The method body ends `return list.ToArray()`, so the postfix param `ref RichTextComponentBase[] __result`
  is the full page. Attribute-match on the name alone works (only one overload):
  `[HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), nameof(...GetHandbookInfo))]`.
- **Append-only, allocate a fresh array:** copy `__result` into a new `RichTextComponentBase[old + n]`,
  place the appended components after, reassign `__result`. Never mutate existing entries — a page with
  no Scribe content stays byte-identical to vanilla.
- **Clickable-link ctor confirmed present in `VintagestoryAPI.dll`:**
  `LinkTextComponent(ICoreClientAPI api, string displayText, CairoFont font, Action<LinkTextComponent> onLinkClicked)`
  (alongside the `LinkTextComponent(string href)` nav ctor). The `onLinkClicked` delegate captures the
  page's `inSlot.Itemstack.Collectible.Code.ToString()` (the exact "domain:path" target) and dispatches
  to the mod system — no stack clone needed when you only want the code string.
- **Lifecycle:** `new Harmony(id).PatchAll(typeof(ScribeModSystem).Assembly)` in `StartClientSide`
  (client-only — the Handbook is a client GUI), `harmony.UnpatchAll(id)` in `Dispose`.
- **Opening a specific entry programmatically (used by the footer "guide" action, not the patch):**
  the registered link protocol does it without reflection —
  `capi.LinkProtocols.TryGetValue("handbook", out var open); open(new LinkTextComponent("handbook://<pageCode>"))`.
  Detect whether the Handbook is already open via
  `capi.Gui.OpenedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")`.
- **Custom explainer pages need NO code.** A JSON file under `assets/<domain>/config/handbook/*.json`
  of shape `{ pageCode, title, text }` (title/text are lang keys) is auto-discovered and registered as a
  standalone handbook entry — link to it with `handbook://<pageCode>`. See
  `src/Mod/assets/scribe/config/handbook/03-task-types.json`.
- **Jump the player straight to a FOCUSED search box** with the sibling protocol
  `capi.LinkProtocols.TryGetValue("handbooksearch", out var s); s(new LinkTextComponent("handbooksearch://<text>"))`.
  It opens the Handbook OVERVIEW composer (the one that owns the `"searchField"` text input, focused on
  build) and runs `Search(text)` — empty text = "open the Handbook ready to type." This matters because the
  Handbook has **two separate composers**: `overviewGui` (has the search field) and `detailViewGui` (an item/
  entry page, NO search field). You cannot show a search box *on* an entry page — they never coexist. So the
  fastest "let the player find an item to track/link" path is `handbooksearch://` (add-tracker-link-tasks
  7.11c: chosen over opening the explainer entry, which dead-ended the player away from search).
- **VTML does NOT decode HTML entities.** Handbook/tooltip copy is VTML, not HTML: `&amp;` renders as the
  literal text `&amp;` and `&#47;` as `&#47;` — use a literal `&` and `/` in the lang string. Valid VTML
  tags (`<strong>`, `<em>`, `<br>`, `<a href="...">`, `<hotkey>`) DO render. Bit us in the task-types article
  (`&amp;`, `12&#47;20` showed raw); other strings that already used literal `&`/`/` rendered fine.

## Attribute-encoded items (lanterns, meals) — identity lives in `ItemStack.Attributes`, not the code (2026-08-19)

**Symptom: a lantern (or meal) task shows the raw lang key `Game:Block-Lantern-Small-up` instead of
"Copper Lantern", and its Handbook page has no "Add Crafting Task" link.** The block code is only
`lantern-{size}-{position}`; `material`/`glass`/`lining` live in `stack.Attributes`, and
`BlockLantern.GetHeldItemName` builds the name from the `material` attribute. Reducing the stack to
`Collectible.Code.ToString()` throws the attributes away, so the name key never matches and
`recipe.Output.ResolvedItemStack.Satisfies(bareStack)` matches no lantern recipe.

**Encoding the meaningful attributes into a string (confirmed via decompiling VSSurvivalMod.dll):**
`GuiHandbookItemStackPage.PageCodeForStack` is the canonical recipe — clone `stack.Attributes`, remove
every `GlobalConstants.IgnoredStackAttributes` key (= `temperature`, `toolMode`, `renderVariant`,
`transitionstate`) plus `durability`, take `SortedCopy(true)` for determinism, then
`TreeAttribute.ToJsonToken(sorted)`. Round-trip back with `stack.Attributes = (ITreeAttribute)TreeAttribute.FromJson(json)`
(the `ItemStack.Attributes` setter casts to `TreeAttribute`, and `FromJson` returns one). Scribe wraps
that JSON in base64 behind a `stack@<code>|<b|i>|<blob>` marker (`ScribeItemRef.Encode`/`ResolveStack`).

**Exact-variant matching direction:** `thisStack.Satisfies(other)` is `Collectible.Satisfies`, which for
same class+id returns `this.Attributes.IsSubSetOf(world, other.Attributes)` — i.e. `this` is a satisfactory
replacement of `other`, ignoring extra attributes on `other`. So to count "carried is exactly a copper
lantern," use `targetStack.Satisfies(carried)` (target's material=copper must be present-and-equal in
carried; an iron lantern fails). Slots into `CraftingRecipeIngredient` cleanly: set `MatchingType = Exact`
and assign `ResolvedItemStack = targetStack` directly (do NOT call `Resolve()` — a fresh ingredient's
`deduplicationIndex` is -1, so the `ResolvedItemStack` setter stores locally; then `SatisfiesAsIngredient`
reduces to `ResolvedItemStack.Satisfies(input)`). **Fix pattern:** see `ScribeItemRef.cs`,
`ScribeTrackerCounter.cs`, `ScribeCraftRecipeProbe.cs` (feed the probe the attributed `inSlot.Itemstack`,
not a bare code). Meals: prefer `IHandBookPageCodeProvider.HandbookPageCodeForStack` (in
`Vintagestory.GameContent`) over `PageCodeForStack` for the nav target, guarded with a fallback.

## Entry template

```
**Symptom: <what you observed, in the words someone debugging it later would use>.**

<the actual mechanism, confirmed via decompile — name the type/method>.

**Fix pattern:** <what to do instead>. See `<file>`.
```
