using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal enum MetalBindingPipelineType : byte
    {
        Compute = 0,
        Raytracing = 1,
        Raster = 2
    }

    internal interface IMetalBindingBackend : IDisposable
    {
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
    }

    internal sealed class MetalArgumentTableBindingBackend : MetalBindingBackendBase
    {
        // ── Single-set mode state (direct MTL4ArgumentTable resource binding) ──
        private readonly SortedDictionary<uint, MTL4ArgumentTable> m_ArgumentTables;

        // ── Multi-set mode state (descriptor buffer per set → single root argument table) ──
        private bool m_IsMultiSet;
        private MTL4ArgumentTable m_RootArgumentTable;
        private readonly SortedDictionary<uint, MTLBuffer> m_DescriptorBuffers;

        // ── Shared state ──
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
            m_DescriptorBuffers = new SortedDictionary<uint, MTLBuffer>();
            m_RasterVertexBindings = new SortedDictionary<uint, RasterVertexBinding>();
            m_CommandQueue = commandQueue;
        }

        public override bool UsesReservedRayFunctionTableSlots => PipelineType == MetalBindingPipelineType.Raytracing;

        public override void ResetForPipeline(MetalPipelineLayout pipelineLayout)
        {
            base.ResetForPipeline(pipelineLayout);
            ReleaseArgumentTables();
            ReleaseDescriptorBuffers();
            m_RasterVertexBindings.Clear();

            int layoutCount = pipelineLayout.ArgumentTableLayoutCount;
            m_IsMultiSet = layoutCount > 1;

            if (m_IsMultiSet)
            {
                CreateRootArgumentTable(pipelineLayout);
            }
        }

        protected override void OnArgumentTableUpdated(MetalArgumentTable resourceTable, in uint tableIndex)
        {
            if (m_IsMultiSet)
            {
                MTLBuffer descriptorBuffer = GetOrCreateDescriptorBuffer(resourceTable.ArgumentTableLayout, tableIndex);
                PopulateDescriptorBuffer(descriptorBuffer, resourceTable, m_CommandQueue);
                m_RootArgumentTable.SetAddress(descriptorBuffer.GpuAddress, tableIndex);
                m_CommandQueue?.AddResidencyAllocation(descriptorBuffer);
                ApplyRasterVertexBufferBindingsToRoot();
            }
            else
            {
                MTL4ArgumentTable argumentTable = GetOrCreateArgumentTable(resourceTable.ArgumentTableLayout, tableIndex);
                PopulateArgumentTable(argumentTable, resourceTable);
                ApplyRasterVertexBufferBindings(argumentTable);
            }
        }

        public override void SetRasterVertexBuffer(in uint slot, in ulong address, in ulong stride)
        {
            if (PipelineType != MetalBindingPipelineType.Raster)
            {
                return;
            }

            m_RasterVertexBindings[slot] = new RasterVertexBinding(address, stride);

            if (m_IsMultiSet)
            {
                if (m_RootArgumentTable.NativePtr != IntPtr.Zero)
                {
                    ApplyRasterVertexBufferBinding(m_RootArgumentTable, slot, m_RasterVertexBindings[slot]);
                }
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    ApplyRasterVertexBufferBinding(pair.Value, slot, m_RasterVertexBindings[slot]);
                }
            }
        }

        public override void CommitCompute(in MTLComputeCommandEncoder encoder)
        {
            ThrowRequiresMtl4Encoder("compute");
        }

        public override void CommitCompute(in MTL4ComputeCommandEncoder encoder)
        {
            if (m_IsMultiSet)
            {
                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    encoder.SetArgumentTable(pair.Value.NativePtr);
                }
            }
        }

        public override void CommitRaytracing(in MTLComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            ThrowRequiresMtl4Encoder("raytracing");
        }

        public override void CommitRaytracing(in MTL4ComputeCommandEncoder encoder, MetalFunctionTable? functionTable)
        {
            if (m_IsMultiSet)
            {
                if (functionTable != null)
                {
                    PopulateRayFunctionTablesOnRoot(functionTable);
                }

                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr);
            }
            else
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
        }

        public override void CommitRaster(in MTLRenderCommandEncoder encoder)
        {
            ThrowRequiresMtl4Encoder("raster");
        }

        public override void CommitRaster(in MTL4RenderCommandEncoder encoder)
        {
            ulong stages = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Vertex) | MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Fragment);

            if (m_IsMultiSet)
            {
                ApplyRasterVertexBufferBindingsToRoot();
                encoder.SetArgumentTable(m_RootArgumentTable.NativePtr, stages);
            }
            else
            {
                foreach (KeyValuePair<uint, MTL4ArgumentTable> pair in m_ArgumentTables)
                {
                    ApplyRasterVertexBufferBindings(pair.Value);
                    encoder.SetArgumentTable(pair.Value.NativePtr, stages);
                }
            }
        }

        protected override void DisposeBackend()
        {
            ReleaseArgumentTables();
            ReleaseDescriptorBuffers();
            m_RasterVertexBindings.Clear();
        }

        // ── Single-set mode: direct MTL4ArgumentTable ──

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
                                argumentTable.SetTexture(textureView.ResourceID, slotIndex);
                                m_CommandQueue?.AddResidencyAllocation(textureView.ParentTexture);
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

        // ── Multi-set mode: descriptor buffer per set → single root argument table ──

        private void CreateRootArgumentTable(MetalPipelineLayout pipelineLayout)
        {
            // The root argument table holds one buffer slot per set (for descriptor buffer GPU addresses),
            // plus reserved slots for RT function tables and raster vertex buffers.
            ulong maxBufferCount = (ulong)pipelineLayout.ArgumentTableLayoutCount;

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
            descriptor.MaxTextureBindCount = 1UL;
            descriptor.MaxSamplerStateBindCount = 1UL;
            descriptor.InitializeBindings = true;
            descriptor.SupportAttributeStrides = PipelineType == MetalBindingPipelineType.Raster;

            NSError error = default;
            m_RootArgumentTable = Device.NativeDevice.NewArgumentTable(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (m_RootArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create multi-set root argument table: {errorText}");
            }
        }

        private MTLBuffer GetOrCreateDescriptorBuffer(MetalArgumentTableLayout layout, in uint tableIndex)
        {
            if (m_DescriptorBuffers.TryGetValue(tableIndex, out MTLBuffer existingBuffer))
            {
                return existingBuffer;
            }

            // Each entry in the descriptor buffer is 8 bytes (ulong):
            // - Buffer bindings: GPU address
            // - Texture bindings: MTLResourceID._impl
            // - Sampler bindings: MTLResourceID._impl
            // - AccelStruct bindings: MTLResourceID._impl
            ulong byteSize = (ulong)layout.TotalElementCount * 8UL;
            byteSize = Math.Max(byteSize, 8UL);

            MTLBuffer buffer = Device.NativeDevice.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeShared);
            if (buffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to allocate descriptor buffer for set {tableIndex} (size={byteSize}).");
            }

            m_DescriptorBuffers.Add(tableIndex, buffer);
            return buffer;
        }

        private static void PopulateDescriptorBuffer(MTLBuffer buffer, MetalArgumentTable table, MetalCommandQueue? commandQueue)
        {
            IntPtr ptr = buffer.Contents;
            MetalBindInfo[] binds = table.ArgumentTableLayout.BindInfos;

            for (int i = 0; i < binds.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                int arrayCount = (int)Math.Max(1u, bind.Count);
                int baseOffset = table.ArgumentTableLayout.GetElementOffset(i);

                for (int j = 0; j < arrayCount; ++j)
                {
                    RHIArgumentTableElement element = table.GetElement(i, j);
                    int entryByteOffset = (baseOffset + j) * 8;

                    switch (bind.Type)
                    {
                        case ERHIBindType.Buffer:
                        case ERHIBindType.StorageBuffer:
                        case ERHIBindType.UniformBuffer:
                            if (element.BufferView is MetalBufferView bufferView)
                            {
                                ulong bufOffset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                                ulong address = bufferView.Buffer.NativeBuffer.GpuAddress + bufOffset;
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)address);
                                commandQueue?.AddResidencyAllocation(bufferView.Buffer.NativeBuffer);
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
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)textureView.ResourceID._impl);
                                commandQueue?.AddResidencyAllocation(textureView.ParentTexture);
                            }

                            break;

                        case ERHIBindType.Sampler:
                            if (element.Sampler is MetalSampler sampler)
                            {
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)sampler.NativeSampler.GpuResourceID._impl);
                            }

                            break;

                        case ERHIBindType.AccelStruct:
                            if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                            {
                                Marshal.WriteInt64(ptr + entryByteOffset, (long)topLevel.NativeAccelerationStructure.GpuResourceID._impl);
                                commandQueue?.AddResidencyAllocation(topLevel.NativeAccelerationStructure);
                            }

                            break;
                    }
                }
            }
        }

        private void PopulateRayFunctionTablesOnRoot(MetalFunctionTable functionTable)
        {
            if (functionTable.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_RootArgumentTable.SetResource(functionTable.IntersectionFunctionTable.GpuResourceID, MetalBindingHelpers.RtIntersectionFunctionTableSlot);
            }

            if (functionTable.VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_RootArgumentTable.SetResource(functionTable.VisibleFunctionTable.GpuResourceID, MetalBindingHelpers.RtVisibleFunctionTableSlot);
            }
        }

        // ── Raster vertex buffer helpers ──

        private void ApplyRasterVertexBufferBindings(MTL4ArgumentTable argumentTable)
        {
            foreach (KeyValuePair<uint, RasterVertexBinding> pair in m_RasterVertexBindings)
            {
                ApplyRasterVertexBufferBinding(argumentTable, pair.Key, pair.Value);
            }
        }

        private void ApplyRasterVertexBufferBindingsToRoot()
        {
            if (m_RootArgumentTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            ApplyRasterVertexBufferBindings(m_RootArgumentTable);
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

        // ── Lifecycle helpers ──

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

        private void ReleaseDescriptorBuffers()
        {
            foreach (KeyValuePair<uint, MTLBuffer> pair in m_DescriptorBuffers)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value.NativePtr);
                }
            }

            m_DescriptorBuffers.Clear();

            if (m_RootArgumentTable.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_RootArgumentTable.NativePtr);
                m_RootArgumentTable = default;
            }

            m_IsMultiSet = false;
        }
    }
}
