## Context

`ScribeDocumentCodec` currently holds the accepted-version window as two magic constants
(`Version = 5`, `PriorVersion = 4`) and a single `bool isCurrent` branch in `TryDeserialize`.
This is fine for one version of history, but it will not scale cleanly as future tiers bump the
version (v6 for signatures, chronicle for timestamps). Each bump currently silently removes the
oldest accepted version; there is no documented upgrade chain, no explicit "what changes between
version N and N+1", and the one existing migration step (v4→v5: supply `DefaultTitle`) is
inlined as an afterthought rather than a named operation.

`ScribePinCodec` has a similar structure (`PinVersion = 1`, no prior version constant, no named
migration steps) — it hasn't needed one yet, but setting the pattern now is cheaper than
retrofitting it when the first pin-format change arrives.

## Goals / Non-Goals

**Goals:**
- Refactor the v4→v5 document upgrade into a named, documented migration step that is the
  pattern for all future steps.
- Add a per-version accepted-range table to both codecs' doc-comments so the window is visible
  in one place.
- Add `ScribePinCodec` migration scaffolding (accepted-range constant and migration helper
  skeleton) so the next pin-format bump has a clear home.
- Cover each accepted prior version with a dedicated "reads an older blob" unit test that
  asserts the exact post-migration field values (not just that the read succeeds).
- Write `docs/CODEC-MIGRATION.md`: a short how-to reference for the next developer adding a
  version, covering the append-only rule, the window update, and the migration-step pattern.

**Non-Goals:**
- Widening the accepted-version window beyond the current one-prior-version policy. The window
  stays at current + one prior; the scaffold makes the *structure* of that window explicit, not
  larger.
- Changing the wire format or bumping `Version` / `PinVersion`. This is pure refactor + tests.
- Adding migration logic to `ScribePinCodec` beyond the scaffold (no pin-format fields are
  changing here).
- Any `src/Mod/` or game-API changes. Pure `src/Core/` + `tests/Core.Tests/`.

## Decisions

### Named migration steps over inline branching

**Decision:** Extract each version upgrade into a private static method
(`MigrateV4ToV5(ref string title)` or similar) that is called inside `TryDeserialize` at the
appropriate branch. The method's name and doc-comment state which fields were added/changed in
that version.

**Alternatives considered:**
- Keep the `isCurrent` inline branch — works fine for one migration step, but as steps
  accumulate the `TryDeserialize` body becomes a vertical list of ad-hoc branches with no
  discoverable structure.
- A migration-table dispatch (`Dictionary<byte, Action<...>>`) — over-engineered for a codec
  that only accepts two versions at a time and changes very rarely.

### Accepted-version table in doc-comment, not in code

**Decision:** Document the accepted range as a small table in the codec class doc-comment
(current version, accepted prior versions, rationale) rather than as a runtime data structure.
The codec already checks version at the top of `TryDeserialize`; the table is for humans, not
the runtime.

### One-prior-version window unchanged

**Decision:** Keep accepting exactly `Version` and `Version - 1`. Do not widen the window. A
player who skips more than one release can still open their world in the intermediate version
to migrate forward; widening the window would add reader complexity for a scenario that has
not been requested. If this policy needs to change, `docs/CODEC-MIGRATION.md` will be the place
to update the rationale.

### `docs/CODEC-MIGRATION.md` instead of inline comments only

**Decision:** A short standalone doc (not a large wiki page) that a developer reads in 2
minutes before adding a new version. The doc-comment in each codec file links to it.

## Risks / Trade-offs

- **Risk:** The refactor accidentally changes read behavior for v4 bytes.
  → **Mitigation:** The existing `TryDeserialize_V4Bytes_Succeeds_AndSurfacesNoLegacyPinnedIds`
  and `TryDeserialize_V4Bytes_SuppliesDefaultTitle` tests cover the v4 path already; the new
  "older blob" fixture tests add explicit field-value assertions on top. Run `dotnet test` before
  and after; both must pass.

- **Risk:** Future developers add a new version without reading the migration doc and bypass the
  pattern.
  → **Mitigation:** The doc-comment in `ScribeDocumentCodec` links to `CODEC-MIGRATION.md` and
  states the pattern. The CLAUDE.md guardrail ("check VSAPI-NOTES.md first") and the OpenSpec
  spec-driven process mean the next version bump will produce a proposal that references this
  change.

## Migration Plan

This change is purely additive to `src/Core/` and `tests/Core.Tests/`. No game data is
touched, no wire format changes, no `src/Mod/` changes. There is nothing to deploy or roll
back beyond the normal PR + tag flow. The test suite (`dotnet test`) is the only gate.
