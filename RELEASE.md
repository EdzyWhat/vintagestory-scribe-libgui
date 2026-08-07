# Release plan — Scribe v1.0.0 (Tablets tier + first "complete" cut)

Tracked checklist for the **in-flight** release cut. This is the **map**; the framing/rationale
hub is the Slack canvas *"Scribe v1.0.0 — Release Hub"* (`F0BMW2XE2H5`), per-task detail lives in
the linked OpenSpec changes and `docs/`, shipped releases are recorded in
[`CHANGELOG.md`](./CHANGELOG.md), and the forward tier-map is [`ROADMAP.md`](./ROADMAP.md).

**v1.0.0 is LOCKED** (2026-08-05). The first "complete" cut: a feature-complete handheld + lectern
writing system across three tiers (Lectern, Notebooks, **Tablets** — the marquee new tier), with an
entirely *additive* remaining roadmap. Ships **everything** currently in flight, cuneiform and firing
included — no fast-follow split. Deps unchanged: `game 1.22.x`, hard `gui 3.1.0`.

> **The one thing to internalize:** the code is done. The gate to release is a **verify → archive →
> cleanup → ship** sweep, not a build sprint. The full in-game playtest sweep is complete
> (`TESTING.md`: 286 confirmed / 27 obsolete / 1 parked, 0 broken, 0 untested). What's left is the
> save-compat gate, archiving in dependency order, doc/cleanup, and the mechanical cut.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done.

## Track V — Verification (the real gate)

- [x] **V.1. In-game playtest sweep** — every in-game-testable item across all in-flight changes has a
      recorded verdict in `TESTING.md`. Complete: 286 confirmed, 27 obsolete, 1 parked (`7a86b890`
      sink-ordering, a known non-blocking fix deferred), 0 still-broken, 0 untested.
- [x] **V.2. Firing tuning values** — `meltingPoint 650` / `meltingDuration 30` set in
      `scribetablet.json`; `Harden` `freshHours ≈48` + `transitionHours` tuned. Confirmed in-game via the
      dry→hard→fire→rehydrate chain (`add-tablet-firing-mechanic` 8.2–8.7 all confirmed). Firing-tuning
      OpenSpec tasks 0.2/0.3 still show `[ ]` — reconcile the checkboxes when archiving that change.
- [x] **V.3. SAVE-COMPAT LOAD TEST — was the ⚠️ HARD BLOCKER, now PASSED (2026-08-06).** Codec is
      `Version=5` accepting `PriorVersion=4`. Verified live: created a lectern world under the staged
      v0.1.2 build (codec v4), reloaded the same world under the v5 dev build — the lectern opened clean
      and every task survived intact. v0.1.2 predates notebooks, so lectern docs are the only v4 payload;
      there is no v4 notebook data to migrate. Recorded in `TESTING.md` (`04e53d95`).
- [x] **V.4. Localization sweep** — done 2026-08-06. Swept all 72 `src/Mod/*.cs`; all GUI/HUD/error
      text already routed through `Lang.Get`. Fixed the residue: the ` Year ` date prose (6 sites,
      centralized to `NotebookHost.FormatCalendarDate` + `scribe:date-format` key, collapsing 4 inline
      dupes), the 3 default document titles (`doctitle-{lectern,notebook,tablet}`), the storm-strength
      word (`storm-strength-{light,medium,heavy}`), and the 3 defensive death-message fallbacks
      (`death-generic`, `death-slain-by`). English output byte-identical; verify.sh green (Core 286/286,
      Atlas 25/25).
- [x] **V.5. FIRST-OPEN FLICKER FIX — was a ⚠️ HARD BLOCKER (found 2026-08-06), now DONE + ARCHIVED.** The first open of any
      not-yet-crafted Scribe item (notebook/clockmaker/tablet) flickers closed and needs a second
      right-click. Root cause traced: the one-time server "Picked up" history re-sync fires `SlotModified`
      and the DocId-strict close-guard mis-reads it as a switch-away. Spec'd as OpenSpec change
      `fix-item-dialog-first-open-flicker`. **FIXED + CONFIRMED 2026-08-06** — `OnHotbarSlotModified` now uses
      a presence-only check (`ActiveHandHoldsAnyScribeDocumentItem`) instead of the DocId-strict guard, in
      both `GuiDialogScribeNotebook` and `GuiDialogScribeTablet` (clockmaker inherits); verify.sh green
      (Core 286/286, Atlas 25/25, restaged). In-game confirmed across all TESTING.md items (first-open
      notebook/clockmaker/wet+wax tablet, self-crafted control, drop-closes, switch-away-closes, wet→hard
      transition). No longer a blocker — ready to archive this change in Track A.

## Track A — Archive completed OpenSpec changes (in dependency order)

All 11 in-flight changes are code-complete and playtest-confirmed. Archive each with
`openspec archive <name>` **in dependency order** or the delta headers drift. Ordering gates:
`add-tablet-firing-mechanic` archives AFTER `add-tablet-clay-type-backdrops`;
`wire-tablet-clay-art-and-variants` archives AFTER both.

- [x] **A.1.** `add-tablet-clay-type-backdrops` (archive first — others gate on it)
- [x] **A.2.** `add-tablet-firing-mechanic` (after A.1)
- [x] **A.3.** `wire-tablet-clay-art-and-variants` (after A.1 + A.2)
- [x] **A.4.** `tune-tablet-clay-text-contrast`
- [x] **A.5.** `add-tablet-cuneiform-chrome`
- [x] **A.6.** `add-cuneiform-handwriting-feel`
- [x] **A.7.** `add-clockmaker-notebook-schematic`
- [x] **A.8.** `add-unified-quick-add-interaction` (code done; testable items confirmed)
- [x] **A.9.** `fix-settings-numeric-arrow-focus-leak`
- [x] **A.10.** `zero-point-three-fixes`
- [x] **A.11.** `add-handbook-web-editor`

> Before archiving each: reconcile any `tasks.md` `[ ]` boxes that are actually done-but-unchecked
> (in-game verifications now confirmed in `TESTING.md`, bookkeeping `0.x` "apply after" gates) so the
> archived delta reflects reality.

**Done 2026-08-06.** All 11 changes archived to `openspec/changes/archive/2026-08-*/` and `tasks.md`
boxes reconciled from `TESTING.md` verdicts first. Because 7 capabilities were touched by more than one
pending change (header drift — see `[[openspec-archive-order-header-drift]]`), those 8 changes were
archived `--skip-specs` (move-only) and the 3 changes touching only unique capabilities archived with
full spec sync; a single consolidated manual spec-sync then rewrote the 7 contested main specs
(`tablet-dialog`, `clay-wax-tablet-item`, `tablet-clay-hardening`, `tablet-firing`,
`clockmaker-notebook-schematic`, `gui-backdrop`, `scribe-document-policy`) to the shipped end-state
(last-writer-wins across date-ordered deltas). `openspec validate --specs` → 52 passed, 0 failed.

## Track C — Cleanup pass ("what have we missed")

- [x] **C.1. Dead code / dev harness sweep** — done 2026-08-06. The prior note here (and a memory)
      was WRONG: the cuneiform harness was NOT already stripped. Found + removed two **ungated client**
      dev commands still shipping to survival players — `/scripttf` (live model-transform tuner) and
      `/cuneiform` (`GuiDialogCuneiformHarness`, 115 lines) — plus the cached `cuneiformHarness` field,
      its registration, and its disposal; deleted `GuiDialogCuneiformHarness.cs`. Kept `/scribe seed`
      (double-gated: `controlserver` privilege + creative check; needed for B.2 screenshots).
      `.cuneiformglow` confirmed already gone (only a historical comment at `CuneiformGlow.cs:39`). No
      dead code found; `#if DEBUG` blocks compile out of Release; `placeholder` hits are all live
      UI hint/fallback code. verify.sh green (Core 286/286, Atlas 25/25, restaged).
- [x] **C.2. In-game handbook audit** — done 2026-08-06. Audited all 10 `scribetablet` variants
      (clay red/blue/fire × wet/hard/fired + wax) in `itemtypes/scribetablet.json`: every variant
      resolves handbook `extraSections` (base `about`/`states`/`hud-ref`; `*-hard`→`hard`, `*-fired`→
      `fired`, `*-wax`→`wax`), and **every referenced lang key exists** in `scribe/lang/en.json` — no
      key-echo misses. Tablets correctly reuse the shared `handbook-scribe-hud-ref-*` section like the
      notebook/lectern. Two optional polish items surfaced and were declined for 1.0.0: (a) no dedicated
      `craft` section like the notebooks have (tablets cover crafting inline + recipes auto-render), and
      (b) all state variants stay handbook-visible (no `handbook.exclude` on intermediate `-hard`/
      `-fired`) — intentional. Ships as-is.
- [x] **C.3. Wiki: new Tablets page** — done 2026-08-06. Added `Clay-and-Wax-Tablets.md` (wet→hard→
      fired life-cycle, water re-wet, never-drying wax, 10-task/1-pin limits, Shift+RC quick-add,
      cuneiform toggle, clay colours), sourcing all mechanics from the shipped handbook/lang text.
      Updated `Home.md` (nav link + intro + roadmap: Tablets = shipped **v1.0**, Writing Desk → v1.1),
      `Items.md` (Tablets section + reframed intro), `Crafting-the-Lectern.md` (clay + wax grid recipes
      from the actual recipe JSON), and the wiki `README.md` (1.0.0 changelog + publish checklist).
      Swept for stale refs (v0.4 / "two items" / 0.2.0) — none remain; dep line still gui 3.1.0.
      Tablets page has no images yet — add tablet/cuneiform/firing screenshots after Track B.2.
- [ ] **C.4. ROADMAP.md** — mark v3 (Tablets) shipped; strike the cut **Scratch+ paper** tier row;
      reconcile "wets out in water" → the shipped harden/rehydrate/fire mechanic; mark the deferred
      **error surface** partially resolved (`zero-point-three-fixes` §7 shipped `TriggerIngameError`);
      confirm the runway is just Desk (v1.1) + Board.
- [ ] **C.5. CHANGELOG.md** — fold the `[Unreleased]` block into a real `[1.0.0]` header and complete
      it with the full tablet tier (cuneiform, firing, wax, per-clay themes) + the BREAKING quick-add
      gesture change, called out prominently.
- [ ] **C.6. README.md** — verify feature list + version references match the release.
- [x] **C.7. CREDITS** — JeanPierre (Wanderer's Sketchbook) already credited; keep in the consistency check.

## Track B — Launch material

- [ ] **B.1. Mod-page text** (`docs/media/mod-page.txt`) — new tier, roadmap bump, dep line; fix the
      stale `v0.3 Writing Desk` next-tier line (Desk is now v1.1).
- [ ] **B.2. Screenshots** into `docs/media/screenshots/1.0/` via `/scribe seed` — tablets, cuneiform, firing.
- [ ] **B.3. Announcement drafts** for all four channels (0.2 reddit + teaser exist as templates in
      `docs/media/`) — **call out the BREAKING quick-add gesture change** (lectern Shift+RC and
      held-item ground placement both change).

## Track G — Ship (mechanical, after V + A + C)

- [ ] **G.1. Version bump** — `modinfo.json` `0.2.0` → `1.0.0`.
- [ ] **G.2. Version consistency check** — `modinfo.json` · `CHANGELOG` header · mod page · wiki ·
      video script all state **1.0.0** and **gui 3.1.0**.
- [ ] **G.3. Build the release zip**, tag `v1.0.0`, create the GitHub Release, upload, publish to mod DB.
- [ ] **G.4. Announce — wiki-first:** publish/refresh the **wiki** (source of truth), then **Mod DB**
      (`mods.vintagestory.at/scribe`), then **Reddit (r/VintageStory)**, then **VS Discord**. Seed the
      FAQ for "where did my ground-place go" (the quick-add gesture change).

## After ship

- Watch mod-DB comments + reddit + Discord for early-adopter reports; triage into a `1.0.1` fixes change.
- Open **Writing Desk (v1.1)** planning: scope is genuinely open (copy/paste export
  lectern↔notebook↔Desk + CSV/Excel interchange; kanban/search deprioritized) — needs a definition
  pass before it's a clean `openspec-propose`.
