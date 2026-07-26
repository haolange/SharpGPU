using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        internal int BindingTableLayoutCount => m_BindingTableLayouts.Length;
        internal ReadOnlySpan<MetalBindingTableLayout> BindingTableLayouts => m_BindingTableLayouts;
        internal MetalDevice? Device { get; }

        private readonly RHIPipelineLayoutDescriptor m_Descriptor;
        private readonly MetalBindingTableLayout[] m_BindingTableLayouts;

        public MetalPipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            InitializePipelineCacheIdentity(descriptor);
            if (descriptor.PushConstantSize != 0)
            {
                throw new NotSupportedException(
                    "Metal 4 push constants are not supported until the compiled shader layout provides an explicit buffer index.");
            }

            RHIBindingTableLayout[]? sourceLayouts = descriptor.BindingTableLayouts;
            if (sourceLayouts is null || sourceLayouts.Length == 0)
            {
                m_BindingTableLayouts = Array.Empty<MetalBindingTableLayout>();
            }
            else
            {
                m_BindingTableLayouts = new MetalBindingTableLayout[sourceLayouts.Length];
                HashSet<uint> indices = new();
                for (int index = 0; index < sourceLayouts.Length; ++index)
                {
                    MetalBindingTableLayout layout = sourceLayouts[index] as MetalBindingTableLayout
                        ?? throw new ArgumentException(
                            $"Metal pipeline binding table {index} must be a {nameof(MetalBindingTableLayout)}.",
                            nameof(descriptor));
                    if (layout.IsDisposed)
                    {
                        throw new ObjectDisposedException(
                            nameof(descriptor),
                            $"Metal pipeline binding table {layout.Index} is disposed.");
                    }

                    if (!indices.Add(layout.Index))
                    {
                        throw new ArgumentException(
                            $"Metal pipeline contains duplicate binding table index {layout.Index}.",
                            nameof(descriptor));
                    }

                    m_BindingTableLayouts[index] = layout;
                }
            }

            m_Descriptor = descriptor;
            m_Descriptor.BindingTableLayouts = new RHIBindingTableLayout[m_BindingTableLayouts.Length];
            for (int index = 0; index < m_BindingTableLayouts.Length; ++index)
            {
                m_Descriptor.BindingTableLayouts[index] = m_BindingTableLayouts[index];
            }
        }

        internal MetalPipelineLayout(
            MetalDevice device,
            in RHIPipelineLayoutDescriptor descriptor)
            : this(descriptor)
        {
            for (int index = 0; index < m_BindingTableLayouts.Length; ++index)
            {
                MetalBindingTableLayout layout = m_BindingTableLayouts[index];
                if (!ReferenceEquals(layout.Device, device))
                {
                    throw new ArgumentException(
                        $"Metal pipeline binding table {layout.Index} belongs to a different Metal device.",
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
                pipelineLayout.BindingTableLayouts, MetalBindingPipelineType.Compute);
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
                pipelineLayout.BindingTableLayouts, MetalBindingPipelineType.Raytracing);
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
                pipelineLayout.BindingTableLayouts,
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

    #region PrivateRasterBinding
    internal readonly struct MetalPrivateRasterBindingPlan
    {
        internal uint TextureBase { get; }
        internal uint MaximumTextureBindings { get; }
        internal uint RequiredTextureBindingCount { get; }
        internal byte RasterOrderedMask { get; }

        internal bool HasRasterOrderedBindings =>
            RasterOrderedMask != 0;

        private MetalPrivateRasterBindingPlan(
            uint textureBase,
            uint maximumTextureBindings,
            uint requiredTextureBindingCount,
            byte rasterOrderedMask)
        {
            TextureBase = textureBase;
            MaximumTextureBindings = maximumTextureBindings;
            RequiredTextureBindingCount = requiredTextureBindingCount;
            RasterOrderedMask = rasterOrderedMask;
        }

        internal uint GetRasterOrderedTextureIndex(
            int logicalAttachment)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            byte bit =
                checked((byte)(1 << logicalAttachment));
            if ((RasterOrderedMask & bit) == 0)
            {
                throw new InvalidOperationException(
                    $"Logical attachment {logicalAttachment} is not " +
                    "raster-ordered in this pipeline.");
            }
            uint index = checked(
                TextureBase +
                checked((uint)logicalAttachment));
            if (index >= MaximumTextureBindings)
            {
                throw new InvalidOperationException(
                    $"Metal private texture index {index} exceeds the " +
                    $"qualified fragment texture limit " +
                    $"{MaximumTextureBindings}.");
            }
            return index;
        }

        internal static MetalPrivateRasterBindingPlan Compile(
            MetalDevice device,
            MetalPipelineLayout pipelineLayout,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            ValidatePipelineLayoutIdentity(
                pipelineLayout.IsDisposed,
                pipelineLayout.Device,
                device);
            if (!device.Capabilities.Binding.DescriptorIndexing.Limits
                    .TryGetValue(
                        ERHICapabilityLimitKind.MaximumBoundTextures,
                        out ulong maximumTextureBindings) ||
                maximumTextureBindings == 0 ||
                maximumTextureBindings > uint.MaxValue)
            {
                throw new NotSupportedException(
                    "The Metal device did not publish a usable " +
                    "MaximumBoundTextures capability limit.");
            }

            return Compile(
                pipelineLayout.BindingTableLayouts,
                checked((uint)maximumTextureBindings),
                in attachmentInterface);
        }

        internal static MetalPrivateRasterBindingPlan Compile(
            ReadOnlySpan<MetalBindingTableLayout> layouts,
            uint maximumTextureBindings,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumTextureBindings == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTextureBindings),
                    maximumTextureBindings,
                    "Metal must expose at least one fragment texture binding.");
            }

            uint textureBase = GetOrdinaryTextureRangeEnd(layouts);
            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            uint requiredTextureBindingCount = textureBase;
            if (rasterOrderedMask != 0)
            {
                int highestLogicalAttachment =
                    HighestSetBit(rasterOrderedMask);
                uint highestPrivateIndex = checked(
                    textureBase +
                    checked((uint)highestLogicalAttachment));
                if (highestPrivateIndex >= maximumTextureBindings)
                {
                    throw new NotSupportedException(
                        $"Metal ordinary texture bindings consume indices " +
                        $"through {Math.Max(0, (long)textureBase - 1)}; " +
                        $"raster-ordered logical attachment " +
                        $"{highestLogicalAttachment} would require private " +
                        $"index {highestPrivateIndex}, exceeding the " +
                        $"qualified limit {maximumTextureBindings}.");
                }
                requiredTextureBindingCount =
                    checked(highestPrivateIndex + 1);
            }

            return new MetalPrivateRasterBindingPlan(
                textureBase,
                maximumTextureBindings,
                requiredTextureBindingCount,
                rasterOrderedMask);
        }

        internal static uint GetOrdinaryTextureRangeEnd(
            ReadOnlySpan<MetalBindingTableLayout> layouts)
        {
            uint nextIndex = 0;
            for (int layoutIndex = 0;
                 layoutIndex < layouts.Length;
                 ++layoutIndex)
            {
                MetalBindingTableLayout layout =
                    layouts[layoutIndex]
                    ?? throw new ArgumentException(
                        $"Metal pipeline layout slot {layoutIndex} is null.",
                        nameof(layouts));
                if (layout.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        $"MetalBindingTableLayout[{layout.Index}]");
                }

                ReadOnlySpan<MetalBindInfo> bindings =
                    layout.BindInfos;
                for (int bindingIndex = 0;
                     bindingIndex < bindings.Length;
                     ++bindingIndex)
                {
                    ref readonly MetalBindInfo binding =
                        ref bindings[bindingIndex];
                    if (MetalBindingTableValidation
                            .GetDirectBindingNamespace(
                                binding.Type) !=
                        MetalBindingTableValidation
                            .BindingNamespace.Texture)
                    {
                        continue;
                    }

                    nextIndex = Math.Max(
                        nextIndex,
                        checked(binding.Slot + binding.Count));
                }
            }
            return nextIndex;
        }

        internal static void ValidatePipelineLayoutIdentity(
            bool isDisposed,
            object? actualDevice,
            object expectedDevice)
        {
            ArgumentNullException.ThrowIfNull(expectedDevice);
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(MetalPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Metal pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }

        private static int HighestSetBit(byte mask)
        {
            for (int bit = 7; bit >= 0; --bit)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    return bit;
                }
            }
            throw new ArgumentOutOfRangeException(
                nameof(mask),
                mask,
                "A non-empty mask is required.");
        }
    }
    #endregion

    #region MachineLearning
internal sealed class MetalMLProgram : RHIMLProgram
    {
        internal MTLLibrary NativeLibrary => m_NativeLibrary;
        internal string EntryName { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }

        private MTLLibrary m_NativeLibrary;
        private readonly string? m_OwnedPackageDirectory;

        internal const string DefaultEntryName = "main";

        internal MetalMLProgram(
            string name,
            MTLLibrary nativeLibrary,
            string entryName,
            RHIMLTensorBindingInfo[] bindingInfos,
            string? ownedPackageDirectory = null)
        {
            m_Name = name;
            if (nativeLibrary.NativePtr == IntPtr.Zero)
            {
                throw new ArgumentException("Metal ML program requires a native MTLLibrary.", nameof(nativeLibrary));
            }

            m_NativeLibrary = nativeLibrary;
            EntryName = string.IsNullOrWhiteSpace(entryName) ? DefaultEntryName : entryName;
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
            m_OwnedPackageDirectory = ownedPackageDirectory;
        }

        protected override void Release()
        {
            m_NativeLibrary = default;
            if (!string.IsNullOrWhiteSpace(m_OwnedPackageDirectory))
            {
                try
                {
                    if (Directory.Exists(m_OwnedPackageDirectory))
                    {
                        Directory.Delete(m_OwnedPackageDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup of extracted MetalPackageV1 contents.
                }
            }
        }
    }

    internal sealed class MetalMLBindingTable : RHIMLBindingTable
    {
        internal MetalMLPipeline PipelineTyped => (MetalMLPipeline)(m_Pipeline ?? throw new InvalidOperationException("Metal ML binding table pipeline is unavailable."));
        internal MetalTensor[] Inputs { get; }
        internal MetalTensor[] Outputs { get; }
        internal MetalTensorView[]? InputViews { get; }
        internal MetalTensorView[]? OutputViews { get; }
        internal MTL4ArgumentTable NativeArgumentTable => m_NativeArgumentTable;
        internal MTLHeap NativeIntermediatesHeap => m_IntermediatesHeap?.NativeHeap ?? default;

        private MTL4ArgumentTable m_NativeArgumentTable;
        private MetalHeap? m_IntermediatesHeap;

        internal MetalMLBindingTable(MetalDevice device, in RHIMLBindingTableDescriptor descriptor)
        {
            if (descriptor.Pipeline is not MetalMLPipeline metalPipeline)
            {
                throw new InvalidOperationException($"Metal ML binding table requires a {nameof(MetalMLPipeline)}.");
            }

            m_Pipeline = metalPipeline;
            (Inputs, InputViews) = ResolveBindings(
                descriptor.Inputs.Span,
                descriptor.InputViews.Span,
                ERHIMLTensorBindingKind.Input,
                metalPipeline.InputCount);
            (Outputs, OutputViews) = ResolveBindings(
                descriptor.Outputs.Span,
                descriptor.OutputViews.Span,
                ERHIMLTensorBindingKind.Output,
                metalPipeline.OutputCount);

            MTL4ArgumentTableDescriptor bindingTableDescriptor = MTL4ArgumentTableDescriptor.New();
            bindingTableDescriptor.MaxBufferBindCount = Math.Max(1UL, metalPipeline.NativeArgumentTableBufferBindCount);
            bindingTableDescriptor.InitializeBindings = true;
            SharpMetal.Foundation.NSError error = default;
            m_NativeArgumentTable = device.NativeDevice.NewArgumentTable(bindingTableDescriptor, ref error);
            SharpMetal.ObjectiveCCore.ObjectiveCRuntime.Release(bindingTableDescriptor);

            if (m_NativeArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTL4ArgumentTable for ML binding table: {errorText}");
            }

            device.RegisterMetalMLNativeArgumentTable(m_NativeArgumentTable);

            // WWDC25 / Apple docs: MTLHeapTypePlacement with size >= pipeline.intermediatesHeapSize.
            // Do not force ResourceOptions on this heap ? MetalHeap.CreateMachineLearningIntermediates
            // mirrors Apple's minimal descriptor (type + size only).
            ulong intermediatesHeapSize = Math.Max(metalPipeline.TemporaryResourceSize, 1UL);
            m_IntermediatesHeap = MetalHeap.CreateMachineLearningIntermediates(device, intermediatesHeapSize);
            device.RegisterMetalMLIntermediatesHeap(m_IntermediatesHeap);

            PopulateNativeArgumentTable(device, metalPipeline);
        }

        private static (MetalTensor[] Tensors, MetalTensorView[]? Views) ResolveBindings(
            ReadOnlySpan<RHITensor> tensors,
            ReadOnlySpan<RHITensorView> views,
            ERHIMLTensorBindingKind kind,
            uint expectedCount)
        {
            if (views.Length > 0)
            {
                if (tensors.Length > 0)
                {
                    throw new ArgumentException(
                        $"Metal ML {kind} bindings must provide either tensors or tensor views, not both.");
                }

                if (views.Length != expectedCount)
                {
                    throw new InvalidOperationException(
                        $"Metal ML binding count mismatch for {kind}. expected={expectedCount}, actual={views.Length}.");
                }

                MetalTensorView[] resolvedViews = new MetalTensorView[views.Length];
                for (int i = 0; i < views.Length; ++i)
                {
                    resolvedViews[i] = views[i] as MetalTensorView
                        ?? throw new InvalidOperationException($"Metal ML binding tensor view[{i}] must be a {nameof(MetalTensorView)}.");
                }

                return (Array.Empty<MetalTensor>(), resolvedViews);
            }

            return (ConvertTensors(tensors, kind, expectedCount), null);
        }

        private static MetalTensor[] ConvertTensors(ReadOnlySpan<RHITensor> tensors, ERHIMLTensorBindingKind kind, uint expectedCount)
        {
            if (tensors.Length != expectedCount)
            {
                throw new InvalidOperationException($"Metal ML binding count mismatch for {kind}. expected={expectedCount}, actual={tensors.Length}.");
            }

            MetalTensor[] result = new MetalTensor[tensors.Length];
            for (int i = 0; i < tensors.Length; ++i)
            {
                result[i] = tensors[i] as MetalTensor
                    ?? throw new InvalidOperationException($"Metal ML binding tensor[{i}] must be a {nameof(MetalTensor)}.");
            }

            return result;
        }

        private void PopulateNativeArgumentTable(MetalDevice device, MetalMLPipeline pipeline)
        {
            _ = device;
            if (pipeline.ReflectionBindingCount != 0UL && pipeline.ReflectionBindingCount != (ulong)pipeline.BindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML reflection binding count mismatch. reflection={pipeline.ReflectionBindingCount}, expected={pipeline.BindingInfos.Length}.");
            }

            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos = pipeline.BindingInfos.Span;
            ReadOnlySpan<ulong> bindingSlots = pipeline.NativeBindingSlots.Span;
            if (bindingSlots.Length != bindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML native binding slot count mismatch. slots={bindingSlots.Length}, bindings={bindingInfos.Length}.");
            }

            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                RHIMLTensorDescriptor actualDescriptor;
                MTLTensor nativeTensor;
                if (bindingInfo.Kind == ERHIMLTensorBindingKind.Input && InputViews != null)
                {
                    MetalTensorView view = InputViews[(int)bindingInfo.Index];
                    actualDescriptor = view.Descriptor;
                    nativeTensor = view.NativeTensor;
                }
                else if (bindingInfo.Kind == ERHIMLTensorBindingKind.Output && OutputViews != null)
                {
                    MetalTensorView view = OutputViews[(int)bindingInfo.Index];
                    actualDescriptor = view.Descriptor;
                    nativeTensor = view.NativeTensor;
                }
                else
                {
                    MetalTensor tensor = bindingInfo.Kind switch
                    {
                        ERHIMLTensorBindingKind.Input => Inputs[(int)bindingInfo.Index],
                        ERHIMLTensorBindingKind.Output => Outputs[(int)bindingInfo.Index],
                        _ => throw new InvalidOperationException($"Unsupported Metal ML tensor binding kind '{bindingInfo.Kind}'."),
                    };
                    actualDescriptor = tensor.Descriptor;
                    nativeTensor = tensor.NativeTensor;
                }

                if (!RHIMLHelpers.HasCompatibleLayout(bindingInfo.Descriptor, actualDescriptor))
                {
                    throw new InvalidOperationException(
                        $"Metal ML tensor layout mismatch for '{bindingInfo.Name}'. expected={RHIMLHelpers.DescribeLayout(bindingInfo.Descriptor)}, actual={RHIMLHelpers.DescribeLayout(actualDescriptor)}.");
                }

                ulong bindingSlot = bindingSlots[i];
                m_NativeArgumentTable.SetResource(nativeTensor.GpuResourceID, bindingSlot);
            }
        }

        protected override void Release()
        {
            m_NativeArgumentTable = default;
            // Keep the managed heap alive until device teardown; MetalDevice retains the
            // native MTLHeap. Dropping + GC-finalizing here raced with later ML encodes.
            m_IntermediatesHeap = null;
        }

    }

    internal class MetalMLPipeline : RHIMLPipeline
    {
        internal MTL4MachineLearningPipelineState NativePipelineState => m_NativePipelineState;
        internal MetalMLProgram Program => m_Program;
        internal ulong ReflectionBindingCount => m_ReflectionBindingCount;
        internal ReadOnlyMemory<ulong> NativeBindingSlots => m_NativeBindingSlots;
        internal ulong NativeArgumentTableBufferBindCount => m_NativeArgumentTableBufferBindCount;

        private MTL4MachineLearningPipelineState m_NativePipelineState;
        private readonly MetalMLProgram m_Program;
        private readonly ulong[] m_NativeBindingSlots;
        private readonly ulong m_NativeArgumentTableBufferBindCount;
        private ulong m_ReflectionBindingCount;

        public MetalMLPipeline(MetalDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_Program = MetalMlBinaryCodec.LoadProgram(device, descriptor.Binary, descriptor.Name);
            m_BindingInfos = m_Program.BindingInfos;

            if (!device.SupportsMetal4)
            {
                throw new NotSupportedException(
                    "MetalMLPipeline requires Metal 4 (MTL4MachineLearningPipelineState). " +
                    "The current device does not support Metal 4.");
            }

            // ?? Create MTL4Compiler ??
            NSError compilerError = default;
            MTL4CompilerDescriptor compilerDesc = MTL4CompilerDescriptor.New();
            MTL4Compiler compiler = device.NativeDevice.NewCompiler(compilerDesc, ref compilerError);
            ObjectiveCRuntime.Release(compilerDesc);

            if (compiler.NativePtr == IntPtr.Zero)
            {
                string errorText = compilerError.NativePtr != IntPtr.Zero
                    ? compilerError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4Compiler - {errorText}");
            }

            // ?? Build MTL4LibraryFunctionDescriptor from RHIFunction ??
            MTL4LibraryFunctionDescriptor funcDesc = MTL4LibraryFunctionDescriptor.New();
            funcDesc.Library = m_Program.NativeLibrary;
            funcDesc.Name = new NSString(m_Program.EntryName);

            // ?? Build ML pipeline descriptor ??
            MTL4MachineLearningPipelineDescriptor mlDesc = MTL4MachineLearningPipelineDescriptor.New();
            mlDesc.MachineLearningFunctionDescriptor = funcDesc;
            MTL4PipelineOptions pipelineOptions = MTL4PipelineOptions.New();
            pipelineOptions.ShaderReflection = MTL4ShaderReflection.BindingInfo;
            MTL4PipelineDescriptor pipelineDescriptor = mlDesc;
            pipelineDescriptor.Options = pipelineOptions;
            ObjectiveCRuntime.Release(pipelineOptions.NativePtr);
            if (!string.IsNullOrEmpty(descriptor.Name))
            {
                mlDesc.Label = new NSString(descriptor.Name);
            }

            // Static MTLPackage networks embed extents; WWDC notes setInputDimensions only for
            // dynamic inputs. Skip here so logical binding indices cannot disagree with native slots.
            ObjectiveCRuntime.Release(funcDesc);

            // ?? Create pipeline state ??
            NSError pipelineError = default;
            m_NativePipelineState = compiler.NewMachineLearningPipelineState(mlDesc, ref pipelineError);
            ObjectiveCRuntime.Release(mlDesc);
            ObjectiveCRuntime.Release(compiler);

            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = pipelineError.NativePtr != IntPtr.Zero
                    ? pipelineError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4MachineLearningPipelineState '{descriptor.Name}' - {errorText}");
            }

            device.RegisterMetalMLPipelineState(m_NativePipelineState);
            m_NativeBindingSlots = ResolveNativeBindingSlots();
            m_NativeArgumentTableBufferBindCount = CalculateNativeArgumentTableBufferBindCount(m_NativeBindingSlots);

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            m_TemporaryResourceSize = m_NativePipelineState.IntermediatesHeapSize;
            m_PersistentResourceSize = 0;
        }

        protected override void Release()
        {
            m_NativePipelineState = default;
        }

        private static void ReleaseNativeObject(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            ObjectiveCRuntime.Release(nativePtr);
        }

        private ulong[] ResolveNativeBindingSlots()
        {
            MTL4MachineLearningPipelineReflection reflection = m_NativePipelineState.Reflection;
            if (reflection.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal ML pipeline reflection is unavailable; cannot bind artifact-backed tensor arguments.");
            }

            NSArray reflectionBindings = reflection.Bindings;
            if (reflectionBindings.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal ML pipeline reflection did not expose tensor bindings.");
            }

            m_ReflectionBindingCount = reflectionBindings.Count;
            Dictionary<string, ulong> slotsByName = new Dictionary<string, ulong>(StringComparer.Ordinal);
            List<ulong> tensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<ulong> readOnlyTensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<ulong> writableTensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<string> observedBindings = new List<string>(checked((int)m_ReflectionBindingCount));

            for (ulong i = 0; i < m_ReflectionBindingCount; ++i)
            {
                IntPtr bindingPtr = reflectionBindings.Object(i);
                if (bindingPtr == IntPtr.Zero)
                {
                    continue;
                }

                MTLBinding binding = new MTLBinding(bindingPtr);
                string bindingName = GetBindingName(binding);
                MTLBindingType bindingType = binding.Type;
                MTLBindingAccess bindingAccess = binding.Access;
                ulong bindingIndex = binding.Index;
                observedBindings.Add(string.IsNullOrWhiteSpace(bindingName)
                    ? $"<unnamed>:{bindingType}:{bindingAccess}@{bindingIndex}"
                    : $"{bindingName}:{bindingType}:{bindingAccess}@{bindingIndex}");

                if (bindingType != MTLBindingType.Tensor)
                {
                    continue;
                }

                tensorSlots.Add(bindingIndex);
                if (bindingAccess == MTLBindingAccess.ReadOnly)
                {
                    readOnlyTensorSlots.Add(bindingIndex);
                }
                else
                {
                    writableTensorSlots.Add(bindingIndex);
                }

                if (string.IsNullOrWhiteSpace(bindingName))
                {
                    continue;
                }

                if (!slotsByName.TryAdd(bindingName, bindingIndex))
                {
                    throw new InvalidOperationException($"Metal ML pipeline reflection has duplicate tensor binding name '{bindingName}'.");
                }
            }

            RHIMLTensorBindingInfo[] bindingInfos = m_BindingInfos
                ?? throw new InvalidOperationException("Metal ML pipeline binding metadata is unavailable.");
            if (slotsByName.Count == 0 && tensorSlots.Count == bindingInfos.Length)
            {
                if (TryResolveUnnamedBindingSlotsByAccess(bindingInfos, readOnlyTensorSlots, writableTensorSlots, out ulong[]? accessSlots) && accessSlots != null)
                {
                    return accessSlots;
                }

                return tensorSlots.ToArray();
            }

            if (slotsByName.Count < bindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML pipeline reflection exposed {slotsByName.Count} named tensor binding(s), expected {bindingInfos.Length}. observed=[{string.Join(", ", observedBindings)}].");
            }

            ulong[] slots = new ulong[bindingInfos.Length];
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                if (!slotsByName.TryGetValue(bindingInfo.Name, out ulong slot))
                {
                    throw new InvalidOperationException(
                        $"Metal ML pipeline reflection did not expose expected tensor binding '{bindingInfo.Name}'. observed=[{string.Join(", ", observedBindings)}].");
                }

                slots[i] = slot;
            }

            return slots;
        }

        private static bool TryResolveUnnamedBindingSlotsByAccess(
            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos,
            List<ulong> readOnlyTensorSlots,
            List<ulong> writableTensorSlots,
            out ulong[]? slots)
        {
            slots = null;
            int inputCount = 0;
            int outputCount = 0;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                if (bindingInfos[i].Kind == ERHIMLTensorBindingKind.Input)
                {
                    ++inputCount;
                }
                else if (bindingInfos[i].Kind == ERHIMLTensorBindingKind.Output)
                {
                    ++outputCount;
                }
            }

            if (readOnlyTensorSlots.Count != inputCount || writableTensorSlots.Count != outputCount)
            {
                return false;
            }

            slots = new ulong[bindingInfos.Length];
            int inputCursor = 0;
            int outputCursor = 0;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                slots[i] = bindingInfos[i].Kind switch
                {
                    ERHIMLTensorBindingKind.Input => readOnlyTensorSlots[inputCursor++],
                    ERHIMLTensorBindingKind.Output => writableTensorSlots[outputCursor++],
                    _ => throw new InvalidOperationException($"Unsupported Metal ML tensor binding kind '{bindingInfos[i].Kind}'."),
                };
            }

            return true;
        }

        private static string GetBindingName(in MTLBinding binding)
        {
            NSString name = binding.Name;
            return name.NativePtr == IntPtr.Zero ? string.Empty : name.ToString();
        }

        private static ulong CalculateNativeArgumentTableBufferBindCount(ReadOnlySpan<ulong> bindingSlots)
        {
            ulong maxSlot = 0;
            for (int i = 0; i < bindingSlots.Length; ++i)
            {
                maxSlot = Math.Max(maxSlot, bindingSlots[i]);
            }

            return maxSlot + 1UL;
        }
    }

    #endregion

    #region MLBinaryCodec
    /// <summary>
    /// MetalPackageV1 (.mtlmlbin): magic "MTLM", version 1, reflection sidecar, embedded .mtlpackage zip payload.
    /// </summary>
    internal static class MetalMlBinaryCodec
    {
        internal const uint Magic = 0x4D4C544D; // "MTLM" little-endian
        internal const byte Version = 1;
        internal const int HeaderSize = 13;

        internal static RHIMLBinary Load(ReadOnlyMemory<byte> container)
        {
            RHIMLBinaryReflection reflection = ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.MetalPackageV1,
                container,
                reflection,
                contentHash);
        }

        internal static RHIMLBinary PackFromMtlPackageDirectory(string mtlPackageDirectory, in RHIMLBinaryReflection reflection)
        {
            if (string.IsNullOrWhiteSpace(mtlPackageDirectory) || !Directory.Exists(mtlPackageDirectory))
            {
                throw new DirectoryNotFoundException($"Metal ML package directory not found: {mtlPackageDirectory}");
            }

            byte[] packageZip = ZipDirectoryAsPackageEntry(mtlPackageDirectory);
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(0UL); // placeholder hash
                WriteReflectionBlock(writer, in reflection);
                writer.Write((ulong)packageZip.Length);
                writer.Write(packageZip);
            }

            byte[] container = stream.ToArray();
            ulong contentHash = RHIMLBinaryHash.ComputeContentHash(container.AsSpan(HeaderSize));
            BinaryPrimitives.WriteUInt64LittleEndian(container.AsSpan(5, 8), contentHash);
            RHIMLBinaryReflection storedReflection = reflection;
            if (string.IsNullOrWhiteSpace(storedReflection.EntryName))
            {
                storedReflection.EntryName = MetalMLProgram.DefaultEntryName;
            }

            return new RHIMLBinary(
                ERHIMLBinaryFormat.MetalPackageV1,
                container,
                storedReflection,
                contentHash);
        }

        private static byte[] ZipDirectoryAsPackageEntry(string mtlPackageDirectory)
        {
            string fullPath = Path.GetFullPath(mtlPackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Loader extracts to a fixed "package.mtlpackage" directory name.
            const string packageName = "package.mtlpackage";

            using MemoryStream archiveStream = new MemoryStream();
            using (ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string filePath in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(fullPath, filePath).Replace('\\', '/');
                    if (IsAppleDoubleOrJunkPath(relative))
                    {
                        continue;
                    }

                    ZipArchiveEntry entry = archive.CreateEntry(packageName + "/" + relative, CompressionLevel.Optimal);
                    using Stream entryStream = entry.Open();
                    using FileStream fileStream = File.OpenRead(filePath);
                    fileStream.CopyTo(entryStream);
                }
            }

            return archiveStream.ToArray();
        }

        internal static RHIMLBinaryReflection ReadReflection(ReadOnlyMemory<byte> container)
        {
            ValidateContainerHeader(container.Span, out int reflectionOffset);
            return ReadReflectionBlock(container.Span.Slice(reflectionOffset));
        }

        internal static MetalMLProgram LoadProgram(MetalDevice device, RHIMLBinary binary, string pipelineName)
        {
            if (binary.Format != ERHIMLBinaryFormat.MetalPackageV1)
            {
                throw new InvalidOperationException($"Metal ML binary loader requires {nameof(ERHIMLBinaryFormat.MetalPackageV1)}.");
            }

            ValidateContainerHeader(binary.Payload.Span, out int reflectionOffset);
            RHIMLBinaryReflection reflection = ReadReflectionBlock(binary.Payload.Span.Slice(reflectionOffset));
            int packageOffset = reflectionOffset + MeasureReflectionBlock(reflection);
            ReadOnlySpan<byte> container = binary.Payload.Span;
            if (packageOffset + 8 > container.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 container is missing package length.");
            }

            ulong packageLength = BinaryPrimitives.ReadUInt64LittleEndian(container.Slice(packageOffset, 8));
            int packageBytesOffset = packageOffset + 8;
            if (packageBytesOffset + (long)packageLength > container.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 embedded package bytes are truncated.");
            }

            ReadOnlySpan<byte> packageBytes = container.Slice(packageBytesOffset, checked((int)packageLength));
            string tempRoot = Path.Combine(Path.GetTempPath(), "sharpgpu-mtlmlbin-" + Guid.NewGuid().ToString("N"));
            string packagePath = Path.Combine(tempRoot, "package.mtlpackage");
            Directory.CreateDirectory(tempRoot);
            try
            {
                ExtractPackageArchive(packageBytes, packagePath);
                NSURL packageUrl = NSURL.FileURLWithPath(new NSString(packagePath));
                NSError error = default;
                MTLLibrary library = device.NativeDevice.NewLibrary(packageUrl, ref error);
                if (library.NativePtr == IntPtr.Zero)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to load MTLLibrary from MetalPackageV1 payload: {errorText}");
                }

                // Keep extracted package alive for the MetalMLProgram lifetime: MTLLibrary loaded from
                // URL may still depend on on-disk package contents during pipeline compilation.
                return new MetalMLProgram(
                    pipelineName,
                    library,
                    reflection.EntryName ?? MetalMLProgram.DefaultEntryName,
                    reflection.Bindings ?? Array.Empty<RHIMLTensorBindingInfo>(),
                    tempRoot);
            }
            catch
            {
                TryDeleteDirectory(tempRoot);
                throw;
            }
        }

        private static void ExtractPackageArchive(ReadOnlySpan<byte> packageBytes, string packagePath)
        {
            using MemoryStream archiveStream = new MemoryStream(packageBytes.ToArray(), writable: false);
            using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            string extractRoot = Path.GetDirectoryName(packagePath)
                ?? throw new InvalidOperationException("MetalPackageV1 extract root is unavailable.");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.Combine(extractRoot, entry.FullName);
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            if (!Directory.Exists(packagePath))
            {
                throw new InvalidOperationException(
                    "MetalPackageV1 archive must contain a .mtlpackage directory entry.");
            }
        }

        private static void ValidateContainerHeader(ReadOnlySpan<byte> container, out int reflectionOffset)
        {
            if (container.Length < HeaderSize)
            {
                throw new InvalidOperationException("MetalPackageV1 container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(container.Slice(0, 4));
            if (magic != Magic)
            {
                throw new InvalidOperationException($"MetalPackageV1 magic mismatch. expected=0x{Magic:X8}, actual=0x{magic:X8}.");
            }

            byte version = container[4];
            if (version != Version)
            {
                throw new InvalidOperationException($"MetalPackageV1 version mismatch. expected={Version}, actual={version}.");
            }

            ReadOnlySpan<byte> tail = container.Slice(HeaderSize);
            ulong storedHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Slice(5, 8));
            ulong computedHash = RHIMLBinaryHash.ComputeContentHash(tail);
            if (storedHash != computedHash)
            {
                throw new InvalidOperationException(
                    $"MetalPackageV1 content hash mismatch. expected=0x{storedHash:X16}, actual=0x{computedHash:X16}.");
            }

            reflectionOffset = HeaderSize;
        }

        private static RHIMLBinaryReflection ReadReflectionBlock(ReadOnlySpan<byte> payload)
        {
            int offset = 0;
            string entryName = ReadString(payload, ref offset);
            int bindingCount = ReadInt32(payload, ref offset);
            RHIMLTensorBindingInfo[] bindings = new RHIMLTensorBindingInfo[bindingCount];
            for (int i = 0; i < bindingCount; ++i)
            {
                bindings[i] = ReadBinding(payload, ref offset);
            }

            ulong intermediateHeapSizeHint = ReadUInt64(payload, ref offset);
            return new RHIMLBinaryReflection
            {
                EntryName = entryName,
                Bindings = bindings,
                IntermediateHeapSizeHint = intermediateHeapSizeHint,
            };
        }

        private static int MeasureReflectionBlock(in RHIMLBinaryReflection reflection)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteReflectionBlock(writer, in reflection);
            return checked((int)stream.Length);
        }

        private static void WriteReflectionBlock(BinaryWriter writer, in RHIMLBinaryReflection reflection)
        {
            WriteString(writer, reflection.EntryName ?? MetalMLProgram.DefaultEntryName);
            writer.Write(reflection.Bindings?.Length ?? 0);
            if (reflection.Bindings is { Length: > 0 } bindings)
            {
                for (int i = 0; i < bindings.Length; ++i)
                {
                    WriteBinding(writer, in bindings[i]);
                }
            }

            writer.Write(reflection.IntermediateHeapSizeHint);
        }

        private static void WriteBinding(BinaryWriter writer, in RHIMLTensorBindingInfo binding)
        {
            WriteString(writer, binding.Name ?? string.Empty);
            writer.Write(binding.Index);
            writer.Write((byte)binding.Kind);
            WriteTensorDescriptor(writer, in binding.Descriptor);
        }

        private static RHIMLTensorBindingInfo ReadBinding(ReadOnlySpan<byte> payload, ref int offset)
        {
            return new RHIMLTensorBindingInfo
            {
                Name = ReadString(payload, ref offset),
                Index = ReadUInt32(payload, ref offset),
                Kind = (ERHIMLTensorBindingKind)ReadByte(payload, ref offset),
                Descriptor = ReadTensorDescriptor(payload, ref offset),
            };
        }

        private static void WriteTensorDescriptor(BinaryWriter writer, in RHIMLTensorDescriptor descriptor)
        {
            writer.Write((byte)descriptor.DataType);
            writer.Write((uint)descriptor.UsageFlag);
            writer.Write((byte)descriptor.StorageMode);
            uint[] dimensions = descriptor.Dimensions.ToArray();
            writer.Write(dimensions.Length);
            for (int i = 0; i < dimensions.Length; ++i)
            {
                writer.Write(dimensions[i]);
            }

            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                uint[] strides = explicitStrides.ToArray();
                writer.Write((byte)1);
                writer.Write(strides.Length);
                for (int i = 0; i < strides.Length; ++i)
                {
                    writer.Write(strides[i]);
                }
            }
            else
            {
                writer.Write((byte)0);
            }
        }

        private static RHIMLTensorDescriptor ReadTensorDescriptor(ReadOnlySpan<byte> payload, ref int offset)
        {
            ERHIMLDataType dataType = (ERHIMLDataType)ReadByte(payload, ref offset);
            ERHITensorUsage usage = (ERHITensorUsage)ReadUInt32(payload, ref offset);
            ERHIStorageMode storageMode = (ERHIStorageMode)ReadByte(payload, ref offset);
            int dimensionCount = ReadInt32(payload, ref offset);
            uint[] dimensions = new uint[dimensionCount];
            for (int i = 0; i < dimensionCount; ++i)
            {
                dimensions[i] = ReadUInt32(payload, ref offset);
            }

            Memory<uint>? strides = null;
            if (ReadByte(payload, ref offset) != 0)
            {
                int strideCount = ReadInt32(payload, ref offset);
                uint[] strideValues = new uint[strideCount];
                for (int i = 0; i < strideCount; ++i)
                {
                    strideValues[i] = ReadUInt32(payload, ref offset);
                }

                strides = strideValues;
            }

            return new RHIMLTensorDescriptor
            {
                DataType = dataType,
                UsageFlag = usage,
                StorageMode = storageMode,
                Dimensions = dimensions,
                Strides = strides,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
        {
            int length = ReadInt32(payload, ref offset);
            if (length < 0 || offset + length > payload.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 string length is out of range.");
            }

            string value = Encoding.UTF8.GetString(payload.Slice(offset, length));
            offset += length;
            return value;
        }

        private static byte ReadByte(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 1);
            byte value = payload[offset];
            ++offset;
            return value;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            // Preserve signed lengths/counts; do not round-trip via checked uint cast.
            EnsureRemaining(payload, offset, 4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static ulong ReadUInt64(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(offset, 8));
            offset += 8;
            return value;
        }

        private static void EnsureRemaining(ReadOnlySpan<byte> payload, int offset, int required)
        {
            if (offset + required > payload.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 payload is truncated.");
            }
        }

        private static bool IsAppleDoubleOrJunkPath(string relativePath)
        {
            string[] parts = relativePath.Split('/', '\\');
            for (int i = 0; i < parts.Length; ++i)
            {
                string part = parts[i];
                if (part.StartsWith("._", StringComparison.Ordinal)
                    || string.Equals(part, ".DS_Store", StringComparison.Ordinal)
                    || string.Equals(part, "__MACOSX", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
    #endregion

}
