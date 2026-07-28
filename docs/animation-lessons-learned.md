# Animation lessons learned (LibGUI): why Scribe keeps its self-ticking animation stack

**Status:** Reference note. Written 2026-07-25 after the `refactor-reconciling-gui-rebuild`
change was implemented through the reconciliation work but **abandoned during the
"simplify animations toward stock" step** (group 6.1). That branch is left unmerged; this
doc records *why* the stock-animation goal is not reachable, so we don't re-attempt it from
scratch.

If you are tempted to replace `ScribeCollapsible` / `ScribeHeightFactorRender` /
`ScribeCollapseRegistry` (or the HUD's fade) with stock `AnimatedSize` / `AnimatedOpacity`,
**read this first.**

---

## TL;DR

- Scribe's row **deletion-collapse** animation (height → 0, then the row leaves the list)
  is driven by a **self-ticking `AnimationController`** inside `ScribeCollapsible`, over a
  custom `ScribeHeightFactorRender` box, with the controller **host-owned** in a
  `ScribeCollapseRegistry` keyed by TaskId. This looks non-idiomatic. It is deliberate.
- We tried to make it stock. **It cannot be fully stock**, for two independent reasons:
  1. **`AnimatedSize` exposes no completion callback.** A disappear animation needs a
     "the shrink is finished → now remove the row from the data" signal. Stock `AnimatedSize`
     is a bare `SingleChildWidget` over a render object with **no `onEnd`, no status event**.
     So the removal lifecycle *always* needs an explicit `AnimationController` (or a timer)
     regardless of how the visual shrink is drawn.
  2. **LibGUI's reconciler is positional, so implicit-animation state does not survive a
     list mutation.** Deleting a mid-list row remounts every row after it (fresh render
     object / state), which restarts any in-render-object animation controller from zero —
     re-collapsing an in-flight neighbor from full height. The host-owned registry exists
     precisely to *resume* a collapse across that remount; stock widgets have no equivalent.
- The **fade** (HUD destructive-pending countdown) *can* be expressed as stock
  `AnimatedOpacity` once the tree reconciles — but only if the surrounding tree reconciles
  (see below). On the current `ForceRebuild`-based stack it would snap, so it stays
  `ScribeFadeText` here too.

**Conclusion: keep the self-ticking stack.** It is the idiomatic tool for *this* framework's
constraints, the same way Flutter's own `AnimatedList` uses a `SizeTransition` driven by an
explicit controller rather than an implicit `AnimatedSize`.

---

## Background: the two update paths, and why they matter for animation

LibGUI (like Flutter) has two ways to push new UI:

- **Reconcile** — `State.SetState` / `Element.MarkNeedsBuild` marks an element dirty; on the
  next frame `BuildOwner.BuildDirtyElements()` re-runs `Build()` and diffs the children,
  **reusing** matching elements (same type + key) and their `State` / `AnimationController` /
  `RenderObject`. Implicit animations (`AnimatedOpacity`, `AnimatedSize`, …) **only animate
  on this path** — they retarget their tween inside `UpdateWidget` when the parent rebuilds
  with a changed value.
- **`ForceRebuild()`** — `GuiBase`'s hot-reload tool. Unmounts and recreates the **entire**
  widget tree. Every `State` / controller / render object is disposed and rebuilt fresh. An
  implicit animation recreated fresh initializes `Begin == End == target` and evaluates to
  the target instantly — i.e. it **snaps**, no motion.

Scribe historically used top-level `ForceRebuild` as its everyday update path (the HUD and
lectern hosts). That is *why* every Scribe animation is self-ticking: a self-owned
`AnimationController` started in `InitState` ticks itself frame-by-frame via `MarkNeedsBuild`,
so it animates correctly even though the parent uses `ForceRebuild`. `ScribeFadeText` and
`ScribeCollapsible` both do this.

The `refactor-reconciling-gui-rebuild` change replaced top-level `ForceRebuild` with
reconciliation (`SetState` on a persistent content widget). That part **worked** (HUD,
settings form, lectern read view, and lectern editor structural mutations all converted,
build-clean, 102/102 Core tests). The goal after that was: now that we reconcile, revert the
animations toward stock. That is the step that failed.

---

## Why the deletion-collapse cannot be stock

### Reason 1 — `AnimatedSize` has no completion signal

A "collapse then remove" animation is two things:

- (a) the **visual** height shrink (1 → 0), and
- (b) the **lifecycle** signal: *the shrink is done, now remove the row from the document /
  pin set*.

Stock `AnimatedSize` (`Gui.Widgets.Animations.AnimatedSize`) is a `SingleChildWidget` whose
render object `RenderAnimatedSize` owns an internal controller and animates whenever its
child's **measured size changes** between layouts. Verified against the source: it exposes
**no `onEnd`, no `OnStatusChanged`, no status property at all**. So (b) is impossible to get
from `AnimatedSize`.

`AnimatedOpacity` *does* have an `onEnd`, but wiring "remove the row when the fade ends" is
wrong: the fade and the size-collapse are two independent controllers with two different
durations, and the removal must wait for the **size** animation, not the opacity one. Firing
removal on the opacity `onEnd` yanks the row mid-collapse.

**Therefore the removal timing requires an explicit `AnimationController` (or a tick
countdown) no matter what** — which is exactly the controller `ScribeCollapsible` already
owns and fires `OnCollapsed` from. There is no version of "fully stock" that removes this.

### Reason 2 — positional reconciliation restarts in-flight collapses

Even setting Reason 1 aside, `AnimatedSize`'s controller lives **in the render object** and
its progress is render-object-local (`_hasLayoutOnce`, `_lastChildSize`). LibGUI's
multi-child reconciler (`MultiChildElement.Update`) matches children **positionally** —
`_children[i]` against `nextWidgets[i]` — and a slot whose key no longer matches is
**unmounted and remounted** (fresh render object). There is **no keyed reordering**.

So during rapid removals (delete row 2 while row 4's collapse ghost is mid-shrink): the ghost
shifts to slot 3, remounts, gets a brand-new `RenderAnimatedSize`, and **restarts its
collapse from full height**. This is a real, tested scenario (`scribe-list-collapse` playtest
item `58707ebd`, "collapse under rebuild … resume-from-elapsed registry").

`ScribeCollapseRegistry` fixes this by parking the `AnimationController` on the **host**,
keyed by TaskId, so a remounted `ScribeCollapsible` **resumes** the same controller instead
of restarting. Stock widgets have no equivalent because Flutter/LibGUI implicit animations
assume keyed reordering *preserves* the element — which LibGUI's positional matcher does not
provide.

### Bonus wrinkle — implicit animations don't animate on mount

`AnimatedSize` / `AnimatedOpacity` animate on a *change to an already-mounted* widget, not on
first mount (`ImplicitlyAnimatedWidgetState.InitState` seeds `Begin == End == target`). Our
collapse ghosts are *born* in the departing state, so a stock widget would need a
"mount full, then flip to collapsed on a later frame" trigger — and LibGUI has **no
post-frame callback API** (`PostFrame` / `addPostFrameCallback` do not exist). So even the
first collapse would need an explicit "flip next frame" mechanism.

---

## What the reconciliation refactor *did* make possible for animations

Not nothing — the refactor's reconciliation work is sound, and if it's ever revived:

- **The fade → stock `AnimatedOpacity` is genuinely reachable** *once the surrounding tree
  reconciles* (as it does on the abandoned branch). The checkbox click flips a `fading` flag,
  the reconcile retargets the opacity tween 1 → 0 over the pin window (`Curves.Linear`
  matches the countdown), and an unrelated push mid-fade passes the same target so it doesn't
  restart. An undo retargets back to 1. This was implemented and compiled on the branch.
  - **But** on *this* (`add-pinned-task-foundation`) branch the HUD still uses `ForceRebuild`,
    so `AnimatedOpacity` would snap. `ScribeFadeText` stays here.
- **`ScribeFadeText` was improved** on the branch to start/cancel its fade on the `fading`
  transition in `UpdateWidget` (not just `InitState`), which is the correct behavior under
  reconciliation. If the reconciliation refactor is ever revived, carry that over; on the
  current stack the `InitState`-only version is fine because `ForceRebuild` remounts it each
  push.

---

## Practical guidance (what to actually do)

- **Do not** try to replace `ScribeCollapsible` / `ScribeHeightFactorRender` with stock
  `AnimatedSize`. It is not a style nit — the removal lifecycle and the rapid-removal resume
  both require the explicit host-owned controller. This is the idiomatic solution for
  LibGUI's positional reconciler + callback-less `AnimatedSize`, mirroring Flutter's own
  `AnimatedList` (`SizeTransition` + controller).
- **Disappear/appear animations in LibGUI** (anything where the widget must be *removed* when
  the animation ends) should follow the `ScribeCollapsible` pattern: self-ticking
  `AnimationController`, host-owned + identity-keyed if it must survive a list mutation, with
  the removal deferred out of the ticker callback (a `needs*Cleanup` bool acted on in the next
  `OnRenderGUI`) to avoid re-entrant tree teardown.
- **Implicit `AnimatedOpacity` / `AnimatedSize`** are fine for *in-place value changes on a
  widget that stays mounted* (hover tints, a sunk-row mute) — but only when the host
  **reconciles** rather than `ForceRebuild`s. On a `ForceRebuild` host they snap.
- **There is no post-frame callback in LibGUI.** If you need "do X one frame after mount,"
  you need a self-ticking controller or a `capi` tick — not a framework hook.

## Scroll/settling coordination and the race class (v1-playtest-fixes, 2026-07-27)

This is the same machinery animations live in, so it belongs here even though it's about
scroll: the lectern editor coordinates scroll offset through **`OnRenderGUI` post-layout
settling loops** that run every frame after `base.OnRenderGUI`, because the things they read
(content height, `MaxScrollExtent`, a target's live geometry) are only correct *after* layout
has run for the current frame — the same reason `Scrollable.EnsureVisible` and any
mount-then-animate trigger must be deferred to `OnRenderGUI` rather than fired from `Build()`
or an event handler. There is no post-frame callback in LibGUI (see the TL;DR), so
`OnRenderGUI` IS the post-layout hook. Understanding its dynamics is prerequisite to touching
either the animation stack or the scroll stack, because they interleave in this one method.

**The three settling loops** (all in `GuiDialogScribeLecternLibGui.OnRenderGUI`) are
**frame-count-bounded, not convergence-guaranteed**, and can fight within a frame:

- `pendingEnsureVisible` — scroll the focused editor row into view once (reads live geometry).
- `pendingRestoreScrollOffset` (5-frame) — re-apply an offset captured before a view switch /
  rebuild, because a `ForceRebuild` **resets `SingleChildScrollView`'s offset to 0** (it
  remounts the scroll view; the offset is render-object-local `State`, disposed on unmount —
  the exact positional-reconciliation teardown Reason 2 above describes for `AnimatedSize`).
  Retried over a few frames because the read view's virtualized `ListView` re-derives its
  content height (hence max extent) over the first frame(s) after the swap, so a single
  `JumpTo` can still be clamped toward the top before the real row heights are known.
- `pendingClampToExtent` (5-frame) — after a delete, clamp the offset DOWN to the shrunk
  `MaxScrollExtent` once layout reports the reduced content height. Needed because LibGUI's
  own auto-correct (`ScrollWheelHandler.ClampOffset`) **ignores an overshoot of ≤50px**, so
  deleting one ~30px row while scrolled near the bottom strands the viewport past the new max.

The proven "hold still across a rebuild" primitive is **capture + restore**
(`CaptureScrollForRestore` before the `ForceRebuild`, then the restore loop re-applies it) —
used by Pin/Unpin, view-switch, Sink completion, and the deletion-collapse cleanup. Do NOT
invent a new scroll path; reuse capture+restore. Note the clamp loop can only clamp DOWN, so
it cannot recover a `ForceRebuild`-reset-to-0 on its own — that's why the collapse-cleanup
rebuild pairs capture+restore WITH the clamp (restore the pre-collapse offset, let the natural
clamp reduce it to the shortened list's real bottom).

**The race class (both v1 scroll bugs, and the shape to watch in animation work).** Both bugs
fixed in this pass were **intermittent** because they depended on the ordering of an async
event against a local mutation — the signature of a settling/timing race, which is exactly
what makes animation bugs (mid-flight remounts, restart-from-zero, snap-instead-of-animate)
hard to reproduce and diagnose:

1. **Enter-makes-a-self-destructing-task**: an async server resync (`RefreshReadView`, fired
   by the authoritative-doc push) pruned a row the local editor had *just* optimistically
   created but not yet persisted. Any async server-resync that prunes local rows against a
   server snapshot must special-case legitimately-local-only in-flight rows (here: never drop
   the focused row; never drop an empty task, which is never persisted by design). The
   animation-relevant lesson: **a `ForceRebuild` triggered by an async server push can land at
   any frame** and tear down in-flight local state — the same hazard that restarts an in-flight
   collapse (Reason 2). Guard local optimistic state against async resync teardown.
2. **Uncheck-jumps-the-viewport**: a same-row focus RE-HOME reused the cross-row nav helper
   (`FocusEditorRow`), which couples focus with `pendingEnsureVisible` (a scroll-into-view).
   Re-homing focus after a `DispatchPointerDown` blur (a05caret1) is right; scrolling to it is
   not. **Separate "focus here AND scroll to it" from "the caret is already here, only re-grant
   the token"** — call `FocusNode.RequestFocus()` directly for the latter. General: watch for a
   convenience helper that bundles a side effect (a scroll, a rebuild, an animation retarget)
   you don't want at a particular call site.

**Diagnosis method that worked (keep for animation bugs too).** These were misdiagnosed for
multiple rounds by hypothesis-first blind fixes. What finally worked: a **DEBUG-only frame-by-
frame trace** — a `[Conditional("DEBUG")]` `TraceScroll(tag)` logging offset / max extent /
view / focused index / every pending-flag-with-frame-counter, plus a `#if DEBUG` subscription
to `ScrollController.OnChanged` so LibGUI's OWN internal mutations (the ±50 `ClampOffset`
`JumpTo` none of our loops can see) show up too. Intent tags at every mutation site
(insert-below, delete, complete-with-policy, reorder, ensure-visible, restore, clamp,
capture-restore) turn "intent → resulting OnChanged" into a diffable per-frame log. An
equivalent controller-state trace is the right first move for any animation race (collapse
restart, fade snap): log the controller's value/status/elapsed per frame and diff a
working-vs-broken run rather than guessing. The trace was removed after these fixes verified,
but the pattern (`[Conditional("DEBUG")]` + `OnChanged` subscription + intent tags) is the
standing tool — re-add it for the next settling regression. Read the trace with
`tail -f <client-main.log> | grep --line-buffered -i <tag>`; the project's `scribe-log.sh` and
the raw log both let an unrelated GL-error flood through on Apple Silicon (harmless; see
`VSAPI-NOTES.md`), so grep for your own tag.

## Pointers

- `src/Mod/GuiDialogScribeLecternLibGui.cs` — the three `OnRenderGUI` settling loops,
  `CaptureScrollForRestore`, and the two v1 race fixes (`RefreshReadView` guard,
  `ToggleEditorTask` re-home). Also documented in `VSAPI-NOTES.md` `## LibGUI`.
- `src/Mod/ScribeCollapsible.cs` — the collapse widget, height-factor render box, and
  host-owned registry (the pattern this doc defends).
- `src/Mod/HudScribePins.cs` — `ScribeFadeText` (self-ticking fade) lives at the bottom.
- `VSAPI-NOTES.md` `## LibGUI` section — the `ForceRebuild`-snaps-animations note and the
  stock `ListView` child-cache note.
- `openspec/changes/refactor-reconciling-gui-rebuild/` (on the
  `refactor-reconciling-gui-rebuild` branch) — the abandoned change; `tasks.md` has the
  ABANDONED status banner and per-group notes on what was done.
- Memory: `forcerebuild-vs-reconciling-libgui`.
