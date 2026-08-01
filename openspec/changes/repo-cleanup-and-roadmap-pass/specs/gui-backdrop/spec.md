## MODIFIED Requirements

### Requirement: Each item and view declares its own backdrop art of any size
The mod SHALL model a Scribe dialog backdrop as a per-item, per-dialog specification that names a texture
asset by `AssetLocation` and makes NO assumption about the art's pixel dimensions. Each item (Lectern,
plain Notebook, and Clockmaker's Notebook now; Desk / Clay Tablet later) SHALL be able to declare its own
backdrop specification. Adding a new item's backdrop SHALL require only a new specification plus its PNG —
no change to the dialog's backdrop-wrapping logic or the bitmap cache.

#### Scenario: An item declares its backdrop
- **WHEN** a new item's backdrop is added to the backdrop-specification holder
- **THEN** the item contributes one specification naming its own texture `AssetLocation`, without any
  shared-size constraint or change to the backdrop-wrapping logic or the bitmap cache

#### Scenario: A backdrop of any dimensions is accepted
- **WHEN** a backdrop specification names a PNG whose dimensions differ from another item's PNG
- **THEN** both are handled by the same dialog backdrop-wrapping logic, and neither is required to match
  a fixed or shared backdrop size

#### Scenario: Distinct items render distinct art
- **WHEN** the player opens the Lectern, the plain Notebook, and the Clockmaker's Notebook with
  `PixelArtDisplay` ON
- **THEN** each draws its own declared backdrop specification, even where two items share an underlying
  dialog host

## REMOVED Requirements

### Requirement: The mechanism supports distinct per-view backdrops
**Reason**: The per-view (read/editor page vs. settings page) distinct-backdrop path was reserved but
never wired. The in-dialog settings view was removed in the 2026-07-25 pivot (the gear opens the
standalone settings window, which follows the global theme and is not backdrop-wrapped), so no item ever
exposed two backdrop-bearing views. The reserved `LecternSettings` specification and the standalone `Wrap`
helper that this requirement described are being deleted as dead code. The live capability — each item
declaring and drawing its own single body backdrop — is retained under "Each item and view declares its
own backdrop art of any size."
**Migration**: None. No shipped behavior changes: every dialog already draws its single `host.BackdropSpec`
via the dialog's own wrapping logic. A future item that genuinely needs a second backdrop-bearing view can
reintroduce a per-view specification at that time.
