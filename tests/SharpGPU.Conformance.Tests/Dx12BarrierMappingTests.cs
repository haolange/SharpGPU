#if SHARPGPU_ENABLE_DX12
using Xunit;
using System;
using System.Linq;
using SharpGPU;
using System.Reflection;
using Vortice.Direct3D12;

namespace SharpGPU.Conformance.Tests
{
    public class Dx12BarrierMappingTests
    {
        private static readonly Type s_EmitterType = typeof(RHIBarrier).Assembly.GetType("SharpGPU.Dx12BarrierEmitter", throwOnError: true)!;

        [Fact]
        public void EnhancedSyncMapping_CombinesAllRequestedStages()
        {
            BarrierSync sync = InvokePrivate<BarrierSync>(
                "ConvertToBarrierSync",
                ERHIStageMask.Vertex
                | ERHIStageMask.Transfer
                | ERHIStageMask.Indirect
                | ERHIStageMask.IndexInput
                | ERHIStageMask.RayTracing
                | ERHIStageMask.AccelStructBuild
                | ERHIStageMask.AccelStructCopy);

            Assert.True((sync & BarrierSync.VertexShading) != 0);
            Assert.True((sync & BarrierSync.Copy) != 0);
            Assert.True((sync & BarrierSync.ExecuteIndirect) != 0);
            Assert.True((sync & BarrierSync.IndexInput) != 0);
            Assert.True((sync & BarrierSync.Raytracing) != 0);
            Assert.True((sync & BarrierSync.BuildRaytracingAccelerationStructure) != 0);
            Assert.True((sync & BarrierSync.CopyRaytracingAccelerationStructure) != 0);
        }

        [Fact]
        public void EnhancedAccessMapping_MapsShaderAndTransferFlags()
        {
            BarrierAccess access = InvokePrivate<BarrierAccess>(
                "ConvertToBarrierAccess",
                ERHIAccessMask.ShaderRead | ERHIAccessMask.ShaderWrite | ERHIAccessMask.TransferRead | ERHIAccessMask.TransferWrite);

            Assert.True((access & BarrierAccess.UnorderedAccess) != 0);
            Assert.False((access & BarrierAccess.ShaderResource) != 0);
            Assert.True((access & BarrierAccess.CopySource) != 0);
            Assert.True((access & BarrierAccess.CopyDestination) != 0);
        }

        [Fact]
        public void EnhancedLayoutMapping_IsQueueSpecific()
        {
            BarrierLayout computeLayout = InvokePrivate<BarrierLayout>(
                "ConvertToBarrierLayout",
                ERHITextureLayout.ShaderReadOnly,
                ERHIPipelineType.Compute);

            BarrierLayout graphicsLayout = InvokePrivate<BarrierLayout>(
                "ConvertToBarrierLayout",
                ERHITextureLayout.ShaderReadOnly,
                ERHIPipelineType.Graphics);

            Assert.Equal(BarrierLayout.ComputeQueueShaderResource, computeLayout);
            Assert.Equal(BarrierLayout.DirectQueueShaderResource, graphicsLayout);
        }

        [Fact]
        public void EnhancedSubresourceRangeMapping_HandlesWholeAndPartialRanges()
        {
            const uint textureMipLevelCount = 8u;
            const uint textureArrayLayerCount = 10u;

            BarrierSubresourceRange whole = InvokePrivate<BarrierSubresourceRange>(
                "ConvertToSubresourceRange",
                RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Color);

            Assert.Equal(0U, whole.IndexOrFirstMipLevel);
            Assert.Equal(textureMipLevelCount, whole.NumMipLevels);
            Assert.Equal(0U, whole.FirstArraySlice);
            Assert.Equal(textureArrayLayerCount, whole.NumArraySlices);
            Assert.Equal(0U, whole.FirstPlane);
            Assert.Equal(1U, whole.NumPlanes);

            RHITextureSubresourceRange partialRange = new RHITextureSubresourceRange
            {
                BaseMipLevel = 2,
                MipLevelCount = 3,
                BaseArrayLayer = 4,
                ArrayLayerCount = 5,
                AspectMask = ERHITextureAspectMask.Color
            };

            BarrierSubresourceRange partial = InvokePrivate<BarrierSubresourceRange>(
                "ConvertToSubresourceRange",
                partialRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Color);

            Assert.Equal(2U, partial.IndexOrFirstMipLevel);
            Assert.Equal(3U, partial.NumMipLevels);
            Assert.Equal(4U, partial.FirstArraySlice);
            Assert.Equal(5U, partial.NumArraySlices);
            Assert.Equal(0U, partial.FirstPlane);
            Assert.Equal(1U, partial.NumPlanes);
        }

        [Fact]
        public void ResourceBarrierMapping_UsesConservativeStates()
        {
            ResourceStates conservativeCommon = InvokePrivate<ResourceStates>(
                "ConvertToResourceTextureStates",
                ERHITextureLayout.Undefined,
                ERHIAccessMask.None,
                ERHIPipelineType.Graphics);
            Assert.Equal(ResourceStates.Common, conservativeCommon);

            ResourceStates copyDest = InvokePrivate<ResourceStates>(
                "ConvertToResourceTextureStates",
                ERHITextureLayout.CopyDestination,
                ERHIAccessMask.TransferWrite,
                ERHIPipelineType.Graphics);
            Assert.True((copyDest & ResourceStates.CopyDest) != 0);
        }

        private static T InvokePrivate<T>(string methodName, params object[] args)
        {
            MethodInfo? method = s_EmitterType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(candidate => IsCompatible(candidate, methodName, args));
            Assert.NotNull(method);
            object? result = method!.Invoke(null, args);
            return (T)result!;
        }

        private static bool IsCompatible(MethodInfo method, string methodName, object[] args)
        {
            if (!String.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length)
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; ++i)
            {
                Type parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    parameterType = parameterType.GetElementType()!;
                }

                object arg = args[i];
                if (arg == null)
                {
                    if (parameterType.IsValueType)
                    {
                        return false;
                    }

                    continue;
                }

                if (!parameterType.IsInstanceOfType(arg))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
