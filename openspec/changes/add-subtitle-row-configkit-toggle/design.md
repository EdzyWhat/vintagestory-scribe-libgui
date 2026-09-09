## Context

`ScribeTabHeader.Build` (`src/Mod/ScribeDialogBase.Layout.cs`) is a shared static helper, added by
`unify-tab-header-layout`, that builds the Row 2 subtitle + Row 3 per-tab controls + trailing
divider for every non-tablet dialog tab. It has ~10 call sites: 5 are `ScribeDialogBase`
subclasses that already hold a `modSystem` field (`GuiDialogScribeInbox`, `GuiDialogScribeNotebook`,
`GuiDialogScribeScriptorium`, `GuiDialogClockmakerNotebook`, `ScribeDialogBase.Guestbook.cs`); 5 are
plain `StatefulWidget`s with no `ScribeModSystem` reference at all (`ScribeReadContent`,
`ScribeEditorContent`, `ScribePinnedContent`, `ScribeAssignmentFormContent`, `ScribeInboxContent`),
instantiated by a `ScribeDialogBase`-owning caller that does have `modSystem`.

`ScribeVisualTuning` (`src/Mod/ScribeVisualTuning.cs`) already establishes the precedent this
change reuses: a small POCO loaded once via `api.LoadModConfig<ScribeVisualTuning>` in
`ScribeModSystem.StartClientSide`, exposed as the lazy-defaulting `modSystem.VisualTuning`
property, with no live-reload — a config-library (ConfigKit) edit takes effect on next relaunch.
Its `configlib-patches.json` manifest currently declares only `integer`/`float` settings; the
ConfigKit format also supports a `boolean` settings type in the same manifest, writing to the same
underlying file.

This change depends on `unify-tab-header-layout` (Row 2 itself) having been archived, so that
`scribe-dialog-base`'s main spec already contains the "three-row header" requirement this change's
delta modifies. If `unify-tab-header-layout` is still unarchived when this change is archived, its
`## ADDED Requirements` needs archiving first, or the two changes' deltas won't reconcile (see
`openspec-archive-order-header-drift` precedent).

## Goals / Non-Goals

**Goals:**
- One boolean, read at GUI-build time, hides Row 2 uniformly across every tab that currently
  renders it.
- Reuse the existing `ScribeVisualTuning` load/manifest mechanism exactly — no new config file, no
  new `LoadModConfig`/`StoreModConfig` call, no new mod-system field.
- Zero new plumbing for the widgets that don't already receive settings-derived values: thread the
  flag the same way theme/font-scale/shade values are already threaded into these widgets today.

**Non-Goals:**
- No checkbox in Scribe's own in-game Settings screen (ConfigKit-only, matching the
  `ScribeVisualTuning` precedent). If the author later wants this live-toggleable without a
  relaunch, that's a separate change that would move the flag onto `ScribePlayerSettings` instead —
  not attempted here.
- No live-reload / no re-render of already-open dialogs when the underlying file changes; identical
  to every other `ScribeVisualTuning` value.
- No change to Row 3 content, the divider's positioning math, or any tab's scrollable content — only
  Row 2's presence is conditional.

## Decisions

**Where the boolean lives: extend `ScribeVisualTuning`, not a new class/file.**
Alternative considered: a new `ScribeUiToggles` class with its own config file, keeping
`ScribeVisualTuning`'s doc-scope ("two purely-cosmetic rendering subsystems") textually accurate.
Rejected: a single boolean doesn't justify a second `LoadModConfig` call, a second manifest file,
and a second doc-comment block — that's the premature-abstraction failure mode this project's
guardrails call out. `ScribeVisualTuning`'s doc comment is updated to describe three knobs instead
of two rather than spinning up parallel plumbing for one flag.

**Manifest shape: add a `"boolean"` block to the existing `configlib-patches.json`, same `file`.**
The ConfigKit format's object-form `settings` map is keyed by type (`integer`, `float`, `boolean`,
...) and all types in one manifest write to the same target file (`scribe-visual-tuning.json`).
Adding `"boolean": { "ShowSubtitleRow": {...} }` alongside the existing `"integer"`/`"float"` blocks
requires no new file and no new `"file"` declaration.

**Gating mechanism: a new parameter on `ScribeTabHeader.Build`, not a self-reading static.**
`Build` stays a pure function of its parameters (no static/singleton access to `ScribeModSystem`),
consistent with how `SupportsTabHeader` already gates the call itself rather than being read inside
`Build`. Each of the ~10 call sites passes `modSystem.VisualTuning.ShowSubtitleRow` (the 5
`ScribeDialogBase`-subclass sites read it directly; the 5 plain-widget sites receive it as one new
constructor property, exactly mirroring how theme/font-scale/shade values already reach these same
widgets from their owning dialog).

**Row 3 and the divider are unaffected by the flag.** When Row 2 is hidden, `Build`'s internal
`Column` simply omits the subtitle widget; Row 3 (if present) and the trailing divider render
immediately under Row 1 instead of under Row 2, at the same 8-layout-unit offset the "end of the
header block" language in the spec already generalizes to. No separate padding/divider-position
branch is needed — the existing "end of header block" framing already treats Row 2's absence the
same way it treats Row 3's absence today.

## Risks / Trade-offs

- [Widening `ScribeVisualTuning`'s documented scope to include a layout toggle, not just rendering
  constants] → Acceptable: the doc comment is updated to say so explicitly; the class's mechanics
  (load-once, ConfigKit-only, ConfigKit manifest) are identical regardless of what the value
  controls.
- [A player toggles the setting via ConfigKit mid-session and is confused it doesn't take effect
  until relaunch] → Matches every other `ScribeVisualTuning` value already; no new confusion
  surface introduced by this change specifically.
- [Threading one more constructor property into 5 widgets is mechanical, repetitive work across
  many files] → No alternative avoids it without adding a static accessor (rejected — see above);
  the diff per site is one line.

## Migration Plan

No persisted-state migration: a missing/absent key in `scribe-visual-tuning.json` already falls
back to a built-in default, so existing installs with no file, or a file predating this change,
see `ShowSubtitleRow = true` (identical to today's unconditional rendering) with no action needed.
