#!/usr/bin/env bash
# Double-clickable launcher for Scribe's playtest checklist. The actual app lives in the
# standalone vs-playtest-checklist repo (https://github.com/EdzyWhat/vs-playtest-checklist)
# -- this just points it at this repo's own TESTING.md and opens it.
#
# Always kills anything already on PORT and starts a fresh server rather than reusing
# one that's already running there -- server.py has no state worth preserving, and
# reusing a stale process would silently keep serving old code after a `git pull` of
# vs-playtest-checklist.
set -euo pipefail

PORT=8792
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TESTING_FILE="$REPO_ROOT/TESTING.md"
APP_DIR="$HOME/claude/vintage-story/vs-playtest-checklist"
URL="http://localhost:${PORT}/index.html"

if [[ ! -d "$APP_DIR" ]]; then
  echo "vs-playtest-checklist not found at $APP_DIR -- clone it first:" >&2
  echo "  git clone https://github.com/EdzyWhat/vs-playtest-checklist.git $APP_DIR" >&2
  exit 1
fi

EXISTING_PID="$(lsof -ti "tcp:${PORT}" 2>/dev/null || true)"
if [[ -n "$EXISTING_PID" ]]; then
  kill $EXISTING_PID 2>/dev/null || true
  sleep 0.3
fi

python3 "$APP_DIR/server.py" --testing-file "$TESTING_FILE" --port "$PORT" &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null' EXIT

for _ in $(seq 1 30); do
  if curl -s -o /dev/null "$URL"; then
    break
  fi
  sleep 0.2
done

open "$URL"

wait "$SERVER_PID"
