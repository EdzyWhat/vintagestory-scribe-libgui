## Why

LibGUI (mod id `gui`) is Scribe's first and only **hard** mod dependency (`src/Mod/modinfo.json:13`,
`"gui": "3.1.0"`). On some Linux machines (rolling-release distros — Arch, CachyOS) LibGUI **crashes
the client at font setup when joining a world, before any GUI opens** — a native-library ABI
collision: LibGUI 3.1.0 bundles its own `libHarfBuzzSharp` 8.3.1, which clashes with the system's
`libharfbuzz` (suspected `free(): invalid pointer`). It is a 3.1.0 regression, **open/unresolved
upstream** ([ripls56/vslibgui#2](https://github.com/ripls56/vslibgui/issues/2)). Because the
dependency is hard, those players **cannot run Scribe at all** — the game drops the mod before it
loads. Since the offending native lib ships *inside* the `gui` mod, a client that simply doesn't load
`gui` avoids the crash entirely. We want a path where affected players get a working Scribe, ideally
without abandoning the polished LibGUI experience for everyone else.

This change is an **assessment, not an implementation**. It exists to drive a decision: is a
no-LibGUI Scribe feasible, what does it cost, what features are lost, and can one codebase serve
both audiences or do we need a separate mod? The output is the decision matrix below plus the
evidence in [`design.md`](./design.md). No production code changes here; a follow-up change
implements whichever path is chosen.

> Note: an older native-GUI ancestor exists at `../vintagestory-scribe` (v0.1.0, lectern-only). It
> is the fork point where the LibGUI spike happened, so it is a genuine **reference implementation**
> of native-GUI Scribe — but it is ~12 releases and 5+ artifact interfaces behind. See design.md §4.

## What we found (the four load-bearing facts)

1. **The product is already GUI-agnostic below the dialog layer.** All of `src/Core/` (37 files,
   VS-API-free), every `Scribe*Message.cs` network packet, the host/store abstractions
   (`IScribeDocumentHost`, `IScribeDocumentItem`, `NotebookHost`, `TabletHost`, `ScribePinStore`),
   and the block/item classes are free of `using Gui`. Edits are server-authoritative
   (GUI → network → server mutates store → sync back). **~50 files carry the entire LibGUI
   coupling**, all in the dialog/HUD/widget layer. **Caveat (severability audit):** the block/item
   classes and the ModSystem are not yet fully Gui-clean — they hold Gui-*deriving* types as fields
   and return types (`ScribeModSystem.pinHud`, `BlockEntityScribeWritingStation.dialog`, the
   `CreateDialog`/`OpenScribeDialog` return types). Severing the core assembly from `Gui` means
   introducing Gui-free interfaces (`IScribeDialog`/`IScribePinHud`) and retyping those members, not
   just relocating files — see design.md §1 and the S–M sizing in §6.

2. **In-place dual support in ONE DLL is impossible — proven, not guessed.** VS discovers mods by
   calling `Assembly.GetTypes()` (`VintagestoryLib.dll`, `ModContainer.InstantiateModSystems`),
   which force-resolves every type's base class. Scribe's dialogs derive from LibGUI bases
   (`ScribeDialogBase : GuiDialogBlockEntityBase`, `HudScribePins : GuiBase`). If `Gui.dll` is
   absent, `GetTypes()` throws `ReflectionTypeLoadException`, VS **silently drops the whole mod —
   ModSystem included.** Guarding call-sites with `IsModEnabled("gui")` cannot help; the failure is
   at type-enumeration, before any method runs. The LibGUI-deriving types must physically live in an
   assembly that can be *absent-or-ignored*.

3. **Multiplayer forces the shape: the server-enforced mod must be gui-free.** Servers require a
   joining client to have every mod that is `Side: Universal` + `requiredOnClient` (matched by
   `modid@NetworkVersion`), and a client only satisfies it if that mod **loaded without error**
   (`ServerSystemHeartbeat.cs:103`, `SystemModHandler.cs:60`). Today this **locks Linux clients out
   of every Scribe server** two ways: (a) a server running Scribe must load `gui`, and `gui` is
   itself `Universal` + `requiredOnClient`, so the server advertises `gui` as client-required; (b) a
   client whose `gui` fails gets `ModError.Dependency` on Scribe, dropping it from the satisfied set →
   disconnect. The fix isn't just "Scribe shouldn't depend on `gui`" — **the server must never load
   `gui` at all.** So the enforced mod (`scribe`, Universal, `requiredOnClient/Server`) must depend on
   vanilla only and **carry the native GUI**, and LibGUI must be a `Side: Client` add-on that never
   reaches the server. See design.md §3, "Server mod enforcement." **Caveat — verify before relying
   on this:** that a `Side: Client` dependent never drags `gui` onto the server is inferred from the
   loader source, not yet observed. It needs an empirical join test (a `gui`-disabled/Linux client
   against a server running the native `scribe`) before we commit — this is the exact class of
   assumption `VSAPI-NOTES.md` warns about. Tracked in tasks.md §1.

4. **Every LibGUI-only visual is cosmetic and cut-safe.** The entire animation subsystem (row
   collapse/slide, list diffing, cuneiform reveal, HUD fade, gear spin, copy-stamp flourish) and the
   Skia-only effects (illumination tint, gear silhouette, pixel-art backdrop, cuneiform glow) gate
   **no** data operation — all edits complete server-side before any animation plays. A native build
   loses polish, not capability. The hardest interaction — a wrapping, growing, editable text row —
   was already solved **twice** (natively in the ancestor's `ScribeRowTextInput`, then again on
   LibGUI's public API because LibGUI's own `TextField` is single-line and non-subclassable).

## The decision matrix

Two orthogonal decisions. First — **architecture** (how non-LibGUI users get a working Scribe).
Multiplayer (fact #3) narrows this: the server-enforced mod must be `Universal` + gui-free and carry
the native GUI, and LibGUI must be a `Side: Client` add-on that never loads server-side.

| Option | Crash-safe for Linux? | Server-safe (Linux client can join a Scribe server)? | LibGUI users keep polish? | Code shape | Effort (beyond the native GUI itself) | Verdict |
|---|---|---|---|---|---|---|
| **A. In-place, single DLL** (`IsModEnabled` guards) | ❌ No — whole mod dropped when `Gui.dll` absent | ❌ No | — | one assembly | n/a | **Ruled out** (fact #2) |
| **B. Split assembly, one Universal mod** — gui-free `Scribe.dll` (native, sole ModSystem) + `ScribeLibGui.dll` (add-on, no ModSystem, reached reflectively) | ✅ Yes — core DLL is Gui-free, always discovered | ✅ Yes — enforced mod has no `gui` dep | ✅ Yes | 2 DLLs in one mod | Medium (S–M interface seam + reflective load + one-ModSystem-per-DLL discipline) | **Viable** — but ships the inert gui-deriving DLL server-side too |
| **C. Companion mod, LibGUI `Side: Client`** — `scribe` (Universal, gui-free, native) + `scribelibgui` (`Side: Client`, deps `gui`+`scribe`) | ✅ Yes — affected users just don't install the companion | ✅ Yes — LibGUI mod is client-only, never in server enforcement | ✅ Yes | 2 mods | Medium (S–M interface seam; cleaner packaging; mirrors ConfigLib pattern) | **Viable — cleanest, and the natural fit under fact #3** |
| **D. Native-only, drop LibGUI entirely** | ✅ Yes | ✅ Yes | ❌ No — everyone loses LibGUI polish | one assembly | Lowest packaging cost | Viable if we judge LibGUI not worth its fragility |
| **E. Revive the old native repo as the base** | ✅ Yes | ✅ Yes | ❌ No | one assembly | **Highest** — re-port ~12 releases / 5 interfaces | **Not recommended** |

Second — **which native features to cut** (applies to B/C/D/E equally; full table in design.md §2):

| Keep (ports to native VS GUI) | Cut or degrade in the native build |
|---|---|
| Task checklist, notes, pins, delete, reorder, tracker/link tasks, completion policies, guestbook, history, transcribe import/export, timer + alarm, settings, multi-line editing, scrolling, item icons, tooltips, TTF fonts | **Cut (cosmetic):** all animations, copy-stamp flourish, cuneiform glow, gear FX, illumination tint. **Degrade:** pixel-art backdrop → Cairo nearest-filter or flat image; segmented drop-up add-menu → plain dropdown; shaded tooltips → plain hover text. **Large-but-possible:** cuneiform *display* (geometry is portable — it's in Core; only the Skia fill moves to Cairo) and the editable cuneiform field. |

## Recommendation

- **Architecture: Option C (companion mod, LibGUI as `Side: Client`)**, with B as the fallback if we
  ever want a single downloadable. Both need the *same* enabling refactor, so the choice can be
  deferred until the native GUI exists. C is cleanest and is the natural fit under the multiplayer
  constraint: the base `scribe` mod stays `Universal` + gui-free with the native GUI aboard (so it's
  the mod servers enforce, satisfiable by any client), and the LibGUI layer is a client-only add-on
  that never loads server-side and so can't be dragged into server mod enforcement. It also obeys
  VS's "one ModSystem per mod" rule without discipline and mirrors the existing ConfigLib soft-dep
  philosophy. **Do not let the base mod depend on `gui`, and do not let a Scribe server load `gui`** —
  `gui` ships as `Side: Universal, requiredOnClient: true` (verified in its `modinfo.json`), so any
  server that loads it forces it on all clients and re-locks out LibGUI-incapable players.
- **Do NOT revive the old native repo (E).** Reuse its custom Cairo *elements* as reference, but
  build on today's shared Core/network/blocks — the ancestor is lectern-only and misses the
  notebook, tablets, scriptorium, chalkboard, HUD, timer, tracker/link tasks, and more.
- **The enabling refactor is worth doing regardless of the final decision (sized S–M, not S):** it's
  more than moving `new GuiDialogScribe…()` behind a factory — the severability audit found Gui-typed
  *fields/returns* on the ModSystem, the block-entity base, and the item classes, so the refactor must
  introduce Gui-free interfaces (`IScribeDialog`/`IScribePinHud`) and retype those members, then a
  compile pass confirms no `Gui`-typed field remains on the core side. This is the seam every viable
  option (B/C/D) depends on, and it stays low-risk. See design.md §1/§6.
- **On keeping LibGUI at all:** the crash is **unresolved upstream** (a 3.1.0 native-lib ABI-collision
  regression with no maintainer fix), so B/C keep a live dependency on a library whose crash class
  could shift with distro updates. That doesn't override C, but it raises the weight of Option D
  (native-only) as the path that removes the risk surface entirely — a genuine maintainer call (§7).

## Scope / non-goals

- This change ships **no** production code and makes **no** capability-spec changes. It is a
  decision document. It does not touch `Directory.Build.props`, CI, or the LibGUI build.
- It does not decide *whether* to keep LibGUI long-term (Option D vs B/C) — it surfaces the
  trade-off (LibGUI's documented fragility vs. its polish) for the maintainer to weigh.
- Effort figures are relative t-shirt/dev-week ranges (design.md §5), not commitments; the native
  dialog rebuild is the dominant, irreducible cost of every crash-safe option.

## Impact

- New files only, under `openspec/changes/assess-libgui-decoupling/` (`proposal.md`, `design.md`,
  this `.openspec.yaml`). Additive; safe alongside in-flight work.
- Follow-up (post-decision): a `decouple-libgui` implementation change that (1) introduces the
  dialog-factory seam in Core/blocks, (2) builds the native GUI layer reusing the ancestor's Cairo
  elements, (3) packages per the chosen option, and (4) makes `gui` a runtime-detected soft
  dependency (removed from `modinfo.json`, gated by `IsModEnabled("gui")`).
