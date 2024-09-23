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

    public struct RHIComputePipelineDescriptor
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

    public struct RHIMeshletAssemblerDescriptor
    {
        public RHIFunction TaskFunction;
        public RHIFunction MeshFunction;

        public RHIMeshletAssemblerDescriptor(RHIFunction taskFunction, RHIFunction meshFunction)
        {
            TaskFunction = taskFunction;
            MeshFunction = meshFunction;
        }
    }

    public struct RHIPrimitiveAssemblerDescriptor
    {
        public ERHIPrimitiveType PrimitiveType => (MeshletAssembler.HasValue ? ERHIPrimitiveType.Mesh : ERHIPrimitiveType.Vertex);

        public ERHIPrimitiveTopology PrimitiveTopology;
        public RHIVertexAssemblerDescriptor? VertexAssembler;
        public RHIMeshletAssemblerDescriptor? MeshletAssembler;
    }

    public struct RHIRasterPipelineDescriptor
    {
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat DepthFormat;
        public ERHIPixelFormat[] ColorFormats;
        public RHIRenderStateDescriptor RenderState;
        public RHIFunction FragmentFunction;
        public RHIPipelineLayout PipelineLayout;
        public RHIPrimitiveAssemblerDescriptor PrimitiveAssembler;
    }

    public struct RHIPipelineLibraryResult
    {
        public uint ByteSize;
        public IntPtr ByteCode;
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

    public abstract class RHIRasterPipeline : Disposal
    { 
        public RHIRasterPipelineDescriptor Descriptor => m_Descriptor;

        protected RHIRasterPipelineDescriptor m_Descriptor;
    }

    public abstract class RHIPipelineLibrary : Disposal
    {
        public RHIPipelineLibrary(in RHIPipelineLibraryResult pipelineLibraryResult)
        {

        }

        public abstract void StoreComputePipeline(string name, RHIComputePipeline computePipeline);
        public abstract void StoreRaytracingPipeline(string name, RHIRaytracingPipeline raytracingPipeline);
        public abstract void StoreRasterPipeline(string name, RHIRasterPipeline rasterPipeline);
        public abstract RHIComputePipeline LoadComputePipeline(RHIComputePipelineDescriptor computePipelineDescriptor);
        public abstract RHIRaytracingPipeline LoadRaytracingPipeline(RHIRaytracingPipelineDescriptor raytracingPipelineDescriptor);
        public abstract RHIRasterPipeline LoadRasterPipeline(RHIRasterPipelineDescriptor rasterPipelineDescriptor);
        public abstract RHIPipelineLibraryResult Serialize();
    }
}
