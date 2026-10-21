## Context

Both codecs persist a per-player list (pins; quest decisions) as `[magic][version][playerCount]([uid][list])*`.
`ScribePinCodec` passes the version byte into its list reader; the quest codec does not need it. Core must stay free
of Vintage Story types.

## Goals / Non-Goals

**Goals:** one copy of the framing and guards; byte-identical output; unchanged public API of both codecs.

**Non-Goals:** touching other Core stores (`TimerStore`, `PendingHistoryStore`, `GuestbookStore`), changing versions
or magics, or altering what malformed input does (fail-safe empty).

## Decisions

- **A small static generic helper, not a base class.** The two codecs are static; a helper taking delegates
  `(BinaryReader r, int totalBytes, byte version) -> List<T>` and `(BinaryWriter w, List<T>)` keeps the call sites
  readable. Alternative: an abstract `PlayerListStoreCodec<T>`. Rejected as more ceremony for two users.
- **Golden bytes first.** Before refactoring, capture serialized output of a fixed sample store from each codec as a
  test fixture, so byte-identity is asserted directly and not only through round-trip.
- **Keep the exception filter and guard values exactly** (`EndOfStreamException or IOException or FormatException`;
  `playerCount` bounded by `MaxPlayers` and byte length).

## Risks / Trade-offs

Round-trip tests alone can pass while the wire format drifts; the golden-bytes test closes that gap.
