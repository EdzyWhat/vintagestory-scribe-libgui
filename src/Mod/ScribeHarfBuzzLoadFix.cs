using System;
using System.IO;
using System.Runtime.InteropServices;
using HarfBuzzSharp.Internals;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Pre-loads LibGUI's bundled HarfBuzzSharp native library ourselves, with <c>RTLD_DEEPBIND</c>, on
/// any glibc Linux desktop, isolating its internal symbol lookups from a system libharfbuzz already
/// resident in the process (confirmed via coredumpctl backtrace on KDE Plasma: <c>hb_font_create</c>
/// in the bundled <c>.so</c> landing in the system's <c>.so</c> instead, corrupting the heap until
/// <c>free()</c> aborts — GTK and Qt desktops can both preload a system HarfBuzz the same way, so
/// this isn't KDE-specific). See
/// <c>openspec/changes/fix-linux-harfbuzz-symbol-collision</c> and
/// <c>assess-libgui-decoupling/design.md</c> §5 "Confirmed" for the crash-dump evidence trail.
///
/// <para><b>Deliberately does NOT use <see cref="NativeLibrary.SetDllImportResolver"/>.</b> Decompiling
/// <c>Gui.dll</c> (not just <c>HarfBuzzSharp.dll</c>) found <c>Gui.NativeLibraryLoader.Register()</c>
/// — called at the top of <c>GuiModSystem.StartClientSide</c> — already registers a resolver for this
/// exact assembly, UNGUARDED, on every platform. That API throws if called twice for the same
/// assembly, and LibGUI has no try/catch around its own call, so a second registration would take down
/// the whole <c>GuiModSystem</c> (worse than the crash this fixes). Instead, this calls the raw OS
/// <c>dlopen</c> directly, once, before LibGUI's loader runs — no resolver registration at all, so
/// nothing to collide with. The OS dedupes <c>dlopen</c> calls by canonical file path: once a library
/// is mapped, LibGUI's own later, flag-less load of the SAME path transparently reuses the
/// already-mapped, already-deep-bound handle rather than reopening it. So this only needs to win the
/// race to open the file first; it never intercepts or replaces how anything else resolves it
/// afterward. The handle is intentionally never closed, so the library stays resident (and
/// deep-bound) for the process's lifetime.</para>
///
/// <para><b>A standalone <see cref="ModSystem"/>, not a <see cref="ScribeModSystem"/> partial.</b> Vintage
/// Story auto-discovers every <see cref="ModSystem"/> in a mod's assembly, so a second small ModSystem
/// class alongside <see cref="ScribeModSystem"/> is a normal, supported pattern — and it is load-bearing
/// here: this class's very low <see cref="ExecuteOrder"/> must affect ONLY this one dlopen, not
/// Scribe's entire startup sequence. An earlier version of this fix lived inside
/// <see cref="ScribeModSystem"/> itself at <c>ExecuteOrder() =&gt; -1000</c> — that dragged Scribe's
/// WHOLE <c>StartClientSide</c> (including its own font-metric probing, which shapes text through this
/// same HarfBuzzSharp assembly) ahead of `gui`'s own <c>StartClientSide</c>, so Scribe's first shape call
/// landed BEFORE LibGUI had installed ITS OWN resolver — the one that actually knows how to find
/// <c>native/&lt;rid&gt;/native/libHarfBuzzSharp.*</c> on every platform. On macOS this crashed
/// <c>ScribeModSystem.StartClientSide</c> outright with a <c>BadImageFormatException</c>-adjacent
/// <c>dlopen</c> failure (the file genuinely isn't at the flat path .NET's default resolver tries),
/// which then broke the network channel registration that never got to run — i.e. "every Scribe object
/// stopped responding," not just a font glitch. Keeping this isolated fixes that regression without
/// giving up the Linux protection: <see cref="ExecuteOrder"/> only needs to beat `gui`'s own load of
/// THIS specific native file, which a standalone ModSystem can do without touching Scribe's own phase
/// ordering at all.</para>
/// </summary>
public sealed class ScribeHarfBuzzLoadFix : ModSystem
{
    private const int RtldNow = 0x2;
    private const int RtldDeepBind = 0x8; // glibc extension; not defined on musl.

    /// <summary>
    /// Lower than the default 0.1 every other mod (including LibGUI's own GuiModSystem) leaves
    /// unmodified, so this runs — and dlopens the native library — before LibGUI's own loader gets a
    /// chance to load it first without the isolation flag. Deliberately NOT an extreme value: it only
    /// needs to beat `gui`, not preempt every mod's entire startup (see class remarks).
    /// </summary>
    public override double ExecuteOrder() => -1.0;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        if (!OperatingSystem.IsLinux())
        {
            // This bug is glibc + a system HarfBuzz already loaded process-wide; not a cross-platform
            // concern. No-op elsewhere, including macOS/Windows.
            return;
        }

        if (!PlatformConfiguration.IsGlibc)
        {
            api.Logger.Notification(
                "[scribe] non-glibc Linux detected; skipping HarfBuzz native-load isolation " +
                "(RTLD_DEEPBIND is a glibc extension)");
            return;
        }

        try
        {
            string? assemblyDir = Path.GetDirectoryName(typeof(HarfBuzzSharp.Face).Assembly.Location);
            string rid = RuntimeInformation.RuntimeIdentifier;
            if (string.IsNullOrEmpty(assemblyDir))
            {
                api.Logger.Warning(
                    "[scribe] could not determine HarfBuzzSharp assembly directory; " +
                    "HarfBuzz native-load isolation skipped");
                return;
            }

            string soPath = Path.Combine(assemblyDir, "native", rid, "native", "libHarfBuzzSharp.so");
            if (!File.Exists(soPath))
            {
                api.Logger.Warning(
                    "[scribe] bundled libHarfBuzzSharp.so not found at expected path '{0}'; " +
                    "HarfBuzz native-load isolation skipped", soPath);
                return;
            }

            IntPtr handle = dlopen(soPath, RtldNow | RtldDeepBind);
            if (handle == IntPtr.Zero)
            {
                api.Logger.Warning(
                    "[scribe] dlopen('{0}', RTLD_DEEPBIND) failed; LibGUI will load it normally " +
                    "instead (no isolation)", soPath);
                return;
            }

            // Deliberately not closed — see class remarks. Leaking this handle keeps the library
            // resident, deep-bound, for the rest of the process's lifetime.
            api.Logger.Notification(
                "[scribe] bundled libHarfBuzzSharp.so pre-loaded with RTLD_DEEPBIND from '{0}' " +
                "(avoids a symbol collision with any system libharfbuzz)", soPath);
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[scribe] HarfBuzz native-load isolation failed unexpectedly: {0}", exception.Message);
        }
    }

    [DllImport("libdl.so.2", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr dlopen(string filename, int flags);
}
