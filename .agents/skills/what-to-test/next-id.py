#!/usr/bin/env python3
"""Hand out the next TESTING.md item code(s).

The playtest app parses item codes with a strict `^[0-9a-f]{8}$` regex. A zero-padded
incrementing counter formatted as 8 hex digits (00000001, 00000002, ... 0000000a, ...)
satisfies that regex exactly, is unique, and needs no hashing. This replaces the old
sha256(task-id + text)[:8] scheme for NEW items — existing sha256 codes already in
TESTING.md keep working unchanged (both forms match the same regex).

Usage:
    python3 next-id.py            # print the next 1 code, advance the counter
    python3 next-id.py 3          # print the next 3 codes, advance by 3
    python3 next-id.py --peek     # show the next code WITHOUT advancing

The counter persists in `.next-id` beside this script. Codes already present in the
repo's TESTING.md are skipped, so a sequential code can never collide with a legacy
sha256 code that happens to look like a low number.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
COUNTER_FILE = HERE / ".next-id"
# repo root is three levels up from .claude/skills/what-to-test/
TESTING_MD = HERE.parents[2] / "TESTING.md"

CODE_RE = re.compile(r"`([0-9a-f]{8})`")


def used_codes() -> set[str]:
    if not TESTING_MD.exists():
        return set()
    return set(CODE_RE.findall(TESTING_MD.read_text(encoding="utf-8")))


def read_counter() -> int:
    if COUNTER_FILE.exists():
        try:
            return int(COUNTER_FILE.read_text().strip())
        except ValueError:
            pass
    return 1  # first code is 00000001


def main() -> int:
    args = [a for a in sys.argv[1:]]
    peek = "--peek" in args
    args = [a for a in args if a != "--peek"]
    count = int(args[0]) if args else 1

    n = read_counter()
    taken = used_codes()
    out = []
    while len(out) < count:
        code = format(n, "08x")
        n += 1
        if code in taken:
            continue
        out.append(code)

    if not peek:
        COUNTER_FILE.write_text(str(n) + "\n")

    print("\n".join(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
