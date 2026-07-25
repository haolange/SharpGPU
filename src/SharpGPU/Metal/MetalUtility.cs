using System;
using SharpMetal.Metal;
using SharpGPU.Mathematics;

namespace SharpGPU
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
                // 8-Bits
                case ERHIPixelFormat.R8_UInt:
                    return MTLPixelFormat.R8Uint;
                case ERHIPixelFormat.R8_SInt:
                    return MTLPixelFormat.R8Sint;
                case ERHIPixelFormat.R8_UNorm:
                    return MTLPixelFormat.R8Unorm;
                case ERHIPixelFormat.R8_SNorm:
                    return MTLPixelFormat.R8Snorm;
                // 16-Bits
                case ERHIPixelFormat.R16_UInt:
                    return MTLPixelFormat.R16Uint;
                case ERHIPixelFormat.R16_SInt:
                    return MTLPixelFormat.R16Sint;
                case ERHIPixelFormat.R16_Float:
                    return MTLPixelFormat.R16Float;
                case ERHIPixelFormat.R8G8_UInt:
                    return MTLPixelFormat.RG8Uint;
                case ERHIPixelFormat.R8G8_SInt:
                    return MTLPixelFormat.RG8Sint;
                case ERHIPixelFormat.R8G8_UNorm:
                    return MTLPixelFormat.RG8Unorm;
                case ERHIPixelFormat.R8G8_SNorm:
                    return MTLPixelFormat.RG8Snorm;
                // 32-Bits
                case ERHIPixelFormat.R32_UInt:
                    return MTLPixelFormat.R32Uint;
                case ERHIPixelFormat.R32_SInt:
                    return MTLPixelFormat.R32Sint;
                case ERHIPixelFormat.R32_Float:
                    return MTLPixelFormat.R32Float;
                case ERHIPixelFormat.R16G16_UInt:
                    return MTLPixelFormat.RG16Uint;
                case ERHIPixelFormat.R16G16_SInt:
                    return MTLPixelFormat.RG16Sint;
                case ERHIPixelFormat.R16G16_Float:
                    return MTLPixelFormat.RG16Float;
                case ERHIPixelFormat.R8G8B8A8_UInt:
                    return MTLPixelFormat.RGBA8Uint;
                case ERHIPixelFormat.R8G8B8A8_SInt:
                    return MTLPixelFormat.RGBA8Sint;
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return MTLPixelFormat.RGBA8Unorm;
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return MTLPixelFormat.RGBA8UnormsRGB;
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return MTLPixelFormat.RGBA8Snorm;
                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return MTLPixelFormat.BGRA8Unorm;
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return MTLPixelFormat.BGRA8UnormsRGB;
                case ERHIPixelFormat.R99GB99_E5_Float:
                    return MTLPixelFormat.RGB9E5Float;
                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return MTLPixelFormat.RGB10A2Uint;
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return MTLPixelFormat.RGB10A2Unorm;
                case ERHIPixelFormat.R11G11B10_Float:
                    return MTLPixelFormat.RG11B10Float;
                // 64-Bits
                case ERHIPixelFormat.RG32_UInt:
                    return MTLPixelFormat.RG32Uint;
                case ERHIPixelFormat.RG32_SInt:
                    return MTLPixelFormat.RG32Sint;
                case ERHIPixelFormat.RG32_Float:
                    return MTLPixelFormat.RG32Float;
                case ERHIPixelFormat.R16G16B16A16_UInt:
                    return MTLPixelFormat.RGBA16Uint;
                case ERHIPixelFormat.R16G16B16A16_SInt:
                    return MTLPixelFormat.RGBA16Sint;
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return MTLPixelFormat.RGBA16Float;
                // 128-Bits
                case ERHIPixelFormat.R32G32B32A32_UInt:
                    return MTLPixelFormat.RGBA32Uint;
                case ERHIPixelFormat.R32G32B32A32_SInt:
                    return MTLPixelFormat.RGBA32Sint;
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return MTLPixelFormat.RGBA32Float;
                // Depth-Stencil
                case ERHIPixelFormat.D16_UNorm:
                    return MTLPixelFormat.Depth16Unorm;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return MTLPixelFormat.Depth24UnormStencil8;
                case ERHIPixelFormat.D32_Float:
                    return MTLPixelFormat.Depth32Float;
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return MTLPixelFormat.Depth32FloatStencil8;
                // Block-Compressed (BC formats available on macOS, not iOS)
                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return MTLPixelFormat.BC1RGBAsRGB;
                case ERHIPixelFormat.RGB_DXT1_UNorm:
                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return MTLPixelFormat.BC1RGBA;
                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return MTLPixelFormat.BC2RGBAsRGB;
                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return MTLPixelFormat.BC2RGBA;
                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return MTLPixelFormat.BC3RGBAsRGB;
                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return MTLPixelFormat.BC3RGBA;
                case ERHIPixelFormat.R_BC4_UNorm:
                    return MTLPixelFormat.BC4RUnorm;
                case ERHIPixelFormat.R_BC4_SNorm:
                    return MTLPixelFormat.BC4RSnorm;
                case ERHIPixelFormat.RG_BC5_UNorm:
                    return MTLPixelFormat.BC5RGUnorm;
                case ERHIPixelFormat.RG_BC5_SNorm:
                    return MTLPixelFormat.BC5RGSnorm;
                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return MTLPixelFormat.BC6HRGBUfloat;
                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return MTLPixelFormat.BC6HRGBFloat;
                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return MTLPixelFormat.BC7RGBAUnormsRGB;
                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return MTLPixelFormat.BC7RGBAUnorm;
                // ASTC (available on iOS and Apple Silicon Macs)
                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                    return MTLPixelFormat.ASTC4x4sRGB;
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                    return MTLPixelFormat.ASTC4x4LDR;
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                    return MTLPixelFormat.ASTC4x4HDR;
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                    return MTLPixelFormat.ASTC5x5sRGB;
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                    return MTLPixelFormat.ASTC5x5LDR;
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                    return MTLPixelFormat.ASTC5x5HDR;
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                    return MTLPixelFormat.ASTC6x6sRGB;
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                    return MTLPixelFormat.ASTC6x6LDR;
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                    return MTLPixelFormat.ASTC6x6HDR;
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                    return MTLPixelFormat.ASTC8x8sRGB;
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                    return MTLPixelFormat.ASTC8x8LDR;
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                    return MTLPixelFormat.ASTC8x8HDR;
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                    return MTLPixelFormat.ASTC10x10sRGB;
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                    return MTLPixelFormat.ASTC10x10LDR;
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                    return MTLPixelFormat.ASTC10x10HDR;
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                    return MTLPixelFormat.ASTC12x12sRGB;
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                    return MTLPixelFormat.ASTC12x12LDR;
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return MTLPixelFormat.ASTC12x12HDR;
                default:
                    return MTLPixelFormat.Invalid;
            }
        }

        internal static ERHIPixelFormat ConvertToRhiPixelFormat(in MTLPixelFormat format)
        {
            switch (format)
            {
                // 8-Bits
                case MTLPixelFormat.R8Uint:
                    return ERHIPixelFormat.R8_UInt;
                case MTLPixelFormat.R8Sint:
                    return ERHIPixelFormat.R8_SInt;
                case MTLPixelFormat.R8Unorm:
                    return ERHIPixelFormat.R8_UNorm;
                case MTLPixelFormat.R8Snorm:
                    return ERHIPixelFormat.R8_SNorm;
                // 16-Bits
                case MTLPixelFormat.R16Uint:
                    return ERHIPixelFormat.R16_UInt;
                case MTLPixelFormat.R16Sint:
                    return ERHIPixelFormat.R16_SInt;
                case MTLPixelFormat.R16Float:
                    return ERHIPixelFormat.R16_Float;
                case MTLPixelFormat.RG8Uint:
                    return ERHIPixelFormat.R8G8_UInt;
                case MTLPixelFormat.RG8Sint:
                    return ERHIPixelFormat.R8G8_SInt;
                case MTLPixelFormat.RG8Unorm:
                    return ERHIPixelFormat.R8G8_UNorm;
                case MTLPixelFormat.RG8Snorm:
                    return ERHIPixelFormat.R8G8_SNorm;
                // 32-Bits
                case MTLPixelFormat.R32Uint:
                    return ERHIPixelFormat.R32_UInt;
                case MTLPixelFormat.R32Sint:
                    return ERHIPixelFormat.R32_SInt;
                case MTLPixelFormat.R32Float:
                    return ERHIPixelFormat.R32_Float;
                case MTLPixelFormat.RG16Uint:
                    return ERHIPixelFormat.R16G16_UInt;
                case MTLPixelFormat.RG16Sint:
                    return ERHIPixelFormat.R16G16_SInt;
                case MTLPixelFormat.RG16Float:
                    return ERHIPixelFormat.R16G16_Float;
                case MTLPixelFormat.RGBA8Uint:
                    return ERHIPixelFormat.R8G8B8A8_UInt;
                case MTLPixelFormat.RGBA8Sint:
                    return ERHIPixelFormat.R8G8B8A8_SInt;
                case MTLPixelFormat.RGBA8Unorm:
                    return ERHIPixelFormat.R8G8B8A8_UNorm;
                case MTLPixelFormat.RGBA8UnormsRGB:
                    return ERHIPixelFormat.R8G8B8A8_UNorm_Srgb;
                case MTLPixelFormat.RGBA8Snorm:
                    return ERHIPixelFormat.R8G8B8A8_SNorm;
                case MTLPixelFormat.BGRA8Unorm:
                    return ERHIPixelFormat.B8G8R8A8_UNorm;
                case MTLPixelFormat.BGRA8UnormsRGB:
                    return ERHIPixelFormat.B8G8R8A8_UNorm_Srgb;
                case MTLPixelFormat.RGB9E5Float:
                    return ERHIPixelFormat.R99GB99_E5_Float;
                case MTLPixelFormat.RGB10A2Unorm:
                    return ERHIPixelFormat.R10G10B10A2_UNorm;
                case MTLPixelFormat.RGB10A2Uint:
                    return ERHIPixelFormat.R10G10B10A2_UInt;
                case MTLPixelFormat.RG11B10Float:
                    return ERHIPixelFormat.R11G11B10_Float;
                // 64-Bits
                case MTLPixelFormat.RG32Uint:
                    return ERHIPixelFormat.RG32_UInt;
                case MTLPixelFormat.RG32Sint:
                    return ERHIPixelFormat.RG32_SInt;
                case MTLPixelFormat.RG32Float:
                    return ERHIPixelFormat.RG32_Float;
                case MTLPixelFormat.RGBA16Uint:
                    return ERHIPixelFormat.R16G16B16A16_UInt;
                case MTLPixelFormat.RGBA16Sint:
                    return ERHIPixelFormat.R16G16B16A16_SInt;
                case MTLPixelFormat.RGBA16Float:
                    return ERHIPixelFormat.R16G16B16A16_Float;
                // 128-Bits
                case MTLPixelFormat.RGBA32Uint:
                    return ERHIPixelFormat.R32G32B32A32_UInt;
                case MTLPixelFormat.RGBA32Sint:
                    return ERHIPixelFormat.R32G32B32A32_SInt;
                case MTLPixelFormat.RGBA32Float:
                    return ERHIPixelFormat.R32G32B32A32_Float;
                // Depth-Stencil
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
                case ERHISemanticFormat.Short:
                    return MTLVertexFormat.Short;
                case ERHISemanticFormat.Short2:
                    return MTLVertexFormat.Short2;
                case ERHISemanticFormat.Short4:
                    return MTLVertexFormat.Short4;
                case ERHISemanticFormat.UShort:
                    return MTLVertexFormat.UShort;
                case ERHISemanticFormat.UShort2:
                    return MTLVertexFormat.UShort2;
                case ERHISemanticFormat.UShort4:
                    return MTLVertexFormat.UShort4;
                case ERHISemanticFormat.ShortNormalized:
                    return MTLVertexFormat.ShortNormalized;
                case ERHISemanticFormat.Short2Normalized:
                    return MTLVertexFormat.Short2Normalized;
                case ERHISemanticFormat.Short4Normalized:
                    return MTLVertexFormat.Short4Normalized;
                case ERHISemanticFormat.UShortNormalized:
                    return MTLVertexFormat.UShortNormalized;
                case ERHISemanticFormat.UShort2Normalized:
                    return MTLVertexFormat.UShort2Normalized;
                case ERHISemanticFormat.UShort4Normalized:
                    return MTLVertexFormat.UShort4Normalized;
                case ERHISemanticFormat.Byte:
                    return MTLVertexFormat.Char;
                case ERHISemanticFormat.Byte2:
                    return MTLVertexFormat.Char2;
                case ERHISemanticFormat.Byte4:
                    return MTLVertexFormat.Char4;
                case ERHISemanticFormat.UByte:
                    return MTLVertexFormat.UChar;
                case ERHISemanticFormat.UByte2:
                    return MTLVertexFormat.UChar2;
                case ERHISemanticFormat.UByte4:
                    return MTLVertexFormat.UChar4;
                case ERHISemanticFormat.ByteNormalized:
                    return MTLVertexFormat.CharNormalized;
                case ERHISemanticFormat.Byte2Normalized:
                    return MTLVertexFormat.Char2Normalized;
                case ERHISemanticFormat.Byte4Normalized:
                    return MTLVertexFormat.Char4Normalized;
                case ERHISemanticFormat.UByteNormalized:
                    return MTLVertexFormat.UCharNormalized;
                case ERHISemanticFormat.UByte2Normalized:
                    return MTLVertexFormat.UChar2Normalized;
                case ERHISemanticFormat.UByte4Normalized:
                    return MTLVertexFormat.UChar4Normalized;
                default:
                    return MTLVertexFormat.Invalid;
            }
        }

        internal static MTLIndexType ConvertToMetalIndexType(in ERHIBufferFormat format)
        {
            return format == ERHIBufferFormat.UInt32 ? MTLIndexType.UInt32 : MTLIndexType.UInt16;
        }

        internal static MTLAttributeFormat ConvertToMetalAttributeFormat(in ERHIPixelFormat format)
        {
            switch (format)
            {
                case ERHIPixelFormat.R32_Float:
                    return MTLAttributeFormat.Float;
                case ERHIPixelFormat.R16_Float:
                    return MTLAttributeFormat.Half;
                case ERHIPixelFormat.R16G16_Float:
                    return MTLAttributeFormat.Half2;
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return MTLAttributeFormat.Half4;
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return MTLAttributeFormat.Float4;
                default:
                    throw new NotSupportedException($"Unsupported RT attribute format '{format}' for Metal acceleration structure.");
            }
        }

        internal static MTLAccelerationStructureUsage ConvertToMetalAccelerationStructureUsage(in EAccelStructFlag flag)
        {
            MTLAccelerationStructureUsage usage = MTLAccelerationStructureUsage.None;
            if ((flag & EAccelStructFlag.AllowUpdate) != 0 || (flag & EAccelStructFlag.PerformUpdate) != 0)
            {
                usage |= MTLAccelerationStructureUsage.Refit;
            }

            if ((flag & EAccelStructFlag.PreferFastBuild) != 0)
            {
                usage |= MTLAccelerationStructureUsage.PreferFastBuild;
            }

            return usage;
        }

        internal static MTLAccelerationStructureInstanceOptions ConvertToMetalAccelerationStructureInstanceOptions(in EAccelStructInstanceFlag flag)
        {
            MTLAccelerationStructureInstanceOptions options = MTLAccelerationStructureInstanceOptions.None;
            if ((flag & EAccelStructInstanceFlag.TriangleCullDisable) != 0)
            {
                options |= MTLAccelerationStructureInstanceOptions.DisableTriangleCulling;
            }

            if ((flag & EAccelStructInstanceFlag.TriangleFrontCounterclockwise) != 0)
            {
                options |= MTLAccelerationStructureInstanceOptions.TriangleFrontFacingWindingCounterClockwise;
            }

            if ((flag & EAccelStructInstanceFlag.ForceOpaque) != 0)
            {
                options |= MTLAccelerationStructureInstanceOptions.Opaque;
            }

            if ((flag & EAccelStructInstanceFlag.ForceNonOpaque) != 0)
            {
                options |= MTLAccelerationStructureInstanceOptions.NonOpaque;
            }

            return options;
        }

        internal static bool IsMetalGeometryOpaque(in EAccelStructGeometryFlag flag)
        {
            return (flag & EAccelStructGeometryFlag.Opaque) != 0;
        }

        internal static bool AllowMetalDuplicateIntersectionInvocation(in EAccelStructGeometryFlag flag)
        {
            return (flag & EAccelStructGeometryFlag.NoDuplicateAnyhitInverseOcation) == 0;
        }

        internal static MTLCurveType ConvertToMetalCurveType(in EAccelStructCurveType curveType)
        {
            switch (curveType)
            {
                case EAccelStructCurveType.Round:
                    return MTLCurveType.Round;
                case EAccelStructCurveType.Flat:
                    return MTLCurveType.Flat;
                default:
                    throw new NotSupportedException($"Unsupported curve type '{curveType}'.");
            }
        }

        internal static MTLCurveBasis ConvertToMetalCurveBasis(in EAccelStructCurveBasis curveBasis)
        {
            switch (curveBasis)
            {
                case EAccelStructCurveBasis.BSpline:
                    return MTLCurveBasis.BSpline;
                case EAccelStructCurveBasis.CatmullRom:
                    return MTLCurveBasis.CatmullRom;
                case EAccelStructCurveBasis.Linear:
                    return MTLCurveBasis.Linear;
                case EAccelStructCurveBasis.Bezier:
                    return MTLCurveBasis.Bezier;
                default:
                    throw new NotSupportedException($"Unsupported curve basis '{curveBasis}'.");
            }
        }

        internal static MTLCurveEndCaps ConvertToMetalCurveEndCaps(in EAccelStructCurveEndCaps curveEndCaps)
        {
            switch (curveEndCaps)
            {
                case EAccelStructCurveEndCaps.None:
                    return MTLCurveEndCaps.None;
                case EAccelStructCurveEndCaps.Disk:
                    return MTLCurveEndCaps.Disk;
                case EAccelStructCurveEndCaps.Sphere:
                    return MTLCurveEndCaps.Sphere;
                default:
                    throw new NotSupportedException($"Unsupported curve end caps '{curveEndCaps}'.");
            }
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

        internal static ulong ConvertToMetal4Stages(in ERHISyncStageMask stages)
        {
            if (stages == ERHISyncStageMask.None)
            {
                return 0;
            }

            const ERHISyncStageMask unsupportedSharpGpuStages =
                ERHISyncStageMask.Task |
                ERHISyncStageMask.Mesh |
                ERHISyncStageMask.MachineLearning;
            const ERHISyncStageMask knownStages =
                ERHISyncStageMask.Transfer |
                ERHISyncStageMask.Indirect |
                ERHISyncStageMask.IndexInput |
                ERHISyncStageMask.VertexInput |
                ERHISyncStageMask.Vertex |
                ERHISyncStageMask.Fragment |
                ERHISyncStageMask.Compute |
                unsupportedSharpGpuStages |
                ERHISyncStageMask.RayTracing |
                ERHISyncStageMask.AccelStructBuild |
                ERHISyncStageMask.AccelStructCopy;

            ERHISyncStageMask normalizedStages = stages;
            if (stages == ERHISyncStageMask.All)
            {
                normalizedStages = knownStages & ~unsupportedSharpGpuStages;
            }
            else if (stages == ERHISyncStageMask.AllGraphics)
            {
                normalizedStages =
                    ERHISyncStageMask.Indirect |
                    ERHISyncStageMask.IndexInput |
                    ERHISyncStageMask.VertexInput |
                    ERHISyncStageMask.Vertex |
                    ERHISyncStageMask.Fragment;
            }
            else if (stages == ERHISyncStageMask.AllShading)
            {
                normalizedStages =
                    ERHISyncStageMask.Vertex |
                    ERHISyncStageMask.Fragment |
                    ERHISyncStageMask.Compute |
                    ERHISyncStageMask.RayTracing;
            }
            else
            {
                ERHISyncStageMask unknownStages = stages & ~knownStages;
                if (unknownStages != ERHISyncStageMask.None)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(stages),
                        stages,
                        "Metal synchronization contains an unknown stage bit.");
                }

                ERHISyncStageMask unsupportedStages =
                    stages & unsupportedSharpGpuStages;
                if (unsupportedStages != ERHISyncStageMask.None)
                {
                    throw new NotSupportedException(
                        $"Metal synchronization stages '{unsupportedStages}' are unavailable because the corresponding SharpGPU backend capability is unavailable.");
                }
            }

            ulong result = 0;
            if ((normalizedStages & ERHISyncStageMask.Transfer) != 0) result |= 1UL << 27;
            if ((normalizedStages & ERHISyncStageMask.Indirect) != 0) result |= 1UL << 27;
            if ((normalizedStages & ERHISyncStageMask.IndexInput) != 0) result |= 1UL << 0;
            if ((normalizedStages & ERHISyncStageMask.VertexInput) != 0) result |= 1UL << 0;
            if ((normalizedStages & ERHISyncStageMask.Vertex) != 0) result |= 1UL << 0;
            if ((normalizedStages & ERHISyncStageMask.Fragment) != 0) result |= 1UL << 1;
            if ((normalizedStages & ERHISyncStageMask.Compute) != 0) result |= 1UL << 27;
            if ((normalizedStages & ERHISyncStageMask.RayTracing) != 0) result |= 1UL << 29;
            if ((normalizedStages & ERHISyncStageMask.AccelStructBuild) != 0) result |= 1UL << 29;
            if ((normalizedStages & ERHISyncStageMask.AccelStructCopy) != 0) result |= 1UL << 29;
            return result;
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
