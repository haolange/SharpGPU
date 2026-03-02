using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal static unsafe class Dx12PixEventMarker
    {
#if DEBUG
        private const string PixRuntimeFileName = "WinPixEventRuntime.dll";
        private static readonly object Sync = new();
        private static bool s_RuntimeLoadAttempted;
        private static bool s_RuntimeAvailable;
        private static nint s_RuntimeHandle;

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXBeginEventOnCommandList(nint commandList, ulong color, [MarshalAs(UnmanagedType.LPStr)] string formatString);

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXEndEventOnCommandList(nint commandList);
#endif

        public static void BeginEvent(nint commandList, string name)
        {
#if DEBUG
            if (commandList == 0 || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (!EnsureRuntimeLoaded())
            {
                return;
            }

            PIXBeginEventOnCommandList(commandList, 0UL, name);
#endif
        }

        public static void EndEvent(nint commandList)
        {
#if DEBUG
            if (commandList == 0 || !EnsureRuntimeLoaded())
            {
                return;
            }

            PIXEndEventOnCommandList(commandList);
#endif
        }

#if DEBUG
        private static bool EnsureRuntimeLoaded()
        {
            lock (Sync)
            {
                if (s_RuntimeLoadAttempted)
                {
                    return s_RuntimeAvailable;
                }

                s_RuntimeLoadAttempted = true;
                foreach (string candidate in EnumerateRuntimeCandidates())
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    if (NativeLibrary.TryLoad(candidate, out s_RuntimeHandle))
                    {
                        s_RuntimeAvailable = true;
                        return true;
                    }
                }

                if (NativeLibrary.TryLoad(PixRuntimeFileName, out s_RuntimeHandle))
                {
                    s_RuntimeAvailable = true;
                    return true;
                }

                return false;
            }
        }

        private static IEnumerable<string> EnumerateRuntimeCandidates()
        {
            string baseDir = AppContext.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
            string? archFolder = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "AMD64",
                Architecture.Arm64 => "ARM64",
                _ => null,
            };

            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(archFolder))
            {
                yield break;
            }

            HashSet<string> visitedRoots = new(StringComparer.OrdinalIgnoreCase);
            foreach (string start in new[] { baseDir, assemblyDir, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrWhiteSpace(start))
                {
                    continue;
                }

                DirectoryInfo? current = new(start);
                int depth = 0;
                while (current != null && depth < 16)
                {
                    if (visitedRoots.Add(current.FullName))
                    {
                        yield return Path.Combine(current.FullName, "Binaries", "ThirdParty", "PIX", "Win", archFolder, PixRuntimeFileName);
                    }

                    current = current.Parent;
                    depth++;
                }
            }

        }
#endif
    }
}
