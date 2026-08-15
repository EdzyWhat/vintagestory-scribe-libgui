# v7 — Scriptorium + Task Types

Architect-level spec for the v1.2/v1.3 feature cluster. The Scriptorium (formerly "Writing Desk")
is the new placed block that anchors this tier. Task Types (Tracker, Link, Crafting) and the
Assignment system are the major new capabilities.

---

## Framing

The v4 Writing Desk spec (`docs/specs/v4-writing-desk.md`) explored the design space but is
superseded by the decisions here. Key divergences from v4:

- **Not private / not owner-gated** — anyone can open and use it.
- **No kanban board** — not a fit for VS's single-column GUI (per ROADMAP.md discipline).
- **Renamed: Scriptorium** — with in-world fiction (a place of record-keeping and correspondence).
- Assignment is **place-bound**, not portable.

---

## The Scriptorium block

A new placeable block, the third "placed" tier after the Lectern.

**Recipe:** cheap, ~Lectern cost. More planks + nails, different shape, no iron. Intentionally not
gated behind metal tiers.

**3D model:** unique Blockbench model (art-gated parallel track). Merge of scribe-table base +
lectern elements for comparison. Retexture the wood to be less rotten. Add a quill + inkwell.
Appearance optionally variable based on desk inventory contents.

**Unique views (Desk-only):**
- The standard Read/Edit views + a unique **Assign & History** view showing what tasks have been
  assigned, to whom, and what happened (accepted/completed/declined/deleted). No other surface
  has this view.
- The **Inbox** nav-rail view (also on Lecterns).

---

## Task types

### Tracker task

Track acquisition of N items. Reuses Tallybook's vanilla-API count engine.

**Data model additions:**
- `ScribeBlockKind.Tracker` — new kind alongside Task/Note.
- Fields: `TargetItemCode` (AssetLocation), `TargetQuantity` (int), `CurrentQuantity` (int,
  server-authoritative).
- Completion setting (per-player Scribe setting): acquisition **completes** / **deletes** /
  **does nothing** the task. Default: completes.

**Count engine (vanilla, no Harmony):**
- Subscribe to `IInventory.SlotModified` on hotbar + backpack.
- Debounce → recount via `SatisfiesAsIngredient(stack, checkStackSize:false)`.
- Add a 1s poll for edge cases (Tallybook pattern).
- Server-authoritative: client sends count events; server owns `CurrentQuantity`.

**Display:**
- Row: item icon + name + `have/need` counter ("Nails 3/5").
- 4-state progress bar: empty → <half → >half → full+check (Satisfactory To-Do List pattern).
- Red = shortfall; neutral/gray = satisfied. Matches existing done-row treatment.

**Entry point:**
- "How many" flow at task creation — enter N. Tracker sets `TargetQuantity = N`.
- Handbook "→ Add to Scribe" button: Harmony postfix on
  `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` appending a `LinkTextComponent`.
  Harmony ships with VS (`0Harmony.dll`) — not a new mod dependency.

### Link task

A task that links to a handbook page or in-world location. Taps the same handbook-button entry
point as Tracker.

### Crafting task (v1.3)

Track crafting N of an item. Recipe decomposition + ingredient sub-rows.

**Recipe decomposition (vanilla API):**
- Filter `world.GridRecipes` by `recipe.RecipeOutput.ResolvedItemStack.Satisfies(target)`.
- Read cells from `ResolvedIngredients` (NOT `Ingredients` — null at runtime since 1.20.4).
- Skip tool/non-consumed cells (`!ingredient.Consume` / `IsTool`).
- Group by `RecipeGroup`: same group = interchangeable variants (one picker entry, cycling);
  different group = separate recipe picker entry.
- Quantity multiply: `craftsNeeded = ceil(N / Output.Quantity)`;
  `perIngredient = ingredient.Quantity * craftsNeeded`, summed across cells sharing the same item.

**Any-of-group ingredients:**
- Cycle acceptable stacks through one slot on ~1s timer (JEI CycleTimer pattern).
- Freeze cycle on hover (slot is a click target).
- Acceptable set = `world.Collectibles.Where(c => ingredient.SatisfiesAsIngredient(stack, false))`.

**Parent completion (assisted-manual):**
- Sub-rows auto-check from inventory (same SlotModified engine as Tracker).
- When all ingredient counts satisfied: parent shows **"Ready to craft"** highlight.
- Player taps parent row to complete — no auto-complete.
- Rationale: game cannot observe the actual craft without a brittle `InventoryCraftingGrid` patch;
  the assisted-manual design sidesteps this cleanly.

---

## Copy/paste and import/export

**Copy/paste via inventory slots:**
- Move tasks item-to-item using the inventory — physically moving a note from one surface to
  another. "Feels like putting papers in a drawer."

**Import/export: JSON + CSV**
- Round-trip out of a save, refine in Excel/Google Sheets, load back into same or another save.
- Uses the existing `ScribeDocument ⇄ JSON` codec path (anticipated in
  `docs/specs/chronicle-and-integrations.md`).
- Both formats ship together.

---

## Assignment system (v1.3)

### Place-bound principle

Assignment and acceptance are **block-bound interactions**, not portable operations. This keeps the
"physical note left for someone" fiction and avoids making Notebook/Tablet feel like a phone.

- **Assign:** Scriptorium only, via the unique Assign & History view.
- **Accept / Decline:** Scriptorium or Lectern only, via the Inbox view.
- **Notebook / Tablet:** never an assignment surface.

### Ambient signals

- **World particle** on the block when incoming work is waiting for you.
- **Count badge** on the Inbox nav-rail button inside the block's sidebar.
- Both together; HUD badge explicitly not used (would imply portability).

### Routing

Routed to **player UID** (not block-bound). Any Desk/Lectern surfaces incoming work. The particle
on the *block* provides the "walking past" ambient signal without requiring the exact source block.

### Flyer modes (group assignment)

Three modes when assigning to a group:

1. **Single** — one person accepts, then gone (pool of 1).
2. **Limited(N)** — pool draws down with each accept; locks at zero.
3. **Per-member** — fans out: each group member gets their own independent copy.

Pool never refills on delete: shows "deleted by X" in history, assigner re-assigns manually.

Groups: player must be IN the group to assign to it (uses VS first-party
`ICoreServerAPI.Groups` / `IPlayer.GetGroups()` / `PlayerGroupMembership`).

### Locked on send

Assigned task text is **immutable** after sending. Recipients can complete/delete/pin but cannot
reword. Assigner can **revoke** (a visible action recorded in history) but cannot silently edit.

### State machine

```
Pending → Accepted | Declined
               ↓
          Working → Completed | Deleted
```

Event-driven on explicit player actions only. **No partial-completion telemetry** sent back to
assigner — player-trusting by design (enables roleplay/accountability fiction). Assigner sees only
state transitions, not inventory counts.

### Accept mechanics

On **Accept:**
- Task copies into recipient's personal Scribe item (Notebook/Tablet) AND always pins to HUD.
- **Itemless recipients:** task enters a per-player pending queue (temporary, for HUD display).
  Auto-migrates to the first **legally writable** Scribe item opened:
  - Wet clay tablet (not hardened/fired) with < 10 tasks ✓
  - Wax tablet with < 10 tasks ✓
  - Notebook / Clockmaker's Notebook ✓
  - Hardened / fired tablet ✗ (locked)
  - Full tablet (10/10) ✗ — skipped, not errored; next legal item gets it
  - Lecterns / Desks ✗ — shared/placed blocks

On **Decline:**
- Non-destructive to sender's copy.
- Inbox entry moves to Declined state with optional reason.
- Sender's task on the shared board shows "declined by X" — assigner can reassign.

On **Ignore (close without acting):**
- True no-op. Offer stays in Incoming.
- Distinct from Decline (which is an explicit communicated rejection).

### Assign & History view (Desk-only)

The unique view only the Scriptorium has. Shows:
- Tasks assigned by me: to whom, what flyer mode, current state per recipient.
- Full audit trail: "RaptorKhan: accepted → completed; Mira: declined; Tovan: working."

### Persistence

Uses the existing `AssignedToUid` reserved field in `ScribeBlock` (already round-tripped by codec).
Player-addressed assignments stored server-side per player (extending the existing per-player pin
data store). Synced via the existing Sign-pattern (`ToTreeAttributes`/`FromTreeAttributes`,
`SendBlockEntityPacket`, `MarkDirty`, server-authoritative).

---

## Phasing

| Feature | Release |
|---|---|
| Scriptorium block + model + recipe | 1.2 |
| Tracker tasks | 1.2 |
| Link tasks | 1.2 |
| Handbook entry-point button (Harmony postfix) | 1.2 |
| "How many" quantity flow + completion setting | 1.2 |
| Copy/paste via inventory slots | 1.2 |
| JSON + CSV import/export | 1.2 |
| Crafting tasks (recipe decomp + assisted-manual) | 1.3 |
| Location/Waypoint tasks | 1.3 |
| Assignment + Inbox system | 1.3 |
