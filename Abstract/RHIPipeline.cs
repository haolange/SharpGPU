using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIOutputStateDescriptor
    {
        //public uint SliceCount;
        public ESampleCount SampleCount;
        public EPixelFormat? DepthStencilFormat;
        public Memory<EPixelFormat> ColorFormats;
    }

    public struct RHIVertexElementDescriptor
    {
        public uint Slot;
        public uint Offset;
        public ESemanticType Type;
        public ESemanticFormat Format;
    }

    public struct RHIVertexLayoutDescriptor
    {
        public uint Index;
        public uint Stride;
        public uint StepRate;
        public EVertexStepMode StepMode;
        public Memory<RHIVertexElementDescriptor> VertexElements;
    }

    public struct RHIBlendDescriptor
    {
        public bool BlendEnable;
        public EBlendOp BlendOpColor;
        public EBlendMode SrcBlendColor;
        public EBlendMode DstBlendColor;
        public EBlendOp BlendOpAlpha;
        public EBlendMode SrcBlendAlpha;
        public EBlendMode DstBlendAlpha;
        public EColorWriteChannel ColorWriteChannel;
    }

    public struct RHIBlendStateDescriptor
    {
        public bool AlphaToCoverage;
        public bool IndependentBlend;
        public RHIBlendDescriptor BlendDescriptor0;
        public RHIBlendDescriptor BlendDescriptor1;
        public RHIBlendDescriptor BlendDescriptor2;
        public RHIBlendDescriptor BlendDescriptor3;
        public RHIBlendDescriptor BlendDescriptor4;
        public RHIBlendDescriptor BlendDescriptor5;
        public RHIBlendDescriptor BlendDescriptor6;
        public RHIBlendDescriptor BlendDescriptor7;
    }

    public struct RHIRasterizerStateDescriptor
    {
        public EFillMode FillMode;
        public ECullMode CullMode;
        public bool DepthClipEnable;
        public bool ConservativeRaster;
        public bool AntialiasedLineEnable;
        public bool FrontCounterClockwise;
        public uint DepthBias;
        public float DepthBiasClamp;
        public float SlopeScaledDepthBias;
    }

    public struct RHIStencilStateDescriptor
    {
        public EStencilOp StencilPassOp;
        public EStencilOp StencilFailOp;
        public EStencilOp StencilDepthFailOp;
        public EComparisonMode ComparisonMode;
    }

    public struct RHIDepthStencilStateDescriptor
    {
        public bool DepthEnable;
        public bool DepthWriteMask;
        public bool StencilEnable;
        public byte StencilReadMask;
        public byte StencilWriteMask;
        public EComparisonMode ComparisonMode;
        public RHIStencilStateDescriptor BackFace;
        public RHIStencilStateDescriptor FrontFace;
    }

    public struct RHIRenderStateDescriptor
    {
        public uint? SampleMask;
        public RHIBlendStateDescriptor BlendState;
        public RHIRasterizerStateDescriptor RasterizerState;
        public RHIDepthStencilStateDescriptor DepthStencilState;
    }

    public struct RHIComputePipelineDescriptor
    {
        public uint3 ThreadSize;
        public RHIFunction ComputeFunction;
        public RHIPipelineLayout PipelineLayout;
    }

    public struct RHIRayHitGroupDescriptor
    {
        public string Name;
        public EHitGroupType Type;
        internal RHIPipelineLayout PipelineLayout;
        public RHIRayFunctionDescriptor? AnyHit;
        public RHIRayFunctionDescriptor? Intersect;
        public RHIRayFunctionDescriptor? ClosestHit;
    }

    public struct RHIRayGeneralGroupDescriptor
    {
        public string Name;
        public RHIRayFunctionDescriptor General;
        internal RHIPipelineLayout PipelineLayout;
    }

    public struct RHIRaytracingPipelineDescriptor
    {
        public uint MaxPayloadSize;
        public uint MaxAttributeSize;
        public uint MaxRecursionDepth;
        public RHIPipelineLayout PipelineLayout;
        public RHIFunctionLibrary FunctionLibrary;
        public RHIRayGeneralGroupDescriptor RayGeneration;
        public Memory<RHIRayHitGroupDescriptor> RayHitGroups;
        public Memory<RHIRayGeneralGroupDescriptor> RayMissGroups;
    }

    public struct RHIMeshletPipelineDescriptor
    {
        public RHIFunction TaskFunction;
        public RHIFunction MeshFunction;
        public RHIFunction FragmentFunction;
        public RHIPipelineLayout PipelineLayout;
        public EPrimitiveTopology PrimitiveTopology;
        public RHIOutputStateDescriptor OutputState;
        public RHIRenderStateDescriptor RenderState;
    }

    public struct RHIGraphicsPipelineDescriptor
    {
        public RHIFunction VertexFunction;
        public RHIFunction FragmentFunction;
        public RHIPipelineLayout PipelineLayout;
        public EPrimitiveTopology PrimitiveTopology;
        public RHIOutputStateDescriptor OutputState;
        public RHIRenderStateDescriptor RenderState;
        public Memory<RHIVertexLayoutDescriptor> VertexLayouts;
    }

    public abstract class RHIComputePipeline : Disposal
    {
        public RHIComputePipelineDescriptor Descriptor => m_Descriptor;

        protected RHIComputePipelineDescriptor m_Descriptor;
    }

    public abstract class RHIRaytracingPipeline : Disposal
    {
        public RHIRaytracingPipelineDescriptor Descriptor => m_Descriptor;

        protected RHIRaytracingPipelineDescriptor m_Descriptor;
    }

    public abstract class RHIMeshletPipeline : Disposal
    {
        public RHIMeshletPipelineDescriptor Descriptor => m_Descriptor;

        protected RHIMeshletPipelineDescriptor m_Descriptor;
    }

    public abstract class RHIGraphicsPipeline : Disposal
    {
        public RHIGraphicsPipelineDescriptor Descriptor => m_Descriptor;

        protected RHIGraphicsPipelineDescriptor m_Descriptor;
    }
}
