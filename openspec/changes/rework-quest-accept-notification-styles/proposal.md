## Why

Today's quest-accept notification is a single, fixed HUD banner style, and it's easy to miss: no
animation calls attention to it, and its Accept/Dismiss/Settings row reads as plain text rather than
clearly-actionable buttons. Players want a choice here rather than one fixed style — and a genuinely
different option (a center-screen modal) is only safe to build now that `vanilla-dialog-guard`
(Change 3, this session) exists to keep it from contesting a click against a vanilla dialog it can't
out-paint.

## What Changes

- `ScribeQuestAcceptPolicy` gains a fourth value: `Always | Never | PromptHud (renamed from Prompt,
  same underlying byte value) | PromptPopup` (new). `ScribeQuestCompletionPolicy` is unchanged
  (Non-Goal — see design.md).
- The existing HUD banner (used by `PromptHud` and, unchanged, by every completion-prompt regardless
  of the accept policy) gets a visual pass: a brief on-appear animation to draw the eye, and its
  three actions rendered as clearly-bordered, color-coded buttons — the accept action, labeled
  "Track Quest" (renamed from "Link" — the prior label read as a hyperlink-style connection that
  doesn't apply, and didn't convey that the quest gets recorded into a Scribe document), colored
  green; the dismiss action, labeled "Not Now" (renamed from "Dismiss" — ambiguous about whether it
  dismissed the quest or just the prompt), colored red (retuned from orange); Settings staying
  near-white/neutral. The HUD banner's title now renders as two lines: a gold label line ("Add
  quest to Scribe?:" / "Mark quest done:", wording changed from "Link quest: {quest name}?") followed
  by the quest's own name (plus per-kind trailing punctuation) on its own line below in the HUD's
  standard near-white row-text color, not gold — so the prompt's boilerplate wording reads visually
  distinct from the quest's actual name. The accept and dismiss buttons' label text renders in that
  same near-white color, decoupled from their own accent color, so the label reads as ordinary HUD
  text sitting atop the colored button fill (fill-alpha per interaction state also retuned, see
  design.md) — NOT pure white, which turned out to silently fail to render at all (see design.md's
  landmine note) — including the same glow the title uses, so the label reads exactly like ordinary
  HUD text.
- New: a center-screen modal notification style (`PromptPopup`) — "Add this Quest to Scribe?" / quest
  name / Track Quest, Not Now, Settings buttons (same color scheme as the polished banner, same
  near-white label-text treatment, no glow — the modal has an opaque backdrop, not an in-world
  overlay) — shown only when `vanilla-dialog-guard` reports no vanilla dialog is currently open;
  otherwise deferred until it clears (never dropped).
- Settings: Quest Accept Policy's dropdown gains the fourth option with updated helptext.
- Investigated a hover visual complaint on all three quest-prompt buttons (Track Quest/Not Now/
  Settings, HUD banner and center modal alike): they appear to grow downward on hover instead of
  from their own center. Root-caused to a hard-coded, non-overridable hover shadow inside LibGUI's
  own stock `Button` widget (see design.md). A purpose-built replacement widget was tried and
  reverted — not worth maintaining a duplicate of library button logic for one button's sake (direct
  user direction). The buttons keep using the stock `Button`; the downward-hover-shadow look is an
  accepted stock-LibGUI quirk.

## Capabilities

### New Capabilities
- `quest-accept-notification-style`: the two accept-prompt presentation styles (HUD banner, center
  modal), their visual requirements, and the modal's guard-gated timing.

### Modified Capabilities
- `settings-tab`: Quest Accept Policy's dropdown gains a fourth option (`Prompt via Popup`); Quest
  Completion Policy is unchanged.

## Impact

- `src/Core/ScribeQuestPolicy.cs`: `ScribeQuestAcceptPolicy` enum rename + new value.
- `src/Mod/HudScribePins.cs`: banner visual polish (animation, bordered/colored buttons); new modal
  rendering path, gated on `ScribeVanillaDialogGuard` (Change 3).
- `src/Mod/ScribeModSystem.Quest.cs`: no behavioral change to detection/queueing — `QueuePrompt`
  already carries everything the new render path needs (`Source`, `QuestCode`, `Title`,
  `IsCompletion`); only which policy value routes to which render path changes.
- Settings dialog content (wherever the Quest Accept Policy dropdown is built) + lang keys for the
  new option and updated helptext.
- No `src/Core/` changes beyond the enum itself (pure data, no VS API).
