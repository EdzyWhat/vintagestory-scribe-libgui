## ADDED Requirements

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
