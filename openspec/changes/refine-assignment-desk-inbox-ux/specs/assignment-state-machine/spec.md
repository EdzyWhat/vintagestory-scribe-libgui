## MODIFIED Requirements

### Requirement: New (unseen) is a flag on Unaccepted, not a separate state
An Unaccepted assignment SHALL carry a Seen flag, defaulting to unseen ("New") at creation. The
flag SHALL become seen the moment the Assignee's Inbox view for that assignment becomes the active
view on any Inbox-capable surface — whether reached by clicking an Inbox nav button, by opening a
surface whose Inbox view is its default or only view (the standalone Inbox block), or by any other
path that makes the Inbox view active — not only via an explicit nav-button click, unless the
Assignee immediately takes an action that moves it to another state (in which case the flag becomes
moot). The Seen flag SHALL NOT alter which transitions are valid from Unaccepted.

#### Scenario: A newly-sent assignment starts unseen
- **WHEN** an Assigner sends a new assignment
- **THEN** the assignment's state is Unaccepted and its Seen flag is false

#### Scenario: Opening the Inbox marks it seen
- **WHEN** the Assignee opens an Inbox view that shows a New (unseen) assignment and takes no
  further action before closing it
- **THEN** the assignment remains Unaccepted but its Seen flag becomes true

#### Scenario: Opening the standalone Inbox block marks it seen
- **WHEN** the Assignee opens the standalone Inbox block's dialog, which lands directly on its
  Inbox view as that block's only view, showing a New (unseen) assignment
- **THEN** the assignment's Seen flag becomes true, the same as if the Assignee had reached the
  Inbox view via a nav-button click on any other surface

#### Scenario: Re-opening the standalone Inbox block also marks it seen
- **WHEN** the Assignee closes and re-opens the standalone Inbox block's dialog while it still
  shows a New (unseen) assignment
- **THEN** the assignment's Seen flag becomes true on that re-open, the same as on first open
