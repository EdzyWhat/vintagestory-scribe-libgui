## Why

`ScribeModSystem.Network.cs` repeats the same guards in about 16 handlers (null `sapi`, resolve the host station,
read two GUIDs and log a malformed packet), and a few small helpers exist in several copies (`Clamp01` three times,
`HasTranslation ? Get : fallback` four times). Collapsing them makes each handler show only what is specific to it.

## What Changes

- Add private helpers in the `ScribeModSystem` network partial: `WithStation`, `TryReadDocTask` (keeping each
  handler's existing trace text), and `SendToClient`; move `TryReadGuid` to a shared static.
- Replace the duplicated `Clamp01` in `ScribeAmbientLightSampler`, `ScribeStamp` and the inline clamp in
  `ScribeGlobalTint.Q` with one helper.
- Add `ScribeLang.GetOrNull`/`GetOr` for the translate-or-fallback pattern; `HistoryDisplay.HasKey` keeps its own
  flags (`findWildcarded:false`, `logErrors:false`) as parameters.
- Leave the float-to-colour rounding differences between `ClientPrefs` and `GlobalTint` alone.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. Handler behavior and log text are preserved (`skip_specs: true`).

## Impact

`src/Mod/ScribeModSystem.Network.cs` and `.Assignment.cs`, the four small helper sites, `ScribeQuestCatalog`,
`ScribeProgressionFrameworkQuestCatalog`, `HistoryDisplay`. About 100 lines removed.
