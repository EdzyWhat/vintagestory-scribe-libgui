#!/usr/bin/env bash
# Follow the Vintage Story game logs and filter them down to Scribe-relevant lines, so a
# developer can watch the server-authoritative flow live during a playtest without hand-building
# the log path and grep each time.
#
# By default it follows the SERVER log (server-main.log) -- the mod's `[scribe]`-prefixed trace
# is emitted at Notification level server-side (see ScribeModSystem.Trace), which is exactly what
# you want when diagnosing whether a network round-trip (pin/complete) actually landed on the
# server. The filter also keeps asset/mod-load failures (Error/Warning/Fatal lines and lines
# mentioning scribe/gui) so a packaging or dependency problem is visible in the same stream.
#
# Resolves the log directory from the standard VintagestoryData location, overridable via the
# VINTAGESTORY_DATA env var (matches the game's own --dataPath). Note the current game writes
# `*-main.log` (older builds/docs say `*-main.txt`); this resolves whichever exists.
#
# Usage:
#   ./build/scribe-log.sh            Follow server-main.log, Scribe lines + load errors only
#   ./build/scribe-log.sh --client   Also follow client-main.log (merged, tagged S:/C:)
#   ./build/scribe-log.sh --all      No filter -- raw follow of the selected log(s)
#   ./build/scribe-log.sh --since     Show the whole current file first, then follow (default: tail only)
set -euo pipefail

DATA_DIR="${VINTAGESTORY_DATA:-$HOME/Library/Application Support/VintagestoryData}"
LOG_DIR="$DATA_DIR/Logs"

INCLUDE_CLIENT=0
NO_FILTER=0
FROM_START=0
for arg in "$@"; do
  case "$arg" in
    --client) INCLUDE_CLIENT=1 ;;
    --all)    NO_FILTER=1 ;;
    --since)  FROM_START=1 ;;
    *) echo "usage: $0 [--client] [--all] [--since]" >&2; exit 2 ;;
  esac
done

if [[ ! -d "$LOG_DIR" ]]; then
  echo "error: log directory not found: $LOG_DIR" >&2
  echo "  set VINTAGESTORY_DATA to your VintagestoryData folder if it lives elsewhere." >&2
  exit 1
fi

# Resolve one log by base name, preferring the current .log and falling back to a legacy .txt.
resolve_log() {
  local base="$1"
  if [[ -f "$LOG_DIR/$base.log" ]]; then echo "$LOG_DIR/$base.log";
  elif [[ -f "$LOG_DIR/$base.txt" ]]; then echo "$LOG_DIR/$base.txt";
  fi
}

FILES=()
server_log="$(resolve_log server-main)"
[[ -n "$server_log" ]] && FILES+=("$server_log")
if [[ "$INCLUDE_CLIENT" -eq 1 ]]; then
  client_log="$(resolve_log client-main)"
  [[ -n "$client_log" ]] && FILES+=("$client_log")
fi

if [[ "${#FILES[@]}" -eq 0 ]]; then
  echo "error: no server-main log found in $LOG_DIR (has the game/server run yet?)" >&2
  exit 1
fi

echo "Following: ${FILES[*]}"
if [[ "$NO_FILTER" -eq 1 ]]; then
  echo "(raw -- no filter)"
else
  echo "(filter: [scribe] trace + asset/mod-load errors; use --all for raw)"
fi
echo "----"

# -F keeps following across the game's log rotation (it reopens the path when a new file replaces
# it). -n0 starts at the end (tail only) unless --since asked for the whole file first.
TAIL_START="0"
[[ "$FROM_START" -eq 1 ]] && TAIL_START="+1"

if [[ "$NO_FILTER" -eq 1 ]]; then
  tail -F -n "$TAIL_START" "${FILES[@]}"
else
  # Keep: our own [scribe] trace, and Error/Warning/Fatal lines that mention scribe or the gui dep
  # (asset/mod-load failures). Line-buffered grep so lines appear as they arrive, not in blocks.
  tail -F -n "$TAIL_START" "${FILES[@]}" \
    | grep --line-buffered -iE '\[scribe\]|\[(Error|Fatal)\]|(Warning).*(scribe|gui)|(scribe|gui).*(not found|failed|missing|could not)'
fi
