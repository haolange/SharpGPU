using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIPipelineLayoutDescriptor
    {
        public bool bUseVertexLayout;
        public bool bIsLocalSignature;
        public RHIBindTableLayout[] BindTableLayouts;
        //public RHIPipelineConstantLayout[] PipelineConstantLayouts;
        public Memory<RHIStaticSamplerDescriptor>? StaticSamplers;
    };

    public abstract class RHIPipelineLayout : Disposal
    {

    }
}

