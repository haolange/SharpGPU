using System;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using SharpShader.Compilation;
using SharpShader.HLSLCrossCompiler;
using SharpShader.SharpGPU;

namespace SharpGPU.Conformance.Tests
{
    public sealed class SharpShaderGeneratedBindingGpuTests
    {
        private const string VariantKey = "default";
        private const string EntryPoint = "CSMain";
        private const uint ExpectedResult = 41;

        private readonly ITestOutputHelper m_Output;

        private static readonly ShaderBindingKey s_TextureKey =
            new(4, 0, ShaderBindingClass.ShaderResource);
        private static readonly ShaderBindingKey s_SamplerKey =
            new(4, 0, ShaderBindingClass.Sampler);
        private static readonly ShaderBindingKey s_ConstantsKey =
            new(4, 0, ShaderBindingClass.ConstantBuffer);
        private static readonly ShaderBindingKey s_OutputKey =
            new(4, 0, ShaderBindingClass.UnorderedAccess);
        private static readonly ShaderBindingKey s_InputsKey =
            new(9, 0, ShaderBindingClass.ShaderResource);

        public SharpShaderGeneratedBindingGpuTests(ITestOutputHelper output)
        {
            m_Output = output;
        }

        [Fact]
        public void GeneratedMapping_SparseTablesMixedNamespacesAndArrayElement_DispatchOnDx12AndVulkan()
        {
            ShaderProgramCompilation compilation =
                new ShaderProgramCompiler().Compile(new ShaderProgramCompileRequest(
                    GeneratedMappingShader,
                    "SharpShaderGeneratedBindingGpuTests.hlsl",
                    new[]
                    {
                        new ShaderProgramEntry(
                            EntryPoint,
                            ShaderExecutionStage.Compute),
                    },
                    new[] { ShaderProgramVariant.Default },
                    ShaderProgramTarget.DirectX12 | ShaderProgramTarget.Vulkan,
                    new ShaderModelVersion(6, 6)));

            m_Output.WriteLine(
                "Compiled one program for DXIL/SPIR-V; cacheKey="
                + compilation.CacheKey);

            Assert.Equal(2, compilation.Artifacts.Count);
            AssertGeneratedMappings(compilation);

            bool executed = false;
            if (RHIInstance.IsBackendSupported(
                    ERHIBackend.DirectX12,
                    out _))
            {
                ExecuteBackend(compilation, ERHIBackend.DirectX12);
                executed = true;
            }

            if (RHIInstance.IsBackendSupported(
                    ERHIBackend.Vulkan,
                    out _))
            {
                ExecuteBackend(compilation, ERHIBackend.Vulkan);
                executed = true;
            }

            Assert.True(
                executed,
                "Neither DX12 nor Vulkan is available for generated-binding GPU conformance.");
        }

        private static void AssertGeneratedMappings(
            ShaderProgramCompilation compilation)
        {
            ShaderInterfaceLayout logical =
                Assert.Single(compilation.Manifest.LogicalLayouts);
            Assert.Equal(5, logical.Bindings.Count);
            Assert.Contains(logical.Bindings, binding => binding.Key == s_TextureKey);
            Assert.Contains(logical.Bindings, binding => binding.Key == s_SamplerKey);
            Assert.Contains(logical.Bindings, binding => binding.Key == s_ConstantsKey);
            Assert.Contains(logical.Bindings, binding => binding.Key == s_OutputKey);
            ShaderLogicalBinding inputs =
                Assert.Single(logical.Bindings, binding => binding.Key == s_InputsKey);
            Assert.Equal(2u, inputs.Shape.Array.BoundedElementCount);

            ShaderBackendLayouts backend =
                Assert.Single(compilation.Manifest.BackendLayouts);
            Assert.Equal(
                new uint[] { 4, 4, 4, 4, 9 },
                backend.Dx12!.Bindings
                    .Select(mapping => mapping.RegisterSpace)
                    .OrderBy(value => value));
            Assert.Equal(
                new[]
                {
                    ShaderBindingClass.ShaderResource,
                    ShaderBindingClass.Sampler,
                    ShaderBindingClass.ConstantBuffer,
                    ShaderBindingClass.UnorderedAccess,
                },
                backend.Dx12.Bindings
                    .Where(mapping => mapping.RegisterSpace == 4)
                    .Select(mapping => mapping.RegisterClass)
                    .OrderBy(value => value));
            Assert.All(
                backend.Dx12.Bindings.Where(mapping => mapping.RegisterSpace == 4),
                mapping => Assert.Equal(0u, mapping.ShaderRegister));
            Assert.Equal(
                new[]
                {
                    (0u, 0u),
                    (0u, 1u),
                    (0u, 2u),
                    (0u, 3u),
                    (1u, 0u),
                },
                backend.Vulkan!.Bindings.Select(mapping =>
                    (mapping.DescriptorSet, mapping.Binding)));
        }

        private void ExecuteBackend(
            ShaderProgramCompilation compilation,
            ERHIBackend backend)
        {
            using RHIInstance instance = RHIInstance.Create(
                new RHIInstanceDescriptor
                {
                    Backend = backend,
                    // This is a compute-only conformance path. It does not
                    // create a swapchain, so requiring a native window would
                    // add an unrelated surface/loader dependency and make a
                    // headless test host unsafe.
                    SurfaceKind = ERHINativeSurfaceKind.Headless,
                    EnableDebugLayer = backend == ERHIBackend.DirectX12,
                    EnableValidation = backend == ERHIBackend.Vulkan,
                    GraphicsQueueRequestCount = 1,
                });
            Assert.NotNull(instance);
            Assert.True(
                instance.DeviceCount > 0,
                $"{backend} exposed no devices.");

            VulkanValidationCollector? vulkanValidation = null;
            if (backend == ERHIBackend.Vulkan)
            {
                VulkanInstance vulkanInstance =
                    Assert.IsType<VulkanInstance>(instance);
                Assert.True(
                    vulkanInstance.HasDebugUtils,
                    "Vulkan validation was requested, but VK_EXT_debug_utils is unavailable.");
#if DEBUG
                Assert.True(
                    vulkanInstance.HasValidationLayerEnabled,
                    "Vulkan validation was requested, but no validation layer was enabled.");
#else
                Assert.False(
                    vulkanInstance.HasValidationLayerEnabled,
                    "Vulkan validation layers must remain compiled out in Release.");
#endif
                vulkanValidation =
                    VulkanValidationCollector.Attach(
                        vulkanInstance.NativeInstance.Handle);
            }

            using VulkanValidationCollector? vulkanValidationScope =
                vulkanValidation;
            if (vulkanValidation is not null)
            {
                m_Output.WriteLine(
                    "Vulkan: test-scoped debug messenger attached after RHI instance/device creation; creation-time messages are outside this collector and require separate detailed runner-output review.");
            }
            RHIDevice device = SelectDevice(instance, backend);

            m_Output.WriteLine(
                $"{backend}: selected device='{device.Name}'.");

#if SHARPGPU_ENABLE_DX12
            Dx12ValidationCollector? dx12Validation = null;
            try
            {
                if (backend == ERHIBackend.DirectX12)
                {
                    dx12Validation =
                        Dx12ValidationCollector.Attach(
                            Assert.IsType<Dx12Device>(device));
                }
            }
            catch
            {
                device.Dispose();
                throw;
            }

            using Dx12ValidationCollector? dx12ValidationScope =
                dx12Validation;
            dx12Validation?.CheckpointAndAssert(
                "instance/device creation");
#endif

            using (device)
            {
                {
                    // End transient GPU lifetimes before validating cleanup
                    // while the selected device and its InfoQueue remain alive.
                    RHICommandQueue queue =
                        device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
                        ?? throw new InvalidOperationException(
                            $"{backend} graphics queue is unavailable for '{device.Name}'.");

                    SharpGpuBindingTableLayoutPlan plan =
                        SharpGpuShaderInterfaceAdapter.CreateBindingTableLayoutPlan(
                            compilation.Manifest,
                            VariantKey,
                            EntryPoint,
                            ShaderExecutionStage.Compute,
                            backend);
                    AssertPlan(compilation, plan, backend);

                    using SharpGpuBindingTableLayouts ownedLayouts =
                        plan.CreateBindingTableLayouts(device);
                    using RHIPipelineLayout pipelineLayout =
                        device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
                        {
                            BindingTableLayouts = ownedLayouts.Layouts.ToArray(),
                        });
                    using RHIFunction function = CreateFunction(
                        device,
                        compilation,
                        backend);
                    using RHIComputePipeline pipeline =
                        device.CreateComputePipeline(new RHIComputePipelineDescriptor
                        {
                            ThreadSize = new uint3(1, 1, 1),
                            ComputeFunction = function,
                            PipelineLayout = pipelineLayout,
                        });

                    using RHIBuffer input0 = CreateHostBuffer(
                        device,
                        3,
                        ERHIBufferUsage.ShaderResource);
                    using RHIBuffer input1 = CreateHostBuffer(
                        device,
                        11,
                        ERHIBufferUsage.ShaderResource);
                    using RHIBuffer constants = CreateConstantBuffer(device, 13);
                    using RHIBuffer output = CreateBuffer(
                        device,
                        sizeof(uint),
                        ERHIBufferUsage.UnorderedAccess | ERHIBufferUsage.CopySrc,
                        ERHIStorageMode.GPULocal);
                    using RHIBuffer readback = CreateBuffer(
                        device,
                        sizeof(uint),
                        ERHIBufferUsage.CopyDst,
                        ERHIStorageMode.Readback);
                    using RHIBuffer textureUpload = CreateTextureUpload(device, 17);
                    using RHIBufferView input0View = CreateBufferView(
                        input0,
                        ERHIBufferViewType.ShaderResource);
                    using RHIBufferView input1View = CreateBufferView(
                        input1,
                        ERHIBufferViewType.ShaderResource);
                    using RHIBufferView constantsView =
                        constants.CreateBufferView(new RHIBufferViewDescriptor
                        {
                            Count = 1,
                            Stride = 256,
                            ViewType = ERHIBufferViewType.UniformBuffer,
                        });
                    using RHIBufferView outputView = CreateBufferView(
                        output,
                        ERHIBufferViewType.UnorderedAccess);
                    using RHITexture texture =
                        device.CreateTexture(new RHITextureDescriptor
                        {
                            MipCount = 1,
                            Extent = new uint3(1, 1, 1),
                            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                            SampleCount = ERHISampleCount.None,
                            StorageMode = ERHIStorageMode.GPULocal,
                            UsageFlag =
                                ERHITextureUsage.ShaderResource
                                | ERHITextureUsage.CopyDst,
                            Dimension = ERHITextureDimension.Texture2D,
                        });
                    using RHITextureView textureView =
                        texture.CreateTextureView(new RHITextureViewDescriptor
                        {
                            MipCount = 1,
                            ArrayCount = 1,
                            ViewType = ERHITextureViewType.ShaderResource,
                        });
                    using RHISampler sampler =
                        device.CreateSampler(CreatePointSampler());
                    using BindingTableSet tables = new(
                        device,
                        plan,
                        ownedLayouts);

                    tables.Set(
                        plan.GetBinding(s_TextureKey),
                        new RHIBindingTableElement { TextureView = textureView });
                    tables.Set(
                        plan.GetBinding(s_SamplerKey),
                        new RHIBindingTableElement { Sampler = sampler });
                    tables.Set(
                        plan.GetBinding(s_ConstantsKey),
                        new RHIBindingTableElement { BufferView = constantsView });
                    tables.Set(
                        plan.GetBinding(s_OutputKey),
                        new RHIBindingTableElement { BufferView = outputView });
                    tables.Set(
                        plan.GetBinding(s_InputsKey),
                        new RHIBindingTableElement { BufferView = input0View },
                        0);
                    tables.Set(
                        plan.GetBinding(s_InputsKey),
                        new RHIBindingTableElement { BufferView = input1View },
                        1);

                    using RHIFence fence = device.CreateFence();
                    using RHICommandBuffer commandBuffer =
                        queue.CreateCommandBuffer();
                    commandBuffer.Begin(
                        $"SharpShader.GeneratedBinding.{backend}");

                    RHITransferEncoder upload = commandBuffer.BeginTransferPass(
                        new RHITransferPassDescriptor
                        {
                            Name = "UploadTexture",
                        });
                    upload.Barrier(RHIBarrier.Texture(
                        texture,
                        RHITextureSubresourceRange.Whole(
                            ERHITextureAspectMask.Color),
                        ERHITextureLayout.Undefined,
                        ERHITextureLayout.CopyDestination,
                        ERHIStageMask.None,
                        ERHIStageMask.Transfer,
                        ERHIAccessMask.None,
                        ERHIAccessMask.TransferWrite));
                    upload.CopyBufferToTexture(
                        new RHIBufferCopyDescriptor
                        {
                            Buffer = textureUpload,
                            RowPitch = 256,
                            TextureHeight = new uint3(1, 1, 1),
                        },
                        new RHITextureCopyDescriptor
                        {
                            Texture = texture,
                            SliceCount = 1,
                            Origin = new uint3(0, 0, 0),
                        },
                        new int3(1, 1, 1));
                    upload.Barrier(RHIBarrier.Texture(
                        texture,
                        RHITextureSubresourceRange.Whole(
                            ERHITextureAspectMask.Color),
                        ERHITextureLayout.CopyDestination,
                        ERHITextureLayout.ShaderReadOnly,
                        ERHIStageMask.Transfer,
                        ERHIStageMask.Compute,
                        ERHIAccessMask.TransferWrite,
                        ERHIAccessMask.ShaderRead));
                    commandBuffer.EndTransferPass();

                    RHIComputeEncoder compute = commandBuffer.BeginComputePass(
                        new RHIComputePassDescriptor
                        {
                            Name = "GeneratedBindingDispatch",
                        });
                    compute.Barrier(RHIBarrier.Buffer(
                        output,
                        RHIBufferRange.Whole(),
                        ERHIStageMask.None,
                        ERHIStageMask.Compute,
                        ERHIAccessMask.None,
                        ERHIAccessMask.ShaderWrite));
                    compute.SetPipeline(pipeline);
                    tables.Bind(compute);
                    compute.Dispatch(1, 1, 1);
                    commandBuffer.EndComputePass();

                    RHITransferEncoder copy = commandBuffer.BeginTransferPass(
                        new RHITransferPassDescriptor
                        {
                            Name = "Readback",
                        });
                    copy.Barrier(RHIBarrier.Buffer(
                        output,
                        RHIBufferRange.Whole(),
                        ERHIStageMask.Compute,
                        ERHIStageMask.Transfer,
                        ERHIAccessMask.ShaderWrite,
                        ERHIAccessMask.TransferRead));
                    copy.CopyBufferToBuffer(
                        output,
                        0,
                        readback,
                        0,
                        sizeof(uint));
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();

                    fence.Reset();
                    queue.Submit(new RHIQueueSubmitDescriptor(
                        new RHICommandBuffer[] { commandBuffer },
                        completionFence: fence));
                    fence.Wait();

                    IntPtr pointer = readback.Map(0, sizeof(uint));
                    uint actual;
                    try
                    {
                        actual = unchecked((uint)Marshal.ReadInt32(pointer));
                    }
                    finally
                    {
                        readback.UnMap(0, 0);
                    }

                    Assert.Equal(ExpectedResult, actual);

                    m_Output.WriteLine(
                        $"{backend}: dispatch/readback={actual}, expected={ExpectedResult}.");

                }
#if SHARPGPU_ENABLE_DX12
                if (backend == ERHIBackend.DirectX12)
                {
                    Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
                    SharpGen.Runtime.Result removedReason =
                        dx12Device.NativeDevice.DeviceRemovedReason;
                    Assert.True(
                        removedReason.Success,
                        $"DX12 device was removed: 0x{(int)removedReason:X8}.");
                    Assert.NotNull(dx12Validation);
                    dx12Validation!.CheckpointAndAssert(
                        "pipeline/resource/dispatch/readback/resource cleanup");
                }
#endif
                if (backend == ERHIBackend.Vulkan)
                {
                    VulkanDevice vulkanDevice =
                        Assert.IsType<VulkanDevice>(device);
                    Vortice.Vulkan.VkResult status =
                        VulkanNative.vkDeviceWaitIdle(
                            vulkanDevice.NativeDevice);
                    Assert.Equal(
                        Vortice.Vulkan.VkResult.Success,
                        status);

                }
            }

#if SHARPGPU_ENABLE_DX12
            if (backend == ERHIBackend.DirectX12)
            {
                Assert.NotNull(dx12Validation);
                dx12Validation!.CompleteAndAssert(
                    "device cleanup");
                m_Output.WriteLine(
                    "DirectX12: debugLayer=true, creationErrors=0, runtimeAndCleanupErrors=0, deviceRemoved=false.");
            }
#endif
            if (backend == ERHIBackend.Vulkan)
            {
                Assert.NotNull(vulkanValidation);
                vulkanValidation!.CompleteAndAssert(
                    "pipeline/resource/dispatch/readback/resource/device cleanup");

#if DEBUG
                m_Output.WriteLine(
                    "Vulkan: validationRequest=true, validationLayerEnabled=true, build=Debug, debugMessengerErrors=0, deviceStatus=Success.");
#else
                m_Output.WriteLine(
                    "Vulkan: validationRequest=true, validationLayerEnabled=false, build=Release(layer enabling is compiled out), debugMessengerErrors=0, deviceStatus=Success.");
#endif
            }
        }

        private static void AssertPlan(
            ShaderProgramCompilation compilation,
            SharpGpuBindingTableLayoutPlan plan,
            ERHIBackend backend)
        {
            Assert.Equal(backend, plan.Backend);
            ShaderInterfaceLayout logical =
                Assert.Single(compilation.Manifest.LogicalLayouts);
            Assert.Equal(logical.Signature, plan.LogicalLayoutSignature);
            Assert.Equal(2, plan.BindingTableCount);

            SharpGpuBindingLocation texture =
                plan.GetBinding(s_TextureKey);
            SharpGpuBindingLocation sampler =
                plan.GetBinding(s_SamplerKey);
            SharpGpuBindingLocation constants =
                plan.GetBinding(s_ConstantsKey);
            SharpGpuBindingLocation output =
                plan.GetBinding(s_OutputKey);
            SharpGpuBindingLocation inputs =
                plan.GetBinding(s_InputsKey);
            Assert.Equal(2u, inputs.Count);

            if (backend == ERHIBackend.DirectX12)
            {
                Assert.Equal(4u, texture.BindingTableIndex);
                Assert.Equal(4u, sampler.BindingTableIndex);
                Assert.Equal(4u, constants.BindingTableIndex);
                Assert.Equal(4u, output.BindingTableIndex);
                Assert.Equal(9u, inputs.BindingTableIndex);
                Assert.All(
                    new[] { texture, sampler, constants, output, inputs },
                    binding => Assert.Equal(0u, binding.Slot));
            }
            else
            {
                Assert.Equal(
                    new[] { 0u, 1u },
                    plan.CreateBindingTableLayoutDescriptors()
                        .Select(descriptor => descriptor.Index));
                Assert.Equal(
                    new uint[] { 0, 1, 2, 3 },
                    new[]
                    {
                        texture.Slot,
                        sampler.Slot,
                        constants.Slot,
                        output.Slot,
                    });
                Assert.Equal(1u, inputs.BindingTableIndex);
                Assert.Equal(0u, inputs.Slot);
            }
        }

        private static RHIDevice SelectDevice(
            RHIInstance instance,
            ERHIBackend backend)
        {
            RHIDevice? preferred = Enumerable
                .Range(0, instance.DeviceCount)
                .Select(index => instance.GetDevice(index))
                .FirstOrDefault(candidate =>
                    candidate?.Name?.Contains(
                        "RTX 5090",
                        StringComparison.OrdinalIgnoreCase) == true);
            RHIDevice device = preferred ?? instance.GetDevice(0);
            Assert.Equal(backend, device.BackendType);
            return device;
        }

        private static RHIFunction CreateFunction(
            RHIDevice device,
            ShaderProgramCompilation compilation,
            ERHIBackend backend)
        {
            ShaderArtifactKind artifactKind =
                backend == ERHIBackend.DirectX12
                    ? ShaderArtifactKind.Dxil
                    : ShaderArtifactKind.SpirV;
            ERHIShaderPayloadKind payloadKind =
                backend == ERHIBackend.DirectX12
                    ? ERHIShaderPayloadKind.Dxil
                    : ERHIShaderPayloadKind.SpirV;
            byte[] payload = compilation.GetArtifact(
                    VariantKey,
                    EntryPoint,
                    ShaderExecutionStage.Compute,
                    artifactKind)
                .Content
                .ToArray();
            Assert.NotEmpty(payload);

            IntPtr pointer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(
                    payload,
                    0,
                    pointer,
                    payload.Length);
                return device.CreateFunction(
                    new RHIFunctionDescriptor
                    {
                        ByteSize = checked((uint)payload.Length),
                        ByteCode = pointer,
                        EntryName = EntryPoint,
                        Type = ERHIFunctionType.Compute,
                        PayloadKind = payloadKind,
                    });
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static RHIBuffer CreateBuffer(
            RHIDevice device,
            int byteSize,
            ERHIBufferUsage usage,
            ERHIStorageMode storageMode)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = usage,
                StorageMode = storageMode,
            });
        }

        private static RHIBuffer CreateHostBuffer(
            RHIDevice device,
            int value,
            ERHIBufferUsage usage)
        {
            RHIBuffer buffer = CreateBuffer(
                device,
                sizeof(uint),
                usage,
                ERHIStorageMode.HostUpload);
            try
            {
                IntPtr pointer = buffer.Map(0, sizeof(uint));
                try
                {
                    Marshal.WriteInt32(pointer, value);
                }
                finally
                {
                    buffer.UnMap(0, sizeof(uint));
                }

                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static RHIBuffer CreateConstantBuffer(
            RHIDevice device,
            int value)
        {
            RHIBuffer buffer = CreateBuffer(
                device,
                256,
                ERHIBufferUsage.UniformBuffer
                    | ERHIBufferUsage.ShaderResource,
                ERHIStorageMode.HostUpload);
            try
            {
                IntPtr pointer = buffer.Map(0, 256);
                try
                {
                    Marshal.WriteInt32(pointer, value);
                }
                finally
                {
                    buffer.UnMap(0, 256);
                }

                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static RHIBuffer CreateTextureUpload(
            RHIDevice device,
            byte red)
        {
            byte[] pixels = new byte[256];
            pixels[0] = red;
            pixels[3] = byte.MaxValue;
            RHIBuffer buffer = CreateBuffer(
                device,
                256,
                ERHIBufferUsage.CopySrc,
                ERHIStorageMode.HostUpload);
            try
            {
                IntPtr pointer = buffer.Map(0, checked((uint)pixels.Length));
                try
                {
                    Marshal.Copy(pixels, 0, pointer, pixels.Length);
                }
                finally
                {
                    buffer.UnMap(0, checked((uint)pixels.Length));
                }

                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static RHIBufferView CreateBufferView(
            RHIBuffer buffer,
            ERHIBufferViewType viewType)
        {
            return buffer.CreateBufferView(
                new RHIBufferViewDescriptor
                {
                    Count = 1,
                    Stride = sizeof(uint),
                    ViewType = viewType,
                });
        }

        private static RHISamplerDescriptor CreatePointSampler()
        {
            return new RHISamplerDescriptor
            {
                LodMin = 0,
                LodMax = 0,
                Anisotropy = 1,
                MinFilter = ERHIFilterMode.Point,
                MagFilter = ERHIFilterMode.Point,
                MipFilter = ERHIFilterMode.Point,
                AddressModeU = ERHIAddressMode.ClampToEdge,
                AddressModeV = ERHIAddressMode.ClampToEdge,
                AddressModeW = ERHIAddressMode.ClampToEdge,
                ComparisonMode = ERHIComparisonMode.Never,
            };
        }

#if SHARPGPU_ENABLE_DX12
        private sealed class Dx12ValidationCollector : IDisposable
        {
            private Vortice.Direct3D12.Debug.ID3D12InfoQueue?
                m_InfoQueue;
            private bool m_IsCompleted;
            private bool m_IsDisposed;

            private Dx12ValidationCollector(
                Vortice.Direct3D12.Debug.ID3D12InfoQueue infoQueue)
            {
                m_InfoQueue = infoQueue;
            }

            public static Dx12ValidationCollector Attach(
                Dx12Device device)
            {
                ArgumentNullException.ThrowIfNull(device);
                Vortice.Direct3D12.Debug.ID3D12InfoQueue?
                    infoQueue =
                        device.NativeDevice
                            .QueryInterfaceOrNull<
                                Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
                return new Dx12ValidationCollector(
                    infoQueue
                    ?? throw new InvalidOperationException(
                        "The selected DX12 device did not expose ID3D12InfoQueue."));
            }

            public void CheckpointAndAssert(
                string observedStages)
            {
                ThrowIfUnavailable();
                if (m_IsCompleted)
                {
                    throw new InvalidOperationException(
                        "DX12 validation collection is already complete.");
                }

                CollectAndAssert(observedStages);
            }

            public void CompleteAndAssert(
                string observedStages)
            {
                ThrowIfUnavailable();
                if (m_IsCompleted)
                {
                    throw new InvalidOperationException(
                        "DX12 validation collection is already complete.");
                }

                m_IsCompleted = true;
                CollectAndAssert(observedStages);
            }

            public void Dispose()
            {
                if (m_IsDisposed)
                {
                    return;
                }

                Vortice.Direct3D12.Debug.ID3D12InfoQueue?
                    infoQueue = m_InfoQueue;
                m_InfoQueue = null;
                m_IsDisposed = true;
                if (infoQueue is not null)
                {
                    infoQueue.Release();
                }
            }

            private void CollectAndAssert(
                string observedStages)
            {
                List<string> messages = new();
                ulong discardedMessageCount = 0;
                try
                {
                    Vortice.Direct3D12.Debug.ID3D12InfoQueue
                        infoQueue = m_InfoQueue!;
                    discardedMessageCount =
                        infoQueue
                            .NumMessagesDiscardedByMessageCountLimit;
                    ulong count =
                        infoQueue
                            .NumStoredMessagesAllowedByRetrievalFilter;
                    for (ulong index = 0; index < count; ++index)
                    {
                        Vortice.Direct3D12.Debug.Message message =
                            infoQueue.GetMessage(index);
                        if (message.Severity
                                == Vortice.Direct3D12.Debug
                                    .MessageSeverity.Error
                            || message.Severity
                                == Vortice.Direct3D12.Debug
                                    .MessageSeverity.Corruption)
                        {
                            messages.Add(
                                $"[{message.Severity}] {message.Description}");
                        }
                    }
                }
                finally
                {
                    m_InfoQueue!.ClearStoredMessages();
                }

                Assert.True(
                    messages.Count == 0
                        && discardedMessageCount == 0,
                    $"DX12 debug layer reported Error/Corruption during {observedStages}:"
                    + $" discardedMessages={discardedMessageCount}"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, messages));
            }

            private void ThrowIfUnavailable()
            {
                ObjectDisposedException.ThrowIf(
                    m_IsDisposed || m_InfoQueue is null,
                    this);
            }
        }
#endif

        private sealed class BindingTableSet : IDisposable
        {
            private readonly RHIBindingTable[] m_Tables;
            private readonly Dictionary<uint, RHIBindingTable>
                m_TablesByIndex;
            private readonly uint[] m_TableIndices;
            private bool m_IsDisposed;

            public BindingTableSet(
                RHIDevice device,
                SharpGpuBindingTableLayoutPlan plan,
                SharpGpuBindingTableLayouts layouts)
            {
                ArgumentNullException.ThrowIfNull(device);
                ArgumentNullException.ThrowIfNull(plan);
                ArgumentNullException.ThrowIfNull(layouts);

                RHIBindingTableLayoutDescriptor[] descriptors =
                    plan.CreateBindingTableLayoutDescriptors();
                if (descriptors.Length != layouts.Layouts.Count)
                {
                    throw new InvalidOperationException(
                        "Generated SharpGPU descriptors and owned layouts differ in count.");
                }

                m_Tables =
                    new RHIBindingTable[descriptors.Length];
                m_TablesByIndex =
                    new Dictionary<uint, RHIBindingTable>(
                        descriptors.Length);
                m_TableIndices = new uint[descriptors.Length];
                int created = 0;
                try
                {
                    for (int index = 0;
                         index < descriptors.Length;
                         ++index)
                    {
                        uint tableIndex = descriptors[index].Index;
                        RHIBindingTable table =
                            device.CreateBindingTable(
                                new RHIBindingTableDescriptor
                                {
                                    Layout = layouts.Layouts[index],
                                    Elements =
                                        Array.Empty<
                                            RHIBindingTableElement>(),
                                });
                        if (!m_TablesByIndex.TryAdd(
                                tableIndex,
                                table))
                        {
                            table.Dispose();
                            throw new InvalidOperationException(
                                $"Generated SharpGPU table index {tableIndex} is duplicated.");
                        }

                        m_Tables[index] = table;
                        m_TableIndices[index] = tableIndex;
                        ++created;
                    }
                }
                catch
                {
                    for (int index = created - 1;
                         index >= 0;
                         --index)
                    {
                        m_Tables[index].Dispose();
                    }

                    throw;
                }
            }

            public void Set(
                SharpGpuBindingLocation binding,
                in RHIBindingTableElement element)
            {
                ThrowIfDisposed();
                if (binding.Count != 1)
                {
                    throw new ArgumentException(
                        $"Binding {binding.LogicalBinding} is an array of {binding.Count}; use the array-index overload.",
                        nameof(binding));
                }

                GetTable(binding).SetBindElement(
                    element,
                    binding.BindType,
                    checked((int)binding.Slot));
            }

            public void Set(
                SharpGpuBindingLocation binding,
                in RHIBindingTableElement element,
                int arrayIndex)
            {
                ThrowIfDisposed();
                if (binding.Count <= 1
                    || arrayIndex < 0
                    || arrayIndex >= binding.Count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(arrayIndex),
                        arrayIndex,
                        $"Binding {binding.LogicalBinding} accepts array indices [0, {binding.Count}).");
                }

                GetTable(binding).SetBindElement(
                    element,
                    binding.BindType,
                    checked((int)binding.Slot),
                    arrayIndex);
            }

            public void Bind(RHIComputeEncoder encoder)
            {
                ThrowIfDisposed();
                ArgumentNullException.ThrowIfNull(encoder);
                for (int index = 0;
                     index < m_Tables.Length;
                     ++index)
                {
                    encoder.SetBindingTable(
                        m_Tables[index],
                        m_TableIndices[index]);
                }
            }

            public void Dispose()
            {
                if (m_IsDisposed)
                {
                    return;
                }

                for (int index = m_Tables.Length - 1;
                     index >= 0;
                     --index)
                {
                    m_Tables[index].Dispose();
                }

                m_TablesByIndex.Clear();
                m_IsDisposed = true;
            }

            private RHIBindingTable GetTable(
                SharpGpuBindingLocation binding)
            {
                return m_TablesByIndex.TryGetValue(
                        binding.BindingTableIndex,
                        out RHIBindingTable? table)
                    ? table
                    : throw new KeyNotFoundException(
                        $"Generated SharpGPU table {binding.BindingTableIndex} for {binding.LogicalBinding} does not exist.");
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(
                    m_IsDisposed,
                    this);
            }
        }

        private const string GeneratedMappingShader = """
            Texture2D<float4> InputTexture : register(t0, space4);
            SamplerState InputSampler : register(s0, space4);

            cbuffer Constants : register(b0, space4)
            {
                uint Bias;
                uint3 Padding;
            };

            RWStructuredBuffer<uint> Output : register(u0, space4);
            StructuredBuffer<uint> Inputs[2] : register(t0, space9);

            [numthreads(1, 1, 1)]
            void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
            {
                uint sample = (uint)round(
                    InputTexture.SampleLevel(
                        InputSampler,
                        float2(0.5, 0.5),
                        0).r * 255.0);
                Output[0] = sample + Bias + Inputs[1][0];
            }
            """;
    }
}
