## Why

The standalone Inbox block can only be opened, viewed, and left — there is nowhere near it to
actually hold the Task Notice items that arrive there, or to stash general inventory while
managing assignments. A second tab giving the block its own small inventory turns it into a
real waypoint for physically-delivered assignments (see `add-assignment-physical-delivery-mode`)
instead of a read-only mailbox.

## What Changes

- Add a new **Inbox Inventory** tab to the standalone Inbox block: 8 slots laid out as 2 rows of
  4, centered horizontally and vertically in the tab's content region.
- Of the 8 slots, 4 accept ONLY Task Notice items (`ItemScribeTaskNotice`); the other 4 accept
  any item with no restriction.
- All 8 slots share the Assignment Desk's existing slot visual styling (size, border color,
  background color); additionally, the 4 restricted slots show the same background-image
  treatment the Assignment Desk uses to hint at expected item type, while the 4 open slots show
  no such image.
- Slot contents are server-authoritative and persist/sync via the block's existing vanilla
  Sign-pattern storage, matching the Scriptorium inventory's precedent.
- The Inbox block gains a nav-tab switcher (Inbox / Inbox Inventory) where today it has none —
  its dialog is currently architected around always showing exactly one view.
- The Inbox block's dialog dimensions/layout requirement is revised to accommodate a second tab,
  following the Assignment Desk's existing 2-tab layout precedent rather than the current
  single 1:1-square-content assumption.

## Capabilities

### New Capabilities
- `inbox-inventory`: the Inbox block's 8-slot inventory (4 restricted to Task Notice items, 4
  open), its tab, its visual styling, and its persistence/sync — mirrors `scriptorium-inventory`'s
  shape for a Scribe-items-only inventory, adapted for a mixed restricted/open slot layout scoped
  to Task Notice items specifically.

### Modified Capabilities
- `inbox-block`: today's spec states the Inbox block's "sole capability is showing the shared
  Inbox tab" with no other tab ever present, and fixes its dialog to a single 1:1-square content
  region sized for the Inbox row list alone. Both requirements change: the block gains a second,
  non-creation tab (Inbox Inventory), and its layout requirement is revised to a 2-tab shape.
  The existing "no create-and-send capability" requirement is unaffected (the new tab is storage,
  not creation) and is not modified.

## Impact

- **Affected code**: `BlockEntityInbox.cs`, `BlockInbox.cs`, `GuiDialogScribeInbox.cs` (nav
  switcher, new view state, new tab content), a new slot-building path alongside the Assignment
  Desk's existing `ItemSlotTaskNotice`/`ItemSlotStyle` usage (`GuiDialogScribeAssignmentDesk.cs`)
  for styling reuse, and `IScribeDocumentHost.GetLayout` for the Inbox block's revised dimensions.
- **Affected specs**: new `specs/inbox-inventory/spec.md`; delta to `specs/inbox-block/spec.md`.
- **Persistence**: additive to `BlockEntityInbox`'s existing `ToTreeAttributes`/
  `FromTreeAttributes` — a pre-existing placed Inbox block SHALL load with an empty inventory
  (same additive-safety precedent as `scriptorium-inventory`).
- **No Core changes**: this is pure Mod-layer inventory/UI/persistence, matching the
  Scriptorium inventory's precedent (`src/Core/` untouched).
