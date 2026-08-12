## Why

Players can only add checkbox **tasks** in the editor — every "Add task" click creates a
Task block, even when the player just wants a freeform note with no completion state. The
document model already supports a no-checkbox `Text` block (and the editor already renders
one), but there is no UI entry point to create it: the footer button is hardwired to
`AddTask("")`. This change gives players a way to add a plain note, and does so through the
extensible **kind picker** the roadmap already committed to (the "New Task dropdown" from
the resolved picker-keystone design) — so the later Tracked/Linked kinds slot in without
reworking the entry point.

## What Changes

- Replace the editor footer's single hardwired **"Add task"** button with a **kind picker**
  (a dropdown/split control) whose selection determines which block kind the add creates.
- Ship two live kinds in this interim release: **Standard Task** (checkbox, today's
  behavior) and **Note** (freeform `Text` block, no checkbox / no completion). Wire a
  `Note` add path to `ScribeDocument.AddTextSection("")`, mirroring the existing empty-task
  add (append, focus, ghost-hint, self-destruct-if-abandoned).
- Build the picker's kind registry to be **extensible**: adding a future kind
  (Tracked/Linked) is a matter of registering it, not restructuring the footer. Kinds not
  shipping this release are absent from the live menu, not stubbed as dead options.
- Generalize the **empty-row self-destruct** from "empty task" to "empty task **or** note":
  an abandoned empty note is removed on blur, and on switch-to-read / close, exactly as an
  empty task is today. (The Core model still stores text verbatim; removal stays an
  editing-layer behavior.)
- Every editor surface that shares `ScribeEditorContent`'s footer (Lectern, plain Notebook,
  Clockmaker's Notebook, always-edit tablet) inherits the picker; the tablet's task-count
  cap still governs task adds.

## Capabilities

### New Capabilities
- `task-kind-picker`: The editor footer's add control is a kind picker whose registry
  defines the available block kinds, their labels, and their add behavior; selecting a kind
  creates a block of that kind. Extensible by design — this release registers Standard Task
  and Note; future kinds register without changing the footer contract.

### Modified Capabilities
- `lectern-gui-shell`: The "New tasks are created empty" add control becomes kind-aware (an
  add can create a Note as well as a Task), and "An empty task row is removed when it loses
  focus" generalizes to remove an empty **task or note** on blur / switch-to-read / close.

## Impact

- **Code (`src/Mod/`)**: `ScribeEditorContent` footer (swap the `Button` for a kind picker;
  add a `Dropdown`/kind-select control); `ScribeDialogBase.Editor.cs` (`OnClickAddTask` → a
  kind-parameterized add; a `Note` path calling `AddTextSection`); the empty-row lifecycle
  (`PurgeEmptyTasksFromScratch`, `OnRowBlurred`, the `pendingEmptyRowRemoval` guard, and
  `FocusedRowIsEmptyTask`) generalize their "IsTask && blank" checks to "blank of either
  kind".
- **Core (`src/Core/`)**: no model change — `ScribeBlockKind.Text` and `AddTextSection`
  already exist; kinds still round-trip via the existing codec.
- **Lang**: new key(s) for the picker labels ("Note", and the kind-menu affordance).
- **Interaction with `scribe-document-policy`**: the tablet's 10-**task** cap is unchanged;
  design decides whether notes count toward any cap (leaning uncapped, since the cap is
  task-scoped).
- **Interaction with `reconcile-animating-surfaces`** (in flight): the add/self-destruct
  paths already route through `RebuildBody()`; the generalized note add reuses that path, no
  new rebuild trigger.
