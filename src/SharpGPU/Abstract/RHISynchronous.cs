using System;
using System.Threading;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum ERHIFenceStatus : byte
    {
        Success = 0,
        NotReady = 1,
        Undefined
    };

    public abstract class RHIFence : Disposal
    {
        internal object OwnerDevice
        {
            get;
        }

        private int m_State;

        protected RHIFence(object ownerDevice)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            m_State = (int)BinarySyncLifecycle.Ready;
        }

        public abstract ERHIFenceStatus Status
        {
            get;
        }

        public abstract void Reset();
        public abstract ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue);

        internal void ReserveSignal()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Pending,
                    (int)BinarySyncLifecycle.Ready) !=
                (int)BinarySyncLifecycle.Ready)
            {
                throw new InvalidOperationException(
                    "The fence must be Ready before it can be used as a completion fence.");
            }
        }

        internal void RollbackSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Ready,
                    (int)BinarySyncLifecycle.Pending) !=
                (int)BinarySyncLifecycle.Pending)
            {
                throw new InvalidOperationException("The fence signal reservation is not pending.");
            }
        }

        protected bool BeginReset()
        {
            ThrowIfSynchronizationDisposed();
            while (true)
            {
                BinarySyncLifecycle state =
                    (BinarySyncLifecycle)Volatile.Read(ref m_State);
                if (state == BinarySyncLifecycle.Ready)
                {
                    return false;
                }

                if (state != BinarySyncLifecycle.Signaled)
                {
                    throw new InvalidOperationException(
                        "A fence cannot be reset before its pending signal has completed.");
                }

                if (Interlocked.CompareExchange(
                        ref m_State,
                        (int)BinarySyncLifecycle.Resetting,
                        (int)BinarySyncLifecycle.Signaled) ==
                    (int)BinarySyncLifecycle.Signaled)
                {
                    return true;
                }
            }
        }

        protected void CompleteReset()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Ready,
                    (int)BinarySyncLifecycle.Resetting) !=
                (int)BinarySyncLifecycle.Resetting)
            {
                throw new InvalidOperationException("The fence reset operation is not active.");
            }
        }

        protected void RollbackReset()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Signaled,
                    (int)BinarySyncLifecycle.Resetting) !=
                (int)BinarySyncLifecycle.Resetting)
            {
                throw new InvalidOperationException("The fence reset operation is not active.");
            }
        }

        protected bool IsSignalPending
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                return (BinarySyncLifecycle)Volatile.Read(ref m_State) ==
                    BinarySyncLifecycle.Pending;
            }
        }

        protected bool IsSignalKnownComplete
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                return (BinarySyncLifecycle)Volatile.Read(ref m_State) ==
                    BinarySyncLifecycle.Signaled;
            }
        }

        protected void EnsureWaitable()
        {
            ThrowIfSynchronizationDisposed();
            BinarySyncLifecycle state =
                (BinarySyncLifecycle)Volatile.Read(ref m_State);
            if (state == BinarySyncLifecycle.Ready)
            {
                throw new InvalidOperationException("A fence cannot be waited before it has been submitted.");
            }

            if (state == BinarySyncLifecycle.Resetting)
            {
                throw new InvalidOperationException("A fence cannot be waited while it is being reset.");
            }
        }

        protected void MarkSignaled()
        {
            int prior = Interlocked.CompareExchange(
                ref m_State,
                (int)BinarySyncLifecycle.Signaled,
                (int)BinarySyncLifecycle.Pending);
            if (prior != (int)BinarySyncLifecycle.Pending &&
                prior != (int)BinarySyncLifecycle.Signaled)
            {
                throw new InvalidOperationException("The fence does not have a pending signal.");
            }
        }

        protected void ThrowIfSynchronizationDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (OwnerDevice is RHIDevice device)
            {
                device.ThrowIfDeviceUnavailable();
            }
        }
    }

    public abstract class RHISemaphore : Disposal
    {
        internal object OwnerDevice
        {
            get;
        }

        private int m_State;

        protected RHISemaphore(object ownerDevice)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            m_State = (int)BinarySyncLifecycle.Ready;
        }

        internal void ReserveSignal()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Pending,
                    (int)BinarySyncLifecycle.Ready) !=
                (int)BinarySyncLifecycle.Ready)
            {
                throw new InvalidOperationException(
                    "A binary semaphore cannot be signaled again before its prior signal has been consumed.");
            }
        }

        internal void CommitSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Signaled,
                    (int)BinarySyncLifecycle.Pending) !=
                (int)BinarySyncLifecycle.Pending)
            {
                throw new InvalidOperationException("The semaphore signal reservation is not pending.");
            }
        }

        internal void RollbackSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Ready,
                    (int)BinarySyncLifecycle.Pending) !=
                (int)BinarySyncLifecycle.Pending)
            {
                throw new InvalidOperationException("The semaphore signal reservation is not pending.");
            }
        }

        internal void ReserveWait()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Pending,
                    (int)BinarySyncLifecycle.Signaled) !=
                (int)BinarySyncLifecycle.Signaled)
            {
                throw new InvalidOperationException(
                    "A binary semaphore cannot be waited before a signal has been submitted or after it has been consumed.");
            }
        }

        internal void CommitWait()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Ready,
                    (int)BinarySyncLifecycle.Pending) !=
                (int)BinarySyncLifecycle.Pending)
            {
                throw new InvalidOperationException("The semaphore wait reservation is not pending.");
            }
        }

        internal void RollbackWait()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)BinarySyncLifecycle.Signaled,
                    (int)BinarySyncLifecycle.Pending) !=
                (int)BinarySyncLifecycle.Pending)
            {
                throw new InvalidOperationException("The semaphore wait reservation is not pending.");
            }
        }

        private void ThrowIfSynchronizationDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (OwnerDevice is RHIDevice device)
            {
                device.ThrowIfDeviceUnavailable();
            }
        }
    }

    #region Barriers
    [Flags]
    public enum ERHIStageMask : ulong
    {
        None = 0,
        Transfer = 1UL << 0,
        Copy = Transfer,
        Indirect = 1UL << 1,
        IndexInput = 1UL << 2,
        VertexInput = 1UL << 3,
        Vertex = 1UL << 4,
        Fragment = 1UL << 5,
        Compute = 1UL << 6,
        Task = 1UL << 7,
        Mesh = 1UL << 8,
        RayTracing = 1UL << 9,
        AccelStructBuild = 1UL << 10,
        AccelStructCopy = 1UL << 11,
        MachineLearning = 1UL << 12,
        AllGraphics = Indirect | IndexInput | VertexInput | Vertex | Fragment | Task | Mesh,
        AllShading = Vertex | Fragment | Compute | Task | Mesh | RayTracing | MachineLearning,
        All = ulong.MaxValue
    }

    [Flags]
    public enum ERHIAccessMask : ulong
    {
        None = 0,
        IndirectCommandRead = 1UL << 0,
        IndexRead = 1UL << 1,
        VertexRead = 1UL << 2,
        ConstantRead = 1UL << 3,
        ShaderRead = 1UL << 4,
        ShaderWrite = 1UL << 5,
        RenderTargetRead = 1UL << 6,
        RenderTargetWrite = 1UL << 7,
        DepthStencilRead = 1UL << 8,
        DepthStencilWrite = 1UL << 9,
        TransferRead = 1UL << 10,
        TransferWrite = 1UL << 11,
        ResolveRead = 1UL << 12,
        ResolveWrite = 1UL << 13,
        Present = 1UL << 14,
        ShadingRateRead = 1UL << 15,
        AccelStructRead = 1UL << 16,
        AccelStructWrite = 1UL << 17
    }

    public enum ERHITextureLayout : byte
    {
        Undefined = 0,
        General = 1,
        CopySource = 2,
        CopyDestination = 3,
        ShaderReadOnly = 4,
        RenderTarget = 5,
        DepthStencilReadOnly = 6,
        DepthStencilWrite = 7,
        ResolveSource = 8,
        ResolveDestination = 9,
        Present = 10,
        ShadingRateSurface = 11,
        Common = 12
    }

    [Flags]
    public enum ERHITextureAspectMask : byte
    {
        None = 0,
        Color = 1 << 0,
        Depth = 1 << 1,
        Stencil = 1 << 2
    }

    public struct RHIBufferRange
    {
        public const ulong WholeSize = ulong.MaxValue;

        public ulong Offset;
        public ulong Size;

        public static RHIBufferRange Whole()
        {
            RHIBufferRange range;
            range.Offset = 0;
            range.Size = WholeSize;
            return range;
        }

        public bool IsWholeRange => Offset == 0 && Size == WholeSize;
    }

    public struct RHITextureSubresourceRange
    {
        public const uint All = uint.MaxValue;

        public uint BaseMipLevel;
        public uint MipLevelCount;
        public uint BaseArrayLayer;
        public uint ArrayLayerCount;
        public ERHITextureAspectMask AspectMask;

        public static RHITextureSubresourceRange Whole(ERHITextureAspectMask aspectMask = ERHITextureAspectMask.Color)
        {
            RHITextureSubresourceRange range;
            range.BaseMipLevel = 0;
            range.MipLevelCount = All;
            range.BaseArrayLayer = 0;
            range.ArrayLayerCount = All;
            range.AspectMask = aspectMask;
            return range;
        }
    }

    public enum ERHIBarrierKind : byte
    {
        Global = 0,
        Buffer = 1,
        Texture = 2
    }

    public struct RHIGlobalBarrier
    {
        public ERHIStageMask StageBefore;
        public ERHIStageMask StageAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
    }

    public struct RHIBufferBarrier
    {
        public RHIBuffer Resource;
        public RHIBufferRange Range;
        public ERHIStageMask StageBefore;
        public ERHIStageMask StageAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
        public ERHIPipelineType? SourceQueue;
        public ERHIPipelineType? DestinationQueue;
    }

    public struct RHITextureBarrier
    {
        public RHITexture Resource;
        public RHITextureSubresourceRange SubresourceRange;
        public ERHITextureLayout LayoutBefore;
        public ERHITextureLayout LayoutAfter;
        public ERHIStageMask StageBefore;
        public ERHIStageMask StageAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
        public ERHIPipelineType? SourceQueue;
        public ERHIPipelineType? DestinationQueue;
    }

    public struct RHIBarrier
    {
        private ERHIBarrierKind m_Kind;
        private RHIGlobalBarrier m_Global;
        private RHIBufferBarrier m_Buffer;
        private RHITextureBarrier m_Texture;

        public ERHIBarrierKind Kind => m_Kind;
        public RHIGlobalBarrier GlobalBarrier => m_Global;
        public RHIBufferBarrier BufferBarrier => m_Buffer;
        public RHITextureBarrier TextureBarrier => m_Texture;

        public static RHIBarrier Global(ERHIStageMask stageBefore,
                                        ERHIStageMask stageAfter,
                                        ERHIAccessMask accessBefore,
                                        ERHIAccessMask accessAfter)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Global;
            barrier.m_Global.StageBefore = stageBefore;
            barrier.m_Global.StageAfter = stageAfter;
            barrier.m_Global.AccessBefore = accessBefore;
            barrier.m_Global.AccessAfter = accessAfter;
            return barrier;
        }

        public static RHIBarrier Buffer(RHIBuffer resource,
                                        RHIBufferRange range,
                                        ERHIStageMask stageBefore,
                                        ERHIStageMask stageAfter,
                                        ERHIAccessMask accessBefore,
                                        ERHIAccessMask accessAfter,
                                        ERHIPipelineType? sourceQueue = null,
                                        ERHIPipelineType? destinationQueue = null)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Buffer;
            barrier.m_Buffer.Resource = resource;
            barrier.m_Buffer.Range = range;
            barrier.m_Buffer.StageBefore = stageBefore;
            barrier.m_Buffer.StageAfter = stageAfter;
            barrier.m_Buffer.AccessBefore = accessBefore;
            barrier.m_Buffer.AccessAfter = accessAfter;
            barrier.m_Buffer.SourceQueue = sourceQueue;
            barrier.m_Buffer.DestinationQueue = destinationQueue;
            return barrier;
        }

        public static RHIBarrier Texture(RHITexture resource,
                                         RHITextureSubresourceRange subresourceRange,
                                         ERHITextureLayout layoutBefore,
                                         ERHITextureLayout layoutAfter,
                                         ERHIStageMask stageBefore,
                                         ERHIStageMask stageAfter,
                                         ERHIAccessMask accessBefore,
                                         ERHIAccessMask accessAfter,
                                         ERHIPipelineType? sourceQueue = null,
                                         ERHIPipelineType? destinationQueue = null)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Texture;
            barrier.m_Texture.Resource = resource;
            barrier.m_Texture.SubresourceRange = subresourceRange;
            barrier.m_Texture.LayoutBefore = layoutBefore;
            barrier.m_Texture.LayoutAfter = layoutAfter;
            barrier.m_Texture.StageBefore = stageBefore;
            barrier.m_Texture.StageAfter = stageAfter;
            barrier.m_Texture.AccessBefore = accessBefore;
            barrier.m_Texture.AccessAfter = accessAfter;
            barrier.m_Texture.SourceQueue = sourceQueue;
            barrier.m_Texture.DestinationQueue = destinationQueue;
            return barrier;
        }
    }

    internal static class RHIBarrierUtility
    {
        internal static void ValidateQueueOwnership(in RHIBarrier barrier, ERHIPipelineType recordingQueue)
        {
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                    return;
                case ERHIBarrierKind.Buffer:
                    ValidateQueueOwnership(
                        barrier.BufferBarrier.SourceQueue,
                        barrier.BufferBarrier.DestinationQueue,
                        recordingQueue);
                    return;
                case ERHIBarrierKind.Texture:
                    ValidateQueueOwnership(
                        barrier.TextureBarrier.SourceQueue,
                        barrier.TextureBarrier.DestinationQueue,
                        recordingQueue);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(barrier),
                        barrier.Kind,
                        "Unknown RHI barrier kind.");
            }
        }

        internal static bool TryGetQueueOwnership(
            in RHIBarrier barrier,
            out ERHIPipelineType sourceQueue,
            out ERHIPipelineType destinationQueue)
        {
            ERHIPipelineType? source = null;
            ERHIPipelineType? destination = null;
            if (barrier.Kind == ERHIBarrierKind.Buffer)
            {
                source = barrier.BufferBarrier.SourceQueue;
                destination = barrier.BufferBarrier.DestinationQueue;
            }
            else if (barrier.Kind == ERHIBarrierKind.Texture)
            {
                source = barrier.TextureBarrier.SourceQueue;
                destination = barrier.TextureBarrier.DestinationQueue;
            }

            if (!source.HasValue || !destination.HasValue)
            {
                sourceQueue = default;
                destinationQueue = default;
                return false;
            }

            sourceQueue = source.Value;
            destinationQueue = destination.Value;
            return true;
        }

        private static void ValidateQueueOwnership(
            ERHIPipelineType? sourceQueue,
            ERHIPipelineType? destinationQueue,
            ERHIPipelineType recordingQueue)
        {
            if (sourceQueue.HasValue != destinationQueue.HasValue)
            {
                throw new ArgumentException(
                    "Queue ownership transfer requires both source and destination queues.");
            }

            if (!sourceQueue.HasValue)
            {
                return;
            }

            ValidateQueueType(sourceQueue.Value, nameof(sourceQueue));
            ValidateQueueType(destinationQueue!.Value, nameof(destinationQueue));
            ValidateQueueType(recordingQueue, nameof(recordingQueue));
            if (sourceQueue.Value == destinationQueue.Value)
            {
                throw new ArgumentException(
                    "Queue ownership transfer requires different logical source and destination queues.");
            }

            if (recordingQueue != sourceQueue.Value && recordingQueue != destinationQueue.Value)
            {
                throw new InvalidOperationException(
                    $"A {sourceQueue.Value}->{destinationQueue.Value} ownership barrier cannot be recorded on the {recordingQueue} queue.");
            }
        }

        private static void ValidateQueueType(ERHIPipelineType queue, string parameterName)
        {
            if (queue != ERHIPipelineType.Transfer &&
                queue != ERHIPipelineType.Compute &&
                queue != ERHIPipelineType.Graphics)
            {
                throw new ArgumentOutOfRangeException(parameterName, queue, "Unknown logical queue type.");
            }
        }

        internal static ERHIStageMask ConvertToStageMask(ERHIPipelineType pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipelineType.Transfer:
                    return ERHIStageMask.Transfer;
                case ERHIPipelineType.Compute:
                    return ERHIStageMask.Compute | ERHIStageMask.Transfer;
                case ERHIPipelineType.Graphics:
                    return ERHIStageMask.AllGraphics | ERHIStageMask.AllShading | ERHIStageMask.Transfer;
                default:
                    return ERHIStageMask.All;
            }
        }

        internal static RHITextureSubresourceRange CreateWholeSubresourceRange(RHITexture texture)
        {
            ERHITextureAspectMask aspectMask = ERHITextureAspectMask.Color;
            if (texture != null)
            {
                aspectMask = InferAspectMask(texture.Descriptor.Format);
            }

            return RHITextureSubresourceRange.Whole(aspectMask);
        }

        internal static ERHITextureAspectMask InferAspectMask(ERHIPixelFormat format)
        {
            switch (format)
            {
                case ERHIPixelFormat.D16_UNorm:
                case ERHIPixelFormat.D32_Float:
                    return ERHITextureAspectMask.Depth;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil;
                default:
                    // Opaque sampler-feedback maps use the color aspect of the
                    // existing barrier utility. They are not depth/stencil and
                    // not framebuffer-local read.
                    return ERHITextureAspectMask.Color;
            }
        }

        internal static bool IsSamplerFeedbackOpaqueFormat(ERHIPixelFormat format)
        {
            return RHITexture.IsSamplerFeedbackOpaqueFormat(format);
        }
    }
    #endregion
}

file enum BinarySyncLifecycle : byte
{
    Ready = 0,
    Pending = 1,
    Signaled = 2,
    Resetting = 3
}
