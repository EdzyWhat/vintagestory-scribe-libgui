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

/// <summary>Whether this process can attempt glibc <c>RTLD_DEEPBIND</c> isolation (platform gating only).</summary>
internal enum HarfBuzzPlatformSupport
{
    /// <summary>Not Linux; the bug does not apply, so the fix is a silent no-op.</summary>
    NotLinux,

    /// <summary>Linux without glibc; <c>RTLD_DEEPBIND</c> is a glibc extension, so isolation is skipped and logged.</summary>
    UnsupportedLibc,

    /// <summary>Linux + glibc; isolation is attempted.</summary>
    GlibcLinux,
}

/// <summary>
/// Harmony-patches LibGUI's <c>Gui.NativeLibraryLoader.Register()</c> to install an <c>RTLD_DEEPBIND</c>-isolated
/// resolver for the bundled HarfBuzzSharp native library, isolating its symbol lookups from a system
/// libharfbuzz already resident in the process (otherwise <c>hb_font_create</c> in the bundled <c>.so</c> can
/// land in the system's <c>.so</c> and corrupt the heap; GTK and Qt desktops can both preload HarfBuzz).
///
/// <para><b>Deterministic replacement, not a startup-order race.</b> <c>Register()</c> is an internal,
/// parameterless method whose whole body registers one <see cref="NativeLibrary.SetDllImportResolver(System.Reflection.Assembly, System.Runtime.InteropServices.DllImportResolver)"/>
/// call for the HarfBuzzSharp assembly. A <c>Prefix</c> installs Scribe's isolated resolver and skips
/// <c>gui</c>'s original, regardless of mod load order. The technique was independently shipped by Seralth
/// (<c>github.com/Seralth/harfbuzzfix</c>); this is reimplemented from analysis of the shipped <c>Gui.dll</c>.</para>
///
/// <para><b>Fails closed at every step.</b> <c>Register()</c> is an internal detail of <c>gui</c> and may be
/// renamed or restructured. If the type or method is missing, or patching throws, this logs a warning and
/// <c>gui</c>'s original runs unpatched. If the <c>Prefix</c> itself fails it returns <c>true</c>, so Harmony
/// still runs <c>gui</c>'s original rather than leaving no resolver registered.</para>
///
/// <para><b>A standalone <see cref="ModSystem"/>, not a <see cref="ScribeModSystem"/> partial</b>, so its
/// lifecycle (own <see cref="Harmony"/> instance) is isolated from Scribe's startup. The patch is applied in
/// <see cref="StartPre"/>, which runs before any mod's <c>StartClientSide</c>, so it is in place before
/// <c>gui</c> calls <c>Register()</c> and needs no <c>ExecuteOrder</c> tuning.</para>
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

        var support = ClassifyPlatform(OperatingSystem.IsLinux(), PlatformConfiguration.IsGlibc);
        if (support == HarfBuzzPlatformSupport.NotLinux)
        {
            return;
        }

        if (support == HarfBuzzPlatformSupport.UnsupportedLibc)
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

    /// <summary>Pure platform-gating decision; the live checks are parameters so it is testable anywhere.</summary>
    internal static HarfBuzzPlatformSupport ClassifyPlatform(bool isLinux, bool isGlibc) =>
        !isLinux ? HarfBuzzPlatformSupport.NotLinux
        : !isGlibc ? HarfBuzzPlatformSupport.UnsupportedLibc
        : HarfBuzzPlatformSupport.GlibcLinux;

    /// <summary>Harmony prefix for <c>Register()</c>. <c>false</c> skips gui's original body; <c>true</c> is
    /// the fail-safe fallback that lets it run.</summary>
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

    /// <summary>Deep-bind <c>dlopen</c> of the bundled library, falling back to the runtime's normal resolution.</summary>
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

    /// <summary>Locates the bundled <c>native/&lt;rid&gt;/native/</c> directory next to the HarfBuzzSharp assembly.</summary>
    private static string? FindNativeDir(Assembly assembly)
    {
        string? assemblyDir = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(assemblyDir)) return null;

        string rid = GetLinuxRid(RuntimeInformation.ProcessArchitecture);
        string nativeDir = Path.Combine(assemblyDir, "native", rid, "native");
        return Directory.Exists(nativeDir) && File.Exists(Path.Combine(nativeDir, "libHarfBuzzSharp.so"))
            ? nativeDir
            : null;
    }

    /// <summary>Deliberately NOT <see cref="RuntimeInformation.RuntimeIdentifier"/>, which can return a
    /// distro-qualified RID that does not match the flat folder name the native asset ships under. This mirrors
    /// gui's own <c>GetRid()</c> mapping from <see cref="RuntimeInformation.ProcessArchitecture"/>.</summary>
    internal static string GetLinuxRid(Architecture architecture) => architecture switch
    {
        Architecture.Arm => "linux-arm",
        Architecture.Arm64 => "linux-arm64",
        Architecture.X86 => "linux-x86",
        _ => "linux-x64",
    };

    [DllImport("libdl.so.2", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr dlopen(string filename, int flags);
}
