## 1. Pin current behavior

- [x] 1.1 Add an Atlas scenario asserting readability, glow, palette, backdrop, quest-link accent and layout
  proportions for each shipped `(material, state)` pair through the existing entry points; run `build/verify.sh`
  and confirm it passes before any code changes

## 2. Facade

- [x] 2.1 Add `TabletStyle.For(material, state)` composing the existing tables, with red as the single unknown-material fallback; extend the matrix scenario to assert the facade equals the pinned values and add an unknown-material case
- [x] 2.2 Route `GuiDialogScribeTablet`, `ItemScribeTablet`, `CuneiformGlowTable` and `TabletHost` through the facade and delete the string switches; verify build and matrix

## 3. Verify

- [x] 3.1 Run `build/verify.sh`; confirm Core and Atlas pass
- [x] 3.2 Playtest: open each of the 10 tablet variants (red, blue, fire x wet/hard/fired, plus wax) and confirm ink, backdrop, glow, quest-link color and layout look unchanged
