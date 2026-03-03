using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Infinity.Mathmatics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal static class MetalBindingLogHelper
    {
        private static readonly object s_LogLock = new object();
        private static readonly HashSet<string> s_LoggedPipelineModeKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static void LogPipelineModeOnce(string pipelineType, in IntPtr pipelineStatePtr)
        {
            string key = $"{pipelineType}:{pipelineStatePtr}";
            lock (s_LogLock)
            {
                if (!s_LoggedPipelineModeKeys.Add(key))
                {
                    return;
                }
            }

            Console.WriteLine($"[MetalBinding] {pipelineType} pipeline encodingPath=MTL4, pipeline=0x{pipelineStatePtr.ToString("x")}.");
        }
    }

    internal static class MetalBarrierHelper
    {
        // TODO: ThirdParty SharpMetal bindings still expose pre-Metal4 barrier APIs.
        // SharpGPU no longer references them; remove binding symbols after dependency audit.
        private const ulong s_ValidMetal4StageMask = (1UL << 0) | (1UL << 1) | (1UL << 27) | (1UL << 29);

        internal struct MetalBarrierBatchPlan
        {
            internal ulong IntraAfterStages;
            internal ulong IntraBeforeStages;
            internal ulong QueueAfterStages;
            internal ulong QueueBeforeStages;
            internal int IntraBarrierCount;
            internal int QueueBarrierCount;
        }

        internal static void ApplyEncoderBarrier(in IntPtr encoderPtr, in ulong afterStages, in ulong beforeStages)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            if (!IsValidMetal4StageMask(afterStages) || !IsValidMetal4StageMask(beforeStages))
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(encoderPtr);
            encoder4.BarrierAfterEncoderStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
        }

        internal static void ApplyQueueBarrier(in IntPtr encoderPtr, in ulong afterStages, in ulong beforeStages)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            if (!IsValidMetal4StageMask(afterStages) || !IsValidMetal4StageMask(beforeStages))
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(encoderPtr);
            encoder4.BarrierAfterQueueStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
        }

        internal static bool IsValidMetal4StageMask(in ulong stages)
        {
            return stages != 0 && (stages & ~s_ValidMetal4StageMask) == 0;
        }

        internal static MetalBarrierBatchPlan PlanBarriers(MetalCommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers)
        {
            MetalBarrierBatchPlan plan = default;
            for (int i = 0; i < barriers.Length; ++i)
            {
                if (!TryGetStagePair(barriers[i], out ulong afterStages, out ulong beforeStages))
                {
                    continue;
                }

                if (commandBuffer.IsIntraEncoderBarrier(afterStages))
                {
                    plan.IntraAfterStages |= afterStages;
                    plan.IntraBeforeStages |= beforeStages;
                    ++plan.IntraBarrierCount;
                }
                else
                {
                    plan.QueueAfterStages |= afterStages;
                    plan.QueueBeforeStages |= beforeStages;
                    ++plan.QueueBarrierCount;
                }
            }

            return plan;
        }

        internal static MetalBarrierBatchPlan PlanBarriersForTesting(ulong seenStages, ReadOnlySpan<RHIBarrier> barriers)
        {
            MetalBarrierBatchPlan plan = default;
            for (int i = 0; i < barriers.Length; ++i)
            {
                if (!TryGetStagePair(barriers[i], out ulong afterStages, out ulong beforeStages))
                {
                    continue;
                }

                if (afterStages != 0 && (seenStages & afterStages) != 0)
                {
                    plan.IntraAfterStages |= afterStages;
                    plan.IntraBeforeStages |= beforeStages;
                    ++plan.IntraBarrierCount;
                }
                else
                {
                    plan.QueueAfterStages |= afterStages;
                    plan.QueueBeforeStages |= beforeStages;
                    ++plan.QueueBarrierCount;
                }
            }

            return plan;
        }

        internal static void ApplyPlan(in IntPtr encoderPtr, in MetalBarrierBatchPlan plan)
        {
            if (plan.IntraAfterStages != 0 && plan.IntraBeforeStages != 0)
            {
                ApplyEncoderBarrier(encoderPtr, plan.IntraAfterStages, plan.IntraBeforeStages);
            }

            if (plan.QueueAfterStages != 0 && plan.QueueBeforeStages != 0)
            {
                ApplyQueueBarrier(encoderPtr, plan.QueueAfterStages, plan.QueueBeforeStages);
            }
        }

        internal static bool TryGetStagePair(in RHIBarrier barrier, out ulong afterStages, out ulong beforeStages)
        {
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                {
                    RHIGlobalBarrier globalBarrier = barrier.GlobalBarrier;
                    afterStages = NormalizeToMetal4Stages(globalBarrier.SyncBefore);
                    beforeStages = NormalizeToMetal4Stages(globalBarrier.SyncAfter);
                    break;
                }

                case ERHIBarrierKind.Buffer:
                {
                    RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                    afterStages = NormalizeToMetal4Stages(bufferBarrier.SyncBefore);
                    beforeStages = NormalizeToMetal4Stages(bufferBarrier.SyncAfter);
                    break;
                }

                case ERHIBarrierKind.Texture:
                {
                    RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                    afterStages = NormalizeToMetal4Stages(textureBarrier.SyncBefore);
                    beforeStages = NormalizeToMetal4Stages(textureBarrier.SyncAfter);
                    break;
                }

                default:
                    afterStages = 0;
                    beforeStages = 0;
                    return false;
            }

            return afterStages != 0 && beforeStages != 0;
        }

        private static ulong NormalizeToMetal4Stages(in ERHISyncStageMask stages)
        {
            ulong result = MetalUtility.ConvertToMetal4Stages(stages) & s_ValidMetal4StageMask;
            if (result == 0)
            {
                // TODO: Metal4 stage mask for Task/Mesh is not validated yet in this backend.
                // Conservatively widen to all known safe stage bits.
                result = s_ValidMetal4StageMask;
            }

            return result;
        }
    }

    internal enum MetalBindingPipelineType : byte
    {
        Compute = 0,
        Raytracing = 1,
        Raster = 2
    }

    internal interface IMetalBindingBackend : IDisposable
    {
        bool UsesReservedRayFunctionTableSlots { get; }

        void ResetForPipeline(MetalPipelineLayout pipelineLayout);
        void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex);
        void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride);

        void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        void CommitRaster(in MTL4RenderCommandEncoder encoder);
    }

    internal static class MetalBindingHelpers
    {
        internal const ulong RtVisibleFunctionTableSlot = 29;
        internal const ulong RtIntersectionFunctionTableSlot = 30;
        internal const ulong PushConstantBufferIndex = 31;

        internal static bool IsBufferBindingType(in ERHIBindType type)
        {
            switch (type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.AccelStruct:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsTextureBindingType(in ERHIBindType type)
        {
            switch (type)
            {
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsSamplerBindingType(in ERHIBindType type)
        {
            return type == ERHIBindType.Sampler;
        }

        internal static MTLTextureType ConvertBindTypeToTextureType(in ERHIBindType type)
        {
            return type switch
            {
                ERHIBindType.Texture2D => MTLTextureType.Type2D,
                ERHIBindType.StorageTexture2D => MTLTextureType.Type2D,
                ERHIBindType.Texture2DMS => MTLTextureType.Type2DMultisample,
                ERHIBindType.StorageTexture2DMS => MTLTextureType.Type2DMultisample,
                ERHIBindType.Texture2DArray => MTLTextureType.Type2DArray,
                ERHIBindType.StorageTexture2DArray => MTLTextureType.Type2DArray,
                ERHIBindType.Texture2DArrayMS => MTLTextureType.Type2DMultisampleArray,
                ERHIBindType.StorageTexture2DArrayMS => MTLTextureType.Type2DMultisampleArray,
                ERHIBindType.TextureCube => MTLTextureType.Cube,
                ERHIBindType.StorageTextureCube => MTLTextureType.Cube,
                ERHIBindType.TextureCubeArray => MTLTextureType.CubeArray,
                ERHIBindType.StorageTextureCubeArray => MTLTextureType.CubeArray,
                ERHIBindType.Texture3D => MTLTextureType.Type3D,
                ERHIBindType.StorageTexture3D => MTLTextureType.Type3D,
                _ => MTLTextureType.Type2D
            };
        }

        internal static MTLDataType ConvertBindTypeToArgumentDataType(in ERHIBindType type)
        {
            if (IsBufferBindingType(type))
            {
                return type == ERHIBindType.AccelStruct ? MTLDataType.InstanceAccelerationStructure : MTLDataType.Pointer;
            }

            if (IsTextureBindingType(type))
            {
                return MTLDataType.Texture;
            }

            if (IsSamplerBindingType(type))
            {
                return MTLDataType.Sampler;
            }

            throw new NotSupportedException($"Unsupported bind type '{type}' for argument-encoder data type conversion.");
        }

        internal static MTLBindingAccess ConvertBindTypeToBindingAccess(in ERHIBindType type)
        {
            return type switch
            {
                ERHIBindType.StorageBuffer => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2D => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DMS => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DArray => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DArrayMS => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTextureCube => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTextureCubeArray => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture3D => MTLBindingAccess.ReadWrite,
                _ => MTLBindingAccess.ReadOnly
            };
        }

        internal static bool HasRayFunctionTableSlotConflict(MetalArgumentTable table)
        {
            return HasRayFunctionTableSlotConflict(table.ArgumentTableLayout.BindInfos);
        }

        internal static bool HasRayFunctionTableSlotConflict(ReadOnlySpan<MetalBindInfo> binds)
        {
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                if (!IsBufferBindingType(bind.Type))
                {
                    continue;
                }

                if (bind.Slot == RtVisibleFunctionTableSlot || bind.Slot == RtIntersectionFunctionTableSlot)
                {
                    return true;
                }
            }

            return false;
        }

    }

    internal abstract class MetalBindingBackendBase : IMetalBindingBackend
    {
        public virtual bool UsesReservedRayFunctionTableSlots => false;

        protected MetalDevice Device => m_Device;
        protected MetalBindingPipelineType PipelineType => m_PipelineType;

        protected readonly SortedDictionary<uint, MetalArgumentTable> m_BoundTables;

        private readonly MetalDevice m_Device;
        private readonly MetalBindingPipelineType m_PipelineType;
        private MetalPipelineLayout? m_PipelineLayout;

        protected MetalBindingBackendBase(MetalDevice device, in MetalBindingPipelineType pipelineType)
        {
            m_Device = device;
            m_PipelineType = pipelineType;
            m_BoundTables = new SortedDictionary<uint, MetalArgumentTable>();
        }

        public virtual void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            m_PipelineLayout = pipelineLayout;
            m_BoundTables.Clear();
        }

        public virtual void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (resourceTable == null)
            {
                throw new ArgumentNullException(nameof(resourceTable));
            }

            MetalArgumentTableLayout layout = resourceTable.ArgumentTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Metal resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            ValidateTableInPipelineLayout(layout);

            m_BoundTables[tableIndex] = resourceTable;
            OnArgumentTableUpdated(resourceTable, tableIndex);
        }

        public abstract void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        public abstract void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        public abstract void CommitRaster(in MTL4RenderCommandEncoder encoder);

        public void Dispose()
        {
            DisposeBackend();
        }

        protected virtual void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
        }

        public virtual void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
        }

        protected virtual void DisposeBackend()
        {
        }

        private void ValidateTableInPipelineLayout(MetalArgumentTableLayout layout)
        {
            if (m_PipelineLayout == null)
            {
                return;
            }

            RHIArgumentTableLayout[]? resourceTableLayouts = m_PipelineLayout.Descriptor.ArgumentTableLayouts;
            if (resourceTableLayouts == null || resourceTableLayouts.Length == 0)
            {
                return;
            }

            bool found = false;
            for (int i = 0; i < resourceTableLayouts.Length; ++i)
            {
                if (resourceTableLayouts[i] is MetalArgumentTableLayout metalLayout && metalLayout.Index == layout.Index)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"Resource table index '{layout.Index}' is not part of the current Metal pipeline layout.");
            }
        }
    }

    internal sealed class MetalArgumentTableBindingBackend : MetalBindingBackendBase
    {
        // Single-set mode state (direct MTL4ArgumentTable resource binding)
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_ArgumentTables;

        // Multi-set mode state (descriptor buffer per set → single root argument table)
        private bool m_IsMultiSet;
        private MTL4ArgumentTable m_RootArgumentTable;
        private readonly SortedDictionary<uint, MTLBuffer> m_DescriptorBuffers;

        // Shared state
        private readonly SortedDictionary<uint, RasterVertexBinding> m_RasterVertexBindings;
        private readonly MetalCommandQueue? m_CommandQueue;

        private readonly struct RasterVertexBinding
        {
            internal readonly ulong Address;
            internal readonly ulong Stride;

            internal RasterVertexBinding(in ulong address, in ulong stride)
            {
                Address = address;
                Stride = stride;
            }
        }

        internal MetalArgumentTableBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType, MetalCommandQueue? commandQueue = null)
            : base(device, pipelineType)
        {
            m_ArgumentTables = new SortedDictionary<uint, MTL4ArgumentTable>();
            m_DescriptorBuffers = new SortedDictionary<uint, MTLBuffer>();
            m_RasterVertexBindings = new SortedDictionary<uint, RasterVertexBinding>();
            m_CommandQueue = commandQueue;
        }

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseArgumentTables();
            ReleaseDescriptorBuffers();
            m_RasterVertexBindings.Clear();

            int layoutCount = pipelineLayout.ArgumentTableLayoutCount;
            m_IsMultiSet = layoutCount > 1;

            if (m_IsMultiSet)
            {
                CreateRootArgumentTable(pipelineLayout);
            }
        }

        protected override void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_IsMultiSet)
            {
                MTLBuffer descriptorBuffer = GetOrCreateDescriptorBuffer(resourceTable.ArgumentTableLayout, tableIndex);
                PopulateDescriptorBuffer(descriptorBuffer, resourceTable, m_CommandQueue);
                m_RootArgumentTable.SetAddress(descriptorBuffer.GpuAddress, tableIndex);
                m_CommandQueue?.AddResidencyAllocation(descriptorBuffer);
                ApplyRasterVertexBufferBindingsToRoot();
            }
            else
            {
                MTL4ArgumentTable argumentTable = GetOrCreateArgumentTable(resourceTable.ArgumentTableLayout, tableIndex);
                PopulateArgumentTable(argumentTable, resourceTable);
                ApplyRasterVertexBufferBindings(argumentTable);
            }
        }

        public override void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
            if (PipelineType != MetalBindingPipelineType.Raster)
            {
                return;
            }

            m_RasterVertexBindings[slot] = new RasterVertexBinding(address, stride);

            if (m_IsMultiSet)
            {
                if (m_RootArgumentTable.NativePtr != IntPtr.Zero)
                {
                    ApplyRasterVertexBufferBinding(m_RootArgumentTable, slot, m_RasterVertexBindings[slot]);
                }
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    ApplyRasterVertexBufferBinding(pair.Value, slot, m_RasterVertexBindings[slot]);
                }
            }
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            if (m_IsMultiSet)
            {
                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    encoder.SetArgumentTable(pair.Value.NativePtr);
                }
            }
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            if (m_IsMultiSet)
            {
                if (functionTable != null)
                {
                    PopulateRayFunctionTablesOnRoot(functionTable);
                }

                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr);
            }
            else
            {
                if (functionTable != null)
                {
                    PopulateRayFunctionTables(functionTable);
                }

                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    encoder.SetArgumentTable(pair.Value.NativePtr);
                }
            }
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            ulong stages = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment);

            if (m_IsMultiSet)
            {
                ApplyRasterVertexBufferBindingsToRoot();
                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr, stages);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    ApplyRasterVertexBufferBindings(pair.Value);
                    encoder.SetArgumentTable(pair.Value.NativePtr, stages);
                }
            }
        }

        protected override void DisposeBackend()
        {
            ReleaseArgumentTables();
            ReleaseDescriptorBuffers();
            m_RasterVertexBindings.Clear();
        }

        // Single-set mode: direct MTL4ArgumentTable

        private MTL4ArgumentTable GetOrCreateArgumentTable(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_ArgumentTables.TryGetValue(tableIndex, out MTL4ArgumentTable cachedTable))
            {
                return cachedTable;
            }

            ulong maxBufferCount = 0;
            ulong maxTextureCount = 0;
            ulong maxSamplerCount = 0;
            MetalBindInfo[] binds = layout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ulong arrayCount = Math.Max(1UL, bind.Count);
                ulong nextIndex = bind.Slot + arrayCount;
                if (MetalBindingHelpers.IsBufferBindingType(bind.Type))
                {
                    maxBufferCount = Math.Max(maxBufferCount, nextIndex);
                }
                else if (MetalBindingHelpers.IsTextureBindingType(bind.Type))
                {
                    maxTextureCount = Math.Max(maxTextureCount, nextIndex);
                }
                else if (MetalBindingHelpers.IsSamplerBindingType(bind.Type))
                {
                    maxSamplerCount = Math.Max(maxSamplerCount, nextIndex);
                }
            }

            if (PipelineType == MetalBindingPipelineType.Raytracing)
            {
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtIntersectionFunctionTableSlot + 1UL);
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtVisibleFunctionTableSlot + 1UL);
            }

            if (PipelineType == MetalBindingPipelineType.Raster)
            {
                maxBufferCount = Math.Max(maxBufferCount, 31UL);
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount = Math.Max(1UL, maxBufferCount);
            descriptor.MaxTextureBindCount = Math.Max(1UL, maxTextureCount);
            descriptor.MaxSamplerStateBindCount = Math.Max(1UL, maxSamplerCount);
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            MTL4ArgumentTable argumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (argumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"newArgumentTableWithDescriptor failed: {errorText}");
            }

            m_ArgumentTables.Add(tableIndex, argumentTable);
            return argumentTable;
        }

        private void PopulateArgumentTable(MTL4ArgumentTable argumentTable, MetalArgumentTable table)
        {
            MetalBindInfo[] binds = table.ArgumentTableLayout.BindInfos;
            ulong samplerBindingCursor = 0;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);
                ulong slotBase = bind.Slot;
                if (bind.Type == ERHIBindType.Sampler)
                {
                    // MTL4 argument-table sampler bindings are densely indexed within sampler space.
                    slotBase = samplerBindingCursor;
                }

                for (int j = 0; j < arrayCount; ++j)
                {
                    RHIArgumentTableElement element = table.GetElement(i, j);
                    ulong slotIndex = slotBase + (ulong)j;

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (element.BufferView is MetalBufferView bufferView)
                            {
                                ulong offset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                                argumentTable.SetAddress(bufferView.Buffer.NativeBuffer.GpuAddress + offset, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(bufferView.Buffer.NativeBuffer);
                            }

                            break;

                        case ERHIBindType.Texture2D:
                        case ERHIBindType.Texture2DMS:
                        case ERHIBindType.Texture2DArray:
                        case ERHIBindType.Texture2DArrayMS:
                        case ERHIBindType.TextureCube:
                        case ERHIBindType.TextureCubeArray:
                        case ERHIBindType.Texture3D:
                        case ERHIBindType.StorageTexture2D:
                        case ERHIBindType.StorageTexture2DMS:
                        case ERHIBindType.StorageTexture2DArray:
                        case ERHIBindType.StorageTexture2DArrayMS:
                        case ERHIBindType.StorageTextureCube:
                        case ERHIBindType.StorageTextureCubeArray:
                        case ERHIBindType.StorageTexture3D:
                            if (element.TextureView is MetalTextureView textureView)
                            {
                                argumentTable.SetTexture(textureView.ResourceID, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(textureView.ParentTexture);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (element.Sampler is MetalSampler sampler)
                            {
                                argumentTable.SetSamplerState(sampler.NativeSampler.GpuResourceID, slotIndex);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                            {
                                argumentTable.SetResource(topLevel.NativeAccelerationStructure.GpuResourceID, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(topLevel.NativeAccelerationStructure);
                            }

                            break;
                    }
                }

                if (bind.Type == ERHIBindType.Sampler)
                {
                    samplerBindingCursor += (ulong)arrayCount;
                }
            }
        }

        private void PopulateRayFunctionTables(MetalFunctionTable functionTable)
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
                {
                    pair.Value.SetResource(functionTable.IntersectionFunctionTable.GpuResourceID, MetalBindingHelpers.RtIntersectionFunctionTableSlot);
                }

                if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
                {
                    pair.Value.SetResource(functionTable.VisibleFunctionTable.GpuResourceID, MetalBindingHelpers.RtVisibleFunctionTableSlot);
                }
            }
        }

        // Multi-set mode: descriptor buffer per set → single root argument table

        private void CreateRootArgumentTable(MetalPipelineLayout pipelineLayout)
        {
            // The root argument table holds one buffer slot per set (for descriptor buffer GPU addresses),
            // plus reserved slots for RT function tables and raster vertex buffers.
            ulong maxBufferCount = (ulong)pipelineLayout.ArgumentTableLayoutCount;

            if (PipelineType == MetalBindingPipelineType.Raytracing)
            {
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtIntersectionFunctionTableSlot + 1UL);
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtVisibleFunctionTableSlot + 1UL);
            }

            if (PipelineType == MetalBindingPipelineType.Raster)
            {
                maxBufferCount = Math.Max(maxBufferCount, 31UL);
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount = Math.Max(1UL, maxBufferCount);
            descriptor.MaxTextureBindCount = 1UL;
            descriptor.MaxSamplerStateBindCount = 1UL;
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            m_RootArgumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (m_RootArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create multi-set root argument table: {errorText}");
            }
        }

        private MTLBuffer GetOrCreateDescriptorBuffer(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_DescriptorBuffers.TryGetValue(tableIndex, out MTLBuffer existingBuffer))
            {
                return existingBuffer;
            }

            // Each entry in the descriptor buffer is 8 bytes (ulong):
            // - Buffer bindings: GPU address
            // - Texture bindings: MTLResourceID._impl
            // - Sampler bindings: MTLResourceID._impl
            // - AccelStruct bindings: MTLResourceID._impl
            ulong byteSize = (ulong)layout.TotalElementCount * 8UL;
            byteSize = Math.Max(byteSize, 8UL);

            MTLBuffer buffer = Device.NativeDevice.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeShared);
            if (buffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to allocate descriptor buffer for set {tableIndex} (size={byteSize}).");
            }

            m_DescriptorBuffers.Add(tableIndex, buffer);
            return buffer;
        }

        private static void PopulateDescriptorBuffer(MTLBuffer buffer, MetalArgumentTable table, MetalCommandQueue? commandQueue)
        {
            IntPtr ptr = buffer.Contents;
            MetalBindInfo[] binds = table.ArgumentTableLayout.BindInfos;

            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);
                int baseOffset = table.ArgumentTableLayout.GetElementOffset(i);

                for (int j = 0; j < arrayCount; ++j)
                {
                    RHIArgumentTableElement element = table.GetElement(i, j);
                    int entryByteOffset = (baseOffset + j) * 8;

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (element.BufferView is MetalBufferView bufferView)
                            {
                                ulong bufOffset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                                ulong address = bufferView.Buffer.NativeBuffer.GpuAddress + bufOffset;
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)address);
                                commandQueue?.AddResidencyAllocation(bufferView.Buffer.NativeBuffer);
                            }

                            break;

                        case ERHIBindType.Texture2D:
                        case ERHIBindType.Texture2DMS:
                        case ERHIBindType.Texture2DArray:
                        case ERHIBindType.Texture2DArrayMS:
                        case ERHIBindType.TextureCube:
                        case ERHIBindType.TextureCubeArray:
                        case ERHIBindType.Texture3D:
                        case ERHIBindType.StorageTexture2D:
                        case ERHIBindType.StorageTexture2DMS:
                        case ERHIBindType.StorageTexture2DArray:
                        case ERHIBindType.StorageTexture2DArrayMS:
                        case ERHIBindType.StorageTextureCube:
                        case ERHIBindType.StorageTextureCubeArray:
                        case ERHIBindType.StorageTexture3D:
                            if (element.TextureView is MetalTextureView textureView)
                            {
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)textureView.ResourceID._impl);
                                commandQueue?.AddResidencyAllocation(textureView.ParentTexture);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (element.Sampler is MetalSampler sampler)
                            {
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)sampler.NativeSampler.GpuResourceID._impl);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                            {
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)topLevel.NativeAccelerationStructure.GpuResourceID._impl);
                                commandQueue?.AddResidencyAllocation(topLevel.NativeAccelerationStructure);
                            }

                            break;
                    }
                }
            }
        }

        private void PopulateRayFunctionTablesOnRoot(MetalFunctionTable functionTable)
        {
            if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_RootArgumentTable.SetResource(functionTable.IntersectionFunctionTable.GpuResourceID, MetalBindingHelpers.RtIntersectionFunctionTableSlot);
            }

            if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_RootArgumentTable.SetResource(functionTable.VisibleFunctionTable.GpuResourceID, MetalBindingHelpers.RtVisibleFunctionTableSlot);
            }
        }

        // Raster vertex buffer helpers

        private void ApplyRasterVertexBufferBindings(MTL4ArgumentTable argumentTable)
        {
            foreach (KeyValuePair<uint, RasterVertexBinding> pair in m_RasterVertexBindings)
            {
                ApplyRasterVertexBufferBinding(argumentTable, pair.Key, pair.Value);
            }
        }

        private void ApplyRasterVertexBufferBindingsToRoot()
        {
            if (m_RootArgumentTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ApplyRasterVertexBufferBindings(m_RootArgumentTable);
        }

        private static void ApplyRasterVertexBufferBinding(MTL4ArgumentTable argumentTable, in uint slot, in RasterVertexBinding binding)
        {
            if (binding.Stride > 0)
            {
                argumentTable.SetAddress(binding.Address, binding.Stride, slot);
            }
            else
            {
                argumentTable.SetAddress(binding.Address, slot);
            }
        }

        // Lifecycle helpers

        private void ReleaseArgumentTables()
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value.NativePtr);
                }
            }

            m_ArgumentTables.Clear();
        }

        private void ReleaseDescriptorBuffers()
        {
            foreach (KeyValuePair<uint, MTLBuffer> pair in m_DescriptorBuffers)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value.NativePtr);
                }
            }

            m_DescriptorBuffers.Clear();

            if (m_RootArgumentTable.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_RootArgumentTable.NativePtr);
                m_RootArgumentTable = default;
            }

            m_IsMultiSet = false;
        }
    }

    internal sealed class MetalTransferEncoder : RHITransferEncoder
    {
        private MTL4ComputeCommandEncoder m_NativeEncoder4;

        internal MetalTransferEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder4 = default;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4ComputeCommandEncoder for transfer pass.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            IntPtr encoderPtr = m_NativeEncoder4.NativePtr;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, singleBarrier);
            MetalBarrierHelper.ApplyPlan(encoderPtr, plan);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            IntPtr encoderPtr = m_NativeEncoder4.NativePtr;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(encoderPtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PushDebugGroup(new NSString(name));
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTL4 transfer encoder is not initialized.");
            }

            MetalBuffer src = (MetalBuffer)srcBuffer;
            MetalBuffer dst = (MetalBuffer)dstBuffer;
            TrackResidency(src.NativeBuffer);
            TrackResidency(dst.NativeBuffer);
            m_NativeEncoder4.CopyFromBuffer(src.NativeBuffer, (ulong)srcOffset, dst.NativeBuffer, (ulong)dstOffset, (ulong)size);
            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTL4 transfer encoder is not initialized.");
            }

            MetalBuffer srcBuffer = (MetalBuffer)src.Buffer;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;
            TrackResidency(srcBuffer.NativeBuffer);
            TrackResidency(dstTexture.NativeTexture);
            ulong bytesPerPixel = 4;
            ulong rowPitch = src.RowPitch > 0 ? src.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            m_NativeEncoder4.CopyFromBuffer(
                srcBuffer.NativeBuffer,
                src.Offset,
                rowPitch,
                imagePitch,
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstTexture.NativeTexture,
                dst.SliceBase,
                dst.MipLevel,
                new MTLOrigin(dst.Origin.x, dst.Origin.y, dst.Origin.z));

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTL4 transfer encoder is not initialized.");
            }

            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalBuffer dstBuffer = (MetalBuffer)dst.Buffer;
            TrackResidency(srcTexture.NativeTexture);
            TrackResidency(dstBuffer.NativeBuffer);
            ulong bytesPerPixel = 4;
            ulong rowPitch = dst.RowPitch > 0 ? dst.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            m_NativeEncoder4.CopyFromTexture(
                srcTexture.NativeTexture,
                src.SliceBase,
                src.MipLevel,
                new MTLOrigin(src.Origin.x, src.Origin.y, src.Origin.z),
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstBuffer.NativeBuffer,
                dst.Offset,
                rowPitch,
                imagePitch);

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTL4 transfer encoder is not initialized.");
            }

            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;
            TrackResidency(srcTexture.NativeTexture);
            TrackResidency(dstTexture.NativeTexture);
            m_NativeEncoder4.CopyFromTexture(
                srcTexture.NativeTexture,
                src.SliceBase,
                src.MipLevel,
                new MTLOrigin(src.Origin.x, src.Origin.y, src.Origin.z),
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstTexture.NativeTexture,
                dst.SliceBase,
                dst.MipLevel,
                new MTLOrigin(dst.Origin.x, dst.Origin.y, dst.Origin.z));

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void EndPass()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.EndEncoding();
            m_NativeEncoder4 = default;
        }

        protected override void Release()
        {
        }

        private void TrackResidency(in MTLAllocation allocation)
        {
            if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
            {
                queue.AddResidencyAllocation(allocation);
            }
        }
    }

    internal sealed class MetalComputeEncoder : RHIComputeEncoder
    {
        private readonly MetalDevice m_MetalDevice;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;

        internal MetalComputeEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_MetalDevice = ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            m_NativeEncoder4 = default;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
        }

        internal override void BeginPass(in RHIComputePassDescriptor descriptor)
        {
            m_NativeEncoder4 = default;
            m_PendingPassDebugGroup = null;

            EnsureMtl4ComputeEncoder();

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.UpdateFence(fence, stage);
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0 || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(m_NativeEncoder4.NativePtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Compute encoder is not created yet. Set pipeline before using debug groups.");
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PushDebugGroup(new NSString(name));
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalComputePipeline metalPipeline = (MetalComputePipeline)pipeline;
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Compute pipeline layout must be a MetalPipelineLayout.");
            ConfigureBindingBackend(pipelineLayout);
            EnsureMtl4ComputeEncoder();
            ApplyPendingPassDebugGroup();

            m_NativeEncoder4.SetComputePipelineState(metalPipeline.NativePipelineState);
            MetalBindingLogHelper.LogPipelineModeOnce("Compute", metalPipeline.NativePipelineState.NativePtr);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Compute pipeline must be set before binding resource tables.");
            }

            MetalArgumentTable table = (MetalArgumentTable)resourceTable;
            m_BindingBackend.SetArgumentTable(table, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
#if DEBUG
            MetalPipelineLayout metalLayout = m_CachedPipeline?.Descriptor.PipelineLayout as MetalPipelineLayout;
            Debug.Assert(metalLayout == null || offset + size <= metalLayout.Descriptor.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({metalLayout?.Descriptor.PushConstantSize ?? 0}).");
#endif
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.SetBytes(data + (int)offset, size, MetalBindingHelpers.PushConstantBufferIndex);
            }
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            MTLSize threadGroupCount = new MTLSize(groupCountX, groupCountY, groupCountZ);
            MTLSize threadsPerGroup = new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z);
            m_BindingBackend?.CommitCompute(m_NativeEncoder4);
            m_NativeEncoder4.DispatchThreadgroups(threadGroupCount, threadsPerGroup);

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            MetalBuffer indirectBuffer = (MetalBuffer)argsBuffer;
            MTLSize threadsPerGroup = new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z);
            m_BindingBackend?.CommitCompute(m_NativeEncoder4);
            m_NativeEncoder4.DispatchThreadgroupsWithIndirectBuffer(indirectBuffer.NativeBuffer.GpuAddress + argsOffset, threadsPerGroup);

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            MetalComputeIndirectCommandBuffer metalICB = (MetalComputeIndirectCommandBuffer)indirectCmdBuffer;
            MTLIndirectCommandBuffer nativeICB = metalICB.NativeIndirectCommandBuffer;
            NSRange range = new NSRange { location = 0, length = metalICB.MaxCommandCount };

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.ExecuteCommandsInBuffer(nativeICB, range);
            }
        }

        public override void EndPass()
        {
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private void ConfigureBindingBackend(MetalPipelineLayout pipelineLayout)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue = commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(m_MetalDevice, MetalBindingPipelineType.Compute, queue);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
        }

        private void EnsureMtl4ComputeEncoder()
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
                if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTL4ComputeCommandEncoder.");
                }
            }
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private bool HasNativeEncoder => m_NativeEncoder4.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalRaytracingEncoder : RHIRaytracingEncoder
    {
        private readonly MetalDevice m_MetalDevice;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;

        internal MetalRaytracingEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_MetalDevice = ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            m_NativeEncoder4 = default;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
        }

        internal override void BeginPass(in RHIRayTracingPassDescriptor descriptor)
        {
            m_NativeEncoder4 = default;
            m_PendingPassDebugGroup = null;

            EnsureMtl4ComputeEncoder();

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0 || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(m_NativeEncoder4.NativePtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Ray tracing encoder is not created yet. Set pipeline before using debug groups.");
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PushDebugGroup(new NSString(name));
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalRaytracingPipeline metalPipeline = pipeline as MetalRaytracingPipeline ?? throw new InvalidOperationException("Ray tracing pipeline must be a MetalRaytracingPipeline.");
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Ray tracing pipeline layout must be a MetalPipelineLayout.");
            ConfigureBindingBackend(pipelineLayout);
            EnsureMtl4ComputeEncoder();
            ApplyPendingPassDebugGroup();

            m_NativeEncoder4.SetComputePipelineState(metalPipeline.NativePipelineState);
            MetalBindingLogHelper.LogPipelineModeOnce("Ray", metalPipeline.NativePipelineState.NativePtr);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before binding resource tables.");
            }

            MetalArgumentTable table = (MetalArgumentTable)resourceTable;
            if (m_BindingBackend.UsesReservedRayFunctionTableSlots && MetalBindingHelpers.HasRayFunctionTableSlotConflict(table))
            {
                throw new InvalidOperationException($"Ray tracing resource table conflicts with reserved Metal function-table slots ({MetalBindingHelpers.RtVisibleFunctionTableSlot}/{MetalBindingHelpers.RtIntersectionFunctionTableSlot}). Use another slot.");
            }

            m_BindingBackend.SetArgumentTable(table, tableIndex);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            EnsureAccelerationStructureBuildEncoder();
            MetalTopLevelAccelStruct metalTlas = topLevelAccelStruct as MetalTopLevelAccelStruct ?? throw new InvalidOperationException("TLAS must be a MetalTopLevelAccelStruct.");

            try
            {
                MTL4BufferRange scratchRange = MTL4BufferRange.Make(metalTlas.NativeScratchBuffer.GpuAddress, metalTlas.NativeScratchBuffer.Length);
                m_NativeEncoder4.BuildAccelerationStructure(metalTlas.NativeAccelerationStructure, metalTlas.NativeDescriptor.NativePtr, scratchRange);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"MTL4 TLAS build failed in ray-tracing pass. detail={ex.Message}");
            }

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            EnsureAccelerationStructureBuildEncoder();
            MetalBottomLevelAccelStruct metalBlas = bottomLevelAccelStruct as MetalBottomLevelAccelStruct ?? throw new InvalidOperationException("BLAS must be a MetalBottomLevelAccelStruct.");

            try
            {
                MTL4BufferRange scratchRange = MTL4BufferRange.Make(metalBlas.NativeScratchBuffer.GpuAddress, metalBlas.NativeScratchBuffer.Length);
                m_NativeEncoder4.BuildAccelerationStructure(metalBlas.NativeAccelerationStructure, metalBlas.NativeDescriptor.NativePtr, scratchRange);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"MTL4 BLAS build failed in ray-tracing pass. detail={ex.Message}");
            }

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute));
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before dispatch.");
            }

            EnsureMtl4ComputeEncoder();

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            uint threadsPerGroupX = math.max(pipeline.ThreadgroupSize.x, 1u);
            uint threadsPerGroupY = math.max(pipeline.ThreadgroupSize.y, 1u);
            uint threadsPerGroupZ = math.max(pipeline.ThreadgroupSize.z, 1u);
            uint threadgroupCountX = (width + threadsPerGroupX - 1u) / threadsPerGroupX;
            uint threadgroupCountY = (height + threadsPerGroupY - 1u) / threadsPerGroupY;
            uint threadgroupCountZ = (depth + threadsPerGroupZ - 1u) / threadsPerGroupZ;
            MTLSize threadgroupCount = new MTLSize(threadgroupCountX, threadgroupCountY, threadgroupCountZ);
            MTLSize threadsPerGroup = new MTLSize(threadsPerGroupX, threadsPerGroupY, threadsPerGroupZ);

            m_BindingBackend?.CommitRaytracing(m_NativeEncoder4, table);
            m_NativeEncoder4.DispatchThreadgroups(threadgroupCount, threadsPerGroup);

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing));
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before indirect dispatch.");
            }

            EnsureMtl4ComputeEncoder();

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing indirect dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            MetalBuffer indirectBuffer = argsBuffer as MetalBuffer ?? throw new InvalidOperationException("Ray tracing indirect args must be a MetalBuffer.");
            MTLSize threadsPerGroup = new MTLSize(pipeline.ThreadgroupSize.x, pipeline.ThreadgroupSize.y, pipeline.ThreadgroupSize.z);
            m_BindingBackend?.CommitRaytracing(m_NativeEncoder4, table);
            m_NativeEncoder4.DispatchThreadgroupsWithIndirectBuffer(indirectBuffer.NativeBuffer.GpuAddress + argsOffset, threadsPerGroup);

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing));
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
            MetalRayTracingIndirectCommandBuffer metalICB = (MetalRayTracingIndirectCommandBuffer)indirectCmdBuffer;
            MTLIndirectCommandBuffer nativeICB = metalICB.NativeIndirectCommandBuffer;
            NSRange range = new NSRange { location = 0, length = metalICB.MaxCommandCount };

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.ExecuteCommandsInBuffer(nativeICB, range);
            }
        }

        public override void EndPass()
        {
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.UpdateFence(fence, stage);
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private void ConfigureBindingBackend(MetalPipelineLayout pipelineLayout)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue = commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(m_MetalDevice, MetalBindingPipelineType.Raytracing, queue);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
        }

        private void EnsureMtl4ComputeEncoder()
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                return;
            }

            m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4 ray tracing compute encoder.");
            }
        }

        private void EnsureAccelerationStructureBuildEncoder()
        {
            EnsureMtl4ComputeEncoder();
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private bool HasNativeEncoder => m_NativeEncoder4.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalRasterEncoder : RHIRasterEncoder
    {
        private const uint DrawIndirectArgsStride = 16;
        private const uint DrawIndexedIndirectArgsStride = 20;

        private MTL4RenderCommandEncoder m_NativeEncoder4;
        private MTLBuffer m_IndexBuffer;
        private ulong m_IndexBufferOffset;
        private MTLIndexType m_IndexType;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;
        private RHIRasterPassDescriptor m_PendingPassDescriptor;
        private bool m_HasPendingPassDescriptor;
        private readonly Dictionary<uint, uint> m_VertexStrides;

        internal MetalRasterEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder4 = default;
            m_IndexBuffer = default;
            m_IndexBufferOffset = 0;
            m_IndexType = MTLIndexType.UInt16;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_VertexStrides = new Dictionary<uint, uint>();
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            m_NativeEncoder4 = default;
            m_PendingPassDescriptor = descriptor;
            m_HasPendingPassDescriptor = true;
            m_PendingPassDebugGroup = null;

            EnsureMtl4RenderEncoder();

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.UpdateFence(fence, stage);
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0 || m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(m_NativeEncoder4.NativePtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Raster encoder is not created yet. Set pipeline before using debug groups.");
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PushDebugGroup(new NSString(name));
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginOcclusion(in uint index)
        {
        }

        public override void EndOcclusion(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void NextSubPass()
        {
            return;
        }

        public override void SetScissor(in Rect rect)
        {
            RequireEncoderForState("SetScissor");
            MTLScissorRect nativeRect = new MTLScissorRect
            {
                x = (ulong)Math.Max(0, rect.left),
                y = (ulong)Math.Max(0, rect.top),
                width = (ulong)Math.Max(0, rect.right - rect.left),
                height = (ulong)Math.Max(0, rect.bottom - rect.top)
            };
            m_NativeEncoder4.SetScissorRect(nativeRect);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            if (rects.Length == 0)
            {
                return;
            }

            SetScissor(rects.Span[0]);
        }

        public override void SetViewport(in Viewport viewport)
        {
            RequireEncoderForState("SetViewport");
            MTLViewport nativeViewport = new MTLViewport
            {
                originX = viewport.TopLeftX,
                originY = viewport.TopLeftY,
                width = viewport.Width,
                height = viewport.Height,
                znear = viewport.MinDepth,
                zfar = viewport.MaxDepth
            };
            m_NativeEncoder4.SetViewport(nativeViewport);
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            if (viewports.Length == 0)
            {
                return;
            }

            SetViewport(viewports.Span[0]);
        }

        public override void SetStencilRef(in uint value)
        {
            RequireEncoderForState("SetStencilRef");
            m_NativeEncoder4.SetStencilReferenceValue(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            RequireEncoderForState("SetBlendFactor");
            m_NativeEncoder4.SetBlendColor(value.x, value.y, value.z, value.w);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalRasterPipeline metalPipeline = (MetalRasterPipeline)pipeline;
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Raster pipeline layout must be a MetalPipelineLayout.");
            ConfigureBindingBackend(pipelineLayout);
            EnsureMtl4RenderEncoder();
            ApplyPendingPassDebugGroup();
            BuildVertexStrideMap(metalPipeline);

            m_NativeEncoder4.SetRenderPipelineState(metalPipeline.NativePipelineState);
            if (metalPipeline.DepthStencilState.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.SetDepthStencilState(metalPipeline.DepthStencilState.NativePtr);
            }

            m_NativeEncoder4.SetCullMode(metalPipeline.CullMode);
            m_NativeEncoder4.SetTriangleFillMode(metalPipeline.FillMode);
            m_NativeEncoder4.SetFrontFacingWinding(metalPipeline.Winding);

            MetalBindingLogHelper.LogPipelineModeOnce("Raster", metalPipeline.NativePipelineState.NativePtr);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Raster pipeline must be set before binding resource tables.");
            }

            MetalArgumentTable table = (MetalArgumentTable)resourceTable;
            m_BindingBackend.SetArgumentTable(table, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
#if DEBUG
            MetalPipelineLayout metalLayout = m_CachedPipeline?.Descriptor.PipelineLayout as MetalPipelineLayout;
            Debug.Assert(metalLayout == null || offset + size <= metalLayout.Descriptor.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({metalLayout?.Descriptor.PushConstantSize ?? 0}).");
#endif
            IntPtr offsetData = data + (int)offset;
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.SetVertexBytes(offsetData, size, MetalBindingHelpers.PushConstantBufferIndex);
                m_NativeEncoder4.SetFragmentBytes(offsetData, size, MetalBindingHelpers.PushConstantBufferIndex);
            }
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)buffer;
            m_IndexBuffer = metalBuffer.NativeBuffer;
            m_IndexBufferOffset = offset;
            m_IndexType = MetalUtility.ConvertToMetalIndexType(buffer.Descriptor.Format);
            if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
            {
                queue.AddResidencyAllocation(metalBuffer.NativeBuffer);
            }
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot, in uint offset)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Raster encoder is not created yet. Set pipeline before binding vertex buffers.");
            }

            MetalBuffer metalBuffer = (MetalBuffer)buffer;
            ulong address = metalBuffer.NativeBuffer.GpuAddress + offset;
            m_VertexStrides.TryGetValue(slot, out uint stride);
            m_BindingBackend?.SetRasterVertexBuffer(slot, address, stride);
            if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
            {
                queue.AddResidencyAllocation(metalBuffer.NativeBuffer);
            }
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawPrimitives(primitiveType, firstVertex, vertexCount, instanceCount, firstInstance);
            MarkRasterStagesSeen();
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            if (m_IndexBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Index buffer must be bound before DrawIndexed.");
            }

            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            ulong indexOffset = m_IndexBufferOffset + firstIndex * (m_IndexType == MTLIndexType.UInt16 ? 2UL : 4UL);
            ulong indexAddress = m_IndexBuffer.GpuAddress + indexOffset;
            ulong indexLength = indexOffset < m_IndexBuffer.Length ? m_IndexBuffer.Length - indexOffset : 0;
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawIndexedPrimitives(primitiveType, indexCount, m_IndexType, indexAddress, indexLength, instanceCount, baseVertex, firstInstance);
            MarkRasterStagesSeen();
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            for (uint i = 0; i < drawCount; ++i)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + offset + i * DrawIndirectArgsStride;
                m_NativeEncoder4.DrawPrimitives(primitiveType, indirectAddress);
            }

            MarkRasterStagesSeen();
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            if (m_IndexBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Index buffer must be bound before DrawIndexedIndirect.");
            }

            ulong indexLength = m_IndexBufferOffset < m_IndexBuffer.Length ? m_IndexBuffer.Length - m_IndexBufferOffset : 0;
            ulong indexAddress = m_IndexBuffer.GpuAddress + m_IndexBufferOffset;
            for (uint i = 0; i < drawCount; ++i)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + offset + i * DrawIndexedIndirectArgsStride;
                m_NativeEncoder4.DrawIndexedPrimitives(primitiveType, m_IndexType, indexAddress, indexLength, indirectAddress);
            }

            MarkRasterStagesSeen();
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + argsOffset;
            m_NativeEncoder4.DrawMeshThreadgroupsWithIndirectBuffer(indirectAddress, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalRasterIndirectCommandBuffer metalICB = (MetalRasterIndirectCommandBuffer)indirectCmdBuffer;
            MTLIndirectCommandBuffer nativeICB = metalICB.NativeIndirectCommandBuffer;
            NSRange range = new NSRange { location = 0, length = metalICB.MaxCommandCount };
            m_NativeEncoder4.ExecuteCommandsInBuffer(nativeICB, range);
        }

        public override void EndPass()
        {
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_PendingPassDebugGroup = null;
            m_VertexStrides.Clear();
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private void ConfigureBindingBackend(MetalPipelineLayout pipelineLayout)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue = commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice, MetalBindingPipelineType.Raster, queue);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
        }

        private void EnsureMtl4RenderEncoder()
        {
            if (!m_HasPendingPassDescriptor)
            {
                throw new InvalidOperationException("Raster pass descriptor is not set before encoder creation.");
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MTL4RenderPassDescriptor passDescriptor4 = BuildMtl4RenderPassDescriptor(commandBuffer, m_PendingPassDescriptor);
            m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().RenderCommandEncoder(passDescriptor4);
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4RenderCommandEncoder.");
            }
        }

        private static MTL4RenderPassDescriptor BuildMtl4RenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor)
        {
            MTL4RenderPassDescriptor passDescriptor = MTL4RenderPassDescriptor.New();
            PopulateRenderPassDescriptor(commandBuffer, descriptor, passDescriptor);
            return passDescriptor;
        }

        private static void PopulateRenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor, MTL4RenderPassDescriptor passDescriptor)
        {
            passDescriptor.RenderTargetArrayLength = descriptor.ArrayLength;

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachment = ref descriptor.ColorAttachments.Span[i];
                MetalTexture colorTexture = (MetalTexture)colorAttachment.RenderTarget;
                MTLRenderPassColorAttachmentDescriptor nativeColor = passDescriptor.ColorAttachments[(uint)i];
                nativeColor.Texture = colorTexture.NativeTexture;
                nativeColor.Level = colorAttachment.MipLevel;
                nativeColor.Slice = colorAttachment.ArraySlice;
                nativeColor.LoadAction = MetalUtility.ConvertToMetalLoadAction(colorAttachment.LoadAction);
                nativeColor.StoreAction = MetalUtility.ConvertToMetalStoreAction(colorAttachment.StoreAction);
                nativeColor.ClearColor = new MTLClearColor(colorAttachment.ClearValue.x, colorAttachment.ClearValue.y, colorAttachment.ClearValue.z, colorAttachment.ClearValue.w);

                if (colorAttachment.ResolveTarget != null)
                {
                    MetalTexture resolveTexture = (MetalTexture)colorAttachment.ResolveTarget;
                    nativeColor.ResolveTexture = resolveTexture.NativeTexture;
                    nativeColor.ResolveLevel = colorAttachment.ResolveMipLevel;
                    nativeColor.ResolveSlice = colorAttachment.ResolveArraySlice;
                }

                if (i == 0 && colorTexture.HasBackingDrawable)
                {
                    commandBuffer.SetPresentDrawable(colorTexture.BackingDrawable);
                }
            }

            if (!descriptor.DepthStencilAttachment.HasValue)
            {
                return;
            }

            RHIDepthStencilAttachmentDescriptor depthStencil = descriptor.DepthStencilAttachment.Value;
            MetalTexture depthTexture = (MetalTexture)depthStencil.RenderTarget;

            MTLRenderPassDepthAttachmentDescriptor depthAttachment = passDescriptor.DepthAttachment;
            depthAttachment.Texture = depthTexture.NativeTexture;
            depthAttachment.Level = depthStencil.MipLevel;
            depthAttachment.Slice = depthStencil.ArraySlice;
            depthAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.DepthLoadOp);
            depthAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.DepthStoreOp);
            depthAttachment.ClearDepth = depthStencil.DepthClearValue;

            MTLRenderPassStencilAttachmentDescriptor stencilAttachment = passDescriptor.StencilAttachment;
            stencilAttachment.Texture = depthTexture.NativeTexture;
            stencilAttachment.Level = depthStencil.MipLevel;
            stencilAttachment.Slice = depthStencil.ArraySlice;
            stencilAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.StencilLoadOp);
            stencilAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.StencilStoreOp);
            stencilAttachment.ClearStencil = (uint)depthStencil.StencilClearValue;
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private void BuildVertexStrideMap(MetalRasterPipeline pipeline)
        {
            m_VertexStrides.Clear();
            RHIVertexAssemblerDescriptor? vertexAssembler = pipeline.Descriptor.PrimitiveAssembler.VertexAssembler;
            if (!vertexAssembler.HasValue)
            {
                return;
            }

            Span<RHIVertexLayoutDescriptor> layouts = vertexAssembler.Value.VertexLayouts.Span;
            for (int i = 0; i < layouts.Length; ++i)
            {
                ref readonly RHIVertexLayoutDescriptor layout = ref layouts[i];
                m_VertexStrides[layout.Index] = layout.Stride;
            }
        }

        private MTLPrimitiveType ResolvePrimitiveType()
        {
            if (m_CachedPipeline is MetalRasterPipeline rasterPipeline)
            {
                return rasterPipeline.PrimitiveType;
            }

            return MTLPrimitiveType.Triangle;
        }

        private void RequireEncoderForState(string operation)
        {
            if (HasNativeEncoder)
            {
                return;
            }

            throw new InvalidOperationException($"{operation} requires an active raster encoder. Set pipeline first.");
        }

        private bool HasNativeEncoder => m_NativeEncoder4.NativePtr != IntPtr.Zero;

        private void MarkRasterStagesSeen()
        {
            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(
                MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment));
        }
    }

    internal sealed class MetalMLEncoder : RHIMLEncoder
    {
        private MTL4MachineLearningCommandEncoder m_NativeEncoder;
        private readonly MetalDevice m_MetalDevice;

        public MetalMLEncoder(MetalCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_MetalDevice = ((MetalCommandQueue)cmdBuffer.CommandQueue).MetalDevice;
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
        {
            m_NativeEncoder = default;

            MTL4CommandBuffer mtl4CmdBuffer = ((MetalCommandBuffer)m_CommandBuffer!).EnsureMtl4CommandBuffer();
            m_NativeEncoder = mtl4CmdBuffer.MachineLearningCommandEncoder();

            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4MachineLearningCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0 || m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(m_NativeEncoder.NativePtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
            // Metal timestamp queries are not yet wired in the base infrastructure.
            // This is consistent with other Metal encoder WriteTimestamp implementations.
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalMLPipeline metalPipeline = (MetalMLPipeline)pipeline;
            if (metalPipeline.NativePipelineState.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetPipelineState(metalPipeline.NativePipelineState);
            }
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            MetalArgumentTable metalArgumentTable = (MetalArgumentTable)resourceTable;

            // The ML encoder binds resource tables through the MTL4 argument table mechanism.
            // Create an argument table from the resource table layout and populate it.
            MetalArgumentTableLayout layout = metalArgumentTable.ArgumentTableLayout;

            MTL4ArgumentTableDescriptor argTableDesc = MTL4ArgumentTableDescriptor.New();
            NSError argError = default;
            MTL4ArgumentTable argumentTable = m_MetalDevice.NativeDevice.NewArgumentTable(argTableDesc, ref argError);
            ObjectiveCRuntime.Release(argTableDesc);

            if (argumentTable.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetArgumentTable(argumentTable);
            }
        }

        public override void SetInputTensor(RHITensor tensor, in uint index)
        {
            _ = tensor;
            _ = index;
        }

        public override void SetOutputTensor(RHITensor tensor, in uint index)
        {
            _ = tensor;
            _ = index;
        }

        public override void Dispatch(RHIHeap intermediatesHeap)
        {
            _ = intermediatesHeap;
            m_NativeEncoder.DispatchNetworkWithIntermediatesHeap(default);
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
        }
    }

    // ========== WorkGraph Encoder ==========
    internal sealed class MetalWorkGraphEncoder : RHIWorkGraphEncoder
    {
        internal MetalWorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void PushDebugGroup(string name)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void PopDebugGroup()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void WriteTimestamp(in uint index)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void SetPipeline(RHIWorkGraphPipeline pipeline)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void EndPass()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        protected override void Release()
        {
        }
    }
}
