using Infinity.Mathmatics;
using SharpMetal.Metal;

namespace Infinity.Graphics
{
    internal static class MetalUtility
    {
        internal static uint AlignUp(in uint value, in uint alignment)
        {
            return ((value + alignment - 1u) / alignment) * alignment;
        }

        internal static MTLPixelFormat ConvertToMetalPixelFormat(in ERHIPixelFormat format)
        {
            switch (format)
            {
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return MTLPixelFormat.RGBA8Unorm;
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return MTLPixelFormat.RGBA8UnormsRGB;
                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return MTLPixelFormat.BGRA8Unorm;
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return MTLPixelFormat.BGRA8UnormsRGB;
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return MTLPixelFormat.RGB10A2Unorm;
                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return MTLPixelFormat.RGB10A2Uint;
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return MTLPixelFormat.RGBA16Float;
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return MTLPixelFormat.RGBA32Float;
                case ERHIPixelFormat.R32_Float:
                    return MTLPixelFormat.R32Float;
                case ERHIPixelFormat.R32_UInt:
                    return MTLPixelFormat.R32Uint;
                case ERHIPixelFormat.D16_UNorm:
                    return MTLPixelFormat.Depth16Unorm;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return MTLPixelFormat.Depth24UnormStencil8;
                case ERHIPixelFormat.D32_Float:
                    return MTLPixelFormat.Depth32Float;
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return MTLPixelFormat.Depth32FloatStencil8;
                default:
                    return MTLPixelFormat.Invalid;
            }
        }

        internal static ERHIPixelFormat ConvertToRhiPixelFormat(in MTLPixelFormat format)
        {
            switch (format)
            {
                case MTLPixelFormat.RGBA8Unorm:
                    return ERHIPixelFormat.R8G8B8A8_UNorm;
                case MTLPixelFormat.RGBA8UnormsRGB:
                    return ERHIPixelFormat.R8G8B8A8_UNorm_Srgb;
                case MTLPixelFormat.BGRA8Unorm:
                    return ERHIPixelFormat.B8G8R8A8_UNorm;
                case MTLPixelFormat.BGRA8UnormsRGB:
                    return ERHIPixelFormat.B8G8R8A8_UNorm_Srgb;
                case MTLPixelFormat.RGB10A2Unorm:
                    return ERHIPixelFormat.R10G10B10A2_UNorm;
                case MTLPixelFormat.RGB10A2Uint:
                    return ERHIPixelFormat.R10G10B10A2_UInt;
                case MTLPixelFormat.RGBA16Float:
                    return ERHIPixelFormat.R16G16B16A16_Float;
                case MTLPixelFormat.RGBA32Float:
                    return ERHIPixelFormat.R32G32B32A32_Float;
                case MTLPixelFormat.Depth16Unorm:
                    return ERHIPixelFormat.D16_UNorm;
                case MTLPixelFormat.Depth24UnormStencil8:
                    return ERHIPixelFormat.D24_UNorm_S8_UInt;
                case MTLPixelFormat.Depth32Float:
                    return ERHIPixelFormat.D32_Float;
                case MTLPixelFormat.Depth32FloatStencil8:
                    return ERHIPixelFormat.D32_Float_S8_UInt;
                default:
                    return ERHIPixelFormat.Unknown;
            }
        }

        internal static MTLPixelFormat ConvertToMetalSwapchainFormat(in ERHISwapChainFormat format)
        {
            return ConvertToMetalPixelFormat(RHIUtility.ConvertToPixelFormat(format));
        }

        internal static MTLTextureType ConvertToMetalTextureType(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2D:
                    return MTLTextureType.Type2D;
                case ERHITextureDimension.Texture2DMS:
                    return MTLTextureType.Type2DMultisample;
                case ERHITextureDimension.Texture2DArray:
                    return MTLTextureType.Type2DArray;
                case ERHITextureDimension.Texture2DArrayMS:
                    return MTLTextureType.Type2DMultisampleArray;
                case ERHITextureDimension.TextureCube:
                    return MTLTextureType.Cube;
                case ERHITextureDimension.TextureCubeArray:
                    return MTLTextureType.CubeArray;
                case ERHITextureDimension.Texture3D:
                    return MTLTextureType.Type3D;
                default:
                    return MTLTextureType.Type2D;
            }
        }

        internal static MTLTextureUsage ConvertToMetalTextureUsage(in ERHITextureUsage usage)
        {
            MTLTextureUsage result = MTLTextureUsage.Unknown;
            int flags = (int)usage;
            if ((flags & (int)ERHITextureUsage.ShaderResource) != 0)
            {
                result |= MTLTextureUsage.ShaderRead;
            }

            if ((flags & ((int)ERHITextureUsage.UnorderedAccess | (int)ERHITextureUsage.RasterizerOrdered)) != 0)
            {
                result |= MTLTextureUsage.ShaderWrite;
            }

            if ((flags & ((int)ERHITextureUsage.RenderTarget | (int)ERHITextureUsage.DepthStencil | (int)ERHITextureUsage.ResolveTarget)) != 0)
            {
                result |= MTLTextureUsage.RenderTarget;
            }

            return result;
        }

        internal static MTLResourceOptions ConvertToMetalResourceOptions(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.GPULocal:
                    return MTLResourceOptions.ResourceStorageModePrivate;
                case ERHIStorageMode.Memoryless:
                    return MTLResourceOptions.ResourceStorageModeMemoryless;
                case ERHIStorageMode.HostUpload:
                    return MTLResourceOptions.ResourceStorageModeShared | MTLResourceOptions.ResourceCPUCacheModeWriteCombined;
                case ERHIStorageMode.Readback:
                case ERHIStorageMode.GPUUpload:
                default:
                    return MTLResourceOptions.ResourceStorageModeShared;
            }
        }

        internal static MTLStorageMode ConvertToMetalStorageMode(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.GPULocal:
                    return MTLStorageMode.Private;
                case ERHIStorageMode.Memoryless:
                    return MTLStorageMode.Memoryless;
                case ERHIStorageMode.HostUpload:
                case ERHIStorageMode.GPUUpload:
                case ERHIStorageMode.Readback:
                default:
                    return MTLStorageMode.Shared;
            }
        }

        internal static ERHIStorageMode ConvertToRhiStorageMode(in MTLStorageMode storageMode)
        {
            switch (storageMode)
            {
                case MTLStorageMode.Private:
                    return ERHIStorageMode.GPULocal;
                case MTLStorageMode.Memoryless:
                    return ERHIStorageMode.Memoryless;
                case MTLStorageMode.Managed:
                    return ERHIStorageMode.Readback;
                case MTLStorageMode.Shared:
                default:
                    return ERHIStorageMode.HostUpload;
            }
        }

        internal static ERHITextureUsage ConvertToRhiTextureUsage(in MTLTextureUsage usage)
        {
            ERHITextureUsage result = 0;
            ulong flags = (ulong)usage;
            if ((flags & (ulong)MTLTextureUsage.ShaderRead) != 0)
            {
                result |= ERHITextureUsage.ShaderResource;
            }

            if ((flags & (ulong)MTLTextureUsage.ShaderWrite) != 0)
            {
                result |= ERHITextureUsage.UnorderedAccess;
            }

            if ((flags & (ulong)MTLTextureUsage.RenderTarget) != 0)
            {
                result |= ERHITextureUsage.RenderTarget;
            }

            return result == 0 ? ERHITextureUsage.Pending : result;
        }

        internal static ERHITextureDimension ConvertToRhiTextureDimension(in MTLTextureType type)
        {
            switch (type)
            {
                case MTLTextureType.Type2D:
                    return ERHITextureDimension.Texture2D;
                case MTLTextureType.Type2DMultisample:
                    return ERHITextureDimension.Texture2DMS;
                case MTLTextureType.Type2DArray:
                    return ERHITextureDimension.Texture2DArray;
                case MTLTextureType.Type2DMultisampleArray:
                    return ERHITextureDimension.Texture2DArrayMS;
                case MTLTextureType.Cube:
                    return ERHITextureDimension.TextureCube;
                case MTLTextureType.CubeArray:
                    return ERHITextureDimension.TextureCubeArray;
                case MTLTextureType.Type3D:
                    return ERHITextureDimension.Texture3D;
                default:
                    return ERHITextureDimension.Texture2D;
            }
        }

        internal static MTLLoadAction ConvertToMetalLoadAction(in ERHILoadAction action)
        {
            switch (action)
            {
                case ERHILoadAction.Load:
                    return MTLLoadAction.Load;
                case ERHILoadAction.Clear:
                    return MTLLoadAction.Clear;
                default:
                    return MTLLoadAction.DontCare;
            }
        }

        internal static MTLStoreAction ConvertToMetalStoreAction(in ERHIStoreAction action)
        {
            switch (action)
            {
                case ERHIStoreAction.Store:
                    return MTLStoreAction.Store;
                case ERHIStoreAction.Resolve:
                    return MTLStoreAction.MultisampleResolve;
                case ERHIStoreAction.StoreAndResolve:
                    return MTLStoreAction.StoreAndMultisampleResolve;
                default:
                    return MTLStoreAction.DontCare;
            }
        }

        internal static MTLPrimitiveType ConvertToMetalPrimitiveType(in ERHIPrimitiveTopology topology)
        {
            switch (topology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return MTLPrimitiveType.Point;
                case ERHIPrimitiveTopology.LineList:
                    return MTLPrimitiveType.Line;
                case ERHIPrimitiveTopology.LineStrip:
                    return MTLPrimitiveType.LineStrip;
                case ERHIPrimitiveTopology.TriangleStrip:
                    return MTLPrimitiveType.TriangleStrip;
                default:
                    return MTLPrimitiveType.Triangle;
            }
        }

        internal static MTLPrimitiveTopologyClass ConvertToMetalPrimitiveTopologyClass(in ERHIPrimitiveTopology topology)
        {
            switch (topology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return MTLPrimitiveTopologyClass.Point;
                case ERHIPrimitiveTopology.LineList:
                case ERHIPrimitiveTopology.LineStrip:
                    return MTLPrimitiveTopologyClass.Line;
                default:
                    return MTLPrimitiveTopologyClass.Triangle;
            }
        }

        internal static MTLCullMode ConvertToMetalCullMode(in ERHICullMode cullMode)
        {
            switch (cullMode)
            {
                case ERHICullMode.Front:
                    return MTLCullMode.Front;
                case ERHICullMode.Back:
                    return MTLCullMode.Back;
                default:
                    return MTLCullMode.None;
            }
        }

        internal static MTLTriangleFillMode ConvertToMetalFillMode(in ERHIFillMode fillMode)
        {
            return fillMode == ERHIFillMode.Wireframe ? MTLTriangleFillMode.Lines : MTLTriangleFillMode.Fill;
        }

        internal static MTLWinding ConvertToMetalWinding(in bool frontCounterClockwise)
        {
            return frontCounterClockwise ? MTLWinding.CounterClockwise : MTLWinding.Clockwise;
        }

        internal static MTLCompareFunction ConvertToMetalCompareFunction(in ERHIComparisonMode mode)
        {
            switch (mode)
            {
                case ERHIComparisonMode.Never:
                    return MTLCompareFunction.Never;
                case ERHIComparisonMode.Less:
                    return MTLCompareFunction.Less;
                case ERHIComparisonMode.Equal:
                    return MTLCompareFunction.Equal;
                case ERHIComparisonMode.LessEqual:
                    return MTLCompareFunction.LessEqual;
                case ERHIComparisonMode.Greater:
                    return MTLCompareFunction.Greater;
                case ERHIComparisonMode.NotEqual:
                    return MTLCompareFunction.NotEqual;
                case ERHIComparisonMode.GreaterEqual:
                    return MTLCompareFunction.GreaterEqual;
                default:
                    return MTLCompareFunction.Always;
            }
        }

        internal static MTLBlendFactor ConvertToMetalBlendFactor(in ERHIBlendMode blendMode)
        {
            switch (blendMode)
            {
                case ERHIBlendMode.Zero:
                    return MTLBlendFactor.Zero;
                case ERHIBlendMode.One:
                    return MTLBlendFactor.One;
                case ERHIBlendMode.SrcColor:
                    return MTLBlendFactor.SourceColor;
                case ERHIBlendMode.OneMinusSrcColor:
                    return MTLBlendFactor.OneMinusSourceColor;
                case ERHIBlendMode.SrcAlpha:
                    return MTLBlendFactor.SourceAlpha;
                case ERHIBlendMode.OneMinusSrcAlpha:
                    return MTLBlendFactor.OneMinusSourceAlpha;
                case ERHIBlendMode.DstColor:
                    return MTLBlendFactor.DestinationColor;
                case ERHIBlendMode.OneMinusDstColor:
                    return MTLBlendFactor.OneMinusDestinationColor;
                case ERHIBlendMode.DstAlpha:
                    return MTLBlendFactor.DestinationAlpha;
                case ERHIBlendMode.OneMinusDstAlpha:
                    return MTLBlendFactor.OneMinusDestinationAlpha;
                case ERHIBlendMode.SrcAlphaSaturate:
                    return MTLBlendFactor.SourceAlphaSaturated;
                case ERHIBlendMode.BlendFactor:
                    return MTLBlendFactor.BlendColor;
                case ERHIBlendMode.InverseBlendFactor:
                    return MTLBlendFactor.OneMinusBlendColor;
                case ERHIBlendMode.SecondarySourceColor:
                    return MTLBlendFactor.Source1Color;
                case ERHIBlendMode.InverseSecondarySourceColor:
                    return MTLBlendFactor.OneMinusSource1Color;
                case ERHIBlendMode.SecondarySourceAlpha:
                    return MTLBlendFactor.Source1Alpha;
                case ERHIBlendMode.InverseSecondarySourceAlpha:
                    return MTLBlendFactor.OneMinusSource1Alpha;
                default:
                    return MTLBlendFactor.One;
            }
        }

        internal static MTLBlendOperation ConvertToMetalBlendOperation(in ERHIBlendOp blendOp)
        {
            switch (blendOp)
            {
                case ERHIBlendOp.Min:
                    return MTLBlendOperation.Min;
                case ERHIBlendOp.Max:
                    return MTLBlendOperation.Max;
                case ERHIBlendOp.Substract:
                    return MTLBlendOperation.Subtract;
                case ERHIBlendOp.ReverseSubstract:
                    return MTLBlendOperation.ReverseSubtract;
                default:
                    return MTLBlendOperation.Add;
            }
        }

        internal static MTLColorWriteMask ConvertToMetalColorWriteMask(in ERHIColorWriteChannel writeMask)
        {
            MTLColorWriteMask mask = MTLColorWriteMask.None;
            int bits = (int)writeMask;
            if ((bits & (int)ERHIColorWriteChannel.Red) != 0)
            {
                mask |= MTLColorWriteMask.Red;
            }

            if ((bits & (int)ERHIColorWriteChannel.Green) != 0)
            {
                mask |= MTLColorWriteMask.Green;
            }

            if ((bits & (int)ERHIColorWriteChannel.Blue) != 0)
            {
                mask |= MTLColorWriteMask.Blue;
            }

            if ((bits & (int)ERHIColorWriteChannel.Alpha) != 0)
            {
                mask |= MTLColorWriteMask.Alpha;
            }

            return mask;
        }

        internal static MTLVertexStepFunction ConvertToMetalVertexStepFunction(in ERHIVertexStepMode stepMode)
        {
            return stepMode == ERHIVertexStepMode.PerInstance ? MTLVertexStepFunction.PerInstance : MTLVertexStepFunction.PerVertex;
        }

        internal static MTLVertexFormat ConvertToMetalVertexFormat(in ERHISemanticFormat format)
        {
            switch (format)
            {
                case ERHISemanticFormat.Float:
                    return MTLVertexFormat.Float;
                case ERHISemanticFormat.Float2:
                    return MTLVertexFormat.Float2;
                case ERHISemanticFormat.Float3:
                    return MTLVertexFormat.Float3;
                case ERHISemanticFormat.Float4:
                    return MTLVertexFormat.Float4;
                case ERHISemanticFormat.UInt:
                    return MTLVertexFormat.UInt;
                case ERHISemanticFormat.UInt2:
                    return MTLVertexFormat.UInt2;
                case ERHISemanticFormat.UInt3:
                    return MTLVertexFormat.UInt3;
                case ERHISemanticFormat.UInt4:
                    return MTLVertexFormat.UInt4;
                case ERHISemanticFormat.Int:
                    return MTLVertexFormat.Int;
                case ERHISemanticFormat.Int2:
                    return MTLVertexFormat.Int2;
                case ERHISemanticFormat.Int3:
                    return MTLVertexFormat.Int3;
                case ERHISemanticFormat.Int4:
                    return MTLVertexFormat.Int4;
                case ERHISemanticFormat.Half:
                    return MTLVertexFormat.Half;
                case ERHISemanticFormat.Half2:
                    return MTLVertexFormat.Half2;
                case ERHISemanticFormat.Half4:
                    return MTLVertexFormat.Half4;
                default:
                    return MTLVertexFormat.Invalid;
            }
        }

        internal static MTLIndexType ConvertToMetalIndexType(in ERHIBufferFormat format)
        {
            return format == ERHIBufferFormat.UInt32 ? MTLIndexType.UInt32 : MTLIndexType.UInt16;
        }

        internal static MTLSamplerMinMagFilter ConvertToMetalFilter(in ERHIFilterMode filterMode)
        {
            return filterMode == ERHIFilterMode.Point ? MTLSamplerMinMagFilter.Nearest : MTLSamplerMinMagFilter.Linear;
        }

        internal static MTLSamplerMipFilter ConvertToMetalMipFilter(in ERHIFilterMode filterMode)
        {
            switch (filterMode)
            {
                case ERHIFilterMode.Point:
                    return MTLSamplerMipFilter.Nearest;
                case ERHIFilterMode.Linear:
                case ERHIFilterMode.Anisotropic:
                    return MTLSamplerMipFilter.Linear;
                default:
                    return MTLSamplerMipFilter.NotMipmapped;
            }
        }

        internal static MTLSamplerAddressMode ConvertToMetalAddressMode(in ERHIAddressMode addressMode)
        {
            switch (addressMode)
            {
                case ERHIAddressMode.ClampToEdge:
                    return MTLSamplerAddressMode.ClampToEdge;
                case ERHIAddressMode.MirrorRepeat:
                    return MTLSamplerAddressMode.MirrorRepeat;
                default:
                    return MTLSamplerAddressMode.Repeat;
            }
        }

        internal static MTLRenderStages ConvertToMetalRenderStages(in ERHIPipelineStage stage)
        {
            switch (stage)
            {
                case ERHIPipelineStage.Vertex:
                    return MTLRenderStages.RenderStageVertex;
                case ERHIPipelineStage.Fragment:
                    return MTLRenderStages.RenderStageFragment;
                default:
                    return MTLRenderStages.RenderStageVertex | MTLRenderStages.RenderStageFragment;
            }
        }

        internal static MTLBarrierScope ConvertToMetalBarrierScope(in ERHIResourceType type)
        {
            return type == ERHIResourceType.Buffer ? MTLBarrierScope.Buffers : MTLBarrierScope.Textures;
        }

        internal static ulong ConvertToMetal4Stages(in ERHIPipelineStage stage)
        {
            switch (stage)
            {
                case ERHIPipelineStage.Vertex:
                    return 1UL << 0;
                case ERHIPipelineStage.Fragment:
                    return 1UL << 1;
                case ERHIPipelineStage.Compute:
                    return 1UL << 27;
                case ERHIPipelineStage.RayTracing:
                    return 1UL << 29;
                case ERHIPipelineStage.Common:
                default:
                    return (ulong)long.MaxValue;
            }
        }

        internal static MTLSize ConvertToMetalSize(in uint3 value)
        {
            return new MTLSize(value.x, value.y, value.z);
        }

        internal static MTLOrigin ConvertToMetalOrigin(in uint3 value)
        {
            return new MTLOrigin(value.x, value.y, value.z);
        }
    }
}
