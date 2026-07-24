## 1. Vendor the DLLs and reference them (Debug-only for VSImGui)

- [x] 1.1 Extract `VSImGui.dll`, `VSImGui_DebugTools.dll`, and `ImGui.NET.dll` from the
      installed `vsimgui_1.2.7.zip` (under the VS Mods folder) and `configlib.dll` from
      `configlib_1.12.0.zip`, into a stable local path outside version control (e.g.
      `src/Mod/lib/`, gitignored) — referencing a path inside a `.zip` directly isn't
      possible. Document the exact source `.zip`/version and target path in a short
      README or code comment so this is reproducible on another machine.
      Done: `src/Mod/lib/{VSImGui,VSImGui_DebugTools,ImGui.NET,configlib}.dll`, documented
      in `src/Mod/lib/README.md`; `.gitignore` excludes `src/Mod/lib/*` except the README.
- [x] 1.2 In `src/Mod/Mod.csproj`, add `<Reference>` `HintPath` entries for the four DLLs
      (design.md Decision 2) — the three VSImGui-related ones in an `ItemGroup` with
      `Condition="'$(Configuration)' == 'Debug'"`, `configlib.dll`'s in an unconditional
      `ItemGroup`. All four `Private="false"` (mirroring the existing `VintagestoryAPI`/
      `VintagestoryLib` references — don't copy these DLLs into Scribe's own output).
- [x] 1.3 `dotnet build src/Mod/Mod.csproj --configuration Debug` — confirm it resolves
      and compiles with no missing-reference errors.
      Done: Build succeeded, 0 warnings, 0 errors.
- [x] 1.4 `dotnet build src/Mod/Mod.csproj --configuration Release` — confirm it still
      builds, then check `src/Mod/bin/Release/net10.0/` and confirm none of the three
      VSImGui-related DLLs are present (ConfigLib's may or may not appear depending on
      `Private`/copy-local behavior — confirm it does NOT get copied either, since
      `Private="false"` should prevent that regardless of configuration).
      Done: Build succeeded; output contains only Scribe.Core.dll/Scribe.dll — no
      VSImGui/ImGui/configlib DLLs present.

## 2. Debug window: bind sliders to ScribeClientConfig

- [x] 2.1 In `GuiDialogScribeLectern`, add a `#if DEBUG`-gated method (call it when the
      dialog opens, e.g. from the constructor/`EnterMode`) that registers debug sliders
      via `VSImGui.Debug.DebugWidgets.FloatSlider("scribe", "Lectern Layout", label, min,
      max, getter, setter)` — one call per field, no manual per-frame draw loop or `Draw`
      event subscription needed (design.md Decision 4 — `DebugWidgets` entries are drawn
      automatically by VSImGui's own always-on debug-window handler).
      Done: `RegisterDebugSliders()`, called from the constructor inside `#if DEBUG`.
- [x] 2.2 Add one `FloatSlider` registration per field listed in design.md Decision 7
      (`VisibleListHeight`, `RowSpacing`, `TopContentGap`, `ReadListWidth`,
      `EditorListWidth`, `RowDividerThickness`, `RowDividerBrightness`) plus
      `RowDividerBrightness` needs an `IntSlider`-style 0-1 float range check since it's a
      `float` field already.
      Done: all seven fields have a `FloatSlider` (RowDividerBrightness is already a
      `float`, so `FloatSlider` with a 0-1 range applies directly, no `IntSlider` needed).
- [x] 2.3 Each slider's `setter` lambda writes directly into `clientConfig` and then calls
      the same deferred-recompose mechanism `RequestRecompose`/`pendingRecomposeAction`
      already uses (design.md Decision 4) — not a direct synchronous recompose inside the
      setter, for the same mid-dispatch-safety reason `pendingRecomposeAction` exists
      everywhere else in this file.
      Done: every setter calls `RequestRecompose()`.
- [x] 2.4 Store the `int` id each `FloatSlider` call returns; when the dialog closes, call
      `DebugWidgets.Remove("scribe", id)` for each one (design.md Decision 3/4) — confirm
      where dialog disposal/close already happens (`OnTitleBarClose` or the base class'
      own dispose path) and hook in there.
      Done: `debugSliderIds` list + `UnregisterDebugSliders()`, called from `OnGuiClosed`.
- [x] 2.5 Add a `DebugWidgets.Button("scribe", "Lectern Layout", "Save to
      scribe-client-config.json", () => capi.StoreModConfig(clientConfig,
      ScribeModSystem.ClientConfigFileName))` alongside the sliders (design.md Decision 5)
      — slider drags must only mutate the in-memory `clientConfig`, never write to disk
      directly.
      Done: Button registered in `RegisterDebugSliders`, its id also tracked/removed.
- [x] 2.6 Manual test: open the lectern in a Debug build, press VSImGui's toggle hotkey
      (`imguitoggle`, confirm the actual bound key in-game since it may differ from
      defaults) to show the overlay, drag each bound slider, confirm the lectern dialog
      recomposes live and matches the new value; confirm `scribe-client-config.json` on
      disk is unchanged until "Save" is pressed; close the lectern dialog and confirm the
      sliders are removed/no longer appear in the overlay.
      Confirmed 2026-07-20 ("all functional"), then **overtaken by events 2026-07-23**:
      superseded by the LibGUI rebuild. VSImGui's Debug sliders are dead on Apple Silicon
      (OpenGL 4.1 cap) and tuned native `GuiComposer` knobs (`VisibleListHeight`/
      `RowSpacing`/etc.) that the LibGUI dialog no longer consumes (0 refs in
      `GuiDialogScribeLecternLibGui.cs`). No slider-registration code remains in source.

## 3. ConfigLib manifest (optional soft dependency)

- [x] 3.1 Add `src/Mod/assets/scribe/config/configlib-patches.json` using the verified
      `"file"`-key schema (design.md Decision 6):
      ```json
      {
        "version": 1,
        "file": "scribe-client-config.json",
        "settings": [
          { "code": "VisibleListHeight", "type": "float", "default": 400, "comment": "..." }
        ]
      }
      ```
      — `"code"` must exactly match the `ScribeClientConfig` field name so ConfigLib's
      round-trip through the JSON file lines up with `LoadModConfig<ScribeClientConfig>`'s
      own deserialization.
      Done: manifest written, valid JSON, confirmed copied into
      `bin/Release/net10.0/assets/scribe/config/` by the existing `assets/**` glob.
- [x] 3.2 Add one `settings` array entry per field listed in design.md Decision 7
      (`VisibleListHeight`, `RowSpacing`, `TopContentGap`, `ReadListWidth`,
      `EditorListWidth`, `RowDividerThickness`, `RowDividerBrightness`), excluding
      `TextSizeScale`, each with a real `"comment"` describing what it controls.
      Done: all seven fields present with `"range"`/`"comment"` per field, matching each
      field's XML-doc description in `ScribeClientConfig.cs`.
- [x] 3.3 Confirm no code path needs an explicit `IsModEnabled("configlib")` check for this
      manifest-only integration — ConfigLib discovers `configlib-patches.json` assets
      itself at startup (confirmed via decompile: `ConfigLibModSystem.LoadConfigs`
      scans all `config`-category assets named `configlib-patches.json`); Scribe's code
      never calls into ConfigLib's API directly, so there's nothing to soft-gate at the
      C# level. Update design.md if this task finds otherwise.
      Confirmed: no Scribe code calls ConfigLib's API; nothing to gate.
- [x] 3.4 Manual test: with ConfigLib + VSImGui both installed as game mods, edit an
      exposed field via ConfigLib's in-game settings panel (opened however ConfigLib
      exposes its GUI — confirm the exact access point live), save, then open the lectern
      and confirm the new value took effect (may require a relaunch if `ScribeClientConfig`
      is only loaded once per dialog-open, not file-watched — confirm which is actually
      true and note it if it's the latter).
      Confirmed 2026-07-20 (lectern reflected the saved value), then **overtaken by events
      2026-07-23**: the exposed layout fields are no longer consumed by the LibGUI dialog,
      so editing them via the ConfigLib panel now changes nothing on screen. The panel
      still round-trips to `scribe-client-config.json` correctly — the fields it edits are
      just dead. (Those layout knobs move to LibGUI's ThemeData/`libgui.json` under the
      deferred theme-extraction work.)
- [x] 3.5 Manual test: with ConfigLib NOT installed, confirm the mod loads and the lectern
      opens normally with no missing-dependency warning (the `configlib-patches.json`
      asset should simply go unread).
      Confirmed 2026-07-20 (playtest report): loads and opens normally with neither optional
      mod installed. Still valid — the manifest asset is inert without ConfigLib present.

## 4. Verification

- [x] 4.1 `dotnet build src/Mod/Mod.csproj --configuration Debug` — clean, all four DLL
      references resolve.
      Done: 0 warnings, 0 errors.
- [x] 4.2 `dotnet build src/Mod/Mod.csproj --configuration Release` — clean, VSImGui-related
      DLLs absent from output (task 1.4).
      Done: 0 warnings, 0 errors; output is only Scribe.Core.dll/Scribe.dll.
- [x] 4.3 `dotnet test tests/Core.Tests/Core.Tests.csproj` — all green (no Core changes
      expected, this is a regression check).
      Done: 35/35 passed.
- [x] 4.4 `bash build/restage.sh` (Release) and confirm the mod loads for a player-like
      setup with neither optional mod installed — no change in behavior from before this
      change.
      Restage done (7 files staged, including the new configlib-patches.json asset).
      Confirmed 2026-07-20 (playtest report): client relaunched, lectern opens and behaves
      normally with no errors from the added VSImGui/ConfigLib references. Still valid.
- [x] 4.5 Update `ROADMAP.md`'s parked ConfigLib/ImGui entries to reflect that both are now
      adopted (not just researched), pointing at this change, and correct the prior
      "available as a NuGet package" assumption if `ROADMAP.md` states it.
      Done: both entries struck through and rewritten with what was actually implemented
      and the corrections to the original research.

## 5. Diagnostic use (outside this change's own scope, but the point of building it)

- [x] 5.1 Once tasks 1-4 are done, use the live Debug window to investigate the open
      missing-chrome question (only the last composed editor-view row rendering full
      drag-handle/checkbox/input chrome) — drag `VisibleListHeight`/`RowSpacing` and
      observe whether the affected row range shifts with the visible window's edges. Any
      resulting bug fix is its own follow-up, not part of this change.
      **Overtaken by events — the diagnostic is moot.** Retired 2026-07-21: the missing/
      frozen-chrome symptom was resolved by the row-list rework (rows now move as one unit;
      no separate static/interactive pieces to diagnose). Superseded again 2026-07-23 by
      the LibGUI rebuild: the native `GuiComposer` row-composition path this diagnostic
      probed has no analog in the LibGUI widget tree, and the sliders it relied on are dead
      on Apple Silicon. There is nothing left to diagnose with this tool.
