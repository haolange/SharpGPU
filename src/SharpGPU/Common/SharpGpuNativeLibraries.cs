using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    /// <summary>Explicit native runtime locations. Configure before creating a device.</summary>
    public static class SharpGpuNativeLibraries
    {
        private static readonly object s_Sync = new();
        private static string? s_DirectMLDirectory;
        private static string? s_DirectStorageDirectory;
        private static string? s_PixDirectory;
        private static bool s_Frozen;

        public static void Configure(string directMLDirectory, string directStorageDirectory, string pixDirectory)
        {
            string directML = ValidateDirectory(directMLDirectory);
            string directStorage = ValidateDirectory(directStorageDirectory);
            string pix = ValidateDirectory(pixDirectory);
            lock (s_Sync)
            {
                if (s_Frozen)
                {
                    throw new InvalidOperationException("SharpGPU native locations are frozen after the first runtime request.");
                }
                s_DirectMLDirectory = directML;
                s_DirectStorageDirectory = directStorage;
                s_PixDirectory = pix;
            }
        }

        internal static string Resolve(string library, string fileName)
        {
            lock (s_Sync)
            {
                s_Frozen = true;
                string? configured = library switch
                {
                    "DirectML" => s_DirectMLDirectory,
                    "DirectStorage" => s_DirectStorageDirectory,
                    "PIX" => s_PixDirectory,
                    _ => throw new ArgumentOutOfRangeException(nameof(library)),
                };
                if (configured is not null)
                {
                    return Path.Combine(configured, fileName);
                }
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x64",
                    Architecture.Arm64 => "arm64",
                    _ => throw new PlatformNotSupportedException("SharpGPU desktop native runtimes require x64 or ARM64."),
                };
                string portable = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-" + arch, "native", fileName);
                return File.Exists(portable) ? portable : Path.Combine(AppContext.BaseDirectory, fileName);
            }
        }

        private static string ValidateDirectory(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            if (!Path.IsPathFullyQualified(directory))
            {
                throw new ArgumentException("Native runtime directories must be absolute paths.", nameof(directory));
            }
            string fullPath = Path.GetFullPath(directory);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(fullPath);
            }
            return fullPath;
        }
    }
}
