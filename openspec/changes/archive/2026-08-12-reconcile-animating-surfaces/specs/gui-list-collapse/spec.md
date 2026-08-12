## ADDED Requirements

### Requirement: On a reconciling host, hover and click activation hold via preserved identity
When the collapse mechanism runs on a surface that updates by reconciliation (rather than
`ForceRebuild`), the hover-currency and click-activation guarantees SHALL hold because the elements
under the cursor are preserved across the update, NOT via a per-frame hover re-dispatch or a
post-rebuild hover-refresh latch. Specifically, an element that slides beneath a stationary cursor as
rows reflow SHALL retain its hover state, and a row's control that is pressed SHALL remain the same
element at release so its activation is recognized, without the user moving the mouse.

#### Scenario: Hover persists mid-collapse without a refresh latch on a reconciling host
- **WHEN** a row collapses on a reconciling surface and a different row slides up beneath a stationary
  cursor
- **THEN** the row beneath the cursor shows its hover-gated controls because its element (and hover
  state) is preserved by reconciliation, with no per-frame re-dispatch required

#### Scenario: Consecutive mid-collapse deletes each register on the first click
- **WHEN** the user deletes a row and then, without moving the cursor, clicks the delete control of the
  row that has slid under the cursor while its collapse is still animating — repeatedly
- **THEN** each delete registers on the first click, because the delete control's element is preserved
  across the reconcile so the pressed and released element are the same and the activation is
  recognized (superseding the moving-target/rebuild-divide race)
