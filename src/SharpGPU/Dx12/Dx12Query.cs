using System;
using SharpGPU.Collections.LowLevel;

namespace SharpGPU
{
#pragma warning disable CA1416

    internal unsafe class Dx12Query : RHIQuery
    {
        public Vortice.Direct3D12.ID3D12Resource QueryResult  => m_QueryResult;
        public Vortice.Direct3D12.ID3D12QueryHeap QueryHeap => m_QueryHeap;
        public uint ResultStrideInBytes => m_ResultStrideInBytes;

        private Vortice.Direct3D12.ID3D12Resource m_QueryResult;
        private Vortice.Direct3D12.ID3D12QueryHeap m_QueryHeap;
        private readonly uint m_ResultStrideInBytes;

        public Dx12Query(Dx12Device device, in RHIQueryDescriptor descriptor)
        {
            m_QueryDescriptor = descriptor;
            m_ResultStrideInBytes = GetResultStrideInBytes(descriptor.Type);
            uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));

            m_Results = new ulong[resultElementCount];
            Results = new ReadOnlyMemory<ulong>(m_Results);

            Vortice.Direct3D12.QueryHeapDescription queryHeapDesc;
            queryHeapDesc.Type = Dx12Utility.ConvertToDx12QueryHeapType(descriptor.Type);
            queryHeapDesc.Count = descriptor.Count;
            queryHeapDesc.NodeMask = 0;

            SharpGen.Runtime.Result queryHeapResult =
                device.NativeDevice.CreateQueryHeap(queryHeapDesc, out Vortice.Direct3D12.ID3D12QueryHeap? queryHeap);
            m_QueryHeap = Dx12Utility.RequireCreatedObject(
                queryHeap,
                queryHeapResult,
                "ID3D12Device.CreateQueryHeap");

            Vortice.Direct3D12.HeapProperties heapProperties;
            {
                heapProperties.Type = Vortice.Direct3D12.HeapType.Readback;
                heapProperties.CPUPageProperty = Vortice.Direct3D12.CpuPageProperty.Unknown;
                heapProperties.MemoryPoolPreference = Vortice.Direct3D12.MemoryPool.Unknown;
                heapProperties.VisibleNodeMask = 0;
                heapProperties.CreationNodeMask = 0;
            }
            Vortice.Direct3D12.ResourceDescription resourceDesc;
            {
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
            }
            SharpGen.Runtime.Result queryResultCreation = device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                resourceDesc,
                Vortice.Direct3D12.ResourceStates.CopyDest,
                null,
                out Vortice.Direct3D12.ID3D12Resource? queryResult);
            m_QueryResult = Dx12Utility.RequireCreatedObject(
                queryResult,
                queryResultCreation,
                "ID3D12Device.CreateCommittedResource(query readback)");
        }

        private static uint GetResultStrideInBytes(in ERHIQueryType queryType)
        {
            return queryType == ERHIQueryType.Statistics
                ? (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics)
                : sizeof(ulong);
        }

        public override bool ResolveData()
        {
            void* queryResult = null;
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(0, 0);
            Span<ulong> resultsSpan = new Span<ulong>(m_Results);

            SharpGen.Runtime.Result mapResult = m_QueryResult.Map(0, range, &queryResult);
            Dx12Utility.CHECK_HR(mapResult);
            if (queryResult == null)
            {
                throw new RHIException(
                    ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    mapResult.Code,
                    "ID3D12Resource.Map succeeded without returning a query readback pointer.",
                    ERHIDeviceState.Operational);
            }

            try
            {
                new IntPtr(queryResult).CopyTo(resultsSpan);
                return true;
            }
            finally
            {
                m_QueryResult.Unmap(0, null);
            }
        }

        protected override void Release()
        {
            m_QueryHeap.Release();
            m_QueryResult.Release();
        }
    }
#pragma warning restore CA1416
}
