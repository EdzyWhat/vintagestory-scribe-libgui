#!/usr/bin/env python3
"""Assemble a STARTER Scriptorium block shape by combining four vanilla shapes:
  - table-long          (clutter)  -> the desk body            [anchor, no offset]
  - lecturn-book-open   (clutter)  -> slanted reading board + open book (post/legs DROPPED)
  - bookpile1           (clutter)  -> a stack of books, lifted onto the tabletop
  - inkandquill         (item)     -> inkwell + quill, set on the tabletop beside the book

Coordinate rule (verified against the vanilla files): child element from/to are RELATIVE to
their PARENT element's `from`; only rotationOrigin drives rotation. So to translate a whole
group we offset TOP-LEVEL elements only -- nested children ride along. (A recursive offset would
double-shift nested groups, so we deliberately do NOT recurse.)

This is a rough first pass for opening in VS Model Creator and dragging pieces into final place;
textures are the vanilla survival-domain paths (the final block JSON will override them anyway).
"""
import json, copy, os

VS = "/Applications/Vintage Story.app/assets/survival/shapes"
SRC = {
  "table":  f"{VS}/block/clutter/table-long.json",
  # The BOOKSHELVES variant (ornate ebony/mahogany lectern), not the plain standalone clutter one.
  "lectern":f"{VS}/block/clutter/bookshelves/lecturn-book-open.json",
  "pile":   f"{VS}/block/clutter/bookshelves/bookpile1.json",
  "ink":    f"{VS}/item/tool/inkandquill.json",
}
def load(p): return json.load(open(p))

def offset_toplevel(elems, d):
    """Add delta d=(dx,dy,dz) to the from/to (and rotationOrigin) of each TOP-LEVEL element only."""
    out = []
    for e in elems:
        e = copy.deepcopy(e)
        for key in ("from","to","rotationOrigin"):
            if key in e and e[key] is not None:
                e[key] = [e[key][0]+d[0], e[key][1]+d[1], e[key][2]+d[2]]
        out.append(e)
    return out

def prefix_tex(tex, domain="survival"):
    return {k: (v if ":" in v else f"{domain}:{v}") for k,v in tex.items()}

table   = load(SRC["table"])
lectern = load(SRC["lectern"])
pile    = load(SRC["pile"])
ink     = load(SRC["ink"])

# --- textures: merge all four (keys are unique across sources), prefix survival: ---
textures = {}
for s in (table, lectern, pile, ink):
    textures.update(prefix_tex(s.get("textures", {})))

elements = []

# 1) DESK = table-long, unchanged (anchor). Rename top-level for clarity.
desk = offset_toplevel(table["elements"], (0,0,0))
for e in desk: e["name"] = "desk:" + str(e.get("name"))
elements += desk

# 2) READING = the FULL bookshelves lectern (stand + legs + feet + book rest), inserted as one new
#    group, nothing trimmed (the stand is wanted). Native placement (offset 0) — drag into final spot
#    in Model Creator. Only the top-level 'origin' is renamed for clarity.
lorigin = copy.deepcopy(lectern["elements"][0])
lorigin["name"] = "reading:origin"
elements.append(lorigin)

# 3) BOOKPILE = all top-level book stacks, lifted onto the tabletop and nudged back-left.
pile_off = offset_toplevel(pile["elements"], (-2, 18, 4))
for e in pile_off:
    if e.get("name") == "origin": e["name"] = "pile:origin"
elements += pile_off

# 4) INK & QUILL = single 'origin' group, raised onto the tabletop beside the book (front-right).
ink_off = offset_toplevel(ink["elements"], (3, 17, -4))
for e in ink_off: e["name"] = "ink:" + str(e.get("name"))
elements += ink_off

shape = {
  "editor": {"allAngles": False, "entityTextureMode": False, "showSizeAdjustDialog": False},
  "textureWidth": 16, "textureHeight": 16,  # all four sources author UVs in a 16x16 space
  "textures": textures,
  "elements": elements,
}
out = "/Users/nick.edises/claude/vintage-story/vintagestory-scribe-libgui/src/Mod/assets/scribe/shapes/block/scriptorium/scriptorium.json"
with open(out, "w") as f:
    json.dump(shape, f, indent="\t")
    f.write("\n")
print("wrote", out)
print("top-level elements:", len(elements), "| textures:", len(textures))

# Emit the BLOCK `textures` dict (VS block form: key -> { base: "domain:path" }) for pasting into
# scriptorium.json, so the model renders textured in-game (embedded shape textures are editor-only for blocks).
block_tex = {k: {"base": v} for k, v in sorted(textures.items())}
print("\n--- block textures (paste into scriptorium.json) ---")
print(json.dumps(block_tex, indent="\t"))
