## Context

Scribe's teaser post landed 500 upvotes in 24 hours with a public "end of the week" release commitment.
The codebase is essentially feature-complete for v1: all v1-blocking code changes are either confirmed
or awaiting a single retest. What remains is verification, one JSON asset (the recipe), one new feature
(font selector), and the distribution scaffolding.

Two risk areas stand out:
1. **Pin Tab** — fully coded, 5 new network messages, but 11 verifications have never run.
2. **Font selector** — a new Settings control and font-bundling work; the Caudex pattern is proven
   (`FontRegistry.RegisterCustomFont` + `TextStyle.FontFamily`) but Scapholene is uncharted.

The design is intentionally minimal: most tasks in this change are verifications and document updates,
not code. The font selector is the only non-trivial code path.

## Goals / Non-Goals

**Goals:**
- Certify every public claim on the mod page (multiplayer-safe, survival-craftable, tested).
- Deliver font choice to users as a direct callback to the teaser community feedback.
- Close the single open test coverage gap that could corrupt player saves (v3-blob codec path).
- Produce the CHANGELOG and updated CREDITS needed for a proper mod DB release.
- Capture post-release action items so nothing is forgotten after the adrenaline of shipping.

**Non-Goals:**
- Authored handbook guide pages (deferred — monitoring discoverability first).
- Any new gameplay feature beyond the font selector.
- Fuzzy reminders, map waypoints, or ownership/permissions (roadmap items, not v1).

## Decisions

**D1 — Font selector stores choice in `ScribePlayerSettings`, not the document**

Font is a display preference, not document content. It belongs alongside `WindowFontScale` and
`PixelArtDisplay` in `ScribePlayerSettings` (the `scribe:settings:v1` JSON blob), persisted and
synced via the existing `UpdateMySettings`/`ApplySettings` path. No codec version bump; the codec
is document-only.

**D2 — Font selector is a dropdown in the Window Appearance section of Scribe Settings**

The Settings window already has a three-section layout (Mod Behavior / Window Appearance / HUD
Appearance). Font for task text is a Window Appearance concern. Implement as a LibGUI dropdown (or
a cycle-button if dropdown is complex) alongside `WindowFontScale`. The button-font decision is
deferred — a separate open question in this change.

**D3 — Scapholene (and other fonts) are bundled TTFs, not system-font references**

System-font access is not viable in the short term (confirmed in teaser thread reply). Bundle each
face under `src/Mod/assets/scribe/textures/fonts/`. Register via `FontRegistry.RegisterCustomFont`
at `StartClientSide` (the proven Caudex pattern). The selector exposes 4 choices; exact font lineup
is an implementation detail tracked in tasks.

**D4 — v3-blob codec test is a pure Core xUnit fixture, no game install needed**

The migration read path (`PriorVersion = 3`) lives entirely in `ScribeDocumentCodec.TryDeserialize`
in `src/Core/`. A test fixture that constructs a hand-crafted v3 byte array and calls `TryDeserialize`
runs on CI with no game install. This is the correct place: the same way other codec tests are structured
in `tests/Core.Tests/`.

**D5 — CHANGELOG.md follows the Keep a Changelog convention**

Simple, widely recognized, no tooling needed. One `## [0.1.0] - YYYY-MM-DD` entry listing Added /
Dependencies sections. Can be expanded for v1.1+.

**D6 — Pin Tab testing added to TESTING.md before in-game run**

The 11 items from `scribe-pin-editor` tasks.md (7.1–7.11) must be transcribed into TESTING.md under
a new `## scribe-pin-editor` section before any in-game run, so verdicts are captured and the
playtest-checklist app can track them. Don't test and then backfill; write the items first.

**D7 — Post-release items are tracked as tasks in this change, not in RELEASE.md**

RELEASE.md covers the mechanical ship steps. Post-release follow-ups (video, mod page, LibGUI
outreach) belong in this change's tasks so they're visible in `openspec list` until archived.

## Risks / Trade-offs

**[Risk] Scapholene rendering on Apple Silicon (Skia/LibGUI path)** → Mitigation: Caudex proved the
`RegisterCustomFont` + `TextStyle.FontFamily` path works on this machine. Test Scapholene early in
the font task before committing to it; have a fallback (a second font candidate) if it fails to
render cleanly at small sizes.

**[Risk] Font selector adds a new `ScribePlayerSettings` field** → Mitigation: the field defaults to
a sentinel ("default" or `null`) that resolves to the existing body font, so the existing UX is
unchanged for players who never touch the selector. No migration needed.

**[Risk] Pin Tab cross-lectern edit against an unloaded lectern** → Mitigation: tasks 7.4 and 7.5
specifically cover the loaded vs. unloaded cases. Run them explicitly; don't mark the section done
until both pass.

**[Risk] "End of the week" commitment slips** → Mitigation: Pin Tab testing, multiplayer pass, and
survival pass are the actual gates. Font selector is additive — if it isn't ready, it ships as v1.1
without impacting the release date claim. Identify early whether fonts will make the cut.

## Open Questions

1. **Button font**: what font (if any) should replace or supplement the current button text? Defer
   to an explicit decision during the font task — don't ship inconsistency if there's no good answer.
2. **Font lineup**: which 4 faces are bundled? Scapholene is confirmed. What are the other 3?
   Candidates from ROADMAP: Playfair Display, Cormorant Unicase (already in LibGUI — zero-asset
   option), a third handwritten/rustic face. Decide in the font task.
3. **Font selector UI control**: LibGUI dropdown vs. cycle-button — depends on how many options
   and whether LibGUI's dropdown is ergonomic enough. Decide during implementation.
