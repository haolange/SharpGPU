using System;
using SharpMetal.Metal;
using System.Diagnostics;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
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

    internal static class MetalEncoderStateValidation
    {
        internal static void RequireActive(
            IntPtr nativeEncoder,
            string operation)
        {
            if (nativeEncoder == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal {operation} requires an active native encoder.");
            }
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
                throw new InvalidOperationException(
                    "Metal encoder barrier emission requires a live MTL4 command encoder.");
            }

            if (!IsValidMetal4StageMask(afterStages) || !IsValidMetal4StageMask(beforeStages))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(afterStages),
                    "Metal encoder barrier emission received an invalid MTL4 stage mask.");
            }

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(encoderPtr);
            encoder4.BarrierAfterEncoderStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
        }

        internal static void ApplyQueueBarrier(in IntPtr encoderPtr, in ulong afterStages, in ulong beforeStages)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Metal queue barrier emission requires a live MTL4 command encoder.");
            }

            if (!IsValidMetal4StageMask(afterStages) || !IsValidMetal4StageMask(beforeStages))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(afterStages),
                    "Metal queue barrier emission received an invalid MTL4 stage mask.");
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
                RHIBarrierUtility.ValidateQueueOwnership(
                    in barriers[i],
                    commandBuffer.CommandQueue.PipelineType);
                ValidateBarrierResource(
                    ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice,
                    in barriers[i],
                    i);
                if (!TryGetStagePair(barriers[i], out ulong afterStages, out ulong beforeStages))
                {
                    continue;
                }

                bool transfersQueueOwnership = RHIBarrierUtility.TryGetQueueOwnership(
                    in barriers[i], out _, out _);
                if (!transfersQueueOwnership && commandBuffer.IsIntraEncoderBarrier(afterStages))
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

                bool transfersQueueOwnership = RHIBarrierUtility.TryGetQueueOwnership(
                    in barriers[i], out _, out _);
                if (!transfersQueueOwnership && afterStages != 0 && (seenStages & afterStages) != 0)
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

        private static void ValidateBarrierResource(
            MetalDevice device,
            in RHIBarrier barrier,
            int index)
        {
            if (barrier.Kind == ERHIBarrierKind.Buffer)
            {
                RHIBuffer resource = barrier.BufferBarrier.Resource;
                if (resource == null)
                {
                    throw new ArgumentException($"Metal buffer barrier resource is null at index {index}.");
                }
                if (resource.IsDisposed)
                {
                    throw new ObjectDisposedException(resource.GetType().FullName);
                }
                if (resource is not MetalBuffer buffer ||
                    !ReferenceEquals(buffer.MetalDevice, device))
                {
                    throw new ArgumentException(
                        $"Metal buffer barrier resource at index {index} was created by a different backend or device.");
                }
                return;
            }

            if (barrier.Kind == ERHIBarrierKind.Texture)
            {
                RHITexture resource = barrier.TextureBarrier.Resource;
                if (resource == null)
                {
                    throw new ArgumentException($"Metal texture barrier resource is null at index {index}.");
                }
                if (resource.IsDisposed)
                {
                    throw new ObjectDisposedException(resource.GetType().FullName);
                }
                if (resource is not MetalTexture texture ||
                    !ReferenceEquals(texture.MetalDevice, device))
                {
                    throw new ArgumentException(
                        $"Metal texture barrier resource at index {index} was created by a different backend or device.");
                }
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

    internal readonly struct MetalRasterOrderedBindingSnapshot
    {
        internal bool IsBound { get; }
        internal MTLResourceID ResourceId { get; }
        internal MTLAllocation ResidencyAllocation { get; }

        internal MetalRasterOrderedBindingSnapshot(
            in MTLResourceID resourceId,
            in MTLAllocation residencyAllocation)
        {
            IsBound = resourceId._impl != 0;
            ResourceId = resourceId;
            ResidencyAllocation = residencyAllocation;
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
        void ResetForRasterPipeline(
            MetalPipelineLayout pipelineLayout,
            in MetalPrivateRasterBindingPlan privateRasterPlan);
        void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex);
        void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride);
        void SetRasterOrderedTextures(
            ReadOnlySpan<MetalRasterOrderedBindingSnapshot> snapshots,
            in byte activeMask);

        void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        void CommitRaster(in MTL4RenderCommandEncoder encoder);
    }

    internal static class MetalBindingHelpers
    {
        internal const ulong RtVisibleFunctionTableSlot = 29;
        internal const ulong RtIntersectionFunctionTableSlot = 30;

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

        internal static bool RequiresReferenceBuffers(ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            if (layouts.Length > 1)
            {
                return true;
            }

            for (int index = 0; index < layouts.Length; ++index)
            {
                if (layouts[index].RequiresReferenceBuffer)
                {
                    return true;
                }
            }

            return false;
        }

        internal static ulong GetPhysicalBindingIndex(in MetalBindInfo bind, in int arrayIndex)
        {
            if (arrayIndex < 0 || (uint)arrayIndex >= bind.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    $"Metal binding slot={bind.Slot}, type={bind.Type} array index must be in [0, {bind.Count}).");
            }

            return checked((ulong)bind.Slot + (uint)arrayIndex);
        }

        internal static ulong GetReferenceByteOffset(in MetalBindInfo bind, in int arrayIndex)
        {
            return checked(GetPhysicalBindingIndex(bind, arrayIndex) * sizeof(ulong));
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

                ulong end = checked((ulong)bind.Slot + bind.Count);
                if ((ulong)bind.Slot <= RtVisibleFunctionTableSlot
                    && RtVisibleFunctionTableSlot < end
                    || (ulong)bind.Slot <= RtIntersectionFunctionTableSlot
                    && RtIntersectionFunctionTableSlot < end)
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

        private readonly MetalDevice m_Device;
        private readonly MetalBindingPipelineType m_PipelineType;
        private MetalPipelineLayout? m_PipelineLayout;

        protected MetalBindingBackendBase(MetalDevice device, in MetalBindingPipelineType pipelineType)
        {
            m_Device = device;
            m_PipelineType = pipelineType;
        }

        public virtual void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            if (pipelineLayout is null)
            {
                throw new ArgumentNullException(nameof(pipelineLayout));
            }

            if (pipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(pipelineLayout));
            }
            if (pipelineLayout.Device is not null
                && !ReferenceEquals(pipelineLayout.Device, m_Device))
            {
                throw new ArgumentException(
                    "Metal pipeline layout belongs to a different Metal device.",
                    nameof(pipelineLayout));
            }

            m_PipelineLayout = pipelineLayout;
        }

        public virtual void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (resourceTable == null)
            {
                throw new ArgumentNullException(nameof(resourceTable));
            }
            if (resourceTable.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(resourceTable));
            }

            if (resourceTable.ArgumentTableLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(resourceTable), "Metal argument table layout is disposed.");
            }

            if (resourceTable.Device is not null
                && !ReferenceEquals(resourceTable.Device, m_Device))
            {
                throw new ArgumentException(
                    "Metal argument table belongs to a different Metal device.",
                    nameof(resourceTable));
            }
            if (resourceTable.ArgumentTableLayout.Device is not null
                && !ReferenceEquals(resourceTable.ArgumentTableLayout.Device, m_Device))
            {
                throw new ArgumentException(
                    "Metal argument table layout belongs to a different Metal device.",
                    nameof(resourceTable));
            }
            MetalArgumentTableLayout layout = resourceTable.ArgumentTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Metal resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            ValidateTableInPipelineLayout(layout);
            resourceTable.ValidateRequiredBindings();

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

        public virtual void ResetForRasterPipeline(
            MetalPipelineLayout pipelineLayout,
            in MetalPrivateRasterBindingPlan privateRasterPlan)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} does not support raster-private bindings.");
        }

        public virtual void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
        }

        public virtual void SetRasterOrderedTextures(
            ReadOnlySpan<MetalRasterOrderedBindingSnapshot> snapshots,
            in byte activeMask)
        {
            if (activeMask != 0)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} does not support raster-ordered texture bindings.");
            }
        }

        protected virtual void DisposeBackend()
        {
        }

        private void ValidateTableInPipelineLayout(MetalArgumentTableLayout layout)
        {
            if (m_PipelineLayout == null)
            {
                throw new InvalidOperationException("Metal pipeline must be configured before binding argument tables.");
            }

            ReadOnlySpan<MetalArgumentTableLayout> resourceTableLayouts = m_PipelineLayout.ArgumentTableLayouts;
            if (resourceTableLayouts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Metal pipeline declares no argument tables; table {layout.Index} cannot be bound.");
            }

            for (int i = 0; i < resourceTableLayouts.Length; ++i)
            {
                MetalArgumentTableLayout pipelineLayout = resourceTableLayouts[i];
                if (pipelineLayout.Index != layout.Index)
                {
                    continue;
                }

                if (ReferenceEquals(pipelineLayout, layout) || pipelineLayout.StructurallyEquals(layout))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Metal argument table {layout.Index} is structurally incompatible with the current pipeline layout.");
            }

            throw new InvalidOperationException(
                $"Metal argument table index {layout.Index} is not part of the current pipeline layout.");
        }
    }

    internal sealed class MetalArgumentTableBindingBackend : MetalBindingBackendBase
    {
        // Direct MTL4ArgumentTable resource-binding state.
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_ArgumentTables;

        // Reference-buffer state: one descriptor buffer per physical table and one root argument table.
        private bool m_UsesReferenceBuffers;
        private MTL4ArgumentTable m_RootArgumentTable;
        private readonly SortedDictionary<uint, MTLBuffer> m_DescriptorBuffers;

        // Shared state
        private readonly SortedDictionary<uint, RasterVertexBinding> m_RasterVertexBindings;
        private readonly MetalCommandQueue? m_CommandQueue;
        private readonly MetalNativeTransientBatch m_NativeTransients;
        private MetalPrivateRasterBindingPlan m_PrivateRasterPlan;

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

        internal MetalArgumentTableBindingBackend(
            MetalDevice device,
            in MetalBindingPipelineType pipelineType,
            MetalNativeTransientBatch nativeTransients,
            MetalCommandQueue? commandQueue = null)
            : base(device, pipelineType)
        {
            m_ArgumentTables = new SortedDictionary<uint, MTL4ArgumentTable>();
            m_DescriptorBuffers = new SortedDictionary<uint, MTLBuffer>();
            m_RasterVertexBindings = new SortedDictionary<uint, RasterVertexBinding>();
            m_CommandQueue = commandQueue;
            m_NativeTransients =
                nativeTransients ??
                throw new ArgumentNullException(nameof(nativeTransients));
            m_PrivateRasterPlan = default;
        }

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            m_PrivateRasterPlan = default;
            ResetForPipelineCore(pipelineLayout);
        }

        public override void ResetForRasterPipeline(
            MetalPipelineLayout pipelineLayout,
            in MetalPrivateRasterBindingPlan privateRasterPlan)
        {
            if (PipelineType != MetalBindingPipelineType.Raster)
            {
                throw new InvalidOperationException(
                    "Raster-private bindings require a raster binding backend.");
            }

            base.ResetForPipeline(pipelineLayout);
            m_PrivateRasterPlan = privateRasterPlan;
            ResetForPipelineCore(pipelineLayout);
        }

        private void ResetForPipelineCore(MetalPipelineLayout pipelineLayout)
        {
            RetireArgumentTables();
            RetireDescriptorBuffers();
            m_RasterVertexBindings.Clear();

            MetalArgumentTableLayout[] layouts = GetPipelineArgumentTableLayouts(pipelineLayout);
            m_UsesReferenceBuffers = MetalBindingHelpers.RequiresReferenceBuffers(layouts);
            MetalBufferBindingPlanner.ValidatePipelineBufferBudget(layouts, PipelineType);

            if (m_UsesReferenceBuffers)
            {
                for (int index = 0; index < layouts.Length; ++index)
                {
                    layouts[index].ValidateReferenceBufferRanges();
                }

                CreateRootArgumentTable(layouts);
            }
            else if (PipelineType == MetalBindingPipelineType.Raster)
            {
                if (layouts.Length == 1)
                {
                    _ = GetOrCreateArgumentTable(layouts[0], layouts[0].Index);
                }
                else
                {
                    CreateRasterVertexOnlyArgumentTable();
                }
            }
            else if (PipelineType == MetalBindingPipelineType.Raytracing && layouts.Length == 0)
            {
                CreateRayFunctionTableOnlyArgumentTable();
            }
        }

        private static MetalArgumentTableLayout[] GetPipelineArgumentTableLayouts(MetalPipelineLayout pipelineLayout)
        {
            if (pipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(pipelineLayout));
            }

            ReadOnlySpan<MetalArgumentTableLayout> sourceLayouts = pipelineLayout.ArgumentTableLayouts;
            if (sourceLayouts.Length == 0)
            {
                return Array.Empty<MetalArgumentTableLayout>();
            }

            MetalArgumentTableLayout[] layouts = sourceLayouts.ToArray();
            HashSet<uint> indices = new();
            for (int index = 0; index < sourceLayouts.Length; ++index)
            {
                MetalArgumentTableLayout layout = layouts[index];
                if (layout.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        nameof(pipelineLayout),
                        $"Metal pipeline layout argument table {layout.Index} is disposed.");
                }

                if (!indices.Add(layout.Index))
                {
                    throw new InvalidOperationException(
                        $"Metal pipeline layout contains duplicate argument table index {layout.Index}.");
                }

                layouts[index] = layout;
            }

            return layouts;
        }

        protected override void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_UsesReferenceBuffers)
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

            if (m_UsesReferenceBuffers)
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

        public override void SetRasterOrderedTextures(
            ReadOnlySpan<MetalRasterOrderedBindingSnapshot> snapshots,
            in byte activeMask)
        {
            if (PipelineType != MetalBindingPipelineType.Raster)
            {
                throw new InvalidOperationException(
                    "Raster-ordered bindings require a raster binding backend.");
            }
            if (activeMask != m_PrivateRasterPlan.RasterOrderedMask)
            {
                throw new InvalidOperationException(
                    $"Active raster-ordered mask 0x{activeMask:X2} does not " +
                    $"match the pipeline mask " +
                    $"0x{m_PrivateRasterPlan.RasterOrderedMask:X2}.");
            }
            if (activeMask == 0)
            {
                return;
            }
            if (snapshots.Length < RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentException(
                    "Metal raster-ordered snapshots must cover all logical attachment indices.",
                    nameof(snapshots));
            }

            for (int logicalAttachment = 0;
                 logicalAttachment < RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte logicalBit =
                    checked((byte)(1 << logicalAttachment));
                if ((activeMask & logicalBit) == 0)
                {
                    continue;
                }

                ref readonly MetalRasterOrderedBindingSnapshot snapshot =
                    ref snapshots[logicalAttachment];
                if (!snapshot.IsBound)
                {
                    throw new InvalidOperationException(
                        $"Metal raster-ordered logical attachment {logicalAttachment} " +
                        "was not captured at BeginRasterPass.");
                }
                uint nativeIndex =
                    m_PrivateRasterPlan.GetRasterOrderedTextureIndex(
                        logicalAttachment);
                if (m_UsesReferenceBuffers)
                {
                    m_RootArgumentTable.SetTexture(
                        snapshot.ResourceId,
                        nativeIndex);
                }
                else
                {
                    foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in
                             m_ArgumentTables)
                    {
                        pair.Value.SetTexture(
                            snapshot.ResourceId,
                            nativeIndex);
                    }
                }
                m_CommandQueue?.AddResidencyAllocation(
                    snapshot.ResidencyAllocation);
            }
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            if (m_UsesReferenceBuffers)
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
            if (m_UsesReferenceBuffers)
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

            if (m_UsesReferenceBuffers)
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

        // Direct mode: one scalar-only physical table.

        private MTL4ArgumentTable GetOrCreateArgumentTable(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_ArgumentTables.TryGetValue(tableIndex, out MTL4ArgumentTable cachedTable))
            {
                return cachedTable;
            }

            ulong maxBufferCount =
                MetalBufferBindingPlanner.GetDirectArgumentTableBufferBindCount(layout, PipelineType);
            ulong maxTextureCount = 0;
            ulong maxSamplerCount = 0;
            ReadOnlySpan<MetalBindInfo> binds = layout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ulong arrayCount = Math.Max(1UL, bind.Count);
                ulong nextIndex = bind.Slot + arrayCount;
                if (MetalBindingHelpers.IsTextureBindingType(bind.Type))
                {
                    maxTextureCount = Math.Max(maxTextureCount, nextIndex);
                }
                else if (MetalBindingHelpers.IsSamplerBindingType(bind.Type))
                {
                    maxSamplerCount = Math.Max(maxSamplerCount, nextIndex);
                }
            }

            if (PipelineType == MetalBindingPipelineType.Raster)
            {
                maxBufferCount = Math.Max(maxBufferCount, MetalBufferBindingPlanner.MaxBufferBindCount);
                maxTextureCount = Math.Max(
                    maxTextureCount,
                    m_PrivateRasterPlan.RequiredTextureBindingCount);
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount = Math.Max(1UL, maxBufferCount);
            descriptor.MaxTextureBindCount = Math.Max(1UL, maxTextureCount);
            descriptor.MaxSamplerStateBindCount = Math.Max(1UL, maxSamplerCount);
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            MTL4ArgumentTable argumentTable;
            try
            {
                argumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }
            if (argumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"newArgumentTableWithDescriptor failed: {errorText}");
            }

            m_ArgumentTables.Add(tableIndex, argumentTable);
            return argumentTable;
        }

        private void CreateRasterVertexOnlyArgumentTable()
        {
            CreateReservedBufferOnlyArgumentTable("raster vertex", supportsAttributeStrides: true);
        }

        private void CreateRayFunctionTableOnlyArgumentTable()
        {
            CreateReservedBufferOnlyArgumentTable("ray function-table", supportsAttributeStrides: false);
        }

        private void CreateReservedBufferOnlyArgumentTable(
            string purpose,
            in bool supportsAttributeStrides)
        {
            const uint tableIndex = 0;
            if (m_ArgumentTables.ContainsKey(tableIndex))
            {
                return;
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount =
                MetalBufferBindingPlanner.GetReservedBufferOnlyBindCount(PipelineType);
            descriptor.MaxTextureBindCount = Math.Max(
                1UL,
                PipelineType == MetalBindingPipelineType.Raster
                    ? m_PrivateRasterPlan.RequiredTextureBindingCount
                    : 0UL);
            descriptor.MaxSamplerStateBindCount = 1;
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = supportsAttributeStrides;

            NSError error = default;
            MTL4ArgumentTable argumentTable;
            try
            {
                argumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }

            if (argumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException(
                    $"Failed to create Metal 4 {purpose} argument table: {errorText}");
            }

            m_ArgumentTables.Add(tableIndex, argumentTable);
        }

        private void PopulateArgumentTable(MTL4ArgumentTable argumentTable, MetalArgumentTable table)
        {
            table.ValidateRequiredBindings();
            ReadOnlySpan<MetalBindInfo> binds = table.ArgumentTableLayout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = checked((int)bind.Count);

                for (int j = 0; j < arrayCount; ++j)
                {
                    MetalArgumentBindingSnapshot binding = table.GetBindingSnapshot(i, j);
                    ulong slotIndex = MetalBindingHelpers.GetPhysicalBindingIndex(bind, j);

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (binding.IsBound)
                            {
                                argumentTable.SetAddress(binding.BufferAddress, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                argumentTable.SetAddress(0, slotIndex);
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
                            if (binding.IsBound)
                            {
                                argumentTable.SetTexture(binding.ResourceId, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                argumentTable.SetTexture(default, slotIndex);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (binding.IsBound)
                            {
                                argumentTable.SetSamplerState(binding.ResourceId, slotIndex);
                            }
                            else
                            {
                                argumentTable.SetSamplerState(default, slotIndex);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (binding.IsBound)
                            {
                                argumentTable.SetResource(binding.ResourceId, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                argumentTable.SetResource(default, slotIndex);
                            }

                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Metal argument table {table.ArgumentTableLayout.Index} contains unsupported binding type {bind.Type}.");
                    }
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

        // Reference-buffer mode: one packed buffer per physical table and one root argument table.

        private void CreateRootArgumentTable(ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            // The root argument table holds one buffer slot per set (for descriptor buffer GPU addresses),
            // plus reserved slots for RT function tables and raster vertex buffers.
            ulong maxBufferCount =
                MetalBufferBindingPlanner.GetReferenceRootBufferBindCount(layouts, PipelineType);

            if (PipelineType == MetalBindingPipelineType.Raster)
            {
                maxBufferCount = Math.Max(maxBufferCount, MetalBufferBindingPlanner.MaxBufferBindCount);
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount = Math.Max(1UL, maxBufferCount);
            descriptor.MaxTextureBindCount = Math.Max(
                1UL,
                PipelineType == MetalBindingPipelineType.Raster
                    ? m_PrivateRasterPlan.RequiredTextureBindingCount
                    : 0UL);
            descriptor.MaxSamplerStateBindCount = 1UL;
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            try
            {
                m_RootArgumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }
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
            ulong byteSize = checked(layout.ReferenceBufferElementCount * sizeof(ulong));
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
            table.ValidateRequiredBindings();
            IntPtr ptr = buffer.Contents;
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal descriptor buffer for table {table.ArgumentTableLayout.Index} is not CPU-addressable.");
            }

            ReadOnlySpan<MetalBindInfo> binds = table.ArgumentTableLayout.BindInfos;

            for (ulong entryIndex = 0; entryIndex < table.ArgumentTableLayout.ReferenceBufferElementCount; ++entryIndex)
            {
                ulong entryByteOffset = checked(entryIndex * sizeof(ulong));
                IntPtr entryAddress = GetDescriptorBufferEntryAddress(ptr, entryByteOffset);
                Marshal.WriteInt64(entryAddress, 0);
            }

            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);

                for (int j = 0; j < arrayCount; ++j)
                {
                    MetalArgumentBindingSnapshot binding = table.GetBindingSnapshot(i, j);
                    ulong entryByteOffset = MetalBindingHelpers.GetReferenceByteOffset(bind, j);
                    IntPtr entryAddress = GetDescriptorBufferEntryAddress(ptr, entryByteOffset);

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (binding.IsBound)
                            {
                                Marshal.WriteInt64(entryAddress, (long)binding.BufferAddress);
                                commandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
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
                            if (binding.IsBound)
                            {
                                Marshal.WriteInt64(entryAddress, (long)binding.ResourceId._impl);
                                commandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (binding.IsBound)
                            {
                                Marshal.WriteInt64(entryAddress, (long)binding.ResourceId._impl);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (binding.IsBound)
                            {
                                Marshal.WriteInt64(entryAddress, (long)binding.ResourceId._impl);
                                commandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }

                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Metal argument table {table.ArgumentTableLayout.Index} contains unsupported binding type {bind.Type}.");
                    }
                }
            }
        }

        private static IntPtr GetDescriptorBufferEntryAddress(IntPtr baseAddress, in ulong byteOffset)
        {
            long address = checked(baseAddress.ToInt64() + checked((long)byteOffset));
            return new IntPtr(address);
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

        private void RetireArgumentTables()
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    m_NativeTransients.RetainOwnership(
                        pair.Value.NativePtr);
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

            m_UsesReferenceBuffers = false;
        }

        private void RetireDescriptorBuffers()
        {
            foreach (KeyValuePair<uint, MTLBuffer> pair in m_DescriptorBuffers)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    m_NativeTransients.RetainOwnership(
                        pair.Value.NativePtr);
                }
            }

            m_DescriptorBuffers.Clear();

            if (m_RootArgumentTable.NativePtr != IntPtr.Zero)
            {
                m_NativeTransients.RetainOwnership(
                    m_RootArgumentTable.NativePtr);
                m_RootArgumentTable = default;
            }

            m_UsesReferenceBuffers = false;
        }
    }

    internal sealed class MetalTransferEncoder : RHITransferEncoder
    {
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private RHITransferPassDescriptor m_PassDescriptor;

        internal MetalTransferEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder4 = default;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (descriptor.Timestamp.HasValue)
            {
                MetalQuery query = descriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal transfer timestamp pass requires a MetalQuery.");
                query.WriteTimestamp(commandBuffer, descriptor.Timestamp.Value.BeginIndex);
            }

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
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal transfer timestamp pass requires a MetalQuery.");
                query.WriteTimestamp(m_NativeEncoder4, index);
            }
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
            MetalQuery metalQuery = query as MetalQuery
                ?? throw new InvalidOperationException("Metal transfer resolve requires a MetalQuery.");
            metalQuery.Resolve((MetalCommandBuffer)m_CommandBuffer!, startIndex, queriesCount);
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

        internal override void EndPassCore()
        {
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                return;
            }

            RHITimestampDescriptor? timestamp = m_PassDescriptor.Timestamp;

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.EndEncoding();
            m_NativeEncoder4 = default;

            if (timestamp.HasValue)
            {
                MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
                MetalQuery query = timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal transfer timestamp pass requires a MetalQuery.");
                query.WriteTimestamp(commandBuffer, timestamp.Value.EndIndex);
            }

            m_PassDescriptor = default;
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
        private RHIComputePassDescriptor m_PassDescriptor;

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
            m_PassDescriptor = descriptor;
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

            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(WaitForFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Compute);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
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
            if (barriers.Length == 0)
            {
                return;
            }
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "barrier emission");

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
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "debug-group pop");

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal compute timestamp pass requires a MetalQuery.");
                query.WriteTimestamp(m_NativeEncoder4, index);
            }
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

            MetalArgumentTable table = resourceTable as MetalArgumentTable
                ?? throw new ArgumentException("Metal compute binding requires a MetalArgumentTable.", nameof(resourceTable));
            ((MetalCommandBuffer)m_CommandBuffer!).MarkArgumentTablesUsed();
            m_BindingBackend.SetArgumentTable(table, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            throw new NotSupportedException(
                "Metal 4 push constants require an explicit compiled buffer binding and are not supported by this backend.");
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

        internal override void EndPassCore()
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
            m_PassDescriptor = default;
        }

        internal void ResetForRecording()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
            m_NativeEncoder4 = default;
            m_PendingPassDebugGroup = null;
            m_PassDescriptor = default;
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            ResetForRecording();
        }

        private void ConfigureBindingBackend(MetalPipelineLayout pipelineLayout)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue = commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(
                    m_MetalDevice,
                    MetalBindingPipelineType.Compute,
                    commandBuffer.NativeTransientBatch,
                    queue);
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

        private void ValidateFenceAndEncoder(
            in MTLFence fence,
            string operation)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                throw new ArgumentException(
                    $"Metal {operation} requires a valid native fence.",
                    nameof(fence));
            }
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal {operation} requires an active compute encoder.");
            }
        }

        private bool HasNativeEncoder => m_NativeEncoder4.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalRaytracingEncoder : RHIRaytracingEncoder
    {
        private readonly MetalDevice m_MetalDevice;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;
        private RHIRayTracingPassDescriptor m_PassDescriptor;

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
            m_PassDescriptor = descriptor;
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

            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0)
            {
                return;
            }
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "barrier emission");

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
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "debug-group pop");

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal ray tracing timestamp pass requires a MetalQuery.");
                query.WriteTimestamp(m_NativeEncoder4, index);
            }
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

            MetalArgumentTable table = resourceTable as MetalArgumentTable
                ?? throw new ArgumentException("Metal ray tracing binding requires a MetalArgumentTable.", nameof(resourceTable));
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

        internal override void EndPassCore()
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
            m_PassDescriptor = default;
        }

        internal void WaitForFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(WaitForFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.RayTracing);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.UpdateFence(fence, stage);
        }

        internal void ResetForRecording()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
            m_NativeEncoder4 = default;
            m_PendingPassDebugGroup = null;
            m_PassDescriptor = default;
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            ResetForRecording();
        }

        private void ConfigureBindingBackend(MetalPipelineLayout pipelineLayout)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue = commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(
                    m_MetalDevice,
                    MetalBindingPipelineType.Raytracing,
                    commandBuffer.NativeTransientBatch,
                    queue);
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

        private void ValidateFenceAndEncoder(
            in MTLFence fence,
            string operation)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                throw new ArgumentException(
                    $"Metal {operation} requires a valid native fence.",
                    nameof(fence));
            }
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal {operation} requires an active ray-tracing encoder.");
            }
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
        private MetalRasterPassLowering? m_RasterLowering;
        private MTLLogicalToPhysicalColorAttachmentMap[] m_ColorAttachmentMaps;
        private readonly MetalRasterOrderedBindingSnapshot[]
            m_RasterOrderedBindings;
        private uint m_RenderTargetWidth;
        private uint m_RenderTargetHeight;
        private ERHISampleCount m_RasterSampleCount;
        private int m_CurrentSubPassIndex;

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
            m_RasterLowering = null;
            m_ColorAttachmentMaps =
                Array.Empty<MTLLogicalToPhysicalColorAttachmentMap>();
            m_RasterOrderedBindings =
                new MetalRasterOrderedBindingSnapshot[
                    RHIAttachmentIndexArray.MaxAttachments];
            m_RenderTargetWidth = 0;
            m_RenderTargetHeight = 0;
            m_RasterSampleCount = ERHISampleCount.None;
            m_CurrentSubPassIndex = 0;
        }

        internal override void BeginPassCore(RasterPassPlan plan)
        {
            RHIRasterPassDescriptor descriptor = plan.DescriptorSnapshot;
            MetalCommandBuffer commandBuffer =
                (MetalCommandBuffer)m_CommandBuffer!;
            MetalDevice device =
                ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            MetalNativeTransientBatch nativeTransients =
                commandBuffer.NativeTransientBatch;
            int transientCheckpoint =
                nativeTransients.CaptureCheckpoint();
            try
            {
                m_RasterLowering = MetalRasterPassLowering.Compile(
                    plan,
                    device.RasterCapabilities);
                m_RenderTargetWidth = plan.Width;
                m_RenderTargetHeight = plan.Height;
                m_RasterSampleCount = plan.SampleCount;
                CaptureRasterOrderedBindings(
                    plan,
                    m_RasterLowering.RasterOrderedAttachmentMask);
                ReleaseColorAttachmentMaps();
                m_ColorAttachmentMaps = CreateColorAttachmentMaps(
                    plan,
                    m_RasterLowering);
            }
            catch
            {
                ReleaseColorAttachmentMaps();
                Array.Clear(m_RasterOrderedBindings);
                m_RasterLowering = null;
                m_RenderTargetWidth = 0;
                m_RenderTargetHeight = 0;
                m_RasterSampleCount = ERHISampleCount.None;
                nativeTransients.RollbackTo(transientCheckpoint);
                throw;
            }

            m_CurrentSubPassIndex = 0;
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

            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(WaitForFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
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
            if (barriers.Length == 0)
            {
                return;
            }
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "barrier emission");

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
            MetalEncoderStateValidation.RequireActive(
                m_NativeEncoder4.NativePtr,
                "debug-group pop");

            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.PopDebugGroup();
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PendingPassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster timestamp pass requires a MetalQuery.");
                ulong stages = MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Vertex | ERHISyncStageMask.Fragment);
                query.WriteTimestamp(m_NativeEncoder4, stages, index);
            }
        }

        public override void BeginOcclusion(in uint index)
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Occlusion.HasValue)
            {
                MetalQuery query = m_PendingPassDescriptor.Occlusion.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster occlusion pass requires a MetalQuery.");
                query.BeginOcclusion(m_NativeEncoder4, index);
            }
        }

        public override void EndOcclusion(in uint index)
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Occlusion.HasValue)
            {
                MetalQuery query = m_PendingPassDescriptor.Occlusion.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster occlusion pass requires a MetalQuery.");
                query.EndOcclusion(m_NativeEncoder4, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Statistics.HasValue)
            {
                MetalQuery query = m_PendingPassDescriptor.Statistics.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster statistics pass requires a MetalQuery.");
                query.BeginStatistics(m_NativeEncoder4, index);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Statistics.HasValue)
            {
                MetalQuery query = m_PendingPassDescriptor.Statistics.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster statistics pass requires a MetalQuery.");
                query.EndStatistics(m_NativeEncoder4, index);
            }
        }

        internal override void NextSubPassCore(
            RasterPassPlan plan,
            int sourceSubPassIndex,
            int destinationSubPassIndex)
        {
            RequireEncoderForState(nameof(NextSubPass));
            if (sourceSubPassIndex != m_CurrentSubPassIndex ||
                destinationSubPassIndex != sourceSubPassIndex + 1)
            {
                throw new InvalidOperationException(
                    "Metal subpass advancement is not sequential.");
            }

            ulong fragmentStages =
                MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Fragment);
            new MTL4CommandEncoder(m_NativeEncoder4.NativePtr)
                .BarrierAfterEncoderStages(
                    fragmentStages,
                    fragmentStages,
                    MTL4VisibilityOptions.Device);
            ApplyColorAttachmentMap(destinationSubPassIndex);
            m_CurrentSubPassIndex = destinationSubPassIndex;
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

        internal override void SetPipelineCore(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalRasterPipeline metalPipeline = (MetalRasterPipeline)pipeline;
            MetalPipelineLayout pipelineLayout = pipeline.DescriptorInternal.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Raster pipeline layout must be a MetalPipelineLayout.");
            ConfigureBindingBackend(
                pipelineLayout,
                metalPipeline.PrivateRasterBindingPlan);
            EnsureMtl4RenderEncoder();
            ApplyPendingPassDebugGroup();

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

            MetalArgumentTable table = resourceTable as MetalArgumentTable
                ?? throw new ArgumentException("Metal raster binding requires a MetalArgumentTable.", nameof(resourceTable));
            m_BindingBackend.SetArgumentTable(table, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            throw new NotSupportedException(
                "Metal 4 push constants require an explicit compiled buffer binding and are not supported by this backend.");
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
            MetalRasterPipeline rasterPipeline = m_CachedPipeline as MetalRasterPipeline
                ?? throw new InvalidOperationException("Raster pipeline must be set before binding vertex buffers.");
            MetalVertexBufferBinding binding = rasterPipeline.BufferBindingPlan.GetVertexBinding(slot);
            m_BindingBackend?.SetRasterVertexBuffer(
                checked((uint)binding.PhysicalIndex),
                address,
                binding.Stride);
            if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
            {
                queue.AddResidencyAllocation(metalBuffer.NativeBuffer);
            }
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
        }

        internal override void DrawCore(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawPrimitives(primitiveType, firstVertex, vertexCount, instanceCount, firstInstance);
            MarkRasterStagesSeen();
        }

        internal override void DrawIndexedCore(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
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

        internal override void DrawIndirectCore(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
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

        internal override void DrawIndexedIndirectCore(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
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

        internal override void DispatchMeshCore(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        internal override void DispatchMeshIndirectCore(RHIBuffer argsBuffer, in uint argsOffset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + argsOffset;
            m_NativeEncoder4.DrawMeshThreadgroupsWithIndirectBuffer(indirectAddress, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        internal override void EndPassCore()
        {
            if (m_HasPendingPassDescriptor && m_PendingPassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PendingPassDescriptor.Timestamp.Value.EndIndex);
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            ReleaseColorAttachmentMaps();
            m_RasterLowering = null;
            m_CurrentSubPassIndex = 0;
            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
        }

        internal void ResetForRecording()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
            m_NativeEncoder4 = default;
            m_IndexBuffer = default;
            m_IndexBufferOffset = 0;
            m_IndexType = MTLIndexType.UInt16;
            m_PendingPassDebugGroup = null;
            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_RasterLowering = null;
            ReleaseColorAttachmentMaps();
            Array.Clear(m_RasterOrderedBindings);
            m_RenderTargetWidth = 0;
            m_RenderTargetHeight = 0;
            m_RasterSampleCount = ERHISampleCount.None;
            m_CurrentSubPassIndex = 0;
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            ResetForRecording();
        }

        private void ConfigureBindingBackend(
            MetalPipelineLayout pipelineLayout,
            in MetalPrivateRasterBindingPlan privateRasterPlan)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;

            if (m_BindingBackend == null)
            {
                MetalCommandQueue? queue =
                    commandBuffer.CommandQueue as MetalCommandQueue;
                m_BindingBackend = new MetalArgumentTableBindingBackend(
                    ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice,
                    MetalBindingPipelineType.Raster,
                    commandBuffer.NativeTransientBatch,
                    queue);
            }

            m_BindingBackend.ResetForRasterPipeline(
                pipelineLayout,
                privateRasterPlan);
            MetalRasterPassLowering lowering =
                m_RasterLowering ??
                throw new InvalidOperationException(
                    "Metal raster lowering is unavailable while setting a pipeline.");
            ref readonly MetalRasterSubPassLowering subPass =
                ref lowering.SubPasses.Span[m_CurrentSubPassIndex];
            m_BindingBackend.SetRasterOrderedTextures(
                m_RasterOrderedBindings,
                subPass.RasterOrderedMask);
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
            MTL4RenderPassDescriptor passDescriptor4 =
                BuildMtl4RenderPassDescriptor(
                    commandBuffer,
                    m_PendingPassDescriptor);
            try
            {
                m_NativeEncoder4 =
                    commandBuffer.EnsureMtl4CommandBuffer()
                        .RenderCommandEncoder(passDescriptor4);
            }
            finally
            {
                ObjectiveCRuntime.Release(passDescriptor4.NativePtr);
            }
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Failed to create MTL4RenderCommandEncoder.");
            }
            ApplyColorAttachmentMap(0);
        }

        private MTL4RenderPassDescriptor BuildMtl4RenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor)
        {
            MTL4RenderPassDescriptor passDescriptor =
                MTL4RenderPassDescriptor.New();
            try
            {
                MetalRasterPassLowering lowering =
                    m_RasterLowering ??
                    throw new InvalidOperationException(
                        "Metal raster lowering is unavailable.");
                if (lowering.RequiresColorAttachmentMapping)
                {
                    passDescriptor.SupportColorAttachmentMapping = true;
                }
                passDescriptor.RenderTargetWidth = m_RenderTargetWidth;
                passDescriptor.RenderTargetHeight = m_RenderTargetHeight;
                passDescriptor.DefaultRasterSampleCount =
                    checked((ulong)m_RasterSampleCount);
                PopulateRenderPassDescriptor(
                    commandBuffer,
                    descriptor,
                    lowering.OrdinaryAttachmentMask,
                    passDescriptor);
                return passDescriptor;
            }
            catch
            {
                ObjectiveCRuntime.Release(passDescriptor.NativePtr);
                throw;
            }
        }

        private static void PopulateRenderPassDescriptor(
            MetalCommandBuffer commandBuffer,
            in RHIRasterPassDescriptor descriptor,
            in byte ordinaryAttachmentMask,
            MTL4RenderPassDescriptor passDescriptor)
        {
            passDescriptor.RenderTargetArrayLength = descriptor.ArrayLength;

            if (descriptor.Occlusion.HasValue)
            {
                MetalQuery query = descriptor.Occlusion.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal raster occlusion pass requires a MetalQuery.");
                passDescriptor.VisibilityResultBuffer = query.VisibilityResultBuffer;
                passDescriptor.VisibilityResultType = MTLVisibilityResultMode.Counting;
            }

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                byte logicalBit = checked((byte)(1 << i));
                if ((ordinaryAttachmentMask & logicalBit) == 0)
                {
                    continue;
                }

                ref RHIColorAttachmentDescriptor colorAttachment =
                    ref descriptor.ColorAttachments.Span[i];
                MetalTexture colorTexture =
                    colorAttachment.RenderTarget as MetalTexture ??
                    throw new ArgumentException(
                        $"Metal color attachment {i} must be a MetalTexture.",
                        nameof(descriptor));
                MTLRenderPassColorAttachmentDescriptor nativeColor = passDescriptor.ColorAttachments[(uint)i];
                nativeColor.Texture = colorTexture.NativeTexture;
                nativeColor.Level = colorAttachment.SubresourceRange.BaseMipLevel;
                nativeColor.Slice = colorAttachment.SubresourceRange.BaseArrayLayer;
                nativeColor.LoadAction = MetalUtility.ConvertToMetalLoadAction(colorAttachment.LoadAction);
                nativeColor.StoreAction = MetalUtility.ConvertToMetalStoreAction(colorAttachment.StoreAction);
                nativeColor.ClearColor = new MTLClearColor(colorAttachment.ClearValue.x, colorAttachment.ClearValue.y, colorAttachment.ClearValue.z, colorAttachment.ClearValue.w);

                if (colorAttachment.ResolveTarget != null)
                {
                    MetalTexture resolveTexture = (MetalTexture)colorAttachment.ResolveTarget;
                    nativeColor.ResolveTexture = resolveTexture.NativeTexture;
                    nativeColor.ResolveLevel = colorAttachment.ResolveSubresourceRange.BaseMipLevel;
                    nativeColor.ResolveSlice = colorAttachment.ResolveSubresourceRange.BaseArrayLayer;
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
            depthAttachment.Level = depthStencil.SubresourceRange.BaseMipLevel;
            depthAttachment.Slice = depthStencil.SubresourceRange.BaseArrayLayer;
            depthAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.DepthLoadOp);
            depthAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.DepthStoreOp);
            depthAttachment.ClearDepth = depthStencil.DepthClearValue;

            MTLRenderPassStencilAttachmentDescriptor stencilAttachment = passDescriptor.StencilAttachment;
            stencilAttachment.Texture = depthTexture.NativeTexture;
            stencilAttachment.Level = depthStencil.SubresourceRange.BaseMipLevel;
            stencilAttachment.Slice = depthStencil.SubresourceRange.BaseArrayLayer;
            stencilAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.StencilLoadOp);
            stencilAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.StencilStoreOp);
            stencilAttachment.ClearStencil = (uint)depthStencil.StencilClearValue;

            if (depthStencil.ResolveTarget != null)
            {
                MetalTexture resolveTexture =
                    (MetalTexture)depthStencil.ResolveTarget;
                depthAttachment.ResolveTexture = resolveTexture.NativeTexture;
                depthAttachment.ResolveLevel =
                    depthStencil.ResolveSubresourceRange.BaseMipLevel;
                depthAttachment.ResolveSlice =
                    depthStencil.ResolveSubresourceRange.BaseArrayLayer;
                depthAttachment.DepthResolveFilter =
                    ConvertDepthResolveFilter(depthStencil.DepthResolveMode);
                stencilAttachment.ResolveTexture = resolveTexture.NativeTexture;
                stencilAttachment.ResolveLevel =
                    depthStencil.ResolveSubresourceRange.BaseMipLevel;
                stencilAttachment.ResolveSlice =
                    depthStencil.ResolveSubresourceRange.BaseArrayLayer;
                stencilAttachment.StencilResolveFilter =
                    ConvertStencilResolveFilter(
                        depthStencil.StencilResolveMode);
            }
        }

        private MTLLogicalToPhysicalColorAttachmentMap[]
            CreateColorAttachmentMaps(
                RasterPassPlan plan,
                MetalRasterPassLowering lowering)
        {
            if (!lowering.RequiresColorAttachmentMapping)
            {
                return Array.Empty<
                    MTLLogicalToPhysicalColorAttachmentMap>();
            }

            MetalCommandBuffer commandBuffer =
                (MetalCommandBuffer)m_CommandBuffer!;
            MTLLogicalToPhysicalColorAttachmentMap[] maps =
                new MTLLogicalToPhysicalColorAttachmentMap[
                    plan.SubPassCount];
            for (int subPassIndex = 0;
                 subPassIndex < maps.Length;
                 ++subPassIndex)
            {
                MTLLogicalToPhysicalColorAttachmentMap map =
                    MTLLogicalToPhysicalColorAttachmentMap.New();
                if (map.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "Failed to create a Metal color-attachment map.");
                }

                bool ownershipTransferred = false;
                try
                {
                    ref readonly MetalRasterSubPassLowering subPass =
                        ref lowering.SubPasses.Span[subPassIndex];
                    for (int mappingIndex = 0;
                         mappingIndex <
                             RHIAttachmentIndexArray.MaxAttachments;
                         ++mappingIndex)
                    {
                        map.SetPhysicalIndex(
                            subPass.GetPhysicalAttachmentForMappingIndex(
                                mappingIndex),
                            checked((ulong)mappingIndex));
                    }
                    commandBuffer.NativeTransientBatch.RetainOwnership(
                        map.NativePtr);
                    ownershipTransferred = true;
                    maps[subPassIndex] = map;
                }
                finally
                {
                    if (!ownershipTransferred)
                    {
                        ObjectiveCRuntime.Release(map.NativePtr);
                    }
                }
            }
            return maps;
        }

        private void ApplyColorAttachmentMap(int subPassIndex)
        {
            if (m_RasterLowering?.RequiresColorAttachmentMapping != true)
            {
                return;
            }
            m_NativeEncoder4.SetColorAttachmentMap(
                m_ColorAttachmentMaps[subPassIndex].NativePtr);
        }

        private void ReleaseColorAttachmentMaps()
        {
            // Native map ownership belongs to the command-buffer recording
            // transient batch. The managed wrappers carry no ownership.
            m_ColorAttachmentMaps =
                Array.Empty<MTLLogicalToPhysicalColorAttachmentMap>();
        }

        private void CaptureRasterOrderedBindings(
            RasterPassPlan plan,
            in byte rasterOrderedMask)
        {
            Array.Clear(m_RasterOrderedBindings);
            if (rasterOrderedMask == 0)
            {
                return;
            }

            MetalCommandBuffer commandBuffer =
                (MetalCommandBuffer)m_CommandBuffer!;
            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte logicalBit =
                    checked((byte)(1 << logicalAttachment));
                if ((rasterOrderedMask & logicalBit) == 0)
                {
                    continue;
                }

                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(logicalAttachment);
                MetalTexture texture =
                    attachment.RenderTarget as MetalTexture ??
                    throw new ArgumentException(
                        $"Metal raster-ordered attachment {logicalAttachment} " +
                        "must be a MetalTexture.",
                        nameof(plan));
                MTLTexture nativeTexture = texture.NativeTexture;
                MTLTexture boundTexture = nativeTexture;
                RHITextureSubresourceRange range =
                    attachment.SubresourceRange;
                RHITextureDescriptor textureDescriptor =
                    texture.Descriptor;
                uint fullLayerCount =
                    Math.Max(1u, textureDescriptor.Extent.z);
                bool isFullView =
                    range.BaseMipLevel == 0 &&
                    range.MipLevelCount == textureDescriptor.MipCount &&
                    range.BaseArrayLayer == 0 &&
                    range.ArrayLayerCount == fullLayerCount;
                if (!isFullView)
                {
                    boundTexture = nativeTexture.NewTextureView(
                        MetalUtility.ConvertToMetalPixelFormat(
                            textureDescriptor.Format),
                        MetalUtility.ConvertToMetalTextureType(
                            textureDescriptor.Dimension),
                        new NSRange
                        {
                            location = range.BaseMipLevel,
                            length = range.MipLevelCount
                        },
                        new NSRange
                        {
                            location = range.BaseArrayLayer,
                            length = range.ArrayLayerCount
                        });
                    if (boundTexture.NativePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create a Metal texture view for " +
                            $"raster-ordered attachment {logicalAttachment}.");
                    }
                    try
                    {
                        commandBuffer.NativeTransientBatch.RetainOwnership(
                            boundTexture.NativePtr);
                    }
                    catch
                    {
                        ObjectiveCRuntime.Release(boundTexture.NativePtr);
                        throw;
                    }
                }

                m_RasterOrderedBindings[logicalAttachment] =
                    new MetalRasterOrderedBindingSnapshot(
                        boundTexture.GpuResourceID,
                        new MTLAllocation(nativeTexture.NativePtr));
            }
        }

        private void ValidateFenceAndEncoder(
            in MTLFence fence,
            string operation)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                throw new ArgumentException(
                    $"Metal {operation} requires a valid native fence.",
                    nameof(fence));
            }
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal {operation} requires an active raster encoder.");
            }
        }

        private static MTLMultisampleDepthResolveFilter
            ConvertDepthResolveFilter(EResolveMode mode)
        {
            return mode switch
            {
                EResolveMode.Min => MTLMultisampleDepthResolveFilter.Min,
                EResolveMode.Max => MTLMultisampleDepthResolveFilter.Max,
                _ => MTLMultisampleDepthResolveFilter.Sample0,
            };
        }

        private static MTLMultisampleStencilResolveFilter
            ConvertStencilResolveFilter(EResolveMode mode)
        {
            if (mode != EResolveMode.None && mode != EResolveMode.Sample0)
            {
                throw new NotSupportedException(
                    $"Metal cannot exactly express stencil resolve mode {mode}.");
            }
            return MTLMultisampleStencilResolveFilter.Sample0;
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

    // ========== WorkGraph Encoder ==========
}
