# Design — LibGUI decoupling feasibility

Evidence appendix for the decision matrix in [`proposal.md`](./proposal.md). Grounded in the Scribe
source tree, the native-GUI ancestor (`../vintagestory-scribe`), the archived LibGUI-adoption specs,
and decompiled `VintagestoryLib.dll`/`VintagestoryAPI.dll`. Where a claim is inferred rather than
proven, it says so.

---

## 1. The coupling surface — what actually depends on LibGUI

`src/` splits cleanly into a GUI-agnostic core and a LibGUI-bound presentation layer.

**GUI-agnostic (reusable by ANY GUI, ~unchanged):**
- **`src/Core/` — 37 files, zero VS-API and zero `Gui` references.** Documents, tasks, pins,
  codecs, stores, policies, and the cuneiform *geometry* (`src/Core/Cuneiform/*`, only `using`
  is `System.Text.Json`). This is the enforced project invariant (CLAUDE.md).
- **Network layer** — the ~16 `Scribe*Message.cs` packets + `ScribeModSystem.Network.cs`
  (`GetChannel`/`SendPacket`/`BroadcastPacket`). Edits are server-authoritative.
- **Host/store abstractions** — `IScribeDocumentHost`, `IScribeDocumentItem`, `NotebookHost`,
  `TabletHost`, `ScribePinStore`, `ItemSlotScribeDocument`.
- **Block/item classes** — `BlockEntityScribeLectern/Chalkboard/Scriptorium`,
  `ItemScribeNotebook/Tablet`, etc. (no `using Gui`) — **but not yet Gui-clean** (severability audit,
  2026-08-20). They hold Gui-*deriving* types as **fields and return types**, which `GetTypes()`
  force-resolves (not just the concrete `new`):
  - `ScribeModSystem.cs:84` — `private HudScribePins? pinHud;` (a `GuiBase` subclass) **on the one
    required ModSystem** — this field alone throws at discovery when `gui` is absent.
  - `BlockEntityScribeWritingStation.cs:181` — `private ScribeDialogBase? dialog;` field, plus the
    abstract `CreateDialog(...) : ScribeDialogBase` return type (`:74`).
  - `ItemScribeNotebook.cs:109/112`, `ItemScribeTablet.cs:395/415` — `OpenScribeDialog`/`Open…Dialog`
    return `ScribeDialogBase`.
  - `ScribeModSystem.Handbook.cs:92/98` — `Action<ScribeDialogBase>` param + `.OfType<ScribeDialogBase>()`.
  - Method-body-only (JIT-time, survivable if guarded, but should relocate): `ScribeModSystem.Timer.cs:86`,
    `Network.cs:484/500` — `capi.Gui.OpenedGuis.OfType<GuiDialog…>()`.

  So the enabling refactor is **not** merely "move the concrete `new` behind a factory": it must
  introduce **Gui-free abstractions** (an `IScribeDialog` interface, an `IScribePinHud`) and retype
  those ~5 fields/returns/params so the core assembly names no Gui-deriving type. The abstract seam
  already exists (`BlockEntityScribeWritingStation.cs:74`) but currently returns a Gui type — retyping
  it is the crux. **Good news:** `ScribeBackdropSpec` is Gui-free (a `record` over `AssetLocation` +
  `Vector4?`, `ScribeBackdrop.cs:18`), so backdrop *specs* stay core-side; only their rendering is Gui.

**LibGUI-bound (~50 files with `using Gui`, all in the dialog/HUD/widget layer):**
- Dialog hierarchy: `ScribeDialogBase.cs` + its 10 partials, and the concrete dialogs
  (`GuiDialogScribeLecternLibGui`, `…Chalkboard`, `…Scriptorium`, `…Tablet`, `…Notebook`,
  `GuiDialogClockmakerNotebook`), `ScribeSettingsDialog`, `ScribeGearTuningDialog`,
  `ScribeDialogBody`. **Structural coupling:** `ScribeDialogBase : GuiDialogBlockEntityBase`
  (`ScribeDialogBase.cs:28`) — the base *class* is LibGUI, so this is a rewrite, not a port.
- HUD: `HudScribePins.cs` (`: GuiBase`).
- Custom widgets/effects: `ScribeMultilineField`, `ScribeNumericField`,
  `ScribeCuneiformField/TitleField`, `ScribeRowWidgets`, `ScribeAnimatedList`,
  `ScribeRowSizeAnimation`, `ScribeAddKindPicker`, `ScribeEditorContent`/`ReadContent`/
  `PinnedContent`/`SettingsContent`, `ScribeTheme`, `ScribeStamp`, `ScribeBackdrop*`,
  `ScribeGlobalTint`, `ScribeGearEffect`, `ScribeFieldOnlyTraversalPolicy`, `SilentSoundPlayer`,
  `ScribeDocumentSlot`, `CuneiformText`, `CuneiformGlow`, `ScribeGlyphFallback`.
- Client glue in `ScribeModSystem.*` that instantiates the HUD / opens dialogs
  (`ScribeModSystem.cs:297` unconditionally does `new HudScribePins(...)`).

---

## 2. Capability portability — feature by feature

Classified against vanilla `GuiDialog`/`GuiComposer`/`GuiElement` + Cairo. **PORTABLE** = a direct
vanilla equivalent exists; **HARD** = achievable via a custom `GuiElement` + Cairo (real effort);
**LIBGUI-ONLY** = relies on Skia/declarative/animation infra with no native equivalent → cut.

| Capability | LibGUI usage (evidence) | Native equivalent | Class | Effort | Cut-safe? |
|---|---|---|---|---|---|
| Dialog scaffolding + declarative tree/lifecycle | `GuiDialogBlockEntityBase`/`GuiBase`, `Build()`→widget tree, `ForceRebuild` | `GuiDialog` + imperative `GuiComposer` recompose | HARD | Large | No — rewrite |
| Flex layout (Column/Row/Expanded/Padding/SizedBox/Align) | pervasive (`ScribeDialogBase.cs:13,18`) | absolute `ElementBounds.Fixed` math (no flexbox) | HARD | Large | No |
| Theming (ThemeData, 17-role ColorScheme, cascading `Theme.Of`) | `ScribeTheme.cs` (Light/Chalkboard/4 clay palettes) | `CairoFont` + hand-passed RGBA; no cascade | HARD | Medium | Degrade (fewer palettes) |
| TTF text + embedded custom fonts | `Text`/`TextStyle`/`FontRegistry`/`SkiaAssetLoader` | `GuiElementDynamicText` + `CairoFont` (loads TTFs) | PORTABLE | Medium | No |
| Per-glyph arrow fallback | `ScribeGlyphFallback.cs` (probes `SKTypeface`) | Cairo/Fontconfig fallback is automatic | PORTABLE | Small | **Yes — delete** |
| Scrolling (ListView/SingleChildScrollView/Scrollbar) | 7× each; caret-follow walks `RenderViewport` | `GuiElementScrollbar` + `BeginClip/EndClip` | PORTABLE* | Medium | No |
| Single-line / numeric input | `TextField`/`NumericField` | `GuiElementTextInput`/`GuiElementNumberInput` | PORTABLE | Small | No |
| **Multi-line editable field** | `ScribeMultilineField.cs` (~620-line custom `RenderBox`) | custom `GuiElement` — ancestor's `ScribeRowTextInput` is a proven native impl | HARD | Large | No |
| Focus model (FocusNode/traversal/blur-commit) | `ScribeFieldOnlyTraversalPolicy.cs` | weaker native focus; manual snapshot/restore | HARD | Medium | No |
| Checkbox/Dropdown/Button/GestureDetector/MouseRegion | `ScribeDialogBase.cs:11` | `GuiElementSwitch`/`DropDown`/`TextButton` + `OnMouseDown` | PORTABLE | Medium | No |
| Floating drop-up add-menu (Overlay/LayerLink) | `ScribeAddKindPicker.cs` | no portal layer → plain `GuiElementDropDown` | HARD/degrade | Medium | Degrade |
| Tooltips | `Tooltip`; `ScribeGlobalTint.ShadedTooltip` | `GuiElementHoverText`/`AddHoverText` | PORTABLE | Small | Partial (lose shading) |
| 3D item icons / slots | `ItemStackDisplay`, `FlatItemSlot` | `RenderItemstackToGui` / slot-grid elements | PORTABLE | Medium | No |
| UI click sound + mute | `ISoundPlayer`/`SilentSoundPlayer.cs` | `capi.Gui.PlaySound`; mute = skip call | PORTABLE | Small | **Yes** |
| Pixel-art backdrop | `ScribePixelArtBackdrop.cs` (`SKSamplingOptions.Nearest`) | Cairo `NEAREST` pattern filter, or flat image | HARD | Medium | Degrade/cut |
| Gear cast-shadow/emissive | `ScribeGearEffect.cs` (`SKColorFilter.SrcIn`+blur) | no Cairo color-matrix; manual mask | HARD | Medium | **Yes** |
| Illumination tint / global dim | `ScribeGlobalTint.cs` (`SaveLayer`+color-matrix) | Cairo has no color-matrix; multiply overlay is inferior | HARD | Med-Large | **Yes** |
| SharedPaint reset | `ScribeBackdropPaintReset.cs` | LibGUI-only workaround | n/a | — | **Yes — delete** |
| Cuneiform display | `CuneiformText.cs` (`SKPath` polygon fill) | geometry in Core; fill → Cairo `MoveTo/LineTo/Fill` | HARD | Medium | Partial |
| Cuneiform glow | `CuneiformGlow.cs` (`SKMaskFilter` blur) | Cairo blur is manual/expensive | HARD | Medium | **Yes** |
| **Animation framework** (AnimationController/Curves/Ticker + `ScribeAnimationRegistry`) | `ScribeRowSizeAnimation.cs`, `ScribeAnimatedList.cs`, HUD/stamp/gear | **no native tween system** — only per-frame manual interpolation | LIBGUI-ONLY | — | **Yes — all** |

\* Native scrolling ports, but see §4 fact #2: `BeginClip` does *not* clip a mixed static+interactive
row list — the ancestor solved this by baking each row onto its own Cairo surface and blitting it in
the interactive pass. That technique is the reusable answer.

### Animations — every one is cosmetic
Row collapse-on-delete, row slide-in-on-add, animated-list diffing, cuneiform stroke reveal,
copy-"COPIED" stamp flourish (explicitly documented non-load-bearing, `ScribeStamp.cs:21-24`), gear
oscillation, HUD sunk-row opacity fade, drop-up grow-in, tooltip fade. **None gates a data
operation** — all edits are server-authoritative and complete before any animation plays. Caret
blink is the only "animation" with usability value, and native `GuiElementTextInput` blinks its own
caret for free. **The largest LibGUI-only surface (the whole animation subsystem) drops wholesale.**

---

## 3. Architecture options — the decompilation evidence

### Why single-DLL in-place dual support is impossible (Option A, ruled out)
- Mod discovery calls `Assembly.GetTypes()` (`VintagestoryLib.dll`, `ModContainer` →
  `InstantiateModSystems` → `GetModSystems`). `GetTypes()` force-resolves **every** type's base
  class and interfaces.
- Scribe's types derive from LibGUI bases (`ScribeDialogBase : GuiDialogBlockEntityBase`,
  `HudScribePins : GuiBase`). With `Gui.dll` absent, resolution throws
  `ReflectionTypeLoadException`; `GetModSystems` **catches it and returns empty** — so
  `ScribeModSystem` is never discovered. The whole mod silently vanishes, not just its GUI.
- This is *type-enumeration time*, before any method runs, so `IsModEnabled("gui")` guards at
  call-sites cannot prevent it. Also independently fatal: `ScribeModSystem.cs:297` unconditionally
  runs `new HudScribePins(...)` at client start, JIT-loading `GuiBase`.
- **Consequence:** the LibGUI-deriving types must live in an assembly that can be absent (Option C)
  or present-but-ignored (Option B).

### Option B — split assembly within one mod (viable)
- VS allows multiple DLLs in one mod folder; all are `LoadFrom`'d (`ModContainer.LoadAssembly`,
  `VintagestoryLib.dll`). **Hard constraint: at most one DLL may host a `ModSystem` or a
  `ModInfoAttribute`** — two trips a "multiple ModSystems" throw; zero trips "no ModSystem".
- Design: `Scribe.dll` (Core + network + blocks/items + native GUI; hosts the sole ModSystem;
  **zero `Gui` reference** so its `GetTypes()` always succeeds) + `ScribeLibGui.dll` (all
  Gui-deriving dialogs/widgets/HUD; **no ModSystem, no ModInfoAttribute**). On a `gui`-absent
  machine, `GetModSystems(ScribeLibGui.dll)` throws internally, is caught, and the DLL is ignored —
  loaded but inert. `Scribe.dll` reaches into it **reflectively** (factory interface defined in the
  core, implemented + registered by the add-on at runtime), gated by `IsModEnabled("gui")`.
- Constraint to respect: `Scribe.dll` types may not derive from, implement, or hold **fields** of
  Gui-deriving types (`GetTypes()` resolves base/interface/field types — but not method-signature
  return/param types). Hence the reflective factory rather than a compile-time reference.

### Option C — separate companion mod (viable, cleanest — recommended)
- Two mods: `scribe` (native GUI, its own ModSystem, no `gui` dep) + `scribelibgui` (own
  `modinfo.json` declaring `gui` **and** `scribe` as hard deps, carries the LibGUI dialog factory).
  Affected Linux users install only `scribe`. Sidesteps the one-ModSystem-per-DLL rule entirely and
  mirrors the ConfigLib decoupling philosophy already in the project.

### Soft-dep mechanics (applies to B and C)
- **VS has no `optional` dependency flag** — `ModDependency` (decompiled `VintagestoryAPI.dll`) has
  only `ModID` + `Version`, and `CheckAndSortDependencies` drops any mod with a missing/disabled
  dep. The only "optional" is **omit `gui` from `modinfo.json`** and detect at runtime with
  `capi.ModLoader.IsModEnabled("gui")` — already used in the codebase
  (`ScribeAmbientLightSampler.cs:236`), and the documented ConfigLib pattern (`Mod.csproj`).

### Server mod enforcement — the constraint that decides B vs C

Servers dictate which mods a joining client must have, and the current design **locks
LibGUI-incapable clients out of every Scribe server**. Proven from `VintagestoryLib.dll`.

**How the server builds its required-mods list** (`Vintagestory.Server/ServerSystemHeartbeat.cs:103-108`):
```csharp
mods = (from mod in server.api.ModLoader.Mods
        where mod.Info.Side.IsUniversal() && mod.Info.RequiredOnClient
        select new ModPacket { id = mod.Info.ModID, version = mod.Info.Version }).ToArray();
```
A mod is force-required on clients **iff `Side == Universal` AND `RequiredOnClient == true`**.

**How the joining client is checked** (`Vintagestory.Client.NoObf/SystemModHandler.cs:60-88`):
```csharp
list4 = client mods where Side == Universal && !mod.Error.HasValue      // present AND error-free
list5 = (server mods where RequiredOnClient).Except(list4)              // matched by id@NetworkVersion
if (list5.Count > 0) { game.disconnectReason = Lang.Get("joinerror-modsmissing", …); return; }
```
Matching key is **`modid@NetworkVersion`** (`NetworkVersion` defaults to `Version`). A client
satisfies a requirement only if it has that mod **and the mod loaded without error**.

**Why Linux clients are hard-blocked today (two independent failures):**
1. Scribe (`Universal`, `requiredOnClient: true`) hard-deps `gui`, so a server running Scribe must
   load `gui` server-side. `gui`'s own `modinfo.json` is `Side: Universal, requiredOnClient: true`
   (`requiredOnServer: false`) — so **the server advertises `gui` itself as client-required**, and a
   client that can't run `gui` is rejected on that line alone.
2. Even ignoring #1: a Linux client that *has* the Scribe folder but whose `gui` fails gets
   `mod.Error == ModError.Dependency` on Scribe (`Vintagestory.Common/ModLoader.cs:401-416`), so
   Scribe is excluded from `list4`; the server's required `scribe` lands in `list5` → **disconnect**.

**The forced shape.** The linchpin is not "Scribe shouldn't depend on `gui`" — it's that **the server
must never load `gui` at all** (any Universal mod pulling `gui` onto the server re-triggers failure
#1). Therefore:
- The **server-enforced mod (`scribe`) must be `Universal`, `requiredOnClient/Server: true`, and
  depend on vanilla only (no `gui`)** — and, to be error-free in `list4`, it must **carry the native
  GUI**. Server logic + native GUI ship together in this one mod.
- **LibGUI must be a `Side: Client` add-on** (`scribelibgui`, deps `gui` + `scribe`). Client-only
  mods are never in the server's enforcement list, never loaded server-side, and are opted into
  per-client. A LibGUI user and a Linux user join the *same* server — both satisfy `scribe@version`;
  the LibGUI user additionally runs the client-only visual layer.

**Side-gating evidence (2026-08-20).** `Vintagestory.Common/ModContainer.cs:614`:
`if (!base.Info.Side.Is(side)) { Status = ModStatus.Unused; return; }` — a mod whose declared side
doesn't include the running app side is marked **Unused** and its ModSystems are never instantiated.
So a `Side: Client` companion is inert on a dedicated server, which is why its `gui` dependency can't
be dragged onto the server. **Still to confirm at runtime (tasks.md §1):** that an `Unused` mod's
*dependencies* are excluded from `CheckAndSortDependencies` / the server's enforcement set — the
`Side.Is` gate proves the ModSystems don't run, but the join test closes the dependency-set nuance.

**Effect on the options:** this eliminates any design where the enforced mod touches `gui`.
- **Option C** is the natural fit: LibGUI as `Side: Client` physically never reaches the server, so
  it is definitionally excluded from enforcement and a server cannot be misconfigured into forcing
  `gui` on clients.
- **Option B** still *works* (the one Universal mod carries no `gui` dep; the inert `ScribeLibGui.dll`
  is silently dropped) — but that satellite DLL is now also shipped to and loaded-then-dropped
  **server-side**, relying on the silent-drop behavior in one more place and travelling through the
  enforced artifact.
- **Option A** stays ruled out. **Option D** is trivially server-safe but drops LibGUI for everyone —
  C delivers D's server safety *plus* opt-in polish.

---

## 4. The native-GUI ancestor as reference (`../vintagestory-scribe`, v0.1.0)

**What it proves is buildable natively (all on vanilla `GuiComposer` + custom Cairo elements):**
- Lectern with lock-free Read view + lock-gated Editor view (server-authoritative single-editor).
- Task checklist + free-text notes, custom checkbox glyph, **edit-in-place multiline** with a single
  floating input (`ScribeRowTextInput.cs`, a `GuiElementTextArea` subclass) — Enter=commit/advance,
  Shift+Tab=retreat, Shift+Enter=newline, Mac caret idioms.
- Clipped scrollable row list, scroll-follows-caret, custom mouse-wheel step.
- Custom Cairo row element (`ScribeRowElement.cs`) baking checkbox + wrapped text + ruling onto its
  own `ImageSurface`, blitted in the interactive pass so `BeginClip` clips it.
- Pin toggle, delete affordance (icon-only stub), drag-handle (icon-only), text-size slider.

**Reusable elements for the port:** `ScribeRowElement`, `ScribeRowTextInput`,
`ScribeRowListScrollbar`, `ScribeBlockRowCell` (icon buttons), `RowTextLayout` (absolute-bounds
metric struct). These are the proven native answers to the hardest primitives.

**Feature delta the ancestor is missing (rebuild scope for parity — Core is shared, so this is
GUI + Mod-layer):** Notebook + Clockmaker's Notebook + Timer/alarm, Clay/Wax Tablets (dry/re-wet/fire
lifecycle), Scriptorium (transcribe/import/export), Chalkboard, pinned-task HUD, tracker + link
tasks, "Add to Scribe" Handbook patch, completion policies + undo, guestbook, notebook history,
cuneiform script, themes/settings, temporal-storm HUD effect. The ancestor is the lectern-only v1
slice; today's product is ~12 releases ahead. **Reviving it as the base (Option E) means re-porting
essentially the whole product — not recommended.**

## 5. Why LibGUI was adopted — the gaps a native port re-encounters

From the archived `explore-libgui-adoption` / `adopt-libgui-foundation` specs and
`VSAPI-NOTES.md`. Native `GuiComposer` fought (each has a *proven workaround already in the
ancestor's source*): 1) no flex layout (all absolute bounds); 2) `BeginClip` doesn't clip a
mixed static+interactive list; 3) cull-don't-clip can't contain a row taller than the viewport;
4) `SetValue` before `Compose()` corrupts wrap/height math; 5) recompose destroys focus/caret;
6) mid-dispatch recompose reentrancy crashes (→ `pendingRecomposeAction` defer); 7) overlapping-element
focus theft; 8) native text input single-line unless subclassed, multi-line traps; 9) caret nav is
Windows-keyed (Mac idioms dead by default).

**The pivotal finding:** LibGUI removes 1–3 and 5–7, but **does not remove #8** — its `TextField`
is single-line and `RenderTextField` is `internal`/non-subclassable, so the multi-line editable row
had to be rebuilt on LibGUI anyway. And LibGUI *added* the very risks now biting us: a hard
dependency, the Linux crash motivating this study, global Harmony patching of vanilla dialogs, and
1.22.x version-pinning. Net: the native path's costs are real but largely pre-solved in the ancestor;
LibGUI's marquee benefit (the editable field) did not actually materialize.

### The crash root cause (from [ripls56/vslibgui#2](https://github.com/ripls56/vslibgui/issues/2), read 2026-08-20)
Not a GPU/render bug — a **native-library ABI collision** on the client:
- LibGUI **3.1.0 bundles `libHarfBuzzSharp` 8.3.1 but not `libSkiaSharp`**; Vintage Story ships Skia
  but *not* HarfBuzz. On rolling-release Linux (Arch, CachyOS…) the system's newer `libharfbuzz` and
  the bundled 8.3.1 get "mixed together at runtime" — a suspected `free(): invalid pointer` (the
  reporter's guess; no confirmed stack trace yet).
- It fires at **font setup when the client joins a world — before any GUI window opens**
  ("sometimes even before loading into a world"). So it is purely *client-side*; a headless server
  never runs client font shaping. This reinforces the multiplayer thesis: `gui` belongs client-only.
- Because the offending native lib ships **inside the `gui` mod**, a client that simply **doesn't
  install `gui` never loads it → the crash is fully avoided.** Decoupling directly resolves it.
- **Status: open/unresolved upstream**, a regression in 3.1.0 (pre-3.1.0 worked), no maintainer fix,
  no linked PR. The suggested (untried) mitigation is loading the bundled lib with
  `RTLD_DEEPBIND | RTLD_LOCAL`. Because it's an unfixed ABI-collision class that could shift across
  distro updates, it strengthens the case for at least evaluating **Option D** (drop LibGUI) as the
  path that removes the risk surface entirely, rather than betting on an upstream fix.

## 6. Level of effort (relative — not a commitment)

Dominant, irreducible cost of every crash-safe option is **rebuilding the dialog layer on
`GuiComposer`**. Rough t-shirt sizing:

| Work item | Size | Notes |
|---|---|---|
| Dialog-factory seam refactor + Gui-free abstractions (block entities + items + ModSystem) | **S–M** | Enabling step for B/C/D; low-risk but more than "move the `new`": introduce `IScribeDialog`/`IScribePinHud` and retype ~5 Gui-typed fields/returns/params (§1 severability audit) so the core assembly names no Gui-deriving type |
| Native dialog framework + shared row/scroll/text primitives | **L** | But ancestor's Cairo elements are a proven starting point |
| Re-skin each surface natively (lectern, notebook, clockmaker, tablet, scriptorium, chalkboard, HUD, settings, gear-tuning) | **L** | ~10 dialogs; most logic is shared, the composer wiring is per-dialog |
| Cuneiform display in Cairo (geometry reused from Core) | **M** | Editable cuneiform field is the hard slice |
| Cut animations + Skia effects | **XS** | Deletion, not rework |
| Packaging (split-assembly B, or companion mod C) + soft-dep detection | **S–M** | Reflective factory + `IsModEnabled` gate |

**Interpretation:** this is a *large but non-greenfield* effort — the functional half (Core,
network, blocks, hosts, stores) is already shared and untouched; the cost concentrates in the GUI
rebuild, materially de-risked by the ancestor's proven native elements. Option D (native-only, drop
LibGUI) has the lowest *packaging* cost but abandons LibGUI polish for everyone; B/C cost slightly
more packaging work to keep both audiences.

## 7. Open questions for the maintainer (the actual decision)

1. **Keep LibGUI at all (B/C) or go native-only (D)?** Weigh LibGUI's polish (animations, Skia
   effects, flex layout) against its demonstrated fragility (this Linux crash, Apple-Silicon risk,
   version-pinning, vanilla-patching). If the crash class recurs, D removes the whole risk surface.
   Note the server constraint (§3, "Server mod enforcement") already fixes the base mod as
   `Universal` + gui-free with the native GUI aboard, and LibGUI as a `Side: Client` add-on — so the
   native GUI gets built either way; D just declines to ship the LibGUI add-on.
2. **If keeping both: split-assembly (B, one download) or companion mod (C)?** The server constraint
   tilts this toward **C** — a `Side: Client` LibGUI mod never reaches the server, so it can't be
   dragged into mod enforcement; B's Universal mod ships the inert `gui`-deriving DLL server-side too.
   Still deferrable — both need the same factory seam first.
3. **Feature bar for the native build** — ship a leaner "tasks-first" native Scribe (cut cuneiform,
   fancy backdrops) to reach affected users fast, then close the gap? Or hold for near-parity?

**Inferred, not yet proven (verify before/during implementation — see `tasks.md`):**
1. **Server does not load `gui` when only a `Side: Client` mod depends on it.** The whole
   multiplayer fix rests on this: if `gui` is `Side: Universal`+`requiredOnClient` (verified in its
   `modinfo.json`), then *any* path that loads it server-side re-adds it to the client-enforcement
   list and re-locks out LibGUI-incapable clients. Theory says a `Side: Client` dependent won't drag
   it onto the server, but this is exactly the class of assumption `VSAPI-NOTES.md` warns against —
   it needs a real join test (a `gui`-disabled client against a server running the native `scribe`),
   confirming the client joins *and* that the server never advertises `gui`.
2. The exact reflective-factory registration API for the client-side add-on to inject its dialog
   factory into the base mod.

**Confirmed by investigation (was inferred — severability audit, 2026-08-20):** relocating the GUI
files does **not** by itself sever the core assembly from `Gui` — core-side classes hold Gui-deriving
types as fields/returns (`ScribeModSystem.pinHud`, `BlockEntityScribeWritingStation.dialog`, the
`OpenScribeDialog`/`CreateDialog` return types, the `Action<ScribeDialogBase>` handbook hook; see §1
for the full list). Severing requires introducing `IScribeDialog`/`IScribePinHud` and retyping those
members. A final compile pass should still confirm no *residual* `Gui`-typed field remains once the
interfaces are in place.
