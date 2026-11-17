## ADDED Requirements

### Requirement: Pin Tab rows can be dated from the row menu

The Pin Tab row menu SHALL offer **Add date** on a pin of an undated plain Task and **Change date** and
**Clear date** on a pin of a dated plain Task, when the dialog offers dates and the pin is not orphaned.
The date entries SHALL sit between Unpin and the separator before Delete, with no extra divider. **Repeat
yearly** SHALL NOT be offered. The picker SHALL open at the pin's date, or today when the pin has none,
and Set, Clear date, Today and Cancel SHALL behave as in a Notebook. Choosing Set or Clear date SHALL send
the date operation to the server by the pin's owner document and task, whether or not the document is
loaded on the client. The row's chip SHALL change when the server's refreshed pin set arrives, not before.
The date entries SHALL NOT be offered on a Pin Tab in a Tablet or in the Assignment Desk, on a pin of a
note, tracker, link, craft or quest objective, or on an orphaned pin.

#### Scenario: Dating an undated pin
- **WHEN** a player right-clicks the Pin Tab row of an undated pinned task and chooses Add date, then Set
- **THEN** the picker opens at today, and after the server applies the date the row, the HUD row and the
  task in its Notebook all show the date

#### Scenario: Changing and clearing
- **WHEN** a player right-clicks a dated pin's row
- **THEN** the menu lists Unpin, Change date, Clear date, a separator and Delete, and Change date opens the
  picker at the pin's date

#### Scenario: Another player holds the editor lock
- **WHEN** another player is editing the pinned task's Lectern document and the player sets a date from the
  Pin Tab
- **THEN** the server refuses, the standard locked message shows, and the pin's date does not change

#### Scenario: The source document is gone or unresolvable
- **WHEN** the server cannot find the pinned task's document
- **THEN** it ignores the operation and the pin's date does not change

#### Scenario: Repeat yearly on the Pin Tab
- **WHEN** a player right-clicks a dated pin
- **THEN** the menu has no Repeat yearly entry

#### Scenario: Surfaces and pins without date entries
- **WHEN** a player right-clicks an orphaned pin, a pinned note, or any row of a Pin Tab in a Tablet or the
  Assignment Desk
- **THEN** the menu has no date entries
