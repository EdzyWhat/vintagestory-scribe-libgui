# Changelog

All notable changes to Scribe are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Clockmaker's Notebook Schematic** — a reusable blueprint item sold occasionally by wandering
  **Commodities** and **Treasure Hunter** traders. Added to the crafting grid alongside the usual
  Notebook + temporal gear + metal parts, it lets **any** player craft a Clockmaker's Notebook
  without the Clockmaker class or Tinkerer trait. The schematic is not consumed, so one purchase
  lasts forever. The existing trait-gated recipe and the `scribeClockmakerRequiresTrait` world
  setting are unchanged — this is an additional path, not a replacement.

### Changed
- The editor footer's Information (ⓘ) button now **toggles** the "Scribe Editor Features" handbook
  page: clicking it opens the reference when the handbook is closed and closes the handbook when it
  is already open (if the handbook is open on another page, the first click navigates there and the
  next closes it). Its tooltip now reads "Show / hide Editor Features". Shared across the Lectern,
  both Notebooks, and the tablet editors.

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

[0.2.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/releases/tag/v0.1.0
