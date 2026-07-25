#!/usr/bin/env bash
# Install the version-controlled git hooks into this clone's .git/hooks/. Git does not clone
# hooks, so this must be run once per fresh checkout to activate the opt-in pre-push gate
# (build/hooks/pre-push -> Core + Atlas suites before a push to main; see README).
#
# Symlinks rather than copies, so edits to build/hooks/<name> take effect with no re-install.
# Refuses to clobber an unrelated existing hook that isn't already our symlink (so a hand-written
# .git/hooks/pre-push is never silently overwritten) -- re-run with --force to replace it.
#
# Usage: ./build/install-hooks.sh [--force]
set -euo pipefail

FORCE=0
[[ "${1:-}" == "--force" ]] && FORCE=1

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

GIT_DIR="$(git rev-parse --git-dir)"
HOOKS_DEST="$GIT_DIR/hooks"
mkdir -p "$HOOKS_DEST"

installed=0
for src in build/hooks/*; do
  [[ -f "$src" ]] || continue
  name="$(basename "$src")"
  dest="$HOOKS_DEST/$name"
  # Absolute source path so the symlink resolves regardless of the .git/hooks working dir.
  src_abs="$REPO_ROOT/$src"

  if [[ -e "$dest" || -L "$dest" ]]; then
    # Already ours (points at our source)? Nothing to do. Someone else's? Respect it unless --force.
    if [[ -L "$dest" && "$(readlink "$dest")" == "$src_abs" ]]; then
      echo "  $name: already installed"
      continue
    fi
    if [[ "$FORCE" -ne 1 ]]; then
      echo "  $name: a different hook already exists at $dest -- leaving it (re-run with --force to replace)" >&2
      continue
    fi
  fi

  ln -sf "$src_abs" "$dest"
  chmod +x "$src_abs"
  echo "  $name: installed -> $dest"
  installed=$((installed + 1))
done

echo ""
echo "Done. $installed hook(s) newly installed. Bypass any hook with: git push --no-verify"
