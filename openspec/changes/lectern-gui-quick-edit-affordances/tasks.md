> **Icon + architecture hand-off (2026-07-21, from `add-custom-svg-row-icons`):** the custom SVG
> codes `scribepin` (pushpin) and `scribeclose` (delete X) are now registered at client init and
> available for the pin/delete buttons to draw — no asset/registration work here, just repoint.
> See `docs/specs/scribe-icon-svgs.md`. **BUT this change was drafted against the now-dead
> `ScribeBlockRowCell` row architecture:** after the S2 merge the live row is `ScribeRowElement`
> (checkbox + text + ruling only), which has **no pin/delete gutter columns** — so this change's
> icon-column *width-scaling* tasks (§6) assume columns that no longer exist. Re-adding those
> columns to `ScribeRowElement` + `RowTextLayout` is now owned by the new
> `restore-row-affordance-columns` change (visuals) and its follow-up wiring (delete/pin
> messages). Rescope or defer §6 against that reality before implementing.

## 1. Prerequisites (block everything else)

- [x] 1.1 Confirm with the user whether design.md's Decision 2 (Read-view toggle applies
      directly to the authoritative `Document`, no lock acquisition, accepting a
      last-write-wins race against an in-flight Editor-mode edit) is acceptable as
      described, or needs a stronger guarantee first. Do not proceed past this task
      until answered — it's a real design tradeoff, not an implementation detail.
      (**Accepted as-described by the user, 2026-07-20.** Read-view toggle stays
      lock-free and applies directly to the authoritative document; the accepted worst
      case is a single lost toggle/edit under a same-tick collision with an Editor-mode
      autosave — no corruption. No stronger guarantee required. Design.md's Open
      Question resolved accordingly.)
- [ ] 1.2 Do not start this change until `skeuomorphic-lectern-gui`'s scroll fixes
      (tasks 3.4a viewport-relative row Y, and 3.4b scrollbar thumb-drag defer) are
      implemented and its 3.5 manual test passes. This change edits the same row-
      rendering code paths (`ComposeReadView`/`ComposeEditorView`/`ScribeBlockRowCell`)
      and its own 4.3/7.4 assume a correctly-scrolling row list — building interactive
      Read-view toggles on top of a row list whose scroll is still broken would layer new
      behavior onto an unstable base and make regressions hard to attribute. Hard
      prerequisite, not a sequencing preference: confirm 3.4a/3.4b are done before any
      task below.

## 2. Core: no changes expected, confirm the assumption

- [ ] 2.1 Confirm `ScribeDocument.ToggleTask` requires no change to support being called
      directly against the authoritative document (outside the scratch-copy/lock flow
      Editor view uses) — it should already be a self-contained, idempotent,
      bounds-checked mutation with no dependency on scratch-copy state. If this
      assumption is wrong, stop and revisit design.md's Decision 1/2 before continuing.

## 3. New message type and server-side handling

- [ ] 3.1 Add `ScribeToggleTaskMessage` (`PosX`, `PosY`, `PosZ`, `int BlockIndex`),
      mirroring `ScribeReleaseLockMessage`'s minimal shape (client → server only, no
      reply fields needed since the normal document-resync broadcast covers it).
- [ ] 3.2 Add `BlockEntityScribeLectern.ToggleTask(int index)`: calls
      `Document.ToggleTask(index)` directly (no lock check, no scratch copy) and
      `MarkDirty(redrawOnClient: true)` on success, mirroring `ApplyEdit`'s existing
      dirty/resync pattern minus the lock gate.
- [ ] 3.3 Wire the network channel handler for `ScribeToggleTaskMessage` server-side,
      resolving the target `BlockEntityScribeLectern` by position (mirror the existing
      handler-resolution pattern used for `ScribeRequestAccessMessage`/
      `ScribeReleaseLockMessage`).
- [ ] 3.4 Core.Tests: add/confirm a test asserting `ScribeDocument.ToggleTask` behaves
      correctly when called without any scratch-copy setup (fresh document, direct call) —
      this is likely already covered by existing tests since `ToggleTask` doesn't know
      about scratch copies at all, but confirm rather than assume.

## 4. Read view: interactive checkbox

- [ ] 4.1 In `GuiDialogScribeLectern.ComposeReadView`, replace the plain `AddStaticText`
      rendering for task rows with an interactive toggle element (mirroring
      `ScribeBlockRowCell`'s task-row `AddSwitch` usage, including the
      `size: ToggleWidth * TextSizeScale` fix already shipped for Editor view). Note rows
      keep their existing plain `AddStaticText` rendering unchanged.
- [ ] 4.2 Wire the read-view toggle's callback to send `ScribeToggleTaskMessage` over the
      network channel, keyed by the row's block index.
- [ ] 4.3 Confirm the read-view row list's existing viewport-culling pass-2 logic (full
      containment plus the 3.4a viewport-relative row Y, per `skeuomorphic-lectern-gui`'s
      scroll fixes required by task 1.2) still applies correctly now that task
      rows are interactive elements, not just static text — an interactive element inside
      `BeginClip`'s child-elements block should behave the same way a static one does for
      culling purposes, but confirm rather than assume given `GuiElementTextInput`'s known
      scissor-canceling quirk (`VSAPI-NOTES.md`) applies to *task-row text inputs*, not
      switches — verify a switch element doesn't have an equivalent surprise.
- [ ] 4.4 Manually test in-game: toggle a task from read view; confirm the state updates
      immediately for the toggling player and (in a second client/session) for anyone else
      currently viewing the same lectern, and confirm the editor lock is untouched
      (a second player can still freely enter/exit editor mode around a read-view toggle
      with no refusal).

## 5. Dialog width unification

- [ ] 5.1 Replace `ScribeClientConfig.ReadListWidth`/`EditorListWidth` with a single
      `ListWidth` field; update both `ComposeReadView`/`ComposeEditorView`'s `listWidth`
      locals to reference it.
- [ ] 5.2 Re-check `ScribeBlockRowCell.TextWidth`/`MeasureWrappedHeight` call sites that
      previously passed `EditorListWidth` specifically — confirm none assumed the two
      widths could differ.
- [ ] 5.3 Manually test in-game: switch between read and editor view on the same lectern;
      confirm the dialog's width does not visibly change across the switch.

## 6. Icon-column scaling

> RESCOPED 2026-07-22 by `restore-row-affordance-columns`: the icon columns were dropped when the
> row-list rework replaced `ScribeBlockRowCell.Compose` with `ScribeRowElement`, then restored (with
> `TextSizeScale` scaling built in) on the new architecture in `RowTextLayout.For`. The two code tasks
> below targeted the now-dead `ScribeBlockRowCell.TextWidth`, so they are OBSOLETE; the scaling they
> asked for is already delivered. The manual test (6.3) is superseded by
> `restore-row-affordance-columns` task 6.3(g).

- [~] 6.1 OBSOLETE: targeted `ScribeBlockRowCell` (dead post-rework). Icon-column `TextSizeScale`
      scaling is delivered in `RowTextLayout.For` by `restore-row-affordance-columns`.
- [~] 6.2 OBSOLETE: `ScribeBlockRowCell.TextWidth` is dead; the live budget math lives in
      `RowTextLayout.For` (`textWidth = pinX - textX`), handled by `restore-row-affordance-columns`.
- [~] 6.3 SUPERSEDED by `restore-row-affordance-columns` task 6.3(g) (same text-size-sweep check on
      the live `ScribeRowElement` rows).

## 7. Verification

- [ ] 7.1 `dotnet build src/Mod/Mod.csproj --configuration Release` — clean.
- [ ] 7.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — all green, including any new
      task 3.4 coverage.
- [ ] 7.3 `dotnet test tests/Integration.Tests/Integration.Tests.csproj` — all green
      (requires `VINTAGE_STORY` env var pointed at the game install).
- [ ] 7.4 Restage (`bash build/restage.sh`) and manually retest all three items together
      (4.4, 5.3, 6.3) plus a full regression pass against the existing
      `skeuomorphic-lectern-gui` playtest checklist for anything touching row rendering
      (scroll culling, drag-reorder, pin toggle) — this change edits the same rendering
      code paths, so a regression there is a real risk, not just a formality.
