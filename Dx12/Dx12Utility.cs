using System;
using System.Text;
using NUnit.Framework;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602
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
            return (uint)(presentMode == EPresentMode.VSync ? 1 : 0);
        }

        internal static D3D12_FILTER ConvertToDx12Filter(in RHISamplerDescriptor descriptor)
        {
            EFilterMode minFilter = descriptor.MinFilter;
            EFilterMode magFilter = descriptor.MagFilter;
            EFilterMode mipFilter = descriptor.MipFilter;

            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Linear)  { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_POINT_MIP_LINEAR; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT; }
            if (minFilter == EFilterMode.Point && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Linear)  { return D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_MIP_LINEAR; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_MIP_POINT; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Point && mipFilter == EFilterMode.Linear)  { return D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Point) { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_LINEAR_MIP_POINT; }
            if (minFilter == EFilterMode.Linear && magFilter == EFilterMode.Linear && mipFilter == EFilterMode.Linear)  { return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR; }
            if (minFilter == EFilterMode.Anisotropic || magFilter == EFilterMode.Anisotropic || mipFilter == EFilterMode.Anisotropic) { return D3D12_FILTER.D3D12_FILTER_ANISOTROPIC; }
            return D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT;
        }

        internal static D3D12_HEAP_TYPE ConvertToDx12ResourceFlagByUsage(in EStorageMode resourceUsage)
        {
            D3D12_HEAP_TYPE fallback = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT;
            Dictionary<EStorageMode, D3D12_HEAP_TYPE> heapRules = new Dictionary<EStorageMode, D3D12_HEAP_TYPE>();
            heapRules.Add(EStorageMode.Dynamic, D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD);
            heapRules.Add(EStorageMode.Staging, D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK);

            foreach (KeyValuePair<EStorageMode, D3D12_HEAP_TYPE> rule in heapRules)
            {
                if (resourceUsage == rule.Key)
                {
                    return rule.Value;
                }
            }

            return fallback;
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
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D;

                case ETextureDimension.Texture3D:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE3D;

                default:
                    return D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE1D;
            }
        }

        internal static D3D12_RESOURCE_STATES ConvertToDx12BufferState(in EBufferState state)
        {
            if (state == EBufferState.Common)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON; // also 0

            if ((state & EBufferState.GenericRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;
            if ((state & EBufferState.CopyDest) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & EBufferState.CopySource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
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

        internal static D3D12_RESOURCE_STATES ConvertToDx12TextureState(in ETextureState state)
        {
            if (state == ETextureState.Common)
                return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

            D3D12_RESOURCE_STATES result = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON; // also 0

            if ((state & ETextureState.Present) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
            if ((state & ETextureState.GenericRead) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ;
            if ((state & ETextureState.CopyDest) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST;
            if ((state & ETextureState.CopySource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE;
            if ((state & ETextureState.ResolveDest) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_DEST;
            if ((state & ETextureState.ResolveSource) != 0) result |= D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_SOURCE;
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
                    blendDescription.RenderTarget[i].RenderTargetWriteMask = (byte)blendDescriptorPtr[i].ColorWriteChannel;
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
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.FrontFaceDescriptor.ComparisonMode),
                StencilFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFaceDescriptor.StencilFailOp,
                StencilPassOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFaceDescriptor.StencilPassOp,
                StencilDepthFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.FrontFaceDescriptor.StencilDepthFailOp
            };
            depthStencilDescription.FrontFace = frontFaceDescription;

            D3D12_DEPTH_STENCILOP_DESC backFaceDescription = new D3D12_DEPTH_STENCILOP_DESC
            {
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.BackFaceDescriptor.ComparisonMode),
                StencilFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFaceDescriptor.StencilFailOp,
                StencilPassOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFaceDescriptor.StencilPassOp,
                StencilDepthFailOp = (D3D12_STENCIL_OP)depthStencilStateDescriptor.BackFaceDescriptor.StencilDepthFailOp
            };
            depthStencilDescription.BackFace = backFaceDescription;
            return depthStencilDescription;
        }

        internal static DXGI_FORMAT ConvertToDx12SemanticFormat(in ESemanticFormat format)
        {
            switch (format)
            {
                case ESemanticFormat.UByte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UINT;

                case ESemanticFormat.UByte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT;

                case ESemanticFormat.UByte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT;

                case ESemanticFormat.Byte:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SINT;

                case ESemanticFormat.Byte2:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT;

                case ESemanticFormat.Byte4:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT;

                case ESemanticFormat.UByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_UNORM;

                case ESemanticFormat.UByte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM;

                case ESemanticFormat.UByte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;

                case ESemanticFormat.ByteNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8_SNORM;

                case ESemanticFormat.Byte2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM;

                case ESemanticFormat.Byte4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM;

                case ESemanticFormat.UShort:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

                case ESemanticFormat.UShort2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT;

                case ESemanticFormat.UShort4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT;

                case ESemanticFormat.Short:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SINT;

                case ESemanticFormat.Short2:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT;

                case ESemanticFormat.Short4:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT;

                case ESemanticFormat.UShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_UNORM;

                case ESemanticFormat.UShort2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM;

                case ESemanticFormat.UShort4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM;

                case ESemanticFormat.ShortNormalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16_SNORM;

                case ESemanticFormat.Short2Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM;

                case ESemanticFormat.Short4Normalized:
                    return DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM;

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

                case ESemanticFormat.UInt:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_UINT;

                case ESemanticFormat.UInt2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT;

                case ESemanticFormat.UInt3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_UINT;

                case ESemanticFormat.UInt4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT;

                case ESemanticFormat.Int:
                    return DXGI_FORMAT.DXGI_FORMAT_R32_SINT;

                case ESemanticFormat.Int2:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT;

                case ESemanticFormat.Int3:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32_SINT;

                case ESemanticFormat.Int4:
                    return DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT;
            }
            return DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        }

        internal static DXGI_FORMAT ConvertToDx12Format(in EPixelFormat pixelFormat)
        {
            throw new NotImplementedException();
        }

        internal static DXGI_FORMAT ConvertToDx12ViewFormat(in EPixelFormat pixelFormat)
        {
            throw new NotImplementedException();
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

        internal static byte[] ConvertToDx12SemanticNameByte(this ESemanticType type)
        {
            string semanticName = string.Empty;

            switch (type)
            {
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

                case ESemanticType.Color:
                    semanticName = "COLOR";
                    break;

                case ESemanticType.BlendIndices:
                    semanticName = "BLENDINDICES";
                    break;

                case ESemanticType.BlendWeight:
                    semanticName = "BLENDWEIGHT";
                    break;
            }

            return Encoding.ASCII.GetBytes(semanticName);
        }

        internal static D3D12_INPUT_CLASSIFICATION ConvertToDx12InputSlotClass(this EVertexStepMode stepMode)
        {
            return ((stepMode == EVertexStepMode.PerVertex) || (stepMode != EVertexStepMode.PerInstance)) ? D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA : D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA;
        }

        internal static int GetDx12VertexLayoutCount(in Span<RHIVertexLayoutDescriptor> vertexLayoutDescriptors)
        {
            int num = 0;
            for (int i = 0; i < vertexLayoutDescriptors.Length; ++i)
            {
                num += vertexLayoutDescriptors[i].VertexElementDescriptors.Length;
            }

            return num;
        }

        internal static void ConvertToDx12VertexLayout(in Span<RHIVertexLayoutDescriptor> vertexLayoutDescriptors, in Span<D3D12_INPUT_ELEMENT_DESC> inputElementsView)
        {
            int slot = 0;
            int index = 0;

            while (slot < vertexLayoutDescriptors.Length)
            {
                ref RHIVertexLayoutDescriptor vertexLayoutDescriptor = ref vertexLayoutDescriptors[slot];
                Span<RHIVertexElementDescriptor> vertexElementDescriptors = vertexLayoutDescriptor.VertexElementDescriptors.Span;

                int num6 = 0;

                while (true)
                {
                    if (num6 >= vertexElementDescriptors.Length)
                    {
                        slot++;
                        break;
                    }
                    RHIVertexElementDescriptor vertexElementDescriptor = vertexElementDescriptors[num6];
                    byte[] semanticByte = ConvertToDx12SemanticNameByte(vertexElementDescriptor.Type);
                    ref D3D12_INPUT_ELEMENT_DESC element = ref inputElementsView[index];
                    element.Format = ConvertToDx12SemanticFormat(vertexElementDescriptor.Format);
                    element.InputSlot = (uint)slot;
                    element.SemanticName = (sbyte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(new ReadOnlySpan<byte>(semanticByte)));
                    element.SemanticIndex = vertexElementDescriptor.Index;
                    element.InputSlotClass = ConvertToDx12InputSlotClass(vertexLayoutDescriptor.StepMode);
                    element.AlignedByteOffset = (uint)vertexElementDescriptor.Offset;
                    element.InstanceDataStepRate = (uint)vertexLayoutDescriptor.StepRate;

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

            if (depthStencilAttachment.DepthLoadAction == ELoadAction.Clear) 
            {
                result |= D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH;
            }

            if (depthStencilAttachment.StencilLoadAction == ELoadAction.Clear) 
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

        internal static void FillTexture2DSRV(ref D3D12_TEX2D_SRV srv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2D) == ETextureViewDimension.Texture2D)) {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DArraySRV(ref D3D12_TEX2D_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2DArray) == ETextureViewDimension.Texture2DArray)) {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.FirstArraySlice = (uint)descriptor.BaseArrayLayer;
            srv.ArraySize = (uint)descriptor.ArrayLayerCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeSRV(ref D3D12_TEXCUBE_SRV srv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.TextureCube) == ETextureViewDimension.TextureCube)) {
                return;
            }
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeArraySRV(ref D3D12_TEXCUBE_ARRAY_SRV srv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.TextureCubeArray) == ETextureViewDimension.TextureCubeArray)) {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.NumCubes = (uint)descriptor.ArrayLayerCount;
            srv.First2DArrayFace = (uint)descriptor.BaseArrayLayer;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture3DSRV(ref D3D12_TEX3D_SRV srv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture3D) == ETextureViewDimension.Texture3D)) {
                return;
            }
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DUAV(ref D3D12_TEX2D_UAV uav, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2D) == ETextureViewDimension.Texture2D)) {
                return;
            }
            uav.MipSlice = (uint)descriptor.BaseMipLevel;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayUAV(ref D3D12_TEX2D_ARRAY_UAV uav, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2DArray) == ETextureViewDimension.Texture2DArray)) {
                return;
            }
            uav.MipSlice = (uint)descriptor.BaseMipLevel;
            uav.FirstArraySlice = (uint)descriptor.BaseArrayLayer;
            uav.ArraySize = (uint)descriptor.ArrayLayerCount;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture3DUAV(ref D3D12_TEX3D_UAV uav, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture3D) == ETextureViewDimension.Texture3D)) {
                return;
            }
            uav.WSize = (uint)descriptor.ArrayLayerCount;
            uav.MipSlice = (uint)descriptor.BaseMipLevel;
            uav.FirstWSlice = (uint)descriptor.BaseArrayLayer;
        }

        internal static void FillTexture2DRTV(ref D3D12_TEX2D_RTV rtv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2D) == ETextureViewDimension.Texture2D)) {
                return;
            }
            rtv.MipSlice = (uint)descriptor.BaseMipLevel;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayRTV(ref D3D12_TEX2D_ARRAY_RTV rtv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture2DArray) == ETextureViewDimension.Texture2DArray)) {
                return;
            }
            rtv.MipSlice = (uint)descriptor.BaseMipLevel;
            rtv.FirstArraySlice = (uint)descriptor.BaseArrayLayer;
            rtv.ArraySize = (uint)descriptor.ArrayLayerCount;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture3DRTV(ref D3D12_TEX3D_RTV rtv, in RHITextureViewDescriptor descriptor)
        {
            if (!((descriptor.Dimension & ETextureViewDimension.Texture3D) == ETextureViewDimension.Texture3D)) {
                return;
            }
            rtv.WSize = (uint)descriptor.ArrayLayerCount;
            rtv.MipSlice = (uint)descriptor.BaseMipLevel;
            rtv.FirstWSlice = (uint)descriptor.BaseArrayLayer;
        }
    }
#pragma warning restore CS8600, CS8602
}
