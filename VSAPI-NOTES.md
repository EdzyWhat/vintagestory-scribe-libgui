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
- **Per-player persistent store:** `IServerPlayer.SetModData<T>(key, data)` / `GetModData<T>(key,
  default)` — permanent, per-player, NOT client-synced (also raw-byte `SetModdata`/`GetModdata`/
  `RemoveModdata`). This is where a "milestones seen" set lives.
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

**Fact: `ListView` supports variable-height rows despite the wiki saying otherwise.** The wiki's
*Scrolling* page shows only uniform `itemHeight` ("all items must have the same height"). The source
(`reference/vslibgui/Gui/Gui/Widgets/Scroll/ListView.cs:44` and `:88`) has `estimatedItemHeight` +
`variableHeight: true` constructors backed by an `ItemHeightCache`. Scribe's *display* rows can use
this; but see the two facts below for why editable rows are a different story.

**Fact (spike, 2026-07-23): `TextField` is SINGLE-LINE. LibGUI has no multi-line text input.**
`RenderTextField` (`reference/vslibgui/Gui/Gui/Core/Input/RenderTextField.cs`) measures a single line
(one `MeasureText` + one `lineHeight`), does no newline/soft-wrap handling, and exposes no
`maxLines`/`multiline` flag. `MaxLines` exists only on the *read-only* display widgets (`Text`,
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
`Height` (the showcase uses 35).

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
`KeyEvent` passing only `shift/ctrl/alt` (`reference/vslibgui/.../GuiBase.cs` ~959-1032); VS's own
`KeyEvent.CommandPressed` is never propagated. So a LibGUI widget cannot see Cmd, and the macOS caret
idioms (Cmd+←/→ = line ends, Cmd+A/C/X/V) can't be handled inside the field. **Fix:** translate Cmd
one layer up, in the dialog's `public override void OnKeyDown(KeyEvent args)` — the VS `KeyEvent` is
**mutable** (`KeyCode`/`CtrlPressed`/`CommandPressed` all have setters), so rewrite Cmd+←/→ →
Home/End and Cmd+{A,C,X,V} → Ctrl+{A,C,X,V}, clear `CommandPressed`, THEN call `base.OnKeyDown(args)`
(which does the mapping). Alt/Option *is* delivered as `Alt`, so Alt+Arrow word-skip works in the
field directly. (Mirrors the native `ScribeRowTextInput.TranslateMacCaretModifiers`, moved up a level.)

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

## Entry template

```
**Symptom: <what you observed, in the words someone debugging it later would use>.**

<the actual mechanism, confirmed via decompile — name the type/method>.

**Fix pattern:** <what to do instead>. See `<file>`.
```
