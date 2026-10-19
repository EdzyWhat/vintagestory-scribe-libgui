## Purpose

Gives the player a choice of how a detected quest accept is surfaced — a non-intrusive HUD banner or
a center-screen modal — and makes the non-intrusive option's call-to-action clearly readable while
keeping the modal safe against the known vanilla-dialog click hazard.

## ADDED Requirements

### Requirement: Quest Accept Policy offers two Prompt presentation styles
`ScribeQuestAcceptPolicy` SHALL offer four values: `Always`, `Never`, `PromptHud`, and `PromptPopup`.
`PromptHud` and `PromptPopup` both queue an accept-prompt exactly as the prior single `Prompt` value
did (same detection, same dedup, same queuing) — they differ only in how the queued prompt is
rendered. `ScribeQuestCompletionPolicy` is unchanged: a completion-prompt always renders via the HUD
banner style regardless of the player's Quest Accept Policy.

#### Scenario: PromptHud renders the HUD banner
- **WHEN** a quest accept is detected and Quest Accept Policy is `PromptHud`
- **THEN** the accept-prompt renders as the HUD banner (polished style, per the next requirement)

#### Scenario: PromptPopup renders the center modal
- **WHEN** a quest accept is detected and Quest Accept Policy is `PromptPopup`
- **THEN** the accept-prompt renders as the center-screen modal (per the modal requirement below)
  once it is safe to show

#### Scenario: A completion-prompt is unaffected by Quest Accept Policy
- **WHEN** a quest completion is detected and Quest Completion Policy is `Prompt`
- **THEN** the completion-prompt renders as the HUD banner regardless of the player's Quest Accept
  Policy value

### Requirement: The HUD banner draws attention and reads as three distinct buttons
The HUD banner's on-appear transition SHALL include a brief animation that draws the eye (distinct
from the row's steady state). Its three actions SHALL each render as a bordered, rounded button
rather than plain text: an accept action labeled "Track Quest" and colored green, a dismiss
action labeled "Not Now" and colored red, and a Settings action colored near-white (neutral). The
banner's title SHALL render as two lines: a gold label line (the prompt's own wording, e.g. "Add
quest to Scribe?:") followed by the quest's own name in the HUD's standard near-white row-text
color (not gold) on its own line below. The accept and dismiss actions' label text SHALL render in
a color independent of their button's own accent (not required to match it), for legibility against
the button's fill, and SHALL carry the same glow/shadow effect as the HUD's standard text. This
applies identically to an accept-prompt (`PromptHud`) and a completion-prompt (always this style).

#### Scenario: A new prompt animates on appear
- **WHEN** a new quest prompt is queued and rendered on the HUD
- **THEN** its banner plays a brief draw-attention animation distinct from its steady-state
  appearance

#### Scenario: The three actions are visually distinct buttons
- **WHEN** a quest prompt banner is shown
- **THEN** the "Track Quest", "Not Now", and Settings actions each render as their own
  bordered/rounded button, colored green, red, and near-white respectively, with the title's label
  line in gold, the quest name on its own line below in near-white, and the "Track Quest"/"Not Now"
  labels rendered in their own text color with glow, independent of their button's accent

### Requirement: The center modal is gated on the vanilla-dialog guard and never dropped
A `PromptPopup`-styled accept-prompt SHALL only open its center-screen modal when
`vanilla-dialog-guard` (the `vanilla-dialog-guard` capability) reports no vanilla dialog is currently
open. When a vanilla dialog is open at detection time, the prompt SHALL remain queued (exactly as a
`PromptHud` prompt would) and its modal SHALL open automatically once the guard next reports clear —
it SHALL NEVER be silently dropped or require the player to take any extra action to surface it. The
modal SHALL show "Add this Quest to Scribe?", the quest's name, and the same three colored/bordered
buttons ("Track Quest", "Not Now", Settings) as the polished HUD banner.

#### Scenario: No vanilla dialog open shows the modal immediately
- **WHEN** a quest accept is detected under `PromptPopup` and no vanilla dialog is open
- **THEN** the modal opens immediately, showing the "Add this Quest to Scribe?" prompt, the quest
  name, and the "Track Quest"/"Not Now"/Settings buttons

#### Scenario: A vanilla dialog open defers the modal, never drops it
- **WHEN** a quest accept is detected under `PromptPopup` while a vanilla dialog (e.g. the base-game
  Handbook, or Progression Framework's Ledger) is open
- **THEN** the modal does not open immediately, remains queued, and opens automatically as soon as no
  vanilla dialog is open, with no player action required to surface it

#### Scenario: The modal's content matches the banner's
- **WHEN** the center modal is shown
- **THEN** its "Track Quest"/"Not Now"/Settings buttons use the same colors and label-text
  treatment as the polished HUD banner's
