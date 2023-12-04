using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIOutputStateDescriptor
    {
        public uint OutputCount;
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat ColorFormat0;
        public ERHIPixelFormat ColorFormat1;
        public ERHIPixelFormat ColorFormat2;
        public ERHIPixelFormat ColorFormat3;
        public ERHIPixelFormat ColorFormat4;
        public ERHIPixelFormat ColorFormat5;
        public ERHIPixelFormat ColorFormat6;
        public ERHIPixelFormat ColorFormat7;
        public ERHIPixelFormat DepthStencilFormat;
    }

    public struct RHIVertexElementDescriptor
    {
        public uint Slot;
        public uint Offset;
        public ERHISemanticType Type;
        public ERHISemanticFormat Format;
    }

    public struct RHIVertexLayoutDescriptor
    {
        public uint Index;
        public uint Stride;
        public uint StepRate;
        public ERHIVertexStepMode StepMode;
        public Memory<RHIVertexElementDescriptor> VertexElements;
    }

    public struct RHIBlendDescriptor
    {
        public bool BlendEnable;
        public ERHIBlendOp BlendOpColor;
        public ERHIBlendMode SrcBlendColor;
        public ERHIBlendMode DstBlendColor;
        public ERHIBlendOp BlendOpAlpha;
        public ERHIBlendMode SrcBlendAlpha;
        public ERHIBlendMode DstBlendAlpha;
        public ERHIColorWriteChannel ColorWriteChannel;
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
        public ERHIFillMode FillMode;
        public ERHICullMode CullMode;
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
        public ERHIStencilOp StencilPassOp;
        public ERHIStencilOp StencilFailOp;
        public ERHIStencilOp StencilDepthFailOp;
        public ERHIComparisonMode ComparisonMode;
    }

    public struct RHIDepthStencilStateDescriptor
    {
        public bool DepthEnable;
        public bool DepthWriteMask;
        public bool StencilEnable;
        public byte StencilReadMask;
        public byte StencilWriteMask;
        public ERHIComparisonMode ComparisonMode;
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
        public ERHIHitGroupType Type;
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

    public struct RHIVertexAssemblerDescriptor
    {
        public RHIFunction VertexFunction;
        public Memory<RHIVertexLayoutDescriptor> VertexLayouts;

        public RHIVertexAssemblerDescriptor(RHIFunction vertexFunction, in Memory<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            VertexLayouts = vertexLayouts;
            VertexFunction = vertexFunction;
        }
    }

    public struct RHIMeshAssemblerDescriptor
    {
        public RHIFunction TaskFunction;
        public RHIFunction MeshFunction;

        public RHIMeshAssemblerDescriptor(RHIFunction taskFunction, RHIFunction meshFunction)
        {
            TaskFunction = taskFunction;
            MeshFunction = meshFunction;
        }
    }

    public struct RHIPrimitiveAssemblerDescriptor
    {
        public ERHIPrimitiveType PrimitiveType => (MeshAssembler.HasValue ? ERHIPrimitiveType.Mesh : ERHIPrimitiveType.Vertex);

        public ERHIPrimitiveTopology PrimitiveTopology;
        public RHIMeshAssemblerDescriptor? MeshAssembler;
        public RHIVertexAssemblerDescriptor? VertexAssembler;
    }

    public struct RHIGraphicsPipelineStateDescriptor
    {
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat DepthFormat;
        public ERHIPixelFormat[] ColorFormats;
        public RHIRenderStateDescriptor RenderState;
        public RHIPipelineLayout PipelineLayout;
        public RHIFunction FragmentFunction;
        public RHIPrimitiveAssemblerDescriptor PrimitiveAssembler;
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

    public abstract class RHIGraphicsPipelineState : Disposal
    { 
        public RHIGraphicsPipelineStateDescriptor Descriptor => m_Descriptor;

        protected RHIGraphicsPipelineStateDescriptor m_Descriptor;
    }

    public abstract class RHIPipelineStateLibrary : Disposal
    {
        public RHIPipelineStateLibrary(in RHIPipelineStateLibraryResult pipelineStateLibraryResult)
        {

        }

        public abstract void StoreComputePipelineState(string name, RHIComputePipelineState computePipelineState);
        public abstract void StoreRaytracingPipelineState(string name, RHIRaytracingPipelineState raytracingPipelineState);
        public abstract void StoreGraphicsPipelineState(string name, RHIGraphicsPipelineState graphicsPipelineState);
        public abstract RHIComputePipelineState LoadComputePipelineState(RHIComputePipelineStateDescriptor computePipelineDescriptor);
        public abstract RHIRaytracingPipelineState LoadRaytracingPipelineState(RHIRaytracingPipelineStateDescriptor raytracingPipelineDescriptor);
        public abstract RHIGraphicsPipelineState LoadGraphicsPipelineState(RHIGraphicsPipelineStateDescriptor graphicsPipelineDescriptor);
        public abstract RHIPipelineStateLibraryResult Serialize();
    }
}
