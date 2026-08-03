## Why

`ScribeDialogBase.cs` (~2357 lines) and `ScribeModSystem.cs` (~1974 lines) have grown into
catch-all "god files" that mix several unrelated concerns each. Their size makes them hard to
navigate, review, and reason about — and works against the project's secondary goal of clear,
conventional, well-explained code the author can learn from. Splitting them into cohesive units
improves legibility with zero player-facing change. This is deliberately carved out of the
`repo-cleanup-and-roadmap-pass` change because a structural refactor carries more behavior-risk
than that pass's docs/comment/dead-code tidy-ups and deserves its own gate and revert boundary.

## What Changes

- Split `src/Mod/ScribeDialogBase.cs` into multiple `partial class ScribeDialogBase` files, one
  per concern (candidate seams: title-edit, guestbook, pinned view, backdrop/layout,
  input-capture/focus, view-model/state), each in the same namespace and assembly.
- Split `src/Mod/ScribeModSystem.cs` likewise into `partial class ScribeModSystem` files
  (candidate seams: icon/font registration, host registry, network handlers, backdrop-bitmap
  cache, lifecycle).
- Pure relocation only: no rename, no visibility change, no signature change, no logic change.
  The public API and runtime behavior are identical before and after.

## Capabilities

### New Capabilities
<!-- None — no new behavior. -->

### Modified Capabilities
<!-- None — this is a behavior-preserving structural refactor. No requirement changes; the
     scribe-dialog-base and host-registry capabilities keep their existing specs verbatim. -->

## Impact

- **Code:** `src/Mod/ScribeDialogBase.cs` and `src/Mod/ScribeModSystem.cs` each become a set of
  partial-class files in the same namespace/assembly. No other source changes.
- **Behavior/API:** none — identical public surface and runtime behavior.
- **Tests:** `tests/Core.Tests` must pass unchanged; the Atlas integration suite is the in-game
  safety net (this is a Mod-layer GUI/lifecycle refactor, not a Core change).
- **Out of scope:** any comment/doc edits (those belong to `repo-cleanup-and-roadmap-pass`);
  extracting new standalone helper *types* (only used selectively where a cluster is genuinely
  standalone with no private-state coupling); any behavior or spec change.
