using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIPipelineLayoutDescriptor
    {
        public bool bLocalSignature;
        public bool bUseVertexLayout;
        public RHIBindTableLayout[] BindTableLayouts;
        //public RHIPipelineConstantLayout[] PipelineConstantLayouts;
        public Memory<RHIStaticSamplerDescriptor>? StaticSamplers;
    };

    public abstract class RHIPipelineLayout : Disposal
    {

    }
}

