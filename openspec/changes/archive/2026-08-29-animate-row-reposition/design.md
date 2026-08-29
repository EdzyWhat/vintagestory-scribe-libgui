## Context

`ScribeAnimatedList` (`src/Mod/ScribeAnimatedList.cs`) already diffs its identity-keyed item set every
build to detect **departures** (a live id vanishing → frozen ghost, collapsed via
`ScribeRowSizeAnimation(Collapse)`, which genuinely shrinks LAYOUT height so rows below slide up) and
**appearances** (a new id → `ScribeSlideIn`, a PAINT-only translate+fade; the row's full height is
reserved in layout from frame 1). Neither path animates a **surviving** row's own slot changing — a
row that was live last frame and is still live this frame, just at a different index, renders bare
(`rows.Add(live)`) and snaps straight to its new Column position. That's the exact jump the user is
seeing when a Top-inserted row/pin (`update-pins-1-3-3`) shoves every existing row down in one frame.

Read `ScribeRowSizeAnimation.cs` in full this session to ground the design in what's already
load-bearing:
- Every existing row animation is a **self-ticking `AnimationController` owned by a host-passed
  `ScribeAnimationRegistry`, keyed by the row's stable id** — not a stock LibGUI implicit-animation
  widget (`AnimatedSize`/`AnimatedSlide`/etc. all snap on `ForceRebuild`, which remounts fresh and
  re-inits `Begin==End==target`). This is the established, *not* abandoned, pattern (distinct from
  the separately-abandoned "convert `ForceRebuild` to true reconciling so stock animations survive"
  effort in `docs/animation-lessons-learned.md` — that stays dead; this change extends the
  self-ticking pattern the same way `ScribeRowSizeAnimation`/`ScribeSlideIn` already do).
- `ScribeSlideIn` deliberately uses a **fixed** slide distance (`DefaultSlideDistance = 18f`), not a
  measured one — "the widget can't know the row's height at build time." That shortcut works for
  entry because a brand-new row has no prior on-screen position to match; any starting offset reads
  as "arriving from nearby." It does **not** work for reposition: a surviving row **was** visibly at
  some Y last frame, so its animation must start from that real offset or the motion itself pops at
  the start. The pixel delta must be real, not assumed.
- A real post-layout geometry read already exists elsewhere in the mod: `Element.RenderObject?.Size`
  (`ScribeAddKindPicker.cs:112`) and the render-tree walk `Scrollable`/`ComputeContentSpaceY`-style
  code uses for scroll-follow (`ScribeDialogBase.Lifecycle.cs`). Reading a mounted row's actual
  rendered geometry post-layout is therefore a proven technique in this codebase, not a new one.

## Goals / Non-Goals

**Goals:**
- A surviving row whose slot changes between builds animates a displacement from its real previous
  position to its real new one, instead of snapping — regardless of *why* the slot changed (a row
  inserted above it, a row removed elsewhere, or an explicit reorder like a Sink completion).
- The mechanism lives entirely inside `ScribeAnimatedList`/`ScribeAnimationRegistry`, so all four
  current surfaces (editor, Read view, Pin Tab, HUD) and any future one get it automatically by
  rendering through the container — no surface supplies reposition-specific code.
- Reuse the existing self-ticking, registry-keyed discipline (same curve/duration family as
  `ScribeRowSizeAnimation`/`ScribeSlideIn`) so the motion survives `ForceRebuild` and reconcile like
  every other Scribe row animation, and reads as part of the same animation language.

**Non-Goals:**
- Reworking the entry (`ScribeSlideIn`) or departure (`Collapse`) motions themselves — this only adds
  a third motion for the case neither covers (a survivor's position changing).
- Reopening the abandoned `ForceRebuild`→reconciling conversion (`docs/animation-lessons-learned.md`)
  to get LibGUI's stock implicit animations working — the self-ticking registry pattern already
  solves survival across rebuilds; this change extends it, not the dead effort.
- New causes of reordering (e.g. player drag-and-drop) — only rows that already move today (via
  insertion-edge settings, deletion, or an existing completion-policy reorder like Sink) are in
  scope. Whatever mutation paths exist today get animated motion; none gain new reordering behavior.
- A general-purpose FLIP/animation framework beyond this container's own needs.

## Decisions

### D1: Reposition is detected from the render-order slot diff, not the item-set diff
The departure/appearance diff already answers "did this id's presence change?". Reposition needs a
second question, computed alongside it: for an id that is live in **both** `prevRenderOrder` and this
build's `diff.RenderOrder` (i.e. neither departing, reviving, nor freshly appeared), did its index
change? This reuses data the container already produces every build — no new diff primitive in Core,
just an additional comparison in `ScribeAnimatedListState.Build`.

### D2: The pixel delta is measured, not assumed — resolved via per-row height caching, not a live
geometry read

**Resolved during implementation (2026-08-29), differently than proposed above.** The original plan
(a per-row `GlobalKey` + a post-layout `RenderObject.LocalToGlobal` read) turned out to have a real
chicken-and-egg problem: `Build()` runs *before* layout, so a survivor's true NEW position for the
current frame genuinely isn't knowable at the point `Build()` needs to emit the offset transform, and
correcting it a frame late would mean the row visibly jumps backward before animating forward — worse
than today's snap, not better.

The actual mechanism sidesteps needing a live "new position" at all: **every row rendered through the
container is wrapped in a transparent `ScribeSizeReportWidget`** (a `RenderProxyBox` subclass,
`ScribeSizeReportRender`, that calls `base.PerformLayout()` — already a full pass-through per LibGUI's
own `RenderProxyBox` — then reports the resulting `Size` back to the container). This populates a
per-id height cache (`ScribeAnimatedListState.knownHeight`) every build, for every row, whether or not
it's currently animating.

With that cache, a survivor's old and new cumulative Y is **computed analytically**, not measured
live: walk `prevRenderOrder` summing each preceding id's cached height for the old position, walk the
new `RenderOrder` the same way for the new position — both purely from data already on hand *before*
this frame's layout runs. The delta seeds `ScribeRowReposition`'s `Transform.Translate`, which eases it
to zero exactly like `ScribeSlideIn` eases its fixed offset to zero — except this offset is real.

This also resolves the entering-row edge case cleanly: `ScribeSlideIn`'s own doc comment already
establishes that an entering row's LAYOUT height is its full natural height from frame 1 (only its
*paint* animates) — so by the time it has been live for one build, its height is cached like any other
row. On the one build it first appears, survivors displaced by it under-count the delta by that row's
(not-yet-cached) height; the very next build has the real value and the animation's target offset
(recomputed every build, not seeded once — see `ScribeRowReposition`'s doc comment) self-corrects. No
one-frame global lag, no backward-then-forward pop — just a small, self-healing correction bounded to
the one row whose height was momentarily unknown.

A second, unplanned but welcome property fell out of using *real* measured heights for both sides of
the calculation: a row currently mid-collapse (`ScribeRowSizeAnimation`, whose *reported* height
already shrinks continuously via real layout each frame) contributes a smoothly-changing height to this
same cumulative sum — so a survivor below an ongoing collapse sees near-zero incremental delta per
build (correctly: that motion is already fully handled by the collapse's own layout-height animation)
and only a genuine one-frame index change (an insertion, or an explicit reorder with no gradual
shrink) produces the large, one-shot delta this mechanism is meant to catch. No special-casing was
needed to avoid double-animating a departure-caused reflow.

### D2a: A row that once entered must stay reposition-eligible for the rest of its life
**Found via playtest, fixed 2026-08-29.** The first shipped version gated entry and reposition as
mutually exclusive (`if (entering.Contains(id)) {...} else if (repositionGeneration.TryGetValue...)`).
That's wrong given `entering`'s own documented lifetime: an id is added to `entering` on its first
appearance and **never removed except on departure** (a settled `ScribeSlideIn` is kept forever as an
inert pass-through, for the same focus-safety reason every wrap-forever wrapper in this file exists).
Since every brand-new task enters exactly once and then stays in `entering` permanently, the `else if`
branch was unreachable for it forever — a freshly created task could never reposition-animate again
for the rest of that mounted session, exactly matching the reported symptom (works only after closing
and reopening the dialog, or switching tabs and back — both go through `GuiBase.ForceRebuild`, which
resets `ScribeAnimatedListState` fully, including `entering`, letting the row fall through to the
normal survivor path on its next appearance).

The fix: the two wrappers are independent and **stack** rather than being mutually exclusive. A row
is excluded from reposition only on the exact build it first appears (enforced by
`survivorTargetOffset`'s own `!prevLiveIds.Contains(id)` guard, which needs no change) — not by
`entering` membership, which persists far longer than that. From the next build on, a row is eligible
for reposition regardless of whether its own entry motion has finished playing.

### D2c: "Recompute every build" was the wrong self-correction mechanism — seed once instead
**Found via playtest, fixed 2026-08-29.** Two related bugs surfaced together: (1) a survivor displaced
*solely* by a brand-new row (the single most common case — inserting one task) never animated at all,
and (2) this specifically REGRESSED the exact scenario the whole change exists to fix (siblings jumping
when a new task is created), even though non-insertion-caused repositions worked.

Root cause of (1): `PrefixY`'s missing-height default was `0f`. For a single fresh insertion, the ONLY
thing separating a survivor's old and new cumulative Y is the entering row's own (not-yet-measured)
height — defaulting that to zero makes the computed delta exactly zero, which the `RepositionEpsilon`
gate then filters out entirely. **Fix:** default a missing height to the smallest currently-known row
height, not zero (`PrefixY`) — a reasonable stand-in given a brand-new task always starts as empty
text, so its true height is very likely close to the shortest already-observed row.

Root cause of (2), and the deeper one: the original design fed `ScribeRowReposition.TargetOffsetY`
fresh from the container on every build (D2's "self-correcting" rationale). But the container's own
`Build()` does NOT reliably re-run every animation tick — it only reruns when something calls
`SetState`/`MarkNeedsBuild` on the CONTAINER's own Element, which the reposition motion itself never
does (unlike collapse/entry, which call back in via `OnEnd`). Once the underlying order stabilizes (the
build immediately after the trigger), `survivorTargetOffset` no longer contains the animating row at
all, so if the container's `Build()` happened to rerun for ANY unrelated reason while the reposition
was still easing (a concurrent collapse/entry tick elsewhere, a hover refresh, anything), the freshly
recomputed target would read as ~0 and the widget's own `Build()` — which multiplies `TargetOffsetY *
(1 - eased)` — would render `0 * anything = 0`, SNAPPING the row to rest immediately regardless of how
far through its 200ms easing curve it actually was. This intermittent truncation (not a hard bug like
(1), just a race with however many extraneous container rebuilds happen to land inside the animation's
short window) plausibly explains why even non-insertion repositions could look inconsistent.

**Fix:** `ScribeRowReposition` now captures `TargetOffsetY` ONCE, exactly when a fresh generation
attaches (`InitState`, or `UpdateWidget` when the generation-embedded `Id` changes), into a private
`seededOffset` field — mirroring how `ScribeSlideIn`'s `SlideDistance` is a fixed value, never re-read
from an external prop mid-animation. Later container rebuilds that pass the SAME generation are simply
ignored by the widget (`UpdateWidget` returns early); the motion is now immune to how many times the
container itself rebuilds while it eases.

### D2d: The seed must survive an Element remount too, not just a reconciling rebuild
**Found via playtest, fixed 2026-08-29 (task 3.4).** A Sink completion: the sinking row itself
instant-jumped to the bottom instead of animating, while merely-displaced siblings in the SAME event
animated fine.

D2c's fix (seed once, ignore later `TargetOffsetY=0` builds) only covers the case where
`ScribeRowRepositionState`'s Element *reconciles* across an unrelated later rebuild (`UpdateWidget`'s
early-out). It does not cover the case where that Element gets *remounted*: LibGUI's reconciler matches
a Column slot by INDEX, not by id (`MultiChildElement.Update` — confirmed by decompile, and already
documented in `VSAPI-NOTES.md` § LibGUI as "keying by a stable Guid is necessary but not sufficient —
the type at the slot must also stay stable"). Any row whose slot shifts between builds gets its Element
unmounted at the old slot and a fresh one mounted at the new slot, re-running `InitState`. `seededOffset`
was a private `State` field, so a remount discards it and reseeds from whatever `TargetOffsetY` the
CURRENT build happens to pass — correct on the triggering build (a real delta), but zero (the
materialize-step fallback) on any later build, snapping an in-flight motion to rest.

Hand-tracing a concrete Sink event index-by-index through `MultiChildElement.Update` confirmed the
mechanism is real (both the sinking row and its neighbors get a fresh, correctly-seeded mount on the
triggering build) but could **not** confirm it uniquely explains an *asymmetry* between the sinking row
and its neighbors within that same triggering build — both are symmetric under the reorder in every
concrete example traced. The differentiating trigger for a LATER remount specifically hitting the
sinking row (another `RebuildHudBody()` landing while its 200ms motion is still easing, e.g. the HUD's
independent 250ms tick) was not pinned down empirically (no in-game trace log was captured).

**Fix (applied regardless of the precise trigger, since it closes the underlying vulnerability class
either way):** `seededOffset` moved from a private `State` field into `ScribeAnimationRegistry.Seed(id,
value)` — keyed exactly like the `AnimationController` already is, so it survives a remount the same way
the controller's elapsed progress does. A re-attach to an id already seeded (whether via ordinary
reconcile OR a forced remount) gets back the ORIGINAL seeded value, never a later build's stale/zero one.
Released alongside the controller in `Registry.Release`.

### D2b: `ScribeAnimatedListState`'s own fields do not survive `ForceRebuild` — and that's fine
Decompiling `Gui.dll`'s `GuiBase.ForceRebuild()` during implementation confirmed it unconditionally
unmounts the entire root element and mounts a brand-new tree — every `State` in the tree, including
`ScribeAnimatedListState`, is genuinely recreated (not reconciled). This is why `firstBuild` already
existed and correctly suppresses spurious motion right after such a reset. The height cache and
generation bookkeeping this change adds (`knownHeight`, `repositionGeneration`) live on that same
State and reset the same way — which is safe, because confirmed by reading the actual mod call sites
(`ScribeDialogBase.RebuildBody()`, the local, reconciling rebuild used for ordinary row mutations —
`ForceRebuild()` is reserved for structural changes like a view switch or fresh dialog open, per the
comment at `ScribeDialogBase.cs:380`), an ordinary insert/delete/reorder never actually triggers a
`ForceRebuild` at all, so the cache is intact exactly when reposition math needs it.

### D3: The animation is a new self-ticking motion, registered under a `move:<id>` key
Mirrors `ScribeSlideIn`/`ScribeRowSizeAnimation` exactly: a `StatefulWidget` obtains its
`AnimationController` from the host's `ScribeAnimationRegistry` by a namespaced id (`move:` — distinct
from the collapse key and the `enter:` key, so a row that is simultaneously finishing an entry and
starting a reposition doesn't collide), runs 0→1 over the shared duration/curve constants, and paints
a `Transform.Translate` from the measured delta to zero. Like the entry wrapper, once a row has ever
been repositioned it is safe to keep the (now-inert) wrapper for its remaining live lifetime rather
than type-swapping the slot back to bare — same reasoning `ScribeSlideIn` documents for entry (avoids
remounting the row's own field / dropping focus mid-edit).

### D4: One uniform rule — no per-cause branching
The container does not ask *why* a survivor's slot changed. An insertion above it, a removal
elsewhere, and an explicit reorder (e.g. `gui-hud-shared-row-animation`'s Sink-to-bottom move) all
produce the same observable fact — "this id's index changed between builds" — and get the identical
treatment. This is what makes the mechanism automatically cover reordering later without a second
change, per the goal of extending Scribe's shared animation vocabulary rather than fixing one bug.

## Risks / Trade-offs

- [Resolved, see D2] The originally-feared need for live post-layout geometry (and its one-frame-lag
  cost) did not materialize — the analytical cumulative-height approach needs no such read and has no
  lag beyond the bounded, self-correcting under-count for a row whose own height isn't cached yet.
- [Risk] Multiple simultaneous repositions (e.g. two rows removed at once, shifting several survivors)
  must each compute their own independent delta correctly. `animated-task-list`'s existing multi-
  departure requirement already proves the container can track several simultaneous per-id
  animations without cross-contamination; this reuses that same per-id independence.
- [Risk] A row that is *entering* AND *repositioning* in the same build (rare — e.g. a batch mutation)
  needs defined precedence. → Decision: an entering row never also gets a reposition wrapper — it is
  brand new, so "reposition from its old position" doesn't apply; only a row present in both the old
  and new render order can reposition.
- [Trade-off] This adds a third registry-keyed motion family alongside collapse/entry, growing
  `ScribeAnimatedList`'s internal bookkeeping. Accepted: the alternative (leaving reposition unanimated
  forever) is the exact regression this change exists to fix, and the container is already the single
  place meant to absorb this kind of complexity so surfaces stay animation-code-free.
