## Context

The Scriptorium already proves the container shape this needs: `BlockEntityScriptorium` owns a
lazily-constructed `InventoryGeneric` bound to the block-entity packet channel, persisted under
its own sub-tree key (`ToTreeAttributes`/`FromTreeAttributes`), dropped on break, and gated at the
slot level via a per-slot factory delegate (`(slotId, self) => new ItemSlotScribeDocument(self)`)
so restriction is enforced on every move path (drag, shift-click, hopper) without dialog-side
checks. `ItemSlotTaskNotice` already exists (Assignment Desk's supply/output slots) and gates on
`ItemScribeTaskNotice` specifically. The Assignment Desk's "background image" hint on its Task
Notice slots (see proposal) is not a PNG — it's a `Stack` overlay: a `ScribeVsIconGlyph`
(`"scribeassignment"`, `WatermarkScale` = 0.66, `colors.Primary`) painted under the slot, muted by
the slot's own semi-opaque `veilColor` fill (`colors.Surface with { W = 0.66f }`) sitting on top.

The Inbox block (`BlockEntityInbox` / `GuiDialogScribeInbox`) is the odd one out here: unlike the
Assignment Desk, Lectern, Scriptorium, and Chalkboard, it currently has NO tab switcher at all —
its right-nav is hardcoded to Inbox+Settings with the Inbox button always active, and its spec
explicitly says "no other tab present." Adding Inbox Inventory means giving it its first-ever
second tab, so the Assignment Desk's Assignment/Inbox switching mechanism
(`OnClickSwitchToAssignment`/`OnClickSwitchToInbox`, `IsAssignmentView`/`IsInboxView` in
`ScribeDialogBase.ViewSwitching.cs`) is the pattern to extend, not reinvent.

## Goals / Non-Goals

**Goals:**
- Reuse the Scriptorium's inventory-container shape (`InventoryGeneric` + sub-tree persistence +
  drop-on-break) verbatim for the Inbox block's new inventory.
- Reuse the Assignment Desk's exact slot-styling values (size, border, background color) and its
  watermark-glyph technique for the restricted slots' hint image, so the two surfaces are
  visually identical without copy-pasting magic numbers into a third place.
- Give the Inbox block a minimal, generalizable second-tab mechanism without rewriting
  `ScribeDialogBase.ViewSwitching.cs`'s existing Assignment/Inbox pair.

**Non-Goals:**
- No change to how Task Notice items themselves work, how assignments transition state, or the
  `task-notice-item`/`assignment-state-machine` capabilities.
- No copy/paste, import/export, or any document-interpreting behavior on the new inventory —
  storage only, like the Scriptorium's.
- No change to the Assignment Desk's own slots or styling constants beyond exposing them for
  reuse (extract, don't duplicate).

## Decisions

**D1 — Container shape: `InventoryGeneric` on `BlockEntityInbox`, mirroring `BlockEntityScriptorium`.**
8 slots, one `InventoryGeneric(8, null, null, factory)` where `factory` returns
`new ItemSlotTaskNotice(self)` for slot indices 0-3 and a plain `new ItemSlot(self)` for indices
4-7. Persisted under its own sub-tree key (e.g. `"inboxInventory"`), additive per the Scriptorium
precedent — a pre-existing Inbox block simply lacks the sub-tree and loads with 8 empty slots.
Dropped on break via `Inventory.DropAll(...)` in an `OnBlockBroken` override, matching
`BlockEntityScriptorium.OnBlockBroken` exactly.
- *Alternative considered*: a dedicated `ItemSlotInboxOpen` wrapper type for the 4 open slots
  instead of plain `ItemSlot`. Rejected — `ItemSlot` unrestricted is already exactly "any item, no
  restriction"; a wrapper would add a type with no behavior difference from its base.

**D2 — Slot styling: extract the Assignment Desk's slot-style values, don't duplicate them.**
`GuiDialogScribeAssignmentDesk`'s `SlotSize`, `WatermarkScale`, and the `veilColor`/watermark-glyph
`Stack` construction (currently private to `BuildNoticeSlot`) become a small shared helper (e.g. a
`static` method on a shared location such as `ScribeRowWidgets.cs`, taking `ColorScheme`, the
watermark icon name, and whether to show the watermark at all) that both `GuiDialogScribeAssignmentDesk`
and the new Inbox Inventory tab call. The 4 restricted slots pass the same `"scribeassignment"`
icon (task-scroll hint) the Assignment Desk uses; the 4 open slots call the helper with no
watermark, yielding the same size/border/background with no glyph.
- *Alternative considered*: copy the watermark/veil constants into `GuiDialogScribeInbox` directly.
  Rejected per project convention (favor a shared seam over duplicated magic numbers) and because
  the proposal's own ask is for the two surfaces to look identical — a shared helper is what keeps
  them from drifting apart later.

**D3 — Tab switching: add an Inbox/Inbox-Inventory view pair, following the Assignment/Inbox
pattern already in `ScribeDialogBase.ViewSwitching.cs`.**
Add an `IsInboxInventoryView` flag and `OnClickSwitchToInboxInventory()`/`OnClickSwitchToInbox()`
pair scoped to `GuiDialogScribeInbox` (not the shared base — no other surface needs this tab), the
same way the Assignment Desk's own view pair lives in the shared switching file but is only ever
invoked by that one dialog's nav buttons. `BuildRightColNav()` on `GuiDialogScribeInbox` grows a
third button (Inbox / Inbox Inventory / Settings) instead of its current two.
- *Alternative considered*: a fully generic multi-tab nav abstraction shared across every Scribe
  surface. Rejected as over-scoped for a single two-tab addition on one block — the Assignment
  Desk didn't need one either; follow existing precedent, don't build new infrastructure this
  change doesn't need.

**D4 — Layout: keep the Inbox block's existing W × 1.2W bounding box; the active tab's content
region is the same 1:1 square for either tab.**
No new aspect ratio or dimension calculation — `IScribeDocumentHost.GetLayout` is unchanged in
shape, only its doc-comment/spec wording updates (per the `inbox-block` delta) to describe a
2-tab layout instead of a 1-tab one, mirroring the Assignment Desk's own spec wording verbatim.
The 8-slot grid (2 rows of 4, centered) renders inside that same square region.
- *Alternative considered*: a taller bounding box to give the slot grid more breathing room.
  Rejected — the proposal asks for reuse of the existing per-host layout mechanism, and the
  Assignment Desk's 2-tab precedent already establishes that a 1:1 square comfortably hosts very
  different tab content (a create/send form vs. a row list); a slot grid is not more demanding
  than either.

## Risks / Trade-offs

- [Risk] Extracting the Assignment Desk's slot-style helper touches code the Assignment Desk
  currently owns privately → Mitigation: pure extraction (move values/logic to a shared static
  helper, call it from both places), no behavior change to the Assignment Desk's own slots;
  verify visually unchanged after the move.
- [Risk] Growing `BuildRightColNav()`'s button count on the Inbox dialog could crowd the nav
  column at small window sizes → Mitigation: the Assignment Desk already renders 3+ nav buttons
  (Assignment, Inbox, Settings) in the same column width; reuse its spacing/sizing constants
  (`NavButtonSize`, `spacing: 16`) rather than inventing new ones.
- [Risk] A stale mental model treats the Inbox block as permanently single-view (see
  `GuiDialogScribeInbox`'s existing class doc-comment, which states this explicitly) → Mitigation:
  update that doc-comment as part of implementation so the "sole capability" framing doesn't
  mislead the next reader; this is a doc-comment fix, not a spec concern.

## Migration Plan

Purely additive: new inventory sub-tree, new tab, new nav button. A pre-existing placed Inbox
block loads with 8 empty slots (no sub-tree present) and its existing Inbox-tab behavior is
unchanged. No rollback concerns beyond a normal revert — no data format changes to existing keys.
