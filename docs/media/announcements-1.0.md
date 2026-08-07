# Announcement drafts — Scribe v1.0.0 (Tablets tier + first "complete" cut)

Drafts for all four launch channels. **Publish wiki-first** (per RELEASE.md G.4): wiki → Mod DB →
Reddit → Discord. Each draft **leads with, or prominently flags, the BREAKING gesture change** — the
one thing an existing player must know before updating:

> **Shift + right-click now quick-adds a task** (on the Lectern, Notebook, and a wet Tablet) instead
> of opening the plain editor. To **place** a held Notebook or Tablet on the ground, use
> **Ctrl + Shift + right-click** (the vanilla spear convention). Shift + right-click aimed at water
> still softens a hardened clay tablet.

Deps unchanged: `game 1.22.x`, hard `gui 3.1.0`. Mod DB: https://mods.vintagestory.at/scribe

---

## 1. Reddit — r/VintageStory (feature update)

**Goal:** feature-update post following 0.2. Same casual, personal tone. Lead with the new headline
tier (Tablets), then surface the BREAKING gesture change high in the body so updaters don't miss it.

### Title options (pick one)

1. **Scribe 1.0 is out — early-game clay & wax Tablets, written in cuneiform (+ a heads-up: the right-click gesture changed)**
2. **[Mod Update] Scribe 1.0 — stone-age Tablets you scratch, dry, re-wet or fire; the note-taking mod's first "complete" cut**
3. **Scribe 1.0: carry a clay tablet from day one, write it in cuneiform, fire it to keep it forever**

*(#1 puts both the hook and the gesture heads-up in the title. #2 leads with the archaeology angle. #3 is the most evocative.)*

### Post body (draft — needs media placeholders filled after B.2 screenshots)

> Hey — **Scribe** is my mod for keeping your to-do list *inside* the game instead of on a sticky note
> next to your monitor. It started as a **Lectern** you place and write on (0.1), grew a carried
> **Notebook** and a **Clockmaker's** timer variant (0.2), and now **1.0** adds the tier that was
> always meant to sit at the bottom of the tech tree: **clay & wax Tablets** you can craft in the
> stone age, long before you have parchment or a lectern.
>
> *(video/gif: craft a clay tablet → scratch a couple of tasks in cuneiform → let it dry hard → dunk
> it in water to re-open → fire one in a firepit to lock it → the wax tablet as the reusable option)*
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

### Notes for posting
- **Media:** lead with a Tablets life-cycle gif if one gets made; otherwise a cuneiform-tablet still +
  a firepit-firing still. Fill the `(video/gif: …)` placeholder after B.2.
- Tag/flair as **Mod Update**.
- Drop a short comment on the 0.2 thread pointing here.
- Mod DB link inline: https://mods.vintagestory.at/scribe

---

## 2. Mod DB — release notes / changelog blurb

**Goal:** short, scannable release note for the mod-DB version entry (the mod page proper is the
`mod-page.*` drafts). Put the BREAKING gesture line first — mod-DB readers are often existing users
clicking "update."

> **Scribe 1.0.0 — Clay & Wax Tablets (first "complete" cut)**
>
> **⚠️ Breaking gesture change:** **Shift + right-click now quick-adds a task** (Lectern, Notebook,
> wet Tablet) instead of opening the plain editor. To place a held Notebook/Tablet on the ground, use
> **Ctrl + Shift + right-click** (vanilla spear convention). Shift + right-click on water still
> softens a hardened clay tablet.
>
> **New — the Tablet tier:**
> - **Clay & Wax Tablets**, the earliest handheld writing surface (10 tasks / 1 pin). A wet clay
>   tablet is editable; it dries hard over ~2 in-game days (locking the text), can be re-wet in water
>   to revise, or fired in a firepit to make it permanent. The **Wax Tablet** is reusable and never
>   dries or fires.
> - **Cuneiform** script by default, with a plain-text toggle in Scribe Settings.
> - Clay in **red, blue, and fire-clay** colours.
> - In-game error notices for tablet-full and editor-locked cases.
>
> Requires **LibGUI (gui) 3.1.0** on both client and server. Existing worlds load clean (additive).
> Full notes: see CHANGELOG.md in the repo.

---

## 3. VS Discord — #mods-releases (short post)

**Goal:** one compact message. Discord truncates hard, so lead with the hook, one line on the
breaking change, one link.

> **Scribe 1.0.0 is out** 📜 — the note-taking mod's first "complete" cut. New this release: **clay
> & wax Tablets** you can craft in the stone age — scratch a quick list in **cuneiform**, let it dry
> hard, then re-wet it to revise or **fire it in a firepit to keep it forever**. Wax tablets are the
> reusable option.
>
> ⚠️ **Heads-up for existing users:** the right-click gesture changed — **Shift + right-click now
> quick-adds a task**; use **Ctrl + Shift + right-click** to place a held Notebook/Tablet on the
> ground.
>
> Still needs LibGUI (gui 3.1.0), both sides. Worlds load clean.
> → https://mods.vintagestory.at/scribe

---

## 4. Wiki — front-page / release banner note

**Goal:** the wiki is the source of truth and gets published first. This is the short "what changed"
banner for `Home.md` (the full Tablets page is `Clay-and-Wax-Tablets.md`, already written in C.3).

> **Scribe 1.0.0** adds the **Clay & Wax Tablets** tier — the earliest handheld writing surface,
> craftable in the stone age. See **[Clay & Wax Tablets](Clay-and-Wax-Tablets)** for the full
> wet → hard → re-wet-or-fire life-cycle, the wax option, and the cuneiform toggle.
>
> **Gesture change (from 0.2):** **Shift + right-click** now **quick-adds** a task on the Lectern,
> Notebook, and wet Tablets. To place a held Notebook or Tablet on the ground, use
> **Ctrl + Shift + right-click**. Shift + right-click aimed at water still softens a hardened clay
> tablet.

---

## FAQ seed — "where did my ground-place go?" (for G.4)

Short answer to pre-seed on the mod-DB comments / Discord for the most likely confusion:

> **Q: Shift + right-click used to place my notebook on the ground / open the editor — now it adds a
> task. How do I place it?**
> A: The gesture moved to **Ctrl + Shift + right-click** (the same modifier vanilla uses for placing
> a spear). Plain **Shift + right-click** now quick-adds a task to whatever you're holding or looking
> at (Lectern, Notebook, wet Tablet). This was a deliberate 1.0 change to make the fast "jot one
> task" gesture the default. Shift + right-click on water still softens a hardened clay tablet.
