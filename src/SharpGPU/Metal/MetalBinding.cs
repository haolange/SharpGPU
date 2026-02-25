using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal enum MetalBindingMode : byte
    {
        ArgumentBuffer = 0,
        ArgumentTable = 1
    }

    internal enum MetalBindingPipelineType : byte
    {
        Compute = 0,
        Raytracing = 1,
        Raster = 2
    }

    internal readonly struct MetalBindingCapabilities
    {
        internal bool SupportsMetal3 { get; }
        internal bool SupportsMetal4 { get; }
        internal bool SupportsArgumentBuffer { get; }
        internal bool SupportsArgumentTable { get; }

        internal MetalBindingCapabilities(bool supportsMetal3, bool supportsMetal4, bool supportsArgumentBuffer, bool supportsArgumentTable)
        {
            SupportsMetal3 = supportsMetal3;
            SupportsMetal4 = supportsMetal4;
            SupportsArgumentBuffer = supportsArgumentBuffer;
            SupportsArgumentTable = supportsArgumentTable;
        }
    }

    internal static class MetalBindingPolicyResolver
    {
        private const string BindingModeOverrideEnv = "INFINITY_METAL_BINDING_MODE";

        internal static MetalBindingMode Resolve(in MetalBindingCapabilities capabilities, in int resourceTableLayoutCount)
        {
            if (TryResolveOverride(capabilities, out MetalBindingMode overrideMode))
            {
                return overrideMode;
            }

            if (capabilities.SupportsMetal4 && capabilities.SupportsArgumentTable)
            {
                return MetalBindingMode.ArgumentTable;
            }

            return MetalBindingMode.ArgumentBuffer;
        }

        private static bool TryResolveOverride(in MetalBindingCapabilities capabilities, out MetalBindingMode mode)
        {
            mode = default;
            string? rawValue = Environment.GetEnvironmentVariable(BindingModeOverrideEnv);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            string value = rawValue.Trim().ToLowerInvariant();
            if (value == "auto")
            {
                return false;
            }

            MetalBindingMode requestedMode = value switch
            {
                "argument_buffer" => MetalBindingMode.ArgumentBuffer,
                "argumentbuffer" => MetalBindingMode.ArgumentBuffer,
                "argument_table" => MetalBindingMode.ArgumentTable,
                "argumenttable" => MetalBindingMode.ArgumentTable,
                _ => throw new InvalidOperationException($"Unknown Metal binding mode override '{rawValue}'. Supported: argument_buffer, argument_table.")
            };

            if (!IsModeSupported(requestedMode, capabilities))
            {
                throw new InvalidOperationException($"Metal binding mode override '{rawValue}' is not supported on this device.");
            }

            mode = requestedMode;
            return true;
        }

        private static bool IsModeSupported(in MetalBindingMode mode, in MetalBindingCapabilities capabilities)
        {
            return mode switch
            {
                MetalBindingMode.ArgumentBuffer => capabilities.SupportsArgumentBuffer,
                MetalBindingMode.ArgumentTable => capabilities.SupportsArgumentTable,
                _ => false
            };
        }
    }

    internal interface IMetalBindingBackend : IDisposable
    {
        MetalBindingMode Mode { get; }
        bool UsesReservedRayFunctionTableSlots { get; }

        void ResetForPipeline(MetalPipelineLayout pipelineLayout);
        void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex);
        void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride);

        void CommitCompute(in MTLComputeCommandEncoder encoder);
        void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        void CommitRaster(in MTLRenderCommandEncoder encoder);
        void CommitRaster(in MTL4RenderCommandEncoder encoder);
    }

    internal static class MetalBindingBackendFactory
    {
        internal static IMetalBindingBackend Create(MetalDevice device, in MetalBindingMode mode, in MetalBindingPipelineType pipelineType, MetalCommandQueue? commandQueue = null)
        {
            return mode switch
            {
                MetalBindingMode.ArgumentBuffer => new MetalArgumentBufferBindingBackend(device, pipelineType),
                MetalBindingMode.ArgumentTable => new MetalArgumentTableBindingBackend(device, pipelineType, commandQueue),
                _ => throw new NotSupportedException($"Unsupported Metal binding mode '{mode}'.")
            };
        }
    }

    internal static class MetalBindingHelpers
    {
        internal const ulong RtVisibleFunctionTableSlot = 29;
        internal const ulong RtIntersectionFunctionTableSlot = 30;
        internal const ulong PushConstantBufferIndex = 31;

        internal static bool IsBufferBindingType(in ERHIBindType type)
        {
            switch (type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.AccelStruct:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsTextureBindingType(in ERHIBindType type)
        {
            switch (type)
            {
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsSamplerBindingType(in ERHIBindType type)
        {
            return type == ERHIBindType.Sampler;
        }

        internal static MTLTextureType ConvertBindTypeToTextureType(in ERHIBindType type)
        {
            return type switch
            {
                ERHIBindType.Texture2D => MTLTextureType.Type2D,
                ERHIBindType.StorageTexture2D => MTLTextureType.Type2D,
                ERHIBindType.Texture2DMS => MTLTextureType.Type2DMultisample,
                ERHIBindType.StorageTexture2DMS => MTLTextureType.Type2DMultisample,
                ERHIBindType.Texture2DArray => MTLTextureType.Type2DArray,
                ERHIBindType.StorageTexture2DArray => MTLTextureType.Type2DArray,
                ERHIBindType.Texture2DArrayMS => MTLTextureType.Type2DMultisampleArray,
                ERHIBindType.StorageTexture2DArrayMS => MTLTextureType.Type2DMultisampleArray,
                ERHIBindType.TextureCube => MTLTextureType.Cube,
                ERHIBindType.StorageTextureCube => MTLTextureType.Cube,
                ERHIBindType.TextureCubeArray => MTLTextureType.CubeArray,
                ERHIBindType.StorageTextureCubeArray => MTLTextureType.CubeArray,
                ERHIBindType.Texture3D => MTLTextureType.Type3D,
                ERHIBindType.StorageTexture3D => MTLTextureType.Type3D,
                _ => MTLTextureType.Type2D
            };
        }

        internal static MTLDataType ConvertBindTypeToArgumentDataType(in ERHIBindType type)
        {
            if (IsBufferBindingType(type))
            {
                return type == ERHIBindType.AccelStruct ? MTLDataType.InstanceAccelerationStructure : MTLDataType.Pointer;
            }

            if (IsTextureBindingType(type))
            {
                return MTLDataType.Texture;
            }

            if (IsSamplerBindingType(type))
            {
                return MTLDataType.Sampler;
            }

            throw new NotSupportedException($"Unsupported bind type '{type}' for argument-encoder data type conversion.");
        }

        internal static MTLBindingAccess ConvertBindTypeToBindingAccess(in ERHIBindType type)
        {
            return type switch
            {
                ERHIBindType.StorageBuffer => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2D => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DMS => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DArray => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture2DArrayMS => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTextureCube => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTextureCubeArray => MTLBindingAccess.ReadWrite,
                ERHIBindType.StorageTexture3D => MTLBindingAccess.ReadWrite,
                _ => MTLBindingAccess.ReadOnly
            };
        }

        internal static bool HasRayFunctionTableSlotConflict(MetalArgumentTable table)
        {
            return HasRayFunctionTableSlotConflict(table.ArgumentTableLayout.BindInfos);
        }

        internal static bool HasRayFunctionTableSlotConflict(ReadOnlySpan<MetalBindInfo> binds)
        {
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                if (!IsBufferBindingType(bind.Type))
                {
                    continue;
                }

                if (bind.Slot == RtVisibleFunctionTableSlot || bind.Slot == RtIntersectionFunctionTableSlot)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void BindRayFunctionTables(in MTLComputeCommandEncoder encoder, MetalFunctionTable functionTable)
        {
            if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
            {
                encoder.SetIntersectionFunctionTable(functionTable.IntersectionFunctionTable, RtIntersectionFunctionTableSlot);
            }

            if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                encoder.SetVisibleFunctionTable(functionTable.VisibleFunctionTable, RtVisibleFunctionTableSlot);
            }
        }
    }

    internal abstract class MetalBindingBackendBase : IMetalBindingBackend
    {
        public abstract MetalBindingMode Mode { get; }
        public virtual bool UsesReservedRayFunctionTableSlots => false;

        protected MetalDevice Device => m_Device;
        protected MetalBindingPipelineType PipelineType => m_PipelineType;

        protected readonly SortedDictionary<uint, MetalArgumentTable> m_BoundTables;

        private readonly MetalDevice m_Device;
        private readonly MetalBindingPipelineType m_PipelineType;
        private MetalPipelineLayout? m_PipelineLayout;

        protected MetalBindingBackendBase(MetalDevice device, in MetalBindingPipelineType pipelineType)
        {
            m_Device = device;
            m_PipelineType = pipelineType;
            m_BoundTables = new SortedDictionary<uint, MetalArgumentTable>();
        }

        public virtual void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            m_PipelineLayout = pipelineLayout;
            m_BoundTables.Clear();
        }

        public virtual void SetArgumentTable(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (resourceTable == null)
            {
                throw new ArgumentNullException(nameof(resourceTable));
            }

            MetalArgumentTableLayout layout = resourceTable.ArgumentTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Metal resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            ValidateTableInPipelineLayout(layout);
            ValidateTableArrayBindings(layout, tableIndex);

            m_BoundTables[tableIndex] = resourceTable;
            OnArgumentTableUpdated(resourceTable, tableIndex);
        }

        public abstract void CommitCompute(in MTLComputeCommandEncoder encoder);
        public abstract void CommitCompute(in MTL4ComputeCommandEncoder encoder);
        public abstract void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        public abstract void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable);
        public abstract void CommitRaster(in MTLRenderCommandEncoder encoder);
        public abstract void CommitRaster(in MTL4RenderCommandEncoder encoder);

        public void Dispose()
        {
            DisposeBackend();
        }

        protected virtual void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
        }

        public virtual void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
        }

        protected virtual void DisposeBackend()
        {
        }

        private void ValidateTableInPipelineLayout(MetalArgumentTableLayout layout)
        {
            if (m_PipelineLayout == null)
            {
                return;
            }

            RHIArgumentTableLayout[]? resourceTableLayouts = m_PipelineLayout.Descriptor.ArgumentTableLayouts;
            if (resourceTableLayouts == null || resourceTableLayouts.Length == 0)
            {
                return;
            }

            bool found = false;
            for (int i = 0; i < resourceTableLayouts.Length; ++i)
            {
                if (resourceTableLayouts[i] is MetalArgumentTableLayout metalLayout && metalLayout.Index == layout.Index)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"Resource table index '{layout.Index}' is not part of the current Metal pipeline layout.");
            }
        }

        private static void ValidateTableArrayBindings(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            // Count > 1 (bindless arrays) is now supported across all Metal binding backends.
            // ArgumentBuffer mode uses ArrayLength for argument descriptors.
            // SetBytes mode encodes all array entries into the payload.
            // Legacy mode iterates over array elements directly.
        }
    }

    internal sealed class MetalArgumentBufferBindingBackend : MetalBindingBackendBase
    {
        private sealed class ArgumentBufferState
        {
            internal readonly MetalArgumentTableLayout Layout;
            internal readonly MTLArgumentEncoder Encoder;
            internal MTLBuffer BackingBuffer;

            internal ArgumentBufferState(MetalArgumentTableLayout layout, in MTLArgumentEncoder encoder, in MTLBuffer backingBuffer)
            {
                Layout = layout;
                Encoder = encoder;
                BackingBuffer = backingBuffer;
            }
        }

        private readonly SortedDictionary<uint, ArgumentBufferState> m_TableStates;

        internal MetalArgumentBufferBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType)
            : base(device, pipelineType)
        {

            m_TableStates = new SortedDictionary<uint, ArgumentBufferState>();
        }

        public override MetalBindingMode Mode => MetalBindingMode.ArgumentBuffer;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseTableStates();
        }

        protected override void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            ArgumentBufferState state = GetOrCreateState(resourceTable.ArgumentTableLayout, tableIndex);
            EncodeTableResources(resourceTable, state);
        }

        public override void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, ArgumentBufferState> pair in m_TableStates)
            {
                encoder.SetBuffer(pair.Value.BackingBuffer, 0, pair.Key);
            }
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            throw new InvalidOperationException("Argument-buffer binding backend requires classic Metal encoder path.");
        }

        public override void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            foreach (KeyValuePair<uint, ArgumentBufferState> pair in m_TableStates)
            {
                encoder.SetBuffer(pair.Value.BackingBuffer, 0, pair.Key);
            }

            if (functionTable != null)
            {
                MetalBindingHelpers.BindRayFunctionTables(encoder, functionTable);
            }
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            throw new InvalidOperationException("Argument-buffer binding backend requires classic Metal encoder path.");
        }

        public override void CommitRaster(in MTLRenderCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, ArgumentBufferState> pair in m_TableStates)
            {
                encoder.SetVertexBuffer(pair.Value.BackingBuffer, 0, pair.Key);
                encoder.SetFragmentBuffer(pair.Value.BackingBuffer, 0, pair.Key);
            }
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            throw new InvalidOperationException("Argument-buffer binding backend requires classic Metal encoder path.");
        }

        protected override void DisposeBackend()
        {
            ReleaseTableStates();
        }

        private ArgumentBufferState GetOrCreateState(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_TableStates.TryGetValue(tableIndex, out ArgumentBufferState? cachedState))
            {
                return cachedState;
            }

            MTLArgumentEncoder argumentEncoder = CreateArgumentEncoder(layout);
            MTLBuffer backingBuffer = CreateBackingBuffer(argumentEncoder);
            ArgumentBufferState state = new ArgumentBufferState(layout, argumentEncoder, backingBuffer);
            m_TableStates.Add(tableIndex, state);
            return state;
        }

        private MTLArgumentEncoder CreateArgumentEncoder(MetalArgumentTableLayout layout)
        {
            IntPtr[] descriptorPtrs = new IntPtr[layout.BindInfos.Length];
            for (int i = 0; i < layout.BindInfos.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref layout.BindInfos[i];
                MTLArgumentDescriptor descriptor = MTLArgumentDescriptor.New();
                descriptor.Index = bind.Slot;
                descriptor.ArrayLength = Math.Max(1u, bind.Count);
                descriptor.DataType = MetalBindingHelpers.ConvertBindTypeToArgumentDataType(bind.Type);
                descriptor.Access = MetalBindingHelpers.ConvertBindTypeToBindingAccess(bind.Type);

                if (MetalBindingHelpers.IsTextureBindingType(bind.Type))
                {
                    descriptor.TextureType = MetalBindingHelpers.ConvertBindTypeToTextureType(bind.Type);
                }

                descriptorPtrs[i] = descriptor.NativePtr;
            }

            NSArray descriptorArray = MetalArrayHelper.CreateNSArrayFromPointers(descriptorPtrs);
            MTLArgumentEncoder argumentEncoder = Device.NativeDevice.NewArgumentEncoder(descriptorArray);

            for (int i = 0; i < descriptorPtrs.Length; ++i)
            {
                if (descriptorPtrs[i] != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptorPtrs[i]);
                }
            }

            if (argumentEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal argument encoder for resource table.");
            }

            return argumentEncoder;
        }

        private MTLBuffer CreateBackingBuffer(in MTLArgumentEncoder argumentEncoder)
        {
            ulong alignment = Math.Max(1UL, argumentEncoder.Alignment);
            ulong encodedLength = Math.Max(1UL, argumentEncoder.EncodedLength);
            ulong byteLength = ((encodedLength + alignment - 1UL) / alignment) * alignment;

            MTLBuffer backingBuffer = Device.NativeDevice.NewBuffer(byteLength, MTLResourceOptions.ResourceStorageModeShared);
            if (backingBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to allocate Metal argument-buffer backing store (size={byteLength}).");
            }

            return backingBuffer;
        }

        private static void EncodeTableResources(MetalArgumentTable table, ArgumentBufferState state)
        {
            state.Encoder.SetArgumentBuffer(state.BackingBuffer, 0);

            MetalBindInfo[] binds = table.ArgumentTableLayout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);

                for (int j = 0; j < arrayCount; ++j)
                {
                    RHIArgumentTableElement element = table.GetElement(i, j);
                    ulong encoderIndex = bind.Slot + (ulong)j;

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (element.BufferView is MetalBufferView bufferView)
                            {
                                state.Encoder.SetBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), encoderIndex);
                            }

                            break;

                        case ERHIBindType.Texture2D:
                        case ERHIBindType.Texture2DMS:
                        case ERHIBindType.Texture2DArray:
                        case ERHIBindType.Texture2DArrayMS:
                        case ERHIBindType.TextureCube:
                        case ERHIBindType.TextureCubeArray:
                        case ERHIBindType.Texture3D:
                        case ERHIBindType.StorageTexture2D:
                        case ERHIBindType.StorageTexture2DMS:
                        case ERHIBindType.StorageTexture2DArray:
                        case ERHIBindType.StorageTexture2DArrayMS:
                        case ERHIBindType.StorageTextureCube:
                        case ERHIBindType.StorageTextureCubeArray:
                        case ERHIBindType.StorageTexture3D:
                            if (element.TextureView is MetalTextureView textureView)
                            {
                                state.Encoder.SetTexture(textureView.NativeTexture, encoderIndex);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (element.Sampler is MetalSampler sampler)
                            {
                                state.Encoder.SetSamplerState(sampler.NativeSampler, encoderIndex);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                            {
                                state.Encoder.SetAccelerationStructure(topLevel.NativeAccelerationStructure, encoderIndex);
                            }

                            break;
                    }
                }
            }
        }

        private void ReleaseTableStates()
        {
            foreach (KeyValuePair<uint, ArgumentBufferState> pair in m_TableStates)
            {
                ArgumentBufferState state = pair.Value;

                if (state.BackingBuffer.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(state.BackingBuffer);
                    state.BackingBuffer = default;
                }

                if (state.Encoder.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(state.Encoder);
                }
            }

            m_TableStates.Clear();
        }
    }

    internal sealed class MetalArgumentTableBindingBackend : MetalBindingBackendBase
    {
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_ArgumentTables;
        private readonly SortedDictionary<uint, RasterVertexBinding> m_RasterVertexBindings;
        private readonly MetalCommandQueue? m_CommandQueue;

        private readonly struct RasterVertexBinding
        {
            internal readonly ulong Address;
            internal readonly ulong Stride;

            internal RasterVertexBinding(in ulong address, in ulong stride)
            {
                Address = address;
                Stride = stride;
            }
        }

        internal MetalArgumentTableBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType, MetalCommandQueue? commandQueue = null)
            : base(device, pipelineType)
        {
            m_ArgumentTables = new SortedDictionary<uint, MTL4ArgumentTable>();
            m_RasterVertexBindings = new SortedDictionary<uint, RasterVertexBinding>();
            m_CommandQueue = commandQueue;
        }

        public override MetalBindingMode Mode => MetalBindingMode.ArgumentTable;

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseArgumentTables();
            m_RasterVertexBindings.Clear();
        }

        protected override void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            MTL4ArgumentTable argumentTable = GetOrCreateArgumentTable(resourceTable.ArgumentTableLayout, tableIndex);
            PopulateArgumentTable(argumentTable, resourceTable);
            ApplyRasterVertexBufferBindings(argumentTable);
        }

        public override void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
            if (PipelineType != MetalBindingPipelineType.Raster)
            {
                return;
            }

            m_RasterVertexBindings[slot] = new RasterVertexBinding(address, stride);
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                ApplyRasterVertexBufferBinding(pair.Value, slot, m_RasterVertexBindings[slot]);
            }
        }

        public override void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            ThrowRequiresMtl4Encoder("compute");
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                encoder.SetArgumentTable(pair.Value.NativePtr);
            }
        }

        public override void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            ThrowRequiresMtl4Encoder("raytracing");
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            if (functionTable != null)
            {
                PopulateRayFunctionTables(functionTable);
            }

            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                encoder.SetArgumentTable(pair.Value.NativePtr);
            }
        }

        public override void CommitRaster(in MTLRenderCommandEncoder encoder)
        {
            ThrowRequiresMtl4Encoder("raster");
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            ulong stages = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Vertex) | MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Fragment);
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                ApplyRasterVertexBufferBindings(pair.Value);
                encoder.SetArgumentTable(pair.Value.NativePtr, stages);
            }
        }

        protected override void DisposeBackend()
        {
            ReleaseArgumentTables();
            m_RasterVertexBindings.Clear();
        }

        private MTL4ArgumentTable GetOrCreateArgumentTable(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_ArgumentTables.TryGetValue(tableIndex, out MTL4ArgumentTable cachedTable))
            {
                return cachedTable;
            }

            ulong maxBufferCount = 0;
            ulong maxTextureCount = 0;
            ulong maxSamplerCount = 0;
            MetalBindInfo[] binds = layout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ulong arrayCount = Math.Max(1UL, bind.Count);
                ulong nextIndex = bind.Slot + arrayCount;
                if (MetalBindingHelpers.IsBufferBindingType(bind.Type))
                {
                    maxBufferCount = Math.Max(maxBufferCount, nextIndex);
                }
                else if (MetalBindingHelpers.IsTextureBindingType(bind.Type))
                {
                    maxTextureCount = Math.Max(maxTextureCount, nextIndex);
                }
                else if (MetalBindingHelpers.IsSamplerBindingType(bind.Type))
                {
                    maxSamplerCount = Math.Max(maxSamplerCount, nextIndex);
                }
            }

            if (PipelineType == MetalBindingPipelineType.Raytracing)
            {
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtIntersectionFunctionTableSlot + 1UL);
                maxBufferCount = Math.Max(maxBufferCount, MetalBindingHelpers.RtVisibleFunctionTableSlot + 1UL);
            }

            if (PipelineType == MetalBindingPipelineType.Raster)
            {
                maxBufferCount = Math.Max(maxBufferCount, 31UL);
            }

            MTL4ArgumentTableDescriptor descriptor = MTL4ArgumentTableDescriptor.New();
            descriptor.MaxBufferBindCount = Math.Max(1UL, maxBufferCount);
            descriptor.MaxTextureBindCount = Math.Max(1UL, maxTextureCount);
            descriptor.MaxSamplerStateBindCount = Math.Max(1UL, maxSamplerCount);
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            MTL4ArgumentTable argumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (argumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"newArgumentTableWithDescriptor failed: {errorText}");
            }

            m_ArgumentTables.Add(tableIndex, argumentTable);
            return argumentTable;
        }

        private void PopulateArgumentTable(MTL4ArgumentTable argumentTable, MetalArgumentTable table)
        {
            MetalBindInfo[] binds = table.ArgumentTableLayout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);

                for (int j = 0; j < arrayCount; ++j)
                {
                    RHIArgumentTableElement element = table.GetElement(i, j);
                    ulong slotIndex = bind.Slot + (ulong)j;

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (element.BufferView is MetalBufferView bufferView)
                            {
                                ulong offset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                                argumentTable.SetAddress(bufferView.Buffer.NativeBuffer.GpuAddress + offset, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(bufferView.Buffer.NativeBuffer);
                            }

                            break;

                        case ERHIBindType.Texture2D:
                        case ERHIBindType.Texture2DMS:
                        case ERHIBindType.Texture2DArray:
                        case ERHIBindType.Texture2DArrayMS:
                        case ERHIBindType.TextureCube:
                        case ERHIBindType.TextureCubeArray:
                        case ERHIBindType.Texture3D:
                        case ERHIBindType.StorageTexture2D:
                        case ERHIBindType.StorageTexture2DMS:
                        case ERHIBindType.StorageTexture2DArray:
                        case ERHIBindType.StorageTexture2DArrayMS:
                        case ERHIBindType.StorageTextureCube:
                        case ERHIBindType.StorageTextureCubeArray:
                        case ERHIBindType.StorageTexture3D:
                            if (element.TextureView is MetalTextureView textureView)
                            {
                                argumentTable.SetTexture(textureView.NativeTexture.GpuResourceID, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(textureView.NativeTexture);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (element.Sampler is MetalSampler sampler)
                            {
                                argumentTable.SetSamplerState(sampler.NativeSampler.GpuResourceID, slotIndex);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                            {
                                argumentTable.SetResource(topLevel.NativeAccelerationStructure.GpuResourceID, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(topLevel.NativeAccelerationStructure);
                            }

                            break;
                    }
                }
            }
        }

        private void PopulateRayFunctionTables(MetalFunctionTable functionTable)
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
                {
                    pair.Value.SetResource(functionTable.IntersectionFunctionTable.GpuResourceID, MetalBindingHelpers.RtIntersectionFunctionTableSlot);
                }

                if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
                {
                    pair.Value.SetResource(functionTable.VisibleFunctionTable.GpuResourceID, MetalBindingHelpers.RtVisibleFunctionTableSlot);
                }
            }
        }

        private void ApplyRasterVertexBufferBindings(MTL4ArgumentTable argumentTable)
        {
            foreach (KeyValuePair<uint, RasterVertexBinding> pair in m_RasterVertexBindings)
            {
                ApplyRasterVertexBufferBinding(argumentTable, pair.Key, pair.Value);
            }
        }

        private static void ApplyRasterVertexBufferBinding(MTL4ArgumentTable argumentTable, in uint slot, in RasterVertexBinding binding)
        {
            if (binding.Stride > 0)
            {
                argumentTable.SetAddress(binding.Address, binding.Stride, slot);
            }
            else
            {
                argumentTable.SetAddress(binding.Address, slot);
            }
        }

        private static void ThrowRequiresMtl4Encoder(string pipelineType)
        {
            throw new InvalidOperationException($"Metal argument-table backend requires MTL4 encoder path for {pipelineType} submissions.");
        }

        private void ReleaseArgumentTables()
        {
            foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value.NativePtr);
                }
            }

            m_ArgumentTables.Clear();
        }
    }
}
