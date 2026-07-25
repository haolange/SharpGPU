using System;
using Vortice.Direct3D12;

namespace SharpGPU
{
    internal unsafe class Dx12ComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        public ID3D12CommandSignature NativeCommandSignature => m_NativeCommandSignature;
        public ID3D12Resource NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private ID3D12Resource m_NativeArgumentBuffer;
        private ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12ComputeIndirectCommandBuffer(Dx12Device device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchComputeIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ResourceDescription bufferDesc = ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(DispatchArguments));
            HeapProperties heapProps = new HeapProperties(HeapType.Default);

            ID3D12Resource? resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                HeapFlags.None,
                bufferDesc,
                ResourceStates.Common,
                null,
                out resource);
            m_NativeArgumentBuffer = Dx12Utility.RequireCreatedObject(
                resource,
                hResult,
                "ID3D12Device.CreateCommittedResource(compute-indirect-args)");
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }

    internal unsafe class Dx12RayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        public ID3D12CommandSignature NativeCommandSignature => m_NativeCommandSignature;
        public ID3D12Resource NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private ID3D12Resource m_NativeArgumentBuffer;
        private ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12RayTracingIndirectCommandBuffer(Dx12Device device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchRayIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ResourceDescription bufferDesc = ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(DispatchRaysDescription));
            HeapProperties heapProps = new HeapProperties(HeapType.Default);

            ID3D12Resource? resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                HeapFlags.None,
                bufferDesc,
                ResourceStates.Common,
                null,
                out resource);
            m_NativeArgumentBuffer = Dx12Utility.RequireCreatedObject(
                resource,
                hResult,
                "ID3D12Device.CreateCommittedResource(raytracing-indirect-args)");
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }

    internal unsafe class Dx12RasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        public ID3D12CommandSignature NativeCommandSignature => m_NativeCommandSignature;
        public ID3D12Resource NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private ID3D12Resource m_NativeArgumentBuffer;
        private ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12RasterIndirectCommandBuffer(Dx12Device device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DrawIndexedIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ResourceDescription bufferDesc = ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(DrawIndexedArguments));
            HeapProperties heapProps = new HeapProperties(HeapType.Default);

            ID3D12Resource? resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                HeapFlags.None,
                bufferDesc,
                ResourceStates.Common,
                null,
                out resource);
            m_NativeArgumentBuffer = Dx12Utility.RequireCreatedObject(
                resource,
                hResult,
                "ID3D12Device.CreateCommittedResource(raster-indirect-args)");
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }
}
