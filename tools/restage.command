#!/usr/bin/env bash
# Double-clickable / globally-callable restage launcher for the Scribe LibGUI mod.
#
# Thin wrapper over build/restage.sh so the mod can be rebuilt-and-staged from a Dock
# launcher or a global `scribe-restage` command without cd-ing into the repo first.
# Defaults to Debug (the live-tuning build used for iterating transforms/assets); pass
# Release to stage the player-like build instead.
#
# Usage:
#   ./tools/restage.command [Debug|Release]
#   scribe-restage [Debug|Release]      # once symlinked onto PATH (see below)
set -euo pipefail

# Resolve the repo root from THIS script's location (works whether invoked directly, via
# the ~/Launchers alias, or through a PATH symlink whose target is this file).
SOURCE="${BASH_SOURCE[0]}"
while [[ -h "$SOURCE" ]]; do
  DIR="$(cd -P "$(dirname "$SOURCE")" && pwd)"
  SOURCE="$(readlink "$SOURCE")"
  [[ "$SOURCE" != /* ]] && SOURCE="$DIR/$SOURCE"
done
REPO_ROOT="$(cd -P "$(dirname "$SOURCE")/.." && pwd)"

CONFIG="${1:-Debug}"

echo "==> Scribe restage ($CONFIG)  [$REPO_ROOT]"
"$REPO_ROOT/build/restage.sh" "$CONFIG"
STATUS=$?

echo
if [[ $STATUS -eq 0 ]]; then
  echo "✅ Restage complete. Fully quit and relaunch the game client to load the new build,"
  echo "   then break-and-replace any placed items so their transforms re-tesselate."
else
  echo "❌ Restage failed (exit $STATUS) — see the build output above."
fi

# When double-clicked from Finder this runs in a Terminal window; pause so the result is
# readable before the window closes. Skipped when run non-interactively (PATH command).
if [[ -t 1 && -t 0 ]]; then
  echo
  read -r -n 1 -s -p "Press any key to close…"
  echo
fi

exit $STATUS
