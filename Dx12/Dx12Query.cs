using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Infinity.Collections.LowLevel;

namespace Infinity.Graphics
{
#pragma warning disable CA1416, CS8600, CS8602

    internal unsafe class Dx12Query : RHIQuery
    {
        public ID3D12Resource* QueryResult  => m_QueryResult;
        public ID3D12QueryHeap* QueryHeap => m_QueryHeap;

        private ID3D12Resource* m_QueryResult;
        private ID3D12QueryHeap* m_QueryHeap;

        public Dx12Query(Dx12Device device, in RHIQueryDescriptor descriptor)
        {
            m_QueryDescriptor = descriptor;

            m_Results = new ulong[descriptor.Count];
            Results = new ReadOnlyMemory<ulong>(m_Results);

            D3D12_QUERY_HEAP_DESC queryHeapDesc;
            queryHeapDesc.Type = Dx12Utility.ConvertToDx12QueryHeapType(descriptor.Type);
            queryHeapDesc.Count = descriptor.Count;
            queryHeapDesc.NodeMask = 0;

            ID3D12QueryHeap* queryHeap;
            device.NativeDevice->CreateQueryHeap(&queryHeapDesc, Windows.__uuidof<ID3D12QueryHeap>(), (void**)&queryHeap);
            m_QueryHeap = queryHeap;

            D3D12_HEAP_PROPERTIES heapProperties;
            {
                heapProperties.Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK;
                heapProperties.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY.D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
                heapProperties.MemoryPoolPreference = D3D12_MEMORY_POOL.D3D12_MEMORY_POOL_UNKNOWN;
                heapProperties.VisibleNodeMask = 0;
                heapProperties.CreationNodeMask = 0;
            }
            D3D12_RESOURCE_DESC resourceDesc;
            {
                resourceDesc.Alignment = 0;
                resourceDesc.Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER;
                resourceDesc.Width = sizeof(ulong) * descriptor.Count;
                resourceDesc.Height = 1;
                resourceDesc.DepthOrArraySize = 1;
                resourceDesc.MipLevels = 1;
                resourceDesc.SampleDesc.Count = 1;
                resourceDesc.SampleDesc.Quality = 0;
                resourceDesc.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
                resourceDesc.Flags = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;
                resourceDesc.Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            }
            ID3D12Resource* queryResult;
            device.NativeDevice->CreateCommittedResource(&heapProperties, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &resourceDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST, null, Windows.__uuidof<ID3D12Resource>(), (void**)&queryResult);
            m_QueryResult = queryResult;
        }

        public override bool ResolveData()
        {
            void* queryResult;
            D3D12_RANGE range = new D3D12_RANGE(0, 0);
            Span<ulong> resultsSpan = new Span<ulong>(m_Results);

            m_QueryResult->Map(0, &range, &queryResult);
            new IntPtr(queryResult).CopyTo(resultsSpan);
            m_QueryResult->Unmap(0, null);

            if (queryResult == null)
            {
                return false;
            }
            return true;
        }

        protected override void Release()
        {
            m_QueryHeap->Release();
            m_QueryResult->Release();
        }
    }
#pragma warning restore CA1416, CS8600, CS8602
}
