# Vendored third-party mod DLLs

This folder is gitignored — these DLLs must be re-extracted on any machine that builds
this project, following the steps below. They are vendored here instead of referenced via
NuGet because neither LibGUI nor ConfigLib has a usable published package at the version
actually installed and tested against.

## Source and versions

Extracted from the game mod `.zip`s under the local Vintage Story Mods folder
(`~/Library/Application Support/VintagestoryData/Mods/` on macOS):

- `gui_3.1.0.zip` (LibGUI, modid `gui` — Scribe's hard dependency; see `Mod.csproj`) → the 7
  vendored managed DLLs: `Gui.dll`, `ExCSS.dll`, `ShimSkiaSharp.dll`, `SkiaSharp.HarfBuzz.dll`,
  `Svg.Custom.dll`, `Svg.Model.dll`, `Svg.Skia.dll`, `HarfBuzzSharp.dll`. Only `Gui.dll` is
  referenced for compile (`Mod.csproj`); the other six are its runtime companions, extracted so a
  build machine has the whole set the installed mod provides. (`OpenTK.Mathematics.dll` and
  `SkiaSharp.dll`, which LibGUI's public API also surfaces, come from the game's `Lib/`, not here.)
- `configlib_1.12.0.zip` (ConfigLib, modid `configlib` — Scribe's OPTIONAL soft dependency;
  unify-row-sizing-libgui) → `configlib.dll`. Referenced for compile in `Mod.csproj` only so the
  build resolves; Scribe calls no ConfigLib API (the integration is the no-code
  `assets/scribe/config/configlib-patches.json` manifest). `Private=false`, so it's never copied
  into Scribe's output; the mod loader provides it if installed, and the mod runs fine without it.

## Re-extraction steps

```bash
cd /tmp
MODS="$HOME/Library/Application Support/VintagestoryData/Mods"

unzip -o "$MODS/gui_3.1.0.zip" -d gui_extract \
  Gui.dll ExCSS.dll ShimSkiaSharp.dll SkiaSharp.HarfBuzz.dll \
  Svg.Custom.dll Svg.Model.dll Svg.Skia.dll HarfBuzzSharp.dll
unzip -o "$MODS/configlib_1.12.0.zip" -d configlib_extract configlib.dll

cp gui_extract/{Gui.dll,ExCSS.dll,ShimSkiaSharp.dll,SkiaSharp.HarfBuzz.dll,Svg.Custom.dll,Svg.Model.dll,Svg.Skia.dll,HarfBuzzSharp.dll} src/Mod/lib/
cp configlib_extract/configlib.dll src/Mod/lib/
```

If you install a newer version of either mod, re-extract and update the version numbers above
(and re-verify any API assumptions still hold — these are not stable published packages with
change logs to check against).

## Linux native-loading note

LibGUI's bundled `libHarfBuzzSharp.so` exports HarfBuzz symbols that can collide with a
different system `libharfbuzz.so.0` already loaded by the Vintage Story process. This is
not a KDE- or Qt-specific condition: GTK-based desktops and other host toolkits can load
the conflicting system library too. Scribe installs a glibc-only resolver early in client
startup and uses `RTLD_DEEPBIND` for the bundled library. On non-glibc Linux, that
isolation is unavailable; Scribe leaves the normal runtime loader in place and logs that
the workaround was not applied. A non-glibc failure must not be classified as the same
HarfBuzz crash without native backtrace or loader evidence.

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

**ConfigLib** (the optional `configlib` soft dependency)
- Mod portal (manifest schema + examples): https://mods.vintagestory.at/configlib#tab-description
- Source: https://github.com/maltiez2/vsmod_configlib
- Wiki (the `configlib-patches.json` manifest / `"file"`-key form): https://github.com/maltiez2/vsmod_configlib/wiki
- Note: expose only FLOAT settings — an integer setting once threw while drawing and broke the
  entire ConfigLib panel (see `VSAPI-NOTES.md`).
