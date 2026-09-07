using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal static class ThirdPartyNativeLibraryResolver
    {
        private static readonly object s_Sync = new();
        private static readonly HashSet<Assembly> s_RegisteredAssemblies = new();
        private static bool s_DirectMLRegistered;

        public static void EnsureResolverRegistered(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            lock (s_Sync)
            {
                if (s_RegisteredAssemblies.Contains(assembly))
                {
                    return;
                }
                NativeLibrary.SetDllImportResolver(assembly, ResolveLibraryImport);
                s_RegisteredAssemblies.Add(assembly);
            }
        }

        public static void EnsureDirectMLResolverRegistered()
        {
            lock (s_Sync)
            {
                if (!s_DirectMLRegistered)
                {
                    Vortice.DirectML.DML.ResolveLibrary += ResolveLibraryImport;
                    s_DirectMLRegistered = true;
                }
            }
        }

        public static IEnumerable<string> EnumerateCandidates(string libraryName)
        {
            if (!OperatingSystem.IsWindows())
            {
                yield break;
            }
            string name = Path.GetFileName(libraryName).ToLowerInvariant();
            (string Library, string File)? profile = name switch
            {
                "directml" or "directml.dll" => ("DirectML", "DirectML.dll"),
                "directml.debug" or "directml.debug.dll" => ("DirectML", "DirectML.Debug.dll"),
                "dstorage" or "dstorage.dll" => ("DirectStorage", "dstorage.dll"),
                "dstoragecore" or "dstoragecore.dll" => ("DirectStorage", "dstoragecore.dll"),
                "winpixeventruntime" or "winpixeventruntime.dll" => ("PIX", "WinPixEventRuntime.dll"),
                _ => null,
            };
            if (profile.HasValue)
            {
                yield return SharpGpuNativeLibraries.Resolve(profile.Value.Library, profile.Value.File);
            }
        }

        // Successful callers own one native load reference and must release probes.
        public static bool TryResolve(string libraryName, out nint handle, out string? resolvedPath)
        {
            foreach (string candidate in EnumerateCandidates(libraryName))
            {
                if (NativeLibrary.TryLoad(GetNativeLoadPath(candidate), out handle))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
            handle = 0;
            resolvedPath = null;
            return false;
        }

        private static string GetNativeLoadPath(string path)
        {
            if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path;
            }
            string fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + fullPath.Substring(2)
                : @"\\?\" + fullPath;
        }
        public static nint ResolveLibraryImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            foreach (string candidate in EnumerateCandidates(libraryName))
            {
                // A configured runtime never silently resolves another version from PATH.
                return NativeLibrary.Load(GetNativeLoadPath(candidate));
            }
            return 0;
        }
    }
}
