# Crash investigations

Native (SIGSEGV/EXC_BAD_ACCESS-style) crashes that bypass Vintage Story's own managed crash
logger — so `client-crash.log` shows nothing — and instead only show up as an OS-level crash
report (Apple Crash Reporter on macOS, WER on Windows). Logged here per-incident so we don't
re-derive the same read of a log/crash-report pair twice. Newest entry on top.

---

## 2026-09-04 — `glDeleteBuffers` SIGSEGV on `SingleplayerServer` thread (macOS, Apple M4)

**Status:** Unresolved / watching for recurrence. Not acted on — no code change made.

**Evidence:**
- `~/Library/Application Support/VintagestoryData/Logs/client-main.log`
- Apple Crash Reporter report, saved by the user as `~/Downloads/SeafarerCrash1.txt`

**Timeline (client-main.log):**
- `07:39:40` — `Client pause state is now on` fires; in the same instant the log starts
  spamming `[Error] after final compo   - OpenGL threw an error: InvalidOperation` — **51,387**
  repeats of that one line, one per rendered frame, nonstop.
- `07:54:48` — the spam stops.
- `07:54:49` — `Destroying game session` / `Stopping single player server` (world teardown
  begins).
- `07:54:51` (crash report timestamp) — hard crash.

**Why no VS crash log:** it's a native `SIGSEGV` (`EXC_BAD_ACCESS`), not a caught .NET
exception, so Vintage Story's own managed crash handler never gets a chance to run — only the
OS-level reporter catches it. Consistent with `client-crash.log`'s mtime being stale (untouched
by this incident).

**Crash report facts (SeafarerCrash1.txt):**
- macOS 26.6.2, Apple M4 (Mac16,13), VS 1.22.6.
- `Exception Type: EXC_BAD_ACCESS (SIGSEGV)`, `KERN_INVALID_ADDRESS at 0x1420`.
- Crashing thread: **`SingleplayerServer`**.
- Top frame: **`libGL.dylib glDeleteBuffers`** — i.e. a GPU mesh-buffer delete call, issued from
  the integrated-server thread rather than the render/main thread.
- `Time Since Wake: 2075s` (~34 min) — machine had woken from sleep not long before.

**Mods loaded this session:** game, vsmctfdesigner, almanacilluminated, configkit, envelopes,
gui, messengerpigeons, noticeboard, progressionframework, toolsmith, creative, survival, hudui,
libguitoolsmithsharpness, playerinvui, scribe, seafarer, thebasics.

**Working theory:** the 15-minute run of continuous per-frame `InvalidOperation` GL errors
(starting the instant the game paused) suggests the OpenGL context was already unhealthy —
plausibly from sitting paused/idle that long on this machine. When shutdown then ran and
something disposed a cached GPU mesh (`MeshRef.Dispose()` → `glDeleteBuffers`), it hit that
already-broken context from the wrong thread and segfaulted. Points at a **macOS GL-context
health issue tied to a long paused/idle stretch**, not obviously a single mod's logic bug.

**Checked and ruled out as *this session's* cause:** `ItemScribeTaskNotice.OnUnloaded`
(`src/Mod/ItemScribeTaskNotice.cs`) disposes a cached `MultiTextureMeshRef` unconditionally,
which is the same *shape* of call as the crashing frame — but it's copied verbatim from
vanilla's own `CollectibleBehaviorCustomTongedShape.OnUnloaded`
(`vssurvivalmod/CollectibleBehavior/CollectibleBehaviorCustomTongedShape.cs`), same
no-thread-guard pattern, so if this call shape is ever the actual trigger it's a latent
base-game pattern, not something introduced by any Scribe change from this session. Neither of
this session's actual code changes (add-configkit-visual-tuning, fix-quest-catalog-domain-scoping)
touch any GL-resource lifecycle code at all — both are pure client-rendering *values* (tuning
knobs, asset-domain search scope), not mesh/buffer disposal.

**Next data point that would move this forward:** whether a recurrence always follows a
similarly long paused/idle stretch before quitting. If yes, that confirms the idle-GL-context
theory over a mod-teardown-ordering bug. If a crash instead happens right after a *short*
session with no extended pause, revisit `ItemScribeTaskNotice.OnUnloaded` (and grep for any
other Scribe `OnUnloaded` that disposes a `MeshRef`/GPU resource) for a thread/Side guard.
