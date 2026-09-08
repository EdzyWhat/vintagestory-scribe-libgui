## Context

See proposal.md - Why. Confirmed by decompiling the installed Progression Framework build
(`reference/ProgressionInvestigations/progressionframework-decompiled/`):

- `ProgressionFrameworkModSystem.StartClientSide` registers a hotkey (`"progressionframeworkledger"`,
  default `L`) and registers `GuiDialogLedger` via `api.Gui.RegisterDialog(...)` — no explicit
  `SetHotKeyHandler` call. `GuiDialogLedger.ToggleKeyCombinationCode => "progressionframeworkledger"`
  is a **public** override; the engine's own hotkey-to-dialog toggle matches on this string (the same
  mechanism already documented for the base-game Handbook in `VSAPI-NOTES.md`), so `HotKey.Handler` is
  never set for this dialog — invoking it would be a dead end.
- `GuiDialogLedger` has two tabs (`Training`, `QuestLog`) tracked by a **private** `currentTab` field,
  defaulting to `Training`. `SwitchTab(...)` (private) is the only way to change it short of the
  player clicking the tab toggle button themselves.
- There is no public or reflectable per-quest scroll/expand target: `collapsedQuests` (private) only
  tracks which quests are manually collapsed, and quest ordering in `LedgerQuestLogTab.Compose` is
  whatever order `QuestSystem.QuestsByCode` iterates — not something worth chasing for this change.

## Goals / Non-Goals

**Goals:**
- A Progression Framework Quest Link's click opens the Quest Log dialog, landed on the Quest Log tab.

**Non-Goals:**
- VS Quest: no equivalent investigation was done (vsquest is closed-source and not in
  `vs-source/`), so its Quest Links keep today's behavior (Handbook no-op) unless a future change
  investigates it. Not blocking — VS Quest and Progression Framework are mutually exclusive in
  practice (per `quest-auto-detect`).
- Deep-linking to the specific quest row or scrolling it into view — no seam exists for either (see
  Context). The player still has to find their quest in the list themselves once the tab is right.
- Un-collapsing a manually-collapsed quest — a second reflected field, decided against for now (see
  Risks); revisit only if it turns out to matter in practice.

## Decisions

**Open via `capi.Gui.LoadedGuis` + `TryOpen()`, not the hotkey.** `HotKey.Handler` is unset for this
dialog (see Context), so the only two options are simulating the keypress (fragile, steals real input
state) or finding the registered dialog instance directly and calling its own public `TryOpen()` —
the same pattern already proven for the base-game Handbook
(`capi.Gui.LoadedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")`, documented in
`VSAPI-NOTES.md`). Zero reflection, matches an established precedent exactly.

**Force the Quest Log tab via one reflected field (`currentTab`), self-disabling on failure.**
`GuiDialogLedger` exposes no public tab API. Reflecting `currentTab` (an enum field) to `QuestLog`
before calling `TryOpen()` (which internally calls the private `ComposeDialog()` and rebuilds from
current state) is the only way to land on the right tab. This mirrors the existing self-disabling
reflection posture already used for VS Quest's progress-count mirroring: wrapped in try/catch, logs
once, permanently disables itself for the session on any failure (field renamed/retyped), and the
plain open (Goal 1, no reflection) still succeeds even if this fails — reflecting a private field is
strictly additive to the zero-reflection open.

**No attempt at `collapsedQuests`.** Reflecting a second private field (a `HashSet<string>`) to force
one quest's collapse-state off is a small addition in principle, but `ApplyDefaultCollapseState` only
auto-collapses **completed** quests — an active quest a player just accepted is never collapsed by
default. The case this would help (a player who manually collapsed an active quest, then clicks its
Scribe Link) is narrow enough not to justify the extra reflection surface in this change.

## Risks / Trade-offs

- **[Risk]** The `currentTab` reflection breaks on a Progression Framework update (field renamed,
  retyped, or the dialog restructured into multiple classes). → **Mitigation**: self-disabling
  try/catch, logged once; the zero-reflection open (dialog appears, just possibly on the wrong tab)
  still works regardless, so this never regresses to "click does nothing" even in total reflection
  failure.
- **[Risk]** `TryOpen()` internally calls `ComposeDialog()`, which does real work (rebuilds
  `barValues`, quest list, scrollbar) — calling it from Scribe's click handler runs that mid-frame.
  → **Mitigation**: this is the exact same call the engine's own hotkey-driven toggle makes; no new
  risk beyond what already happens when the player presses `L` themselves.

## Migration Plan

Purely additive, client-only, no persisted state involved. Rollback is a plain revert.

## Open Questions

- Should a future change investigate whether VS Quest has an equivalent open-dialog seam? Deferred —
  not blocking, no VS Quest source available to investigate right now.
