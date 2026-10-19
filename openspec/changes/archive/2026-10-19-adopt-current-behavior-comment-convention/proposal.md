## Why

About 35% of `src/` (roughly 18k of 51.7k lines) is comments, and a large share narrates history
rather than describing the code as it is: dates, change names, spec IDs ("design D6", "§5.4"),
commit hashes, and phrasing like "used to", "no longer", "regression", "playtest feedback". That
history is already recorded in git (600+ commits), the archived OpenSpec changes, `VSAPI-NOTES.md`
and memory, so the comments duplicate it, go stale, and sometimes contradict the code. They make
files longer and harder to read, which works against the goal of readable code enabling features.

A trial on `src/Mod/ScribeReadContent.cs` showed the shape of the win: history references went from
~38 to 0, comments from 309 to 259 lines, code lines unchanged (479), and the compiled IL was
identical before and after. The gain is accuracy and clarity more than raw size.

## What Changes

- Adopt a commenting convention: comments describe **current behavior and still-true constraints** in
  the present tense; they do not narrate how the code got here.
- Apply a **moderate** trim across the codebase: strip history markers, drop comments that merely
  restate the code, correct comments found to be stale; do not invent new content or remove live
  "why" explanations.
- Reword the project `CLAUDE.md` guardrail ("favor clear, well-explained solutions") so it no longer
  reads as "comment heavily", and add the commenting rule there.
- Route durable API/engine facts uncovered during the sweep to `VSAPI-NOTES.md` instead of leaving
  them as code comments; history stays in commit messages and the OpenSpec archive.
- Run the sweep as comment-only changes, in parallel batches, each verified by compiling and
  comparing the compiled IL against its base commit.
- No runtime behavior changes. No new dependencies. `.github/workflows/*.yml` and
  `Directory.Build.props` are not touched.

## Capabilities

### New Capabilities
- `code-comment-convention`: the rule for what source comments may and may not contain, and the
  requirement that a comment-only sweep preserves compiled behavior.

### Modified Capabilities
<!-- None: no existing spec's requirements change. -->

## Impact

- Source: every `.cs` file under `src/Mod/`, `src/Core/`, and `tests/`, comment lines only.
- Docs: `CLAUDE.md` (guardrail wording), `VSAPI-NOTES.md` (a few relocated facts).
- Tooling: a repeatable verification step (build + IL diff) used per batch and on the merged result.
- Risk is concentrated in accidental code edits during rewriting; the IL comparison exists to catch
  exactly that.
