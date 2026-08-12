## Why

Task/note rows currently **pop into existence** — a freshly added row appears at full height in a
single frame, with no motion, while its sibling *removal* already animates (rows collapse out
smoothly via `ScribeAnimatedList`). That asymmetry reads as unfinished: deletes glide, adds snap.
The animation harness generalized in `reconcile-animating-surfaces` was explicitly pre-wired for
this — Core `ScribeListDiff` already computes an `Appeared` set, `ScribeAnimatedList` already
exposes it as an (unused) `lastAppeared` seam, and `ScribeRowSizeDirection.Reveal` (grow height
0→1) is already built. This change wires those existing on-ramps up so row insertion animates in,
mirroring the collapse-on-removal that already ships.

## What Changes

- **New rows animate into view instead of popping in**, across all three animating surfaces
  (editor, Pin Tab, Read view) — they route through the same `ScribeAnimatedList` container that
  already animates removals, so each surface adopts it with no bespoke per-surface machinery.
- **The editor is migrated onto `ScribeAnimatedList` as part of this change** (folding in
  `extract-animated-task-list` §6.1, previously a deferred follow-up). Today the editor hand-wires
  its own collapse choreography — a `DepartingRows` list on the dialog, a ghost splice in
  `ScribeEditorContent`, an `OnDepartingCollapsed` cleanup callback — duplicating exactly what the
  container already does for Read/Pin. Routing the editor through the container deletes that
  duplication and, critically, means insertion-entry is wired **once** (in the container) instead
  of once in the container plus again inline in the editor. After this change 3 of the 4 animating
  surfaces (editor, Read, Pin Tab) share one animation path; only the HUD remains bespoke, for a
  principled reason (its undo-window `Delayed` policy), tracked as its own follow-up.
- **Two entry modes, chosen by focus-safety** (the load-bearing rule):
  - A **non-focused** appearance (quick-add on a closed/other surface, a row appearing on the Pin
    Tab / Read view, a peer row) uses the **height-grow slide** (`ScribeRowSizeDirection.Reveal`,
    factor 0→1) so rows below settle down to make room.
  - A **freshly-created, auto-focused editor row** (the one you immediately type into) enters
    **full-height with an opacity fade** (0→1), NEVER a height-grow — a shrinking row hides the
    caret and mislocates clicks mid-animation (documented in `docs/animation-lessons-learned.md`:
    height-grow is not focus-safe; fade/slide-at-full-size is). This preserves the exact
    focus/caret guarantee the reconcile conversion won.
- **Symmetry note (optional polish, not load-bearing):** today's *removal* collapse is
  height-only (a pure slide, no fade). Layering a matching opacity fade onto the slide paths
  (both enter and exit) is in-scope-if-cheap for visual consistency, but the hard requirement is
  only the focus-safe fade-in for the auto-focused row.
- The Core `Appeared` seam and the `Reveal` direction stop being dead code — they gain their first
  consumer.

Non-goals (explicitly deferred): reorder/swap slide animation (moving an existing row to a new
slot) — the harness can support it later but it is not part of this change; and any new persisted
state, sync, or Core model change (this is view-layer motion only).

## Capabilities

### New Capabilities
- `gui-row-insertion-animation`: When a row is added to an animating list, it enters with motion
  rather than appearing in a single frame — a height-grow slide for non-focused appearances and a
  focus-safe full-height opacity fade for a freshly-created auto-focused editor row. Covers which
  entry mode applies, the focus-safety invariant, rebuild/reconcile stability of the entry
  animation, and that entry motion never disturbs caret position or first-click targeting.

### Modified Capabilities
<!-- None. The row-animation harness spec (gui-row-animation-harness) is introduced by the
     not-yet-archived reconcile-animating-surfaces change and does not exist in openspec/specs/
     yet, so this change adds a sibling capability rather than delta-editing a spec that isn't on
     main. When reconcile archives, the harness spec and this one both describe the same container
     from complementary angles (harness = the primitive; this = the insertion behavior). -->

## Impact

- **Depends on `reconcile-animating-surfaces`** being merged/available: this change consumes
  `ScribeAnimatedList`, `ScribeListDiff.Appeared`, and `ScribeRowSizeDirection.Reveal`, all of
  which land in that change. Sequence after it (or on the same branch).
- **Affected code (view layer only):**
  - `src/Mod/ScribeAnimatedList.cs` — wire the `lastAppeared` seam to wrap appeared ids in a
    `Reveal` animation (or a fade for the focus-flagged row); add a `focusedAppearedId` input so the
    container picks fade vs. grow per D1.
  - `src/Mod/ScribeRowSizeAnimation.cs` — the `Reveal` direction is built; verify/settle its
    rebuild-stable behavior for the enter case (self-ticking, resumes across ForceRebuild/reconcile).
  - **Editor migration onto the container** (`extract-animated-task-list` §6.1, folded in here):
    `src/Mod/ScribeEditorContent.cs` routes its rows + `ScribeFrozenEditorRow` ghosts through
    `ScribeAnimatedList` (drag-reorder state stays in a thin editor `State` that builds the items);
    `src/Mod/ScribeDialogBase.Editor.cs` / `.Lifecycle.cs` shed the hand-wired `DepartingRows`
    bookkeeping and `OnDepartingCollapsed` cleanup, delegating removal + focus fix-up to the
    container (focus fix-up rides `OnDepartureSettled`). The `needsEditorCollapseCleanup` deferred
    flag is removed (the container self-cleans via deferred `SetState`).
  - The surface adopters (`ScribeEditorContent`, Pin Tab, `ScribeReadContent`) pass whichever
    new-row is auto-focused so the container can pick fade vs. grow.
  - A small host-owned-controller fade primitive (a `ScribeFade`-style `Opacity` wrapper driven by
    the `ScribeAnimationRegistry`, NOT `ScribeFadeText`'s self-owned controller — that one snaps on
    `ForceRebuild`), for the focus-safe fade-in.
- **Core:** no model/persistence/sync change. `ScribeListDiff.Appeared` already exists; at most a
  Core.Tests addition covering the appeared-set → entry-mode selection if any pure logic is added.
- **No new dependencies.** Vanilla `VintagestoryAPI` + the existing harness only.
