# Changelog

All notable changes to Scribe are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Two new task types: Trackers and Links.** Alongside standard tasks and plain notes, the add
  menu can now create a **Tracker** — a task tied to an item that counts how many of that item you
  are carrying toward a target you set (e.g. "gather 32 flax"), showing the item's icon and a live
  `have/need` counter that updates as you pick things up or drop them. Only what you are *carrying*
  counts (hotbar + backpacks); items sitting in a chest do not. Set the target with the inline
  up/down stepper on the row. A **Link** is a task that points at a Handbook page: clicking it —
  in a Scribe surface or as a pinned task on the HUD — jumps straight to that item's Handbook entry,
  without ever changing the task's completion.
- **"Add to Scribe" from the Handbook.** Every item's Handbook page now carries an **Add to Scribe**
  link at the bottom that creates a Tracker or a Link for that item in one click — into whichever
  Scribe surface you have open, or the Scribe item you are carrying if none is open. There is also a
  new Handbook entry explaining the Tracker and Link task types.
- **Tracker completion setting.** A new per-player setting controls what happens when a Tracker
  reaches its target — **complete** it (the default), **delete** it, or **do nothing** — so a met
  gathering goal can tick itself off, tidy itself away, or simply sit satisfied, to taste.
- **Scriptorium block (foundation).** A new craftable, placeable **Scriptorium** — a dedicated
  shared writing station alongside the Lectern. It hosts a full Scribe document with the same Read /
  Task Editor / Pinned / Guest Book / Settings views, a one-editor-at-a-time lock, server-authoritative
  persistence and sync, floor placement that faces the player, and document carry-over when broken and
  re-placed. Crafted plank-heavy (planks + feather + nails, ink-filled). This lands the v1.2 organization
  tier's block foundation; the richer collaborative features (Tracker/Link task types, copy/paste,
  import/export, and the v1.3 assignment system) follow as separate changes.
  - *Provisional art:* the Scriptorium currently reuses the Lectern's model, textures, and page
    backdrop as a stand-in — a placed Scriptorium looks like a Lectern for now. The dedicated model
    and backdrop are a tracked follow-up and will swap in without a save-format change.

## [1.1.1] - 2026-08-14

### Added
- Brazilian Portuguese (pt-br) translation, contributed by **Arquimago**.

## [1.1.0] - 2026-08-14

An interim polish release. The headline is that an open Scribe page now responds to the real
light around you instead of glowing in the dark; alongside it are plain notes (not just tasks),
shelf placement for Notebooks and Tablets, typed arrows, smooth list animations, and a batch of
editor and HUD fixes. Fully save-compatible with 1.0.x — existing Lecterns, Notebooks, and
Tablets open unchanged.

### Added
- **Scribe pages now dim and warm to the light around you.** Instead of glowing at full brightness
  in a pitch-black cave, an open Lectern, Notebook, or Tablet is now shaded by the actual light
  reaching you — bright and neutral in daylight, warm and dim under a torch, and nearly unreadable
  in true darkness — so carrying a light source finally matters when you want to read your notes. A
  held torch, lantern, or oil lamp lights the page in its own colour, and the shading eases smoothly
  as you move between light and shadow rather than snapping. A **minimum-brightness** floor in
  Scribe's client config sets how dark the page can get in total darkness; raise it all the way to
  `1.0` to keep the old always-bright look. Because Scribe reads the game's real light every frame,
  lighting mods such as WarmerLighting and Immersive Light are respected automatically.
- **Add plain notes, not just tasks.** The editor's **Add task** button is now an add menu — click
  the caret beside it to choose between a **Standard Task** (a checkbox item, as before) and a
  **Note**: freeform text with no checkbox and no completion state, for jotting things that aren't
  to-dos. An abandoned empty note tidies itself away just like an empty task. Notes hold up to
  10,000 characters (tasks stay at 1,000), and if you reach either limit the editor now tells you so
  instead of silently dropping the extra text.
- **Store Notebooks and Tablets on shelves, bookshelves, and cabinets.** Notebooks, Clockmaker's
  Notebooks, and every clay and wax Tablet can now be placed on the same furniture that holds
  vanilla books — general shelves, bookshelves, and display cabinets — instead of only dropping into
  ground-storage piles. A shelved document keeps everything written in it, so putting it away and
  taking it back down reopens your notes intact.
- **Typed arrows.** In any Scribe editor (Lectern, Notebook, Clockmaker's Notebook, and a wet Tablet),
  typing `->` now becomes `→` and `<-` becomes `←` as you type — in both task and note text. The real
  Unicode arrow is stored, so it copies, searches, and reopens as an arrow. Substitution fires only as
  you complete the digraph on the keyboard; pasting `->` leaves it literal. If your chosen task font
  has no arrow glyph, the editor now falls back to a bundled font that does, so the arrow always shows
  instead of a missing-glyph box.
- **Cuneiform now writes arrows and comparison signs.** Tablet (cuneiform) text can now render `←`, `→`,
  `<`, and `>` as authored glyphs, so the typed arrows above — and comparisons like `2 < 3` — render on
  a tablet instead of falling back to a blank gap.
- **A clockwork gear-train now turns on the Clockmaker's Notebook Timer tab.** The Timer page shows
  an ambient mechanism — a central teal temporal gear driving two steel cogs and a large escape
  wheel — that ticks one tooth per real second with a spring-wound snap. It runs whenever the tab is
  open, regardless of whether a timer is set. Starting a timer slides the escape wheel into place;
  when the timer fires the mechanism shudders and locks, and the wheel retracts. A faint tick-tock
  plays on the Effect volume channel while the tab is open (silenced by the "Mute Scribe UI sounds"
  setting). Purely decorative — it never changes timer behavior or intercepts the controls.
- **Task and pin lists now animate smoothly as rows come and go.** Adding a task or note, or pinning
  one, animates the new row sliding into place and nudging its neighbours aside to make room — across
  the Lectern, Notebook, and Tablet editors, the Pinned tab, and the pinned-task HUD alike. Removing
  a row collapses it and slides the rest up (the Pinned tab, which used to snap, now does this too). A
  row you create and immediately start typing into fades in at full size so your caret never jumps.
- **Held-light flicker now carries onto the page (with Immersive Lanterns).** If you run the
  Immersive Lanterns mod, the flicker it gives a held torch, lantern, or candle now shows on the
  Scribe page as well, so the page flickers in time with the light in your hand instead of holding a
  dead-steady glow. Without Immersive Lanterns installed, nothing changes. Builds on the light-aware
  shading above.
- **Jump to the first or last row of a note.** In any Scribe editor, **Cmd+Up/Down** on macOS or
  **Ctrl+Up/Down** on Windows now moves straight to the first row (caret at its start) or the last row
  (caret at its end), instead of nudging the caret one line. **Ctrl+Home / Ctrl+End** do the same, for
  the standard Windows document-navigation muscle memory. Plain Up/Down still moves one visual line,
  and Home/End still jump to the start/end of the current line.

### Fixed
- **Completing a task on the HUD now updates an open Pinned tab or editor immediately.** When a
  Notebook or Lectern's **Pinned tab** or **editor** was open and you completed one of those tasks
  from the HUD under a policy that keeps it in the list (**Keep** or **Sink to bottom**), the open
  view's checkbox didn't reflect the completion until you reopened it — and in the editor's case the
  next auto-save could even *undo* the completion, because the editor was still holding the
  pre-completion copy. Both views now update live (the sunk task moves to the bottom in the editor
  too, matching the read view), the completion is no longer reverted by a later save, and your caret
  and in-progress text in another row are left undisturbed. This matches the reverse direction, which
  already worked. The **Unpin** and **Delete** policies were unaffected, since there the row leaves
  the list entirely.
- **HUD pinned tasks no longer lose their text when you cancel a completion at the last moment.**
  Checking a pinned task on the HUD fades its text out over a short undo window; if you *un*-checked
  it right as the text finished fading, the row could come back as a bare checkbox with no text until
  the HUD next rebuilt. The task itself was never lost — only its on-screen label — but it looked
  alarming. The fade now clears cleanly on undo, so the text always reappears. (This was a rare,
  long-standing glitch that became easy to trigger after recent HUD-rendering work; both are fixed.)
- **The pinned-task HUD and the Pinned tab now show your pins in the same order.** The two views
  could drift apart — pin across several documents, or play across sessions, and a pin might sit in a
  different spot on the HUD than in the Pinned tab. They now render one shared, per-player order, so
  they always agree. As part of this, un-completing a task you'd sunk to the bottom now returns it to
  its previous position instead of leaving it stranded at the end of the list.
- **Editing a long task or note no longer makes the view jump around.** When a row grew taller than
  the editor's visible area, every keystroke used to bounce the scroll between the top and bottom of
  that row, making it almost impossible to edit. The editor now follows your caret — scrolling only
  when the caret would leave the view, and then only far enough to bring it back — so long rows edit
  smoothly. Arrow keys and Tab navigation follow the caret the same way.
- **Deleting several rows in a row no longer needs a mouse wiggle between each.** Removing a task,
  note, or pin slides the row below up to fill the gap — but that row's delete and pin buttons (which
  only appear on hover) used to stay hidden until you physically nudged the mouse, so clearing a list
  meant delete, wiggle, delete, wiggle. The row that slides under your cursor now shows its buttons
  immediately, so you can delete straight down a list.

## [1.0.1] - 2026-08-07

A small display fix for shelved Scribe items.

### Fixed
- **Scribe items no longer clip through the cabinet's bottom shelf.** The clay & wax Tablets, the
  Notebook, and the Clockmaker's Notebook had their in-cabinet display transform hand-tuned (scale
  and vertical offset) so they sit cleanly on a shelf instead of poking through the shelf above.
  Display-only — no change to items already stored, and shelf/bookshelf placement is unchanged.

## [1.0.0] - 2026-08-06

Scribe's first "complete" cut: the new **Tablets** tier fills in the earliest end of the
progression, giving a full handheld → carried → placed writing system. This release also changes
the right-click modifier gestures — **see the BREAKING note under Changed.**

### Added
- **Clay & Wax Tablets — a new early-game writing tier.** A cheap handheld tablet you can craft
  long before a Notebook or Lectern, holding a short list (up to **10 tasks and 1 pinned task**) as
  a quick scratchpad. Grid-crafted from a knife, a stick, and clay (or a saw, planks, a stick, and
  beeswax for the wax variant) — no writing set or fired ink bowl required. Each has an in-game
  handbook entry.
- **Clay life-cycle: dry, re-wet, and fire.** A freshly crafted clay Tablet is **wet** and freely
  editable. Left alone it dries **hard** over about two in-game days, locking its writing. From
  there you can **dunk it in water** (drop it into a water block, or Shift+right-click aimed at
  water) to soften it back to a wet, editable tablet — resetting its drying timer — or **fire it in
  a fire pit**, like pottery, to make its writing **permanent** (water no longer softens it). Firing
  a blank tablet sets its empty surface in stone.
- **Clay-type tablets** — the Clay Tablet comes in three colours: **Red**, **Blue**, and **Fire
  Clay**. The colour is cosmetic (all three behave identically), each with its own crafting recipe
  and a distinct full-page dialog backdrop, and — once fired — a distinct fired-ceramic appearance.
- **Wax Tablet** — a reusable step up from clay: a wooden frame filled with beeswax that **never
  dries or fires**, so it can always be rewritten. Same 10-task / 1-pin limits as the clay Tablet.
- **Cuneiform script** — Tablet text is written in a bespoke carved-wedge **cuneiform** font by
  default, in keeping with the tier's ancient-writing theme. Prefer plain text? Turn off **Cuneiform
  tablets** in Scribe Settings to render Tablet text in your selected task font instead.
- **Quick-add task (Shift+right-click)** — Shift+right-clicking any Scribe surface (Lectern,
  Notebook, Clockmaker's Notebook, or a wet Tablet) now opens its editor with a fresh empty task
  already added at the **top** of the list and the caret focused, so you can capture a task in one
  gesture without scrolling to an "Add task" button. A quick-add at a full tablet (10-task cap) opens
  the editor and shows the "tablet is full" notice without adding a row. On a hard or fired (read-only)
  tablet it simply opens the tablet — nothing can be added there.
- **In-game error notices** — a refused edit now tells you *why* through the vanilla client error
  channel instead of failing silently: e.g. a tablet at its 10-task cap shows "A tablet holds at most
  10 tasks", a locked (hardened/fired) tablet explains it can't be changed, and a held Lectern edit
  lock surfaces the reason.
- **Clockmaker's Notebook Schematic** — a reusable blueprint item sold occasionally by wandering
  **Commodities** and **Treasure Hunter** traders. Added to the crafting grid alongside the usual
  Notebook + temporal gear + metal parts, it lets **any** player craft a Clockmaker's Notebook
  without the Clockmaker class or Tinkerer trait. The schematic is not consumed, so one purchase
  lasts forever. The existing trait-gated recipe and the `scribeClockmakerRequiresTrait` world
  setting are unchanged — this is an additional path, not a replacement.

### Changed
- **BREAKING — right-click modifier gestures changed.** With quick-add taking Shift+right-click, two
  older gestures move:
  - **Lectern:** Shift+right-click no longer opens the plain editor view — it now quick-adds. To open
    the editor without adding a task, right-click to Read, then use the **Editor** nav tab.
  - **Notebook, Clockmaker's Notebook, and Tablet:** placing the held item on the ground now requires
    **Ctrl+Shift+right-click** (following the vanilla spear convention), since plain Shift+right-click
    now quick-adds. The Tablet's water-soften gesture is unchanged — Shift+right-click **aimed at
    water** still softens a hard tablet; only Shift+right-click *not* aimed at water changed (it now
    quick-adds instead of placing on the ground).
- The editor footer's Information (ⓘ) button now **toggles** the "Scribe Editor Features" handbook
  page: clicking it opens the reference when the handbook is closed and closes the handbook when it
  is already open (if the handbook is open on another page, the first click navigates there and the
  next closes it). Its tooltip now reads "Show / hide Editor Features". Shared across the Lectern,
  both Notebooks, and the tablet editors.

### Fixed
- The **first** right-click on a not-yet-crafted Scribe item (Notebook, Clockmaker's Notebook, or
  Tablet) no longer flickers its dialog closed, requiring a second click to open. The one-time server
  "picked up" history sync was being misread as a switch to a different item and closing the
  just-opened dialog.
- Stepping a Scribe Settings numeric field with the Up/Down arrow keys now works on **every**
  consecutive press instead of only the first. With a document editor (Lectern, Notebook, or Tablet)
  also open, the arrow keys previously leaked to the editor's last-touched row after the first step —
  driving that row's text caret instead of the numeric field — because the open editor kept capturing
  keyboard input even when the settings window was the active dialog. The editor now only captures
  input while it is the focused dialog.

## [0.2.0] - 2026-08-01

### Added
- **Notebook** — a carried, personal document with the same task checklist and freeform
  notes as the Lectern, but in your inventory instead of on a block. No editor lock (a held
  stack has one holder). Now craftable in survival from a paper + leather writing set (feather,
  parchment, leather, nails, and a fired bowl of 1 L black dye) on a 3×2 grid.
- **Clockmaker's Notebook** — the Notebook's advanced sibling, adding a **Timer** tab for
  real-time and in-game-time countdowns with an optional label. A running timer shows on the
  Pinned Task HUD and blinks when it fires. Crafted from a Notebook + a temporal gear + metal
  parts.
- **Notebook History** — an append-only chronicle recorded automatically while you carry a
  Notebook: crafted, picked up, deaths, PvP kills, boss kills, and temporal storms, each stamped
  with the in-game date. High-frequency kinds roll off past a per-kind cap.
- **Guestbook** — a Lectern tab that logs visitors automatically: each player who opens the
  Lectern gets a dated entry, with room for a short personal note they can edit per day. Ideal
  for a shared base or trader stall.
- **Temporal-storm HUD effect** — during temporal instability the Pinned Task HUD text briefly
  corrupts and the document title flickers, as a flavor tie-in to the storm. Toggleable in
  settings.
- **Document title on tooltips** — a Lectern block, and a Notebook or Clockmaker's Notebook
  item, now shows its document title in the hover tooltip, so you can tell your notes apart
  without opening them.
- **Up/Down caret navigation** — the arrow keys now move the text caret between visual lines in
  the multi-line editor and pinned rows (to the line's start/end at the first/last line), with
  Shift extending the selection.
- **In-game handbook entries** for the Notebook and Clockmaker's Notebook, plus refreshed
  Lectern sections and guide pages so the mod's handbook reads coherently as a whole.
- **Clockmaker's Notebook craft gate** — the recipe requires the vanilla `tinkerer` trait
  (granted by the Clockmaker class). Server operators can lift it world-wide with the
  `scribeClockmakerRequiresTrait` world setting (Customize screen or `/worldconfig`).
- **`/scribe seed` dev command** (creative + `controlserver` only) — seeds believable demo
  content (tasks, notes, History on a Notebook, Guestbook on a Lectern) for screenshot/video
  capture, through the normal server-authoritative flow.

### Changed
- Refreshed the Notebook, Clockmaker's Notebook, and Lectern art, and gave each dialog its own
  distinct GUI backdrop.
- **Tab / Shift+Tab** traversal in editable views now visits only the rows' text fields,
  skipping the completion checkboxes (still clickable by mouse).

### Fixed
- A held **Clockmaker's Notebook** now behaves like the plain Notebook everywhere it previously
  didn't: live History events record into it, closing its dialog persists task/note edits (was
  silently dropped), pin/edit routing resolves it, and switching hotbar slots no longer
  force-closes its open dialog. The inventory detection matched only `ItemScribeNotebook` and
  silently excluded its sibling class.
- Corrected the Clockmaker's Notebook recipe, whose non-existent `game:metalparts-*` item
  wildcard crashed the handbook's "Created by" page on open.
- A newly placed Lectern would refuse to open because its host registry was keyed under a stale
  document id; the key now tracks the placed block.
- Various Guestbook fixes: editing a note no longer blocks player movement, long notes wrap
  correctly, and the tooltip is no longer double-prefixed with the mod domain.

## [0.1.2] - 2026-07-28

### Changed
- Updated LibGUI dependency from v2.0.0 to v3.1.0.

## [0.1.1] - 2026-07-28

### Added
- Mod icon (`modicon.png`) — shown in the in-game mod manager.

### Fixed
- In-game handbook: Lectern extra sections and guide pages now resolve lang keys correctly (added `scribe:` domain prefix; switched `\n` to `<br>`).

## [0.1.0] - 2026-07-28

First public release.

### Added
- **Lectern block** — a craftable, placeable notebook you write on: a task
  checklist plus a freeform note section. Server-authoritative and
  multiplayer-safe (edits sync live; the editor is one-person-at-a-time while
  others read). Its document survives break and re-placement.
- **Survival grid recipe** — craft the Lectern from 4 planks, nails, parchment,
  a feather, plain leather, and a bowl holding 1 L of black dye.
- **Pinned-task HUD** — an always-on, in-world overlay of your pinned tasks,
  with a rebindable toggle hotkey (default **P**) and per-player completion
  policies (keep, sink, unpin, or delete on completing a task).
- **Pin Tab** — a Lectern view listing all your pins across every document, with
  editable rows, reordering, and the completion-policy picker.
- **Scribe Settings** — a settings window (from the Lectern gear or the HUD
  gear) for all display/behavior preferences: theme, window size and text
  scale, HUD anchor/rows/width/offsets and text size, completion policy, and a
  UI-sound mute toggle.
- **Font selector for task text** — choose the Lectern's task/note font from
  Scapholène, Caudex, La Belle Aurore, Noto Sans, Noto Serif, Playfair Display,
  or Cormorant Unicase (or the default). Buttons keep a fixed Caudex face.

### Dependencies
- `game` 1.22.0
- `gui` 2.0.0 (LibGUI)

[1.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.2.0...v1.0.0
[0.2.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/releases/tag/v0.1.0
