## Why

The pinned-task **HUD** is the last of the four animating surfaces still hand-wiring its own row
choreography. `extract-animated-task-list` built the reusable `ScribeAnimatedList` container and
adopted it on the Read view and Pin Tab; `animate-row-insertion` (§0) migrates the editor onto it
too. After that, the editor, Read view, and Pin Tab all share one animation path — but the HUD
still owns a private copy: `departing` / `BeginDeparting` / `ReconcileDeparting` /
`OnDepartingCollapsed`, plus its own `ScribeFadeText` fade and undo-window timing.

Keeping the HUD bespoke is a real maintenance cost: every collapse/hover/scroll fix has to be made
(and kept in sync) in two places, and it's the surface most prone to regressions (the reconcile
conversion already surfaced a long-standing `ScribeFadeText` undo-fade bug here). The HUD was left
for last on purpose — it's the ONE surface that needs the container's **`Delayed` removal policy**
(hold-at-full-height undo window, optionally fading, before the collapse), which today is a guarded
stub that throws. This change wires that policy for real and moves the HUD onto the container,
retiring the duplicated choreography so exactly one animation path remains.

## What Changes

- **Wire the `Delayed` removal policy in `ScribeAnimatedList`** (currently `throw NotSupportedException`):
  a departed row under this policy holds at full height for an undo window — optionally fading its
  content via the shared, host-owned-controller fade primitive — and only then collapses using the
  same height-collapse mechanism every other surface uses. The collapse *shape* is unchanged; only
  *when* it begins differs (as the existing `RemovalPolicy` requirement already specifies).
- **Migrate the HUD (`HudScribePins.cs`) onto `ScribeAnimatedList`** with the `Delayed` policy,
  deleting its hand-wired `departing` / `BeginDeparting` / `ReconcileDeparting` /
  `OnDepartingCollapsed` machinery. The HUD keeps its distinct *behavior* (undo window + text fade +
  sink), now expressed through the container's policy rather than a private copy.
- **Preserve the HUD's undo semantics exactly** ([[hud-undo-window-is-policy-hiding]]): the undo
  window exists ONLY because the HUD hides the Completion Policy, so a completion can be a silent
  delete that needs a misclick-rescue window. That behavior is retained — this is a mechanism
  migration, not a UX change.
- **Retire now-dead duplicated primitives** and confirm one choreography path remains across all
  four surfaces (`extract-animated-task-list` §6.3, the final consolidation step).

Non-goals: no change to the HUD's undo-window *duration* or fade *feel* (behavior-preserving); no
Core model/persistence/sync change (view-layer only); no change to the other three surfaces'
already-migrated behavior.

## Capabilities

### Modified Capabilities
<!-- The animated-task-list capability (the RemovalPolicy requirement, incl. the Delayed scenario)
     is introduced by the not-yet-archived extract-animated-task-list change and is not in
     openspec/specs/ yet, so this change adds sibling requirements describing the Delayed policy as
     WIRED and the HUD's behavior-preserving adoption, rather than delta-editing a spec that isn't on
     main. When extract-animated-task-list archives, both describe the same container from
     complementary angles. -->

### New Capabilities
- `gui-hud-delayed-removal`: The pinned-task HUD removes a completed/unpinned/deleted row through the
  shared animation container using a delayed-removal policy — the row holds at full height for an
  undo window (optionally fading) before collapsing — preserving the HUD's misclick-rescue undo
  semantics while sharing one animation path with the editor, Read view, and Pin Tab.

## Impact

- **Depends on `extract-animated-task-list`** (the container + the `Delayed` policy stub) and is
  best sequenced **after `animate-row-insertion`** (which migrates the editor and proves the
  container carries a fourth, interactive consumer). Not a hard code dependency on insertion, but
  doing it after keeps one migration in flight at a time.
- **Affected code (view layer only):**
  - `src/Mod/ScribeAnimatedList.cs` — replace the `Delayed` guard with a real implementation
    (hold-then-collapse timing + optional fade), reusing the host-owned-controller fade primitive
    from `animate-row-insertion`.
  - `src/Mod/HudScribePins.cs` — route rows through `ScribeAnimatedList(Delayed)`; delete
    `departing` / `BeginDeparting` / `ReconcileDeparting` / `OnDepartingCollapsed`; keep the HUD's
    completion/unpin/delete handlers, which now just mutate data and let the container animate.
  - `src/Mod/ScribeRowSizeAnimation.cs` / the fade primitive — verify the `Delayed` timing composes
    with the existing collapse controller without a second registry.
- **Core:** no model/persistence/sync change; at most a Core.Tests addition if the delayed-timing
  gets any pure logic (hold-window elapsed → begin-collapse predicate).
- **No new dependencies.** Vanilla `VintagestoryAPI` + the existing harness only.
