## Why

LibGUI 4.0.0 is published. It is the first release with public source and docs that match the shipped
binary (the local `reference/vslibgui` clone is stuck at 2.0.0), it fixes the Linux HarfBuzz crash upstream,
and it reworks repaint and hit-testing internals that every Scribe dialog and the HUD sit on. Scribe's hard
dependency is pinned to `gui` 3.1.0. We want to move to 4.0.0 first, as a deliberately minimal step, so
any behavioral difference is attributable to the upgrade alone before we adopt new features or clean up
code in follow-up changes.

## What Changes

- Raise the hard `gui` dependency from `3.1.0` to `4.0.0` in `modinfo.json`.
- Replace the vendored LibGUI managed DLLs in `src/Mod/lib/` with the 4.0.0 set and rebuild.
- Point the Atlas integration suite at `gui_4.0.0.zip` (csproj and `AssemblyInfo`).
- Update version references in docs (`README.md`, `src/Mod/lib/README.md`, `docs/libgui-reference.md`), and
  refresh the local `reference/vslibgui` clone to the 4.0.0 source so the version-skew warning can go.
- Playtest the upgraded build: every dialog and the HUD, with attention to the areas 4.0.0 changed
  (repaint boundaries, hover and hit testing, `WindowFrame` title bars, sounds, text shaping).
- No behavior changes, no feature adoption, no refactors, and no removal of existing workarounds
  (`ScribeHarfBuzzLoadFix`, the silent sound player, the focus traversal policy). Those are follow-ups.
- **BREAKING (dependency):** players need `gui` 4.0.0; the old 3.1.0 no longer satisfies the dependency.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. No spec-level behavior changes, so `skip_specs: true`.

## Impact

- `src/Mod/modinfo.json`, `src/Mod/lib/*.dll`, `tests/Integration.Tests/Integration.Tests.csproj`,
  `tests/Integration.Tests/AssemblyInfo.cs`, and the docs listed above.
- Expected build warnings from `[Obsolete]` `WindowFrame` constructors (warnings are not errors in this repo).
- Follow-up changes, not part of this one: adopt 4.0.0 features (title-bar styling, repaint boundaries for
  custom render objects, `UiSounds`), retire `ScribeHarfBuzzLoadFix` and resolve the
  `redirect-harfbuzz-to-system-lib` and `assess-libgui-decoupling` changes once Linux players confirm the
  upstream fix, revisit workarounds for 3.1.0 bugs, and clean up code.
