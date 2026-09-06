## 1. Core: policy enum

- [x] 1.1 In `src/Core/ScribeQuestPolicy.cs`, rename `ScribeQuestAcceptPolicy.Prompt` to `PromptHud`
      (same byte value, `2`) and add `PromptPopup = 3`. Update every reference
      (`ScribeModSystem.Quest.cs`, `ScribePlayerSettings.cs`'s `NormalizeQuestAcceptPolicy`, settings
      dialog code) to the new name. Verify `dotnet build` (Core + Mod) succeeds with no leftover
      `Prompt` reference.
- [x] 1.2 Add/update a unit test confirming `NormalizeQuestAcceptPolicy` accepts all four values and
      an out-of-range byte still normalizes to `PromptHud` (the existing default). Verify `dotnet test`
      passes.

## 2. HUD banner visual polish

- [x] 2.1 In `HudScribePins`' quest-prompt rendering, add the on-appear draw-attention animation,
      reusing the existing `ScribeAnimatedList`/`ScribeRowSizeAnimation` infrastructure (see
      design.md). Verify `dotnet build src/Mod` succeeds.
- [x] 2.2 Render Link/Dismiss/Settings as bordered, rounded-rect buttons: Link green, Dismiss orange,
      Settings near-white/neutral; keep the quest title gold. Pull colors from the active theme's
      semantic roles where defined, falling back to fixed values otherwise (design.md Risk
      mitigation). Verify `dotnet build src/Mod` succeeds.
- [x] 2.3 Confirm (read the render path) this polish applies identically to a completion-prompt
      (`IsCompletion == true`) — no accept-only special-casing leaks into the banner's shared render
      code. Verify by manual read/diff, no behavior test needed beyond Task 5's playtests.

## 3. Center modal

- [x] 3.1 Add a new `GuiDialog` (mirroring `ScribeSettingsDialog`'s `WindowFrame`-hosted construction,
      `DrawOrder => 0.2`) showing "Add this Quest to Scribe?", the quest's title, and the same
      Link/Dismiss/Settings buttons/colors as the polished banner. Verify `dotnet build src/Mod`
      succeeds.
- [x] 3.2 Wire its three buttons to the existing `AcceptQuestPrompt`/`DismissQuestPrompt` calls (Link
      = accept, Dismiss = dismiss, Settings = open Scribe Settings then dismiss the modal), matching
      the banner's existing action semantics exactly. Verify `dotnet build src/Mod` succeeds.
- [x] 3.3 Add the routing check: for each pending, non-completion `ScribeQuestPrompt`, when
      `MySettings.QuestAcceptPolicy == PromptPopup`, render via the modal instead of the banner.
      Verify `dotnet build src/Mod` succeeds and a completion-prompt is unaffected (still always
      banner).
- [x] 3.4 Gate the modal's auto-open on `ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen` (Change 3):
      check on the same tick cadence as detection; if a vanilla dialog is open, leave the prompt
      queued and re-check on a later tick rather than opening immediately. Verify `dotnet build
      src/Mod` succeeds.

## 4. Settings

- [x] 4.1 Update the Quest Accept Policy dropdown (wherever it's built in the settings content) to
      offer four options with updated labels/helptext ("Prompt via Scribe HUD", "Prompt via Popup").
      Leave Quest Completion Policy's dropdown unchanged (three options). Verify `dotnet build src/Mod`
      succeeds.
- [x] 4.2 Add/update lang keys for the new dropdown option and any updated helptext. Verify the
      lang file is valid JSON and keys resolve (no raw key shown in-game — confirmed in Task 5's
      playtest).

## 5. Verification

- [x] 5.1 `dotnet test` (Core) green.
- [x] 5.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [x] 5.3 Manual playtest: set Quest Accept Policy to Prompt via Scribe HUD, trigger a quest accept —
  - Confirmed 2026-09-05: TESTING.md `00000096` "(no note)" (submission 2026-09-05T22-19-48)
      confirm the banner animates in and its three actions read as distinct colored/bordered buttons.
- [x] 5.4 Manual playtest: set Quest Accept Policy to Prompt via Popup with no vanilla dialog open,
  - Confirmed 2026-09-05: TESTING.md `00000097` "This worked, and should be the default. Change this setting to be the default in Scribe Settings." (submission 2026-09-05T22-19-48)
      trigger a quest accept — confirm the modal opens immediately with the correct copy and buttons.
- [x] 5.5 Manual playtest: set Quest Accept Policy to Prompt via Popup, open the base-game Handbook
  - Confirmed 2026-09-05: TESTING.md `00000098` "(no note)" (submission 2026-09-05T22-19-48)
      (or Progression Framework's Ledger), then trigger a quest accept — confirm the modal does NOT
      open while the vanilla dialog is open, and opens automatically once it's closed.
- [ ] 5.6 Manual playtest: trigger a quest completion (any Quest Accept Policy setting) — confirm it
      always renders as the polished HUD banner, never the modal.
- [x] 5.7 Manual playtest: from the modal, click Settings — confirm Scribe Settings opens and the
  - Confirmed 2026-09-05: TESTING.md `0000009a` "(no note)" (submission 2026-09-05T22-19-48)
      modal dismisses without also accepting or discarding the quest.
- [x] 5.8 Regression check: with a supported quest backend NOT installed, confirm neither Quest
  - Confirmed 2026-09-05: TESTING.md `0000009b` "(no note)" (submission 2026-09-05T22-19-48)
      policy row appears in Settings (unchanged gating).

## 6. Post-ship wording & color revision

- [x] 6.1 Update the accept-prompt and dismiss-prompt lang keys (currently rendering "Link"/
      "Dismiss") to "Track Quest" and "Not Now" respectively, in both the HUD banner and center
      modal call sites (they share the same lang keys). Verify the lang file is valid JSON and the
      new strings render in-game with no raw key shown. DONE: `en.json`'s
      `scribe-hud-questprompt-accept-button`/`-dismiss-button` keys updated (label went through one
      further revision, "Track in Scribe" → "Track Quest", per follow-up user direction); the
      accept-prompt title (`scribe-hud-questprompt-accept-title`) was also reworded, "Link quest:
      {0}?" → "Add quest to Scribe?: {0}"; both call sites read through the same lang keys, JSON
      validated.
- [x] 6.2 Retune `ScribeRowConstants.QuestPromptLinkColor`, `QuestPromptDismissColor` (green → the
      tuned dark green; orange → the tuned red), `QuestPromptSettingsColor`, and the banner title
      text color to the values confirmed via `tools/quest-prompt-colors/index.html` (see design.md's
      new Decision). Verify `dotnet build src/Mod` succeeds. DONE, with one correction found while
      implementing: the title color literal only exists in `HudScribePins.BuildQuestPromptBanner`
      (now promoted to a new `QuestPromptTitleColor` constant) — the center modal
      (`GuiDialogScribeQuestPrompt`) titles its prompt with the active theme's `ColorScheme.Primary`,
      not this literal, so there was no matching modal-side title color to retune. `dotnet build
      src/Mod` succeeds.
- [x] 6.3 Retune `ScribeQuestPromptActions.AccentButton`'s per-interaction-state fill alpha from
      0.16/0.28/0.38 to 0.29/0.48/0.68. Verify `dotnet build src/Mod` succeeds. DONE.
- [x] 6.4 Add an optional `textColor` parameter to `AccentButton` (default `accent`, so the
      Settings call site is unaffected); pass a decoupled color for the accept ("Track Quest") and
      dismiss ("Not Now") buttons at both call sites (HUD banner + center modal). Verify `dotnet
      build src/Mod` succeeds and both call sites still go through the one shared helper (no
      divergence between the two presentation styles). DONE — also threaded the parameter through
      `BuildLinkControl` (the accept button's own builder, used by both call sites), excluding its
      0-candidates disabled/tooltip branch, which uses a distinct muted accent unrelated to the
      green accept color. `dotnet build src/Mod` succeeds; `dotnet test` (Core, 729 tests) passes.
- [x] 6.4a Playtest turned up a real bug in 6.4: passing pure white (`Vector4.One`) rendered as
      **black** in-game (screenshot evidence). Root cause: the LibGUI 3.1.0 `TextStyle.Merge`
      landmine (`VSAPI-NOTES.md`) — white is `Merge`'s "unset" sentinel, so an explicit pure-white
      override silently inherits an ancestor `DefaultTextStyle`'s color instead. Per follow-up user
      direction, fixed by switching to `ScribeRowConstants.HudStandardTextColor` (the HUD's own
      near-white `(0.93, 0.93, 0.93, 1)` row-text color — not the sentinel value, so it overrides
      correctly) plus the HUD's own glow (`GlowWidth`/`GlowColor`) on the banner's two buttons only
      (the modal has no glow — opaque backdrop, not an in-world overlay). `BuildRow`'s own literal
      near-white was also folded onto the new shared constant to prevent future drift. `dotnet build
      src/Mod` succeeds.
- [x] 6.5r Follow-up HUD-banner-only revision (title layout/color; retuned the gold slightly more
      yellow; button-text glow briefly removed then restored): in `HudScribePins.BuildQuestPromptBanner`,
      split the title into two stacked lines instead of one — line 1 is the gold label ("Add quest
      to Scribe?:" / "Mark quest done:", from the now placeholder-free
      `scribe-hud-questprompt-accept-title`/`-complete-title` lang keys), line 2 is the quest's own
      name (plus a per-kind suffix — "?" for completion, empty for accept, from new `-title-suffix`
      lang keys) rendered in `ScribeRowConstants.HudStandardTextColor` (off-white, matching ordinary
      HUD row text) instead of gold — so the prompt's own boilerplate reads visually distinct from
      the quest's actual name. Both lines keep the title's existing glow (unchanged, glow was never
      in scope for the title). `QuestPromptTitleColor` retuned to `(1.0, 0.9529, 0.6392, 1.0)` per
      user follow-up ("a bit more yellow"). The accept/dismiss button LABEL text briefly dropped its
      glow (`BuildLinkControl`/dismiss `AccentButton` calls omitted `glowWidth`/`glowColor`), then the
      user reversed that ("bring back the shadow on the button text") — both calls now pass
      `glowWidth: GlowWidth, glowColor: glow` again, same as 6.4a originally shipped; net effect is
      unchanged from 6.4a on this axis. The center modal (`GuiDialogScribeQuestPrompt`) renders the
      quest name via its own `shownPrompt.Title` Text, not these lang keys, and never had button-label
      glow — unaffected by this task either way. `dotnet build src/Mod` succeeds.
- [x] 6.5 Manual playtest: trigger an accept-prompt under both `PromptHud` and `PromptPopup` —
  - Confirmed 2026-09-05: TESTING.md `0000009c` "I have modified the en.json file a bit. I am comfortable with how it reads, but if this test is accurate, we may need to update the spec." (submission 2026-09-05T22-19-48)
      confirm the buttons read "Track Quest" / "Not Now" / "Settings", accept is green, dismiss
      is red, both labels render as legible near-white text with glow (matching standard HUD text)
      against their fill, and the HUD banner's title renders as two lines: a gold "Add quest to
      Scribe?:" label followed by the off-white quest name on its own line below.
- [ ] 6.6 Manual playtest: trigger a completion-prompt — confirm its HUD banner shows the same
      retuned colors, labels, and near-white-with-glow button text treatment as the accept-prompt
      banner (no accept-only special-casing), with its own two-line title ("Mark quest done:" in
      gold, then the off-white quest name followed by "?").

## 7. Post-ship hover-growth investigation (all three quest-prompt buttons) — reverted, no net change

- [x] 7.1 Root-caused a user-reported visual bug: the "Track Quest"/"Not Now"/"Settings" buttons
      appeared to grow downward on hover rather than from their center. Decompiled the actually-
      shipped LibGUI 3.1.0 `Gui.dll` (`ButtonState.Build`/`BuildShadows`, `RenderTransform`,
      `Alignment.CalculateOffset`) to confirm the mechanism: the stock `Button`'s hover/press scale
      (`AnimatedScale` at 1.03/0.96) IS already symmetric about the button's own center — verified via
      `Alignment.CalculateOffset`'s pivot math, not a bug — but `ButtonState.BuildShadows` separately
      adds a hard-coded hover-only `BoxShadow` offset `(0, 3)` pixels downward that is NOT exposed on
      `ButtonStyle`/`ButtonVariantStyle` (no public field, no override hook). On a large flat
      full-width button (e.g. the Read view's "Task Editor" button) that shadow is present but
      visually negligible; on our small colored pill buttons it reads as the whole button growing
      downward. `dotnet build src/Mod` succeeds (research-only task, no code change).
- [x] 7.2 Tried a fix: added `src/Mod/ScribeAccentButton.cs`, a custom `StatefulWidget`/`State<T>`
      pair built directly from LibGUI primitives (`MouseRegion`, `GestureDetector`, `AnimatedScale`,
      `AnimatedContainer`, `Padding`) reproducing the stock `Button`'s hover/press scale, color
      transition, and click-sound feel with no box-shadow. Switched `AccentButton` to build it.
      Playtest (7.4) found it still read as broken (asymmetric growth/shadow persisted) — see 7.4.
      Further design review (`RenderClip.Paint`/`RenderBox.PaintOuterShadow`) also showed an
      outer-`Clip`-around-the-stock-`Button` alternative wasn't viable either (would truncate the
      hover-grow effect itself, see design.md). Per direct user direction, reverted: deleted
      `ScribeAccentButton.cs`, restored `AccentButton` to build `Gui.Widgets.Basic.Button` — not
      worth maintaining a duplicate of library button logic for one button. `dotnet build src/Mod`
      succeeds; net effect on shipped behavior is unchanged from before this investigation.
- [x] 7.3 ~~Switched `ScribeQuestPromptActions.AccentButton` to build `ScribeAccentButton`~~ —
      reverted as part of 7.2 above.
- [x] 7.4 Manual playtest: hovered the quest-prompt buttons with `ScribeAccentButton` in place —
      still grows asymmetrically / still shows a shadow (per user report). This is what triggered the
      7.2 revert; no further playtest needed since behavior is now back to pre-investigation stock
      `Button`.
