using System;
using System.Text;
using Infinity.Core;
using NUnit.Framework;
using System.Diagnostics;
using Infinity.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal enum EOSPlatform
    {
        Windows,
        Linux,
        Android,
        MacOS,
        iOS
    }

    internal static unsafe class VulkanUtility
    {
        public static EOSPlatform GetCurrentOSPlatfom()
        {
            return !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? EOSPlatform.Windows : (!RuntimeInformation.OSDescription.Contains("Darwin") ? EOSPlatform.MacOS : EOSPlatform.iOS)) : (!RuntimeInformation.OSDescription.Contains("Unix") ? EOSPlatform.Linux : EOSPlatform.Android)) : EOSPlatform.Windows;
        }

        public static byte* ToPointer(this string text)
        {
            return (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(text);
        }

        public static uint Version(uint major, uint minor, uint patch)
        {
            return (major << 22) | (minor << 12) | patch;
        }

        public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
        {
            return (&memoryProperties.memoryTypes_0)[index];
        }

        public static unsafe string GetString(byte* stringStart)
        {
            int characters = 0;
            while (stringStart[characters] != 0)
            {
                characters++;
            }

            return System.Text.Encoding.UTF8.GetString(stringStart, characters);
        }

        [Conditional("DEBUG")]
        public static void CheckErrors(VkResult result)
        {
            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(result.ToString());
            }
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
