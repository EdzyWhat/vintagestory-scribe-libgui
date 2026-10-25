## 1. Timer tab staleness

- [x] 1.1 Extract the "does this status change need a rebuild" decision into a pure function with Core tests for Idle/Running/Fired transitions, including a dialog opened mid-run and on a fired timer
- [x] 1.2 Seed the Notebook's tracked status from the live timer when the dialog opens, without replaying the gearworks lock/engage animation; verify the tests

## 2. HUD reappearance

- [x] 2.1 Add a temporary debug trace of `ApplyAnchor` on timer open and reproduce the ModDB report in game. Finding: the report did not reproduce (run, end, clear, restart showed the HUD each time) and the trace never ran `ApplyAnchor` while closed, so the anchor-cache hypothesis is unconfirmed
- [x] 2.2 Dropped: no confirmed cause, so no speculative cache reset; trace removed

## 3. Verify

- [x] 3.1 Run `build/verify.sh`; confirm Core and Atlas pass
- [x] 3.2 Playtest: start a 5 s real-time timer, close the Notebook, reopen after it fires, press Stop Timer and confirm the form returns
- [x] 3.3 Playtest: run, end and clear a timer, then start another and confirm it appears on the HUD without touching settings
