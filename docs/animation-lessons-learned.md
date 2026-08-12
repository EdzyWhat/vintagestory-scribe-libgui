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

## Re-check: does 3.1.0 + an ADD-grow use case change the verdict? (2026-07-30)

Revisited after the LibGUI 3.1.0 upgrade, specifically for an **"Add Task" appear animation** — a
new task row that *pops into existence and grows to full vertical height* — prompted by the
`.ui showcase` `AnimatedSize` example (`_expanded ? Column[header, extra] : header`). Assessment
only; **not acted on.** Conclusion: the prior "keep the self-ticking stack" verdict **stands**, but
the reasons are narrower than they look, and ADD-grow is a *different, easier* case than the
DELETE-collapse this doc originally killed.

**3.1.0's `AnimatedSize` is unchanged in the ways that matter.** Verified against the shipped
`Gui.dll`: `Gui.Widgets.Animations.AnimatedSize` is still a bare `SingleChildWidget` (ctor
`(TimeSpan duration, Curve?, Widget? child, Key?)`) with **no `onEnd`, no status** — same shape this
doc analyzed for 2.0.0. So nothing about the completion-signal argument (Reason 1) changed.

**Which of the three walls actually apply to ADD-grow (vs. DELETE-collapse):**

| Wall | DELETE-collapse (killed) | ADD-grow (this idea) |
|---|---|---|
| R1 — no `onEnd` for lifecycle | Fatal (must remove row when shrink ends) | **N/A** — nothing to remove; row just settles at full height |
| R2 — positional remount restarts in-flight anim | Fatal (deleting row 2 remounts row 4's ghost) | **Mostly N/A** — `OnClickAddTask` APPENDS to the end (`scratch.AddTask("")` → last slot; `ScribeDialogBase.cs:1112`); nothing after it to remount |
| Bonus — no animate-on-mount + no post-frame hook | worked around via ghost | **This is the real (small) obstacle** — implicit widgets seed `Begin==End` on first mount, and LibGUI has no post-frame callback to flip them |

So only the *smallest* of the three walls applies to Add-Task — and it's the same one
`ScribeCollapsible` already solves (a self-ticking controller started in `InitState` animates on
mount without any post-frame hook).

**The bigger blocker is the host, not `AnimatedSize`.** `OnClickAddTask` ends in **`ForceRebuild()`**
(`ScribeDialogBase.cs:1121`); the whole editor updates via `ForceRebuild`. Per this doc's core law,
implicit animations **snap** on a `ForceRebuild` host — so a naive stock `AnimatedSize` there pops to
full height with no motion. The `.ui showcase` example animates only because the showcase host
*reconciles* (`SetState`); it's also the *in-place expand on an already-mounted widget* case (the one
case this doc says implicit animation genuinely fits), which is subtly different from Scribe's
*freshly-mounted row under a `ForceRebuild` host*.

**Three paths, if this is ever picked up:**
- **A — stock `AnimatedSize`, naive:** snaps. Dead on arrival on the `ForceRebuild` editor host.
- **B — invert `ScribeCollapsible` into a grow-on-mount "ScribeRevealable" (RECOMMENDED):**
  self-ticking, host-owned controller growing factor 0→1 instead of 1→0, **no `onEnd` needed** (no
  removal). Reuses the exact pattern the codebase already trusts, starts in `InitState` (solves the
  animate-on-mount wrinkle), and sidesteps both the `ForceRebuild`-snap and the reconciler. It is
  essentially `ScribeCollapsible` run backwards, *minus* the removal callback — simpler than what
  exists. Open sub-questions to work when acting: where the wrapper sits relative to the new row, and
  how `autoFocusRowOnRebuild` / `pendingEnsureVisible` interact with a row whose height is still
  growing.
- **C — revive reconciliation, THEN use stock `AnimatedSize`:** the big one. This is exactly the
  abandoned `refactor-reconciling-gui-rebuild` path; it would unlock stock `AnimatedSize` *and* the
  fades, but re-opens a grave already dug. Worth it only as a broad "stock animations everywhere"
  goal, not for Add-Task alone.

**Bottom line:** the 3.1.0 upgrade does **not** unlock stock implicit animation for Scribe's cases —
the `ForceRebuild` host is the gate, and `AnimatedSize` is still callback-less. But ADD-grow is a
legitimately easier case than the DELETE-collapse this doc killed, and Path B is a modest, proven way
to get it without touching the reconciler.

## Re-evaluation: reconcile for IDENTITY, not for stock animations (2026-08-09)

Prompted by a fresh round of animation pain — the list-collapse **stale-hover** fix (shipped),
plus the **mass-delete first-click-doesn't-register** bug and a general "every animation is a
bespoke fight, and I keep abandoning them" frustration. This section reframes what reconcile is
*for*, because the framing above (and Path C) quietly conflated two different goals and, having
killed one, looked like it killed both.

**The trap this doc itself fell into.** Everything above weighs reconcile **in service of making
animations stock** — and against *that* goal, reconcile is correctly buried (R1: no `AnimatedSize`
`onEnd`; R2: positional reconciler restarts in-flight collapses). Path C is "a grave already dug."
**All true, and it still stands: the stock-animation goal stays dead.** But that is not the only
reason to reconcile, and it was never the one that maps to the day-to-day pain.

**The goal reconcile actually serves: killing the identity-loss class.** `ForceRebuild` disposes
*every* `State` / `AnimationController` / `RenderObject` and mounts a brand-new tree. Everything
that is *identity* — hover state, focus/caret, `EventDispatcher`'s press-capture (`_capturedElement`
is a concrete Element **reference**), a live animation controller — is destroyed at the rebuild.
That single fact is the root of a whole family of bugs we have each fixed bespoke:

- the **one-frame flicker** after a rebuild (accepted as inherent),
- **lost hover** when a row slides/rebuilds under a still cursor (fixed via the `ScribeHoverRefreshLatch`),
- **first-click-doesn't-delete** mid-collapse (a moving-target *and* a rebuild-divide race: press
  captures Element A, a rebuild replaces it, release can never match `hit == target`),
- caret/scroll-offset loss across a rebuild (fixed via `autoFocusRowOnRebuild` + capture-restore).

Each of those is a **scaffold to smuggle identity past a `ForceRebuild` that need not happen on that
surface.** On the **reconcile** path (`SetState` → dirty-only rebuild → `UpdateChild`/`CanUpdate`
*reuses* the same Element+State+RenderObject when type+key match) that identity is **never torn
down**, so the entire class evaporates — no latch, no re-home, no capture-restore. That is the
value proposition this doc never scored, because it was only ever asking "does reconcile make
animations stock?" (no) and never "does reconcile stop the identity churn?" (yes, definitively).

**What does NOT change, and must not be re-litigated:**

- **The self-ticking animation stack STAYS and gets *generalized*, not deleted.** R1 and R2 are
  permanent: `AnimatedSize` has no completion callback, and the reconciler is positional, so a
  mid-list delete still remounts trailing rows and restarts their motion. The host-owned,
  identity-keyed, self-ticking controller (`ScribeCollapsible` + `ScribeCollapseRegistry`) is the
  load-bearing answer to *motion* **regardless of reconcile**. The 2026-07-27 refactor died
  because it tried to *delete* this stack (task group 6, "simplify toward stock"); the whole change
  was thrown out with that one false sub-goal. **This time the harness is the deliverable, not the
  casualty** — generalize it into one reusable enter/exit/reorder primitive.
- **Fade is not an escape hatch.** `AnimatedOpacity` is the *same* live-controller-vs-rebuild class
  (snaps on `ForceRebuild`), plus it composites to an offscreen `SaveLayer` for the entire mid-fade
  duration and stays hit-testable at α=0. Look-choice only; rides the same harness.

**Why this is not the same grave (the honest differences from 2026-07-27):**

1. **Different, correct value proposition** — kill identity churn (flicker/hover/click/caret), NOT
   "stock animations." The measurable wins are the bug class, not code deletion.
2. **Keep + generalize the self-ticking harness** — the exact thing last time tried to delete.
3. **Playtest is a per-surface gate, first-class** — last time was "build-clean, 102/102 tests,
   **never playtested**," and died before the only gate that matters for GUI work.
4. **Mine, don't merge.** The abandoned `refactor-reconciling-gui-rebuild` branch is **259 commits
   behind main** and rewrote `GuiDialogScribeLecternLibGui.cs`, which has since been split into the
   `ScribeDialogBase*.cs` partials — un-rebaseable. Its one durable artifact is
   `src/Mod/ScribeListView.cs` (107 lines, never adopted): lift it as a **reference**, don't merge.

**Standing guidance for the next person (including future me):** if you are reaching for reconcile,
be explicit about *which* goal. "Make animations stock" → stop, read R1/R2, it's dead. "Stop the
identity churn / make the rebuild stop destroying hover/focus/capture/controllers" → valid, and the
subject of the `reconcile-animating-surfaces` change (2026-08-09). Do not sell the second goal on the
first goal's promises, or it gets buried with them again.

## The diffing container: motion for free by comparing frames (extract-animated-task-list, 2026-08-10)

Follow-through on the 2026-08-09 reframe: with reconcile in place (a widget subtree now *survives* a
data mutation), the departing-ghost choreography that was hand-wired into the editor — and copied into
the HUD, and missing from the Pinned tab — becomes extractable into **one rendering-agnostic container**,
`ScribeAnimatedList`. A surface gets the editor's collapse-on-removal animation "for free" by rendering
its rows through it and mutating **only its data**; it never learns the animation vocabulary.

**How it works.** The container is a `StatefulWidget` whose State caches the id-keyed rows it rendered
last frame. On each rebuild it diffs the incoming live ids against the cached set (the pure math is in
Core's `ScribeListDiff`, unit-tested game-free): an id present last frame but absent now is a **departure**
— it is spliced back at the slot it left, wrapped in `ScribeRowSizeAnimation(Collapse)` from a host-owned
registry, rendered as a frozen ghost. When the collapse finishes the container drops the ghost itself.

**Two things it deliberately does NOT abstract:**

1. **Content / layout (D6).** It touches exactly two things about a row — its stable `Guid` and its height
   — and never inspects what the row renders. The caller supplies the row widgets AND the layout wrapper
   (a `layoutBuilder` taking the final ordered list). So an editable task row, a static Read line, a
   multi-column Guestbook entry are all "a widget at an id"; each view's content stays free to diverge.
   There is **no "view behavior profile"** layer — that was explicitly rejected as a miscut that would
   fight the divergence.
2. **Scroll-pin + hover-refresh (open question §2.7, resolved: NOT autonomous).** Those touch
   dialog-level state — the shared `ScrollController`, `RootElement`, `RefreshHoverAtCursor` — so they
   **stay in the host's `OnRenderGUI`**, driven off the *same host-owned* `ScribeAnimationRegistry`'s
   `AnyAnimating` that the container animates against. The container packages diff/ghost/slot/self-cleanup;
   the host keeps the two inherently dialog-level loops (plus an `onDepartureSettled` callback for the final
   scroll clamp). The registry is host-owned precisely so the host can read `AnyAnimating` without reaching
   into the container's State. Trying to make the container fully autonomous would mean it hooking a
   post-layout point and owning a scroll controller it doesn't create — more coupling, not less.

**The one improvement over the editor's hand-wired path: self-cleanup, no host flag.** The editor defers
its ghost retirement through a `needsEditorCollapseCleanup` bool processed in `OnRenderGUI`, because its
`onEnd` fires from inside the animation pump and it rebuilds the *dialog* tree (a cross-tree `RebuildBody`)
— re-entrant if done directly. The container instead calls `SetState` from its own `onEnd`: LibGUI's
`MarkNeedsBuild` is **deferred** (it adds the element to `BuildOwner`'s dirty set, drained on the next
`BuildDirtyElements`, which explicitly "handles cascaded rebuilds from animation controllers or state
changes triggered inside `Build()`"). So the container schedules its *own local* rebuild with no
re-entrancy and no host-visible flag; the next `Build` retires every ghost whose controller `IsComplete`.
This is safe only because the rebuild is local to the container — the editor's flag exists because IT
rebuilds a *different* (ancestor) subtree.

**Ghost source (D2).** A live interactive row is unsafe to freeze in place (its checkbox/field/gestures
would stay live mid-collapse, and its focus node is gone once the data leaves), so each `ScribeAnimatedListItem`
supplies an explicit static `Ghost`. The Pin Tab reuses `ScribeFrozenEditorRow` via a `ScribeEditRowData`
adapter (`Pinned:false` — a Pin Tab row has no resting tint), so it collapses byte-identically to the
editor. The container falls back to caching the live `Child` only for a genuinely static row.

**Adopted on the Pinned tab first** (no animation before → highest payoff, no risk to already-playtested
surfaces), then the **editor and Read view** were migrated onto the same container in `animate-row-insertion`
(2026-08-11) — so three of the four animating surfaces now share one motion path and only the **HUD** stays
bespoke (its migration, plus the `Delayed` undo-window/fade policy the HUD needs, is promoted to its own
follow-up change `migrate-hud-onto-animated-list`; the `Delayed` enum value still **throws** today so it
can't ship half-built).

## Row ENTRY animation: uniform slide-in, realized (animate-row-insertion, 2026-08-12)

The 2026-07-30 re-check above sketched a "ScribeRevealable" grow-on-mount widget (Path B) as the way to
make an added row *enter* with motion instead of popping in. With the editor now on `ScribeAnimatedList`
and rebuilding via `RebuildBody()` (reconcile, container State survives), that sketch is **realized** — but
as a capability *of the container*, not a standalone widget. The container already diffs frame-to-frame, so
an id present now but absent last frame is an **appearance** (the mirror of the departure seam it already
had). The Core `ScribeListDiff` reports appearances; the container animates them.

**One uniform slide, not a height grow (the design that shipped).** The *first* cut tried a focus split:
grow non-focused rows (`ScribeRowSizeAnimation(Reveal)`), fade the auto-focused new row at full height
(`ScribeFade`). Two findings killed that:
1. **The full-height fade "appeared instantly"** (playtest `d87250f4`, 2026-08-12). An opacity-only fade at
   a fixed position over 200ms against the parchment is *too subtle to read as motion*. A moving row is
   unmistakable; a same-position fade is not.
2. **Growing a variable-height row is the caret hazard**, not the fix for it. Height changes every frame, so
   a wrapped-text row shrinks/mislocates its own caret and the `pendingEnsureVisible` scroll-to fights the
   changing height.

So the shipped entry is **one motion for every appearance: `ScribeSlideIn`** — the row takes its **full
height in its slot from frame one** (the translate is *paint-only*: `Transform` passes layout constraints
through unchanged), and only the *painted content* translates in from above while fading up. Translation is
the primary read; the fade is layered polish off the **same controller value** (one controller, no
per-row bookkeeping doubling — the trap that made the old D4 symmetry-fade "not cheap"). Because height is
final from the first frame, the caret, pointer hit-tests, and ensure-visible all work against final geometry
immediately — which is *why* a uniform motion is now safe for the auto-focused row too (the whole reason the
focus split existed is gone). `RenderTransform.GlobalToChild` inverts the matrix for hit-testing, so a click
lands where the row is **drawn** mid-slide. No view learns any entry vocabulary; the container wraps every
appeared id and the surface just supplies the row set.

**The load-bearing reconciler finding: the entry wrapper must stay on the row for its whole lifetime.**
LibGUI's reconciler is **positional by (type + key)** (`Widget.CanUpdate` = `GetType()==GetType() &&
Equals(Key,Key)`). If a wrapper is present at a slot one frame and gone the next, the slot's widget *type*
changes and the reconciler **remounts the inner subtree** — which for the auto-focused row would destroy its
`GuiElementTextInput` and lose the caret mid-keystroke. So `ScribeSlideIn.Build` **always** renders the same
`Opacity > Transform > child` shape (returning `Opacity(1f, Transform.Translate(child, Vector2.Zero))` when
settled/not animating), and the container **keeps the wrapper on the row for its entire live lifetime** — an
inert identity pass-through once the slide completes, never removed, never a type-swap. (This is why there is
no per-mode retire logic anymore: every entry is kept-for-life, so `entering` is a plain `HashSet<Guid>`, not
a mode map.)

**Opacity floor.** `ScribeSlideIn` clamps rendered opacity to `MinOpacity = 0.02f` rather than starting at
literal 0. `RenderOpacity` skips paint entirely at `Opacity <= 0.001f`, so a true-zero start frame would
flash a one-frame gap under a live caret; the floor keeps the first frame paintable while still reading as
"fading in."

**`firstBuild` suppression.** On open / view-switch / any `ForceRebuild`, the container remounts fresh with
an empty `prevLiveIds`, so *every* row looks like an appearance and the whole list would animate in at once.
A `firstBuild` flag suppresses entry animation on that first build after (re)mount — only genuine
frame-to-frame additions on a *surviving* container animate.

**Distinct entry vs collapse registry keys.** Entry controllers are keyed `EntryKey(id)` = `"enter:"+id`,
separate from the collapse `Key(id)`. Without the prefix a slide-then-delete of one id would *resume* the
already-`Complete` entry controller instead of starting a fresh collapse — rendering an instantly-closed
ghost. Same host-owned registry, disjoint key namespaces, so `AnyAnimating` (and thus the host's scroll-pin +
hover-latch loops) covers entry automatically with no new wiring.

## The HUD migration, and why the "Delayed removal policy" was a misconception (migrate-hud-onto-animated-list, 2026-08-12)

The pinned-task HUD was the **last** of four animating surfaces still hand-wiring its own departure
choreography (a `departing` map, `BeginDeparting`/`ReconcileDeparting`/`CancelDeparting`/
`OnDepartingCollapsed`, a `needsCollapseCleanup` deferral, and a per-row `ScribeRowSizeAnimation` wrap in
`BuildRow`). It now routes through `ScribeAnimatedList(Immediate)` like the editor / Read / Pin Tab, so
exactly **one animation path** remains across all four surfaces.

**The trap this closes.** The change was originally scoped around wiring the container's stubbed **`Delayed`**
removal policy — a held, faded *ghost* in front of the collapse — on the belief that the HUD's undo window
*needs* a container-level hold. That was backwards. Trace the HUD's destructive-completion timeline: on a
"complete" click the pin **stays live in `MyPins`** at full height, its checkbox clickable, for
`PinHudWaitMs` (1500ms); undo is **unchecking that live row** (`pendingCompletions.Remove` → nothing was ever
sent). The pin only leaves the rendered set at *send-time*, when it enters `awaitingRemoval` and is filtered
out of the item set handed to the container — and *that* departure is exactly what the existing `Immediate`
policy already collapses. So:

- **The undo window is a deferred-network-send phase on a LIVE row, not an animation hold.** It is domain
  state (a pending unsent packet + optimistic flag), and it lives *before* the row ever enters the
  container. A container `Delayed` ghost-hold could not carry it anyway: **a frozen ghost has no live
  checkbox**, so it can't host the uncheck-to-undo affordance. `ScribeListRemovalPolicy.Delayed` was removed
  as a misconception, not an unbuilt feature; the enum is single-valued (`Immediate`).
- **The removal *animation* and the undo *semantics* are separable concerns** that got conflated only
  because the HUD was the sole surface with both. Keep them apart: the collapse belongs to
  `ScribeAnimatedList(Immediate)`; the window stays in the HUD.
- **The frozen ghost must render the ALREADY-FADED row.** During the window the live row's `ScribeFadeText`
  ramps the text to ~0 opacity as a countdown; the HUD's ghost (`BuildFrozenGhost`) is therefore a disabled
  checkbox + **zero-opacity text**, so the collapse closes empty space instead of flashing the text back at
  full opacity for a frame.
- **`ScribeFadeText` stays** (it is the live-window countdown fade, self-ticking so it survives the host's
  rebuilds — see the widget's own remarks). An earlier plan to replace it with a host-controller `ScribeFade`
  primitive was itself part of the misconception; there is no `ScribeFade`, and the row fade was always the
  live-window countdown, not a departure fade.

Two scope-adds rode along for free once the HUD was on the container: **D6** — HUD rows now ENTER with the
same `ScribeSlideIn` as every other surface (`animateEntry` on). **D7** — the HUD's base row order is aligned
with the Pin Tab via `ScribePinOrdering.ForDisplay` under sinking policies, re-applying only the two
HUD-specific overlays the Pin Tab has no equivalent for (the durable session-sink `sunkOrder` bottom-hold and
the in-undo-window in-place hold).

## Pointers

- `src/Mod/GuiDialogScribeLecternLibGui.cs` — the three `OnRenderGUI` settling loops,
  `CaptureScrollForRestore`, and the two v1 race fixes (`RefreshReadView` guard,
  `ToggleEditorTask` re-home). Also documented in `VSAPI-NOTES.md` `## LibGUI`.
- `src/Mod/ScribeRowSizeAnimation.cs` — the collapse/reveal height-factor widget + render box, the
  host-owned `ScribeAnimationRegistry` (the pattern this doc defends), and `ScribeSlideIn` (the parallel
  registry-driven `Opacity > Transform` wrapper for the uniform row entry slide).
- `src/Mod/ScribeAnimatedList.cs` — the diffing container (motion-only, D6), including the appearance seam
  and the uniform `ScribeSlideIn` entry (kept-for-life, `entering` is a plain id set);
  `src/Core/ScribeListDiff.cs` — its pure, game-free identity diff
  (tested in `tests/Core.Tests/ScribeListDiffTests.cs`).
- `src/Mod/HudScribePins.cs` — the pinned HUD, migrated onto `ScribeAnimatedList(Immediate)`
  (migrate-hud-onto-animated-list): `BuildFrozenGhost` (zero-opacity-text collapse ghost), the
  `awaitingRemoval`-triggered departure, and `ScribeFadeText` (self-ticking live-window countdown fade) at
  the bottom.
- `VSAPI-NOTES.md` `## LibGUI` section — the `ForceRebuild`-snaps-animations note and the
  stock `ListView` child-cache note.
- `openspec/changes/refactor-reconciling-gui-rebuild/` (on the
  `refactor-reconciling-gui-rebuild` branch) — the abandoned change; `tasks.md` has the
  ABANDONED status banner and per-group notes on what was done.
- Memory: `forcerebuild-vs-reconciling-libgui`.
