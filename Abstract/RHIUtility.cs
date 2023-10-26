using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
    public enum ERHIBackend : byte
    {
        Metal,
        Vulkan,
        DirectX12,
        Pending
    }

    public enum ERHIMatrixMajorons : byte
    {
        RowMajor,
        ColumnMajor,
        Pending
    }

    public enum ERHIDepthValueRange : byte
    {
        ZeroToOne,
        NegativeOneToOne,
        Pending
    }

    public enum ERHIMultiviewStrategy : byte
    {
        ViewIndex,
        Unsupported,
        RenderTargetIndex,
        Pending
    }

    public enum ERHIDeviceType : byte
    {
        Hardware,
        Software,
        Pending
    }

    public enum ERHIPipeline : byte
    {
        Transfer,
        Compute,
        Graphics,
        Pending
    }

    public enum ERHIGraphicsPipelineType : byte
    {
        Meshlet,
        Tradition,
        Pending
    }

    public enum ERHIQueryType : byte
    {
        Occlusion,
        Statistics,
        TimestampTransfer,
        TimestampGenerice,
        Pending
    }

    public enum ERHISemanticType : byte
    {
        Color = 0,
        Position = 1,
        TexCoord = 2,
        Normal = 3,
        Tangent = 4,
        Binormal = 5,
        BlendIndices = 6,
        BlendWeights = 7,
        ShadingRate = 8,
        Pending
    }

    public enum ERHISwapChainFormat : byte
    {
        R8G8B8A8_UNorm,
        R10G10B10A2_UNorm,
        R16G16B16A16_Float,
        Pending
    }

    public enum ERHIPixelFormat : byte
    {
        Unknown,
        // 8-Bits
        R8_UInt,
        R8_SInt,
        R8_UNorm,
        R8_SNorm,
        // 16-Bits
        R16_UInt,
        R16_SInt,
        R16_Float,
        R8G8_UInt,
        R8G8_SInt,
        R8G8_UNorm,
        R8G8_SNorm,
        // 32-Bits
        R32_UInt,
        R32_SInt,
        R32_Float,
        R16G16_UInt,
        R16G16_SInt,
        R16G16_Float,
        R8G8B8A8_UInt,
        R8G8B8A8_SInt,
        R8G8B8A8_UNorm,
        R8G8B8A8_UNorm_Srgb,
        R8G8B8A8_SNorm,
        B8G8R8A8_UNorm,
        B8G8R8A8_UNorm_Srgb,
        R99GB99_E5_Float,
        R10G10B10A2_UInt,
        R10G10B10A2_UNorm,
        R11G11B10_Float,
        // 64-Bits
        RG32_UInt,
        RG32_SInt,
        RG32_Float,
        R16G16B16A16_UInt,
        R16G16B16A16_SInt,
        R16G16B16A16_Float,
        // 128-Bits
        R32G32B32A32_UInt,
        R32G32B32A32_SInt,
        R32G32B32A32_Float,
        // Depth-Stencil
        D16_UNorm,
        D24_UNorm_S8_UInt,
        D32_Float,
        D32_Float_S8_UInt,
        // Block-Compressed
        RGBA_DXT1_SRGB,
        RGB_DXT1_UNorm,
        RGBA_DXT1_UNorm,
        RGBA_DXT3_SRGB,
        RGBA_DXT3_UNorm,
        RGBA_DXT5_SRGB,
        RGBA_DXT5_UNorm,
        R_BC4_UNorm,
        R_BC4_SNorm,
        RG_BC5_UNorm,
        RG_BC5_SNorm,
        RGB_BC6H_UFloat,
        RGB_BC6H_SFloat,
        RGBA_BC7_SRGB,
        RGBA_BC7_UNorm,
        // AdaptiveScalable-Compressed
        RGBA_ASTC4X4_SRGB,
        RGBA_ASTC4X4_UNorm,
        RGBA_ASTC4X4_UFloat,
        RGBA_ASTC5X5_SRGB,
        RGBA_ASTC5X5_UNorm,
        RGBA_ASTC5X5_UFloat,
        RGBA_ASTC6X6_SRGB,
        RGBA_ASTC6X6_UNorm,
        RGBA_ASTC6X6_UFloat,
        RGBA_ASTC8X8_SRGB,
        RGBA_ASTC8X8_UNorm,
        RGBA_ASTC8X8_UFloat,
        RGBA_ASTC10X10_SRGB,
        RGBA_ASTC10X10_UNorm,
        RGBA_ASTC10X10_UFloat,
        RGBA_ASTC12X12_SRGB,
        RGBA_ASTC12X12_UNorm,
        RGBA_ASTC12X12_UFloat,
        // YUV 4:2:2 Video resource format.
        YUV2,
        Pending
    }

    public enum ERHIBufferFormat : byte
    {
        Undefine,
        UInt16,
        UInt32,
        Pending
    }

    public enum ERHISemanticFormat : byte
    {
        Byte,
        Byte2,
        Byte4,
        UByte,
        UByte2,
        UByte4,
        ByteNormalized,
        Byte2Normalized,
        Byte4Normalized,
        UByteNormalized,
        UByte2Normalized,
        UByte4Normalized,
        Short,
        Short2,
        Short4,
        UShort,
        UShort2,
        UShort4,
        ShortNormalized,
        Short2Normalized,
        Short4Normalized,
        UShortNormalized,
        UShort2Normalized,
        UShort4Normalized,
        Int,
        Int2,
        Int3,
        Int4,
        UInt,
        UInt2,
        UInt3,
        UInt4,
        Half,
        Half2,
        Half4,
        Float,
        Float2,
        Float3,
        Float4,
        Pending
    }

    public enum ERHIShadingRate : byte
    {
        Rate1x1 = 0,
        Rate1x2 = 1,
        Rate2x1 = 4,
        Rate2x2 = 5,
        Rate2x4 = 6,
        Rate4x2 = 9,
        Rate4x4 = 10,
        Pending
    }

    public enum ERHIShadingRateCombiner : byte
    {
        Min = 0,
        Max = 1,
        Sum = 2,
        Override = 3,
        Passthrough = 4,
        Pending
    }

    public enum ERHITextureDimension : byte
    {
        Texture2D,
        Texture2DMS,
        Texture2DArray,
        Texture2DArrayMS,
        TextureCube,
        TextureCubeArray,
        Texture3D,
        Pending
    }

    public enum ERHIAddressMode : byte
    {
        Repeat,
        ClampToEdge,
        MirrorRepeat,
        Pending
    }

    public enum ERHIFilterMode : byte
    {
        Point,
        Linear,
        Anisotropic,
        Pending
    }

    public enum ERHIBindType : byte
    {
        Sampler,
        Buffer,
        AccelStruct,
        StorageBuffer,
        UniformBuffer,
        Texture2D,
        Texture2DMS,
        Texture2DArray,
        Texture2DArrayMS,
        TextureCube,
        TextureCubeArray,
        Texture3D,
        StorageTexture2D,
        StorageTexture2DMS,
        StorageTexture2DArray,
        StorageTexture2DArrayMS,
        StorageTextureCube,
        StorageTextureCubeArray,
        StorageTexture3D,
        Pending
    }

    public enum ERHIVertexStepMode : byte
    {
        PerVertex,
        PerInstance,
        Pending
    }

    public enum ERHIPrimitiveTopology : byte
    {
        PointList,
        LineList,
        LineStrip,
        TriangleList,
        TriangleStrip,
        LineListAdj,
        LineStripAdj,
        TriangleListAdj,
        TriangleStripAdj,
        Pending
    }

    public enum ERHIPrimitiveTopologyType : byte
    {
        Point,
        Line,
        Triangle,
        Pending
    }

    public enum ERHISampleCount : byte
    {
        None = 1,
        Count2 = 2,
        Count4 = 4,
        Count8 = 8,
        Pending
    }

    public enum ERHIBlendOp : byte
    {
        Add = 1,
        Substract = 2,
        ReverseSubstract = 3,
        Min = 4,
        Max = 5,
        Pending
    }

    public enum ERHIBlendMode : byte
    {
        Zero = 1,
        One = 2,
        DstColor = 9,
        SrcColor = 3,
        OneMinusDstColor = 10,
        SrcAlpha = 5,
        OneMinusSrcColor = 4,
        DstAlpha = 7,
        OneMinusDstAlpha = 8,
        SrcAlphaSaturate = 11,
        OneMinusSrcAlpha = 6,
        Pending
    }

    public enum ERHIColorWriteChannel : byte
    {
        None = 0,
        Red = 0x1,
        Green = 0x2,
        Blue = 0x4,
        Alpha = 0x8,
        All = Red | Green | Blue | Alpha,
        Pending
    }

    public enum ERHIComparisonMode : byte
    {
        Never = 0,
        Less = 1,
        Equal = 2,
        LessEqual = 3,
        Greater = 4,
        NotEqual = 5,
        GreaterEqual = 6,
        Always = 7,
        Pending
    }

    public enum ERHIStencilOp : byte
    {
        Keep = 1,
        Zero = 2,
        Replace = 3,
        IncrementSaturation = 4,
        DecrementSaturation = 5,
        Invert = 6,
        Increment = 7,
        Decrement = 8,
        Pending
    }

    public enum ERHIFillMode : byte
    {
        Solid = 3,
        Wireframe = 2,
        Pending
    }

    public enum ERHICullMode : byte
    {
        None = 1,
        Back = 3,
        Front = 2,
        Pending
    }

    public enum ERHILoadAction : byte
    {
        Load,
        Clear,
        DontCare,
        Pending
    }

    public enum ERHIStoreAction : byte
    {
        Store,
        Resolve,
        StoreAndResolve,
        DontCare,
        Pending
    }

    public enum ERHIPresentMode : byte
    {
        VSync,
        Immediately,
        Pending
    }

    public enum ERHIBarrierType : byte
    {
        UAV,
        Aliasing,
        Triansition
    }

    public enum ERHIResourceType : byte
    {
        Buffer,
        Texture,
        Pending
    }

    public enum ERHIStorageMode : byte
    {
        GPULocal,
        Readback,
        GPUUpload,
        HostUpload,
        Memoryless,
        Pending
    }

    public enum ERHIBufferState
    {
        Undefine = 0x00,
        CopySrc = 0x01,
        CopyDst = 0x02,
        IndexBuffer = 0x04,
        VertexBuffer = 0x08,
        ConstantBuffer = 0x10,
        IndirectArgument = 0x20,
        ShaderResource = 0x40,
        UnorderedAccess = 0x80,
        AccelStructRead = 0x0100,
        AccelStructWrite = 0x0200,
        AccelStructBuildInput = 0x0400,
        AccelStructBuildBlast = 0x0800,
        Pending
    }

    public enum ERHITextureState
    {
        Undefine = 0x00,
        Present = 0x01,
        CopySrc = 0x02,
        CopyDst = 0x04,
        ResolveSrc = 0x08,
        ResolveDst = 0x10,
        DepthRead = 0x20,
        DepthWrite = 0x40,
        RenderTarget = 0x80,
        ShaderResource = 0x100,
        UnorderedAccess = 0x200,
        ShadingRateSurface = 0x0400,
        Pending
    }

    public enum ERHIBufferUsage
    {
        CopySrc = 0x01,
        CopyDst = 0x02,
        AccelStruct = 0x04,
        IndexBuffer = 0x08,
        VertexBuffer = 0x10,
        UniformBuffer = 0x20,
        IndirectBuffer = 0x40,
        ShaderResource = 0x80,
        UnorderedAccess = 0x100,
        Pending
    }

    public enum ERHIBufferViewType : byte
    {
        AccelStruct,
        UniformBuffer,
        ShaderResource,
        UnorderedAccess,
        Pending
    }

    public enum ERHITextureUsage
    {
        CopySrc = 0x01,
        CopyDst = 0x02,
        DepthStencil = 0x04,
        RenderTarget = 0x08,
        ResolveTarget = 0x10,
        ShaderResource = 0x20,
        UnorderedAccess = 0x40,
        Pending
    }

    public enum ERHITextureViewType : byte
    {
        //DepthStencil,
        //RenderTarget,
        ShaderResource,
        UnorderedAccess,
        Pending
    }

    public enum ERHIFunctionType : byte
    {
        Vertex,
        Fragment,
        Compute,
        Task,
        Mesh,
        RayTracing,
        Pending
    }

    public enum ERHIFunctionStage
    {
        Vertex = 0x1,
        Fragment = 0x2,
        Compute = 0x4,
        Task = 0x8,
        Mesh = 0x10,
        AllGraphics = 0x20,
        //AllGraphics = Vertex | Fragment,
        RayTracing = 0x40,
        All = 0x80,
        //All = Vertex | Fragment | Compute | Task | Mesh | RayTracing,
        Pending
    }

    public enum ERHIHitGroupType : byte
    {
        Triangles,
        Procedural,
        Pending
    }

    internal static unsafe class RHIUtility
    {
        internal static ERHIPixelFormat ConvertToPixelFormat(in ERHISwapChainFormat swapChainFormat)
        {
            switch (swapChainFormat)
            {
                case ERHISwapChainFormat.R8G8B8A8_UNorm:
                    return ERHIPixelFormat.R8G8B8A8_UNorm;

                case ERHISwapChainFormat.R10G10B10A2_UNorm:
                    return ERHIPixelFormat.R10G10B10A2_UNorm;

                case ERHISwapChainFormat.R16G16B16A16_Float:
                    return ERHIPixelFormat.R16G16B16A16_Float;
            }
            return ERHIPixelFormat.R8G8B8A8_UNorm_Srgb;
        }

        /*internal static ERHIBufferState ConvertToBufferStateFormStorageMode(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.Static:
                    return ERHIBufferState.GenericRead;

                case ERHIStorageMode.Dynamic:
                    return ERHIBufferState.GenericRead;

                case ERHIStorageMode.Staging:
                    return ERHIBufferState.CopyDst;

                default:
                    return ERHIBufferState.Common;
            }
        }

        internal static ERHITextureState ConvertToTextureStateFormStorageMode(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.Static:
                    return ERHITextureState.GenericRead;

                case ERHIStorageMode.Dynamic:
                    return ERHITextureState.GenericRead;

                case ERHIStorageMode.Staging:
                    return ERHITextureState.CopyDst;

                default:
                    return ERHITextureState.Common;
            }
        }*/
    }
}
