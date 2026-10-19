## Context

See proposal.md - Why. Roughly a third of `src/` is comments; many carry history. A trial on one file
(`ScribeReadContent.cs`) established that a comment-only rewrite can be proven safe by building before
and after and diffing `ilspycmd -il` output (identical), and that the result is ~6% fewer lines in
that file with all history references removed.

Constraints: `src/Core/` stays free of Vintage Story API references (comment-only edits cannot
violate this). `.github/workflows/*.yml` and `Directory.Build.props` are off-limits. Building needs
`VINTAGE_STORY` pointing at the game install (`/Applications/Vintage Story.app` locally). The build is
output-only and does not stage into the Mods folder when given `-o <dir>`.

## Goals / Non-Goals

**Goals:**
- Apply the commenting convention across `src/Mod`, `src/Core`, and `tests/` at a moderate trim level.
- Do it in parallel without the diffs flowing through the orchestrating conversation.
- Make "comments only" a machine-checked property, not a promise.

**Non-Goals:**
- No aggressive deletion of explanatory comments; no new documentation authoring.
- No code changes, renames, or refactors (those are the separate consolidation work).
- No change to locale `lang/*.json`, handbook text, or non-C# files.
- Removing the stale-prone `using` type-list comments is not part of this change.

## Decisions

**1. Verify with IL diff, not text-stripping.** Build the base commit and the edited tree to separate
output directories, decompile both with `ilspycmd -il`, and diff. *Alternative:* strip comments with
sed and diff the remainder. Rejected: `//` appears inside strings and URLs, so a regex strip is
unreliable, and it would not catch a change that only compiles differently. IL is the ground truth.
Debug-info line numbers change with comments, so DLL bytes differ; IL text does not.

**2. One agent per batch, each in its own git worktree.** Agents edit disjoint file sets, build in
their own worktree (separate `obj/`), and self-verify against their own base commit. *Alternative:*
all agents in the main tree. Rejected: concurrent builds collide on `obj/` and a stray edit by one
agent breaks another's build. Worktrees also leave each batch as a reviewable, discardable branch.

**3. Batch by size and file, ~2.5k lines each.** About 15-20 batches over `src/Mod`, with
`HudScribePins.cs` (2.3k lines, heaviest comments) on its own, `src/Core` as one batch, and `tests/`
as a final batch. Related files (for example the `ScribeDialogBase.*` partials) stay together so
cross-references in comments stay consistent.

**4. Agents return a short report, not diffs.** Each report is at most ~15 lines: files touched,
before/after comment and total line counts, IL-identical yes/no, and a list of flagged uncertain
comments (including any stale-comment corrections). The orchestrator reviews the flags and spot-checks
one or two files per batch rather than reading every diff.

**5. A per-file revert on IL mismatch.** If a batch's IL differs, the agent bisects by file, reverts
the offending file, and reports it. Nothing mismatched is merged.

**6. Rule first, sweep second.** The `CLAUDE.md` guardrail rewording lands before the sweep so new
code stops adding history while the sweep runs.

**7. Run as a Workflow script.** The fan-out, verification, and report collection are deterministic,
so a script (pipeline: sweep, verify, collect) is preferred over hand-launched agents. The merge and
final full verification stay with the orchestrator.

**Trim rubric given to every agent (moderate):**
- Remove: dates, change names, spec/design IDs, commit hashes, playtest-round references, "used to /
  no longer / previously / regression" narration.
- Remove: comments that only restate the adjacent code.
- Keep, in the present tense: what a thing does, invariants, and any non-obvious reason that is still
  true. Keep XML doc `<summary>`s that describe a member's contract.
- Correct: a comment found to contradict the code is corrected to match the code, and flagged.
- Relocate: a reusable API/engine fact is listed in the report for `VSAPI-NOTES.md`; agents do not
  edit that file (single writer avoids conflicts).
- Never: change a code token, string literal, or whitespace outside comment lines; add new content.

## Risks / Trade-offs

- [An agent edits code by accident] -> The per-batch and final IL diff catch it; the file is reverted.
- [An agent deletes a comment that held a live constraint] -> Not machine-checkable. Mitigated by the
  keep-the-why rubric, flagged-uncertainty reporting, and spot-checking the highest-risk files
  (`HudScribePins`, `ScribeDialogBase.*`).
- [Rewrites introduce inaccurate new statements] -> Agents may not add content; corrections must be
  flagged; spot-check compares comment claims against the code.
- [Merge conflicts with in-progress OpenSpec changes touching the same files] -> Batches are
  comment-only and merge early; in-progress changes touching a swept file rebase over comment edits.
  Near-complete changes should be closed out first where practical.
- [Value is smaller than the raw line count suggests] -> Accepted; the trial showed ~6% total, with
  the benefit being accuracy. Success is measured by history-marker count, not lines removed.
- [Worktree builds are slower than the incremental main-tree build] -> Acceptable; run once per batch.

## Migration Plan

1. Land the `CLAUDE.md` guardrail change on the branch.
2. Run the Workflow sweep; each batch lands on its own worktree branch.
3. Merge batch branches (disjoint files); run the full build, IL diff against the base commit, and
   Core tests on the combined result.
4. Apply collected `VSAPI-NOTES.md` additions.
5. Rollback: each batch is a separate branch and the sweep is comment-only, so any batch can be
   dropped or reverted independently.

## Open Questions

- Whether to also add a lightweight lint (grep for history markers) to `build/verify.sh` so the
  convention is enforced going forward. Deferrable; can be a follow-up change.
