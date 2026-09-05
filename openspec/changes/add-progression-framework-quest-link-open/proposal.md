## Why

A Quest Link's label is a no-op today: `ScribeItemRef.OpenHandbookPage` explicitly returns early for
any quest target (`if (ScribeLinkTarget.IsQuest(code)) return;`), so clicking one does nothing —
silently contradicting the already-merged `link-task` requirement that "a Link task behaves as a
hyperlink from every surface." Progression Framework's own Quest Log (`GuiDialogLedger`, opened by the
`L` hotkey) has a public `ToggleKeyCombinationCode`, so it can be opened the same reflection-free way
Scribe already opens the base-game Handbook. It has no public tab-selection API, so landing on the
Quest Log tab specifically (rather than whichever tab — Training or Quest Log — the player last had
open) needs one best-effort reflected field read, mirroring the self-disabling posture already used
for VS Quest's live progress reflection.

## What Changes

- `ScribeItemRef.OpenHandbookPage` no longer silently no-ops for a Progression Framework Quest Link's
  target; a Quest Link's click dispatches to a new quest-log-open path instead of the Handbook path.
- New helper opens Progression Framework's `GuiDialogLedger` via `capi.Gui.LoadedGuis` +
  `TryOpen()` (no reflection — matches the existing Handbook-dialog-lookup pattern).
- Before opening, best-effort reflection forces `GuiDialogLedger`'s private `currentTab` field to
  `QuestLog` so the click always lands on the Quest Log, not the Training tab. Self-disabling on any
  failure (missing/renamed field), never blocking the plain open.
- VS Quest Links are unaffected — this change is scoped to Progression Framework only (see design.md
  Non-Goals for why VS Quest's equivalent isn't pursued here).
- No deep-link to the specific quest or scroll-to-row: `GuiDialogLedger` has no seam for either
  (private `collapsedQuests`, no addressable scroll position) — landing on the correct tab, with the
  player then finding their quest in the (usually short) list, is the full scope of this change.

## Capabilities

### Modified Capabilities
- `link-task`: extends "A Link task behaves as a hyperlink from every surface" with a Progression
  Framework Quest Link's specific activation target (the Quest Log dialog, not a Handbook page) and
  fixes the current no-op.

## Impact

- `src/Mod/ScribeItemRef.cs`: `OpenHandbookPage` dispatch change.
- `src/Mod/ScribeDialogBase.Layout.cs`: `OpenRowLink`'s Quest Link branch (or a new sibling method) —
  route to the new quest-log-open helper instead of (or in addition to) the Handbook path.
- New file or addition for the Progression Framework Ledger-open helper (reflection-gated, self-disabling).
- No `src/Core/` changes — this is Mod-layer only (no API-free logic changes).
