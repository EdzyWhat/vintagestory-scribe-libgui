## Context

`GuiDialogScribeLecternLibGui.cs` is a 3,581-line monolith containing: the dialog class itself, a
`LecternLayout` struct, all build methods, all view-state fields and event handlers, and six widget
classes (read content + row, editor content + row, pinned content + row) that have no coupling to the
Lectern at all. The dialog is coupled to `BlockEntityScribeLectern` via a concrete field (both sides
hold each other's concrete type). `ScribeDocument.DefaultTitle` is the string `"Lectern"`, baked into
the Core layer despite being item-specific.

Three upcoming blocks — Notebook, Desk, Clockmaker's Notebook — share the Lectern's views, framing,
and behavior. Without this refactor, each block gets its own copy of the 3,581-line file.

## Goals / Non-Goals

**Goals:**
- Introduce `IScribeDocumentHost`: the minimal interface the dialog needs from any block entity
- Generalize `LecternLayout` into `ScribeLayout` + `ScribeLayoutProportions` (overridable per-item)
- Extract `ScribeDialogBase`: the shared dialog operating against the interface
- Move row content widget classes into separate files with item-neutral names
- Slim `GuiDialogScribeLecternLibGui` to a ~80-line sealed subclass
- Add `IScribeDocumentHost` impl to `BlockEntityScribeLectern`
- Fix `ScribeDocument.DefaultTitle` to `"Untitled"` with per-host override

**Non-Goals:**
- No behavior changes whatsoever (the Lectern must work identically after the refactor)
- No new items (Notebook, Desk) shipped in this change
- No changes to network protocol, persistence, assets, or Core test suite

## Decisions

### D1: Interface-based host abstraction over base class

The dialog couples to the block entity through exactly 5 surfaces: `Pos` (for packets),
`Document` (for read-view seeding + DocId), `IsLockedByOther` (for affordance gating),
`ApplyLocalOptimisticEdit` (post-flush cache), and the layout/backdrop/title metadata.
These are all data-access or metadata calls — no callbacks from the BE into the dialog are expressed
this way (those go through the existing `HandleServerReply` / `EnterEditorMode` pattern which stays
unchanged). An interface is the right shape: small, testable, and decoupled.

Alternative considered: abstract base class on `BlockEntityScribeLectern`. Rejected because VS block
entities already inherit `BlockEntity`; C# doesn't allow multiple inheritance, and an abstract BE base
would force Notebook and Desk into the same class hierarchy even when their lock semantics differ.

### D2: `ScribeLayoutProportions` as an overridable record

`ScribeLayout` today hardcodes all column proportions as constants inside `LecternLayout`. A Desk
might want wider side columns; a tablet might use a different aspect ratio. `ScribeLayoutProportions`
is a `readonly record struct` with `init`-only fields and a `Default` singleton, so a subclass
overrides only what it needs:

```csharp
ScribeLayoutProportions.Default with { SideColFrac = 0.12f }
```

The base `ScribeDialogBase` calls `host.GetLayout(pixelArtSize)` every build, so a subclass that
returns a different `ScribeLayout` gets different proportions automatically, with no conditional in
the base.

Alternative considered: pass individual floats through the constructor. Rejected because the number
of params would grow with each new item and the interface would become fragile.

### D3: `ScribeDialogBase` absorbs ~95% of the monolith; extension points are minimal

The base class has a single `virtual` extension point: `GetExtraNavButtons()` (returns empty by
default). Every item gets Read/Edit/Pinned/Settings for free; extra buttons (History, Assignment,
Reminders) are returned by the subclass and appended to the nav column. No other method is virtual —
the build/view-switch/lock/autosave logic is not meant to be overridden per item.

`BuildDocumentHeader` uses `host.DefaultDocumentTitle` for the empty-title fallback (instead of the
hardcoded `ScribeDocument.DefaultTitle`).

### D4: Row content classes renamed item-neutrally; placed in separate files

`ScribeLecternReadContent` → `ScribeReadContent` in `ScribeReadContent.cs`
`ScribeLecternEditorContent` → `ScribeEditorContent` in `ScribeEditorContent.cs`
`ScribeLecternPinnedContent` → `ScribePinnedContent` in `ScribePinnedContent.cs`

The "Lectern" prefix was always a misnomer — these widgets operate on `ScribeReadRowData`,
`ScribeEditRowData`, and `ScribePinRowData` respectively; none of them reference the Lectern. Renaming
makes the intent clear and removes the false coupling from the type names.

### D5: `ScribeDocument.DefaultTitle` → `"Untitled"`

The constant currently read `"Lectern"` — an item-specific string in the Core layer, which must have
no VS-API or item-specific dependencies. Changing it to `"Untitled"` makes the Core layer correct.
Each `IScribeDocumentHost` implementation supplies `DefaultDocumentTitle` (e.g. `"Lectern"`,
`"Notebook"`) for the commit-time fallback in `CommitTitleIfEditing`. This is a data migration in
name only: the constant is only used as a fallback for a blank-out-and-save gesture; any document that
was previously saved with `Title = "Lectern"` retains that title in the serialized bytes, unchanged.

### D6: Explicit interface members for `IScribeDocumentHost` on `BlockEntityScribeLectern`

`Pos` and `Document` are already public properties on the BE, satisfying the interface implicitly.
`IsLockedByOther` and `ApplyLocalOptimisticEdit` are also already public. The three new items
(`BackdropSpec`, `GetLayout`, `DefaultDocumentTitle`) are implemented as explicit interface members
so they don't pollute the BE's public API — they're dialog concerns, not BE concerns.

The `dialog` field is retyped from `GuiDialogScribeLecternLibGui?` to `ScribeDialogBase?`, since
`HandleServerReply` only calls `EnterEditorMode`, `EnterReadMode`, `HandleSaveFailed`, and
`RefreshReadView` — all defined on the base.

## Risks / Trade-offs

[Large mechanical refactor] → Risk of subtle regressions in scroll state, focus-node lifetime, lock
orchestration. Mitigation: the refactor is a pure structural move with zero behavior changes. Every
existing test passes unchanged. Manual playtest (open → Read → Edit → Pinned → close; reopen; lock
contention multiplayer) is the primary verification.

[`ScribeDocument.DefaultTitle` change] → Risk of a document that was saved with `Title = "Lectern"`
now showing a mismatch if the code is compared to the constant. Mitigation: the constant is only
used at commit-time (when the player blanks the title and saves). Existing serialized documents that
already contain `"Lectern"` as their title are unaffected — the codec reads the persisted string
verbatim. The only observable change is: if a player clears the title field and clicks Done on the
Lectern after this change, it resets to `"Lectern"` (from `DefaultDocumentTitle`) rather than
`"Untitled"` — which is the desired behavior. Net change to players: zero.

[Single-file monolith becomes multiple files] → Better for maintenance, but the diff is large. No
functional risk; the logic is identical. CI still builds from the project file which includes all
`*.cs` in `src/Mod/`.

## Open Questions

None. The design is fully determined by the exploration session.
