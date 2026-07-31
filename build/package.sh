#!/usr/bin/env bash
# Package the Scribe mod into a distributable zip: Releases/scribe_<version>.zip
#
# Run this LOCALLY (it needs the Vintage Story install to compile the Mod project).
# It reads the version from src/Mod/modinfo.json, builds the Mod in Release, and zips
# the compiled DLL together with modinfo.json and the assets folder.
#
# Usage: ./build/package.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

MODINFO="src/Mod/modinfo.json"
if [[ ! -f "$MODINFO" ]]; then
  echo "error: $MODINFO not found (has the Mod been scaffolded yet?)" >&2
  exit 1
fi

# Extract modid and version from modinfo.json (grep/sed to avoid a jq dependency).
MODID=$(grep -iE '"modid"' "$MODINFO" | head -1 | sed -E 's/.*"modid" *: *"([^"]+)".*/\1/')
VERSION=$(grep -iE '"version"' "$MODINFO" | head -1 | sed -E 's/.*"version" *: *"([^"]+)".*/\1/')
MODID="${MODID:-scribe}"
echo "Packaging ${MODID} v${VERSION}"

# Build the mod in Release.
dotnet build src/Mod/Mod.csproj --configuration Release

# Stage the mod contents. Copy every built DLL, not just Scribe.dll -- the mod references
# Scribe.Core.dll as a separate assembly (not merged/ILRepacked), so it must ship alongside
# the mod DLL or the game fails to load the block entity class at runtime with a
# FileNotFoundException. VintagestoryAPI.dll itself is excluded from the build output
# (Private=false in Mod.csproj), so a blanket *.dll copy is safe. Same holds for the `gui`
# (LibGUI) hard dep and its companions, and for configlib: all are Private=false, so Gui.dll
# (etc.) never lands in bin/ and is NOT shipped here -- the separately-installed mod provides it.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp "$MODINFO" "$STAGE/"
# worldconfig.json (mod-root sibling of modinfo.json) declares the worldConfigAttributes the
# engine reads into Mod.WorldConfig. Without it in the shipped zip, /worldconfig can't find our
# keys ("No such config found") on ANY world -- the released mod would have no WorldConfig at all.
# Mirror restage.sh's guard; optional because not every mod ships one.
if [[ -f "src/Mod/worldconfig.json" ]]; then
  cp "src/Mod/worldconfig.json" "$STAGE/"
fi
cp src/Mod/bin/Release/net10.0/*.dll "$STAGE/"
if [[ -d src/Mod/assets ]]; then
  cp -R src/Mod/assets "$STAGE/assets"
fi
if [[ -f src/Mod/modicon.png ]]; then
  cp src/Mod/modicon.png "$STAGE/modicon.png"
fi

# Zip it up.
mkdir -p Releases
OUT="$REPO_ROOT/Releases/${MODID}_${VERSION}.zip"
rm -f "$OUT"
( cd "$STAGE" && zip -r -q "$OUT" . -x "**/.DS_Store" )
echo "Wrote $OUT"
