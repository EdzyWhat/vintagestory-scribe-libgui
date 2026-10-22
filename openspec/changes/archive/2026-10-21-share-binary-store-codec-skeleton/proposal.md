## Why

`ScribePinCodec` and `ScribeQuestDecisionCodec` contain the same store skeleton: header read, version-range check,
player-count and uid-length guards, the same exception filter, the same `SerializeStore`, a byte-identical
`ReadHeader`, and a duplicated `MaxPlayers`. A fix to one (for example a new guard) has to be remembered in the other.

## What Changes

- Add one Core helper holding the shared framing (magic and version header, player-count loop, guards, exception
  handling) parameterised by the magic, version range, and per-list read/write delegates.
- Rewrite both codecs to call it, keeping their list-level read/write logic.
- Serialized bytes stay identical (magics `SPIN`/`SPST` and `SQDL`/`SQDS`, existing versions).

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. The on-disk format is unchanged (`skip_specs: true`).

## Impact

`src/Core/ScribePinCodec.cs`, `src/Core/ScribeQuestDecisionCodec.cs`, one new Core file. About 90 lines removed.
Tests in `ScribePinCodecTests` and `ScribeQuestDecisionTests` are the safety net.
