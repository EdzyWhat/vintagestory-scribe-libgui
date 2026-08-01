## 1. Split ScribeDialogBase.cs

- [x] 1.1 Read the current `src/Mod/ScribeDialogBase.cs` and identify cohesive concern clusters
      from its `#region`/method grouping (candidate seams: title-edit, guestbook, pinned view,
      backdrop/layout, input-capture/focus, view-model/state).
- [x] 1.2 Create `partial class ScribeDialogBase` files named `ScribeDialogBase.<Concern>.cs` in
      `src/Mod/`, moving each cluster's members verbatim (comments included). Keep the primary
      declaration, all field initializers, and the ctor in `ScribeDialogBase.cs`.
- [x] 1.3 Confirm the move is pure relocation: no rename, no visibility change, no signature
      change, no logic change (a diff should show members leaving one file and arriving in
      another, nothing else).
- [x] 1.4 `dotnet build src/Mod/Mod.csproj -c Debug` — clean; `dotnet test tests/Core.Tests` — all
      pass. Commit this file's split on its own.

## 2. Split ScribeModSystem.cs

- [x] 2.1 Read the current `src/Mod/ScribeModSystem.cs` and identify concern clusters (candidate
      seams: icon/font registration, host/document registry, network packet handlers,
      backdrop-bitmap cache, client/server lifecycle).
- [x] 2.2 Create `partial class ScribeModSystem` files named `ScribeModSystem.<Concern>.cs`,
      moving each cluster verbatim. Keep the primary declaration, field initializers, and
      `Start*`/lifecycle entry points in `ScribeModSystem.cs`.
- [x] 2.3 Confirm pure relocation (same constraint as 1.3).
- [x] 2.4 `dotnet build` clean; `dotnet test tests/Core.Tests` pass. Commit this file's split on
      its own.

## 3. Verify behavior preservation end-to-end

- [ ] 3.1 `bash build/restage.sh Debug`, then run the Atlas integration suite (local pre-push
      gate) — green.
- [ ] 3.2 Manual in-game smoke: open the Lectern, plain Notebook, and Clockmaker's Notebook;
      verify Read / Editor / Pinned / Guestbook / Timer views, title editing, lock/autosave, and
      backdrops all render and function identically to before.
- [ ] 3.3 Confirm no file under `src/Mod/` remains a ~2000-line catch-all (both former god-files
      are now a set of concern-named partials).

## 4. Validate

- [ ] 4.1 `openspec validate split-large-gui-files --strict` passes.
