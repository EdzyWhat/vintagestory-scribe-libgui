## Context

The Lectern (`BlockScribeLectern` + `BlockEntityScribeLectern`) is the shipped, tested "placed
writing station" in Scribe. It hosts a `ScribeDocument`, opens the shared LibGUI dialog through
the `IScribeDocumentHost` seam, and handles placement orientation, break/pick document
carry-over, one-editor-at-a-time locking, guestbook, pin-store registration, and the Sign-pattern
persistence/sync. The Scriptorium (per `docs/specs/v7-scriptorium-and-task-types.md`) is the v1.2
tier's new placed block. Functionally, in v1.2 it is *the same writing station* as the Lectern —
same document, same dialog, same persistence — differing only in identity: block code, 3D model,
textures, recipe, name, and (later) its unique Assign & History / Inbox views.

Both block classes today are `sealed`, and nearly all of `BlockEntityScribeLectern`'s ~700 lines
are generic writing-station logic; the only Lectern-specific parts are already funneled through
the `IScribeDocumentHost` explicit members (`BackdropSpec`, `GetLayout`, `DefaultDocumentTitle`)
plus the object-cache mesh key and a couple of lang keys. This is the reuse seam this change
builds on.

## Goals / Non-Goals

**Goals:**
- Ship the Scriptorium as a distinct, craftable, placeable block that hosts a Scribe document and
  opens the existing dialog, with full Lectern parity for document behavior and persistence.
- Reuse the Lectern's writing-station machinery rather than cloning it, so the two blocks cannot
  drift apart in behavior.
- Keep the code path testable/landable ahead of the final art (the unique Blockbench model is a
  parallel art track).
- Leave clean attachment points for the v1.3 Scriptorium-only views (Assign & History, Inbox).

**Non-Goals:**
- No new GUI surfaces. Assign & History and Inbox are explicitly out of scope (v1.3 assignment
  system).
- No Tracker/Link/Crafting task types, copy-paste, or import/export (their own v1.2/v1.3 changes).
- No `src/Core/` changes — Core is surface-agnostic and already covers the document model.
- No change to the document codec or save format beyond the additive new block id.

## Decisions

### Decision 1: Extract a shared abstract base rather than duplicate or subclass the Lectern

Introduce `BlockScribeWritingStation` (abstract) and `BlockEntityScribeWritingStation` (abstract)
holding the generic writing-station logic. Refactor `BlockScribeLectern` /
`BlockEntityScribeLectern` to derive from them and supply only Lectern-specific config;
`BlockScriptorium` / `BlockEntityScriptorium` derive from the same base and supply Scriptorium
config.

The base owns everything shared: document field + `IScribeDocumentHost` implementation, mesh-angle
placement/rotation, `RotatedBox` collision/selection, break/pick document carry-over, pin-store
registration, editor-lock state machine, guestbook, tooltip title line, server reply routing, and
the Sign-pattern tree round-trip. Subclasses supply an abstract config surface:

- block code / registration name (via JSON + `RegisterBlockClass`),
- `BackdropSpec`, `GetLayout(width)`, `DefaultDocumentTitle` (the existing host members),
- the object-cache mesh key prefix (must be per-block so meshes don't collide),
- the interaction-hint and default-title lang keys.

**Alternatives considered:**
- *Duplicate `BlockEntityScribeLectern` into `BlockEntityScriptorium`.* Rejected: ~700 lines of
  behavior-critical, multiplayer-authoritative code copied verbatim would inevitably drift, and
  every future lectern fix would need doing twice. Violates the DRY and "one interface, many
  textures"-style discipline already established for the tablet.
- *Make `BlockScriptorium` subclass the (un-sealed) `BlockScribeLectern` directly.* Rejected:
  semantically wrong (a Scriptorium is not a kind of Lectern), and it couples Scriptorium identity
  to Lectern-specific naming/lang; the Scriptorium will soon add views the Lectern must not have.
- *Keep both sealed, share via composition/helper statics.* Rejected: the shared state (lock,
  document, mesh angle, guestbook) is instance state tightly coupled to BlockEntity lifecycle
  hooks; a base class is the conventional VS idiom (mirrors how vanilla shares BE behavior).

The base extraction is a **behavior-preserving refactor** of the shipped Lectern — no Lectern
behavior changes — which the existing Core + Atlas suites guard.

### Decision 2: Land the code with a stand-in shape; final art swaps in via JSON only

The unique Scriptorium Blockbench model + textures are an art-gated parallel track. The block/BE
code, recipe, registration, and dialog wiring do not depend on the final art, so this change lands
with a working stand-in shape (a derived/retextured Lectern shape or a simple placeholder) declared
entirely in `blocktypes/scriptorium.json`. Replacing it with the final model is a pure asset/JSON
swap — no code change — so the art track can proceed independently and the feature is testable
in-game immediately.

### Decision 3: Reuse the Lectern GUI backdrop as a placeholder; own spec key reserved

For v1.2 the Scriptorium's dialog uses the existing `ScribeBackdrops.LecternPage` backdrop (via
its host `BackdropSpec`). A distinct `ScribeBackdrops.ScriptoriumPage` is a later polish item once
the block's material/art palette is decided (mirrors how the tablet's contrast decision was
deferred until its backdrops rendered). Noted so the reviewer knows the shared backdrop is
intentional, not an oversight.

### Decision 4: Recipe — cheap, planks + nails, no metal tier

Per the v7 spec, the Scriptorium recipe is intentionally not gated behind iron. It reuses the
Lectern recipe's ink-fill mechanic (the `liquidContainerProps` "requires black dye" attribute,
matching the "quill + inkwell" fiction) but with a cheaper, plank-heavy pattern and no
`metalnailsandstrips`/iron requirement beyond ordinary nails. Exact grid pattern is tuned during
implementation against vanilla item codes; the recipe is a data file, easy to iterate.

### Decision 5: Registration and assets mirror the Lectern's footprint

Two lines in `ScribeModSystem` (`RegisterBlockClass("BlockScriptorium", …)` and
`RegisterBlockEntityClass("Scriptorium", …)`), a `blocktypes/scriptorium.json`, a grid recipe,
shape + textures, a GUI backdrop texture (placeholder = Lectern's), `lang/en.json` entries (block
name, `blockhelp-scriptorium-open`/`-edit`, `doctitle-scriptorium`), and a handbook/item entry.
No new network packets — the Scriptorium is just another host on the existing channel and registry.

## Risks / Trade-offs

- **[Base extraction regresses the shipped Lectern]** → Keep it a strictly behavior-preserving
  refactor (move code up, no logic changes); rely on the Core unit suite and the local Atlas
  integration suite (which stages the `gui` dep) to catch any regression before push. Review the
  diff as a pure move.
- **[Object-cache mesh key collision between the two blocks]** → The base must key the cached mesh
  by `Block.Code` (already does: `"scribelecternmesh-" + Block.Code + …`); generalize the prefix so
  Lectern and Scriptorium never share a cache entry. Explicit test: place one of each and confirm
  distinct meshes/facings.
- **[Placeholder art / shared backdrop makes the Scriptorium look like a Lectern]** → Acceptable
  for a v1.2 code-first landing; the model and backdrop are tracked follow-ups. Flag clearly in the
  changelog that art is provisional.
- **[Sealed-class churn]** → `BlockScribeLectern`/`BlockEntityScribeLectern` lose `sealed` (or
  become thin sealed leaves on an abstract base). Minor; the base classes are `abstract` so they
  can't be instantiated directly.

## Migration Plan

- Additive only: a new block id (`scribe:scriptorium`). No document codec change, no save
  migration, no change to existing blocks. Existing worlds gain the new craftable block on update.
- Implementation order: (1) extract the abstract base as a pure refactor and confirm Lectern parity
  green; (2) add `BlockScriptorium`/`BlockEntityScriptorium` as thin subclasses; (3) add assets
  (blocktype, recipe, stand-in shape, lang, handbook) and registration; (4) in-game verification;
  (5) final art swap (JSON only) when the model lands.
- Rollback: revert the change; since it is additive, no world data depends on it. A world that had
  crafted Scriptoriums would show unknown-block placeholders on downgrade (standard VS behavior for
  removing a block), so treat the art swap and code as one shipped unit.

## Open Questions

- Final recipe grid pattern and whether to keep the ink-fill (`liquidContainerProps`) requirement
  or simplify it — resolve during implementation against current vanilla item codes.
- Whether the Scriptorium gets its own `ScribeBackdrops.ScriptoriumPage` in this change or defers
  to a polish pass (leaning defer, per Decision 3).
