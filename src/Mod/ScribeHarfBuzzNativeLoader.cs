using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HarfBuzzSharp.Internals;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Installs the Linux/glibc native resolver for LibGUI's bundled HarfBuzzSharp library.
/// The resolver is deliberately a Scribe-side workaround: the durable fix belongs in
/// LibGUI/HarfBuzzSharp's native packaging or symbol visibility.
/// </summary>
public sealed partial class ScribeModSystem
{
    private const string HarfBuzzLibraryName = "libHarfBuzzSharp";
    private const int RtldNow = 0x2;
    private const int RtldDeepBind = 0x8;

    /// <summary>
    /// Runs before the default mod-system order so the resolver is installed before any
    /// dependent LibGUI mod can make its first HarfBuzz call. Desktop toolkit identity is
    /// intentionally irrelevant: GTK and Qt can both preload system HarfBuzz.
    /// </summary>
    public override double ExecuteOrder() => -1000;

    private static void InitializeHarfBuzzNativeLoader(ICoreClientAPI api)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (!PlatformConfiguration.IsGlibc)
        {
            api.Logger.Notification(
                "[scribe] HarfBuzz native isolation unavailable on non-glibc Linux; using the default loader");
            return;
        }

        try
        {
            var assembly = typeof(HarfBuzzSharp.Face).Assembly;
            NativeLibrary.SetDllImportResolver(assembly, (libraryName, _, _) =>
            {
                if (!string.Equals(libraryName, HarfBuzzLibraryName, StringComparison.Ordinal))
                {
                    return IntPtr.Zero;
                }

                return TryLoadBundledHarfBuzz(assembly, api);
            });
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[scribe] HarfBuzz native isolation registration failed; using the default loader ({0})",
                exception.Message);
        }
    }

    private static IntPtr TryLoadBundledHarfBuzz(Assembly assembly, ICoreClientAPI api)
    {
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            api.Logger.Warning(
                "[scribe] HarfBuzz native isolation could not locate HarfBuzzSharp.dll; using the default loader");
            return IntPtr.Zero;
        }

        var rid = RuntimeInformation.RuntimeIdentifier;
        var nativePath = Path.Combine(
            assemblyDirectory,
            "native",
            rid,
            "native",
            "libHarfBuzzSharp.so");

        if (!File.Exists(nativePath))
        {
            api.Logger.Warning(
                "[scribe] HarfBuzz native isolation could not find bundled library at {0}; using the default loader",
                nativePath);
            return IntPtr.Zero;
        }

        try
        {
            var handle = dlopen(nativePath, RtldNow | RtldDeepBind);
            if (handle == IntPtr.Zero)
            {
                api.Logger.Warning(
                    "[scribe] HarfBuzz native isolation failed to load {0}; using the default loader",
                    nativePath);
                return IntPtr.Zero;
            }

            api.Logger.Notification(
                "[scribe] HarfBuzz native isolation enabled for {0}", nativePath);
            return handle;
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[scribe] HarfBuzz native isolation failed for {0}; using the default loader ({1})",
                nativePath,
                exception.Message);
            return IntPtr.Zero;
        }
    }

    [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern IntPtr dlopen(string fileName, int flags);
}
