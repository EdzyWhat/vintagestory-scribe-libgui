## Context

Scribe vendors LibGUI's managed DLLs (`src/Mod/lib/`, gitignored) for compile and loads the `gui` mod at runtime.
The Atlas suite stages the `gui` zip into the test world. A diff of the 3.1.0 and 4.0.0 sources shows
4.0.0 is binary compatible for mods compiled against 3.x; the one source-level break is `ItemStackDisplay`
no longer deriving `RenderObjectWidget`, which Scribe never relies on (it only constructs it). Nothing was
removed; `WindowFrame`/`WindowTitleBar` 3.x constructors remain as `[Obsolete]` forwarders until 6.0.0.

## Goals / Non-Goals

**Goals:**
- Scribe builds, passes `build/verify.sh` (Core + Atlas), and plays identically on `gui` 4.0.0.
- A playtest list that targets what 4.0.0 actually changed, so regressions surface here and not later.

**Non-Goals:**
- Adopting new LibGUI features, deleting workarounds, or refactoring. One step at a time.
- Proving the Linux crash fix (needs a Linux player), or resolving the HUD-timer report.

## Decisions

- **Bump the dependency to exactly `4.0.0`.** Matches how it is pinned today and what we test against.
  Alternative: a lower bound or range; rejected because the version we verified is the only one we can vouch for.
- **Leave `ScribeHarfBuzzLoadFix` in place.** Its Harmony patch targets `Gui.NativeLibraryLoader.Register` by
  name; 4.0.0 changed that method to `Register(ILogger?)`, which the name-based lookup still finds, and the
  prefix skips gui's original, so behavior should be unchanged. Verify at startup (the log line shows the patch
  applied) rather than remove it blind. Removal is a follow-up gated on Linux confirmation.
- **Refresh the local source clone to 4.0.0** and drop the "decompile `Gui.dll`" guidance from
  `docs/libgui-reference.md`, since public source now matches what we ship.
- **Playtest by risk, not exhaustively.** The riskiest areas are custom render objects that paint outside their
  own box (4.0.0 crops a repaint boundary's recording to its computed paint bounds), hover/tooltip behavior
  (new global pointer tracking, the mod's own synthetic hover re-dispatch), and the HUD (repaint now
  stops at the nearest boundary, and the HUD-timer report may be related).

## Risks / Trade-offs

- [Custom render objects clipped inside the new auto-placed boundaries] → Playtest glow, cuneiform field,
  multiline field, raster icon and backdrop painting; if something clips, fix is a follow-up override.
- [Hover or tooltip behavior shifts] → Playtest collapse animations, row delete buttons, tooltips, and
  dialogs stacked over each other.
- [Known 3.1.0 workarounds interact with 4.0.0 fixes] → Left untouched; each is checked in the follow-up.
- [Harmony patch no longer applies on Linux] → Log line check; fails closed to gui's own (now DEEPBIND) loader,
  so the downside is a duplicate fix, not a crash.
- [Players on 3.1.0 cannot load this build] → Called out in the changelog; the mod page dependency is updated.
