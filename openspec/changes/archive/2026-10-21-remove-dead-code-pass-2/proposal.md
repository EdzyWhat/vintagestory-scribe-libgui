## Why

The first dead-code pass covered unreferenced methods and classes. A second audit looked at what that pass skipped
(lang keys, constants, fields, enum members, unused parameters, whole files) and found a short list of certain
leftovers: 11 lang keys no code or asset reads, 9 parameters that are never used, and 1 dead local.

## What Changes

- Remove 11 unreferenced keys from every lang file (`scribe-gui-title`, `scribe-gui-addcraft-variant`,
  `scribe-gui-delete`, `scribe-gui-pin`, `scribe-gui-unpin`, `scribe-gui-textsize`,
  `scribe-gui-newnote-placeholder`, `scribe-gui-collapse`, `scribe-gui-expand`, `scribe-tab-inventory`,
  `scribe-guestbook-col-date`) and from `build/gen_ru.py` if it lists them.
- Remove 9 unused parameters and their call-site arguments: `colors` in four `HudPinsContent` builders and in
  `ScribeSettingsContent.PairedControls`; `capi` in `ScribeCraftRecipeProbe.DisplayName`; `kind` in
  `DispatchItemKindGuide`; `taskId` in `ScribeCompletion.ApplyBoundParent`; `state` in
  `ScribeTheme.ForTabletQuestLink`.
- Remove the unused local `fieldW` in `GuiDialogClockmakerNotebook`.
- Keep: engine-resolved keys (`block-*`, `item-*`), dynamically built keys, `_comment-*` keys,
  `ScribeAccessMode.Private`, signature-forced parameters, and the reflection-loaded files.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. No player-visible behavior changes (`skip_specs: true`).

## Impact

`src/Mod/assets/scribe/lang/*.json` (13 files), `build/gen_ru.py`, and about 8 source files for the signature tidy.
About 150 lines removed, most of them lang lines.
