## Context

The Notebook already stores its document in `ItemStack.Attributes["scribeDocument"]` via
`ScribeDocumentAttributes`. `NotebookHost` wraps that slot, owns the document, and calls
`Flush()` to write through and push a sync packet to the client. `GuestbookStore` in
`src/Core/` is an existing append-only log model with a versioned binary codec that
`BlockEntityScribeLectern` uses — it is the direct structural template for `HistoryStore`.

Server-side event infrastructure in `ScribeModSystem` already registers `OnSaveGameLoaded`,
`GameWorldSave`, and `PlayerNowPlaying`. The pattern for adding new server hooks is
established and lightweight.

## Goals / Non-Goals

**Goals:**
- Seven auto-recorded event types, capped per kind, stored in the item's `ItemStack`
- History tab in the Notebook dialog (read-only for auto events, editable for manual)
- History survives world restart, inventory moves, and being given to another player
- Temporal storm detection is null-safe if the survival mod is absent

**Non-Goals:**
- Lectern History tab (Notebook-only for this change)
- Lore/discovery tracking (enum value reserved, not wired)
- Boss kill tracking for any entity other than Eidolon and Erel (Mad Crow)
- Retroactive history population (events before the change is installed are not back-filled)
- History editing beyond the 10 manual entries (auto entries are immutable)

## Decisions

### 1. History stored in ItemStack attributes, not the server document store

The plan explored two options: `ItemStack.Attributes["scribeHistory"]` vs. the server-side
document store keyed by DocId. We use the **item attributes** path.

**Why:** History should physically travel with the item — if a player trades the notebook,
the new holder sees its full chronicle. The document store would require an explicit copy
step on item transfer. Item attributes are already the storage model for the document itself
(`scribeDocument`), so `NotebookHost.Flush()` simply writes a second key alongside it with
no new infrastructure. The player perception that "history lives in the notebook" is genuine,
not an illusion.

**Risk:** Item attributes have a size budget (VS uses compressed NBT). With max entries
(10 deaths + 5 storms + 10 pvp + 10 boss + unlimited holds + 10 manual + 1 crafted), the
worst-case serialized size is roughly 5–8 KB — well within VS's practical item attribute
limits. `MaxEntries` constants enforce this ceiling; the codec includes a `MaxHistoryBytes`
guard as a backstop.

### 2. Death message reconstructed from `DamageSource`, not intercepted from chat

Death messages in VS are assembled from `deathmsg-{entityCode}-{N}` lang keys with
`{0} = playerName`, where N is chosen randomly from the available variants. We reconstruct
the message on the server using the same `Lang.Get()` call the game itself uses, picking N
with a deterministic hash of the player's UID to avoid `Random` (unavailable in Core).

**Why:** Intercepting the chat broadcast would require hooking a lower-level server message
path that is not part of the stable modding API. Reconstruction from `DamageSource.Source`
and `DamageSource.SourceEntity.Code` produces the same string the player sees in chat.

**Alternative rejected:** Store only the raw damage source type and format on display —
less immersive (the stored string would differ from what the player saw in chat).

### 3. PickedUp detected at dialog-open time, not via inventory slot events

When `NotebookHost` is constructed server-side (on dialog open), we check if the current
player's name already appears as a `PickedUp` entry. If not, we add one.

**Why:** VS has no clean "item picked up" event. Inventory slot-change hooks fire for every
stack mutation (including crafting output, sorting, etc.) and would require filtering noise.
Dialog-open is the meaningful "first interaction" signal — if a player never opens the
notebook, they haven't really "held" it in any significant sense. This also means the
Crafted entry (written at craft time) is always the first entry; the crafter's PickedUp is
written on their first open.

### 4. Temporal storm detected via slow-tick edge detector

`SystemTemporalStability.StormData.nowStormActive` is polled every 5 seconds. On a
`false → true` transition we record the storm for all online notebook holders.

**Why:** No event is fired by `SystemTemporalStability` when a storm starts. The 5-second
poll granularity is fine — storms announce 1–2 in-game days in advance and last many
minutes; a 5-second detection lag is imperceptible.

**Null-safety:** If `GetModSystem<SystemTemporalStability>()` returns null (non-survival
server), the tick handler silently skips. No hard dependency added.

### 5. Boss kill by proximity (100 blocks), not killing-blow attribution

On `OnEntityDeath` for a boss entity, we check all online players within 100 blocks who
hold a Notebook. This captures group/assist kills.

**Why:** In practice bosses are rarely soloed; attribution to the killing blow would miss
most real encounters. 100 blocks is generous enough to cover any reasonable fight area
without covering the entire world.

**Boss entity codes (confirmed from entity JSON):**
- Eidolon: code prefix `eidolon` (variants: `eidolon-immobilized`)
- Mad Crow: code prefix `erel` (variants: `erel-pristine`, `erel-corrupted`)
A small hardcoded display-name table maps prefix → "Eidolon" / "Mad Crow".

### 6. Manual entries use a new network packet, not the existing edit path

`ScribeAddHistoryEntryMessage` carries the entry text; the server supplies the date and
validates the cap. This is separate from `ScribeEditDocumentMessage` because history entries
are not document blocks — they live in a different store with different mutation rules.

## Risks / Trade-offs

- **Risk:** `OnCreatedByCrafting` fires client-side too in some mod configurations.
  → **Mitigation:** Gate the write with `Api.Side == EnumAppSide.Server`.

- **Risk:** `OnEntityDeath` fires for every entity death, including hostile mobs. The filter
  (`entity is EntityPlayer` for death/pvp; entity code prefix for boss) must be fast.
  → **Mitigation:** Prefix check is a string `StartsWith` on the entity code — O(1) in
  practice. Only runs the "find notebook holders" scan when the filter passes.

- **Risk:** History codec version bump needed if `LoreDiscovery` is wired later.
  → **Mitigation:** Codec is `SHST v1` with `PriorVersion` constant and `ApplyMigrations`
  stub, following the `ScribeDocumentCodec` pattern just established.

## Migration Plan

No existing saves to migrate — this is new data. A notebook with no `scribeHistory`
attribute opens with an empty `HistoryStore` (same as a fresh notebook). No rollback
needed; removing the change simply means the attribute is ignored.
