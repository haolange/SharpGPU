using System;
using SharpGPU.Collections.LowLevel;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe sealed class Dx12Query : RHIQuery
    {
        public Vortice.Direct3D12.ID3D12Resource QueryResult
        {
            get
            {
                ThrowIfDisposed();
                return m_QueryResult;
            }
        }

        public Vortice.Direct3D12.ID3D12QueryHeap QueryHeap
        {
            get
            {
                ThrowIfDisposed();
                return m_QueryHeap;
            }
        }

        public uint ResultStrideInBytes => m_ResultStrideInBytes;

        private readonly Vortice.Direct3D12.ID3D12Resource m_QueryResult;
        private readonly Vortice.Direct3D12.ID3D12QueryHeap m_QueryHeap;
        private readonly uint m_ResultStrideInBytes;

        public Dx12Query(Dx12Device device, in RHIQueryDescriptor descriptor)
        {
            m_Device = device;
            m_QueryDescriptor = descriptor;
            m_ResultStrideInBytes = GetResultStrideInBytes(descriptor.Type);
            uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));
            m_Results = new ulong[resultElementCount];

            Vortice.Direct3D12.QueryHeapDescription queryHeapDesc;
            queryHeapDesc.Type = Dx12Utility.ConvertToDx12QueryHeapType(descriptor.Type);
            queryHeapDesc.Count = descriptor.Count;
            queryHeapDesc.NodeMask = 0;

            SharpGen.Runtime.Result queryHeapResult =
                device.NativeDevice.CreateQueryHeap(
                    queryHeapDesc,
                    out Vortice.Direct3D12.ID3D12QueryHeap queryHeap);
            Dx12Utility.CHECK_HR(queryHeapResult);
            m_QueryHeap = queryHeap ?? throw new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.DirectX12,
                queryHeapResult.Code,
                "ID3D12Device.CreateQueryHeap returned no query heap.",
                ERHIDeviceState.Operational);

            Vortice.Direct3D12.HeapProperties heapProperties;
            heapProperties.Type = Vortice.Direct3D12.HeapType.Readback;
            heapProperties.CPUPageProperty = Vortice.Direct3D12.CpuPageProperty.Unknown;
            heapProperties.MemoryPoolPreference = Vortice.Direct3D12.MemoryPool.Unknown;
            heapProperties.VisibleNodeMask = 0;
            heapProperties.CreationNodeMask = 0;

            Vortice.Direct3D12.ResourceDescription resourceDesc;
            resourceDesc.Alignment = 0;
            resourceDesc.Dimension = Vortice.Direct3D12.ResourceDimension.Buffer;
            resourceDesc.Width = m_ResultStrideInBytes * descriptor.Count;
            resourceDesc.Height = 1;
            resourceDesc.DepthOrArraySize = 1;
            resourceDesc.MipLevels = 1;
            resourceDesc.SampleDescription.Count = 1;
            resourceDesc.SampleDescription.Quality = 0;
            resourceDesc.Format = Vortice.DXGI.Format.Unknown;
            resourceDesc.Flags = Vortice.Direct3D12.ResourceFlags.None;
            resourceDesc.Layout = Vortice.Direct3D12.TextureLayout.RowMajor;

            SharpGen.Runtime.Result resourceResult =
                device.NativeDevice.CreateCommittedResource(
                    heapProperties,
                    Vortice.Direct3D12.HeapFlags.None,
                    resourceDesc,
                    Vortice.Direct3D12.ResourceStates.CopyDest,
                    null,
                    out Vortice.Direct3D12.ID3D12Resource queryResult);
            try
            {
                Dx12Utility.CHECK_HR(resourceResult);
                m_QueryResult = queryResult ?? throw new RHIException(
                    ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    resourceResult.Code,
                    "ID3D12Device.CreateCommittedResource returned no query readback buffer.",
                    ERHIDeviceState.Operational);
            }
            catch
            {
                m_QueryHeap.Release();
                throw;
            }
        }

        private static uint GetResultStrideInBytes(in ERHIQueryType queryType)
        {
            return queryType == ERHIQueryType.Statistics
                ? (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics)
                : sizeof(ulong);
        }

        public override ERHIQueryResultStatus ResolveData()
        {
            ThrowIfDisposed();
            void* queryResult = null;
            Vortice.Direct3D12.Range readRange =
                new Vortice.Direct3D12.Range(0, checked((nuint)(m_Results.Length * sizeof(ulong))));
            SharpGen.Runtime.Result mapResult = m_QueryResult.Map(0, readRange, &queryResult);
            Dx12Utility.CHECK_HR(mapResult);
            if (queryResult == null)
            {
                throw new RHIException(
                    ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    mapResult.Code,
                    "ID3D12Resource.Map returned a null query readback pointer.",
                    ERHIDeviceState.Operational);
            }

            try
            {
                new IntPtr(queryResult).CopyTo(m_Results.AsSpan());
            }
            finally
            {
                m_QueryResult.Unmap(0, null);
            }

            return ERHIQueryResultStatus.Ready;
        }

        protected override void Release()
        {
            m_QueryHeap.Release();
            m_QueryResult.Release();
        }
    }
#pragma warning restore CA1416
}