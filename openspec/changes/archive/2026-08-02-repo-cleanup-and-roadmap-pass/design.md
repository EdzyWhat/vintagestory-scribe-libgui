## Context

Post-v0.2.0 the repo carries four kinds of drift, surfaced by a full audit:

- **Docs:** `README.md:64/66/67` name `gui_2.0.0.zip` for Atlas staging, but every code path
  stages `gui_3.1.0.zip` (`modinfo.json:13` = `"gui": "3.1.0"`, `Integration.Tests` refs) — an
  outright setup-breaking error. `README.md:11` still says "early development" (shipped, v0.2.0,
  live on the mod DB). `openspec/config.yaml:38` still says `Current focus: v1`.
- **Roadmap fragmentation:** roadmap content lives in `RELEASE.md` (which now stacks a ~95%-done
  v0.1.0 plan *and* the live v0.2.0 cut, with shipped items still written as "post ASAP"),
  `ROADMAP.md` (a forward map accreting strikethrough "done"/"superseded" history), `config.yaml`,
  and `CHANGELOG.md` (the proper shipped record).
- **Dead code / orphaned assets:** `ScribeBackdrops.LecternSettings` and `ScribeBackdrop.Wrap`
  are unreferenced (the live path is the inlined `ScribeDialogBase.WrapBackdrop`);
  `lecternbackdrop.png` is git-tracked and ships but is wired to nothing;
  `lecternsettingsbackdrop.png` is referenced but does not exist; local `*-og`/`.psd` backups and
  ~70 `.DS_Store` files clutter the tree.
- **Comment verbosity:** the large GUI files read as design journals (`ScribeDialogBase.cs` ~34%
  comments, `ScribePlayerSettings.cs` >50%), with dated crash post-mortems on single fields and
  four near-identical "plain bool, no clamp" summaries.

This is a behavior-preserving cleanup. The only spec-level change is `gui-backdrop` (the reserved
per-view path is deleted). All player-facing behavior stays identical and `tests/Core.Tests` must
pass unchanged. The god-file split — the one piece that could plausibly alter runtime behavior —
is deliberately carved out into its own change (`split-large-gui-files`), leaving this pass free
of runtime-affecting edits.

## Goals / Non-Goals

**Goals:**
- Correct documentation so a contributor can set up and build without hitting the `gui_2.0.0`
  error, and so the roadmap has one clear home per concern.
- Remove confirmed-dead code and the unwired shipping asset; clear local backup/junk files.
- Reduce comment noise (moderate) while preserving the substantive "why" and `(change-name)`
  traceability.

**Non-Goals:**
- No player-facing behavior change; no codec/persistence version bump.
- Not touching `src/Core`'s no-VS-API invariant.
- The god-file split is out of scope here — tracked separately as `split-large-gui-files`.
- Not archiving or advancing the two near-complete changes (`v1-release-checklist`,
  `scribe-0-2-0-release-content`) — left alone per decision.
- No aggressive comment strip (the `(change-name)` tags stay); no new dependency.

## Decisions

### Track ordering: docs → dead-code/assets → comments
Ordered lowest-risk-first, each track independently committable. The god-file split — the only
work here that could plausibly break runtime behavior — was lifted out into its own change
(`split-large-gui-files`) so this pass stays entirely behavior-neutral and can land ahead of the
riskier refactor.

### Roadmap consolidation: CHANGELOG is the shipped record; RELEASE = live cut; ROADMAP = forward
- Move `RELEASE.md`'s fully-shipped v0.1.0 plan and the historical "Critical path"/"Settled
  decisions" prose out; `RELEASE.md` keeps only the in-flight v0.2.0 cut (and future cuts).
- Move `ROADMAP.md`'s strikethrough and "Done / superseded" history into `CHANGELOG.md` (or drop
  it where `CHANGELOG` already records it); `ROADMAP.md` becomes a pure forward tier-map.
- `CHANGELOG.md` stays Keep-a-Changelog format; shipped-plan detail is summarized, not pasted
  wholesale (the git history and archived openspec changes are the exhaustive record).
- **Alternative considered — one merged ROADMAP:** rejected; the release-cut checklist and the
  long-range tier map serve different audiences/cadences and are cleaner apart.

### Dead-code removal: delete `LecternSettings`, `ScribeBackdrop.Wrap`, and the whole `ScribeBackdrop` class if empty
`ScribeBackdrop.Wrap` is the only member of the `ScribeBackdrop` helper class; if removing it
leaves the class empty, delete the class. `ScribeBackdrops` (the spec-holder with
`LecternPage`/`NotebookPage`/`ClockmakerPage`) stays. Update the `gui-backdrop` spec (delta) so
the docs match the code. Confirm no `<see cref>` doc-comments dangle after removal (they'd warn).

### Asset removal: untrack `lecternbackdrop.png`; delete local backups + `.DS_Store`
`git rm` `lecternbackdrop.png` (tracked). The `*-og`/`.psd`/`lectern (OG)/` files are already
gitignored, so just delete them from the working tree. `.DS_Store` files are untracked junk;
delete them (including the two inside archived openspec dirs) and confirm `**/.DS_Store` is
gitignored so they don't return to the tree.
- **Risk — `lecternbackdrop.png` is a real fallback?** No: `ScribeBackdrops` points at
  `scribe-lectern.png`/`scribe-notebook.png`/`scribe-clockmakers-notebook.png`; the flat-color
  path is the code fallback, not this PNG. Verified unreferenced.

### Comment simplification: moderate, mechanical, comment-only
Per-file editing rules: collapse multi-line crash/incident post-mortems to a single explanatory
line (keep the "what breaks and why" nut); delete dated `(2026-..)` clarifying asides; deduplicate
the repeated boilerplate summaries (e.g. state "plain bool, not normalized" once or inline). KEEP
`(change-name)` tags and any comment that explains a non-obvious invariant. No signature, name, or
logic edits in this track — a `git diff` should show only comment lines changed.

## Risks / Trade-offs

- **[Comment pass accidentally edits code]** → enforce "comment lines only" per commit; review
  the diff for any non-`//`/`///` change before committing.
- **[Roadmap move loses information]** → nothing is deleted outright; shipped/superseded content
  is relocated to `CHANGELOG.md` (or already recorded there) and remains in git history.
- **[`gui-backdrop` spec drift on archive]** → the delta MODIFIES the exact existing requirement
  header and REMOVES the per-view requirement with a Reason/Migration, so archive reconciliation
  is clean (mind the archive-order header-drift gotcha if another backdrop change is queued).

## Migration Plan

No data or user migration. Docs and comments are non-runtime. Dead-code and asset removal are
behavior-neutral (verified unreferenced). Rollback is reverting the relevant commit(s); tracks are
committed separately so any one can be backed out without the others.

## Open Questions

None blocking.
