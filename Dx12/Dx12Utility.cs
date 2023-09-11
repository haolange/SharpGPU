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
using Silk.NET.Core.Native;

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
            HRESULT hResult = device->CreateDescriptorHeap(&descriptorInfo, __uuidof<ID3D12DescriptorHeap>(), (void**)&descriptorHeap);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
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

        internal static D3D12_QUERY_TYPE ConvertToDx12QueryType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION;

                case ERHIQueryType.TimestampTransfer:
                case ERHIQueryType.TimestampGenerice:
                    return D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP;

                default:
                    return 0;
            }
        }

        internal static D3D12_QUERY_HEAP_TYPE ConvertToDx12QueryHeapType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_OCCLUSION;

                case ERHIQueryType.TimestampTransfer:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_COPY_QUEUE_TIMESTAMP;

                case ERHIQueryType.TimestampGenerice:
                    return D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP;

                default:
                    return 0;
            }
        }

        internal static D3D12_COMMAND_LIST_TYPE ConvertToDx12QueueType(in ERHIPipeline pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipeline.Compute:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COMPUTE;

                case ERHIPipeline.Graphics:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT;

                default:
                    return D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COPY;
            }
        }

        internal static uint ConvertToDx12SyncInterval(in ERHIPresentMode presentMode)
        {
            switch (presentMode)
            {
                case ERHIPresentMode.VSync:
                    return 1;

                case ERHIPresentMode.Immediately:
                    return 0;

                default:
                    return 0;
            }
        }

        internal static DXGI_SWAP_EFFECT ConvertToDx12SwapEffect(in ERHIPresentMode presentMode)
        {
            switch (presentMode)
            {
                case ERHIPresentMode.VSync:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;

                case ERHIPresentMode.Immediately:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD;

                default:
                    return DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD;
            }
        }

        internal static D3D12_FILTER ConvertToDx12Filter(in RHISamplerDescriptor descriptor)
        {
            ERHIFilterMode minFilter = descriptor.MinFilter;
            ERHIFilterMode magFilter = descriptor.MagFilter;
            ERHIFilterMode mipFilter = descriptor.MipFilter;

            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_POINT_MIP_LINEAR; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_MIP_LINEAR; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_MIP_POINT; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_LINEAR_MIP_POINT; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Linear) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR; }
            if (minFilter == ERHIFilterMode.Anisotropic || magFilter == ERHIFilterMode.Anisotropic || mipFilter == ERHIFilterMode.Anisotropic) { return D3D12_FILTER.D3D12_FILTER_ANISOTROPIC; }
            return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT;
        }

        internal static D3D12_TEXTURE_ADDRESS_MODE ConvertToDx12AddressMode(in ERHIAddressMode addressMode)
        {
            switch (addressMode)
            {
                case ERHIAddressMode.MirrorRepeat:
                    return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_MIRROR;

                case ERHIAddressMode.ClampToEdge:
                    return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
            }
            return D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        }

        // convert to dx12 format COMPARISON func
        internal static D3D12_COMPARISON_FUNC ConvertToDx12ComparisonMode(in ERHIComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case ERHIComparisonMode.Less:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS;

                case ERHIComparisonMode.Equal:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_EQUAL;

                case ERHIComparisonMode.LessEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS_EQUAL;

                case ERHIComparisonMode.Greater:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER;

                case ERHIComparisonMode.NotEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NOT_EQUAL;

                case ERHIComparisonMode.GreaterEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER_EQUAL;

                case ERHIComparisonMode.Always:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS;
            }

            return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER;
        }

        internal static D3D12_HEAP_TYPE ConvertToDx12HeapTypeByStorage(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.HostUpload:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD;

                case ERHIStorageMode.Readback:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK;

                default:
                    return D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT;
            }
        }

        internal static D3D12_SHADING_RATE ConvertToDx12ShadingRate(in ERHIShadingRate shadingRate)
        {
            switch (shadingRate)
            {
                case ERHIShadingRate.Rate1x1:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_1X1;

                case ERHIShadingRate.Rate1x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_1X2;

                case ERHIShadingRate.Rate2x1:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X1;

                case ERHIShadingRate.Rate2x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X2;

                case ERHIShadingRate.Rate2x4:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_2X4;

                case ERHIShadingRate.Rate4x2:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_4X2;

                default:
                    return D3D12_SHADING_RATE.D3D12_SHADING_RATE_4X4;
            }
        }

        internal static D3D12_SHADING_RATE_COMBINER ConvertToDx12ShadingRateCombiner(in ERHIShadingRateCombiner shadingRateCombiner)
        {
            switch (shadingRateCombiner)
            {
                case ERHIShadingRateCombiner.Min:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MIN;

                case ERHIShadingRateCombiner.Max:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX;

                case ERHIShadingRateCombiner.Sum:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_SUM;

                case ERHIShadingRateCombiner.Override:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_OVERRIDE;

                default:
                    return D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_PASSTHROUGH;
            }
        }

        internal static byte ConvertToDx12WriteChannel(in ERHIColorWriteChannel writeChannel)
        {
            byte result = 0;

            if ((writeChannel & ERHIColorWriteChannel.Red) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_RED;
            if ((writeChannel & ERHIColorWriteChannel.Green) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_GREEN;
            if ((writeChannel & ERHIColorWriteChannel.Blue) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_BLUE;
            if ((writeChannel & ERHIColorWriteChannel.Alpha) != 0) result |= (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALPHA;

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12BufferStateByFlag(in ERHIBufferUsage bufferFlag)
        {
            /*Dictionary<ERHIBufferUsage, D3D12_RESOURCE_STATES> stateRules = new Dictionary<ERHIBufferUsage, D3D12_RESOURCE_STATES>();
            stateRules.Add(ERHIBufferUsage.CopySrc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            stateRules.Add(ERHIBufferUsage.CopyDst, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            stateRules.Add(ERHIBufferUsage.Index, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(ERHIBufferUsage.Vertex, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(ERHIBufferUsage.Uniform, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(ERHIBufferUsage.Indirect, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
            stateRules.Add(ERHIBufferUsage.StorageResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);*/

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            /*foreach (KeyValuePair<ERHIBufferUsage, D3D12_RESOURCE_STATES> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12TextureStateByFlag(in ERHITextureUsage textureflag)
        {
            /*Dictionary<ERHITextureUsage, D3D12_RESOURCE_STATES> stateRules = new Dictionary<ERHITextureUsage, D3D12_RESOURCE_STATES>();
            stateRules.Add(ERHITextureUsage.CopySrc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            stateRules.Add(ERHITextureUsage.CopyDst, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            stateRules.Add(ERHITextureUsage.DepthAttachment, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE);
            stateRules.Add(ERHITextureUsage.ColorAttachment, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
            stateRules.Add(ERHITextureUsage.ShaderResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON);
            stateRules.Add(ERHITextureUsage.StorageResource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);*/

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            /*foreach (KeyValuePair<ERHITextureUsage, D3D12_RESOURCE_STATES> rule in stateRules)
            {
                if ((textureUsages & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static D3D12_RESOURCE_FLAGS ConvertToDx12BufferFlag(in ERHIBufferUsage bufferflag)
        {
            Dictionary<ERHIBufferUsage, D3D12_RESOURCE_FLAGS> stateRules = new Dictionary<ERHIBufferUsage, D3D12_RESOURCE_FLAGS>();
            stateRules.Add(ERHIBufferUsage.UnorderedAccess, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

            D3D12_RESOURCE_FLAGS result = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
            foreach (KeyValuePair<ERHIBufferUsage, D3D12_RESOURCE_FLAGS> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static D3D12_RESOURCE_FLAGS ConvertToDx12TextureFlag(in ERHITextureUsage textureflag)
        {
            Dictionary<ERHITextureUsage, D3D12_RESOURCE_FLAGS> stateRules = new Dictionary<ERHITextureUsage, D3D12_RESOURCE_FLAGS>();
            stateRules.Add(ERHITextureUsage.DepthStencil, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
            stateRules.Add(ERHITextureUsage.RenderTarget, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
            stateRules.Add(ERHITextureUsage.UnorderedAccess, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

            D3D12_RESOURCE_FLAGS result = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
            foreach (KeyValuePair<ERHITextureUsage, D3D12_RESOURCE_FLAGS> rule in stateRules)
            {
                if ((textureflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static D3D12_RESOURCE_DIMENSION ConvertToDx12TextureDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2D:
                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.Texture2DMS:
                case ERHITextureDimension.Texture2DArrayMS:
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D;

                case ERHITextureDimension.Texture3D:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE3D;

                default:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_UNKNOWN;
            }
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12BufferState(in ERHIBufferState state)
        {
            if (state == ERHIBufferState.Undefine)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            if ((state & ERHIBufferState.CopyDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & ERHIBufferState.CopySrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
            if ((state & ERHIBufferState.IndexBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDEX_BUFFER;
            if ((state & ERHIBufferState.VertexBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
            if ((state & ERHIBufferState.ConstantBuffer) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
            if ((state & ERHIBufferState.IndirectArgument) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT;
            if ((state & ERHIBufferState.ShaderResource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & ERHIBufferState.UnorderedAccess) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
            if ((state & ERHIBufferState.AccelStructRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;
            if ((state & ERHIBufferState.AccelStructWrite) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;
            if ((state & ERHIBufferState.AccelStructBuildInput) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & ERHIBufferState.AccelStructBuildBlast) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE;

            return result;
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12ResourceStateFormStorageMode(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.HostUpload:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;

                case ERHIStorageMode.Readback:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;

                default:
                    return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
            }
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12TextureState(in ERHITextureState state)
        {
            if (state == ERHITextureState.Undefine)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            if ((state & ERHITextureState.Present) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
            //if ((state & ERHITextureState.GenericRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;
            if ((state & ERHITextureState.CopyDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & ERHITextureState.CopySrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
            if ((state & ERHITextureState.ResolveDst) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_DEST;
            if ((state & ERHITextureState.ResolveSrc) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_SOURCE;
            if ((state & ERHITextureState.DepthRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_READ;
            if ((state & ERHITextureState.DepthWrite) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE;
            if ((state & ERHITextureState.RenderTarget) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
            if ((state & ERHITextureState.ShaderResource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
            if ((state & ERHITextureState.UnorderedAccess) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
            if ((state & ERHITextureState.ShadingRateSurface) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_SHADING_RATE_SOURCE;

            return result;
        }

        internal static D3D_PRIMITIVE_TOPOLOGY ConvertToDx12PrimitiveTopology(in ERHIPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_POINTLIST;

                case ERHIPrimitiveTopology.LineList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINELIST;

                case ERHIPrimitiveTopology.LineStrip:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINESTRIP;

                case ERHIPrimitiveTopology.TriangleList:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST;

                case ERHIPrimitiveTopology.TriangleStrip:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP;

                case ERHIPrimitiveTopology.LineListAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINELIST_ADJ;

                case ERHIPrimitiveTopology.LineStripAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_LINESTRIP_ADJ;

                case ERHIPrimitiveTopology.TriangleListAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST_ADJ;

                case ERHIPrimitiveTopology.TriangleStripAdj:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP_ADJ;

                default:
                    return D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_UNDEFINED;
            }
        }

        internal static D3D12_PRIMITIVE_TOPOLOGY_TYPE ConvertToDx12PrimitiveTopologyType(in ERHIPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_POINT;

                case ERHIPrimitiveTopology.LineList:
                case ERHIPrimitiveTopology.LineStrip:
                case ERHIPrimitiveTopology.LineListAdj:
                case ERHIPrimitiveTopology.LineStripAdj:
                    return D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_LINE;

                case ERHIPrimitiveTopology.TriangleList:
                case ERHIPrimitiveTopology.TriangleStrip:
                case ERHIPrimitiveTopology.TriangleListAdj:
                case ERHIPrimitiveTopology.TriangleStripAdj:
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

        internal static D3D12_COMPARISON_FUNC ConvertToDx12Comparison(in ERHIComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case ERHIComparisonMode.Never:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER;

                case ERHIComparisonMode.Less:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS;

                case ERHIComparisonMode.Equal:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_EQUAL;

                case ERHIComparisonMode.LessEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS_EQUAL;

                case ERHIComparisonMode.Greater:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER;

                case ERHIComparisonMode.NotEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NOT_EQUAL;

                case ERHIComparisonMode.GreaterEqual:
                    return D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER_EQUAL;

                case ERHIComparisonMode.Always:
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

        internal static DXGI_FORMAT ConvertToDx12SemanticFormat(in ERHISemanticFormat format)
        {
            switch (format)
            {
                case ERHISemanticFormat.Byte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SINT;

                case ERHISemanticFormat.Byte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT;

                case ERHISemanticFormat.Byte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT;

                case ERHISemanticFormat.UByte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UINT;

                case ERHISemanticFormat.UByte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT;

                case ERHISemanticFormat.UByte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT;

                case ERHISemanticFormat.ByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SNORM;

                case ERHISemanticFormat.Byte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM;

                case ERHISemanticFormat.Byte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM;

                case ERHISemanticFormat.UByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UNORM;

                case ERHISemanticFormat.UByte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM;

                case ERHISemanticFormat.UByte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;

                case ERHISemanticFormat.Short:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;

                case ERHISemanticFormat.Short2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT;

                case ERHISemanticFormat.Short4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT;

                case ERHISemanticFormat.UShort:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

                case ERHISemanticFormat.UShort2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT;

                case ERHISemanticFormat.UShort4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT;

                case ERHISemanticFormat.ShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SNORM;

                case ERHISemanticFormat.Short2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM;

                case ERHISemanticFormat.Short4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM;

                case ERHISemanticFormat.UShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UNORM;

                case ERHISemanticFormat.UShort2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM;

                case ERHISemanticFormat.UShort4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM;

                case ERHISemanticFormat.Int:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;

                case ERHISemanticFormat.Int2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT;

                case ERHISemanticFormat.Int3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_SINT;

                case ERHISemanticFormat.Int4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT;

                case ERHISemanticFormat.UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;

                case ERHISemanticFormat.UInt2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT;

                case ERHISemanticFormat.UInt3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_UINT;

                case ERHISemanticFormat.UInt4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT;

                case ERHISemanticFormat.Half:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT;

                case ERHISemanticFormat.Half2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT;

                case ERHISemanticFormat.Half4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT;

                case ERHISemanticFormat.Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT;

                case ERHISemanticFormat.Float2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT;

                case ERHISemanticFormat.Float3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT;

                case ERHISemanticFormat.Float4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        //convert dxgi format to pixel format
        internal static DXGI_FORMAT ConvertToDx12Format(in ERHIPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case ERHIPixelFormat.R8_UNorm:
                case ERHIPixelFormat.R8_SNorm:
                case ERHIPixelFormat.R8_UInt:
                case ERHIPixelFormat.R8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_TYPELESS;

                case ERHIPixelFormat.R16_UInt:
                case ERHIPixelFormat.R16_SInt:
                case ERHIPixelFormat.R16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS;

                case ERHIPixelFormat.R8G8_UInt:
                case ERHIPixelFormat.R8G8_SInt:
                case ERHIPixelFormat.R8G8_UNorm:
                case ERHIPixelFormat.R8G8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_TYPELESS;

                case ERHIPixelFormat.R32_UInt:
                case ERHIPixelFormat.R32_SInt:
                case ERHIPixelFormat.R32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS;

                case ERHIPixelFormat.R16G16_UInt:
                case ERHIPixelFormat.R16G16_SInt:
                case ERHIPixelFormat.R16G16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_TYPELESS;

                case ERHIPixelFormat.R8G8B8A8_UInt:
                case ERHIPixelFormat.R8G8B8A8_SInt:
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS;

                case ERHIPixelFormat.B8G8R8A8_UNorm:
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_TYPELESS;

                case ERHIPixelFormat.R99GB99_E5_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R9G9B9E5_SHAREDEXP;

                case ERHIPixelFormat.R10G10B10A2_UInt:
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_TYPELESS;

                case ERHIPixelFormat.R11G11B10_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT;

                case ERHIPixelFormat.RG32_UInt:
                case ERHIPixelFormat.RG32_SInt:
                case ERHIPixelFormat.RG32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_TYPELESS;

                case ERHIPixelFormat.R16G16B16A16_UInt:
                case ERHIPixelFormat.R16G16B16A16_SInt:
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_TYPELESS;

                case ERHIPixelFormat.R32G32B32A32_UInt:
                case ERHIPixelFormat.R32G32B32A32_SInt:
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_TYPELESS;

                case ERHIPixelFormat.D16_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_D16_UNORM;

                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT;

                case ERHIPixelFormat.D32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;

                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;

                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB;

                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM;

                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM;

                case ERHIPixelFormat.R_BC4_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM;

                case ERHIPixelFormat.R_BC4_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM;

                case ERHIPixelFormat.RG_BC5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM;

                case ERHIPixelFormat.RG_BC5_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM;

                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16;

                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16;

                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM;

                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

                case ERHIPixelFormat.YUV2:
                    return DXGI_FORMAT.DXGI_FORMAT_YUY2;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        internal static DXGI_FORMAT ConvertToDx12ViewFormat(in ERHIPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case ERHIPixelFormat.R8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UNORM;

                case ERHIPixelFormat.R8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SNORM;

                case ERHIPixelFormat.R8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UINT;

                case ERHIPixelFormat.R8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SINT;

                case ERHIPixelFormat.R16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

                case ERHIPixelFormat.R16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;

                case ERHIPixelFormat.R16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT;

                case ERHIPixelFormat.R8G8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM;

                case ERHIPixelFormat.R8G8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM;

                case ERHIPixelFormat.R8G8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT;

                case ERHIPixelFormat.R8G8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT;

                case ERHIPixelFormat.R32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;

                case ERHIPixelFormat.R32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;

                case ERHIPixelFormat.R32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT;

                case ERHIPixelFormat.R16G16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT;

                case ERHIPixelFormat.R16G16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT;

                case ERHIPixelFormat.R16G16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT;

                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;

                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;

                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM;

                case ERHIPixelFormat.R8G8B8A8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT;

                case ERHIPixelFormat.R8G8B8A8_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT;

                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;

                case ERHIPixelFormat.R99GB99_E5_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R9G9B9E5_SHAREDEXP;

                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UINT;

                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM;

                case ERHIPixelFormat.R11G11B10_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT;

                case ERHIPixelFormat.RG32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT;

                case ERHIPixelFormat.RG32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT;

                case ERHIPixelFormat.RG32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT;

                case ERHIPixelFormat.R16G16B16A16_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT;

                case ERHIPixelFormat.R16G16B16A16_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT;

                case ERHIPixelFormat.R16G16B16A16_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT;

                case ERHIPixelFormat.R32G32B32A32_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT;

                case ERHIPixelFormat.R32G32B32A32_SInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT;

                case ERHIPixelFormat.R32G32B32A32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT;

                case ERHIPixelFormat.D16_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_D16_UNORM;

                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT;

                case ERHIPixelFormat.D32_Float:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;

                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;

                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB;

                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM;

                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM;

                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM;

                case ERHIPixelFormat.R_BC4_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM;

                case ERHIPixelFormat.R_BC4_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM;

                case ERHIPixelFormat.RG_BC5_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM;

                case ERHIPixelFormat.RG_BC5_SNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM;

                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16;

                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16;

                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB;

                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM;

                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

                case ERHIPixelFormat.YUV2:
                    return DXGI_FORMAT.DXGI_FORMAT_YUY2;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        internal static DXGI_FORMAT ConvertToDx12IndexFormat(in ERHIIndexFormat format)
        {
            return (format == ERHIIndexFormat.UInt16) ? DXGI_FORMAT.DXGI_FORMAT_R16_UINT : ((format != ERHIIndexFormat.UInt32) ? DXGI_FORMAT.DXGI_FORMAT_UNKNOWN : DXGI_FORMAT.DXGI_FORMAT_R32_UINT);
        }

        internal static DXGI_SAMPLE_DESC ConvertToDx12SampleCount(in ERHISampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case ERHISampleCount.None:
                    return new DXGI_SAMPLE_DESC(1, 0);

                case ERHISampleCount.Count2:
                    return new DXGI_SAMPLE_DESC(2, 0);

                case ERHISampleCount.Count4:
                    return new DXGI_SAMPLE_DESC(4, 0);

                case ERHISampleCount.Count8:
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

        internal static D3D12_DSV_DIMENSION ConvertToDx12TextureDSVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DMS;

                case ERHITextureDimension.Texture2DArray:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.Texture2DArrayMS:
                    return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DMSARRAY;
            }
            return D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_RTV_DIMENSION ConvertToDx12TextureRTVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DMS;

                case ERHITextureDimension.Texture2DArray:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.Texture2DArrayMS:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DMSARRAY;

                case ERHITextureDimension.Texture3D:
                    return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE3D;
            }
            return D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_SRV_DIMENSION ConvertToDx12TextureSRVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DMS;

                case ERHITextureDimension.Texture2DArray:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.Texture2DArrayMS:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DMSARRAY;

                case ERHITextureDimension.TextureCube:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURECUBE;

                case ERHITextureDimension.TextureCubeArray:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURECUBEARRAY;

                case ERHITextureDimension.Texture3D:
                    return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D;
            }
            return D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D;
        }

        internal static D3D12_UAV_DIMENSION ConvertToDx12TextureUAVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DMS;

                case ERHITextureDimension.Texture2DArray:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.Texture2DArrayMS:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DMSARRAY;

                case ERHITextureDimension.TextureCube:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.TextureCubeArray:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2DARRAY;

                case ERHITextureDimension.Texture3D:
                    return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE3D;
            }
            return D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D;
        }

        internal static byte[] ConvertToDx12SemanticNameByte(this ERHISemanticType type)
        {
            string semanticName = string.Empty;

            switch (type)
            {
                case ERHISemanticType.Color:
                    semanticName = "COLOR";
                    break;

                case ERHISemanticType.Position:
                    semanticName = "POSITION";
                    break;

                case ERHISemanticType.TexCoord:
                    semanticName = "TEXCOORD";
                    break;

                case ERHISemanticType.Normal:
                    semanticName = "NORMAL";
                    break;

                case ERHISemanticType.Tangent:
                    semanticName = "TANGENT";
                    break;

                case ERHISemanticType.Binormal:
                    semanticName = "BINORMAL";
                    break;

                case ERHISemanticType.BlendIndices:
                    semanticName = "BLENDINDICES";
                    break;

                case ERHISemanticType.BlendWeights:
                    semanticName = "BLENDWEIGHTS";
                    break;
            }

            return Encoding.ASCII.GetBytes(semanticName);
        }

        internal static D3D12_INPUT_CLASSIFICATION ConvertToDx12InputSlotClass(this ERHIVertexStepMode stepMode)
        {
            return ((stepMode == ERHIVertexStepMode.PerVertex) || (stepMode != ERHIVertexStepMode.PerInstance)) ? D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA : D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA;
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

        internal static D3D12_DESCRIPTOR_RANGE_TYPE ConvertToDx12BindType(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.Texture:
                case ERHIBindType.AccelStruct:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV;

                case ERHIBindType.Sampler:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SAMPLER;

                case ERHIBindType.UniformBuffer:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_CBV;

                case ERHIBindType.StorageBuffer:
                case ERHIBindType.StorageTexture:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_UAV;

                default:
                    return D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            }
        }

        internal static uint GetDx12BindKey(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.Texture:
                    return 64;

                case ERHIBindType.Sampler:
                    return 128;

                case ERHIBindType.UniformBuffer:
                    return 256;

                case ERHIBindType.StorageBuffer:
                case ERHIBindType.StorageTexture:
                    return 512;

                default:
                    return 64;
            }
        }

        internal static D3D12_DESCRIPTOR_RANGE_FLAGS GetDx12DescriptorRangeFalag(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.Texture:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;

                case ERHIBindType.Sampler:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE;

                case ERHIBindType.UniformBuffer:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_VOLATILE;

                case ERHIBindType.StorageBuffer:
                case ERHIBindType.StorageTexture:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE | D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DATA_VOLATILE;

                default:
                    return D3D12_DESCRIPTOR_RANGE_FLAGS.D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE;
            }
        }

        internal static D3D12_SHADER_VISIBILITY ConvertToDx12ShaderStage(in ERHIFunctionStage shaderStage)
        {
            switch (shaderStage)
            {
                case ERHIFunctionStage.Task:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_AMPLIFICATION;

                case ERHIFunctionStage.Mesh:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_MESH;

                case ERHIFunctionStage.Vertex:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_VERTEX;

                case ERHIFunctionStage.Fragment:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_PIXEL;

                default:
                    return D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_ALL;
            }
        }

        internal static D3D12_CLEAR_FLAGS GetDx12ClearFlagByDSA(in RHIDepthStencilAttachmentDescriptor depthStencilAttachment)
        {
            D3D12_CLEAR_FLAGS result = new D3D12_CLEAR_FLAGS();

            if (depthStencilAttachment.DepthLoadOp == ERHILoadAction.Clear)
            {
                result |= D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH;
            }

            if (depthStencilAttachment.StencilLoadOp == ERHILoadAction.Clear)
            {
                result |= D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_STENCIL;
            }
            return result;
        }

        internal static D3D12_HIT_GROUP_TYPE ConverteToDx12HitGroupType(in ERHIHitGroupType type)
        {
            switch (type)
            {
                case ERHIHitGroupType.Procedural:
                    return D3D12_HIT_GROUP_TYPE.D3D12_HIT_GROUP_TYPE_PROCEDURAL_PRIMITIVE;

                default:
                    return D3D12_HIT_GROUP_TYPE.D3D12_HIT_GROUP_TYPE_TRIANGLES;
            }
        }

        internal static bool IsIndexBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.IndexBuffer) == ERHIBufferUsage.IndexBuffer;
        }

        internal static bool IsVertexBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.VertexBuffer) == ERHIBufferUsage.VertexBuffer;
        }

        internal static bool IsConstantBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.UniformBuffer) == ERHIBufferUsage.UniformBuffer;
        }

        internal static bool IsAccelStruct(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct;
        }

        internal static bool IsShaderResourceBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.ShaderResource) == ERHIBufferUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.UnorderedAccess) == ERHIBufferUsage.UnorderedAccess;
        }

        internal static bool IsDepthStencilTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.DepthStencil) == ERHITextureUsage.DepthStencil;
        }

        internal static bool IsRenderTargetTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.RenderTarget) == ERHITextureUsage.RenderTarget;
        }

        internal static bool IsShaderResourceTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.ShaderResource) == ERHITextureUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.UnorderedAccess) == ERHITextureUsage.UnorderedAccess;
        }

        internal static void FillTexture2DSRV(ref D3D12_TEX2D_SRV srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2D) == ERHITextureDimension.Texture2D))
            {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DArraySRV(ref D3D12_TEX2D_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2DArray) == ERHITextureDimension.Texture2DArray))
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.FirstArraySlice = descriptor.BaseArraySlice;
            srv.ArraySize = descriptor.ArrayCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeSRV(ref D3D12_TEXCUBE_SRV srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.TextureCube) == ERHITextureDimension.TextureCube))
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeArraySRV(ref D3D12_TEXCUBE_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.TextureCubeArray) == ERHITextureDimension.TextureCubeArray))
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.NumCubes = descriptor.ArrayCount;
            srv.First2DArrayFace = descriptor.BaseArraySlice;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture3DSRV(ref D3D12_TEX3D_SRV srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture3D) == ERHITextureDimension.Texture3D))
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DUAV(ref D3D12_TEX2D_UAV uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2D) == ERHITextureDimension.Texture2D))
            {
                return;
            }
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayUAV(ref D3D12_TEX2D_ARRAY_UAV uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2DArray) == ERHITextureDimension.Texture2DArray))
            {
                return;
            }
            uav.ArraySize = descriptor.ArrayCount;
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstArraySlice = descriptor.BaseArraySlice;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture3DUAV(ref D3D12_TEX3D_UAV uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture3D) == ERHITextureDimension.Texture3D))
            {
                return;
            }
            uav.WSize = descriptor.ArrayCount;
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstWSlice = descriptor.BaseArraySlice;
        }

        internal static void FillTexture2DRTV(ref D3D12_TEX2D_RTV rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2D) == ERHITextureDimension.Texture2D))
            {
                return;
            }
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayRTV(ref D3D12_TEX2D_ARRAY_RTV rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture2DArray) == ERHITextureDimension.Texture2DArray))
            {
                return;
            }
            rtv.ArraySize = descriptor.ArrayCount;
            rtv.MipSlice = descriptor.MipCount;
            rtv.FirstArraySlice = descriptor.BaseArraySlice;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture3DRTV(ref D3D12_TEX3D_RTV rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (!((dimension & ERHITextureDimension.Texture3D) == ERHITextureDimension.Texture3D))
            {
                return;
            }
            rtv.WSize = descriptor.ArrayCount;
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.FirstWSlice = descriptor.BaseArraySlice;
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
