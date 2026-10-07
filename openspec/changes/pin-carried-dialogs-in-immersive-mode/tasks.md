## 1. Implementation

- [x] 1.1 In `GuiDialogScribeNotebook`, add `protected override WorldAnchor? Anchor => null;` with a short comment explaining the dummy `BlockPos(0)`; verify `dotnet build` succeeds
- [x] 1.2 In `GuiDialogScribeTablet`, add the same override; verify `dotnet build` succeeds
- [x] 1.3 Confirm `GuiDialogClockmakerNotebook` needs no override (inherits from the Notebook) and that no block dialog overrides `Anchor`; verify with `rg "Anchor" src/Mod`

## 2. Verification

- [x] 2.1 Run the Core test suite and verify it passes unchanged
- [x] 2.2 Add manual TESTING.md items for this change: Notebook and Tablet stay put in Immersive Mouse Mode while looking around; they can be dragged; Lectern/Chalkboard still float above their block; behavior unchanged with Immersive Mouse Mode off
- [x] 2.3 Restage with `build/restage.sh Debug` (client closed) and playtest the items from 2.2 in-game
