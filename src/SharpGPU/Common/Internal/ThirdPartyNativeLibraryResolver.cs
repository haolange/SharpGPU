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

        // This seam is intentionally internal: IE's integration tests verify
        // the explicit repository/native layout without changing runtime
        // discovery policy or exposing test-only path enumeration publicly.
        internal static IEnumerable<string> EnumerateCandidatesWithExplicitRootsForTesting(
            string libraryName,
            string? explicitRoots)
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

            if (!profile.HasValue)
            {
                yield break;
            }

            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "AMD64",
                System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
                _ => string.Empty,
            };
            if (string.IsNullOrEmpty(arch))
            {
                yield break;
            }

            HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(explicitRoots))
            {
                foreach (string rawRoot in explicitRoots.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string root;
                    try
                    {
                        root = Path.GetFullPath(rawRoot);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(
                        root,
                        "Binaries",
                        "ThirdParty",
                        "Microsoft",
                        profile.Value.Library,
                        "Win",
                        arch,
                        profile.Value.File);
                    if (emitted.Add(candidate))
                    {
                        yield return candidate;
                    }

                    string rootName = Path.GetFileName(
                        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string? canonical = rootName.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(root, "ThirdParty", "Microsoft")
                        : rootName.Equals("ThirdParty", StringComparison.OrdinalIgnoreCase)
                            ? Path.Combine(root, "Microsoft")
                            : rootName.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
                                ? root
                                : Path.Combine(root, "ThirdParty", "Microsoft");
                    candidate = Path.Combine(
                        canonical,
                        profile.Value.Library,
                        "Win",
                        arch,
                        profile.Value.File);
                    if (emitted.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }

            // Preserve the normal configured candidate as an auto-discovery
            // fallback when an explicit root is absent or invalid.
            string fallback = SharpGpuNativeLibraries.Resolve(
                profile.Value.Library,
                profile.Value.File);
            if (emitted.Add(fallback))
            {
                yield return fallback;
            }
        }

        // Successful callers own one native load reference and must release probes.
        public static bool TryResolve(string libraryName, out nint handle, out string? resolvedPath)
        {
            foreach (string candidate in EnumerateCandidates(libraryName))
            {
                if (NativeLibrary.TryLoad(candidate, out handle))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
            handle = 0;
            resolvedPath = null;
            return false;
        }

        public static nint ResolveLibraryImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            foreach (string candidate in EnumerateCandidates(libraryName))
            {
                // A configured runtime never silently resolves another version from PATH.
                return NativeLibrary.Load(candidate);
            }
            return 0;
        }
    }
}
