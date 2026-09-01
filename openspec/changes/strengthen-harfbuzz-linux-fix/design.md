## Context

`src/Mod/ScribeHarfBuzzLoadFix.cs` is a standalone `ModSystem` (deliberately NOT a
`ScribeModSystem` partial — see its own remarks on the macOS regression a prior, more tightly
coupled version caused). On Linux/glibc only, its `StartClientSide` (`ExecuteOrder() => -1.0`)
raw-P/Invokes `dlopen(soPath, RTLD_NOW | RTLD_DEEPBIND)` on the bundled `libHarfBuzzSharp.so`,
resolved relative to the loaded `HarfBuzzSharp` assembly. This deliberately avoids
`NativeLibrary.SetDllImportResolver` because `Gui.NativeLibraryLoader.Register()` (called from
`GuiModSystem.StartClientSide`) already calls it for the same assembly, unguarded against a second
caller — a second `SetDllImportResolver` call on the same assembly throws, and `gui`'s own call has
no try/catch around it. So today's fix instead wins a *race*: it maps the file first, and relies on
the OS deduping `dlopen` by canonical path so `gui`'s later, unflagged load of the same path
transparently reuses the already-deep-bound handle.

This works, but only because Scribe's `ExecuteOrder` is tuned to run before `gui`'s. A community
member (Seralth, `github.com/Seralth/harfbuzzfix`) independently confirmed the same root cause and
shipped a standalone mod using a different, order-independent mechanism: a Harmony `Prefix` patch on
`Gui.NativeLibraryLoader.Register()` itself, replacing its body outright rather than racing it.
Decompiling the shipped `Gui.dll` (v3.1.0) confirms `Register()` is `internal static void
Register()` — parameterless, single-purpose (its entire body registers exactly one resolver, for
the `HarfBuzzSharp` assembly, guarded by its own `_registered` bool) — so a prefix that
unconditionally skips the original body cannot accidentally skip unrelated registration logic;
there isn't any.

## Goals / Non-Goals

**Goals:**
- Replace the race-based mechanism with a deterministic one: Scribe's isolation applies regardless
  of mod load order, the same way Seralth's mod does.
- Preserve every existing safety property of the current fix: Linux-only, glibc-only, no-op
  elsewhere, never throws, never regresses a user below "no fix at all" if anything about the
  mechanism fails.
- Credit Seralth's independent discovery and corroborate the upstream issue with this evidence.

**Non-Goals:**
- Not building a general Harmony-patch framework for other `gui` internals — this is one narrowly
  scoped patch for one method, kept as isolated as the current fix already is.
- Not modifying `broaden-linux-harfbuzz-fix`'s own remaining tasks (Core test coverage for its
  outcome model, community-distribution work) — this change only swaps the mechanism inside
  `ScribeHarfBuzzLoadFix`.
- Not vendoring or copying Seralth's source — his repo carries no license, so this reimplements the
  *technique* from first-hand analysis of the shipped `Gui.dll`, not his file.

## Decisions

### 1. Fully replace the dlopen-race mechanism (not layer both)

Per the author's direction: delete the raw-`dlopen` race entirely rather than keeping it as a
fallback alongside the Harmony patch. One mechanism is easier to reason about, test, and explain in
code comments than two overlapping ones — and the Harmony patch is strictly more robust (assuming it
applies at all — see Decision 2's fail-closed behavior for the case where it can't), so the race
buys nothing once the patch is in place except doubled maintenance surface.

**Alternative considered**: keep the dlopen-race as an automatic fallback if the Harmony patch fails
to apply. Rejected for this change — it's a reasonable *future* hardening step if the Harmony patch
turns out fragile in practice (e.g. a `gui` update breaks it more often than expected), but adds
complexity now for a scenario that hasn't happened yet. If the patch fails, failing open to `gui`'s
own (unisolated) loader is exactly what happens today for any platform/condition this fix doesn't
cover (Windows, macOS, musl) — not a new gap, just the same "no worse than not having this mod"
floor the current fix already guarantees.

### 2. Fail-closed patch application, same discipline as today's fix

Wrap the Harmony `Patch(...)` call (finding `Gui.NativeLibraryLoader` via `AccessTools.TypeByName`,
then `AccessTools.Method(type, "Register")`) in the same style of guard the current fix already
uses for `File.Exists`/`dlopen` failures: if the type or method can't be found, or `Patch` itself
throws, log a `Warning` and return — `gui`'s original `Register()` then runs completely unpatched,
exactly as if this fix weren't installed. This is what makes the mechanism swap safe against a
future `gui` release renaming, removing, or restructuring `Register()`: the patch attempt itself
never crashes Scribe's startup, it just silently stops protecting the user (same floor as any other
unsupported case).

The `Prefix` method itself must also never throw past Harmony's `Patch` scaffolding: if isolation
setup inside the prefix fails (e.g. `SetDllImportResolver` throws for an unexpected reason), catch
it, log, and return `true` so Harmony runs `gui`'s original method afterward instead of leaving
HarfBuzzSharp with no resolver registered at all.

### 3. The prefix's own resolver logic mirrors the current fix's isolation approach, not `gui`'s

The replacement resolver (installed via `NativeLibrary.SetDllImportResolver` inside the prefix)
resolves the bundled `.so` the same way the current fix already does — relative to the loaded
`HarfBuzzSharp` assembly's location, same `native/<rid>/native/libHarfBuzzSharp.so` layout — and
opens it with `dlopen(path, RTLD_NOW | RTLD_DEEPBIND)`. If that lookup or `dlopen` call fails, fall
back to the unflagged `NativeLibrary.TryLoad(name, ...)` gui's own resolver would have used, so a
missing/misplaced file degrades to today's un-isolated (but still functional on non-conflicting
systems) behavior rather than a hard failure.

**Alternative considered**: locate the native file the way Seralth's fallback does (probing several
candidate directories, including a `runtimes/<rid>/native` layout). Rejected — Scribe's own fix has
already confirmed the exact bundled layout (`native/<rid>/native/`) works in production; adding
untested candidate paths is speculative complexity with no known case it fixes.

### 4. Same Linux/glibc gating, same standalone `ModSystem` shape

No change to `OperatingSystem.IsLinux()` / `PlatformConfiguration.IsGlibc` gating, and no change to
keeping this as its own `ModSystem` (not a `ScribeModSystem` partial) — that isolation is what
prevented the macOS regression documented in the current file's remarks, and is orthogonal to which
mechanism runs once the Linux/glibc gate passes. `ExecuteOrder` no longer needs to be tuned to beat
`gui` (the patch is order-independent), so it can move to Harmony's usual patch-application timing —
`StartPre`, matching Seralth's own mod's timing, since the patch only needs to be IN PLACE before
`gui`'s `StartClientSide` calls `Register()`, and `StartPre` runs earlier for every mod than any
mod's `StartClientSide`.

## Risks / Trade-offs

- **[Risk] Harmony-patching an `internal` method is inherently coupled to `gui`'s current
  implementation, not a public contract** (project precedent already flags "private/internals
  patch = avoid" as a fragility caution, vs. today's fix which touches no `gui` code at all) →
  **Mitigation**: Decision 2's fail-closed guard means a signature/behavior change in a future `gui`
  release degrades this to a no-op with a logged warning, never a crash or a worse-than-today state.
  This is the same trade Seralth's own mod already accepts in production.
- **[Risk] Losing the race-based fix's one advantage — it patches nothing, so it can never break due
  to a `gui`-internals change** → **Mitigation**: accepted per Decision 1; revisit adding the
  dlopen-race back as an automatic fallback (not a parallel mechanism) if the Harmony patch is
  observed failing in the wild.
- **[Trade-off] No vendored license for the technique** — reimplemented independently from
  first-hand `Gui.dll` decompilation rather than Seralth's source, but the *idea* (Harmony-prefix a
  single-purpose internal loader method, self-register an isolated resolver) is credited to him in
  code comments and this design doc regardless, since he shipped and validated it first.

## Migration Plan

- Straight replacement inside one file (`ScribeHarfBuzzLoadFix.cs`); no data model, no persisted
  state, no wire-protocol involvement — nothing to migrate.
- Rollback is a normal revert; the old dlopen-race code stays in git history if a future regression
  ever calls for reverting rather than re-patching.
- Manual/in-game verification (task list) must happen on a real Linux/glibc box before shipping,
  same as the original fix required — this is Linux-only behavior no macOS/Windows dev machine can
  smoke-test locally.

## Open Questions

None outstanding — the two decisions the author flagged (replace vs. layer; whether to still pursue
the upstream issue) were resolved directly with the author before writing this design.
