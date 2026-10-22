#!/usr/bin/env python3
"""Reorder an `ilspycmd -il` dump so it no longer depends on where code sits in the source.

Moving a type to another file, or splitting a class into partials, changes the order types and members appear in
the dump while leaving every instruction identical. This sorts top-level types by name and the members inside each
type (fields, methods, properties, events, nested types) by their text, recursing into nested types, so two dumps
of the same code diff clean.

Also masked or ignored, because they follow source position and not behavior: method RVA offsets, the method
ordinal inside generated closure names (`<>c__DisplayClass62_0`), blank lines, and the `// Fields` / `// Methods`
section labels. Reads stdin, writes stdout.
"""
import re
import sys

TYPE_START = re.compile(r"^\.class\b")
RVA = re.compile(r"(// Method begins at RVA )0x[0-9a-fA-F]+")
# Compiler-generated closure/lambda names (`<>c__DisplayClass62_0`, `<>9__50_0`, `<Build>b__62_1`) lead with the
# declaring method's ordinal in the class, which shifts when methods move between files. Mask that ordinal and
# keep the index after it, which counts closures inside one method.
GENERATED_ORDINAL = re.compile(r"(<>c__DisplayClass|<>9__|>b__|<>8__)\d+(_\d+)")


def split_types(lines):
    """Yield (header_or_preamble, [lines]) chunks: every top-level `.class` block, plus anything outside one."""
    chunk, in_type = [], False
    for line in lines:
        if TYPE_START.match(line) and chunk:
            yield in_type, chunk
            chunk = []
        if TYPE_START.match(line):
            in_type = True
        chunk.append(line)
        if line.startswith("} // end of class"):
            yield in_type, chunk
            chunk, in_type = [], False
    if chunk:
        yield in_type, chunk


def sort_members(block, depth=1):
    """Keep a type's header up to its first member, then emit members sorted by their text.

    Members sit one tab deeper than their type; a nested type is sorted the same way, one level further in."""
    tabs = "\t" * depth
    member_start = re.compile("^" + tabs + r"(\.method|\.field|\.property|\.event|\.class)\b")
    section_label = re.compile("^" + tabs + r"// (Fields|Methods|Properties|Events|Nested Types)\s*$")
    first = next((i for i, l in enumerate(block) if member_start.match(l)), None)
    if first is None:
        return block
    head, rest = block[:first], block[first:]
    tail_at = len(rest)
    for i in range(len(rest) - 1, -1, -1):
        if "} // end of class" in rest[i] and rest[i].startswith("}" if depth == 1 else tabs[:-1] + "}"):
            tail_at = i
            break
    body, tail = rest[:tail_at], rest[tail_at:]
    members, cur = [], []
    for line in body:
        if section_label.match(line):
            continue
        if member_start.match(line) and cur:
            members.append(cur)
            cur = []
        cur.append(line)
    if cur:
        members.append(cur)
    members = [sort_members(m, depth + 1) if member_start.match(m[0]).group(1) == ".class" else m for m in members]
    members.sort(key=lambda m: "\n".join(m))
    return head + [l for m in members for l in m] + tail


def main():
    # The RVA comment is a file offset, so it moves whenever code moves; mask it.
    lines = [GENERATED_ORDINAL.sub(r"\1#\2", RVA.sub(r"\1<rva>", l)) for l in sys.stdin.read().split("\n") if l.strip()]
    types, other = [], []
    for is_type, chunk in split_types(lines):
        (types if is_type else other).append(sort_members(chunk) if is_type else chunk)
    types.sort(key=lambda b: b[0])
    out = [l for c in other for l in c] + [l for c in types for l in c]
    sys.stdout.write("\n".join(out))


main()
