using System;
using SharpGPU.Core;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SharpGPU
{
    public class RHIDeviceLimit
    {
        public readonly int UniformBufferAlignment;
        public readonly int UploadBufferAlignment;
        public readonly int UploadBufferTextureAlignment;
        public readonly int UploadBufferTextureRowAlignment;
        public readonly int MaxMSAACount;
        public readonly int MaxBoundTexture;
        public readonly int MinWavefrontSize;
        public readonly int MaxWavefrontSize;
        public readonly int MaxComputeThreads;
        public readonly int MaxGroupShareMemorySize;
        public readonly int MaxVertexInputBindings;
        public readonly int MaxColorAttachments;
        public readonly int MaxTexture2DSize;
        public readonly int MaxTextureCubeSize;

        internal RHIDeviceLimit(in int uniformBufferAlignment,
                                in int uploadBufferAlignment,
                                in int uploadBufferTextureAlignment,
                                in int uploadBufferTextureRowAlignment, 
                                in int maxMSAACount,
                                in int maxBoundTexture,
                                in int minWavefrontSize,
                                in int maxWavefrontSize,
                                in int maxComputeThreads,
                                in int maxGroupShareMemorySize,
                                in int maxVertexInputBindings,
                                in int maxColorAttachments,
                                in int maxTexture2DSize,
                                in int maxTextureCubeSize)
        {
            UniformBufferAlignment = uniformBufferAlignment;
            UploadBufferAlignment = uploadBufferAlignment;
            UploadBufferTextureAlignment = uploadBufferTextureAlignment;
            UploadBufferTextureRowAlignment = uploadBufferTextureRowAlignment;
            MaxMSAACount = maxMSAACount;
            MaxBoundTexture = maxBoundTexture;
            MinWavefrontSize = minWavefrontSize;
            MaxWavefrontSize = maxWavefrontSize;
            MaxComputeThreads = maxComputeThreads;
            MaxGroupShareMemorySize = maxGroupShareMemorySize;
            MaxVertexInputBindings = maxVertexInputBindings;
            MaxColorAttachments = maxColorAttachments;
            MaxTexture2DSize = maxTexture2DSize;
            MaxTextureCubeSize = maxTextureCubeSize;
        }
    }

    public struct RHIVendorId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
        public ERHIVendorType StringValue => (ERHIVendorType)IntValue;
    }

    public struct RHIDeviceId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
    }

    public abstract class RHIDevice : Disposal
    {
        public string? Name => m_Name;
        public RHIVendorId VendorId => m_VendorId;
        public RHIDeviceId DeviceId => m_DeviceId;
        public string DriverVersion => m_DriverVersion;
        public ERHIDeviceType Type => m_Type;
        /// <summary>
        /// The backend kind this device belongs to (DirectX12 / Metal / Vulkan). Exposed on the
        /// abstract HAL so higher layers (e.g. SharpNeural's GPU backend) can branch on the active
        /// backend without taking a compile-time dependency on backend-specific types or performing
        /// runtime type checks. This is a HAL-level property, not part of the ML execution-layer
        /// surface frozen by ADR-0019.
        /// </summary>
        public abstract ERHIBackend BackendType { get; }
        public RHIDeviceLimit? Limit => m_Limit;
        public RHIDeviceCapabilities Capabilities => m_Capabilities;
        public int ComputeQueueCount => m_ComputeQueueCount;
        public int TransferQueueCount => m_TransferQueueCount;
        public int GraphicsQueueCount => m_GraphicsQueueCount;
        public ERHIDeviceState State =>
            (ERHIDeviceState)Volatile.Read(ref m_DeviceState);
        public RHIException? DeviceLossDiagnostic =>
            Volatile.Read(ref m_DeviceLossDiagnostic);

        protected string? m_Name;
        protected RHIVendorId m_VendorId;
        protected RHIDeviceId m_DeviceId;
        protected string m_DriverVersion = "Unknown";
        protected ERHIDeviceType m_Type;
        protected RHIDeviceLimit? m_Limit;
        protected RHIDeviceCapabilities m_Capabilities = RHIDeviceCapabilities.CreateUnprobed("RHIDevice subclass default");
        protected int m_ComputeQueueCount;
        protected int m_TransferQueueCount;
        protected int m_GraphicsQueueCount;
        protected Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>? m_CommandQueueMap;
        private readonly object m_DeviceStateLock = new();
        private int m_DeviceState =
            (int)ERHIDeviceState.Operational;
        private RHIException? m_DeviceLossDiagnostic;

        internal RHIException MarkDeviceLost(
            RHIException diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            if (diagnostic.ErrorCode != ERHIErrorCode.DeviceLost)
            {
                throw new ArgumentException(
                    "A device-loss transition requires a DeviceLost diagnostic.",
                    nameof(diagnostic));
            }
            if (diagnostic.Backend != BackendType)
            {
                throw new ArgumentException(
                    "The device-loss diagnostic belongs to a different backend.",
                    nameof(diagnostic));
            }
            if (diagnostic.DeviceState is
                ERHIDeviceState.Unknown or
                ERHIDeviceState.Operational)
            {
                throw new ArgumentException(
                    "A device-loss diagnostic must carry Lost, Removed, or Reset state.",
                    nameof(diagnostic));
            }

            lock (m_DeviceStateLock)
            {
                if ((ERHIDeviceState)m_DeviceState !=
                    ERHIDeviceState.Operational)
                {
                    return m_DeviceLossDiagnostic ??
                        throw new InvalidOperationException(
                            "The device is unavailable without a loss diagnostic.");
                }

                m_DeviceLossDiagnostic = diagnostic;
                Volatile.Write(
                    ref m_DeviceState,
                    (int)diagnostic.DeviceState);
                return diagnostic;
            }
        }

        internal void ThrowIfDeviceUnavailable()
        {
            ThrowIfDisposed();
            ERHIDeviceState state = State;
            if (state == ERHIDeviceState.Operational)
            {
                return;
            }

            RHIException? diagnostic = DeviceLossDiagnostic;
            if (diagnostic == null)
            {
                throw new InvalidOperationException(
                    $"The {BackendType} device entered '{state}' without a diagnostic.");
            }

            throw diagnostic;
        }

        public abstract RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index);
        public abstract RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor);
        public abstract RHIFence CreateFence();
        public abstract RHISemaphore CreateSemaphore();
        public abstract RHIStorageQueue CreateStorageQueue();
        public abstract RHIQuery CreateQuery(in RHIQueryDescriptor descriptor);
        public abstract RHIHeap CreateHeap(in RHIHeapDescription descriptor);
        public virtual RHIResourceMemoryRequirements GetBufferMemoryRequirements(in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose exact buffer memory requirements.");
        }
        public virtual RHIResourceMemoryRequirements GetTextureMemoryRequirements(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose exact texture memory requirements.");
        }
        public abstract RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor);
        public virtual RHIBuffer CreatePlacedBuffer(
            RHIHeap heap,
            ulong heapOffset,
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose placed buffers.");
        }
        public abstract RHITexture CreateTexture(in RHITextureDescriptor descriptor);
        public virtual RHITexture CreatePlacedTexture(
            RHIHeap heap,
            ulong heapOffset,
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose placed textures.");
        }
        public virtual RHISparseTextureMemoryRequirements GetSparseTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose sparse texture requirements.");
        }
        public virtual RHITexture CreateSparseTexture(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose sparse textures.");
        }
        public virtual RHIMemoryBudget QueryMemoryBudget(ERHIStorageMode storageMode)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose an exact native memory budget.");
        }
        public virtual void RequestResidency(in RHIResidencyRequestDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose residency requests with explicit completion.");
        }
        public abstract RHISampler CreateSampler(in RHISamplerDescriptor descriptor);
        public abstract RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
        public abstract RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor);
        public abstract RHIArgumentTableLayout CreateArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor);
        public abstract RHIArgumentTable CreateArgumentTable(in RHIArgumentTableDescriptor descriptor);
        public abstract RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor);
        public abstract RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor);
        public abstract RHIFunctionLibrary CreateFunctionLibrary(in RHIFunctionLibraryDescriptor descriptor);
        public abstract RHIFunctionTable CreateFunctionTable();
        public abstract RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor);
        public abstract RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor);
        public abstract RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor);
        public abstract RHIPipelineCache CreatePipelineCache();
        public abstract RHIComputeIndirectCommandBuffer CreateComputeIndirectCommandBuffer(in RHIComputeIndirectCommandBufferDescription descriptor);
        public abstract RHIRayTracingIndirectCommandBuffer CreateRayTracingIndirectCommandBuffer(in RHIRayTracingIndirectCommandBufferDescription descriptor);
        public abstract RHIRasterIndirectCommandBuffer CreateRasterIndirectCommandBuffer(in RHIRasterIndirectCommandBufferDescription descriptor);
        public abstract RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor);
        public abstract RHIMLBindingSet CreateMLBindingSet(in RHIMLBindingSetDescriptor descriptor);
        public abstract RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor);
        public abstract RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor);

        /// <summary>
        /// Builds an <see cref="RHIMLProgram"/> from a backend-neutral program descriptor (an ordered
        /// op sequence). This is the constructible counterpart to the existing
        /// <see cref="CreateMLPipeline"/>/<see cref="CreateMLBindingSet"/>/<see cref="CreateTensor"/>
        /// execution surface: it lets a graph builder above the RHI lower a subgraph without taking a
        /// hard compile-time dependency on any backend's private operator-description types. Backends
        /// translate each <see cref="RHIMLOpDescriptor"/> into their native operator description
        /// (DirectML on DX12, Metal 4 ML on Metal). See RFC-0003 §3.3 / ADR-0028.
        /// </summary>
        public abstract RHIMLProgram CreateMLProgram(in RHIMLProgramDescriptor descriptor);

        public virtual bool TryToggleGpuCapture(string savedPath, string reason)
        {
            return false;
        }
    }
}
