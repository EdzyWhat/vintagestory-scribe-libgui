## Why

The `/simplify` review of `lectern-multiline-edit-input` surfaced two altitude issues that
were out of scope for a cleanup pass but are worth fixing deliberately:

1. **Trim policy leaked into the domain model.** `ScribeDocument` (in `src/Core/`, the
   game-agnostic model) trims task text in two places — `AddTask` and `SetBlockText`'s
   default `trimTask: true`. But normalization is really an editing-layer concern: both live
   `SetBlockText` call sites now pass `trimTask: false`, and the Mod's `NormalizeRowOnCommit`
   already owns commit-time trimming (`TrimEnd()`, keeping intentional leading indent). The
   Core default and the commit-layer policy currently disagree about what "trim" means, and
   the only thing still exercising Core's trim is the test suite — a sign the seam is in the
   wrong layer.

2. **Two divergent wrapped-text-height helpers.** `ScribeBlockRowCell.MeasureWrappedHeight`
   uses the raw engine `GetMultilineTextHeight`, which does not count a trailing newline's
   empty line — the exact quirk `ScribeRowElement.MeasureWrappedTextHeightScaled` was written
   to work around. The raw helper survives at a single call site (the empty-list edit hint)
   where it happens to be harmless, but it invites a future caller to reach for the wrong,
   trailing-newline-blind measure.

## What Changes

- **Make Core trim-agnostic.** Remove the `.Trim()` from `ScribeDocument.AddTask` and drop the
  `trimTask` parameter from `SetBlockText`; Core stores task text verbatim. The one domain
  invariant it keeps is rejecting blank/whitespace-only task text (`IsNullOrWhiteSpace`). All
  whitespace normalization becomes the responsibility of the editing/commit layer
  (`NormalizeRowOnCommit`), which already does it.
- **Update `Core.Tests`** to match: the trim-on-`AddTask`/`SetBlockText` assertions move to
  "stored verbatim" assertions, and the `trimTask`-specific cases are removed. Blank/whitespace
  rejection tests stay.
- **Collapse the duplicate height measure.** Retire `ScribeBlockRowCell.MeasureWrappedHeight`
  and point its single caller (the empty-list edit hint) at the corrected per-segment measure,
  so there is one wrapped-text-height primitive in the codebase.
- No user-facing behavior change is intended beyond *where* trimming happens (commit layer
  only). The already-shipped commit behavior — `TrimEnd()` preserving interior newlines and
  intentional leading indent — is unchanged.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `task-note-document`: adds a requirement that the document model stores task text verbatim
  (rejecting only blank/whitespace-only text) and does NOT trim surrounding whitespace —
  making explicit that whitespace normalization is an editing-layer concern, not a domain-model
  one. This codifies the trim-layering decision as a testable contract of Core.

## Impact

- `src/Core/ScribeDocument.cs` — `AddTask` (drop `.Trim()`), `SetBlockText` (drop `trimTask`
  param and the conditional trim). Public API change: `SetBlockText`'s signature loses its
  optional parameter.
- `src/Mod/GuiDialogScribeLectern.cs` — the two `SetBlockText(..., trimTask: false)` call sites
  drop the now-removed argument; `NormalizeRowOnCommit` is unchanged in behavior. The
  empty-list edit-hint measurement (line ~403) switches from `ScribeBlockRowCell.MeasureWrappedHeight`
  to the corrected measure.
- `src/Mod/ScribeBlockRowCell.cs` — remove `MeasureWrappedHeight`.
- `src/Mod/ScribeRowElement.cs` — the corrected per-segment measure becomes the single shared
  primitive (expose it if the hint site needs it without a full `ScribeBlock`).
- `tests/Core.Tests/ScribeDocumentTests.cs` — trim assertions updated to verbatim-storage
  assertions; `trimTask` cases removed; blank-rejection cases retained.
- No persistence/codec change, no network change, no new dependency. CI (Core-only) still
  covers the Core edits.
