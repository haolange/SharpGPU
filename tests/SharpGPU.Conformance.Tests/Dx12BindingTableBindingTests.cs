#if SHARPGPU_ENABLE_DX12
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpGPU.Mathematics;
using SharpShader.HLSLCrossCompiler;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12BindingTableBindingTests
{
    [Fact]
    public void PipelinePlan_TwoTablesWithTwoGroupsEach_UsesStrictlyIncreasingRootParameters()
    {
        using Dx12BindingTableLayout table0 = CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer),
            Binding(0, ERHIBindType.Sampler));
        using Dx12BindingTableLayout table1 = CreateLayout(
            1,
            Binding(0, ERHIBindType.Buffer),
            Binding(0, ERHIBindType.Sampler));

        Dx12PipelineLayoutPlan plan = new Dx12PipelineLayoutPlan(new RHIPipelineLayoutDescriptor
        {
            BindingTableLayouts = new RHIBindingTableLayout[] { table0, table1 },
        });

        Assert.Equal(new uint[] { 0, 1 }, plan.TablePlans[0].RootParameterIndices);
        Assert.Equal(new uint[] { 2, 3 }, plan.TablePlans[1].RootParameterIndices);
        Assert.Equal(4, plan.DescriptorTableParameterCount);
        Assert.Equal(4, plan.TotalRootParameterCount);
    }

    [Fact]
    public void LayoutCompiler_AllowsDistinctRegisterNamespacesAndMapsStorageTexture2DmsToUav()
    {
        using Dx12BindingTableLayout layout = CreateLayout(
            7,
            Binding(0, ERHIBindType.Buffer),
            Binding(0, ERHIBindType.Sampler),
            Binding(0, ERHIBindType.UniformBuffer),
            Binding(0, ERHIBindType.StorageBuffer),
            Binding(3, ERHIBindType.StorageTexture2DMS));

        Assert.Equal(Vortice.Direct3D12.DescriptorRangeType.ShaderResourceView, layout.BindInfos[0].NativeRangeType);
        Assert.Equal(Vortice.Direct3D12.DescriptorRangeType.Sampler, layout.BindInfos[1].NativeRangeType);
        Assert.Equal(Vortice.Direct3D12.DescriptorRangeType.ConstantBufferView, layout.BindInfos[2].NativeRangeType);
        Assert.Equal(Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView, layout.BindInfos[3].NativeRangeType);
        Assert.Equal(Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView, layout.BindInfos[4].NativeRangeType);
        Assert.Equal(2, layout.Groups.Length);
    }

    [Fact]
    public void LayoutCompiler_RejectsInvalidAndAmbiguousBindings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer, count: 0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateLayout(
            0,
            Binding(0, ERHIBindType.Pending)));

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateLayout(
            0,
            Binding(
                0,
                ERHIBindType.Buffer,
                requirement: (ERHIBindingRequirement)byte.MaxValue)));

        Assert.Throws<NotSupportedException>(() => CreateLayout(
            0,
            Binding(
                0,
                ERHIBindType.Sampler,
                requirement: ERHIBindingRequirement.Optional)));

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer, stages: (ERHIShaderStageMask)(1 << 15))));

        using Dx12BindingTableLayout combinedVisibility = CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer, stages: ERHIShaderStageMask.Vertex | ERHIShaderStageMask.Fragment),
            Binding(1, ERHIBindType.Texture2D, stages: ERHIShaderStageMask.MachineLearning));
        Assert.All(
            combinedVisibility.BindInfos,
            bindInfo => Assert.Equal(Vortice.Direct3D12.ShaderVisibility.All, bindInfo.NativeVisibility));

        Assert.Throws<ArgumentException>(() => CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer),
            Binding(0, ERHIBindType.Buffer)));

        Assert.Throws<ArgumentException>(() => CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer, count: 2),
            Binding(1, ERHIBindType.Texture2D)));

        using Dx12BindingTableLayout disjointVisibility = CreateLayout(
            0,
            Binding(0, ERHIBindType.Buffer, stages: ERHIShaderStageMask.Vertex),
            Binding(0, ERHIBindType.Texture2D, stages: ERHIShaderStageMask.Fragment));
        Assert.Equal(2, disjointVisibility.Groups.Length);
    }

    [Fact]
    public void PipelinePlan_RejectsDuplicateSpacesMisalignedConstantsAndRootBudgetOverflow()
    {
        using Dx12BindingTableLayout table0 = CreateLayout(0, Binding(0, ERHIBindType.Buffer));
        using Dx12BindingTableLayout duplicateSpace = CreateLayout(0, Binding(1, ERHIBindType.Buffer));

        Assert.Throws<ArgumentException>(() => new Dx12PipelineLayoutPlan(new RHIPipelineLayoutDescriptor
        {
            BindingTableLayouts = new RHIBindingTableLayout[] { table0, duplicateSpace },
        }));

        Assert.Throws<ArgumentException>(() => new Dx12PipelineLayoutPlan(new RHIPipelineLayoutDescriptor
        {
            PushConstantSize = 2,
            BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
        }));

        Assert.Throws<ArgumentException>(() => new Dx12PipelineLayoutPlan(new RHIPipelineLayoutDescriptor
        {
            PushConstantSize = 256,
            BindingTableLayouts = new RHIBindingTableLayout[] { table0 },
        }));
    }

    [Fact]
    public void PipelinePlan_UsesStructuralCompatibilityInsteadOfHashIdentity()
    {
        using Dx12BindingTableLayout expected = CreateLayout(
            3,
            Binding(4, ERHIBindType.Buffer, count: 2),
            Binding(1, ERHIBindType.Sampler));
        using Dx12BindingTableLayout equivalent = CreateLayout(
            3,
            Binding(4, ERHIBindType.Buffer, count: 2),
            Binding(1, ERHIBindType.Sampler));
        using Dx12BindingTableLayout incompatible = CreateLayout(
            3,
            Binding(5, ERHIBindType.Buffer, count: 2),
            Binding(1, ERHIBindType.Sampler));
        using Dx12BindingTableLayout optional = CreateLayout(
            3,
            Binding(
                4,
                ERHIBindType.Buffer,
                count: 2,
                requirement: ERHIBindingRequirement.Optional),
            Binding(1, ERHIBindType.Sampler));

        Dx12PipelineLayoutPlan plan = new Dx12PipelineLayoutPlan(new RHIPipelineLayoutDescriptor
        {
            BindingTableLayouts = new RHIBindingTableLayout[] { expected },
        });

        Assert.Same(plan.TablePlans[0], plan.Resolve(3, expected));
        Assert.Same(plan.TablePlans[0], plan.Resolve(3, equivalent));
        Assert.Throws<ArgumentException>(() => plan.Resolve(3, incompatible));
        Assert.Throws<ArgumentException>(() => plan.Resolve(3, optional));
        Assert.Throws<ArgumentException>(() => plan.Resolve(9, expected));
    }

    [Fact]
    public void BindingTable_RequiredNullTypeDimensionAndArrayContracts_AreReleaseValidated()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            AssertDx12DeviceAlive(context.Device, "context creation");
            using RHIBindingTableLayout samplerLayout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[] { Binding(0, ERHIBindType.Sampler) },
            });
            using RHIBindingTable samplerTable = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = samplerLayout,
                Elements = Array.Empty<RHIBindingTableElement>(),
            });
            AssertDx12DeviceAlive(context.Device, "sampler table creation");
            Dx12BindingTable dx12SamplerTable = Assert.IsType<Dx12BindingTable>(samplerTable);
            Assert.Throws<InvalidOperationException>(dx12SamplerTable.EnsureReadyForBinding);

            using RHISampler sampler = context.Device.CreateSampler(CreatePointSampler());
            RHIBindingTableElement samplerElement = new RHIBindingTableElement { Sampler = sampler };
            samplerTable.SetBindElement(samplerElement, ERHIBindType.Sampler, 0);
            dx12SamplerTable.EnsureReadyForBinding();
            AssertDx12DeviceAlive(context.Device, "sampler update");
            Assert.Throws<ArgumentException>(() => samplerTable.SetBindElement(samplerElement, ERHIBindType.Sampler, 0, 0));
            Assert.Throws<ArgumentException>(() => samplerTable.SetBindElement(samplerElement, ERHIBindType.Sampler, 99));
            samplerTable.SetBindElement(default, ERHIBindType.Sampler, 0);
            Assert.Throws<InvalidOperationException>(dx12SamplerTable.EnsureReadyForBinding);

            using RHIBindingTableLayout partialSrvLayout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 1,
                Elements = new[]
                {
                    Binding(
                        0,
                        ERHIBindType.Buffer,
                        requirement: ERHIBindingRequirement.Optional),
                },
            });
            using RHIBindingTable partialSrvTable = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = partialSrvLayout,
                Elements = Array.Empty<RHIBindingTableElement>(),
            });
            Assert.IsType<Dx12BindingTable>(partialSrvTable).EnsureReadyForBinding();
            AssertDx12DeviceAlive(context.Device, "partial SRV table creation");

            using RHIBuffer buffer = CreateBuffer(
                context.Device,
                sizeof(uint),
                ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
                ERHIStorageMode.GPULocal);
            AssertDx12DeviceAlive(context.Device, "buffer creation");
            using RHIBufferView uav = buffer.CreateBufferView(new RHIBufferViewDescriptor
            {
                Count = 1,
                Stride = sizeof(uint),
                ViewType = ERHIBufferViewType.UnorderedAccess,
            });
            AssertDx12DeviceAlive(context.Device, "UAV creation");
            Assert.Throws<ArgumentException>(() => partialSrvTable.SetBindElement(
                new RHIBindingTableElement { BufferView = uav },
                ERHIBindType.Buffer,
                0));

            AssertDx12DeviceAlive(context.Device, "rejected wrong buffer view");
            using RHIBindingTableLayout texture3DLayout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 2,
                Elements = new[] { Binding(0, ERHIBindType.Texture3D) },
            });
            using RHIBindingTable texture3DTable = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = texture3DLayout,
                Elements = Array.Empty<RHIBindingTableElement>(),
            });
            AssertDx12DeviceAlive(context.Device, "texture 3D table creation");
            using RHITexture texture2D = context.Device.CreateTexture(new RHITextureDescriptor
            {
                MipCount = 1,
                Extent = new uint3(1, 1, 1),
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHITextureUsage.ShaderResource,
                Dimension = ERHITextureDimension.Texture2D,
            });
            using RHITextureView texture2DView = texture2D.CreateTextureView(new RHITextureViewDescriptor
            {
                MipCount = 1,
                ArrayCount = 1,
                ViewType = ERHITextureViewType.ShaderResource,
            });
            Assert.Throws<ArgumentException>(() => texture3DTable.SetBindElement(
                new RHIBindingTableElement { TextureView = texture2DView },
                ERHIBindType.Texture3D,
                0));

            Assert.True(
                FeatureContractContext.TryCreateDx12(out FeatureContractContext? foreignContext, out string foreignReason),
                foreignReason);
            using (foreignContext!)
            using (RHIPipelineLayout localPipelineLayout = context.Device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
            {
                BindingTableLayouts = new[] { partialSrvLayout },
            }))
            using (RHICommandBuffer localCommandBuffer = context.CommandQueue.CreateCommandBuffer())
            {
                Assert.Throws<ArgumentException>(() => foreignContext!.Device.CreateBindingTable(
                    new RHIBindingTableDescriptor
                    {
                        Layout = partialSrvLayout,
                        Elements = Array.Empty<RHIBindingTableElement>(),
                    }));
            }

            partialSrvTable.Dispose();
            Assert.Throws<ObjectDisposedException>(() =>
                Assert.IsType<Dx12BindingTable>(partialSrvTable).EnsureReadyForBinding());
            Assert.Throws<ObjectDisposedException>(() => partialSrvTable.SetBindElement(
                default,
                ERHIBindType.Buffer,
                0));
        }
    }

    [Fact]
    public void BindingTable_DescriptorExhaustion_RollsBackPreviouslyAllocatedGroups()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            Dx12Device device = Assert.IsType<Dx12Device>(context.Device);
            _ = device.NullDescriptors.Get(ERHIBindType.Buffer);
            int cbvSrvUavAvailable = device.DescriptorHeapCbvSrvUav.AvailableDescriptorCount;
            int samplerAvailable = device.DescriptorHeapSampler.AvailableDescriptorCount;

            using RHIBindingTableLayout layout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    Binding(0, ERHIBindType.Buffer, count: 2),
                    Binding(0, ERHIBindType.Sampler, count: 2049),
                },
            });

            Assert.Throws<InvalidOperationException>(() => context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = Array.Empty<RHIBindingTableElement>(),
            }));
            Assert.Equal(cbvSrvUavAvailable, device.DescriptorHeapCbvSrvUav.AvailableDescriptorCount);
            Assert.Equal(samplerAvailable, device.DescriptorHeapSampler.AvailableDescriptorCount);

            using Dx12CpuDescriptorPool stagingPool = new Dx12CpuDescriptorPool(
                device.NativeDevice,
                Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                2);
            Dx12CpuDescriptorAllocation page0 = stagingPool.Allocate(2, "test staging rollover");
            Dx12CpuDescriptorAllocation page1 = stagingPool.Allocate(1, "test staging rollover");
            Assert.NotSame(page0.Heap, page1.Heap);
            Assert.Equal(0, page0.Descriptor.Index);
            Assert.Equal(0, page1.Descriptor.Index);
            stagingPool.Free(page1.Heap, page1.Descriptor.Index);
            stagingPool.Free(page0.Heap, page0.Descriptor.Index, 2);
        }
    }

    [Fact]
    public void Dx12_GroupedTables_ArrayUpdateAndRegisterNamespaces_DispatchAndReadBack()
    {
        if (!OperatingSystem.IsWindows()
            || !RHIInstance.IsBackendSupported(ERHIBackend.DirectX12, out _))
        {
            return;
        }

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            EnableDebugLayer = true,
            EnableValidation = false,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        RHIDevice device = Enumerable.Range(0, instance.DeviceCount)
            .Select(index => instance.GetDevice(index))
            .FirstOrDefault(candidate => candidate?.Name?.Contains("RTX 5090", StringComparison.OrdinalIgnoreCase) == true)
            ?? instance.GetDevice(0);
        RHICommandQueue queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
            ?? throw new InvalidOperationException($"DX12 graphics queue is unavailable for '{device.Name}'.");
        Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
        Assert.True(ClearDx12InfoQueue(dx12Device), "DX12 debug layer did not expose ID3D12InfoQueue.");

        foreach (ERHIBindType bindType in Enum.GetValues<ERHIBindType>())
        {
            if (bindType != ERHIBindType.Pending
                && bindType != ERHIBindType.Sampler)
            {
                dx12Device.NullDescriptors.Get(bindType);
            }
        }
        AssertDx12DeviceAlive(device, "creating every supported null descriptor shape");
        string[] nullDescriptorMessages = CollectDx12Errors(dx12Device);
        Assert.True(
            nullDescriptorMessages.Length == 0,
            "DX12 debug layer rejected a null descriptor shape:" + Environment.NewLine + string.Join(Environment.NewLine, nullDescriptorMessages));

        using RHIFence fence = device.CreateFence();
        using RHIBindingTableLayout table0Layout = device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
        {
            Index = 0,
            Elements = new[]
            {
                Binding(0, ERHIBindType.Buffer),
                Binding(0, ERHIBindType.Sampler),
                Binding(0, ERHIBindType.UniformBuffer),
                Binding(0, ERHIBindType.StorageBuffer),
                Binding(1, ERHIBindType.Texture2D),
            },
        });
        using RHIBindingTableLayout table1Layout = device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
        {
            Index = 1,
            Elements = new[]
            {
                Binding(0, ERHIBindType.Buffer, count: 2),
                Binding(0, ERHIBindType.Sampler),
                Binding(2, ERHIBindType.Texture2D),
            },
        });
        using RHIPipelineLayout pipelineLayout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
        {
            BindingTableLayouts = new[] { table0Layout, table1Layout },
        });
        Dx12PipelineLayoutPlan nativePlan = Assert.IsType<Dx12PipelineLayout>(pipelineLayout).Plan;
        Assert.Equal(new uint[] { 0, 1 }, nativePlan.TablePlans[0].RootParameterIndices);
        Assert.Equal(new uint[] { 2, 3 }, nativePlan.TablePlans[1].RootParameterIndices);

        using RHIFunction function = CompileComputeFunction(device, GroupedComputeShader);
        using RHIComputePipeline pipeline = device.CreateComputePipeline(new RHIComputePipelineDescriptor
        {
            ThreadSize = new uint3(1, 1, 1),
            ComputeFunction = function,
            PipelineLayout = pipelineLayout,
        });

        using RHIBuffer primary = CreateHostBuffer(device, 7, ERHIBufferUsage.ShaderResource);
        using RHIBuffer array0 = CreateHostBuffer(device, 5, ERHIBufferUsage.ShaderResource);
        using RHIBuffer array1 = CreateHostBuffer(device, 11, ERHIBufferUsage.ShaderResource);
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

        using RHIBufferView primaryView = CreateBufferView(primary, ERHIBufferViewType.ShaderResource);
        using RHIBufferView array0View = CreateBufferView(array0, ERHIBufferViewType.ShaderResource);
        using RHIBufferView array1View = CreateBufferView(array1, ERHIBufferViewType.ShaderResource);
        using RHIBufferView constantsView = constants.CreateBufferView(new RHIBufferViewDescriptor
        {
            Count = 1,
            Stride = 256,
            ViewType = ERHIBufferViewType.UniformBuffer,
        });
        using RHIBufferView outputView = CreateBufferView(output, ERHIBufferViewType.UnorderedAccess);
        using RHITexture texture = device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(1, 1, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.ShaderResource | ERHITextureUsage.CopyDst,
            Dimension = ERHITextureDimension.Texture2D,
        });
        using RHITextureView textureView = texture.CreateTextureView(new RHITextureViewDescriptor
        {
            MipCount = 1,
            ArrayCount = 1,
            ViewType = ERHITextureViewType.ShaderResource,
        });
        using RHISampler sampler0 = device.CreateSampler(CreatePointSampler());
        using RHISampler sampler1 = device.CreateSampler(CreatePointSampler());

        using RHIBindingTable table0 = device.CreateBindingTable(new RHIBindingTableDescriptor
        {
            Layout = table0Layout,
            Elements = new[]
            {
                new RHIBindingTableElement { BufferView = primaryView },
                new RHIBindingTableElement { Sampler = sampler0 },
                new RHIBindingTableElement { BufferView = constantsView },
                new RHIBindingTableElement { BufferView = outputView },
                new RHIBindingTableElement { TextureView = textureView },
            },
        });
        using RHIBindingTable table1 = device.CreateBindingTable(new RHIBindingTableDescriptor
        {
            Layout = table1Layout,
            Elements = new[]
            {
                new RHIBindingTableElement { BufferView = array0View },
                new RHIBindingTableElement { Sampler = sampler1 },
                new RHIBindingTableElement { TextureView = textureView },
            },
        });
        table1.SetBindElement(
            new RHIBindingTableElement { BufferView = array1View },
            ERHIBindType.Buffer,
            0,
            1);

        using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
        commandBuffer.Begin("Dx12.BindingTable.GroupedDispatch");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "UploadTexture" });
        upload.Barrier(RHIBarrier.Texture(
            texture,
            RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
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
            RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
            ERHITextureLayout.CopyDestination,
            ERHITextureLayout.ShaderReadOnly,
            ERHIStageMask.Transfer,
            ERHIStageMask.Compute,
            ERHIAccessMask.TransferWrite,
            ERHIAccessMask.ShaderRead));
        commandBuffer.EndTransferPass();

        RHIComputeEncoder compute = commandBuffer.BeginComputePass(new RHIComputePassDescriptor { Name = "GroupedDispatch" });
        compute.Barrier(RHIBarrier.Buffer(
            output,
            RHIBufferRange.Whole(),
            ERHIStageMask.None,
            ERHIStageMask.Compute,
            ERHIAccessMask.None,
            ERHIAccessMask.ShaderWrite));
        compute.SetPipeline(pipeline);
        compute.SetBindingTable(table0, 0);
        compute.SetBindingTable(table1, 1);
        compute.Dispatch(1, 1, 1);
        commandBuffer.EndComputePass();

        RHITransferEncoder copy = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Readback" });
        copy.Barrier(RHIBarrier.Buffer(
            output,
            RHIBufferRange.Whole(),
            ERHIStageMask.Compute,
            ERHIStageMask.Transfer,
            ERHIAccessMask.ShaderWrite,
            ERHIAccessMask.TransferRead));
        copy.CopyBufferToBuffer(output, 0, readback, 0, sizeof(uint));
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        fence.Reset();
        queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: fence));
        fence.Wait();

        IntPtr readbackPointer = readback.Map(0, sizeof(uint));
        int actual = Marshal.ReadInt32(readbackPointer);
        readback.UnMap(0, 0);
        Assert.Equal(65, actual);
        Assert.True(dx12Device.NativeDevice.DeviceRemovedReason.Success);

        string[] seriousMessages = CollectDx12Errors(dx12Device);
        Assert.True(
            seriousMessages.Length == 0,
            "DX12 debug layer reported serious messages:" + Environment.NewLine + string.Join(Environment.NewLine, seriousMessages));
    }

    private static Dx12BindingTableLayout CreateLayout(
        uint index,
        params RHIBindingTableLayoutElement[] elements)
    {
        return new Dx12BindingTableLayout(new RHIBindingTableLayoutDescriptor
        {
            Index = index,
            Elements = elements,
        });
    }

    private static RHIBindingTableLayoutElement Binding(
        uint slot,
        ERHIBindType type,
        uint count = 1,
        ERHIShaderStageMask stages = ERHIShaderStageMask.Compute,
        ERHIBindingRequirement requirement =
            ERHIBindingRequirement.Required)
    {
        return new RHIBindingTableLayoutElement
        {
            Slot = slot,
            Count = count,
            Type = type,
            Stages = stages,
            Requirement = requirement,
        };
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

    private static RHIBuffer CreateHostBuffer(RHIDevice device, int value, ERHIBufferUsage usage)
    {
        RHIBuffer buffer = CreateBuffer(device, sizeof(uint), usage, ERHIStorageMode.HostUpload);
        IntPtr pointer = buffer.Map(0, sizeof(uint));
        Marshal.WriteInt32(pointer, value);
        buffer.UnMap(0, sizeof(uint));
        return buffer;
    }

    private static RHIBuffer CreateConstantBuffer(RHIDevice device, int value)
    {
        RHIBuffer buffer = CreateBuffer(
            device,
            256,
            ERHIBufferUsage.UniformBuffer | ERHIBufferUsage.ShaderResource,
            ERHIStorageMode.HostUpload);
        IntPtr pointer = buffer.Map(0, 256);
        Marshal.WriteInt32(pointer, value);
        buffer.UnMap(0, 256);
        return buffer;
    }

    private static RHIBuffer CreateTextureUpload(RHIDevice device, byte red)
    {
        RHIBuffer buffer = CreateBuffer(device, 256, ERHIBufferUsage.CopySrc, ERHIStorageMode.HostUpload);
        byte[] pixels = new byte[256];
        pixels[0] = red;
        pixels[3] = byte.MaxValue;
        IntPtr pointer = buffer.Map(0, 256);
        Marshal.Copy(pixels, 0, pointer, pixels.Length);
        buffer.UnMap(0, 256);
        return buffer;
    }

    private static RHIBufferView CreateBufferView(RHIBuffer buffer, ERHIBufferViewType viewType)
    {
        return buffer.CreateBufferView(new RHIBufferViewDescriptor
        {
            Count = 1,
            Stride = sizeof(uint),
            ViewType = viewType,
        });
    }

    private static RHIFunction CompileComputeFunction(RHIDevice device, string source)
    {
        ShaderCompileResult result = HLSLCrossCompiler.Compile(new ShaderCompileRequest
        {
            Source = source,
            EntryPoint = "main",
            SourceName = "Dx12BindingTableBindingTests.hlsl",
            Stage = ShaderStageKind.Compute,
            ShaderModel = new ShaderModelVersion(6, 6),
            Target = ShaderTargetKind.Dxil,
        });
        Assert.NotEmpty(result.Bytecode);

        IntPtr pointer = Marshal.AllocHGlobal(result.Bytecode.Length);
        try
        {
            Marshal.Copy(result.Bytecode, 0, pointer, result.Bytecode.Length);
            return device.CreateFunction(new RHIFunctionDescriptor
            {
                ByteSize = checked((uint)result.Bytecode.Length),
                ByteCode = pointer,
                EntryName = "main",
                Type = ERHIFunctionType.Compute,
                PayloadKind = ERHIShaderPayloadKind.Dxil,
            });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void AssertDx12DeviceAlive(RHIDevice device, string scope)
    {
        Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
        SharpGen.Runtime.Result reason = dx12Device.NativeDevice.DeviceRemovedReason;
        Assert.True(reason.Success, $"DX12 device was removed after {scope}: 0x{(int)reason:X8}.");
    }

    private static Vortice.Direct3D12.Debug.ID3D12InfoQueue? OpenDx12InfoQueue(Dx12Device device)
    {
        Vortice.Direct3D12.Debug.ID3D12InfoQueue? infoQueue =
            device.NativeDevice.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
        if (infoQueue != null)
        {
            return infoQueue;
        }

        SharpGen.Runtime.Result result =
            Vortice.Direct3D12.D3D12.D3D12GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12InfoQueue? globalInfoQueue);
        return result.Success ? globalInfoQueue : null;
    }

    private static bool ClearDx12InfoQueue(Dx12Device device)
    {
        Vortice.Direct3D12.Debug.ID3D12InfoQueue? infoQueue = OpenDx12InfoQueue(device);
        if (infoQueue == null)
        {
            return false;
        }

        try
        {
            infoQueue.ClearStoredMessages();
            return true;
        }
        finally
        {
            infoQueue.Release();
        }
    }

    private static string[] CollectDx12Errors(Dx12Device device)
    {
        Vortice.Direct3D12.Debug.ID3D12InfoQueue? infoQueue = OpenDx12InfoQueue(device);
        if (infoQueue == null)
        {
            return new[] { "ID3D12InfoQueue is unavailable." };
        }

        try
        {
            List<string> messages = new List<string>();
            ulong count = infoQueue.NumStoredMessagesAllowedByRetrievalFilter;
            for (ulong index = 0; index < count; ++index)
            {
                Vortice.Direct3D12.Debug.Message message = infoQueue.GetMessage(index);
                string severity = message.Severity.ToString();
                if (string.Equals(severity, "Error", StringComparison.Ordinal)
                    || string.Equals(severity, "Corruption", StringComparison.Ordinal))
                {
                    messages.Add($"[{severity}] {message.Description}");
                }
            }
            infoQueue.ClearStoredMessages();
            return messages.ToArray();
        }
        finally
        {
            infoQueue.Release();
        }
    }

    private const string GroupedComputeShader = """
StructuredBuffer<uint> Primary : register(t0, space0);
SamplerState Sampling : register(s0, space0);
cbuffer Constants : register(b0, space0)
{
    uint Addend;
    uint3 Padding;
};
RWStructuredBuffer<uint> Output : register(u0, space0);
Texture2D<float4> PrimaryTexture : register(t1, space0);

StructuredBuffer<uint> Inputs[2] : register(t0, space1);
SamplerState SecondarySampling : register(s0, space1);
Texture2D<float4> SecondaryTexture : register(t2, space1);

[numthreads(1, 1, 1)]
void main(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint primarySample = (uint)round(PrimaryTexture.SampleLevel(Sampling, float2(0.5, 0.5), 0).r * 255.0);
    uint secondarySample = (uint)round(SecondaryTexture.SampleLevel(SecondarySampling, float2(0.5, 0.5), 0).r * 255.0);
    Output[0] = Primary[0] + Inputs[1][0] + Addend + primarySample + secondarySample;
}
""";
}
#endif
