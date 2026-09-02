## 1. Implementation

- [x] 1.1 Replace `FindNativeDir`'s `RuntimeInformation.RuntimeIdentifier` call with a
      manual switch on `RuntimeInformation.ProcessArchitecture`, matching `gui`'s own
      decompiled `GetRid()` mapping for Linux (`linux-arm`, `linux-arm64`, `linux-x86`,
      `linux-x64`).
- [x] 1.2 Add a short comment citing the decompiled `Gui.dll` finding, so the deviation from
      the "obvious" `RuntimeIdentifier` approach isn't silently reverted later.
- [x] 1.3 No other change to `ScribeHarfBuzzLoadFix.cs`.

## 2. Verification

- [x] 2.1 `dotnet build` succeeds; no `src/Core/` changes.
- [x] 2.2 Build an isolated test zip (throwaway branch/worktree, version-bumped locally
      only) containing only this fix on top of `1.4.0-rc.1`, for local VM testing. Built at
      `spike/harfbuzz-ridfix` (worktree `/tmp/scribe-ridfix-worktree`), version
      `1.4.0-spike.ridfix`, packaged to `Releases/scribe_1.4.0-spike.ridfix.zip`.
- [x] 2.3 Tested on a CachyOS/KDE VM matching lunardiver's reported setup, against a
      baseline (unmodified `1.4.0-rc.1`) that reproduced the crash in 3 of 4 runs: this fix
      ran clean 3/3 times, with no `"Could not locate HarfBuzzSharp's native library
      directory"` fallback warning logged. Chosen over the sibling `sysredirect` spike
      (`redirect-harfbuzz-to-system-lib`, parked, also 3/3 clean on the same VM) because it
      depends only on `RuntimeInformation.ProcessArchitecture` — architecture, not host
      package state — so it carries no new cross-distro risk to verify, unlike relying on
      a system `libharfbuzz` of unknown vintage/ABI-compatibility.
- [x] 2.4 Merged directly to `main` (no separate release cut yet) so Assignment Desk/Inbox
      work can continue on a fixed baseline.
