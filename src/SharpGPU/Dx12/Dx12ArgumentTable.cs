using System;
using Vortice.DXGI;
using Vortice.Direct3D;
using System.Diagnostics;
using Vortice.Direct3D12;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindInfo
    {
        public uint Slot;
        public uint Index;
        public uint Count;
        public ERHIBindType Type;
        public ERHIShaderStage Stage;

        internal bool IsBindless => Count > 1;
    }

    internal struct Dx12BindlessSlotAllocation
    {
        public int HeapIndex;
        public int Count;
        public bool IsSampler;
    }

    internal unsafe class Dx12ArgumentTableLayout : RHIArgumentTableLayout
    {
        public uint Index
        {
            get
            {
                return m_Index;
            }
        }
        public Dx12BindInfo[] BindInfos
        {
            get
            {
                return m_BindInfos;
            }
        }

        private uint m_Index;
        private Dx12BindInfo[] m_BindInfos;

        public Dx12ArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new Dx12BindInfo[descriptor.Elements.Length];

            Span<RHIArgumentTableLayoutElement> elements = descriptor.Elements.Span;
            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref RHIArgumentTableLayoutElement element = ref elements[i];
                ref Dx12BindInfo bindInfo = ref m_BindInfos[i];
                bindInfo.Index = descriptor.Index;
                bindInfo.Slot = element.Slot;
                bindInfo.Type = element.Type;
                bindInfo.Count = element.Count;
                bindInfo.Stage = element.Stage;
            }
        }

        protected override void Release()
        {
            m_Index = 0;
        }
    }

    internal unsafe class Dx12ArgumentTable : RHIArgumentTable
    {
        public Dx12ArgumentTableLayout ArgumentTableLayout
        {
            get
            {
                return m_ArgumentTableLayout;
            }
        }
        public Vortice.Direct3D12.GpuDescriptorHandle[] NativeGpuDescriptorHandles
        {
            get
            {
                return m_NativeGpuDescriptorHandles;
            }
        }

        private Dx12Device m_Dx12Device;
        private Dx12ArgumentTableLayout m_ArgumentTableLayout;
        private Vortice.Direct3D12.GpuDescriptorHandle[] m_NativeGpuDescriptorHandles;
        private Dx12BindlessSlotAllocation[] m_BindlessAllocations;

        public Dx12ArgumentTable(Dx12Device device, in RHIArgumentTableDescriptor descriptor)
        {
            Dx12ArgumentTableLayout resourceTableLayout = descriptor.Layout as Dx12ArgumentTableLayout;
#if DEBUG
            Debug.Assert(resourceTableLayout != null, "ArgumentTableLayout is null in descriptor");
#endif
            m_Dx12Device = device;
            m_ArgumentTableLayout = resourceTableLayout;
            m_NativeGpuDescriptorHandles = new Vortice.Direct3D12.GpuDescriptorHandle[resourceTableLayout.BindInfos.Length];
            m_BindlessAllocations = new Dx12BindlessSlotAllocation[resourceTableLayout.BindInfos.Length];

            for (int i = 0; i < resourceTableLayout.BindInfos.Length; ++i)
            {
                ref Dx12BindInfo bindInfo = ref resourceTableLayout.BindInfos[i];
                m_BindlessAllocations[i].HeapIndex = -1;
                m_BindlessAllocations[i].Count = 0;
                m_BindlessAllocations[i].IsSampler = false;

                if (bindInfo.IsBindless)
                {
                    // Allocate a contiguous range for the bindless array in the GPU-visible heap
                    int count = (int)bindInfo.Count;
                    bool isSampler = bindInfo.Type == ERHIBindType.Sampler;

                    Dx12DescriptorInfo allocation;
                    if (isSampler)
                    {
                        allocation = device.AllocateSamplerDescriptor(count);
                    }
                    else
                    {
                        allocation = device.AllocateCbvSrvUavDescriptor(count);
                    }

#if DEBUG
                    Debug.Assert(allocation.Index >= 0, $"Failed to allocate {count} contiguous descriptors for bindless slot {bindInfo.Slot}");
#endif

                    m_BindlessAllocations[i].HeapIndex = allocation.Index;
                    m_BindlessAllocations[i].Count = count;
                    m_BindlessAllocations[i].IsSampler = isSampler;

                    // The GPU handle for a bindless descriptor table points to the base of the contiguous range
                    m_NativeGpuDescriptorHandles[i] = allocation.GpuHandle;

                    // If the first element is provided in the descriptor, copy it to array index 0
                    if (i < descriptor.Elements.Length)
                    {
                        ref RHIArgumentTableElement element = ref descriptor.Elements.Span[i];
                        CopyElementToBindlessSlot(i, 0, element, bindInfo.Type);
                    }
                }
                else
                {
                    // Non-bindless (Count <= 1): directly reference the view's existing descriptor
                    if (i < descriptor.Elements.Length)
                    {
                        ref RHIArgumentTableElement element = ref descriptor.Elements.Span[i];
                        ref Vortice.Direct3D12.GpuDescriptorHandle nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[i];
                        SetGpuHandleFromElement(ref nativeGpuDescriptorHandle, element, bindInfo.Type);
                    }
                }
            }
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot)
        {
            int bindIndex = FindBindIndex(slot, bindType);
            if (bindIndex < 0) return;

            ref Dx12BindInfo bindInfo = ref m_ArgumentTableLayout.BindInfos[bindIndex];

            if (bindInfo.IsBindless)
            {
                // For bindless slots, update array index 0 by default
                CopyElementToBindlessSlot(bindIndex, 0, element, bindType);
            }
            else
            {
                ref Vortice.Direct3D12.GpuDescriptorHandle nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[bindIndex];
                SetGpuHandleFromElement(ref nativeGpuDescriptorHandle, element, bindType);
            }
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex)
        {
            int bindIndex = FindBindIndex(slot, bindType);
            if (bindIndex < 0) return;

            ref Dx12BindInfo bindInfo = ref m_ArgumentTableLayout.BindInfos[bindIndex];

#if DEBUG
            Debug.Assert(bindInfo.IsBindless, $"SetBindElement with arrayIndex called on non-bindless slot {slot}");
            Debug.Assert(arrayIndex >= 0 && arrayIndex < (int)bindInfo.Count, $"arrayIndex {arrayIndex} out of range [0, {bindInfo.Count}) for slot {slot}");
#endif

            CopyElementToBindlessSlot(bindIndex, arrayIndex, element, bindType);
        }

        protected override void Release()
        {
            // Free all bindless descriptor allocations
            for (int i = 0; i < m_BindlessAllocations.Length; ++i)
            {
                ref Dx12BindlessSlotAllocation alloc = ref m_BindlessAllocations[i];
                if (alloc.HeapIndex >= 0 && alloc.Count > 0)
                {
                    if (alloc.IsSampler)
                    {
                        m_Dx12Device.FreeSamplerDescriptor(alloc.HeapIndex, alloc.Count);
                    }
                    else
                    {
                        m_Dx12Device.FreeCbvSrvUavDescriptor(alloc.HeapIndex, alloc.Count);
                    }
                    alloc.HeapIndex = -1;
                    alloc.Count = 0;
                }
            }
        }

        private int FindBindIndex(in int slot, in ERHIBindType bindType)
        {
            // For non-bindless tables, slot maps directly to bind index (preserving original behavior)
            if (slot >= 0 && slot < m_ArgumentTableLayout.BindInfos.Length)
            {
                ref Dx12BindInfo bindInfo = ref m_ArgumentTableLayout.BindInfos[slot];
                if (bindInfo.Slot == (uint)slot)
                {
                    return slot;
                }
            }

            // Fallback: linear search by slot number
            for (int i = 0; i < m_ArgumentTableLayout.BindInfos.Length; ++i)
            {
                ref Dx12BindInfo bindInfo = ref m_ArgumentTableLayout.BindInfos[i];
                if (bindInfo.Slot == (uint)slot && bindInfo.Type == bindType)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SetGpuHandleFromElement(ref Vortice.Direct3D12.GpuDescriptorHandle handle, in RHIArgumentTableElement element, in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.AccelStruct:
                    Dx12TopLevelAccelStruct accelStruct = element.AccelStruct as Dx12TopLevelAccelStruct;
                    if (accelStruct != null) handle = accelStruct.NativeGpuDescriptorHandle;
                    break;

                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                    if (bufferView != null) handle = bufferView.NativeGpuDescriptorHandle;
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
                    Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                    if (textureView != null) handle = textureView.NativeGpuDescriptorHandle;
                    break;

                case ERHIBindType.Sampler:
                    Dx12Sampler samplerState = element.Sampler as Dx12Sampler;
                    if (samplerState != null) handle = samplerState.NativeGpuDescriptorHandle;
                    break;
            }
        }

        private void CopyElementToBindlessSlot(in int bindIndex, in int arrayIndex, in RHIArgumentTableElement element, in ERHIBindType bindType)
        {
            ref Dx12BindlessSlotAllocation alloc = ref m_BindlessAllocations[bindIndex];
            if (alloc.HeapIndex < 0) return;

            Vortice.Direct3D12.CpuDescriptorHandle srcHandle = default;
            bool hasSource = false;

            switch (bindType)
            {
                case ERHIBindType.AccelStruct:
                    Dx12TopLevelAccelStruct accelStruct = element.AccelStruct as Dx12TopLevelAccelStruct;
                    if (accelStruct != null) { srcHandle = accelStruct.NativeCpuDescriptorHandle; hasSource = true; }
                    break;

                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                    if (bufferView != null) { srcHandle = bufferView.NativeCpuDescriptorHandle; hasSource = true; }
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
                    Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                    if (textureView != null) { srcHandle = textureView.NativeCpuDescriptorHandle; hasSource = true; }
                    break;

                case ERHIBindType.Sampler:
                    Dx12Sampler samplerState = element.Sampler as Dx12Sampler;
                    if (samplerState != null) { srcHandle = samplerState.NativeCpuDescriptorHandle; hasSource = true; }
                    break;
            }

            if (hasSource)
            {
                // Copy the single descriptor from the view's CPU handle into the contiguous GPU-visible range at the given array index
                Dx12DescriptorHeap dstHeap = alloc.IsSampler ? m_Dx12Device.DescriptorHeapSampler : m_Dx12Device.DescriptorHeapCbvSrvUav;
                int dstIndex = alloc.HeapIndex + arrayIndex;
                Vortice.Direct3D12.CpuDescriptorHandle dstHandle = dstHeap.NativeCpuStartHandle.Offset(dstIndex, dstHeap.DescriptorSize);
                m_Dx12Device.NativeDevice.CopyDescriptorsSimple(1, dstHandle, srcHandle, dstHeap.NativeType);
            }
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
