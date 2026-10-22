## 1. Lang keys

- [x] 1.1 Re-grep each of the 11 keys across `src`, `tests`, `tools` and assets, then delete them from all 13 lang files and `build/gen_ru.py`; verify the grep finds nothing and `build/check-locales.py` passes
## 2. Unused parameters and local

- [x] 2.1 Remove `colors` from the four `HudPinsContent` builders and `ScribeSettingsContent.PairedControls`, `capi` from `ScribeCraftRecipeProbe.DisplayName`, `kind` from `DispatchItemKindGuide`, `taskId` from `ScribeCompletion.ApplyBoundParent`, `state` from `ScribeTheme.ForTabletQuestLink`, with all call-site arguments; verify `dotnet build` succeeds
- [x] 2.2 Remove the unused local `fieldW` in `GuiDialogClockmakerNotebook`; verify build

## 3. Verify

- [x] 3.1 Run `build/verify.sh`; confirm Core and Atlas pass
