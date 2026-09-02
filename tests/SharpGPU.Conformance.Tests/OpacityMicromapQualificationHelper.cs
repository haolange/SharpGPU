using System;
using System.Runtime.InteropServices;
using SharpGPU;

namespace SharpGPU.Conformance.Tests
{
    internal static class OpacityMicromapQualificationHelper
    {
        internal static void BuildTinyTwoStateOmmAndOneTriangleBlas(RHIDevice device, RHICommandQueue queue, RHIFence fence)
        {
            using RHIBuffer input = CreateUpload(device, 128, ERHIBufferUsage.ShaderResource | ERHIBufferUsage.AccelStruct);
            FillOpaqueBits(input);

            RaytracingOpacityMicromapCpuDesc triangleDesc = new()
            {
                ByteOffset = 0,
                PackedSubdivisionAndFormat = 0 | ((uint)ERHIOpacityMicromapFormat.Oc1_2State << 16),
            };
            using RHIBuffer triangleArray = CreateUpload(device, 8, ERHIBufferUsage.ShaderResource | ERHIBufferUsage.AccelStruct);
            WriteStruct(triangleArray, in triangleDesc);

            RHIOpacityMicromapBuildDescriptor ommDescriptor = new()
            {
                Flag = ERHIAccelStructFlag.None,
                UsageCounts = new[]
                {
                    new RHIOpacityMicromapUsageCount
                    {
                        Count = 1,
                        SubdivisionLevel = 0,
                        Format = ERHIOpacityMicromapFormat.Oc1_2State,
                    },
                },
                InputBuffer = input,
                TriangleArrayBuffer = triangleArray,
                TriangleArrayStride = 8,
            };

            RHIOpacityMicromapMemoryRequirements sizes = device.GetOpacityMicromapMemoryRequirements(in ommDescriptor);
            AssertPositive(sizes.ResultSizeInBytes, "OMM result size");
            AssertPositive(sizes.ScratchSizeInBytes, "OMM scratch size");

            using RHIOpacityMicromap micromap = device.CreateOpacityMicromap(in ommDescriptor);

            float[] vertices =
            {
                0f, 0f, 0f, 1f,
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 1f,
            };
            using RHIBuffer vertexBuffer = CreateUpload(device, vertices.Length * sizeof(float), ERHIBufferUsage.VertexBuffer | ERHIBufferUsage.AccelStruct);
            WriteFloats(vertexBuffer, vertices);

            ushort[] indices = { 0, 1, 2 };
            using RHIBuffer indexBuffer = CreateUpload(device, indices.Length * sizeof(ushort), ERHIBufferUsage.IndexBuffer | ERHIBufferUsage.AccelStruct);
            WriteUshorts(indexBuffer, indices);

            uint[] ommIndices = { 0 };
            using RHIBuffer ommIndexBuffer = CreateUpload(device, sizeof(uint), ERHIBufferUsage.ShaderResource | ERHIBufferUsage.AccelStruct);
            WriteUints(ommIndexBuffer, ommIndices);

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
                            OpacityMicromap = micromap,
                            OpacityMicromapIndexBuffer = ommIndexBuffer,
                            OpacityMicromapIndexStride = 4,
                            OpacityMicromapIndexFormat = ERHIBufferFormat.UInt32,
                        },
                    },
                });

            using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
            commandBuffer.Begin("OpacityMicromap.TinyBuild");
            RHIRaytracingEncoder encoder = commandBuffer.BeginRaytracingPass(
                new RHIRayTracingPassDescriptor
                {
                    Name = "OpacityMicromap.TinyBuild",
                });
            encoder.BuildOpacityMicromap(micromap);
            encoder.BuildAccelerationStructure(blas);
            commandBuffer.EndRaytracingPass();
            commandBuffer.End();

            fence.Reset();
            queue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: fence));
            fence.Wait();
        }

        internal static void AssertUnavailableFactoriesThrow(RHIDevice device)
        {
            RHIOpacityMicromapBuildDescriptor descriptor = default;
            AssertThrowsNotSupported(() => device.GetOpacityMicromapMemoryRequirements(in descriptor));
            AssertThrowsNotSupported(() => device.CreateOpacityMicromap(in descriptor));
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

            throw new InvalidOperationException("Expected NotSupportedException for unavailable opacity micromap factories.");
        }

        private static void AssertPositive(ulong value, string name)
        {
            if (value == 0)
            {
                throw new InvalidOperationException($"{name} must be greater than zero.");
            }
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

        private static void FillOpaqueBits(RHIBuffer buffer)
        {
            IntPtr mapped = buffer.Map(0, 0);
            try
            {
                byte[] opaque = new byte[128];
                Array.Fill(opaque, (byte)0xFF);
                Marshal.Copy(opaque, 0, mapped, opaque.Length);
            }
            finally
            {
                buffer.UnMap(0, 128);
            }
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

        private static void WriteUints(RHIBuffer buffer, uint[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(uint)];
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

        private static void WriteStruct<T>(RHIBuffer buffer, in T value)
            where T : unmanaged
        {
            int byteCount = Marshal.SizeOf<T>();
            byte[] bytes = new byte[byteCount];
            T copy = value;
            IntPtr handle = Marshal.AllocHGlobal(byteCount);
            try
            {
                Marshal.StructureToPtr(copy, handle, false);
                Marshal.Copy(handle, bytes, 0, byteCount);
            }
            finally
            {
                Marshal.FreeHGlobal(handle);
            }

            IntPtr mapped = buffer.Map(0, 0);
            try
            {
                Marshal.Copy(bytes, 0, mapped, byteCount);
            }
            finally
            {
                buffer.UnMap(0, checked((uint)byteCount));
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
        private struct RaytracingOpacityMicromapCpuDesc
        {
            public uint ByteOffset;
            public uint PackedSubdivisionAndFormat;
        }
    }
}
