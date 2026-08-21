## Context

Scribe already has `Tracker` (Item Tracker), which counts carried items toward a target and drives
completion automatically. Block kind is a byte (`ScribeBlockKind`); the Tracker fields
(`TargetItemCode`, `TargetQuantity`, `CurrentQuantity`) and a reserved `Depth` field already live on
`ScribeBlock`, and `Depth` already round-trips through all three codecs (binary `w.Write(block.Depth)`,
JSON `Depth`, TSV column). The carried-inventory scan (`ScribeTrackerCounter`, invoked from
`ScribeModSystem`) updates `CurrentQuantity` for Tracker rows over the player's carried inventory,
matching via `CraftingRecipeIngredient.SatisfiesAsIngredient`. Handbook creation already works: a
Harmony postfix (`ScribeHandbookPatch`) appends "Add Tracker"/"Add Link" links onto an item's page,
which call `ScribeModSystem.AddFromHandbook(kind, itemCode)` and land on a three-tier-resolved Scribe
surface.

A Crafting Task is not "a Tracker with a different label." It is a **composite generator**: it owns an
output goal AND expands a chosen grid recipe into an ingredient shopping list that self-updates. That
requires two capabilities — a general subtask (depth) capability, and the recipe-bound generator that
consumes it. The design deliberately builds subtasks as an orthogonal, kind-agnostic feature so the
one-level-deep rule is structural, not a special Craft rule.

Studied precedent: the current Tallybook mod solves the same recipe-variant problem with a single
Handbook link plus a large in-game "choose recipe" journal screen and a recursive tally tree, because
it spans many production methods (grid, smithing, cooking…). Scribe's scope is deliberately narrower
(grid recipes, one level of subtasks), which keeps the variant count small enough that per-variant
Handbook links stay manageable and no separate picker screen is needed.

## Goals / Non-Goals

**Goals:**
- A `Craft = 4` kind bound to a grid recipe (via a persisted recipe-signature string) that generates
  and maintains ingredient `Tracker` subtasks at depth 1.
- A general, kind-agnostic depth-1 subtask capability: indent rendering + grip-tap depth toggle.
- Loose, self-healing reconciliation of the ingredient run (update/create, never delete, one level).
- Handbook per-variant "Add Crafting Task" links (single when N==1), reusing the existing postfix.
- Family matching for wildcard ingredients, concrete matching for `{var}`-bound ingredients.
- Ceil batch math (crafts-needed = ceil(target ÷ output-per-craft)); Core-side, API-free.

**Non-Goals:**
- Cumulative crafted-event tracking (`OnItemCrafted`) — deferred; carried-count is the first signal.
- Litre-accurate liquid-ingredient tracking — deferred to a non-counting note or omission.
- Recursive/multi-level crafting trees (sub-ingredients that are themselves craftable) — one level.
- A separate in-game recipe-picker screen — per-variant Handbook links cover the grid-only scope.
- New network packets, block entity schema changes, or a persistence format change beyond the
  appended `Kind` value and one new recipe-signature string field.

## Decisions

### D1: Parent = `Craft` kind, children = plain `Tracker` kind at Depth 1 (structural one-level rule)
The parent is a new `Craft` kind that holds the recipe binding and output goal. Each generated
ingredient child is an **ordinary `Tracker` row** at `Depth` 1 — not a new "ingredient" kind. This
makes the one-level-deep constraint *structural*: children are already leaf Trackers with no
generator of their own, so nothing recurses. Children render, count, and complete through the exact
existing Tracker paths; only the parent carries generator logic.

**Alternative considered:** a dedicated `CraftIngredient` kind — rejected; it would duplicate all of
Tracker's rendering/counting/completion for no behavioral difference, and it would invite a recursive
tree. Reusing `Tracker` keeps the surface area minimal.

### D2: Subtasks are a separate, kind-agnostic capability (`task-subtasks`)
Depth-1 indentation and the grip-tap depth toggle are specified independently of crafting, because
`Depth` is already a general field and any kind can meaningfully be a subtask. Crafting Tasks are the
first consumer, but the capability stands alone. This keeps the "one level deep" rule in one place
(the depth clamp) rather than scattered through Craft logic.

**Alternative considered:** folding depth entirely into craft-task — rejected; it would bury a
reusable primitive inside one feature and make future manual-subtask use a re-derivation.

### D3: Persist a recipe **signature**, not the resolved ingredient list
A `Craft` block stores a stable recipe-signature string (identifying the grid variant), not a frozen
snapshot of ingredients. On open / target-change, the generator re-resolves the recipe from the
signature against the live recipe registry and reconciles. This keeps documents small, survives recipe
data updates, and matches Tallybook's signature approach (output page + pattern + dimensions). The
signature is chosen to be stable across sessions and specific enough to disambiguate variants.

**Alternative considered:** persisting the full expanded ingredient list on the block — rejected;
larger payload, goes stale when recipes change, and duplicates data derivable from the signature.

### D4: Loose, self-healing reconciliation keyed by item code — never delete, one level
On target-change / document-open, the generator inspects the **contiguous run of `Depth` 1 rows
directly below the parent**, matches them to the freshly derived ingredient list by item code, updates
matched rows' `TargetQuantity` to the new batch amount, and creates rows for unmatched ingredients. It
never auto-deletes (so a player who deletes/edits a subtask keeps their choice) and never touches
anything below depth 1. A row the player promotes to depth 0 or drags out of the run simply leaves the
managed set. "Loose" is the deliberate contract: self-healing convenience without fighting the player.

**Alternative considered:** strict regeneration (wipe + rebuild children) — rejected; it would erase
player edits and carried-progress and clash with the "player-trusting" design ethos used elsewhere.

### D5: Handbook per-variant links, reusing the append-only postfix (Q3)
Creation is Handbook-only. `ScribeHandbookPatch` (the existing postfix) appends one "Add Crafting
Task" link per grid recipe variant — collapsing to a single link when N==1, and none when the item has
no grid recipe. Links dispatch to a new `ScribeModSystem.AddCraftFromHandbook(itemCode,
recipeSignature)`, mirroring `AddFromHandbook`. Variants are grouped by recipe group, wildcard-material
fan-out collapses to one link, disabled recipes and pure tool ingredients are filtered, and each link
is labeled by its distinguishing ingredient with a "Recipe N" fallback. The handbook is append-only,
so multiple discrete links (rather than an inline expandable list) are the correct fit.

**Alternative considered:** one link + an in-game recipe-picker screen (Tallybook's model) — deferred;
justified there by many production methods, unnecessary for grid-only where N stays small.
**Alternative considered:** a bare footer "Crafting Task" entry — rejected; there is no item context
to bind a recipe to, so the kind is `RequiresItemContext` and no-ops on a null code (like Tracker/Link).

### D6: Ingredient matching — family for wildcards, concrete for `{var}` bindings
Wildcard/family ingredients (`linen-*`, `bowl-*-fired`) must count the whole family, so
`ScribeTrackerCounter.TryResolveIngredient` is extended to resolve wildcard/family codes (today it
resolves concrete codes only and treats wildcards as 0). But an ingredient bound to the output's
`{var}` substitution (e.g. a `plank-{wood}` recipe whose ingredient is the same-wood log) must resolve
to the **concrete** bound code for the chosen variant, so the birch-plank task counts birch logs, not
"any log." The generator therefore substitutes `{var}` bindings to concrete codes *before* emitting the
child, and only genuine wildcards are left broad.

**Alternative considered:** always broaden to family — rejected; it would make a birch-plank task count
oak logs, silently over-counting.

### D7: Liquid ingredients → non-counting note (v1 deferral)
Ingredients declared via `liquidContainerProps` / `requiresLitres` (e.g. 0.25 L honey in the poultice
recipe) are not litre-countable with the carried-count mechanism, so v1 emits them as a non-counting
`Text` note at depth 1 (or omits them) rather than as a broken Tracker child. Litre-accurate tracking
is a named follow-up.

### D8: Drag grip is dual-capacity (tap = toggle depth, hold = reorder)
The grip already wires `onPress`/`onRelease` to drag start/end. LibGUI's `GestureDetector` exposes a
distinct `onTap`, and `EventDispatcher.DispatchPointerUp` fires the tap/click only when release lands
over the same pressed element — so tap-vs-drag is discriminated positionally with no threshold to tune.
Adding `onTap: _ => Widget.OnGripTap(index)` toggles the row's depth; the existing press-hold-drag path
is untouched. The `OnRowDragEnd` no-op branch is the seam.

**Alternative considered:** a separate indent affordance/button per row — rejected; adds chrome and
per-surface layout work when the grip already owns "row structure" gestures.

### D9: Ceil batch math and ingredient derivation live in Core (API-free)
Crafts-needed = `ceil(TargetQuantity ÷ outputQuantity)`, and each child target = `ingredientQty ×
crafts-needed`. The math and the ingredient-list *shape* (a small value model) live in `src/Core/` so
they are unit-testable without a game install; the mod layer supplies the recipe data (from the VS
registry) into that Core model. This respects the load-bearing "Core never references the VS API"
invariant.

## Risks / Trade-offs

- **Recipe-signature stability across game/mod updates** → if a recipe's identifying signature changes
  between versions, a bound `Craft` task can't re-resolve. Mitigation: derive the signature from stable
  fields (output code + pattern + dimensions, per Tallybook), and degrade gracefully — an unresolved
  signature leaves the parent as a plain output tracker with its existing children untouched (never a
  crash, never a mass-delete, consistent with D4's never-delete rule).

- **"Craft" vs. "have" provenance** → progress is carried-count, so a player could satisfy a Craft task
  by looting/buying rather than crafting. Deliberate simplification (possession, not provenance); the
  label communicates intent and the ingredient subtasks still guide gathering. Cumulative-crafted
  tracking is the named stricter follow-up.

- **Wildcard resolver extension touching the existing Tracker counter** → extending
  `TryResolveIngredient` to resolve families could regress plain Tracker counts. Mitigation: keep the
  concrete-code path byte-identical and add family resolution as an additive branch; cover both with
  unit tests before shipping.

- **Codec forward-compat** → a document with `Kind = 4` or a recipe-signature field opened in an older
  Scribe version. Mitigation: the codec already degrades an unknown `Kind` to a plain Task on read;
  confirm the recipe-signature field reads as absent/empty on older versions and add an explicit test.
  No block entity schema bump.

- **Self-heal running on every open** → reconciliation cost. Mitigation: it scans only the parent's
  contiguous depth-1 run (a handful of rows), keyed by item code; negligible for realistic documents.

## Migration Plan

No data migration. `Kind = 4` is a new appended value (existing documents contain only 0–3); the new
recipe-signature string defaults empty on existing blocks. `Depth` already serializes in every codec,
so subtasks need no format change. A mixed-version client/server pair degrades gracefully: an older
client renders a `Craft` row as a plain Task and ignores the recipe signature, while the depth-1
children remain ordinary Trackers it already understands.

## Open Questions

- **Exact recipe-signature composition** — confirm the minimal stable field set (output code + pattern
  + WxH is the working proposal from Tallybook precedent) resolves grid variants uniquely in the VS
  registry; validate against the multi-recipe poultice/plank cases during implementation.
- **Craft-parent visual treatment** — hammer-style icon vs. "Craft" label framing (D3 of the old
  design); decide at row-rendering time, no model impact.
- **Liquid note wording vs. omission** — whether the non-counting liquid note (D7) is worth the row or
  should be omitted entirely in v1; settle during playtest.
