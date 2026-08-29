#!/usr/bin/env bash
# Decompiles the vendored LibGUI DLL (src/Mod/lib/Gui.dll — the shipped binary, which is more
# authoritative than the community vslibgui source when they disagree, per VSAPI-NOTES.md) into a
# gitignored reference/ tree, so LibGUI internals can be grepped/read directly instead of
# re-running one-off `ilspycmd -t <Type>` calls every time a question comes up.
#
# Usage: build/decompile-libgui.sh
# Output: reference/vslibgui-decompiled/ (gitignored — regenerate any time Gui.dll is bumped).
set -euo pipefail

cd "$(dirname "$0")/.."

DLL="src/Mod/lib/Gui.dll"
OUT="reference/vslibgui-decompiled"
ILSPY="$HOME/.dotnet/tools/ilspycmd"

if [[ ! -f "$DLL" ]]; then
  echo "error: $DLL not found" >&2
  exit 1
fi
if [[ ! -x "$ILSPY" ]]; then
  echo "error: ilspycmd not found at $ILSPY (dotnet tool install -g ilspycmd)" >&2
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT"
"$ILSPY" -p -o "$OUT" "$DLL"

echo "Decompiled $DLL -> $OUT"
