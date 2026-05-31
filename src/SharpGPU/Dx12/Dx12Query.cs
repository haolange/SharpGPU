using System;
using SharpGPU.Collections.LowLevel;

namespace SharpGPU
{
#pragma warning disable CA1416, CS8600, CS8602

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

            Vortice.Direct3D12.ID3D12QueryHeap queryHeap;
            device.NativeDevice.CreateQueryHeap(queryHeapDesc, out queryHeap);
            m_QueryHeap = queryHeap;

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
            Vortice.Direct3D12.ID3D12Resource queryResult;
            device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                resourceDesc,
                Vortice.Direct3D12.ResourceStates.CopyDest,
                null,
                out queryResult);
            m_QueryResult = queryResult;
        }

        private static uint GetResultStrideInBytes(in ERHIQueryType queryType)
        {
            return queryType == ERHIQueryType.Statistics
                ? (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics)
                : sizeof(ulong);
        }

        public override bool ResolveData()
        {
            void* queryResult;
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(0, 0);
            Span<ulong> resultsSpan = new Span<ulong>(m_Results);

            m_QueryResult.Map(0, range, &queryResult);
            new IntPtr(queryResult).CopyTo(resultsSpan);
            m_QueryResult.Unmap(0, null);

            if (queryResult == null)
            {
                return false;
            }
            return true;
        }

        protected override void Release()
        {
            m_QueryHeap.Release();
            m_QueryResult.Release();
        }
    }
#pragma warning restore CA1416, CS8600, CS8602
}
