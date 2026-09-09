## Why

`unify-tab-header-layout` made the Row 2 subtitle line (a small-caps label + italic descriptor
under the title bar) mandatory on every non-tablet dialog tab, with no way to turn it off. Some
players may find the extra line unwanted once the novelty wears off, and the author wants a
low-cost way to let it be disabled without building a full in-mod settings UI for a single cosmetic
line. `ScribeVisualTuning` already establishes a "ConfigKit-only, load-once-at-startup" precedent
for exactly this kind of client-local cosmetic toggle, so this reuses that mechanism rather than
inventing a new one.

## What Changes

- Add a `ShowSubtitleRow` boolean to `ScribeVisualTuning` (default `true`), persisted in the
  existing `scribe-visual-tuning.json` config file alongside the current 8 numeric tuning values.
- Add a `"boolean"` settings section to `configlib-patches.json` exposing `ShowSubtitleRow` as
  "Show subtitle row" in a config-library mod's (ConfigKit's) settings screen — same file, same
  no-dependency mechanism as the existing numeric knobs.
- When `ShowSubtitleRow` is `false`, `ScribeTabHeader.Build` omits Row 2 (the subtitle) entirely on
  every tab that currently renders it; Row 3 (a tab's own controls, if any) and the trailing
  durable divider still render, sitting directly under Row 1 instead of under Row 2.
- ConfigKit-only: no checkbox is added to Scribe's own in-game Settings screen. Matches the
  existing `ScribeVisualTuning` precedent (not surfaced in Scribe Settings, read once at client
  startup — a change made via ConfigKit takes effect on the next relaunch, not live). The Tablet
  dialog already renders neither Row 2 nor Row 3 (`SupportsTabHeader = false`) and is unaffected
  either way.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `scribe-dialog-base`: the "Non-tablet dialog tabs share a three-row header above one durable
  divider" requirement changes from "every covered tab renders Row 2 unconditionally" to "renders
  Row 2 only when the `ShowSubtitleRow` setting is on."
- `visual-tuning-config`: the "Visual tuning values are read from a local config file with
  built-in defaults" requirement grows from 8 numeric values to include a 9th, boolean value
  (`ShowSubtitleRow`), and the config-file/manifest requirements now cover a `boolean` settings
  type in addition to `integer`/`float`.

## Impact

- `src/Mod/ScribeVisualTuning.cs` — new `ShowSubtitleRow` property + default constant; doc comment
  updated to describe the third, layout-toggle knob alongside the two rendering subsystems.
- `src/Mod/assets/scribe/config/configlib-patches.json` — new `"boolean"` settings block, new
  `"formatting"` separator.
- `src/Mod/ScribeDialogBase.Layout.cs` (`ScribeTabHeader.Build`) — new parameter gating Row 2;
  callers that are `ScribeDialogBase` subclasses read `modSystem.VisualTuning.ShowSubtitleRow`
  directly.
- `src/Mod/ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs`,
  `ScribeAssignmentFormContent.cs`, `ScribeInboxContent.cs` — the five plain-`StatefulWidget` call
  sites gain a new constructor property threaded from their owning `ScribeDialogBase`, mirroring
  how theme/font-scale settings are already threaded into these widgets.
- No changes to `src/Core/`, network messages, or persisted document state.
