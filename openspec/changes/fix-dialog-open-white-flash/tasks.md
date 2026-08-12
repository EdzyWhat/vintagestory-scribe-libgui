## 1. Confirm the mechanism (decisive discriminator)

- [x] 1.1 Open a flashing surface (Lectern/Notebook/Tablet) with **Pixel Art Display OFF** in Scribe
  Settings — the backdrop becomes a plain `SizedBox` with no texture (`WrapBackdrop`,
  Layout.cs:~88). Capture with the DEBUG frame-trace / OpenCV frame-extract method. **Flash gone →**
  backdrop bitmap paint confirmed as the mechanism, go to §2. **Flash survives →** not the backdrop;
  skip to §3. *(Playtest 2026-08-11, `f79c21bf`: "The flash is gone!" with Pixel Art OFF → backdrop-bitmap paint CONFIRMED as the mechanism. Proceed to §2.)*
- [x] 1.2 Record the discriminator result in `VSAPI-NOTES.md` (`## "White flash"…`) so the next
  reader doesn't re-run it. *(2026-08-11: recorded the "DISCRIMINATOR RESOLVED" block + updated the heading — backdrop-bitmap paint is the confirmed mechanism; fix work moves to §2.)*

## 2. Fix (if the backdrop paint is confirmed)

- [ ] 2.1 Read the backdrop upload path: how `WrapBackdrop` gets its 1024×1160 bitmap into a texture,
  and where/when that texture is created and released across dialog open/close (decompile the Skia /
  `LoadedTexture` upload in `VintagestoryLib.dll` if the mod-side path bottoms out in engine code).
  Find WHY the texture looks evicted between closes (a cold per-open upload landing on a live frame is
  the working theory).
- [ ] 2.2 Make the backdrop texture persistent: pre-decode + upload once at mod load (or cache it
  resident between opens) so no cold GPU upload happens on the frame the dialog opens. Do NOT add
  speculative GL calls — target only the confirmed cold-upload.
- [ ] 2.3 `dotnet build src/Mod/Mod.csproj` clean (0 new warnings); `dotnet test tests/Core.Tests`
  green; `bash build/restage.sh Debug`.

## 3. Fix (if the backdrop is refuted)

- [ ] 3.1 Diff what `ScribeDialogBase` / `GuiDialogBlockEntityBase` do on open against the clean
  `GuiBase`-derived Settings window (`ScribeSettingsDialog`, which does NOT flash). Trace the extra
  open-time work (block-entity dialog lifecycle, any `MarkDirty`/chunk touch, GUI-list reorder) to
  the one step that drops the opaque terrain pass for a frame.
- [ ] 3.2 Fix the identified step; build/test/restage as in 2.3.

## 4. Verify

- [ ] 4.1 Re-run the DEBUG frame-trace on every flashing surface (Lectern, Notebook, Tablet, both
  Pixel Art on/off): the one-frame opaque-terrain dropout is gone and the dialog still renders
  correctly. Confirm the Settings window and `.ui` showcase are unregressed.
- [ ] 4.2 `openspec validate fix-dialog-open-white-flash` passes; record the playtest verdict in
  `TESTING.md`; update `VSAPI-NOTES.md` and memory `[[white-flash-is-world-render-stall]]` with the
  root cause + fix.
