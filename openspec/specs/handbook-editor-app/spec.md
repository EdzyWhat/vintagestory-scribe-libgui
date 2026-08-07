# handbook-editor-app Specification

## Purpose
TBD - created by archiving change add-handbook-web-editor. Update Purpose after archive.
## Requirements
### Requirement: Local server over mod assets

The tool SHALL run as a local server that exposes the assembled handbook entries to a
browser client and accepts save requests, reading from and writing to a configured
mod-assets root. The server SHALL be startable with a documented single command and default
to binding localhost only.

#### Scenario: Server starts and serves entries

- **WHEN** the author starts the server pointed at the mod-assets root and opens the app in a
  browser
- **THEN** the app loads and lists the discovered handbook entries

#### Scenario: Localhost-only by default

- **WHEN** the server starts without an explicit LAN/host override
- **THEN** it binds to localhost and does not accept remote connections

### Requirement: Dock launcher

The tool SHALL ship a double-click launcher script (a macOS `.command`) that starts the
server and opens the app in a browser, matching the author's existing local-tool launch
pattern. The README SHALL document how to place a Finder alias to that launcher in
`~/Launchers/` for the macOS Dock.

#### Scenario: Double-click launch

- **WHEN** the author double-clicks the launcher `.command`
- **THEN** the server starts and the app opens in a browser without further manual steps

#### Scenario: Dock placement documented

- **WHEN** the author follows the README
- **THEN** it explains creating a `~/Launchers/` alias to the launcher for dragging onto the
  Dock

### Requirement: Three-column workspace

The app SHALL present a three-column workspace: a **library** column (left), an **editor**
column (middle), and a **preview** column (right). The library column SHALL be collapsible,
and collapsing it SHALL give its space to the editor and preview.

#### Scenario: Columns present

- **WHEN** the app is open with an entry selected
- **THEN** the library, editor, and preview are shown as three side-by-side columns

#### Scenario: Library collapses

- **WHEN** the author collapses the library column
- **THEN** the library is hidden and the editor and preview expand to use the freed space

### Requirement: Navigable entry library

The library column SHALL list all discovered entries — guide articles and item/block entries
alike — grouped/labeled by kind and owning subject (e.g. the block/item name or guide-page
title), and selecting one SHALL open it in the editor and preview columns.

#### Scenario: Entries grouped and listed

- **WHEN** the app loads
- **THEN** the library lists every discovered entry, grouped so guide articles and item/block
  entries are distinguishable

#### Scenario: Author selects an entry

- **WHEN** the author clicks an entry in the library
- **THEN** the editor shows that entry's ordered sections with their current title and body
  text, and the preview renders it

### Requirement: Link-to-clipboard generator

The library SHALL offer, per entry, an action that copies that entry's correct `handbook://`
link to the clipboard, using the canonical page code — including the concrete variant code for
variant items (e.g. `item-scribe:scribetablet-clay-red`).

#### Scenario: Copy an entry link

- **WHEN** the author triggers the copy-link action on a library entry
- **THEN** the clipboard receives that entry's correct `handbook://<pagecode>` link

#### Scenario: Variant item link uses a concrete variant

- **WHEN** the author copies the link for a variant item (e.g. the clay Tablet)
- **THEN** the copied link contains a concrete variant code, not a bare base code

### Requirement: High-fidelity assembled section preview

For a selected entry, the app SHALL display its sections in order, and for each section show
both the editable body source and a high-fidelity preview of Vintage Story's supported markup
subset. The preview SHALL render body text inside a fixed column matching the in-game
handbook detail-text width (500 logical units) with a visible width indicator, and SHALL
approximate the handbook's typography (font size, weight, line-height) and link styling
closely enough that line wrapping and vertical extent track what the player sees. The preview
SHALL render at minimum `<strong>` bold, `<br>` line breaks, and `handbook://` links styled so
resolvable vs. unresolvable targets are visually distinguishable, and SHALL render unknown
tags literally.

#### Scenario: Markup rendered at the game's text width

- **WHEN** a section body contains `<strong>`, `<br>`, and a `handbook://` link
- **THEN** the preview shows bold text, a line break, and the styled link, wrapped within the
  fixed 500-unit-equivalent text column with its width indicator visible

#### Scenario: Real-estate is judgeable

- **WHEN** the author views an entry's preview
- **THEN** the preview conveys how much vertical space the entry occupies at the game's text
  width, so an over-long entry is visibly apparent

#### Scenario: Unresolvable link visibly flagged

- **WHEN** a section body links to a target the model reports as unresolvable
- **THEN** the preview marks that link as broken/unresolvable

#### Scenario: Unknown tag shown literally

- **WHEN** a section body contains a tag outside the supported subset
- **THEN** the preview shows that tag literally rather than silently dropping it

### Requirement: Source editing with formatting helpers

The app SHALL let the author edit each section's title and body as raw Vintage Story markup
source, such that what is shown in the editor is exactly what is saved (no WYSIWYG round-trip
conversion). The editor SHALL provide formatting-helper actions that insert supported markup
at the caret or around the current selection — at minimum `<strong>…</strong>` bold, `<br>`
line breaks, and an `<a href="handbook://…">…</a>` link scaffold. Edits SHALL reflect in the
preview before saving.

#### Scenario: Editing body updates preview

- **WHEN** the author changes a section's body source in the editor
- **THEN** the preview updates to reflect the new markup without a save

#### Scenario: Formatting helper inserts markup

- **WHEN** the author selects text and triggers the bold helper (or places the caret and
  triggers the line-break or link helper)
- **THEN** the corresponding VS markup is inserted into the source at that position

#### Scenario: What is shown is what is saved

- **WHEN** the author saves
- **THEN** the persisted body text is byte-for-byte the source shown in the editor (no
  generated/normalized markup differing from what was displayed)

### Requirement: Structure editing

The app SHALL let the author add a section, remove a section, and reorder sections within an
entry, and edit or insert `handbook://` cross-link targets, with the target of a link
validated against known pages before save.

#### Scenario: Reorder sections

- **WHEN** the author moves a section to a new position within the entry
- **THEN** the editor reflects the new order, which is what a subsequent save persists

#### Scenario: Add and remove sections

- **WHEN** the author adds a new empty section, or removes an existing one
- **THEN** the editor shows the updated section list, pending save

#### Scenario: Link target validated

- **WHEN** the author enters a `handbook://` target that matches no known page
- **THEN** the app warns that the target is unresolvable before the author saves

### Requirement: Save and feedback

The app SHALL provide an explicit save action that writes the current entry back through the
server, and SHALL report success or failure (including a validation-abort) to the author
without silently discarding edits.

#### Scenario: Successful save reported

- **WHEN** the author saves a valid edited entry
- **THEN** the server writes the changes and the app confirms success

#### Scenario: Failed save surfaced

- **WHEN** a save is rejected (e.g. would produce invalid JSON, or targets a path outside the
  assets root)
- **THEN** the app reports the failure and the author's in-progress edits remain in the
  editor

### Requirement: Before/after snapshot toggle

On opening an entry, the app SHALL capture a session-original baseline of that entry's state
and retain it across subsequent saves within the session (the baseline SHALL NOT reset to the
last-saved state). The app SHALL provide a toggle that switches both the editor and preview
columns between this baseline ("before") and the current working state ("after"), so the
author can compare cumulative progress against where the entry started.

#### Scenario: Baseline captured on open

- **WHEN** the author opens an entry
- **THEN** the app records its current state as the session-original baseline

#### Scenario: Toggle updates both columns

- **WHEN** the author toggles to "before"
- **THEN** both the editor and preview columns display the baseline state; toggling back to
  "after" restores the current working state

#### Scenario: Baseline survives a save

- **WHEN** the author saves and then toggles to "before"
- **THEN** the displayed baseline is still the session-original state (the state at first
  open), not the just-saved state

