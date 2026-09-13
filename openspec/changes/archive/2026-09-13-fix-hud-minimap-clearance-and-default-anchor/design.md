## Context

See proposal.md - Why. Two independent pieces of code change together because they touch the same
anchor logic:

- `HudScribePins.cs`'s `ApplyAnchor()`: `prebakeX = anchor == ScribeHudAnchor.TopRight && minimapOn ?
  DefaultTopRightMinimapClearanceX : 0f;` where `DefaultTopRightMinimapClearanceX = 260f` is a
  compile-time constant derived from the vanilla minimap's own logical-pixel footprint (confirmed
  against the open-source `GuiDialogWorldMap.cs`: `ElementBounds.Fixed(0,0,250,250)` +
  `GuiStyle.DialogToScreenPadding` (10f) = 260, both defined in vanilla's GUIScale-invariant logical
  space).
- `ScribePlayerSettings.cs`: `HudAnchor` defaults to `ScribeHudAnchor.TopRight`;
  `NormalizeAnchor` falls back to `TopRight` for any unrecognized stored value.

The clearance bug is confirmed in-game (author repro, GUI Scale 8) but not root-caused at the engine
level — the vanilla minimap's own numbers are GUIScale-invariant by construction on paper, so the
mismatch most likely lives in how LibGUI's Skia-based rendering pipeline and vanilla's Cairo-based
dialog system each convert `RuntimeEnv.GUIScale` to actual screen pixels, which this project cannot
fully verify without decompiling `VintagestoryLib.dll` (the closed-source engine implementation — no
open equivalent exists, unlike `VintagestoryAPI.dll`/`vsessentialsmod`).

## Goals / Non-Goals

**Goals:**
- The top-right anchor's minimap clearance is visually consistent across GUI Scale settings, without
  requiring a fully understood engine-level explanation for why the old constant drifted.
- New installs default to an anchor that needs no minimap-clearance guess at all.
- Zero behavior change for a player who already has an explicit anchor preference saved (at any
  value, including a previously-default-only `TopRight`... with the one accepted exception noted in
  Risks below).

**Non-Goals:**
- Fully root-causing *why* the two rendering pipelines diverge at non-reference GUI Scale. Useful to
  know eventually (worth a `VSAPI-NOTES.md` entry if ever confirmed), but not required to ship a
  correct-by-construction fix.
- Changing the anchor picker's available positions, offset mechanism, or adding an in-mod settings UI
  for it (explicitly out of scope per the existing spec).
- Migrating any player's already-saved `HudAnchor` value.

## Decisions

**Stop deriving the clearance from a hardcoded pixel constant; measure the minimap's actual on-screen
footprint at runtime instead.** Rather than trying to find the "right" constant for every GUI Scale
(which requires understanding a cross-pipeline conversion this project can't fully verify),
`ApplyAnchor` SHALL query the live minimap HUD dialog's actual rendered bounds when it is open and
compute the clearance from that, falling back to today's constant only when the minimap dialog isn't
resolvable (e.g. `showMinimapHud` is off, or the dialog hasn't composed yet). Vanilla's minimap is a
`GuiDialogWorldMap` registered as a `GuiDialog` with `EnumDialogType.HUD`; `capi.Gui.OpenedGuis`
exposes open dialogs by type, so Scribe can look up the vanilla world-map HUD dialog the same way
`HudScribePins.DialogHeldTrackerDocs()` already looks up its own dialog type via
`capi.Gui.OpenedGuis.OfType<T>()` — same pattern, different target type. Reading its `SingleComposer`
(or equivalent) bounds gives the actual on-screen width/position in whatever units that dialog
already resolved GUIScale into, sidestepping the cross-pipeline conversion question entirely: Scribe
never needs to know *why* the two pipelines differ, only what the minimap's real footprint currently
is. Alternative considered: find and apply the correct GUIScale conversion factor by trial (test at
GUI Scale 8/10/12, fit a formula). Rejected — fragile (a magic-number fix for a not-fully-understood
mismatch), and would need re-deriving again if a future game update changes either pipeline's
conversion.

**If the live minimap dialog isn't resolvable, fall back to today's constant unchanged, not a
different guess.** Keeps a graceful degradation path (e.g. very early in `StartClientSide` before any
dialog has composed) without inventing a second guess to maintain. This is a narrow, rare fallback
window, not the steady-state path.

**Cache-key impact on `hud-anchor-optimization`'s per-frame gating:** `AnchorInputs` (the
`ApplyAnchor` cache key) currently holds `(ScreenW, ScreenH, Anchor, OffX, OffY, MinimapOn)`. Reading
the live minimap's rendered width each call means that width must join the cache key (or the read
must itself be cheap enough to do unconditionally before the key comparison) so a minimap resize
(e.g. the player opens/closes the full map, or drags... though the minimap itself isn't resizable
today) is still honored without recomputing every frame when nothing changed. Reading one dialog's
already-computed bounds is a cheap property read, not a fresh layout pass, so this stays consistent
with that capability's existing "don't do the expensive part unless an input changed" contract — no
spec change needed there, only an implementation-level addition to what counts as an input.

**Default anchor: flip both the default and the invalid-value fallback to `TopLeft` together.**
Leaving `NormalizeAnchor`'s fallback at `TopRight` while the default becomes `TopLeft` would mean a
corrupted/legacy config value silently lands on the anchor this change is specifically moving away
from as the "safe" choice — inconsistent for no benefit. Both become `TopLeft`.

## Risks / Trade-offs

[A player who never touched the anchor setting, and whose config file already has an explicit
`"HudAnchor": "TopRight"` written to disk (because it was the default at the time they first loaded),
will NOT move to `TopLeft` after this change — only a genuinely new install (no file yet) gets the
new default] → Documented in proposal.md as an accepted, known limitation. Not attempting a migration
(e.g. "reset to new default if the stored value equals the old default and was never explicitly
chosen") because there is no reliable signal in the stored data to distinguish "explicitly chose
TopRight" from "never touched it while TopRight was the default" — inventing one would be more
complex and riskier than leaving existing players where they are.

[Looking up the live minimap dialog by type assumes `GuiDialogWorldMap` (or whatever the current
game version's class/dialog-type is) is resolvable via `capi.Gui.OpenedGuis` the way
`ScribeDialogBase` instances already are for `DialogHeldTrackerDocs`] → Verify this resolves in the
same VS version this project already targets (1.22.x) before relying on it; if the vanilla minimap
HUD dialog is only present in `OpenedGuis` while actually open/composed (not merely enabled in
settings), confirm the fallback path (today's constant) covers the gap rather than transiently
zeroing the clearance.

[`GuiDialogWorldMap` lives in `Vintagestory.GameContent`, but concretely in the `VSEssentials.dll`
assembly (confirmed against the shipped game install's `Mods/` folder) — `Mod.csproj` currently
references `VSSurvivalMod.dll` for other `GameContent` types but does NOT reference
`VSEssentials.dll`. This is a new build-time dependency, not just a new `using`] → A task below adds
the `<Reference Include="VSEssentials">` entry (same `HintPath`/`Private=false`-style pattern already
used for `VSSurvivalMod`); this is a hard-dependency reference like the existing one, not a new mod
dependency (still vanilla `VintagestoryAPI`-family DLLs shipped with the game, same category the
"no new mod dependencies" guardrail already exempts).

## Migration Plan

No data/save migration. Client-local preference file: an existing saved `HudAnchor` value (at any
setting) is read as-is and unaffected. No rollback concern beyond reverting the code change; this is
a client-side, cosmetic-positioning fix with no persisted-state hazard.
