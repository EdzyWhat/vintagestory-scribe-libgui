## Context

`ScribeHarfBuzzLoadFix.FindNativeDir` (from `strengthen-harfbuzz-linux-fix`) resolves the
bundled `libHarfBuzzSharp.so`'s directory as:
```csharp
string rid = RuntimeInformation.RuntimeIdentifier;
string nativeDir = Path.Combine(assemblyDir, "native", rid, "native");
```
Decompiling the shipped `Gui.dll` shows `Gui.NativeLibraryLoader.GetRid()` — the method
that resolves the identical `native/<rid>/native/` layout for `gui`'s own (normally
unpatched) loading of the same assembly — never calls `RuntimeInformation.RuntimeIdentifier`.
It manually switches on `RuntimeInformation.ProcessArchitecture`:
```csharp
// Linux branch, decompiled from Gui.dll:
return RuntimeInformation.ProcessArchitecture switch {
    Architecture.Arm => "linux-arm",
    Architecture.Arm64 => "linux-arm64",
    Architecture.X86 => "linux-x86",
    _ => "linux-x64",
};
```
Seralth's independently-shipped `harfbuzzfix` mod does the same kind of manual mapping
(`RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64"`,
plus a directory-search fallback as extra safety). Our code is the only one of the three
implementations that trusts `RuntimeInformation.RuntimeIdentifier` directly for this.

## Goals / Non-Goals

**Goals:**
- Make `FindNativeDir`'s RID resolution match the convention `gui` itself already uses for
  the exact same native-asset layout, since that's the ground truth for how the folder is
  actually named on disk.
- Change nothing else about the fix's structure or safety guarantees.

**Non-Goals:**
- Not adopting Seralth's additional directory-search fallback (multiple candidate paths) —
  out of scope for this narrowly-targeted fix; worth revisiting separately if the manual-RID
  fix alone turns out insufficient.
- Not touching the Harmony-patch skeleton, the glibc gating, or any other part of
  `ScribeHarfBuzzLoadFix.cs`.

## Decisions

### Match `gui`'s own `GetRid()` mapping exactly, rather than inventing a different one

Since this method only needs to resolve the same on-disk folder `gui`'s own loader already
resolves successfully in the non-crashing (no system-HarfBuzz-collision) case, the safest
choice is to copy the exact mapping already proven to work — not a new, unverified
heuristic. Structured as a small switch on `RuntimeInformation.ProcessArchitecture`,
Linux-only (matching where this method is actually called from today), with an inline note
citing the decompiled source for why this deviates from the more "obvious"
`RuntimeInformation.RuntimeIdentifier` approach — so nobody "fixes" it back later without
knowing why.

**Alternative considered**: use `RuntimeInformation.RuntimeIdentifier` but with a fallback
directory search (Seralth's approach) instead of replacing it outright. Rejected for this
change — that reintroduces exactly the API this fix is trying to stop trusting, as the
*first* attempt; better to lead with the known-correct mapping and treat this as the
complete fix unless field evidence says otherwise.

## Risks / Trade-offs

- **[Risk] `gui` could change its own RID mapping in a future release, silently
  desynchronizing ours from the ground truth again** → **Mitigation**: same category of
  risk the Harmony-patch skeleton already accepts for `Register()`'s internal signature;
  no different in kind. Not solvable without an upstream contract, which doesn't exist for
  an `internal` method.
- **[Trade-off] Doesn't validate whether this was actually the cause of lunardiver's
  1.4.0-rc.1 regression** — the identical `RuntimeInformation.RuntimeIdentifier`-based
  lookup was also present in the earlier RC that worked for him, on the same machine, which
  weakens (without ruling out) this being *his* specific trigger. Shipped anyway as a
  correctness fix worth having regardless, and testable in isolation via its own VM build.
