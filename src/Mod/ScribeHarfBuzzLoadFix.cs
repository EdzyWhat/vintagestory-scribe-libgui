using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarfBuzzSharp.Internals;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Harmony-patches LibGUI's own <c>Gui.NativeLibraryLoader.Register()</c> to install an
/// <c>RTLD_DEEPBIND</c>-isolated resolver for the bundled HarfBuzzSharp native library, instead of
/// letting it register its own unisolated one — isolating its internal symbol lookups from a system
/// libharfbuzz already resident in the process (confirmed via coredumpctl backtrace on KDE Plasma:
/// <c>hb_font_create</c> in the bundled <c>.so</c> landing in the system's <c>.so</c> instead,
/// corrupting the heap until <c>free()</c> aborts — GTK and Qt desktops can both preload a system
/// HarfBuzz the same way, so this isn't KDE-specific). See
/// <c>openspec/changes/fix-linux-harfbuzz-symbol-collision</c>,
/// <c>broaden-linux-harfbuzz-fix/design.md</c>, and this fix's own
/// <c>strengthen-harfbuzz-linux-fix/design.md</c> for the evidence trail and the reasoning below.
///
/// <para><b>Deterministic replacement, not a startup-order race.</b> An earlier version of this fix
/// raced <c>gui</c>'s own loader: it raw-<c>dlopen</c>'d the bundled <c>.so</c> itself, before
/// <c>gui</c>'s <c>StartClientSide</c> ran, relying on the OS deduping <c>dlopen</c> calls by
/// canonical path so <c>gui</c>'s later, unflagged load of the same path transparently reused the
/// already-deep-bound handle. That only protected users because Scribe's own startup order was
/// tuned to beat <c>gui</c>'s. This version instead Harmony-patches
/// <c>Gui.NativeLibraryLoader.Register()</c> — an <c>internal</c>, parameterless, single-purpose
/// method (confirmed by decompiling the shipped <c>Gui.dll</c>: its entire body registers exactly
/// one <see cref="NativeLibrary.SetDllImportResolver(System.Reflection.Assembly, System.Runtime.InteropServices.DllImportResolver)"/>
/// call for the HarfBuzzSharp assembly, guarded by its own idempotency flag) — with a
/// <c>Prefix</c> that installs Scribe's own isolated resolver and skips <c>gui</c>'s original,
/// unisolated one. This applies regardless of mod load order: independently discovered and shipped
/// by a community member (Seralth, <c>github.com/Seralth/harfbuzzfix</c>) against the same upstream
/// report this repo's maintainer filed (<c>ripls56/vslibgui#2</c>) — reimplemented here from
/// first-hand analysis of the shipped <c>Gui.dll</c> rather than his source (his repo carries no
/// license), but the technique is his to credit.</para>
///
/// <para><b>Fails closed at every step.</b> Because <c>Register()</c> is <c>internal</c> — an
/// implementation detail of <c>gui</c>, not a contract — a future <c>gui</c> release could rename,
/// remove, or restructure it. If the type or method can't be found, or applying the Harmony patch
/// throws, this logs a warning and returns: <c>gui</c>'s original <c>Register()</c> then runs
/// completely unpatched, exactly as if this fix weren't installed. If the <c>Prefix</c> itself
/// fails (e.g. the isolated resolver can't be registered for an unexpected reason), it returns
/// <c>true</c> so Harmony still runs <c>gui</c>'s original method afterward, rather than leaving the
/// HarfBuzzSharp assembly with no resolver registered at all. A user is never worse off than not
/// having this fix installed.</para>
///
/// <para><b>A standalone <see cref="ModSystem"/>, not a <see cref="ScribeModSystem"/> partial.</b>
/// Vintage Story auto-discovers every <see cref="ModSystem"/> in a mod's assembly, so a second small
/// ModSystem class alongside <see cref="ScribeModSystem"/> is a normal, supported pattern — and it
/// keeps this fix's lifecycle (its own <see cref="Harmony"/> instance, patched/unpatched here) fully
/// isolated from Scribe's own startup sequence. An earlier version of this fix lived inside
/// <see cref="ScribeModSystem"/> itself at a very low <c>ExecuteOrder</c>, which dragged Scribe's
/// WHOLE <c>StartClientSide</c> ahead of <c>gui</c>'s own — crashing macOS startup outright (see the
/// git history on this file for that regression). This version's patch is applied in
/// <see cref="StartPre"/>, which runs before ANY mod's <c>StartClientSide</c> — since the patch only
/// needs to be IN PLACE before <c>gui</c>'s <c>StartClientSide</c> calls <c>Register()</c>, not
/// racing anything, there is no <c>ExecuteOrder</c> tuning to get wrong here.</para>
/// </summary>
public sealed class ScribeHarfBuzzLoadFix : ModSystem
{
    private const string HarmonyId = "scribe:harfbuzz-isolation";
    private const int RtldNow = 0x2;
    private const int RtldDeepBind = 0x8; // glibc extension; not defined on musl.

    private static bool isolatedResolverRegistered;
    private static ICoreAPI? patchApi;

    private Harmony? harmony;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        patchApi = api;

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
            var loaderType = AccessTools.TypeByName("Gui.NativeLibraryLoader");
            if (loaderType is null)
            {
                api.Logger.Notification(
                    "[scribe] Gui.NativeLibraryLoader not found (gui not installed?); " +
                    "HarfBuzz native-load isolation skipped");
                return;
            }

            var registerMethod = AccessTools.Method(loaderType, "Register");
            if (registerMethod is null)
            {
                api.Logger.Warning(
                    "[scribe] Gui.NativeLibraryLoader.Register method not found; " +
                    "HarfBuzz native-load isolation skipped");
                return;
            }

            harmony = new Harmony(HarmonyId);
            harmony.Patch(registerMethod,
                prefix: new HarmonyMethod(typeof(ScribeHarfBuzzLoadFix), nameof(RegisterPrefix)));
            api.Logger.Notification(
                "[scribe] patched Gui.NativeLibraryLoader.Register — HarfBuzzSharp will load with " +
                "RTLD_DEEPBIND isolation (avoids a symbol collision with any system libharfbuzz)");
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[scribe] failed to patch Gui.NativeLibraryLoader.Register; gui will use its own " +
                "(unisolated) loader instead: {0}", exception.Message);
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        base.Dispose();
    }

    /// <summary>Harmony prefix for <c>Gui.NativeLibraryLoader.Register()</c>. Returning <c>false</c>
    /// skips gui's original (unisolated) body entirely; returning <c>true</c> lets it run normally as
    /// a fail-safe fallback.</summary>
    private static bool RegisterPrefix()
    {
        try
        {
            RegisterIsolatedResolver();
            return false;
        }
        catch (Exception exception)
        {
            patchApi?.Logger.Warning(
                "[scribe] isolated HarfBuzz resolver registration failed; falling back to gui's " +
                "default loader: {0}", exception.Message);
            return true;
        }
    }

    private static void RegisterIsolatedResolver()
    {
        if (isolatedResolverRegistered) return;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "HarfBuzzSharp");
        if (assembly is null)
            throw new InvalidOperationException("HarfBuzzSharp assembly is not loaded yet.");

        string? nativeDir = FindNativeDir(assembly);
        if (nativeDir is null)
            throw new InvalidOperationException("Could not locate HarfBuzzSharp's native library directory.");

        NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => ResolveNativeLibrary(name, nativeDir));
        isolatedResolverRegistered = true;
    }

    /// <summary>The isolated resolver: deep-bind <c>dlopen</c> the bundled library by name, falling back
    /// to the runtime's normal (unflagged) resolution if that fails — matching gui's own resolver's
    /// fallback shape, just with isolation attempted first.</summary>
    private static IntPtr ResolveNativeLibrary(string name, string nativeDir)
    {
        string fileName = (name.StartsWith("lib", StringComparison.Ordinal) ? "" : "lib") + name + ".so";
        string path = Path.Combine(nativeDir, fileName);
        if (File.Exists(path))
        {
            IntPtr handle = dlopen(path, RtldNow | RtldDeepBind);
            if (handle != IntPtr.Zero) return handle;
        }

        NativeLibrary.TryLoad(name, out IntPtr fallback);
        return fallback;
    }

    /// <summary>Locates the bundled <c>native/&lt;rid&gt;/native/</c> directory relative to the loaded
    /// HarfBuzzSharp assembly — the same layout the fix's prior dlopen-race version already confirmed
    /// works in production.</summary>
    private static string? FindNativeDir(Assembly assembly)
    {
        string? assemblyDir = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(assemblyDir)) return null;

        string rid = GetLinuxRid();
        string nativeDir = Path.Combine(assemblyDir, "native", rid, "native");
        return Directory.Exists(nativeDir) && File.Exists(Path.Combine(nativeDir, "libHarfBuzzSharp.so"))
            ? nativeDir
            : null;
    }

    /// <summary>Deliberately NOT <see cref="RuntimeInformation.RuntimeIdentifier"/> — decompiling the
    /// shipped <c>Gui.dll</c> shows <c>Gui.NativeLibraryLoader.GetRid()</c> (which resolves this exact
    /// same <c>native/&lt;rid&gt;/native/</c> layout for the same assembly, in the normal, unpatched
    /// case) manually maps <see cref="RuntimeInformation.ProcessArchitecture"/> instead; Seralth's
    /// independently-shipped <c>harfbuzzfix</c> does the same. <c>RuntimeIdentifier</c> can return a
    /// longer, distro-qualified RID that doesn't match the flat folder name the native asset actually
    /// ships under — this mirrors the known-correct mapping rather than that fragile API.</summary>
    private static string GetLinuxRid() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm => "linux-arm",
        Architecture.Arm64 => "linux-arm64",
        Architecture.X86 => "linux-x86",
        _ => "linux-x64",
    };

    [DllImport("libdl.so.2", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr dlopen(string filename, int flags);
}
