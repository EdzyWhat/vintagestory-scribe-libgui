## ADDED Requirements

### Requirement: A tab bar navigates between the read/edit page and the settings page
A Scribe dialog SHALL present a navigation tab bar that lets the player switch between the dialog's
read/edit page and its settings page. The tab bar SHALL replace the single right-aligned gear button as
the navigation affordance and SHALL be present on every page (read, editor, and settings) so navigation
is reachable from any view, not only the read/editor views. The tab bar SHALL expose at least a
read/edit tab (the same page shown in read or edit mode, labeled for the current mode) and a settings
tab, each labeled and individually selectable.

#### Scenario: The tab bar replaces the gear button
- **WHEN** a player opens the Lectern dialog
- **THEN** a labeled navigation tab bar is shown for switching pages, and no standalone gear button is
  the navigation affordance

#### Scenario: The tab bar is present on the settings page
- **WHEN** the player is on the settings page
- **THEN** the same tab bar is shown, so the player can navigate back to the read/edit page from it
  (settings is no longer a separate chrome-less view)

### Requirement: Selecting a tab drives the real navigation methods, not a local flag flip
Selecting a tab SHALL invoke the dialog's existing navigation methods rather than directly mutating a
view-mode field. Selecting the edit affordance SHALL call the lock-acquiring editor-access path,
selecting read SHALL call the switch-to-read path, and selecting settings / leaving settings SHALL call
the open-settings / close-settings paths. The tab bar SHALL NOT bypass these methods by flipping a
`isEditorMode` / `isSettingsMode` flag itself, so all lock and return-path semantics remain owned by
those methods.

#### Scenario: Tabs delegate to navigation methods
- **WHEN** the player selects any tab
- **THEN** the tab bar invokes the corresponding navigation method (switch-to-read, request-editor-access,
  open-settings, or close-settings) rather than setting a view-mode flag directly

### Requirement: The active tab is visually animated
The tab bar SHALL animate the transition of the active tab rather than swapping its appearance
instantly. The active tab SHALL be distinguished by an animated highlight (color) and/or an animated
scale, driven by LibGUI's animation primitives (e.g. `AnimatedContainer` / `AnimatedScale`), so selecting
a tab produces a smooth visual transition on the newly active tab.

#### Scenario: Selecting a tab animates the highlight
- **WHEN** the player selects a different tab
- **THEN** the newly active tab animates into its active appearance (an easing highlight and/or scale)
  rather than changing appearance in a single instant frame

### Requirement: The tab bar's animation state survives dialog rebuilds
The tab bar SHALL be a stateful widget whose element identity is stabilized by a `Key`, so its animation
`State` (and its animation controllers) survive the dialog's `ForceRebuild` view swaps rather than being
torn down and recreated on every rebuild. The tab bar's animation controllers SHALL be disposed when its
`State` is disposed, so no controller leaks across the dialog's lifetime.

#### Scenario: Animation state persists across a rebuild
- **WHEN** the dialog rebuilds (for example, a view swap or a settings-driven rebuild) while the tab bar
  is shown
- **THEN** the tab bar's animation state is preserved via its keyed `State`, and no mid-flight animation
  is reset by the rebuild

#### Scenario: Controllers are disposed with the state
- **WHEN** the tab bar's `State` is disposed
- **THEN** every animation controller it owns is disposed, leaking none

### Requirement: Tab chrome degrades gracefully before pixel-art assets exist
The tab bar SHALL render with a flat, animated placeholder chrome (e.g. an `AnimatedContainer` fill) when
no pixel-art tab sprites are present, so the full navigation structure is usable and testable in-game
before any art is drawn. When crisp pixel-art tab sprites are supplied, the chrome MAY be upgraded to a
nearest-neighbor `NineSliceBox` frame without changing the tab bar's navigation behavior.

#### Scenario: Tabs render with a flat placeholder before art
- **WHEN** the tab bar is shown and no pixel-art tab sprites exist yet
- **THEN** each tab renders with a flat animated placeholder chrome, fully labeled and selectable, and
  the active-tab animation still plays

#### Scenario: Pixel-art chrome is an art-only swap
- **WHEN** crisp pixel-art tab sprites are later supplied and rendered via `NineSliceBox`
- **THEN** the tab bar's selection and animation behavior is unchanged — only the chrome appearance
  differs
