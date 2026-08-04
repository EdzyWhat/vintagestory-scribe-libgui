## ADDED Requirements

### Requirement: A dev-gated command seeds sample tasks and notes

The mod SHALL provide a server-side chat command (`/scribe seed`) that populates a target Scribe
document with sample content for screenshot and video capture. The command SHALL be gated so it is
only usable by a privileged/creative caller: it SHALL require the `controlserver` privilege and
SHALL refuse to run for a caller not in creative mode. When seeding tasks, it SHALL add at least
twelve tasks with a mix of completed and incomplete states, plus a small number of freeform note
sections, using the existing document mutation methods (`AddTask`, `AddTextSection`, `ToggleTask`).

#### Scenario: Privileged creative player seeds tasks

- **WHEN** a creative-mode player with the `controlserver` privilege runs the seed command targeting
  a valid Notebook or Lectern
- **THEN** the target document gains at least twelve tasks (some marked done) and a few note sections

#### Scenario: Command refuses for an unprivileged or survival caller

- **WHEN** a player lacking the `controlserver` privilege, or a player in survival mode, runs the
  command
- **THEN** the command does not seed content and returns an error result

### Requirement: The command resolves and respects the correct seed target

The command SHALL target either the player's held Notebook / Clockmaker's Notebook item or the
Lectern block the player is looking at, defaulting to the looked-at Lectern when present and
otherwise the held notebook. Because History is hosted only on the Notebook and Guestbook only on
the Lectern, the command SHALL seed History only when targeting a notebook and Guestbook only when
targeting a lectern, silently skipping content types that do not apply to the resolved target.

#### Scenario: Auto target prefers the looked-at lectern

- **WHEN** the player is looking at a Lectern and runs the seed command without an explicit target
- **THEN** the Lectern is seeded

#### Scenario: History is skipped on a lectern target

- **WHEN** the player seeds "history" (or "all") against a Lectern
- **THEN** no History entries are attempted on the Lectern and the command reports History as not
  applicable to that target

### Requirement: The command seeds otherwise un-fakeable History and Guestbook logs

The command SHALL populate the notebook's append-only History log with a variety of event kinds
(e.g. Crafted, PickedUp, Death, PvpKill, BossKill, TemporalStorm, LoreDiscovery) and SHALL populate
a lectern's Guestbook with several fictional visitors, some carrying short notes within the
Guestbook note-length limit. Seeded entries SHALL use plausible, varied in-game dates spanning
multiple days rather than all sharing the current date. All seeding SHALL go through the existing
Core stores (`HistoryStore.TryAddEntry`, `GuestbookStore.TryAddEntry` / `TrySetNote`), so store caps
and de-duplication are respected.

#### Scenario: Notebook History is populated with varied events and dates

- **WHEN** the player seeds "history" (or "all") against a Notebook
- **THEN** the notebook's History tab shows multiple entries of differing kinds with dates spread
  across several in-game days

#### Scenario: Lectern Guestbook is populated with fictional visitors

- **WHEN** the player seeds "guestbook" (or "all") against a Lectern
- **THEN** the Lectern's Guestbook shows several visitor entries, some with short notes, respecting
  the note-length and entry-count limits

### Requirement: Seeded content persists server-authoritatively and syncs to clients

Seeded content SHALL be written through the normal server-authoritative persistence path so it
survives a save/reload and reaches other clients. For a Notebook the command SHALL flush the item's
document and history to the `ItemStack` and push the standard notebook save message; for a Lectern
it SHALL mark the block entity dirty with a client redraw so the block-entity sync repaints the read
view. The command SHALL NOT introduce any new network message type.

#### Scenario: Seeded notebook content survives reload and reaches the holder's client

- **WHEN** a Notebook is seeded and the world is saved and reloaded, or the notebook is opened on the
  client
- **THEN** the seeded tasks, notes, and history are present via the existing save/sync flow
