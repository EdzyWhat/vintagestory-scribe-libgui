# Tasks — LibGUI decoupling feasibility

This is an **assessment** change: it ships no production code and makes no spec deltas, so
`openspec validate` will report "no deltas" — that is expected here. The tasks below are the
**decision-support and de-risking steps** the assessment surfaced, not implementation of a spec.
They should be resolved *before* committing to an option or opening the follow-up `decouple-libgui`
implementation change. See `proposal.md` (the matrix) and `design.md` (evidence) for context.

## 1. Verify the multiplayer assumption the recommendation rests on

The entire server story (proposal.md fact #3, design.md §3 "Server mod enforcement") assumes a
`Side: Client` LibGUI mod is never loaded server-side and so is never dragged into the server's
client-enforcement list. This is inferred from the loader source, not observed. Confirm empirically.

- [ ] 1.1 **Join test — native client on a Scribe server.** Stand up a dedicated (or LAN-hosted)
      server running only the native `scribe` mod (Universal, gui-free) with `gui` **not installed
      server-side**. Connect a client that has `scribe` but **not** `gui` (or with `gui` disabled).
      Expected: the client joins with no "joinerror-modsmissing" disconnect. Record the outcome.
- [ ] 1.2 **Confirm `gui` is never advertised.** With a `Side: Client` LibGUI add-on installed on a
      *client* (deps `gui` + `scribe`) but **not** on the server, confirm the server's required-mods
      list (the `Universal && requiredOnClient` set from `ServerSystemHeartbeat`) contains `scribe`
      but **not** `gui` — i.e. a `gui`-disabled peer can still join that same server. Check the
      server log / connect handshake, not just "it worked on my machine."
- [ ] 1.3 **Negative control — reproduce today's block.** Confirm the *current* build (Scribe
      hard-deps `gui`, so the server loads `gui`) rejects a `gui`-disabled client with
      `joinerror-modsmissing`, so we know the test in 1.1/1.2 is actually exercising the fix and not
      a false pass. If 1.3 does *not* reproduce a block, re-examine the enforcement model before
      trusting 1.1/1.2.
- [ ] 1.4 If any of 1.1–1.3 contradicts the model, update `design.md §3` and the matrix, and
      re-weigh Option C vs B (or whether a `Side: Client` split is even sufficient) before deciding.

## 2. Resolve the maintainer decisions (design.md §7)

These are the actual choices the assessment defers to the maintainer; record the answers here.

- [ ] 2.1 **Keep LibGUI (B/C) or go native-only (D)?** — weigh LibGUI polish vs. its demonstrated
      fragility (this Linux crash, Apple-Silicon risk, version-pinning, vanilla-patching).
- [ ] 2.2 **If keeping both: companion mod (C, LibGUI `Side: Client`) or split-assembly (B)?** —
      deferrable; both need the same factory seam first. C is the recommended natural fit under the
      server constraint.
- [ ] 2.3 **Native feature bar** — ship a leaner tasks-first native build fast, or hold for
      near-parity? List which §2 "Cut/Degrade" items are acceptable for a first native release.

## 3. Confirm the enabling refactor is viable (low-risk, worth doing regardless)

- [ ] 3.1 Introduce Gui-free interfaces (`IScribeDialog`, `IScribePinHud`) and retype the Gui-typed
      members the severability audit found (design.md §1): `ScribeModSystem.pinHud` (`:84`),
      `BlockEntityScribeWritingStation.dialog` (`:181`) + its `CreateDialog` return (`:74`), the
      `OpenScribeDialog`/`Open…Dialog` returns on `ItemScribeNotebook`/`ItemScribeTablet`, and the
      `Action<ScribeDialogBase>` handbook hook. Then replace the hard-coded `new GuiDialogScribe…()`
      in `CreateDialog` (`BlockEntityScribeLectern.cs:24-25`, and siblings) with a factory resolved at
      runtime. Relocate/guard the `capi.Gui.OpenedGuis.OfType<GuiDialog…>()` client-glue calls
      (`ScribeModSystem.Timer.cs:86`, `Network.cs:484/500`).
- [ ] 3.2 Compile pass: confirm the core (`scribe`) assembly names **no** `Gui`-typed field/base/return
      after 3.1, so its `GetTypes()` always succeeds with `gui` absent. (The audit already proved the
      pre-refactor tree is NOT clean — this verifies the interfaces closed every hole; design.md §7.)
- [ ] 3.3 Identify the exact reflective-factory registration API the client add-on uses to inject its
      factory into the base mod (design.md §7 item 2).

---

**Next step after these resolve:** open a `decouple-libgui` implementation change (with real spec
deltas) per the chosen option — not part of this assessment.
