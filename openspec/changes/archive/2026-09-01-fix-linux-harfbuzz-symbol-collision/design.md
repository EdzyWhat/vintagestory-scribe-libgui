## Context

Confirmed via a real `coredumpctl` crash dump (not log-line inference — see
`assess-libgui-decoupling/design.md` §5 "Confirmed" for the full evidence trail and
`fix-linux-sans-serif-font-crash/` for the disproven prior theory) that Linux clients on KDE
Plasma/KWin abort with `free(): invalid pointer` via symbol interposition:

```
#0  abort (libc.so.6)
#1-3 n/a (libc.so.6)   [glibc abort path]
#4  n/a (libharfbuzz.so.0 + 0x57152)               <- the SYSTEM's HarfBuzz
#5  hb_font_create (libHarfBuzzSharp.so + 0x22f67)  <- LibGUI's BUNDLED HarfBuzzSharp
```

`hb_font_create` is a real, named export inside the *bundled* `libHarfBuzzSharp.so` (LibGUI 3.1.0
ships HarfBuzzSharp 8.3.1), but execution lands in frame #4, inside a *separate*, already-loaded
system `libharfbuzz.so.0` (present because KDE Plasma's Qt text stack loads system HarfBuzz
process-wide). Both libraries export overlapping `hb_*` global symbols at incompatible versions; the
dynamic linker resolves an internal call inside the bundled library to the wrong one, corrupting the
heap until `free()` aborts. This is the exact mechanism the user filed upstream months ago
([ripls56/vslibgui#2](https://github.com/ripls56/vslibgui/issues/2)) — that issue's own suggested
mitigation, `RTLD_DEEPBIND | RTLD_LOCAL`, was never implemented by the maintainer.

Decompiling the real shipped `src/Mod/lib/HarfBuzzSharp.dll` (via `ilspycmd`) confirms it registers no
custom native-loading logic at all: every P/Invoke uses a plain
`[DllImport("libHarfBuzzSharp")]`/`[LibraryImport("libHarfBuzzSharp")]` attribute, relying entirely
on .NET's default native-library resolution (a normal `dlopen` with standard flags — no
`RTLD_DEEPBIND`). There is no `SetDllImportResolver`/custom loader anywhere in that assembly to
override — it's an unfilled extension point, not something to patch or replace.

`HarfBuzzSharp.Internals.PlatformConfiguration` (same assembly, `public static class`) already ships
a ready-made `IsGlibc` check (calls `gnu_get_libc_version()` via P/Invoke and catches
`TypeLoadException` on non-glibc systems) — useful directly, since `RTLD_DEEPBIND` is a glibc
extension flag not guaranteed to exist/behave the same on musl-based distros.

## Goals / Non-Goals

**Goals:**
- Force the bundled `libHarfBuzzSharp.so` to load with `RTLD_DEEPBIND` on Linux/glibc, so its
  internal symbol lookups prefer its own copy over anything else already resident in the process —
  removing the interposition without touching LibGUI, HarfBuzzSharp, or any native binary.
- Win the race against every other mod's `StartClientSide`, so this protects not just Scribe's own
  first HarfBuzz call, but any other LibGUI-based mod (HudUI, ChatUI, etc.) on the same client that
  would otherwise trigger the default, unisolated load path first.
- Fail closed to today's exact behavior on any platform or condition this doesn't apply to or can't
  help — never introduce a new crash risk while fixing an existing one.

**Non-Goals:**
- Fixing this upstream in LibGUI/HarfBuzzSharp itself — tracked as a follow-up communication (relay
  this pattern to ripls56 as a candidate fix once verified), not part of this change's code.
- Any Windows/macOS behavior change — this bug is glibc + a system HarfBuzz already loaded
  process-wide (observed under KDE Plasma/Qt), not a cross-platform concern.
- Revisiting `fix-linux-sans-serif-font-crash`'s alias mechanism — that change stays archived as a
  disproven hypothesis; this change does not modify or depend on it.

## Decisions

**Mechanism: `NativeLibrary.SetDllImportResolver` against `HarfBuzzSharp.Face`'s assembly, not
Harmony.** `NativeLibrary.SetDllImportResolver(assembly, resolver)` is a public, documented .NET BCL
extension point specifically for taking over native-library resolution per-assembly. Since
`HarfBuzzSharp.dll` has no existing resolver to override, a plain Harmony patch isn't needed — we're
filling an empty hook, not intercepting existing behavior. `HarfBuzzSharp.Face` is a public type in
the target assembly, giving a clean `typeof(HarfBuzzSharp.Face).Assembly` handle without
reflection-by-string-name.

**Winning the load race: override `ModSystem.ExecuteOrder()` to run before everything else.**
Decompiled `VintagestoryLib.dll` confirms `ModLoader.instantiateMods` sorts every mod's
`StartClientSide` primarily by `ExecuteOrder()` (default `0.1`, unmodified by `ScribeModSystem` or
LibGUI's own `GuiModSystem`), ties broken by dependency-topological/alphabetical order. Returning a
value well below `0.1` from a new override guarantees this fix's registration runs before *any* mod's
first HarfBuzz-touching code — including Scribe's own `BuildMetrics` probe, LibGUI's own deferred
Settings dialog, and any other LibGUI-based mod. This is the same load-order mechanism examined (and
found unhelpful without an explicit override) during the disproven prior investigation — the
difference here is that we now deliberately set it, rather than relying on incidental alphabetical
luck.

**Resolver body: manual `dlopen(path, RTLD_NOW | RTLD_DEEPBIND)`, gated on `PlatformConfiguration.IsGlibc`.**
`RTLD_DEEPBIND` (`0x00008` on glibc) has no managed equivalent — `NativeLibrary.Load` doesn't expose
custom `dlopen` flags — so the resolver calls `dlopen` directly via
`[DllImport("libdl.so.2")] static extern IntPtr dlopen(string filename, int flags)` and returns the
resulting handle. Gate the whole attempt on `HarfBuzzSharp.Internals.PlatformConfiguration.IsGlibc`
(already shipped in the same assembly) rather than writing a new glibc-detection heuristic — `RTLD_DEEPBIND`
is not a portable POSIX flag and its behavior on musl is unspecified.

**Locate the bundled `.so` relative to the loaded `HarfBuzzSharp` assembly, not a hardcoded path.**
The real shipped `gui_3.1.0.zip` lays out natives at `native/<rid>/native/libHarfBuzzSharp.so`
(confirmed via `unzip -l` on the actual mod zip). Resolve the assembly's own `Location` at runtime and
derive the sibling native path from it, since mod install directories vary by machine/OS and by
whether the user runs a zip or extracted-folder install.

**Fallback path: return `IntPtr.Zero` on any failure, at every step.** Non-glibc Linux, `dlopen`
failure, or a `.so` that can't be located all fall through to `IntPtr.Zero`, which tells
`NativeLibrary.SetDllImportResolver`'s caller to proceed with the runtime's normal default resolution
— exactly today's existing (working, for unaffected systems) behavior. Never throw from the resolver
delegate itself.

**Considered and rejected: Harmony patch on an existing HarfBuzzSharp method.** There's no existing
method to intercept — `HarfBuzzSharp.dll` never sets a resolver of its own, so Harmony would have
nothing to redirect. A resolver registration achieves the same outcome with a supported, documented
API instead of IL patching, and is inherently more forward-compatible with future HarfBuzzSharp
versions that also don't set their own resolver.

**Considered and rejected: fork LibGUI/HarfBuzzSharp entirely.** Discussed directly with the
maintainer of this project; rejected because the actual bug is scoped to one dependency's
native-loading behavior (an empty extension point to fill), not to anything in LibGUI's ~500-file
managed framework — forking the whole framework would trade a small, targeted, low-maintenance fix
for an open-ended commitment to an entire Skia/HarfBuzz UI framework's native binaries and future bug
surface, disproportionate to the one confirmed bug.

## Risks / Trade-offs

- **[Risk] `RTLD_DEEPBIND` is glibc-specific; behavior on other Linux libc implementations (musl) is
  unspecified** → **Mitigation:** gated on `PlatformConfiguration.IsGlibc`; non-glibc systems get no
  custom resolver at all, falling through to today's existing default behavior (which, notably, may
  not even be broken for those systems — this crash has only been observed on glibc/KDE systems so
  far).
- **[Risk] Overriding `ExecuteOrder()` to an extreme value could have unintended interactions with
  other mods that also override it for their own startup-ordering needs** → **Mitigation:** decompiled
  evidence shows no other mod in this ecosystem currently overrides it; even if one later does,
  running "very early" doesn't conflict with anything Scribe or LibGUI do at that point (registering a
  resolver has no side effects on anything except the specific native-library name it targets).
- **[Risk] No local Linux/glibc/KDE machine to reproduce or verify on** → **Mitigation:** same as the
  prior change — community-assisted verification via the same ModDB/Discord reporters, primarily
  SnuwWulfie (already has working `coredumpctl` tooling). Verify by checking whether a new crash dump's
  backtrace still shows the `libharfbuzz.so.0` interposition frame.
- **[Risk] If `dlopen`'s returned handle doesn't expose the same symbols under the same names the
  runtime's P/Invoke marshaling expects, this could produce a *new*, different failure mode instead of
  fixing anything** → **Mitigation:** `dlopen` returns a standard library handle; P/Invoke resolution
  against it works identically regardless of which flags were used to open it — `RTLD_DEEPBIND` only
  changes symbol *lookup scope* during execution, not the handle's shape. Verify empirically via 2.2
  before shipping to reporters.

## Migration Plan

Pure additive client-side code change — one new startup registration, no data migration, no
save-format or network change. Ship in the next release. Rollback is a plain revert; nothing here is
persisted or migrated.

## Open Questions

- Does this actually eliminate the crash on a real affected machine, or is there a further wrinkle
  (e.g. the system HarfBuzz getting loaded via a path that also touches something outside
  `libHarfBuzzSharp.so`'s own resolution)? Only answerable by a reporter testing a build — tracked as
  a task, not blocking implementation.
- Should this also register a resolver for `libSkiaSharp` defensively, in case a similar interposition
  risk exists there? Not indicated by any current evidence (the confirmed crash is HarfBuzz-specific,
  and Vintage Story already ships its own Skia), so left out to keep this change narrowly scoped to
  the reported and confirmed crash.
