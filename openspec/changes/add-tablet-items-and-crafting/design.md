## Context

Scribe's shipped writing surfaces (Lectern, Notebook, Clockmaker's Notebook) are all mid/late-game
crafts, leaving early-tech-tree players with nowhere to record tasks. The "scratch tier" plan adds
two early-game craftable tablets (clay, wax) that reuse Scribe's existing document infrastructure
but present a leaner, more limited experience. The full plan splits into four proposals sequenced
to prove the cuneiform font first:

- **A** — `add-cuneiform-glyph-font` (font prototype; being implemented separately — do not touch).
- **B** — `add-tablet-items-and-crafting` (**this change**): the item(s), grid recipes, docId
  persistence, the Core policy, and registration.
- **C** — `add-tablet-dialog`: the bespoke tablet GUI.
- **D** — `add-tablet-pencil-toggle-row`: the editable cuneiform row.

This proposal deliberately isolates item/recipe/persistence risk from GUI risk: a tablet crafted
here opens the **existing** `GuiDialogScribeNotebook` so the item is fully testable before the
bespoke dialog (C) exists.

Key existing pieces reused verbatim: `ItemScribeNotebook` (the item shape), `NotebookHost` (the
`IScribeDocumentHost` adapter with server write-through), `ScribeDocumentAttributes` (docId +
document bytes on the ItemStack), and `ScribeNotebookSaveMessage` (the save packet). The network
message registration order in `ScribeModSystem.Start()` is frozen and MUST NOT change.

## Goals / Non-Goals

**Goals:**
- Two early-game tablet items (clay, wax) via one `ItemScribeTablet` class + a `material` variant axis.
- Both craftable by simple grid recipes (not clayforming).
- Document persistence on the ItemStack by pure reuse of `ScribeDocumentAttributes` — no new
  persistence code, no new packet.
- A `TabletHost` (`IScribeDocumentHost`) that mirrors `NotebookHost` and enforces a tablet cap.
- A reusable Core `ScribeDocumentPolicy` (max blocks / max pins / read-only) with a `Tablet` preset
  (10 tasks, 1 pin), unit-tested, applied at the mutation boundary.
- The tablet opens the existing document dialog (interim), so it is testable now.

**Non-Goals (explicitly deferred / excluded):**
- The bespoke tablet dialog — theme, backdrop, always-edit no-tabs layout (Proposal C).
- The pencil-toggle editable input/output row (Proposal D).
- The cuneiform pseudo-font (Proposal A).
- The stylus-in-offhand edit gate — explicitly **NOT** included.
- Firing → archive, water damage, carry-forward migration, and wax-wipe — all deferred mechanics
  from the older `v3-clay-tablet` design spec.

## Decisions

**One item class with a `material` variant axis, not two classes.**
Mirrors how the codebase already parameterises one host across notebook variants. Alternative
(two classes) duplicates the `ItemScribeNotebook` logic twice for no behavioral gain. The old
spec's `state:[soft,fired]` axis is dropped because firing is deferred — adding it now would bake a
persistence/variant shape we would have to migrate.

**Grid recipes, not clayforming.**
The user confirmed simple grid crafting for both tablets (clay + sticks; beeswax + frame).
Clayforming would couple the clay tablet to the pottery system and complicate the wax variant,
which has no clay path. Grid recipes keep both variants uniform and early-game-cheap.

**Cap at the host/editor boundary via a Core policy, not inside `ScribeDocument`.**
`ScribeDocument` backs every tier (Lectern, Notebook, tablet) and must stay tier-agnostic and
uncapped — putting a cap inside it would leak the tablet tier into the shared model and risk
silently limiting the notebook. Instead a small Core `ScribeDocumentPolicy` value type
(`int? MaxBlocks`, `int? MaxPins`, `bool ReadOnly`) exposes `CanAdd`/`CanPin`, and `TabletHost`
consults it at the mutation boundary. It is its own capability because Proposals C and D also
consume it. The cap counts **task blocks** (the user said "10 tasks").

**Reuse `ScribeDocumentAttributes` and `ScribeNotebookSaveMessage` — add no persistence or packet.**
The notebook already persists a document + docId on an ItemStack and syncs it server-authoritatively
through a frozen packet registration order. A tablet is the same shape of thing, so it rides the
same path. Introducing a `ScribeTabletSaveMessage` would force a new registration slot into a frozen
order for zero benefit.

**Interim dialog reuse (`GuiDialogScribeNotebook`), described generically in the spec.**
The spec requirement says "opens the existing Scribe document editing dialog," with a scenario
naming `GuiDialogScribeNotebook` as the interim reuse. This lets Proposal C swap in the bespoke
dialog without filing a MODIFIED delta against this capability's requirement.

**Placeholder art via a vanilla clutter tablet.**
`itemtypes/scribetablet.json` points its shape at a vanilla `game:` clutter tablet model with
texture remaps (per the plan). Authentic ancient art (Mesopotamian clay pillow, Roman wax diptych)
comes later. Stage scripts blanket-copy `src/Mod/assets`, so no build-script edits are needed
(the clockmaker-schematic precedent).

## Risks / Trade-offs

- **Vanilla clutter tablet shapes are *block* shapes rendered on an *item*.** → The plan flags this
  as a prototype unknown; verify held/inventory/ground rendering during implementation and fall
  back to a flat item texture if broken. This is placeholder art, so a fallback is cheap.
- **Placeholder recipe ingredient codes may not be the final ones.** → tasks.md flags the chosen
  codes (`game:clay-blue` + `game:stick`; `game:beeswax` + a wood/frame code) as placeholders to
  finalize against the installed game's actual codes during implementation.
- **Cap counts task blocks, not text sections.** → Intentional (the user said "10 tasks"); the
  policy predicate is documented to count task blocks so a future text-block cap can be added
  without ambiguity.
- **Interim notebook dialog shows notebook chrome (tabs, notebook backdrop) on a tablet.** →
  Accepted for this change; the whole point is testability before Proposal C. Documented as interim.

## Migration Plan

Purely additive — new items, new recipes, new Core type, one registration line. No world-save
migration: existing saves have no tablets, and the reused `ScribeDocumentAttributes`/packet formats
are unchanged. Rollback is removing the new files, the registration line, and the assets; no data
format is touched, so an unmodded/older client loses only the ability to craft/open tablets.

## Open Questions

- Final recipe ingredient codes and quantities (placeholders chosen now; confirm against the
  installed game during implementation).
- Exact vanilla clutter tablet shape/texture paths that render acceptably on a held item (verify
  in-game; fall back to a flat texture if the block shape misbehaves).
