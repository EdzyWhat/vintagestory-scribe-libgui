## Context

Scribe's per-player preferences live in `src/Core/ScribePlayerSettings.cs`, persisted client-locally as
`scribe-hud-config.json` and held mutable in `ScribeModSystem` (`MySettings`, `UpdateMySettings`, which
normalizes → `StoreModConfig` → fires `MyPinsChanged`). The pinned-task HUD (`HudScribePins`) and the
lectern dialog (`GuiDialogScribeLecternLibGui`) both read `MySettings` and rebuild on `MyPinsChanged`, so
a write already fans out live. Today those preferences (completion policy, HUD rows/anchor/offsets/width/
collapse) are only changeable by hand-editing the JSON; `add-pinned-task-hud` deferred the picker UI, and
two in-game tests (runtime policy switch; non-default anchor/offsets/width) are backlogged on it.

Separately, `ScribeClientConfig` (`scribe-client-config.json`, in `src/Mod`) is a read-only, re-read-per-
open tuning file with no write path, referenced only by `ScribeRowStyle.cs` and the lectern dialog. Of its
~35 fields, only the set consumed by `ScribeRowStyle.FromConfig` is live (`TextSizeScale`, `RowFontSize`,
row paddings, checkbox size, field paddings, pinned-tint RGBA); the other ~25 are dead leftovers from the
pre-LibGUI native renderer (grep-confirmed no consumers). The window text size the user wants to expose is
exactly `RowFontSize * TextSizeScale`.

The settings surface must be LibGUI, not ConfigLib: ConfigLib's ImGui panel is broken on Apple Silicon
(the dev machine) — the reason this in-mod surface exists (`add-pinned-task-hud` D4). The `gui` LibGUI dep
already ships every control needed: `Dropdown<T>`, `Slider`, `NumericField`, `Checkbox`, `Tooltip`,
`TabView`, `WindowFrame`, plus Column/Row/Expanded/Padding/SizedBox (verified in `reference/vslibgui`).
Core must stay VS-API-free (unit-testable); UI lives in `src/Mod`.

## Goals / Non-Goals

**Goals:**
- Expose every per-player preference through an in-game LibGUI surface that writes the same client-local
  config via `UpdateMySettings` — no JSON hand-editing — unblocking the two backlogged tests.
- Add HUD and window font-size scaling, both live.
- Consolidate to a single client-local player config; delete `ScribeClientConfig` and its dead fields.
- Make the surface host-agnostic so future block/item dialogs (Desk, Notebook) embed the same widget.

**Non-Goals:**
- Any server-wide / world balance setting (e.g. gating the HUD behind a crafted item) — a separate future
  server config, never the per-player settings surface.
- A ConfigLib panel, a standalone settings window, or a settings hotkey.
- A spatial 3×3 anchor grid (a dropdown is used).
- Reviving any deleted `ScribeClientConfig` knob (paddings, ruling, affordance colors, inspect overlay) as
  a user control — those become code constants or are removed.
- The in-game error-surface roadmap item; per-anchor offset auto-detection (already rejected upstream).

## Decisions

### D1: One consolidated Core config; delete `ScribeClientConfig`
Fold the still-live font knob into `ScribePlayerSettings` as two multiplier fields, `HudFontScale` and
`WindowFontScale`. Both are **5-notch discrete scales**: -20%, -10%, 0% (default), +10%, +20% — i.e.
`{0.8, 0.9, 1.0, 1.1, 1.2}`, default `1.0`, clamped/snapped in `Normalized()` via a new `ClampFontScale`
(clamp to `[0.8, 1.2]` and snap to the nearest 0.1). The two user-facing settings are named `HudFontScale` (scales the pinned-task HUD's text) and
`WindowFontScale` (scales the block/item window text — Lectern now, Desk/Notebook later); `WindowFontScale`
supersedes the old `TextSizeScale`. Each scale multiplies a descriptively-named base-size constant:
`BaseWindowFontSize` (= the old `RowFontSize`, 15) and `BaseHudFontSize` (= the HUD's current hardcoded 16),
so window text = `BaseWindowFontSize × WindowFontScale` and HUD text = `BaseHudFontSize × HudFontScale`. The
old confusing `RowFontSize` name is retired — nothing user-facing carries a raw config-field name.

**Which `ScribeClientConfig` fields are live today** (grep-verified — only the set `ScribeRowStyle.FromConfig`
reads): `TextSizeScale`, `RowFontSize`, `RowVerticalPadding`, `RowHorizontalPadding`, `RowCheckboxTextGap`,
`RowCheckboxSize`, `FieldInnerPaddingX`, `FieldInnerPaddingY`, and `PinnedRowTint{R,G,B,A}` — 11 fields of
~35. Only the font becomes a user setting (`TextSizeScale` → `WindowFontScale`, `RowFontSize` →
`BaseWindowFontSize` constant). The row paddings, checkbox size, and field paddings become **inlined code
constants** in `src/Mod`. The pinned-row tint is NOT re-inlined as a constant color — it is **derived from
the active LibGUI theme** (see D1b). Every other `ScribeClientConfig` field (`Ruling*`, `Affordance*`,
`TaskRowHeight`, `RowListWidth`, `VisibleListHeight`, `ToggleWidth`, `PinnedIndicatorMode`,
`InspectOverlayMode`, etc. — ~24 fields) has zero live consumers and is deleted. `ScribeClientConfig.cs` is
deleted outright. `ScribeRowStyle.FromConfig` is retargeted to take `ScribePlayerSettings`
(window font = `BaseWindowFontSize × WindowFontScale`); its long-standing doc-comment already anticipated
this split at that single chokepoint.

### D1b: Pinned-row tint is derived from the LibGUI theme, not a stored color
The resting pinned-row tint SHALL be a slightly-transparent transform of the active theme's `Primary`
color (`Theme.Of(context).ColorScheme.Primary` at a low alpha), computed at build time in the row widget,
rather than the four `PinnedRowTint{R,G,B,A}` config constants (which are dropped). This respects theming —
switching the LibGUI theme re-tints pinned rows automatically — and removes the tint from `ScribeRowStyle`
entirely (the row already has the `BuildContext` to read the theme, exactly as it reads `colors.Primary`
for the editor pin glyph today). Alternative rejected: re-inlining the amber tint as a fixed constant color
(ignores the theme, the thing the user actually wants respected).
- *Why:* the user wants one central player config; the dead fields are cruft; keeping font in a second,
  write-less file would leave the settings surface unable to persist it live. Multipliers (not absolute pt)
  because VS's Interface → GUI Scale is a global multiplier our text already stacks on — an absolute pt
  would fight it at any non-default GUI scale.
- *Alternatives rejected:* keep `ScribeClientConfig` read-only and only mirror the font (two files, the
  thing the user wants gone); give `ScribeClientConfig` a write path (needless second store).
- *Core purity:* the added fields are plain scalars with clamp helpers — no VS API — so Core stays
  unit-testable. Migrated-then-demoted layout constants live in `src/Mod`, not Core.

### D2: Settings is a host-agnostic widget swapped into a dialog's central region; gear entry points
The settings form is a single `ScribeSettingsContent` widget (LibGUI, `src/Mod`) that takes a settings
snapshot + an `onChange` callback and makes no assumptions about window size; it is wrapped in
`SingleChildScrollView` + `Scrollbar` so it fits smaller hosts (Desk). The lectern gains a third central-
region state (`isSettingsMode`) beside read/editor; a **gear** control in the `WindowFrame` chrome (present
in both views) toggles into it and `ForceRebuild()`s, with a back affordance to the prior view. A second
gear sits on the HUD next to the collapse chevron. Because the settings view is lock-free, entering it from
the editor reuses the existing commit-then-release-lock sequencing (`OnClickSwitchToRead`).
- *Why:* the user wants the gear to "take over the central window" of the tactile block/item surface and to
  generalize across future differently-sized UIs; a host-agnostic widget embedded in each dialog's center
  does both. A `TabView` is intentionally NOT used for the read/edit/settings switch — the existing dialog
  already owns view-switch state and lock handling; adding the gear as a third state reuses that machinery.
- *Alternatives rejected:* a standalone settings dialog (loses the tactile in-surface feel; a second window
  to manage); a `TabView` rework of the whole dialog (duplicates the view/lock state machine already there).
- *HUD gear (target resolved):* the HUD is an always-on overlay (`EnumDialogType.HUD`) with no central
  region to swap and may be up with nothing else open, so the HUD gear opens a **minimal standalone LibGUI
  dialog** hosting the same `ScribeSettingsContent` widget. This is the one place a standalone window is
  used; the in-lectern gear still swaps the lectern's central region. The shared widget means both paths
  render an identical form.

### D3: Instant write-through, no buffer
Every control's change handler calls `modSystem.UpdateMySettings(s => s.Field = value)`, which normalizes,
persists, and fires `MyPinsChanged`; the HUD rebuilds and (D4) the open dialog rebuilds, so the form re-
renders from the clamped value on its next build. No local buffer / apply / cancel.
- *Slider write frequency:* a drag fires `OnChanged` continuously. Persist on the slider's `OnChangeEnd`
  (release) and, if in-flight echo is wanted, hold a transient local value during the drag; default is
  straight write-through, refined only if IO/flicker shows in testing.
- *Alternatives rejected:* buffered apply/cancel — the user chose instant write, and `UpdateMySettings`
  already gives free live preview.

### D4: Live window font while a dialog is open (cheap)
The lectern already subscribes to `MyPinsChanged` and `ForceRebuild()`s (`OnMyPinsChanged`), and
`UpdateMySettings` fires that event. The only change is to stop caching `rowStyle` in the dialog ctor and
instead derive it per-`Build()` from `modSystem.MySettings`. Then a font change from the settings view
repaints read/editor instantly. A font change mid-edit rebuilds the editor tree; the existing
`autoFocusRowOnRebuild` / `focusedEditIndex` caret-preservation (already used for pin-change rebuilds)
keeps the caret. The HUD font is likewise live: replace the hardcoded `RowFontSize` in `HudPinsContent`
with `base × HudFontScale`.
- *Required regardless:* `ScribeRowControlNudge.CheckboxAndGripTop` / `FloatingButtonTop` are hand-tuned to
  font size 15 (their own doc-comment says so) and must be computed from measured input/control heights
  (reuse `TextLayoutHelper.MeasureText`, already used for the read ListView height estimate) once font is
  adjustable. This is the one non-trivial task.

### D5: Control mapping (field → widget)
- Behavior: CompletionPolicy → `Dropdown<ScribeCompletionPolicy>` (3 localized items); HudCollapsed →
  `Checkbox` (backup for the chevron/hotkey).
- Appearance: HudAnchor → `Dropdown<ScribeHudAnchor>` (7 localized items); HudMaxRows → `Slider(min 1,
  max 20, divisions 19)` (integer snap, value label); HudRowWidth → `Slider(min 80, max 1000)` with value
  label; HudOffsetX + HudOffsetY → two `NumericField(step …)` laid out on ONE `Row` (per user); HudFontScale
  + WindowFontScale → `Slider(min 0.5, max 2.5, divisions ~20 → 0.1 steps)`, value label formatted as a
  percentage/×.
- Slider ranges mirror the existing `Min*`/`Max*` consts (and new font-scale consts) so the clamp in
  `Normalized()` is the single source of truth.

### D6: Icon + localization
A new `gear` SVG under `assets/scribe/textures/icons/` registered as code `scribegear` in
`ScribeModSystem.RegisterCustomIcons`, rendered via `VsIcon` (the mod's self-healing CustomIcons path — not
LibGUI's SVG-by-path `Icon`, which fails on post-startup-unloaded assets). The supplied Bootstrap `bi-gear`
uses two `currentColor` paths; flatten to a single `#000000` shape to match the existing pin/grip/close/
edit convention (flood-recolored to the button color at draw time). All labels, section titles, enum option
labels, and helptext are `Lang.Get("scribe:…")` keys in `assets/scribe/lang/en.json`.

### D7: Deferred-send completion window + the `Keep` policy (playtest round 1)
Completing a task from the HUD is **deferred**: the click flips the row optimistically and records a
pending completion `{policy, expiry}` keyed by `(docId, taskId)`, but the `ScribeCompleteTaskMessage` is
NOT sent until a shared window (`PinHudWaitMs`, 1500ms) elapses on the HUD tick. Unchecking within the
window cancels the pending entry and clears the optimistic flag — a **true undo**, because nothing was
sent to the server. This matters most for the destructive `Unpin`/`Delete` policies (the reason the user
wanted a gradual, reversible window). While pending, the row animates (`Gui.Widgets.Animations`):
`Unpin`/`Delete` fade their text via `AnimatedOpacity` (checkbox stays opaque/clickable so undo is always
possible); a `Sink` row keeps its mute fade as its settle cue.
- *New `Keep` policy (=3):* completing keeps the pin and leaves it in place (does not sink). Server-side it
  is treated exactly like `Sink` (no pin/task removal) — the only difference is HUD ordering (`SunkForOrder`
  never sinks a `Keep` pin). `NormalizePolicy` already accepts it via `Enum.IsDefined`.
- *Sink-slide caveat:* a FLIP reorder across a reflowing `Column` isn't expressible with LibGUI's implicit
  pixel-offset `AnimatedSlide` (a zero offset animates nothing; the row just jumps to its new Column slot),
  so no slide is added — the mute fade is the sink cue. A real reorder animation is deferred to the
  custom-checkbox/animation change.
- *Read-view unchanged:* the lectern read-view checkbox still completes immediately (a different surface,
  not the "pin window"); only the HUD defers.
- *Alternatives rejected:* send-immediately with a visual-only delay (can't truly undo a `Delete`); a
  per-policy window (the user wanted one shared `pinHudWaitTime`).

### D8: Numeric fields over sliders; relative offsets; 0.05 font notches (playtest round 1)
The settings form's numeric preferences use `NumericField`, NOT `Slider`: a slider grabs the scroll wheel
and overwrites list scrolling (observed in playtest), and sliders are still visually rough here.
`NumericField` is uncontrolled and unclamped, so each field clamps inside its `onChanged` (delegating to
`UpdateMySettings`, which normalizes) and is **keyed by a `ValueKey` of its current value** so a clamp that
changed the value remounts the field showing the clamped result on the next write-through rebuild. The
font scale is entered as a **percent** (80–120, step 5), converted to/from the stored multiplier.
- *0.05 snap:* `ClampFontScale` now snaps to the nearest **0.05** (was 0.1) so 85/95/105/115% are valid
  notches, still clamped to `[0.8, 1.2]`.
- *Relative offsets:* `HudOffsetX/Y` are nudges relative to the anchor's built-in pre-baked offset
  (`ApplyAnchor` computes `prebaked(anchor) + userOffset`; `prebaked` = the top-right minimap clearance for
  `TopRight`, else 0), so a stored 0 sits at the sensible default and the value reads as "further from the
  built-in position." Clamp widens to **±300**. This drops the old "apply clearance only when offset==0"
  special-case (which made 0 ambiguous).
- *Live-scaled form:* the settings form's own `Text`/`Checkbox` sizes derive from `WindowFontScale × base`,
  and the HUD checkbox from `HudFontScale × base`, so both re-render at the new scale on the write-through
  rebuild `UpdateMySettings`→`MyPinsChanged` already fires. Scope is text + checkboxes only; dropdowns keep
  LibGUI's fixed theme heights (lectern row controls already scale via `ScribeRowStyle`).

### D9: Chrome polish + multiplayer Back safety (playtest round 1)
The `WindowFrame` title is `"Scribe Settings"` while `isSettingsMode` (WindowFrame reads its title live per
build, so a conditional string suffices). The gear uses the filled `~/Downloads/gear-filled.svg` (single
path, flattened to `#000000`), code `scribegear` unchanged. `BlockEntityScribeLectern.HandleServerReply`
gains a graceful fallback: an editor-access denial **while the dialog is already open** (and not the
save-failed recovery) now calls `dialog.EnterReadMode()` instead of doing nothing, so a Back-from-editor
that re-requests the lock and loses it to another player lands in the read view rather than a stranded
settings frame.

### Deferred to a future change: custom SVG check+box
A fully custom check+box (SVG box + background + check, overlaid and animated on toggle) to replace
LibGUI's dot-style `Checkbox` is a **non-goal here** — it is a visual/animation feature with its own design
(SVG layering, toggle animation, and wiring across HUD + lectern + settings). Scaling in this change uses
LibGUI's existing `Checkbox.size` param, which is all scaling requires; the custom checkbox does not ease
scaling and is deferred to its own proposal.

## Risks / Trade-offs

- Editor-mode font change rebuild drops the caret → reuse the existing caret-preservation path used for
  pin-change rebuilds.
- Slider drag persists on every tick (many `StoreModConfig` writes) → persist on `OnChangeEnd`, echo
  locally during drag if needed.
- Font/offset abuse (huge/negative) → clamp in `Normalized()`; slider bounds and numeric-field clamping
  mirror the clamp; the HUD already hard-caps rendered rows.
- Deleting `ScribeClientConfig` while an old file exists on disk → harmless: `LoadModConfig`'s Newtonsoft
  deserialize ignores unknown keys and the file simply goes unread; covered in Migration.
- Computed control-centering could regress row alignment at scale 1.0 → validate that scale 1.0 reproduces
  today's pixel layout in-game before shipping.

## Migration Plan

- Add the two font-scale fields + clamps to `ScribePlayerSettings`; ship. Existing `scribe-hud-config.json`
  files load unchanged (absent keys default to `1.0`).
- Delete `ScribeClientConfig.cs`; retarget `ScribeRowStyle.FromConfig`. Any existing
  `scribe-client-config.json` becomes vestigial and unread — no load error (unknown keys tolerated). A
  player who had hand-edited a non-default `TextSizeScale`/`RowFontSize` re-enters it once via the settings
  view (window font scale). No pin data or document data is touched.
- Rollback: remove the settings view + gear + font fields; the mod falls back to base font sizing. No
  persisted-data migration is required in either direction.

## Open Questions

- Whether the demoted layout knobs (row paddings, checkbox size, pinned tint) should remain overridable at
  all (e.g. a dev-only constant block) or be fully inlined — leaning fully inlined constants.
