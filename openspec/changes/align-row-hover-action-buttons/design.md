## Context

See proposal.md for the reported symptom. Read, Editor, and Pinned rows place hover actions with
`ScribeRowControlNudge.FloatingButtonTop`. That helper centers a drawn `ScribeRowButton` box against
`SingleLineInputHeight(style)` for every row. Item names use a separate first-line band, and the
cuneiform path expands that band through `ItemNameLineHeight(style)`. The quest-marker alignment fix
already established `ItemNameLineHeight` as the shared source for this distinction.

## Goals / Non-Goals

**Goals:**
- Give every floating row action the same vertical center as its row's first rendered text line.
- Reuse the established cuneiform-aware item-name measurement.
- Keep one shared offset calculation across the three affected surfaces.

**Non-Goals:**
- Changing button chrome, colors, size, horizontal spacing, glyph scale, or interaction behavior.
- Moving non-hover controls such as completion checkboxes, quest markers, and drag grips.
- Adding row actions to surfaces that do not currently offer them.

## Decisions

**D1: Extend the shared offset helper with row-layout context.**
`FloatingButtonTop` will accept whether the row uses item content. Ordinary rows retain their
existing input-band calculation. Item rows use the same cuneiform-aware item-name line height that
centers quest markers and grips, combined with the row's existing top padding. Each call site already
knows its row kind, so no model or persistence field is needed.

An alternative would place separate formulas in Read, Editor, and Pinned. A shared helper keeps the
drawn-box correction and font-scale behavior consistent as styles change.

**D2: Update every floating row-action call site together.**
The Read Pin, Editor Delete/Pin pair, and Pinned Delete/Unpin pair all use the same calculated top.
Paired controls continue sharing one local value, which keeps their centers identical.

An alternative would patch only the cuneiform surface that exposed the issue. All three surfaces
render the same row kinds and can reach the same calculation, so complete call-site coverage avoids
surface-specific drift.

## Risks / Trade-offs

- **[Risk]** Item-row classification differs between the three row snapshots. → **Mitigation:** use
  each snapshot's existing `IsItemKind` or equivalent property and verify every call site by search.
- **[Risk]** A new center calculation could increase row height. → **Mitigation:** position remains
  an overlay offset inside the existing Stack; verification checks layout dimensions stay unchanged.

## Migration Plan

No migration is required. The change affects client-side layout only and rolls back through a normal
code revert.
