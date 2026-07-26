using System;
using SharpMetal.Metal;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;

namespace SharpGPU
{
    internal sealed class MetalPipelineLayout : RHIPipelineLayout
    {
        public RHIPipelineLayoutDescriptor Descriptor => m_Descriptor;
        internal int ArgumentTableLayoutCount => m_ArgumentTableLayouts.Length;
        internal ReadOnlySpan<MetalArgumentTableLayout> ArgumentTableLayouts => m_ArgumentTableLayouts;
        internal MetalDevice? Device { get; }

        private readonly RHIPipelineLayoutDescriptor m_Descriptor;
        private readonly MetalArgumentTableLayout[] m_ArgumentTableLayouts;

        public MetalPipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            InitializePipelineCacheIdentity(descriptor);
            if (descriptor.PushConstantSize != 0)
            {
                throw new NotSupportedException(
                    "Metal 4 push constants are not supported until the compiled shader layout provides an explicit buffer index.");
            }

            RHIArgumentTableLayout[]? sourceLayouts = descriptor.ArgumentTableLayouts;
            if (sourceLayouts is null || sourceLayouts.Length == 0)
            {
                m_ArgumentTableLayouts = Array.Empty<MetalArgumentTableLayout>();
            }
            else
            {
                m_ArgumentTableLayouts = new MetalArgumentTableLayout[sourceLayouts.Length];
                HashSet<uint> indices = new();
                for (int index = 0; index < sourceLayouts.Length; ++index)
                {
                    MetalArgumentTableLayout layout = sourceLayouts[index] as MetalArgumentTableLayout
                        ?? throw new ArgumentException(
                            $"Metal pipeline argument table {index} must be a {nameof(MetalArgumentTableLayout)}.",
                            nameof(descriptor));
                    if (layout.IsDisposed)
                    {
                        throw new ObjectDisposedException(
                            nameof(descriptor),
                            $"Metal pipeline argument table {layout.Index} is disposed.");
                    }

                    if (!indices.Add(layout.Index))
                    {
                        throw new ArgumentException(
                            $"Metal pipeline contains duplicate argument table index {layout.Index}.",
                            nameof(descriptor));
                    }

                    m_ArgumentTableLayouts[index] = layout;
                }
            }

            m_Descriptor = descriptor;
            m_Descriptor.ArgumentTableLayouts = new RHIArgumentTableLayout[m_ArgumentTableLayouts.Length];
            for (int index = 0; index < m_ArgumentTableLayouts.Length; ++index)
            {
                m_Descriptor.ArgumentTableLayouts[index] = m_ArgumentTableLayouts[index];
            }
        }

        internal MetalPipelineLayout(
            MetalDevice device,
            in RHIPipelineLayoutDescriptor descriptor)
            : this(descriptor)
        {
            for (int index = 0; index < m_ArgumentTableLayouts.Length; ++index)
            {
                MetalArgumentTableLayout layout = m_ArgumentTableLayouts[index];
                if (!ReferenceEquals(layout.Device, device))
                {
                    throw new ArgumentException(
                        $"Metal pipeline argument table {layout.Index} belongs to a different Metal device.",
                        nameof(descriptor));
                }
            }

            Device = device;
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalComputePipeline : RHIComputePipeline
    {
        public MTLComputePipelineState NativePipelineState => m_NativePipelineState;
        public SharpGPU.Mathematics.uint3 ThreadgroupSize => m_Descriptor.ThreadSize;

        private MTLComputePipelineState m_NativePipelineState;

        public MetalComputePipeline(MetalDevice device, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            MetalPipelineLayout pipelineLayout = descriptor.PipelineLayout as MetalPipelineLayout
                ?? throw new ArgumentException("Metal compute pipeline requires a MetalPipelineLayout.", nameof(descriptor));
            MetalBufferBindingPlanner.ValidatePipelineBufferBudget(
                pipelineLayout.ArgumentTableLayouts, MetalBindingPipelineType.Compute);
            MetalFunction computeFunction = (MetalFunction)descriptor.ComputeFunction;
            MTLComputePipelineDescriptor nativeDescriptor = MTLComputePipelineDescriptor.New();
            nativeDescriptor.ComputeFunction = computeFunction.NativeFunction;
            // Metal ICB encode requires pipelines compiled with supportIndirectCommandBuffers.
            nativeDescriptor.SupportIndirectCommandBuffers = true;
            NSError error = default;
            m_NativePipelineState = device.NativeDevice.NewComputePipelineState(
                nativeDescriptor,
                MTLPipelineOption.None,
                IntPtr.Zero,
                ref error);
            ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTLComputePipelineState: {errorText}");
            }
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }
        }
    }

    internal sealed class MetalRaytracingPipeline : RHIRaytracingPipeline
    {
        internal MTLComputePipelineState NativePipelineState => m_NativePipelineState;
        internal uint3 ThreadgroupSize => m_ThreadgroupSize;
        internal string RayGenerationEntryName => m_RayGenerationEntryName;
        internal int HitGroupCount => m_HitGroups.Length;
        internal int MissGroupCount => m_MissGroups.Length;
        internal int CallableGroupCount => m_CallableGroups.Length;

        private readonly MetalFunctionLibrary m_FunctionLibrary;
        private readonly uint3 m_ThreadgroupSize;
        private readonly string m_RayGenerationEntryName;
        private readonly RHIRayHitGroupDescriptor[] m_HitGroups;
        private readonly RHIRayGeneralGroupDescriptor[] m_MissGroups;
        private readonly RHIRayGeneralGroupDescriptor[] m_CallableGroups;
        private readonly Dictionary<string, RHIRayHitGroupDescriptor> m_HitGroupsByName;
        private readonly HashSet<string> m_MissEntryNames;
        private readonly HashSet<string> m_CallableEntryNames;
        private readonly Dictionary<string, MTLFunction> m_VisibleFunctionCache;
        private readonly Dictionary<string, MTLFunction> m_IntersectionFunctionCache;
        private readonly MTLFunction m_RayGenerationFunction;
        private MTLComputePipelineState m_NativePipelineState;

        public MetalRaytracingPipeline(MetalDevice device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            MetalPipelineLayout pipelineLayout = descriptor.PipelineLayout as MetalPipelineLayout
                ?? throw new ArgumentException("Metal ray tracing pipeline requires a MetalPipelineLayout.", nameof(descriptor));
            MetalBufferBindingPlanner.ValidatePipelineBufferBudget(
                pipelineLayout.ArgumentTableLayouts, MetalBindingPipelineType.Raytracing);
            m_FunctionLibrary = descriptor.FunctionLibrary as MetalFunctionLibrary ?? throw new InvalidOperationException("Metal ray tracing pipeline requires a Metal function library.");
            m_RayGenerationEntryName = descriptor.RayGeneration.General.EntryName;
            if (string.IsNullOrWhiteSpace(m_RayGenerationEntryName))
            {
                throw new InvalidOperationException("Ray generation entry name is empty.");
            }

            m_ThreadgroupSize = new uint3(Math.Max(1u, descriptor.ThreadSize.x), Math.Max(1u, descriptor.ThreadSize.y), Math.Max(1u, descriptor.ThreadSize.z));
            m_HitGroups = descriptor.RayHitGroups.Span.ToArray();
            m_MissGroups = descriptor.RayMissGroups.Span.ToArray();
            m_CallableGroups = descriptor.RayCallableGroups.Span.ToArray();
            m_HitGroupsByName = new Dictionary<string, RHIRayHitGroupDescriptor>(m_HitGroups.Length, StringComparer.Ordinal);
            m_MissEntryNames = new HashSet<string>(StringComparer.Ordinal);
            m_CallableEntryNames = new HashSet<string>(StringComparer.Ordinal);
            m_VisibleFunctionCache = new Dictionary<string, MTLFunction>(StringComparer.Ordinal);
            m_IntersectionFunctionCache = new Dictionary<string, MTLFunction>(StringComparer.Ordinal);

            m_RayGenerationFunction = ResolveKernelFunction(m_RayGenerationEntryName);

            for (int i = 0; i < m_HitGroups.Length; ++i)
            {
                RHIRayHitGroupDescriptor hitGroup = m_HitGroups[i];
                if (string.IsNullOrWhiteSpace(hitGroup.Name))
                {
                    throw new InvalidOperationException($"Ray hit-group[{i}] has an empty name.");
                }

                if (!m_HitGroupsByName.TryAdd(hitGroup.Name, hitGroup))
                {
                    throw new InvalidOperationException($"Duplicate ray hit-group name '{hitGroup.Name}' in pipeline descriptor.");
                }
            }

            for (int i = 0; i < m_MissGroups.Length; ++i)
            {
                string entryName = m_MissGroups[i].General.EntryName;
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException($"Ray miss-group[{i}] has an empty function entry.");
                }

                m_MissEntryNames.Add(entryName);
            }

            for (int i = 0; i < m_CallableGroups.Length; ++i)
            {
                string entryName = m_CallableGroups[i].General.EntryName;
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException($"Ray callable-group[{i}] has an empty function entry.");
                }

                m_CallableEntryNames.Add(entryName);
            }

            List<IntPtr> linkedFunctionPointers = new List<IntPtr>(Math.Max(1, m_MissGroups.Length + m_CallableGroups.Length + m_HitGroups.Length));
            for (int i = 0; i < m_MissGroups.Length; ++i)
            {
                MTLFunction missFunction = ResolveVisibleFunction(m_MissGroups[i].General.EntryName);
                linkedFunctionPointers.Add(missFunction.NativePtr);
            }

            for (int i = 0; i < m_CallableGroups.Length; ++i)
            {
                MTLFunction callableFunction = ResolveVisibleFunction(m_CallableGroups[i].General.EntryName);
                linkedFunctionPointers.Add(callableFunction.NativePtr);
            }

            for (int i = 0; i < m_HitGroups.Length; ++i)
            {
                RHIRayHitGroupDescriptor hitGroup = m_HitGroups[i];
                if (hitGroup.Type == ERHIHitGroupType.Procedural)
                {
                    string? entryName = hitGroup.Intersect?.EntryName;
                    if (string.IsNullOrWhiteSpace(entryName))
                    {
                        throw new InvalidOperationException($"Hit group '{hitGroup.Name}' is procedural but has no intersection function.");
                    }

                    MTLFunction intersectionFunction = ResolveIntersectionFunction(entryName);
                    linkedFunctionPointers.Add(intersectionFunction.NativePtr);
                }
            }

            MTLComputePipelineDescriptor nativeDescriptor = MTLComputePipelineDescriptor.New();
            nativeDescriptor.ComputeFunction = m_RayGenerationFunction;
            if (linkedFunctionPointers.Count > 0)
            {
                MTLLinkedFunctions linkedFunctions = MTLLinkedFunctions.New();
                linkedFunctions.Functions = MetalArrayHelper.CreateNSArrayFromPointers(linkedFunctionPointers.ToArray());
                nativeDescriptor.LinkedFunctions = linkedFunctions;
                ObjectiveCRuntime.Release(linkedFunctions.NativePtr);
            }

            NSError error = default;
            m_NativePipelineState = device.NativeDevice.NewComputePipelineState(nativeDescriptor, MTLPipelineOption.None, IntPtr.Zero, ref error);
            ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create Metal ray tracing compute pipeline state: {errorText}");
            }
        }

        internal RHIRayHitGroupDescriptor GetHitGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_HitGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_HitGroups[index];
        }

        internal RHIRayHitGroupDescriptor GetHitGroupDescriptor(string hitGroupName)
        {
            if (string.IsNullOrWhiteSpace(hitGroupName))
            {
                throw new ArgumentException("Hit-group name is empty.", nameof(hitGroupName));
            }

            if (!m_HitGroupsByName.TryGetValue(hitGroupName, out RHIRayHitGroupDescriptor hitGroup))
            {
                throw new InvalidOperationException($"Hit-group '{hitGroupName}' does not exist in the ray tracing pipeline.");
            }

            return hitGroup;
        }

        internal bool ContainsHitGroup(string hitGroupName)
        {
            if (string.IsNullOrWhiteSpace(hitGroupName))
            {
                return false;
            }

            return m_HitGroupsByName.ContainsKey(hitGroupName);
        }

        internal bool ContainsMissEntry(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return false;
            }

            return m_MissEntryNames.Contains(entryName);
        }

        internal bool ContainsCallableEntry(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return false;
            }

            return m_CallableEntryNames.Contains(entryName);
        }

        internal RHIRayGeneralGroupDescriptor GetMissGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_MissGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_MissGroups[index];
        }

        internal RHIRayGeneralGroupDescriptor GetCallableGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_CallableGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_CallableGroups[index];
        }

        internal MTLFunction ResolveVisibleFunction(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new ArgumentException("Visible function entry name is empty.", nameof(entryName));
            }

            if (m_VisibleFunctionCache.TryGetValue(entryName, out MTLFunction cached))
            {
                return cached;
            }

            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewFunction(new NSString(entryName));
            if (function.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to resolve visible function '{entryName}' from Metal function library.");
            }

            m_VisibleFunctionCache.Add(entryName, function);
            return function;
        }

        internal MTLFunction ResolveIntersectionFunction(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new ArgumentException("Intersection function entry name is empty.", nameof(entryName));
            }

            if (m_IntersectionFunctionCache.TryGetValue(entryName, out MTLFunction cached))
            {
                return cached;
            }

            MTLIntersectionFunctionDescriptor descriptor = MTLIntersectionFunctionDescriptor.New();
            descriptor.Name = new NSString(entryName);

            NSError error = default;
            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewIntersectionFunction(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (function.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to resolve intersection function '{entryName}': {errorText}");
            }

            m_IntersectionFunctionCache.Add(entryName, function);
            return function;
        }

        private MTLFunction ResolveKernelFunction(string entryName)
        {
            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewFunction(new NSString(entryName));
            if (function.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to resolve kernel function '{entryName}' from Metal function library.");
            }

            if (function.FunctionType != MTLFunctionType.Kernel)
            {
                throw new InvalidOperationException($"Ray generation entry '{entryName}' is not a compute kernel function.");
            }

            return function;
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }

            if (m_RayGenerationFunction.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_RayGenerationFunction);
            }

            foreach (KeyValuePair<string, MTLFunction> pair in m_VisibleFunctionCache)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value);
                }
            }

            foreach (KeyValuePair<string, MTLFunction> pair in m_IntersectionFunctionCache)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value);
                }
            }

            m_VisibleFunctionCache.Clear();
            m_IntersectionFunctionCache.Clear();
            m_HitGroupsByName.Clear();
            m_MissEntryNames.Clear();
            m_CallableEntryNames.Clear();
        }
    }

    internal sealed class MetalRasterPipeline : RHIRasterPipeline
    {
        public MTLRenderPipelineState NativePipelineState => m_NativePipelineState;
        public MTLDepthStencilState DepthStencilState => m_DepthStencilState;
        public MTLPrimitiveType PrimitiveType => m_PrimitiveType;
        public MTLCullMode CullMode => m_CullMode;
        public MTLTriangleFillMode FillMode => m_FillMode;
        public MTLWinding Winding => m_Winding;
        internal MetalRasterBufferBindingPlan BufferBindingPlan => m_BufferBindingPlan;
        internal MetalPrivateRasterBindingPlan PrivateRasterBindingPlan =>
            m_PrivateRasterBindingPlan;

        private MTLRenderPipelineState m_NativePipelineState;
        private MTLDepthStencilState m_DepthStencilState;
        private readonly MTLPrimitiveType m_PrimitiveType;
        private readonly MTLCullMode m_CullMode;
        private readonly MTLTriangleFillMode m_FillMode;
        private readonly MTLWinding m_Winding;
        private readonly MetalRasterBufferBindingPlan m_BufferBindingPlan;
        private readonly MetalPrivateRasterBindingPlan
            m_PrivateRasterBindingPlan;

        public MetalRasterPipeline(MetalDevice device, in RHIRasterPipelineDescriptor descriptor)
        {
            m_Descriptor = RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);

            MetalPipelineLayout pipelineLayout = descriptor.PipelineLayout as MetalPipelineLayout
                ?? throw new ArgumentException("Metal raster pipeline requires a MetalPipelineLayout.", nameof(descriptor));
            RHIVertexAssemblerDescriptor vertexAssembler = descriptor.PrimitiveAssembler.VertexAssembler
                ?? throw new NotSupportedException("Metal raster mesh pipelines are not supported by the vertex-buffer binding planner.");
            m_BufferBindingPlan = MetalBufferBindingPlanner.CompileRaster(
                pipelineLayout.ArgumentTableLayouts,
                vertexAssembler.VertexLayouts.Span);
            m_PrivateRasterBindingPlan =
                MetalPrivateRasterBindingPlan.Compile(
                    device,
                    pipelineLayout,
                    in m_Descriptor.AttachmentInterface);

            MetalFunction vertexFunction = (MetalFunction)vertexAssembler.VertexFunction;
            MetalFunction fragmentFunction = descriptor.FragmentFunction as MetalFunction
                ?? throw new ArgumentException(
                    "Metal raster pipeline requires a MetalFunction fragment shader.",
                    nameof(descriptor));

            MTL4LibraryFunctionDescriptor vertexFunctionDescriptor =
                MTL4LibraryFunctionDescriptor.New();
            vertexFunctionDescriptor.Library = vertexFunction.NativeLibrary;
            vertexFunctionDescriptor.Name =
                new NSString(vertexFunction.Descriptor.EntryName);
            MTL4LibraryFunctionDescriptor fragmentFunctionDescriptor =
                MTL4LibraryFunctionDescriptor.New();
            fragmentFunctionDescriptor.Library = fragmentFunction.NativeLibrary;
            fragmentFunctionDescriptor.Name =
                new NSString(fragmentFunction.Descriptor.EntryName);

            MTL4RenderPipelineDescriptor nativeDescriptor =
                MTL4RenderPipelineDescriptor.New();
            nativeDescriptor.VertexFunctionDescriptor =
                vertexFunctionDescriptor;
            nativeDescriptor.FragmentFunctionDescriptor =
                fragmentFunctionDescriptor;
            nativeDescriptor.RasterSampleCount = (ulong)descriptor.SampleCount;
            nativeDescriptor.InputPrimitiveTopology = MetalUtility.ConvertToMetalPrimitiveTopologyClass(descriptor.PrimitiveAssembler.PrimitiveTopology);
            nativeDescriptor.AlphaToCoverageState =
                descriptor.RenderState.BlendState.AlphaToCoverage
                    ? MTL4AlphaToCoverageState.Enabled
                    : MTL4AlphaToCoverageState.Disabled;
            nativeDescriptor.RasterizationEnabled = true;
            nativeDescriptor.ColorAttachmentMappingState =
                MTL4LogicalToPhysicalColorAttachmentMappingState.Inherited;

            // Use the SnapshotAndValidate'd descriptor: raw AttachmentInterface is
            // often default (ColorOutputLocationCount=0), which skips PSO color
            // pixelFormat setup and drops fragment writes on BGRA drawables.
            ConfigureColorAttachments(nativeDescriptor, in m_Descriptor);
            ConfigureDepthStencilAttachment(nativeDescriptor, m_Descriptor.DepthFormat);
            ConfigureVertexLayout(nativeDescriptor, in m_Descriptor, m_BufferBindingPlan);

            MTL4PipelineOptions pipelineOptions = MTL4PipelineOptions.New();
            MTL4PipelineDescriptor pipelineDescriptor = nativeDescriptor;
            pipelineDescriptor.Options = pipelineOptions;

            NSError compilerError = default;
            MTL4CompilerDescriptor compilerDescriptor =
                MTL4CompilerDescriptor.New();
            MTL4Compiler compiler =
                device.NativeDevice.NewCompiler(
                    compilerDescriptor,
                    ref compilerError);
            ObjectiveCRuntime.Release(compilerDescriptor);
            if (compiler.NativePtr == IntPtr.Zero)
            {
                string errorText = compilerError.NativePtr != IntPtr.Zero
                    ? compilerError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"Failed to create MTL4Compiler for raster pipeline: {errorText}");
            }

            MTL4CompilerTaskOptions taskOptions =
                MTL4CompilerTaskOptions.New();
            NSError error = default;
            m_NativePipelineState = compiler.NewRenderPipelineState(
                pipelineDescriptor,
                taskOptions,
                ref error);
            ObjectiveCRuntime.Release(taskOptions);
            ObjectiveCRuntime.Release(compiler);
            ObjectiveCRuntime.Release(pipelineOptions);
            ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            ObjectiveCRuntime.Release(fragmentFunctionDescriptor);
            ObjectiveCRuntime.Release(vertexFunctionDescriptor);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException(
                    $"Failed to create MTL4 raster pipeline state: {errorText}");
            }

            m_DepthStencilState = CreateDepthStencilState(device, descriptor.RenderState.DepthStencilState);
            m_PrimitiveType = MetalUtility.ConvertToMetalPrimitiveType(descriptor.PrimitiveAssembler.PrimitiveTopology);
            m_CullMode = MetalUtility.ConvertToMetalCullMode(descriptor.RenderState.RasterizerState.CullMode);
            m_FillMode = MetalUtility.ConvertToMetalFillMode(descriptor.RenderState.RasterizerState.FillMode);
            m_Winding = MetalUtility.ConvertToMetalWinding(descriptor.RenderState.RasterizerState.FrontCounterClockwise);
        }

        private static void ConfigureColorAttachments(
            MTL4RenderPipelineDescriptor nativeDescriptor,
            in RHIRasterPipelineDescriptor descriptor)
        {
            RHIAttachmentInterfaceSignature signature =
                descriptor.AttachmentInterface;
            for (int outputLocation = 0;
                 outputLocation < signature.ColorOutputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    signature.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (logicalAttachment ==
                    RHIAttachmentInterfaceSignature
                        .UnboundLogicalAttachment)
                {
                    continue;
                }

                byte logicalBit =
                    checked((byte)(1 << logicalAttachment));
                if ((signature.RasterOrderedReadWriteMask &
                     logicalBit) != 0)
                {
                    // ROG textures use only the backend-private fragment
                    // texture range and never an ordinary color target.
                    continue;
                }

                MTL4RenderPipelineColorAttachmentDescriptor nativeColor =
                    nativeDescriptor.ColorAttachments.Object(
                        checked((uint)outputLocation));
                nativeColor.PixelFormat =
                    MetalUtility.ConvertToMetalPixelFormat(
                        descriptor.ColorFormats[logicalAttachment]);

                RHIBlendDescriptor blend =
                    GetBlendDescriptor(
                        descriptor.RenderState.BlendState,
                        outputLocation);
                nativeColor.BlendingState = blend.BlendEnable
                    ? MTL4BlendState.Enabled
                    : MTL4BlendState.Disabled;
                nativeColor.SourceRGBBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.SrcBlendColor);
                nativeColor.DestinationRGBBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.DstBlendColor);
                nativeColor.RGBBlendOperation = MetalUtility.ConvertToMetalBlendOperation(blend.BlendOpColor);
                nativeColor.SourceAlphaBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.SrcBlendAlpha);
                nativeColor.DestinationAlphaBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.DstBlendAlpha);
                nativeColor.AlphaBlendOperation = MetalUtility.ConvertToMetalBlendOperation(blend.BlendOpAlpha);
                nativeColor.WriteMask = MetalUtility.ConvertToMetalColorWriteMask(blend.ColorWriteChannel);
            }
        }

        private static RHIBlendDescriptor GetBlendDescriptor(in RHIBlendStateDescriptor blendState, in int index)
        {
            switch (index)
            {
                case 0: return blendState.BlendDescriptor0;
                case 1: return blendState.BlendDescriptor1;
                case 2: return blendState.BlendDescriptor2;
                case 3: return blendState.BlendDescriptor3;
                case 4: return blendState.BlendDescriptor4;
                case 5: return blendState.BlendDescriptor5;
                case 6: return blendState.BlendDescriptor6;
                default: return blendState.BlendDescriptor7;
            }
        }

        private static void ConfigureDepthStencilAttachment(MTL4RenderPipelineDescriptor nativeDescriptor, in ERHIPixelFormat depthFormat)
        {
            if (depthFormat != ERHIPixelFormat.Unknown)
            {
                MTLPixelFormat nativeFormat = MetalUtility.ConvertToMetalPixelFormat(depthFormat);
                nativeDescriptor.DepthAttachmentPixelFormat = nativeFormat;
                if (depthFormat == ERHIPixelFormat.D24_UNorm_S8_UInt || depthFormat == ERHIPixelFormat.D32_Float_S8_UInt)
                {
                    nativeDescriptor.StencilAttachmentPixelFormat = nativeFormat;
                }
            }
        }

        private static void ConfigureVertexLayout(
            MTL4RenderPipelineDescriptor nativeDescriptor,
            in RHIRasterPipelineDescriptor descriptor,
            MetalRasterBufferBindingPlan bufferBindingPlan)
        {
            if (!descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                return;
            }

            MTLVertexDescriptor nativeVertexDescriptor = MTLVertexDescriptor.New();
            Span<RHIVertexLayoutDescriptor> layouts = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexLayouts.Span;

            uint attributeIndex = 0;
            for (int layoutIndex = 0; layoutIndex < layouts.Length; ++layoutIndex)
            {
                ref RHIVertexLayoutDescriptor layout = ref layouts[layoutIndex];
                MetalVertexBufferBinding binding = bufferBindingPlan.GetVertexBinding(layout.Index);
                MTLVertexBufferLayoutDescriptor nativeLayout = nativeVertexDescriptor.Layouts[checked((uint)binding.PhysicalIndex)];
                nativeLayout.Stride = layout.Stride;
                nativeLayout.StepFunction = MetalUtility.ConvertToMetalVertexStepFunction(layout.StepMode);
                nativeLayout.StepRate = Math.Max(1u, layout.StepRate);

                Span<RHIVertexElementDescriptor> elements = layout.VertexElements.Span;
                for (int elementIndex = 0; elementIndex < elements.Length; ++elementIndex)
                {
                    ref RHIVertexElementDescriptor element = ref elements[elementIndex];
                    MTLVertexAttributeDescriptor nativeAttribute = nativeVertexDescriptor.Attributes[attributeIndex++];
                    nativeAttribute.Format = MetalUtility.ConvertToMetalVertexFormat(element.Format);
                    nativeAttribute.Offset = element.Offset;
                    nativeAttribute.BufferIndex = binding.PhysicalIndex;
                }
            }

            nativeDescriptor.VertexDescriptor = nativeVertexDescriptor;
        }

        private static MTLDepthStencilState CreateDepthStencilState(MetalDevice device, in RHIDepthStencilStateDescriptor descriptor)
        {
            if (!descriptor.DepthEnable && !descriptor.StencilEnable)
            {
                return default;
            }

            MTLDepthStencilDescriptor nativeDepthStencil = MTLDepthStencilDescriptor.New();
            nativeDepthStencil.DepthCompareFunction = MetalUtility.ConvertToMetalCompareFunction(descriptor.ComparisonMode);
            nativeDepthStencil.SetDepthWriteEnabled(descriptor.DepthEnable && descriptor.DepthWriteMask);

            if (descriptor.StencilEnable)
            {
                MTLStencilDescriptor frontFace = BuildStencilDescriptor(descriptor.FrontFace, descriptor.StencilReadMask, descriptor.StencilWriteMask);
                MTLStencilDescriptor backFace = BuildStencilDescriptor(descriptor.BackFace, descriptor.StencilReadMask, descriptor.StencilWriteMask);
                nativeDepthStencil.FrontFaceStencil = frontFace;
                nativeDepthStencil.BackFaceStencil = backFace;
            }

            return device.NativeDevice.NewDepthStencilState(nativeDepthStencil);
        }

        private static MTLStencilDescriptor BuildStencilDescriptor(in RHIStencilStateDescriptor descriptor, in byte readMask, in byte writeMask)
        {
            MTLStencilDescriptor nativeStencil = MTLStencilDescriptor.New();
            nativeStencil.StencilCompareFunction = MetalUtility.ConvertToMetalCompareFunction(descriptor.ComparisonMode);
            nativeStencil.StencilFailureOperation = ConvertToMetalStencilOperation(descriptor.StencilFailOp);
            nativeStencil.DepthFailureOperation = ConvertToMetalStencilOperation(descriptor.StencilDepthFailOp);
            nativeStencil.DepthStencilPassOperation = ConvertToMetalStencilOperation(descriptor.StencilPassOp);
            nativeStencil.ReadMask = readMask;
            nativeStencil.WriteMask = writeMask;
            return nativeStencil;
        }

        private static MTLStencilOperation ConvertToMetalStencilOperation(in ERHIStencilOp op)
        {
            switch (op)
            {
                case ERHIStencilOp.Zero:
                    return MTLStencilOperation.Zero;
                case ERHIStencilOp.Replace:
                    return MTLStencilOperation.Replace;
                case ERHIStencilOp.IncrementSaturation:
                    return MTLStencilOperation.IncrementClamp;
                case ERHIStencilOp.DecrementSaturation:
                    return MTLStencilOperation.DecrementClamp;
                case ERHIStencilOp.Invert:
                    return MTLStencilOperation.Invert;
                case ERHIStencilOp.Increment:
                    return MTLStencilOperation.IncrementWrap;
                case ERHIStencilOp.Decrement:
                    return MTLStencilOperation.DecrementWrap;
                default:
                    return MTLStencilOperation.Keep;
            }
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }

            if (m_DepthStencilState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_DepthStencilState);
                m_DepthStencilState = default;
            }
        }
    }

}
