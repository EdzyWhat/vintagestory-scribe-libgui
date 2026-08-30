## Context

`FindCarriedNotebooks` (`ScribeModSystem.History.cs:63-81`) walks `player.InventoryManager.InventoriesOrdered`
and yields a `NotebookHost`/`TabletHost` for every slot whose inventory's `ClassName` is in
`CarriedInventoryClasses` — currently a 4-entry allow-list (`hotbar`/`backpack`/`character`/`mouse`).

Root-cause investigation (full detail in the `history-storm-death-regression-investigation` memory
file) found two independent, unrelated reasons this misses real carried notebooks:

1. **`InventoriesOrdered` is `PlayerInventoryManager.Inventories.ValuesOrdered`** (decompiled
   `VintagestoryLib.dll`), and any mod can add its own `InventoryBase` directly into that
   dictionary. xSkills' "Strong Back" survival ability does exactly this — `Survival.OnStrongBack`
   permanently adds an `XSkillsPlayerInventory` (`ClassName = "xskillshotbar"`) the moment a player
   takes the ability tier, with no dialog needing to be open. The allow-list has no way to know
   about this in advance.
2. **The CarryOn mod family (CarryOnLib + CarryOn) never adds anything to `InventoriesOrdered` at
   all.** Carrying a container block (chest, basket, etc.) on the back/hands/shoulder freezes its
   entire block-entity state — including its own inventory — into a raw `ITreeAttribute`
   (`CarriedBlock.BlockEntityData`) attached to the player entity. There is no live `IInventory` to
   enumerate, so this needs an entirely separate detection path, not a `ClassName` fix.

A survey of 12 popular player-storage mods found xSkills' Strong Back is the only mod that
reproduces failure mode 1 — every backpack-style mod surveyed reuses vanilla's existing `backpack`
`ClassName` (already allow-listed) rather than inventing a new inventory class.

## Goals / Non-Goals

**Goals:**
- Make `FindCarriedNotebooks` include any current or future mod-added `InventoryBasePlayer`
  inventory automatically, without needing to know its `ClassName` in advance.
- Include `craftinggrid` as "carried" (previously excluded; the mod author now wants notebooks in
  the 3×3 crafting grid to participate in history recording).
- Detect and record history on Notebooks stored inside a container currently carried via the
  CarryOn mod family, when that mod is installed — with zero build-time dependency on it.
- Preserve the one real, already-fixed bug this code guards against: never write history into a
  transiently-attached inventory that isn't genuinely the player's own (the original `creative`
  template-mutation bug, `c36e1cb`).

**Non-Goals:**
- Supporting every conceivable third-party "player storage" mechanism exhaustively. Only the
  CarryOn family is targeted, because it's the only surveyed mechanism with genuinely no live
  inventory to enumerate. A future mod using some other novel storage mechanism is out of scope
  until it's reported.
- Tick-rate performance tuning. This code only runs on rare events (death, PvP kill, storm rising
  edge) — never per-tick — so the CarryOn scan's cost is a non-issue regardless of how many
  container types or nesting levels it walks.
- Any change to `src/Core/` — this is entirely within the game-facing adapter layer.

## Decisions

### 1. Replace the `ClassName` allow-list with a type check + a 2-entry denylist

`CarriedInventoryClasses` (a `HashSet<string>` of 4 names) is replaced by:

```csharp
inv is InventoryBasePlayer && !DeniedInventoryClasses.Contains(inv.ClassName)
```

where `DeniedInventoryClasses = { GlobalConstants.creativeInvClassName, GlobalConstants.groundInvClassName }`.

**Why not a pure name-based denylist (deny `creative`/`ground`/keep excluding nothing else)?**
`InventoriesOrdered` doesn't only hold the player's own persistent inventories — vanilla also adds
*other* inventories to it for the duration their dialog is open (a chest, oven, trader stall, etc.,
added via `OpenInventory`). Those use plain `InventoryGeneric` (or a subclass of it), confirmed via
`vsapi` source (`InventoryGeneric`'s `Derived` types are `CreativeInventoryTab`, `InventoryDisplayed`,
`InventoryPerPlayer` — none of which are `InventoryBasePlayer`). A pure name-based denylist would
have no way to exclude these transiently-opened external containers, so a Notebook sitting inside a
chest the player merely has open nearby would start recording history — the exact bug class
`c36e1cb` already fixed once, just against a different inventory. The type check is what makes a
denylist safe: `InventoryBasePlayer`'s own doc-comment defines it as "all inventories that are 'on'
the player" — the engine's own boundary for what counts as carried.

**Why still need a 2-entry denylist at all, if the type check is doing the real work?**
Decompiled `VintagestoryLib.dll` confirms all 7 vanilla default player inventories — including
`creative` (`InventoryPlayerCreative`) and `ground` (`InventoryPlayerGround`) — ARE
`InventoryBasePlayer`. The type check alone doesn't exclude them; they're excluded by name because
of specific, unrelated reasons: `creative` holds infinite template stacks (writing history mutates
every future copy, the original `c36e1cb` bug), `ground` is transient block-drop staging, not
on-person. `craftinggrid` is deliberately NOT in this denylist per the mod author's decision —
items in the crafting grid are "in your hands" during that transient dialog.

**Alternatives considered:**
- *Keep the allow-list, just add `"xskillshotbar"` to it.* Rejected — fixes this one mod, not the
  general class of bug (any future mod-added `InventoryBasePlayer` would still be missed).
- *Denylist by name only, no type check.* Rejected — see the chest-dialog regression risk above.

### 2. CarryOn integration via pure reflection, no compile-time reference

Decided with the user: zero build-time dependency on `CarryOnLib.dll`/`CarryOn.dll` — nothing is
added to the `.csproj`. At runtime, gated behind `sapi.ModLoader.IsModEnabled("carryon")`
(`CarryOn.API.Common.Models.CarryConstants.ModId = "carryon"`, confirmed via decompilation), the
integration:

1. Locates the manager via the **vanilla, string-based** `IModLoader.GetModSystem(string fullName)`
   overload — `sapi.ModLoader.GetModSystem("CarryOn.CarryOnLib.CarryOnLibSystem")`. This returns a
   base `ModSystem` (a vanilla type Scribe already references), so no CarryOn type is needed even
   for this step.
2. Reflects the `CarryManager` property off that `ModSystem` instance (confirmed via decompiled
   `CarryOnLibSystem : ModSystem { public ICarryManager? CarryManager { get; set; } }`) to get the
   manager object (typed as `object` in Scribe's code — `ICarryManager` is never referenced).
3. Reflects and invokes `GetAllCarried(Entity)` on that object to enumerate the player's carried
   blocks (0-3 per player: Back/Hands/Shoulder), then reflects the `ItemStack` and
   `BlockEntityData` properties off each boxed `CarriedBlock` result. **`ItemStack` and
   `BlockEntityData` (`ITreeAttribute`) are themselves vanilla types** — confirmed via decompiled
   `CarriedBlock` — so once obtained, all further processing (the recursive tree walk, resolving
   nested item stacks, checking `IScribeDocumentItem`, mutating `scribeHistory` bytes) is ordinary,
   fully-typed vanilla code with no further reflection needed.
4. To write an updated Notebook stack back: mutate the resolved `ItemStack.Attributes` in place,
   write it back into its parent tree via `ITreeAttribute.SetItemstack(key, stack)` (never assume
   in-place aliasing persists — same explicit-write-back discipline `NotebookHost.Flush()` already
   follows), then reflect-invoke `SetCarried(entity, carriedBlockObj, null, true)` on the manager
   (the same `CarriedBlock` object reference, `markDirty: true`) so CarryOn re-persists and re-syncs
   it through its own normal path.
5. All reflection calls are wrapped in try/catch with a one-time (not per-event) log on failure, so
   a future CarryOn API change degrades to "feature silently inactive," never a hard error.

**Why reflection over a soft compile-time reference (the `ConfigLib` pattern)?** Decided with the
user in favor of reflection specifically because it adds literally nothing to the project file —
the strongest possible reading of "no new mod dependencies." A soft compile-time reference (vendor
`CarryOnLib.dll` locally, `Private=false`) was the alternative; rejected because `CarryOnLib` was
never previously named a planned exception the way `ConfigLib` was, and reflection's downside
(harder to write, silently stale on an API-shape change) is acceptable given how small and
well-isolated the reflected surface is (5 members total: `IsModEnabled`, `GetModSystem(string)`,
one property, `GetAllCarried`, `SetCarried`).

### 3. Generic recursive `ITreeAttribute` walk to find a Notebook inside a carried block

`BlockEntityData` has no single universal "the inventory" key — different container block types
serialize their slots under different keys via their own `BlockEntity.FromTreeAttributes`. Since
`ITreeAttribute : IEnumerable<KeyValuePair<string, IAttribute>>` (confirmed via `vsapi` source) is
generically walkable without knowing key names in advance, the detection path recursively visits
every value in the tree: nested `ITreeAttribute` values are recursed into, and any value holding a
resolvable `ItemStack` is checked for `Collectible is IScribeDocumentItem`. This is
version/block-type-agnostic by construction — it doesn't need to know how a chest vs. a basket vs.
a trunk stores its slots.

## Risks / Trade-offs

- **[Risk] The exact `IAttribute` subtype(s) used for stored item stacks (and whether slots are
  ever stored as a tree-array rather than individually-keyed trees) aren't confirmed yet — the
  `vsapi` interface confirms the walkable shape but not every concrete storage pattern in the
  wild.** → Mitigation: first implementation task is a spike against a real carried chest in a
  local game session (per-CLAUDE.md local Atlas/manual-verify convention) before building the full
  feature, to confirm the walk correctly finds and round-trips a test Notebook.
- **[Risk] CarryOn ships pre-release versions (v2.0.0-pre.8 uses a rewritten
  `EntityBehaviorAttachableCarryable` architecture internally) that could change `ICarryManager`'s
  shape or the `CarryOnLibSystem.CarryManager` property in a future release.** → Mitigation: this is
  exactly why reflection was chosen over a compile-time reference — a shape change degrades to
  "integration silently inactive" (logged once) rather than a build break or runtime crash.
- **[Risk] `craftinggrid` inclusion means a Notebook temporarily placed in the crafting grid (e.g.
  mid-recipe-arranging) now participates in Death/PvpKill/TemporalStorm recording** — a genuine,
  intentional behavior change, not a bug, per the mod author's explicit request.
- **[Trade-off] The CarryOn detection path only fires on the same rare events as everything else
  (death/kill/storm), so a Notebook placed in a carried container and then removed between two such
  events is treated no differently than one placed in any other unwatched storage** — consistent
  with how the mod already treats all "carried" storage (it's a snapshot at event time, not a
  continuous watch), so this isn't a new inconsistency.

## Migration Plan

No data migration — `HistoryStore`'s on-disk format is unchanged; only which inventories are
scanned changes. Ship as a normal mod version bump. No rollback concerns beyond a normal revert,
since no persisted data shape changes.

## Open Questions

- Confirm during implementation (not before): the concrete `IAttribute` subtype(s) actually used
  for stored item stacks inside a carried block's frozen data, and whether any carryable container
  type stores slots as a tree-array rather than individually-keyed sub-trees — see the first risk
  above.
- Should `AttachedCarriedBlock`s (a carried block can have another block attached to it, e.g. a
  sign on a carried chest) also be scanned recursively? `CarriedBlock.AttachedBlocks` exists;
  confirm during implementation whether each entry exposes its own `BlockEntityData` analogous to
  the primary carried block's.
