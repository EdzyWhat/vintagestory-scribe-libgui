## 1. Shared row-action alignment

- [x] 1.1 Extend `ScribeRowControlNudge.FloatingButtonTop` with item-row context and center item-row
  actions from the established cuneiform-aware item-name line height. Keep the ordinary row formula
  and drawn-box correction intact. Verify the Mod project builds and inspect both branches against
  their corresponding row text measurements.
- [x] 1.2 Pass each row's existing item-kind classification from every floating-action call site in
  Read, Editor, and Pinned content. Verify an `rg` search shows every `FloatingButtonTop` call passing
  row context, with each paired Delete/Pin or Delete/Unpin set sharing one computed top value.

## 2. Verification

- [x] 2.1 Run `VINTAGE_STORY="/Applications/Vintage Story.app" ./build/verify.sh Debug --no-restage`
  and confirm the build, Core suite, and Atlas suite pass.
- [x] 2.2 Restage Debug and manually inspect ordinary task, Latin item, and cuneiform tablet item rows
  - Confirmed 2026-09-08: TESTING.md `000000bb` "(no note)" (submission 2026-09-08T10-23-43)
  in Read, Editor, and Pinned views. Confirm the visible Pin, Unpin, and Delete button boxes align with
  the first text line while retaining their prior size, horizontal placement, hover visibility, and
  actions.
- [x] 2.3 Run `openspec validate align-row-hover-action-buttons --strict` and confirm the change
  artifacts pass strict validation.
