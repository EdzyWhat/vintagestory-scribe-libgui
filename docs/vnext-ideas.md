# Scribe — vNext idea backlog

A durable parking lot for post-1.0 feature ideas, captured before they're sketched in detail.
**Nothing here is a commitment.** When an item is ready to build, it graduates to an
`openspec-propose` (or folds into an existing `docs/specs/*` tier). This doc is the raw
brainstorm + triage layer *above* [`ROADMAP.md`](../ROADMAP.md); the roadmap stays the committed
tier map and the specs hold the "how."

**How to read the status tags:**
- 🆕 **new** — not yet represented anywhere in the roadmap/specs.
- 🗺️ **on roadmap** — already a planned tier or spec; this entry adds detail/nuance to fold in.
- ✅ **partly shipped** — related capability already ships; this extends it.
- ⚠️ **concern flagged** — the author has a stated reservation (scope, immersion, architecture);
  keep the concern attached to the idea so it isn't lost when we scope it.

Captured 2026-08-07. Author's own reasoning/concerns are preserved deliberately — they're the
hardest part to reconstruct later.

---

## 1. Item & block visual polish

### 1.1 Revamped Timer Page appearance — 🆕 new
A ticking **Temporal Gear** seen as if through glass embedded in the notebook page. Skeuomorphic
depth for the Clockmaker timer view.
- *Open Qs:* is the gear a live-animated render (spinning), a static art layer, or a shader-ish
  "glass" overlay? How does it read against the existing paper backdrop and the timer rows? Does
  it reuse the vanilla temporal-gear item texture (we already ship `reference-temporalgear.png`)?

### 1.2 Per-tab subtitle + distinct theming — 🆕 new
Each tab in the Notebook & Lectern gets a **subtitle** and a **more distinct appearance / colour
theme** — not just a different paper texture. Make tabs feel like separate places.
- *Open Qs:* which tabs (Tasks / Pins / Timer / Settings)? Is theming per-tab colour accents on
  chrome, or fuller backdrop swaps? Interaction with the existing per-material tablet backdrops.

### 1.3 Clay Tablet **block** variant — 🆕 new ⚠️ concern flagged
A wall-mountable / leaning block version of the Clay Tablet that's **still usable** (openable to
edit) while placed. Think: propped against a wall or hung like a small plaque.
- *Concern:* the tablet is currently a held item with a display transform on shelves/cabinets
  (see the 1.0.1 tuning). A true block variant is a new block entity + placement + interaction —
  meaningful scope. How does its data relate to the held item (same docId store? a block that
  *is* a placed tablet)?
- *Open Qs:* lean vs. wall-mount vs. both? Does placing consume the held tablet and vice-versa?

### 1.4 New Lectern model — more "Scribe" — 🆕 new (art-gated)
The lectern currently reuses the vanilla `lecturn-book-open` shape (plain wood). Make it visibly
*ours*: add an **ink & quill** model element on the lectern.
- *Note:* roadmap already lists "block shape refinement" as art-gated under lectern-gui-polish —
  this is the concrete direction for it.

### 1.5 Stylus widget on the Tablet — 🆕 new (art-gated)
A long pen-like **stylus** modelled attached to the side of each Tablet, reinforcing the
"write into clay" fantasy.
- *Note:* the v3 spec explicitly shipped "no offhand stylus" as a mechanic — this is a purely
  cosmetic model element, not a held offhand item, so it doesn't reopen that decision.

---

## 2. New task types — "New Task" dropdown

The unifying idea: the **New Task** action becomes a small dropdown offering several task
*kinds*. Standard task stays the default; the others are opt-in.

### 2.1 Dropdown itself — 🆕 new
Options: **Standard** · **Linked (Handbook)** · **Tracked (numeric goal)** · **Mapped**.
- *Open Qs:* does this live on the Lectern/Notebook only, or Tablets too (Tablets are the "quick
  scratchpad" tier — maybe standard-only there to keep them simple)? How does it interact with the
  Shift+RC quick-add gesture (which always makes a *standard* task at top)?

### 2.2 Tracked (numeric progression) task — 🆕 new
Track collecting items against a goal (e.g. "collect 32 reeds"). When editing a row, an extra
button (alongside pin & delete) spins out a **wizard/modal**: pick an item + a count, and the task
then tracks against the player's inventory, showing progress.
- *Open Qs:* how is the item picked (a creative-style item search? handbook picker?)? Does progress
  auto-complete the task at goal? Polling inventory (we already poll inventory for
  milestone-suggested tasks per the chronicle spec — reuse that mechanism). Client-local vs. synced.
- *Design overlap:* the chronicle spec's "milestone-suggested tasks (self-detected via inventory
  polling)" is the same primitive — build once, use for both.

### 2.3 Linked (Handbook) task — 🆕 new
A task that links to a handbook entry: in read view the player clicks the link and it **opens the
handbook** for that item. Possibly: type an item name and it auto-links, or a dedicated
"link to handbook entry" task kind.
- *Note:* roadmap's chronicle spec already reserves **"long-term Handbook bookmarking"** — this is
  a lighter, per-task cousin. Consider unifying the "open handbook at entry X" primitive.
- *Open Qs:* free-text auto-linking (fuzzy match a word → handbook page) is much harder than an
  explicit "pick a handbook entry" task kind. Prefer the explicit kind first.

### 2.4 Mapped task — 🆕 new ⚠️ concern flagged
Attach a task to a specific **map location** — it shows up on the world map, maybe as a custom
marker. Possibly accessed as a **side widget on the map view** that propagates back to the held
Notebook.
- *Concern:* map integration is a distinct UI surface (VS world map / waypoint system). Splits
  development away from the writing dialogs. Might be cleaner as "a waypoint that references a
  task" than "a task that owns a waypoint."
- *Open Qs:* does it create a real vanilla waypoint/marker? Two-way sync (edit on map ↔ edit in
  notebook)? Does this need the Desk, or work on a held Notebook?

---

## 3. Ambient / world-visible tasks

### 3.1 Hover-over-block task preview — 🆕 new ⚠️ concern flagged
Hovering a Scribe block (lectern? tablet block?) shows a **task preview** — maybe just a little
text, not the full UI.
- *Author's own tension (preserved):* "is this too disruptive? Maybe we don't need the full UI,
  just a little text. But that might then be disruptive to fully switch in, and it splits the
  development, and also distracts — hurting immersion. Lots to discuss."
- *Open Qs:* interaction-help text (the vanilla `GetPlacedBlockInfo` line) vs. a floating custom
  tooltip. Where's the line between "helpful glance" and "immersion-breaking HUD clutter"?

### 3.2 Bulletin Board / Chalkboard — world-visible text — 🗺️ on roadmap (v6) ⚠️ concern flagged
A new Scribe block whose text is **visible in the world without opening an interface** — direct
inspiration from the **Billposting** mod. Ideally you could check off a checkbox just by looking
at it. Envisioned as a **2×2 wall-mounted block** (like a big painting; Billposting's fist is
2×1) giving more visible space without opening the full dialog.
- *Roadmap status:* v6 "Bulletin board (social) + chalkboard" already exists as a planned tier
  (`docs/specs/v6-bulletin-board.md`), with decided lock-free last-write-wins + always-signed
  concurrency. **This entry adds the key new requirement:** in-world rendered text + in-world
  interaction (check a box without opening a dialog).
- *Author's framing (preserved):* "This would be an architectural shift, I have no idea how to do
  this. But it has been done!" Billposting is about a **town center**; "our version is more about
  community work to be done, but there's definitely overlap I want to be conscious of."
- *Open Qs:* how is text rendered onto a block face in-world (Billposting is the precedent to
  study)? How does a "look at the checkbox and toggle it" raycast interaction work? Fold these
  into the v6 spec rather than a fresh tier.

---

## 4. Editor UX fixes & affordances

### 4.1 Faster delete — fix the mouse-move-to-reveal bug — 🆕 new (bug-ish, high value)
When a row is deleted, the delete button doesn't reappear until the mouse **moves**, even though
the delete animation has already slid a *new* row under the cursor. Result: mass-deleting forces
a tiny mouse wiggle between every delete — a frustrating loop.
- *Note:* this is close to a bug/polish item, not a feature. Likely a hover-state / hit-test
  refresh issue after the row-list re-layout animation settles. Cross-reference the
  `[[forcerebuild-vs-reconciling-libgui]]` and `[[libgui-settling-loops-and-race-diagnosis]]`
  notes — the post-layout settling machinery is where the stale hover state lives.
- *Likely small + high-satisfaction — a good early standalone change.*

### 4.2 Undo — 🆕 new ⚠️ concern flagged (author leans "maybe not")
An undo button, maybe at the bottom of the Edit page. Would need to store the last X changes
temporarily and group them (e.g. capture a text segment when typing slows). Or scope it down to
just **undo of task deletion**.
- *Author's own reservation (preserved):* "This is a very out-of-Vintage-Story-feeling capability.
  Very age of computers. Maybe not appropriate, and a massive architectural hurdle."
- *Triage lean:* if pursued, undo-delete-only is dramatically cheaper than full text undo and
  covers the most painful loss. Full undo is likely out of scope / off-theme.

---

## 5. The Writing Desk (v1.1 tier) — 🗺️ on roadmap (v4)

The Desk is already the planned **v1.1** tier (`docs/specs/v4-writing-desk.md`): private
owner-gated block, consolidates notes, kanban tabs, within-document search, faction/shared
task-assignment. The ideas below expand or stress-test that scope.

### 5.1 Copy/paste between items — 🗺️ on roadmap (core Desk feature)
Take the full data of one Notebook (or other item) and copy it onto another. **Author calls this
the one the Desk "definitely should include."** Aligns with the roadmap's existing "copy/paste
export between Lectern/Notebook/Desk."

### 5.2 CSV / spreadsheet import-export — 🗺️ on roadmap ⚠️ concern flagged
Take tasks **out of and into** the game as CSV, pasteable directly into Excel / Google Docs; build
a list outside the game and bring it back.
- *Roadmap status:* the chronicle spec already lists "cross-world JSON export/import" as a meta
  workstream; CSV-for-spreadsheets is the user-facing framing of the same capability.
- *Concern (author):* part of the broader Desk immersion worry below.

### 5.3 Assignment / notifications — 🗺️ on roadmap (v4 faction feature)
If a player has visited the Desk, they can be **notified of tasks someone else assigned** them and
pick those into their pins/notebooks. Matches v4's "faction/shared task-assignment"; the
faction-backing choice is a deferred open decision (roadmap Open Decisions #1 — VS has a
first-party player-group system, so possibly no dependency).

### 5.4 Search — 🗺️ on roadmap (v4 within-doc search) ⚠️ concern flagged
Highlight/filter tasks. Could be Desk-exclusive or more general.
- *Author's own reservation (preserved):* "I haven't heard of anyone actually use the mod yet to
  know if they're going to have that many tasks that a mod would be useful." → **validate demand
  before building.** Roadmap already scopes within-doc search to the Desk and global
  cross-document search as a later separate change.

### 5.5 Custom renamable tabs / sections — 🆕 new ⚠️ concern flagged (author leans "bad")
Multiple renamable sections you can move tasks between.
- *Author's own verdict (preserved):* "this is something you'd want everywhere if you're a project
  manager, but seems pointless to everyone else. Might turn Vintage Story into a JOB. Bad."
- *Triage lean:* keep parked; the v4 kanban tabs (Active/Backlog/Completed) already cover grouping
  without turning the game into project-management software.

### 5.6 Desk crafting cost — 🆕 new
Maybe the Desk takes **iron nails** specifically — signals it's a "more serious use" block, gates
it behind the metal age.

### 5.7 Desk model — 🆕 new ⚠️ concern flagged (scope + wood-variant question)
Two directions: (a) a **small block that sits on top of** a player-built (chiselable) desk — lets
players customize the surrounding furniture; or (b) a **fully-modelled desk block** (wood, legs,
drawers).
- *Open Q (author):* how do we account for **different woods**? "We can look at mods that have
  multiple wood-grain models for examples." (This is a known VS pattern — texture-swap by wood
  type.)

### 5.8 Overarching Desk concern — ⚠️ concern flagged (the big one)
Author's own framing, preserved verbatim in spirit: *"The Desk's fantasy is really expansive, and
very developed — but it might not feel as grounded and real as the rest of Vintage Story, which is
the main thing that concerns me. That and the massive scope. Maybe it's an iterative thing..."*
- **Triage implication:** the Desk should ship as a **minimal v1.1 core** (place block + copy/paste
  + maybe kanban tabs) with the expansive features (CSV, assignment, search) as later increments —
  each earning its place against real player demand and the immersion bar.

---

## 6. Integrations & tech-risk items

### 6.1 Modular Backpacks integration — 🆕 new ⚠️ concern flagged
Let players attach their Notebook to a backpack. Modular Backpacks already supports attaching tools
that carry collected data, so there may be a hook.
- *Concern (author):* "Might be a mess."
- *Guardrail check:* this would be a **new mod dependency** (soft, presumably) — the CLAUDE.md
  guardrail requires asking before adding any dep. Investigation-only until then.

### 6.2 VTML support in tasks — 🆕 new ⚠️ concern flagged
Support [VTML](https://wiki.vintagestory.at/VTML) (Vintage Story's rich-text markup) in task text.
- *Author's own read (preserved):* "it might not be that crazy. Or maybe it's extremely crazy.
  Hopefully no one asks for this."
- *Triage lean:* investigation spike to gauge effort before committing; low priority unless
  requested.

---

## 7. Presentation / marketing

### 7.1 Remake the ModDB page with Photoshop explanatory art — 🆕 new
Richer mod-DB page: **callout graphics** (Photoshop) for the HUD and for crafting, not just prose +
screenshots.
- *Note:* the three parallel mod-page files (`docs/media/mod-page*.{txt,html}`) stay text; this is
  new **image assets** (annotated screenshots) to embed. Pairs naturally with a future release's
  screenshot pass.

---

## Cross-reference summary — idea → roadmap

| Idea | Roadmap home |
|------|--------------|
| Desk copy/paste, CSV, assignment, search (5.1–5.4) | v4 (`v4-writing-desk.md`) + chronicle export |
| Custom tabs (5.5) | v4 kanban tabs (author leans cut) |
| Bulletin board world-text + check-in-world (3.2) | v6 (`v6-bulletin-board.md`) — adds in-world render req |
| Linked/handbook task (2.3) | chronicle "handbook bookmarking" (lighter cousin) |
| Tracked numeric task (2.2) | chronicle "milestone-suggested via inventory polling" (shared primitive) |
| Lectern model / block refinement (1.4) | lectern-gui-polish "block shape refinement" (art-gated) |
| Everything else (1.1–1.3, 1.5, 2.1, 2.4, 3.1, 4.1, 4.2, 5.5–5.8, 6.x, 7.1) | 🆕 net-new — no roadmap home yet |

## Suggested triage buckets (for the breakout pass)

- **Quick wins (small, standalone, high satisfaction):** 4.1 faster-delete, 1.2 tab
  subtitles/theming, 1.4/1.5 model polish (art-gated).
- **New task-type cluster (one coherent change):** 2.1 dropdown + 2.2 tracked + 2.3 linked
  (2.4 mapped is heavier — maybe its own change).
- **Desk increment (v1.1, ship minimal):** 5.1 copy/paste core first; 5.2/5.3/5.4 as later
  increments gated on demand + immersion.
- **Investigation spikes (effort unknown):** 6.1 backpacks, 6.2 VTML, 3.2 in-world board render,
  3.1 hover preview — scope before committing.
- **Parked / leaning-no:** 4.2 undo, 5.5 custom tabs — keep on file, don't build without a strong
  reason.
