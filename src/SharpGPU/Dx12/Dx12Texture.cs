using System;
using System.Diagnostics;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12Texture : RHITexture
    {
        public Dx12Device Dx12Device
        {
            get
            {
                ThrowIfDisposed();
                return m_Dx12Device;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeResource
        {
            get
            {
                return m_NativeResource ?? throw new ObjectDisposedException(nameof(Dx12Texture));
            }
        }
        internal RHISparseTextureMemoryRequirements? SparseRequirements { get; }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource? m_NativeResource;
        private RHIHeapPlacement? m_Placement;

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            Vortice.Direct3D12.HeapProperties heapProperties = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Default/*Dx12Utility.ConvertToDx12ResourceFlagByUsage(descriptor.StorageMode)*/);
            Vortice.Direct3D12.ResourceDescription textureDesc =
                Dx12MemoryUtility.BuildTextureDescription(descriptor);

            Vortice.Direct3D12.ID3D12Resource? dx12Resource;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                textureDesc,
                Vortice.Direct3D12.ResourceStates.Common/*Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode)*/,
                null,
                out dx12Resource);
            if (hResult.Failure || dx12Resource == null)
            {
                dx12Resource?.Release();
                SharpGen.Runtime.Result removedReason = m_Dx12Device.NativeDevice.DeviceRemovedReason;
                throw new InvalidOperationException($"Failed to create DX12 texture (Device={m_Dx12Device.Name}, Dimension={descriptor.Dimension}, Extent={descriptor.Extent}, Format={descriptor.Format}, Usage={descriptor.UsageFlag}, HRESULT=0x{(int)hResult:X8}, DeviceRemovedReason=0x{(int)removedReason:X8}).");
            }

            m_NativeResource = dx12Resource;
        }

        internal Dx12Texture(
            Dx12Device device,
            in RHITextureDescriptor descriptor,
            Dx12Heap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;
            Vortice.Direct3D12.ResourceDescription resourceDescription =
                Dx12MemoryUtility.BuildTextureDescription(descriptor);
            SharpGen.Runtime.Result result = device.NativeDevice.CreatePlacedResource(
                heap.NativeHeap,
                heapOffset,
                resourceDescription,
                Vortice.Direct3D12.ResourceStates.Common,
                out Vortice.Direct3D12.ID3D12Resource? resource);
            Dx12Utility.CHECK_HR(result);
            m_NativeResource = resource ?? throw new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.DirectX12,
                result.Code,
                "CreatePlacedResource returned a null DX12 texture.",
                ERHIDeviceState.Operational);
            m_Placement = placement;
        }

        internal Dx12Texture(
            Dx12Device device,
            in RHITextureDescriptor descriptor,
            bool createSparse)
        {
            if (!createSparse)
            {
                throw new ArgumentException(
                    "The sparse texture constructor requires sparse creation.",
                    nameof(createSparse));
            }

            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_AllocationMode = ERHIResourceAllocationMode.Sparse;
            m_NativeResource =
                Dx12SparseMemoryUtility.CreateReservedTexture(device, descriptor);
            try
            {
                SparseRequirements =
                    Dx12SparseMemoryUtility.QueryRequirements(
                        device,
                        descriptor,
                        m_NativeResource);
            }
            catch
            {
                m_NativeResource.Release();
                m_NativeResource = null;
                throw;
            }
        }

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor Descriptor, in Vortice.Direct3D12.ID3D12Resource nativeResource)
        {
            m_Dx12Device = device;
            m_Descriptor = Descriptor;
            m_NativeResource = nativeResource;
            m_AllocationMode = ERHIResourceAllocationMode.External;
        }

        internal static Dx12Texture CreateSamplerFeedbackMap(
            Dx12Device device,
            in RHITextureDescriptor descriptor,
            Dx12Texture pairedTexture,
            ERHISamplerFeedbackMode mode,
            SharpGPU.Mathematics.uint3 mipRegion)
        {
            Vortice.Direct3D12.HeapProperties heapProperties = new(
                Vortice.Direct3D12.HeapType.Default);
            Vortice.Direct3D12.ResourceDescription1 textureDesc =
                Vortice.Direct3D12.ResourceDescription1.Texture2D(
                    Dx12Utility.ConvertToDx12Format(descriptor.Format),
                    descriptor.Extent.x,
                    descriptor.Extent.y,
                    checked((ushort)descriptor.Extent.z),
                    checked((ushort)descriptor.MipCount),
                    sampleCount: 1,
                    sampleQuality: 0,
                    flags: Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess,
                    samplerFeedbackMipRegionWidth: mipRegion.x,
                    samplerFeedbackMipRegionHeight: mipRegion.y,
                    samplerFeedbackMipRegionDepth: mipRegion.z);

            Vortice.Direct3D12.ID3D12Resource nativeResource;
            try
            {
                nativeResource =
                    device.NativeDevice.CreateCommittedResource2<Vortice.Direct3D12.ID3D12Resource>(
                        heapProperties,
                        Vortice.Direct3D12.HeapFlags.None,
                        textureDesc,
                        Vortice.Direct3D12.ResourceStates.Common,
                        protectedSession: null!);
            }
            catch (Exception exception)
            {
                SharpGen.Runtime.Result removedReason = device.NativeDevice.DeviceRemovedReason;
                throw new InvalidOperationException(
                    $"Failed to create DX12 sampler-feedback map (Device={device.Name}, Dimension={descriptor.Dimension}, Extent={descriptor.Extent}, Format={descriptor.Format}, Mode={mode}, MipRegion={mipRegion}, DeviceRemovedReason=0x{(int)removedReason:X8}).",
                    exception);
            }

            Dx12Texture map = new(device, descriptor, nativeResource);
            map.m_AllocationMode = ERHIResourceAllocationMode.Committed;
            map.BindSamplerFeedbackPairing(pairedTexture, mode, mipRegion);
            return map;
        }

        public override RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12TextureView(this, descriptor);
        }

        protected override void Release()
        {
            Vortice.Direct3D12.ID3D12Resource? nativeResource = m_NativeResource;
            m_NativeResource = null;
            nativeResource?.Release();
            m_Placement?.Dispose();
            m_Placement = null;
        }
    }
#pragma warning restore CA1416
}
