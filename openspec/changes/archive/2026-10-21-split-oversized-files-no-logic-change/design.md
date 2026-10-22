## Context

`build/verify-comment-only.sh` proves an edit left compiled IL unchanged by diffing `ilspycmd -il` output of the
base and the working tree. Moving a type to another file, or splitting a class into partials, can change the order
types and methods appear in the assembly (metadata order follows compilation order), so a plain diff may report
differences even though the code is identical.

## Goals / Non-Goals

**Goals:** relocate code with no semantic change, and prove it.

**Non-Goals:** renames, signature changes, visibility changes, or tidying logic while moving. Do not split
`ScribeDialogBase.*` (already cohesive partials).

## Decisions

- **Order-insensitive IL comparison.** Before the first move, run the script on a no-op split to see whether it
  reports a diff. If it does, extend the script (a flag such as `--ignore-order`) to split each IL dump into
  per-type and per-method blocks, sort the blocks, then diff. Sorting blocks keeps every instruction compared while
  ignoring placement. Alternative: compare type-by-type with `ilspycmd -t`. Rejected as slower and no stronger.
- **One file per moved top-level type; partials for State classes.** File names match the type name; partial files
  use `Type.Concern.cs` like the existing `ScribeDialogBase.*.cs`.
- **One file at a time, commit per file.** Each commit builds and passes the IL check, so a failure points at one
  move.
- **Do the moves before any consolidation change** so later diffs are small and readable.

## Risks / Trade-offs

- Comments that describe a region ("see below") may point at the wrong file after a move; check them.
- `using` directives and file-scoped namespaces must be copied; the build catches omissions.
- IL equality does not cover assets or reflection by name; types keep their names and namespaces, so none changes.
