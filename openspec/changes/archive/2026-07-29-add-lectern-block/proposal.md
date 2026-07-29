## Why

Vintage Story's progression is long and branching, and it's easy to lose track of what
you were working toward. Scribe needs a first, playable way to jot down tasks and a note
inside the game. Shipping one simple, well-built writing method now — before the full
tool progression — lets us validate the whole vertical slice (in-game block → GUI →
server-authoritative persistence → multiplayer sync → tests → CI → release) and get it
into playtesting.

## What Changes

- Add a **lectern block** that reuses the vanilla "lecturn-book-open" clutter shape (no
  new art). For this first slice the block is **creative-inventory only** (a crafting
  recipe comes later). Players place it and open it by **right-clicking** it.
- Opening the lectern shows a **GUI** to edit a **task checklist** (add / rename /
  toggle-complete / delete items) and a **short freeform note**. Only **one player at a
  time** may have a given lectern open.
- The lectern's contents are **persisted with the world** and **synchronized to all
  players** in multiplayer, with the server as the source of truth.
- Introduce the game-agnostic **document model** (tasks + note) and its mutation rules
  and serialization in a unit-tested `Core` library.
- Wire up the supporting project scaffolding this slice needs: the `Core` + `Mod` +
  `Core.Tests` projects, `modinfo.json`, GitHub Actions CI (Core build/test), and
  tag-driven release packaging.

Non-goals for this change (deferred to later tiers): a crafting recipe (creative-only for
now), held note items, the `docId`-on-item store, clay/paper mechanics, ownership gating,
categories, the pinned-task HUD, and drawing. The block simply stores one document keyed
by its position.

## Capabilities

### New Capabilities

- `task-note-document`: The game-agnostic model of a Scribe document — an ordered sequence
  of blocks, where each block is a task (text + done flag) or a freeform text section, with
  a reserved depth for future nesting — plus the rules for mutating it (add task / add text
  section, edit block text, toggle, delete, reorder) and serializing it to/from bytes.
  Lives in `Core`, no game references, fully unit-tested.
- `lectern-block`: The in-game lectern block — placement, interaction (look+hotkey or
  right-click) to open a GUI, and server-authoritative persistence + multiplayer
  synchronization of the document keyed to the block's position.

### Modified Capabilities

<!-- None — this is the first change; no existing specs. -->

## Impact

- **New code:** `src/Core/` (document model + codec), `src/Mod/` (block, block entity,
  GUI dialog, network messages, mod system), `tests/Core.Tests/`.
- **New assets:** `modinfo.json`, block JSON reusing the vanilla lectern shape, `en.json`
  lang file.
- **Build/CI:** solution file; `.github/workflows/ci.yml` (builds/tests `Core`) and
  `release.yml` (packages the mod zip on tag). The full mod compiles locally against
  `VintagestoryAPI.dll` via the `VINTAGE_STORY` env var.
- **Dependencies:** none beyond vanilla `VintagestoryAPI`.
- **Compatibility:** the mod is Universal (`side: Universal`, `requiredOnServer: true`).
