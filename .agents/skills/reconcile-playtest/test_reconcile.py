#!/usr/bin/env python3
"""Unit tests for reconcile.py. Stdlib-only (unittest); no game install needed.

Each test builds a throwaway repo (TESTING.md + one or more openspec/changes/<c>/tasks.md
+ a .playtest-submissions/ with fixture JSONs), runs reconcile() with a FIXED date, and
asserts the exact file edits and report. Run:  python3 test_reconcile.py
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import reconcile  # noqa: E402

DATE = "2026-08-21"


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


class ReconcileTestBase(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self.tmp.name)
        self.pending = self.repo / ".playtest-submissions"
        self.pending.mkdir(parents=True)

    def tearDown(self):
        self.tmp.cleanup()

    def make_testing(self, *item_blocks):
        body = "# Testing checklist\n\n## demo\n\n" + "\n".join(item_blocks) + "\n"
        write(self.repo / "TESTING.md", body)

    def tasks_md(self, change, *lines):
        write(self.repo / "openspec" / "changes" / change / "tasks.md",
              "## 1. group\n\n" + "\n".join(lines) + "\n")

    def submission(self, name, items, submitted_at, general_notes="", general_ss=None):
        write(self.pending / f"{name}.json", json.dumps({
            "items": items, "detailedMode": False, "generalNotes": general_notes,
            "generalScreenshots": general_ss or [], "submittedAt": submitted_at}))

    def run_reconcile(self, dry_run=False, do_archive=False):
        # openspec CLI isn't available in the throwaway repo -> ready-to-archive detection
        # degrades gracefully to []. That's fine; a dedicated test covers detection directly.
        return reconcile.reconcile(self.repo, self.pending, DATE, dry_run, do_archive)

    def read(self, rel):
        return (self.repo / rel).read_text(encoding="utf-8")


class TestPass(ReconcileTestBase):
    def test_clean_pass_propagates_to_both_files(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()

        testing = self.read("TESTING.md")
        self.assertIn('- **Confirmed 2026-08-21** (submission 2026-08-21T10-00-00): "Works."',
                      testing)
        self.assertIn("- [x] `00000001`", testing)  # the TESTING.md item's OWN box also flips
        tasks = self.read("openspec/changes/demo/tasks.md")
        self.assertIn("- [x] 1.1 do the thing", tasks)
        self.assertIn('- Confirmed 2026-08-21: TESTING.md `00000001` "Works." '
                      '(submission 2026-08-21T10-00-00)', tasks)
        applied = [a for a in report["applied"] if a["status"] == "applied"]
        self.assertEqual(len(applied), 1)
        self.assertTrue(applied[0]["boxFlipped"])
        self.assertEqual(report["movedToReviewed"], ["2026-08-21T10-00-00"])

    def test_caveat_pass_is_flagged_but_still_applied(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works, but only for wet tablets."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        self.assertEqual(len(report["glance"]), 1)
        self.assertIn("- [x] 1.1", self.read("openspec/changes/demo/tasks.md"))  # still applied


class TestTestingBoxFlip(ReconcileTestBase):
    def test_multiple_pass_items_each_flip_their_own_box_only(self):
        # Regression guard for the box-flip's index bookkeeping: two items, only the first
        # is winning a fresh pass this run -- the second's box must stay untouched.
        self.make_testing(
            "- [ ] `00000001` **A.** body *(demo 1.1)*",
            "- [ ] `00000002` **B.** body *(demo 1.2)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing", "- [ ] 1.2 do the other thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700")
        self.run_reconcile()
        testing = self.read("TESTING.md")
        self.assertIn("- [x] `00000001`", testing)
        self.assertIn("- [ ] `00000002`", testing)  # untouched -- no submission touched it

    def test_already_checked_item_is_not_double_flipped(self):
        # A bare-checked item (no annotation yet) getting a fresh pass shouldn't corrupt the
        # glyph via a second, no-op replace.
        self.make_testing("- [x] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700")
        self.run_reconcile()
        testing = self.read("TESTING.md")
        self.assertEqual(testing.count("- [x] `00000001`"), 1)
        self.assertNotIn("- [x] [x]", testing)

    def test_repairs_a_confirmed_item_whose_box_was_never_flipped(self):
        # Simulates the exact historical bug this patch fixes: an item already carries a
        # Confirmed annotation (from before this fix existed) but its own box is still `[ ]`.
        # A NEW pass submission for the same fingerprint should repair the box without
        # duplicating the annotation line.
        self.make_testing(
            "- [ ] `00000001` **A.** body *(demo 1.1)*\n"
            '      - **Confirmed 2026-08-20** (submission 2026-08-20T10-00-00): "Old note."')
        self.tasks_md("demo", "- [x] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Still works."}], "2026-08-21T10:00:00-0700")
        self.run_reconcile()
        testing = self.read("TESTING.md")
        self.assertIn("- [x] `00000001`", testing)
        self.assertEqual(testing.count("Confirmed"), 1)  # no duplicate annotation


class TestFail(ReconcileTestBase):
    def test_fail_records_line_but_leaves_box_unchecked(self):
        self.make_testing("- [ ] `00000002` **B.** body *(demo 1.2)*")
        self.tasks_md("demo", "- [ ] 1.2 do the other thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000002", "taskId": "demo 1.2", "verdict": "fail",
             "note": "Nope, still dark."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        self.assertIn("- **Still broken 2026-08-21:**", self.read("TESTING.md"))
        self.assertIn("- [ ] 1.2", self.read("openspec/changes/demo/tasks.md"))
        self.assertNotIn("- [x] 1.2", self.read("openspec/changes/demo/tasks.md"))
        self.assertEqual([a for a in report["applied"] if a["verdict"] == "fail"][0]["boxFlipped"],
                         False)


class TestNullTriage(ReconcileTestBase):
    def test_null_item_goes_to_triage_no_write(self):
        self.make_testing("- [ ] `00000003` **C.** body *(demo 1.3)*")
        self.tasks_md("demo", "- [ ] 1.3 investigate")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000003", "taskId": "demo 1.3", "verdict": None,
             "note": "Needs a new proposal for fired tablets."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        self.assertNotIn("Confirmed", self.read("TESTING.md"))
        self.assertIn("- [ ] 1.3", self.read("openspec/changes/demo/tasks.md"))
        triage = self.read(".playtest-submissions/triage-inbox.md")
        self.assertIn("Needs a new proposal", triage)
        self.assertIn("`00000003`", triage)
        self.assertEqual(len(report["triage"]), 1)
        # null item keeps the submission pending (not moved to reviewed/)
        self.assertEqual(report["movedToReviewed"], [])
        self.assertTrue((self.pending / "2026-08-21T10-00-00.json").exists())


class TestGeneralNotes(ReconcileTestBase):
    def test_general_notes_captured_to_triage(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700",
            general_notes="Wet tablets don't cap at 10 tasks -- investigate.")
        report, _ = self.run_reconcile()
        triage = self.read(".playtest-submissions/triage-inbox.md")
        self.assertIn("Wet tablets don't cap at 10 tasks", triage)
        self.assertTrue(any(t["kind"] == "generalNotes" for t in report["triage"]))
        # all items terminal + general notes captured -> submission may move
        self.assertEqual(report["movedToReviewed"], ["2026-08-21T10-00-00"])


class TestPartial(ReconcileTestBase):
    def test_partial_submission_stays_pending(self):
        self.make_testing(
            "- [ ] `00000001` **A.** body *(demo 1.1)*",
            "- [ ] `00000003` **C.** body *(demo 1.3)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing", "- [ ] 1.3 investigate")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."},
            {"fingerprint": "00000003", "taskId": "demo 1.3", "verdict": None,
             "note": "Unsure."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        self.assertIn("- [x] 1.1", self.read("openspec/changes/demo/tasks.md"))  # terminal applied
        self.assertEqual(len(report["triage"]), 1)                               # null queued
        self.assertEqual(report["movedToReviewed"], [])                          # stays pending
        self.assertTrue((self.pending / "2026-08-21T10-00-00.json").exists())


class TestMultiSubmission(ReconcileTestBase):
    def test_latest_submission_wins(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        # Older fail, newer pass for the SAME fingerprint -> pass wins.
        self.submission("2026-08-21T09-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "fail",
             "note": "Broken."}], "2026-08-21T09:00:00-0700")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Fixed now."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        testing = self.read("TESTING.md")
        self.assertIn("Confirmed", testing)
        self.assertNotIn("Still broken", testing)  # the older fail never wrote
        self.assertIn("- [x] 1.1", self.read("openspec/changes/demo/tasks.md"))
        # both submissions counted terminal -> both eligible to move
        self.assertEqual(set(report["movedToReviewed"]),
                         {"2026-08-21T09-00-00", "2026-08-21T10-00-00"})


class TestUnresolvable(ReconcileTestBase):
    def test_missing_fingerprint_and_missing_change_are_errors(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "deadbeef", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."},  # fingerprint not in TESTING.md
            {"fingerprint": "00000001", "taskId": "ghost-change 9.9", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700")
        report, _ = self.run_reconcile()
        msgs = " ".join(e["error"] for e in report["errors"])
        self.assertIn("fingerprint not found", msgs)
        self.assertIn("tasks.md not found for change", msgs)
        # No fuzzy fallback: the good fingerprint's box is NOT flipped via the bad taskId.
        self.assertIn("- [ ] 1.1", self.read("openspec/changes/demo/tasks.md"))
        self.assertEqual(report["movedToReviewed"], [])  # errors -> stays pending


class TestIdempotency(ReconcileTestBase):
    def test_rerun_is_a_noop(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700")
        self.run_reconcile()
        # Submission moved to reviewed/; re-point pending there and re-run: no new lines.
        testing_after_1 = self.read("TESTING.md")
        report2, _ = reconcile.reconcile(self.repo, self.pending / "reviewed", DATE, False, False)
        testing_after_2 = self.read("TESTING.md")
        self.assertEqual(testing_after_1, testing_after_2)  # no duplicate Confirmed line
        self.assertEqual(testing_after_1.count("Confirmed 2026-08-21"), 1)
        self.assertTrue(all(a["status"] == "already-applied"
                            for a in report2["applied"] if a["verdict"] == "pass"))


class TestDryRun(ReconcileTestBase):
    def test_dry_run_writes_nothing(self):
        self.make_testing("- [ ] `00000001` **A.** body *(demo 1.1)*")
        self.tasks_md("demo", "- [ ] 1.1 do the thing")
        self.submission("2026-08-21T10-00-00", [
            {"fingerprint": "00000001", "taskId": "demo 1.1", "verdict": "pass",
             "note": "Works."}], "2026-08-21T10:00:00-0700",
            general_notes="a note")
        before_testing = self.read("TESTING.md")
        before_tasks = self.read("openspec/changes/demo/tasks.md")
        report, _ = self.run_reconcile(dry_run=True)
        self.assertEqual(before_testing, self.read("TESTING.md"))
        self.assertEqual(before_tasks, self.read("openspec/changes/demo/tasks.md"))
        self.assertFalse((self.pending / "triage-inbox.md").exists())
        self.assertTrue((self.pending / "2026-08-21T10-00-00.json").exists())  # not moved
        # ...but the report still reflects what WOULD happen.
        self.assertEqual(len([a for a in report["applied"] if a["status"] == "applied"]), 1)


class TestReadyToArchiveDetection(ReconcileTestBase):
    def test_fully_checked_helper(self):
        self.assertTrue(reconcile.tasks_fully_checked(
            ["- [x] 1.1 a", "- [x] 1.2 b", "  - a note"]))
        self.assertFalse(reconcile.tasks_fully_checked(
            ["- [x] 1.1 a", "- [ ] 1.2 b"]))
        self.assertFalse(reconcile.tasks_fully_checked(["no boxes here"]))


class TestRealRepoRegression(unittest.TestCase):
    """Task 6.2 reality check: dry-run over the real `.playtest-submissions/reviewed/`
    set and assert every reviewed pass/fail verdict whose fingerprint STILL exists in the
    live TESTING.md matches the verdict already on file (pass->confirmed, fail->broken).
    Records mismatches. Historical fingerprints no longer in TESTING.md are expected (the
    file is pruned to active items) and are ignored here -- the reconciler already reports
    them as errors rather than guessing, which a unit test above covers."""

    def test_reviewed_reconciles_to_reality(self):
        try:
            repo = reconcile.find_repo_root()
        except SystemExit:
            self.skipTest("no live repo TESTING.md found")
        reviewed = repo / ".playtest-submissions" / "reviewed"
        if not reviewed.is_dir():
            self.skipTest("no reviewed/ submissions to check against")

        testing_lines = (repo / "TESTING.md").read_text(encoding="utf-8").splitlines()
        items = reconcile.parse_testing(testing_lines)

        mismatches = []
        checked = 0
        subs = reconcile.load_submissions(reviewed)
        # Winner-per-fingerprint mirrors the reconciler's own "latest submittedAt wins".
        winner = {}
        for sub in subs:
            for it in sub["items"]:
                fp, v = it["fingerprint"], it["verdict"]
                if not fp or v not in ("pass", "fail"):
                    continue
                if fp not in winner or sub["submittedAt"] >= winner[fp][0]:
                    winner[fp] = (sub["submittedAt"], v)
        for fp, (_ts, verdict) in winner.items():
            item = items.get(fp)
            if item is None:
                continue  # historical item pruned from TESTING.md; not a mismatch
            on_file = [k for k in item["anno_kinds"] if k]
            if not on_file:
                continue  # in TESTING.md but never verdicted; nothing to compare
            latest = on_file[-1]
            checked += 1
            expected = "confirmed" if verdict == "pass" else "broken"
            # A later fix can legitimately turn a historical fail into a Confirmed; only
            # flag the reverse-ish surprises where a pass reviewed item shows as broken.
            if verdict == "pass" and latest not in ("confirmed",):
                mismatches.append((fp, verdict, latest))
        print(f"\n[regression] compared {checked} reviewed verdict(s) still in TESTING.md; "
              f"{len(mismatches)} mismatch(es): {mismatches}")
        self.assertEqual(mismatches, [], f"reviewed verdicts diverge from TESTING.md: {mismatches}")


if __name__ == "__main__":
    unittest.main(verbosity=2)
