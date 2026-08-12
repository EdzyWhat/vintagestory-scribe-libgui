# gui-foundation-policy Specification

## Purpose
Governs when Scribe may take on a third-party GUI framework as a hard mod dependency. Adopting any
GUI framework (LibGUI or otherwise) is gated on a throwaway spike clearing an explicit go/no-go
checklist recorded in the driving change's proposal, with "renders on this Apple Silicon Mac" as the
make-or-break gate. Emerged from the `explore-libgui-adoption` exploration; LibGUI (modid `gui`)
cleared this gate on 2026-07-23 (GO), which is why it may be adopted as Scribe's first hard GUI dep.
## Requirements
### Requirement: A GUI framework may become a hard dependency only after a passing spike

Scribe SHALL NOT adopt any third-party GUI framework (LibGUI or otherwise) as a hard, always-required
mod dependency until a throwaway proof-of-concept ("spike") has cleared an explicit go/no-go checklist
recorded in the driving change's proposal. Until then the framework MUST remain unreferenced by the
shipped mod (`src/Mod/Mod.csproj`, `src/Mod/modinfo.json`) — any framework reference lives only on the
throwaway spike branch and MUST NOT be merged unless the decision is "go". This extends the existing
"No new mod dependencies — ask before adding any" guardrail, which today permits only ConfigLib and
only as an optional `IsModEnabled`-gated soft dependency.

#### Scenario: Framework reference kept off the shipped mod pre-decision

- **WHEN** the GUI-framework adoption is still under assessment (no "go" recorded)
- **THEN** the shipped `src/Mod/Mod.csproj` and `src/Mod/modinfo.json` MUST NOT reference or depend on
  the framework
- **AND** any such reference exists only on a throwaway spike branch that is not merged

#### Scenario: Adoption proceeds only after every gate passes

- **WHEN** someone proposes adopting the framework for real
- **THEN** the proposal MUST cite a completed spike whose every checklist gate passed
- **AND** if any gate failed or is unanswered, the adoption MUST NOT proceed and the alternative path
  (continuing the custom `GuiElement` foundation) is taken instead

### Requirement: Apple-Silicon rendering is the make-or-break spike gate

Because the mod is developed on Apple Silicon and a prior tool (VSImGui) is unusable there for
native-rendering reasons, the spike checklist SHALL treat "the framework actually renders on this
Apple Silicon Mac" as a mandatory, blocking gate. A framework that cannot render on the development
machine MUST NOT be adopted regardless of its other merits.

#### Scenario: Non-rendering framework is rejected

- **WHEN** the spike shows the framework does not render on the Apple Silicon development machine
  (e.g. bundled native text-shaping libraries fail to load for the machine's architecture)
- **THEN** the framework MUST NOT be adopted
- **AND** the failure is recorded in the change so the dead end is not re-investigated blindly later

### Requirement: Reconcile is the default update path for animating surfaces
For a Scribe GUI surface that animates or that hosts interactions spanning an update (hover-gated
controls, an active caret, a press-then-release gesture), content changes SHALL be pushed by
reconciliation — a persistent content `StatefulWidget` updated via `SetState` — rather than by
`GuiBase.ForceRebuild()`. `ForceRebuild()` unmounts and recreates the entire widget tree, disposing
every `State`, `AnimationController`, and `RenderObject` and orphaning the pointer-capture the event
dispatcher holds as a concrete element reference; reconciliation preserves those matching elements
(and their identity) across the update. `ForceRebuild()` SHALL be reserved for genuinely-new trees —
switching between distinct views, seeding a fresh editor, lost-lock recovery — and for dev hot-reload.

#### Scenario: An animating surface updates by reconcile, not full rebuild
- **WHEN** a converted animating surface (e.g. the editor) changes its content in place (add, delete,
  reorder, toggle) while nothing about the surface's identity should reset
- **THEN** the surface updates via `SetState` on its persistent content, preserving hover, focus/caret,
  pointer-capture, and in-flight animation controllers, rather than calling `ForceRebuild()`

#### Scenario: ForceRebuild is retained for a genuinely-new tree
- **WHEN** the surface switches to a genuinely different tree (a different view, a fresh editor seed, or
  lost-lock recovery)
- **THEN** `ForceRebuild()` is still used, because there is no identity to preserve across that change

### Requirement: Reconcile-hosted rows are keyed by stable identity and never swap type at a slot
A surface converted to reconcile SHALL key its rows by stable logical identity (the row's TaskId, not
its array index), and SHALL keep the same widget type at a given slot across a row's state transitions
(for example, a departing/collapsing row is an internal state of one stable row widget, not a
different widget type spliced into that slot). This is required because LibGUI reconciliation reuses an
element only when its type and key match at its position; an index-based key that shifts, or a
type-swap at a slot, silently destroys that subtree's `State` (caret, focus, optimistic flags) exactly
as a full rebuild would.

#### Scenario: A row keeps its state across a list mutation because its key is stable
- **WHEN** rows are added, removed, or reordered on a reconcile-hosted surface
- **THEN** a surviving row that is being edited keeps its `State` (caret position, in-progress text,
  focus) because it is matched by its stable TaskId key rather than a shifting index

#### Scenario: A departing row does not change widget type at its slot
- **WHEN** a row transitions into its departing/collapsing animation
- **THEN** the slot keeps the same widget type (the transition is an internal state of the stable row
  widget), so reconciliation does not tear down and remount the subtree

