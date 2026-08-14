## Why

Opening any full-page Scribe surface (Lectern, Notebook, or Tablet) flashes the world white for
one frame behind the dialog. It is visually jarring on every open, and the author wants it fixed —
not merely diagnosed. This was surfaced and characterized during `reconcile-animating-surfaces`
(§3.11) but is **bisect-confirmed pre-existing** (it reproduces on `5f6022a`, before the
`ScribeDialogBody` reconcile root existed) and is orthogonal to that change's identity work, so it
is split out here rather than blocking the reconcile branch's archive.

## What Changes

- **Confirmed (§1.1, 2026-08-11):** painting the pixel-art backdrop bitmap on open is the mechanism
  (Pixel Art OFF → no flash). **Root-caused (§2.1, 2026-08-13, DLL + LibGUI-source research):** the
  flash is the one **cold GPU texture upload** for that backdrop landing on a live gameplay frame —
  synchronous `GL.TexImage2D` on the single shared GL context stalls the frame and drops the opaque
  terrain pass. Size-independent (128 KB flashes as hard as 4.75 MB). VS never hitches because it
  warms all its textures behind the loading screen; LibGUI uploads asset bitmaps lazily on first draw.
- **Fix (Route 1, see design.md):** mirror LibGUI's own `VsIconTextureCache` idiom — upload each
  backdrop to a GL texture at mod load (behind the loading screen, off the live frame), then draw it
  as a GPU-resident `SKImage` via `SKImage.FromTexture(GrContext,…)`, which references the resident
  texture without re-uploading. Eliminates the in-frame upload entirely, including the first open.
- Complementary (kept, not the fix): `SetImmutable`, the 128×145 native re-export, and
  nearest-neighbour crispness — these cut every-open → first-open and make warming all specs at load
  cheap, but do not reach zero on their own (`SKImage.FromBitmap` still lazy-uploads).
- Verify any fix with the DEBUG frame-trace / OpenCV frame-extract method **and** a visual check
  (origin/format correctness of the wrapped texture) — do NOT add speculative GL code.

## Capabilities

### New Capabilities
<!-- None. This is a rendering-artifact bug fix with no new player-facing capability and no
     spec-level behavior change: the dialogs already specify "open and paint their backdrop"; this
     only removes a one-frame terrain-pass dropout behind that paint. -->

### Modified Capabilities
<!-- None. No requirement changes. -->

## Impact

- Almost certainly `ScribeDialogBase.Layout.cs` (`WrapBackdrop`, ~line 88 — the backdrop paint) and
  the backdrop bitmap upload path; possibly a load-time texture-residency addition. No `Core`
  changes (this is pure client-render). No new mod dependency.
- Localization of the artifact is already done: Lectern + Notebook + Tablet flash; the `.ui`
  showcase LibGUI windows and the `GuiBase`-derived Scribe Settings window do NOT; clicking inside
  an already-open Scribe window does NOT. The isolated variable is painting the pixel-art parchment
  backdrop bitmap on open.
- Full prior write-up lives in `VSAPI-NOTES.md` (`## "White flash" behind a Scribe dialog…`) and in
  memory `[[white-flash-is-world-render-stall]]`; this change carries that diagnosis to a fix.
