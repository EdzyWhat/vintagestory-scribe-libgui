## Why

Scribe's teaser post landed 500 upvotes (99% upvoted) and 75 comments in 24 hours, and the community
was told "I should have a v1 release before the end of the week." This change tracks every gate between
the current state and a truthful, polished public release — and the immediate post-release work that
turns a launch into a mod that grows.

## What Changes

**Pre-release gates (block v0.1.0 shipping):**
- Bundle 4 fonts (including Scapholene, requested by the teaser community) and add a font selector in
  Scribe Settings for task text. Button font TBD.
- Complete the 11 never-run Pin Tab in-game verifications (scribe-pin-editor 7.1–7.11) — the only
  major v1 surface that has never been playtested.
- Run the multiplayer test (A4) on a second machine: cross-session read sync, independent lecterns,
  editor lock, and reorder/settings persistence. A public commitment was made on reddit.
- Run the survival pass (A5): confirm the lectern is craftable and usable in a real survival world.
- Confirm the new grid recipe appears in the in-game Lectern handbook entry (add-lectern-recipe 3.2).
- Collect all third-party credits and update CREDITS: new fonts, JeanPierre (Wanderer's Sketchbook).
- Add a v3-blob codec test — the v3→v4 migration path exists but has zero test coverage; this is the
  one code path that can silently corrupt player saves on upgrade.
- Add a CHANGELOG.md stub with the v0.1.0 entry.
- Retest the sidebar nav buttons (923a395a — fix applied, awaiting one in-game retest).

**Post-release actions (not ship gates):**
- Refine the VS mod DB page after initial feedback.
- Reach out to the LibGUI author (courtesy / attribution).
- Capture B2 feature screenshots (HUD in-world, settings, notebook backdrop).
- Draft and post the reddit release post — must answer teaser thread questions: recipe, LibGUI dep,
  multiplayer confirmed, sound toggle, timers roadmap, Scapholene callout.
- Produce the 60–90s feature showcase video.
- Add Tab/Shift+Tab hotkey tooltips (discoverability gap raised in teaser comments).
- Run `/simplify` code quality pass.

## Capabilities

### New Capabilities
- `font-selector`: user-facing font choice for task text in Scribe Settings. Bundles Scapholene
  (and up to 3 other faces); exposes a dropdown in the Settings window. Button font is a separate
  open decision, tracked here.
- `v1-release-distribution`: CHANGELOG.md, updated CREDITS, v0.1.0 version freeze, and all
  in-game verifications that certify the "multiplayer-safe, survival-craftable" claims on the mod page.

### Modified Capabilities
- `settings-tab`: gains a font selector control (new dropdown row in the Window Appearance section).

## Impact

- **New files**: `CHANGELOG.md`, `src/Mod/assets/scribe/textures/fonts/scapholene.ttf` (+ up to 3
  additional TTFs), one new xUnit fixture in `tests/Core.Tests/`.
- **Modified files**: `CREDITS`, `src/Mod/ScribeSettingsContent.cs` (font selector control),
  `src/Mod/assets/scribe/lang/en.json` (font selector label), `TESTING.md` (Pin Tab items added),
  `src/Mod/ScribeModSystem.cs` or font-registration entry point (register new faces).
- **No Core changes**: font selection is a display preference, not a document-model concern.
  `ScribePlayerSettings` may gain a `TaskFontFamily` field (Mod-side only, persisted in
  `scribe:settings:v1` JSON, no codec version bump needed).
- **No new mod dependencies**: all fonts are bundled assets; the font selector uses the existing
  `ScribeNumericField` / LibGUI pattern.
