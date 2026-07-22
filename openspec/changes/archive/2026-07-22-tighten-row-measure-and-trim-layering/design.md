## Context

Two structural issues remain after `lectern-multiline-edit-input` shipped:

**Trim layering.** `src/Core/` is the game-agnostic document model and the load-bearing
invariant is that it never depends on the VS API so it stays unit-testable. Today it also owns a
*presentation* decision — trimming task whitespace — in two spots:
- `AddTask` does `text.Trim()`.
- `SetBlockText(index, text, trimTask = true)` does `trimTask ? text.Trim() : text`.

The `trimTask` flag was added during the 4.6 fix so the live editor could keep a just-typed
trailing newline long enough for the row to grow. Both live call sites in the Mod now pass
`trimTask: false`, and the Mod's `NormalizeRowOnCommit` already does the real normalization on
commit — deliberately `TrimEnd()` only, so a player's intentional leading indent survives. That
leaves Core's default (`Trim()`, both ends) contradicting the commit layer's chosen policy
(`TrimEnd()`), reconciled only by the fact that the default path is no longer taken outside
tests. The seam is in the wrong layer.

**Duplicate height measure.** `ScribeBlockRowCell.MeasureWrappedHeight` wraps the raw engine
`GetMultilineTextHeight`, which does not count a trailing newline's empty line. `lectern-multiline-edit-input`
introduced `ScribeRowElement.MeasureWrappedTextHeightScaled` precisely to correct that quirk and
routed all row-height paths through it. The raw helper now survives at exactly one caller — the
empty-list edit hint (`GuiDialogScribeLectern.cs:~403`), which measures a static `Lang` string that
never has a trailing newline, so it's harmless *today*. But two measurement primitives sitting
side by side invites a future caller to grab the wrong one.

## Goals / Non-Goals

**Goals:**
- Core stores task text verbatim; the only invariant it enforces is rejecting blank/whitespace-only
  task text. All whitespace normalization lives in the editing/commit layer.
- Exactly one wrapped-text-height primitive in the codebase.
- No user-facing behavior change beyond *where* trimming happens. Committed rows still get
  `TrimEnd()` (interior newlines and leading indent preserved), exactly as they do now.

**Non-Goals:**
- Changing the commit-time normalization policy itself (`NormalizeRowOnCommit` stays `TrimEnd()`).
- Touching persistence, the codec, or networking.
- The broader row-affordance work (`restore-row-affordance-columns`) — unrelated arc.

## Decisions

**1. Drop trimming from Core entirely.**
- `AddTask`: remove `.Trim()`; store `text` verbatim. Keep the `IsNullOrWhiteSpace` rejection.
- `SetBlockText`: remove the `trimTask` parameter and the conditional; store `text` verbatim for
  tasks (still rejecting blank/whitespace-only), unchanged behavior for text sections.
- This is a public API change (`SetBlockText` loses its optional parameter), but the only callers
  are in-repo (two Mod sites + tests), and both Mod sites already pass `trimTask: false` — so they
  simply drop the argument and their behavior is identical.
- Rationale: normalization is an editing concern. Core's job is the domain invariant (no blank
  tasks), captured as a spec requirement so it's a tested contract rather than an incidental
  implementation detail.

**2. Editing layer keeps ownership of normalization, unchanged.**
- `NormalizeRowOnCommit` already does `TrimEnd()` on commit and is the sole normalization site.
  It needs no change — it was already passing `trimTask: false` and will keep calling
  `SetBlockText(index, trimmed)` (now with the parameter gone).
- The initial placeholder from `AddTask` (`scribe:scribe-gui-newtask-placeholder`) is a clean Lang
  string with no surrounding whitespace, so dropping `AddTask`'s trim is a no-op for that path.

**3. Single height primitive.**
- Remove `ScribeBlockRowCell.MeasureWrappedHeight`.
- Point the empty-list edit-hint site at the corrected measure. `MeasureWrappedTextHeightScaled`
  is currently `private static` on `ScribeRowElement` and takes raw text + font + scaled width;
  the hint site has text + font + a fixed (unscaled) width and its own `minHeight` floor. Prefer
  the smallest change that yields one primitive: promote the per-segment measure to an
  `internal static` on `ScribeRowElement` taking `(capi, text, font, scaledWidth)` and have the
  hint site call it (scaling its width and applying its own `Math.Max(minHeight, …)` floor at the
  call site, matching what `MeasureWrappedHeight` did). Keep `RowHeightFixed` calling it exactly as
  it does now.

## Risks / Trade-offs

- **Behavior drift risk (low).** The one way this could change visible behavior is if some path
  actually depended on Core trimming. Grep confirms only two `SetBlockText` callers (both already
  `trimTask: false`) and one `AddTask` caller (a whitespace-free Lang placeholder). So the removal
  is inert at runtime; the change is exercised meaningfully only by the updated tests.
- **Public-signature change.** Removing `SetBlockText`'s optional parameter is a breaking API
  change in principle, but the API is internal to this mod and all callers are updated in the same
  change. Acceptable.
- **Hint-site measurement shift.** Switching the edit hint to the per-segment measure changes its
  height math from `GetMultilineTextHeight` to summed per-segment lines. For a single-paragraph,
  no-newline `Lang` string the two agree, so the hint renders identically; the trade is a tiny bit
  more work (one extra split of a short string on a cold path) for one fewer primitive. Worth it.
- **Test churn.** A handful of `Core.Tests` assertions flip from "trimmed" to "verbatim"; the
  blank-rejection tests are unaffected. Mechanical, and the new spec scenarios map directly onto
  the new assertions.
