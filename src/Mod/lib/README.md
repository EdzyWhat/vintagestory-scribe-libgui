# Vendored third-party mod DLLs

This folder is gitignored — these DLLs must be re-extracted on any machine that builds
this project, following the steps below. They are vendored here instead of referenced via
NuGet because LibGUI has no usable published package at the version actually installed and
tested against.

## Source and versions

Extracted from the game mod `.zip` under the local Vintage Story Mods folder
(`~/Library/Application Support/VintagestoryData/Mods/` on macOS):

- `gui_2.0.0.zip` (LibGUI, modid `gui` — Scribe's hard dependency; see `Mod.csproj`) → the 7
  vendored managed DLLs: `Gui.dll`, `ExCSS.dll`, `ShimSkiaSharp.dll`, `SkiaSharp.HarfBuzz.dll`,
  `Svg.Custom.dll`, `Svg.Model.dll`, `Svg.Skia.dll`, `HarfBuzzSharp.dll`. Only `Gui.dll` is
  referenced for compile (`Mod.csproj`); the other six are its runtime companions, extracted so a
  build machine has the whole set the installed mod provides. (`OpenTK.Mathematics.dll` and
  `SkiaSharp.dll`, which LibGUI's public API also surfaces, come from the game's `Lib/`, not here.)

## Re-extraction steps

```bash
cd /tmp
MODS="$HOME/Library/Application Support/VintagestoryData/Mods"

unzip -o "$MODS/gui_2.0.0.zip" -d gui_extract \
  Gui.dll ExCSS.dll ShimSkiaSharp.dll SkiaSharp.HarfBuzz.dll \
  Svg.Custom.dll Svg.Model.dll Svg.Skia.dll HarfBuzzSharp.dll

cp gui_extract/{Gui.dll,ExCSS.dll,ShimSkiaSharp.dll,SkiaSharp.HarfBuzz.dll,Svg.Custom.dll,Svg.Model.dll,Svg.Skia.dll,HarfBuzzSharp.dll} src/Mod/lib/
```

If you install a newer version of the mod, re-extract and update the version number above
(and re-verify any API assumptions in the LibGUI dialog still hold — this is not a stable
published package with a change log to check against).

## Upstream references

Check these before decompiling the vendored DLLs — decompiling is the last-resort
fallback (per the project's modding-references guardrail), not the first stop. The mod
`.zip` ships no published NuGet package or docs, so these upstreams are the only
authoritative source short of the DLLs themselves.

**LibGUI** (the `gui` hard dependency)
- Source: https://github.com/ripls56/vslibgui
- Wiki: https://github.com/ripls56/vslibgui.wiki.git
- Local clones (gitignored, per the project's LibGUI modding-references guardrail):
  `./.wiki/` (wiki) and `./reference/vslibgui/` (source).
