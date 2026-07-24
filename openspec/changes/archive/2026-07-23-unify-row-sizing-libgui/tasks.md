## 1. Config fields + style struct (data model)

- [x] 1.1 In `src/Mod/ScribeClientConfig.cs`, add seven new `float` fields with XML-doc
      comments describing their LibGUI semantics: `RowFontSize = 15f`,
      `RowVerticalPadding = 4f`, `RowHorizontalPadding = 2f`, `RowCheckboxTextGap = 6f`,
      `RowCheckboxSize = 22f`, `FieldInnerPaddingX = 8f`, `FieldInnerPaddingY = 6f`. Leave
      the existing native-GUI leftover fields untouched.
- [x] 1.2 Add an `internal readonly record struct ScribeRowStyle` (in
      `GuiDialogScribeLecternLibGui.cs` near the other row-data records, or a new
      `src/Mod/ScribeRowStyle.cs`) carrying: `FontSize`, `RowVerticalPadding`,
      `RowHorizontalPadding`, `CheckboxTextGap`, `CheckboxSize`, `FieldPadX`, `FieldPadY`
      (all `float`).
- [x] 1.3 Add a static `ScribeRowStyle FromConfig(ScribeClientConfig c)` factory that is the
      single scaling chokepoint: read `float s = c.TextSizeScale` and multiply the scalable
      values (`RowFontSize`, `RowVerticalPadding`, `RowCheckboxTextGap`, `RowCheckboxSize`,
      `FieldInnerPaddingY`, `FieldInnerPaddingX`) by `s`. With `s == 1f` the result equals the
      configured base values (no-op today). Document that this is the one place scaling is
      applied.

## 2. Load config into the dialog and thread the style

- [x] 2.1 In `GuiDialogScribeLecternLibGui`, add `private readonly ScribeClientConfig config;`
      and initialize it in the constructor via
      `capi.LoadModConfig<ScribeClientConfig>(ScribeModSystem.ClientConfigFileName) ?? new ScribeClientConfig()`.
- [x] 2.2 Build `var style = ScribeRowStyle.FromConfig(config);` once and thread it into the
      read-view content builder and the editor-view content builder (add a `ScribeRowStyle`
      param to `ScribeLecternReadContent` and `ScribeLecternEditorContent`), then down into
      each `ScribeReadRow` / `ScribeEditRow` (add a `ScribeRowStyle` field/param on each).

## 3. Apply the unification recipe

- [x] 3.1 `ScribeMultilineField` (`src/Mod/ScribeMultilineField.cs`): promote `PadX`/`PadY`
      from `const` to instance fields with public properties on the render object (mirror the
      existing `FontSize` property), add `PadX`/`PadY` ctor args (defaults `8f`/`6f`) on the
      render-widget and the public `ScribeMultilineField`, and assign them in
      `UpdateRenderObject` the same way `FontSize` is handled.
- [x] 3.2 `ScribeEditRow`: checkbox `size:` → `style.CheckboxSize`; field `fontSize:` →
      `style.FontSize` and pass `PadX: style.FieldPadX`, `PadY: style.FieldPadY`; `Row`
      `spacing:` → `style.CheckboxTextGap`; keep `crossAxisAlignment: Start`; outer `Padding`
      → `EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding)`.
- [x] 3.3 `ScribeReadRow`: `Text` `FontSize` → `style.FontSize`; wrap the `Text` (the
      `Expanded` child) in `Padding(EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX))`;
      checkbox `size:` → `style.CheckboxSize`; `Row` `spacing:` → `style.CheckboxTextGap`;
      change `crossAxisAlignment` from `Center` to **`Start`**; outer `Padding` →
      `EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding)`;
      keep no border.
- [x] 3.4 Editor content: set the inner `Column` `spacing:` to `0` (all row separation now
      comes from each row's own vertical padding).
- [x] 3.5 Read content: keep `ListView` `variableHeight: true`; set `estimatedItemHeight` from
      the style (≈ `style.FontSize * 1.2f + style.FieldPadY * 2 + style.RowVerticalPadding * 2`).

## 4. ConfigLib re-add (optional soft dependency)

- [x] 4.1 In `src/Mod/Mod.csproj`, add an `<ItemGroup>` with
      `<Reference Include="configlib"><HintPath>lib/configlib.dll</HintPath><Private>false</Private></Reference>`.
      Confirm `lib/configlib.dll` is present (re-extract per `src/Mod/lib/README.md` if needed)
      and update `lib/README.md` + `.gitignore` comments to mention ConfigLib again.
- [x] 4.2 Create `src/Mod/assets/scribe/config/configlib-patches.json` with `"version": 1`,
      `"file": "scribe-client-config.json"`, and a `"settings"` array with one
      `"type": "float"` entry per new field (`RowFontSize`, `RowVerticalPadding`,
      `RowHorizontalPadding`, `RowCheckboxTextGap`, `RowCheckboxSize`, `FieldInnerPaddingX`,
      `FieldInnerPaddingY`), each with `"code"` = exact field name, a `"default"`, a
      `"comment"`, and a `"range"`. No integer entries.
- [x] 4.3 Do NOT add a ConfigLib dependency to `modinfo.json` (it stays optional; the manifest
      is inert without ConfigLib installed). Confirm no Scribe code calls a ConfigLib API.

## 5. Build & test verification

- [x] 5.1 `dotnet build src/Mod/Mod.csproj --configuration Debug` — clean, config reference
      resolves.
- [x] 5.2 `dotnet build src/Mod/Mod.csproj --configuration Release` — clean; confirm
      `configlib.dll` is NOT copied into `bin/Release/net10.0/` (`Private=false`).
- [x] 5.3 `dotnet test tests/Core.Tests/Core.Tests.csproj` — all green (regression check; Core
      is untouched).
- [x] 5.4 `bash build/restage.sh` (Release) and confirm the new `configlib-patches.json` asset
      is staged.

## 6. Manual in-game verification (playtest)

- [x] 6.1 Open a lectern with several single-line tasks; switch read↔editor repeatedly and
      confirm each task stays at the same vertical position with no jump (single-line parity).
      Confirmed 2026-07-23 (playtest): tasks stay pinned across view switches.
- [x] 6.2 Confirm the read view draws no field border while the editor field does, and the
      text left edge and top edge line up across the switch.
      Confirmed 2026-07-23 (playtest): read view borderless, text edges align across the switch.
- [ ] 6.3 Edit a row-sizing value in `scribe-client-config.json`, reopen the lectern, and
      confirm the rows render at the new size (edit-file-then-reopen loop).
      **Backlogged to PC 2026-07-23** (author will verify on PC alongside 6.4).
- [ ] 6.4 With ConfigLib installed, open its settings panel, change a row-sizing float, save,
      reopen the lectern, and confirm the new size applies. Confirm the panel opens without
      error (float-only settings).
      **Blocked/backlogged to PC 2026-07-23:** opening ConfigLib's "Mod Settings" panel freezes
      game input on the author's Apple Silicon Mac (ESC, mouse-look). Manifest is confirmed
      float-only, so this is not the old integer-panel bug — likely the OpenGL 4.1/4.3 ImGui
      wall (see the configlib-panel-freeze-mac memory + VSAPI-NOTES). Verify on PC (OpenGL >= 4.3).
- [x] 6.5 With ConfigLib NOT installed, confirm the mod loads and the lectern opens normally
      with no missing-dependency warning.
      Confirmed 2026-07-23 (playtest): loads and opens normally.
- [x] 6.6 Look at the checkbox-vs-first-line alignment under `Start`; if the 22px checkbox
      reads too tall against the line, dial `RowCheckboxSize` in-game (do not revert read row
      to `Center`). Record the chosen value.
      Confirmed 2026-07-23 (playtest): alignment reads fine at the default `RowCheckboxSize = 22`.
- [x] 6.7 Check a multi-line task in both views: confirm wrap parity is acceptable (best-effort,
      not a blocker).
      Confirmed 2026-07-23 (playtest): multi-line wrap parity acceptable.
