# Changelog

All notable changes to Scribe are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.4.0-rc.1] - 2026-09-01

Early release candidate — includes the new Assignment Desk/Inbox and Quest Links features below
alongside the Linux HarfBuzz durability fix, ahead of the next real release.

### Added
- **Assignment Desk and Inbox.** A new Assignment Desk block lets you write a task and send it
  directly to another player; a new standalone Inbox block (plus an Inbox tab reachable from the
  Lectern, Scriptorium, and Chalkboard) is where you receive one. Accept, Decline, or Cancel before
  it's accepted; once accepted it's yours to check off (which completes the assignment
  automatically) or Discard. An unseen assignment glows softly on the nearby block until you open
  its Inbox. **Not yet obtainable via survival crafting in this build** — the grid recipes are
  pending final art/balancing; use creative mode or `/giveblock` to place one for testing.
- **Quest Links**, for players who also have an optional supported quest mod installed (currently
  VS Quest): add a Quest Link from the footer's New Task menu to keep one of that mod's quests
  listed next to your other goals. Scribe also notices when you accept or complete a quest near its
  giver and always says so in chat; a new Quest Accept Policy and Quest Completion Policy in
  Settings (Always/Never/Prompt, default Prompt) choose what happens next — automatically link/mark
  it done, do nothing, or ask via a small HUD banner. While a quest's own window is open, its Quest
  Link row shows live kill/place/break progress underneath it.

### Fixed
- **Linux/glibc HarfBuzz crash, hardened further.** The font-shaping crash affecting some Linux
  desktops (bundled HarfBuzzSharp colliding with a system `libharfbuzz` already resident in the
  process) is now isolated via a Harmony patch on `gui`'s own native-library registration, replacing
  the previous startup-order-dependent `dlopen` race. This mechanism doesn't depend on mod load
  order, matches an independently-shipped community fix (Seralth's `harfbuzzfix`) validated against
  the same root cause, and fails closed (falls back to today's behavior) if it can't apply. **This
  release candidate is specifically to gather confirmation from Linux users** that the fix holds
  across different desktop/toolkit environments (KDE, GTK, Qt) — reports welcome via Mod DB
  comments, Discord, or GitHub.

## [1.3.3] - 2026-08-30

### Added
- **Pin Insert.** A Settings dropdown (Bottom by default, or Top) chooses where a newly pinned
  task lands in your pin list — separate from New Task Insert. A subtask pinned under an
  already-pinned parent still attaches directly beneath it either way.
- Existing rows now slide smoothly to their new position instead of jumping instantly whenever the
  list around them changes — a row inserted above them (e.g. New Task Insert / Pin Insert set to
  Top), a row removed elsewhere, or a completion policy reordering a pin all animate the same way.
  Applies everywhere Scribe animates rows today (editor, Read view, Pin Tab, HUD) and to any future
  surface built the same way, with no extra work required.
- Notebooks/Tablets stored inside a container the player is currently carrying via the CarryOn mod
  (e.g. a chest carried on the back) now also record Death, PvP Kill, and Temporal Storm history,
  when CarryOn is installed. No effect if CarryOn isn't installed.
- A Notebook's History tab now has an "Add Entry" button for writing your own custom entries
  (up to 1,000 characters) alongside the automatically recorded events. Only the player who wrote
  an entry can edit or delete it; an empty entry left untyped is discarded rather than saved. A
  Notebook keeps up to 30 custom entries (oldest dropped first).
- The Temporal Storm history cap is raised from 5 to 10 entries per notebook.

### Fixed
- Pinning or unpinning a task while the game is paused (e.g. singleplayer auto-pause while the
  Handbook is open) now updates the HUD and Pin Tab immediately, instead of only after unpausing.
- Drag-reordering a task or pinned task now only allows dropping onto a target at the same
  indentation depth — dropping a subtask among top-level tasks (or vice versa) is rejected, and the
  drop-target arrow only appears over a valid same-depth row. Dragging a parent task (or pinned
  parent) with subtasks/pinned children now always moves the whole group together, including when
  dragged forward past another parent's group.
- Notebook/Tablet Death, PvP Kill, and Temporal Storm history now records correctly when the
  notebook is carried in an inventory added by another mod (e.g. a bonus storage slot granted by a
  skill/ability mod) — previously such inventories were invisible to history recording because they
  weren't on a hardcoded allow-list. A Notebook sitting in the crafting grid now also counts as
  carried, so it participates in this recording too (previously excluded).

### Changed
- Reordered and regrouped the Scribe Settings menu for better scanability: New Task Insert/Pin
  Insert and Counter Completion Behavior/Subtask Behavior now sit in side-by-side column pairs;
  Font moved to the top of Window Appearance; Pixel-Art Display and Cuneiform Tablets now share a
  row at the bottom of Window Appearance, with Cuneiform Press-In alone below them. Also renamed
  "Item Tracker Completion Behavior" to "Counter Completion Behavior", "Task text font" to "Font",
  and "HUD task width" to "HUD width".

## [1.3.2] - 2026-08-28

Crafting Tasks keep their ingredient lists together, new rows land at the top by default,
and switching task fonts no longer jumps the list. Fully save-compatible with 1.3.0 —
no codec change.

### Added
- **Subtask Behavior.** A Settings dropdown (Bound by default, Independent, Discard children)
  decides what happens to rows nested under a parent when you complete, sink, delete, or trash
  it. Bound keeps the group together; Independent changes only the parent; Discard removes the
  children first.
- **Pin notes.** Text rows can be pinned. The HUD shows them as text only (no checkbox); unpin
  from the Pin Tab.
- **HUD settings gear toggle.** Hide the HUD gear from Settings; the Lectern Settings tab stays.
- **New Task Insert.** A Settings dropdown (Top by default, or Bottom) chooses where Add,
  Shift+right-click, and Handbook Add to Scribe put a new row. Enter in the editor still inserts
  under the row you are typing in.

### Changed
- **Crafting Tasks no longer recreate deleted ingredients.** Opening the editor does not heal
  missing children. Bumping the parent target rescales remaining matches only. Handbook create
  still expands the recipe once.
- **Tool and tag-only ingredients are skipped** (axes, `!Consume`, default `*:*`). A debarked
  oak log's Crafting Task is the parent plus the oak log — no catch-all child.
- **Pinning a child inserts it under its pinned parent** instead of appending; pinning the
  parent gathers already-pinned children.
- **HUD Crafting Tasks show have/need.** Title is **Scribe Pins**, sized with the row font. Max
  HUD rows goes to 30.
- **Handbook "Add to Scribe"** reads Link to this page → Count this item → Add ingredients.
  The editor Add ▾ labels are unchanged.
- **Grip tap vs drag.** Nesting is tap-only; a drag starts after the pointer moves, and
  releasing a drag (even on the same row) does not nest.
- **Task fonts share Caudex's line height.** Switching Task Text Font no longer grows or
  shrinks single-line rows on Read or Edit. Titles, buttons, the HUD, Settings chrome, and
  cuneiform tablets are unchanged.
- **Settings layout.** Timer prefs sit in Mod Behavior (not a Clockmaker-only section).
  Cuneiform tablets and press-in sit in Window Appearance. HUD collapse and storm
  corruption sit in HUD Appearance. The HUD-gear settings window is 480×620.

### Fixed
- **Tracker counts while paused.** Inventory clicks with a Scribe dialog open no longer call
  `RegisterCallback` in a way that throws in developer mode. Counts recompute immediately while
  paused; delayed coalescing uses `permittedWhilePaused: true`.

The Windows/Optimum `c00000fd` silent crash reproduced with Scribe disabled; this cut has no
remaining diagnostic probes.

## [1.3.1] - 2026-08-23

A bugfix for clay tablets that were already wet.

### Fixed
- **Dunking a wet clay tablet in water now resets its drying timer.** In 1.3.0, water only
  restarted the clock when a *hard* tablet softened back to wet. Dropping a still-wet tablet
  into water, swimming with one in hand, or quenching it against a water container left the
  existing harden timer running — so a half-dry tablet could still lock on the original
  schedule. Those three water paths now restart the ~2-day drying clock. Fired tablets are
  unchanged.

Fully save-compatible with 1.3.0 — no codec change.

## [1.3.0] - 2026-08-21

Crafting Tasks and the wall-mounted Chalkboard. Open an item's Handbook page and add a
recipe-bound goal that builds its own ingredient shopping list; hang a short shared list
on a wall like a painting. Tablets are easier to read in every drying state. Fully
save-compatible with 1.0–1.2 worlds — existing documents open unchanged. New writes use
document codec v8 (`RecipeSignature` per block) and pin codec v5 (`Depth`); a 1.2 client
cannot read a 1.3 save. Vintage Story requires matching mod versions, so mixed-version
multiplayer is not a supported case.

### Added
- **Crafting Tasks.** From an item's Handbook page, **Add Crafting Task** binds a grid
  recipe: the row tracks the output like an Item Tracker and auto-builds one ingredient
  Item Tracker subtask per recipe input (liquids as litre trackers). Items with several
  recipe variants get a labeled link for each. Tap a row's drag grip (without holding to
  reorder) to indent it as a subtask.
- **Chalkboard.** A wall-mounted form-factor of the Lectern — the same shared document,
  Guest Book, and one-at-a-time editor lock, hung on a wall like a painting. Capped at
  10 tasks. Not a drawable board.
- **Meal-page Add to Scribe.** Cooked-meal Handbook pages (stews, pies) now carry an
  **Add Link** that creates a guide-page Link to that meal's recipe.
- **Transcribe stamp sound.** The Scriptorium's stamp flourish now thumps when it lands
  on copy, import, or export. Volume follows the existing Timer alarm slider.
- **Tablet readability.** Cuneiform ink, glow, and stroke weight now follow each clay
  colour and drying state (wet / hard / fired), so fired and hardened tablets stay
  legible. Long titles wrap on every surface, including the tablet title band.
- **Tablet row links.** Link rows on a tablet render as tappable cuneiform names, not
  just on Lecterns and Notebooks.

### Changed
- **Handbook uniqueness-first.** Per-object entries (Lectern, Scriptorium, Chalkboard,
  Notebook, Tablet) describe what makes that surface unique and link out to shared
  guides (Getting Started, Tabs & Views, Editor Reference) instead of repeating the
  same tab tour. Getting Started, the task-types explainer, and the editor reference
  now name Crafting Tasks and the Chalkboard.

### Fixed
- **Recipe variant identity.** Crafting Tasks for attribute-encoded outputs (Hunter's
  Backpack, metal lanterns, whole-code wildcards) now bind the variant whose Handbook
  page you opened, not whichever recipe came first in the registry.
- **Craft-subtask live rescale.** Changing a Crafting Task's target count updates its
  ingredient subtask quantities immediately.

## [1.2.1] - 2026-08-18

### Added
- **Clockmaker Notebook alarm sound.** When the timer fires, a gentle mechanical alarm bell
  (CC BY 4.0, credited in CREDITS.txt) now plays. The sound ramps up over half a second
  (easeInCubic), breathes softly at ±10% volume on a 3-second cycle, then fades out with a
  smooth easeInOutSine when you dismiss. Pauses automatically if you pause the game. A new
  **Alarm Volume** setting (0–100, default 65) in the **Timer** section of Scribe Settings lets
  you tune it live — changes take effect immediately while the alarm is playing.

### Changed
- **Item Tracker** — the task type previously called "Tracker" is now labelled "Item Tracker"
  everywhere it appears in the UI and Handbook, to make its purpose clearer at a glance.
- **Illumination curve tuned.** The lighting response curve used to shade the Scribe GUI has
  been adjusted: the floor (darkness minimum) is 5% instead of 3%, the mid point shifts
  slightly brighter (45% input → 53% output), and full brightness is reached at 90% of the
  light range rather than the very top, giving a more comfortable read in most interiors.
- **Timer** section in Scribe Settings now groups **Alarm Volume** and **Timer Disappears**
  together; both were previously scattered or absent.

### Fixed
- Alarm sound now pauses correctly when the game is paused in single-player (ESC or Handbook).

## [1.2.0] - 2026-08-17

The organization release. Scribe grows from a personal notebook into a shared planning station:
a new craftable **Scriptorium** block, two new task types — **Trackers** that count items you are
carrying and **Links** that jump to a Handbook page — and a **Transcribe** desk that copies
documents between items and round-trips them in and out of the game as JSON or a
spreadsheet-friendly table. Fully save-compatible with 1.0.x and 1.1.x — existing Lecterns,
Notebooks, and Tablets open unchanged.

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
- **Scriptorium block.** A new craftable, placeable **Scriptorium** — a dedicated shared writing
  station alongside the Lectern, with its own desk model (a long desk with an open reading board, a
  book stack, and an ink & quill). It hosts a full Scribe document with the same Read / Task Editor /
  Pinned / Guest Book / Settings views, plus a **Transcribe** desk (below), a one-editor-at-a-time
  lock, server-authoritative persistence and sync, floor placement that faces the player, and document
  carry-over when broken and re-placed. Crafted like a more-expensive Lectern — the same feather,
  parchment, leather, and ink-filled bowl, with double the planks.
  - *Note:* the open dialog's page backdrop still borrows the Lectern's for now; a dedicated one is a
    tracked follow-up that will swap in without a save-format change.
- **Transcribe desk — copy, import, and export documents.** The Scriptorium's **Transcribe** tab
  moves whole documents around. Drop a Scribe item in and copy its tasks onto another item —
  **overwrite** the target or **append** onto it — with the result stamped like sealed paper. The same
  tab round-trips a document in and out of the game as text over the clipboard: **Copy as JSON** for a
  complete, human-readable snapshot, or **Copy as TSV** for a spreadsheet-friendly table
  (`Type · Done · Text · Special · Count · Depth`) you can bulk-edit in Excel or Google Sheets and
  paste back. **Import** auto-detects JSON or TSV from the clipboard and writes it onto the slotted
  item. Unknown item or link references land as plain tasks rather than failing the whole import, and
  imported tasks are never pinned — an import brings the words, not anyone's HUD state. On a shared
  Scriptorium, the "stamped" flourish now also plays for anyone else watching the same block, not just
  the player who pressed the button.

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

[1.3.2]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.2.0...v1.0.0
[0.2.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/releases/tag/v0.1.0
