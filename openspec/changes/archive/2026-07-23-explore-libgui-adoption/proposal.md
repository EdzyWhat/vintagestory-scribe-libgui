## Decision: GO (2026-07-23)

**Adopt LibGUI as Scribe's GUI foundation.** All make-or-break spike gates passed — renders on
Apple Silicon (A), block-entity dialog lifecycle intact (B), read view renders the live document
(C), and the decisive gate, a live-editable variable-height text row, is buildable on LibGUI's
public API (D), with build & staging proven (E). Four of five checklist boxes are ✅; the fifth —
theme-JSON hot-reload replacing the `ScribeClientConfig` knobs — is **deferred, not blocking**: it
gates only the later theme-extraction step, and the parchment theme can be code-defined until then.

The migration proceeds as a sequence of separate OpenSpec changes (per the spec-driven guardrail),
starting with `adopt-libgui-foundation` (de-spike the build wiring + a production read-view dialog).
The work happens in the sibling project `vintagestory-scribe-libgui` (a full-history fork); the
native-GUI original is retained untouched as a fallback. The throwaway spike source
(`SpikeLibGuiLecternDialog.cs`, `SpikeScribeMultilineField.cs`) does not ship — the latter is kept
transiently as the reference implementation for the editor-view port.

## Why

Building Scribe's lectern GUI on Vintage Story's native `GuiComposer`/`GuiElement` system with
absolute `ElementBounds.Fixed` math has been the exhausting part of the project — the Core logic
was the easy part. Nearly every non-trivial visual (the row, the checkbox, the pin/delete/grip
buttons, the clipped scroll region) had to be re-implemented from scratch as a custom `GuiElement`
baking its own Cairo textures, because the engine's clip, scroll, styling, and cell-list
primitives each failed in a documented way — `VSAPI-NOTES.md` is a catalog of multi-round
render-pass / scissor / focus / reentrancy bugs. The team already built *two* diagnostic aids to
cope with this (the in-game `ScribeInspectOverlay` and the external `scribe-layout-workbench`) and
just opened an empty `own-lectern-element-bounds` change signalling intent to rework the layout
foundation. All of this sits under a roadmap (`ROADMAP.md`) that reuses this GUI foundation across
~6 block/held-item interfaces — so the foundation choice compounds, and now is the moment to ask
whether the foundation itself is the problem.

**LibGUI** (`ripls56/vslibgui`; portal name "libGUI", modid `gui`; MIT; v2.0.0; net10.0; VS
1.22.x) is a from-scratch, Flutter-style **reactive UI framework** rendered via SkiaSharp that
*bypasses* `GuiComposer` entirely. It directly targets every pain point above: flexbox relative
layout (`Row`/`Column`/`Expanded`), a virtualized `ListView` that culls off-screen rows and
supports variable row heights, a centralized semantic theme (`ColorScheme`/`ThemeData`,
hot-reloadable from JSON), and declarative `BoxStyle` styling. It is genuinely compelling for
Scribe's needs.

This change is **research/exploration only**. It does not migrate anything. Its outcome is an
informed decision — and, because the risks are real, that decision is **spike-first, not
"adopt now."**

## What Changes

- **No production code changes.** This change produces documents plus one clearly-gated,
  throwaway proof-of-concept (the "spike"), and captures a go/no-go decision.
- Add a durable **LibGUI reference** (`docs/libgui-reference.md`) distilled from the LibGUI wiki,
  GitHub source, and mod portal — the widget/element/render-object model, layout & flex,
  data-driven row generation, scrolling/virtualization, theming, dialogs, events, and integration
  mechanics.
- Add a **Scribe→LibGUI migration guide** (`docs/libgui-migration-guide.md`) — a concrete mapping
  table from each current custom-GUI file/mechanism to its LibGUI equivalent, plus a migration
  order, for both the author and the agent.
- Clone the LibGUI **wiki** (to `./.wiki/`) and **source** (to `./reference/vslibgui/`) as
  gitignored local references so future work can `ripgrep` them instead of re-fetching.
- Add a **lessons rule**: a `## LibGUI` section in `VSAPI-NOTES.md` plus a one-line rule in
  `CLAUDE.md` to append LibGUI layout-bug/misconception notes there (no separate
  `LESSONS_LEARNED.md` — `VSAPI-NOTES.md` already fills that role).
- **Recommendation: spike first.** Before any adoption, a throwaway proof-of-concept must clear a
  go/no-go checklist (below). Nothing from the spike merges unless the decision is "go."

## Recommendation & the spike gate

Adopt LibGUI **only after** a throwaway proof-of-concept answers all of these. The fork being
decided is explicit: **adopt LibGUI** vs. **continue the custom `GuiElement` +
`own-lectern-element-bounds` path.**

- [x] **Renders at all on Apple Silicon.** ✅ **PASS (2026-07-23).** The make-or-break gate. The
  feared "single `osx` RID → no arm64" risk was disproven twice: (1) static — the bundled
  `native/osx/native/libHarfBuzzSharp.dylib` is a **universal binary containing arm64**
  (`lipo -archs` → `x86_64 arm64`), and `SkiaRenderer.cs` uses an unpinned `GRGlInterface.Create()`
  so it doesn't need GL 4.3 the way VSImGui did; (2) empirical — stock libGUI 2.0.0 installed on the
  M-series Mac, `.ui showcase` renders the full `ExampleGui` window with crisp HarfBuzz-shaped text
  (incl. a syntax-highlighted code block), the gold/brown parchment `ColorScheme`, and live
  interactive layout demos, with no native-load / `GRContext` / GL errors. Evidence:
  `screenshots/debug/2026-07-23_12-39-57_libgui-showcase-renders-on-apple-silicon-gate-a-pass.png`.
- [x] **Block-entity dialog lifecycle intact** ✅ **PASS (2026-07-23).** Via
  `GuiDialogBlockEntityBase`: the spike dialog opens (`.scribespike` at a targeted lectern), the X
  closes it, the title bar drags, and minimize/expand works. Walk-away auto-close was NOT confirmed,
  but that is the pre-existing Creative-mode inflated-reach quirk that affects Scribe's *native*
  dialog too (see ROADMAP "editor view doesn't auto-close" note) — deferred to a survival test once a
  recipe exists, not a LibGUI regression. The server-authoritative packet flow was not re-exercised
  by the spike (it opens directly, reusing the live `Document`); the existing wire path is unchanged.
  Evidence: `screenshots/debug/2026-07-23_13-04-54_libgui-spike-readview-renders-document-gate-bc.png`.
- [x] **Read view renders the live document** ✅ **PASS (2026-07-23).** The spike renders the live
  `ScribeDocument` — a wrapping free-text block plus three task rows whose checkboxes reflect Done
  state and show task text — in the parchment `ColorScheme`, via `Column`/`ListView`/`Row`. (Same
  screenshot.)
- [x] **A live-editable, variable-height text row works** ✅ **PASS (2026-07-23).** LibGUI's stock
  `TextField` is single-line only (`RenderTextField` measures one line; no wrap/`maxLines`; its
  `internal` so not subclassable) — so this was NOT free. But a **custom multi-line editable widget
  built on LibGUI's *public* API** (`src/Mod/SpikeScribeMultilineField.cs`:
  `ScribeMultilineFieldRender : RenderBox` + a `StatefulWidget`/`IFocusable` + `IKeyChar/IKeyDownHandler`,
  greedy-wrapping via public `TextLayoutHelper.MeasureText`, painting via `PaintingContext.DrawText/
  DrawBox`) **wraps, auto-grows in height, holds focus, and accepts typing — both standalone and inside
  a `ListView` row.** Confirmed in-game (screenshots
  `…14-17-47…wraps-and-grows…`, `…14-34-12…focus-type-wrap-grow-all-work.png`). Two lessons banked in
  `VSAPI-NOTES.md`: (i) a custom field must set `FocusNode.Owner = Element` or it never focuses;
  (ii) **LibGUI text fields (incl. its own `TextField`) leak keypresses to the game** — WASD moves the
  player / E opens inventory while typing, because `OnKeyDown` doesn't mark char/movement keys
  `Handled`. Fixable (dialog `CaptureAllInputs() => true`, or a `default:` swallow in the field's
  `OnKeyDown`); Scribe's native GUI already solves this class of problem. **Net: the one capability
  LibGUI didn't give for free is buildable on its public API — this was the decisive gate.**
- [ ] **Theme JSON hot-reload** can replace the ~60 `ScribeClientConfig` layout/color knobs.
- [x] **Build & staging** ✅ **PASS (2026-07-23).** `src/Mod` builds clean (0 warnings/errors) with
  the vendored `Gui.dll` referenced (`Private=false`); `restage.sh` stages it; the client loads the
  mod (proven by `.scribespike` existing and opening the dialog). NOTE: building `Gui.dll` from the
  cloned source was blocked — `Gui.csproj` references six managed assemblies (`Svg.Skia`, `Svg.Model`,
  `Svg.Custom`, `ShimSkiaSharp`, `ExCSS`, `HarfBuzzSharp`, `SkiaSharp.HarfBuzz`) the game install
  ships nowhere; they live inside the published mod zip, so the spike vendors the published `Gui.dll`
  + companions instead. A real adoption would either vendor these or add them as build deps.

**Observed side effect (confirms a flagged risk):** installing the LibGUI mod added a click *sound*
to Scribe's *native* drag-handle button (a `GuiElementToggleButton`) without any Scribe change —
LibGUI's Harmony + `VanillaDialogCleanup` patching reaches vanilla `GuiComposer` widgets globally.
Benign here, but live proof of the "patches vanilla dialogs → compatibility vector" risk. Weigh in
go/no-go.

## Risks & cost (not soft-pedaled)

- **First hard, always-required mod dependency.** CLAUDE.md: "No new mod dependencies … ask before
  adding any"; ConfigLib is only a *soft*, `IsModEnabled`-gated dep. LibGUI would be required on
  both client and server presence-wise (its own rendering is client-only, but consumers declare a
  hard `gui` dependency).
- **Young & thin.** v2.0.0, ~1861 downloads, two consumers (both by the author), two retracted
  early releases for a "critical layout bug." Near-zero external review.
- **Apple-Silicon native risk** (see gate above) — the single biggest unknown.
- **Name collision.** The dependency modid is the generic `gui`; the assembly is `Gui.dll`.
- **Vanilla-dialog patching.** LibGUI uses Harmony + a `VanillaDialogCleanup`, a compatibility
  vector with other GUI-touching mods.
- **Pinned to 1.22.x**, and hijacks the ortho render stage + spawns a phantom `GuiComposer` for
  wheel events — non-trivial coupling to VS internals that can break across game updates.
- **Docs gap.** No `docs/*.md` in the repo; the source and the interactive `.ui` commands are the
  reference (this change's `docs/libgui-reference.md` mitigates that for us).
- **What we'd lose vs. keep.** The `src/Core/` VS-API-free invariant is **unaffected** — LibGUI is
  a Mod-layer concern only. But most of our hard-won `VSAPI-NOTES.md` GuiComposer knowledge becomes
  moot, replaced by learning the Flutter widget/element/render-object model.

## Roadmap fit

One shared LibGUI foundation would be reused across the `ROADMAP.md` interfaces (lectern →
notebook → clay/wax tablets → writing desk → backpack HUD → bulletin board). This rides the same
"build the reusable base once" logic as the already-decided ROADMAP decision #4 (one
artifact-agnostic `scribe:doc:<docId>` store + generalized packets). Deciding the GUI foundation
now, while v1 is the only interface built, is far cheaper than after held-tier GUIs exist.

## Capabilities

### New Capabilities
- `gui-foundation-policy`: the binding project rule that emerges from this exploration — adopting
  any GUI framework (LibGUI or otherwise) as a **hard mod dependency** is gated on a throwaway
  spike clearing an explicit go/no-go checklist, with Apple-Silicon rendering as the make-or-break
  gate. This is the one durable, enforceable outcome; everything else the change produces is
  documentation. (The actual migration, if the spike passes, is a future separate change.)

### Modified Capabilities
<!-- None. No existing lectern-gui-shell (or other) requirement changes. This change does not
     alter the running mod's behavior; it produces docs + a gated spike + this new policy. -->

## Impact

- **New (docs/reference):** `docs/libgui-reference.md`, `docs/libgui-migration-guide.md`; cloned
  `./.wiki/` and `./reference/vslibgui/` (both gitignored).
- **Modified (small):** `VSAPI-NOTES.md` (add `## LibGUI` section), `CLAUDE.md` (lessons rule +
  `.wiki` ripgrep guidance), `.gitignore` (ignore `.wiki/`, `reference/vslibgui/`).
- **Spike-only — throwaway branch, NOT merged unless the decision is "go":** `src/Mod/Mod.csproj`
  (`Gui.dll` reference), `src/Mod/modinfo.json` (dependency on modid `gui`), a throwaway `GuiBase`
  subclass rebuilding the lectern read-view.
- **No `src/Core/` change** (respects the Core-must-not-reference-VSAPI invariant). **No dependency
  added to the shipped mod** by this change — the `Gui.dll` reference lives only on the gated spike
  branch. No network/persistence/lang change.
- **Read-only during authoring:** `src/Mod/GuiDialogScribeLectern.cs`, `ScribeRowElement.cs`,
  `RowTextLayout.cs`, `ScribeRowTextInput.cs`, `ScribeBlockRowCell.cs`, `ScribeInspectOverlay.cs`,
  `ScribeClientConfig.cs`, `ROADMAP.md` — the current state the guide maps from.
