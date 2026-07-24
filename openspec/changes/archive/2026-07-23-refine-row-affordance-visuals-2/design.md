## Context

`refine-row-affordance-visuals` (committed `c15e5f2`) rebuilt the per-row affordances as
custom-drawn `ScribeHoverIconButton`s: opaque parchment fill, thin ink outline, large icon, single-
line height, far-left grip, and a symmetric focused-input margin. A playtest (report
2026-07-22T13-17-40) confirmed the mechanism but asked for a second refinement pass. This change is
that pass, plus wiring the long-stubbed pin persistence and adding a resting pinned indicator.

Current state, from reading the code:
- Pin, delete, and grip are all the same `ScribeHoverIconButton` (`src/Mod/ScribeBlockRowCell.cs`),
  self-drawing via `BakeButton` into an `ImageSurface`, hover-gated in `RenderInteractiveElements`.
- Pin/delete widths come from `RowTextLayout` (`PinWidth`/`DeleteWidth` = 32, `× TextSizeScale`);
  height is the shared single-line row height, so buttons are not square; the icon is sized off width.
- There is no minimum-width floor and no pressed state; only an off/on texture (on = pinned fill).
- The ruling is a drawn Cairo line at the row's bottom edge (`ScribeRowElement.DrawRuling`); the
  visible gap is the `RulingPadding` layout band, not a baked-in image margin.
- `ScribeBlock.Pinned` is persisted (codec v3) and `ScribeDocument.TogglePinned` exists, but the
  editor toggle `OnEditViewTogglePin` is a logging stub that never mutates the model.

Constraints: `src/Core/` must not reference the VS API; no new mod dependencies; new synced state
follows the vanilla Sign / existing done-toggle pattern (`ScribeToggleTaskMessage`).

## Goals / Non-Goals

**Goals:**
- Pin + delete read as one bordered, divided group; grip is chrome-less; buttons are square with a
  minimum size; a click is visibly acknowledged; the ruling hugs its content.
- Pinning actually persists and syncs (editor + read view + multiplayer), reusing the done-toggle path.
- A pinned task is distinguishable at rest, with the on-screen treatment selectable in-game via config.

**Non-Goals:**
- Drag-to-reorder interaction feedback (still owned by the parked `lectern-drag-reorder-feedback`).
- Any HUD / on-screen-outside-the-lectern rendering of pinned tasks (a later tier).
- Sorting or filtering by pinned state — pinning remains purely a flag + indicator here.
- Reworking the checkbox, note text area, or read-view toggle behavior.

## Decisions

**1. Extend `ScribeHoverIconButton` with flags rather than new classes.** Add a `drawChrome` flag
(grip passes `false` → skip fill + outline, draw only the icon) and pressed-state support (a
mouse-down/up override + a light overlay drawn over the baked texture in `RenderInteractiveElements`,
clipped to the rounded rect). Rationale: the class already owns the bake/blit/dispose lifecycle;
a second near-duplicate class would fork that lifecycle. Alternative (separate `ScribeGripElement`)
rejected — the existing unused `ScribeDragHandleElement` shows how easily a parallel path goes stale.

**2. Group pin + delete as abutted buttons sharing a drawn divider, keeping two hit targets.**
Anchor delete flush-right and pin immediately left of it (no gap) in `RowTextLayout`, draw one outer
rounded-rect outline spanning both and a 1px ink divider between the icons. Keep them as two
interactive elements so `ScribeRowElement.IsInIconGutter` / per-button callbacks still route pin vs.
delete correctly. Rationale: preserves the existing routing and hover-occlusion behavior; avoids a
custom two-action composite element. Alternative (one element hit-testing internally) rejected as
more code for no behavioral gain.

**3. Square buttons + a `MinAffordanceButtonSize` floor.** Drive pin/delete width AND height from one
square dimension = `max(MinAffordanceButtonSize (scaled), singleLineHeight)`, and size both icons from
that shared dimension so they match. Rationale: fixes both "not square" and "too small at 30%" in one
knob; mirrors the existing `MinRowHeight` crash-guard pattern.

**4. Ruling padding → configurable, default the internal band toward zero.** Reduce/zero the
`RulingPadding` contribution in `TopPadFixed`/`BottomOverheadFixed` (`ScribeRowElement.cs`) but keep
`BottomOverheadBandFixed` feeding the floating input's height so the focus highlight keeps its margin.
Rationale: the user liked the symmetric focus margin; only the *ruling's own* padding should go. Kept
as a knob because the user flagged this as "an area to revisit."

**5. Pin persistence mirrors the done-toggle exactly.** Editor: `OnEditViewTogglePin` becomes
`scratchDocument?.TogglePinned(index); isDirty = true; RequestRecompose();` (parallel to
`OnEditViewToggleTask`). Read view / multiplayer: add `ScribeTogglePinMessage` (Pos + BlockIndex,
lock-free like `ScribeToggleTaskMessage`), a `BlockEntityScribeLectern.TogglePinFromReader` calling
`Document.TogglePinned` + `MarkDirty(redrawOnClient: true)`, registered/handled in `ScribeModSystem`.
Rationale: server-authoritative parity with the proven done path; no new persistence design.

**6. Pinned indicator: two config-selectable variants, chosen in-game.** Implement both behind a
config enum/knob: (a) a row-level accent drawn in `ScribeRowElement` compose keyed off `block.Pinned`
(visible both views), and (b) exempting a pinned row's pin button from the `HoverRegion` gate so it
stays visible in its filled "on" look. Rationale: the user chose to decide after seeing them live;
both are cheap and the loser can be dropped when a decision lands.

## Risks / Trade-offs

- [Always-visible pin button vs. hover-occlusion] On a long single-line task, a pinned pin that never
  hides would permanently occlude the text beneath it. → Prefer the row-level accent as the safer
  default for that case; keep the always-show variant off by default until judged in-game.
- [Grouped-button hit-testing drift] Abutting the two buttons risks a 1px seam where neither routes. →
  Derive divider position from the same geometry that sets the two bounds so they tile exactly; verify
  both halves fire in the playtest (this also closes the previously-unverified click-routing item).
- [Removing ruling padding regresses the focus margin] Zeroing the band could let the highlight touch
  the line again. → Keep `BottomOverheadBandFixed` as the input-height source independent of the
  ruling's own padding; verify the focus margin in-game after the change.
- [Sync message proliferation] A second near-identical toggle message duplicates
  `ScribeToggleTaskMessage`. → Accept the small duplication for clarity/parity rather than a generic
  message; matches the codebase's explicit-message style.

## Migration Plan

Pure additive GUI + sync change; no data migration (codec already at v3 with `Pinned`). Deploy by
restaging Debug and relaunching the client. Rollback is reverting the commit — persisted pinned flags
written meanwhile remain valid (they were always part of the saved document).

## Open Questions

- Final pinned-indicator treatment (row accent vs. always-show button vs. both) — deferred to the
  in-game decision, per the user.
- Exact resting-accent form (glyph vs. dot vs. tinted edge vs. row tint) — settle during the accent
  task, tunable via config.
