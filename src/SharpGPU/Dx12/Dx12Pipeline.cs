using System;
using System.Buffers.Binary;
using System.IO;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using Vortice.DirectML;

namespace SharpGPU
{
#pragma warning disable CS0169, CS0649, CA1416
    internal sealed class Dx12PipelineBindingTablePlan
    {
        public uint TableIndex { get; }
        public Dx12BindingTableLayout Layout { get; }
        public uint[] RootParameterIndices { get; }

        public Dx12PipelineBindingTablePlan(
            in uint tableIndex,
            Dx12BindingTableLayout layout,
            uint[] rootParameterIndices)
        {
            TableIndex = tableIndex;
            Layout = layout;
            RootParameterIndices = rootParameterIndices;
        }
    }

    internal sealed class Dx12PipelineLayoutPlan
    {
        internal const uint AttachmentRegisterSpace = 0xFFFFu;

        public Dx12PipelineBindingTablePlan[] TablePlans { get; }
        public int DescriptorTableParameterCount { get; }
        public int TotalRootParameterCount { get; }
        public uint PushConstantRootParameterIndex { get; }
        public uint PushConstantSize { get; }

        private readonly Dictionary<uint, Dx12PipelineBindingTablePlan> m_TablePlanMap;

        public Dx12PipelineLayoutPlan(in RHIPipelineLayoutDescriptor descriptor)
        {
            if ((descriptor.PushConstantSize & 3u) != 0)
            {
                throw new ArgumentException(
                    $"DX12 push constant size {descriptor.PushConstantSize} must be four-byte aligned.",
                    nameof(descriptor));
            }

            PushConstantSize = descriptor.PushConstantSize;
            RHIBindingTableLayout[] layouts = descriptor.BindingTableLayouts ?? Array.Empty<RHIBindingTableLayout>();
            TablePlans = new Dx12PipelineBindingTablePlan[layouts.Length];
            m_TablePlanMap = new Dictionary<uint, Dx12PipelineBindingTablePlan>(layouts.Length);

            int rootCursor = 0;
            for (int tableIndex = 0; tableIndex < layouts.Length; ++tableIndex)
            {
                Dx12BindingTableLayout layout = layouts[tableIndex] as Dx12BindingTableLayout
                    ?? throw new ArgumentException(
                        $"DX12 pipeline layout table {tableIndex} must be a Dx12BindingTableLayout from the same backend.",
                        nameof(descriptor));
                if (m_TablePlanMap.ContainsKey(layout.Index))
                {
                    throw new ArgumentException(
                        $"DX12 pipeline layout contains duplicate binding table space/index {layout.Index}.",
                        nameof(descriptor));
                }
                if (layout.Index == AttachmentRegisterSpace)
                {
                    throw new ArgumentException(
                        $"DX12 argument-table space {AttachmentRegisterSpace} is reserved for the backend-private raster attachment ABI.",
                        nameof(descriptor));
                }

                uint[] rootParameterIndices = new uint[layout.Groups.Length];
                for (int groupIndex = 0; groupIndex < rootParameterIndices.Length; ++groupIndex)
                {
                    rootParameterIndices[groupIndex] = checked((uint)rootCursor++);
                }

                Dx12PipelineBindingTablePlan tablePlan = new Dx12PipelineBindingTablePlan(
                    layout.Index,
                    layout,
                    rootParameterIndices);
                TablePlans[tableIndex] = tablePlan;
                m_TablePlanMap.Add(layout.Index, tablePlan);
            }

            DescriptorTableParameterCount = rootCursor;
            uint pushConstantDwordCount = descriptor.PushConstantSize / 4u;
            ulong rootDwordCost =
                (ulong)rootCursor + pushConstantDwordCount;
            if (rootDwordCost > 64UL)
            {
                throw new ArgumentException(
                    $"DX12 root signature costs {rootDwordCost} DWORDs ({rootCursor} descriptor tables + {pushConstantDwordCount} push-constant DWORDs), exceeding the 64-DWORD limit.",
                    nameof(descriptor));
            }

            bool hasPushConstants = descriptor.PushConstantSize != 0;
            PushConstantRootParameterIndex = hasPushConstants ? checked((uint)rootCursor) : uint.MaxValue;
            TotalRootParameterCount = checked(rootCursor + (hasPushConstants ? 1 : 0));
        }

        public Dx12PipelineBindingTablePlan Resolve(
            in uint tableIndex,
            Dx12BindingTableLayout actualLayout)
        {
            if (!m_TablePlanMap.TryGetValue(tableIndex, out Dx12PipelineBindingTablePlan? tablePlan))
            {
                throw new ArgumentException(
                    $"DX12 pipeline layout does not declare binding table space/index {tableIndex}.",
                    nameof(tableIndex));
            }
            if (!tablePlan.Layout.IsStructurallyCompatibleWith(actualLayout))
            {
                throw new ArgumentException(
                    $"DX12 binding table space/index {tableIndex} is structurally incompatible with the pipeline layout.",
                    nameof(actualLayout));
            }

            return tablePlan;
        }
    }

    internal static class Dx12BindingTableBinder
    {
        public static int BindCompute(
            Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList,
            Dx12PipelineLayout pipelineLayout,
            RHIBindingTable bindingTable,
            in uint tableIndex)
        {
            Dx12BindingTable dx12BindingTable = ResolveReadyTable(
                pipelineLayout,
                bindingTable,
                tableIndex,
                out Dx12PipelineBindingTablePlan tablePlan);

            for (int groupIndex = 0; groupIndex < tablePlan.RootParameterIndices.Length; ++groupIndex)
            {
                commandList.SetComputeRootDescriptorTable(
                    tablePlan.RootParameterIndices[groupIndex],
                    dx12BindingTable.GetGroupGpuHandle(groupIndex));
            }

            return tablePlan.RootParameterIndices.Length;
        }

        public static int BindGraphics(
            Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList,
            Dx12PipelineLayout pipelineLayout,
            RHIBindingTable bindingTable,
            in uint tableIndex)
        {
            Dx12BindingTable dx12BindingTable = ResolveReadyTable(
                pipelineLayout,
                bindingTable,
                tableIndex,
                out Dx12PipelineBindingTablePlan tablePlan);

            for (int groupIndex = 0; groupIndex < tablePlan.RootParameterIndices.Length; ++groupIndex)
            {
                commandList.SetGraphicsRootDescriptorTable(
                    tablePlan.RootParameterIndices[groupIndex],
                    dx12BindingTable.GetGroupGpuHandle(groupIndex));
            }

            return tablePlan.RootParameterIndices.Length;
        }

        public static bool ValidatePushConstantWrite(
            Dx12PipelineLayout pipelineLayout,
            IntPtr data,
            in uint size,
            in uint offset)
        {
            if (size == 0)
            {
                return false;
            }
            if (data == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(data), "DX12 push-constant data cannot be null when size is non-zero.");
            }
            if (((size | offset) & 3u) != 0)
            {
                throw new ArgumentException(
                    $"DX12 push-constant write offset {offset} and size {size} must be four-byte aligned.");
            }
            if (offset > pipelineLayout.PushConstantSize || size > pipelineLayout.PushConstantSize - offset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    $"DX12 push-constant range [{offset}, {(ulong)offset + size}) exceeds the declared size {pipelineLayout.PushConstantSize}.");
            }

            return true;
        }

        private static Dx12BindingTable ResolveReadyTable(
            Dx12PipelineLayout pipelineLayout,
            RHIBindingTable bindingTable,
            in uint tableIndex,
            out Dx12PipelineBindingTablePlan tablePlan)
        {
            Dx12BindingTable dx12BindingTable = bindingTable as Dx12BindingTable
                ?? throw new ArgumentException("DX12 encoder requires a Dx12BindingTable from the same backend.", nameof(bindingTable));
            if (pipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(Dx12PipelineLayout));
            }
            if (!ReferenceEquals(pipelineLayout.Device, dx12BindingTable.Device))
            {
                throw new ArgumentException(
                    "DX12 encoder cannot bind a binding table allocated from a different DX12 device.",
                    nameof(bindingTable));
            }
            if (dx12BindingTable.BindingTableLayout.Index != tableIndex)
            {
                throw new ArgumentException(
                    $"DX12 binding table reports space/index {dx12BindingTable.BindingTableLayout.Index}, but SetBindingTable requested {tableIndex}.",
                    nameof(tableIndex));
            }

            tablePlan = pipelineLayout.Plan.Resolve(tableIndex, dx12BindingTable.BindingTableLayout);
            if (dx12BindingTable.GroupCount != tablePlan.RootParameterIndices.Length)
            {
                throw new InvalidOperationException(
                    $"DX12 binding table space/index {tableIndex} has {dx12BindingTable.GroupCount} descriptor groups, but the pipeline plan expects {tablePlan.RootParameterIndices.Length}.");
            }

            dx12BindingTable.EnsureReadyForBinding();
            return dx12BindingTable;
        }
    }

    internal unsafe class Dx12PipelineLayout : RHIPipelineLayout
    {
        public int ParameterCount => Plan.DescriptorTableParameterCount;
        public uint PushConstantRootParameterIndex => Plan.PushConstantRootParameterIndex;
        public uint PushConstantSize => Plan.PushConstantSize;
        public Dx12PipelineLayoutPlan Plan { get; }
        internal Dx12Device Device { get; }
        internal bool IsLocalSignature { get; }
        internal bool UsesVertexLayout { get; }
        public Vortice.Direct3D12.ID3D12RootSignature NativeRootSignature =>
            m_NativeRootSignature ?? throw new ObjectDisposedException(GetType().FullName);

        private Vortice.Direct3D12.ID3D12RootSignature? m_NativeRootSignature;

        public Dx12PipelineLayout(Dx12Device device, in RHIPipelineLayoutDescriptor descriptor)
        {
            Device = device;
            RHIBindingTableLayout[]? bindingTableLayouts = descriptor.BindingTableLayouts;
            if (bindingTableLayouts != null)
            {
                for (int index = 0; index < bindingTableLayouts.Length; ++index)
                {
                    Dx12BindingTableLayout layout = bindingTableLayouts[index] as Dx12BindingTableLayout
                        ?? throw new ArgumentException(
                            $"DX12 pipeline binding table {index} must be a {nameof(Dx12BindingTableLayout)}.",
                            nameof(descriptor));
                    if (layout.IsDisposed)
                    {
                        throw new ObjectDisposedException(layout.GetType().FullName);
                    }
                    if (!ReferenceEquals(layout.Device, device))
                    {
                        throw new ArgumentException(
                            $"DX12 pipeline binding table {layout.Index} belongs to a different DX12 device.",
                            nameof(descriptor));
                    }
                }
            }
            InitializePipelineCacheIdentity(descriptor);
            IsLocalSignature = descriptor.bLocalSignature;
            UsesVertexLayout = descriptor.bUseVertexLayout;
            Plan = new Dx12PipelineLayoutPlan(descriptor);
            Vortice.Direct3D12.RootParameter1[] rootParameters = new Vortice.Direct3D12.RootParameter1[Plan.TotalRootParameterCount];

            for (int tableIndex = 0; tableIndex < Plan.TablePlans.Length; ++tableIndex)
            {
                Dx12PipelineBindingTablePlan tablePlan = Plan.TablePlans[tableIndex];
                Dx12BindingTableLayout layout = tablePlan.Layout;
                for (int groupIndex = 0; groupIndex < layout.Groups.Length; ++groupIndex)
                {
                    Dx12BindingTableGroupPlan group = layout.Groups[groupIndex];
                    Vortice.Direct3D12.DescriptorRange1[] ranges = new Vortice.Direct3D12.DescriptorRange1[group.BindingIndices.Length];
                    for (int rangeIndex = 0; rangeIndex < group.BindingIndices.Length; ++rangeIndex)
                    {
                        ref readonly Dx12BindInfo bindInfo = ref layout.BindInfos[group.BindingIndices[rangeIndex]];
                        ranges[rangeIndex] = new Vortice.Direct3D12.DescriptorRange1
                        {
                            RangeType = bindInfo.NativeRangeType,
                            NumDescriptors = bindInfo.Count,
                            BaseShaderRegister = bindInfo.Slot,
                            RegisterSpace = layout.Index,
                            Flags = Dx12Utility.GetDx12DescriptorRangeFlags(bindInfo.Type),
                            OffsetInDescriptorsFromTableStart = checked((uint)bindInfo.DescriptorOffset),
                        };
                    }

                    uint rootParameterIndex = tablePlan.RootParameterIndices[groupIndex];
                    rootParameters[rootParameterIndex] = new Vortice.Direct3D12.RootParameter1(
                        new Vortice.Direct3D12.RootDescriptorTable1(ranges),
                        group.Visibility);
                }
            }

            if (Plan.PushConstantSize != 0)
            {
                rootParameters[Plan.PushConstantRootParameterIndex] = new Vortice.Direct3D12.RootParameter1(
                    new Vortice.Direct3D12.RootConstants(0, 0, Plan.PushConstantSize / 4u),
                    Vortice.Direct3D12.ShaderVisibility.All);
            }

            Vortice.Direct3D12.RootSignatureFlags rootSignatureFlags = Vortice.Direct3D12.RootSignatureFlags.None;
            rootSignatureFlags |= Vortice.Direct3D12.RootSignatureFlags.DenyHullShaderRootAccess;
            rootSignatureFlags |= Vortice.Direct3D12.RootSignatureFlags.DenyDomainShaderRootAccess;
            rootSignatureFlags |= Vortice.Direct3D12.RootSignatureFlags.DenyGeometryShaderRootAccess;
            if (descriptor.bLocalSignature)
            {
                rootSignatureFlags |= Vortice.Direct3D12.RootSignatureFlags.LocalRootSignature;
            }
            if (descriptor.bUseVertexLayout)
            {
                rootSignatureFlags |= Vortice.Direct3D12.RootSignatureFlags.AllowInputAssemblerInputLayout;
            }

            Vortice.Direct3D12.VersionedRootSignatureDescription rootSignatureDescription = new Vortice.Direct3D12.VersionedRootSignatureDescription(
                new Vortice.Direct3D12.RootSignatureDescription1(
                    rootSignatureFlags,
                    rootParameters,
                    Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob? signature = null;
            try
            {
                string rootSignatureError = Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(
                    rootSignatureDescription,
                    out signature);
                if (!string.IsNullOrWhiteSpace(rootSignatureError))
                {
                    throw new InvalidOperationException($"DX12 root-signature serialization failed: {rootSignatureError}");
                }
                if (signature == null)
                {
                    throw new InvalidOperationException("DX12 root-signature serialization returned no signature blob.");
                }

                Dx12Utility.CHECK_HR(device.NativeDevice.CreateRootSignature(
                    0,
                    signature.BufferPointer,
                    signature.BufferSize,
                    out Vortice.Direct3D12.ID3D12RootSignature? rootSignature));
                m_NativeRootSignature = rootSignature
                    ?? throw new InvalidOperationException("DX12 root-signature creation returned null.");
            }
            finally
            {
                signature?.Release();
            }
        }

        protected override void Release()
        {
            NativeRootSignature.Release();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_RT_FORMAT_ARRAY
    {
        private fixed int m_Formats[8];
        public uint NumRenderTargets;

        public D3D12_RT_FORMAT_ARRAY(Vortice.DXGI.Format* formats, uint numRenderTargets)
        {
            NumRenderTargets = Math.Min(8u, numRenderTargets);
            fixed (int* dst = m_Formats)
            {
                for (int i = 0; i < 8; ++i)
                {
                    dst[i] = 0;
                }

                for (int i = 0; i < NumRenderTargets; ++i)
                {
                    dst[i] = (int)formats[i];
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_CUSTOM_COMPUTE_PIPELINE_STATE_DESC
    {
        public Vortice.Direct3D12.PipelineStateSubObjectType RootSignature_Type;
        public Vortice.Direct3D12.ID3D12RootSignature pRootSignature;
        public Vortice.Direct3D12.PipelineStateSubObjectType CS_Type;
        public Vortice.Direct3D12.ShaderBytecode CS;
        public Vortice.Direct3D12.PipelineStateSubObjectType Flags_Type;
        public Vortice.Direct3D12.PipelineStateFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_MESH_PIPELINE_STATE_DESC
    {
        public Vortice.Direct3D12.PipelineStateSubObjectType RootSignature_Type;
        public Vortice.Direct3D12.ID3D12RootSignature pRootSignature;
        public Vortice.Direct3D12.PipelineStateSubObjectType PrimitiveTopology_Type; 
        public Vortice.Direct3D12.PrimitiveTopologyType PrimitiveTopologyType;
        public Vortice.Direct3D12.PipelineStateSubObjectType TaskShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode TaskShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType MeshShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode MeshShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType PixelShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode PixelShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType RasterizerState_Type; 
        public Vortice.Direct3D12.RasterizerDescription RasterizerState;
        public Vortice.Direct3D12.PipelineStateSubObjectType DepthStencilState_Type; 
        public Vortice.Direct3D12.DepthStencilDescription DepthStencilState;
        public Vortice.Direct3D12.PipelineStateSubObjectType BlendState_Type; 
        public Vortice.Direct3D12.BlendDescription BlendState;
        public Vortice.Direct3D12.PipelineStateSubObjectType SampleDesc_Type; 
        public Vortice.DXGI.SampleDescription SampleDesc;
        public Vortice.Direct3D12.PipelineStateSubObjectType SampleMask_Type; 
        public uint SampleMask;
        public Vortice.Direct3D12.PipelineStateSubObjectType RTVFormats_Type; 
        public D3D12_RT_FORMAT_ARRAY RTVFormats;
        public Vortice.Direct3D12.PipelineStateSubObjectType DSVFormat_Type;
        public Vortice.DXGI.Format DSVFormat;
    }

    internal static unsafe class Dx12PipelineDebug
    {
        private const ulong MaxMessagesToDump = 32;

        public static void DumpDeviceMessages(Dx12Device device, string scope)
        {
            Console.Write(CollectDeviceMessages(device, scope));
        }

        public static string CollectDeviceMessages(Dx12Device device, string scope)
        {
            StringBuilder builder = new StringBuilder();
            Vortice.Direct3D12.Debug.ID3D12InfoQueue? infoQueue = device.NativeDevice.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
            if (infoQueue == null)
            {
                builder.AppendLine($"{scope} Failed to query Vortice.Direct3D12.Debug.ID3D12InfoQueue.");
                return builder.ToString();
            }

            try
            {
                ulong messageCount = infoQueue.NumStoredMessagesAllowedByRetrievalFilter;
                if (messageCount == 0)
                {
                    builder.AppendLine($"{scope} Vortice.Direct3D12.D3D12 info queue has no stored messages.");
                    return builder.ToString();
                }

                ulong startIndex = messageCount > MaxMessagesToDump ? messageCount - MaxMessagesToDump : 0;
                for (ulong messageIndex = startIndex; messageIndex < messageCount; ++messageIndex)
                {
                    Vortice.Direct3D12.Debug.Message message = infoQueue.GetMessage(messageIndex);
                    string text = message.Description ?? string.Empty;
                    builder.AppendLine($"{scope} [Vortice.Direct3D12.D3D12 {message.Severity}] {text}");
                }

                infoQueue.ClearStoredMessages();
            }
            finally
            {
                infoQueue.Release();
            }

            return builder.ToString();
        }
    }

    internal unsafe class Dx12ComputePipeline : RHIComputePipeline
    {
        public Vortice.Direct3D12.ID3D12PipelineState NativePipelineState =>
            m_NativePipelineState ?? throw new ObjectDisposedException(GetType().FullName);

        private Vortice.Direct3D12.ID3D12PipelineState? m_NativePipelineState;

        public Dx12ComputePipeline(Dx12Device device, in RHIComputePipelineDescriptor descriptor)
            : this(device, descriptor, null)
        {
        }

        internal Dx12ComputePipeline(
            Dx12Device device,
            in RHIComputePipelineDescriptor descriptor,
            Dx12PipelineCache? pipelineCache)
        {
            m_Descriptor = descriptor;
            Dx12Function computeFunction = descriptor.ComputeFunction as Dx12Function
                ?? throw new InvalidOperationException("Dx12ComputePipeline requires a Dx12Function compute shader.");
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout
                ?? throw new InvalidOperationException("Dx12ComputePipeline requires a Dx12PipelineLayout.");

            Vortice.Direct3D12.ComputePipelineStateDescription description = new Vortice.Direct3D12.ComputePipelineStateDescription
            {
                RootSignature = pipelineLayout.NativeRootSignature,
                ComputeShader = computeFunction.NativeShaderData,
                Flags = Vortice.Direct3D12.PipelineStateFlags.None,
            };

            if (pipelineCache != null)
            {
                m_NativePipelineState = pipelineCache.CreateComputePipelineState(
                    descriptor,
                    description);
                return;
            }

            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateComputePipelineState(description, out Vortice.Direct3D12.ID3D12PipelineState? nativePipelineState);

#if DEBUG
            if (hResult.Failure)
            {
                Console.WriteLine($"[Dx12ComputePipeline] CreateComputePipelineState failed. SharpGen.Runtime.Result=0x{hResult:X8}");
                ReadOnlySpan<byte> shaderBytes = computeFunction.NativeShaderData.Span;
                uint shaderMagic = shaderBytes.Length >= 4 ? BitConverter.ToUInt32(shaderBytes.Slice(0, 4)) : 0;
                Console.WriteLine($"[Dx12ComputePipeline] Shader bytecode length={shaderBytes.Length}, magic=0x{shaderMagic:X8}");
                Dx12PipelineDebug.DumpDeviceMessages(device, "[Dx12ComputePipeline]");
            }
#endif
            Dx12Utility.CHECK_HR(hResult);
            m_NativePipelineState = Dx12Utility.RequireCreatedObject(
                nativePipelineState,
                hResult,
                "ID3D12Device.CreateComputePipelineState");
        }

        protected override void Release()
        {
            m_NativePipelineState?.Release();
            m_NativePipelineState = null;
        }
    }

    internal unsafe class Dx12RaytracingPipeline : RHIRaytracingPipeline
    {
        public Vortice.Direct3D12.ID3D12StateObject NativePipeline =>
            m_NativePipeline ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D12.ID3D12StateObjectProperties NativeStateObjectProperties =>
            m_NativeStateObjectProperties ?? throw new ObjectDisposedException(GetType().FullName);

        private Vortice.Direct3D12.ID3D12RootSignature? m_LocalConstantsRootSignature;
        private Vortice.Direct3D12.ID3D12StateObject? m_NativePipeline;
        private Vortice.Direct3D12.ID3D12StateObjectProperties? m_NativeStateObjectProperties;
        private string m_RayGenerationExport;
        private string[] m_MissExports;
        private string[] m_HitGroupExports;
        private string[] m_CallableExports;

        public Dx12RaytracingPipeline(Dx12Device device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_LocalConstantsRootSignature = null;
            m_NativePipeline = null;
            m_NativeStateObjectProperties = null;
            m_RayGenerationExport = descriptor.RayGeneration.General.EntryName;
            m_MissExports = Array.Empty<string>();
            m_HitGroupExports = Array.Empty<string>();
            m_CallableExports = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(m_RayGenerationExport))
            {
                throw new InvalidOperationException("Ray generation entry name is empty.");
            }

            Span<RHIRayGeneralGroupDescriptor> missGroups = descriptor.RayMissGroups.Span;
            Span<RHIRayHitGroupDescriptor> hitGroups = descriptor.RayHitGroups.Span;
            Span<RHIRayGeneralGroupDescriptor> callableGroups = descriptor.RayCallableGroups.Span;

            m_MissExports = new string[missGroups.Length];
            for (int i = 0; i < missGroups.Length; ++i)
            {
                m_MissExports[i] = missGroups[i].General.EntryName;
            }

            m_HitGroupExports = new string[hitGroups.Length];
            for (int i = 0; i < hitGroups.Length; ++i)
            {
                m_HitGroupExports[i] = hitGroups[i].Name;
            }

            m_CallableExports = new string[callableGroups.Length];
            for (int i = 0; i < callableGroups.Length; ++i)
            {
                m_CallableExports[i] = callableGroups[i].General.EntryName;
            }

            Dx12FunctionLibrary functionLibrary = descriptor.FunctionLibrary as Dx12FunctionLibrary
                ?? throw new InvalidOperationException("Dx12RaytracingPipeline requires a Dx12FunctionLibrary.");

            Dx12PipelineLayout globalPipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout
                ?? throw new InvalidOperationException("Dx12RaytracingPipeline requires a Dx12PipelineLayout.");

            List<Vortice.Direct3D12.ExportDescription> exportDescriptors = new List<Vortice.Direct3D12.ExportDescription>(1 + missGroups.Length + callableGroups.Length + hitGroups.Length * 3);
            static void AddLibraryExport(List<Vortice.Direct3D12.ExportDescription> exports, string entryName)
            {
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException("Ray-tracing library export entry name is empty.");
                }

                exports.Add(new Vortice.Direct3D12.ExportDescription(entryName, null, Vortice.Direct3D12.ExportFlags.None));
            }

            AddLibraryExport(exportDescriptors, m_RayGenerationExport);
            for (int i = 0; i < missGroups.Length; ++i)
            {
                AddLibraryExport(exportDescriptors, missGroups[i].General.EntryName);
            }

            for (int i = 0; i < callableGroups.Length; ++i)
            {
                AddLibraryExport(exportDescriptors, callableGroups[i].General.EntryName);
            }

            for (int i = 0; i < hitGroups.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                if (hitGroup.AnyHit.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.AnyHit.Value.EntryName);
                }

                if (hitGroup.Intersect.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.Intersect.Value.EntryName);
                }

                if (hitGroup.ClosestHit.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.ClosestHit.Value.EntryName);
                }
            }

            Vortice.Direct3D12.DxilLibraryDescription dxilLibraryDescription = new Vortice.Direct3D12.DxilLibraryDescription(functionLibrary.NativeShaderData, exportDescriptors.ToArray());
            List<Vortice.Direct3D12.StateSubObject> stateSubObjects = new List<Vortice.Direct3D12.StateSubObject>(1 + hitGroups.Length + 3 + (descriptor.LocalDataStrideInBytes > 0 ? 2 : 0));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(dxilLibraryDescription));

            for (int i = 0; i < hitGroups.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                Vortice.Direct3D12.HitGroupDescription hitGroupDescription = new Vortice.Direct3D12.HitGroupDescription(
                    hitGroup.Name,
                    Dx12Utility.ConverteToDx12HitGroupType(hitGroup.Type),
                    hitGroup.AnyHit.HasValue ? hitGroup.AnyHit.Value.EntryName : string.Empty,
                    hitGroup.ClosestHit.HasValue ? hitGroup.ClosestHit.Value.EntryName : string.Empty,
                    hitGroup.Intersect.HasValue ? hitGroup.Intersect.Value.EntryName : string.Empty);
                stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(hitGroupDescription));
            }

            if (descriptor.LocalDataStrideInBytes > 0)
            {
                m_LocalConstantsRootSignature = BuildLocalConstantsRootSignature(device, descriptor.LocalDataStrideInBytes);
                Vortice.Direct3D12.StateSubObject localRootSignatureSubObject = new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.LocalRootSignature(m_LocalConstantsRootSignature));
                stateSubObjects.Add(localRootSignatureSubObject);

                List<string> associationExports = new List<string>(1 + missGroups.Length + hitGroups.Length + callableGroups.Length)
                {
                    m_RayGenerationExport,
                };
                for (int i = 0; i < missGroups.Length; ++i)
                {
                    associationExports.Add(missGroups[i].General.EntryName);
                }
                for (int i = 0; i < hitGroups.Length; ++i)
                {
                    associationExports.Add(hitGroups[i].Name);
                }
                for (int i = 0; i < callableGroups.Length; ++i)
                {
                    associationExports.Add(callableGroups[i].General.EntryName);
                }

                Vortice.Direct3D12.SubObjectToExportsAssociation associationDescription = new Vortice.Direct3D12.SubObjectToExportsAssociation(localRootSignatureSubObject, associationExports.ToArray());
                stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(associationDescription));
            }

            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.RaytracingShaderConfig(descriptor.MaxPayloadSize, descriptor.MaxAttributeSize)));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.RaytracingPipelineConfig(descriptor.MaxRecursionDepth)));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.GlobalRootSignature(globalPipelineLayout.NativeRootSignature)));

            Vortice.Direct3D12.StateObjectDescription stateObjectDesc = new Vortice.Direct3D12.StateObjectDescription(Vortice.Direct3D12.StateObjectType.RaytracingPipeline, stateSubObjects.ToArray());

            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateStateObject(stateObjectDesc, out Vortice.Direct3D12.ID3D12StateObject? nativePipeline);
#if DEBUG
            if (hResult.Failure)
            {
                Console.WriteLine($"[Dx12RaytracingPipeline] CreateStateObject failed. SharpGen.Runtime.Result=0x{hResult:X8}");
                Dx12PipelineDebug.DumpDeviceMessages(device, "[Dx12RaytracingPipeline]");
            }
#endif
            m_NativePipeline = Dx12Utility.RequireCreatedObject(
                nativePipeline,
                hResult,
                "ID3D12Device.CreateStateObject(raytracing pipeline)");
            m_NativeStateObjectProperties = m_NativePipeline.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12StateObjectProperties>()
                ?? throw new InvalidOperationException("Failed to query Vortice.Direct3D12.ID3D12StateObjectProperties from raytracing pipeline state object.");
        }

        internal string GetExportName(in ERHIRayShaderTableSection section, in int groupIndex)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => groupIndex == 0 ? m_RayGenerationExport : throw new ArgumentOutOfRangeException(nameof(groupIndex)),
                ERHIRayShaderTableSection.Miss => m_MissExports[groupIndex],
                ERHIRayShaderTableSection.Hit => m_HitGroupExports[groupIndex],
                ERHIRayShaderTableSection.Callable => m_CallableExports[groupIndex],
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };
        }

        private static Vortice.Direct3D12.ID3D12RootSignature BuildLocalConstantsRootSignature(Dx12Device device, in uint localDataStrideInBytes)
        {
            uint dwordCount = Math.Max(1u, (localDataStrideInBytes + 3u) / 4u);
            Vortice.Direct3D12.RootParameter1 localRootParameter = default;
            localRootParameter = new Vortice.Direct3D12.RootParameter1(new Vortice.Direct3D12.RootConstants(0, 0, dwordCount), Vortice.Direct3D12.ShaderVisibility.All);

            Vortice.Direct3D12.VersionedRootSignatureDescription rootSigDesc = new Vortice.Direct3D12.VersionedRootSignatureDescription(
                new Vortice.Direct3D12.RootSignatureDescription1(
                    Vortice.Direct3D12.RootSignatureFlags.LocalRootSignature,
                    new[] { localRootParameter },
                    Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob? signatureBlob = null;
            try
            {
                string rootSigError = Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(rootSigDesc, out signatureBlob);
                if (!string.IsNullOrWhiteSpace(rootSigError))
                {
                    throw new InvalidOperationException($"DX12 local root-signature serialization failed: {rootSigError}");
                }
                if (signatureBlob == null)
                {
                    throw new InvalidOperationException("DX12 local root-signature serialization returned no signature blob.");
                }

                SharpGen.Runtime.Result createRootSignatureResult = device.NativeDevice.CreateRootSignature(
                    0,
                    signatureBlob.BufferPointer,
                    signatureBlob.BufferSize,
                    out Vortice.Direct3D12.ID3D12RootSignature? localRootSignature);
                return Dx12Utility.RequireCreatedObject(
                    localRootSignature,
                    createRootSignatureResult,
                    "ID3D12Device.CreateRootSignature(local raytracing constants)");
            }
            finally
            {
                signatureBlob?.Release();
            }
        }

        protected override void Release()
        {
            if (m_NativeStateObjectProperties != null)
            {
                m_NativeStateObjectProperties.Release();
                m_NativeStateObjectProperties = null;
            }

            if (m_NativePipeline != null)
            {
                m_NativePipeline.Release();
                m_NativePipeline = null;
            }

            if (m_LocalConstantsRootSignature != null)
            {
                m_LocalConstantsRootSignature.Release();
                m_LocalConstantsRootSignature = null;
            }
        }
    }

    internal unsafe class Dx12RasterPipeline : RHIRasterPipeline
    {
        public uint[] VertexStrides
        {
            get
            {
                return m_VertexStrides;
            }
        }
        public Vortice.Direct3D12.ID3D12PipelineState NativePipelineState =>
            m_NativePipelineState ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D.PrimitiveTopology PrimitiveTopology
        {
            get
            {
                return m_PrimitiveTopology;
            }
        }
        internal Vortice.Direct3D12.ID3D12RootSignature NativeRootSignature =>
            m_NativeRootSignature ?? throw new ObjectDisposedException(GetType().FullName);
        internal uint AttachmentRootParameterIndex { get; private set; } =
            uint.MaxValue;
        internal bool HasPrivateAttachmentRootSignature =>
            m_OwnsNativeRootSignature;

        private uint[] m_VertexStrides = Array.Empty<uint>();
        private Vortice.Direct3D12.ID3D12PipelineState? m_NativePipelineState;
        private Vortice.Direct3D.PrimitiveTopology m_PrimitiveTopology;
        private Vortice.Direct3D12.ID3D12RootSignature? m_NativeRootSignature;
        private bool m_OwnsNativeRootSignature;

        public Dx12RasterPipeline(Dx12Device device, in RHIRasterPipelineDescriptor descriptor)
            : this(device, descriptor, null)
        {
        }

        internal Dx12RasterPipeline(
            Dx12Device device,
            in RHIRasterPipelineDescriptor descriptor,
            Dx12PipelineCache? pipelineCache)
        {
            m_Descriptor = RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
            if (descriptor.PrimitiveAssembler.PrimitiveType == ERHIPrimitiveType.Mesh)
            {
                device.Capabilities.Mesh.Shader.Require(
                    "DX12 mesh-shader pipelines");
                throw new NotSupportedException(
                    "DX12 mesh-shader pipeline-state-stream path is not implemented.");
            }

            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology);
            m_VertexStrides = Array.Empty<uint>();

            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout
                ?? throw new ArgumentException(
                    "Dx12RasterPipeline requires a Dx12PipelineLayout.",
                    nameof(descriptor));
            bool needsPrivateAttachmentRootSignature =
                m_Descriptor.AttachmentInterface.ColorInputMask != 0;
            if (needsPrivateAttachmentRootSignature)
            {
                m_NativeRootSignature =
                    Dx12RasterAttachmentRootSignature.Create(
                        device,
                        pipelineLayout,
                        out uint attachmentRootParameterIndex);
                AttachmentRootParameterIndex = attachmentRootParameterIndex;
                m_OwnsNativeRootSignature = true;
            }
            else
            {
                m_NativeRootSignature = pipelineLayout.NativeRootSignature;
            }
            Vortice.Direct3D12.PrimitiveTopologyType primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveAssembler.PrimitiveTopology);

            switch (descriptor.PrimitiveAssembler.PrimitiveType)
            {
                case ERHIPrimitiveType.Mesh:
                    throw new InvalidOperationException("DX12 mesh-shader capability gate was bypassed.");

                case ERHIPrimitiveType.Vertex:
                    Dx12Function fragmentFunction = descriptor.FragmentFunction as Dx12Function
                        ?? throw new ArgumentException(
                            "Dx12RasterPipeline requires a Dx12Function fragment shader.",
                            nameof(descriptor));
                    if (!descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
                    {
                        throw new InvalidOperationException("Vertex pipeline descriptor is missing VertexAssembler.");
                    }

                    Dx12Function? vertexFunction = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexFunction as Dx12Function;
                    Span<RHIVertexLayoutDescriptor> vertexLayouts = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexLayouts.Span;
                    if (vertexFunction != null)
                    {
                        m_VertexStrides = new uint[vertexLayouts.Length];
                        for (int j = 0; j < vertexLayouts.Length; ++j)
                        {
                            m_VertexStrides[j] = vertexLayouts[j].Stride;
                        }
                    }

                    int inputElementCount = Dx12Utility.GetDx12VertexLayoutCount(vertexLayouts);
                    Vortice.Direct3D12.InputElementDescription[] inputElements = new Vortice.Direct3D12.InputElementDescription[inputElementCount];
                    Dx12Utility.ConvertToDx12VertexLayout(vertexLayouts, inputElements);
                    ERHIPixelFormat[] outputLocationFormats =
                        Dx12RasterSubPassLowering.ResolveOutputLocationFormats(
                            in m_Descriptor.AttachmentInterface,
                            m_Descriptor.ColorFormats);

                    Vortice.Direct3D12.GraphicsPipelineStateDescription nativeGraphicsPipelineDesc = new Vortice.Direct3D12.GraphicsPipelineStateDescription
                    {
                        InputLayout = new Vortice.Direct3D12.InputLayoutDescription(inputElements),
                        RootSignature = m_NativeRootSignature,
                        PrimitiveTopologyType = primitiveTopologyType,
                        SampleDescription = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount),
                        SampleMask = descriptor.RenderState.SampleMask.HasValue ? descriptor.RenderState.SampleMask.Value : uint.MaxValue,
                        BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderState.BlendState),
                        RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderState.RasterizerState, descriptor.SampleCount != ERHISampleCount.None),
                        DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderState.DepthStencilState),
                        Flags = Vortice.Direct3D12.PipelineStateFlags.None,
                        RenderTargetFormats = new Vortice.DXGI.Format[outputLocationFormats.Length],
                    };

                    if (descriptor.DepthFormat != ERHIPixelFormat.Unknown)
                    {
                        nativeGraphicsPipelineDesc.DepthStencilFormat = Dx12Utility.ConvertToDx12Format(descriptor.DepthFormat);
                    }

                    for (int outputLocation = 0;
                         outputLocation < outputLocationFormats.Length;
                         ++outputLocation)
                    {
                        nativeGraphicsPipelineDesc.RenderTargetFormats[
                            outputLocation] =
                            Dx12Utility.ConvertToDx12ViewFormat(
                                outputLocationFormats[outputLocation]);
                    }

                    if (vertexFunction != null)
                    {
                        nativeGraphicsPipelineDesc.VertexShader = vertexFunction.NativeShaderData;
                    }

                    if (fragmentFunction != null)
                    {
                        nativeGraphicsPipelineDesc.PixelShader = fragmentFunction.NativeShaderData;
                    }

                    if (pipelineCache != null)
                    {
                        m_NativePipelineState = pipelineCache.CreateRasterPipelineState(
                            descriptor,
                            nativeGraphicsPipelineDesc);
                        break;
                    }

                    SharpGen.Runtime.Result hResult = device.NativeDevice.CreateGraphicsPipelineState(nativeGraphicsPipelineDesc, out Vortice.Direct3D12.ID3D12PipelineState? nativePipelineState);
                    if (hResult.Failure || nativePipelineState == null)
                    {
                        string message =
                            $"Failed to create DX12 raster pipeline state. HRESULT=0x{hResult.Code:X8}; " +
                            $"PrimitiveTopology={descriptor.PrimitiveAssembler.PrimitiveTopology}; " +
                            $"TopologyType={nativeGraphicsPipelineDesc.PrimitiveTopologyType}; " +
                            $"DepthFormat={descriptor.DepthFormat}/{nativeGraphicsPipelineDesc.DepthStencilFormat}; " +
                            $"ColorFormats={string.Join(",", descriptor.ColorFormats)}; " +
                            $"RTVFormats={string.Join(",", nativeGraphicsPipelineDesc.RenderTargetFormats)}; " +
                            $"Sample={nativeGraphicsPipelineDesc.SampleDescription.Count}x q{nativeGraphicsPipelineDesc.SampleDescription.Quality}; " +
                            $"InputElements={inputElements.Length}; " +
                            $"VS={DescribeShaderBytecode(vertexFunction)}; PS={DescribeShaderBytecode(fragmentFunction)}" +
                            Environment.NewLine + Dx12PipelineDebug.CollectDeviceMessages(device, "[Dx12RasterPipeline]");
                        throw new InvalidOperationException(message);
                    }

                    m_NativePipelineState = nativePipelineState;
                    break;
            }
        }

        private static string DescribeShaderBytecode(Dx12Function? function)
        {
            if (function == null)
            {
                return "<null>";
            }

            ReadOnlySpan<byte> bytes = function.NativeShaderData.Span;
            uint magic = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes.Slice(0, 4)) : 0;
            return $"entry={function.Descriptor.EntryName}, payload={function.Descriptor.PayloadKind}, bytes={bytes.Length}, magic=0x{magic:X8}";
        }

        protected override void Release()
        {
            m_NativePipelineState?.Release();
            m_NativePipelineState = null;
            if (m_OwnsNativeRootSignature)
            {
                m_NativeRootSignature?.Release();
                m_NativeRootSignature = null;
            }
        }
    }

#pragma warning restore CS0169, CS0649, CA1416
    internal sealed class Dx12WorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal Vortice.Direct3D12.ID3D12StateObject NativeStateObject =>
            m_NativeStateObject ?? throw new ObjectDisposedException(GetType().FullName);
        internal Vortice.Direct3D12.ProgramIdentifier ProgramIdentifier => m_ProgramIdentifier;
        internal Vortice.Direct3D12.WorkGraphMemoryRequirements NativeMemoryRequirements => m_MemoryRequirements;
        public override RHIWorkGraphMemoryRequirements MemoryRequirements => new RHIWorkGraphMemoryRequirements(
            m_MemoryRequirements.MinSizeInBytes,
            m_MemoryRequirements.MaxSizeInBytes,
            m_MemoryRequirements.SizeGranularityInBytes);
        internal uint WorkGraphIndex => m_WorkGraphIndex;

        private Vortice.Direct3D12.ID3D12StateObject? m_NativeStateObject;
        private Vortice.Direct3D12.ID3D12StateObjectProperties1? m_StateObjectProperties;
        private Vortice.Direct3D12.ID3D12WorkGraphProperties? m_WorkGraphProperties;
        private Vortice.Direct3D12.ProgramIdentifier m_ProgramIdentifier;
        private Vortice.Direct3D12.WorkGraphMemoryRequirements m_MemoryRequirements;
        private readonly Dictionary<string, uint> m_EntrypointIndices = new Dictionary<string, uint>(StringComparer.Ordinal);
        private uint m_WorkGraphIndex;

        internal Dx12WorkGraphPipeline(Dx12Device device, in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            Dx12FunctionLibrary functionLibrary = descriptor.FunctionLibrary as Dx12FunctionLibrary
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline requires a Dx12FunctionLibrary.");
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline requires a Dx12PipelineLayout.");
            string programName = string.IsNullOrWhiteSpace(descriptor.Name) ? "WorkGraph" : descriptor.Name;

            Vortice.Direct3D12.StateSubObject[] stateSubObjects =
            [
                new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.DxilLibraryDescription(functionLibrary.NativeShaderData)),
                new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.GlobalRootSignature(pipelineLayout.NativeRootSignature)),
                new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.WorkGraphDescription
                {
                    ProgramName = programName,
                    Flags = Vortice.Direct3D12.WorkGraphFlags.IncludeAllAvailableNodes
                })
            ];

            Vortice.Direct3D12.StateObjectDescription stateObjectDesc = new Vortice.Direct3D12.StateObjectDescription(
                Vortice.Direct3D12.StateObjectType.Executable,
                stateSubObjects);
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateStateObject(stateObjectDesc, out Vortice.Direct3D12.ID3D12StateObject? nativeStateObject);
            m_NativeStateObject = Dx12Utility.RequireCreatedObject(
                nativeStateObject,
                hResult,
                "ID3D12Device.CreateStateObject(work graph pipeline)");

            m_StateObjectProperties = m_NativeStateObject.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12StateObjectProperties1>()
                ?? throw new InvalidOperationException("DX12 WorkGraph state object does not expose ID3D12StateObjectProperties1.");
            m_WorkGraphProperties = m_NativeStateObject.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12WorkGraphProperties>()
                ?? throw new InvalidOperationException("DX12 WorkGraph state object does not expose ID3D12WorkGraphProperties.");
            m_WorkGraphIndex = m_WorkGraphProperties!.GetWorkGraphIndex(programName);
            m_MemoryRequirements = m_WorkGraphProperties.GetWorkGraphMemoryRequirements(m_WorkGraphIndex);
            m_ProgramIdentifier = m_StateObjectProperties!.GetProgramIdentifier(programName);
        }

        internal uint GetEntrypointIndex(string entrypoint)
        {
            if (string.IsNullOrWhiteSpace(entrypoint))
            {
                throw new ArgumentException("WorkGraph entrypoint cannot be null or empty.", nameof(entrypoint));
            }

            if (m_EntrypointIndices.TryGetValue(entrypoint, out uint cachedIndex))
            {
                return cachedIndex;
            }

            uint entrypointIndex = m_WorkGraphProperties!.GetEntrypointIndex(m_WorkGraphIndex, new Vortice.Direct3D12.NodeId
            {
                Name = entrypoint,
                ArrayIndex = 0
            });
            m_EntrypointIndices.Add(entrypoint, entrypointIndex);
            return entrypointIndex;
        }

        protected override void Release()
        {
            m_WorkGraphProperties?.Release();
            m_WorkGraphProperties = null;
            m_StateObjectProperties?.Release();
            m_StateObjectProperties = null;
            m_NativeStateObject?.Release();
            m_NativeStateObject = null;
        }
    }

    internal unsafe class Dx12MLPipeline : RHIMLPipeline
    {
        internal string Name => m_Name;
        internal Dx12MLProgram Program => m_Program;
        internal Dx12Device Device => m_Dx12Device;
        internal int StageCount => m_CompiledOperators.Length;
        internal Vortice.DirectML.IDMLOperatorInitializer OperatorInitializer => m_OperatorInitializer ?? throw new InvalidOperationException("DX12 ML operator initializer is unavailable.");
        internal Vortice.DirectML.BindingProperties InitializerBindingProperties => m_InitializerBindingProperties;
        internal uint RequiredDescriptorCount => m_RequiredDescriptorCount;
        internal ulong ProgramIntermediateTensorSize => m_ProgramIntermediateTensorSize;

        private readonly string m_Name;
        private readonly Dx12Device m_Dx12Device;
        private readonly Dx12MLProgram m_Program;
        private Vortice.DirectML.IDMLOperator[] m_NativeOperators;
        private Vortice.DirectML.IDMLCompiledOperator[] m_CompiledOperators;
        private Vortice.DirectML.IDMLOperatorInitializer? m_OperatorInitializer;
        private Vortice.DirectML.BindingProperties[] m_CompiledBindingProperties;
        private ulong[] m_PersistentResourceOffsets;
        private Vortice.DirectML.BindingProperties m_InitializerBindingProperties;
        private uint m_RequiredDescriptorCount;
        private ulong m_ProgramIntermediateTensorSize;

        public Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor)
            : this(
                device,
                descriptor,
                descriptor.Binary.Format == ERHIMLBinaryFormat.DirectMLProgramV1
                    ? Dx12MlBinaryCodec.DeserializeProgram(descriptor.Binary.Payload)
                    : throw new InvalidOperationException(
                        $"DX12 ML pipeline requires {nameof(ERHIMLBinaryFormat.DirectMLProgramV1)} binary."))
        {
        }

        /// <summary>
        /// NativeML internal path: compile DirectML stages from private program IR (ADR-0053).
        /// Does not require a public <see cref="RHIMLBinary"/>.
        /// </summary>
        internal static Dx12MLPipeline CreateFromProgramIR(Dx12Device device, string name, in RHIMLProgramIR programIr)
        {
            return new Dx12MLPipeline(
                device,
                new RHIMLPipelineDescriptor
                {
                    Name = name ?? string.Empty,
                    Binary = default,
                },
                programIr);
        }

        private Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor, in RHIMLProgramIR programIr)
        {
            m_Descriptor = descriptor;
            m_Dx12Device = device;
            m_Name = descriptor.Name;
            m_Program = Dx12MLProgram.Create(programIr);
            m_Program.ValidateDeviceSupport(device);
            m_BindingInfos = m_Program.BindingInfos;

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            try
            {
                Vortice.DirectML.OperatorDescription[] operatorDescriptions = m_Program.CreateOperatorDescriptions();
                m_NativeOperators = new Vortice.DirectML.IDMLOperator[operatorDescriptions.Length];
                m_CompiledOperators = new Vortice.DirectML.IDMLCompiledOperator[operatorDescriptions.Length];
                m_CompiledBindingProperties = new Vortice.DirectML.BindingProperties[operatorDescriptions.Length];
                m_PersistentResourceOffsets = new ulong[operatorDescriptions.Length];

                for (int i = 0; i < operatorDescriptions.Length; ++i)
                {
                    m_NativeOperators[i] = device.DirectMLDevice.CreateOperator(operatorDescriptions[i]);
                    m_CompiledOperators[i] = device.DirectMLDevice.CompileOperator(m_NativeOperators[i], Vortice.DirectML.ExecutionFlags.None);
                    m_CompiledBindingProperties[i] = m_CompiledOperators[i].GetBindingProperties();
                    m_RequiredDescriptorCount = Math.Max(m_RequiredDescriptorCount, m_CompiledBindingProperties[i].RequiredDescriptorCount);
                    m_TemporaryResourceSize = Math.Max(m_TemporaryResourceSize, m_CompiledBindingProperties[i].TemporaryResourceSize);
                    m_PersistentResourceOffsets[i] = m_PersistentResourceSize;
                    m_PersistentResourceSize += m_CompiledBindingProperties[i].PersistentResourceSize;
                }

                m_OperatorInitializer = device.DirectMLDevice.CreateOperatorInitializer(m_CompiledOperators);
                m_InitializerBindingProperties = m_OperatorInitializer.GetBindingProperties();
                m_RequiredDescriptorCount = Math.Max(m_RequiredDescriptorCount, m_InitializerBindingProperties.RequiredDescriptorCount);
                m_TemporaryResourceSize = Math.Max(m_TemporaryResourceSize, m_InitializerBindingProperties.TemporaryResourceSize);
                m_ProgramIntermediateTensorSize = CalculateProgramIntermediateTensorSize(m_Program);
            }
            catch
            {
                Release();
                throw;
            }
        }

        internal Vortice.DirectML.IDMLCompiledOperator GetCompiledOperator(int stageIndex)
        {
            return m_CompiledOperators[stageIndex];
        }

        internal ulong GetPersistentResourceOffset(int stageIndex)
        {
            return m_PersistentResourceOffsets[stageIndex];
        }

        internal ulong GetPersistentResourceSize(int stageIndex)
        {
            return m_CompiledBindingProperties[stageIndex].PersistentResourceSize;
        }

        internal uint GetRequiredDescriptorCount(int stageIndex)
        {
            return m_CompiledBindingProperties[stageIndex].RequiredDescriptorCount;
        }

        private static ulong CalculateProgramIntermediateTensorSize(Dx12MLProgram program)
        {
            ulong total = 0;
            RHIMLTensorDescriptor[] intermediates = program.IntermediateTensorDescriptors;
            for (int i = 0; i < intermediates.Length; ++i)
            {
                total += RHIMLHelpers.CalculateMinimumByteLength(intermediates[i]);
            }

            return total;
        }

        protected override void Release()
        {
            if (m_OperatorInitializer != null)
            {
                m_OperatorInitializer.Release();
                m_OperatorInitializer = null;
            }

            if (m_CompiledOperators != null)
            {
                for (int i = 0; i < m_CompiledOperators.Length; ++i)
                {
                    m_CompiledOperators[i]?.Release();
                }
                m_CompiledOperators = Array.Empty<Vortice.DirectML.IDMLCompiledOperator>();
            }

            if (m_NativeOperators != null)
            {
                for (int i = 0; i < m_NativeOperators.Length; ++i)
                {
                    m_NativeOperators[i]?.Release();
                }
                m_NativeOperators = Array.Empty<Vortice.DirectML.IDMLOperator>();
            }
        }
    }

    #region MLProgramIR
    /// <summary>
    /// Backend-private ML operator kinds used by cook tools and internal program IR.
    /// Not part of the RHI public surface (ADR-0052).
    /// </summary>
    internal enum ERHIMLOpKind : ushort
    {
        Unknown = 0,

        ElementWiseAdd = 1,
        ElementWiseSubtract = 2,
        ElementWiseMultiply = 3,
        ElementWiseDivide = 4,
        ElementWiseNegate = 5,

        ActivationRelu = 10,
        ActivationSigmoid = 11,
        ActivationTanh = 12,

        MatrixMultiply = 20,
        GeneralMatrixMultiply = 21,

        ActivationSoftmax = 30,
        MeanVarianceNormalization = 31,
        ReduceMean = 32,

        Reshape = 40,
        Transpose = 41,

        ElementWiseIdentity = 50,
    }

    internal enum ERHIMLMatrixTransform : byte
    {
        None = 0,
        Transpose = 1,
    }

    internal enum ERHIMLFusedActivation : byte
    {
        None = 0,
        Relu = 1,
        Sigmoid = 2,
        Tanh = 3,
    }

    internal struct RHIMLOpTensorRef
    {
        public int InputIndex;
        public int OpIndex;
        public bool IsOpOutput;

        public static RHIMLOpTensorRef FromInput(int inputIndex)
        {
            return new RHIMLOpTensorRef { InputIndex = inputIndex, OpIndex = -1, IsOpOutput = false };
        }

        public static RHIMLOpTensorRef FromOpOutput(int opIndex)
        {
            if (opIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(opIndex), "Op index must be non-negative.");
            }

            return new RHIMLOpTensorRef { InputIndex = -1, OpIndex = opIndex, IsOpOutput = true };
        }

        public readonly override string ToString() => IsOpOutput ? $"op[{OpIndex}].out" : $"input[{InputIndex}]";
    }

    internal struct RHIMLOpDescriptor
    {
        public ERHIMLOpKind Kind;
        public RHIMLOpTensorRef[] Inputs;
        public RHIMLTensorDescriptor Output;
        public string Name;

        public float Alpha;
        public float Beta;
        public ERHIMLMatrixTransform TransformA;
        public ERHIMLMatrixTransform TransformB;
        public ERHIMLFusedActivation FusedActivation;
        public float Epsilon;
        public int[]? Axes;

        public static RHIMLOpDescriptor Create(ERHIMLOpKind kind, RHIMLOpTensorRef[] inputs, in RHIMLTensorDescriptor output, string? name = null)
        {
            return new RHIMLOpDescriptor
            {
                Kind = kind,
                Inputs = inputs ?? Array.Empty<RHIMLOpTensorRef>(),
                Output = output,
                Name = name ?? string.Empty,
                Alpha = 1.0f,
                Beta = 1.0f,
                TransformA = ERHIMLMatrixTransform.None,
                TransformB = ERHIMLMatrixTransform.None,
                FusedActivation = ERHIMLFusedActivation.None,
                Epsilon = 0.0f,
                Axes = null,
            };
        }
    }

    /// <summary>
    /// Backend-private ordered op-sequence program IR. Cook tools serialize this into
    /// <see cref="RHIMLBinary"/> payloads; runtime never exposes it publicly (ADR-0052).
    /// </summary>
    internal struct RHIMLProgramIR
    {
        public string Name;
        public RHIMLTensorDescriptor[] Inputs;
        public RHIMLTensorDescriptor[] Outputs;
        public RHIMLOpDescriptor[] Ops;

        public static RHIMLProgramIR Create(string name, RHIMLTensorDescriptor[] inputs, RHIMLTensorDescriptor[] outputs, RHIMLOpDescriptor[] ops)
        {
            return new RHIMLProgramIR
            {
                Name = name,
                Inputs = inputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Outputs = outputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Ops = ops ?? Array.Empty<RHIMLOpDescriptor>(),
            };
        }
    }
    #endregion

    #region MLBinaryHash
    internal static class RHIMLBinaryHash
    {
        internal static ulong ComputeContentHash(ReadOnlySpan<byte> payload)
        {
            const ulong offsetBasis = 0xCBF29CE484222325UL;
            const ulong prime = 0x100000001B3UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < payload.Length; ++i)
            {
                hash ^= payload[i];
                hash *= prime;
            }

            return hash;
        }
    }
    #endregion

    #region MLBinaryCodec
    /// <summary>
    /// DirectMLProgramV1 (.dmlbin) container: magic "DMLB", version 1, content hash, program IR payload.
    /// </summary>
    internal static class Dx12MlBinaryCodec
    {
        internal const uint Magic = 0x424C4D44; // "DMLB" little-endian
        internal const byte Version = 1;
        internal const int HeaderSize = 13;

        internal static RHIMLBinary Load(ReadOnlyMemory<byte> container)
        {
            ValidateContainer(container, ERHIMLBinaryFormat.DirectMLProgramV1);
            RHIMLBinaryReflection reflection = ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.DirectMLProgramV1,
                container,
                reflection,
                contentHash);
        }

        internal static RHIMLBinary Pack(in RHIMLProgramIR program)
        {
            RHIMLBinaryReflection reflection = BuildReflection(program);
            byte[] payload = SerializeProgram(program);
            byte[] container = WriteContainer(payload);
            ulong contentHash = RHIMLBinaryHash.ComputeContentHash(payload);
            return new RHIMLBinary(
                ERHIMLBinaryFormat.DirectMLProgramV1,
                container,
                reflection,
                contentHash);
        }

        internal static RHIMLProgramIR DeserializeProgram(ReadOnlyMemory<byte> container)
        {
            ReadOnlyMemory<byte> programPayload = ValidateContainer(container, ERHIMLBinaryFormat.DirectMLProgramV1);
            return ReadProgram(programPayload.Span);
        }

        internal static RHIMLBinaryReflection ReadReflection(ReadOnlyMemory<byte> container)
        {
            RHIMLProgramIR program = DeserializeProgram(container);
            return BuildReflection(program);
        }

        internal static byte[] SerializeProgram(in RHIMLProgramIR program)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteProgram(writer, program);
            return stream.ToArray();
        }

        private static byte[] WriteContainer(ReadOnlySpan<byte> programPayload)
        {
            byte[] container = new byte[HeaderSize + programPayload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(0, 4), Magic);
            container[4] = Version;
            ulong hash = RHIMLBinaryHash.ComputeContentHash(programPayload);
            BinaryPrimitives.WriteUInt64LittleEndian(container.AsSpan(5, 8), hash);
            programPayload.CopyTo(container.AsSpan(HeaderSize));
            return container;
        }

        internal static ReadOnlyMemory<byte> ValidateContainer(ReadOnlyMemory<byte> container, ERHIMLBinaryFormat expectedFormat)
        {
            if (expectedFormat != ERHIMLBinaryFormat.DirectMLProgramV1)
            {
                throw new InvalidOperationException($"DX12 ML binary codec cannot decode format '{expectedFormat}'.");
            }

            ReadOnlySpan<byte> bytes = container.Span;
            if (bytes.Length < HeaderSize)
            {
                throw new InvalidOperationException("DirectMLProgramV1 container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            if (magic != Magic)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 magic mismatch. expected=0x{Magic:X8}, actual=0x{magic:X8}.");
            }

            byte version = bytes[4];
            if (version != Version)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 version mismatch. expected={Version}, actual={version}.");
            }

            ulong storedHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(5, 8));
            ReadOnlySpan<byte> programPayload = bytes.Slice(HeaderSize);
            ulong computedHash = RHIMLBinaryHash.ComputeContentHash(programPayload);
            if (storedHash != computedHash)
            {
                throw new InvalidOperationException(
                    $"DirectMLProgramV1 content hash mismatch. expected=0x{storedHash:X16}, actual=0x{computedHash:X16}.");
            }

            return container.Slice(HeaderSize);
        }

        private static RHIMLBinaryReflection BuildReflection(in RHIMLProgramIR program)
        {
            int bindingCount = program.Inputs.Length + program.Outputs.Length;
            RHIMLTensorBindingInfo[] bindings = new RHIMLTensorBindingInfo[bindingCount];
            int writeIndex = 0;
            for (uint index = 0; index < program.Inputs.Length; ++index)
            {
                bindings[writeIndex++] = new RHIMLTensorBindingInfo
                {
                    Name = $"input{index}",
                    Index = index,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(in program.Inputs[index]),
                };
            }

            for (uint index = 0; index < program.Outputs.Length; ++index)
            {
                bindings[writeIndex++] = new RHIMLTensorBindingInfo
                {
                    Name = $"output{index}",
                    Index = index,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(in program.Outputs[index]),
                };
            }

            return new RHIMLBinaryReflection
            {
                EntryName = "main",
                Bindings = bindings,
                IntermediateHeapSizeHint = 0,
            };
        }

        private static void WriteProgram(BinaryWriter writer, in RHIMLProgramIR program)
        {
            WriteString(writer, program.Name ?? string.Empty);
            WriteTensorDescriptors(writer, program.Inputs);
            WriteTensorDescriptors(writer, program.Outputs);
            writer.Write(program.Ops.Length);
            for (int i = 0; i < program.Ops.Length; ++i)
            {
                WriteOp(writer, in program.Ops[i]);
            }
        }

        private static RHIMLProgramIR ReadProgram(ReadOnlySpan<byte> payload)
        {
            int offset = 0;
            string name = ReadString(payload, ref offset);
            RHIMLTensorDescriptor[] inputs = ReadTensorDescriptors(payload, ref offset);
            RHIMLTensorDescriptor[] outputs = ReadTensorDescriptors(payload, ref offset);
            int opCount = ReadInt32(payload, ref offset);
            RHIMLOpDescriptor[] ops = new RHIMLOpDescriptor[opCount];
            for (int i = 0; i < opCount; ++i)
            {
                ops[i] = ReadOp(payload, ref offset);
            }

            if (offset != payload.Length)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 payload has trailing bytes. consumed={offset}, total={payload.Length}.");
            }

            return RHIMLProgramIR.Create(name, inputs, outputs, ops);
        }

        private static void WriteOp(BinaryWriter writer, in RHIMLOpDescriptor op)
        {
            writer.Write((ushort)op.Kind);
            WriteString(writer, op.Name ?? string.Empty);
            writer.Write(op.Inputs.Length);
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef input = op.Inputs[i];
                writer.Write(input.InputIndex);
                writer.Write(input.OpIndex);
                writer.Write(input.IsOpOutput);
            }

            WriteTensorDescriptor(writer, in op.Output);
            writer.Write(op.Alpha);
            writer.Write(op.Beta);
            writer.Write((byte)op.TransformA);
            writer.Write((byte)op.TransformB);
            writer.Write((byte)op.FusedActivation);
            writer.Write(op.Epsilon);
            if (op.Axes is { Length: > 0 } axes)
            {
                writer.Write(axes.Length);
                for (int i = 0; i < axes.Length; ++i)
                {
                    writer.Write(axes[i]);
                }
            }
            else
            {
                writer.Write(0);
            }
        }

        private static RHIMLOpDescriptor ReadOp(ReadOnlySpan<byte> payload, ref int offset)
        {
            ERHIMLOpKind kind = (ERHIMLOpKind)ReadUInt16(payload, ref offset);
            string name = ReadString(payload, ref offset);
            int inputCount = ReadInt32(payload, ref offset);
            RHIMLOpTensorRef[] inputs = new RHIMLOpTensorRef[inputCount];
            for (int i = 0; i < inputCount; ++i)
            {
                inputs[i] = new RHIMLOpTensorRef
                {
                    InputIndex = ReadInt32(payload, ref offset),
                    OpIndex = ReadInt32(payload, ref offset),
                    IsOpOutput = ReadBoolean(payload, ref offset),
                };
            }

            RHIMLTensorDescriptor output = ReadTensorDescriptor(payload, ref offset);
            RHIMLOpDescriptor op = RHIMLOpDescriptor.Create(kind, inputs, in output, name);
            op.Alpha = ReadSingle(payload, ref offset);
            op.Beta = ReadSingle(payload, ref offset);
            op.TransformA = (ERHIMLMatrixTransform)ReadByte(payload, ref offset);
            op.TransformB = (ERHIMLMatrixTransform)ReadByte(payload, ref offset);
            op.FusedActivation = (ERHIMLFusedActivation)ReadByte(payload, ref offset);
            op.Epsilon = ReadSingle(payload, ref offset);
            int axisCount = ReadInt32(payload, ref offset);
            if (axisCount > 0)
            {
                int[] axes = new int[axisCount];
                for (int i = 0; i < axisCount; ++i)
                {
                    axes[i] = ReadInt32(payload, ref offset);
                }

                op.Axes = axes;
            }

            return op;
        }

        private static void WriteTensorDescriptors(BinaryWriter writer, RHIMLTensorDescriptor[] descriptors)
        {
            writer.Write(descriptors.Length);
            for (int i = 0; i < descriptors.Length; ++i)
            {
                WriteTensorDescriptor(writer, in descriptors[i]);
            }
        }

        private static RHIMLTensorDescriptor[] ReadTensorDescriptors(ReadOnlySpan<byte> payload, ref int offset)
        {
            int count = ReadInt32(payload, ref offset);
            RHIMLTensorDescriptor[] descriptors = new RHIMLTensorDescriptor[count];
            for (int i = 0; i < count; ++i)
            {
                descriptors[i] = ReadTensorDescriptor(payload, ref offset);
            }

            return descriptors;
        }

        private static void WriteTensorDescriptor(BinaryWriter writer, in RHIMLTensorDescriptor descriptor)
        {
            writer.Write((byte)descriptor.DataType);
            writer.Write((uint)descriptor.UsageFlag);
            writer.Write((byte)descriptor.StorageMode);
            uint[] dimensions = descriptor.Dimensions.ToArray();
            writer.Write(dimensions.Length);
            for (int i = 0; i < dimensions.Length; ++i)
            {
                writer.Write(dimensions[i]);
            }

            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                uint[] strides = explicitStrides.ToArray();
                writer.Write((byte)1);
                writer.Write(strides.Length);
                for (int i = 0; i < strides.Length; ++i)
                {
                    writer.Write(strides[i]);
                }
            }
            else
            {
                writer.Write((byte)0);
            }
        }

        private static RHIMLTensorDescriptor ReadTensorDescriptor(ReadOnlySpan<byte> payload, ref int offset)
        {
            ERHIMLDataType dataType = (ERHIMLDataType)ReadByte(payload, ref offset);
            ERHITensorUsage usage = (ERHITensorUsage)ReadUInt32(payload, ref offset);
            ERHIStorageMode storageMode = (ERHIStorageMode)ReadByte(payload, ref offset);
            int dimensionCount = ReadInt32(payload, ref offset);
            uint[] dimensions = new uint[dimensionCount];
            for (int i = 0; i < dimensionCount; ++i)
            {
                dimensions[i] = ReadUInt32(payload, ref offset);
            }

            Memory<uint>? strides = null;
            byte hasStrides = ReadByte(payload, ref offset);
            if (hasStrides != 0)
            {
                int strideCount = ReadInt32(payload, ref offset);
                uint[] strideValues = new uint[strideCount];
                for (int i = 0; i < strideCount; ++i)
                {
                    strideValues[i] = ReadUInt32(payload, ref offset);
                }

                strides = strideValues;
            }

            return new RHIMLTensorDescriptor
            {
                DataType = dataType,
                UsageFlag = usage,
                StorageMode = storageMode,
                Dimensions = dimensions,
                Strides = strides,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
        {
            int length = ReadInt32(payload, ref offset);
            if (length < 0 || offset + length > payload.Length)
            {
                throw new InvalidOperationException("DirectMLProgramV1 string length is out of range.");
            }

            string value = Encoding.UTF8.GetString(payload.Slice(offset, length));
            offset += length;
            return value;
        }

        private static byte ReadByte(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 1);
            byte value = payload[offset];
            ++offset;
            return value;
        }

        private static bool ReadBoolean(ReadOnlySpan<byte> payload, ref int offset)
        {
            return ReadByte(payload, ref offset) != 0;
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
            offset += 2;
            return value;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            // Must preserve signed values (e.g. OpIndex/InputIndex sentinel -1).
            EnsureRemaining(payload, offset, 4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static float ReadSingle(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 4);
            float value = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static void EnsureRemaining(ReadOnlySpan<byte> payload, int offset, int required)
        {
            if (offset + required > payload.Length)
            {
                throw new InvalidOperationException("DirectMLProgramV1 payload is truncated.");
            }
        }
    }
    #endregion
    #region MachineLearning
internal static class Dx12MLUtilities
    {
        internal static TensorDataType ConvertToDirectMLDataType(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => TensorDataType.Float32,
                ERHIMLDataType.Float16 => TensorDataType.Float16,
                ERHIMLDataType.Int32 => TensorDataType.Int32,
                ERHIMLDataType.Int16 => TensorDataType.Int16,
                ERHIMLDataType.Int8 => TensorDataType.Int8,
                ERHIMLDataType.UInt32 => TensorDataType.Uint32,
                ERHIMLDataType.UInt16 => TensorDataType.Uint16,
                ERHIMLDataType.UInt8 => TensorDataType.Uint8,
                _ => throw new NotSupportedException($"DX12 DirectML does not support ML data type '{dataType}'."),
            };
        }

        internal static TensorDescription CreateTensorDescription(in RHIMLTensorDescriptor descriptor)
        {
            BufferTensorDescription tensorDescription = new BufferTensorDescription
            {
                DataType = ConvertToDirectMLDataType(descriptor.DataType),
                Flags = TensorFlags.None,
                Sizes = descriptor.Dimensions.ToArray(),
                Strides = RHIMLHelpers.GetEffectiveStrides(descriptor),
                TotalTensorSizeInBytes = RHIMLHelpers.CalculateMinimumByteLength(descriptor),
                GuaranteedBaseOffsetAlignment = 0,
            };
            return tensorDescription;
        }

        internal static BindingDescription CreateBufferBinding(Dx12Buffer buffer, ulong offset, ulong sizeInBytes)
        {
            BufferBinding bufferBinding;
            bufferBinding.Buffer = buffer.NativeResource;
            bufferBinding.Offset = offset;
            bufferBinding.SizeInBytes = sizeInBytes;
            return bufferBinding;
        }

        internal static BindingDescription CreateTensorBinding(Dx12Tensor tensor)
        {
            return CreateBufferBinding(tensor.BackingBuffer, tensor.BackingBufferOffset, tensor.ByteLength);
        }

        internal static void ValidateTensorLayout(string label, in RHIMLTensorDescriptor expected, in RHIMLTensorDescriptor actual)
        {
            if (!RHIMLHelpers.HasCompatibleLayout(expected, actual))
            {
                throw new InvalidOperationException(
                    $"{label} tensor layout mismatch. expected={RHIMLHelpers.DescribeLayout(expected)}, actual={RHIMLHelpers.DescribeLayout(actual)}.");
            }
        }

        /// <summary>
        /// Builds the DirectML <see cref="OperatorDescription"/> for a single RHI ML op, using the
        /// provided resolved tensor descriptions (already mapped from program inputs / earlier op
        /// outputs). Returns null when the op kind is not mapped to a DirectML operator; the caller
        /// surfaces that as an explicit unsupported-op error.
        /// </summary>
        internal static OperatorDescription? CreateOperatorDescription(
            in RHIMLOpDescriptor op,
            TensorDescription[] resolvedInputs,
            TensorDescription outputTensor)
        {
            switch (op.Kind)
            {
                case ERHIMLOpKind.ElementWiseAdd:
                    return new ElementWiseAddOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseSubtract:
                    return new ElementWiseSubtractOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseMultiply:
                    return new ElementWiseMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseDivide:
                    return new ElementWiseDivideOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseNegate:
                    return new ElementWiseNegateOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationRelu:
                    return new ActivationReluOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationSigmoid:
                    return new ActivationSigmoidOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationTanh:
                    return new ActivationTanhOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.MatrixMultiply:
                    return new GeneralMatrixMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        CTensor = null,
                        OutputTensor = outputTensor,
                        TransformA = MapMatrixTransform(op.TransformA),
                        TransformB = MapMatrixTransform(op.TransformB),
                        Alpha = op.Alpha,
                        Beta = 0.0f,
                        FusedActivation = BuildFusedActivation(op.FusedActivation),
                    };
                case ERHIMLOpKind.GeneralMatrixMultiply:
                    return new GeneralMatrixMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        CTensor = resolvedInputs.Length >= 3 ? resolvedInputs[2] : null,
                        OutputTensor = outputTensor,
                        TransformA = MapMatrixTransform(op.TransformA),
                        TransformB = MapMatrixTransform(op.TransformB),
                        Alpha = op.Alpha,
                        Beta = op.Beta,
                        FusedActivation = BuildFusedActivation(op.FusedActivation),
                    };
                case ERHIMLOpKind.ActivationSoftmax:
                    return new ActivationSoftmaxOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.MeanVarianceNormalization:
                    return new MeanVarianceNormalization1OperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        ScaleTensor = resolvedInputs.Length >= 2 ? resolvedInputs[1] : null,
                        BiasTensor = resolvedInputs.Length >= 3 ? resolvedInputs[2] : null,
                        OutputTensor = outputTensor,
                        Axes = op.Axes ?? new[] { -1 },
                        NormalizeVariance = true,
                        Epsilon = op.Epsilon,
                        FusedActivation = null,
                    };
                case ERHIMLOpKind.ReduceMean:
                    return new ReduceOperatorDescription
                    {
                        Function = ReduceFunction.Average,
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                        Axes = op.Axes ?? new[] { -1 },
                    };
                case ERHIMLOpKind.Reshape:
                case ERHIMLOpKind.Transpose:
                case ERHIMLOpKind.ElementWiseIdentity:
                    // DirectML has no dedicated reshape/transpose operator: both are expressed as an
                    // element-wise identity copy with the permuted/reshaped layout encoded on the
                    // output tensor descriptor (sizes + strides). The outputTensor carries the
                    // target shape (reshape) or the permuted strides (transpose).
                    return new ElementWiseIdentityOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                        ScaleBias = null,
                    };
                default:
                    return null;
            }
        }

        internal static OperatorDescription? BuildFusedActivation(ERHIMLFusedActivation activation)
        {
            return activation switch
            {
                ERHIMLFusedActivation.None => null,
                ERHIMLFusedActivation.Relu => new ActivationReluOperatorDescription(),
                ERHIMLFusedActivation.Sigmoid => new ActivationSigmoidOperatorDescription(),
                ERHIMLFusedActivation.Tanh => new ActivationTanhOperatorDescription(),
                _ => null,
            };
        }

        internal static MatrixTransform MapMatrixTransform(ERHIMLMatrixTransform transform)
        {
            return transform switch
            {
                ERHIMLMatrixTransform.None => MatrixTransform.None,
                ERHIMLMatrixTransform.Transpose => MatrixTransform.Transpose,
                _ => MatrixTransform.None,
            };
        }
    }

    internal sealed class Dx12MLProgram : RHIMLProgram
    {
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }
        internal RHIMLTensorDescriptor[] IntermediateTensorDescriptors => m_IntermediateTensorDescriptors;
        internal RHIMLOpDescriptor[] Ops => m_Ops;
        internal RHIMLTensorDescriptor[] ProgramInputs => m_ProgramInputs;
        internal RHIMLTensorDescriptor[] ProgramOutputs => m_ProgramOutputs;

        private readonly RHIMLTensorDescriptor[] m_ProgramInputs;
        private readonly RHIMLTensorDescriptor[] m_ProgramOutputs;
        private readonly RHIMLTensorDescriptor[] m_IntermediateTensorDescriptors;
        private readonly RHIMLOpDescriptor[] m_Ops;

        private Dx12MLProgram(
            string name,
            RHIMLTensorDescriptor[] programInputs,
            RHIMLTensorDescriptor[] programOutputs,
            RHIMLTensorDescriptor[] intermediateDescriptors,
            RHIMLOpDescriptor[] ops,
            RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            m_ProgramInputs = programInputs;
            m_ProgramOutputs = programOutputs;
            m_IntermediateTensorDescriptors = intermediateDescriptors;
            m_Ops = ops;
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        /// <summary>
        /// Descriptor-driven constructor: the canonical entry point for general subgraph lowering
        /// (ADR-0028). Each op's output that is not a program output becomes an intermediate
        /// tensor; program outputs are surfaced as the binding table's output slots. Binding infos
        /// advertise the program's input and output slots so that <see cref="Dx12MLPipeline"/> and
        /// <see cref="Dx12MLBindingTable"/> can wire every stage from the same descriptor contract.
        /// </summary>
        internal static Dx12MLProgram Create(in RHIMLProgramIR descriptor)
        {
            if (descriptor.Ops.Length == 0)
            {
                throw new InvalidOperationException("DX12 ML program descriptor must contain at least one op.");
            }

            RHIMLTensorDescriptor[] programInputs = CloneDescriptors(descriptor.Inputs);
            RHIMLOpDescriptor[] ops = new RHIMLOpDescriptor[descriptor.Ops.Length];
            List<RHIMLTensorDescriptor> intermediates = new List<RHIMLTensorDescriptor>();

            // Determine which op outputs are program outputs (by matching shape/dtype to the
            // declared program outputs, in declared order) and which are intermediates. A program
            // output is matched to the op that produces it by position: the k-th program output is
            // the output of the op referenced by no-one-but-the-output-binding. Because the
            // descriptor is a linear DAG, we mark an op output as a program output when its op is
            // the last producer of that logical output; the simpler, contract-faithful rule used
            // here: op outputs that are never consumed by a later op are program outputs (in op
            // order), and their count must equal descriptor.Outputs.Length.
            bool[] isProgramOutput = new bool[ops.Length];
            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                isProgramOutput[i] = true;
            }

            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in descriptor.Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex < isProgramOutput.Length)
                    {
                        isProgramOutput[inputRef.OpIndex] = false;
                    }
                }
            }

            int programOutputCount = 0;
            for (int i = 0; i < isProgramOutput.Length; ++i)
            {
                if (isProgramOutput[i])
                {
                    ++programOutputCount;
                }
            }

            if (programOutputCount != descriptor.Outputs.Length)
            {
                throw new InvalidOperationException(
                    $"DX12 ML program descriptor output count mismatch: {programOutputCount} op(s) produce unconsumed outputs but {descriptor.Outputs.Length} program output(s) were declared.");
            }

            // Build per-op output descriptors: program outputs use the declared output descriptors
            // (in op order over unconsumed ops); intermediate outputs get a GPULocal descriptor with
            // the op's output shape. The op's output descriptor already carries the shape from the
            // graph builder.
            int outputMatchIndex = 0;
            RHIMLTensorDescriptor[] opOutputDescriptors = new RHIMLTensorDescriptor[ops.Length];
            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                ref readonly RHIMLOpDescriptor srcOp = ref descriptor.Ops[i];
                if (isProgramOutput[i])
                {
                    opOutputDescriptors[i] = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[outputMatchIndex]);
                    ++outputMatchIndex;
                }
                else
                {
                    RHIMLTensorDescriptor intermediate = RHIMLHelpers.CloneLayoutDescriptor(srcOp.Output);
                    intermediate.StorageMode = ERHIStorageMode.GPULocal;
                    intermediate.BackingBuffer = null;
                    intermediate.BackingBufferOffset = 0;
                    intermediate.UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write;
                    opOutputDescriptors[i] = intermediate;
                    intermediates.Add(intermediate);
                }

                ops[i] = srcOp;
            }

            // Binding infos: program inputs first (Input kind), then program outputs (Output kind).
            // Intermediate tensors are owned by the binding table, not advertised as program bindings.
            List<RHIMLTensorBindingInfo> bindingInfos = new List<RHIMLTensorBindingInfo>();
            for (int i = 0; i < programInputs.Length; ++i)
            {
                bindingInfos.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"Input{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(programInputs[i]),
                });
            }

            for (int i = 0; i < descriptor.Outputs.Length; ++i)
            {
                bindingInfos.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"Output{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[i]),
                });
            }

            return new Dx12MLProgram(
                descriptor.Name,
                programInputs,
                CloneDescriptors(descriptor.Outputs),
                intermediates.ToArray(),
                ops,
                bindingInfos.ToArray());
        }

        internal void ValidateDeviceSupport(Dx12Device device)
        {
            ThrowIfDisposed();
            if (!device.SupportsDirectML)
            {
                throw new NotSupportedException("DirectML is unavailable on this DX12 device.");
            }

            HashSet<TensorDataType> seenTypes = new HashSet<TensorDataType>();
            for (int i = 0; i < m_ProgramInputs.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_ProgramInputs[i].DataType));
            }

            for (int i = 0; i < m_ProgramOutputs.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_ProgramOutputs[i].DataType));
            }

            for (int i = 0; i < m_IntermediateTensorDescriptors.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_IntermediateTensorDescriptors[i].DataType));
            }

            foreach (TensorDataType tensorDataType in seenTypes)
            {
                if (!device.DirectMLDevice.CheckTensorDataTypeSupport(tensorDataType))
                {
                    throw new NotSupportedException($"DirectML tensor data type '{tensorDataType}' is not supported by the current DX12 device.");
                }
            }
        }

        internal OperatorDescription[] CreateOperatorDescriptions()
        {
            OperatorDescription[] descriptions = new OperatorDescription[m_Ops.Length];
            for (int i = 0; i < m_Ops.Length; ++i)
            {
                ref readonly RHIMLOpDescriptor op = ref m_Ops[i];
                TensorDescription[] resolvedInputs = ResolveOpInputs(op);
                TensorDescription outputTensor = Dx12MLUtilities.CreateTensorDescription(GetOpOutputDescriptor(i));
                OperatorDescription? description = Dx12MLUtilities.CreateOperatorDescription(op, resolvedInputs, outputTensor);
                if (!description.HasValue)
                {
                    throw new NotSupportedException($"DirectML does not support RHI ML op kind '{op.Kind}' (op '{op.Name}').");
                }

                descriptions[i] = description.Value;
            }

            return descriptions;
        }

        internal RHIMLTensorDescriptor GetOpOutputDescriptor(int opIndex)
        {
            // A program output if unconsumed; otherwise an intermediate.
            // Re-derive the isProgramOutput flag consistently with Create().
            bool isProgramOutput = true;
            for (int i = 0; i < m_Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in m_Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex == opIndex && i != opIndex)
                    {
                        isProgramOutput = false;
                        break;
                    }
                }

                if (!isProgramOutput)
                {
                    break;
                }
            }

            if (isProgramOutput)
            {
                // Map to the program output by op order among unconsumed ops.
                int outputIndex = 0;
                for (int i = 0; i < opIndex; ++i)
                {
                    bool unconsumed = true;
                    for (int j = 0; j < m_Ops.Length; ++j)
                    {
                        foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                        {
                            if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                            {
                                unconsumed = false;
                                break;
                            }
                        }

                        if (!unconsumed)
                        {
                            break;
                        }
                    }

                    if (unconsumed)
                    {
                        ++outputIndex;
                    }
                }

                return m_ProgramOutputs[outputIndex];
            }

            // Intermediate: find by op order among consumed ops.
            int intermediateIndex = 0;
            for (int i = 0; i < opIndex; ++i)
            {
                bool consumed = false;
                for (int j = 0; j < m_Ops.Length; ++j)
                {
                    foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                    {
                        if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                        {
                            consumed = true;
                            break;
                        }
                    }

                    if (consumed)
                    {
                        break;
                    }
                }

                if (consumed)
                {
                    ++intermediateIndex;
                }
            }

            return m_IntermediateTensorDescriptors[intermediateIndex];
        }

        internal int GetOpIntermediateIndex(int opIndex)
        {
            int intermediateIndex = 0;
            for (int i = 0; i < opIndex; ++i)
            {
                bool consumed = false;
                for (int j = 0; j < m_Ops.Length; ++j)
                {
                    foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                    {
                        if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                        {
                            consumed = true;
                            break;
                        }
                    }

                    if (consumed)
                    {
                        break;
                    }
                }

                if (consumed)
                {
                    ++intermediateIndex;
                }
            }

            return intermediateIndex;
        }

        private TensorDescription[] ResolveOpInputs(in RHIMLOpDescriptor op)
        {
            TensorDescription[] resolved = new TensorDescription[op.Inputs.Length];
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef inputRef = op.Inputs[i];
                if (inputRef.IsOpOutput)
                {
                    resolved[i] = Dx12MLUtilities.CreateTensorDescription(GetOpOutputDescriptor(inputRef.OpIndex));
                }
                else
                {
                    resolved[i] = Dx12MLUtilities.CreateTensorDescription(m_ProgramInputs[inputRef.InputIndex]);
                }
            }

            return resolved;
        }

        private static RHIMLTensorDescriptor[] CloneDescriptors(RHIMLTensorDescriptor[] descriptors)
        {
            RHIMLTensorDescriptor[] clones = new RHIMLTensorDescriptor[descriptors.Length];
            for (int i = 0; i < descriptors.Length; ++i)
            {
                clones[i] = RHIMLHelpers.CloneLayoutDescriptor(descriptors[i]);
            }

            return clones;
        }

        protected override void Release()
        {
        }
    }

    internal sealed class Dx12MLBindingTable : RHIMLBindingTable
    {
        internal Dx12MLPipeline PipelineTyped => (Dx12MLPipeline)(m_Pipeline ?? throw new InvalidOperationException("DX12 ML binding table pipeline is unavailable."));
        internal Dx12Tensor[] Inputs { get; }
        internal Dx12Tensor[] Outputs { get; }
        internal Dx12Buffer? TemporaryBuffer => m_TemporaryBuffer;
        internal Dx12Buffer? PersistentBuffer => m_PersistentBuffer;
        internal Dx12Buffer[] IntermediateBuffers => m_IntermediateBuffers;
        internal IDMLBindingTable InitializerBindingTable => m_InitializerBindingTable ?? throw new InvalidOperationException("DX12 ML initializer binding table is unavailable.");
        internal bool IsInitialized => m_IsInitialized;
        internal bool InternalResourcesPrepared => m_InternalResourcesPrepared;
        internal Dx12Device Device => m_Device;

        private readonly Dx12Device m_Device;
        private readonly BindingDescription[] m_ProgramInputBindings;
        private readonly BindingDescription[] m_ProgramOutputBindings;
        private readonly BindingDescription[] m_IntermediateBindings;
        private readonly BindingDescription[][] m_StageInputs;
        private readonly BindingDescription[][] m_StageOutputs;
        private readonly BindingDescription?[] m_StagePersistentBindings;
        private readonly BindingDescription? m_TemporaryBinding;

        private IDMLBindingTable? m_InitializerBindingTable;
        private IDMLBindingTable?[] m_ExecutionBindingTables;
        private Dx12DescriptorInfo[] m_ExecutionDescriptorAllocations;
        private int[] m_ExecutionDescriptorCounts;
        private Dx12DescriptorInfo m_InitializerDescriptorAllocation;
        private int m_InitializerDescriptorCount;
        private Dx12Buffer? m_TemporaryBuffer;
        private Dx12Buffer? m_PersistentBuffer;
        private Dx12Buffer[] m_IntermediateBuffers;
        private bool m_IsInitialized;
        private bool m_InternalResourcesPrepared;
        private readonly bool m_OwnsInputTensors;
        private readonly bool m_OwnsOutputTensors;

        internal Dx12MLBindingTable(Dx12Device device, in RHIMLBindingTableDescriptor descriptor)
        {
            m_Device = device;

            if (descriptor.Pipeline is not Dx12MLPipeline dx12Pipeline)
            {
                throw new ArgumentException($"DX12 ML binding table requires a {nameof(Dx12MLPipeline)}.", nameof(descriptor));
            }
            if (dx12Pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Pipeline.GetType().FullName);
            }
            if (!ReferenceEquals(dx12Pipeline.Device, device))
            {
                throw new ArgumentException(
                    "DX12 ML binding-table pipeline belongs to a different device.",
                    nameof(descriptor));
            }

            m_Pipeline = dx12Pipeline;
            m_OwnsInputTensors = descriptor.InputViews.Length > 0;
            m_OwnsOutputTensors = descriptor.OutputViews.Length > 0;
            Inputs = ResolveTensors(
                device,
                dx12Pipeline,
                descriptor.Inputs.Span,
                descriptor.InputViews.Span,
                ERHIMLTensorBindingKind.Input,
                dx12Pipeline.InputCount);
            Outputs = ResolveTensors(
                device,
                dx12Pipeline,
                descriptor.Outputs.Span,
                descriptor.OutputViews.Span,
                ERHIMLTensorBindingKind.Output,
                dx12Pipeline.OutputCount);

            m_ProgramInputBindings = CreateTensorBindings(Inputs);
            m_ProgramOutputBindings = CreateTensorBindings(Outputs);
            m_ExecutionBindingTables = new IDMLBindingTable?[dx12Pipeline.StageCount];
            m_ExecutionDescriptorAllocations = new Dx12DescriptorInfo[dx12Pipeline.StageCount];
            m_ExecutionDescriptorCounts = new int[dx12Pipeline.StageCount];
            m_StagePersistentBindings = new BindingDescription?[dx12Pipeline.StageCount];

            Dx12MLProgram program = dx12Pipeline.Program;
            int intermediateCount = program.IntermediateTensorDescriptors.Length;
            m_IntermediateBuffers = new Dx12Buffer[intermediateCount];
            m_IntermediateBindings = new BindingDescription[intermediateCount];

            Dx12Buffer? temporaryBuffer = null;
            Dx12Buffer? persistentBuffer = null;
            IDMLBindingTable? initializerBindingTable = null;

            try
            {
                m_InitializerDescriptorCount = Math.Max(1, checked((int)dx12Pipeline.InitializerBindingProperties.RequiredDescriptorCount));
                m_InitializerDescriptorAllocation = device.AllocateCbvSrvUavDescriptor(m_InitializerDescriptorCount);
                BindingTableDescription initializerTableDescription = new BindingTableDescription
                {
                    Dispatchable = dx12Pipeline.OperatorInitializer,
                    CPUDescriptorHandle = m_InitializerDescriptorAllocation.CpuHandle,
                    GPUDescriptorHandle = m_InitializerDescriptorAllocation.GpuHandle,
                    SizeInDescriptors = checked((uint)m_InitializerDescriptorCount),
                };
                initializerBindingTable = device.DirectMLDevice.CreateBindingTable(ref initializerTableDescription);

                if (dx12Pipeline.TemporaryResourceSize > 0)
                {
                    temporaryBuffer = CreateInternalResourceBuffer(device, dx12Pipeline.TemporaryResourceSize);
                    m_TemporaryBinding = Dx12MLUtilities.CreateBufferBinding(temporaryBuffer, 0, dx12Pipeline.TemporaryResourceSize);
                }

                if (dx12Pipeline.PersistentResourceSize > 0)
                {
                    persistentBuffer = CreateInternalResourceBuffer(device, dx12Pipeline.PersistentResourceSize);
                }

                for (int i = 0; i < intermediateCount; ++i)
                {
                    ulong intermediateSize = RHIMLHelpers.CalculateMinimumByteLength(program.IntermediateTensorDescriptors[i]);
                    Dx12Buffer intermediateBuffer = CreateInternalResourceBuffer(device, intermediateSize);
                    m_IntermediateBuffers[i] = intermediateBuffer;
                    m_IntermediateBindings[i] = Dx12MLUtilities.CreateBufferBinding(intermediateBuffer, 0, intermediateSize);
                }

                // Per-stage input/output binding arrays, resolved from the program's op dataflow.
                m_StageInputs = new BindingDescription[dx12Pipeline.StageCount][];
                m_StageOutputs = new BindingDescription[dx12Pipeline.StageCount][];
                for (int stageIndex = 0; stageIndex < dx12Pipeline.StageCount; ++stageIndex)
                {
                    m_StageInputs[stageIndex] = ResolveStageInputs(program, stageIndex);
                    m_StageOutputs[stageIndex] = ResolveStageOutputs(program, stageIndex);

                    ulong stagePersistentSize = dx12Pipeline.GetPersistentResourceSize(stageIndex);
                    if (persistentBuffer != null && stagePersistentSize > 0)
                    {
                        m_StagePersistentBindings[stageIndex] = Dx12MLUtilities.CreateBufferBinding(
                            persistentBuffer,
                            dx12Pipeline.GetPersistentResourceOffset(stageIndex),
                            stagePersistentSize);
                    }

                    int executionDescriptorCount = Math.Max(1, checked((int)dx12Pipeline.GetRequiredDescriptorCount(stageIndex)));
                    Dx12DescriptorInfo executionDescriptorAllocation = device.AllocateCbvSrvUavDescriptor(executionDescriptorCount);
                    BindingTableDescription executionTableDescription = new BindingTableDescription
                    {
                        Dispatchable = dx12Pipeline.GetCompiledOperator(stageIndex),
                        CPUDescriptorHandle = executionDescriptorAllocation.CpuHandle,
                        GPUDescriptorHandle = executionDescriptorAllocation.GpuHandle,
                        SizeInDescriptors = checked((uint)executionDescriptorCount),
                    };

                    m_ExecutionDescriptorCounts[stageIndex] = executionDescriptorCount;
                    m_ExecutionDescriptorAllocations[stageIndex] = executionDescriptorAllocation;
                    m_ExecutionBindingTables[stageIndex] = device.DirectMLDevice.CreateBindingTable(ref executionTableDescription);
                }

                m_InitializerBindingTable = initializerBindingTable;
                m_TemporaryBuffer = temporaryBuffer;
                m_PersistentBuffer = persistentBuffer;
            }
            catch
            {
                initializerBindingTable?.Release();
                temporaryBuffer?.Dispose();
                persistentBuffer?.Dispose();
                for (int i = 0; i < m_IntermediateBuffers.Length; ++i)
                {
                    m_IntermediateBuffers[i]?.Dispose();
                }

                if (m_InitializerDescriptorCount > 0)
                {
                    device.FreeCbvSrvUavDescriptor(m_InitializerDescriptorAllocation.Index, m_InitializerDescriptorCount);
                    m_InitializerDescriptorCount = 0;
                }

                ReleaseExecutionBindingsAndDescriptors();

                throw;
            }
        }

        internal void PrepareForInitialization()
        {
            if (m_TemporaryBinding.HasValue)
            {
                InitializerBindingTable.BindTemporaryResource(m_TemporaryBinding);
            }

            if (m_PersistentBuffer != null && PipelineTyped.PersistentResourceSize > 0)
            {
                InitializerBindingTable.BindPersistentResource(Dx12MLUtilities.CreateBufferBinding(m_PersistentBuffer, 0, PipelineTyped.PersistentResourceSize));
            }
        }

        internal void PrepareForExecution(int stageIndex)
        {
            IDMLBindingTable bindingTable = m_ExecutionBindingTables[stageIndex]
                ?? throw new InvalidOperationException($"DX12 ML execution binding table for stage {stageIndex} is unavailable.");
            bindingTable.BindInputs(m_StageInputs[stageIndex]);
            bindingTable.BindOutputs(m_StageOutputs[stageIndex]);
            if (m_TemporaryBinding.HasValue)
            {
                bindingTable.BindTemporaryResource(m_TemporaryBinding);
            }

            if (m_StagePersistentBindings[stageIndex].HasValue)
            {
                bindingTable.BindPersistentResource(m_StagePersistentBindings[stageIndex]);
            }
        }

        internal IDMLBindingTable GetExecutionBindingTable(int stageIndex)
        {
            return m_ExecutionBindingTables[stageIndex]
                ?? throw new InvalidOperationException($"DX12 ML execution binding table for stage {stageIndex} is unavailable.");
        }

        internal void MarkInitialized()
        {
            m_IsInitialized = true;
        }

        internal void MarkInternalResourcesPrepared()
        {
            m_InternalResourcesPrepared = true;
        }

        private BindingDescription[] ResolveStageInputs(Dx12MLProgram program, int stageIndex)
        {
            RHIMLOpDescriptor op = program.Ops[stageIndex];
            BindingDescription[] inputs = new BindingDescription[op.Inputs.Length];
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef inputRef = op.Inputs[i];
                if (inputRef.IsOpOutput)
                {
                    int intermediateIndex = program.GetOpIntermediateIndex(inputRef.OpIndex);
                    inputs[i] = m_IntermediateBindings[intermediateIndex];
                }
                else
                {
                    inputs[i] = m_ProgramInputBindings[inputRef.InputIndex];
                }
            }

            return inputs;
        }

        private BindingDescription[] ResolveStageOutputs(Dx12MLProgram program, int stageIndex)
        {
            // A stage output is either a program output (unconsumed op) or an intermediate.
            bool isProgramOutput = true;
            for (int i = 0; i < program.Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in program.Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex == stageIndex && i != stageIndex)
                    {
                        isProgramOutput = false;
                        break;
                    }
                }

                if (!isProgramOutput)
                {
                    break;
                }
            }

            if (isProgramOutput)
            {
                int outputIndex = 0;
                for (int i = 0; i < stageIndex; ++i)
                {
                    bool unconsumed = true;
                    for (int j = 0; j < program.Ops.Length; ++j)
                    {
                        foreach (RHIMLOpTensorRef inputRef in program.Ops[j].Inputs)
                        {
                            if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                            {
                                unconsumed = false;
                                break;
                            }
                        }

                        if (!unconsumed)
                        {
                            break;
                        }
                    }

                    if (unconsumed)
                    {
                        ++outputIndex;
                    }
                }

                return new[] { m_ProgramOutputBindings[outputIndex] };
            }

            int intermediateIndex = program.GetOpIntermediateIndex(stageIndex);
            return new[] { m_IntermediateBindings[intermediateIndex] };
        }

        private static Dx12Tensor[] ResolveTensors(
            Dx12Device device,
            Dx12MLPipeline pipeline,
            ReadOnlySpan<RHITensor> tensors,
            ReadOnlySpan<RHITensorView> views,
            ERHIMLTensorBindingKind kind,
            uint expectedCount)
        {
            if (views.Length > 0)
            {
                if (tensors.Length > 0)
                {
                    throw new ArgumentException(
                        $"DX12 ML {kind} bindings must provide either tensors or tensor views, not both.");
                }

                return ConvertViews(device, pipeline, views, kind, expectedCount);
            }

            return ConvertTensors(device, pipeline, tensors, kind, expectedCount);
        }

        private static Dx12Tensor[] ConvertViews(
            Dx12Device device,
            Dx12MLPipeline pipeline,
            ReadOnlySpan<RHITensorView> views,
            ERHIMLTensorBindingKind kind,
            uint expectedCount)
        {
            if (views.Length != expectedCount)
            {
                throw new InvalidOperationException($"DX12 ML binding count mismatch for {kind}. expected={expectedCount}, actual={views.Length}.");
            }

            Dx12Tensor[] result = new Dx12Tensor[views.Length];
            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos = pipeline.BindingInfos.Span;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                if (bindingInfo.Kind != kind)
                {
                    continue;
                }

                if (bindingInfo.Index >= views.Length)
                {
                    throw new InvalidOperationException($"DX12 ML binding index out of range for {kind}. index={bindingInfo.Index}, count={views.Length}.");
                }

                RHITensorView candidate = views[(int)bindingInfo.Index]
                    ?? throw new ArgumentException($"DX12 ML binding tensor view[{bindingInfo.Index}] cannot be null.", nameof(views));
                if (candidate.IsDisposed)
                {
                    throw new ObjectDisposedException(candidate.GetType().FullName);
                }
                Dx12TensorView view = candidate as Dx12TensorView
                    ?? throw new ArgumentException($"DX12 ML binding tensor view[{bindingInfo.Index}] must be a {nameof(Dx12TensorView)}.", nameof(views));
                if (!ReferenceEquals(view.Device, device))
                {
                    throw new ArgumentException(
                        $"DX12 ML {kind} view[{bindingInfo.Index}] belongs to a different device.",
                        nameof(views));
                }

                Dx12MLUtilities.ValidateTensorLayout(
                    $"{kind}[{bindingInfo.Index}] '{bindingInfo.Name}'",
                    bindingInfo.Descriptor,
                    view.Descriptor);
                Dx12Tensor tensor = new Dx12Tensor(device, view.ToBackingTensorDescriptor());
                if (tensor.BackingBuffer.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
                {
                    tensor.Dispose();
                    throw new InvalidOperationException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] '{bindingInfo.Name}' must be backed by a GPULocal buffer. " +
                        $"DirectML dispatch requires resources in COMMON/UAV-capable memory, but got {view.BackingBuffer.Descriptor.StorageMode}.");
                }
                if ((tensor.BackingBuffer.Descriptor.UsageFlag & ERHIBufferUsage.UnorderedAccess) == 0)
                {
                    tensor.Dispose();
                    throw new InvalidOperationException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] '{bindingInfo.Name}' requires UnorderedAccess backing-buffer usage.");
                }

                result[bindingInfo.Index] = tensor;
            }

            for (int i = 0; i < result.Length; ++i)
            {
                if (result[i] is null)
                {
                    throw new InvalidOperationException($"DX12 ML binding table is missing a {kind} tensor view at index {i}.");
                }
            }

            return result;
        }

        private static Dx12Tensor[] ConvertTensors(
            Dx12Device device,
            Dx12MLPipeline pipeline,
            ReadOnlySpan<RHITensor> tensors,
            ERHIMLTensorBindingKind kind,
            uint expectedCount)
        {
            if (tensors.Length != expectedCount)
            {
                throw new InvalidOperationException($"DX12 ML binding count mismatch for {kind}. expected={expectedCount}, actual={tensors.Length}.");
            }

            Dx12Tensor[] result = new Dx12Tensor[tensors.Length];
            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos = pipeline.BindingInfos.Span;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                if (bindingInfo.Kind != kind)
                {
                    continue;
                }

                if (bindingInfo.Index >= tensors.Length)
                {
                    throw new InvalidOperationException($"DX12 ML binding index out of range for {kind}. index={bindingInfo.Index}, count={tensors.Length}.");
                }

                RHITensor candidate = tensors[(int)bindingInfo.Index]
                    ?? throw new ArgumentException($"DX12 ML binding tensor[{bindingInfo.Index}] cannot be null.", nameof(tensors));
                if (candidate.IsDisposed)
                {
                    throw new ObjectDisposedException(candidate.GetType().FullName);
                }
                Dx12Tensor tensor = candidate as Dx12Tensor
                    ?? throw new ArgumentException($"DX12 ML binding tensor[{bindingInfo.Index}] must be a {nameof(Dx12Tensor)}.", nameof(tensors));
                if (!ReferenceEquals(tensor.Device, device))
                {
                    throw new ArgumentException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] belongs to a different device.",
                        nameof(tensors));
                }
                Dx12MLUtilities.ValidateTensorLayout($"{kind}[{bindingInfo.Index}] '{bindingInfo.Name}'", bindingInfo.Descriptor, tensor.Descriptor);
                if (tensor.BackingBuffer.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
                {
                    throw new InvalidOperationException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] '{bindingInfo.Name}' must be backed by a GPULocal buffer. " +
                        $"DirectML dispatch requires resources in COMMON/UAV-capable memory, but got {tensor.BackingBuffer.Descriptor.StorageMode}.");
                }
                if ((tensor.BackingBuffer.Descriptor.UsageFlag & ERHIBufferUsage.UnorderedAccess) == 0)
                {
                    throw new InvalidOperationException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] '{bindingInfo.Name}' requires UnorderedAccess backing-buffer usage.");
                }
                result[bindingInfo.Index] = tensor;
            }

            for (int i = 0; i < result.Length; ++i)
            {
                if (result[i] is null)
                {
                    throw new InvalidOperationException($"DX12 ML binding table is missing a {kind} tensor at index {i}.");
                }
            }

            return result;
        }

        private static BindingDescription[] CreateTensorBindings(Dx12Tensor[] tensors)
        {
            BindingDescription[] bindings = new BindingDescription[tensors.Length];
            for (int i = 0; i < tensors.Length; ++i)
            {
                bindings[i] = Dx12MLUtilities.CreateTensorBinding(tensors[i]);
            }

            return bindings;
        }

        private static Dx12Buffer CreateInternalResourceBuffer(Dx12Device device, ulong byteSize)
        {
            RHIBufferDescriptor bufferDescriptor = new RHIBufferDescriptor
            {
                ByteSize = checked((int)byteSize),
                Format = ERHIBufferFormat.Undefine,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
            };
            return new Dx12Buffer(device, bufferDescriptor);
        }

        protected override void Release()
        {
            m_InitializerBindingTable?.Release();
            m_InitializerBindingTable = null;

            ReleaseExecutionBindingsAndDescriptors();
            m_ExecutionBindingTables = Array.Empty<IDMLBindingTable?>();
            m_ExecutionDescriptorAllocations = Array.Empty<Dx12DescriptorInfo>();
            m_ExecutionDescriptorCounts = Array.Empty<int>();

            m_TemporaryBuffer?.Dispose();
            m_TemporaryBuffer = null;

            m_PersistentBuffer?.Dispose();
            m_PersistentBuffer = null;

            if (m_IntermediateBuffers != null)
            {
                for (int i = 0; i < m_IntermediateBuffers.Length; ++i)
                {
                    m_IntermediateBuffers[i]?.Dispose();
                }
                m_IntermediateBuffers = Array.Empty<Dx12Buffer>();
            }

            if (m_OwnsInputTensors)
            {
                for (int i = 0; i < Inputs.Length; ++i)
                {
                    Inputs[i]?.Dispose();
                }
            }

            if (m_OwnsOutputTensors)
            {
                for (int i = 0; i < Outputs.Length; ++i)
                {
                    Outputs[i]?.Dispose();
                }
            }

            if (m_InitializerDescriptorCount > 0)
            {
                m_Device.FreeCbvSrvUavDescriptor(m_InitializerDescriptorAllocation.Index, m_InitializerDescriptorCount);
                m_InitializerDescriptorCount = 0;
            }
        }

        private void ReleaseExecutionBindingsAndDescriptors()
        {
            for (int i = 0; i < m_ExecutionDescriptorCounts.Length; ++i)
            {
                m_ExecutionBindingTables[i]?.Release();
                m_ExecutionBindingTables[i] = null;

                if (m_ExecutionDescriptorCounts[i] > 0)
                {
                    m_Device.FreeCbvSrvUavDescriptor(m_ExecutionDescriptorAllocations[i].Index, m_ExecutionDescriptorCounts[i]);
                    m_ExecutionDescriptorCounts[i] = 0;
                }
            }
        }
    }
    #endregion

    #region PipelineCache
#pragma warning disable CA1416
internal sealed unsafe class Dx12PipelineCache : RHIPipelineCache
    {
        private const int EInvalidArg = unchecked((int)0x80070057);
        private const int ENoInterface = unchecked((int)0x80004002);
        private const int EFail = unchecked((int)0x80004005);
        private const int EOutOfMemory = unchecked((int)0x8007000E);
        private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

        private readonly object m_Gate = new object();
        private readonly Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12PipelineLibrary1? m_NativePipelineCache;

        private Vortice.Direct3D12.ID3D12PipelineLibrary1 NativePipelineCache =>
            m_NativePipelineCache ?? throw new ObjectDisposedException(GetType().FullName);

        private int m_NativeHitCount;
        private int m_NativeMissCount;
        private int m_NativeStoreCount;

        internal int NativeHitCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeHitCount;
                }
            }
        }

        internal int NativeMissCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeMissCount;
                }
            }
        }

        internal int NativeStoreCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeStoreCount;
                }
            }
        }

        internal static bool TryProbeNativeSupport(Dx12Device device, out string reason)
        {
            ArgumentNullException.ThrowIfNull(device);

            Vortice.Direct3D12.ID3D12PipelineLibrary? baseCache = null;
            Vortice.Direct3D12.ID3D12PipelineLibrary1? nativeCache = null;
            try
            {
                SharpGen.Runtime.Result result =
                    ((Vortice.Direct3D12.ID3D12Device2)device.NativeDevice)
                    .CreatePipelineLibrary(
                        Array.Empty<byte>().AsSpan(),
                        out baseCache);
                if (result.Failure || baseCache == null)
                {
                    reason =
                        $"ID3D12Device2.CreatePipelineLibrary(empty) failed with HRESULT=0x{result.Code:X8}.";
                    return false;
                }

                nativeCache =
                    baseCache.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
                if (nativeCache == null)
                {
                    reason =
                        "The runtime-created DX12 pipeline library does not expose ID3D12PipelineLibrary1.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                reason =
                    $"DX12 pipeline-library runtime probing threw HRESULT=0x{exception.HResult:X8}.";
                return false;
            }
            finally
            {
                nativeCache?.Release();
                baseCache?.Release();
            }
        }

        internal Dx12PipelineCache(Dx12Device device)
            : base(device)
        {
            m_Dx12Device = device;
            if (!TryCreateNativeCache(
                    ReadOnlySpan<byte>.Empty,
                    out m_NativePipelineCache,
                    out string reason,
                    out int nativeCode))
            {
                throw CreateNativeException(
                    ERHIErrorCode.InitializationFailed,
                    nativeCode,
                    $"DX12 pipeline-cache initialization failed: {reason}");
            }
        }

        public override RHIComputePipeline CreateComputePipeline(
            in RHIComputePipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12ComputePipeline(m_Dx12Device, descriptor, this);
        }

        public override RHIRasterPipeline CreateRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12RasterPipeline(m_Dx12Device, descriptor, this);
        }

        internal Vortice.Direct3D12.ID3D12PipelineState CreateComputePipelineState(
            in RHIComputePipelineDescriptor descriptor,
            in Vortice.Direct3D12.ComputePipelineStateDescription nativeDescriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            string key = BuildComputePipelineCacheKey(descriptor);

            lock (m_Gate)
            {
                try
                {
                    Vortice.Direct3D12.ID3D12PipelineState? nativePipeline =
                        NativePipelineCache.LoadComputePipeline(
                        key,
                        nativeDescriptor);
                    if (nativePipeline == null)
                    {
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            EFail,
                            $"DX12 compute pipeline cache returned a null state for key {key}.");
                    }
                    ++m_NativeHitCount;
                    return nativePipeline;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                    when (exception.HResult == EInvalidArg)
                {
                    // E_INVALIDARG is the documented cache-miss signal for Load*Pipeline.
                    ++m_NativeMissCount;
                    SharpGen.Runtime.Result createResult =
                        m_Dx12Device.NativeDevice.CreateComputePipelineState(
                            nativeDescriptor,
                            out Vortice.Direct3D12.ID3D12PipelineState? nativePipeline);
                    if (createResult.Failure || nativePipeline == null)
                    {
                        nativePipeline?.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            createResult.Code,
                            $"DX12 compute pipeline creation failed while populating cache key {key}. "
                            + $"HRESULT=0x{createResult.Code:X8}.");
                    }

                    try
                    {
                        NativePipelineCache.StorePipeline(key, nativePipeline);
                        ++m_NativeStoreCount;
                        return nativePipeline;
                    }
                    catch (SharpGen.Runtime.SharpGenException storeException)
                    {
                        nativePipeline.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            storeException.HResult,
                            $"DX12 compute pipeline cache store failed for key {key}.",
                            storeException);
                    }
                    catch
                    {
                        nativePipeline.Release();
                        throw;
                    }
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        $"DX12 compute pipeline cache lookup failed for key {key}.",
                        exception);
                }
            }
        }

        internal Vortice.Direct3D12.ID3D12PipelineState CreateRasterPipelineState(
            in RHIRasterPipelineDescriptor descriptor,
            in Vortice.Direct3D12.GraphicsPipelineStateDescription nativeDescriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            string key = BuildRasterPipelineCacheKey(descriptor);

            lock (m_Gate)
            {
                try
                {
                    Vortice.Direct3D12.ID3D12PipelineState? nativePipeline =
                        NativePipelineCache.LoadGraphicsPipeline(
                        key,
                        nativeDescriptor);
                    if (nativePipeline == null)
                    {
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            EFail,
                            $"DX12 raster pipeline cache returned a null state for key {key}.");
                    }
                    ++m_NativeHitCount;
                    return nativePipeline;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                    when (exception.HResult == EInvalidArg)
                {
                    // E_INVALIDARG is the documented cache-miss signal for Load*Pipeline.
                    ++m_NativeMissCount;
                    SharpGen.Runtime.Result createResult =
                        m_Dx12Device.NativeDevice.CreateGraphicsPipelineState(
                            nativeDescriptor,
                            out Vortice.Direct3D12.ID3D12PipelineState? nativePipeline);
                    if (createResult.Failure || nativePipeline == null)
                    {
                        nativePipeline?.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            createResult.Code,
                            $"DX12 raster pipeline creation failed while populating cache key {key}. "
                            + $"HRESULT=0x{createResult.Code:X8}.");
                    }

                    try
                    {
                        NativePipelineCache.StorePipeline(key, nativePipeline);
                        ++m_NativeStoreCount;
                        return nativePipeline;
                    }
                    catch (SharpGen.Runtime.SharpGenException storeException)
                    {
                        nativePipeline.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            storeException.HResult,
                            $"DX12 raster pipeline cache store failed for key {key}.",
                            storeException);
                    }
                    catch
                    {
                        nativePipeline.Release();
                        throw;
                    }
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        $"DX12 raster pipeline cache lookup failed for key {key}.",
                        exception);
                }
            }
        }

        protected override bool TryReplaceNativePayload(
            ReadOnlySpan<byte> nativePayload,
            out string reason)
        {
            ThrowIfDisposed();
            if (!TryCreateNativeCache(
                    nativePayload,
                    out Vortice.Direct3D12.ID3D12PipelineLibrary1? replacement,
                    out reason,
                    out int nativeCode))
            {
                if (nativePayload.IsEmpty || IsFatalNativeFailure(nativeCode))
                {
                    throw CreateNativeException(
                        ERHIErrorCode.InitializationFailed,
                        nativeCode,
                        $"DX12 pipeline-cache import initialization failed: {reason}");
                }

                // A compatible wrapper whose backend-private payload is rejected is corrupt.
                return false;
            }

            lock (m_Gate)
            {
                Vortice.Direct3D12.ID3D12PipelineLibrary1? previous =
                    m_NativePipelineCache;
                m_NativePipelineCache = replacement;
                previous?.Release();
            }
            return true;
        }

        protected override byte[] ExportNativePayload()
        {
            ThrowIfDisposed();
            lock (m_Gate)
            {
                try
                {
                    SharpGen.Runtime.PointerUSize nativeSize =
                        NativePipelineCache.SerializedSize;
                    nuint byteCount = nativeSize;
                    if (byteCount > int.MaxValue)
                    {
                        throw new InvalidOperationException(
                            $"DX12 pipeline cache is {byteCount} bytes, exceeding the managed blob limit.");
                    }

                    byte[] payload = new byte[(int)byteCount];
                    if (payload.Length == 0)
                    {
                        return payload;
                    }

                    fixed (byte* payloadPointer = payload)
                    {
                        NativePipelineCache.Serialize(
                            (IntPtr)payloadPointer,
                            nativeSize);
                    }
                    return payload;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        "DX12 pipeline-cache export failed.",
                        exception);
                }
            }
        }

        protected override void Release()
        {
            lock (m_Gate)
            {
                if (m_NativePipelineCache != null)
                {
                    m_NativePipelineCache.Release();
                    m_NativePipelineCache = null;
                }
            }
        }

        private bool TryCreateNativeCache(
            ReadOnlySpan<byte> nativePayload,
            out Vortice.Direct3D12.ID3D12PipelineLibrary1? nativeCache,
            out string reason,
            out int nativeCode)
        {
            nativeCache = null;
            reason = string.Empty;
            nativeCode = 0;
            byte[] payloadCopy = nativePayload.ToArray();
            SharpGen.Runtime.Result createResult;
            Vortice.Direct3D12.ID3D12PipelineLibrary? baseCache = null;
            try
            {
                createResult =
                    ((Vortice.Direct3D12.ID3D12Device2)m_Dx12Device.NativeDevice)
                    .CreatePipelineLibrary(
                        payloadCopy.AsSpan(),
                        out baseCache);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                baseCache?.Release();
                nativeCode = exception.HResult;
                reason =
                    $"ID3D12Device2.CreatePipelineLibrary threw HRESULT=0x{nativeCode:X8}.";
                return false;
            }
            if (createResult.Failure || baseCache == null)
            {
                nativeCode = createResult.Code;
                reason =
                    $"ID3D12Device2.CreatePipelineLibrary failed with HRESULT=0x{createResult.Code:X8}.";
                baseCache?.Release();
                return false;
            }

            try
            {
                nativeCache =
                    baseCache.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
                if (nativeCache == null)
                {
                    nativeCode = ENoInterface;
                    reason =
                        "The created DX12 cache does not expose ID3D12PipelineLibrary1.";
                    return false;
                }
                return true;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                nativeCache?.Release();
                nativeCache = null;
                nativeCode = exception.HResult;
                reason =
                    $"ID3D12PipelineLibrary1 query failed with HRESULT=0x{nativeCode:X8}.";
                return false;
            }
            finally
            {
                baseCache.Release();
            }
        }

        private bool IsFatalNativeFailure(in int nativeCode)
        {
            return nativeCode == EOutOfMemory
                || GetDeviceState(out _) != ERHIDeviceState.Operational;
        }

        private RHIException CreateNativeException(
            in ERHIErrorCode requestedErrorCode,
            in long nativeCode,
            string message,
            Exception? innerException = null)
        {
            ERHIDeviceState deviceState = GetDeviceState(out int deviceNativeCode);
            ERHIErrorCode errorCode =
                deviceState != ERHIDeviceState.Operational
                    ? ERHIErrorCode.DeviceLost
                    : nativeCode == EOutOfMemory
                        ? ERHIErrorCode.OutOfMemory
                        : requestedErrorCode;
            long effectiveNativeCode =
                nativeCode != 0 ? nativeCode : deviceNativeCode;
            return new RHIException(
                errorCode,
                ERHIBackend.DirectX12,
                effectiveNativeCode,
                message,
                deviceState,
                innerException);
        }

        private ERHIDeviceState GetDeviceState(out int nativeCode)
        {
            try
            {
                SharpGen.Runtime.Result removalReason =
                    m_Dx12Device.NativeDevice.DeviceRemovedReason;
                nativeCode = removalReason.Code;
                if (removalReason.Success)
                {
                    return ERHIDeviceState.Operational;
                }

                return nativeCode == DxgiErrorDeviceReset
                    ? ERHIDeviceState.Reset
                    : ERHIDeviceState.Removed;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                nativeCode = exception.HResult;
                return nativeCode == DxgiErrorDeviceReset
                    ? ERHIDeviceState.Reset
                    : nativeCode == DxgiErrorDeviceRemoved
                        ? ERHIDeviceState.Removed
                        : ERHIDeviceState.Unknown;
            }
        }

        private void ValidateLayoutDevice(RHIPipelineLayout? pipelineLayout)
        {
            Dx12PipelineLayout dx12Layout = pipelineLayout as Dx12PipelineLayout
                ?? throw new ArgumentException(
                    "DX12 pipeline cache requires a Dx12PipelineLayout.",
                    nameof(pipelineLayout));
            if (dx12Layout.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Layout.GetType().FullName);
            }
            if (!ReferenceEquals(dx12Layout.Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    "DX12 pipeline cache cannot create a pipeline from a different device's layout.",
                    nameof(pipelineLayout));
            }
        }
    }
#pragma warning restore CA1416
    #endregion

#pragma warning restore CS0169, CS0649, CA1416
}
