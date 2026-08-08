#!/usr/bin/env bash
# Rebuild the mod and restage it into the local Vintage Story Mods folder for manual
# playtesting. Unlike package.sh (which zips a Release build for distribution), this
# copies straight into ~/Library/Application Support/VintagestoryData/Mods/<modid> so the
# game picks it up on next launch.
#
# Always wipes the staged mod folder's assets/ before recopying (rather than cp -R'ing
# on top of an existing one) - cp -R onto an existing directory nests the source inside
# it instead of overwriting, which silently left a stale duplicate assets/assets/ tree
# and a stale lang file in place during earlier manual re-staging.
#
# Usage: ./build/restage.sh [Debug|Release]
#
# Configuration defaults to Release (the player-like build; VSImGui is excluded from it
# by a Condition in Mod.csproj, so a Release stage has zero ImGui presence). Pass Debug to
# stage a build that includes the VSImGui live-tuning sliders (RegisterDebugSliders) --
# required for the add-imgui-configlib-tuning task 5.1 investigation, since those sliders
# are #if DEBUG-gated AND the reference itself is Debug-only. Note VSImGui's overlay only
# actually renders on a machine with OpenGL >= 4.3 -- it draws nothing on Apple Silicon
# (OpenGL 4.1 over Metal); see VSAPI-NOTES.md "VSImGui debug overlay".
set -euo pipefail

CONFIG="${1:-Release}"
if [[ "$CONFIG" != "Debug" && "$CONFIG" != "Release" ]]; then
  echo "error: configuration must be Debug or Release (got '$CONFIG')" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

MODINFO="src/Mod/modinfo.json"
if [[ ! -f "$MODINFO" ]]; then
  echo "error: $MODINFO not found (has the Mod been scaffolded yet?)" >&2
  exit 1
fi

MODID=$(grep -iE '"modid"' "$MODINFO" | head -1 | sed -E 's/.*"modid" *: *"([^"]+)".*/\1/')
MODID="${MODID:-scribe}"

STAGE="$HOME/Library/Application Support/VintagestoryData/Mods/$MODID"
echo "Restaging ${MODID} (${CONFIG}) -> $STAGE"

dotnet build src/Mod/Mod.csproj --configuration "$CONFIG"

mkdir -p "$STAGE"
cp "$MODINFO" "$STAGE/"
# worldconfig.json (mod-root sibling of modinfo.json) declares worldConfigAttributes the engine
# reads into Mod.WorldConfig. Without staging it, /worldconfig can't find our keys ("No such
# config found") because the staged mod has no WorldConfig. Optional: not every mod ships one.
if [[ -f "src/Mod/worldconfig.json" ]]; then
  cp "src/Mod/worldconfig.json" "$STAGE/"
fi
# Blanket copy of the build output DLLs (Scribe.dll + Scribe.Core.dll). The `gui` (LibGUI) hard
# dep, ConfigLib, and the game DLLs are all Private=false, so they never land in bin/ and are NOT
# staged here -- the separately-installed mods/game provide them at runtime (verified: no Gui.dll).
cp "src/Mod/bin/$CONFIG/net10.0/"*.dll "$STAGE/"

rm -rf "$STAGE/assets"
if [[ -d src/Mod/assets ]]; then
  cp -R src/Mod/assets "$STAGE/assets"
fi
# modicon.png (shown in the in-game mod manager). package.sh ships this too; stage it here so a
# playtest reflects icon changes -- without this the staged copy went stale and the old icon
# lingered in-game long after the repo icon was updated.
if [[ -f src/Mod/modicon.png ]]; then
  cp src/Mod/modicon.png "$STAGE/modicon.png"
fi

echo "Staged: $(find "$STAGE" -type f | wc -l | tr -d ' ') files"
echo "Fully quit and relaunch the game client to pick up the new build (lang/assets load once at boot, not per-world-join)."

# Config-drift guard: the mod reads its client settings from an on-disk JSON that WINS over the
# code defaults - a key present in that file with a stale value silently shadows any new default in
# ScribeClientConfig.cs (this bit us once: a pressed-overlay default changed white->dark in code, but
# the old white value sat in the JSON, so the fix looked broken in-game). Warn when the on-disk config
# predates a change to the defaults, so a playtester knows to reset/reconcile it before trusting the
# visuals. Non-fatal (never blocks a stage) and best-effort - a missing config, or git/stat hiccup,
# just skips the warning. Config edits take effect on REOPENING the lectern (no restage needed).
CONFIG_JSON="$HOME/Library/Application Support/VintagestoryData/ModConfig/scribe-client-config.json"
DEFAULTS_SRC="src/Mod/ScribeClientConfig.cs"
if [[ -f "$CONFIG_JSON" && -f "$DEFAULTS_SRC" ]]; then
  cfg_mtime=$(stat -f %m "$CONFIG_JSON" 2>/dev/null || echo 0)
  # Last commit that touched the defaults (0 if not yet committed / not in git).
  defaults_commit=$(git log -1 --format=%ct -- "$DEFAULTS_SRC" 2>/dev/null || echo 0)
  # Uncommitted edits to the defaults that are staged into this build but not yet on disk.
  defaults_dirty=$(git status --porcelain -- "$DEFAULTS_SRC" 2>/dev/null || echo "")

  if [[ -n "$defaults_dirty" || ( "$defaults_commit" != 0 && "$cfg_mtime" -lt "$defaults_commit" ) ]]; then
    echo ""
    echo "⚠️  CONFIG DRIFT: on-disk scribe-client-config.json may be shadowing new code defaults."
    echo "    $CONFIG_JSON"
    echo "    was written before the latest ScribeClientConfig.cs change, so any default you changed"
    echo "    could be overridden by a stale value already in that file. Before testing the visuals:"
    echo "      - reset:     mv the JSON aside (back it up) and let the mod rewrite fresh defaults, OR"
    echo "      - reconcile: edit just the drifted keys in the JSON, keeping your real tuning."
    echo "    (Takes effect on reopening the lectern - no restage needed for a config-only edit.)"
  fi
fi
