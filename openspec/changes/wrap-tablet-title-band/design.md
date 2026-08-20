## Context

The Tablet title band is built by `ScribeDialogBase.BuildTitleBar` (`ScribeDialogBase.Layout.cs`):
a `W × TitleBarH` (`0.13·H`) drag band holding a bottom-anchored `TitleBtnsW × TitleBtnsH`
(`0.75W × 0.065H`) row. That row is a `Row(SpaceBetween)` of an `Expanded` title slot on the left and
a min-width trailing group (pencil · drag-grip · close) on the right. The title slot's widget is
supplied by two overridable hooks:

- `BuildTitleDisplay(displayTitle, titleStyle)` — resting. Base default: single-line `RichText` with
  `TextOverflow.Ellipsis`. Tablet override (`GuiDialogScribeTablet.cs:225`): `Clip( CuneiformText(…) )`
  — a single-line cuneiform render, hard-clipped to the band width (cuneiform has no `…` glyph).
- `BuildTitleField(titleStyle)` — editing. Base default: single-line `TextField`. Tablet override
  (`GuiDialogScribeTablet.cs:249`): `ScribeCuneiformTitleField(…)`, which wraps `ScribeCuneiformField`
  with `singleLine: true` (`ScribeCuneiformTitleField.cs:347`).

`ScribeCuneiformField` already supports wrapping: with `singleLine: false` it calls
`layout.LayoutWrapped(text, maxWidthGrid)` and sizes its height to `lines.Count · lineHeightPx`
(`ScribeCuneiformField.cs:181-199`). There is no built-in line cap — wrapping yields as many lines as
the text needs. `wrap-tablet-item-titles` already routes item-row labels through this wrapping path;
this change extends the same technique to the title band, with a 2-line ceiling.

The band is deliberately taller than its content row: `TitleBarH = 0.13·H` versus
`TitleBtnsH = 0.065·H`, and the content row is bottom-anchored (`Align.BottomCenter`), so roughly one
line-height of vertical slack already sits above the single-line title within the band.

## Goals / Non-Goals

**Goals:**
- A long Tablet title wraps to a **maximum of 2 lines** (resting and editing) instead of clipping to
  one; a title beyond two lines clips at the end of line 2 (existing clip behavior, one line lower).
- A short (one-line) title is visually identical to today — no band-height change, no shift.
- Reuse the existing wrapping cuneiform renderer; no new widget, no `gui` fork, no `Core` change.
- Keep the drag/pencil/close chrome centered and clear of the wrapped text.

**Non-Goals:**
- No change to Lectern/Notebook/Scriptorium/HUD title chrome (they stay single-line ellipsis; not
  reported as clipping). Base `BuildTitleDisplay`/`BuildTitleField` are untouched.
- No unbounded growth — 2 lines is a hard cap (matches the author's ModDB commitment).
- No new lang strings, no persisted setting, no cuneiform-geometry/jitter/reveal change.

## Decisions

- **Cap at 2 lines by clipping, not by a widget line-limit.** Switch the tablet's title renderers
  from single-line to the wrapping path (`singleLine: false`), and keep the resting title's enclosing
  `Clip` but give it a height budget of two line-heights. Wrapping still produces N lines internally;
  the `Clip` shows the first two and clips the rest. *Alternative considered:* add a `maxLines` param
  to `ScribeCuneiformField` to truncate `lines` to 2. Rejected for now — it touches a shared,
  well-tested widget and the clip-to-height approach needs no widget change; revisit only if the
  overflow-clip on the third line reads badly (a half-line sliver).
- **Grow the title slot into the band's existing vertical slack; do NOT change the shared
  `TitleBarH`/`TitleBtnsH` metrics.** The band (`0.13·H`) already has ~one line of headroom above the
  bottom-anchored content row (`0.065·H`). Let the tablet's two-line title occupy that headroom by
  sizing the title slot to up to two line-heights and keeping it bottom-aligned so line 1 grows
  upward. *Why not grow `TitleBarH`?* It is computed in `ScribeLayout` and shared by every surface;
  growing it there would enlarge the Lectern/Notebook bands too (a non-goal) and would need a
  per-surface conditional in the layout core. Keeping the growth inside the tablet's slot override
  confines the change to the tablet and to `BuildTitleBar`'s slot sizing. **The exact vertical budget
  is an in-game calibration** (measure, don't theorize): confirm two lines fit within `TitleBarH`
  without overrunning the band top or colliding with the pencil/grip/close group; if two full lines
  don't fit in the current slack, the fallback is a tablet-only band-height bump plumbed as an
  override (documented below), not a change to the shared metric.
- **Both resting and editing wrap** (consistent behavior; the band looks the same whether or not the
  pencil is active). Enter still commits via the existing `OnTitleFieldKeyDown` (it intercepts Enter
  before a newline is inserted), and the maxlength gate is unchanged — a 2-line wrap is purely a
  render/layout property of the same bounded text buffer, so no controller/commit machinery changes.
- **Keep the fixed title jitter seed** (`TitleJitterSeed`) so the wobble stays stable across rebuilds
  while typing — wrapping does not change the per-character seed model.

## Risks / Trade-offs

- **Two lines may not fit the current band slack** → the primary approach clips or crowds. Mitigation:
  the in-game calibration task gates this; the documented fallback is a tablet-scoped band-height
  override (a virtual `TitleBarHeightFor(...)` or an extra tablet-only pad) rather than editing the
  shared `ScribeLayout` metric.
- **Vertical centering of the chrome** (pencil/grip/close) is `CrossAxisAlignment.Center` against a
  now-taller title slot → the buttons could drift up when the title is two lines. Mitigation: anchor
  the title slot's growth downward (bottom-aligned) and verify the trailing group stays put in-game;
  if it drifts, pin the trailing group to the band bottom independently of the title slot height.
- **Third-line clip sliver** → a title just over two lines' width could show a clipped partial third
  line. Mitigation: the clip height is an exact multiple of the line-height so no partial line renders;
  confirm in-game. If a sliver still appears, adopt the rejected `maxLines` widget cap.
- **Cuneiform vs. fallback parity** → when cuneiform is disabled or the glyph bundle isn't loaded, the
  tablet falls back to the base single-line `RichText`/`TextField`. Trade-off accepted: the wrap is a
  cuneiform-surface refinement; the readable fallback keeping its existing ellipsis is fine and avoids
  touching the shared base path.

## Open Questions

- Does the current band slack hold two full lines, or is the tablet-only height bump needed? Resolved
  by the in-game calibration task, not up front.
