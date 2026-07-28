# Changelog

All notable changes to Scribe are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.0]: https://github.com/EdzyWhat/vintagestory-scribe-libgui/releases/tag/v0.1.0
