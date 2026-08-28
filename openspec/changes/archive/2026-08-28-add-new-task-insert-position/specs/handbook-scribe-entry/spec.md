## MODIFIED Requirements

### Requirement: Add to Scribe resolves a target surface in three tiers
Triggering "Add to Scribe" SHALL create a new task for that item, resolving which Scribe surface
receives it in three ordered tiers:
1. If a Scribe surface is **currently open** — a block (Scriptorium, Lectern) or an item (Notebook,
   Tablet) — the task SHALL be added to that open surface.
2. Otherwise, if the player **carries a Scribe item**, the game SHALL open the UI for the
   last-opened Scribe item the player still carries (or, if there is no last-opened record, a Scribe
   item they carry) and add the task to it.
3. Otherwise, the action SHALL show a Vintage Story error to the effect of "You need a Scribe item
   to do that." and SHALL create no task.
In tiers 1 and 2 the task SHALL be added through the normal server-authoritative edit path (not
written client-side only). The new block SHALL be inserted at the player's **New Task Insert** edge
(`Top` → index 0, `Bottom` → append), not unconditionally appended. A Crafting Task SHALL keep its
ingredient children in the contiguous depth-1 run under the parent.

#### Scenario: Task lands in the already-open surface
- **WHEN** the player has any Scribe surface open (block or item) and triggers "Add to Scribe"
- **THEN** a new task for that item is inserted at the New Task Insert edge of that open surface's
  document and is visible there

#### Scenario: Top insert from Handbook
- **WHEN** New Task Insert is Top, a Scribe surface is open, and the player triggers "Add to Scribe"
- **THEN** the new row is at index 0 (a Crafting Task's children sit immediately under that parent)

#### Scenario: A carried Scribe item is opened and receives the task
- **WHEN** no Scribe surface is open but the player carries a Scribe item, and they trigger "Add to
  Scribe"
- **THEN** the last-opened carried Scribe item's UI opens and the new task is added to it at the
  New Task Insert edge

#### Scenario: No Scribe item reports guidance
- **WHEN** the player triggers "Add to Scribe" with no Scribe surface open and no Scribe item carried
- **THEN** the player is shown a "You need a Scribe item to do that." error and no task is created
