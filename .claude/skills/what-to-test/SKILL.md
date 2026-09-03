---
name: what-to-test
description: Surface a short, concrete list of in-game conditions to test in Vintage Story, pulled from the remaining manual-test tasks in any in-progress OpenSpec change; persist/regenerate that list as TESTING.md at the repo root so it survives across sessions, with agent-recorded verdicts (or a bare hand-checked box, trusted as an implicit Confirmed) as the source of truth; and use freshly captured screenshots as evidence against it. Items carry one of four lifecycle verdicts -- Confirmed, Still broken, Backlogged, Obsolete -- and verdict-carrying (or checked) items are retained across regeneration (they don't vanish when their task is done or removed). Use when the user asks "what should I test", "what should I check in-game", "give me a testing checklist", "what's left to verify", "update the testing checklist", wants to review/process submitted playtest reports, wants an item marked completed/backlogged/obsolete, or mentions a "screenshot"/"screen"/"pic" while discussing testing, a bug, or a feature (a new capture from the game's screenshot folder to triage and check against the checklist).
version: "1.6"
---

# What should I test?

Answers "what should I test" by turning OpenSpec's own remaining manual-test tasks into
a short, concrete checklist of things to actually go do in-game — not a dump of raw task
text, and not a guess if OpenSpec has nothing queued.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo
registered on this machine) or the work lives in one, pass `--store <id>` on `list` and
`status`. Without a store, these act on the nearest local `openspec/` root.

## Steps

1. **Find in-progress changes.**
   ```bash
   openspec list --json
   ```
   Collect entries with `status: "in-progress"` (i.e. `completedTasks < totalTasks`).

   - If the user named a specific change, use only that one (skip the filter above).
   - If the current conversation has clearly been focused on one particular change,
     prioritize that one — lead with its items — but still check the others; don't
     silently drop unrelated in-progress work the user might also want to hear about.
   - If there are none, skip straight to the last step below (nothing to pull from OpenSpec).

2. **Resolve each candidate change's `tasks.md`.** The canonical way is
   `openspec status --change "<name>" --json` → `artifactPaths.tasks.existingOutputPaths[0]`. If the
   `tasks` artifact doesn't exist yet (change still blocked earlier in planning), skip that change —
   nothing to test yet, not an error.

   **Speed note (this skill is run often and the per-change `openspec status` call is ~0.6s each,
   which adds up across many in-progress changes):** the tasks file is conventionally
   `openspec/changes/<name>/tasks.md`. You MAY read that path directly and skip the per-change
   `status` call when it exists — falling back to `openspec status` only if the direct path is
   missing (a non-standard layout). Prefer reading the several `tasks.md` files in one batched set of
   tool calls over serial `status` invocations. If the conversation is already focused on ONE change
   (the common case right after implementing it), resolve just that one and don't fan out over all
   in-progress changes at all — mention the others exist but only expand them if asked.

3. **Read each resolved `tasks.md` and extract unchecked items (`- [ ]`)**, including
   their full continuation text (a task's description often wraps onto indented
   following lines — read the whole block, not just the first line).

4. **Filter to items that describe something to verify in-game.** Keep a task if it:
   - Explicitly says "manually test", "in-game", "playtest", or similar, OR
   - Is a decision that can only be made by looking at the live result (e.g. "decide
     based on how it actually looks in-game").

   Drop tasks that are pure code/investigation/test-suite work with no in-game
   verification step (e.g. "add Core.Tests coverage for X", "investigate Y in source") —
   those aren't things to go test in the game client, they're implementation work still
   pending. If literally every remaining task in a change is like this, that change
   contributes nothing to the list (not an error — just has no in-game-testable items
   right now).

5. **Distill each kept task into ONE crisp, concrete, actionable line** — imperative,
   specific steps plus what outcome to check. Do not paste the raw multi-sentence task
   prose verbatim; compress it to what someone would actually do with the mouse/keyboard
   in-game and what they're looking for. Tag each line with its source
   `(change-name task-id)` so the user can cross-reference `tasks.md` directly.

   **Lead every line with a bolded, up-to-four-word summary** (`**Test slider sync.**`,
   `**Check hover focus.**`) before the fuller description — a quick "what to actually
   do" flag for scanning a long list fast. Keep it imperative and concrete (name the
   thing being poked, not "verify that X works correctly"). The playtest checklist app
   renders this lead-in bolded; without it an item still works, just reads slower.

   Example transformation:
   - Raw task: "Manually test in-game: confirm hover-icon show/hide does not reset focus
     or caret position while typing (the exact regression this render-time approach is
     meant to avoid — verify it actually holds, don't just assume the mechanism works)."
   - Distilled: "**Check hover focus.** Start typing in a note's text area, then move
     the mouse over a different row's delete/pin icon and back — confirm your caret
     position and in-progress text are undisturbed. *(skeuomorphic-lectern-gui 6.6)*"

6. **Dedupe across changes.** An older change's pending task can be superseded by a
   newer change already covering the same ground (e.g. an old "no scrollbar exists yet"
   task vs. a newer change's own scroll-testing task). When two kept items clearly test
   the same underlying thing, keep only the one from the more recently modified change
   and drop the other — don't make the user verify the same behavior twice under two
   different labels.

7. **Cap the list at roughly 5-8 items.** If more candidates exist than that, prioritize
   in this order, and say how many were left out (offer to list the rest if asked):
   1. Tasks already suspected broken or blocking a retest (e.g. the task directly tied
      to a bug fix that's awaiting live confirmation).
   2. Lower task-group number first (earlier, more foundational work).
   3. Tasks whose change the conversation has been actively focused on.

8. **Present the list** as a compact bulleted checklist. If items came from more than
   one change, group by change name with a small header per group. Keep the framing
   terse — this is a checklist to act on, not a report to read.

9. **If no in-progress OpenSpec change has any in-game-testable item** (no in-progress
   changes at all, all blocked on earlier planning, or every remaining task is
   non-manual work), say so plainly — don't fabricate a list to fill the gap — and ask
   the user directly what they'd like to test or work on instead. Keep this open-ended
   (plain question, not a forced multiple-choice) since at that point there's no
   OpenSpec signal to build options from.

## Persisting the checklist (`TESTING.md`)

The in-chat list above doesn't survive a session boundary — the user can't scroll back
through a future session to find it, and there's no way to mark an item done without
touching `tasks.md` (which is OpenSpec's file, not a scratch checklist). `TESTING.md` at
the repo root solves both: a standalone, git-tracked, regenerable file that mirrors the
in-game-testable items this skill surfaces, with real checkable boxes.

**Core rule: an item's status comes from its verdict annotation when one exists.
Prefer writing a real annotation whenever you have first-hand evidence** (it's the
only way to record `Still broken`/`Backlogged`/`Obsolete`, and it's the richer record
for a `Confirmed` too — cite what you saw). **A checked `[x]` with no annotation is
trusted as an implicit Confirmed** (2026-08-30 policy change — the user decided a
hand-checked box is an acceptable confirmation on its own, no annotation required).
Regeneration retains it like any other Confirmed item; only a genuinely *unchecked*
`[ ]` with no annotation is untested and gets re-derived fresh from `tasks.md`.

### Item lifecycle: four verdict states

An item's status is the bold lead word of the verdict annotation directly under it. The
playtest checklist app derives which tab an item shows under purely from this word, so
the vocabulary is fixed:

| Annotation | Meaning | Box | App tab |
|---|---|---|---|
| `- **Confirmed <date>** …` | Verified working from first-hand evidence | `[x]` | Completed |
| `- **Still broken <date>:** …` | Evidence shows it's wrong; needs a retest after a fix | `[ ]` | To Test (badged) |
| `- **Backlogged <date>** …` | Can't be tested yet (blocked on a feature/dependency/environment) | `[ ]` | Backlog |
| `- **Obsolete <date>** …` | The thing it tested changed or was removed; the test no longer applies | `[ ]` | Obsolete |

`Confirmed` is the only one that checks the box. `Backlogged` and `Obsolete` are terminal
"parked" states — the item is kept as a record but off the active To Test list.
**Obsolete is how a no-longer-relevant test is retired: annotate it, don't delete it** —
the retention rule under "Generating / regenerating" below then keeps it in the file
under its own tab instead of dropping it.

A bare checked `[x]` with no annotation underneath is the implicit-Confirmed case above —
treat it exactly like an annotated `Confirmed` everywhere in this skill (retention,
retirement eligibility, tab placement).

### File format

```markdown
# Testing checklist

Regenerated by the `what-to-test` skill from OpenSpec's remaining tasks. A verdict line
under a checked item is the richer record when one exists -- but a hand-checked box with
no verdict line is ALSO trusted as Confirmed (only re-check an item if you've actually
verified it yourself).

Each item's verdict line puts it in one of four states (its bold lead word): **Confirmed**
(done, box checked), **Still broken** (needs a retest), **Backlogged** (deferred), or
**Obsolete** (the feature changed; test no longer applies). The playtest checklist app
shows these as tabs. Items with any verdict, or a bare checked box, are kept across
regeneration; only untested (unchecked, unannotated) items are re-derived fresh from
`tasks.md`.

## skeuomorphic-lectern-gui

- [ ] `7d808ca9` **Overflow and scroll.** Add enough rows to overflow the visible dialog
      height; confirm every row is reachable by scrolling, in both read and editor view.
      *(3.5)*
- [x] `805e78a7` **Check hover focus.** Start typing in a note's text area, then hover a
      different row's delete/pin icon and back -- confirm caret/typing is undisturbed.
      *(6.6)*
      - **Confirmed 2026-07-19** via screenshots/debug/2026-07-19_.._focus-check.png:
        caret position held after hovering the delete icon on an adjacent row.

## add-lectern-block

- [ ] `c127b9ad` **Test multiplayer sync.** Two clients, one lectern each -- confirm edits are
      session-independent and visible live in read view. *(7.5)*
```

Each item carries a leading code (`` `7d808ca9` ``) that must be exactly 8 lowercase hex
chars (`^[0-9a-f]{8}$`). It is not meant to be human-meaningful — it exists only so the app
can key items uniquely and so a submission can reference one across machines.

**Generate a NEW item's code with the counter tool — do NOT hash, and never improvise a
readable/mnemonic code:**

```bash
python3 .claude/skills/what-to-test/next-id.py 3   # prints the next 3 codes, advances the counter
python3 .claude/skills/what-to-test/next-id.py --peek   # next code without advancing
```

The tool hands out a persistent incrementing counter formatted as 8 hex digits
(`00000001`, `00000002`, … `0000000a`, …) and skips any code already in `TESTING.md`. Call
it once per regeneration for however many *new* items you're adding, in list order.

Why a counter instead of the old `sha256(task-id + text)[:8]`: the hash was slow to
reproduce by hand and impossible to verify without re-deriving the exact task-id + text
normalization (which drifts), so it was a recurring time-sink. Sequential codes satisfy the
same regex, are trivially unique, and never need reproducing. **Existing sha256 codes
already in `TESTING.md` stay as-is** — both forms match the parser; only new items use the
counter. A retained item keeps its old code forever (its verdict is carried verbatim); you
only mint a new code for a genuinely new item.

The 8-hex-char rule is not stylistic: the playtest app parses items with a strict regex
(`- \[[ x]\] `[0-9a-f]{8}` …`), so a code that isn't 8 hex chars **fails to parse as an
item** — the app silently treats that whole line as body text of the *previous* valid item
and vacuums its `- **verdict**` sub-bullets into that neighbor's timeline. The malformed
item vanishes from every tab and the innocent neighbor above it gets mis-bucketed. Always
take the code from the tool.

**One `tasks.md` task may map to SEVERAL `TESTING.md` items.** A compound task (e.g. one
line covering delete + pin + reorder + checkbox) can be split into several granular retest
items so each is confirmed independently in the app. Each split item gets its OWN computed
fingerprint (hash the task-id plus that item's OWN distinct text, so the codes differ) and
carries the same source task-id tag `(change-name task-id)`. Regeneration keys on
fingerprint, not task-id, so multiple items sharing a task-id is fine.

### Generating / regenerating `TESTING.md`

Regeneration is a **merge, not a rebuild.** The file is the union of two sets, keyed by
fingerprint: (a) the fresh active items derived from `tasks.md`, and (b) every retained
item already in the file that carries a verdict annotation. This is what lets a completed
or obsolete item stay in the file (and in its tab) even after its underlying task is
checked off or removed from `tasks.md` — retention is by "has a verdict on file," not by
"still an open task."

1. Run this skill's normal steps (1-7 above) to produce the current in-game-testable
   item list, computing each item's fingerprint as described. Call this the **fresh
   active set**.
2. If `TESTING.md` already exists, read it and split its existing items:
   - **Retained** = any item with a verdict annotation whose bold lead word is
     `Confirmed`, `Still broken`, `Backlogged`, or `Obsolete`, OR a bare checked `[x]`
     with no annotation (implicit `Confirmed` — see Core rule above). Carry these forward
     **verbatim** — the box state, the annotation (if any), and the item text exactly as
     written — regardless of whether the fingerprint still appears in the fresh active set
     or in `tasks.md` at all. (An `Obsolete` item's task is often gone; that's expected. A
     `Confirmed` item's `tasks.md` box is often now checked, so it's absent from the fresh
     active set; also expected. Both are still kept.)
   - **Not retained** = an unchecked `[ ]` item with no annotation (genuinely untested).
     These are dropped from the old file and re-derived only if they reappear in the fresh
     active set.
2b. **Validate + repair item codes before merging.** For every item read from the existing
   file, check its code matches `^[0-9a-f]{8}$`. If one doesn't (a hand-authored mnemonic,
   a wrong length, uppercase, etc.), it's malformed — the app silently drops it and misfiles
   its neighbor (see the code rule above). Repair it: mint a fresh code with
   `python3 .claude/skills/what-to-test/next-id.py` and replace the malformed one, carrying
   the item's verdict lines forward verbatim (a code is just an identity, so re-coding a
   retained item never invalidates its verdict). Flag each repair in the write-back summary.
3. Merge. Codes are stable identities now (a counter value assigned once), NOT derived from
   text — so match a retained item to a fresh active item by the **task-id tag + item text**
   they describe, and keep the retained item's existing code. Then:
   - An item present in **both** retained and fresh active → keep the retained version (its
     code + verdict win; don't overwrite a recorded result with a blank fresh item).
   - An item **only in retained** → keep it as-is (completed/backlog/obsolete/broken history
     that must survive), code unchanged.
   - An item **only in fresh active** (genuinely new) → mint a new code with `next-id.py` and
     write it unchecked, no annotation.
   - A retained item whose **task text was edited** → keep its existing code and verdict only
     if the change is cosmetic and the test still means the same thing; if the edit changes
     what's being tested, treat it as a new item (new code, no annotation) and let the old
     one age out. Use judgment — the code no longer tells you automatically, since it's not
     text-derived.
   - Never invent or infer an annotation; if there isn't one on file for an item, it's
     unconfirmed.
4. Write the full file back (grouped by change, same order/priority as the in-chat list;
   retained items that no longer map to a current change keep their last group heading).
   Mention to the user that `TESTING.md` was written/updated, and if any items were
   retained purely on their verdict (completed/backlog/obsolete no longer in `tasks.md`),
   it's worth a one-line note so they know nothing was silently dropped.

### Retiring an archived change's history (keep `TESTING.md` lean)

The retention rule above keeps every verdict-carrying item forever, which is correct for LIVE
changes but lets `TESTING.md` bloat with the full verdict-chains of changes that are long done —
each regeneration then re-reads and re-merges hundreds of dead lines, which is the main thing that
makes this skill slow. So when a change is **archived** (its dir has moved to
`openspec/changes/archive/`), its group graduates out of the active file:

- **Trigger:** a `TESTING.md` group whose change no longer exists under `openspec/changes/<name>/`
  (only under `archive/`) AND whose items are all in terminal states (`Confirmed`, `Obsolete`, or a
  bare checked `[x]` with no annotation — nothing still `Still broken` awaiting a retest, and no
  `Backlogged` item that could still become testable).
- **Where it goes:** append the whole group, verbatim (heading + items + verdict lines), to
  `playtest-history/TESTING-archive.md` at the repo root (create it if missing, with a one-line
  header explaining it's retired history the app does not read). This sits alongside the app's
  existing `playtest-history/` convention (screenshots + `HISTORY.md` from `promote_screenshot.py`)
  and is deliberately OUTSIDE the app's live `TESTING.md` read path.
- **Then remove that group from `TESTING.md`.** The verdicts also still live in the archived
  `tasks.md`, so nothing is lost — this is purely moving dead history out of the hot file.
- **Do NOT retire** a group that still has a `Still broken` (needs retest) or `Backlogged` item, or
  whose change is still in-progress — those are live. A single lingering broken item keeps the whole
  group in `TESTING.md`.
- **Tell the user** which groups were retired and to where (one line), so the shrink is transparent.

This is a merge-time housekeeping step: do it as part of a regeneration when you notice retired-change
groups present, not as a separate destructive pass.

### Recording a verdict

When the user reports back a test result (in this session or a future one — this is
exactly the scrollback problem this file solves), and you have first-hand evidence for
it (you watched them describe the live behavior, or you read a screenshot per the
section below), record it as one of the four verdict states. Pick the state from the
evidence and your judgment, **not** from what a submission's pass/fail button claimed —
a submission is a claim to be reviewed, not an authoritative result.

1. Write the matching verdict line immediately under the item, in your own words:
   - **Confirmed** — you have evidence it works. Check the box and add `**Confirmed
     <YYYY-MM-DD>** <how, and what was actually observed>`.
   - **Still broken** — evidence shows it's wrong. Leave the box unchecked; add `**Still
     broken <date>:** <what's wrong and where>`. It stays on the To Test list (badged) for
     a retest after the fix.
   - **Backlogged** — it can't be tested yet (blocked on a feature landing, a dependency,
     an environment you don't have). Leave unchecked; add `**Backlogged <date>** <what
     it's waiting on>`.
   - **Obsolete** — the thing it tested changed or was removed, so the test no longer
     applies. Leave unchecked; add `**Obsolete <date>** <what changed>`. Prefer this over
     deleting the item — regeneration keeps verdict-carrying items, so it lands in the
     Obsolete tab as history rather than vanishing.
   - If you genuinely can't resolve it either way, leave it untested (no annotation) and
     say out loud what additional angle/state would settle it — don't guess a verdict.
2. Only write an annotation from your own observation in the current turn — never because
   the user told you to just check a box, and never by copying an annotation forward from
   a *different* fingerprint than the one currently on file (that would be confirming
   stale text, not the current task).
3. If a Confirmed verdict is the item's last remaining confirmation and the user wants it
   reflected upstream, offer to help them check off the corresponding `tasks.md` box too
   (via the normal `/opsx:apply` flow) — `TESTING.md` and `tasks.md` are two different
   files with two different authorities; confirming one doesn't silently edit the other.

### Reviewing a submission queue (close the loop)

Playtest reports submitted from the app land as JSON in `.playtest-submissions/` (next to
`TESTING.md`). The app shows the user a **"N submissions awaiting review"** banner that
counts these loose files — it's the user's backstop against a report you forgot to
process.

**Sweep the WHOLE loose queue, not just the report you came for.** The recurring failure is
processing only the submission tied to the change you just tested and leaving older loose files
behind, so the banner never reaches zero and stale reports pile up (this has happened repeatedly).
So the FIRST thing to do whenever you touch submissions — including right after recording verdicts
for a change you just implemented — is enumerate every loose `.json` at the top level of
`.playtest-submissions/` (NOT `reviewed/`) and account for each one before you finish the turn:

```bash
ls .playtest-submissions/*.json 2>/dev/null   # every file here keeps the banner lit
```

- The report(s) for the change you just tested → record fresh verdicts from the evidence (below),
  then move to `reviewed/`.
- Older reports whose items **already carry verdicts** in `TESTING.md` (or the user says are already
  assessed) → they need no new verdict; just move them to `reviewed/` so the banner clears. Don't
  re-litigate a result that's already on file.
- Any older report with an **unresolved** touched item → resolve it per the rules below (or say why
  you're leaving it), same as a fresh one.

The turn isn't done until `ls .playtest-submissions/*.json` is empty (every loose report moved) or
you've explicitly told the user which ones you're leaving and why. When you review submissions:

1. Read each report (and any screenshots it references under
   `.playtest-submissions/screenshots/`) and, **for every item the report touched**,
   record a verdict per "Recording a verdict" above — or explicitly leave it untested with
   a stated reason. There is no "deal with it later" state for a touched item: it ends
   Confirmed, Still broken, Backlogged, Obsolete, or untested-with-reason.
2. **Only once every item in that report is resolved**, move the report out of the queue
   so the banner clears:
   ```bash
   mkdir -p .playtest-submissions/reviewed
   mv .playtest-submissions/<timestamp>.json .playtest-submissions/reviewed/
   ```
   Don't move a report to `reviewed/` while any item it touched is still unresolved —
   that count is exactly what tells the user a report still needs attention. Moving it
   prematurely hides work that isn't done.

This is the discipline that answers "make sure items get moved to completed": a report
isn't reviewed until its items sit in real buckets, and the banner stays lit until it is.
The full project-agnostic statement of this contract is the playtest tool's own
`REVIEW.md` (in the `vs-playtest-checklist/` repo) — this section is scribe's
implementation of it.

## Screenshots as test evidence

When the user mentions "screenshot(s)", "screen(s)", or "pic(s)" in this context, they
mean images the game itself just auto-captured (fn+F12) into
`~/Pictures/Vintagestory/`, timestamp-named — not a general image reference. Treat that
mention as a trigger to pull the evidence in and use it, not just acknowledge it:

1. **Triage first.** Use the `triage-screenshot` skill (same repo,
   `.claude/skills/triage-screenshot/`) to move the new file(s) out of the game's flat
   source folder into this project's own `screenshots/debug/` (or `screenshots/progress/`
   for a clearly progress/demo shot) with a context-slugged filename. Don't duplicate
   that skill's move/rename logic here — invoke it and use its result.

2. **Read the triaged image(s)** and connect them back to whichever checklist item (from
   this skill's own output, or `TESTING.md` if it exists) they're evidence for. Match by
   what the screenshot actually shows, not by assuming it matches whatever was mentioned
   last — e.g. if six items were just handed to the user and the screenshot shows a
   title-bar overlap, that's evidence for a rendering/layout item, not for an unrelated
   persistence check.

3. **Report a verdict against that item**, not just a description of the picture:
   confirms it passes, confirms it's still broken (say what's visibly wrong and where),
   or is inconclusive (say what additional angle/state would resolve it). If the shot
   reveals a NEW problem unrelated to any current checklist item, say so plainly rather
   than forcing it to fit one of the existing items.

4. **If several screenshots arrive together** (the user often captures more than one in
   a row while testing), triage all of them, but read them as a sequence when they
   plausibly document one interaction (e.g. before/after a scroll) rather than grading
   each in isolation.

5. **If `TESTING.md` exists**, record the verdict there per "Recording a verdict"
   above — a screenshot that clearly shows correct behavior is exactly the kind of
   first-hand evidence that annotation is for; cite the triaged screenshot's path in the
   verdict line. A screenshot showing a failure gets the matching "Still broken" note
   instead, box left unchecked.

## Notes

- This skill surfaces the list and (optionally) persists it to `TESTING.md`. It never
  checks a `tasks.md` box itself. Propagating verdicts upstream (TESTING.md verdict lines
  + `tasks.md` checkbox flips) is the job of the **`reconcile-playtest`** skill / its
  `reconcile.py` command, which runs *after* a play session over the submitted reports.
  The full loop is: **what-to-test** (generate this list) → play + submit via the
  `vs-playtest-checklist` app → **reconcile-playtest** (close boxes + triage feedback).
  `triage-screenshot` remains the image-review step within that loop.
- `TESTING.md` and `tasks.md` have different authorities: `tasks.md` is OpenSpec's own
  planning artifact (edited via `/opsx:apply`/`/opsx:update`); `TESTING.md` is this
  skill's own regenerable, git-tracked scratch checklist. Don't conflate them, and don't
  let the user's request to "check something off" default to editing `tasks.md` —
  clarify which file they mean if it's ambiguous.
- Don't invent test conditions that aren't grounded in an actual pending task unless the
  user has explicitly said OpenSpec has nothing relevant and asked for ideas anyway —
  the whole point is pulling from the real remaining work, not guessing.
- Prefer writing a verdict annotation whenever you have first-hand evidence for it (a
  described live result, a read screenshot), and never write one you didn't personally
  derive from evidence in the current turn. A hand-checked box with no annotation is
  ALSO trusted as an implicit Confirmed (2026-08-30 policy change) — so don't strip or
  revert a bare `[x]` you find on regeneration, but an annotation is still the richer,
  preferred record when you have the evidence to write one.
