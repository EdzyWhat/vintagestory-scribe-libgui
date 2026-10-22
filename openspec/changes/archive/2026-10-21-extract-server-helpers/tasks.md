## 1. Map coverage

- [x] 1.1 Record in `design.md` which Atlas scenarios exercise each packet handler in `ScribeModSystem.Network.cs`; verify the list names every handler to be changed and marks the uncovered ones

## 2. Small helpers

- [x] 2.1 Add the shared `Clamp01` and replace the copies in `ScribeAmbientLightSampler`, `ScribeStamp`, `ScribeGlobalTint.Q`; verify build and identical results for 0, 0.5, 1, and out-of-range inputs via a small test
- [x] 2.2 Skipped: only three one-line lang ternaries exist, so a helper would save no lines (see design.md)

## 3. Network handlers (tests first)

- [x] 3.1 Make the `OnServerReceived*` handlers internal and add `ServerPacketHandlerScenarios` (valid, malformed and unknown-document packets); verify the ten scenarios pass and a mutated handler (disabled call, swapped ids) fails them
- [x] 3.2 Add `TryReadDocTask` and convert the six doc+task handlers keeping each trace label; verify build and the packet scenarios
- [x] 3.3 Add `ResolveStation` and convert the five station handlers; verify build and the packet scenarios
- [x] 3.4 Dropped after inspection: the two notebook-save echoes carry different fields, so a `SendToClient` helper would not remove real duplication; and `TryReadGuid` is already shared across the `ScribeModSystem` partials (see design.md)

## 4. Verify

- [x] 4.1 Run `build/verify.sh`; confirm Core and Atlas pass
