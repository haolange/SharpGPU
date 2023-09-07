using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIOutputStateDescriptor
    {
        public uint OutputCount;
        public ESampleCount SampleCount;
        public EPixelFormat ColorFormat0;
        public EPixelFormat ColorFormat1;
        public EPixelFormat ColorFormat2;
        public EPixelFormat ColorFormat3;
        public EPixelFormat ColorFormat4;
        public EPixelFormat ColorFormat5;
        public EPixelFormat ColorFormat6;
        public EPixelFormat ColorFormat7;
        public EPixelFormat DepthStencilFormat;
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

    public struct RHIComputePipelineStateDescriptor
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

    public struct RHIRaytracingPipelineStateDescriptor
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

    public struct RHIMeshletPipelineStateDescriptor
    {
        public RHIFunction TaskFunction;
        public RHIFunction MeshFunction;
        public RHIFunction FragmentFunction;
        public RHIPipelineLayout PipelineLayout;
        public EPrimitiveTopology PrimitiveTopology;
        public RHIOutputStateDescriptor OutputState;
        public RHIRenderStateDescriptor RenderState;
    }

    public struct RHIGraphicsPipelineStateDescriptor
    {
        public RHIFunction VertexFunction;
        public RHIFunction FragmentFunction;
        public RHIPipelineLayout PipelineLayout;
        public EPrimitiveTopology PrimitiveTopology;
        public RHIOutputStateDescriptor OutputState;
        public RHIRenderStateDescriptor RenderState;
        public Memory<RHIVertexLayoutDescriptor> VertexLayouts;
    }

    public struct RHIPipelineStateLibraryResult
    {
        public uint ByteSize;
        public IntPtr ByteCode;
    }

    public abstract class RHIComputePipelineState : Disposal
    {
        public RHIComputePipelineStateDescriptor Descriptor => m_Descriptor;

        protected RHIComputePipelineStateDescriptor m_Descriptor;
    }

    public abstract class RHIRaytracingPipelineState : Disposal
    {
        public RHIRaytracingPipelineStateDescriptor Descriptor => m_Descriptor;

        protected RHIRaytracingPipelineStateDescriptor m_Descriptor;
    }

    public abstract class RHIMeshletPipelineState : Disposal
    {
        public RHIMeshletPipelineStateDescriptor Descriptor => m_Descriptor;

        protected RHIMeshletPipelineStateDescriptor m_Descriptor;
    }

    public abstract class RHIGraphicsPipelineState : Disposal
    {
        public RHIGraphicsPipelineStateDescriptor Descriptor => m_Descriptor;

        protected RHIGraphicsPipelineStateDescriptor m_Descriptor;
    }

    public abstract class RHIPipelineStateLibrary : Disposal
    {
        public RHIPipelineStateLibrary(in RHIPipelineStateLibraryResult PipelineStateCache)
        {

        }

        public abstract void StoreComputePipelineState(string name, RHIComputePipelineState computePipelineState);
        public abstract void StoreRaytracingPipelineState(string name, RHIRaytracingPipelineState raytracingPipelineState);
        public abstract void StoreMeshletPipelineState(string name, RHIMeshletPipelineState meshletPipelineState);
        public abstract void StoreGraphicsPipelineState(string name, RHIGraphicsPipelineState graphicsPipelineState);
        public abstract RHIComputePipelineState LoadComputePipelineState(RHIComputePipelineStateDescriptor computePipelineDescriptor);
        public abstract RHIRaytracingPipelineState LoadRaytracingPipelineState(RHIRaytracingPipelineStateDescriptor raytracingPipelineDescriptor);
        public abstract RHIMeshletPipelineState LoadMeshletPipelineState(RHIMeshletPipelineStateDescriptor meshletPipelineDescriptor);
        public abstract RHIGraphicsPipelineState LoadGraphicsPipelineState(RHIGraphicsPipelineStateDescriptor graphicsPipelineDescriptor);
        public abstract RHIPipelineStateLibraryResult Serialize();
    }
}
