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

## Pointers

- `src/Mod/ScribeCollapsible.cs` — the collapse widget, height-factor render box, and
  host-owned registry (the pattern this doc defends).
- `src/Mod/HudScribePins.cs` — `ScribeFadeText` (self-ticking fade) lives at the bottom.
- `VSAPI-NOTES.md` `## LibGUI` section — the `ForceRebuild`-snaps-animations note and the
  stock `ListView` child-cache note.
- `openspec/changes/refactor-reconciling-gui-rebuild/` (on the
  `refactor-reconciling-gui-rebuild` branch) — the abandoned change; `tasks.md` has the
  ABANDONED status banner and per-group notes on what was done.
- Memory: `forcerebuild-vs-reconciling-libgui`.
