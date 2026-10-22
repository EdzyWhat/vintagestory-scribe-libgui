## 1. Lock the format

- [x] 1.1 Add golden-byte tests that serialize a fixed sample store with each codec and assert the exact bytes (including magic and version); verify they pass on the current code before any refactor

## 2. Extract the helper

- [x] 2.1 Add the shared Core helper (header read/write, version range, player-count and uid guards, exception filter, single `MaxPlayers`); verify `src/Core` has no `Vintagestory` reference
- [x] 2.2 Switch `ScribePinCodec` to the helper, keeping the version passed to its list reader; verify `ScribePinCodecTests` and the golden test pass
- [x] 2.3 Switch `ScribeQuestDecisionCodec` to the helper; verify `ScribeQuestDecisionTests` and the golden test pass
- [x] 2.4 Delete the duplicated `ReadHeader` and `MaxPlayers`; verify with grep and a full Core test run

## 3. Verify

- [x] 3.1 Run `build/verify.sh`; confirm Core and Atlas (`PersistenceScenarios`, `PinScenarios`, `MigrationScenarios`) pass
