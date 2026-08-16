## Why

Today every Scribe task is a plain free-text line the player writes and checks off by hand.
The Scriptorium block foundation has landed, but the two task types that give it a reason to
exist — a **Tracker** ("gather 5 nails", counter fills as you collect them) and a **Link**
("this handbook page / place matters", one click to revisit) — don't exist yet. Both hang off a
single new affordance the game already invites: a button on any Handbook item page that drops a
ready-made task straight into your open Scribe surface. This is the v1.2 task-types cluster from
`docs/specs/v7-scriptorium-and-task-types.md`.

## What Changes

- **New `ScribeBlockKind.Tracker` task.** Targets a specific item (an `AssetLocation`) and a
  quantity N. A carried-inventory count engine keeps a live `have/need` counter; when the target
  is met, a per-player completion setting decides whether the task auto-completes, deletes, or does
  nothing (default: completes). Counting scope is **carried only** (hotbar + backpack), matching
  the Tallybook pattern — no world/chest scanning.
- **New `ScribeBlockKind.Link` task.** A reference task pointing at a Handbook page (and, where
  available, an in-world location). No count engine — tapping it re-opens the referenced page.
  Reuses the same Handbook entry point as Tracker.
- **Handbook "→ Add to Scribe" button.** A Harmony postfix on
  `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` appends a link component to every
  item page. Clicking it creates a task for that item, resolving a **target Scribe surface** in
  three tiers: (1) if a Scribe surface is open (block *or* item — Scriptorium/Lectern or
  Tablet/Notebook), use it; (2) else if the player carries a Scribe item, open the last-opened
  Scribe item's UI and use it; (3) else show a Vintage Story error ("You need a Scribe item to do
  that."). Harmony ships with the base game (`Lib/0Harmony.dll`) — this is **not** a new mod
  dependency.
- **Footer add-picker gains Tracker & Link entries — as Handbook guides, not direct creators.**
  Because a Tracker/Link needs an item identity the footer can't supply, clicking these entries
  routes the player to the Handbook: if the Handbook is **closed**, it opens to a new explainer
  entry describing how these task types work and prompting the player to find the item/page they
  want; if the Handbook is **already open**, it fires a Vintage Story error telling the player what
  to do (scroll to the bottom of the current entry and click the "Add to Scribe" link for the task
  type they want).
- **A new Handbook explainer entry** describes the Tracker and Link task types and points at the
  per-item "Add to Scribe" links — the destination of the footer guide.
- **Link tasks are hyperlinks.** Clicking a Link task in the HUD or in any Scribe UI opens the
  Handbook to that linked entry (standard link behavior), without changing the task's completion.
- **Inline quantity entry for Trackers.** A Tracker's target N is set on the row itself via the
  existing arrow-affordance numeric stepper (the `typed-arrow-substitution` / Settings numeric
  control), not a separate "how many" modal. A Link created from the same button carries no
  quantity.
- **Codec version bump v5 → v6.** The new Tracker fields (`TargetItemCode`, `TargetQuantity`,
  `CurrentQuantity`) and Link reference field append to the block record, following the
  established version-aware read-migration discipline (`codec-migration`, `docs/CODEC-MIGRATION.md`).
  v6 reads v5/v4 blobs by defaulting the new fields.
- **Out of scope (deferred, decided):** the in-Scribe embedded search-picker viewMode (creation is
  Handbook-driven for now); carried+chests count scope; the Scriptorium's own inventory slots
  (Scribe-items-only; belongs to the copy/paste change); the v1.3 Crafting task type and the
  assignment system.

## Capabilities

### New Capabilities
- `tracker-task`: the Tracker task kind — item target + quantity, carried-only live count engine
  (server-authoritative), `have/need` progress display, and the completion-setting behavior.
- `link-task`: the Link task kind — a reference task pointing at a Handbook page that re-opens it
  when the task is clicked in the HUD or any Scribe UI (hyperlink behavior).
- `handbook-scribe-entry`: the Handbook item-page "Add to Scribe" button (Harmony postfix), the
  three-tier target-surface resolution (open surface → last-opened Scribe item → error), the footer
  add-picker's Tracker/Link guide entries, and the new Handbook explainer entry for these task types.

### Modified Capabilities
- `task-note-document`: the Core document/block model gains the `Tracker` and `Link` kinds
  (appended, never renumbered) plus the Tracker/Link fields and their mutation/validation rules.
- `codec-migration`: the serialization codec advances to Version 6, adding the new fields under the
  single-version-line append discipline and reading older (v5/v4) blobs by defaulting them.

## Impact

- **Core (`src/Core/`, no VS API):** `ScribeBlockKind` (+`Tracker`, +`Link`), `ScribeBlock`
  (new fields + constructor params + validation), `ScribeDocumentCodec` (v6 serialize + v5→v6
  migration step), plus `tests/Core.Tests` coverage (new-kind round-trip, old-blob read, quantity
  clamping). CI Core suite runs this with no game install.
- **Mod (`src/Mod/`):** a carried-inventory count engine (subscribe `IInventory.SlotModified` on
  hotbar+backpack, debounced recount via `SatisfiesAsIngredient(stack, checkStackSize:false)`, 1s
  edge-case poll, server owns `CurrentQuantity`); a Harmony patch class for the Handbook button;
  last-opened-Scribe-item tracking + the three-tier target-surface resolution; wiring the new kinds
  into the row renderer (item icon + counter + progress bar) and the existing kind-registry seam
  (`ScribeAddKind` / `ScribeAddKindPicker`) with the Tracker/Link entries dispatching a
  Handbook-guide action rather than a document mutation; a network path to create a task from the
  Handbook into the resolved surface; Link-task activation (HUD + Scribe UI) opening the Handbook;
  and a per-player completion setting in the client config surfaced through the Settings tab.
- **Assets (`lang/en.json`, handbook):** new lang keys for the Handbook button, footer guide
  entries + their error text, task-type labels, the completion setting, and the `have/need`
  counter; a new Handbook explainer entry (registration JSON + copy) for the Tracker/Link types.
- **Dependencies:** Harmony only (ships with the game — not a new mod dep). No new NuGet/mod
  packages.
- **Save compatibility:** a v6 blob is unreadable by pre-v6 clients, but v6 reads all existing
  saves. First writer-side use of a new kind is the version boundary — documented in
  `docs/CODEC-MIGRATION.md`.
