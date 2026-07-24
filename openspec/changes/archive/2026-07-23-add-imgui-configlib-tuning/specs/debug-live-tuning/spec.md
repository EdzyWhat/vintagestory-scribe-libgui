## ADDED Requirements

### Requirement: Live ImGui tuning is absent from Release builds
A Release-configuration build of the `Mod` project SHALL contain no reference to, or
runtime dependency on, VSImGui — the debug-tuning window and its underlying dependency
SHALL exist only in Debug-configuration builds.

#### Scenario: Release build has no VSImGui assembly
- **WHEN** `src/Mod/Mod.csproj` is built with `--configuration Release`
- **THEN** the output directory contains no VSImGui assembly and no code path in the
  built DLL calls into VSImGui

#### Scenario: Release build behaves identically for players without VSImGui installed
- **WHEN** a player without the VSImGui/ImGui mod installed loads a Release build of Scribe
- **THEN** the mod loads and functions exactly as it did before this change, with no
  missing-dependency warning or error related to VSImGui

### Requirement: Debug builds can live-tune lectern layout config via ImGui
In a Debug-configuration build, opening the lectern dialog SHALL make an ImGui window
available that exposes the row-list/culling-related `ScribeClientConfig` fields as
sliders, and changing a slider's value SHALL visibly update the open lectern dialog's
layout without closing and reopening it.

#### Scenario: Dragging a slider recomposes the open dialog
- **WHEN** a Debug build's lectern dialog is open and a developer drags the
  `VisibleListHeight` (or another exposed) slider in the ImGui debug window
- **THEN** the lectern dialog recomposes and visibly reflects the new value within the
  same or next rendered frame

#### Scenario: Debug window is scoped to the currently open dialog
- **WHEN** a lectern dialog is closed
- **THEN** its associated ImGui debug window is also closed/unregistered, and no stale
  window remains bound to a disposed dialog instance

#### Scenario: Slider drags do not persist to disk until an explicit save
- **WHEN** a developer drags a debug-window slider without triggering its save action
- **THEN** `scribe-client-config.json` on disk is unchanged until the save action is
  explicitly triggered (or the debug window is closed, if close-to-save is the chosen
  behavior)

### Requirement: ConfigLib exposes lectern layout config in-game
When the ConfigLib mod is installed and enabled, Scribe SHALL expose a subset of
`ScribeClientConfig`'s layout fields through ConfigLib's in-game settings panel, reading
from and writing to the same `scribe-client-config.json` file the mod already uses.

#### Scenario: ConfigLib edits are visible to Scribe on next load
- **WHEN** a player edits an exposed `ScribeClientConfig` field via ConfigLib's settings
  panel and the value is saved
- **THEN** the next `GuiDialogScribeLectern` instance loaded via `LoadModConfig` reflects
  the edited value

#### Scenario: Scribe functions without ConfigLib installed
- **WHEN** ConfigLib is not installed
- **THEN** Scribe loads and functions normally, with configuration only editable via the
  JSON file or the Debug-only ImGui window

#### Scenario: TextSizeScale is not exposed through ConfigLib
- **WHEN** a player opens ConfigLib's settings panel for Scribe
- **THEN** `TextSizeScale` is not among the exposed fields, since it already has its own
  in-GUI slider meant for routine in-play adjustment
