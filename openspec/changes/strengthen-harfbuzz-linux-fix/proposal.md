## Why

Scribe's current Linux/glibc HarfBuzz symbol-collision fix (`src/Mod/ScribeHarfBuzzLoadFix.cs`,
shipped via `broaden-linux-harfbuzz-fix`) works by winning a startup-order race: it raw-`dlopen`s
the bundled `libHarfBuzzSharp.so` with `RTLD_DEEPBIND` before `gui`'s own loader gets a chance to
load the same file unflagged. This is order-dependent — it only protects users because Scribe's
`ExecuteOrder` is deliberately tuned to run first.

A community member (Seralth, `https://github.com/Seralth/harfbuzzfix`) independently diagnosed the
identical root cause (confirmed by us via `coredumpctl`, and by them against an old pre-fix Scribe
build plus several other `gui`-dependent mods) and shipped a working standalone mod using a
different mechanism: Harmony-patching `gui`'s own `Gui.NativeLibraryLoader.Register()` to
*replace* its resolver registration outright, rather than racing to load the file first. That's
strictly more robust — it doesn't depend on any mod-load-order tuning — but today it only protects
players who separately install his mod. We want that same robustness for every Scribe user with
nothing extra to install.

## What Changes

- Replace `ScribeHarfBuzzLoadFix`'s raw-`dlopen`-race mechanism with a Harmony `Prefix` patch on
  `Gui.NativeLibraryLoader.Register()` that installs Scribe's own `RTLD_DEEPBIND`-isolated
  `SetDllImportResolver` for the HarfBuzzSharp assembly, skipping `gui`'s original (unisolated)
  registration.
- The patch must fail closed: if Harmony can't find/patch `Register()` at all (e.g. a future `gui`
  release renames or restructures it), log and let `gui`'s original loader run unmodified — never
  leave a Scribe user worse off than today's fix, matching the discipline the current fix and
  Seralth's mod both already follow.
- No change to the existing Linux-only / glibc-only / no-op-elsewhere gating — same platform
  conditions as today, just a different mechanism once those conditions are met.
- Post a comment on the upstream `ripls56/vslibgui#2` issue (opened by the maintainer of this repo)
  with the confirmed root cause and this mechanism, crediting Seralth's independent fix as
  corroborating evidence, to push for a durable upstream fix in `gui` itself — this remains valuable
  even once Scribe absorbs the fix, since only an upstream fix protects every `gui`-dependent mod's
  users without per-mod patching.

## Capabilities

### New Capabilities
- `linux-harfbuzz-native-isolation`: Scribe SHALL isolate the bundled HarfBuzzSharp native library
  from symbol collision with any system `libharfbuzz` on Linux/glibc, deterministically (not
  order-dependently), failing closed to no-op if the isolation mechanism itself cannot apply.

### Modified Capabilities
(none — no existing `openspec/specs/` capability covers this; `broaden-linux-harfbuzz-fix` is still
in-progress, not yet archived into a spec file, so there is nothing to delta against.)

## Impact

- `src/Mod/ScribeHarfBuzzLoadFix.cs` — mechanism rewritten (Harmony prefix instead of raw `dlopen`
  race); class name/remarks updated to reflect the new approach. Still a standalone `ModSystem`,
  not a `ScribeModSystem` partial (unrelated to the mechanism change, but the isolation rationale
  from the macOS-regression history in the current file's remarks must be preserved/ported forward
  since it's still relevant to how this ModSystem is structured).
- No `src/Core/` impact (Mod-layer only, no Vintage Story API in Core).
- Harmony (`0Harmony.dll`) already ships with the base game — not a new mod dependency.
- `openspec/changes/broaden-linux-harfbuzz-fix/` — cross-referenced, left untouched (its own
  remaining tasks are Core test coverage and community/upstream-issue work, unrelated to this
  mechanism swap).
- Upstream: a comment on `ripls56/vslibgui#2` (external, no repo code impact).
