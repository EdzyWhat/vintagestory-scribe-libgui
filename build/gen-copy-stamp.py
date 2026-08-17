#!/usr/bin/env python3
"""Generate the pixel-art wooden rubber stamp used by the Scriptorium "Transcribe" copy
flourish (add-transcribe-copy-paste, task 5.1).

This is a swappable asset ("I bake it, you refine"): the mod loads the PNG by a fixed path
and scales it up NEAREST-neighbour (crisp pixels) via ScribePixelArtBackdrop, so the authored
size stays modest. Re-run this script to tweak the palette/shape, or repaint the PNG by hand —
the filename is stable, so no code changes to swap the art.

    python3 build/gen-copy-stamp.py

Output: src/Mod/assets/scribe/textures/gui/scribe-copy-stamp.png (48x66, RGBA).

PERSPECTIVE (refinement 2026-08-16): the stamp is drawn as a classic wooden rubber stamp seen
from a 3/4 view slightly ABOVE — a rounded knob handle on top, a turned neck, and a round
wooden base whose TOP face is visible. Because we look down onto it, the red rubber die on the
UNDERSIDE never shows (the old upright view showed the red pressing face, which read as "seen
from underneath"). No red at all now — pure wood. The base is the round disk the handle rises
from, NOT the flat wooden block a stamp rests on in product photos (that block is intentionally
omitted). Taller-than-wide aspect so it doesn't read as squished. The animation descends it
onto the Duplicate slot, presses with a slight squash, then lifts, leaving a "COPIED" imprint
(that imprint is rendered procedurally in code, NOT from this asset).

NOTE (2026-08-16): the SHIPPED scribe-copy-stamp.png is now a hand-painted asset supplied by the
author, NOT the output of this baker. This script is kept as the fallback "I bake it, you refine"
starting point; re-running it will OVERWRITE the hand-painted art with the round-base bake below.
"""

import os
from PIL import Image, ImageDraw

# Earthen / wood palette (matches the parchment-and-wood dialog theme). No red — the pressing
# face is on the hidden underside in this above-angle view.
OUTLINE = (36, 24, 12, 255)
WOOD_XD = (58, 36, 18, 255)   # deepest shade (base underside band)
WOOD_D  = (84, 52, 26, 255)
WOOD_M  = (122, 78, 40, 255)
WOOD_L  = (156, 108, 60, 255)
WOOD_H  = (190, 144, 88, 255)
WOOD_XH = (214, 176, 120, 255)  # brightest specular

W, H = 48, 66
CX = 24  # horizontal centre

img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# --- Round wooden BASE, seen from 3/4 above (top ellipse + thickness band + bottom rim) ---
# Drawn first (furthest back / lowest). The underside band uses the deepest shade; the top
# face is the widest element of the whole stamp.
d.ellipse((6, 44, 42, 55), fill=WOOD_XD, outline=OUTLINE)   # bottom front rim of the disk
d.rectangle((6, 38, 42, 50), fill=WOOD_XD)                  # vertical thickness band
d.ellipse((6, 31, 42, 46), fill=WOOD_M, outline=OUTLINE)    # top face (visible from above)
d.ellipse((11, 33, 31, 42), fill=WOOD_L)                    # top-face sheen
d.ellipse((14, 34, 24, 39), fill=WOOD_H)                    # bright specular on the top face
d.arc((6, 31, 42, 46), start=25, end=155, fill=OUTLINE)     # crisp front lip of the top face

# --- Turned NECK rising from the base centre to the knob ---
# Drawn on top of the base top face (it emerges from the disk's centre toward the viewer).
d.rectangle((19, 20, 29, 35), fill=WOOD_D, outline=OUTLINE)
d.rectangle((20, 20, 22, 35), fill=WOOD_M)                  # left-lit edge
d.rectangle((19, 24, 29, 26), fill=WOOD_L)                  # a turned ring highlight
d.rectangle((19, 30, 29, 31), fill=WOOD_XD)                 # ring shadow below it

# --- KNOB handle (rounded wooden ball on top) ---
d.ellipse((13, 2, 35, 23), fill=WOOD_M, outline=OUTLINE)
d.ellipse((16, 4, 30, 17), fill=WOOD_L)                     # main lit face
d.ellipse((18, 5, 26, 12), fill=WOOD_H)                     # highlight
d.ellipse((20, 6, 24, 9),  fill=WOOD_XH)                    # specular dot
d.arc((13, 2, 35, 23), start=35, end=150, fill=WOOD_XD)     # underside shade of the knob

out = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "src", "Mod", "assets", "scribe", "textures", "gui", "scribe-copy-stamp.png",
)
img.save(out)
print(f"wrote {out} ({W}x{H})")
