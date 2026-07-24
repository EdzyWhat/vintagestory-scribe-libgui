## Context

`ScribeClientConfig` (`src/Mod/ScribeClientConfig.cs`) already centralizes every GUI
layout/spacing/sizing constant specifically so the dialog can be re-tuned by editing its
on-disk JSON (`ScribeModSystem.ClientConfigFileName`) and relaunching — this was done
ahead of time for exactly this kind of tuning work. `GuiDialogScribeLectern` loads its own
`ScribeClientConfig` instance fresh via `capi.LoadModConfig<ScribeClientConfig>(...)` each
time the dialog is constructed (`EnterMode`/constructor, not a shared singleton) — there is
no single long-lived config object; whichever `GuiDialogScribeLectern` instance is
currently open owns the copy actually driving what's on screen.

Currently diagnosing a layout question (e.g. "why does only the last composed row in
editor view render its checkbox/drag-handle chrome") requires: edit the JSON file by hand →
fully quit and relaunch the client (lang/assets/mod config load once at boot, confirmed
in `VSAPI-NOTES.md`) → reopen the lectern → observe. This is the loop this change replaces
for iterative/exploratory tuning specifically — it does not replace the JSON file as the
permanent, checked-in-adjacent tuning mechanism.

Two previously-researched, previously-parked tools cover this (see `ROADMAP.md`'s parked
section, both researched 2026-07-19, **re-verified by decompiling the actual installed game
mods** — `vsimgui_1.2.7.zip`/`configlib_1.12.0.zip` under the local VS Mods folder — rather
than trusting secondhand summaries, since an initial research pass surfaced inconsistent
API claims; every method signature and manifest key cited below was confirmed against the
real DLLs via `ilspycmd`):
- **VSImGui** (mod id `vsimgui`, installed version 1.2.7): a real Dear ImGui overlay,
  draws its own overlay window (a dedicated `VSImGuiDialog`, not a preview of the actual
  `GuiComposer` dialog). **Not usable via NuGet**: the published `VSImGui`/
  `VSImGui_DebugTools` NuGet packages are stuck at v0.0.6, target net7.0, and are
  meaningfully older/different from the actual installed game-mod DLLs (`VSImGui.dll`,
  `VSImGui_DebugTools.dll`, plus bundled `ImGui.NET.dll`/`ImGuiController_OpenTK.dll` at
  version 1.2.7) — referencing the NuGet package would build against a different, stale API
  than what's actually running in-game. Referenced instead via a plain `<Reference>`
  `HintPath` against the installed mod's extracted DLLs, the same mechanism already used
  for `VintagestoryAPI.dll`/`VintagestoryLib.dll` in `Mod.csproj`.
- **ConfigLib** (mod id `configlib`, installed version 1.12.0): a generic in-game
  settings-panel mod. **No NuGet package exists** (the top "configlib" NuGet search hit is
  an unrelated package from a different author) — likewise referenced via `HintPath`
  against the installed `configlib.dll`. Hard-depends on `vsimgui` itself (confirmed via
  its own `modinfo.json`: `"dependencies": { "vsimgui": "1.2.0" }`), so Scribe's optional
  dependency on ConfigLib transitively requires VSImGui to be installed too, though Scribe
  itself only references ConfigLib's assembly.

## Goals / Non-Goals

**Goals:**
- Let a developer drag a slider and see the lectern dialog's layout react within the same
  frame or two, for any `ScribeClientConfig` field relevant to the current row-list/culling
  investigation.
- Add ConfigLib as the permanent, planned in-game settings surface for the same config
  fields, matching `ROADMAP.md`'s already-decided direction.
- Guarantee neither addition affects a Release build's behavior, players without the
  optional mods installed, or CI (which only builds/tests `Core`).

**Non-Goals:**
- Not building a custom in-house live-reload mechanism — both tools already exist and are
  purpose-built for this.
- Not exposing every possible layout constant through ConfigLib on day one — start with the
  fields relevant to the current investigation (row-list/culling/width), extend later.
- Not resolving the missing-chrome bug itself in this change — this change only builds the
  diagnostic capability; the bug investigation happens afterward, using it.

## Decisions

**1. VSImGui is wrapped in `#if DEBUG`, not a runtime `IsModEnabled` check.**
Unlike ConfigLib (a real, permanent, player-facing optional feature), VSImGui is
exclusively a developer diagnostic tool and must never be reachable in a shipped build,
even if a player happened to have the ImGui mod installed. A compile-time `#if DEBUG`
around both the reference and the window-registration call guarantees this categorically,
rather than relying on a runtime flag that could be misconfigured or accidentally left on.
*Alternative rejected:* a runtime `IsModEnabled`-style gate (matching ConfigLib's pattern)
— rejected because it's the wrong guarantee for a tool that should not exist in Release at
all, not just be inactive by default.

**2. VSImGui and ConfigLib are both referenced via a plain `<Reference>` `HintPath`
against the locally-installed game mod's DLLs, not a `PackageReference` — and VSImGui's
`ItemGroup` is conditioned on `Configuration == 'Debug'`.**
Neither mod has a usable NuGet package (see Context) — both are referenced the same way
`Mod.csproj` already references `VintagestoryAPI.dll`: a `HintPath` pointing at the
installed mod's extracted DLL location, `Private=false` (don't copy the DLL into our own
output; the game's mod loader provides it). This guarantees the build compiles against the
exact DLL versions actually present in the game install this project targets, not a
possibly-mismatched published package. VSImGui's `<Reference>` items carry
`Condition="'$(Configuration)' == 'Debug'"` so a Release build has zero VSImGui presence —
no assembly copied, nothing implied for players. ConfigLib's `<Reference>` is
unconditional (it's the permanent integration) but the DLL itself has `Private=false`, so
it's never copied into Scribe's own build output either way — exactly mirroring how the
game DLLs themselves are referenced without being redistributed. *Why this matters:*
`add-lectern-block`'s own guardrail is "no new mod dependencies" for players; a Release
build must be indistinguishable, dependency-wise, from before this change, and building
against the real installed DLLs (rather than a stale NuGet snapshot) avoids a silent
API-mismatch risk on top of that.
*Mechanical note:* the installed mods live inside `.zip` files under the VS Mods folder,
not as loose DLLs on disk — task 1.1 covers extracting the specific DLLs needed
(`VSImGui.dll`, `VSImGui_DebugTools.dll`, `ImGui.NET.dll`) into a stable local path the
`HintPath` can target (e.g. a `lib/` folder outside version control, documented in a
README so another machine can reproduce it), since referencing a path inside a `.zip`
isn't possible directly.

**3. Each open `GuiDialogScribeLectern` instance registers/unregisters its own ImGui debug
window rather than one global window bound to a static config.**
Since `ScribeClientConfig` is loaded fresh per dialog instance (not a shared singleton —
see Context), a single persistent ImGui window bound to "the" config would either go stale
across dialog close/reopen or require introducing a shared config instance solely to serve
the debug tool, which would be a real (if small) architecture change to accommodate a
diagnostic feature. Instead: the debug window is created when a `GuiDialogScribeLectern`
opens (bound to that instance's own `clientConfig` and a recompose callback) and disposed
when it closes. *Alternative rejected:* promoting `ScribeClientConfig` to a
`ScribeModSystem`-owned singleton passed into every dialog — rejected as unnecessary
architectural change motivated only by the debug tool; `ScribeClientConfig`'s per-dialog
load-on-open already matches how a player would expect a relaunch-scoped setting to behave,
and this change shouldn't disturb that for production behavior.

**4. Each slider is registered once (on dialog open) via
`VSImGui.Debug.DebugWidgets.FloatSlider(domain, category, label, min, max, getter, setter)`
— there is no manual per-frame draw call or event subscription needed for this.**
Verified via decompile: `ImGuiModSystem` itself already subscribes a `DrawDebugWindow`
handler to its own `Draw` event at startup, which calls `DebugWindowsManager.Draw()` every
frame — so any `DebugWidgets.*` entry registered by any mod is automatically drawn, grouped
into tabs by `domain`/`category`, with zero additional wiring. The overlay's visibility is
also already player-toggleable via VSImGui's own built-in hotkey (`imguitoggle`) — nothing
needed on Scribe's side to show/hide it. The `getter`/`setter` lambdas passed to
`FloatSlider` read/write `clientConfig` directly and the `setter` also triggers the same
recompose path `OnTextSizeSliderChanged`/`RequestRecompose` already use — reusing an
existing, already-proven pattern in this file rather than inventing a second one. Call
`DebugWidgets.Remove(domain, id)` (using the `int` id `FloatSlider` returns) when the
dialog closes, per Decision 3.
*Correction from this change's original draft:* earlier design language described this as
needing "a forced recompose each frame" and a `PackageReference`-bound `DrawCallbackDelegate`
subscription — neither is accurate; `DebugWidgets` already handles per-frame redraw
internally, and the getter/setter pair is the entire integration surface needed.

**5. Persist to disk only on an explicit action, not on every slider-drag tick.**
`DebugWidgets.FloatSlider`'s `setter` fires on every intermediate drag value, same as any
ImGui slider. Writing to `scribe-client-config.json` (via `StoreModConfig`) on every one of
those would be needless I/O and risks corrupting a concurrent hand-edit of the file.
Register a `DebugWidgets.Button(domain, category, "Save", ...)` alongside the sliders that
calls `StoreModConfig` explicitly — the sliders themselves only ever write to the in-memory
`clientConfig` instance.

**6. ConfigLib integration uses the no-code `configlib-patches.json` manifest's `"file"`
key path (not `RegisterCustomConfig`/reflection-based registration).**
Verified via decompile (`ConfigLibModSystem.LoadConfig`): a manifest with a top-level
`"file"` key is parsed by constructing `ConfigLib.Config` directly against that file path
(relative to the game's `ModConfig` folder) — this is a distinct, simpler code path from
the asset-patching (`"patches"`-keyed) use case the public wiki documents most prominently,
and reads/writes the *existing* `scribe-client-config.json` file directly — zero new
serialization code, same file the mod already loads via `LoadModConfig`, no
dual-source-of-truth risk. Settings are declared via the manifest's `"settings"` array,
each entry requiring `"code"` (matched to `ScribeClientConfig`'s field name) and `"type"`
(`"integer"`/`"float"`/`"number"`/`"boolean"`/`"string"`/`"other"`/`"color"`), plus
`"default"`/`"comment"`/optional `"range"` — confirmed against
`Config.SettingsAndFormattingFromJsonArray`/`ParseSettingBlock`'s actual parsing logic, not
the wiki's object-keyed example (which is a different, valid schema variant for the same
array key, used for the patches use case instead). The reflection-based
`RegisterCustomConfig`/`RegisterCustomManagedConfig` alternative was considered and
rejected for this change: it needs `ConfigLibModSystem` API calls at mod-start time and
would require exposing `ScribeClientConfig`'s instance to it, reintroducing the same "no
shared instance exists today" issue as Decision 3, for no benefit the manifest path
doesn't already provide.

**7. ConfigLib's exposed field set starts scoped to layout/spacing fields relevant to the
current investigation, not the entire class.**
`TextSizeScale` is excluded (it already has its own in-GUI slider and is meant to change
during normal play, not via a settings panel) — everything else in `ScribeClientConfig` is
a candidate. Start with `VisibleListHeight`, `RowSpacing`, `TopContentGap`,
`ReadListWidth`, `EditorListWidth`, `RowDividerThickness`, `RowDividerBrightness`; add the
rest opportunistically as later tuning needs them, per the proposal's stated non-goal.

## Risks / Trade-offs

- **[Risk] A `Condition`-gated `<Reference>` `ItemGroup` is easy to get subtly wrong (e.g.
  a typo'd `Configuration` property name silently including the reference in Release
  anyway)** → Mitigation: task list includes an explicit verification step — build in
  Release configuration and confirm the VSImGui assembly is absent from
  `bin/Release/net10.0/`.
- **[Risk] Referencing DLLs extracted from an installed mod's `.zip` (not a versioned
  package) makes the build depend on local machine state that isn't captured by source
  control** → Mitigation: document the exact extraction step and target path in a README/
  task so it's reproducible on another machine; this trade-off was accepted explicitly
  (see Decision 2) because no legitimate package exists for either dependency at the
  versions actually installed.
- **[Risk] `#if DEBUG` call sites can bit-rot if VSImGui's own API changes between mod
  versions, silently breaking only in Debug builds** → Mitigation: accepted — this is a
  developer-only tool with no player-facing consequence if it breaks; low severity, not
  worth extra process overhead to guard against.
- **[Risk] Exposing config fields through ConfigLib in-game and the JSON file by hand
  simultaneously could confuse a future contributor about which is authoritative** →
  Mitigation: they are the same file; ConfigLib is documented (in this design and a code
  comment) as just another editor of the existing file, not a second store.

## Open Questions

- None currently — the initial draft of this design contained inaccurate assumptions about
  both dependencies' distribution (NuGet packages) and API surface (VSImGui's event/draw
  model, ConfigLib's manifest schema), traced to secondhand research that partially
  conflicted with the tools' own current source. All decisions above were re-verified by
  decompiling the actual installed game-mod DLLs (`vsimgui_1.2.7.zip`/
  `configlib_1.12.0.zip`) before being finalized here — no remaining unknowns block
  implementation.
