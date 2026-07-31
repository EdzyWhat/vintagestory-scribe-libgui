# Release plan — Scribe v1 (v0.1.0)

Tracked checklist for the first playable release. This is the **map**; per-task detail lives in
the linked OpenSpec changes / `docs/`. Update the boxes here as tracks land.

**Target:** `v0.1.0` — first public release. Lectern + pinned-task HUD, server-authoritative,
multiplayer- and survival-safe. Deps: `game 1.22.0`, hard `gui 3.1.0` (LibGUI). **Current: v0.1.2, shipped + live on mod DB.**

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done.
**Dependency spine:** Track A (finish v1) → Track B (media) feeds → E (video), F (mod page).
Track C (reddit teaser) is being pulled FORWARD — it only needs one hero shot (B1) and goes out
ASAP, ahead of the rest (decided 2026-07-26). Track D (handbook) is independent.

---

## Track A — Finish v1 (product) — GATES EVERYTHING

The release can't be truthful until this is done. Ordered by dependency.

- [x] **A1. Clip over-long task text.** Confirmed in-game 2026-07-26. Core `MaxTaskTextLength=1000` +
      clip-on-read; editor `ScribeMultilineField.MaxLength` on Task rows. Shipped in v0.1.0.
- [x] **A2. ~~Per-task soft length limit~~ — MERGED into A1** (the clip IS the length limit).
- [x] **A2d. Lectern Pin Tab.** Confirmed in-game 2026-07-28. Nav-column Pin Tab view; full editor parity
      per row. Shipped in v0.1.0.
- [x] **A2b. Lectern placement orientation — face the player.** Confirmed in-game 2026-07-26.
      Sign `MeshAngleRad` idiom; `TryPlaceBlock` sets facing from player yaw. Shipped in v0.1.0.
- [x] **A2c. Lectern floor-only placement.** Confirmed in-game 2026-07-26. `CanPlaceBlock` override
      rejects non-solid-ground placement. Shipped in v0.1.0.
- [x] **A3. Drive the staged polish retests to done.** All three change groups fully confirmed:
      `refine-settings-and-window-chrome` (all confirmed 2026-07-27),
      `scribe-notebook-frame` (all confirmed 2026-07-27/28),
      `scribe-gui-backdrops` (all confirmed or obsoleted 2026-07-28).
- [x] **A4. Multiplayer / lock / reorder verification.** Headless server + 2nd client: all items
      confirmed 2026-07-28 (live cross-session sync, per-lectern docs, editor lock, drag-reorder).
- [x] **A5. Survival-safe sanity pass.** Crafting recipe archived + survival walk-away confirmed 2026-07-28.
- [x] **A6. Freeze scope.** v0.1.2 / `game 1.22.0` / `gui 3.1.0` — deps confirmed current.

## Track B — Media capture (shared input for C, E, F)

Do this ONCE, after the GUI is visually final (post-A3). Everything downstream reuses these.

- [x] **B1. Hero screenshot** — captured + embedded on mod DB page (ScribeHeroBig + 3 UI screenshots).
- [x] **B2. Feature screenshots** — 4 screenshots live on mod DB (UI, HUD, crafting recipe).
- [ ] **B3. Store raw + final under a tracked path** — screenshots are on the mod DB but not committed
      to `docs/media/`. Decide: pull URLs down or store originals here for archival.

## Track C — Reddit r/vintagestory concept teaser (DO ASAP — pulled to front)

**Priority: NOW (decided 2026-07-26).** Goes out ahead of everything else — "mod coming soon, here's
a picture, what do people think?" Only needs ONE hero shot, not a finished multiplayer pass or final
polish. Concept feedback can then shape the rest of v1. Only real prerequisite: the GUI looks good
enough for a single screenshot (it does — notebook art + settings landed).

- [x] **C1. Grab a hero screenshot NOW** — captured (used on mod DB page).
- [x] **C2. Draft post copy** — complete in `docs/media/reddit-teaser.md`.
- [x] **C3. Confirm r/vintagestory rules** — done.
- [x] **C4. Post** — WIP/concept teaser posted at https://www.reddit.com/r/VintageStory/comments/1v7jgfi/
      **Launch announcement post** still needed (draft in `docs/media/reddit-announcement.md`).

## Track D — In-game handbook guides (independent)

Follow VS handbook convention. Can run anytime after GUI is final.

- [x] **D1. Research the convention** — researched and implemented.
- [x] **D2. Draft guide content** — "Using the Lectern" + HUD page written.
- [x] **D3. Wire it in** — handbook entries committed (`fe71d53`), polished (`d05958d`); in-game
      rotation confirmed 2026-07-28.

## Track E — Release showcase video (NEW)

Short feature-showcase video with a follow-along script. Needs the final GUI (A3) and ideally A4.

- [x] **E1. Write the script/shot-list** — beat sheet in `docs/media/video-script.md`.
      8 beats: problem → place → open/write → check off → pin to HUD → settings → multiplayer → CTA.
      Target ~90–120s.
- [ ] **E2. Capture footage** per the shot list.
- [ ] **E3. Edit + export**; embed on mod DB page and link from reddit announcement.

## Track F — Official VS mod DB page draft (LAUNCH VEHICLE — last)

Assembles everything above. Draft the content in-repo (`docs/mod-page.md`), then paste into the mod DB.
**Mod DB page is LIVE: https://mods.vintagestory.at/scribe#tab-description**

- [x] **F1. Hook** — live on mod DB.
- [x] **F2. Description** — live on mod DB.
- [x] **F3. How to use** — live on mod DB.
- [x] **F4. Dependencies & compat** — LibGUI hard dep documented; `gui 3.1.0` for v0.1.2.
- [x] **F5. Pictures** — 4 screenshots embedded. Video: not done yet (Track E).
- [x] **F6. Housekeeping** — MIT, source link, version, changelog on mod DB.

## Track G — Ship (mechanical, after A–F ready)

- [x] **G1.** Packages built: `scribe_0.1.0.zip`, `0.1.1.zip`, `0.1.2.zip` all in `Releases/`.
- [x] **G2.** Tags `v0.1.0`, `v0.1.1`, `v0.1.2` pushed; GitHub Releases created via `release.yml`.
- [x] **G3.** Zips uploaded for all three releases.
- [x] **G4.** Mod DB page published (77 downloads). Reddit post still pending (Track C).

---

### Critical path (shortest route to a truthful launch)

**Now:** Track C (reddit teaser) — grab one hero shot + post ASAP, independent of the rest.

**Then, to launch:** `A1+A2 (error surface + length limit, both v1-blocking) → A3 (polish retests) →`
`A4/A5 (mp+survival verify) → B (final media) → E (video) + F (mod page) → G (ship)`.
Track D (handbook) is independent and can slot in anywhere after A3.

### Settled decisions (2026-07-26)
- A1 (error surface) **and** A2 (length limit) are **both v1-blocking**.
- Reddit teaser (C) is pulled to the **front** — post ASAP, ahead of finishing v1.

### Open decision still to settle
- Press-media location: reuse `screenshots/` or a new `docs/media/` (recommend separate from debug shots).

---

# Release plan — Scribe v0.2.0 (Notebook & Clockmaker's Notebook)

Second release. Tracked checklist for shipping the carried Notebook, its Clockmaker's variant with
timers, the History/Guestbook logs, and the refreshed launch material. Map lives in the OpenSpec
change `scribe-0-2-0-release-content`; per-task detail is in that change's `tasks.md`.

**Target:** `v0.2.0`. Adds the Notebook + Clockmaker's Notebook (built in prior changes), the plain
Notebook's survival recipe, in-game handbook coverage, a `tinkerer`-trait craft gate for the
Clockmaker's Notebook (+ worldconfig bypass), a Clockmaker's-Notebook detection bugfix, a
creative-only `/scribe seed` demo command, and refreshed launch material. Deps unchanged:
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
