## 1. Core: ExtraInfo field and codecs

- [x] 1.1 Add `ScribeBlock.ExtraInfo` (nullable string, kind-agnostic, defaults null) and verify existing `Core.Tests` for `ScribeBlock` still pass unchanged
- [x] 1.2 Persist `ExtraInfo` in `ScribeDocumentCodec` (presence-flag + string, bump the binary version like `LinkDescription`'s v11) and verify a round-trip unit test (write then read) preserves it, and that a pre-bump saved blob still deserializes with `ExtraInfo` defaulting to null
- [x] 1.3 Persist `ExtraInfo` in `ScribeDocumentJsonCodec` (new optional `extraInfo` key, omitted when null, bump `Version`) and verify a round-trip unit test preserves it and that an older-version payload still imports with `ExtraInfo` null
- [x] 1.4 Add a pure Core helper that maps `(title, bodyText, extraInfo)` into the correct block shape (title+optional nested note; promoted standalone note when title is absent; `ExtraInfo` always on the resulting Depth-0 block) with clipping to the existing per-kind length caps, and verify unit tests cover all four combinations (title+body, title only, body only, both empty) plus an over-length input case

## 2. Core: last-opened-item tracking data

- [x] 2.1 Confirm no Core changes are needed here (the tracker is server-side, in-memory, Mod-layer-only) — verify by checking the design's Decisions section still holds before starting section 4

## 3. Mod: hover-info icon and row wiring

- [x] 3.1 Add a new generic hover-info icon widget (parallel to, not derived from, `ScribeAssignedTaskIcon`) that renders only when a row's `ExtraInfo` is non-empty, showing the raw string as tooltip content, and verify it visually via the running game (Read view) for a hand-crafted test block with `ExtraInfo` set
- [x] 3.2 Thread `ExtraInfo` through the Read view and Editor row data/builders the same way `IsAcceptedAssignment` is threaded, and verify the icon appears on both surfaces for a block carrying `ExtraInfo`, and does not appear when it's null
- [x] 3.3 Add `ExtraInfo`-derived fields to `ScribePinnedRef`, bump the pin codec version, and verify a round-trip unit test preserves them across pin serialize/deserialize
- [x] 3.4 Thread the pinned `ExtraInfo` snapshot through `ScribePinnedContent`/`ScribePinRow` (Pin Tab) and verify the icon appears there for a pinned task carrying `ExtraInfo`
- [x] 3.5 Verify `HudScribePins.cs` requires no changes and confirm in-game that a pinned task carrying `ExtraInfo` shows the icon in the Pin Tab but not in the HUD

## 4. Mod: server-side last-opened-item tracker

- [x] 4.1 Add a per-player `Dictionary<string, Guid>` on the server-side `ScribeModSystem` and populate it inside the existing `OnServerReceivedNotebookOpened` handler, and verify via a log/breakpoint check (or an Atlas integration scenario) that opening any of the three Scribe item types updates the tracked DocId for that player
- [x] 4.2 Verify the tracker is never persisted to the save file and starts empty on server boot (in-memory only, matching the client-side field's lifetime)

## 5. Mod: target resolution and the public method

- [x] 5.1 Add a server-side target-resolution helper mirroring `ResolveWriteableCarriedSlot`'s logic (prefer the tracked last-opened writeable carried Scribe item, else the first writeable one, else null) adapted for `IServerPlayer`, and verify unit/integration coverage for: last-opened carried and writeable; last-opened not carried but another writeable item is; only locked items carried; no Scribe item carried
- [x] 5.2 Add `TryCreateExternalTask(IServerPlayer player, string? title, string? bodyText, string? extraInfo) -> bool` on `ScribeModSystem`, wiring the Core mapping helper (1.4) and the target resolver (5.1), applying the result through the same save/sync path a normal edit uses (`ToTreeAttributes`/`MarkDirty`), and verify an integration scenario for the title+body+extraInfo success path
- [x] 5.3 Add the server→client failure-notice path for the three known failure reasons (no Scribe item, all locked, target full) and verify each reason is distinguishable in an integration scenario (or manual in-game check) by the message the target player receives
- [x] 5.4 Verify capacity enforcement: calling against a target document already at its task cap returns `false` and triggers the "target full" notice rather than silently truncating or overflowing

## 6. Verification and documentation

- [x] 6.1 Add an Atlas integration scenario exercising `TryCreateExternalTask` end-to-end (title-only, title+body, body-only/no-title, with and without `extraInfo`) against a real player/item, verified by running the local Atlas suite
- [x] 6.2 Update `CHANGELOG.md` with the new public API surface
- [x] 6.3 Run the full `dotnet test` (Core suite) and confirm all tests pass before marking this change ready to archive
