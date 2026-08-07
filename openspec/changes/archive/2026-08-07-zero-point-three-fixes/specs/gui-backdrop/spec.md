## ADDED Requirements

### Requirement: A textured backdrop always renders at full opacity

A themed-mode textured backdrop SHALL render at the opacity authored into its PNG, independent of what any
prior frame drew. The backdrop-wrapping logic SHALL guarantee this even though the underlying GUI framework
reuses a single shared paint across draw operations and across frames and its textured-box draw op reuses
that paint's color without re-setting it — so an unguarded backdrop would be modulated by whatever color the
previous frame's last draw op happened to leave (e.g. a read-only view whose last painted element is a
low-alpha scrollbar track, which uniformly faded the backdrop). The guarantee SHALL hold for every themed
view regardless of which element paints last, and SHALL NOT alter the appearance of any view that was
already rendering correctly.

#### Scenario: A read-only view's backdrop is fully opaque

- **WHEN** the player opens a themed-mode view whose last-painted element is a low-alpha element (such as
  the always-visible scrollbar track on a read-only tablet)
- **THEN** the backdrop renders at its authored opacity rather than being modulated toward transparency by
  the prior frame's residual paint color

#### Scenario: Correctly-rendering views are unchanged

- **WHEN** the player opens a themed-mode view that already rendered its backdrop opaquely (such as the
  editor or a tabbed Lectern/Notebook view that paints an opaque element last)
- **THEN** its backdrop appearance is unchanged
