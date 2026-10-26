## 1. Bump the dependency

- [x] 1.1 Extract the 4.0.0 DLLs from `gui_4.0.0.zip` into `src/Mod/lib/` per `src/Mod/lib/README.md`, update that README's version and steps, and verify `dotnet build` succeeds
- [x] 1.2 Set `"gui": "4.0.0"` in `src/Mod/modinfo.json` and verify the file parses and the build output's `modinfo.json` shows it
- [x] 1.3 Point `tests/Integration.Tests` (csproj and `AssemblyInfo.cs`) at `gui_4.0.0.zip`, install the zip into the Mods folder, and verify the Atlas suite finds it
- [x] 1.4 Record any new compiler warnings (expected: `[Obsolete]` `WindowFrame`) and confirm none are errors. Recorded: one warning, CS0618 `SlotController.LeaveSlot()` in `ScribeDocumentSlot.cs:100` (now wants `LeaveSlot(ItemSlot?)`); no `WindowFrame` warnings; none are errors

## 2. Docs and references

- [x] 2.1 Update version mentions in `README.md` and `docs/libgui-reference.md`, replacing the version-skew warning with the 4.0.0 source and wiki links, and verify `rg "3\.1\.0"` shows only intentional history
- [x] 2.2 Re-clone `reference/vslibgui` and `.wiki/` at 4.0.0 and verify the clone's `Gui/Gui/modinfo.json` reads 4.0.0

## 3. Automated verification

- [x] 3.1 With the game client closed, run `build/verify.sh` with `VINTAGE_STORY` set and verify Core and Atlas suites are green. Result: Core green, locale checks pass, Atlas 123/123 on gui 4.0.0, restaged
- [ ] 3.2 Launch the client once and verify the log shows `gui` 4.0.0 loaded, the Scribe HarfBuzz patch line, and no `[GUI]` errors

## 4. Playtest

- [x] 4.1 Add playtest items to `TESTING.md` (regenerate with `/what-to-test`) covering every Scribe dialog, the HUD, and the risk areas in design.md
- [ ] 4.2 Playtest: notebook, lectern, scriptorium, assignment desk, inbox and tablets open, edit, and close correctly (no clipped or missing art)
- [ ] 4.3 Playtest: HUD pin rows, timer row, gear button and anchor settings, including starting a timer after cancelling or ending one without touching settings
- [ ] 4.4 Playtest: hover states, tooltips, row delete buttons and collapse animations, and a Scribe dialog opened over the inventory or handbook
- [ ] 4.5 Playtest: cuneiform field, multiline field (caret, selection, scroll), Settings dialog, and `MuteUiSounds` on and off (no doubled or unmuted click sounds)
- [ ] 4.6 Playtest: window resize and GUI scale change with a dialog and the HUD open
