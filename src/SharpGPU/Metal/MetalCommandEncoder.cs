using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;

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
        // Vertex | Fragment | Dispatch | Blit | AccelerationStructure | MachineLearning
        private const ulong s_ValidMetal4StageMask =
            (1UL << 0) | (1UL << 1) | (1UL << 27) | (1UL << 28) | (1UL << 29) | (1UL << 30);
        private const ulong s_Metal4MachineLearningStage = 1UL << 30;

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
            bool afterIncludesMl = (afterStages & s_Metal4MachineLearningStage) != 0;
            bool beforeIncludesMl = (beforeStages & s_Metal4MachineLearningStage) != 0;
            if (beforeIncludesMl && !afterIncludesMl)
            {
                // Producer encoder (e.g. blit) finished; subsequent ML work must wait.
                // WWDC25: barrierAfterStages:beforeQueueStages:
                encoder4.BarrierAfterStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
            }
            else
            {
                // Consumer encoder waits for prior queue stages (including ML).
                encoder4.BarrierAfterQueueStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
            }
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
                // MTLStageMachineLearning is not an encoder-local stage for blit/compute/render.
                // Cross-stage sync involving ML must use queue barriers (WWDC25).
                bool involvesMachineLearning =
                    (afterStages & s_Metal4MachineLearningStage) != 0 ||
                    (beforeStages & s_Metal4MachineLearningStage) != 0;
                if (!transfersQueueOwnership &&
                    !involvesMachineLearning &&
                    commandBuffer.IsIntraEncoderBarrier(afterStages))
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
                bool involvesMachineLearning =
                    (afterStages & s_Metal4MachineLearningStage) != 0 ||
                    (beforeStages & s_Metal4MachineLearningStage) != 0;
                if (!transfersQueueOwnership &&
                    !involvesMachineLearning &&
                    afterStages != 0 &&
                    (seenStages & afterStages) != 0)
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
                    afterStages = NormalizeToMetal4Stages(globalBarrier.StageBefore);
                    beforeStages = NormalizeToMetal4Stages(globalBarrier.StageAfter);
                    break;
                }

                case ERHIBarrierKind.Buffer:
                {
                    RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                    afterStages = NormalizeToMetal4Stages(bufferBarrier.StageBefore);
                    beforeStages = NormalizeToMetal4Stages(bufferBarrier.StageAfter);
                    break;
                }

                case ERHIBarrierKind.Texture:
                {
                    RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                    afterStages = NormalizeToMetal4Stages(textureBarrier.StageBefore);
                    beforeStages = NormalizeToMetal4Stages(textureBarrier.StageAfter);
                    break;
                }

                default:
                    afterStages = 0;
                    beforeStages = 0;
                    return false;
            }

            return afterStages != 0 && beforeStages != 0;
        }

        private static ulong NormalizeToMetal4Stages(in ERHIStageMask stages)
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
        void SetBindingTable(MetalBindingTable resourceTable, in uint tableIndex);
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

        internal static bool RequiresReferenceBuffers(ReadOnlySpan<MetalBindingTableLayout> layouts)
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

        internal static bool HasRayFunctionTableSlotConflict(MetalBindingTable table)
        {
            return HasRayFunctionTableSlotConflict(table.BindingTableLayout.BindInfos);
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

        public virtual void SetBindingTable(MetalBindingTable resourceTable, in uint tableIndex)
        {
            if (resourceTable == null)
            {
                throw new ArgumentNullException(nameof(resourceTable));
            }
            if (resourceTable.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(resourceTable));
            }

            if (resourceTable.BindingTableLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(resourceTable), "Metal binding table layout is disposed.");
            }

            if (resourceTable.Device is not null
                && !ReferenceEquals(resourceTable.Device, m_Device))
            {
                throw new ArgumentException(
                    "Metal binding table belongs to a different Metal device.",
                    nameof(resourceTable));
            }
            if (resourceTable.BindingTableLayout.Device is not null
                && !ReferenceEquals(resourceTable.BindingTableLayout.Device, m_Device))
            {
                throw new ArgumentException(
                    "Metal binding table layout belongs to a different Metal device.",
                    nameof(resourceTable));
            }
            MetalBindingTableLayout layout = resourceTable.BindingTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Metal resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            ValidateTableInPipelineLayout(layout);
            resourceTable.ValidateRequiredBindings();

            OnBindingTableUpdated(resourceTable, tableIndex);
        }

        public abstract void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        public abstract void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        public abstract void CommitRaster(in MTL4RenderCommandEncoder encoder);

        public void Dispose()
        {
            DisposeBackend();
        }

        protected virtual void OnBindingTableUpdated(MetalBindingTable resourceTable, in uint tableIndex)
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

        private void ValidateTableInPipelineLayout(MetalBindingTableLayout layout)
        {
            if (m_PipelineLayout == null)
            {
                throw new InvalidOperationException("Metal pipeline must be configured before binding binding tables.");
            }

            ReadOnlySpan<MetalBindingTableLayout> resourceTableLayouts = m_PipelineLayout.BindingTableLayouts;
            if (resourceTableLayouts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Metal pipeline declares no binding tables; table {layout.Index} cannot be bound.");
            }

            for (int i = 0; i < resourceTableLayouts.Length; ++i)
            {
                MetalBindingTableLayout pipelineLayout = resourceTableLayouts[i];
                if (pipelineLayout.Index != layout.Index)
                {
                    continue;
                }

                if (ReferenceEquals(pipelineLayout, layout) || pipelineLayout.StructurallyEquals(layout))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Metal binding table {layout.Index} is structurally incompatible with the current pipeline layout.");
            }

            throw new InvalidOperationException(
                $"Metal binding table index {layout.Index} is not part of the current pipeline layout.");
        }
    }

    internal sealed class MetalBindingTableBindingBackend : MetalBindingBackendBase
    {
        // Direct MTL4ArgumentTable resource-binding state.
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_NativeArgumentTables;

        // Reference-buffer state: one descriptor buffer per physical table and one root binding table.
        private bool m_UsesReferenceBuffers;
        private MTL4ArgumentTable m_RootNativeArgumentTable;
        private readonly SortedDictionary<uint, MTLBuffer> m_DescriptorBuffers;

        // Shared state
        private readonly SortedDictionary<uint, RasterVertexBinding> m_RasterVertexBindings;
        private readonly MetalCommandQueue? m_CommandQueue;
        private readonly MetalTransientNativeBatch m_NativeTransients;
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

        internal MetalBindingTableBindingBackend(
            MetalDevice device,
            in MetalBindingPipelineType pipelineType,
            MetalTransientNativeBatch nativeTransients,
            MetalCommandQueue? commandQueue = null)
            : base(device, pipelineType)
        {
            m_NativeArgumentTables = new SortedDictionary<uint, MTL4ArgumentTable>();
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
            ResetForPipelineBindings(pipelineLayout);
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
            ResetForPipelineBindings(pipelineLayout);
        }

        private void ResetForPipelineBindings(MetalPipelineLayout pipelineLayout)
        {
            RetireNativeArgumentTables();
            RetireDescriptorBuffers();
            m_RasterVertexBindings.Clear();

            MetalBindingTableLayout[] layouts = GetPipelineBindingTableLayouts(pipelineLayout);
            m_UsesReferenceBuffers = MetalBindingHelpers.RequiresReferenceBuffers(layouts);
            MetalBufferBindingPlanner.ValidatePipelineBufferBudget(layouts, PipelineType);

            if (m_UsesReferenceBuffers)
            {
                for (int index = 0; index < layouts.Length; ++index)
                {
                    layouts[index].ValidateReferenceBufferRanges();
                }

                CreateRootNativeArgumentTable(layouts);
            }
            else if (PipelineType == MetalBindingPipelineType.Raster)
            {
                if (layouts.Length == 1)
                {
                    _ = GetOrCreateBindingTable(layouts[0], layouts[0].Index);
                }
                else
                {
                    CreateRasterVertexOnlyNativeArgumentTable();
                }
            }
            else if (PipelineType == MetalBindingPipelineType.Raytracing && layouts.Length == 0)
            {
                CreateRayFunctionTableOnlyNativeArgumentTable();
            }
        }

        private static MetalBindingTableLayout[] GetPipelineBindingTableLayouts(MetalPipelineLayout pipelineLayout)
        {
            if (pipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(pipelineLayout));
            }

            ReadOnlySpan<MetalBindingTableLayout> sourceLayouts = pipelineLayout.BindingTableLayouts;
            if (sourceLayouts.Length == 0)
            {
                return Array.Empty<MetalBindingTableLayout>();
            }

            MetalBindingTableLayout[] layouts = sourceLayouts.ToArray();
            HashSet<uint> indices = new();
            for (int index = 0; index < sourceLayouts.Length; ++index)
            {
                MetalBindingTableLayout layout = layouts[index];
                if (layout.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        nameof(pipelineLayout),
                        $"Metal pipeline layout binding table {layout.Index} is disposed.");
                }

                if (!indices.Add(layout.Index))
                {
                    throw new InvalidOperationException(
                        $"Metal pipeline layout contains duplicate binding table index {layout.Index}.");
                }

                layouts[index] = layout;
            }

            return layouts;
        }

        protected override void OnBindingTableUpdated(MetalBindingTable resourceTable, in uint tableIndex)
        {
            if (m_UsesReferenceBuffers)
            {
                MTLBuffer descriptorBuffer = GetOrCreateDescriptorBuffer(resourceTable.BindingTableLayout, tableIndex);
                PopulateDescriptorBuffer(descriptorBuffer, resourceTable, m_CommandQueue);
                m_RootNativeArgumentTable.SetAddress(descriptorBuffer.GpuAddress, tableIndex);
                m_CommandQueue?.AddResidencyAllocation(descriptorBuffer);
                ApplyRasterVertexBufferBindingsToRoot();
            }
            else
            {
                MTL4ArgumentTable bindingTable = GetOrCreateBindingTable(resourceTable.BindingTableLayout, tableIndex);
                PopulateNativeArgumentTable(bindingTable, resourceTable);
                ApplyRasterVertexBufferBindings(bindingTable);
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
                if (m_RootNativeArgumentTable.NativePtr != IntPtr.Zero)
                {
                    ApplyRasterVertexBufferBinding(m_RootNativeArgumentTable, slot, m_RasterVertexBindings[slot]);
                }
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
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
                    m_RootNativeArgumentTable.SetTexture(
                        snapshot.ResourceId,
                        nativeIndex);
                }
                else
                {
                    foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in
                             m_NativeArgumentTables)
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
                // Native MTL4 API name (not RHI BindingTable).
                encoder.SetArgumentTable(m_RootNativeArgumentTable.NativePtr);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
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

                encoder.SetArgumentTable(m_RootNativeArgumentTable.NativePtr);
            }
            else
            {
                if (functionTable != null)
                {
                    PopulateRayFunctionTables(functionTable);
                }

                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
                {
                    encoder.SetArgumentTable(pair.Value.NativePtr);
                }
            }
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            ulong stages = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Vertex | ERHIStageMask.Fragment);

            if (m_UsesReferenceBuffers)
            {
                ApplyRasterVertexBufferBindingsToRoot();
                encoder.SetArgumentTable(m_RootNativeArgumentTable.NativePtr, stages);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
                {
                    ApplyRasterVertexBufferBindings(pair.Value);
                    encoder.SetArgumentTable(pair.Value.NativePtr, stages);
                }
            }
        }

        protected override void DisposeBackend()
        {
            ReleaseNativeArgumentTables();
            ReleaseDescriptorBuffers();
            m_RasterVertexBindings.Clear();
        }

        // Direct mode: one scalar-only physical table.

        private MTL4ArgumentTable GetOrCreateBindingTable(MetalBindingTableLayout layout, in uint tableIndex)
        {
            if (m_NativeArgumentTables.TryGetValue(tableIndex, out MTL4ArgumentTable cachedTable))
            {
                return cachedTable;
            }

            ulong maxBufferCount =
                MetalBufferBindingPlanner.GetDirectBindingTableBufferBindCount(layout, PipelineType);
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
            MTL4ArgumentTable bindingTable;
            try
            {
                bindingTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }
            if (bindingTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"newArgumentTableWithDescriptor failed: {errorText}");
            }

            m_NativeArgumentTables.Add(tableIndex, bindingTable);
            return bindingTable;
        }

        private void CreateRasterVertexOnlyNativeArgumentTable()
        {
            CreateReservedBufferOnlyNativeArgumentTable("raster vertex", supportsAttributeStrides: true);
        }

        private void CreateRayFunctionTableOnlyNativeArgumentTable()
        {
            CreateReservedBufferOnlyNativeArgumentTable("ray function-table", supportsAttributeStrides: false);
        }

        private void CreateReservedBufferOnlyNativeArgumentTable(
            string purpose,
            in bool supportsAttributeStrides)
        {
            const uint tableIndex = 0;
            if (m_NativeArgumentTables.ContainsKey(tableIndex))
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
            MTL4ArgumentTable bindingTable;
            try
            {
                bindingTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }

            if (bindingTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException(
                    $"Failed to create Metal 4 {purpose} binding table: {errorText}");
            }

            m_NativeArgumentTables.Add(tableIndex, bindingTable);
        }

        private void PopulateNativeArgumentTable(MTL4ArgumentTable bindingTable, MetalBindingTable table)
        {
            table.ValidateRequiredBindings();
            ReadOnlySpan<MetalBindInfo> binds = table.BindingTableLayout.BindInfos;
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
                                bindingTable.SetAddress(binding.BufferAddress, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                bindingTable.SetAddress(0, slotIndex);
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
                                bindingTable.SetTexture(binding.ResourceId, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                bindingTable.SetTexture(default, slotIndex);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (binding.IsBound)
                            {
                                bindingTable.SetSamplerState(binding.ResourceId, slotIndex);
                            }
                            else
                            {
                                bindingTable.SetSamplerState(default, slotIndex);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (binding.IsBound)
                            {
                                bindingTable.SetResource(binding.ResourceId, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(binding.ResidencyAllocation);
                            }
                            else
                            {
                                bindingTable.SetResource(default, slotIndex);
                            }

                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Metal binding table {table.BindingTableLayout.Index} contains unsupported binding type {bind.Type}.");
                    }
                }
            }
        }

        private void PopulateRayFunctionTables(MetalFunctionTable functionTable)
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
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

        // Reference-buffer mode: one packed buffer per physical table and one root binding table.

        private void CreateRootNativeArgumentTable(ReadOnlySpan<MetalBindingTableLayout> layouts)
        {
            // The root binding table holds one buffer slot per set (for descriptor buffer GPU addresses),
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
                m_RootNativeArgumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(descriptor.NativePtr);
            }
            if (m_RootNativeArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create multi-set root binding table: {errorText}");
            }
        }

        private MTLBuffer GetOrCreateDescriptorBuffer(MetalBindingTableLayout layout, in uint tableIndex)
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

        private static void PopulateDescriptorBuffer(MTLBuffer buffer, MetalBindingTable table, MetalCommandQueue? commandQueue)
        {
            table.ValidateRequiredBindings();
            IntPtr ptr = buffer.Contents;
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Metal descriptor buffer for table {table.BindingTableLayout.Index} is not CPU-addressable.");
            }

            ReadOnlySpan<MetalBindInfo> binds = table.BindingTableLayout.BindInfos;

            for (ulong entryIndex = 0; entryIndex < table.BindingTableLayout.ReferenceBufferElementCount; ++entryIndex)
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
                                $"Metal binding table {table.BindingTableLayout.Index} contains unsupported binding type {bind.Type}.");
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
                m_RootNativeArgumentTable.SetResource(functionTable.IntersectionFunctionTable.GpuResourceID, MetalBindingHelpers.RtIntersectionFunctionTableSlot);
            }

            if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_RootNativeArgumentTable.SetResource(functionTable.VisibleFunctionTable.GpuResourceID, MetalBindingHelpers.RtVisibleFunctionTableSlot);
            }
        }

        // Raster vertex buffer helpers

        private void ApplyRasterVertexBufferBindings(MTL4ArgumentTable bindingTable)
        {
            foreach (KeyValuePair<uint, RasterVertexBinding> pair in m_RasterVertexBindings)
            {
                ApplyRasterVertexBufferBinding(bindingTable, pair.Key, pair.Value);
            }
        }

        private void ApplyRasterVertexBufferBindingsToRoot()
        {
            if (m_RootNativeArgumentTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ApplyRasterVertexBufferBindings(m_RootNativeArgumentTable);
        }

        private static void ApplyRasterVertexBufferBinding(MTL4ArgumentTable bindingTable, in uint slot, in RasterVertexBinding binding)
        {
            if (binding.Stride > 0)
            {
                bindingTable.SetAddress(binding.Address, binding.Stride, slot);
            }
            else
            {
                bindingTable.SetAddress(binding.Address, slot);
            }
        }

        // Lifecycle helpers

        private void ReleaseNativeArgumentTables()
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value.NativePtr);
                }
            }

            m_NativeArgumentTables.Clear();
        }

        private void RetireNativeArgumentTables()
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_NativeArgumentTables)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    m_NativeTransients.RetainOwnership(
                        pair.Value.NativePtr);
                }
            }

            m_NativeArgumentTables.Clear();
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

            if (m_RootNativeArgumentTable.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_RootNativeArgumentTable.NativePtr);
                m_RootNativeArgumentTable = default;
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

            if (m_RootNativeArgumentTable.NativePtr != IntPtr.Zero)
            {
                m_NativeTransients.RetainOwnership(
                    m_RootNativeArgumentTable.NativePtr);
                m_RootNativeArgumentTable = default;
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
            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Transfer));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Transfer));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Transfer));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Transfer));
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                RHITimestampDescriptor? timestamp = m_PassDescriptor.Timestamp;

                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;

                if (timestamp.HasValue)
                {
                    MetalCommandBuffer metalCommandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
                    MetalQuery query = timestamp.Value.Query as MetalQuery
                        ?? throw new InvalidOperationException("Metal transfer timestamp pass requires a MetalQuery.");
                    query.WriteTimestamp(metalCommandBuffer, timestamp.Value.EndIndex);
                }
            }

            m_PassDescriptor = default;
            commandBuffer.MarkEncoderEndFromEncoder();
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
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute);
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

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Compute pipeline must be set before binding resource tables.");
            }

            MetalBindingTable table = resourceTable as MetalBindingTable
                ?? throw new ArgumentException("Metal compute binding requires a MetalBindingTable.", nameof(resourceTable));
            ((MetalCommandBuffer)m_CommandBuffer!).MarkNativeArgumentTablesUsed();
            m_BindingBackend.SetBindingTable(table, tableIndex);
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute));
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The compute encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Compute);

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
            commandBuffer.MarkEncoderEndFromEncoder();
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
                m_BindingBackend = new MetalBindingTableBindingBackend(
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

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before binding resource tables.");
            }

            MetalBindingTable table = resourceTable as MetalBindingTable
                ?? throw new ArgumentException("Metal ray tracing binding requires a MetalBindingTable.", nameof(resourceTable));
            if (m_BindingBackend.UsesReservedRayFunctionTableSlots && MetalBindingHelpers.HasRayFunctionTableSlotConflict(table))
            {
                throw new InvalidOperationException($"Ray tracing resource table conflicts with reserved Metal function-table slots ({MetalBindingHelpers.RtVisibleFunctionTableSlot}/{MetalBindingHelpers.RtIntersectionFunctionTableSlot}). Use another slot.");
            }

            m_BindingBackend.SetBindingTable(table, tableIndex);
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.RayTracing));
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

            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.RayTracing));
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The ray-tracing encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.RayTracing);

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
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal void WaitForFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(WaitForFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.RayTracing);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.RayTracing);
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
                m_BindingBackend = new MetalBindingTableBindingBackend(
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

    #region RasterPass
internal readonly struct MetalRasterCapabilities
    {
        internal bool ColorOutputMapping { get; }
        internal bool FramebufferLocalRead { get; }
        internal bool RasterOrderGroups { get; }
        internal bool PassMappingSelector { get; }
        internal bool MapEntrySelector { get; }
        internal bool EncoderMappingSelector { get; }

        internal MetalRasterCapabilities(
            bool colorOutputMapping,
            bool framebufferLocalRead,
            bool rasterOrderGroups,
            bool passMappingSelector = true,
            bool mapEntrySelector = true,
            bool encoderMappingSelector = true)
        {
            ColorOutputMapping = colorOutputMapping;
            FramebufferLocalRead = framebufferLocalRead;
            RasterOrderGroups = rasterOrderGroups;
            PassMappingSelector = passMappingSelector;
            MapEntrySelector = mapEntrySelector;
            EncoderMappingSelector = encoderMappingSelector;
        }

        internal static MetalRasterCapabilities FromSelectorProbes(
            bool passMappingSelector,
            bool mapEntrySelector,
            bool encoderMappingSelector,
            bool rasterOrderGroups)
        {
            bool completeMappingMechanism =
                passMappingSelector &&
                mapEntrySelector &&
                encoderMappingSelector;
            return new MetalRasterCapabilities(
                colorOutputMapping: completeMappingMechanism,
                framebufferLocalRead: completeMappingMechanism,
                rasterOrderGroups,
                passMappingSelector,
                mapEntrySelector,
                encoderMappingSelector);
        }
    }

    internal readonly struct MetalRasterSubPassLowering
    {
        internal byte LocalInputMask { get; }
        internal byte OrdinaryOutputMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PreserveMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool HasLocalInputs => LocalInputMask != 0;
        internal bool HasNonIdentityOutputMapping { get; }
        internal bool RequiresColorAttachmentMap =>
            HasLocalInputs || HasNonIdentityOutputMapping;
        internal int LocalInputSlotCount =>
            m_LocalInputPhysicalAttachments.Length;
        internal int OutputLocationCount =>
            m_OutputPhysicalAttachments.Length;

        private readonly int[] m_LocalInputPhysicalAttachments;
        private readonly int[] m_OutputPhysicalAttachments;
        private readonly ulong[] m_PhysicalAttachmentsByMappingIndex;

        internal MetalRasterSubPassLowering(
            in RHIRasterSubPassPlan plan,
            byte passRasterOrderedMask)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            byte phaseRasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            LocalInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~phaseRasterOrderedMask));
            OrdinaryOutputMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~phaseRasterOrderedMask));
            RasterOrderedMask = phaseRasterOrderedMask;
            PreserveMask = plan.PreserveMask;
            DepthStencilFlags = attachmentInterface.DepthStencilFlags;

            m_LocalInputPhysicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            Array.Fill(
                m_LocalInputPhysicalAttachments,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            m_OutputPhysicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            Array.Fill(
                m_OutputPhysicalAttachments,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            m_PhysicalAttachmentsByMappingIndex =
                new ulong[RHIAttachmentIndexArray.MaxAttachments];
            Array.Fill(
                m_PhysicalAttachmentsByMappingIndex,
                ulong.MaxValue);

            for (int inputIndex = 0;
                 inputIndex < m_LocalInputPhysicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0 ||
                    IsMaskSet(
                        phaseRasterOrderedMask,
                        logicalAttachment))
                {
                    continue;
                }

                m_LocalInputPhysicalAttachments[inputIndex] =
                    logicalAttachment;
                AddMapping(
                    m_PhysicalAttachmentsByMappingIndex,
                    inputIndex,
                    logicalAttachment,
                    "framebuffer-local input");
            }

            bool hasNonIdentityOutputMapping = false;
            for (int outputLocation = 0;
                 outputLocation < m_OutputPhysicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (logicalAttachment < 0)
                {
                    continue;
                }

                if (IsMaskSet(
                        phaseRasterOrderedMask,
                        logicalAttachment))
                {
                    hasNonIdentityOutputMapping = true;
                    continue;
                }

                if (IsMaskSet(
                        passRasterOrderedMask,
                        logicalAttachment))
                {
                    throw new NotSupportedException(
                        $"Metal output location {outputLocation} targets " +
                        $"logical attachment {logicalAttachment}, which is " +
                        "raster-ordered in another phase. A Metal ROG " +
                        "texture must never also be an ordinary color " +
                        "attachment in the same render pass.");
                }

                m_OutputPhysicalAttachments[outputLocation] =
                    logicalAttachment;
                AddMapping(
                    m_PhysicalAttachmentsByMappingIndex,
                    outputLocation,
                    logicalAttachment,
                    "color output");
                hasNonIdentityOutputMapping |=
                    outputLocation != logicalAttachment;
            }
            HasNonIdentityOutputMapping =
                hasNonIdentityOutputMapping;
        }

        internal int GetLocalInputPhysicalAttachment(
            int inputIndex)
        {
            if ((uint)inputIndex >=
                (uint)m_LocalInputPhysicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return m_LocalInputPhysicalAttachments[inputIndex];
        }

        internal ulong GetPhysicalOutputAttachmentIndex(
            int outputLocation)
        {
            if ((uint)outputLocation >=
                (uint)m_OutputPhysicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputLocation));
            }
            int physicalAttachment =
                m_OutputPhysicalAttachments[outputLocation];
            return physicalAttachment < 0
                ? ulong.MaxValue
                : checked((ulong)physicalAttachment);
        }

        internal ulong GetPhysicalAttachmentForMappingIndex(
            int mappingIndex)
        {
            if ((uint)mappingIndex >=
                (uint)m_PhysicalAttachmentsByMappingIndex.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mappingIndex));
            }
            return m_PhysicalAttachmentsByMappingIndex[mappingIndex];
        }

        private static void AddMapping(
            ulong[] mappings,
            int mappingIndex,
            int physicalAttachment,
            string purpose)
        {
            ulong physical = checked((ulong)physicalAttachment);
            ulong existing = mappings[mappingIndex];
            if (existing != ulong.MaxValue &&
                existing != physical)
            {
                throw new NotSupportedException(
                    $"Metal cannot independently map {purpose} index " +
                    $"{mappingIndex} to physical attachment {physical} " +
                    $"because the same native mapping index already targets " +
                    $"{existing}.");
            }
            mappings[mappingIndex] = physical;
        }

        private static bool IsMaskSet(
            byte mask,
            int logicalAttachment) =>
            (mask & (1 << logicalAttachment)) != 0;
    }

    internal sealed class MetalRasterPassLowering
    {
        internal bool RequiresColorAttachmentMapping { get; }
        internal bool RequiresFramebufferLocalRead { get; }
        internal bool RequiresRasterOrderGroups { get; }
        internal byte OrdinaryAttachmentMask { get; }
        internal byte RasterOrderedAttachmentMask { get; }
        internal ReadOnlyMemory<MetalRasterSubPassLowering> SubPasses =>
            m_SubPasses;

        private readonly MetalRasterSubPassLowering[] m_SubPasses;

        private MetalRasterPassLowering(
            bool requiresColorAttachmentMapping,
            bool requiresFramebufferLocalRead,
            bool requiresRasterOrderGroups,
            byte ordinaryAttachmentMask,
            byte rasterOrderedAttachmentMask,
            MetalRasterSubPassLowering[] subPasses)
        {
            RequiresColorAttachmentMapping =
                requiresColorAttachmentMapping;
            RequiresFramebufferLocalRead =
                requiresFramebufferLocalRead;
            RequiresRasterOrderGroups = requiresRasterOrderGroups;
            OrdinaryAttachmentMask = ordinaryAttachmentMask;
            RasterOrderedAttachmentMask =
                rasterOrderedAttachmentMask;
            m_SubPasses = subPasses;
        }

        internal static MetalRasterPassLowering Compile(
            RHIRasterPassPlan plan,
            in MetalRasterCapabilities capabilities)
        {
            ArgumentNullException.ThrowIfNull(plan);

            byte passRasterOrderedMask = 0;
            for (int i = 0; i < plan.SubPassCount; ++i)
            {
                passRasterOrderedMask |=
                    plan.GetSubPass(i)
                        .AttachmentInterface
                        .RasterOrderedReadWriteMask;
            }

            bool requiresOutputMapping = false;
            bool requiresFramebufferLocalRead = false;
            byte ordinaryAttachmentMask = 0;
            MetalRasterSubPassLowering[] subPasses =
                new MetalRasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RHIRasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                ValidateMetalAccess(in subPass, i);
                MetalRasterSubPassLowering lowering =
                    new MetalRasterSubPassLowering(
                        subPass,
                        passRasterOrderedMask);
                subPasses[i] = lowering;
                requiresOutputMapping |=
                    lowering.HasNonIdentityOutputMapping;
                requiresFramebufferLocalRead |=
                    lowering.HasLocalInputs;
                ordinaryAttachmentMask |= checked((byte)(
                    lowering.LocalInputMask |
                    lowering.OrdinaryOutputMask |
                    lowering.PreserveMask));
            }

            ordinaryAttachmentMask = checked((byte)(
                ordinaryAttachmentMask &
                ~passRasterOrderedMask));
            bool requiresMapping =
                requiresOutputMapping ||
                requiresFramebufferLocalRead;
            if (requiresOutputMapping &&
                !capabilities.ColorOutputMapping)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires logical-to-physical " +
                    "color-output mapping, but the exact native mapping " +
                    "mechanism was not independently probed.");
            }
            if (requiresFramebufferLocalRead &&
                !capabilities.FramebufferLocalRead)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires framebuffer-local color " +
                    "reads, but the exact MSL [[color(n)]] attachment-read " +
                    "mechanism was not independently probed.");
            }
            if (passRasterOrderedMask != 0 &&
                !capabilities.RasterOrderGroups)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires raster order groups, but " +
                    "MTLDevice.rasterOrderGroupsSupported is false.");
            }
            if (requiresMapping &&
                (!capabilities.PassMappingSelector ||
                 !capabilities.MapEntrySelector ||
                 !capabilities.EncoderMappingSelector))
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires attachment mapping, but " +
                    "one or more required native selectors are unavailable.");
            }

            ValidateMemorylessAttachments(plan);
            return new MetalRasterPassLowering(
                requiresMapping,
                requiresFramebufferLocalRead,
                passRasterOrderedMask != 0,
                ordinaryAttachmentMask,
                passRasterOrderedMask,
                subPasses);
        }

        private static void ValidateMetalAccess(
            in RHIRasterSubPassPlan subPass,
            int subPassIndex)
        {
            if (subPass.AttachmentInterface.SampledFeedbackMask != 0)
            {
                throw new NotSupportedException(
                    $"Metal subpass {subPassIndex} cannot prove sampled " +
                    "attachment feedback. SampledFeedback remains an " +
                    "ordinary shader resource and has no private Metal " +
                    "attachment binding until an exact native strategy is " +
                    "qualified.");
            }
        }

        private static void ValidateMemorylessAttachments(
            RHIRasterPassPlan plan)
        {
            for (int i = 0; i < plan.ColorAttachmentCount; ++i)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(i);
                if (attachment.RenderTarget.Descriptor.StorageMode !=
                    ERHIStorageMode.Memoryless)
                {
                    continue;
                }

                if (attachment.LoadAction == ERHILoadAction.Load)
                {
                    throw new NotSupportedException(
                        $"Memoryless color attachment {i} cannot use Load.");
                }
                if (attachment.StoreAction != ERHIStoreAction.DontCare &&
                    attachment.StoreAction != ERHIStoreAction.Resolve)
                {
                    throw new NotSupportedException(
                        $"Memoryless color attachment {i} must use DontCare " +
                        "or Resolve store action.");
                }
            }

            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }

            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            if (depthStencil.RenderTarget.Descriptor.StorageMode !=
                ERHIStorageMode.Memoryless)
            {
                return;
            }
            if (depthStencil.DepthLoadOp == ERHILoadAction.Load ||
                depthStencil.StencilLoadOp == ERHILoadAction.Load)
            {
                throw new NotSupportedException(
                    "A memoryless depth/stencil attachment cannot use Load.");
            }
            if (!IsMemorylessStoreAction(depthStencil.DepthStoreOp) ||
                !IsMemorylessStoreAction(depthStencil.StencilStoreOp))
            {
                throw new NotSupportedException(
                    "A memoryless depth/stencil attachment must use DontCare " +
                    "or Resolve independently for each selected aspect.");
            }
        }

        private static bool IsMemorylessStoreAction(
            ERHIStoreAction storeAction) =>
            storeAction == ERHIStoreAction.DontCare ||
            storeAction == ERHIStoreAction.Resolve;
    }

    internal readonly struct MetalVertexBufferBinding
    {
        internal uint LogicalIndex { get; }
        internal ulong PhysicalIndex { get; }
        internal uint Stride { get; }

        internal MetalVertexBufferBinding(in uint logicalIndex, in ulong physicalIndex, in uint stride)
        {
            LogicalIndex = logicalIndex;
            PhysicalIndex = physicalIndex;
            Stride = stride;
        }
    }

    internal sealed class MetalRasterBufferBindingPlan
    {
        private readonly Dictionary<uint, MetalVertexBufferBinding> m_VertexBindings;

        internal IReadOnlyDictionary<uint, MetalVertexBufferBinding> VertexBindings => m_VertexBindings;

        internal MetalRasterBufferBindingPlan(
            Dictionary<uint, MetalVertexBufferBinding> vertexBindings)
        {
            m_VertexBindings = vertexBindings;
        }

        internal MetalVertexBufferBinding GetVertexBinding(in uint logicalIndex)
        {
            if (m_VertexBindings.TryGetValue(logicalIndex, out MetalVertexBufferBinding binding))
            {
                return binding;
            }

            throw new InvalidOperationException(
                $"Metal raster pipeline has no vertex-buffer layout for logical slot {logicalIndex}.");
        }
    }

    internal static class MetalBufferBindingPlanner
    {
        internal const int MaxBufferBindCount = 31;
        internal const int MaxBufferIndex = MaxBufferBindCount - 1;

        internal static void ValidatePipelineBufferBudget(
            ReadOnlySpan<MetalBindingTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }
        }

        internal static MetalRasterBufferBindingPlan CompileRaster(
            ReadOnlySpan<MetalBindingTableLayout> layouts,
            ReadOnlySpan<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            Dictionary<uint, MetalVertexBufferBinding> bindings = new(vertexLayouts.Length);

            int nextPhysicalIndex = MaxBufferIndex;
            for (int layoutIndex = 0; layoutIndex < vertexLayouts.Length; ++layoutIndex)
            {
                ref readonly RHIVertexLayoutDescriptor layout = ref vertexLayouts[layoutIndex];
                if (bindings.ContainsKey(layout.Index))
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline contains duplicate vertex-buffer logical slot {layout.Index}.");
                }

                while (nextPhysicalIndex >= 0 && occupied[nextPhysicalIndex])
                {
                    --nextPhysicalIndex;
                }

                if (nextPhysicalIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline requires {vertexLayouts.Length} vertex buffers, but the shared "
                        + $"Metal 4 buffer namespace has no free slot in [0, {MaxBufferIndex}] after shader resources.");
                }

                occupied[nextPhysicalIndex] = true;
                bindings.Add(
                    layout.Index,
                    new MetalVertexBufferBinding(
                        layout.Index,
                        checked((ulong)nextPhysicalIndex),
                        layout.Stride));
                --nextPhysicalIndex;
            }

            return new MetalRasterBufferBindingPlan(bindings);
        }

        internal static ulong GetDirectBindingTableBufferBindCount(
            MetalBindingTableLayout layout,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            ReserveDirectBufferBindings(occupied, layout);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReferenceRootBufferBindCount(
            ReadOnlySpan<MetalBindingTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildReferenceBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReservedBufferOnlyBindCount(in MetalBindingPipelineType pipelineType)
        {
            return pipelineType switch
            {
                MetalBindingPipelineType.Raster or MetalBindingPipelineType.Raytracing =>
                    MaxBufferBindCount,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pipelineType),
                    pipelineType,
                    "Only Metal raster and ray tracing pipelines use reserved-only binding tables.")
            };
        }

        private static bool[] BuildPipelineBufferOccupancy(
            ReadOnlySpan<MetalBindingTableLayout> layouts)
        {
            if (MetalBindingHelpers.RequiresReferenceBuffers(layouts))
            {
                return BuildReferenceBufferOccupancy(layouts);
            }

            bool[] occupied = new bool[MaxBufferBindCount];
            if (layouts.Length == 1)
            {
                ReserveDirectBufferBindings(occupied, layouts[0]);
            }

            return occupied;
        }

        private static bool[] BuildReferenceBufferOccupancy(
            ReadOnlySpan<MetalBindingTableLayout> layouts)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            for (int index = 0; index < layouts.Length; ++index)
            {
                MetalBindingTableLayout layout = layouts[index];
                Reserve(
                    occupied,
                    layout.Index,
                    $"argument-table reference buffer {layout.Index}");
            }

            return occupied;
        }

        private static void ReserveDirectBufferBindings(
            bool[] occupied,
            MetalBindingTableLayout layout)
        {
            ReadOnlySpan<MetalBindInfo> binds = layout.BindInfos;
            for (int index = 0; index < binds.Length; ++index)
            {
                ref readonly MetalBindInfo bind = ref binds[index];
                if (MetalBindingTableValidation.GetDirectBindingNamespace(bind.Type)
                    != MetalBindingTableValidation.BindingNamespace.Buffer)
                {
                    continue;
                }

                ulong end = checked((ulong)bind.Slot + bind.Count);
                if (end > MaxBufferBindCount)
                {
                    throw new InvalidOperationException(
                        $"Metal binding table {layout.Index} binding slot={bind.Slot}, type={bind.Type}, count={bind.Count} "
                        + $"exceeds the Metal 4 buffer index range [0, {MaxBufferIndex}].");
                }

                for (ulong physicalIndex = bind.Slot; physicalIndex < end; ++physicalIndex)
                {
                    Reserve(
                        occupied,
                        physicalIndex,
                        $"binding table {layout.Index} {bind.Type} binding");
                }
            }
        }

        private static void Reserve(bool[] occupied, in ulong physicalIndex, string owner)
        {
            if (physicalIndex > MaxBufferIndex)
            {
                throw new InvalidOperationException(
                    $"Metal {owner} uses buffer index {physicalIndex}, outside the Metal 4 range [0, {MaxBufferIndex}].");
            }

            int index = checked((int)physicalIndex);
            if (occupied[index])
            {
                throw new InvalidOperationException(
                    $"Metal {owner} collides at shared Metal 4 buffer index {physicalIndex}.");
            }

            occupied[index] = true;
        }

        private static ulong GetRequiredBindCount(bool[] occupied)
        {
            for (int index = occupied.Length - 1; index >= 0; --index)
            {
                if (occupied[index])
                {
                    return checked((ulong)index + 1UL);
                }
            }

            return 0;
        }
    }

    #endregion

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
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;

            MetalCommandBuffer commandBuffer =
                (MetalCommandBuffer)m_CommandBuffer!;
            MetalDevice device =
                ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            MetalTransientNativeBatch nativeTransients =
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
            m_PendingPassDescriptor = plan.DescriptorSnapshot;
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
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Vertex | ERHIStageMask.Fragment);
            MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
            encoder4.WaitForFence(fence, stage);
        }

        internal void SignalFence(in MTLFence fence)
        {
            ValidateFenceAndEncoder(fence, nameof(SignalFence));
            ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Vertex | ERHIStageMask.Fragment);
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
                ulong stages = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Vertex | ERHIStageMask.Fragment);
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

        public override void NextSubPass()
        {
            ThrowIfDisposed();
            RHIRasterPassPlan plan = RequireActiveRasterPass();
            int sourceSubPassIndex = m_CurrentSubPassIndex;
            int destinationSubPassIndex = sourceSubPassIndex + 1;
            if (destinationSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            RequireEncoderForState(nameof(NextSubPass));

            ulong fragmentStages =
                MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Fragment);
            new MTL4CommandEncoder(m_NativeEncoder4.NativePtr)
                .BarrierAfterEncoderStages(
                    fragmentStages,
                    fragmentStages,
                    MTL4VisibilityOptions.Device);
            ApplyColorAttachmentMap(destinationSubPassIndex);
            m_CurrentSubPassIndex = destinationSubPassIndex;
            m_PipelineSubPassIndex = -1;
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

        public override unsafe void SetScissors(in Memory<Rect> rects)
        {
            RequireEncoderForState("SetScissors");
            if (rects.Length == 0)
            {
                throw new ArgumentException("At least one scissor rect is required.", nameof(rects));
            }

            const int MaxMetalScissorRects = 16;
            if (rects.Length > MaxMetalScissorRects)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rects),
                    $"Metal supports at most {MaxMetalScissorRects} scissor rects; got {rects.Length}.");
            }

            Span<Rect> rectSpan = rects.Span;
            Span<MTLScissorRect> nativeRects = stackalloc MTLScissorRect[rectSpan.Length];
            for (int i = 0; i < rectSpan.Length; ++i)
            {
                Rect rect = rectSpan[i];
                nativeRects[i] = new MTLScissorRect
                {
                    x = (ulong)Math.Max(0, rect.left),
                    y = (ulong)Math.Max(0, rect.top),
                    width = (ulong)Math.Max(0, rect.right - rect.left),
                    height = (ulong)Math.Max(0, rect.bottom - rect.top)
                };
            }

            fixed (MTLScissorRect* ptr = nativeRects)
            {
                m_NativeEncoder4.SetScissorRects((IntPtr)ptr, (ulong)rectSpan.Length);
            }
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

        public override unsafe void SetViewports(in Memory<Viewport> viewports)
        {
            RequireEncoderForState("SetViewports");
            if (viewports.Length == 0)
            {
                throw new ArgumentException("At least one viewport is required.", nameof(viewports));
            }

            const int MaxMetalViewports = 16;
            if (viewports.Length > MaxMetalViewports)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(viewports),
                    $"Metal supports at most {MaxMetalViewports} viewports; got {viewports.Length}.");
            }

            Span<Viewport> viewportSpan = viewports.Span;
            Span<MTLViewport> nativeViewports = stackalloc MTLViewport[viewportSpan.Length];
            for (int i = 0; i < viewportSpan.Length; ++i)
            {
                Viewport viewport = viewportSpan[i];
                nativeViewports[i] = new MTLViewport
                {
                    originX = viewport.TopLeftX,
                    originY = viewport.TopLeftY,
                    width = viewport.Width,
                    height = viewport.Height,
                    znear = viewport.MinDepth,
                    zfar = viewport.MaxDepth
                };
            }

            fixed (MTLViewport* ptr = nativeViewports)
            {
                m_NativeEncoder4.SetViewports((IntPtr)ptr, (ulong)viewportSpan.Length);
            }
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

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Raster pipeline must be set before binding resource tables.");
            }

            MetalBindingTable table = resourceTable as MetalBindingTable
                ?? throw new ArgumentException("Metal raster binding requires a MetalBindingTable.", nameof(resourceTable));
            m_BindingBackend.SetBindingTable(table, tableIndex);
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
            MetalDevice device = ((MetalCommandQueue)((MetalCommandBuffer)m_CommandBuffer!).CommandQueue).MetalDevice;
            device.Capabilities.Mesh.Shader.Require("Metal mesh shaders");
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            MetalDevice device = ((MetalCommandQueue)((MetalCommandBuffer)m_CommandBuffer!).CommandQueue).MetalDevice;
            device.Capabilities.Mesh.Shader.Require("Metal mesh shaders");
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + argsOffset;
            m_NativeEncoder4.DrawMeshThreadgroupsWithIndirectBuffer(indirectAddress, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            MarkRasterStagesSeen();
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The raster encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Raster);
            RHIRasterPassPlan plan = RequireActiveRasterPass();
            if (m_CurrentSubPassIndex != plan.SubPassCount - 1)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' ended at subpass {m_CurrentSubPassIndex}, " +
                    $"but {plan.SubPassCount} subpasses were declared.");
            }

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
            commandBuffer.MarkEncoderEndFromEncoder();
            ClearRasterPassState();
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
                m_BindingBackend = new MetalBindingTableBindingBackend(
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

                if (commandBuffer.CommandQueue is MetalCommandQueue metalQueue)
                {
                    metalQueue.AddResidencyAllocation(
                        new MTLAllocation(colorTexture.NativeTexture.NativePtr));
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
                RHIRasterPassPlan plan,
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
            RHIRasterPassPlan plan,
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
            ConvertDepthResolveFilter(ERHIResolveMode mode)
        {
            return mode switch
            {
                ERHIResolveMode.Min => MTLMultisampleDepthResolveFilter.Min,
                ERHIResolveMode.Max => MTLMultisampleDepthResolveFilter.Max,
                _ => MTLMultisampleDepthResolveFilter.Sample0,
            };
        }

        private static MTLMultisampleStencilResolveFilter
            ConvertStencilResolveFilter(ERHIResolveMode mode)
        {
            if (mode != ERHIResolveMode.None && mode != ERHIResolveMode.Sample0)
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
                MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Vertex | ERHIStageMask.Fragment));
        }
    }

    // ========== WorkGraph Encoder ==========
}

namespace SharpGPU
{
    /// <summary>
    /// Owns only HAL-private Objective-C objects referenced by one encoded
    /// Metal command-buffer recording. The caller's fence discipline makes
    /// the next command-buffer Begin (or Dispose) the reclaim boundary.
    /// </summary>
    internal sealed class MetalTransientNativeBatch : IDisposable
    {
        private readonly Action<IntPtr> m_Release;
        private readonly List<IntPtr> m_NativeObjects;
        private bool m_IsDisposed;

        internal int Count => m_NativeObjects.Count;

        internal MetalTransientNativeBatch()
            : this(static nativeObject =>
                ObjectiveCRuntime.Release(nativeObject))
        {
        }

        internal MetalTransientNativeBatch(Action<IntPtr> release)
        {
            m_Release = release ??
                throw new ArgumentNullException(nameof(release));
            m_NativeObjects = new List<IntPtr>();
        }

        internal void RetainOwnership(IntPtr nativeObject)
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            if (nativeObject == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "A native transient must have a non-zero pointer.",
                    nameof(nativeObject));
            }
            m_NativeObjects.Add(nativeObject);
        }

        internal int CaptureCheckpoint()
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            return m_NativeObjects.Count;
        }

        internal void RollbackTo(int checkpoint)
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            if ((uint)checkpoint > (uint)m_NativeObjects.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            for (int i = m_NativeObjects.Count - 1;
                 i >= checkpoint;
                 --i)
            {
                m_Release(m_NativeObjects[i]);
            }
            m_NativeObjects.RemoveRange(
                checkpoint,
                m_NativeObjects.Count - checkpoint);
        }

        internal void ReleaseForCommandBufferReuse()
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            RollbackTo(0);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }
            RollbackTo(0);
            m_IsDisposed = true;
        }
    }

    #region MachineLearning
    internal sealed class MetalMLEncoder : RHIMLEncoder
    {
        private MTL4MachineLearningCommandEncoder m_NativeEncoder;
        private readonly MetalDevice m_MetalDevice;
        private RHIMLPassDescriptor m_PassDescriptor;

        public MetalMLEncoder(MetalCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_MetalDevice = ((MetalCommandQueue)cmdBuffer.CommandQueue).MetalDevice;
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
        {
            if (!m_MetalDevice.SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalDevice.MetalMLUnavailableReason ?? "Metal ML is not supported on this device.");
            }

            m_PassDescriptor = descriptor;
            m_NativeEncoder = default;
            m_CachedPipeline = null;
            m_CachedBindingTable = null;

            MTL4CommandBuffer mtl4CmdBuffer = ((MetalCommandBuffer)m_CommandBuffer!).EnsureMtl4CommandBuffer();
            m_NativeEncoder = mtl4CmdBuffer.MachineLearningCommandEncoder();

            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal ML requires a native MTL4MachineLearningCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
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
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal ML timestamp pass requires a MetalQuery.");
                query.WriteTimestamp((MetalCommandBuffer)m_CommandBuffer!, index);
            }
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline as MetalMLPipeline
                ?? throw new InvalidOperationException($"Metal ML encoder expects {nameof(MetalMLPipeline)} but got {pipeline?.GetType().Name ?? "<null>"}.");
            MetalMLPipeline metalPipeline = (MetalMLPipeline)m_CachedPipeline;
            if (metalPipeline.NativePipelineState.NativePtr != IntPtr.Zero)
            {
                if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
                {
                    queue.AddResidencyAllocation(new MTLAllocation(metalPipeline.NativePipelineState.NativePtr));
                }

                m_NativeEncoder.SetPipelineState(metalPipeline.NativePipelineState);
            }
        }

        public override void SetBindingTable(RHIMLBindingTable bindingTable)
        {
            MetalMLBindingTable metalBindingTable = bindingTable as MetalMLBindingTable
                ?? throw new InvalidOperationException($"Metal ML encoder expects {nameof(MetalMLBindingTable)} but got {bindingTable?.GetType().Name ?? "<null>"}.");
            m_CachedBindingTable = metalBindingTable;
            if (metalBindingTable.NativeArgumentTable.NativePtr != IntPtr.Zero)
            {
                TrackBindingResidency(metalBindingTable);
                // Native MTL4 ML API name (not RHI BindingTable).
                m_NativeEncoder.SetArgumentTable(metalBindingTable.NativeArgumentTable);
            }
        }

        public override void Dispatch()
        {
            if (m_CachedPipeline is not MetalMLPipeline)
            {
                throw new InvalidOperationException("Metal ML encoder requires SetPipeline before Dispatch.");
            }

            if (m_CachedBindingTable is not MetalMLBindingTable metalBindingTable)
            {
                throw new InvalidOperationException("Metal ML encoder requires SetBindingTable before Dispatch.");
            }

            m_NativeEncoder.DispatchNetworkWithIntermediatesHeap(metalBindingTable.NativeIntermediatesHeap);
            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHIStageMask.MachineLearning));
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The machine-learning encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.MachineLearning);

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
            m_CachedPipeline = null;
            m_CachedBindingTable = null;
            m_PassDescriptor = default;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release()
        {
        }

        private void TrackBindingResidency(MetalMLBindingTable bindingTable)
        {
            if (m_CommandBuffer?.CommandQueue is not MetalCommandQueue queue)
            {
                return;
            }

            for (int i = 0; i < bindingTable.Inputs.Length; ++i)
            {
                TrackTensorResidency(queue, bindingTable.Inputs[i]);
            }

            for (int i = 0; i < bindingTable.Outputs.Length; ++i)
            {
                TrackTensorResidency(queue, bindingTable.Outputs[i]);
            }

            if (bindingTable.NativeIntermediatesHeap.NativePtr != IntPtr.Zero)
            {
                queue.AddResidencyAllocation(bindingTable.NativeIntermediatesHeap);
            }
        }

        private static void TrackTensorResidency(MetalCommandQueue queue, MetalTensor tensor)
        {
            if (tensor.BackingBuffer != null)
            {
                queue.AddResidencyAllocation(tensor.BackingBuffer.NativeBuffer);
            }

            MTLBuffer nativeBuffer = tensor.NativeTensor.Buffer;
            if (nativeBuffer.NativePtr != IntPtr.Zero)
            {
                queue.AddResidencyAllocation(nativeBuffer);
            }
        }
    }
    #endregion
}
