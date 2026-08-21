---
name: reconcile-playtest
description: Close the back half of the playtest loop after a play session. Runs the deterministic `reconcile.py` command (model-free Python) that reads pending `.playtest-submissions/*.json` reports and, for every item with a terminal verdict, writes the TESTING.md verdict line and flips the matching OpenSpec tasks.md checkbox -- then drives the remaining judgment ~20% in-terminal: presents the triage inbox (verdict:null items + general notes), flags caveat-laden passes for a glance, offers to turn new bug/feature feedback into an openspec-propose change or a new TESTING.md item, and offers to archive changes now at 100%. Use when the user says "reconcile the playtest results", "process the submissions", "close out the testing", "apply the playtest verdicts", "what came back from testing", or otherwise wants submitted playtest reports turned into checked boxes + triaged feedback. This is the complement to `what-to-test` (generate) and `triage-screenshot` (image review).
version: "1.0"
---

# Reconcile playtest results

The playtest loop is **what-to-test** (generate `TESTING.md`) → play + submit (the
`vs-playtest-checklist` web app writes `.playtest-submissions/<ts>.json`) → **this skill**
(close + triage). This skill turns submitted reports into checked boxes and a triage queue.

**The mechanical logic lives entirely in `reconcile.py`, not here.** This skill runs that
command, reads its JSON report, and handles only the judgment ~20% the command deliberately
refuses to guess at. Do not re-implement verdict propagation, box-flipping, or archiving in
the conversation — call the command.

## Steps

1. **Run the reconciler.** From the repo root:
   ```bash
   python3 .claude/skills/reconcile-playtest/reconcile.py --json
   ```
   This applies every terminal-verdict item (deterministically, keyed off
   `fingerprint`/`taskId`), captures judgment-needing feedback to
   `.playtest-submissions/triage-inbox.md`, moves fully-resolved submissions to
   `reviewed/`, and prints a `{applied, readyToArchive, triage, errors, glance,
   movedToReviewed}` report.
   - Prefer a **`--dry-run` first** if the user wants to preview, or if TESTING.md /
     tasks.md have uncommitted hand edits you're unsure about — it prints the same report
     with zero writes.
   - Pass `--date YYYY-MM-DD` only to override the stamp (defaults to today).

2. **Report the mechanical result briefly.** State how many verdicts were applied
   (pass/fail), how many boxes flipped, and how many submissions moved to `reviewed/`.
   Don't paste the whole JSON — summarize.

3. **Surface the `errors`** (if any) first — these are items the command refused to guess:
   an unresolvable `fingerprint` (not in TESTING.md) or `taskId` (task renumbered / change
   missing), or a `fail` that conflicts with an existing Confirmed item. Each needs a human
   decision: fix the key and re-run, or explain why it's stale. Never hand-edit a box to
   paper over one — fix the pointer and let the command reconcile.

4. **Glance at caveat-flagged passes** (`glance` list). These auto-applied (the player said
   "pass") but their note carries a hedge ("but", "doesn't", "except", "todo", …). Show each
   to the user; if the caveat reveals a real gap, treat it like triage below (propose / new
   item) even though the box is checked.

5. **Drain the triage inbox** (`.playtest-submissions/triage-inbox.md` — the `triage` list).
   Each entry is a `verdict:null` item or a submission's general notes. For each, help the
   user classify:
   - **New bug or feature** → offer to run **openspec-propose** to spin a change (the user's
     `AskUserQuestion`-driven design flow), or add a **new TESTING.md item** via the
     `what-to-test` conventions (mint a code with `next-id.py`; never improvise one).
   - **A retest of existing work** → add/adjust the TESTING.md verdict as
     **Still broken**/**Backlogged**/**Obsolete** per the `what-to-test` lifecycle.
   - **Handled / not actionable** → say why, then **delete the drained entry** (and its
     `<!-- key: … -->` line) from `triage-inbox.md` so it doesn't linger.

6. **Offer to archive** the `readyToArchive` changes (those whose tasks.md is now 100%
   checked). With user go-ahead, either re-run with `--archive` (runs
   `openspec archive <name> -y` for each, serialized) or archive them yourself one at a time,
   watching for a spec-delta collision on each. Archiving is never automatic.

## Guardrails

- **This skill holds no mechanical reconciliation logic** — `reconcile.py` is the single
  source of it (unit-tested on fixtures + a real-`reviewed/` regression). If a mechanical
  behavior is wrong, fix the command and its tests, not the conversation.
- **Never fuzzy-match or hand-flip** to route around an error the command reported. Fix the
  `fingerprint`/`taskId`, then re-run — the command is idempotent and re-runnable.
- **`pass` is the only verdict that checks a box.** `fail` records **Still broken** and
  leaves the box; `null` is triage-only. The command never unchecks or downgrades an item
  that already carries a verdict.
- Screenshots and the `triage-screenshot` skill are unchanged; this skill only references
  screenshot paths the report carries.
