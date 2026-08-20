## Why

The Lectern is Scribe's placed shared-document surface, but it has one form factor: a
free-standing reading stand. Players want a wall-hung **chalkboard** — the same Scribe
document/task board, just presented as a different piece of furniture with its own look.
The block art (model, block textures, GUI background) is already authored and committed
(`eb44b45`), but the referenced `BlockChalkboard` / `Chalkboard` C# classes don't exist,
so the block can't register, and the committed `chalkboard.json` is a Scriptorium clone
with stale comments, a wrong texture key, Scriptorium-sized boxes, and Scriptorium
handbook keys. This change makes the chalkboard a real, working block.

## What Changes

- Add the chalkboard as a **wall-mounted form-factor variant of the Lectern** — identical in
  document behavior (shared placed document, server-authoritative lock, guestbook, autosave,
  all task kinds), differing in four cosmetic dimensions (its model, block textures, LibGUI
  dialog theme, GUI background) PLUS one behavioral dimension: it hangs on a wall like a
  vanilla painting instead of standing on the floor. Placement is the ONLY mechanical
  difference; the document layer is untouched.
- Add thin subclasses mirroring the existing Lectern pattern:
  - `BlockScribeChalkboard : BlockScribeWritingStation` (interaction-hint lang keys only).
  - `BlockEntityScribeChalkboard : BlockEntityScribeWritingStation` (page backdrop, page
    aspect, default title key, mesh-cache prefix, dialog factory only).
  - `GuiDialogScribeChalkboard : ScribeDialogBase` (theme override + guestbook nav button).
- Register the two class names (`BlockChalkboard`, `Chalkboard`) in `ScribeModSystem`
  alongside the Lectern/Scriptorium registrations so the committed `chalkboard.json`
  resolves.
- Add a `ScribeBackdrops.ChalkboardPage` spec pointing at the committed
  `textures/gui/scribe-chalkboard.png` (128×145, aspect 145/128).
- Add a distinct chalkboard LibGUI theme (dark slate surface / chalk-light text) to
  `ScribeTheme`, resolved for this dialog only via the existing `ResolveTheme` override
  seam — the player's global Light/Default preference is untouched for every other surface.
- **Fix the malformed committed assets**: correct `chalkboard.json` (drop Scriptorium
  comments; fix the `clate`→`slate` texture key; right-size collision/selection boxes for
  a board form factor; replace the borrowed Scriptorium handbook sections with chalkboard
  copy) and reconcile the `.bbmodel` texture keys with the blocktype `textures` dict so the
  model renders textured.
- Add chalkboard lang strings (interaction hints, default document title, handbook copy)
  and a crafting recipe / creative-inventory entry so the block is obtainable.
- Adopt the vanilla **painting** placement idiom for the wall-mount: the `HorizontalAttachable`
  block behavior + a `side` variant group (`abstract/horizontalorientation` →
  north/east/south/west) with `rotateYByType` on the shape and selection box, plus the
  painting's thin/null collision box and item transforms. Refactor the shared writing-station
  base's floor-only placement into two overridable seams (`RequiresSolidGround`,
  `OrientTowardPlayerOnPlace`) so the chalkboard opts out of floor placement + player-facing
  orientation while the Lectern/Scriptorium keep their current behavior unchanged.
- **Non-goals (explicitly out of scope):** no drawing/stroke input, no new task kinds, no
  new persistence format, no document-layer difference from the Lectern. This is
  NOT the drawable "chalkboard" sketched in `ROADMAP.md` for the v6 bulletin-board tier
  (a from-scratch stroke GUI); it reuses that name for a cosmetic writing-station variant.
  The naming overlap is flagged as an open question in `design.md`.

## Capabilities

### New Capabilities
- `chalkboard-block`: A wall/furniture-form Scribe document block that is behaviorally
  identical to the Lectern and differs only in model, block textures, dialog theme, and
  GUI background — implemented as thin subclasses of the shared writing-station base.

### Modified Capabilities
<!-- None: the Lectern's own behavior/spec is unchanged; the chalkboard reuses the shared
     writing-station behavior without altering it. -->

## Impact

- **New code:** `src/Mod/BlockScribeChalkboard.cs`, `src/Mod/BlockEntityScribeChalkboard.cs`,
  `src/Mod/GuiDialogScribeChalkboard.cs`.
- **Edited code:** `ScribeModSystem.cs` (two `Register…Class` lines); `ScribeBackdrop.cs`
  (new `ChalkboardPage` spec); `ScribeTheme.cs` (new chalkboard `ThemeData`);
  `BlockScribeWritingStation.cs` (two new `protected virtual` placement seams —
  `RequiresSolidGround`, `OrientTowardPlayerOnPlace` — defaulting to today's behavior so the
  Lectern/Scriptorium are unchanged).
- **Edited assets:** `assets/scribe/blocktypes/chalkboard.json`, the `.bbmodel` texture
  keys, `lang/en.json` (new keys), a recipe JSON. All under the committed art.
- **No new dependencies** (vanilla `VintagestoryAPI` + `gui` only); `src/Core/` untouched;
  persistence/sync unchanged (inherited from the writing-station base, which already
  follows the Sign-block pattern).
- **Save-compat:** additive — a new block code; no migration of existing worlds.
