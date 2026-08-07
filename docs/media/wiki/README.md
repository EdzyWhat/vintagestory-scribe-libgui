# Scribe wiki drafts (1.0.0)

In-repo working copies of the GitHub wiki pages, refreshed for the **1.0.0** release. These are
authored here and published to the wiki manually (`../vintagestory-scribe-libgui.wiki`), matching
the launch-material convention in `docs/media/` (design decision 8).

## What changed for 1.0.0

- Added the **Tablets** tier:
  - **Clay & Wax Tablets** (`Clay-and-Wax-Tablets.md`) — the new early-game handheld tier: the
    wet → hardened → fired clay life-cycle, water re-wetting, the never-drying Wax Tablet, the
    10-task/1-pin limits, the Shift+right-click quick-add gesture, and the cuneiform script toggle.
  - **Items** (`Items.md`) — added a Tablets section.
  - **Crafting** (`Crafting-the-Lectern.md`) — added the clay and wax Tablet grid recipes.
  - **Home** (`Home.md`) — added the Tablets nav link and intro mention; roadmap updated (Tablets
    shipped as **v1.0**, Writing Desk moved to **v1.1**).

## Publishing checklist

1. Copy each page here over the same-named file in the wiki clone.
2. Add the new **Clay & Wax Tablets** page to the wiki sidebar / `Home` page nav (already linked in `Home.md`).
3. Verify image links still resolve (imgur URLs are carried over unchanged; 1.0 screenshots go in
   `docs/media/screenshots/1.0/` and can be swapped in once uploaded). The Tablets page currently
   has no images — add tablet/cuneiform/firing screenshots once captured (Track B.2).
