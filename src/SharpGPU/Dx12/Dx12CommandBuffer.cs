using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12CommandBuffer : RHICommandBuffer
    {
        public Vortice.Direct3D12.ID3D12CommandAllocator NativeCommandAllocator
        {
            get
            {
                return m_NativeCommandAllocator ?? throw new ObjectDisposedException(GetType().FullName);
            }
        }
        public Vortice.Direct3D12.ID3D12GraphicsCommandList7 NativeCommandList
        {
            get
            {
                return m_NativeCommandList ?? throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private Dx12TransferEncoder m_TransferEncoder;
        private Dx12ComputeEncoder m_ComputeEncoder;
        private Dx12RasterEncoder m_RasterEncoder;
        private Dx12RaytracingEncoder m_RaytracingEncoder;
        private Dx12MLEncoder m_MLEncoder;
        private Dx12WorkGraphEncoder m_WorkGraphEncoder;
        private Vortice.Direct3D12.ID3D12CommandAllocator? m_NativeCommandAllocator;
        private Vortice.Direct3D12.ID3D12GraphicsCommandList7? m_NativeCommandList;
        private Vortice.Direct3D12.ID3D12DescriptorHeap[] m_DescriptorHeaps;
        private readonly List<(Dx12DescriptorInfo Descriptor, int Count)>
            m_TransientCbvSrvUavDescriptors = new();

        public Dx12CommandBuffer(Dx12CommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;
            InitializeNativeObjects();
            m_TransferEncoder = new Dx12TransferEncoder(this);
            m_ComputeEncoder = new Dx12ComputeEncoder(this);
            m_RasterEncoder = new Dx12RasterEncoder(this);
            m_RaytracingEncoder = new Dx12RaytracingEncoder(this);
            m_MLEncoder = new Dx12MLEncoder(this);
            m_WorkGraphEncoder = new Dx12WorkGraphEncoder(this);

            Dx12Device device = commandQueue.Dx12Device;
            m_DescriptorHeaps = new Vortice.Direct3D12.ID3D12DescriptorHeap[]
            {
                device.DescriptorHeapSampler.NativeDescriptorHeap,
                device.DescriptorHeapCbvSrvUav.NativeDescriptorHeap,
            };
        }

        private void InitializeNativeObjects()
        {
            if (m_CommandQueue is not Dx12CommandQueue commandQueue)
            {
                throw new InvalidOperationException("Dx12CommandBuffer requires a Dx12CommandQueue.");
            }

            Vortice.Direct3D12.ID3D12CommandAllocator? commandAllocator;
            SharpGen.Runtime.Result hResult = commandQueue.Dx12Device.NativeDevice.CreateCommandAllocator(
                Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType),
                out commandAllocator);
            m_NativeCommandAllocator = Dx12Utility.RequireCreatedObject(
                commandAllocator,
                hResult,
                "ID3D12Device.CreateCommandAllocator");

            Vortice.Direct3D12.ID3D12GraphicsCommandList7? commandList;
            hResult = Dx12Utility.CreateCommandListWithoutInitialPipelineState(
                commandQueue.Dx12Device.NativeDevice,
                0,
                Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType),
                out commandList);
            m_NativeCommandList = Dx12Utility.RequireCreatedObject(
                commandList,
                hResult,
                "ID3D12Device4.CreateCommandList1");
            // CreateCommandList1 returns a closed list; the first Begin() Reset()s it.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            ValidateCanBegin();
            try
            {
                ReleaseTransientCbvSrvUavDescriptors();
                NativeCommandAllocator.Reset();
                NativeCommandList.Reset(NativeCommandAllocator);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Dx12CommandBuffer.Begin failed to reset command allocator/list. " +
                    "The allocator is likely still in flight on GPU, or submission/fence sequencing is invalid.",
                    ex);
            }

#if DEBUG
            Dx12PixEventMarker.BeginEvent((nint)NativeCommandList, name);
#endif

            NativeCommandList.SetDescriptorHeaps(m_DescriptorHeaps);
            MarkBeginSucceeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Transfer);
            m_TransferEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Transfer);
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndTransferPass()
        {
            m_TransferEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Compute);
            m_ComputeEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Compute);
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndComputePass()
        {
            m_ComputeEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.RayTracing);
            m_RaytracingEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.RayTracing);
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRaytracingPass()
        {
            m_RaytracingEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Raster);
            m_RasterEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Raster);
            return m_RasterEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRasterPass()
        {
            m_RasterEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.MachineLearning);
            m_MLEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.MachineLearning);
            return m_MLEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndMLPass()
        {
            m_MLEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void End()
        {
            ValidateCanEnd();
#if DEBUG
            Dx12PixEventMarker.EndEvent((nint)NativeCommandList);
#endif
            try
            {
                NativeCommandList.Close();
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                if (m_CommandQueue is Dx12CommandQueue commandQueue)
                {
                    Dx12PipelineDebug.DumpDeviceMessages(commandQueue.Dx12Device, "[Dx12CommandBuffer.End]");
                }

                throw;
            }

            MarkEndSucceeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder GetTransferEncoder()
        {
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder GetComputeEncoder()
        {
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder GetRaytracingEncoder()
        {
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder GetRasterEncoder()
        {
            return m_RasterEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder GetMLEncoder()
        {
            return m_MLEncoder;
        }

        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.WorkGraph);
            m_WorkGraphEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.WorkGraph);
            return m_WorkGraphEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndWorkGraphPass()
        {
            m_WorkGraphEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIWorkGraphEncoder GetWorkGraphEncoder()
        {
            return m_WorkGraphEncoder;
        }

        internal void RegisterTransientCbvSrvUavDescriptor(
            in Dx12DescriptorInfo descriptor,
            int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            m_TransientCbvSrvUavDescriptors.Add((descriptor, count));
        }

        private void ReleaseTransientCbvSrvUavDescriptors()
        {
            Dx12Device device = Dx12EncoderGuards.RequireDevice(this);
            foreach ((Dx12DescriptorInfo descriptor, int count) in
                     m_TransientCbvSrvUavDescriptors)
            {
                device.FreeCbvSrvUavDescriptor(descriptor.Index, count);
            }
            m_TransientCbvSrvUavDescriptors.Clear();
        }

        protected override void Release()
        {
            ReleaseTransientCbvSrvUavDescriptors();
            m_WorkGraphEncoder?.ReleaseCommandListInterface();
            m_NativeCommandList?.Release(); m_NativeCommandList = null;
            m_NativeCommandAllocator?.Release(); m_NativeCommandAllocator = null;
            m_WorkGraphEncoder?.Dispose();
            m_MLEncoder?.Dispose();
            m_RaytracingEncoder?.Dispose();
            m_RasterEncoder?.Dispose();
            m_ComputeEncoder?.Dispose();
            m_TransferEncoder?.Dispose();
        }
    }
#pragma warning restore CA1416
}
