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
        Legacy = 0,
        ArgumentBuffer = 1,
        SetBytes = 2,
        ArgumentTable = 3
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
        private const string LegacyCompatibilityEnv = "INFINITY_METAL_BINDING_LEGACY_COMPAT";

        internal static MetalBindingMode Resolve(in MetalBindingCapabilities capabilities, in int resourceTableLayoutCount)
        {
            if (TryResolveOverride(capabilities, out MetalBindingMode overrideMode))
            {
                return overrideMode;
            }

            int tableCount = Math.Max(0, resourceTableLayoutCount);
            if (capabilities.SupportsMetal3)
            {
                return MetalBindingMode.SetBytes;
            }

            if (capabilities.SupportsArgumentBuffer)
            {
                return MetalBindingMode.ArgumentBuffer;
            }

            return MetalBindingMode.Legacy;
        }

        internal static bool IsLegacyCompatibilityEnabled()
        {
            string? value = Environment.GetEnvironmentVariable(LegacyCompatibilityEnv);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return ParseBooleanValue(value.Trim(), fallback: false);
        }

        internal static bool IsSetBytesModeForced()
        {
            string? mode = Environment.GetEnvironmentVariable(BindingModeOverrideEnv);
            if (string.IsNullOrWhiteSpace(mode))
            {
                return false;
            }

            string value = mode.Trim().ToLowerInvariant();
            return value == "set_bytes" || value == "setbytes";
        }

        internal static bool IsStrictSetBytesModeEnabled()
        {
            return !IsLegacyCompatibilityEnabled() && IsSetBytesModeForced();
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
                "legacy" => MetalBindingMode.Legacy,
                "argument_buffer" => MetalBindingMode.ArgumentBuffer,
                "argumentbuffer" => MetalBindingMode.ArgumentBuffer,
                "set_bytes" => MetalBindingMode.SetBytes,
                "setbytes" => MetalBindingMode.SetBytes,
                "argument_table" => MetalBindingMode.ArgumentTable,
                "argumenttable" => MetalBindingMode.ArgumentTable,
                _ => throw new InvalidOperationException($"Unknown Metal binding mode override '{rawValue}'.")
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
                MetalBindingMode.Legacy => true,
                MetalBindingMode.ArgumentBuffer => capabilities.SupportsArgumentBuffer,
                MetalBindingMode.SetBytes => capabilities.SupportsMetal3,
                MetalBindingMode.ArgumentTable => capabilities.SupportsArgumentTable,
                _ => false
            };
        }

        private static bool ParseBooleanValue(string value, bool fallback)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fallback;
        }
    }

    internal interface IMetalBindingBackend : IDisposable
    {
        MetalBindingMode Mode { get; }
        bool UsesReservedRayFunctionTableSlots { get; }

        void ResetForPipeline(MetalPipelineLayout pipelineLayout);
        void SetResourceTable(MetalResourceTable resourceTable, in uint tableIndex);
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
        internal static IMetalBindingBackend Create(MetalDevice device, in MetalBindingMode mode, in MetalBindingPipelineType pipelineType, in bool? legacyCompatibilityOverride = null)
        {
            return mode switch
            {
                MetalBindingMode.Legacy => new MetalLegacyBindingBackend(device, pipelineType),
                MetalBindingMode.ArgumentBuffer => new MetalArgumentBufferBindingBackend(device, pipelineType),
                MetalBindingMode.SetBytes => new MetalSetBytesBindingBackend(device, pipelineType, legacyCompatibilityOverride),
                MetalBindingMode.ArgumentTable => new MetalArgumentTableBindingBackend(device, pipelineType),
                _ => throw new NotSupportedException($"Unsupported Metal binding mode '{mode}'.")
            };
        }
    }

    internal static class MetalBindingHelpers
    {
        internal const ulong RtVisibleFunctionTableSlot = 29;
        internal const ulong RtIntersectionFunctionTableSlot = 30;

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

        internal static void BindComputeElement(in MTLComputeCommandEncoder encoder, in MetalBindInfo bind, in RHIResourceTableElement element)
        {
            switch (bind.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        encoder.SetBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
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
                        encoder.SetTexture(textureView.NativeTexture, bind.Slot);
                    }

                    break;

                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        encoder.SetSamplerState(sampler.NativeSampler, bind.Slot);
                    }

                    break;

                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                    {
                        encoder.SetAccelerationStructure(topLevel.NativeAccelerationStructure, bind.Slot);
                    }

                    break;
            }
        }

        internal static void BindRasterElement(in MTLRenderCommandEncoder encoder, in MetalBindInfo bind, in RHIResourceTableElement element)
        {
            bool bindVertex = (bind.Stage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex || (bind.Stage & ERHIShaderStage.All) == ERHIShaderStage.All;
            bool bindFragment = (bind.Stage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment || (bind.Stage & ERHIShaderStage.All) == ERHIShaderStage.All;

            switch (bind.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        if (bindVertex)
                        {
                            encoder.SetVertexBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                        }

                        if (bindFragment)
                        {
                            encoder.SetFragmentBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                        }
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
                        if (bindVertex)
                        {
                            encoder.SetVertexTexture(textureView.NativeTexture, bind.Slot);
                        }

                        if (bindFragment)
                        {
                            encoder.SetFragmentTexture(textureView.NativeTexture, bind.Slot);
                        }
                    }

                    break;

                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        if (bindVertex)
                        {
                            encoder.SetVertexSamplerState(sampler.NativeSampler, bind.Slot);
                        }

                        if (bindFragment)
                        {
                            encoder.SetFragmentSamplerState(sampler.NativeSampler, bind.Slot);
                        }
                    }

                    break;

                case ERHIBindType.AccelStruct:
                    throw new NotSupportedException("Raster encoder does not support acceleration-structure bindings on Metal backend.");
            }
        }

        internal static bool HasRayFunctionTableSlotConflict(MetalResourceTable table)
        {
            return HasRayFunctionTableSlotConflict(table.ResourceTableLayout.BindInfos);
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
        protected bool LegacyCompatibilityEnabled => m_LegacyCompatibilityEnabled;

        protected readonly SortedDictionary<uint, MetalResourceTable> m_BoundTables;

        private readonly MetalDevice m_Device;
        private readonly MetalBindingPipelineType m_PipelineType;
        private readonly bool m_LegacyCompatibilityEnabled;
        private MetalPipelineLayout? m_PipelineLayout;

        protected MetalBindingBackendBase(MetalDevice device, in MetalBindingPipelineType pipelineType, bool? legacyCompatibilityOverride = null)
        {
            m_Device = device;
            m_PipelineType = pipelineType;
            m_LegacyCompatibilityEnabled = legacyCompatibilityOverride ?? MetalBindingPolicyResolver.IsLegacyCompatibilityEnabled();
            m_BoundTables = new SortedDictionary<uint, MetalResourceTable>();
        }

        public virtual void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            m_PipelineLayout = pipelineLayout;
            m_BoundTables.Clear();
        }

        public virtual void SetResourceTable(MetalResourceTable resourceTable, in uint tableIndex)
        {
            if (resourceTable == null)
            {
                throw new ArgumentNullException(nameof(resourceTable));
            }

            MetalResourceTableLayout layout = resourceTable.ResourceTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Metal resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            ValidateTableInPipelineLayout(layout);
            ValidateTableArrayBindings(layout, tableIndex);

            m_BoundTables[tableIndex] = resourceTable;
            OnResourceTableUpdated(resourceTable, tableIndex);
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

        protected virtual void OnResourceTableUpdated(MetalResourceTable resourceTable, in uint tableIndex)
        {
        }

        public virtual void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
        }

        protected virtual void DisposeBackend()
        {
        }

        protected void BindLegacyResourcesForCompute(in MTLComputeCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, MetalResourceTable> pair in m_BoundTables)
            {
                MetalBindInfo[] binds = pair.Value.ResourceTableLayout.BindInfos;
                RHIResourceTableElement[] elements = pair.Value.Elements;
                for (int i = 0; i < binds.Length && i < elements.Length; ++i)
                {
                    MetalBindingHelpers.BindComputeElement(encoder, binds[i], elements[i]);
                }
            }
        }

        protected void BindLegacyResourcesForRaster(in MTLRenderCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, MetalResourceTable> pair in m_BoundTables)
            {
                MetalBindInfo[] binds = pair.Value.ResourceTableLayout.BindInfos;
                RHIResourceTableElement[] elements = pair.Value.Elements;
                for (int i = 0; i < binds.Length && i < elements.Length; ++i)
                {
                    MetalBindingHelpers.BindRasterElement(encoder, binds[i], elements[i]);
                }
            }
        }

        private void ValidateTableInPipelineLayout(MetalResourceTableLayout layout)
        {
            if (m_PipelineLayout == null)
            {
                return;
            }

            RHIResourceTableLayout[]? resourceTableLayouts = m_PipelineLayout.Descriptor.ResourceTableLayouts;
            if (resourceTableLayouts == null || resourceTableLayouts.Length == 0)
            {
                return;
            }

            bool found = false;
            for (int i = 0; i < resourceTableLayouts.Length; ++i)
            {
                if (resourceTableLayouts[i] is MetalResourceTableLayout metalLayout && metalLayout.Index == layout.Index)
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

        private static void ValidateTableArrayBindings(MetalResourceTableLayout layout, in uint tableIndex)
        {
            MetalBindInfo[] binds = layout.BindInfos;
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                if (bind.Count > 1)
                {
                    throw new NotSupportedException($"TODO(UNVERIFIED): Metal backend currently does not support Count>1 resource-table element binding without API extension. table={tableIndex}, slot={bind.Slot}, type={bind.Type}, count={bind.Count}");
                }
            }
        }
    }

    internal sealed class MetalLegacyBindingBackend : MetalBindingBackendBase
    {
        internal MetalLegacyBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType)
            : base(device, pipelineType)
        {
        }

        public override MetalBindingMode Mode => MetalBindingMode.Legacy;

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            BindLegacyResourcesForCompute(encoder);
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            throw new InvalidOperationException("Legacy binding backend requires classic Metal encoder path.");
        }

        public override void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            BindLegacyResourcesForCompute(encoder);
            if (LegacyCompatibilityEnabled && functionTable != null)
            {
                MetalBindingHelpers.BindRayFunctionTables(encoder, functionTable);
            }
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            throw new InvalidOperationException("Legacy binding backend requires classic Metal encoder path.");
        }

        public override void CommitRaster(in MTLRenderCommandEncoder encoder)
        {
            BindLegacyResourcesForRaster(encoder);
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            throw new InvalidOperationException("Legacy binding backend requires classic Metal encoder path.");
        }
    }

    internal sealed class MetalArgumentBufferBindingBackend : MetalBindingBackendBase
    {
        private sealed class ArgumentBufferState
        {
            internal readonly MetalResourceTableLayout Layout;
            internal readonly MTLArgumentEncoder Encoder;
            internal MTLBuffer BackingBuffer;

            internal ArgumentBufferState(MetalResourceTableLayout layout, in MTLArgumentEncoder encoder, in MTLBuffer backingBuffer)
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

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseTableStates();
        }

        protected override void OnResourceTableUpdated(MetalResourceTable resourceTable, in uint tableIndex)
        {
            ArgumentBufferState state = GetOrCreateState(resourceTable.ResourceTableLayout, tableIndex);
            EncodeTableResources(resourceTable, state);
        }

        public override void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, ArgumentBufferState> pair in m_TableStates)
            {
                encoder.SetBuffer(pair.Value.BackingBuffer, 0, pair.Key);
            }

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForCompute(encoder);
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

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForCompute(encoder);
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

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForRaster(encoder);
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

        private ArgumentBufferState GetOrCreateState(MetalResourceTableLayout layout, in uint tableIndex)
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

        private MTLArgumentEncoder CreateArgumentEncoder(MetalResourceTableLayout layout)
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

        private static void EncodeTableResources(MetalResourceTable table, ArgumentBufferState state)
        {
            state.Encoder.SetArgumentBuffer(state.BackingBuffer, 0);

            MetalBindInfo[] binds = table.ResourceTableLayout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];

                switch (bind.Type)
                {
                    case ERHIBindType.Buffer:
                    case ERHIBindType.StorageBuffer:
                    case ERHIBindType.UniformBuffer:
                        if (element.BufferView is MetalBufferView bufferView)
                        {
                            state.Encoder.SetBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
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
                            state.Encoder.SetTexture(textureView.NativeTexture, bind.Slot);
                        }

                        break;

                    case ERHIBindType.Sampler:
                        if (element.Sampler is MetalSampler sampler)
                        {
                            state.Encoder.SetSamplerState(sampler.NativeSampler, bind.Slot);
                        }

                        break;

                    case ERHIBindType.AccelStruct:
                        if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                        {
                            state.Encoder.SetAccelerationStructure(topLevel.NativeAccelerationStructure, bind.Slot);
                        }

                        break;
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

    internal sealed class MetalSetBytesBindingBackend : MetalBindingBackendBase
    {
        private static readonly object s_ZeroAddressLogLock = new object();
        private static readonly HashSet<string> s_ZeroAddressLogKeys = new HashSet<string>(StringComparer.Ordinal);

        [StructLayout(LayoutKind.Sequential)]
        private struct MetalBindlessPayloadHeader
        {
            internal uint Magic;
            internal uint Version;
            internal uint EntryCount;
            internal uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MetalBindlessPayloadEntry
        {
            internal uint Slot;
            internal uint BindType;
            internal uint StageMask;
            internal uint Reserved;
            internal ulong Value0;
            internal ulong Value1;
        }

        internal const uint PayloadMagic = 0x534C424D;
        internal const uint PayloadVersion = 1;
        private const int SetBytesMaxLength = 4096;
        private const uint BindTypeIntersectionFunctionTable = 0xFFFF0001;
        private const uint BindTypeVisibleFunctionTable = 0xFFFF0002;

        private readonly SortedDictionary<uint, byte[]> m_TablePayloads;

        internal MetalSetBytesBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType, bool? legacyCompatibilityOverride = null)
            : base(device, pipelineType, legacyCompatibilityOverride)
        {
            m_TablePayloads = new SortedDictionary<uint, byte[]>();
        }

        public override MetalBindingMode Mode => MetalBindingMode.SetBytes;

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing && UsesReservedRayFunctionTableSlotsForCompatibility(LegacyCompatibilityEnabled);

        internal static bool UsesReservedRayFunctionTableSlotsForCompatibility(bool legacyCompatibilityEnabled)
        {
            return legacyCompatibilityEnabled;
        }

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            m_TablePayloads.Clear();
        }

        protected override void OnResourceTableUpdated(MetalResourceTable resourceTable, in uint tableIndex)
        {
            m_TablePayloads[tableIndex] = EncodePayload(resourceTable, null);
        }

        public override unsafe void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, MetalResourceTable> pair in m_BoundTables)
            {
                if (!m_TablePayloads.TryGetValue(pair.Key, out byte[]? payload))
                {
                    payload = EncodePayload(pair.Value, null);
                    m_TablePayloads[pair.Key] = payload;
                }

                fixed (byte* payloadPtr = payload)
                {
                    encoder.SetBytes((IntPtr)payloadPtr, (ulong)payload.Length, pair.Key);
                }
            }

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForCompute(encoder);
            }
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            throw new InvalidOperationException("SetBytes binding backend requires classic Metal encoder path.");
        }

        public override unsafe void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            foreach (KeyValuePair<uint, MetalResourceTable> pair in m_BoundTables)
            {
                byte[] payload = EncodePayload(pair.Value, functionTable);
                m_TablePayloads[pair.Key] = payload;

                fixed (byte* payloadPtr = payload)
                {
                    encoder.SetBytes((IntPtr)payloadPtr, (ulong)payload.Length, pair.Key);
                }
            }

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForCompute(encoder);
            }

            if (functionTable != null)
            {
                MetalBindingHelpers.BindRayFunctionTables(encoder, functionTable);
            }
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            throw new InvalidOperationException("SetBytes binding backend requires classic Metal encoder path.");
        }

        public override unsafe void CommitRaster(in MTLRenderCommandEncoder encoder)
        {
            foreach (KeyValuePair<uint, MetalResourceTable> pair in m_BoundTables)
            {
                if (!m_TablePayloads.TryGetValue(pair.Key, out byte[]? payload))
                {
                    payload = EncodePayload(pair.Value, null);
                    m_TablePayloads[pair.Key] = payload;
                }

                fixed (byte* payloadPtr = payload)
                {
                    encoder.SetVertexBytes((IntPtr)payloadPtr, (ulong)payload.Length, pair.Key);
                    encoder.SetFragmentBytes((IntPtr)payloadPtr, (ulong)payload.Length, pair.Key);
                }
            }

            if (LegacyCompatibilityEnabled)
            {
                BindLegacyResourcesForRaster(encoder);
            }
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            throw new InvalidOperationException("SetBytes binding backend requires classic Metal encoder path.");
        }

        internal static byte[] EncodePayloadForTesting(MetalResourceTable table)
        {
            return EncodePayload(table, null);
        }

        internal static byte[] EncodePayloadForTesting(ReadOnlySpan<MetalBindInfo> binds)
        {
            List<MetalBindlessPayloadEntry> entries = new List<MetalBindlessPayloadEntry>(binds.Length);
            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                entries.Add(new MetalBindlessPayloadEntry
                {
                    Slot = bind.Slot,
                    BindType = (uint)bind.Type,
                    StageMask = (uint)bind.Stage,
                    Reserved = 0,
                    Value0 = 0,
                    Value1 = 0
                });
            }

            return BuildPayload(entries);
        }

        private static byte[] EncodePayload(MetalResourceTable table, MetalFunctionTable? functionTable)
        {
            List<MetalBindlessPayloadEntry> entries = new List<MetalBindlessPayloadEntry>(table.ResourceTableLayout.BindInfos.Length + 2);

            MetalBindInfo[] binds = table.ResourceTableLayout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];

                ulong value0 = 0;
                ulong value1 = 0;
                switch (bind.Type)
                {
                    case ERHIBindType.Buffer:
                    case ERHIBindType.StorageBuffer:
                    case ERHIBindType.UniformBuffer:
                        if (element.BufferView is MetalBufferView bufferView)
                        {
                            ulong offset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                            value0 = bufferView.Buffer.NativeBuffer.GpuAddress + offset;
                            value1 = (ulong)Math.Max(0, bufferView.Descriptor.Stride);
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
                            value0 = textureView.NativeTexture.GpuResourceID._impl;
                        }

                        break;

                    case ERHIBindType.Sampler:
                        if (element.Sampler is MetalSampler sampler)
                        {
                            value0 = sampler.NativeSampler.GpuResourceID._impl;
                        }

                        break;

                    case ERHIBindType.AccelStruct:
                        if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                        {
                            value0 = topLevel.NativeAccelerationStructure.GpuResourceID._impl;
                        }

                        break;
                }

                entries.Add(new MetalBindlessPayloadEntry
                {
                    Slot = bind.Slot,
                    BindType = (uint)bind.Type,
                    StageMask = (uint)bind.Stage,
                    Reserved = 0,
                    Value0 = value0,
                    Value1 = value1
                });

                if (value0 == 0 && bind.Type != ERHIBindType.Sampler)
                {
                    string logKey = $"{bind.Slot}:{bind.Type}:{bind.Stage}";
                    lock (s_ZeroAddressLogLock)
                    {
                        if (s_ZeroAddressLogKeys.Add(logKey))
                        {
                            Console.WriteLine($"[MetalBinding] WARNING: SetBytes payload entry has zero value0. slot={bind.Slot}, type={bind.Type}, stage={bind.Stage}.");
                        }
                    }
                }
            }

            if (functionTable != null)
            {
                if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
                {
                    entries.Add(new MetalBindlessPayloadEntry
                    {
                        Slot = (uint)MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                        BindType = BindTypeIntersectionFunctionTable,
                        StageMask = (uint)ERHIShaderStage.RayTracing,
                        Reserved = 0,
                        Value0 = functionTable.IntersectionFunctionTable.GpuResourceID._impl,
                        Value1 = 0
                    });
                }

                if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
                {
                    entries.Add(new MetalBindlessPayloadEntry
                    {
                        Slot = (uint)MetalBindingHelpers.RtVisibleFunctionTableSlot,
                        BindType = BindTypeVisibleFunctionTable,
                        StageMask = (uint)ERHIShaderStage.RayTracing,
                        Reserved = 0,
                        Value0 = functionTable.VisibleFunctionTable.GpuResourceID._impl,
                        Value1 = 0
                    });
                }
            }

            return BuildPayload(entries);
        }

        private static byte[] BuildPayload(List<MetalBindlessPayloadEntry> entries)
        {
            int headerSize = Marshal.SizeOf<MetalBindlessPayloadHeader>();
            int entrySize = Marshal.SizeOf<MetalBindlessPayloadEntry>();
            byte[] payload = new byte[headerSize + entries.Count * entrySize];

            Span<byte> payloadSpan = payload;
            MetalBindlessPayloadHeader header = new MetalBindlessPayloadHeader
            {
                Magic = PayloadMagic,
                Version = PayloadVersion,
                EntryCount = (uint)entries.Count,
                Reserved = 0
            };
            MemoryMarshal.Write(payloadSpan, in header);

            Span<MetalBindlessPayloadEntry> entrySpan = MemoryMarshal.Cast<byte, MetalBindlessPayloadEntry>(payloadSpan.Slice(headerSize));
            for (int i = 0; i < entries.Count; ++i)
            {
                entrySpan[i] = entries[i];
            }

            if (payload.Length > SetBytesMaxLength)
            {
                throw new NotSupportedException($"TODO(UNVERIFIED): SetBytes payload size {payload.Length} exceeds Metal immediate-byte limit ({SetBytesMaxLength}). Use argument-buffer mode or split payload.");
            }

            return payload;
        }
    }

    internal sealed class MetalArgumentTableBindingBackend : MetalBindingBackendBase
    {
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_ArgumentTables;
        private readonly SortedDictionary<uint, RasterVertexBinding> m_RasterVertexBindings;
        private bool m_HasWarnedLegacyCompatIgnored;

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

        internal MetalArgumentTableBindingBackend(MetalDevice device, in MetalBindingPipelineType pipelineType)
            : base(device, pipelineType)
        {
            m_ArgumentTables = new SortedDictionary<uint, MTL4ArgumentTable>();
            m_RasterVertexBindings = new SortedDictionary<uint, RasterVertexBinding>();
            m_HasWarnedLegacyCompatIgnored = false;
        }

        public override MetalBindingMode Mode => MetalBindingMode.ArgumentTable;

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseArgumentTables();
            m_RasterVertexBindings.Clear();
            m_HasWarnedLegacyCompatIgnored = false;
        }

        protected override void OnResourceTableUpdated(MetalResourceTable resourceTable, in uint tableIndex)
        {
            MTL4ArgumentTable argumentTable = GetOrCreateArgumentTable(resourceTable.ResourceTableLayout, tableIndex);
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
            WarnLegacyCompatibilityIgnoredOnce();
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
            WarnLegacyCompatibilityIgnoredOnce();
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
            WarnLegacyCompatibilityIgnoredOnce();
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

        private MTL4ArgumentTable GetOrCreateArgumentTable(MetalResourceTableLayout layout, in uint tableIndex)
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
                ulong nextIndex = bind.Slot + 1UL;
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

        private static void PopulateArgumentTable(MTL4ArgumentTable argumentTable, MetalResourceTable table)
        {
            MetalBindInfo[] binds = table.ResourceTableLayout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];
                switch (bind.Type)
                {
                    case ERHIBindType.Buffer:
                    case ERHIBindType.StorageBuffer:
                    case ERHIBindType.UniformBuffer:
                        if (element.BufferView is MetalBufferView bufferView)
                        {
                            ulong offset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                            argumentTable.SetAddress(bufferView.Buffer.NativeBuffer.GpuAddress + offset, bind.Slot);
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
                            argumentTable.SetTexture(textureView.NativeTexture.GpuResourceID, bind.Slot);
                        }

                        break;

                    case ERHIBindType.Sampler:
                        if (element.Sampler is MetalSampler sampler)
                        {
                            argumentTable.SetSamplerState(sampler.NativeSampler.GpuResourceID, bind.Slot);
                        }

                        break;

                    case ERHIBindType.AccelStruct:
                        if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                        {
                            argumentTable.SetResource(topLevel.NativeAccelerationStructure.GpuResourceID, bind.Slot);
                        }

                        break;
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

        private void WarnLegacyCompatibilityIgnoredOnce()
        {
            if (!LegacyCompatibilityEnabled || m_HasWarnedLegacyCompatIgnored)
            {
                return;
            }

            Console.WriteLine("[MetalBinding] INFINITY_METAL_BINDING_LEGACY_COMPAT is enabled, but legacy direct-binding is ignored on MTL4 argument-table backend.");
            m_HasWarnedLegacyCompatIgnored = true;
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
