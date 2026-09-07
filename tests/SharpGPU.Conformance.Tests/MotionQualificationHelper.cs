using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;

namespace SharpGPU.Conformance.Tests
{
    internal static class MotionQualificationHelper
    {
        internal static void BuildTinyMotionInstanceTlas(
            RHIDevice device,
            RHICommandQueue queue,
            RHIFence fence)
        {
            float[] vertices =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, vertices);

            ushort[] indices = { 0, 1, 2 };
            using RHIBuffer indexBuffer = CreateUpload(
                device,
                indices.Length * sizeof(ushort),
                ERHIBufferUsage.IndexBuffer | ERHIBufferUsage.AccelStruct);
            WriteUshorts(indexBuffer, indices);

            using RHIBottomLevelAccelStruct blas = device.CreateBottomAccelerationStructure(
                new RHIBottomLevelAccelStructDescriptor
                {
                    Geometries = new RHIAccelStructGeometry[]
                    {
                        new RHIAccelStructTriangles
                        {
                            GeometryType = ERHIAccelStructGeometryType.Triangle,
                            GeometryFlag = ERHIAccelStructGeometryFlag.None,
                            IndexCount = 3,
                            IndexBuffer = indexBuffer,
                            IndexFormat = ERHIBufferFormat.UInt16,
                            VertexCount = 3,
                            VertexStride = 16,
                            VertexBuffer = vertexBuffer,
                            VertexFormat = ERHIPixelFormat.R32G32B32A32_Float,
                        },
                    },
                });

            float4x4 t0 = IdentityMatrix();
            float4x4 t1 = IdentityMatrix();
            t1.c3.x = 0.25f;

            using RHITopLevelAccelStruct tlas = device.CreateTopAccelerationStructure(
                new RHITopLevelAccelStructDescriptor
                {
                    Flag = ERHIAccelStructFlag.Motion,
                    Instances = new[]
                    {
                        new RHIAccelStructInstance
                        {
                            InstanceID = 0,
                            InstanceMask = 0xFF,
                            TransformMatrix = t0,
                            MotionType = ERHIAccelStructMotionInstanceType.Matrix,
                            MotionTransformMatrix = t1,
                            BottomLevelAccelStruct = blas,
                        },
                    },
                });

            using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
            commandBuffer.Begin("Motion.TinyInstanceBuild");
            RHIRaytracingEncoder encoder = commandBuffer.BeginRaytracingPass(
                new RHIRayTracingPassDescriptor
                {
                    Name = "Motion.TinyInstanceBuild",
                });
            encoder.BuildAccelerationStructure(blas);
            encoder.BuildAccelerationStructure(tlas);
            commandBuffer.EndRaytracingPass();
            commandBuffer.End();

            fence.Reset();
            queue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: fence));
            fence.Wait();
        }

        internal static void BuildTinyMotionTriangleBlas(
            RHIDevice device,
            RHICommandQueue queue,
            RHIFence fence)
        {
            float[] verticesT0 =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            float[] verticesT1 =
            {
                0f, 0f, 0.25f, 1f,
                1f, 0f, 0.25f, 1f,
                0f, 1f, 0.25f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(
                device,
                verticesT0.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, verticesT0);
            using RHIBuffer motionVertexBuffer = CreateUpload(
                device,
                verticesT1.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(motionVertexBuffer, verticesT1);

            ushort[] indices = { 0, 1, 2 };
            using RHIBuffer indexBuffer = CreateUpload(
                device,
                indices.Length * sizeof(ushort),
                ERHIBufferUsage.IndexBuffer | ERHIBufferUsage.AccelStruct);
            WriteUshorts(indexBuffer, indices);

            using RHIBottomLevelAccelStruct blas = device.CreateBottomAccelerationStructure(
                new RHIBottomLevelAccelStructDescriptor
                {
                    Flag = ERHIAccelStructFlag.Motion,
                    Geometries = new RHIAccelStructGeometry[]
                    {
                        new RHIAccelStructTriangles
                        {
                            GeometryType = ERHIAccelStructGeometryType.Triangle,
                            GeometryFlag = ERHIAccelStructGeometryFlag.None,
                            IndexCount = 3,
                            IndexBuffer = indexBuffer,
                            IndexFormat = ERHIBufferFormat.UInt16,
                            VertexCount = 3,
                            VertexStride = 16,
                            VertexBuffer = vertexBuffer,
                            VertexFormat = ERHIPixelFormat.R32G32B32A32_Float,
                            MotionVertexBuffer = motionVertexBuffer,
                            MotionVertexOffset = 0,
                            MotionVertexStride = 16,
                        },
                    },
                });

            using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
            commandBuffer.Begin("Motion.TinyTriangleBuild");
            RHIRaytracingEncoder encoder = commandBuffer.BeginRaytracingPass(
                new RHIRayTracingPassDescriptor
                {
                    Name = "Motion.TinyTriangleBuild",
                });
            encoder.BuildAccelerationStructure(blas);
            commandBuffer.EndRaytracingPass();
            commandBuffer.End();

            fence.Reset();
            queue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: fence));
            fence.Wait();
        }

        internal static void AssertUnavailableMotionCreateThrows(RHIDevice device)
        {
            float[] vertices =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, vertices);
            using RHIBuffer motionVertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(motionVertexBuffer, vertices);

            AssertThrowsNotSupported(
                () => device.CreateBottomAccelerationStructure(
                    new RHIBottomLevelAccelStructDescriptor
                    {
                        Flag = ERHIAccelStructFlag.Motion,
                        Geometries = new RHIAccelStructGeometry[]
                        {
                            new RHIAccelStructTriangles
                            {
                                GeometryType = ERHIAccelStructGeometryType.Triangle,
                                VertexCount = 3,
                                VertexStride = 16,
                                VertexBuffer = vertexBuffer,
                                VertexFormat = ERHIPixelFormat.R32G32B32A32_Float,
                                MotionVertexBuffer = motionVertexBuffer,
                            },
                        },
                    }));

            AssertThrowsNotSupported(
                () => device.CreateTopAccelerationStructure(
                    new RHITopLevelAccelStructDescriptor
                    {
                        Flag = ERHIAccelStructFlag.Motion,
                        Instances = new[]
                        {
                            new RHIAccelStructInstance
                            {
                                MotionType = ERHIAccelStructMotionInstanceType.Matrix,
                                MotionTransformMatrix = IdentityMatrix(),
                            },
                        },
                    }));
        }

        internal static void AssertIndexedTriangleIntentIsFailClosed(RHIDevice device)
        {
            try
            {
                using RHIBottomLevelAccelStruct _ = device.CreateBottomAccelerationStructure(
                    new RHIBottomLevelAccelStructDescriptor
                    {
                        Geometries = new RHIAccelStructGeometry[]
                        {
                            new RHIAccelStructTriangles
                            {
                                GeometryType = ERHIAccelStructGeometryType.Triangle,
                                IndexCount = 3,
                            },
                        },
                    });
            }
            catch (ArgumentException)
            {
                float[] vertices =
                {
                    0f, 0f, 0f, 1f,
                    1f, 0f, 0f, 1f,
                    0f, 1f, 0f, 1f,
                };
                using RHIBuffer indexBuffer = CreateUpload(
                    device,
                    vertices.Length * sizeof(float),
                    ERHIBufferUsage.IndexBuffer | ERHIBufferUsage.AccelStruct);
                WriteFloats(indexBuffer, vertices);

                try
                {
                    using RHIBottomLevelAccelStruct _ = device.CreateBottomAccelerationStructure(
                        new RHIBottomLevelAccelStructDescriptor
                        {
                            Geometries = new RHIAccelStructGeometry[]
                            {
                                new RHIAccelStructTriangles
                                {
                                    GeometryType = ERHIAccelStructGeometryType.Triangle,
                                    IndexBuffer = indexBuffer,
                                    IndexCount = 0,
                                },
                            },
                        });
                }
                catch (ArgumentException)
                {
                    try
                    {
                        using RHIBottomLevelAccelStruct _ = device.CreateBottomAccelerationStructure(
                            new RHIBottomLevelAccelStructDescriptor
                            {
                                Geometries = new RHIAccelStructGeometry[]
                                {
                                    new RHIAccelStructTriangles
                                    {
                                        GeometryType = ERHIAccelStructGeometryType.Triangle,
                                        IndexBuffer = indexBuffer,
                                        IndexCount = 2,
                                    },
                                },
                            });
                    }
                    catch (ArgumentException)
                    {
                        RHIAccelStructTriangleContract.ValidateIndexIntent(
                            new RHIAccelStructTriangles());
                        return;
                    }

                    throw new InvalidOperationException(
                        "Expected ArgumentException for IndexCount not divisible by 3.");
                }

                throw new InvalidOperationException(
                    "Expected ArgumentException for IndexBuffer without IndexCount.");
            }

            throw new InvalidOperationException(
                "Expected ArgumentException for IndexCount without IndexBuffer.");
        }

        internal static void AssertMismatchedMotionVertexStrideThrows(RHIDevice device)
        {
            float[] vertices =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, vertices);
            using RHIBuffer motionVertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(motionVertexBuffer, vertices);

            try
            {
                using RHIBottomLevelAccelStruct _ = device.CreateBottomAccelerationStructure(
                    new RHIBottomLevelAccelStructDescriptor
                    {
                        Flag = ERHIAccelStructFlag.Motion,
                        Geometries = new RHIAccelStructGeometry[]
                        {
                            new RHIAccelStructTriangles
                            {
                                GeometryType = ERHIAccelStructGeometryType.Triangle,
                                VertexCount = 3,
                                VertexStride = 16,
                                VertexBuffer = vertexBuffer,
                                VertexFormat = ERHIPixelFormat.R32G32B32A32_Float,
                                MotionVertexBuffer = motionVertexBuffer,
                                MotionVertexStride = 32,
                            },
                        },
                    });
            }
            catch (NotSupportedException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Expected NotSupportedException for MotionVertexStride != VertexStride.");
        }

        internal static void AssertFailedMotionUpdateLeavesDescriptorUnchanged(RHIDevice device)
        {
            if (device.Capabilities.RayTracing.Pipeline.Tier == ERHICapabilityTier.Unavailable)
            {
                return;
            }

            float[] vertices =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(
                device,
                vertices.Length * sizeof(float),
                ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, vertices);
            ushort[] indices = { 0, 1, 2 };
            using RHIBuffer indexBuffer = CreateUpload(
                device,
                indices.Length * sizeof(ushort),
                ERHIBufferUsage.IndexBuffer | ERHIBufferUsage.AccelStruct);
            WriteUshorts(indexBuffer, indices);

            using RHIBottomLevelAccelStruct blas = device.CreateBottomAccelerationStructure(
                new RHIBottomLevelAccelStructDescriptor
                {
                    Geometries = new RHIAccelStructGeometry[]
                    {
                        new RHIAccelStructTriangles
                        {
                            GeometryType = ERHIAccelStructGeometryType.Triangle,
                            GeometryFlag = ERHIAccelStructGeometryFlag.None,
                            IndexCount = 3,
                            IndexBuffer = indexBuffer,
                            IndexFormat = ERHIBufferFormat.UInt16,
                            VertexCount = 3,
                            VertexStride = 16,
                            VertexBuffer = vertexBuffer,
                            VertexFormat = ERHIPixelFormat.R32G32B32A32_Float,
                        },
                    },
                });

            using RHITopLevelAccelStruct tlas = device.CreateTopAccelerationStructure(
                new RHITopLevelAccelStructDescriptor
                {
                    Instances = new[]
                    {
                        new RHIAccelStructInstance
                        {
                            InstanceID = 0,
                            InstanceMask = 0xFF,
                            TransformMatrix = IdentityMatrix(),
                            BottomLevelAccelStruct = blas,
                        },
                    },
                });

            ERHIAccelStructFlag originalFlag = tlas.Descriptor.Flag;
            float4x4 t1 = IdentityMatrix();
            t1.c3.x = 0.25f;
            bool threw = false;
            try
            {
                tlas.UpdateAccelerationStructure(
                    new RHITopLevelAccelStructDescriptor
                    {
                        Flag = ERHIAccelStructFlag.Motion,
                        Instances = new[]
                        {
                            new RHIAccelStructInstance
                            {
                                InstanceID = 0,
                                InstanceMask = 0xFF,
                                TransformMatrix = IdentityMatrix(),
                                MotionType = ERHIAccelStructMotionInstanceType.Matrix,
                                MotionTransformMatrix = t1,
                                BottomLevelAccelStruct = blas,
                            },
                        },
                    });
            }
            catch (NotSupportedException)
            {
                threw = true;
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            if (!threw)
            {
                throw new InvalidOperationException("Expected motion-mode Update to fail-closed.");
            }

            if (tlas.Descriptor.Flag != originalFlag ||
                RHIAccelStructMotionContract.UsesMotionFlag(tlas.Descriptor.Flag))
            {
                throw new InvalidOperationException(
                    "Failed motion Update mutated the TLAS descriptor or native motion mode.");
            }

            RHIAccelStructInstance originalInstance = tlas.Descriptor.Instances.Span[0];
            threw = false;
            try
            {
                tlas.UpdateAccelerationStructure(
                    new RHITopLevelAccelStructDescriptor
                    {
                        Flag = originalFlag,
                        Instances = new[]
                        {
                            new RHIAccelStructInstance
                            {
                                InstanceID = originalInstance.InstanceID,
                                InstanceMask = originalInstance.InstanceMask,
                                TransformMatrix = originalInstance.TransformMatrix,
                                BottomLevelAccelStruct = null,
                            },
                        },
                    });
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            if (!threw)
            {
                throw new InvalidOperationException("Expected same-mode Update with a missing BLAS to fail-closed.");
            }

            if (tlas.Descriptor.Flag != originalFlag ||
                !ReferenceEquals(tlas.Descriptor.Instances.Span[0].BottomLevelAccelStruct, blas))
            {
                throw new InvalidOperationException(
                    "Failed same-mode Update mutated the TLAS descriptor.");
            }
        }

        private static void AssertThrowsNotSupported(Action action)
        {
            try
            {
                action();
            }
            catch (NotSupportedException)
            {
                return;
            }

            throw new InvalidOperationException("Expected NotSupportedException for unavailable motion factories.");
        }

        private static float4x4 IdentityMatrix()
        {
            return new float4x4(
                new float4(1f, 0f, 0f, 0f),
                new float4(0f, 1f, 0f, 0f),
                new float4(0f, 0f, 1f, 0f),
                new float4(0f, 0f, 0f, 1f));
        }

        private static RHIBuffer CreateUpload(RHIDevice device, int byteSize, ERHIBufferUsage usage)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = usage | ERHIBufferUsage.CopySrc,
                StorageMode = ERHIStorageMode.HostUpload,
            });
        }

        private static void WriteFloats(RHIBuffer buffer, float[] values)
        {
            IntPtr mapped = buffer.Map(0, 0);
            try
            {
                Marshal.Copy(values, 0, mapped, values.Length);
            }
            finally
            {
                buffer.UnMap(0, checked((uint)(values.Length * sizeof(float))));
            }
        }

        private static void WriteUshorts(RHIBuffer buffer, ushort[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(ushort)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            IntPtr mapped = buffer.Map(0, 0);
            try
            {
                Marshal.Copy(bytes, 0, mapped, bytes.Length);
            }
            finally
            {
                buffer.UnMap(0, checked((uint)bytes.Length));
            }
        }
    }
}
