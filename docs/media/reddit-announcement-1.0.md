# Reddit announcement — r/vintagestory (1.0 feature update)

**Goal:** feature-update post following the 0.2 release. Same casual, personal tone. Lead with the
new headline tier (Tablets), then surface the BREAKING gesture change high in the body so updaters
don't miss it. Link to the mod DB. Promoted to FINAL with 1.0 screenshots resolved (from the 0.2
draft in `announcements-1.0.md`).

---

## Title options (pick one)

1. **Scribe 1.0 is out — early-game clay & wax Tablets, written in cuneiform (+ a heads-up: the right-click gesture changed)**
2. **[Mod Update] Scribe 1.0 — stone-age Tablets you scratch, dry, re-wet or fire; the note-taking mod's first "complete" cut**
3. **Scribe 1.0: carry a clay tablet from day one, write it in cuneiform, fire it to keep it forever**

*(#1 puts both the hook and the gesture heads-up in the title. #2 leads with the archaeology angle. #3 is the most evocative.)*

---

## Post body (FINAL — ready to post)

> Hey — **Scribe** is my mod for keeping your to-do list *inside* the game instead of on a sticky note
> next to your monitor. It started as a **Lectern** you place and write on (0.1), grew a carried
> **Notebook** and a **Clockmaker's** timer variant (0.2), and now **1.0** adds the tier that was
> always meant to sit at the bottom of the tech tree: **clay & wax Tablets** you can craft in the
> stone age, long before you have parchment or a lectern.
>
> *(lead image: the four Tablet varieties — red / blue / fire clay + wax — laid out on a table, from `TabletsFeatures.png`)*
>
> **⚠️ Heads-up if you're updating from 0.2 — the right-click gestures changed.**
> **Shift + right-click now quick-adds a task** (on the Lectern, the Notebook, and a wet Tablet)
> instead of opening the plain editor. To **place** a held Notebook or Tablet on the ground, use
> **Ctrl + Shift + right-click** — the same modifier the vanilla spear uses. If you're wondering
> "where did my ground-place go," that's it. (Shift + right-click on water still softens a hardened
> clay tablet.)
>
> **What's new in 1.0:**
> - **Clay & Wax Tablets** — the earliest, cheapest writing surface. A quick handheld scratchpad
>   (up to 10 tasks, 1 pin). A fresh clay tablet is **wet and editable**; leave it alone and it
>   **dries hard** over a couple of in-game days, locking the writing — then **dunk it in water** to
>   soften it back and revise, or **fire it in a firepit** to make the writing permanent. It's the
>   real archaeology of clay tablets, turned into a mechanic.
> - **Wax Tablet** — a reusable step up that never dries and never fires. The "erasable slate" option
>   if you don't want the dry/fire life-cycle.
> - **Cuneiform script** — tablet text is written in a carved-wedge cuneiform by default (it *is* a
>   clay tablet, after all). Prefer plain letters? There's a toggle in Scribe Settings.
> - **Clay colours** — tablets come in red, blue, and fire-clay, so they read differently on your
>   hotbar and in a chest.
> - **In-game error notices** — a few "this tablet is full" / "someone else is editing" cases now
>   surface as a proper in-game message instead of failing silently.
>
> *(second image: a clay tablet open in-editor showing the cuneiform script + firepit tooltip, from `TabletsClay.png`. Optional third: tablets shelved in a bookshelf, from `ShelfStorage.png`.)*
>
> This is Scribe's first cut I'm comfortable calling **"complete"** — three writing tiers (Tablets →
> Notebook/Clockmaker → Lectern) all feeding the per-player pinned-task HUD. What's left on the
> roadmap (a private **Writing Desk** in 1.1, a shared **Bulletin Board** later) is genuinely
> additive — nothing here is a placeholder.
>
> Still requires **LibGUI** (one-click from the mod DB, both client and server). Existing worlds are
> fine — everything's additive and your old lectern/notebook data loads clean.
>
> **https://mods.vintagestory.at/scribe** — feedback always welcome, especially on how the
> dry/re-wet/fire timing feels in practice and whether the cuneiform default is fun or annoying.
>
> *(For anyone following along: the 0.2 thread — link the previous release post here.)*

---

## Media (resolved — B.2 screenshots in `screenshots/1.0/`)

Upload order for the post:

1. **`TabletsFeatures.png`** — hero image: the four varieties (red/blue/fire clay + wax) on a table with a "New in 1.0: Tablets" caption. Leads the post.
2. **`TabletsClay.png`** — a Clay Tablet open in the editor, showing the cuneiform font, the feature list, and the "Will harden in 2 days / Cooks into Fired Red Clay Tablet" tooltip. Best single shot of the dry→fire mechanic.
3. **`TabletsWax.png`** — the Wax Tablet editor (contrasts the "never dries, hardens, or fires" line). Good third image or an album with #2.
4. **`ShelfStorage.png`** — tablets shelved in a bookshelf; a nice ambient "these live in your base" beauty shot.

If a life-cycle gif gets made later (craft → scratch → dry → re-wet → fire), lead with that instead and demote `TabletsFeatures.png` to second.

## Notes for posting
- Tag/flair as **Mod Update**.
- Drop a short comment on the 0.2 thread pointing here.
- Mod DB link inline: https://mods.vintagestory.at/scribe
- Screenshots live at `docs/media/screenshots/1.0/` (gitignored — upload to imgur/the post host).
