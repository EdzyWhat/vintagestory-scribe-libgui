## Context

v1.3.0 Crafting Tasks followed D4 of `add-crafting-tasks`: a Craft parent stores a recipe signature; `ReconcileCraftIngredients` inspects the **contiguous depth-1 run** under it, matches by item code, **rescales and creates**, never deletes. That reconcile runs on **editor open** (`SelfHealCraftTasks`) and on the parent target stepper. Complete/Sink/Delete/`MoveTaskToBottom` are identity-addressed **one row**. Pins always `list.Add`. HUD have/need is gated on `IsTracker` only.

Playtest (u/thepeebrain) plus in-world recipes like debarked oak log (`AHL`: tag-only axe + hammer `isTool: true`, plus the log) show: orphaned shopping lists, Sink tearing the group, uncomplete duplicating children, junk “2 Pocketsun (any variant)” tool rows, Craft HUD with no count, subtask pins appending at the end.

## Goals / Non-Goals

**Goals:**
- Stop silent recreation of Craft children except as a **create-once** expansion; stepper only rescales what is still in the owned run.
- Treat a parent as a **range** (depth-0 + contiguous depth-1) for complete/sink/delete/trash, driven by one **Subtask Behavior** picker.
- Pin placement that clusters children under a pinned parent; HUD Craft counters; pin notes as HUD reminders.
- Handbook Add-to-Scribe wording (handbook-only); HUD title/size/gear/cap; grip drag vs nest; skip tag-only tools.

**Non-Goals:**
- Visual collapse of subtask groups.
- Clear-HUD-and-refill from this document.
- Non-task handbook bookmarks; Link↔Tracker↔Craft conversion in the editor.
- A “tools” setting (omit is the default; the leak is a bug).
- Auto-migrating existing junk tool children off documents.
- Recursive crafting trees or depth-2.

## Decisions

### D1: Heal only on the Craft parent’s target stepper; rescale-only

Remove `SelfHealCraftTasks` from editor entry. Keep `ReconcileCraftFromSignature` on Handbook create (first generation) and on `SetEditorTrackerTargetQuantity` for a Craft parent.

When the stepper fires, match by item code inside the contiguous depth-1 run (shuffle among children is fine). **Update `TargetQuantity` only. Insert nothing.** Stop at the first non-depth-1 row. Player-added rows in the run that are not recipe ingredients are left alone.

**Alternative considered:** keep create-missing on stepper — rejected; deleting a child then bumping 1→2 would undo the delete. **Alternative considered:** search past depth-0 gaps — rejected; a gap still ends the group.

### D2: Owned run is positional, kind-agnostic

`OwnedRun(parentIndex)` = `[parentIndex+1, runEnd)` while `Depth == 1`. A parent is any depth-0 row with that run (not Craft-only). Completing a depth-1 row is a leaf (do not walk siblings). Text rows in the run have no `Done` flag but still **move/delete** with the range.

Core owns the scan so every surface (editor scratch, server write-through, tracker auto-complete of a **parent**) uses the same range.

### D3: One range mutation, not N completions

Sink/Delete/complete of a Bound parent extracts the range as a block and applies the document action once (parent then children, that order). Sequential `MoveTaskToBottom` per child **reverses** parent/child order and would re-break heal if heal still created — it must not be the implementation.

Tracker auto-complete of a **child** stays a leaf (emergent: a filled ingredient can Sink out of the group). Tracker auto-complete of a **Craft parent** is a parent complete (Bound takes the run).

### D4: Subtask Behavior travels on the request, like completion policy

Client-local enum on `ScribePlayerSettings` (JSON). Default **Bound**. Sent on complete and standalone delete packets (same pattern as `ScribeCompletionPolicy` on `ScribeCompleteTaskMessage`). Server does not persist it.

| Picker | Complete parent | Trash parent | Uncheck parent |
|---|---|---|---|
| **Bound to parent** | Mark completable rows in the run done; apply completion + pin policy to the **mutated** rows as one range | Delete the range | Uncheck completable rows in the run (no unsink, no undelete) |
| **Independent** | Parent only | Parent only | Parent only |
| **Discard children** | Remove children; parent gets the completion policy alone | Delete the range (children discarded) | Children stay gone; parent flips if still present |

Pin policy applies only to rows that option actually mutates, and only if pinned. True document delete always drops pins (existing).

**Alternative considered:** three boolean settings — rejected; one picker. **Alternative considered:** Bound-complete then each child re-enters completion — rejected (D3).

### D5: Emergent leftovers are accepted

Independent + Sink/Delete leaves depth-1 rows under whatever depth-0 is now above them. Extra indented notes travel with Bound. No special cases.

### D6: Pin insert and gather

Server `SetPin`:
- Depth 0: append, then **gather** — pull existing pins whose `TaskId` is in this parent’s **document** owned run to sit immediately after the new parent pin, preserving their relative order.
- Depth 1: resolve parent `TaskId` by walking back in the source document. If that pin exists, insert after the contiguous HUD cluster (parent, then following pins that are in that owned run). If parent is not pinned or the source is unresolvable: append. Never auto-pin the parent.

**Alternative considered:** auto-pin parent when pinning a child — rejected.

### D7: Pin notes; HUD is display-only for Text

Text rows get a pin control on Read and Edit. Pin snapshot already has `Kind` + `LastKnownText`. HUD: text only (no checkbox, no unpin). Unpin from Pin Tab or source pin icon. Pin Tab checkbox on a note SHALL unpin (not complete); Pin Tab delete still deletes the source row when the host allows.

### D8: HUD Craft counter uses carried-count, not `IsTracker`

`BuildHudItemContent` shall gate the have/need counter on `IsCarriedCountTracked` (Tracker **or** Craft), matching Pin Tab / editor.

### D9: Handbook labels are handbook-only keys

New lang keys for the Handbook postfix. Editor `ScribeAddKinds` keep `Add Link` / `Add Item Tracker` / `Add Crafting Task`. Handbook order: Link, Tracker, Craft. Heading stays `Add to Scribe`. Craft variants: `Add ingredients ({0})`.

### D10: Tag-only / non-consumed tools never become Trackers

In `DeriveIngredients`, skip a cell if `IsTool`, or `!Consume`, or `MatchingType` is tags-only with no usable ingredient code (default `*:*` MUST NOT be `EncodeWildcard`’d into a family Tracker). Debarked log → parent + oak log only. Existing “Pocketsun” rows stay until the player deletes them.

**Alternative considered:** remind-as-notes setting — deferred; omit is enough.

### D11: Grip drag threshold

Do not call `OnDragStart` on press. Start drag only after pointer movement past a small threshold. If a drag started, `onTap` / `OnGripTap` SHALL NOT fire on release (including from==to cancel). Resting glyph stays the grip; nest/unnest is tap-only.

### D12: HUD chrome

- Title lang `scribe-hud-title` → **Scribe Pins** (storm title unchanged).
- Header `FontSize` = `rowFontSize` (16 × HUD font scale), not hardcoded 14.
- `HudShowSettingsGear` bool, default true, HUD section of Settings. Off omits the HUD gear; dialog Settings tab remains.
- `MaxHudMaxRows` / clamp → **30**.

## Risks / Trade-offs

- **Independent leftovers re-parent visually** → accepted (D5); handbook/settings helptext should say Bound is the tree-like default.
- **Existing junk tool children persist** → no silent delete (D1); playtest copy can say “delete the bogus row.”
- **Subtask Behavior on the wire** → if a 1.3.1 client omits the field, server MUST default Bound (same as other new client prefs on a packet).
- **Gather teleporting children** when pinning a parent → intentional; can surprise if children were mixed with other pins.
- **Tablet 1-pin cap** vs parent+child cluster → existing cap; no special case.
- **Header matching row size** may feel large on a dense HUD → accepted vs peebrain’s “header smaller than items.”

## Migration Plan

No codec change. Settings JSON: new keys default via serializer. `HudMaxRows` already stored; values above 10 that were clamped will now stick up to 30. Rollback: revert the cut; old clients ignore unknown settings keys.

## Open Questions

None for implement — all product forks from the 1.3.2 explore are closed. In-game `.scribeprobe` on `debarkedlog-oak` is the verification gate for D10, not an open design question.
