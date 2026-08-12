## Why

Opening any full-page Scribe surface (Lectern, Notebook, or Tablet) flashes the world white for
one frame behind the dialog. It is visually jarring on every open, and the author wants it fixed —
not merely diagnosed. This was surfaced and characterized during `reconcile-animating-surfaces`
(§3.11) but is **bisect-confirmed pre-existing** (it reproduces on `5f6022a`, before the
`ScribeDialogBody` reconcile root existed) and is orthogonal to that change's identity work, so it
is split out here rather than blocking the reconcile branch's archive.

## What Changes

- Run the one decisive discriminator still outstanding — open a flashing surface with **Pixel Art
  Display OFF** (the parchment backdrop becomes a plain `SizedBox` with no texture) — to confirm or
  refute that painting the 1024×1160 backdrop bitmap on open is the mechanism.
- If confirmed: pre-decode/upload the backdrop as a persistent GPU texture at mod load (or otherwise
  keep it resident between opens) so no cold per-open texture upload lands on a live frame; and find
  why the Skia-backed texture appears evicted between closes.
- If refuted: trace what else `ScribeDialogBase` / `GuiDialogBlockEntityBase` do on open that the
  clean `GuiBase`-derived Settings window (which does NOT flash) skips, and fix that.
- Verify any fix with the DEBUG frame-trace / OpenCV frame-extract method — do NOT add render/GL
  code to Scribe blindly.

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
