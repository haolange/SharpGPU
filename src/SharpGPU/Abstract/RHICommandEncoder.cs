using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public struct RHIIndirectDispatchArgs
    {
        public uint GroupCountX;
        public uint GroupCountY;
        public uint GroupCountZ;
        public RHIIndirectDispatchArgs(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            GroupCountX = groupCountX;
            GroupCountY = groupCountY;
            GroupCountZ = groupCountZ;
        }
    }

    public struct RHIIndirectDrawArgs
    {
        public uint VertexCount;
        public uint InstanceCount;
        public uint StartVertexLocation;
        public uint StartInstanceLocation;
        public RHIIndirectDrawArgs(in uint vertexCount, in uint instanceCount, in uint startVertexLocation, in uint startInstanceLocation)
        {
            VertexCount = vertexCount;
            InstanceCount = instanceCount;
            StartVertexLocation = startVertexLocation;
            StartInstanceLocation = startInstanceLocation;
        }
    }

    public struct RHIIndirectDrawIndexedArgs
    {
        public uint IndexCount;
        public uint InstanceCount;
        public uint StartIndexLocation;
        public int BaseVertexLocation;
        public uint StartInstanceLocation;
        public RHIIndirectDrawIndexedArgs(in uint indexCount, in uint instanceCount, in uint startIndexLocation, in int baseVertexLocation, in uint startInstanceLocation)
        {
            IndexCount = indexCount;
            InstanceCount = instanceCount;
            StartIndexLocation = startIndexLocation;
            BaseVertexLocation = baseVertexLocation;
            StartInstanceLocation = startInstanceLocation;
        }
    }

    public struct RHIBufferCopyDescriptor
    {
        public uint Offset;
        public uint RowPitch;
        public uint3 TextureHeight;
        public RHIBuffer Buffer;
    }

    public struct RHITextureCopyDescriptor
    {
        public uint MipLevel;
        public uint SliceBase;
        public uint SliceCount;
        public uint3 Origin;
        public RHITexture Texture;
    }

    public struct RHITimestampDescriptor
    {
        public RHIQuery Query;
        public uint BeginIndex;
        public uint EndIndex;
    }

    public struct RHIOcclusionDescriptor
    {
        public RHIQuery Query;
    }

    public struct RHIStatisticsDescriptor
    {
        public RHIQuery Query;
        public uint WriteIndex;
    }

    public struct RHITransferPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public struct RHIComputePassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
        public RHIStatisticsDescriptor? Statistics;
    }

    public struct RHIRayTracingPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
        public RHIStatisticsDescriptor? Statistics;
    }

    public abstract class RHITransferEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;

        internal abstract void BeginPass(in RHITransferPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount);
        public abstract void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size);
        public abstract void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement transfer pass teardown.");
        }
    }

    public abstract class RHIComputeEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIComputePipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIComputePassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void BeginStatistics(in uint index);
        public abstract void EndStatistics(in uint index);
        public abstract void SetPipeline(RHIComputePipeline pipeline);
        public abstract void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex);
        public abstract void SetPushConstants(IntPtr data, in uint size, in uint offset = 0);
        public abstract void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset);
        public abstract void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer);
        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The compute encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Compute);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement compute pass teardown.");
        }
    }

    public abstract class RHIRaytracingEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIRaytracingPipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void BeginStatistics(in uint index);
        public abstract void EndStatistics(in uint index);
        public abstract void SetPipeline(RHIRaytracingPipeline pipeline);
        public abstract void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex);
        public abstract void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct);
        public abstract void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct);
        public abstract void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable);
        public abstract void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer);
        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The ray-tracing encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.RayTracing);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement ray-tracing pass teardown.");
        }
    }
    
    public struct RHIMLPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public abstract class RHIMLEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIMLPipeline? m_CachedPipeline;
        protected RHIMLBindingTable? m_CachedBindingSet;

        internal abstract void BeginPass(in RHIMLPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void SetPipeline(RHIMLPipeline pipeline);
        public abstract void SetBindingTable(RHIMLBindingTable bindingSet);
        public abstract void Dispatch();
        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The machine-learning encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.MachineLearning);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement machine-learning pass teardown.");
        }
    }

    public struct RHIWorkGraphPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public abstract class RHIWorkGraphEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIWorkGraphPipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIWorkGraphPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void SetPipeline(RHIWorkGraphPipeline pipeline);
        public abstract void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex);
        public abstract void SetPushConstants(IntPtr data, in uint size, in uint offset = 0);
        public abstract void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize);
        public abstract void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null);
        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The work-graph encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.WorkGraph);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement work-graph pass teardown.");
        }
    }
    #region RasterPass
    public struct RHIColorAttachmentDescriptor
    {
        public RHITextureSubresourceRange SubresourceRange;
        public float4 ClearValue;
        public ERHILoadAction LoadAction;
        public ERHIStoreAction StoreAction;
        public ERHIRasterAttachmentAccess Access;
        public RHITexture RenderTarget;
        public RHITextureSubresourceRange ResolveSubresourceRange;
        public RHITexture? ResolveTarget;
    }

    public struct RHIDepthStencilAttachmentDescriptor
    {
        public RHITextureSubresourceRange SubresourceRange;
        public float DepthClearValue;
        public ERHILoadAction DepthLoadOp;
        public ERHIStoreAction DepthStoreOp;
        public int StencilClearValue;
        public ERHILoadAction StencilLoadOp;
        public ERHIStoreAction StencilStoreOp;
        public RHITexture RenderTarget;
        public RHITextureSubresourceRange ResolveSubresourceRange;
        public EResolveMode DepthResolveMode;
        public EResolveMode StencilResolveMode;
        public RHITexture? ResolveTarget;
    }

    public struct RHIAttachmentIndexArray
    {
        public static readonly RHIAttachmentIndexArray Empty = new RHIAttachmentIndexArray(0);

        public const int MaxAttachments = 8;

        private int a0;

        private int a1;

        private int a2;

        private int a3;

        private int a4;

        private int a5;

        private int a6;

        private int a7;

        private int activeAttachments;

        public int Length => activeAttachments;

        public unsafe int this[int index]
        {
            get
            {
                if ((uint)index >= 8u)
                {
                    throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {8}[");
                }

                if ((uint)index >= activeAttachments)
                {
                    throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {activeAttachments}[");
                }

                fixed (RHIAttachmentIndexArray* ptr = &this)
                {
                    int* ptr2 = (int*)ptr;
                    return ptr2[index];
                }
            }
            set
            {
                if ((uint)index >= 8u)
                {
                    throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {8}[");
                }

                if ((uint)index >= activeAttachments)
                {
                    throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {activeAttachments}[");
                }

                fixed (RHIAttachmentIndexArray* ptr = &this)
                {
                    int* ptr2 = (int*)ptr;
                    ptr2[index] = value;
                }
            }
        }

        public RHIAttachmentIndexArray(in int numAttachments)
        {
            if (numAttachments < 0 || numAttachments > 8)
            {
                throw new ArgumentException($"AttachmentIndexArray - numAttachments must be in range of [0, {8}[");
            }

            a0 = (a1 = (a2 = (a3 = (a4 = (a5 = (a6 = (a7 = -1)))))));
            activeAttachments = numAttachments;
        }

        public RHIAttachmentIndexArray(int[] attachments) : this(attachments.Length)
        {
            for (int i = 0; i < activeAttachments; i++)
            {
                this[i] = attachments[i];
            }
        }
    }

    public struct RHISubPassDescriptor
    {
        public ERHISubPassFlags Flags;
        public RHIAttachmentIndexArray ColorInputs;
        public RHIAttachmentIndexArray ColorOutputs;
        public RHIAttachmentIndexArray SampledFeedbackInputs;
    }

    public struct RHIRasterPassDescriptor
    {
        public string Name;
        public uint ArrayLength;
        public ERHISampleCount SampleCount;
        public RHITimestampDescriptor? Timestamp;
        public RHIOcclusionDescriptor? Occlusion;
        public RHIStatisticsDescriptor? Statistics;
        public RHITexture? ShadingRateTexture;
        public Memory<RHIColorAttachmentDescriptor> ColorAttachments;
        public RHIDepthStencilAttachmentDescriptor? DepthStencilAttachment;
        public Memory<RHISubPassDescriptor> SubPassDescriptors;
    }
    internal readonly struct RasterSubPassPlan
    {
        internal RHIAttachmentInterfaceSignature AttachmentInterface { get; }
        internal byte ReadMask { get; }
        internal byte WriteMask { get; }
        internal byte ReadWriteMask { get; }
        internal byte PreserveMask { get; }
        internal byte TransitionMask { get; }
        internal byte AvailableOnEntryMask { get; }
        internal bool PreservesDepthStencil { get; }
        internal bool RequiresDepthStencilTransition { get; }

        internal RasterSubPassPlan(
            in RHIAttachmentInterfaceSignature attachmentInterface,
            byte readMask,
            byte writeMask,
            byte preserveMask,
            byte transitionMask,
            byte availableOnEntryMask,
            bool preservesDepthStencil,
            bool requiresDepthStencilTransition)
        {
            AttachmentInterface = attachmentInterface;
            ReadMask = readMask;
            WriteMask = writeMask;
            ReadWriteMask = checked((byte)(readMask & writeMask));
            PreserveMask = preserveMask;
            TransitionMask = transitionMask;
            AvailableOnEntryMask = availableOnEntryMask;
            PreservesDepthStencil = preservesDepthStencil;
            RequiresDepthStencilTransition = requiresDepthStencilTransition;
        }

        internal RasterSubPassPlan WithPreserveMask(
            byte preserveMask,
            bool preservesDepthStencil)
        {
            return new RasterSubPassPlan(
                AttachmentInterface,
                ReadMask,
                WriteMask,
                preserveMask,
                TransitionMask,
                AvailableOnEntryMask,
                preservesDepthStencil,
                RequiresDepthStencilTransition);
        }
    }

    internal sealed class RasterPassPlan
    {
        private readonly RHIColorAttachmentDescriptor[] m_ColorAttachments;
        private readonly RHISubPassDescriptor[] m_SubPassDescriptors;
        private readonly RasterSubPassPlan[] m_SubPasses;
        private readonly RHIDepthStencilAttachmentDescriptor? m_DepthStencilAttachment;
        private readonly RHITimestampDescriptor? m_Timestamp;
        private readonly RHIOcclusionDescriptor? m_Occlusion;
        private readonly RHIStatisticsDescriptor? m_Statistics;
        private readonly RHITexture? m_ShadingRateTexture;

        internal string Name { get; }
        internal uint ArrayLength { get; }
        internal ERHISampleCount SampleCount { get; }
        internal uint Width { get; }
        internal uint Height { get; }
        internal int ColorAttachmentCount => m_ColorAttachments.Length;
        internal int SubPassCount => m_SubPasses.Length;
        internal bool HasDepthStencilAttachment => m_DepthStencilAttachment.HasValue;
        internal ERHIPixelFormat DepthStencilFormat =>
            m_DepthStencilAttachment?.RenderTarget.Descriptor.Format ??
            ERHIPixelFormat.Unknown;
        internal ERHITextureAspectMask DepthStencilAspects =>
            m_DepthStencilAttachment?.SubresourceRange.AspectMask ??
            ERHITextureAspectMask.None;

        internal RHIRasterPassDescriptor DescriptorSnapshot
        {
            get
            {
                return new RHIRasterPassDescriptor
                {
                    Name = Name,
                    ArrayLength = ArrayLength,
                    SampleCount = SampleCount,
                    Timestamp = m_Timestamp,
                    Occlusion = m_Occlusion,
                    Statistics = m_Statistics,
                    ShadingRateTexture = m_ShadingRateTexture,
                    ColorAttachments =
                        (RHIColorAttachmentDescriptor[])m_ColorAttachments.Clone(),
                    DepthStencilAttachment = m_DepthStencilAttachment,
                    SubPassDescriptors =
                        (RHISubPassDescriptor[])m_SubPassDescriptors.Clone(),
                };
            }
        }

        internal RasterPassPlan(
            string name,
            uint arrayLength,
            ERHISampleCount sampleCount,
            uint width,
            uint height,
            RHIColorAttachmentDescriptor[] colorAttachments,
            RHIDepthStencilAttachmentDescriptor? depthStencilAttachment,
            RHISubPassDescriptor[] subPassDescriptors,
            RasterSubPassPlan[] subPasses,
            RHITimestampDescriptor? timestamp,
            RHIOcclusionDescriptor? occlusion,
            RHIStatisticsDescriptor? statistics,
            RHITexture? shadingRateTexture)
        {
            Name = name;
            ArrayLength = arrayLength;
            SampleCount = sampleCount;
            Width = width;
            Height = height;
            m_ColorAttachments = colorAttachments;
            m_DepthStencilAttachment = depthStencilAttachment;
            m_SubPassDescriptors = subPassDescriptors;
            m_SubPasses = subPasses;
            m_Timestamp = timestamp;
            m_Occlusion = occlusion;
            m_Statistics = statistics;
            m_ShadingRateTexture = shadingRateTexture;
        }

        internal ref readonly RHIColorAttachmentDescriptor GetColorAttachment(int index)
        {
            return ref m_ColorAttachments[index];
        }

        internal RHIDepthStencilAttachmentDescriptor GetDepthStencilAttachment()
        {
            return m_DepthStencilAttachment
                ?? throw new InvalidOperationException(
                    "The raster pass does not declare a depth/stencil attachment.");
        }

        internal ref readonly RasterSubPassPlan GetSubPass(int index)
        {
            return ref m_SubPasses[index];
        }
    }

    internal static class RasterPassPlanner
    {
        private const ERHIRasterAttachmentAccess KnownAttachmentAccess =
            ERHIRasterAttachmentAccess.RasterOrderedReadWrite;
        private const ERHISubPassFlags KnownSubPassFlags =
            ERHISubPassFlags.ReadOnlyDepthStencil;

        internal static RasterPassPlan Compile(in RHIRasterPassDescriptor descriptor)
        {
            RHIColorAttachmentDescriptor[] colorAttachments =
                descriptor.ColorAttachments.Span.ToArray();
            if (colorAttachments.Length > RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    colorAttachments.Length,
                    $"Raster passes support at most {RHIAttachmentIndexArray.MaxAttachments} color attachments.");
            }

            uint requestedArrayLength = descriptor.ArrayLength;
            uint normalizedArrayLength = requestedArrayLength;
            ERHISampleCount normalizedSampleCount = descriptor.SampleCount;
            uint normalizedWidth = 0;
            uint normalizedHeight = 0;
            bool hasAttachmentFacts = false;

            for (int i = 0; i < colorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor attachment = ref colorAttachments[i];
                string parameterName = $"ColorAttachments[{i}]";
                ValidateTexture(attachment.RenderTarget, parameterName);
                RHITextureDescriptor textureDescriptor = attachment.RenderTarget.Descriptor;
                if (textureDescriptor.Format == ERHIPixelFormat.Unknown ||
                    textureDescriptor.Format == ERHIPixelFormat.Pending ||
                    RHIBarrierUtility.InferAspectMask(textureDescriptor.Format) != ERHITextureAspectMask.Color)
                {
                    throw new ArgumentException(
                        $"{parameterName} must use a color format.",
                        nameof(descriptor));
                }
                if ((textureDescriptor.UsageFlag & ERHITextureUsage.RenderTarget) == 0)
                {
                    throw new ArgumentException(
                        $"{parameterName} texture was not created with RenderTarget usage.",
                        nameof(descriptor));
                }
                ValidateLoadAction(attachment.LoadAction, $"{parameterName}.LoadAction");
                ValidateStoreAction(attachment.StoreAction, $"{parameterName}.StoreAction");
                if ((attachment.Access & ~KnownAttachmentAccess) != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        attachment.Access,
                        $"{parameterName} contains an unknown raster attachment access qualifier.");
                }
                if ((attachment.Access & ERHIRasterAttachmentAccess.RasterOrderedReadWrite) != 0 &&
                    (textureDescriptor.UsageFlag & ERHITextureUsage.RasterizerOrdered) == 0)
                {
                    throw new ArgumentException(
                        $"{parameterName} declares RasterOrderedReadWrite but its texture lacks RasterizerOrdered usage.",
                        nameof(descriptor));
                }

                attachment.SubresourceRange = NormalizeSubresourceRange(
                    attachment.SubresourceRange,
                    attachment.RenderTarget,
                    ERHITextureAspectMask.Color,
                    requestedArrayLength == 0 ? 1u : requestedArrayLength,
                    parameterName);

                MergeAttachmentFacts(
                    textureDescriptor,
                    attachment.SubresourceRange,
                    requestedArrayLength,
                    ref normalizedArrayLength,
                    ref normalizedSampleCount,
                    ref normalizedWidth,
                    ref normalizedHeight,
                    ref hasAttachmentFacts,
                    parameterName);

                ValidateColorResolve(
                    ref attachment,
                    normalizedArrayLength,
                    parameterName);
            }

            RHIDepthStencilAttachmentDescriptor? depthStencilAttachment =
                descriptor.DepthStencilAttachment;
            if (depthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor attachment =
                    depthStencilAttachment.Value;
                const string parameterName = "DepthStencilAttachment";
                ValidateTexture(attachment.RenderTarget, parameterName);
                RHITextureDescriptor textureDescriptor = attachment.RenderTarget.Descriptor;
                ERHITextureAspectMask nativeAspects =
                    RHIBarrierUtility.InferAspectMask(textureDescriptor.Format);
                if (textureDescriptor.Format == ERHIPixelFormat.Unknown ||
                    textureDescriptor.Format == ERHIPixelFormat.Pending ||
                    (nativeAspects & ERHITextureAspectMask.Depth) == 0)
                {
                    throw new ArgumentException(
                        "DepthStencilAttachment must use a depth or depth/stencil format.",
                        nameof(descriptor));
                }
                if ((textureDescriptor.UsageFlag & ERHITextureUsage.DepthStencil) == 0)
                {
                    throw new ArgumentException(
                        "DepthStencilAttachment texture was not created with DepthStencil usage.",
                        nameof(descriptor));
                }

                ValidateLoadAction(attachment.DepthLoadOp, "DepthStencilAttachment.DepthLoadOp");
                ValidateStoreAction(attachment.DepthStoreOp, "DepthStencilAttachment.DepthStoreOp");
                ValidateLoadAction(attachment.StencilLoadOp, "DepthStencilAttachment.StencilLoadOp");
                ValidateStoreAction(attachment.StencilStoreOp, "DepthStencilAttachment.StencilStoreOp");
                ValidateResolveMode(attachment.DepthResolveMode, "DepthStencilAttachment.DepthResolveMode");
                ValidateResolveMode(attachment.StencilResolveMode, "DepthStencilAttachment.StencilResolveMode");
                if (attachment.DepthLoadOp == ERHILoadAction.Clear &&
                    (!float.IsFinite(attachment.DepthClearValue) ||
                     attachment.DepthClearValue < 0f ||
                     attachment.DepthClearValue > 1f))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        attachment.DepthClearValue,
                        "Depth clear value must be finite and in [0, 1].");
                }
                if (attachment.StencilLoadOp == ERHILoadAction.Clear &&
                    (uint)attachment.StencilClearValue > byte.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        attachment.StencilClearValue,
                        "Stencil clear value must be in [0, 255].");
                }

                attachment.SubresourceRange = NormalizeSubresourceRange(
                    attachment.SubresourceRange,
                    attachment.RenderTarget,
                    nativeAspects,
                    requestedArrayLength == 0 ? 1u : requestedArrayLength,
                    parameterName,
                    allowAspectSubset: true);

                MergeAttachmentFacts(
                    textureDescriptor,
                    attachment.SubresourceRange,
                    requestedArrayLength,
                    ref normalizedArrayLength,
                    ref normalizedSampleCount,
                    ref normalizedWidth,
                    ref normalizedHeight,
                    ref hasAttachmentFacts,
                    parameterName);

                ValidateDepthStencilResolve(
                    ref attachment,
                    normalizedArrayLength,
                    parameterName);
                depthStencilAttachment = attachment;
            }

            if (!hasAttachmentFacts)
            {
                normalizedArrayLength = requestedArrayLength == 0 ? 1u : requestedArrayLength;
                normalizedSampleCount = NormalizePassSampleCount(
                    normalizedSampleCount,
                    ERHISampleCount.None,
                    nameof(descriptor.SampleCount));
            }
            else
            {
                normalizedSampleCount = RHIRasterPipelineContract.ValidateSampleCount(
                    normalizedSampleCount,
                    nameof(descriptor.SampleCount));
            }

            ValidateAttachmentOverlap(colorAttachments, depthStencilAttachment);

            RHITexture? shadingRateTexture = descriptor.ShadingRateTexture;
            if (shadingRateTexture != null)
            {
                ValidateTexture(shadingRateTexture, nameof(descriptor.ShadingRateTexture));
            }

            CompileSubPasses(
                descriptor.SubPassDescriptors,
                colorAttachments,
                depthStencilAttachment,
                normalizedArrayLength,
                out RHISubPassDescriptor[] subPassDescriptors,
                out RasterSubPassPlan[] subPasses);

            return new RasterPassPlan(
                descriptor.Name ?? string.Empty,
                normalizedArrayLength,
                normalizedSampleCount,
                normalizedWidth,
                normalizedHeight,
                colorAttachments,
                depthStencilAttachment,
                subPassDescriptors,
                subPasses,
                descriptor.Timestamp,
                descriptor.Occlusion,
                descriptor.Statistics,
                shadingRateTexture);
        }

        private static void CompileSubPasses(
            Memory<RHISubPassDescriptor> sourceSubPasses,
            RHIColorAttachmentDescriptor[] colorAttachments,
            RHIDepthStencilAttachmentDescriptor? depthStencilAttachment,
            uint arrayLength,
            out RHISubPassDescriptor[] subPassDescriptors,
            out RasterSubPassPlan[] subPasses)
        {
            int colorAttachmentCount = colorAttachments.Length;
            if (sourceSubPasses.Length == 0)
            {
                RHIAttachmentIndexArray outputs =
                    new RHIAttachmentIndexArray(colorAttachmentCount);
                for (int i = 0; i < colorAttachmentCount; ++i)
                {
                    outputs[i] = i;
                }
                subPassDescriptors = new[]
                {
                    new RHISubPassDescriptor
                    {
                        Flags = ERHISubPassFlags.None,
                        ColorInputs = RHIAttachmentIndexArray.Empty,
                        ColorOutputs = outputs,
                        SampledFeedbackInputs = RHIAttachmentIndexArray.Empty,
                    },
                };
            }
            else
            {
                subPassDescriptors = sourceSubPasses.Span.ToArray();
            }

            subPasses = new RasterSubPassPlan[subPassDescriptors.Length];
            byte declaredAttachmentMask =
                RHIAttachmentInterfaceSignature.CreateDeclaredMask(colorAttachmentCount);
            byte initiallyAvailableMask = 0;
            byte rasterOrderedAttachmentMask = 0;
            for (int i = 0; i < colorAttachmentCount; ++i)
            {
                ref RHIColorAttachmentDescriptor attachment = ref colorAttachments[i];
                if (attachment.LoadAction == ERHILoadAction.Load ||
                    attachment.LoadAction == ERHILoadAction.Clear)
                {
                    initiallyAvailableMask |= checked((byte)(1 << i));
                }
                if ((attachment.Access & ERHIRasterAttachmentAccess.RasterOrderedReadWrite) != 0)
                {
                    rasterOrderedAttachmentMask |= checked((byte)(1 << i));
                }
            }

            byte availableMask = initiallyAvailableMask;
            byte writtenMask = 0;
            byte qualifiedRasterOrderedMask = 0;
            ERHISubPassFlags previousFlags = ERHISubPassFlags.None;
            for (int i = 0; i < subPassDescriptors.Length; ++i)
            {
                ref RHISubPassDescriptor subPass = ref subPassDescriptors[i];
                if ((subPass.Flags & ~KnownSubPassFlags) != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sourceSubPasses),
                        subPass.Flags,
                        $"Subpass {i} contains unknown flags.");
                }
                ValidateDepthStencilFlags(
                    subPass.Flags,
                    depthStencilAttachment,
                    i);

                byte inputMask = BuildAttachmentMask(
                    subPass.ColorInputs,
                    colorAttachmentCount,
                    $"Subpass {i} ColorInputs");
                byte outputMask = BuildAttachmentMask(
                    subPass.ColorOutputs,
                    colorAttachmentCount,
                    $"Subpass {i} ColorOutputs");
                byte sampledFeedbackMask = BuildAttachmentMask(
                    subPass.SampledFeedbackInputs,
                    colorAttachmentCount,
                    $"Subpass {i} SampledFeedbackInputs");
                if ((inputMask & sampledFeedbackMask) != 0)
                {
                    throw new ArgumentException(
                        $"Subpass {i} declares the same attachment as both a local input and SampledFeedback.");
                }
                for (int attachmentIndex = 0;
                     attachmentIndex < colorAttachmentCount;
                     ++attachmentIndex)
                {
                    byte attachmentBit = checked((byte)(1 << attachmentIndex));
                    if ((sampledFeedbackMask & attachmentBit) != 0 &&
                        (colorAttachments[attachmentIndex].RenderTarget.Descriptor.UsageFlag &
                         ERHITextureUsage.ShaderResource) == 0)
                    {
                        throw new ArgumentException(
                            $"Subpass {i} SampledFeedback attachment {attachmentIndex} " +
                            "was not created with ShaderResource usage.");
                    }
                }

                byte readMask = checked((byte)(inputMask | sampledFeedbackMask));
                byte readBeforeWriteMask = checked((byte)(readMask & ~availableMask));
                if (readBeforeWriteMask != 0)
                {
                    throw new ArgumentException(
                        $"Subpass {i} reads unavailable color attachment mask 0x{readBeforeWriteMask:X2}. " +
                        "Use Load/Clear or write the attachment in an earlier ordered subpass.");
                }

                byte readWriteMask = checked((byte)(inputMask & outputMask));
                byte rasterOrderedReadWriteMask =
                    checked((byte)(readWriteMask & rasterOrderedAttachmentMask));
                byte layeredAccessMask = arrayLength > 1
                    ? checked((byte)(
                        inputMask |
                        rasterOrderedReadWriteMask |
                        sampledFeedbackMask))
                    : (byte)0;
                qualifiedRasterOrderedMask |= rasterOrderedReadWriteMask;
                byte transitionMask =
                    checked((byte)((readMask | outputMask) & writtenMask));
                RHIAttachmentInterfaceSignature attachmentInterface =
                    new RHIAttachmentInterfaceSignature(
                        colorAttachmentCount,
                        subPass.ColorInputs,
                        subPass.ColorOutputs,
                        subPass.SampledFeedbackInputs,
                        rasterOrderedReadWriteMask,
                        subPass.Flags,
                        layeredAccessMask: layeredAccessMask);

                bool depthStencilTransition =
                    i != 0 &&
                    depthStencilAttachment.HasValue &&
                    previousFlags != subPass.Flags;
                subPasses[i] = new RasterSubPassPlan(
                    attachmentInterface,
                    readMask,
                    outputMask,
                    preserveMask: 0,
                    transitionMask,
                    availableMask,
                    preservesDepthStencil: false,
                    requiresDepthStencilTransition: depthStencilTransition);

                availableMask = checked((byte)(availableMask | outputMask));
                writtenMask = checked((byte)(writtenMask | outputMask));
                previousFlags = subPass.Flags;
            }

            byte unusedRasterOrderedMask =
                checked((byte)(rasterOrderedAttachmentMask & ~qualifiedRasterOrderedMask));
            if (unusedRasterOrderedMask != 0)
            {
                throw new ArgumentException(
                    $"RasterOrderedReadWrite was declared for attachment mask 0x{unusedRasterOrderedMask:X2}, " +
                    "but no subpass reads and writes those attachments.");
            }

            byte futureUseMask = 0;
            bool futureDepthStencilUse = false;
            for (int i = subPasses.Length - 1; i >= 0; --i)
            {
                RasterSubPassPlan subPass = subPasses[i];
                byte currentUseMask =
                    checked((byte)(subPass.ReadMask | subPass.WriteMask));
                byte preserveMask = checked((byte)(
                    subPass.AvailableOnEntryMask &
                    ~currentUseMask &
                    futureUseMask &
                    declaredAttachmentMask));
                bool hasDepthStencil = depthStencilAttachment.HasValue;
                subPasses[i] = subPass.WithPreserveMask(
                    preserveMask,
                    hasDepthStencil && futureDepthStencilUse);
                futureUseMask |= currentUseMask;
                futureDepthStencilUse |= hasDepthStencil;
            }
        }

        private static byte BuildAttachmentMask(
            in RHIAttachmentIndexArray indices,
            int colorAttachmentCount,
            string name)
        {
            byte mask = 0;
            for (int i = 0; i < indices.Length; ++i)
            {
                int attachmentIndex = indices[i];
                if (attachmentIndex == RHIAttachmentInterfaceSignature.UnboundLogicalAttachment)
                {
                    continue;
                }
                if ((uint)attachmentIndex >= colorAttachmentCount)
                {
                    throw new ArgumentOutOfRangeException(
                        name,
                        attachmentIndex,
                        $"Attachment index must be in [0, {colorAttachmentCount}).");
                }
                byte bit = checked((byte)(1 << attachmentIndex));
                if ((mask & bit) != 0)
                {
                    throw new ArgumentException(
                        $"{name} contains duplicate attachment index {attachmentIndex}.",
                        name);
                }
                mask |= bit;
            }
            return mask;
        }

        private static void ValidateDepthStencilFlags(
            ERHISubPassFlags flags,
            RHIDepthStencilAttachmentDescriptor? depthStencilAttachment,
            int subPassIndex)
        {
            if (flags == ERHISubPassFlags.None)
            {
                return;
            }
            if (!depthStencilAttachment.HasValue)
            {
                throw new ArgumentException(
                    $"Subpass {subPassIndex} declares depth/stencil flags without a depth/stencil attachment.");
            }

            ERHITextureAspectMask aspects =
                depthStencilAttachment.Value.SubresourceRange.AspectMask;
            if ((flags & ERHISubPassFlags.ReadOnlyDepth) != 0 &&
                (aspects & ERHITextureAspectMask.Depth) == 0)
            {
                throw new ArgumentException(
                    $"Subpass {subPassIndex} declares ReadOnlyDepth without a depth aspect.");
            }
            if ((flags & ERHISubPassFlags.ReadOnlyStencil) != 0 &&
                (aspects & ERHITextureAspectMask.Stencil) == 0)
            {
                throw new ArgumentException(
                    $"Subpass {subPassIndex} declares ReadOnlyStencil without a stencil aspect.");
            }
        }

        private static void MergeAttachmentFacts(
            in RHITextureDescriptor textureDescriptor,
            in RHITextureSubresourceRange range,
            uint requestedArrayLength,
            ref uint normalizedArrayLength,
            ref ERHISampleCount normalizedSampleCount,
            ref uint normalizedWidth,
            ref uint normalizedHeight,
            ref bool hasAttachmentFacts,
            string parameterName)
        {
            ERHISampleCount textureSampleCount =
                RHIRasterPipelineContract.ValidateSampleCount(
                    textureDescriptor.SampleCount,
                    $"{parameterName}.RenderTarget.SampleCount");
            uint width = MipExtent(textureDescriptor.Extent.x, range.BaseMipLevel);
            uint height = MipExtent(textureDescriptor.Extent.y, range.BaseMipLevel);

            if (!hasAttachmentFacts)
            {
                normalizedArrayLength =
                    requestedArrayLength == 0
                        ? range.ArrayLayerCount
                        : requestedArrayLength;
                normalizedSampleCount = NormalizePassSampleCount(
                    normalizedSampleCount,
                    textureSampleCount,
                    nameof(RHIRasterPassDescriptor.SampleCount));
                normalizedWidth = width;
                normalizedHeight = height;
                hasAttachmentFacts = true;
            }

            if (range.ArrayLayerCount != normalizedArrayLength)
            {
                throw new ArgumentException(
                    $"{parameterName} has {range.ArrayLayerCount} layers; " +
                    $"the pass requires {normalizedArrayLength}.");
            }
            if (textureSampleCount != normalizedSampleCount)
            {
                throw new ArgumentException(
                    $"{parameterName} sample count {textureSampleCount} " +
                    $"does not match pass sample count {normalizedSampleCount}.");
            }
            if (width != normalizedWidth || height != normalizedHeight)
            {
                throw new ArgumentException(
                    $"{parameterName} extent {width}x{height} " +
                    $"does not match pass extent {normalizedWidth}x{normalizedHeight}.");
            }
        }

        private static ERHISampleCount NormalizePassSampleCount(
            ERHISampleCount requested,
            ERHISampleCount inferred,
            string parameterName)
        {
            if ((byte)requested == 0)
            {
                return inferred;
            }
            return RHIRasterPipelineContract.ValidateSampleCount(
                requested,
                parameterName);
        }

        private static RHITextureSubresourceRange NormalizeSubresourceRange(
            RHITextureSubresourceRange range,
            RHITexture texture,
            ERHITextureAspectMask expectedAspects,
            uint defaultArrayLayerCount,
            string parameterName,
            bool allowAspectSubset = false)
        {
            RHITextureDescriptor descriptor = texture.Descriptor;
            if (descriptor.MipCount == 0)
            {
                throw new ArgumentException(
                    $"{parameterName} texture has zero mip levels.");
            }

            if (range.AspectMask == ERHITextureAspectMask.None)
            {
                range.AspectMask = expectedAspects;
            }
            ERHITextureAspectMask unknownAspects =
                range.AspectMask &
                ~(ERHITextureAspectMask.Color |
                  ERHITextureAspectMask.Depth |
                  ERHITextureAspectMask.Stencil);
            if (unknownAspects != 0 ||
                (allowAspectSubset
                    ? range.AspectMask == ERHITextureAspectMask.None ||
                      (range.AspectMask & ~expectedAspects) != 0
                    : range.AspectMask != expectedAspects))
            {
                throw new ArgumentException(
                    $"{parameterName} aspect mask {range.AspectMask} " +
                    $"is incompatible with texture aspects {expectedAspects}.");
            }

            if (range.MipLevelCount == 0)
            {
                range.MipLevelCount = 1;
            }
            else if (range.MipLevelCount == RHITextureSubresourceRange.All)
            {
                range.MipLevelCount = checked(descriptor.MipCount - range.BaseMipLevel);
            }
            if (range.BaseMipLevel >= descriptor.MipCount ||
                range.MipLevelCount == 0 ||
                (ulong)range.BaseMipLevel + range.MipLevelCount > descriptor.MipCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    range.BaseMipLevel,
                    "Attachment mip range is outside the texture.");
            }
            if (range.MipLevelCount != 1)
            {
                throw new ArgumentException(
                    $"{parameterName} must select exactly one mip level for raster attachment use.");
            }

            uint arrayLayerCapacity = GetArrayLayerCapacity(
                descriptor,
                range.BaseMipLevel);
            if (range.ArrayLayerCount == 0)
            {
                range.ArrayLayerCount = defaultArrayLayerCount;
            }
            else if (range.ArrayLayerCount == RHITextureSubresourceRange.All)
            {
                range.ArrayLayerCount =
                    checked(arrayLayerCapacity - range.BaseArrayLayer);
            }
            if (range.BaseArrayLayer >= arrayLayerCapacity ||
                range.ArrayLayerCount == 0 ||
                (ulong)range.BaseArrayLayer + range.ArrayLayerCount > arrayLayerCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    range.BaseArrayLayer,
                    "Attachment array-layer range is outside the texture.");
            }
            return range;
        }

        private static void ValidateColorResolve(
            ref RHIColorAttachmentDescriptor attachment,
            uint passArrayLength,
            string parameterName)
        {
            bool storeResolves =
                attachment.StoreAction == ERHIStoreAction.Resolve ||
                attachment.StoreAction == ERHIStoreAction.StoreAndResolve;
            if (!storeResolves)
            {
                if (attachment.ResolveTarget != null)
                {
                    throw new ArgumentException(
                        $"{parameterName} supplies ResolveTarget without a resolving StoreAction.");
                }
                attachment.ResolveSubresourceRange = default;
                return;
            }
            if (attachment.ResolveTarget == null)
            {
                throw new ArgumentException(
                    $"{parameterName} resolving StoreAction requires ResolveTarget.");
            }

            ValidateResolvePair(
                attachment.RenderTarget,
                attachment.SubresourceRange,
                attachment.ResolveTarget,
                ref attachment.ResolveSubresourceRange,
                passArrayLength,
                parameterName);
        }

        private static void ValidateDepthStencilResolve(
            ref RHIDepthStencilAttachmentDescriptor attachment,
            uint passArrayLength,
            string parameterName)
        {
            ERHITextureAspectMask aspects = attachment.SubresourceRange.AspectMask;
            bool hasDepth = (aspects & ERHITextureAspectMask.Depth) != 0;
            bool hasStencil = (aspects & ERHITextureAspectMask.Stencil) != 0;
            bool depthStoreResolves =
                attachment.DepthStoreOp == ERHIStoreAction.Resolve ||
                attachment.DepthStoreOp == ERHIStoreAction.StoreAndResolve;
            bool stencilStoreResolves =
                attachment.StencilStoreOp == ERHIStoreAction.Resolve ||
                attachment.StencilStoreOp == ERHIStoreAction.StoreAndResolve;
            bool depthModeResolves = attachment.DepthResolveMode != EResolveMode.None;
            bool stencilModeResolves = attachment.StencilResolveMode != EResolveMode.None;

            if ((!hasDepth && (depthStoreResolves || depthModeResolves)) ||
                (!hasStencil && (stencilStoreResolves || stencilModeResolves)))
            {
                throw new ArgumentException(
                    $"{parameterName} resolve settings reference an aspect not selected by SubresourceRange.");
            }
            if (depthStoreResolves != depthModeResolves ||
                stencilStoreResolves != stencilModeResolves)
            {
                throw new ArgumentException(
                    $"{parameterName} resolve modes and resolving StoreActions must be declared independently and consistently.");
            }

            bool resolves = depthModeResolves || stencilModeResolves;
            if (!resolves)
            {
                if (attachment.ResolveTarget != null)
                {
                    throw new ArgumentException(
                        $"{parameterName} supplies ResolveTarget without a depth or stencil resolve mode.");
                }
                attachment.ResolveSubresourceRange = default;
                return;
            }
            if (attachment.ResolveTarget == null)
            {
                throw new ArgumentException(
                    $"{parameterName} resolve modes require ResolveTarget.");
            }

            ValidateResolvePair(
                attachment.RenderTarget,
                attachment.SubresourceRange,
                attachment.ResolveTarget,
                ref attachment.ResolveSubresourceRange,
                passArrayLength,
                parameterName);
        }

        private static void ValidateResolvePair(
            RHITexture source,
            in RHITextureSubresourceRange sourceRange,
            RHITexture destination,
            ref RHITextureSubresourceRange destinationRange,
            uint passArrayLength,
            string parameterName)
        {
            ValidateTexture(destination, $"{parameterName}.ResolveTarget");
            RHITextureDescriptor sourceDescriptor = source.Descriptor;
            RHITextureDescriptor destinationDescriptor = destination.Descriptor;
            if ((destinationDescriptor.UsageFlag & ERHITextureUsage.ResolveTarget) == 0)
            {
                throw new ArgumentException(
                    $"{parameterName}.ResolveTarget was not created with ResolveTarget usage.");
            }
            if (sourceDescriptor.SampleCount == ERHISampleCount.None ||
                destinationDescriptor.SampleCount != ERHISampleCount.None)
            {
                throw new ArgumentException(
                    $"{parameterName} resolve requires a multisampled source and single-sampled destination.");
            }
            if (sourceDescriptor.Format != destinationDescriptor.Format)
            {
                throw new ArgumentException(
                    $"{parameterName} resolve source and destination formats must match exactly.");
            }

            destinationRange = NormalizeSubresourceRange(
                destinationRange,
                destination,
                sourceRange.AspectMask,
                passArrayLength,
                $"{parameterName}.ResolveSubresourceRange",
                allowAspectSubset: false);
            if (destinationRange.ArrayLayerCount != sourceRange.ArrayLayerCount ||
                MipExtent(destinationDescriptor.Extent.x, destinationRange.BaseMipLevel) !=
                    MipExtent(sourceDescriptor.Extent.x, sourceRange.BaseMipLevel) ||
                MipExtent(destinationDescriptor.Extent.y, destinationRange.BaseMipLevel) !=
                    MipExtent(sourceDescriptor.Extent.y, sourceRange.BaseMipLevel))
            {
                throw new ArgumentException(
                    $"{parameterName} resolve source and destination extent/layer ranges must match.");
            }
        }

        private static void ValidateAttachmentOverlap(
            RHIColorAttachmentDescriptor[] colorAttachments,
            RHIDepthStencilAttachmentDescriptor? depthStencilAttachment)
        {
            for (int i = 0; i < colorAttachments.Length; ++i)
            {
                for (int j = i + 1; j < colorAttachments.Length; ++j)
                {
                    RejectOverlap(
                        colorAttachments[i].RenderTarget,
                        colorAttachments[i].SubresourceRange,
                        colorAttachments[j].RenderTarget,
                        colorAttachments[j].SubresourceRange,
                        $"ColorAttachments[{i}]",
                        $"ColorAttachments[{j}]");
                }
                if (depthStencilAttachment.HasValue)
                {
                    RHIDepthStencilAttachmentDescriptor depth =
                        depthStencilAttachment.Value;
                    RejectOverlap(
                        colorAttachments[i].RenderTarget,
                        colorAttachments[i].SubresourceRange,
                        depth.RenderTarget,
                        depth.SubresourceRange,
                        $"ColorAttachments[{i}]",
                        "DepthStencilAttachment");
                }

                if (colorAttachments[i].ResolveTarget != null)
                {
                    for (int j = 0; j < colorAttachments.Length; ++j)
                    {
                        RejectOverlap(
                            colorAttachments[i].ResolveTarget!,
                            colorAttachments[i].ResolveSubresourceRange,
                            colorAttachments[j].RenderTarget,
                            colorAttachments[j].SubresourceRange,
                            $"ColorAttachments[{i}].ResolveTarget",
                            $"ColorAttachments[{j}]");
                    }
                    for (int j = i + 1; j < colorAttachments.Length; ++j)
                    {
                        if (colorAttachments[j].ResolveTarget != null)
                        {
                            RejectOverlap(
                                colorAttachments[i].ResolveTarget!,
                                colorAttachments[i].ResolveSubresourceRange,
                                colorAttachments[j].ResolveTarget!,
                                colorAttachments[j].ResolveSubresourceRange,
                                $"ColorAttachments[{i}].ResolveTarget",
                                $"ColorAttachments[{j}].ResolveTarget");
                        }
                    }
                }
            }

            if (depthStencilAttachment.HasValue &&
                depthStencilAttachment.Value.ResolveTarget != null)
            {
                RHIDepthStencilAttachmentDescriptor depth =
                    depthStencilAttachment.Value;
                RejectOverlap(
                    depth.ResolveTarget!,
                    depth.ResolveSubresourceRange,
                    depth.RenderTarget,
                    depth.SubresourceRange,
                    "DepthStencilAttachment.ResolveTarget",
                    "DepthStencilAttachment");
                for (int i = 0; i < colorAttachments.Length; ++i)
                {
                    RejectOverlap(
                        depth.ResolveTarget!,
                        depth.ResolveSubresourceRange,
                        colorAttachments[i].RenderTarget,
                        colorAttachments[i].SubresourceRange,
                        "DepthStencilAttachment.ResolveTarget",
                        $"ColorAttachments[{i}]");
                }
            }
        }

        private static void RejectOverlap(
            RHITexture leftTexture,
            in RHITextureSubresourceRange leftRange,
            RHITexture rightTexture,
            in RHITextureSubresourceRange rightRange,
            string leftName,
            string rightName)
        {
            if (!ReferenceEquals(leftTexture, rightTexture) ||
                (leftRange.AspectMask & rightRange.AspectMask) == 0 ||
                !IntervalsOverlap(
                    leftRange.BaseMipLevel,
                    leftRange.MipLevelCount,
                    rightRange.BaseMipLevel,
                    rightRange.MipLevelCount) ||
                !IntervalsOverlap(
                    leftRange.BaseArrayLayer,
                    leftRange.ArrayLayerCount,
                    rightRange.BaseArrayLayer,
                    rightRange.ArrayLayerCount))
            {
                return;
            }

            throw new ArgumentException(
                $"{leftName} and {rightName} illegally overlap the same texture subresources.");
        }

        private static bool IntervalsOverlap(
            uint leftStart,
            uint leftCount,
            uint rightStart,
            uint rightCount)
        {
            ulong leftEnd = (ulong)leftStart + leftCount;
            ulong rightEnd = (ulong)rightStart + rightCount;
            return leftStart < rightEnd && rightStart < leftEnd;
        }

        private static void ValidateTexture(RHITexture? texture, string parameterName)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (texture.IsDisposed)
            {
                throw new ObjectDisposedException(
                    texture.GetType().FullName,
                    $"{parameterName} is disposed.");
            }
            RHITextureDescriptor descriptor = texture.Descriptor;
            if (descriptor.Extent.x == 0 ||
                descriptor.Extent.y == 0 ||
                descriptor.Extent.z == 0)
            {
                throw new ArgumentException(
                    $"{parameterName} texture extent must be non-zero.");
            }
        }

        private static uint GetArrayLayerCapacity(
            in RHITextureDescriptor descriptor,
            uint mipLevel)
        {
            return descriptor.Dimension switch
            {
                ERHITextureDimension.Texture2D => 1,
                ERHITextureDimension.Texture2DMS => 1,
                ERHITextureDimension.Texture2DArray => descriptor.Extent.z,
                ERHITextureDimension.Texture2DArrayMS => descriptor.Extent.z,
                ERHITextureDimension.TextureCube => 6,
                ERHITextureDimension.TextureCubeArray =>
                    checked(descriptor.Extent.z * 6),
                ERHITextureDimension.Texture3D =>
                    MipExtent(descriptor.Extent.z, mipLevel),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Dimension,
                    "Unknown texture dimension."),
            };
        }

        private static uint MipExtent(uint extent, uint mipLevel)
        {
            return mipLevel >= 32
                ? 1u
                : Math.Max(1u, extent >> checked((int)mipLevel));
        }

        private static void ValidateLoadAction(
            ERHILoadAction action,
            string parameterName)
        {
            if (action != ERHILoadAction.Load &&
                action != ERHILoadAction.Clear &&
                action != ERHILoadAction.DontCare)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    action,
                    "Unknown attachment load action.");
            }
        }

        private static void ValidateStoreAction(
            ERHIStoreAction action,
            string parameterName)
        {
            if (action != ERHIStoreAction.Store &&
                action != ERHIStoreAction.Resolve &&
                action != ERHIStoreAction.StoreAndResolve &&
                action != ERHIStoreAction.DontCare)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    action,
                    "Unknown attachment store action.");
            }
        }

        private static void ValidateResolveMode(
            EResolveMode mode,
            string parameterName)
        {
            if (mode != EResolveMode.None &&
                mode != EResolveMode.Min &&
                mode != EResolveMode.Max &&
                mode != EResolveMode.Sample0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    mode,
                    "Unknown attachment resolve mode.");
            }
        }
    }
    #endregion

    #region RasterEncoder
    public abstract class RHIRasterEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIRasterPipeline? m_CachedPipeline;
        internal RasterPassPlan? m_RasterPassPlan;
        internal int m_CurrentSubPassIndex = -1;
        internal int m_PipelineSubPassIndex = -1;

        internal virtual void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;
            try
            {
                throw new NotSupportedException(
                    $"{GetType().Name} does not implement raster pass begin.");
            }
            catch
            {
                ClearRasterPassState();
                throw;
            }
        }

        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void BeginOcclusion(in uint index);
        public abstract void EndOcclusion(in uint index);
        public abstract void BeginStatistics(in uint index);
        public abstract void EndStatistics(in uint index);
        public virtual void NextSubPass()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            int nextSubPassIndex = m_CurrentSubPassIndex + 1;
            if (nextSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            throw new NotSupportedException(
                $"{GetType().Name} does not implement raster subpass transition.");
        }

        public abstract void SetScissor(in Rect rect);
        public abstract void SetScissors(in Memory<Rect> rects);
        public abstract void SetViewport(in Viewport viewport);
        public abstract void SetViewports(in Memory<Viewport> viewports);
        public abstract void SetStencilRef(in uint value);
        public abstract void SetBlendFactor(in float4 value);
        public virtual void SetPipeline(RHIRasterPipeline pipeline)
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            ValidatePipelineCompatibility(plan, m_CurrentSubPassIndex, pipeline);
            throw new NotSupportedException(
                $"{GetType().Name} does not implement raster pipeline binding.");
        }

        public abstract void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex);
        public abstract void SetPushConstants(IntPtr data, in uint size, in uint offset = 0);
        public abstract void SetIndexBuffer(RHIBuffer buffer, in uint offset);
        public abstract void SetVertexBuffer(RHIBuffer buffer, in uint slot, in uint offset);
        public abstract void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner);
        public virtual void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement raster draw.");
        }

        public virtual void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement indexed raster draw.");
        }

        public virtual void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement indirect raster draw.");
        }

        public virtual void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement indexed indirect raster draw.");
        }

        public virtual void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement mesh dispatch.");
        }

        public virtual void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement indirect mesh dispatch.");
        }

        public virtual void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            ValidateDrawState();
            throw new NotSupportedException(
                $"{GetType().Name} does not implement raster indirect command buffer execution.");
        }

        public virtual void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The raster encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Raster);
            RasterPassPlan plan = RequireActiveRasterPass();
            if (m_CurrentSubPassIndex != plan.SubPassCount - 1)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' ended at subpass {m_CurrentSubPassIndex}, " +
                    $"but {plan.SubPassCount} subpasses were declared.");
            }

            throw new NotSupportedException(
                $"{GetType().Name} does not implement raster pass teardown.");
        }

        internal int CurrentSubPassIndex => m_CurrentSubPassIndex;

        internal RasterPassPlan RequireActiveRasterPass()
        {
            return m_RasterPassPlan ??
                throw new InvalidOperationException("No raster pass is active on this encoder.");
        }

        protected void ValidateDrawState()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            if (m_CachedPipeline == null || m_PipelineSubPassIndex != m_CurrentSubPassIndex)
            {
                throw new InvalidOperationException(
                    $"A compatible raster pipeline must be set for subpass {m_CurrentSubPassIndex} before drawing.");
            }
            if (m_CachedPipeline.IsDisposed)
            {
                throw new ObjectDisposedException(m_CachedPipeline.GetType().FullName);
            }

            ValidatePipelineCompatibility(plan, m_CurrentSubPassIndex, m_CachedPipeline);
        }

        internal static void ValidatePipelineCompatibility(
            RasterPassPlan plan,
            int subPassIndex,
            RHIRasterPipeline pipeline)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(pipeline.GetType().FullName);
            }

            RHIRasterPipelineDescriptor descriptor =
                pipeline.DescriptorInternal;
            ERHISampleCount pipelineSampleCount =
                RHIRasterPipelineContract.ValidateSampleCount(
                    descriptor.SampleCount,
                    nameof(descriptor.SampleCount));
            if (pipelineSampleCount != plan.SampleCount)
            {
                throw new ArgumentException(
                    $"Raster pipeline sample count {pipelineSampleCount} does not match pass sample count {plan.SampleCount}.",
                    nameof(pipeline));
            }

            ERHIPixelFormat[] colorFormats = descriptor.ColorFormats ??
                throw new ArgumentException("Raster pipeline ColorFormats cannot be null.", nameof(pipeline));
            if (colorFormats.Length != plan.ColorAttachmentCount)
            {
                throw new ArgumentException(
                    $"Raster pipeline declares {colorFormats.Length} color formats, " +
                    $"but the pass declares {plan.ColorAttachmentCount} color attachments.",
                    nameof(pipeline));
            }
            for (int i = 0; i < colorFormats.Length; ++i)
            {
                ERHIPixelFormat attachmentFormat =
                    plan.GetColorAttachment(i).RenderTarget.Descriptor.Format;
                if (colorFormats[i] != attachmentFormat)
                {
                    throw new ArgumentException(
                        $"Raster pipeline color format {colorFormats[i]} at index {i} " +
                        $"does not match pass attachment format {attachmentFormat}.",
                        nameof(pipeline));
                }
            }

            if (descriptor.DepthFormat != plan.DepthStencilFormat)
            {
                throw new ArgumentException(
                    $"Raster pipeline depth/stencil format {descriptor.DepthFormat} " +
                    $"does not match pass format {plan.DepthStencilFormat}.",
                    nameof(pipeline));
            }

            bool usesDualSourceColor =
                RHIRasterPipelineContract.UsesDualSourceBlend(
                    descriptor.RenderState.BlendState,
                    colorFormats.Length);
            RHIAttachmentInterfaceSignature pipelineSignature =
                descriptor.AttachmentInterface.NormalizeForPipeline(
                    colorFormats.Length,
                    usesDualSourceColor);
            RHIAttachmentInterfaceSignature passSignature =
                plan.GetSubPass(subPassIndex).AttachmentInterface;
            RHIRasterPipelineContract.ValidateDepthStencilCompatibility(
                in descriptor,
                in passSignature,
                plan.DepthStencilAspects,
                nameof(pipeline));
            if (!passSignature.IsPassCompatibleWith(pipelineSignature))
            {
                throw new ArgumentException(
                    $"Raster pipeline attachment interface {pipelineSignature} " +
                    $"does not match subpass interface {passSignature}.",
                    nameof(pipeline));
            }
        }

        internal void ClearRasterPassState()
        {
            m_RasterPassPlan = null;
            m_CurrentSubPassIndex = -1;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;
        }
    }
    #endregion
}
