using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{

internal readonly struct ThirdPartyNativeLibraryProfile
{
    public readonly string Vendor;
    public readonly string Library;
    public readonly string WindowsFileName;
    public readonly string LinuxFileName;
    public readonly string MacFileName;

    public ThirdPartyNativeLibraryProfile(string vendor, string library, string? windowsFileName, string? linuxFileName, string? macFileName)
    {
        Vendor = vendor;
        Library = library;
        WindowsFileName = windowsFileName ?? string.Empty;
        LinuxFileName = linuxFileName ?? string.Empty;
        MacFileName = macFileName ?? string.Empty;
    }
}

internal static class ThirdPartyNativeLibraryResolver
{
    public const string ThirdPartyRootEnvironmentVariableName = "INFINITY_THIRDPARTY_NATIVE_ROOT";
    private const string VendorMicrosoft = "Microsoft";
    private static readonly object s_Sync = new();
    private static readonly HashSet<Assembly> s_RegisteredAssemblies = new();

    private static readonly Dictionary<string, ThirdPartyNativeLibraryProfile> s_LibraryProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dxcompiler"] = new(VendorMicrosoft, "DXC", "dxcompiler.dll", "libdxcompiler.so", "libdxcompiler.dylib"),
        ["dxil"] = new(VendorMicrosoft, "DXC", "dxil.dll", string.Empty, string.Empty),
        ["directml"] = new(VendorMicrosoft, "DirectML", "DirectML.dll", string.Empty, string.Empty),
        ["directml.debug"] = new(VendorMicrosoft, "DirectML", "DirectML.Debug.dll", string.Empty, string.Empty),
        ["dstoragecore"] = new(VendorMicrosoft, "DirectStorage", "dstoragecore.dll", string.Empty, string.Empty),
        ["dstorage"] = new(VendorMicrosoft, "DirectStorage", "dstorage.dll", string.Empty, string.Empty),
        ["winpixeventruntime"] = new(VendorMicrosoft, "PIX", "WinPixEventRuntime.dll", string.Empty, string.Empty),
    };

    public static void EnsureResolverRegistered(Assembly assembly)
    {
        if (assembly == null)
        {
            return;
        }

        lock (s_Sync)
        {
            if (s_RegisteredAssemblies.Contains(assembly))
            {
                return;
            }

            try
            {
                NativeLibrary.SetDllImportResolver(assembly, ResolveLibraryImport);
            }
            catch (InvalidOperationException)
            {
                // If resolver was already set by another initialization path, continue.
            }

            s_RegisteredAssemblies.Add(assembly);
        }
    }

    public static IEnumerable<string> EnumerateCandidates(string libraryName)
    {
        if (!TryEnumerateCandidates(libraryName, out IEnumerable<string> candidates))
        {
            return Array.Empty<string>();
        }

        return candidates;
    }

    public static bool TryResolve(string libraryName, out nint handle, out string? resolvedPath)
    {
        handle = 0;
        resolvedPath = null;

        if (!TryEnumerateCandidates(libraryName, out IEnumerable<string> candidates))
        {
            return false;
        }

        List<string> candidateList = new();
        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidateList.Add(candidate);
            }
        }

        string normalized = NormalizeLibraryName(libraryName);
        Debug.WriteLine($"[ThirdPartyNativeLibraryResolver] candidates for '{normalized}': {string.Join(", ", candidateList)}");

        foreach (string candidate in candidateList)
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                resolvedPath = candidate;
                bool fromThirdParty = candidate.Contains($"{Path.DirectorySeparatorChar}ThirdParty{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || candidate.Contains($"{Path.AltDirectorySeparatorChar}ThirdParty{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                Debug.WriteLine($"[ThirdPartyNativeLibraryResolver] loaded '{normalized}' from '{candidate}' (fromThirdParty={fromThirdParty})");
                return true;
            }
        }

        return false;
    }

    public static nint ResolveLibraryImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (TryResolve(libraryName, out nint handle, out _))
        {
            return handle;
        }

        return 0;
    }

    private static bool TryEnumerateCandidates(string libraryName, out IEnumerable<string> candidates)
    {
        candidates = Array.Empty<string>();
        string normalized = NormalizeLibraryName(libraryName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!s_LibraryProfiles.TryGetValue(normalized, out ThirdPartyNativeLibraryProfile profile))
        {
            return false;
        }

        string? osFolder = ResolveBuildOsFolder();
        if (string.IsNullOrWhiteSpace(osFolder))
        {
            return false;
        }

        string archFolder = ResolveBuildArchFolder();
        if (string.IsNullOrWhiteSpace(archFolder))
        {
            return false;
        }

        string? nativeFileName = ResolveLibraryFileName(profile, osFolder);
        if (string.IsNullOrWhiteSpace(nativeFileName))
        {
            return false;
        }

        List<string> candidateList = new();
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in EnumerateThirdPartyRoots())
        {
            foreach (string candidate in EnumerateCandidateLibraryPaths(root, profile, osFolder, archFolder, nativeFileName))
            {
                if (emitted.Add(candidate))
                {
                    candidateList.Add(candidate);
                }
            }
        }

        candidates = candidateList;
        return true;
    }

    private static IEnumerable<string> EnumerateCandidateLibraryPaths(string root, ThirdPartyNativeLibraryProfile profile, string osFolder, string archFolder, string nativeFileName)
    {
        string normalizedRoot = TryResolveDirectoryExplicitPath(root);
        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            yield break;
        }

        string rootName = Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string vendor = profile.Vendor;

        string normalizedVendorRoot;
        if (rootName.Equals("ThirdParty", StringComparison.OrdinalIgnoreCase))
        {
            normalizedVendorRoot = Path.Combine(normalizedRoot, vendor);
        }
        else if (rootName.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
        {
            normalizedVendorRoot = Path.Combine(normalizedRoot, "ThirdParty", vendor);
        }
        else if (rootName.Equals(vendor, StringComparison.OrdinalIgnoreCase))
        {
            normalizedVendorRoot = normalizedRoot;
        }
        else if (!rootName.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
        {
            normalizedVendorRoot = Path.Combine(normalizedRoot, "ThirdParty", vendor);
        }
        else
        {
            normalizedVendorRoot = Path.Combine(normalizedRoot, vendor);
        }

        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        static string? TryNormalize(string candidate)
        {
            return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
        }

        if (!rootName.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
        {
            string? rootCandidate0 = TryNormalize(Path.Combine(normalizedRoot, "Binaries", "ThirdParty", vendor));
            if (!string.IsNullOrWhiteSpace(rootCandidate0))
            {
                string candidate = Path.Combine(rootCandidate0, profile.Library, osFolder, archFolder, nativeFileName);
                if (emitted.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        string? rootCandidate1 = TryNormalize(Path.Combine(normalizedRoot, "ThirdParty", vendor));
        if (!string.IsNullOrWhiteSpace(rootCandidate1))
        {
            string candidate = Path.Combine(rootCandidate1, profile.Library, osFolder, archFolder, nativeFileName);
            if (emitted.Add(candidate))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedVendorRoot))
        {
            string candidate = Path.Combine(normalizedVendorRoot, profile.Library, osFolder, archFolder, nativeFileName);
            if (emitted.Add(candidate))
            {
                yield return candidate;
            }
        }

        string? rootCandidate3 = TryNormalize(Path.Combine(normalizedRoot, vendor, "ThirdParty"));
        if (!string.IsNullOrWhiteSpace(rootCandidate3))
        {
            string candidate = Path.Combine(rootCandidate3, profile.Library, osFolder, archFolder, nativeFileName);
            if (emitted.Add(candidate))
            {
                yield return candidate;
            }
        }

        // Fallback for historical copy layouts where library directory is nested one level deeper.
        string? rootCandidate4 = TryNormalize(Path.Combine(normalizedRoot, vendor, "ThirdParty", profile.Library, osFolder, archFolder, nativeFileName));
        if (!string.IsNullOrWhiteSpace(rootCandidate4))
        {
            if (emitted.Add(rootCandidate4))
            {
                yield return rootCandidate4;
            }
        }
    }

    private static IEnumerable<string> EnumerateThirdPartyRoots()
    {
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        List<string> repositoryRoots = new();

        foreach (string explicitRoot in EnumerateExplicitThirdPartyRoots())
        {
            if (emitted.Add(explicitRoot))
            {
                yield return explicitRoot;
            }
        }

        foreach (string root in SearchRepositoryRoots())
        {
            if (emitted.Add(root))
            {
                repositoryRoots.Add(root);
                yield return root;
            }
        }

        foreach (string binariesRoot in EnumerateBinariesRoots(repositoryRoots))
        {
            if (emitted.Add(binariesRoot))
            {
                yield return binariesRoot;
            }
        }
    }

    private static IEnumerable<string> EnumerateExplicitThirdPartyRoots()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable(ThirdPartyRootEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(explicitRoot))
        {
            yield break;
        }

        foreach (string? root in explicitRoot.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string candidate = TryResolveDirectoryExplicitPath(root);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateBinariesRoots(IEnumerable<string> repositoryRoots)
    {
        foreach (string repositoryRoot in repositoryRoots)
        {
            string binariesRoot = TryResolveDirectoryExplicitPath(Path.Combine(repositoryRoot, "Binaries"));
            if (!string.IsNullOrWhiteSpace(binariesRoot))
            {
                yield return binariesRoot;
            }
        }
    }

    private static string TryResolveDirectoryExplicitPath(string candidateRoot)
    {
        if (string.IsNullOrWhiteSpace(candidateRoot))
        {
            return string.Empty;
        }

        try
        {
            string normalized = Path.GetFullPath(candidateRoot);
            if (Directory.Exists(normalized))
            {
                return normalized;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static IEnumerable<string> SearchRepositoryRoots()
    {
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        foreach (string start in EnumerateSearchRoots())
        {
            DirectoryInfo? current = new(start);
            for (int i = 0; i < 16 && current != null; i++)
            {
                string candidate = Path.Combine(current.FullName, "Binaries");
                if (Directory.Exists(candidate))
                {
                    string root = current.FullName;
                    if (emitted.Add(root))
                    {
                        yield return root;
                    }
                }

                current = current.Parent;
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        string? baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield return baseDirectory;
        }

        string cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            yield return cwd;
        }
    }

    private static string ResolveLibraryFileName(ThirdPartyNativeLibraryProfile profile, string osFolder)
    {
        return osFolder switch
        {
            "Win" => profile.WindowsFileName,
            "Linux" => profile.LinuxFileName,
            "macOS" => profile.MacFileName,
            _ => string.Empty,
        };
    }

    private static string ResolveBuildOsFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Win";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return string.Empty;
    }

    private static string ResolveBuildArchFolder()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "AMD64";
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return "ARM64";
        }

        return string.Empty;
    }

    private static string NormalizeLibraryName(string? libraryName)
    {
        if (string.IsNullOrWhiteSpace(libraryName))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileNameWithoutExtension(libraryName.Trim());
        if (fileName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(3);
        }

        return fileName;
    }
}
}
