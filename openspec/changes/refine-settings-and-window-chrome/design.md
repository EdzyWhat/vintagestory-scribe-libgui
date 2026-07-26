## Context

The Lectern is now a notebook-framed dialog (`scribe-notebook-frame`): an `OuterArtBox` with a
`TitleBar` band as its drag zone and a right-column nav stack, both built from the mod's registered
SVG icons (`scribegrip`, `scribegear`, `scribeedit`, `scribepin`, `scribeclose`). The Scribe Settings
form (`ScribeSettingsContent`, hosted by the standalone `ScribeSettingsDialog` and reached from both
the Lectern gear and the HUD gear via `ScribeModSystem.OpenSettings`) is a host-agnostic widget with
two sections (Behavior, Appearance) whose numeric fields are `ScribeNumericField`s built by the
`NumericField`/`IntField`/`FontScaleField` helpers.

A playtest flagged five rough edges, none of them new features:
- The draggable `TitleBar` band gives no visual cue that it is draggable.
- The numeric fields clamp in the caller's `onChanged` on EVERY parseable keystroke (Core's
  `Normalized()` is the single clamp source, applied through the write-through rebuild that also
  remounts the `ValueKey`-keyed field). So clearing the field to retype snaps it to a bound the moment
  the running value crosses it — you cannot select-all and type a fresh number. This is a PRE-EXISTING
  `add-settings-tab` behavior (the field's own doc-comment already admits "typing a value whose running
  prefix is below the min briefly clamps mid-type"); the new Pixel Art Size field made it obvious.
- The form is a flat two-section list on a fully transparent `WindowFrame` child.
- The gear buttons only `TryOpen`; there is no toggle-closed.

Constraints: `src/Core/` must never reference the VS API (the `Clamp*` statics + `Min*/Max*` consts
already live there and stay the range source of truth); no new mod dependencies; client-local prefs
persist via the existing JSON config; favor clear, conventional solutions.

## Goals / Non-Goals

**Goals:**
- Make the draggable title-bar band discoverable with a `scribegrip` icon left of the close button.
- Let a player select-all and retype a numeric value without a mid-edit snap: clamp on UNFOCUS, and
  tell them (helper/error text) when a value was clamped and what the valid range is.
- Reorganize Scribe Settings into three dividers-separated sections: Mod Behavior, Window Appearance,
  HUD Appearance.
- Give the settings form a real window panel by painting the theme's default surface color behind it.
- Make both gear entry points toggle the settings window open AND closed.

**Non-Goals:**
- Any new preference or any change to what the numeric ranges ARE (only WHEN they clamp + feedback).
- Rewriting `ScribeNumericField` beyond the clamp-timing seam + feedback text.
- Re-theming the Lectern or HUD, or changing the standalone window's theme inheritance (it still
  follows the player's global LibGUI theme — see `scribe-themed-toggle`).
- Any `src/Core/` VS-API coupling; no change to `Normalized()` semantics.

## Decisions

### D1: Drag-grip icon left of the close button in the TitleTextButtons row
In `GuiDialogScribeLecternLibGui.BuildTitleBar`, add a `scribegrip` `ScribeVsIconGlyph`/`TitleButton`
immediately to the LEFT of the existing `scribeclose` button in the right-aligned trailing group. It
is a passive affordance (a grip cue), tinted `OnSurfaceVariant`, with a "drag to move" tooltip; the
actual drag is still owned by `WindowConfig.DragHandleHeight` over the whole band, so the grip needs no
gesture handler of its own.
- *Alternative rejected — a separate centered grip glyph in the band:* the row already right-aligns the
  button group, so placing the grip in that group (left of close) keeps one consistent cluster and
  needs no new layout box.

### D2: Clamp on unfocus, driven by a clamp callback + range descriptor passed to the field
`ScribeNumericField` gains two optional inputs: a `Func<float,float> clamp` (the field applies it when
it loses focus) and a `string rangeText` (shown as helper/error text when the committed value was
clamped). On blur the field parses its text, applies `clamp`, and — if the clamped result differs from
what the player typed — rewrites the field to the clamped value, fires `onChanged` with it, and shows
the range text as an error/helper line beneath the field until the next successful edit. During typing
the field NO LONGER clamps: `onChanged` still fires the raw parseable value for live preview, but the
snap is deferred to blur so select-all-and-retype works.
- The clamp math stays in Core: the helpers pass `ScribePlayerSettings.ClampHudMaxRows`,
  `ClampHudRowWidth`, `ClampHudOffset`, `ClampFontScale` (as a percent-aware wrapper), and
  `ClampPixelArtSize`. Core is unchanged; only the field's TIMING (a Mod UI concern) moves.
- *Alternative rejected — keep clamping in `onChanged` but debounce it:* a timer-based debounce is
  fragile (rebuild timing, focus churn) and still snaps while the field is focused; blur is the natural,
  conventional commit boundary and matches how the field already treats unparseable text.
- *Alternative rejected — clamp in Core `Normalized()` only, never in the field:* the persisted value
  must still be bounded, so `Normalized()` stays the authority on write; the field-level clamp-on-blur is
  what gives the player the retype window and the feedback text before the value reaches the store.

### D3: Focus/remount interaction with the existing `ValueKey` + `ScribeNumericFocusRegistry`
The fields are uncontrolled and remounted via a `ValueKey<int>` when the persisted value changes; focus
survives via the host-owned `ScribeNumericFocusRegistry`. Clamp-on-blur fits this: because the clamp now
fires ON blur (focus already leaving), the subsequent write-through rebuild remounts the field seeded
from the clamped persisted value with no focus to preserve — so the existing key-remount path shows the
clamped result without fighting the focus registry. The `onStepped`/arm-autofocus path for +/- and arrow
keys is unchanged (those still write through live; stepping already stays within bounds via the buttons).

### D4: Three sections with dividers, controls re-sorted
Split `ScribeSettingsContent.Build` into three `SectionTitle` + section-body pairs separated by a
horizontal divider widget (a thin full-width `Container`/line in `OnSurfaceVariant`):
- **Mod Behavior**: completion policy dropdown; the HUD-collapsed `HuggingCheckbox`.
- **Window Appearance**: Pixel-Art Display toggle; Pixel Art Size; window font scale.
- **HUD Appearance**: HUD anchor; HUD max rows; HUD row width; HUD X/Y offsets; HUD font scale.
The existing `PairedControls` groupings are re-formed within their new home section (e.g. HUD max rows +
row width stay paired under HUD Appearance; window font scale moves to Window Appearance so it no longer
pairs with HUD font scale). Lang keys: rename the two section titles and add the third
(`settings-section-modbehavior`, `settings-section-windowappearance`, `settings-section-hudappearance`).

### D5: Paint the theme's default surface behind the form
Wrap the `ScribeSettingsContent` (inside `ScribeSettingsDialog.Build`, under the `WindowFrame`) in a
`Container` whose `BoxStyle.Color` is the active theme's `ColorScheme.Surface` (read from
`Theme.Of(context)` / `ThemeData.Default`, the same global theme the window frame already follows), so the
inputs sit on a real panel. The window keeps inheriting the player's global LibGUI theme (unchanged from
`scribe-themed-toggle`); only the transparent gap behind the form is filled.
- *Alternative rejected — set the color on the `WindowFrame` itself:* the frame deliberately reads
  `ThemeData.Default` with no explicit colors so it follows the global theme; wrapping the child in a
  surface `Container` fills the body without overriding the frame's chrome.

### D6: `OpenSettings` becomes a toggle
`ScribeModSystem.OpenSettings()` closes the settings window if it is already open, else opens it:
```
settingsDialog ??= new ScribeSettingsDialog(capi, this);
if (settingsDialog.IsOpened()) settingsDialog.TryClose();
else                          settingsDialog.TryOpen();
```
Both call sites (the Lectern right-column gear and the HUD gear) already route through `OpenSettings`, so
the single change covers both. The lazily-built, reused dialog instance is unchanged.
- *Alternative rejected — a separate `ToggleSettings` method:* both entry points want identical
  toggle behavior and there is no remaining caller that needs open-only, so folding the toggle into the
  one shared method keeps a single settings entry point (its existing design intent).

## Risks / Trade-offs

- [Deferring the clamp to blur means the live-preview `onChanged` can briefly drive an out-of-range value
  into an open dialog (e.g. a huge Pixel Art Size while mid-type)] → The raw value only previews; the
  persisted value is still bounded by `Normalized()` on the write that blur triggers, and the range is
  finite/typed so a transient large value can't corrupt state. Accept the brief preview.
- [A field could lose focus via the window closing rather than a deliberate blur, skipping the clamp] →
  `Normalized()` on persist is the backstop: whatever the field last wrote is clamped on load, so an
  un-blurred out-of-range value can never persist.
- [The surface `Container` could tint the form differently under a dark global theme than the light
  Pixel-Art theme] → Intended: the standalone window follows the global theme by design; `Surface` is the
  correct role for a body panel in either theme.
- [The grip icon could be mistaken for an interactive button] → Tooltip says "drag to move"; it reuses the
  passive-affordance styling (variant tint, no hover-press chrome).

## Migration Plan

Purely additive/behavioral; no data or config migration. New/renamed lang keys ship in `en.json`
(old `settings-section-behavior`/`-appearance` keys are replaced by the three new titles). Rollback =
revert the mod-layer edits; Core and persisted config are untouched. Verification is in-game only.

## Resolved Questions (were open; settled during planning 2026-07-26)

- **Clamp feedback style → "error only after clamp" (user decision).** The range text (`⚠ <range>`, in
  `ColorScheme.Error`) appears beneath a field ONLY when a blur actually clamped an out-of-range value, and
  clears on the next edit. It is rendered by the stateful `ScribeNumericField` itself (the host
  `ScribeSettingsContent` is a `StatelessWidget` and cannot hold "was clamped" state; only the field knows
  typed-vs-clamped). Rationale: quietest option, and each field's range already lives in its hover-help
  tooltip, so a persistent always-on line would duplicate it and add chrome under every numeric field.
- **Divider styling → LibGUI's built-in `Divider` widget** (`Gui.Widgets.Basic.Divider`), which defaults to
  a 1px line in the theme's `ColorScheme.Border` and stretches to the `Column` width. No hand-rolled
  `Container` needed.

## Implementation refinement to D2 (discovered while wiring the field)

The mid-edit snap was NOT the field clamping — the field never clamped. It was the HOST's write path: every
parseable keystroke fired `onChanged` → `UpdateMySettings` → `Normalized()` (clamp) → the field's `ValueKey`
wrapper took the clamped value → the uncontrolled field remounted, re-seeding to the clamped text. So "fire
`onChanged` with the raw value during typing for live preview" is incompatible with "no snap" — that write
IS the snap trigger. Resolution: while a field is focused, typing edits ONLY local text and does NOT write
through; the commit (parse → `Clamp` → `onChanged`, once) is deferred entirely to blur. The +/- buttons and
arrow keys STILL write through live (they are always in range), so live preview survives for the common
tuning gesture; you simply don't get a per-digit window relayout while typing (which would be janky anyway).
The `ValueKey` remount and `ScribeNumericFocusRegistry` focus-preservation (D3) are unchanged for stepping.
