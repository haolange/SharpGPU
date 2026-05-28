using System;
using Vortice.DXGI;
using Vortice.Direct3D;
using System.Diagnostics;
using Vortice.Direct3D12;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal static unsafe class Dx12PixEventMarker
    {
#if DEBUG
        private const string PixRuntimeFileName = "WinPixEventRuntime.dll";
        private static readonly object s_Sync = new();
        private static readonly object s_EventDepthSync = new();
        private static bool s_RuntimeLoadAttempted;
        private static bool s_RuntimeAvailable;
        private static nint s_RuntimeHandle;
        private static readonly Dictionary<nint, int> s_EventDepthByCommandList = new();

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXBeginEventOnCommandList(nint commandList, ulong color, [MarshalAs(UnmanagedType.LPStr)] string formatString);

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXEndEventOnCommandList(nint commandList);
#endif

        public static void BeginEvent(nint commandList, string name)
        {
#if DEBUG
            if (commandList == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Unnamed";
            }

            if (!EnsureRuntimeLoaded())
            {
                return;
            }

            try
            {
                PIXBeginEventOnCommandList(commandList, 0UL, name);
                lock (s_EventDepthSync)
                {
                    s_EventDepthByCommandList.TryGetValue(commandList, out int depth);
                    s_EventDepthByCommandList[commandList] = depth + 1;
                }
            }
            catch
            {
            }
#endif
        }

        public static void EndEvent(nint commandList)
        {
#if DEBUG
            if (commandList == 0 || !EnsureRuntimeLoaded())
            {
                return;
            }

            bool hasEventDepth = false;
            lock (s_EventDepthSync)
            {
                if (s_EventDepthByCommandList.TryGetValue(commandList, out int depth) && depth > 0)
                {
                    depth--;
                    if (depth == 0)
                    {
                        s_EventDepthByCommandList.Remove(commandList);
                    }
                    else
                    {
                        s_EventDepthByCommandList[commandList] = depth;
                    }
                    hasEventDepth = true;
                }
            }

            if (!hasEventDepth)
            {
                return;
            }

            try
            {
                PIXEndEventOnCommandList(commandList);
            }
            catch
            {
            }
#endif
        }

#if DEBUG
        private static bool EnsureRuntimeLoaded()
        {
            lock (s_Sync)
            {
                if (s_RuntimeLoadAttempted)
                {
                    return s_RuntimeAvailable;
                }

                s_RuntimeLoadAttempted = true;
                ThirdPartyNativeLibraryResolver.EnsureResolverRegistered(typeof(Dx12PixEventMarker).Assembly);

                if (ThirdPartyNativeLibraryResolver.TryResolve(PixRuntimeFileName, out s_RuntimeHandle, out string? runtimePath))
                {
                    s_RuntimeAvailable = true;
                    Debug.WriteLine($"[Dx12PixEventMarker] PIX runtime loaded from '{runtimePath ?? PixRuntimeFileName}'");
                    return true;
                }

                string candidateList = string.Join(", ", ThirdPartyNativeLibraryResolver.EnumerateCandidates(PixRuntimeFileName));
                if (string.IsNullOrWhiteSpace(candidateList))
                {
                    candidateList = "(none)";
                }

                Debug.WriteLine($"[Dx12PixEventMarker] PIX runtime unavailable, markers disabled. Candidates: {candidateList}");
                return false;
            }
        }
#endif
    }
}
