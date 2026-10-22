## 1. Map coverage

- [x] 1.1 Record in `design.md` which Atlas scenarios exercise each packet handler in `ScribeModSystem.Network.cs`; verify the list names every handler to be changed and marks the uncovered ones

## 2. Small helpers

- [x] 2.1 Add the shared `Clamp01` and replace the copies in `ScribeAmbientLightSampler`, `ScribeStamp`, `ScribeGlobalTint.Q`; verify build and identical results for 0, 0.5, 1, and out-of-range inputs via a small test
- [x] 2.2 Skipped: only three one-line lang ternaries exist, so a helper would save no lines (see design.md)

## 3. Network handlers (one commit per group)

- [ ] 3.1 Move `TryReadGuid` to a shared static and update `ScribeModSystem.Assignment.cs`; verify build
- [ ] 3.2 Add `TryReadDocTask` and convert the six doc+task handlers keeping each trace label; verify build and `PinScenarios`/`ServerAuthoritativeEditScenarios`
- [ ] 3.3 Add `WithStation` and convert the station handlers; verify build and `SingleEditorLockScenarios`/`ReadEditorAccessScenarios`
- [ ] 3.4 Add `SendToClient` and convert the two notebook-save echoes plus the manual-history handlers; verify build and Atlas

## 4. Verify

- [ ] 4.1 Run `build/verify.sh`; confirm Core and Atlas pass
- [ ] 4.2 Add manual smoke items to TESTING.md for any uncovered handler (pin, complete, edit, delete, tracker count) before archiving
