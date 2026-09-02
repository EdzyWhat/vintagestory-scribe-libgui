## Why

`ScribeHarfBuzzLoadFix.FindNativeDir` locates the bundled `native/<rid>/native/` directory
using `RuntimeInformation.RuntimeIdentifier` directly. Decompiling the actual shipped
`Gui.dll` shows `Gui.NativeLibraryLoader`'s own `GetRid()` deliberately does NOT use that
API — it manually maps `RuntimeInformation.ProcessArchitecture` to a hardcoded RID string.
Seralth's independently-shipped `harfbuzzfix` mod does the same manual mapping. Ours is the
only one of the three that trusts `RuntimeInformation.RuntimeIdentifier`, a documented-
fragile API for this exact purpose (it can return a longer, distro-qualified RID that
doesn't match the flat folder name the native asset is actually shipped under). If it ever
returns something other than the expected flat RID on some runtime/OS combination,
`FindNativeDir` returns null, the isolated resolver throws, and the fix silently falls back
to `gui`'s unpatched loader — reproducing the exact crash it exists to prevent. This is a
concrete, evidence-backed correctness bug in our own code, independent of the still-open
1.4.0-rc.1 regression investigation's mechanism-level questions (Harmony patch reflection
failing, or the lazy resolver losing a race) — worth fixing and testing on its own.

## What Changes

- `FindNativeDir` builds the RID the same way `gui`'s own `GetRid()` does: a manual switch
  on `RuntimeInformation.ProcessArchitecture` (and OS, for structural parity even though
  only the Linux branch is reachable today) instead of trusting
  `RuntimeInformation.RuntimeIdentifier`.
- No other behavior change: same fallback structure, same fail-closed guarantees, same
  Harmony-patch skeleton, all untouched.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
(none in `openspec/specs/` — this is an implementation-detail fix inside a capability,
`linux-harfbuzz-native-isolation`, whose own delta specs are still open/unarchived in
sibling changes; no observable requirement changes as a result of this fix, only a
correctness fix to how an existing requirement is satisfied)

## Impact

- `src/Mod/ScribeHarfBuzzLoadFix.cs` — `FindNativeDir` only.
- No `src/Core/` impact, no new dependency.
- Ships as its own isolated test build (worktree/branch, not merged to `main`) so it can be
  verified in a VM independently of other candidate HarfBuzz fixes.
