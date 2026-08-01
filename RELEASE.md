# Release plan — Scribe v0.2.0 (Notebook & Clockmaker's Notebook)

Tracked checklist for the **in-flight** release cut. This is the **map**; per-task detail lives
in the linked OpenSpec change and `docs/`. Update the boxes here as tracks land. Shipped
releases are recorded in [`CHANGELOG.md`](./CHANGELOG.md); the forward tier-map is
[`ROADMAP.md`](./ROADMAP.md).

Adds the Notebook + Clockmaker's Notebook (built in prior changes), the plain Notebook's survival
recipe, in-game handbook coverage, a `tinkerer`-trait craft gate for the Clockmaker's Notebook (+
worldconfig bypass), a Clockmaker's-Notebook detection bugfix, a creative-only `/scribe seed` demo
command, and refreshed launch material. Map lives in the OpenSpec change
`scribe-0-2-0-release-content`; per-task detail is in that change's `tasks.md`. Deps unchanged:
`game 1.22.x`, hard `gui 3.1.0`. **Current: in progress.**

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done.

## Track A2 — Product (content + code)

- [x] **A2.1. Notebook survival recipe** (`recipes/grid/scribenotebook.json`) — data-only, 3×2 writing set.
- [x] **A2.2. Clockmaker's Notebook trait gate** — `requiresTrait: tinkerer` + `scribeClockmakerRequiresTrait`
      worldconfig + startup null-out bypass.
- [x] **A2.3. In-game handbook** — `handbook.extraSections` on both notebook items + refreshed guide pages.
- [x] **A2.4. Detection bugfix** — widened all sibling-exclusion sites to include `ItemClockmakerNotebook`.
- [x] **A2.5. `/scribe seed` demo command** — server-side, creative + `controlserver`-gated; seeds tasks,
      notes, Notebook History, Lectern Guestbook.
- [x] **A2.6. Build + Core suite green** — Mod builds; 183 Core tests pass.
- [ ] **A2.7. In-game verification** — recipe craftable + full chain; trait gate on/off; handbook renders;
      seed populates + persists + syncs on both a Notebook and a Lectern. *(needs the game — see tasks 1.4,
      1b.4, 2.4, 3.9.)*

## Track B2 — Launch material

- [x] **B2.1. Mod page** (`docs/media/mod-page.txt`) — LibGUI 3.1.0, notebook section, roadmap bump.
- [x] **B2.2. Wiki drafts** (`docs/media/wiki/`) — 7 refreshed + 3 new pages + publishing README.
- [x] **B2.3. Reddit 0.2 announcement** (`docs/media/reddit-announcement-0.2.md`).
- [x] **B2.4. Video script + shot-list** (`docs/media/video-script.md`) — 0.2 beats + seed cheat sheet.
- [ ] **B2.5. Screenshots** into `docs/media/screenshots/0.2/` via `/scribe seed`. *(needs the game.)*

## Track G2 — Ship (mechanical, after A2/B2)

- [x] **G2.1. Version bump** — `modinfo.json` → 0.2.0.
- [x] **G2.2. CHANGELOG** — `[0.2.0]` entry.
- [ ] **G2.3. Build the release zip**, tag `v0.2.0`, create the GitHub Release, upload, publish to mod DB.
- [ ] **G2.4. Post** the reddit announcement + refreshed wiki pages.

### Consistency check (task 5.4)
modinfo `0.2.0` · CHANGELOG `[0.2.0]` · mod page + wiki + video script all state **0.2.0** and
**LibGUI 3.1.0**. ✅ verified 2026-07-31.
