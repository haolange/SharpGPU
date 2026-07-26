using System;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CS0169, CS0649, CA1416
    internal sealed class Dx12PipelineArgumentTablePlan
    {
        public uint TableIndex { get; }
        public Dx12ArgumentTableLayout Layout { get; }
        public uint[] RootParameterIndices { get; }

        public Dx12PipelineArgumentTablePlan(
            in uint tableIndex,
            Dx12ArgumentTableLayout layout,
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

        public Dx12PipelineArgumentTablePlan[] TablePlans { get; }
        public int DescriptorTableParameterCount { get; }
        public int TotalRootParameterCount { get; }
        public uint PushConstantRootParameterIndex { get; }
        public uint PushConstantSize { get; }

        private readonly Dictionary<uint, Dx12PipelineArgumentTablePlan> m_TablePlanMap;

        public Dx12PipelineLayoutPlan(in RHIPipelineLayoutDescriptor descriptor)
        {
            if ((descriptor.PushConstantSize & 3u) != 0)
            {
                throw new ArgumentException(
                    $"DX12 push constant size {descriptor.PushConstantSize} must be four-byte aligned.",
                    nameof(descriptor));
            }

            PushConstantSize = descriptor.PushConstantSize;
            RHIArgumentTableLayout[] layouts = descriptor.ArgumentTableLayouts ?? Array.Empty<RHIArgumentTableLayout>();
            TablePlans = new Dx12PipelineArgumentTablePlan[layouts.Length];
            m_TablePlanMap = new Dictionary<uint, Dx12PipelineArgumentTablePlan>(layouts.Length);

            int rootCursor = 0;
            for (int tableIndex = 0; tableIndex < layouts.Length; ++tableIndex)
            {
                Dx12ArgumentTableLayout layout = layouts[tableIndex] as Dx12ArgumentTableLayout
                    ?? throw new ArgumentException(
                        $"DX12 pipeline layout table {tableIndex} must be a Dx12ArgumentTableLayout from the same backend.",
                        nameof(descriptor));
                if (m_TablePlanMap.ContainsKey(layout.Index))
                {
                    throw new ArgumentException(
                        $"DX12 pipeline layout contains duplicate argument table space/index {layout.Index}.",
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

                Dx12PipelineArgumentTablePlan tablePlan = new Dx12PipelineArgumentTablePlan(
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

        public Dx12PipelineArgumentTablePlan Resolve(
            in uint tableIndex,
            Dx12ArgumentTableLayout actualLayout)
        {
            if (!m_TablePlanMap.TryGetValue(tableIndex, out Dx12PipelineArgumentTablePlan? tablePlan))
            {
                throw new ArgumentException(
                    $"DX12 pipeline layout does not declare argument table space/index {tableIndex}.",
                    nameof(tableIndex));
            }
            if (!tablePlan.Layout.IsStructurallyCompatibleWith(actualLayout))
            {
                throw new ArgumentException(
                    $"DX12 argument table space/index {tableIndex} is structurally incompatible with the pipeline layout.",
                    nameof(actualLayout));
            }

            return tablePlan;
        }
    }

    internal static class Dx12ArgumentTableBinder
    {
        public static int BindCompute(
            Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList,
            Dx12PipelineLayout pipelineLayout,
            RHIArgumentTable argumentTable,
            in uint tableIndex)
        {
            Dx12ArgumentTable dx12ArgumentTable = ResolveReadyTable(
                pipelineLayout,
                argumentTable,
                tableIndex,
                out Dx12PipelineArgumentTablePlan tablePlan);

            for (int groupIndex = 0; groupIndex < tablePlan.RootParameterIndices.Length; ++groupIndex)
            {
                commandList.SetComputeRootDescriptorTable(
                    tablePlan.RootParameterIndices[groupIndex],
                    dx12ArgumentTable.GetGroupGpuHandle(groupIndex));
            }

            return tablePlan.RootParameterIndices.Length;
        }

        public static int BindGraphics(
            Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList,
            Dx12PipelineLayout pipelineLayout,
            RHIArgumentTable argumentTable,
            in uint tableIndex)
        {
            Dx12ArgumentTable dx12ArgumentTable = ResolveReadyTable(
                pipelineLayout,
                argumentTable,
                tableIndex,
                out Dx12PipelineArgumentTablePlan tablePlan);

            for (int groupIndex = 0; groupIndex < tablePlan.RootParameterIndices.Length; ++groupIndex)
            {
                commandList.SetGraphicsRootDescriptorTable(
                    tablePlan.RootParameterIndices[groupIndex],
                    dx12ArgumentTable.GetGroupGpuHandle(groupIndex));
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

        private static Dx12ArgumentTable ResolveReadyTable(
            Dx12PipelineLayout pipelineLayout,
            RHIArgumentTable argumentTable,
            in uint tableIndex,
            out Dx12PipelineArgumentTablePlan tablePlan)
        {
            Dx12ArgumentTable dx12ArgumentTable = argumentTable as Dx12ArgumentTable
                ?? throw new ArgumentException("DX12 encoder requires a Dx12ArgumentTable from the same backend.", nameof(argumentTable));
            if (pipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(Dx12PipelineLayout));
            }
            if (!ReferenceEquals(pipelineLayout.Device, dx12ArgumentTable.Device))
            {
                throw new ArgumentException(
                    "DX12 encoder cannot bind an argument table allocated from a different DX12 device.",
                    nameof(argumentTable));
            }
            if (dx12ArgumentTable.ArgumentTableLayout.Index != tableIndex)
            {
                throw new ArgumentException(
                    $"DX12 argument table reports space/index {dx12ArgumentTable.ArgumentTableLayout.Index}, but SetArgumentTable requested {tableIndex}.",
                    nameof(tableIndex));
            }

            tablePlan = pipelineLayout.Plan.Resolve(tableIndex, dx12ArgumentTable.ArgumentTableLayout);
            if (dx12ArgumentTable.GroupCount != tablePlan.RootParameterIndices.Length)
            {
                throw new InvalidOperationException(
                    $"DX12 argument table space/index {tableIndex} has {dx12ArgumentTable.GroupCount} descriptor groups, but the pipeline plan expects {tablePlan.RootParameterIndices.Length}.");
            }

            dx12ArgumentTable.EnsureReadyForBinding();
            return dx12ArgumentTable;
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
            RHIArgumentTableLayout[]? argumentTableLayouts = descriptor.ArgumentTableLayouts;
            if (argumentTableLayouts != null)
            {
                for (int index = 0; index < argumentTableLayouts.Length; ++index)
                {
                    Dx12ArgumentTableLayout layout = argumentTableLayouts[index] as Dx12ArgumentTableLayout
                        ?? throw new ArgumentException(
                            $"DX12 pipeline argument table {index} must be a {nameof(Dx12ArgumentTableLayout)}.",
                            nameof(descriptor));
                    if (layout.IsDisposed)
                    {
                        throw new ObjectDisposedException(layout.GetType().FullName);
                    }
                    if (!ReferenceEquals(layout.Device, device))
                    {
                        throw new ArgumentException(
                            $"DX12 pipeline argument table {layout.Index} belongs to a different DX12 device.",
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
                Dx12PipelineArgumentTablePlan tablePlan = Plan.TablePlans[tableIndex];
                Dx12ArgumentTableLayout layout = tablePlan.Layout;
                for (int groupIndex = 0; groupIndex < layout.Groups.Length; ++groupIndex)
                {
                    Dx12ArgumentTableGroupPlan group = layout.Groups[groupIndex];
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
}
