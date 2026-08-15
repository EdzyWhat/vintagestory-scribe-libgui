# Codec Migration Guide

How to add a new version to `ScribeDocumentCodec` or `ScribePinCodec` without breaking
existing player saves.

## The append-only rule

Both codecs use a single monotonically-increasing version byte (`Version` / `PinVersion`).
Every new field appends to the *end* of the serialized layout — never reorders, never
inserts mid-stream, never recycles a version number. The field history in each codec's
class doc-comment is the authoritative record; keep it current.

Concrete rules:
- `Version` is always the **current** version and is written by `Serialize`.
- `PriorVersion` / `PriorPinVersion` is the one version the reader accepts *in addition* to
  current. Update it whenever you bump `Version`.
- Any byte array with a version number older than `PriorVersion` is **rejected** (return
  `false`). There is no "skip a release" path; a player who skips two releases can open
  their save with the intermediate version first to migrate forward one step at a time.

## The accepted-version window

Each codec class doc-comment contains an **Accepted-version table**:

```
/// Accepted-version window:
///   Current : vN   — description of what N added
///   Prior   : vN-1 — migrated by ApplyV(N-1)ToV(N)Migrations
///   Older   : rejected
```

Keep this table current whenever you bump the version.

## The named-migration-step pattern

When bytes written in version `N-1` are read, a private static method named
`ApplyV(N-1)ToVNMigrations` is responsible for all upgrade logic that converts the older
layout into the current schema. The method is called inside `TryDeserialize` after the
prior-version branch has supplied placeholder values for the new fields.

This pattern keeps all version-specific defaulting in one named, discoverable place rather
than scattered across inline `if (version == Current)` branches.

### Worked example: v4 → v5 (`ScribeDocumentCodec`)

v5 appended a `Title` string after the block list. v4 has no such field.

**What changed in the serialization format:**
```
// v4 layout:  magic | version | DocId | blockCount | [blocks...]
// v5 layout:  magic | version | DocId | blockCount | [blocks...] | title
```

**How the migration works:**

```csharp
// In TryDeserialize:
string title = version == Version
    ? r.ReadString()          // v5: read from stream
    : ScribeDocument.DefaultTitle;   // v4: placeholder
ApplyV4ToV5Migrations(version, ref title);

// The named migration method:
private static void ApplyV4ToV5Migrations(byte version, ref string title)
{
    if (version != PriorVersion) return;
    // v4 has no title field; ensure we have the default (already set above, but
    // this method is the single documented home for v4→v5 upgrade logic).
    if (string.IsNullOrWhiteSpace(title)) title = ScribeDocument.DefaultTitle;
}
```

### Worked example: v5 → v6 (`ScribeDocumentCodec`)

v6 appended **four per-block fields** after each block's `text`, for the Tracker and Link task
kinds: `TargetItemCode` (string?), `TargetQuantity` (int), `CurrentQuantity` (int), and
`LinkTarget` (string?). v5 blocks have none of these.

**What changed in the serialization format:**
```
// v5 per-block: TaskId | kind | done | depth | hasAssignedToUid | [assignedToUid] | text
// v6 per-block: ...v5... | hasTargetItemCode | [targetItemCode] | targetQuantity
//               | currentQuantity | hasLinkTarget | [linkTarget]
```
(The document `title` still follows the block list, unchanged — both v5 and v6 have it, so it is
now read unconditionally for any accepted version.)

**How the migration works** — because the new fields are per-block, the named step supplies their
defaults via `out` params inside the block-read loop rather than a single `ref`:

```csharp
// In TryDeserialize's per-block loop, after reading text:
string? targetItemCode; int targetQuantity; int currentQuantity; string? linkTarget;
if (version == Version)   // v6: read the appended fields from the stream
{
    bool hasTargetItemCode = r.ReadBoolean();
    targetItemCode = hasTargetItemCode ? r.ReadString() : null;
    targetQuantity = r.ReadInt32();
    currentQuantity = r.ReadInt32();
    bool hasLinkTarget = r.ReadBoolean();
    linkTarget = hasLinkTarget ? r.ReadString() : null;
}
else                      // v5: no such fields — default them
{
    ApplyV5ToV6Migrations(out targetItemCode, out targetQuantity, out currentQuantity, out linkTarget);
}

// The named migration method:
private static void ApplyV5ToV6Migrations(out string? targetItemCode, out int targetQuantity,
    out int currentQuantity, out string? linkTarget)
{
    targetItemCode = null;
    targetQuantity = 1;   // satisfies the ScribeBlock TargetQuantity ≥ 1 invariant
    currentQuantity = 0;
    linkTarget = null;
}
```

Note v4 drops out of the accepted window on this bump (window is now `{v6, v5}`); a well-formed v4
payload is rejected, covered by `TryDeserialize_V4Bytes_FailsSafely`.

## How to add a new version (step-by-step)

1. **Add your new field(s) to `Serialize`**, appending them after all existing fields.

2. **Bump `Version`** (or `PinVersion`). Update `PriorVersion` to the old `Version` value.

3. **Update `TryDeserialize`:**
   - After the `version != Version && version != PriorVersion` check, handle the new
     field: read it from the stream for current-version bytes, supply a default for
     prior-version bytes.
   - Call a new `ApplyV(N-1)ToVNMigrations(version, ref <fields>)` method.

4. **Write the migration method** following the pattern above. The method should be a
   no-op for current-version bytes and apply defaults for prior-version bytes.

5. **Update the accepted-version table** in the class doc-comment.

6. **Update the field history** in the class doc-comment.

7. **Add a dedicated older-blob unit test** in `ScribeDocumentCodecTests` (or
   `ScribePinCodecTests`) that:
   - Hand-builds a byte array in exactly the prior format (no helper — explicit bytes
     make the format self-documenting in the test)
   - Asserts the **specific migrated field values** (e.g. `restored.Title == DefaultTitle`),
     not merely that `TryDeserialize` returns `true`

   Test name pattern: `TryDeserialize_V<N>Bytes_<FieldName>_IsUpgraded`

## Current state

| Codec | Current | Prior | Migration method |
|---|---|---|---|
| `ScribeDocumentCodec` | v6 | v5 | `ApplyV5ToV6Migrations` — defaults Tracker/Link per-block fields |
| `ScribePinCodec` | v1 | v1 (no change yet) | `ApplyPinMigrations` — no-op stub |
