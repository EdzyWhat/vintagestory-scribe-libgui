## Context

The tablet is one item with a `material` variant (`clay-red`, `clay-blue`, `clay-fire`, `wax`) and a drying state
(wet, hard, fired; wax is wet only). Each visual concern has its own switch on the material string.

## Goals / Non-Goals

**Goals:** one place resolves material to style; one fallback; a matrix test that proves nothing moved for
shipped variants.

**Non-Goals:** changing any color, backdrop, ink or layout value; generalizing to non-tablet surfaces; changing
the item variants or save data.

## Decisions

- **Facade, not a merge of the tables.** `TabletStyle.For(material, state)` returns a small record composed from
  the existing tables (`TabletReadability`, clay palette, `ScribeBackdropSpec`, quest-link accent, layout
  proportions). The tables keep their data and become keyed by a private material enum or normalized key, so the
  string is parsed exactly once.
- **Fallback is red.** The item already defaults to red when the variant is missing, and the backdrop already falls
  back to red, so red is the common choice. This changes ink, glow and palette for unrecognized materials only.
- **Wax is a style property** (`IsWax`, wet-only) rather than a string compare in `TabletHost`.
- **Test first.** The matrix scenario asserts the current outputs for all 10 variants against the existing
  entry points, lands green, and is then pointed at the facade.

## Risks / Trade-offs

- A mistaken mapping would show the wrong ink or backdrop. Mitigated by the matrix test and a playtest of all 10
  variants.
- Layout proportions live in `TabletHost`; moving them risks a layout shift. Mitigated by moving values verbatim
  and covering them in the matrix.
