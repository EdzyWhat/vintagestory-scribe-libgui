## 1. Refactor ScribeDocumentCodec

- [x] 1.1 Replace the inline `bool isCurrent` branch in `TryDeserialize` with a call to a new
      private static method `ApplyV4ToV5Migrations(ref string title)` that sets `title =
      ScribeDocument.DefaultTitle` when the v4 path is taken. The method should be located below
      `TryDeserialize` with a doc-comment naming the fields introduced in v5.
- [x] 1.2 Update the class-level XML doc-comment to include an explicit accepted-version table:
      current version (5), accepted prior version (4), and what each transition added. The
      existing "Field history" block is a good base — convert it into a proper accepted-version
      table as described in the design.
- [x] 1.3 Add a comment in `TryDeserialize` at the version-check branch pointing to the
      accepted-version table in the doc-comment and to `docs/CODEC-MIGRATION.md`.

## 2. Scaffold ScribePinCodec

- [x] 2.1 Add a `private const byte PriorPinVersion = PinVersion;` constant below `PinVersion`
      in `ScribePinCodec`, with a comment noting that it equals `PinVersion` until a pin-format
      change occurs (and what to update when one does).
- [x] 2.2 Add a private static `ApplyPinMigrations` helper stub (currently a no-op since
      `PriorPinVersion == PinVersion`) called from the version branch in both
      `TryDeserializeList` and `TryDeserializeStore`. The stub should have a doc-comment
      explaining that it is the home for future pin-version migration steps.

## 3. Add older-blob unit tests

- [x] 3.1 In `ScribeDocumentCodecTests`, add
      `TryDeserialize_V4Bytes_MigratesTitle_ToDefault` — a test that hand-builds a valid v4
      byte array (magic + version=4 + DocId + blockCount=1 + one task block, no title field) and
      asserts `restored.Title == ScribeDocument.DefaultTitle`. This is a stronger assertion than
      the existing `TryDeserialize_V4Bytes_SuppliesDefaultTitle` (which already checks this) —
      verify whether the existing test is sufficient or needs augmenting to match the new spec
      requirement; if it already asserts the field value, note it as covering the scenario and
      skip adding a duplicate.
      **NOTE: `TryDeserialize_V4Bytes_SuppliesDefaultTitle` (line 333) already asserts
      `Assert.Equal(ScribeDocument.DefaultTitle, restored!.Title)` — field-value assertion is
      already present. No duplicate test added.**
- [x] 3.2 Verify that all pre-existing codec tests still pass: `dotnet test tests/Core.Tests`.
      No test should need to change — the refactor is behavior-preserving.
      **RESULT: 158 passed, 0 failed.**

## 4. Write docs/CODEC-MIGRATION.md

- [x] 4.1 Create `docs/CODEC-MIGRATION.md` covering: (a) the append-only version discipline
      (never reorder fields, never two transitions at the same version number), (b) how to
      update the accepted-version window when bumping `Version` (update `PriorVersion`,
      add the new `ApplyVNToVN+1Migrations` method, update the doc-comment table), (c) the
      named-migration-step pattern with the v4→v5 title step as the worked example, and (d) a
      reminder to add a dedicated older-blob unit test for each accepted prior version.
- [x] 4.2 Add a `/// See docs/CODEC-MIGRATION.md for the migration step pattern.` line to the
      class doc-comments of both `ScribeDocumentCodec` and `ScribePinCodec`.

## 5. Final verification

- [x] 5.1 Run `dotnet test` — confirm 0 failures across all test projects.
      **RESULT: 158 passed, 0 failed.**
- [x] 5.2 Read through `ScribeDocumentCodec.TryDeserialize` end-to-end and confirm no
      `isCurrent` branch remains; the only version-specific logic lives in named migration
      methods.
      **CONFIRMED: `isCurrent` is gone; `ApplyV4ToV5Migrations` is the sole upgrade home.**
- [x] 5.3 Confirm `docs/CODEC-MIGRATION.md` exists, links correctly from both codec files, and
      contains all four sections listed in task 4.1.
      **CONFIRMED: file exists; both codecs link it; all four sections present.**
