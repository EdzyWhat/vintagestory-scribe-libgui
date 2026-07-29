## ADDED Requirements

### Requirement: IScribeDocumentHost abstracts the block-entity surface used by the dialog
The mod SHALL define an `IScribeDocumentHost` interface in `src/Mod/` that expresses the exact
set of members the shared dialog base requires from any Scribe block entity. The interface SHALL
declare: `BlockPos Pos`, `ScribeDocument Document`, `bool IsLockedByOther(string viewerUid)`,
`void ApplyLocalOptimisticEdit(ScribeDocument doc)`, `ScribeBackdropSpec BackdropSpec`,
`ScribeLayout GetLayout(float pixelArtSize)`, and `string DefaultDocumentTitle`. No other members
SHALL be required; the interface SHALL NOT expose block-entity lifecycle methods (Initialize,
OnBlockRemoved, etc.).

#### Scenario: BlockEntityScribeLectern satisfies the interface
- **WHEN** `BlockEntityScribeLectern` is compiled after this change
- **THEN** it implements `IScribeDocumentHost` and the compiler accepts it with no errors

#### Scenario: A no-lock host (future Notebook) can implement the interface
- **WHEN** a hypothetical `BlockEntityScribeNotebook` implements `IScribeDocumentHost`
- **THEN** it can return `false` from `IsLockedByOther` for all callers, and the base dialog
  receives no lock signal — the editor affordance reads as freely available

### Requirement: ScribeLayoutProportions expresses per-item column/height ratios as an overridable record
The mod SHALL define a `ScribeLayoutProportions readonly record struct` with `init`-only float fields
for all the fractional proportions that drive the dialog layout (title-bar height fraction, inner-box
height fraction, side-column fraction, title-buttons row fractions). It SHALL expose a `Default`
static singleton whose values reproduce the v1 Lectern layout exactly. A future item SHALL be able
to override individual fields using C# `with` expressions without inheriting from any class.

#### Scenario: Default proportions reproduce the Lectern's v1 layout
- **WHEN** `ScribeLayout` is constructed with `ScribeLayoutProportions.Default` and the Lectern's
  pixel-art width
- **THEN** every derived dimension (TitleBarH, InnerW, SideColW, TasksColW, etc.) equals the value
  it had before this refactor

#### Scenario: A with-expression override changes only the targeted field
- **WHEN** a proportions value is created via `ScribeLayoutProportions.Default with { SideColFrac = 0.12f }`
- **THEN** only `SideColW` and `TasksColW` change; all other derived dimensions are identical to
  the Default

### Requirement: ScribeLayout derives all dimensions from W, AspectH, and ScribeLayoutProportions
The `ScribeLayout` struct SHALL replace `LecternLayout` and SHALL be the single source of truth for
all dialog-dimension arithmetic. It SHALL accept `float W`, `float AspectH`, and a
`ScribeLayoutProportions` value (with a convenience two-arg constructor that uses `Default`).
`TasksColW` SHALL be derived as `(1 − 2·SideColFrac)·W` so the three columns always sum to `InnerW`
exactly, regardless of `SideColFrac`.

#### Scenario: Three columns sum to InnerW
- **WHEN** a ScribeLayout is constructed with any valid ScribeLayoutProportions
- **THEN** SideColW + TasksColW + SideColW equals InnerW exactly (no overflow, no gap)
