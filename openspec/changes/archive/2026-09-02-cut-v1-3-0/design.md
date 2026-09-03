## Context

Feature work since `v1.2.1` (2026-08-18) is archived: Crafting Tasks (codec v8), Chalkboard, meal-page Add-to-Scribe, liquid-ingredient trackers, tablet readability (glyph-forge themes), title wrapping, Transcribe stamp sound, recipe-variant identity, handbook uniqueness-first restructure. `main` is one informal commit ahead (`Trying to get v1.3.0 out the door`). The **cut surfaces are stale**:

| Surface | Today |
|---|---|
| `src/Mod/modinfo.json` | `1.2.1` |
| `CHANGELOG.md` | last entry `[1.2.1]`; no `[1.2.1]` compare-link footer |
| `README.md` | “released — v1.1.0” |
| `ROADMAP.md` | v1.2 “in progress”; v1.3 = assignment system |
| `docs/media/mod-page.*` | v1.2 Writing Desk still planned; no Scriptorium/Chalkboard/Craft |
| `docs/media/wiki/` | 1.0.0 drafts |
| In-game handbook | “two item-bound task types”; no Chalkboard on Getting Started |
| `docs/CODEC-MIGRATION.md` + comments | window ends at **v7**; code is **v8** / pin **v5** |

Previous cuts (`Cut v1.1.0`, `Cut v1.2.0`) are the template: one bookkeeping commit, then the author tags/zips/uploads by hand.

`stamp.ogg` is author-recorded — no CREDITS line.

## Goals / Non-Goals

**Goals:**
- A truthful 1.3.0 version freeze: `modinfo`, CHANGELOG, README, mod-page, wiki Home, ROADMAP all agree.
- In-game handbook describes Crafting Tasks and the Chalkboard.
- Codec docs and comments name the actual `[5, 8]` window and v8/pin-v5 fields.
- Wiki drafts no longer claim a Lectern-only 1.0 world.

**Non-Goals:**
- Tagging, zip, GitHub Release, mod-DB upload (author’s ship step).
- pt-br translation (English fallback, same as 1.2).
- Parked bugs, LibGUI decoupling, Scriptorium dedicated backdrop.
- Stamp-sound credit.
- New code behavior, codec bumps, or gameplay changes.
- Reddit / video launch copy (not part of the 1.2 cut either).

## Decisions

### D1 — CHANGELOG from a per-change audit, player-facing only

Audit `git log v1.2.1..HEAD` and the `openspec/changes/archive/2026-08-{19,21}-*` folders. Fold infra into the visible payoff (playtest reconcile, `/scribe tablet`, wrap-title internals). Keep Keep-a-Changelog sections.

**Player-facing inventory (draft — polish at implement time):**

- **Added:** Crafting Tasks (handbook-driven, recipe variants, ingredient subtasks, liquids as litre trackers); wall-mounted Chalkboard (10-task cap); meal-page Add-to-Scribe; Transcribe stamp sound; tablet per-state readability (glyph-forge ink/glow/stroke); wrapping titles; tablet row links (cuneiform names).
- **Fixed:** recipe variant identity (Hunter’s Backpack / whole-code wildcards); craft-subtask live rescale; fired/hardened tablet legibility.
- **Changed:** handbook uniqueness-first restructure (Tabs & Views article; per-object entries uniqueness-first).
- **Save-compat:** document codec v8 (`RecipeSignature`); pin codec v5 (`Depth`). 1.0–1.2 worlds open (`MinVersion` still 5). 1.3 writes are v8 — a 1.2 client cannot read them; VS requires matching mod versions, so mixed MP is not a supported case.

Add compare-link footers for `[1.3.0]` **and** the missing `[1.2.1]`.

Alternative considered: a thin `[Unreleased]` stub. Rejected — 1.2.0 cut went straight to a dated `[1.2.0]` section.

### D2 — ROADMAP: this cut *is* v1.3; assignment is “later”

Do **not** invent a v1.4 number. Mark:

- v1.2 Scriptorium + Tracker/Link + Transcribe **shipped**.
- v1.3 Crafting Tasks + Chalkboard (+ tablet readability) **shipped**.
- Assignment (Assign & History / Inbox) **later**, still on the Scriptorium cluster.
- Bulletin board / drawable chalkboard remain the v6 social tier; this wall Chalkboard is the Lectern form-factor variant, not that.

Alternative considered: call this 1.2.2 because assignment was “the” 1.3. Rejected — new block + new task kind + codec bump is a minor.

### D3 — Patch existing handbook articles; do not add a new `craftinginfo` page

Edit these `en.json` keys in place:

- `craftinginfo-scribe-getting-started-text` — add Chalkboard to the craft list; name Crafting Tasks in the task-types paragraph; fix `featues`, the incomplete “enrich your experiences with other”, “Item Item Tracker” (in the HUD article, not this key), Guest Book / History / Timer line so Chalkboard is a placed surface.
- `craftinginfo-scribe-task-types-title` / `-text` — third type: Crafting Task (handbook page, recipe variants, ingredient subtasks, litre liquids). Keep Tracker/Link. Title becomes something like “Scribe Mod: Item Trackers, Links & Crafting Tasks”.
- `craftinginfo-scribe-editor-reference-text` — “Item Trackers and Links” is no longer “the two item-bound types”; Craft is also handbook-only; grip-tap subtask indent is worth one sentence.
- `craftinginfo-scribe-pinned-hud-text` — pin Craft rows too; fix “Item Item Tracker”.
- `craftinginfo-scribe-views-text` — already names the Chalkboard on Guest Book; leave unless a contradiction turns up.

Chalkboard’s per-object `handbook-chalkboard-about-text` is already uniqueness-first — no rewrite unless Getting Started’s new link needs a wording tweak.

Use the sibling handbook editor if it is handy; surgical `en.json` edits are acceptable. `pt-br.json` is left alone (keys fall back to English).

### D4 — Wiki drafts: Home + Items + Crafting + two new pages

`docs/media/wiki/` is the in-repo source; publishing to the GitHub wiki clone stays manual (same as 1.0).

- **Home.md** — intro mentions Scriptorium + Chalkboard; nav links; roadmap ticks v1.2 and v1.3 shipped, assignment later.
- **Items.md** — Scriptorium and Chalkboard sections (Lectern-length, uniqueness-first).
- **Crafting-the-Lectern.md** — Scriptorium (more-expensive Lectern: same kit, 8 planks) and Chalkboard (planks + charcoal + nails, no ink kit).
- **New `Chalkboard.md`** — wall-mount, 10-task cap, same document as Lectern, not the drawable v6 board.
- **New `Scriptorium.md`** — Transcribe + copy/import/export; 1.2 never got a wiki page.

Editor-Reference.md gets a short Craft / subtask note if the in-game editor-reference change would otherwise leave the wiki lying. No new screenshots required (imgur gaps already exist for Tablets).

### D5 — Codec docs only; no codec behavior

Bring these in line with `ScribeDocumentCodec` (`Version = 8`, `MinVersion = 5`) and `ScribePinCodec` (`PinVersion = 5`):

- Class doc-comment on `ScribeDocumentCodec` is already v8 — leave unless a leftover “v7” remains.
- `TryDeserialize` summary still says `[MinVersion=5, Version=7]` — fix.
- `tests/Core.Tests/ScribeDocumentCodecTests.cs` comments that say `[v5, v7]` — fix. If a dedicated v7 older-blob test (RecipeSignature defaults empty) is missing, add it; Craft tests already cover v8 round-trip and absent-signature defaults.
- `docs/CODEC-MIGRATION.md` — worked example + summary table currently “v7 / `[5, 7]`”. Add v7→v8 (`RecipeSignature`) and pin v4→v5 (`Depth`); table current = v8, window v5–v8.

No `Serialize`/`TryDeserialize` logic change.

### D6 — One “Cut v1.3.0” commit is the implementation’s last task, not this proposal

This change’s tasks **are** the cut. After they land, the author tags. Do not include `git tag` / `gh release` in tasks.

### D7 — Version-surface checklist (must all say 1.3.0)

Copied from the 1.1 cut, plus ROADMAP:

- `src/Mod/modinfo.json` `version`
- `CHANGELOG.md` `[1.3.0]` date + compare links
- `README.md` status line (and feature bullets: Scriptorium, Chalkboard, Craft — today README has Scriptorium but not Chalkboard/Craft)
- `docs/media/mod-page.txt` / `.html` / `mod-page-inline.html`
- `docs/media/wiki/Home.md`
- `ROADMAP.md` status table + staged-plan v4/v7 paragraph

Deps unchanged: `game 1.22.0`, `gui 3.1.0`.

## Risks / Trade-offs

- **[Wiki publish is manual]** → Mitigation: this change only updates `docs/media/wiki/`; the author’s existing publish checklist (`docs/media/wiki/README.md`) still runs at tag time. Note that in the tasks as a reminder, not an automated step.
- **[CHANGELOG misses a player-facing bit]** → Mitigation: audit against the archive list in D1, not git messages alone; skip `/scribe tablet` and playtest-reconcile.
- **[Handbook HTML is easy to smash]** → Mitigation: edit the existing keys; keep cross-links (`handbook://…`); don’t reflow unrelated paragraphs.
- **[pt-br 82 keys behind]** → Accepted: English fallback. Not this change.

## Migration Plan

None for saves. Docs-only + version string. Rollback = revert the cut commit before tagging.

## Open Questions

None. Stamp credit is settled (none). Assignment is “later,” not a numbered 1.4.
