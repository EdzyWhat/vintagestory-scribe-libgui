## Context

The Scribe document model (`src/Core/`) today knows two block kinds: `Task` (a checkbox line) and
`Text` (a freeform note). The editor footer's add-picker (`ScribeAddKind` / `ScribeAddKinds.Live`)
deliberately offers only those two — its doc-comment already anticipates "Tracked/Linked" as future
registry entries. The Scriptorium block foundation shipped in `add-scriptorium-block`.

This change adds the two item-anchored task kinds from `docs/specs/v7-scriptorium-and-task-types.md`:

- **Tracker** — "gather N of item X", with a live `have/need` counter driven by the player's carried
  inventory (the Tallybook pattern).
- **Link** — a reference task pointing at an item's Handbook page.

Both are created through one new affordance: a **"→ Add to Scribe" button on every Handbook item
page** (a Harmony postfix), which drops a ready-made task for that item into the player's currently
open Scribe surface. This is the decided entry point (confirmed with the author): the Handbook is
where you already are when you decide "I need some of this," and it supplies the item identity a
Tracker/Link requires. The in-Scribe embedded search-picker viewMode remains a **decoupled
follow-up**, not part of this change.

Constraints carried in: `Core` must not reference the VS API; no new mod dependencies (Harmony
ships with the game as `Lib/0Harmony.dll`, so it is exempt); persistence follows the codec's
append-only version discipline (`docs/CODEC-MIGRATION.md`); favor clear, conventional solutions.

## Goals / Non-Goals

**Goals:**
- Append `Tracker` and `Link` kinds to the Core model with their fields, mutation ops, and
  serialization, unit-tested with no game install.
- A carried-inventory count engine that keeps a Tracker's `CurrentQuantity` live (hotbar +
  backpack only), server-persisted like any other synced block field.
- A Handbook "Add to Scribe" button that creates a Tracker or Link for that item in the player's
  open Scribe surface, with inline quantity entry for Trackers via the existing arrow-stepper.
- A per-player completion setting (completes / deletes / nothing; default completes) surfaced in the
  Settings tab.
- Codec bump v5 → v6 following the named-migration-step pattern; v6 reads v5 blobs by defaulting the
  new fields.

**Non-Goals:**
- The in-Scribe embedded search-picker viewMode (creation is Handbook-driven this release).
- Counting items in world containers / nearby chests (carried-only, per the spec and the author's
  choice).
- The v1.3 Crafting task type, recipe decomposition, and the assignment system.
- Copy/paste and JSON/CSV import/export (separate v1.2 changes).
- The Scriptorium's own inventory slots. The author has fixed their eventual shape (a *very* limited
  ~2-slot inventory that accepts **only Scribe items**, for the item-to-item copy/paste gesture), but
  building those slots belongs to the copy/paste change, not this one. Recorded here only because it
  settles the shared-Tracker question below.

## Decisions

### D1 — New kinds are appended; Core stays API-free by storing codes as strings
`ScribeBlockKind` gains `Tracker = 2` and `Link = 3` (append-only; `Task`/`Text` never renumber).
`ScribeBlock` gains `TargetItemCode` (string?), `TargetQuantity` (int), `CurrentQuantity` (int), and
`LinkTarget` (string?). The target/reference are stored as **plain strings**, not
`AssetLocation`/`ItemStack`, so `Core` keeps zero VS-API references — the Mod layer parses them into
`AssetLocation` when it needs the game. Clamping (`TargetQuantity ≥ 1`, `CurrentQuantity ∈
[0, TargetQuantity]`) lives in Core so it holds regardless of caller.
*Alternative considered:* a discriminated payload/subclass per kind — rejected as heavier than the
existing flat-record model and worse for the codec's fixed field layout.

### D2 — Codec v6 via `ApplyV5ToV6Migrations`, two-version window
`ScribeDocumentCodec.Version` → 6, `PriorVersion` → 5. The per-block record appends the four new
fields after the existing ones. Reading v5 runs a new named `ApplyV5ToV6Migrations` step that
defaults the new fields (`TargetItemCode`/`LinkTarget` = null, `TargetQuantity` = 1,
`CurrentQuantity` = 0). The accepted window slides to {v6, v5}; **v4 is dropped** (immediately-prior
only — the existing discipline, and v4 predates any release). The existing v4 older-blob test is
replaced by a v5 older-blob test asserting the new fields default correctly. The class doc-comment's
version table is updated.
*Alternative considered:* chaining v4→v5→v6 to keep v4 readable — rejected; it violates the
single-transition rule the codec-migration spec already fixes, and v4 has no shipped saves.

### D3 — Creation entry point: Handbook postfix with three-tier surface resolution
A Harmony postfix on `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` appends a
clickable `LinkTextComponent` ("Add to Scribe") that knows the page's collectible. Clicking resolves
a **target surface** in three tiers:
1. **Open surface** — a currently open Scribe dialog, found via the pattern already used in
   `ScribeModSystem.Network.cs`: `capi.Gui.OpenedGuis.OfType<ScribeDialogBase>().FirstOrDefault(d =>
   d.IsOpened())`. Covers both block surfaces (Scriptorium/Lectern) and item surfaces
   (Notebook/Tablet), since all are `ScribeDialogBase`.
2. **Carried Scribe item** — if nothing is open but the player carries a Scribe item, open the UI of
   the **last-opened** Scribe item still carried (D3a) and use it.
3. **Neither** — `TriggerIngameError("You need a Scribe item to do that.")` and create nothing
   (reuses the error channel from `zero-point-three-fixes`).

Once a surface is resolved (tiers 1–2), its host exposes a `DocId`; the client sends a new
`ScribeCreateTaskFromHandbookMessage { DocIdBytes, ItemCode, Kind, TargetQuantity }` on the existing
network channel; the server appends the block through the normal server-authoritative edit path and
syncs back.
*Alternative considered:* creating the task client-side directly — rejected; all document edits must
flow through the server (the load-bearing sync invariant).

### D3a — Track the last-opened Scribe item (client-side)
Tier 2 needs to know *which* carried Scribe item to open. The client records the last Scribe item
whose dialog it opened (a lightweight in-session field on the client mod-system, set wherever an
item-hosted Scribe dialog opens). Resolution: if that item is still in the player's inventory, open
it; else fall back to the first Scribe item found in the hotbar/backpack; else tier 3. This is a
client-only convenience — no persistence needed (a fresh session with a carried item just falls back
to "first carried Scribe item").

### D3b — Footer Tracker/Link entries are Handbook guides, not creators
The footer add-picker (`ScribeAddKinds.Live`) gains Tracker and Link entries so the kinds are
discoverable, but they cannot create a block (no item identity at the footer). The `ScribeAddKind`
dispatch is extended so an entry may carry a **non-mutating guide action** instead of a
`Func<ScribeDocument, bool>` document mutation. The guide action: if the Handbook is closed, open it
to the Tracker/Link explainer entry (`GuiDialogHandbook` / the handbook open API); if it is already
open, `TriggerIngameError` instructing the player to scroll to the current entry's bottom and click
the "Add to Scribe" link. The explainer entry is a normal registered Handbook entry (registration
JSON + lang copy), matching how the mod already authors handbook entries.
*Alternative considered:* leaving Tracker/Link out of the footer entirely (the original plan) —
reversed at the author's request: the footer entries make the feature discoverable and teach the
Handbook-driven flow.

### D3c — Link tasks are hyperlinks from every surface
A Link task's label is clickable in every Scribe UI and on the pinned-task HUD; clicking opens its
referenced Handbook page (parse `LinkTarget` → `AssetLocation` → open the item's handbook page via
the same handbook open API the "Add to Scribe" button uses). This activation is separate from the
row's completion control, so opening the page never toggles done-state. The HUD path reuses the
existing row-click plumbing, gated on the block's kind being `Link`.

### D4 — Inline quantity via the existing arrow-stepper; Tracker vs Link chosen at the button
The Handbook button offers both a Tracker and a Link path for the item (two link components, or one
with a small choice). A Tracker seeds `TargetQuantity` at a sensible default (1) and the player
adjusts N **on the row** using the existing numeric stepper with arrow affordances
(`typed-arrow-substitution`, the Settings numeric control) — no separate "how many" modal, per the
author. A Link carries only the reference. This reuses the arrow-stepper we already ship and keeps
creation to one gesture + optional tweak.

### D5 — Count engine: carried-only, ingredient-satisfies matching, synced like Done
A Tracker's count is computed from the player's **carried** inventory only (hotbar + backpack).
The Mod builds a `CraftingRecipeIngredient` from `TargetItemCode` and matches carried stacks with
`SatisfiesAsIngredient(stack, checkStackSize:false)` (wildcard-friendly, the Tallybook pattern),
summing stack sizes. It recomputes on `IInventory.SlotModified` (debounced) plus a ~1s edge-case
poll while a Tracker is live, and on dialog open. `CurrentQuantity` is treated as a normal synced
block field routed through the server (like `Done`), so it persists and multiplayer viewers
converge. When the target is met, the **client** applies the owner's completion setting by issuing
the matching edit (complete / delete / none) — the server just persists the resulting edit.
*Alternative considered:* server-side inventory watching — deferred; the client already drives every
other edit and owns the display, and keeping the count on the client avoids a new server-side
per-player inventory subscription.

### D6 — Completion setting lives in the client config + Settings tab
The completes/deletes/nothing preference is a per-player client setting (the existing
`ScribeClientConfig` + `ScribeSettingsContent`/`ScribeSettingsDialog` surface), read when the
Tracker crosses its target. Client-side placement matches D5 (the client detects target-met and
issues the edit) and reuses the config/settings machinery already in place.

## Risks / Trade-offs

- **Shared-Scriptorium Tracker semantics in multiplayer** → A Tracker always counts the **local
  viewing player's carried inventory** — never any block's stored items (the Scriptorium's own slots
  are Scribe-items-only and irrelevant to tracking, per the author's clarification). On a shared
  Scriptorium the synced `CurrentQuantity` therefore reflects whichever player most recently
  viewed/interacted, exactly like a shared `Done` toggle. This is exact in singleplayer (the
  overwhelming common case) and on single-holder item surfaces. Refining true per-player progress on
  a shared doc is deferred to the v1.3 assignment work. Documented, not a blocker.
- **Harmony patch fragility** → The postfix targets a public method
  (`GetHandbookInfo`) and only *appends* a component, mirroring the memory's green-lit "public-method
  postfix" pattern. Low risk; add a VSAPI-NOTES entry for the exact type/signature.
- **Codec v6 is not backward-readable by older clients** → A v6 save can't be read by a pre-v6
  build; this is inherent to any field addition. Mitigation: v6 reads all v5 saves, the boundary is
  documented in `docs/CODEC-MIGRATION.md`, and the first write of a new-kind block is the version
  bump point. No migration is needed for existing saves (they read forward cleanly).
- **Count-engine churn** → Frequent `SlotModified` events could recount too often. Mitigation:
  debounce + a single ~1s poll, and only while at least one Tracker is present in the open document
  (no cost when none exist).

## Migration Plan

1. Land the Core changes (kinds, fields, ops, codec v6 + migration step) with full unit tests; CI
   Core suite is green with no game install.
2. Add the Mod count engine, Harmony postfix, network packet, row rendering, and Settings entry.
3. Restage and playtest (Atlas + in-game) per `what-to-test`.
4. Rollback: the change is additive; reverting the commit restores v5. No save written by a v6 build
   is readable after rollback, so treat v6 saves as the point of no return for a given world (same
   as every prior codec bump — call it out in the changelog).

## Open Questions

- **Per-player progress on shared surfaces:** should a shared-Scriptorium Tracker eventually show
  each viewer their own carried progress (and only auto-complete for the player who meets it), rather
  than a single shared count? Deferred to when shared-doc + assignment semantics are designed (v1.3).
- **Wildcard targets:** the Handbook button supplies a concrete item, but the count engine matches
  via `SatisfiesAsIngredient`, which would also honor a wildcard code. Do we ever want to let a
  Tracker target a wildcard family (e.g. "any plank")? Not needed for this change; the engine
  supports it for free if a later entry point offers it.
