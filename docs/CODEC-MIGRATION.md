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

### Worked example: v6 → v7 (`ScribeDocumentCodec`) — the progressive-read departure

v7 appended **one per-block field** after each block's `LinkTarget`: `LinkLabel` (string?), the captured
display title for a **guide-page Link** (a `page:`-prefixed target that has no item to resolve a name from —
add-tracker-link-tasks 7.6). v6 blocks have no such field.

**Why this bump abandons the strict two-version window** — the same reasoning as the pin codec's v2→v3
switch below. v6 shipped **only on the WIP branch**, but **v5 is shipped and live in real player saves**
(v1.1.1). A naive `{v7, v6}` two-version window would have *rejected every shipped v5 document* — silent
data loss on upgrade. So `ScribeDocumentCodec` switched to **progressive append-only reads**: it accepts any
version in `[MinVersion, Version]` (`[5, 7]` at this bump; later `[5, 8]` — see Current state) and reads each later version's trailing fields only behind a
`version >=` threshold.

**What changed in the serialization format:**
```
// v5 per-block: TaskId | kind | done | depth | hasAssignedToUid | [assignedToUid] | text
// v6 per-block: ...v5... | hasTargetItemCode | [targetItemCode] | targetQuantity
//               | currentQuantity | hasLinkTarget | [linkTarget]
// v7 per-block: ...v6... | hasLinkLabel | [linkLabel]
```

**How the migration works** — the version gate is a *range*, and each version-group's fields are read behind
a `>=` threshold, so a v5 blob stops before the v6 fields and a v6 blob stops before the v7 field. Pre-v6
defaults (now including `linkLabel`) are seeded once up front:

```csharp
private const byte MinVersion = 5;   // oldest accepted (shipped) — replaces PriorVersion
// private const byte Version = 7;   // current

// Version gate:
if (version < MinVersion || version > Version) return false;

// In TryDeserialize's per-block loop, after reading text:
ApplyPreV6Defaults(out string? targetItemCode, out int targetQuantity,
    out int currentQuantity, out string? linkTarget, out string? linkLabel);
if (version >= 6)
{
    bool hasTargetItemCode = r.ReadBoolean();
    targetItemCode = hasTargetItemCode ? r.ReadString() : null;
    targetQuantity = r.ReadInt32();
    currentQuantity = r.ReadInt32();
    bool hasLinkTarget = r.ReadBoolean();
    linkTarget = hasLinkTarget ? r.ReadString() : null;
}
if (version >= 7)
{
    bool hasLinkLabel = r.ReadBoolean();
    linkLabel = hasLinkLabel ? r.ReadString() : null;
}
```

`ApplyV5ToV6Migrations` was renamed **`ApplyPreV6Defaults`** to reflect that it seeds defaults for *every*
field a pre-current block may lack (now the v6 tracker/link group **and** the v7 `linkLabel`), running
unconditionally before the version-gated reads overwrite whatever the blob carried.

Covered by `RoundTrip_PreservesTrackerAndLinkFields` (v7 round-trip of a guide-page Link asserting `LinkLabel`
survives, and an item Link asserting it stays null) and `TryDeserialize_V6Bytes_Succeeds_AndDefaultsLinkLabel`
(hand-built v6 blob → asserts `LinkLabel` defaults to null while the tracker/link fields still round-trip).
The existing v5 test still passes (progressive reads keep v5 accepted).

### Worked example: v7 → v8 (`ScribeDocumentCodec`)

v8 appended **one per-block field** after each block's `LinkLabel`: `RecipeSignature` (a plain string; empty when none), the grid-recipe binding of a **Craft** task (kind 4). v7 blocks have no such field. The codec already uses progressive reads, so this bump just extends the range to `[5, 8]` and adds one more `version >=` group.

Unlike the earlier Tracker/Link fields, `RecipeSignature` is **always written** from v8 (empty string for non-Craft blocks), so it is a plain string — not a has/value pair.

**What changed in the serialization format:**
```
// v7 per-block: ...v6... | hasLinkLabel | [linkLabel]
// v8 per-block: ...v7... | recipeSignature   (always present; empty when none)
```

**How the migration works** — one more threshold-gated read; `ApplyPreV6Defaults` also seeds `recipeSignature` to `""`:

```csharp
// private const byte Version = 8;   // current  (MinVersion stays 5)

// In TryDeserialize's per-block loop, after the v7 LinkLabel group:
if (version >= 8)
{
    recipeSignature = r.ReadString(); // always written from v8; empty when none
}
```

Covered by `RoundTrip_PreservesTrackerAndLinkFields` (v8 round-trip) and
`TryDeserialize_V7Bytes_Succeeds_AndDefaultsRecipeSignature` (hand-built v7 blob → asserts
`RecipeSignature` defaults to empty while the v7 `LinkLabel` still round-trips). The existing
v5/v6 tests still pass (progressive reads keep them accepted).

### Worked example: v8 → v9 (`ScribeDocumentCodec`)

v9 appends an optional rich assignment record after `RecipeSignature`: assigner UID, state,
assigned date, and the unseen/seen flag. The retired v5-v8 bare-UID slot remains unpopulated by
new writers, preserving the append-only layout while allowing old saves to load.

```text
// v8 per-block: ...v7... | recipeSignature
// v9 per-block: ...v8... | hasAssignment | [assignerUid | state | assignedDate | seen]
```

Pre-v9 documents default to no assignment. Assignment data is intentionally omitted from JSON/TSV
clipboard exports because it is place-bound and must not be shared by import.

### Worked example: v9 → v10 (`ScribeDocumentCodec`)

v10 appends one more field to the assignment record added in v9: `TargetPlayerUid` (the recipient),
written right after `Seen`. Needed once a separate `ScribeAssignmentStore` had to filter "what did I
send" vs. "what did I receive" without relying on which dictionary a record happened to be filed
under — the block itself now names both parties.

```text
// v9  per-block assignment: hasAssignment | [assignerUid | state | assignedDate | seen]
// v10 per-block assignment: hasAssignment | [assignerUid | state | assignedDate | seen | targetPlayerUid]
```

v9 never shipped (this is a same-cycle addition within the same in-progress change), so there is no
real migration gap to bridge — a `version >= 10` read gate exists anyway, defaulting a hypothetical
v9-only blob's `TargetPlayerUid` to `""`, for consistency with every other version-gated field.

### Worked example: v1 → v2 (`ScribePinCodec`)

v2 appended **two per-pin fields** after each pin's `LastKnownText`, so the HUD can treat a pinned
Link as a Handbook hyperlink even when the source document is unloaded (add-tracker-link-tasks 5.5):
`Kind` (the `ScribeBlockKind` byte) and `LinkTarget` (a nullable string). v1 pins have neither.

**What changed in the serialization format:**
```
// v1 per-pin: OwnerDocId | TaskId | PinnedAtTotalHours | Orphaned | LastKnownDone | LastKnownText
// v2 per-pin: ...v1... | kind | hasLinkTarget | [linkTarget]
```

**How the migration works** — because the version isn't known inside the shared `TryReadPinList` loop,
the reader is passed the parsed `version` and reads the appended fields only for current-version bytes,
else calls the named step to default them:

```csharp
// In TryReadPinList's per-pin loop, after reading LastKnownText:
if (version == PinVersion)   // v2: read the appended fields
{
    pin.Kind = (ScribeBlockKind)r.ReadByte();
    bool hasLinkTarget = r.ReadBoolean();
    if (hasLinkTarget) pin.LinkTarget = r.ReadString();
}
else                         // v1: no such fields — default them
{
    ApplyV1ToV2Migrations(out var kind, out var linkTarget);
    pin.Kind = kind;
    pin.LinkTarget = linkTarget;
}

// The named migration method:
private static void ApplyV1ToV2Migrations(out ScribeBlockKind kind, out string? linkTarget)
{
    kind = ScribeBlockKind.Task;   // every pre-v2 pin reads as an ordinary Task…
    linkTarget = null;             // …with no link target (Tracker/Link pins only exist from v2 on)
}
```

Covered by `TryDeserialize_V1Bytes_KindAndLinkTarget_AreUpgraded` (hand-built v1 bytes → asserts the
defaults) and `List_RoundTrip_PreservesKindAndLinkTarget` (v2 round-trip of Link/Tracker/Task pins).

### Worked example: v2 → v3 (`ScribePinCodec`) — the progressive-read departure

v3 appended **three per-pin fields** after each pin's `LinkTarget`, so the HUD and Pin Tab can render a
pinned **Tracker** item-shaped (icon + name + a have/need counter) even when its source document is
unloaded (add-tracker-link-tasks 7.9): `TargetItemCode` (string?), `TargetQuantity` (int), and
`CurrentQuantity` (int).

**Why this bump abandons the strict two-version window.** The `PriorPinVersion` rule above accepts exactly
*current + one prior* and rejects anything older. That was safe for v1→v2 because **no v2 pin had ever
shipped** (v2 lived only on the WIP branch). But **v1 pins are shipped and live in real player saves.** A
naive v3 bump with a `{v3, v2}` window would have *rejected every shipped v1 pin* — silent data loss the
moment a player upgraded. So `ScribePinCodec` switched from a two-version window to **progressive
append-only reads**: it accepts *any* version in `[MinPinVersion, PinVersion]` (`[1, 3]`) and reads each
later version's trailing fields only when `version >=` that field-group's threshold.

**What changed in the serialization format:**
```
// v1 per-pin: OwnerDocId | TaskId | PinnedAtTotalHours | Orphaned | LastKnownDone | LastKnownText
// v2 per-pin: ...v1... | kind | hasLinkTarget | [linkTarget]
// v3 per-pin: ...v2... | hasTargetItemCode | [targetItemCode] | targetQuantity | currentQuantity
```

**How the migration works** — the version gate is a *range*, not an equality pair, and each version's
fields are read behind a `>=` threshold so a v1 or v2 blob simply stops reading before the fields it never
wrote. Pre-v2 defaults are seeded once up front:

```csharp
private const byte MinPinVersion = 1;   // oldest accepted (shipped) — replaces PriorPinVersion
// public const byte PinVersion = 3;    // current

// Version gate (both TryDeserializeList and TryDeserializeStore):
if (version < MinPinVersion || version > PinVersion) return false;

// In TryReadPinList's per-pin loop, after reading LastKnownText:
ApplyPreV2Defaults(pin);              // Kind→Task, LinkTarget→null, TargetItemCode→null, qty defaults
if (version >= 2)
{
    pin.Kind = (ScribeBlockKind)r.ReadByte();
    if (r.ReadBoolean()) pin.LinkTarget = r.ReadString();
}
if (version >= 3)
{
    if (r.ReadBoolean()) pin.TargetItemCode = r.ReadString();
    pin.TargetQuantity = r.ReadInt32();
    pin.CurrentQuantity = r.ReadInt32();
}
```

`ApplyV1ToV2Migrations` was renamed **`ApplyPreV2Defaults`** to reflect that it now seeds defaults for
*every* field a pre-current pin may lack (not just the v2 pair), and it runs unconditionally before the
version-gated reads overwrite whatever the blob actually carried.

Covered by `List_RoundTrip_PreservesTrackerFields` (v3 round-trip) and
`TryDeserialize_V2Bytes_TrackerFields_AreDefaulted` (hand-built v2 blob → asserts Kind/LinkTarget round-trip
**and** the three tracker fields default). The existing v1 test still passes (progressive reads keep v1
accepted), now also asserting the tracker fields default.

**When to prefer progressive reads over the two-version window:** whenever an *older-than-prior* version is
still live in shipped saves. The two-version window is fine only when you can prove every intermediate
version was never released (as with the WIP-only v2). When in doubt, progressive reads are strictly safer —
they never drop a payload the code can still parse.

### Worked example: v3 → v4 (`ScribePinCodec`)

v4 appended **one per-pin field** after each pin's `CurrentQuantity`: `LinkLabel` (string?), so a pinned
**guide-page Link** renders its captured title even when the source document is unloaded and there is no item
to resolve a name from (add-tracker-link-tasks 7.6). This is the pin-side mirror of the doc codec's v6→v7
`LinkLabel`. The codec already uses progressive reads, so this bump just extends the range to `[1, 4]` and
adds one more `version >=` group.

**What changed in the serialization format:**
```
// v3 per-pin: ...v2... | hasTargetItemCode | [targetItemCode] | targetQuantity | currentQuantity
// v4 per-pin: ...v3... | hasLinkLabel | [linkLabel]
```

**How the migration works** — one more threshold-gated read; `ApplyPreV2Defaults` also seeds `LinkLabel`:

```csharp
// private const byte PinVersion = 4;    // current  (MinPinVersion stays 1)

// In TryReadPinList's per-pin loop, after the v3 tracker group:
if (version >= 4)
{
    if (r.ReadBoolean())
    {
        string linkLabel = r.ReadString();
        if (linkLabel.Length > ScribeDocumentCodec.MaxTextLength) return false; // bound a hostile blob
        pin.LinkLabel = linkLabel;
    }
}
```

Covered by `List_RoundTrip_PreservesLinkLabel` (v4 round-trip) and
`TryDeserialize_V3Bytes_LinkLabel_IsDefaulted` (hand-built v3 blob → asserts `LinkLabel` defaults to null).
The existing v1/v2 tests still pass.

### Worked example: v4 → v5 (`ScribePinCodec`)

v5 appended **one per-pin field** after each pin's `LinkLabel`: `Depth` (int), the pinned task's subtask
depth, so the HUD and Pin Tab indent a pinned subtask like the other surfaces (add-crafting-tasks /
task-subtasks 5.1). The codec already uses progressive reads, so this bump just extends the range to
`[1, 5]` and adds one more `version >=` group. `ApplyPreV2Defaults` seeds `Depth` to `0`.

**What changed in the serialization format:**
```
// v4 per-pin: ...v3... | hasLinkLabel | [linkLabel]
// v5 per-pin: ...v4... | depth
```

**How the migration works** — one more threshold-gated read, clamped to the one-level subtask contract:

```csharp
// private const byte PinVersion = 5;    // current  (MinPinVersion stays 1)

// In TryReadPinList's per-pin loop, after the v4 LinkLabel group:
if (version >= 5)
{
    // Clamp on read to the one-level subtask contract, matching ScribeBlock.Depth, so a
    // malformed/hostile blob can't smuggle a depth-2+ pin past the reader.
    pin.Depth = Math.Clamp(r.ReadInt32(), 0, 1);
}
```

A pre-v5 pin simply never wrote `Depth` and reads as a top-level row (`Depth = 0`).

### Worked example: v5 → v6 (`ScribePinCodec`)

v6 appended **one per-pin field** after each pin's `Depth`: `IsAcceptedAssignment` (bool), so the HUD and
Pin Tab can render the leading-icon assignment marker without resolving the (possibly unloaded) source
document (add-assignment-and-quest-support 9.3). Same pattern as v4→v5: extend the range to `[1, 6]` and
add one more `version >=` group. No migration default needed — the field's own C# default (`false`) is
already correct for a pre-v6 pin (an un-assigned task, or one whose assignment wasn't yet Accepted).

**What changed in the serialization format:**
```
// v5 per-pin: ...v4... | depth
// v6 per-pin: ...v5... | isAcceptedAssignment
```

**How the migration works** — one more threshold-gated read, no clamping needed for a bool:

```csharp
// private const byte PinVersion = 6;    // current  (MinPinVersion stays 1)

// In TryReadPinList's per-pin loop, after the v5 Depth read:
if (version >= 6)
{
    pin.IsAcceptedAssignment = r.ReadBoolean();
}
```

A pre-v6 pin simply never wrote `IsAcceptedAssignment` and reads as `false` (its C# default).

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

## The text-interchange codecs (JSON + TSV)

`ScribeDocumentJsonCodec` and `ScribeDocumentTsvCodec` are a **separate family** from the two binary
codecs above. They are NOT world-persistence or network-sync formats — they are the clipboard
export/import lanes for the Scriptorium's Import/Export section (add-scriptorium-import-export), so a
player can copy a document out to text, edit it anywhere, and paste it back. They never touch a save
file or a packet payload directly; the Mod re-serializes an imported document through the *binary*
`ScribeDocumentCodec` before it is stored. Both live in Core and use only BCL string/`System.Text.Json`
APIs (no VS API, no new NuGet dep).

Two properties make them safe to evolve, and both differ from the binary codecs' append-only-bytes rule:

### JSON: a version window, extra fields ignored

- `ScribeDocumentJsonCodec.Version` (currently **1**) is a single `"v"` counter, and `MinVersion`
  (currently **1**) is the oldest `v` still accepted. `TryDeserialize` rejects any payload with
  `v < MinVersion` — **and a payload with no `v` at all parses as `v = 0` and is rejected**, so a
  foreign JSON object (or hand-typed junk that happens to be valid JSON) can't slip in as an empty
  document.
- Forward tolerance is free: a newer producer's extra keys are simply ignored by the read DTO
  (`PropertyNameCaseInsensitive`, unknown members dropped). So a `v`-2 export opened by a `v`-1 build
  loses only the fields `v`-1 doesn't know — it does not fail.
- **To add a field:** append it to the DTO, bump `Version`, and only raise `MinVersion` if the new
  field's *absence* would make an old payload unreadable (it usually won't — default it instead). This
  mirrors the binary codec's "append, never reshuffle" spirit without the byte-offset bookkeeping.
- Omitted-by-design fields (never serialized): `TaskId`/`DocId` (import mints fresh identity, so an
  import can never carry a pin), `assignedToUid` (assignment is place-bound), and `currentQuantity`
  (live/derived, recomputed after import).

### TSV: fixed columns forever, richness in `Special`

The TSV lane has **no version field at all** — its stability contract is the *fixed column set*
instead:

```
Type · Done · Text · Special · Count · Depth
```

- **The six columns are frozen.** New per-kind richness goes INSIDE the `Special` cell as a
  comma-separated payload the kind parses itself (a future map block's `x,y,z,icon,color` is the worked
  example) — **never a new column.** This keeps the table narrow and keeps old and new exports mutually
  loadable in a spreadsheet.
- The header is matched **by name, case-insensitive**, so column *order* is cosmetic and **unknown
  trailing columns are ignored** while **missing columns default**. That is the TSV analogue of the JSON
  "extra keys ignored" tolerance.
- **Row position is the sequence** (no order column). A leading `title`-type row carries the document
  title and produces no block; its absence just leaves the title unchanged.
- **Import is loose — degrade, never reject:** an unknown `Type` token becomes a plain Task, a malformed
  row is skipped, caps are enforced, and nothing throws. (Game-resolution of item/link references is the
  Mod layer's job — `ScribeImportValidator` — not this codec's; the codec only carries the reference
  strings.) Every block gets a fresh `TaskId`, so a TSV import can never carry a pin either.

## Current state

| Codec | Kind | Current | Accepted | Migration method / stability rule |
|---|---|---|---|---|
| `ScribeDocumentCodec` | binary (save/sync) | v10 | v5–v10 (progressive reads) | `ApplyPreV6Defaults` — defaults Tracker/Link per-block fields (v6) + guide-page `LinkLabel` (v7) + Craft `RecipeSignature` (v8); v9 assignment then reads behind `version >= 9`, v10 `TargetPlayerUid` behind `version >= 10` |
| `ScribeAssignmentStore` | binary (save/sync) | v1 | v1 only | New in add-assignment-and-quest-support; no prior version to migrate from yet |
| `ScribePinCodec` | binary (save/sync) | v6 | v1–v6 (progressive reads) | `ApplyPreV2Defaults` — seeds Kind (→Task), LinkTarget (→null), TargetItemCode (→null), quantities, LinkLabel (→null), Depth (→0); v2–v6 fields then read behind `version >=` thresholds (v6's `IsAcceptedAssignment` needs no explicit default — its C# `false` default is already correct) |
| `ScribeDocumentJsonCodec` | text (clipboard) | v1 | v1+ (`v >= MinVersion`; missing `v` → rejected) | Version window; unknown keys ignored on read. Add a field → append to DTO + bump `Version`; raise `MinVersion` only if an old payload becomes unreadable |
| `ScribeDocumentTsvCodec` | text (clipboard) | — (no version) | any header with the known columns (by name) | Fixed 6 columns forever (`Type · Done · Text · Special · Count · Depth`); new richness goes in the comma-packed `Special` cell, never a new column; unknown columns ignored, missing columns defaulted |
