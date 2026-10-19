# Results

Measured over all tracked `.cs` files under `src/` and `tests/` (comment line = first non-space
characters are `//`, which includes `///` XML docs). Baseline is the commit before any comment edits.

| | Before | After | Change |
|---|---|---|---|
| Total lines | 65274 | 56058 | -14.1% |
| Comment lines | 19250 | 10035 | -47.9% |
| History-marker comment lines | 689 | 28 | -96% |

History markers match: previously, used to, formerly, no longer, regress, reverted, openspec,
design D<n>, section sign, playtest, ISO dates.

Verification: `build/verify-comment-only.sh` against the baseline reports every assembly
(`Scribe.dll`, `Scribe.Core.dll`, `Core.Tests.dll`, `Integration.Tests.dll`) IL-identical; 831 Core
tests pass.

Pass 1 (history stripped, moderate trim) cut comment lines ~4% and history markers ~93%. Pass 2
(restating and over-explaining comments) cut a further ~46% of the remaining comment lines. The
pass-2 reviewers flagged 21 lost-constraint concerns; 16 were restored, 3 were already covered, and
2 malformed XML doc tags introduced by the edits were fixed.

## Decisions recorded

- History-marker lint in `build/verify.sh` (task 5.3): deferred. `verify.sh` feeds the pre-push
  gate, so adding a check there is its own small change; revisit if history creeps back in.
- Four engine facts that pass 1 removed from comments could not be corroborated in source and were
  not added to `VSAPI-NOTES.md`; they remain recoverable from the baseline commit.
