## 1. Form values

- [x] 1.1 Add Core record `TimerFormValues` with `Blank(TimerMode preferred)`; verify with Core tests for the blank values and for value equality
- [x] 1.2 Hold the last started values on `ScribeModSystem` and set them in `SendStartTimer`; verify by building the Mod project

## 2. Notebook form

- [x] 2.1 Seed the dialog's form fields from the remembered values (or `Blank`) in the constructor; verify with `build/verify.sh`
- [x] 2.2 Add a circle-X clear button on the idle form that resets the fields to `Blank(PreferredTimerMode)` and rebuilds the form; verify with `build/verify.sh`

## 3. Verify

- [ ] 3.1 Run `build/verify.sh`; confirm Core and Atlas pass
- [ ] 3.2 Playtest: start a named timer, let it fire, dismiss it, reopen the Notebook and confirm name, h/m/s and mode are prefilled
- [ ] 3.3 Playtest: stop a running timer and confirm the same prefill; press the circle-X and confirm the fields blank
