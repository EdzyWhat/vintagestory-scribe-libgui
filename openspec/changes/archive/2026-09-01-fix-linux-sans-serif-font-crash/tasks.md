## 1. Implement the alias fix

- [x] 1.1 In `ScribeModSystem.Assets.cs`'s `RegisterCustomFonts()`, track which bundled faces loaded
      successfully (Caudex + each of the 4 task fonts already have this via `bold is null` /
      `face is null` checks — just keep a small ordered success list as the loop runs).
- [x] 1.2 After the task-font registration loop and before `ScribeTaskFont.BuildMetrics(...)` is
      called, pick the first successfully-loaded face from the candidate order
      `["Noto Sans", "Noto Serif", "Scapholene", "La Belle Aurore", "Caudex"]` and call
      `FontRegistry.RegisterFontAlias("sans-serif", <chosen family>)`. If none loaded, skip the call
      (already-logged warnings cover that failure mode).
- [x] 1.3 Add a one-line log (`api.Logger.Notification`) recording which family `"sans-serif"` was
      aliased to, so a future crash log immediately shows whether the alias landed before any crash.
- [x] 1.4 Add diagnostic log brackets in `ScribeTaskFont.BuildMetrics` (`ScribeRowConstants.cs`)
      around both the Caudex reference probe and the `DefaultFamily`/`"sans-serif"` probe, so a
      future crash log pinpoints exactly which measurement was in progress — needed because both are
      real text-shaping calls and neither logged anything on success before, so an existing crash log
      can't distinguish "crashed on Caudex" (would mean this fix doesn't address the real bug) from
      "crashed on sans-serif" (this fix's actual target). See design.md's Open Questions.

## 2. Verify locally (what's testable without a Linux machine)

- [x] 2.1 Run the existing Core test suite (`dotnet test` under `tests/Core.Tests`) — expect no
      change, since this touches only `src/Mod/ScribeModSystem.Assets.cs`. (611 passed, 0 failed.)
- [ ] 2.2 Restage a Debug build (`build/restage.sh Debug`, client fully quit first per project
      convention — done, client was not running) and smoke-test on this machine (macOS): open
      Settings, confirm the font selector and every task font still render correctly; leave
      `TaskFontFamily` at Default and confirm a task row still renders and its height still matches
      Caudex's line-box. **Backlogged 2026-08-31**: moot now that 3.5 confirmed this fix's own
      premise (family resolution) isn't the real crash mechanism — the alias itself is harmless and
      already shipped; a dedicated smoke-test of it isn't worth blocking archival on.
- [ ] 2.3 Confirm the new log line appears on world join, naming the chosen alias family.
      **Backlogged 2026-08-31**: same reasoning as 2.2 — the log line is confirmed working via
      SnuwWulfie's own crash log in 3.2 ("sans-serif" aliased to bundled font 'Noto Sans'"), so this
      is already indirectly verified; not worth a dedicated local retest.

## 3. Community verification (the only real test of the fix)

- [x] 3.1 Publish a build with the fix (`scribe_1.3.4-rc.1.zip`) and ask the affected ModDB reporters
      (Jack_Frost, SnuwWulfie, Nieb, Vinni_Pukh) to test it on their Linux systems.
- [x] 3.2 **Result (2026-08-30, SnuwWulfie, CachyOS): crash still reproduces, and it falsifies this
      fix's premise.** Client log:
      ```
      [scribe] bundled font 'Caudex' (bold cut) registered under all weights for the lectern dialog title
      [scribe] bundled task-text fonts registered for the settings font selector
      [scribe] "sans-serif" aliased to bundled font 'Noto Sans' (avoids a live OS font lookup)
      [scribe] measuring 'Caudex' as the task-font line-box reference
      free(): invalid pointer
      ```
      The alias registered successfully and logged — the crash is not there. It lands at the very
      next line, the Caudex probe, with no "measured reference line-box" line after it. Caudex is a
      fully custom-registered face (loaded from Scribe's own bundled TTF, never touches
      `SKTypeface.FromFamilyName` or any OS/fontconfig lookup) — so this is decisive: the crash is
      not about family *resolution* at all. It's the first HarfBuzz shape call of the session,
      period, regardless of which font is being shaped. This is exactly the *original*
      ripls56/vslibgui#2 ABI-mismatch theory (bundled `libHarfBuzzSharp` 8.3.1 vs. the system's
      `libharfbuzz`), not this change's theory.
- [x] 3.3 This fix does not resolve SnuwWulfie's crash — confirmed, and superseded by a stronger
      finding (see 3.5). `assess-libgui-decoupling/design.md` §5 updated with the outcome. Jack_Frost
      was not independently retested against this same build — left open, low priority now that the
      real mechanism (3.5) is confirmed and is not font-related.
- [x] 3.4 Read the log against 1.4's brackets: crash after "measuring 'Caudex' as the task-font
      line-box reference" but before "measured reference line-box" → **this is what happened.** This
      fix doesn't address the real bug; Caudex itself is the crash site. Escalate to
      `assess-libgui-decoupling` rather than iterating on this change further — done.
- [x] 3.5 **Root cause confirmed with a real backtrace (2026-08-30).** SnuwWulfie pulled the actual
      crash dump via `coredumpctl info 49312 > file.txt` (redirected straight to a file — console
      copy/paste alone truncated it and lost the crashing thread's section on the first attempt).
      Thread 49312 (crashing/main thread)'s top frames:
      ```
      #0  abort (libc.so.6)
      #1-3 n/a (libc.so.6)   [glibc abort path]
      #4  n/a (libharfbuzz.so.0 + 0x57152)               <- system HarfBuzz
      #5  hb_font_create (libHarfBuzzSharp.so + 0x22f67)  <- bundled HarfBuzzSharp's own function
      ```
      Symbol interposition: the bundled `libHarfBuzzSharp.so`'s own `hb_font_create` call lands inside
      the *system's* separately-loaded `libharfbuzz.so.0` (present because KDE/Plasma's Qt text stack
      loads it process-wide) instead of staying inside its own build. Confirms the *original*
      ripls56/vslibgui#2 hypothesis with a named symbol, not inference. Not font-related in any way —
      also confirmed no dynamic/runtime mitigation exists (tested: crash reproduces identically
      whether or not `LD_PRELOAD` forces the system HarfBuzz to load first; the corrupting condition
      is fixed at process load, not per-call). Full writeup:
      `assess-libgui-decoupling/design.md` §5 "Confirmed" section.

## 4. Upstream communication (tracked here, not blocking)

- [x] 4.1 Relayed to ripls56 on the existing GitHub issue (ripls56/vslibgui#2), 2026-08-30 — including
      the confirmed backtrace from 3.5 (stronger evidence than the originally-planned "wider ecosystem
      footgun" framing, which is now superseded by the named-symbol proof). Also posted to the LibGUI
      Discord and the LibGUI ModDB page the same day, for visibility to other affected LibGUI mod
      users who may not watch the GitHub issue.
