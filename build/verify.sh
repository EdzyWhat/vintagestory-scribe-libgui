#!/usr/bin/env bash
# One-command local verification loop: build -> Core tests -> Atlas integration suite ->
# restage. This is the single source of truth for "prove it still works and put it in the
# game," shared by a developer running it by hand and by the pre-push hook (build/hooks/pre-push).
#
# It leans entirely on what THIS machine already has -- the Vintage Story install, the staged
# `gui`/`configlib` mod deps, and the game DLLs -- so nothing is downloaded or staged from a CDN
# (contrast the deferred cloud-CI option; see the improve-testing-and-diagnosis design). It needs
# the same VINTAGE_STORY env var that building the mod and the Atlas suite already require.
#
# Fail-fast contract (spec local-verification-workflow): each stage runs in order; on the FIRST
# failure the script prints which stage failed, exits non-zero, and does NOT restage -- a broken
# build is never silently staged into the Mods folder.
#
# Usage: ./build/verify.sh [Debug|Release]
#   Configuration is passed through to the build + restage (defaults to Release, the player-like
#   build). The tests always run in their own default configuration.
#
#   --no-restage   Run build + both test suites but skip the restage stage. This is what the
#                  pre-push hook uses: it wants the gate (tests must pass) without mutating the
#                  developer's live Mods folder as a side effect of pushing.
set -euo pipefail

CONFIG="Release"
RESTAGE=1
for arg in "$@"; do
  case "$arg" in
    Debug|Release) CONFIG="$arg" ;;
    --no-restage)  RESTAGE=0 ;;
    *) echo "usage: $0 [Debug|Release] [--no-restage]" >&2; exit 2 ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Print a banner and run a stage; on failure, name the stage and abort before restaging.
run_stage() {
  local name="$1"; shift
  echo ""
  echo "==> ${name}"
  if ! "$@"; then
    echo "" >&2
    echo "✗ verify FAILED at stage: ${name}" >&2
    echo "  (not restaging -- fix the failure and re-run ./build/verify.sh)" >&2
    exit 1
  fi
}

run_stage "Build mod (${CONFIG})" \
  dotnet build src/Mod/Mod.csproj --configuration "$CONFIG"

run_stage "Core unit tests" \
  dotnet test tests/Core.Tests

# The Atlas suite boots a real headless server; the FixtureBuilders world-builder scenario is not
# a pass/fail test, so it is excluded from a normal run (see README "Running the Atlas suite").
run_stage "Atlas integration suite" \
  dotnet test tests/Integration.Tests --filter "FullyQualifiedName!~FixtureBuilders"

if [[ "$RESTAGE" -eq 1 ]]; then
  run_stage "Restage into Mods folder" \
    ./build/restage.sh "$CONFIG"
  echo ""
  echo "✓ verify passed -- all stages green and the mod is restaged."
else
  echo ""
  echo "✓ verify passed -- all stages green (restage skipped)."
fi
