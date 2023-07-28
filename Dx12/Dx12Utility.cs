using System;
using System.Text;
using Infinity.Core;
using NUnit.Framework;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;
using TerraFX.Interop.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe struct Dx12DescriptorInfo
    {
        public int Index;
        public ID3D12DescriptorHeap* DescriptorHeap;
        public D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle;
        public D3D12_GPU_DESCRIPTOR_HANDLE GpuHandle;
    };

    internal unsafe class Dx12DescriptorHeap : Disposal
    {
        public uint DescriptorSize => m_DescriptorSize;
        public D3D12_DESCRIPTOR_HEAP_TYPE Type => m_Type;
        public ID3D12DescriptorHeap* DescriptorHeap => m_DescriptorHeap;
        public D3D12_CPU_DESCRIPTOR_HANDLE CpuStartHandle => m_DescriptorHeap->GetCPUDescriptorHandleForHeapStart();
        public D3D12_GPU_DESCRIPTOR_HANDLE GpuStartHandle => m_DescriptorHeap->GetGPUDescriptorHandleForHeapStart();

        private uint m_DescriptorSize;
        private TValueArray<int> m_CacheMap;
        private D3D12_DESCRIPTOR_HEAP_TYPE m_Type;
        private ID3D12DescriptorHeap* m_DescriptorHeap;

        public Dx12DescriptorHeap(ID3D12Device8* device, in D3D12_DESCRIPTOR_HEAP_TYPE type, in D3D12_DESCRIPTOR_HEAP_FLAGS flag, in uint count)
        {
            m_CacheMap = new TValueArray<int>((int)count);
            for (int i = 0; i < (int)count; ++i)
            {
                m_CacheMap.Add(i);
            }

            m_Type = type;
            m_DescriptorSize = device->GetDescriptorHandleIncrementSize(type);

            D3D12_DESCRIPTOR_HEAP_DESC descriptorInfo;
            descriptorInfo.Type = type;
            descriptorInfo.Flags = flag;
            descriptorInfo.NumDescriptors = count;

            ID3D12DescriptorHeap* descriptorHeap;
            bool success = SUCCEEDED(device->CreateDescriptorHeap(&descriptorInfo, __uuidof<ID3D12DescriptorHeap>(), (void**)&descriptorHeap));
#if DEBUG
            Debug.Assert(success);
#endif
            m_DescriptorHeap = descriptorHeap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate()
        {
            int index = m_CacheMap[m_CacheMap.length - 1];
            m_CacheMap.RemoveSwapAtIndex(m_CacheMap.length - 1);
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate(in int count)
        {
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(in int index)
        {
            m_CacheMap.Add(index);
        }

        protected override void Release()
        {
            m_CacheMap.Dispose();
            m_DescriptorHeap->Release();
        }
    }

    internal static unsafe class Dx12Utility
    {
        public static void CHECK_BOOL(bool cond, [CallerFilePath] string __FILE__ = "", [CallerLineNumber] int __LINE__ = 0, [CallerArgumentExpression("cond")] string expr = "")
            => Assert.False(!cond, $"{__FILE__}({__LINE__}): !({(string.IsNullOrEmpty(expr) ? cond : expr)})");

        public static void CHECK_HR(int hr, [CallerFilePath] string __FILE__ = "", [CallerLineNumber] int __LINE__ = 0, [CallerArgumentExpression("hr")] string expr = "")
            => Assert.False(FAILED(hr), $"{__FILE__}({__LINE__}): FAILED({(string.IsNullOrEmpty(expr) ? hr.ToString("X8") : expr)})");

        internal static D3D12_QUERY_TYPE ConvertToDx12QueryType(in EQueryType queryType)
        {
            switch (queryType)
            {
                case EQueryType.Occlusion:
                    return D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION;

                case EQueryType.BinaryOcclusion:
                    return D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION;

                default:
                    return D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP;
            }
        }

        internal static D3D12_QUERY_HEAP_TYPE ConvertToDx12QueryHeapType(in EQueryType queryType)
        {
            switch (queryType)
            {
                case EQueryType.Occlusion:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_OCCLUSION;

                case EQueryType.BinaryOcclusion:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_OCCLUSION;

                default:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP;
            }
        }

        internal static D3D12_COMMAND_LIST_TYPE ConvertToDx12QueueType(in EQueueType queueType)
        {
            switch (queueType)
            {
                case EQueueType.Compute:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COMPUTE;

                case EQueueType.Graphics:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT;

                default:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COPY;
            }
        }

        internal static uint ConvertToDx12SyncInterval(in EPresentMode presentMode)
        {
            switch (presentMode)
            {
                case EPresentMode.VSync:
                    return 1;

                case EPresentMode.Immediately:
                    return 0;

                default:
                    return 0;
            }
        }

        internal static DXGI_SWAP_EFFECT ConvertToDx12SwapEffect(in EPresentMode presentMode)
        {
            switch (presentMode)
            {
                case EPresentMode.VSync:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;

                case EPresentMode.Immediately:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD;

                default:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD;
            }
        }

        internal static D3D12_FILTER ConvertToDx12Filter(in RHISamplerDescriptor descriptor)
        {
            EFilterMode minFilter = descriptor.MinFilter;
            EFilterMode magFilter = descriptor.MagFilter;
            EFilterMode mipFilter = descriptor.MipFilter;

            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_POINT_MIP_LINEAR; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_MIP_LINEAR; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_MIP_POINT; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_LINEAR_MIP_POINT; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR; }
            if (minFilter == EFilterMode.Anisotropic || magFilter == EFilterMode.Anisotropic || mipFilter == EFilterMode.Anisotropic) { return D3D12_FILTER.D3D12_FILTER_ANISOTROPIC; }
            return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT;
        }

        internal static D3D12_TEXTURE_ADDRESS_MODE ConvertToDx12AddressMode(in EAddressMode addressMode)
        {
            switch (addressMode)
            {
                case EAddressMode.MirrorRepeat:
                    return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_MIRROR;

                case EAddressMode.ClampToEdge:
                    return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
            }
            return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        }

        // convert to dx12 format COMPARISON func
        internal static D3D12_COMPARISON_FUNC ConvertToDx12ComparisonMode(in EComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case EComparisonMode.Less:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS;

                case EComparisonMode.Equal:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_EQUAL;

                case EComparisonMode.LessEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS_EQUAL;

                case EComparisonMode.Greater:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER;

                case EComparisonMode.NotEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NOT_EQUAL;

                case EComparisonMode.GreaterEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER_EQUAL;

                case EComparisonMode.Always:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS;
            }

            return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER;
        }

        internal static D3D12_HEAP_TYPE ConvertToDx12HeapTypeByStorage(in EStorageMode storageMode)
        {
            switch (storageMode)
            {
                case EStorageMode.HostUpload:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD;

                case EStorageMode.Readback:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK;

                default:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT;
            }
        }

        internal static D3D12_SHADING_RATE ConvertToDx12ShadingRate(in EShadingRate shadingRate)
        {
            switch (shadingRate)
            {
                case EShadingRate.Rate1x1:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_1X1;

                case EShadingRate.Rate1x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_1X2;

                case EShadingRate.Rate2x1:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X1;

                case EShadingRate.Rate2x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X2;

                case EShadingRate.Rate2x4:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X4;

                case EShadingRate.Rate4x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_4X2;

                default:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_4X4;
            }
        }

        internal static D3D12_SHADING_RATE_COMBINER ConvertToDx12ShadingRateCombiner(in EShadingRateCombiner shadingRateCombiner)
        {
            switch (shadingRateCombiner)
            {
                case EShadingRateCombiner.Min:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MIN;

                case EShadingRateCombiner.Max:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX;

                case EShadingRateCombiner.Sum:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_SUM;

                case EShadingRateCombiner.Override:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_OVERRIDE;

                default:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_PASSTHROUGH;
            }
        }

        internal static byte ConvertToDx12WriteChannel(in EColorWriteChannel writeChannel)
        {
            byte result = 0;

            if ((writeChannel & EColorWriteChannel.Red) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_RED;
            if ((writeChannel & EColorWriteChannel.Green) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_GREEN;
            if ((writeChannel & EColorWriteChannel.Blue) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_BLUE;
            if ((writeChannel & EColorWriteChannel.Alpha) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALPHA;

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12BufferStateByFlag(in EBufferUsage bufferFlag)
        {
            /*Dictionary<EBufferUsage, D3D12_RESOURCE_STATES> stateRules = new Dictionary<EBufferUsage, D3D12_RESOURCE_STATES>();
            stateRules.Add(EBufferUsage.CopySrc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            stateRules.Add(EBufferUsage.CopyDst, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            stateRules.Add(EBufferUsage.Index, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(EBufferUsage.Vertex, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(EBufferUsage.Uniform, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(EBufferUsage.Indirect, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(EBufferUsage.StorageResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);*/

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            /*foreach (KeyValuePair<EBufferUsage, D3D12_RESOURCE_STATES> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12TextureStateByFlag(in ETextureUsage textureflag)
        {
            /*Dictionary<ETextureUsage, D3D12_RESOURCE_STATES> stateRules = new Dictionary<ETextureUsage, D3D12_RESOURCE_STATES>();
            stateRules.Add(ETextureUsage.CopySrc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            stateRules.Add(ETextureUsage.CopyDst, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            stateRules.Add(ETextureUsage.DepthAttachment, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE);
            stateRules.Add(ETextureUsage.ColorAttachment, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
            stateRules.Add(ETextureUsage.ShaderResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON);
            stateRules.Add(ETextureUsage.StorageResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);*/

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            /*foreach (KeyValuePair<ETextureUsage, D3D12_RESOURCE_STATES> rule in stateRules)
            {
                if ((textureUsages & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static D3D12_RESOURCE_FLAGS ConvertToDx12BufferFlag(in EBufferUsage bufferflag)
        {
            Dictionary<EBufferUsage, D3D12_RESOURCE_FLAGS> stateRules = new Dictionary<EBufferUsage, D3D12_RESOURCE_FLAGS>();
            stateRules.Add(EBufferUsage.UnorderedAccess, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

            D3D12_RESOURCE_FLAGS result = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
            foreach (KeyValuePair<EBufferUsage, D3D12_RESOURCE_FLAGS> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static D3D12_RESOURCE_FLAGS ConvertToDx12TextureFlag(in ETextureUsage textureflag)
        {
            Dictionary<ETextureUsage, D3D12_RESOURCE_FLAGS> stateRules = new Dictionary<ETextureUsage, D3D12_RESOURCE_FLAGS>();
            stateRules.Add(ETextureUsage.DepthStencil, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
            stateRules.Add(ETextureUsage.RenderTarget, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
            stateRules.Add(ETextureUsage.UnorderedAccess, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

            D3D12_RESOURCE_FLAGS result = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
            foreach (KeyValuePair<ETextureUsage, D3D12_RESOURCE_FLAGS> rule in stateRules)
            {
                if ((textureflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static D3D12_RESOURCE_DIMENSION ConvertToDx12TextureDimension(in ETextureDimension dimension)
        {
            switch (dimension)
            {
                case ETextureDimension.Texture2D:
                case ETextureDimension.Texture2DArray:
                case ETextureDimension.Texture2DMS:
                case ETextureDimension.Texture2DArrayMS:
                case ETextureDimension.TextureCube:
                case ETextureDimension.TextureCubeArray:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D;

                case ETextureDimension.Texture3D:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE3D;

                default:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_UNKNOWN;
            }
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12BufferState(in EBufferState state)
        {
            if (state == EBufferState.Undefine)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            if ((state & EBufferState.CopyDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & EBufferState.CopySrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
            if ((state & EBufferState.IndexBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDEX_BUFFER;
            if ((state & EBufferState.VertexBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
            if ((state & EBufferState.ConstantBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
            if ((state & EBufferState.IndirectArgument) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT;
            if ((state & EBufferState.ShaderResource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & EBufferState.UnorderedAccess) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
            if ((state & EBufferState.AccelStructRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;
            if ((state & EBufferState.AccelStructWrite) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;
            if ((state & EBufferState.AccelStructBuildInput) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & EBufferState.AccelStructBuildBlast) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12ResourceStateFormStorageMode(in EStorageMode storageMode)
        {
            switch (storageMode)
            {
                case EStorageMode.HostUpload:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;

                case EStorageMode.Readback:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;

                default:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            }
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12TextureState(in ETextureState state)
        {
            if (state == ETextureState.Undefine)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            if ((state & ETextureState.Present) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
            //if ((state & ETextureState.GenericRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;
            if ((state & ETextureState.CopyDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & ETextureState.CopySrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
            if ((state & ETextureState.ResolveDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_DEST;
            if ((state & ETextureState.ResolveSrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_SOURCE;
            if ((state & ETextureState.DepthRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_READ;
            if ((state & ETextureState.DepthWrite) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE;
            if ((state & ETextureState.RenderTarget) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
            if ((state & ETextureState.ShaderResource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & ETextureState.UnorderedAccess) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
            if ((state & ETextureState.ShadingRateSurface) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_SHADING_RATE_SOURCE;

            return result;
        }

        internal static D3D_PRIMITIVE_TOPOLOGY ConvertToDx12PrimitiveTopology(in EPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case EPrimitiveTopology.PointList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_POINTLIST;

                case EPrimitiveTopology.LineList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINELIST;

                case EPrimitiveTopology.LineStrip:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINESTRIP;

                case EPrimitiveTopology.TriangleList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST;

                case EPrimitiveTopology.TriangleStrip:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP;

                case EPrimitiveTopology.LineListAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINELIST_ADJ;

                case EPrimitiveTopology.LineStripAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINESTRIP_ADJ;

                case EPrimitiveTopology.TriangleListAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST_ADJ;

                case EPrimitiveTopology.TriangleStripAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP_ADJ;

                default:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_UNDEFINED;
            }
        }

        internal static D3D12_PRIMITIVE_TOPOLOGY_TYPE ConvertToDx12PrimitiveTopologyType(in EPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case EPrimitiveTopology.PointList:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_POINT;

                case EPrimitiveTopology.LineList:
                case EPrimitiveTopology.LineStrip:
                case EPrimitiveTopology.LineListAdj:
                case EPrimitiveTopology.LineStripAdj:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_LINE;

                case EPrimitiveTopology.TriangleList:
                case EPrimitiveTopology.TriangleStrip:
                case EPrimitiveTopology.TriangleListAdj:
                case EPrimitiveTopology.TriangleStripAdj:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;

                default:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_UNDEFINED;
            }
        }

        internal static unsafe D3D12_BLEND_DESC CreateDx12BlendState(in RHIBlendStateDescriptor blendStateDescriptor)
        {
            D3D12_BLEND_DESC blendDescription = new D3D12_BLEND_DESC();
            blendDescription.AlphaToCoverageEnable = blendStateDescriptor.AlphaToCoverage;
            blendDescription.IndependentBlendEnable = blendStateDescriptor.IndependentBlend;
            fixed (RHIBlendDescriptor* blendDescriptorPtr = &blendStateDescriptor.BlendDescriptor0)
            {
                for (int i = 0; i < 8; i++)
                {
                    blendDescription.RenderTarget[i].BlendEnable = blendDescriptorPtr[i].BlendEnable;
                    blendDescription.RenderTarget[i].BlendOp = (D3D12_BLEND_OP)blendDescriptorPtr[i].BlendOpColor;
                    blendDescription.RenderTarget[i].SrcBlend = (D3D12_BLEND)blendDescriptorPtr[i].SrcBlendColor;
                    blendDescription.RenderTarget[i].DestBlend = (D3D12_BLEND)blendDescriptorPtr[i].DstBlendColor;
                    blendDescription.RenderTarget[i].BlendOpAlpha = (D3D12_BLEND_OP)blendDescriptorPtr[i].BlendOpAlpha;
                    blendDescription.RenderTarget[i].SrcBlendAlpha = (D3D12_BLEND)blendDescriptorPtr[i].SrcBlendAlpha;
                    blendDescription.RenderTarget[i].DestBlendAlpha = (D3D12_BLEND)blendDescriptorPtr[i].DstBlendAlpha;
                    blendDescription.RenderTarget[i].RenderTargetWriteMask = ConvertToDx12WriteChannel(blendDescriptorPtr[i].ColorWriteChannel);
                }
            }
            return blendDescription;
        }

        internal static D3D12_RASTERIZER_DESC CreateDx12RasterizerState(in RHIRasterizerStateDescriptor description, bool bMultisample)
        {
            D3D12_RASTERIZER_DESC rasterDescription;
            rasterDescription.FillMode = (D3D12_FILL_MODE)description.FillMode;
            rasterDescription.CullMode = (D3D12_CULL_MODE)description.CullMode;
            rasterDescription.ForcedSampleCount = 0;
            rasterDescription.MultisampleEnable = bMultisample;
            rasterDescription.DepthBias = (int)description.DepthBias;
            rasterDescription.DepthBiasClamp = description.DepthBiasClamp;
            rasterDescription.DepthClipEnable = description.DepthClipEnable;
            rasterDescription.SlopeScaledDepthBias = description.SlopeScaledDepthBias;
            rasterDescription.AntialiasedLineEnable = description.AntialiasedLineEnable;
            rasterDescription.FrontCounterClockwise = description.FrontCounterClockwise;
            rasterDescription.ConservativeRaster = description.ConservativeRaster ? D3D12_CONSERVATIVE_RASTERIZATION_MODE.D3D12_CONSERVATIVE_RASTERIZATION_MODE_ON : D3D12_CONSERVATIVE_RASTERIZATION_MODE.D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF;
            return rasterDescription;
        }

        internal static D3D12_COMPARISON_FUNC ConvertToDx12Comparison(in EComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case EComparisonMode.Never:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER;

                case EComparisonMode.Less:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS;

                case EComparisonMode.Equal:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_EQUAL;

                case EComparisonMode.LessEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS_EQUAL;

                case EComparisonMode.Greater:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER;

                case EComparisonMode.NotEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NOT_EQUAL;

                case EComparisonMode.GreaterEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER_EQUAL;

                case EComparisonMode.Always:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS;
            }
            return 0;
        }

        internal static D3D12_DEPTH_STENCIL_DESC CreateDx12DepthStencilState(in RHIDepthStencilStateDescriptor depthStencilStateDescriptor)
        {
            D3D12_DEPTH_STENCIL_DESC depthStencilDescription = new D3D12_DEPTH_STENCIL_DESC
            {
                DepthEnable = depthStencilStateDescriptor.DepthEnable,
                DepthFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.ComparisonMode),
                DepthWriteMask = depthStencilStateDescriptor.DepthWriteMask ? D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ZERO,
                StencilEnable = depthStencilStateDescriptor.StencilEnable,
                StencilReadMask = depthStencilStateDescriptor.StencilReadMask,
                StencilWriteMask = depthStencilStateDescriptor.StencilWriteMask
            };
            D3D12_DEPTH_STENCILOP_DESC frontFaceDescription = new D3D12_DEPTH_STENCILOP_DESC
            {
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.FrontFace.ComparisonMode),
                StencilFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFace.StencilFailOp,
                StencilPassOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFace.StencilPassOp,
                StencilDepthFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFace.StencilDepthFailOp
            };
            depthStencilDescription.FrontFace = frontFaceDescription;

            D3D12_DEPTH_STENCILOP_DESC backFaceDescription = new D3D12_DEPTH_STENCILOP_DESC
            {
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.BackFace.ComparisonMode),
                StencilFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFace.StencilFailOp,
                StencilPassOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFace.StencilPassOp,
                StencilDepthFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFace.StencilDepthFailOp
            };
            depthStencilDescription.BackFace = backFaceDescription;
            return depthStencilDescription;
        }

        internal static DXGI_FORMAT ConvertToDx12SemanticFormat(in ESemanticFormat format)
        {
            switch (format)
            {
                case ESemanticFormat.Byte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SINT;

                case ESemanticFormat.Byte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT;

                case ESemanticFormat.Byte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT;

                case ESemanticFormat.UByte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UINT;

                case ESemanticFormat.UByte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT;

                case ESemanticFormat.UByte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT;

                case ESemanticFormat.ByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SNORM;

                case ESemanticFormat.Byte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM;

                case ESemanticFormat.Byte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM;

                case ESemanticFormat.UByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UNORM;

                case ESemanticFormat.UByte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM;

                case ESemanticFormat.UByte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;

                case ESemanticFormat.Short:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;

                case ESemanticFormat.Short2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT;

                case ESemanticFormat.Short4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT;

                case ESemanticFormat.UShort:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

                case ESemanticFormat.UShort2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT;

                case ESemanticFormat.UShort4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT;

                case ESemanticFormat.ShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SNORM;

                case ESemanticFormat.Short2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM;

                case ESemanticFormat.Short4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM;

                case ESemanticFormat.UShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UNORM;

                case ESemanticFormat.UShort2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM;

                case ESemanticFormat.UShort4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM;

                case ESemanticFormat.Int:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;

                case ESemanticFormat.Int2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT;

                case ESemanticFormat.Int3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_SINT;

                case ESemanticFormat.Int4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT;

                case ESemanticFormat.UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;

                case ESemanticFormat.UInt2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT;

                case ESemanticFormat.UInt3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_UINT;

                case ESemanticFormat.UInt4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT;

                case ESemanticFormat.Half:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT;

                case ESemanticFormat.Half2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT;

                case ESemanticFormat.Half4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT;

                case ESemanticFormat.Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT;

                case ESemanticFormat.Float2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT;

                case ESemanticFormat.Float3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT;

                case ESemanticFormat.Float4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        //convert dxgi format to pixel format
        internal static DXGI_FORMAT ConvertToDx12Format(in EPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case EPixelFormat.R8_UNorm:
                case EPixelFormat.R8_SNorm:
                case EPixelFormat.R8_UInt:
                case EPixelFormat.R8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_TYPELESS;

                case EPixelFormat.R16_UInt:
                case EPixelFormat.R16_SInt:
                case EPixelFormat.R16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS;

                case EPixelFormat.R8G8_UInt:
                case EPixelFormat.R8G8_SInt:
                case EPixelFormat.R8G8_UNorm:
                case EPixelFormat.R8G8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_TYPELESS;

                case EPixelFormat.R32_UInt:
                case EPixelFormat.R32_SInt:
                case EPixelFormat.R32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS;

                case EPixelFormat.R16G16_UInt:
                case EPixelFormat.R16G16_SInt:
                case EPixelFormat.R16G16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_TYPELESS;

                case EPixelFormat.R8G8B8A8_UInt:
                case EPixelFormat.R8G8B8A8_SInt:
                case EPixelFormat.R8G8B8A8_UNorm:
                case EPixelFormat.R8G8B8A8_UNorm_Srgb:
                case EPixelFormat.R8G8B8A8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS;

                case EPixelFormat.B8G8R8A8_UNorm:
                case EPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_TYPELESS;

                case EPixelFormat.R99GB99_E5_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R9G9B9E5_SHAREDEXP;

                case EPixelFormat.R10G10B10A2_UInt:
                case EPixelFormat.R10G10B10A2_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_TYPELESS;

                case EPixelFormat.R11G11B10_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT;

                case EPixelFormat.RG32_UInt:
                case EPixelFormat.RG32_SInt:
                case EPixelFormat.RG32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_TYPELESS;

                case EPixelFormat.R16G16B16A16_UInt:
                case EPixelFormat.R16G16B16A16_SInt:
                case EPixelFormat.R16G16B16A16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_TYPELESS;

                case EPixelFormat.R32G32B32A32_UInt:
                case EPixelFormat.R32G32B32A32_SInt:
                case EPixelFormat.R32G32B32A32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_TYPELESS;

                case EPixelFormat.D16_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_D16_UNORM;

                case EPixelFormat.D24_UNorm_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT;

                case EPixelFormat.D32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;

                case EPixelFormat.D32_Float_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;

                case EPixelFormat.RGBA_DXT1_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB;

                case EPixelFormat.RGB_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case EPixelFormat.RGBA_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case EPixelFormat.RGBA_DXT3_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB;

                case EPixelFormat.RGBA_DXT3_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM;

                case EPixelFormat.RGBA_DXT5_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB;

                case EPixelFormat.RGBA_DXT5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM;

                case EPixelFormat.R_BC4_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM;

                case EPixelFormat.R_BC4_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM;

                case EPixelFormat.RG_BC5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM;

                case EPixelFormat.RG_BC5_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM;

                case EPixelFormat.RGB_BC6H_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16;

                case EPixelFormat.RGB_BC6H_SFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16;

                case EPixelFormat.RGBA_BC7_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB;

                case EPixelFormat.RGBA_BC7_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM;

                case EPixelFormat.RGBA_ASTC4X4_SRGB:
                case EPixelFormat.RGBA_ASTC4X4_UNorm:
                case EPixelFormat.RGBA_ASTC4X4_UFloat:
                case EPixelFormat.RGBA_ASTC5X5_SRGB:
                case EPixelFormat.RGBA_ASTC5X5_UNorm:
                case EPixelFormat.RGBA_ASTC5X5_UFloat:
                case EPixelFormat.RGBA_ASTC6X6_SRGB:
                case EPixelFormat.RGBA_ASTC6X6_UNorm:
                case EPixelFormat.RGBA_ASTC6X6_UFloat:
                case EPixelFormat.RGBA_ASTC8X8_SRGB:
                case EPixelFormat.RGBA_ASTC8X8_UNorm:
                case EPixelFormat.RGBA_ASTC8X8_UFloat:
                case EPixelFormat.RGBA_ASTC10X10_SRGB:
                case EPixelFormat.RGBA_ASTC10X10_UNorm:
                case EPixelFormat.RGBA_ASTC10X10_UFloat:
                case EPixelFormat.RGBA_ASTC12X12_SRGB:
                case EPixelFormat.RGBA_ASTC12X12_UNorm:
                case EPixelFormat.RGBA_ASTC12X12_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

                case EPixelFormat.YUV2:
                    return DXGI_FORMAT.DXGI_FORMAT_YUY2;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        internal static DXGI_FORMAT ConvertToDx12ViewFormat(in EPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case EPixelFormat.R8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UNORM;

                case EPixelFormat.R8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SNORM;

                case EPixelFormat.R8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UINT;

                case EPixelFormat.R8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SINT;

                case EPixelFormat.R16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

                case EPixelFormat.R16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;

                case EPixelFormat.R16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT;

                case EPixelFormat.R8G8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM;

                case EPixelFormat.R8G8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM;

                case EPixelFormat.R8G8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT;

                case EPixelFormat.R8G8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT;

                case EPixelFormat.R32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;

                case EPixelFormat.R32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;

                case EPixelFormat.R32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT;

                case EPixelFormat.R16G16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT;

                case EPixelFormat.R16G16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT;

                case EPixelFormat.R16G16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT;

                case EPixelFormat.R8G8B8A8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;

                case EPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;

                case EPixelFormat.R8G8B8A8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM;

                case EPixelFormat.R8G8B8A8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT;

                case EPixelFormat.R8G8B8A8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT;

                case EPixelFormat.B8G8R8A8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

                case EPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;

                case EPixelFormat.R99GB99_E5_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R9G9B9E5_SHAREDEXP;

                case EPixelFormat.R10G10B10A2_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UINT;

                case EPixelFormat.R10G10B10A2_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM;

                case EPixelFormat.R11G11B10_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT;

                case EPixelFormat.RG32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT;

                case EPixelFormat.RG32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT;

                case EPixelFormat.RG32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT;

                case EPixelFormat.R16G16B16A16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT;

                case EPixelFormat.R16G16B16A16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT;

                case EPixelFormat.R16G16B16A16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT;

                case EPixelFormat.R32G32B32A32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT;

                case EPixelFormat.R32G32B32A32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT;

                case EPixelFormat.R32G32B32A32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT;

                case EPixelFormat.D16_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_D16_UNORM;

                case EPixelFormat.D24_UNorm_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT;

                case EPixelFormat.D32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;

                case EPixelFormat.D32_Float_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;

                case EPixelFormat.RGBA_DXT1_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB;

                case EPixelFormat.RGB_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case EPixelFormat.RGBA_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case EPixelFormat.RGBA_DXT3_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB;

                case EPixelFormat.RGBA_DXT3_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM;

                case EPixelFormat.RGBA_DXT5_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB;

                case EPixelFormat.RGBA_DXT5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM;

                case EPixelFormat.R_BC4_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM;

                case EPixelFormat.R_BC4_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM;

                case EPixelFormat.RG_BC5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM;

                case EPixelFormat.RG_BC5_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM;

                case EPixelFormat.RGB_BC6H_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16;

                case EPixelFormat.RGB_BC6H_SFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16;

                case EPixelFormat.RGBA_BC7_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB;

                case EPixelFormat.RGBA_BC7_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM;

                case EPixelFormat.RGBA_ASTC4X4_SRGB:
                case EPixelFormat.RGBA_ASTC4X4_UNorm:
                case EPixelFormat.RGBA_ASTC4X4_UFloat:
                case EPixelFormat.RGBA_ASTC5X5_SRGB:
                case EPixelFormat.RGBA_ASTC5X5_UNorm:
                case EPixelFormat.RGBA_ASTC5X5_UFloat:
                case EPixelFormat.RGBA_ASTC6X6_SRGB:
                case EPixelFormat.RGBA_ASTC6X6_UNorm:
                case EPixelFormat.RGBA_ASTC6X6_UFloat:
                case EPixelFormat.RGBA_ASTC8X8_SRGB:
                case EPixelFormat.RGBA_ASTC8X8_UNorm:
                case EPixelFormat.RGBA_ASTC8X8_UFloat:
                case EPixelFormat.RGBA_ASTC10X10_SRGB:
                case EPixelFormat.RGBA_ASTC10X10_UNorm:
                case EPixelFormat.RGBA_ASTC10X10_UFloat:
                case EPixelFormat.RGBA_ASTC12X12_SRGB:
                case EPixelFormat.RGBA_ASTC12X12_UNorm:
                case EPixelFormat.RGBA_ASTC12X12_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

                case EPixelFormat.YUV2:
                    return DXGI_FORMAT.DXGI_FORMAT_YUY2;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        internal static DXGI_FORMAT ConvertToDx12IndexFormat(in EIndexFormat format)
        {
            return (format == EIndexFormat.UInt16) ? DXGI_FORMAT.DXGI_FORMAT_R16_UINT : ((format != EIndexFormat.UInt32) ? DXGI_FORMAT.DXGI_FORMAT_UNKNOWN : DXGI_FORMAT.DXGI_FORMAT_R32_UINT);
        }

        internal static DXGI_SAMPLE_DESC ConvertToDx12SampleCount(in ESampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case ESampleCount.None:
                    return new DXGI_SAMPLE_DESC(1, 0);

                case ESampleCount.Count2:
                    return new DXGI_SAMPLE_DESC(2, 0);

                case ESampleCount.Count4:
                    return new DXGI_SAMPLE_DESC(4, 0);

                case ESampleCount.Count8:
                    return new DXGI_SAMPLE_DESC(8, 0);
            }
            return new DXGI_SAMPLE_DESC(0, 0);
        }
        
        internal static D3D12_DSV_FLAGS GetDx12DSVFlag(in bool bDepthReadOnly, in bool bStencilReadOnly)
        {
            D3D12_DSV_FLAGS outFlag = D3D12_DSV_FLAGS.D3D12_DSV_FLAG_NONE;

            if (bDepthReadOnly)
            {
                outFlag |= D3D12_DSV_FLAGS.D3D12_DSV_FLAG_READ_ONLY_DEPTH;
            }

            if (bStencilReadOnly)
            {
                outFlag |= D3D12_DSV_FLAGS.D3D12_DSV_FLAG_READ_ONLY_STENCIL;
            }

            return outFlag;
        }

        internal static D3D12_DSV_DIMENSION ConvertToDx12TextureDSVDimension(in ETextureDimension dimension)
        {
            switch (dimension)
            {
                case ETextureDimension.Texture2DMS:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DMS;

                case ETextureDimension.Texture2DArray:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.Texture2DArrayMS:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DMSARRAY;
            }
            return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_RTV_DIMENSION ConvertToDx12TextureRTVDimension(in ETextureDimension dimension)
        {
            switch (dimension)
            {
                case ETextureDimension.Texture2DMS:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DMS;

                case ETextureDimension.Texture2DArray:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.Texture2DArrayMS:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DMSARRAY;

                case ETextureDimension.Texture3D:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE3D;
            }
            return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_SRV_DIMENSION ConvertToDx12TextureSRVDimension(in ETextureDimension dimension)
        {
            switch (dimension)
            {
                case ETextureDimension.Texture2DMS:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DMS;

                case ETextureDimension.Texture2DArray:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.Texture2DArrayMS:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DMSARRAY;

                case ETextureDimension.TextureCube:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURECUBE;

                case ETextureDimension.TextureCubeArray:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURECUBEARRAY;

                case ETextureDimension.Texture3D:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D;
            }
            return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_UAV_DIMENSION ConvertToDx12TextureUAVDimension(in ETextureDimension dimension)
        {
            switch (dimension)
            {
                case ETextureDimension.Texture2DMS:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DMS;

                case ETextureDimension.Texture2DArray:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.Texture2DArrayMS:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DMSARRAY;

                case ETextureDimension.TextureCube:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.TextureCubeArray:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ETextureDimension.Texture3D:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE3D;
            }
            return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D;
        }

        internal static byte[] ConvertToDx12SemanticNameByte(this ESemanticType type)
        {
            string semanticName = string.Empty;

            switch (type)
            {
                case ESemanticType.Color:
                    semanticName = "COLOR";
                    break;

                case ESemanticType.Position:
                    semanticName = "POSITION";
                    break;

                case ESemanticType.TexCoord:
                    semanticName = "TEXCOORD";
                    break;

                case ESemanticType.Normal:
                    semanticName = "NORMAL";
                    break;

                case ESemanticType.Tangent:
                    semanticName = "TANGENT";
                    break;

                case ESemanticType.Binormal:
                    semanticName = "BINORMAL";
                    break;

                case ESemanticType.BlendIndices:
                    semanticName = "BLENDINDICES";
                    break;

                case ESemanticType.BlendWeights:
                    semanticName = "BLENDWEIGHTS";
                    break;
            }

            return Encoding.ASCII.GetBytes(semanticName);
        }

        internal static D3D12_INPUT_CLASSIFICATION ConvertToDx12InputSlotClass(this EVertexStepMode stepMode)
        {
            return ((stepMode == EVertexStepMode.PerVertex) || (stepMode != EVertexStepMode.PerInstance)) ? D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA : D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA;
        }

        internal static int GetDx12VertexLayoutCount(in Span<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            int num = 0;
            for (int i = 0; i < vertexLayouts.Length; ++i)
            {
                num += vertexLayouts[i].VertexElements.Length;
            }

            return num;
        }

        internal static void ConvertToDx12VertexLayout(in Span<RHIVertexLayoutDescriptor> vertexLayouts, in Span<D3D12_INPUT_ELEMENT_DESC> inputElementsView)
        {
            int slot = 0;
            int index = 0;

            while (slot < vertexLayouts.Length)
            {
                ref RHIVertexLayoutDescriptor vertexLayout = ref vertexLayouts[slot];
                Span<RHIVertexElementDescriptor> vertexElements = vertexLayout.VertexElements.Span;

                int num6 = 0;

                while (true)
                {
                    if (num6 >= vertexElements.Length)
                    {
                        slot++;
                        break;
                    }
                    ref RHIVertexElementDescriptor vertexElement = ref vertexElements[num6];
                    byte[] semanticByte = ConvertToDx12SemanticNameByte(vertexElement.Type);
                    ref D3D12_INPUT_ELEMENT_DESC element = ref inputElementsView[index];
                    element.Format = ConvertToDx12SemanticFormat(vertexElement.Format);
                    element.InputSlot = (uint)slot;
                    element.SemanticName = (sbyte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(new ReadOnlySpan<byte>(semanticByte)));
                    element.SemanticIndex = vertexElement.Slot;
                    element.InputSlotClass = ConvertToDx12InputSlotClass(vertexLayout.StepMode);
                    element.AlignedByteOffset = vertexElement.Offset;
                    element.InstanceDataStepRate = vertexLayout.StepRate;

                    ++num6;
                    ++index;
                }
            }
        }

        internal static D3D12_DESCRIPTOR_RANGE_TYPE ConvertToDx12BindType(in EBindType bindType)
        {
            switch (bindType)
            {
                case EBindType.Buffer:
                case EBindType.Texture:
                case EBindType.AccelStruct:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV;

                case EBindType.Sampler:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SAMPLER;

                case EBindType.UniformBuffer:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_CBV;

                case EBindType.StorageBuffer:
                case EBindType.StorageTexture:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_UAV;

                default:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            }
        }

        internal static uint GetDx12BindKey(in EBindType bindType)
        {
            switch (bindType)
            {
                case EBindType.Buffer:
                case EBindType.Texture:
                    return 64;

                case EBindType.Sampler:
                    return 128;

                case EBindType.UniformBuffer:
                    return 256;

                case EBindType.StorageBuffer:
                case EBindType.StorageTexture:
                    return 512;

                default:
                    return 64;
            }
        }

        internal static D3D12_DESCRIPTOR_RANGE_FLAGS GetDx12DescriptorRangeFalag(in EBindType bindType)
        {
            switch (bindType)
            {
                case EBindType.Buffer:
                case EBindType.Texture:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;

                case EBindType.Sampler:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE;

                case EBindType.UniformBuffer:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_VOLATILE;

                case EBindType.StorageBuffer:
                case EBindType.StorageTexture:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_VOLATILE;

                default:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE;
            }
        }

        internal static D3D12_SHADER_VISIBILITY ConvertToDx12ShaderStage(in EFunctionStage shaderStage)
        {
            switch (shaderStage)
            {
                case EFunctionStage.Task:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_AMPLIFICATION;

                case EFunctionStage.Mesh:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_MESH;

                case EFunctionStage.Vertex:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_VERTEX;

                case EFunctionStage.Fragment:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_PIXEL;

                default:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_ALL;
            }
        }

        internal static D3D12_CLEAR_FLAGS GetDx12ClearFlagByDSA(in RHIDepthStencilAttachmentDescriptor depthStencilAttachment)
        {
            D3D12_CLEAR_FLAGS result = new D3D12_CLEAR_FLAGS();

            if (depthStencilAttachment.DepthLoadOp == ELoadOp.Clear)
            {
                result |= D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH;
            }

            if (depthStencilAttachment.StencilLoadOp == ELoadOp.Clear)
            {
                result |= D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_STENCIL;
            }
            return result;
        }

        internal static D3D12_HIT_GROUP_TYPE ConverteToDx12HitGroupType(in EHitGroupType type)
        {
            switch (type)
            {
                case EHitGroupType.Procedural:
                    return D3D12_HIT_GROUP_TYPE.D3D12_HIT_GROUP_TYPE_PROCEDURAL_PRIMITIVE;

                default:
                    return D3D12_HIT_GROUP_TYPE.D3D12_HIT_GROUP_TYPE_TRIANGLES;
            }
        }

        internal static bool IsIndexBuffer(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.IndexBuffer) == EBufferUsage.IndexBuffer;
        }

        internal static bool IsVertexBuffer(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.VertexBuffer) == EBufferUsage.VertexBuffer;
        }

        internal static bool IsConstantBuffer(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.UniformBuffer) == EBufferUsage.UniformBuffer;
        }

        internal static bool IsAccelStruct(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.AccelStruct) == EBufferUsage.AccelStruct;
        }

        internal static bool IsShaderResourceBuffer(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.ShaderResource) == EBufferUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessBuffer(in EBufferUsage bufferflag)
        {
            return (bufferflag & EBufferUsage.UnorderedAccess) == EBufferUsage.UnorderedAccess;
        }

        internal static bool IsDepthStencilTexture(in ETextureUsage textureFlag)
        {
            return (textureFlag & ETextureUsage.DepthStencil) == ETextureUsage.DepthStencil;
        }

        internal static bool IsRenderTargetTexture(in ETextureUsage textureFlag)
        {
            return (textureFlag & ETextureUsage.RenderTarget) == ETextureUsage.RenderTarget;
        }

        internal static bool IsShaderResourceTexture(in ETextureUsage textureFlag)
        {
            return (textureFlag & ETextureUsage.ShaderResource) == ETextureUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessTexture(in ETextureUsage textureFlag)
        {
            return (textureFlag & ETextureUsage.UnorderedAccess) == ETextureUsage.UnorderedAccess;
        }

        internal static void FillTexture2DSRV(ref D3D12_TEX2D_SRV srv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2D) == ETextureDimension.Texture2D))
            {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DArraySRV(ref D3D12_TEX2D_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2DArray) == ETextureDimension.Texture2DArray))
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.FirstArraySlice = descriptor.BaseSliceLevel;
            srv.ArraySize = descriptor.SliceCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeSRV(ref D3D12_TEXCUBE_SRV srv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.TextureCube) == ETextureDimension.TextureCube))
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeArraySRV(ref D3D12_TEXCUBE_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.TextureCubeArray) == ETextureDimension.TextureCubeArray))
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.NumCubes = descriptor.SliceCount;
            srv.First2DArrayFace = descriptor.BaseSliceLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture3DSRV(ref D3D12_TEX3D_SRV srv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture3D) == ETextureDimension.Texture3D))
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DUAV(ref D3D12_TEX2D_UAV uav, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2D) == ETextureDimension.Texture2D))
            {
                return;
            }
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayUAV(ref D3D12_TEX2D_ARRAY_UAV uav, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2DArray) == ETextureDimension.Texture2DArray))
            {
                return;
            }
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstArraySlice = descriptor.BaseSliceLevel;
            uav.ArraySize = descriptor.SliceCount;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture3DUAV(ref D3D12_TEX3D_UAV uav, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture3D) == ETextureDimension.Texture3D))
            {
                return;
            }
            uav.WSize = descriptor.SliceCount;
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstWSlice = descriptor.BaseSliceLevel;
        }

        internal static void FillTexture2DRTV(ref D3D12_TEX2D_RTV rtv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2D) == ETextureDimension.Texture2D))
            {
                return;
            }
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayRTV(ref D3D12_TEX2D_ARRAY_RTV rtv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture2DArray) == ETextureDimension.Texture2DArray))
            {
                return;
            }
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.FirstArraySlice = descriptor.BaseSliceLevel;
            rtv.ArraySize = descriptor.SliceCount;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture3DRTV(ref D3D12_TEX3D_RTV rtv, in RHITextureViewDescriptor descriptor, in ETextureDimension dimension)
        {
            if (!((dimension & ETextureDimension.Texture3D) == ETextureDimension.Texture3D))
            {
                return;
            }
            rtv.WSize = descriptor.SliceCount;
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.FirstWSlice = descriptor.BaseSliceLevel;
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
