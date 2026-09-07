using System;
using System.Reflection;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class TextureUsagePortableContractTests
    {
        [Fact]
        public void TextureUsage_ShouldBeFlagsWithoutPendingAndWithNoneZero()
        {
            Assert.NotNull(typeof(ERHITextureUsage).GetCustomAttribute<FlagsAttribute>());
            Assert.Equal(0, (int)ERHITextureUsage.None);
            string[] names = Enum.GetNames<ERHITextureUsage>();
            Assert.DoesNotContain("Pending", names);
            Assert.Contains(nameof(ERHITextureUsage.None), names);
            Assert.Contains(nameof(ERHITextureUsage.CopySrc), names);
            Assert.Contains(nameof(ERHITextureUsage.CopyDst), names);
            Assert.Contains(nameof(ERHITextureUsage.DepthStencil), names);
            Assert.Contains(nameof(ERHITextureUsage.RenderTarget), names);
            Assert.Contains(nameof(ERHITextureUsage.ResolveTarget), names);
            Assert.Contains(nameof(ERHITextureUsage.ShaderResource), names);
            Assert.Contains(nameof(ERHITextureUsage.UnorderedAccess), names);
            Assert.False(RHIFormatSupportQuery.IsKnownTextureUsage(ERHITextureUsage.None));
            Assert.False(RHIFormatSupportQuery.IsKnownTextureUsage((ERHITextureUsage)0x80));
            Assert.True(RHIFormatSupportQuery.IsKnownTextureUsage(ERHITextureUsage.ShaderResource));
        }

        [Fact]
        public void CreateTexture_ShouldRejectZeroAndUnknownUsage()
        {
            bool probedAny = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
                {
                    continue;
                }

                probedAny = true;
                using (instance)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateTexture(CreateDescriptor(ERHITextureUsage.None)));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateTexture(CreateDescriptor((ERHITextureUsage)0x80)));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.True(
                    probedAny,
                    "Windows host must create DX12 or Vulkan to reject illegal texture usage.");
            }
        }

        [Fact]
        public void GetSparseTextureMemoryRequirements_ShouldRejectZeroAndUnknownUsage()
        {
            bool probedAny = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
                {
                    continue;
                }

                probedAny = true;
                using (instance)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.GetSparseTextureMemoryRequirements(
                            CreateDescriptor(ERHITextureUsage.None)));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.GetSparseTextureMemoryRequirements(
                            CreateDescriptor((ERHITextureUsage)0x80)));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.True(
                    probedAny,
                    "Windows host must create DX12 or Vulkan to reject illegal sparse texture usage.");
            }
        }

        [Fact]
        public void MetalWrap_WithoutExplicitUsage_ShouldFailClosed()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => MetalTexture.BuildDescriptorFromNative(default, ERHITextureUsage.None));
            Assert.Contains("explicit", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MetalTexture.BuildDescriptorFromNative(default, (ERHITextureUsage)0x80));
        }

        private static RHITextureDescriptor CreateDescriptor(ERHITextureUsage usage)
        {
            return new RHITextureDescriptor
            {
                MipCount = 1,
                Extent = new uint3(4, 4, 1),
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = usage,
                Dimension = ERHITextureDimension.Texture2D,
            };
        }

        private static bool TryCreateDevice(
            ERHIBackend backend,
            out RHIInstance? instance,
            out RHIDevice? device)
        {
            instance = null;
            device = null;
            if (!FeatureContractContext.TryCreateInstance(backend, out instance, out _))
            {
                return false;
            }

            if (instance.DeviceCount <= 0)
            {
                instance.Dispose();
                instance = null;
                return false;
            }

            device = instance.GetDevice(0);
            return device != null;
        }
    }
}
