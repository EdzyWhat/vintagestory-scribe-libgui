## Why

Scribe's animating GUI surfaces push almost every state change through `GuiBase.ForceRebuild()`,
which unmounts and recreates the *entire* widget tree — disposing every `State`,
`AnimationController`, `RenderObject`, and the press-capture the `EventDispatcher` holds as a
concrete Element reference. That single teardown is the shared root of a whole family of
bugs we have each fixed bespoke: the one-frame flicker after a rebuild, hover lost when a row
slides/rebuilds under a still cursor, the mass-delete first-click that doesn't register mid-collapse
(press captures Element A, the rebuild replaces it, release can never match), and caret/scroll-offset
loss across a rebuild. Each fix so far has been scaffolding to *smuggle identity past* a `ForceRebuild`
that need not happen on that surface. On LibGUI's **reconcile** path (`SetState` → dirty-only rebuild
→ `UpdateChild`/`CanUpdate` reuses the same Element+State+RenderObject when type+key match) that
identity is never torn down, so the entire class evaporates — no hover-refresh latch, no focus
re-home, no scroll capture-restore.

The forward-looking goal is the real driver: **Scribe wants to add many more animations, and each one
must be cheap to build, triage, and fix — not the bespoke, open-ended whack-a-mole every animation has
been so far.** Today each animation ships its own scaffolding to survive `ForceRebuild`, so every new
one re-fights identity loss from scratch. Making reconcile the substrate means new animations inherit a
surface where hover/focus/press-capture/controllers simply persist, and a single shared harness handles
enter/exit/reorder — so the *next* animation is "instantiate the harness," not "invent a survival
scheme." Fixing today's flicker/hover/click bugs is the near-term proof; lowering the cost of every
future animation is the point.

**This is deliberately NOT a retry of the abandoned `refactor-reconciling-gui-rebuild` change.** That
change was sold on making animations *stock* ("reconcile → delete the self-ticking stack"), a payoff
that is provably false (see `docs/animation-lessons-learned.md`: `AnimatedSize` has no completion
callback; the reconciler is positional, so a mid-list delete restarts trailing rows' motion). It was
build-clean and 102/102 tests green but **never playtested**, and the whole thing was discarded with
that one false sub-goal. This change adopts the *same reconcile mechanics* for a *different, correct*
goal — killing the identity-loss bug class and making future animations cheap — and **keeps and
generalizes** the self-ticking animation harness rather than deleting it.

## What Changes

- Adopt a **reconciling-rebuild discipline** for Scribe's animating surfaces: content that changes is
  built as a persistent `StatefulWidget` updated via `SetState`, so matching subtrees (and their
  `State`/`AnimationController`/`RenderObject`/press-capture) are preserved across updates. Update
  `gui-foundation-policy` to make reconcile the norm and reserve `ForceRebuild` for genuinely-new
  trees (view switches, fresh editor seed, lost-lock recovery) and dev hot-reload.
- **Prove it on the editor surface first** (the hardest identity case: a live multiline caret, focus,
  and an optimistic done-flag must all survive a mid-list delete). A written proof-of-concept gate and
  bail-out criterion sit early in the task list, so the whole branch is droppable if the editor
  conversion fights us — before any further surface is touched.
- **Convert the pinned-task surfaces** (`player-pins` / `pinned-task-hud`) and the read-view resync to
  reconcile once the editor proves the pattern, so external resync (another player toggles a task, an
  autosave lands) repaints without a full-tree rebuild.
- **Generalize the self-ticking animation harness** (`ScribeCollapsible` + `ScribeCollapseRegistry`)
  into one reusable enter/exit/reorder primitive that all animating rows share — the harness is the
  *deliverable*, explicitly NOT deleted in favor of stock `AnimatedSize`.
- Make **row identity stable** — `ValueKey<Guid>(TaskId)` instead of array-index keys, and stop the
  departing-row type-swaps at a slot — which is the real, bounded cost of reconcile (index keys and
  type-swaps are invisible under `ForceRebuild` but silently destroy caret/focus/optimistic state
  under a reconcile).
- The mass-delete first-click-doesn't-register bug is expected to resolve as a *consequence* of the
  editor reconcile (the delete control's element survives the rebuild). This **supersedes the standalone
  `fix-mass-delete-click-target` change ONLY on success** — that narrow fix stays parked as the fallback
  and ships if this branch is abandoned at the proof gate. No external/API behavior breaks either way.
- **Retain `ForceRebuild`** for read⇄editor⇄settings view switches, fresh editor seed, and lost-lock
  recovery — genuinely-new trees where a full rebuild is correct.
- Do this on a **dedicated branch** (`reconcile-animating-surfaces`), converted one surface at a time,
  each **playtested green before the next** — playtest is a first-class per-surface gate, the exact
  step the prior attempt skipped.

## Capabilities

### New Capabilities
- `gui-row-animation-harness`: a reusable, host-owned, identity-keyed self-ticking animation primitive
  for row enter/exit/reorder (generalizing `ScribeCollapsible`/`ScribeCollapseRegistry`), that survives
  both a reconcile and a `ForceRebuild` and requires no per-call-site survival scaffolding.

### Modified Capabilities
- `gui-foundation-policy`: reconcile (persistent content `StatefulWidget` + `SetState`) becomes the
  default update path for animating surfaces; `ForceRebuild` is reserved for genuinely-new trees and
  dev hot-reload, and its identity-teardown consequences (hover/focus/press-capture/controller loss)
  are documented as the reason.
- `gui-list-collapse`: the collapse mechanism's hover-currency and click-activation guarantees are
  restated to hold because the host reconciles (identity preserved) rather than via per-frame
  re-dispatch/latch workarounds; the mass-delete control remains *activatable* mid-collapse.
- `scribe-dialog-base`: the editor surface updates structural mutations (add/delete/reorder) via
  reconcile with stable `TaskId`-keyed rows; caret, focus, and scroll offset survive without the
  `ForceRebuild`-era re-home/capture-restore machinery; view switches keep `ForceRebuild`.
- `player-pins`: the HUD renders its pin list from persistent content updated via `SetState` on
  pin-push/tick/toggle rather than rebuilding the whole HUD tree.

## Impact

- **New:** a reusable row-animation harness (generalized from `src/Mod/ScribeCollapsible.cs`); possibly
  a Scribe-owned reconcile-friendly list container for the read view (reference implementation exists as
  `src/Mod/ScribeListView.cs` on the abandoned branch — to be *mined*, not merged, since that branch is
  259 commits behind main and rewrote a since-split file).
- **Modified:** `src/Mod/ScribeDialogBase*.cs` (editor structural mutations, focus/scroll paths),
  `src/Mod/ScribeEditorContent.cs`, `src/Mod/HudScribePins.cs`, `src/Mod/ScribePinnedContent.cs`,
  `src/Mod/ScribeReadContent.cs`; likely simplification of the hover-refresh latch and scroll
  capture-restore where reconcile now holds identity.
- **No Core changes** — entirely the Mod/GUI adapter layer; `src/Core/` stays API-free.
- **No new dependencies** — vanilla `VintagestoryAPI` + the existing `gui` (LibGUI) hard dep; built on
  LibGUI's public `SetState`/reconcile and render-object APIs (as `ScribeMultilineField` already is).
- **Conditionally supersedes:** the standalone `fix-mass-delete-click-target` change — retired only if
  this branch succeeds through the editor proof gate; kept parked as the fallback fix otherwise.
- **Risk:** high-touch conversion of the exact focus/caret/scroll/external-resync invariants the current
  `ForceRebuild` machinery guarantees — mitigated by the dedicated droppable branch, the editor-first
  proof-of-concept gate with an explicit bail-out, incremental per-surface conversion, and a mandatory
  per-surface playtest gate (the step the 2026-07-27 attempt skipped).
- **Docs:** `docs/animation-lessons-learned.md` already carries the 2026-08-09 reframing (reconcile for
  identity, not stock animations); `VSAPI-NOTES.md` `## LibGUI` to be updated with the discipline.
