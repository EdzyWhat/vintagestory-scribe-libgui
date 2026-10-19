#!/usr/bin/env bash
# Prove an edit touched only comments: build a base ref and the current working tree to
# their own bin/ outputs, decompile both to IL, and diff. Comments never reach the IL, so
# any difference means a code change slipped in. Used by the comment-convention sweep (see
# openspec/changes/adopt-current-behavior-comment-convention).
#
# Usage: ./build/verify-comment-only.sh [BASE_REF]      (BASE_REF defaults to HEAD)
#
# Needs VINTAGE_STORY (the game install) like the normal build, and ilspycmd
# (~/.dotnet/tools/ilspycmd). Builds land in each tree's own gitignored bin/; nothing is staged into the Mods
# folder. Exits 0 if every checked assembly's IL is identical, 1 if any differs, 2 on setup error.
set -euo pipefail

BASE_REF="${1:-HEAD}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ILSPY="${ILSPYCMD:-$HOME/.dotnet/tools/ilspycmd}"
PROJECTS=(src/Mod/Mod.csproj tests/Core.Tests tests/Integration.Tests)

[ -x "$ILSPY" ] || { echo "ilspycmd not found at $ILSPY" >&2; exit 2; }
[ -n "${VINTAGE_STORY:-}" ] || { echo "VINTAGE_STORY is not set" >&2; exit 2; }

WORK="$(mktemp -d)"
BASE_TREE="$WORK/base-tree"
cleanup() { git -C "$REPO_ROOT" worktree remove --force "$BASE_TREE" >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT

# Build every project in tree $1 (normal bin/ output; -o would leak into ProjectReference builds).
build_all() {
  local tree="$1" p
  for p in "${PROJECTS[@]}"; do
    (cd "$tree" && dotnet build "$p" --configuration Release --nologo -v q) >"$WORK/build.log" 2>&1 \
      || { echo "build failed for $p in $tree:" >&2; tail -20 "$WORK/build.log" >&2; exit 2; }
  done
}

# List our own assemblies (Scribe*.dll, *.Tests.dll) under a tree's Release outputs, relative to it.
list_dlls() {
  (cd "$1" && find src/Mod/bin/Release tests/*/bin/Release \( -name 'Scribe*.dll' -o -name '*.Tests.dll' \) 2>/dev/null | sort)
}

# Data-section labels (I_0001CDA0) are file offsets: the debug directory embeds each tree's own
# PDB path, so the offsets shift between two trees even when the code is identical. Mask them.
normalize_il() { sed -E 's/I_[0-9A-F]{8}/I_OFFSET/g'; }

echo "Building base ($BASE_REF)..."
git -C "$REPO_ROOT" worktree add --detach "$BASE_TREE" "$BASE_REF" >/dev/null 2>&1
# The vendored LibGUI DLLs under src/Mod/lib are gitignored, so a fresh worktree lacks them.
cp "$REPO_ROOT"/src/Mod/lib/*.dll "$BASE_TREE/src/Mod/lib/"
build_all "$BASE_TREE"
echo "Building working tree..."
build_all "$REPO_ROOT"

STATUS=0
# Compare only our own assemblies (game/mod dependencies are never copied: Private=false).
for dll in $(list_dlls "$REPO_ROOT"); do
  if [ ! -f "$BASE_TREE/$dll" ]; then echo "MISSING in base: $dll"; STATUS=1; continue; fi
  "$ILSPY" -il "$BASE_TREE/$dll" 2>/dev/null | normalize_il >"$WORK/base.il"
  "$ILSPY" -il "$REPO_ROOT/$dll" 2>/dev/null | normalize_il >"$WORK/new.il"
  if diff -q "$WORK/base.il" "$WORK/new.il" >/dev/null; then
    echo "IL identical: $dll"
  else
    echo "IL DIFFERS:   $dll  ($(diff "$WORK/base.il" "$WORK/new.il" | grep -c '^[<>]') differing lines)"
    STATUS=1
  fi
done
exit $STATUS
