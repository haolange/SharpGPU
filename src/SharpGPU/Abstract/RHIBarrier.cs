using System;

namespace Infinity.Graphics
{
    [Flags]
    public enum ERHISyncStageMask : ulong
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
        ShadingRateSurface = 11
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
        public ERHISyncStageMask SyncBefore;
        public ERHISyncStageMask SyncAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
    }

    public struct RHIBufferBarrier
    {
        public RHIBuffer Resource;
        public RHIBufferRange Range;
        public ERHISyncStageMask SyncBefore;
        public ERHISyncStageMask SyncAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
    }

    public struct RHITextureBarrier
    {
        public RHITexture Resource;
        public RHITextureSubresourceRange SubresourceRange;
        public ERHITextureLayout LayoutBefore;
        public ERHITextureLayout LayoutAfter;
        public ERHISyncStageMask SyncBefore;
        public ERHISyncStageMask SyncAfter;
        public ERHIAccessMask AccessBefore;
        public ERHIAccessMask AccessAfter;
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

        public static RHIBarrier Global(ERHISyncStageMask syncBefore,
                                        ERHISyncStageMask syncAfter,
                                        ERHIAccessMask accessBefore,
                                        ERHIAccessMask accessAfter)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Global;
            barrier.m_Global.SyncBefore = syncBefore;
            barrier.m_Global.SyncAfter = syncAfter;
            barrier.m_Global.AccessBefore = accessBefore;
            barrier.m_Global.AccessAfter = accessAfter;
            return barrier;
        }

        public static RHIBarrier Buffer(RHIBuffer resource,
                                        RHIBufferRange range,
                                        ERHISyncStageMask syncBefore,
                                        ERHISyncStageMask syncAfter,
                                        ERHIAccessMask accessBefore,
                                        ERHIAccessMask accessAfter)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Buffer;
            barrier.m_Buffer.Resource = resource;
            barrier.m_Buffer.Range = range;
            barrier.m_Buffer.SyncBefore = syncBefore;
            barrier.m_Buffer.SyncAfter = syncAfter;
            barrier.m_Buffer.AccessBefore = accessBefore;
            barrier.m_Buffer.AccessAfter = accessAfter;
            return barrier;
        }

        public static RHIBarrier Texture(RHITexture resource,
                                         RHITextureSubresourceRange subresourceRange,
                                         ERHITextureLayout layoutBefore,
                                         ERHITextureLayout layoutAfter,
                                         ERHISyncStageMask syncBefore,
                                         ERHISyncStageMask syncAfter,
                                         ERHIAccessMask accessBefore,
                                         ERHIAccessMask accessAfter)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Kind = ERHIBarrierKind.Texture;
            barrier.m_Texture.Resource = resource;
            barrier.m_Texture.SubresourceRange = subresourceRange;
            barrier.m_Texture.LayoutBefore = layoutBefore;
            barrier.m_Texture.LayoutAfter = layoutAfter;
            barrier.m_Texture.SyncBefore = syncBefore;
            barrier.m_Texture.SyncAfter = syncAfter;
            barrier.m_Texture.AccessBefore = accessBefore;
            barrier.m_Texture.AccessAfter = accessAfter;
            return barrier;
        }
    }

    internal static class RHIBarrierUtility
    {
        internal static ERHISyncStageMask ConvertToSyncStageMask(ERHIPipelineType pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipelineType.Transfer:
                    return ERHISyncStageMask.Transfer;
                case ERHIPipelineType.Compute:
                    return ERHISyncStageMask.Compute | ERHISyncStageMask.Transfer;
                case ERHIPipelineType.Graphics:
                    return ERHISyncStageMask.AllGraphics | ERHISyncStageMask.AllShading | ERHISyncStageMask.Transfer;
                default:
                    return ERHISyncStageMask.All;
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
                    return ERHITextureAspectMask.Color;
            }
        }
    }
}
