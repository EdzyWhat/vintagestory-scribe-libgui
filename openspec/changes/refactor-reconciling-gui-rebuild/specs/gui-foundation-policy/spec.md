## ADDED Requirements

### Requirement: GUI content updates use reconciliation, not full-tree rebuild
Scribe's GUI hosts SHALL update changing content through the framework's reconciling rebuild path —
a persistent content widget whose state is updated in place (so matching subtrees and their state,
animation controllers, and render objects are preserved) — rather than by unmounting and recreating
the entire widget tree. A full-tree rebuild SHALL be used only where a genuinely different widget
tree is being shown (e.g. switching between distinct views, seeding a fresh editor) or for
development hot-reload — never as the routine mechanism for reflecting a state change within the same
view.

#### Scenario: A content change reconciles rather than rebuilding the whole tree
- **WHEN** a state change alters content within an already-shown view (a list item's data changes, a
  row is added or removed, a form value updates)
- **THEN** the host updates the affected content in place through reconciliation, preserving the
  surrounding tree's state, and does NOT unmount and recreate the entire tree

#### Scenario: Full-tree rebuild is reserved for genuinely new trees
- **WHEN** the host must show a genuinely different tree (switching views, seeding a fresh editor) or
  is performing development hot-reload
- **THEN** a full-tree rebuild is acceptable for that transition

#### Scenario: Implicit animations are not defeated by the update path
- **WHEN** content that carries an implicit animation is updated within a view
- **THEN** the update path preserves the animation's state so it animates, rather than discarding and
  recreating it (which would make it snap to its target)
