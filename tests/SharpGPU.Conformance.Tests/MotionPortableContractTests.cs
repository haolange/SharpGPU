using System;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class MotionPortableContractTests
    {
        [Fact]
        public void Motion_ObjectModelSymbolsExist()
        {
            Assert.NotNull(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "Motion"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructTriangles), "MotionVertexBuffer"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructTriangles), "MotionVertexOffset"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructTriangles), "MotionVertexStride"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructInstance), "MotionType"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructInstance), "MotionTransformMatrix"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructInstance), "MotionSrtT0"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructInstance), "MotionSrtT1"));

            Type[] exported = typeof(RHIDevice).Assembly.GetExportedTypes();
            Assert.Contains(exported, static type => type.Name == nameof(ERHIAccelStructMotionInstanceType));
            Assert.Contains(exported, static type => type.Name == nameof(RHIAccelStructSrtTransform));
            Assert.True(Enum.IsDefined(ERHIAccelStructMotionInstanceType.None));
            Assert.True(Enum.IsDefined(ERHIAccelStructMotionInstanceType.Matrix));
            Assert.True(Enum.IsDefined(ERHIAccelStructMotionInstanceType.Srt));
            Assert.Null(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "ShaderExecutionReordering"));
        }

        [Fact]
        public void VulkanMotionInstanceArrayStride_Is160Bytes()
        {
            Assert.Equal(160, VulkanRayTracingMotionNative.MotionInstanceByteCount);
            Assert.Equal(
                160,
                Marshal.SizeOf<VulkanRayTracingMotionNative.VkAccelerationStructureMotionInstanceNV>());
        }

        [Fact]
        public void MotionAttachment_ChangesDescriptorIdentity()
        {
            RHIBottomLevelAccelStructDescriptor staticBlas = new()
            {
                Geometries = new RHIAccelStructGeometry[]
                {
                    new RHIAccelStructTriangles
                    {
                        GeometryType = ERHIAccelStructGeometryType.Triangle,
                    },
                },
            };
            RHIBottomLevelAccelStructDescriptor motionBlas = staticBlas;
            motionBlas.Geometries = new RHIAccelStructGeometry[]
            {
                new RHIAccelStructTriangles
                {
                    GeometryType = ERHIAccelStructGeometryType.Triangle,
                    MotionVertexOffset = 16,
                },
            };

            Assert.NotEqual(
                Convert.ToHexString(RHIAccelStructMotionContract.HashBottomLevel(in staticBlas)),
                Convert.ToHexString(RHIAccelStructMotionContract.HashBottomLevel(in motionBlas)));

            RHITopLevelAccelStructDescriptor staticTlas = new()
            {
                Instances = new[]
                {
                    new RHIAccelStructInstance
                    {
                        MotionType = ERHIAccelStructMotionInstanceType.None,
                    },
                },
            };
            RHITopLevelAccelStructDescriptor motionTlas = new()
            {
                Instances = new[]
                {
                    new RHIAccelStructInstance
                    {
                        MotionType = ERHIAccelStructMotionInstanceType.Matrix,
                        MotionTransformMatrix = new float4x4(
                            new float4(1f, 0f, 0f, 0f),
                            new float4(0f, 1f, 0f, 0f),
                            new float4(0f, 0f, 1f, 0f),
                            new float4(0f, 0f, 0f, 1f)),
                    },
                },
            };
            Assert.NotEqual(
                Convert.ToHexString(RHIAccelStructMotionContract.HashTopLevel(in staticTlas)),
                Convert.ToHexString(RHIAccelStructMotionContract.HashTopLevel(in motionTlas)));
        }

        [Fact]
        public void UnprobedAndUnavailableBackends_MotionCreateThrows()
        {
            RHICapability unavailable = RHICapability.Unavailable(
                "unprobed",
                ERHICapabilityProbeKind.BackendContract,
                "MotionPortableContractTests");
            RHIRayTracingCapabilities unprobed = new(
                unavailable,
                unavailable,
                unavailable,
                unavailable,
                unavailable);
            Assert.Equal(ERHICapabilityTier.Unavailable, unprobed.Motion.Tier);
            Assert.Throws<NotSupportedException>(() => unprobed.Motion.Require("RayTracing.Motion"));

            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    RHIDevice device = instance.GetDevice(0);
                    if (device.Capabilities.RayTracing.Motion.Tier != ERHICapabilityTier.Unavailable)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(
                            device.Capabilities.RayTracing.Motion.Provenance.Source));
                        continue;
                    }

                    MotionQualificationHelper.AssertUnavailableMotionCreateThrows(device);
                }
            }
        }

        [Fact]
        public void MotionFlag_MustMatchMotionData()
        {
            bool probed = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    probed = true;
                    RHIDevice device = instance.GetDevice(0);
                    Assert.Throws<ArgumentException>(
                        () => device.CreateTopAccelerationStructure(
                            new RHITopLevelAccelStructDescriptor
                            {
                                Flag = ERHIAccelStructFlag.Motion,
                                Instances = new[]
                                {
                                    new RHIAccelStructInstance
                                    {
                                        MotionType = ERHIAccelStructMotionInstanceType.None,
                                    },
                                },
                            }));
                    Assert.Throws<ArgumentException>(
                        () => device.CreateTopAccelerationStructure(
                            new RHITopLevelAccelStructDescriptor
                            {
                                Instances = new[]
                                {
                                    new RHIAccelStructInstance
                                    {
                                        MotionType = ERHIAccelStructMotionInstanceType.Matrix,
                                        MotionTransformMatrix = new float4x4(
                                            new float4(1f, 0f, 0f, 0f),
                                            new float4(0f, 1f, 0f, 0f),
                                            new float4(0f, 0f, 1f, 0f),
                                            new float4(0f, 0f, 0f, 1f)),
                                    },
                                },
                            }));
                }
            }

            Assert.True(probed, "Motion flag XOR fail-closed requires at least one live RHI device.");
        }

        [Fact]
        public void UnknownMotionInstanceType_IsFailClosed()
        {
            bool probed = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    probed = true;
                    RHIDevice device = instance.GetDevice(0);
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateTopAccelerationStructure(
                            new RHITopLevelAccelStructDescriptor
                            {
                                Instances = new[]
                                {
                                    new RHIAccelStructInstance
                                    {
                                        MotionType = (ERHIAccelStructMotionInstanceType)99,
                                    },
                                },
                            }));
                }
            }

            Assert.True(probed, "Unknown motion-type fail-closed requires at least one live RHI device.");
        }

        [Fact]
        public void MotionVertexStride_MismatchIsFailClosed()
        {
            bool probed = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    probed = true;
                    MotionQualificationHelper.AssertMismatchedMotionVertexStrideThrows(instance.GetDevice(0));
                }
            }

            Assert.True(probed, "MotionVertexStride fail-closed requires at least one live RHI device.");
        }

        [Fact]
        public void IndexedTriangleIntent_DoesNotSilentlyFallback()
        {
            Assert.Throws<ArgumentException>(
                () => RHIAccelStructTriangleContract.ValidateIndexIntent(
                    new RHIAccelStructTriangles
                    {
                        IndexCount = 3,
                    }));
            Assert.Throws<ArgumentException>(
                () => RHIAccelStructTriangleContract.ValidateIndexIntent(
                    new RHIAccelStructTriangles
                    {
                        IndexCount = 2,
                    }));

            bool probed = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    probed = true;
                    MotionQualificationHelper.AssertIndexedTriangleIntentIsFailClosed(instance.GetDevice(0));
                }
            }

            Assert.True(probed, "Indexed triangle intent fail-closed requires at least one live RHI device.");
        }

        [Fact]
        public void MotionUpdate_LeavesDescriptorUnchangedOnFailure()
        {
            bool probed = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    RHIDevice device = instance.GetDevice(0);
                    if (device.Capabilities.RayTracing.Pipeline.Tier == ERHICapabilityTier.Unavailable)
                    {
                        continue;
                    }

                    probed = true;
                    MotionQualificationHelper.AssertFailedMotionUpdateLeavesDescriptorUnchanged(device);
                }
            }

            Assert.True(probed, "Failed motion Update fail-closed requires a live ray-tracing device.");
        }

        private static PropertyInfo? GetPublicInstanceProperty(Type type, string name)
        {
            return type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }

        private static FieldInfo? GetPublicInstanceField(Type type, string name)
        {
            return type.GetField(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
    }
}
