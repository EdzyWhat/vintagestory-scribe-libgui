## Context

Decompiling the real shipped `src/Mod/lib/Gui.dll` (3.1.0, via `ilspycmd`) shows exactly how LibGUI's
first-run dialog is gated. `GuiModSystem.StartClientSide` calls a private `LoadThemeConfig()`, which
does:

```csharp
GuiConfig guiConfig = Capi.LoadModConfig<GuiConfig>("libgui.json"); // null if the file doesn't exist
if (guiConfig == null)
{
    Capi.StoreModConfig(new GuiConfig { Theme = ThemeSection.FromColorScheme(ColorScheme.Default()) }, "libgui.json");
    ThemeData.Default = new ThemeData();
}
else
{
    _onboarded = guiConfig.Onboarded;
    ThemeData.Default = guiConfig.Theme != null ? new ThemeData(guiConfig.Theme.ToColorScheme(...)) : new ThemeData();
}
```

then, later in the same `StartClientSide`:

```csharp
if (!_onboarded)
{
    _onboarded = true;
    PersistConfig(BuildCurrentConfig(onboarded: true)); // written to disk BEFORE the dialog even opens
    _settingsDialog = BuildSettingsDialog(isFirstRun: true);
    api.Event.EnqueueMainThreadTask(() => _settingsDialog.TryOpen(), "libgui-first-run-settings");
}
```

So the dialog is gated purely on whether `ModConfig/libgui.json` exists with `Onboarded: true` at the
moment `GuiModSystem.StartClientSide` runs — a single file shared by every LibGUI-based mod on that
client install (stored under `Capi.DataBasePath/ModConfig/`, not per-world, per-save, or per-mod).
`GuiConfig` (`Theme`, `Custom`, `Onboarded`) is a public class in `Gui.dll`, and
`ICoreAPI.LoadModConfig<T>`/`StoreModConfig<T>` are standard, already-used VS API methods — no LibGUI
internals need reflection or patching.

Verified locally (macOS): deleting the real `~/Library/Application Support/VintagestoryData/ModConfig/libgui.json`
and relaunching reproduces the dialog; with this change's fix built and staged, deleting the same file
and relaunching shows no dialog and the theme resolves to LibGUI's own default (confirmed by the user
2026-08-30).

## Goals / Non-Goals

**Goals:**
- New Scribe players never see LibGUI's first-run theme dialog; they land on LibGUI's own default
  theme automatically, exactly as if they'd opened the dialog and clicked "use this theme" on the
  default preset.
- Never override an existing, already-decided onboarding state — this only acts on a genuinely fresh
  client install (no `libgui.json` yet).
- Reuse the same low-`ExecuteOrder()` race-winning pattern already established by
  `ScribeHarfBuzzLoadFix` rather than inventing a second mechanism.

**Non-Goals:**
- Letting players configure a different default theme through Scribe — out of scope; LibGUI's own
  `/ui settings` command still works normally for anyone who wants to change it later.
- Scoping this to *only* Scribe's own dialogs — `libgui.json` is inherently shared client-wide; see
  Risks below for why this is accepted rather than avoided.

## Decisions

**Mechanism: pre-seed `libgui.json` with `Onboarded = true` and no theme override, via a standalone
low-`ExecuteOrder()` `ModSystem`.** Writing the exact same "no theme override" shape LibGUI itself
would write on a truly fresh install (see Context) means the resulting theme is identical to LibGUI's
own default — this isn't a different/opinionated theme choice, just skipping the prompt for it.
Mirrors `ScribeHarfBuzzLoadFix`'s `ExecuteOrder() => -1.0` override to guarantee this runs before
`GuiModSystem.StartClientSide`'s own `LoadThemeConfig()` call.

**Only act when the file doesn't exist yet.** `LoadModConfig<GuiConfig>` returns `null` exactly when
there's no prior decision to respect. If the file exists — even with `Onboarded: false` — leave it
alone: that state only occurs if someone deliberately edited the file (e.g. to intentionally re-trigger
the dialog for themselves), and overriding a deliberate action would be a worse outcome than the
dialog itself.

**Considered and rejected: detect the dialog opening and auto-invoke its "use this theme" callback.**
Would require reflecting into `GuiModSystem`'s private `_settingsDialog`/`_onboarded` fields or the
`SettingsDialog`'s internal apply-callback, is far more fragile across LibGUI version bumps, and
produces the exact same end state (default theme, `Onboarded: true` persisted) as the config pre-seed —
with none of the extra fragility.

**Considered and rejected: Harmony patch to skip the onboarding block in `GuiModSystem.StartClientSide`
entirely.** Same end state achievable without patching IL in a dependency we don't control; rejected
for the same reason the HarfBuzz fix rejected a Harmony approach — a supported extension point (here,
the config file LibGUI itself reads) already gets us there.

**Standalone `ModSystem`, not a `ScribeModSystem` partial.** Same reasoning as
`ScribeHarfBuzzLoadFix`: an aggressive `ExecuteOrder()` override must only affect this one
registration, not Scribe's entire startup sequence.

## Risks / Trade-offs

- **[Risk] `ModConfig/libgui.json` is shared client-wide, not scoped to Scribe** → this also skips the
  first-run dialog for any *other* LibGUI-based mod the same player has installed, if Scribe's
  `StartClientSide` happens to run before that mod's own LibGUI usage triggers it. **Mitigation:** none
  needed — this is the accepted, intended side effect (see proposal.md Impact), not a bug. A player who
  installs any LibGUI-based mod alongside Scribe gets the same "just works, no surprise dialog"
  experience Scribe wants for itself.
- **[Risk] A future LibGUI version changes `GuiConfig`'s shape or the onboarding gate condition** →
  **Mitigation:** fails closed — if `GuiConfig` no longer round-trips through `LoadModConfig`/
  `StoreModConfig` cleanly, or the class no longer exists at that name, this throws inside the try/catch
  and logs a warning without touching startup; LibGUI's own onboarding then runs unmodified, exactly
  today's pre-fix behavior. Never a new failure mode, only a potential loss of this convenience.
- **[Risk] Two Scribe `ModSystem`s (`ScribeHarfBuzzLoadFix`, this one) both racing for the lowest
  `ExecuteOrder()`** → **Mitigation:** neither depends on the other or has any interaction; both only
  need to run before `GuiModSystem`, and ties between them are irrelevant. No coordination needed.

## Migration Plan

Pure additive client-side code change — one new startup registration, no data migration, no
save-format or network change. Ship in the next release. Rollback is a plain revert; nothing here is
persisted or migrated beyond the one config file LibGUI itself already owns and would otherwise write
identically (just later, after showing the dialog).
