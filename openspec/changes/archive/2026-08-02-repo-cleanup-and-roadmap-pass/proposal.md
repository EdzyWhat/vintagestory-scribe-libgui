## Why

The repo has accumulated post-v0.2.0 drift: the README's build instructions name the wrong
`gui` dependency version (a setup-breaking error), roadmap content is fragmented across four
files with shipped plans still written as forward-looking, dead code and an unwired shipping
asset linger from the backdrop rework, and the large GUI files carry journal-style comments
(dated crash post-mortems, duplicated boilerplate) that obscure the code. A single housekeeping
pass tidies documentation, removes confirmed-dead code/assets, trims comment verbosity, and
splits the two god-files — improving accuracy for contributors and legibility for the author's
dev-skill goal, with no player-facing behavior change.

## What Changes

- **Docs & roadmap accuracy:**
  - Fix `README.md` staged-dependency references `gui_2.0.0.zip` → `gui_3.1.0.zip` (3 spots) and
    update its stale "early development" status line.
  - Fix `openspec/config.yaml`'s stale `Current focus: v1` pointer.
  - Reorganize the fragmented roadmap: archive `RELEASE.md`'s fully-shipped v0.1.0 plan and
    `ROADMAP.md`'s strikethrough / "Done superseded" history into `CHANGELOG.md`, leaving
    `RELEASE.md` = the current in-flight cut and `ROADMAP.md` = a pure forward map.
- **Dead code & orphaned assets removal:**
  - Remove `ScribeBackdrops.LecternSettings` (unused field) and `ScribeBackdrop.Wrap(...)`
    (unused method superseded by the inlined `ScribeDialogBase.WrapBackdrop`), plus the dangling
    reference to the nonexistent `lecternsettingsbackdrop.png`.
  - Delete the tracked-but-unwired `lecternbackdrop.png` so it stops shipping in the release zip.
  - Delete the local source/backup artifacts still in the working tree (`scribe-sm-OG.png/.psd`,
    `sketchbook-cover-og.png`, `sketchbook-mod.psd`, `textures/block/lectern (OG)/`) and remove
    the ~70 stray `.DS_Store` files (including two inside archived openspec dirs).
- **Comment simplification (moderate, comment-only, no behavior change):** across `src/`, cut
  multi-line crash post-mortems to a single line, drop dated `(2026-..)` asides and duplicated
  boilerplate prose (e.g. the four near-identical "plain bool needing no clamp" summaries in
  `ScribePlayerSettings.cs`), while **keeping** the `(change-name)` traceability tags and the
  substantive "why."

The god-file split (breaking up `ScribeDialogBase.cs` and `ScribeModSystem.cs`) is a separate,
higher-risk structural refactor tracked on its own as `split-large-gui-files` — deliberately kept
out of this behavior-neutral cleanup.

## Capabilities

### New Capabilities
<!-- None — this is a cleanup/refactor pass; no new behavior is introduced. -->

### Modified Capabilities
- `gui-backdrop`: removes the reserved-but-unwired "distinct per-view backdrops" path. The
  spec currently documents a `LecternSettings` reserved specification and a `Wrap` helper; both
  are dead code. The requirement that the mechanism supports a distinct settings-vs-page backdrop
  (and scenarios referencing the `Wrap` helper by name) are revised to describe what actually
  ships: each item/dialog declares its own single body backdrop spec, drawn by the dialog's own
  wrapping logic. The still-live per-item distinctness (Lectern / Notebook / Clockmaker each draw
  their own art) is retained.

## Impact

- **Docs/config:** `README.md`, `RELEASE.md`, `ROADMAP.md`, `CHANGELOG.md`,
  `openspec/config.yaml`. No code impact from this track.
- **Code (removal):** `src/Mod/ScribeBackdrop.cs` (delete `LecternSettings`, `Wrap`; the
  `ScribeBackdrop` class may be deleted entirely if `Wrap` was its only member).
- **Code (comments):** widespread comment edits in `src/Mod/*` and `src/Core/*`; no signature,
  type, or logic changes.
- **Assets:** remove `lecternbackdrop.png` from the tracked/shipping set; delete local backup
  artifacts and `.DS_Store` files.
- **Tests:** `tests/Core.Tests` must still pass unchanged (behavior-preserving); the Atlas suite
  is the in-game safety net for the GUI refactor. No spec/codec version bump.
- **Out of scope (deliberately):** the god-file split (its own change,
  `split-large-gui-files`); the two near-complete OpenSpec changes (`v1-release-checklist` 55/57,
  `scribe-0-2-0-release-content` 31/32) are left alone; adding a hard `ConfigLib` dep or any new
  mod dependency; any change to `src/Core`'s no-VS-API invariant.
