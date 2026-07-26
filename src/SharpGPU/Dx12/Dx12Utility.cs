using System;
using System.Text;
using SharpGPU.Core;
using System.Diagnostics;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

// TODO: follow-up — split Dx12Utility by domain (descriptor/barrier); left intact in layout convergence.

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe struct Dx12DescriptorInfo
    {
        public int Index;
        public Vortice.Direct3D12.ID3D12DescriptorHeap DescriptorHeap;
        public Vortice.Direct3D12.CpuDescriptorHandle CpuHandle;
        public Vortice.Direct3D12.GpuDescriptorHandle GpuHandle;
    };

    internal readonly struct Dx12DescriptorPair
    {
        public Dx12DescriptorInfo ShaderVisible { get; }
        public Dx12DescriptorInfo Staging { get; }
        public Dx12DescriptorHeap StagingHeap { get; }
        public Vortice.Direct3D12.DescriptorHeapType NativeType { get; }

        public Dx12DescriptorPair(
            in Dx12DescriptorInfo shaderVisible,
            in Dx12DescriptorInfo staging,
            Dx12DescriptorHeap stagingHeap,
            in Vortice.Direct3D12.DescriptorHeapType nativeType)
        {
            ShaderVisible = shaderVisible;
            Staging = staging;
            StagingHeap = stagingHeap;
            NativeType = nativeType;
        }
    }

    internal unsafe class Dx12DescriptorHeap : Disposal
    {
        public int Capacity => m_Capacity;
        public bool IsShaderVisible => m_IsShaderVisible;
        public uint DescriptorSize => m_DescriptorSize;
        public Vortice.Direct3D12.DescriptorHeapType NativeType => m_NativeType;
        public Vortice.Direct3D12.ID3D12DescriptorHeap NativeDescriptorHeap => m_NativeDescriptorHeap;
        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuStartHandle => m_NativeDescriptorHeap.GetCPUDescriptorHandleForHeapStart();
        public Vortice.Direct3D12.GpuDescriptorHandle NativeGpuStartHandle => m_IsShaderVisible ? m_NativeDescriptorHeap.GetGPUDescriptorHandleForHeapStart() : default;
        public int AvailableDescriptorCount
        {
            get
            {
                lock (m_AllocationGate)
                {
                    int available = 0;
                    for (int i = 0; i < m_FreeBlocks.Count; ++i)
                    {
                        available = checked(available + m_FreeBlocks.Values[i]);
                    }

                    return available;
                }
            }
        }

        private int m_Capacity;
        private readonly object m_AllocationGate = new();
        private bool m_IsShaderVisible;
        private uint m_DescriptorSize;
        private SortedList<int, int> m_FreeBlocks;
        private Vortice.Direct3D12.DescriptorHeapType m_NativeType;
        private Vortice.Direct3D12.ID3D12DescriptorHeap m_NativeDescriptorHeap;

        public Dx12DescriptorHeap(Vortice.Direct3D12.ID3D12Device10 device, in Vortice.Direct3D12.DescriptorHeapType type, in Vortice.Direct3D12.DescriptorHeapFlags flag, in uint count)
        {
            if (count == 0 || count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "DX12 descriptor heap capacity must be in the range [1, Int32.MaxValue].");
            }

            m_Capacity = checked((int)count);
            m_FreeBlocks = new SortedList<int, int>(16);
            m_FreeBlocks.Add(0, m_Capacity);

            m_NativeType = type;
            m_IsShaderVisible = (flag & Vortice.Direct3D12.DescriptorHeapFlags.ShaderVisible) != 0;
            m_DescriptorSize = device.GetDescriptorHandleIncrementSize(m_NativeType);

            Vortice.Direct3D12.DescriptorHeapDescription descriptorInfo = new Vortice.Direct3D12.DescriptorHeapDescription();
            descriptorInfo.Type = type;
            descriptorInfo.Flags = flag;
            descriptorInfo.DescriptorCount = count;

            Vortice.Direct3D12.ID3D12DescriptorHeap? nativeDescriptorHeap;
            SharpGen.Runtime.Result hResult = device.CreateDescriptorHeap(descriptorInfo, out nativeDescriptorHeap);
            m_NativeDescriptorHeap = Dx12Utility.RequireCreatedObject(
                nativeDescriptorHeap,
                hResult,
                "ID3D12Device.CreateDescriptorHeap");
        }

        public Dx12DescriptorInfo GetDescriptorInfo(in int index)
        {
            if ((uint)index >= (uint)m_Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"DX12 descriptor index must be in [0, {m_Capacity}).");
            }

            return new Dx12DescriptorInfo
            {
                Index = index,
                CpuHandle = NativeCpuStartHandle.Offset(index, DescriptorSize),
                GpuHandle = IsShaderVisible
                    ? NativeGpuStartHandle.Offset(index, DescriptorSize)
                    : default,
                DescriptorHeap = NativeDescriptorHeap,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate()
        {
            return Allocate(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate(in int count)
        {
            lock (m_AllocationGate)
            {
                if (count <= 0 || m_FreeBlocks.Count == 0)
                {
                    return -1;
                }

                for (int i = 0; i < m_FreeBlocks.Count; ++i)
                {
                    int blockStart = m_FreeBlocks.Keys[i];
                    int blockSize = m_FreeBlocks.Values[i];

                    if (blockSize >= count)
                    {
                        m_FreeBlocks.RemoveAt(i);

                        if (blockSize > count)
                        {
                            m_FreeBlocks.Add(blockStart + count, blockSize - count);
                        }

                        return blockStart;
                    }
                }

                return -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(in int index)
        {
            Free(index, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(in int index, in int count)
        {
            lock (m_AllocationGate)
            {
                if (count <= 0 || index < 0 || index > m_Capacity - count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"DX12 descriptor free range [{index}, {index + count}) is outside heap capacity {m_Capacity}.");
                }

                for (int i = 0; i < m_FreeBlocks.Count; ++i)
                {
                    int freeStart = m_FreeBlocks.Keys[i];
                    int freeEnd = checked(freeStart + m_FreeBlocks.Values[i]);
                    int releaseEnd = checked(index + count);
                    if (index < freeEnd && releaseEnd > freeStart)
                    {
                        throw new InvalidOperationException($"DX12 descriptor range [{index}, {releaseEnd}) overlaps the already free range [{freeStart}, {freeEnd}).");
                    }
                }

                int newStart = index;
                int newSize = count;

                // Try to coalesce with the block immediately after
                if (m_FreeBlocks.TryGetValue(index + count, out int afterSize))
                {
                    newSize += afterSize;
                    m_FreeBlocks.Remove(index + count);
                }

                // Try to coalesce with the block immediately before
                int beforeIndex = -1;
                for (int i = 0; i < m_FreeBlocks.Count; ++i)
                {
                    int blockStart = m_FreeBlocks.Keys[i];
                    int blockSize = m_FreeBlocks.Values[i];

                    if (blockStart + blockSize == index)
                    {
                        beforeIndex = i;
                        break;
                    }
                }

                if (beforeIndex >= 0)
                {
                    int blockStart = m_FreeBlocks.Keys[beforeIndex];
                    int blockSize = m_FreeBlocks.Values[beforeIndex];
                    newStart = blockStart;
                    newSize += blockSize;
                    m_FreeBlocks.RemoveAt(beforeIndex);
                }

                m_FreeBlocks.Add(newStart, newSize);
            }
        }

        protected override void Release()
        {
            lock (m_AllocationGate)
            {
                m_FreeBlocks.Clear();
                m_NativeDescriptorHeap.Release();
            }
        }
    }

    internal readonly struct Dx12CpuDescriptorAllocation
    {
        public Dx12DescriptorHeap Heap { get; }
        public Dx12DescriptorInfo Descriptor { get; }

        public Dx12CpuDescriptorAllocation(Dx12DescriptorHeap heap, in Dx12DescriptorInfo descriptor)
        {
            Heap = heap;
            Descriptor = descriptor;
        }
    }

    internal sealed class Dx12CpuDescriptorPool : Disposal
    {
        private readonly object m_Gate = new object();
        private readonly Vortice.Direct3D12.ID3D12Device10 m_Device;
        private readonly Vortice.Direct3D12.DescriptorHeapType m_NativeType;
        private readonly int m_PageCapacity;
        private readonly List<Dx12DescriptorHeap> m_Pages = new List<Dx12DescriptorHeap>();

        public Dx12CpuDescriptorPool(
            Vortice.Direct3D12.ID3D12Device10 device,
            in Vortice.Direct3D12.DescriptorHeapType nativeType,
            in int pageCapacity)
        {
            if (pageCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCapacity), pageCapacity, "DX12 CPU descriptor page capacity must be positive.");
            }

            m_Device = device;
            m_NativeType = nativeType;
            m_PageCapacity = pageCapacity;
        }

        public Dx12CpuDescriptorAllocation Allocate(in int count, string poolName)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, $"DX12 {poolName} allocation count must be positive.");
            }

            lock (m_Gate)
            {
                for (int i = 0; i < m_Pages.Count; ++i)
                {
                    Dx12DescriptorHeap page = m_Pages[i];
                    int index = page.Allocate(count);
                    if (index >= 0)
                    {
                        return new Dx12CpuDescriptorAllocation(page, page.GetDescriptorInfo(index));
                    }
                }

                int pageCapacity = Math.Max(m_PageCapacity, count);
                Dx12DescriptorHeap newPage = new Dx12DescriptorHeap(
                    m_Device,
                    m_NativeType,
                    Vortice.Direct3D12.DescriptorHeapFlags.None,
                    checked((uint)pageCapacity));
                try
                {
                    int index = newPage.Allocate(count);
                    if (index < 0)
                    {
                        throw new InvalidOperationException(
                            $"DX12 {poolName} failed to allocate {count} descriptors from a new page with capacity {pageCapacity}.");
                    }

                    m_Pages.Add(newPage);
                    return new Dx12CpuDescriptorAllocation(newPage, newPage.GetDescriptorInfo(index));
                }
                catch
                {
                    newPage.Dispose();
                    throw;
                }
            }
        }

        public void Free(Dx12DescriptorHeap page, in int index, in int count = 1)
        {
            lock (m_Gate)
            {
                if (!m_Pages.Contains(page))
                {
                    throw new ArgumentException("DX12 CPU descriptor allocation does not belong to this pool.", nameof(page));
                }

                page.Free(index, count);
            }
        }

        protected override void Release()
        {
            lock (m_Gate)
            {
                for (int i = m_Pages.Count - 1; i >= 0; --i)
                {
                    m_Pages[i].Dispose();
                }
                m_Pages.Clear();
            }
        }
    }

    internal static unsafe class Dx12Utility
    {
        public static void CHECK_BOOL(bool cond, [CallerFilePath] string __FILE__ = "", [CallerLineNumber] int __LINE__ = 0, [CallerArgumentExpression("cond")] string expr = "")
        {
            if (!cond)
            {
                throw new InvalidOperationException($"{__FILE__}({__LINE__}): !({(string.IsNullOrEmpty(expr) ? cond : expr)})");
            }
        }

        public static void CHECK_HR(int hr, [CallerFilePath] string __FILE__ = "", [CallerLineNumber] int __LINE__ = 0, [CallerArgumentExpression("hr")] string expr = "")
        {
            if (hr >= 0)
            {
                return;
            }

            ThrowNativeFailure(hr, expr);
        }

        public static void CHECK_HR(SharpGen.Runtime.Result hr, [CallerFilePath] string __FILE__ = "", [CallerLineNumber] int __LINE__ = 0, [CallerArgumentExpression("hr")] string expr = "")
        {
            if (hr.Success)
            {
                return;
            }

            ThrowNativeFailure(hr.Code, expr);
        }

        internal static SharpGen.Runtime.Result CreateCommandListWithoutInitialPipelineState(
            Vortice.Direct3D12.ID3D12Device10 device,
            uint nodeMask,
            Vortice.Direct3D12.CommandListType type,
            out Vortice.Direct3D12.ID3D12GraphicsCommandList7? commandList)
        {
            // CreateCommandList1 creates a closed command list without an initial PSO.
            // Begin() will Reset it against the caller's allocator.
            return device.CreateCommandList1<Vortice.Direct3D12.ID3D12GraphicsCommandList7>(
                nodeMask,
                type,
                Vortice.Direct3D12.CommandListFlags.None,
                out commandList);
        }

        internal static T RequireCreatedObject<T>(
            T? nativeObject,
            SharpGen.Runtime.Result result,
            string operation)
            where T : class
        {
            CHECK_HR(result);
            if (nativeObject != null)
            {
                return nativeObject;
            }

            throw new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.DirectX12,
                result.Code,
                $"{operation} succeeded without returning a native object.",
                ERHIDeviceState.Operational);
        }

        private static void ThrowNativeFailure(int nativeCode, string expression)
        {
            const int EOutOfMemory = unchecked((int)0x8007000E);
            const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
            const int DxgiErrorDeviceHung = unchecked((int)0x887A0006);
            const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

            ERHIErrorCode errorCode = nativeCode switch
            {
                EOutOfMemory => ERHIErrorCode.OutOfMemory,
                DxgiErrorDeviceRemoved or DxgiErrorDeviceHung or DxgiErrorDeviceReset => ERHIErrorCode.DeviceLost,
                _ => ERHIErrorCode.NativeFailure
            };
            ERHIDeviceState deviceState = nativeCode switch
            {
                DxgiErrorDeviceRemoved => ERHIDeviceState.Removed,
                DxgiErrorDeviceReset => ERHIDeviceState.Reset,
                DxgiErrorDeviceHung => ERHIDeviceState.Lost,
                _ => ERHIDeviceState.Operational
            };
            string nativeMessage = string.IsNullOrWhiteSpace(expression)
                ? $"HRESULT 0x{unchecked((uint)nativeCode):X8}"
                : $"{expression} failed with HRESULT 0x{unchecked((uint)nativeCode):X8}";
            throw new RHIException(
                errorCode,
                ERHIBackend.DirectX12,
                unchecked((uint)nativeCode),
                nativeMessage,
                deviceState);
        }

        internal static uint GetFormatBytesPerPixel(in ERHIPixelFormat format)
        {
            switch (format)
            {
                case ERHIPixelFormat.R8_UNorm:
                case ERHIPixelFormat.R8_SNorm:
                case ERHIPixelFormat.R8_UInt:
                case ERHIPixelFormat.R8_SInt:
                    return 1;

                case ERHIPixelFormat.R16_UInt:
                case ERHIPixelFormat.R16_SInt:
                case ERHIPixelFormat.R16_Float:
                case ERHIPixelFormat.R8G8_UNorm:
                case ERHIPixelFormat.R8G8_SNorm:
                case ERHIPixelFormat.R8G8_UInt:
                case ERHIPixelFormat.R8G8_SInt:
                case ERHIPixelFormat.D16_UNorm:
                    return 2;

                case ERHIPixelFormat.R32_UInt:
                case ERHIPixelFormat.R32_SInt:
                case ERHIPixelFormat.R32_Float:
                case ERHIPixelFormat.R16G16_UInt:
                case ERHIPixelFormat.R16G16_SInt:
                case ERHIPixelFormat.R16G16_Float:
                case ERHIPixelFormat.R8G8B8A8_UInt:
                case ERHIPixelFormat.R8G8B8A8_SInt:
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                case ERHIPixelFormat.B8G8R8A8_UNorm:
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                case ERHIPixelFormat.R10G10B10A2_UInt:
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                case ERHIPixelFormat.R11G11B10_Float:
                case ERHIPixelFormat.R99GB99_E5_Float:
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                case ERHIPixelFormat.D32_Float:
                    return 4;

                case ERHIPixelFormat.RG32_UInt:
                case ERHIPixelFormat.RG32_SInt:
                case ERHIPixelFormat.RG32_Float:
                case ERHIPixelFormat.R16G16B16A16_UInt:
                case ERHIPixelFormat.R16G16B16A16_SInt:
                case ERHIPixelFormat.R16G16B16A16_Float:
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return 8;

                case ERHIPixelFormat.R32G32B32A32_UInt:
                case ERHIPixelFormat.R32G32B32A32_SInt:
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return 16;

                // Block-compressed formats: return 0, use GetBlockCompressedRowPitch instead
                default:
                    return 0;
            }
        }

        internal static uint ComputeRowPitch(in ERHIPixelFormat format, in uint width)
        {
            uint bytesPerPixel = GetFormatBytesPerPixel(format);
            uint rowPitch;

            if (bytesPerPixel > 0)
            {
                rowPitch = width * bytesPerPixel;
            }
            else
            {
                // Block-compressed formats: 4x4 block size
                uint blockWidth = (width + 3) / 4;
                uint bytesPerBlock;

                switch (format)
                {
                    case ERHIPixelFormat.RGB_DXT1_UNorm:
                    case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    case ERHIPixelFormat.R_BC4_UNorm:
                    case ERHIPixelFormat.R_BC4_SNorm:
                        bytesPerBlock = 8;
                        break;

                    default:
                        // BC2, BC3, BC5, BC6H, BC7 all use 16 bytes per block
                        bytesPerBlock = 16;
                        break;
                }

                rowPitch = blockWidth * bytesPerBlock;
            }

            // Align to D3D12_TEXTURE_DATA_PITCH_ALIGNMENT (256)
            return ((rowPitch + 255) / 256) * 256;
        }

        internal static Vortice.Direct3D12.QueryType ConvertToDx12QueryType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return Vortice.Direct3D12.QueryType.Occlusion;

                case ERHIQueryType.Statistics:
                    return Vortice.Direct3D12.QueryType.PipelineStatistics;

                case ERHIQueryType.TimestampTransfer:
                case ERHIQueryType.Timestamp:
                    return Vortice.Direct3D12.QueryType.Timestamp;

                default:
                    return 0;
            }
        }

        internal static Vortice.Direct3D12.QueryHeapType ConvertToDx12QueryHeapType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return Vortice.Direct3D12.QueryHeapType.Occlusion;

                case ERHIQueryType.Statistics:
                    return Vortice.Direct3D12.QueryHeapType.PipelineStatistics;

                case ERHIQueryType.TimestampTransfer:
                    return Vortice.Direct3D12.QueryHeapType.CopyQueueTimestamp;

                case ERHIQueryType.Timestamp:
                    return Vortice.Direct3D12.QueryHeapType.Timestamp;

                default:
                    return 0;
            }
        }

        internal static Vortice.Direct3D12.CommandListType ConvertToDx12QueueType(in ERHIPipelineType pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.CommandListType.Compute;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.CommandListType.Direct;

                default:
                    return Vortice.Direct3D12.CommandListType.Copy;
            }
        }

        internal static uint ConvertToDx12SyncInterval(in ERHIPresentMode presentMode)
        {
            switch (presentMode)
            {
                case ERHIPresentMode.VSync:
                    return 1;

                case ERHIPresentMode.Immediately:
                    return 0;

                default:
                    return 0;
            }
        }

        internal static Vortice.DXGI.SwapEffect ConvertToDx12SwapEffect(in ERHIPresentMode presentMode)
        {
            switch (presentMode)
            {
                case ERHIPresentMode.VSync:
                    return Vortice.DXGI.SwapEffect.FlipSequential;

                case ERHIPresentMode.Immediately:
                    return Vortice.DXGI.SwapEffect.FlipDiscard;

                default:
                    return Vortice.DXGI.SwapEffect.FlipDiscard;
            }
        }

        internal static Vortice.Direct3D12.Filter ConvertToDx12Filter(in RHISamplerDescriptor descriptor)
        {
            ERHIFilterMode minFilter = descriptor.MinFilter;
            ERHIFilterMode magFilter = descriptor.MagFilter;
            ERHIFilterMode mipFilter = descriptor.MipFilter;

            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Point) { return Vortice.Direct3D12.Filter.MinMagMipPoint; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Linear) { return Vortice.Direct3D12.Filter.MinMagPointMipLinear; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Point) { return Vortice.Direct3D12.Filter.MinPointMagLinearMipPoint; }
            if (minFilter == ERHIFilterMode.Point && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Linear) { return Vortice.Direct3D12.Filter.MinPointMagMipLinear; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Point) { return Vortice.Direct3D12.Filter.MinLinearMagMipPoint; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Point && mipFilter == ERHIFilterMode.Linear) { return Vortice.Direct3D12.Filter.MinLinearMagPointMipLinear; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Point) { return Vortice.Direct3D12.Filter.MinMagLinearMipPoint; }
            if (minFilter == ERHIFilterMode.Linear && magFilter == ERHIFilterMode.Linear && mipFilter == ERHIFilterMode.Linear) { return Vortice.Direct3D12.Filter.MinMagMipLinear; }
            if (minFilter == ERHIFilterMode.Anisotropic || magFilter == ERHIFilterMode.Anisotropic || mipFilter == ERHIFilterMode.Anisotropic) { return Vortice.Direct3D12.Filter.Anisotropic; }
            return Vortice.Direct3D12.Filter.MinMagMipPoint;
        }

        internal static Vortice.Direct3D12.TextureAddressMode ConvertToDx12AddressMode(in ERHIAddressMode addressMode)
        {
            switch (addressMode)
            {
                case ERHIAddressMode.MirrorRepeat:
                    return Vortice.Direct3D12.TextureAddressMode.Mirror;

                case ERHIAddressMode.ClampToEdge:
                    return Vortice.Direct3D12.TextureAddressMode.Clamp;
            }
            return Vortice.Direct3D12.TextureAddressMode.Wrap;
        }

        // convert to dx12 format COMPARISON func
        internal static Vortice.Direct3D12.ComparisonFunction ConvertToDx12ComparisonMode(in ERHIComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case ERHIComparisonMode.Less:
                    return Vortice.Direct3D12.ComparisonFunction.Less;

                case ERHIComparisonMode.Equal:
                    return Vortice.Direct3D12.ComparisonFunction.Equal;

                case ERHIComparisonMode.LessEqual:
                    return Vortice.Direct3D12.ComparisonFunction.LessEqual;

                case ERHIComparisonMode.Greater:
                    return Vortice.Direct3D12.ComparisonFunction.Greater;

                case ERHIComparisonMode.NotEqual:
                    return Vortice.Direct3D12.ComparisonFunction.NotEqual;

                case ERHIComparisonMode.GreaterEqual:
                    return Vortice.Direct3D12.ComparisonFunction.GreaterEqual;

                case ERHIComparisonMode.Always:
                    return Vortice.Direct3D12.ComparisonFunction.Always;
            }

            return Vortice.Direct3D12.ComparisonFunction.Never;
        }

        internal static Vortice.Direct3D12.HeapType ConvertToDx12HeapTypeByStorage(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.HostUpload:
                    return Vortice.Direct3D12.HeapType.Upload;

                case ERHIStorageMode.Readback:
                    return Vortice.Direct3D12.HeapType.Readback;

                default:
                    return Vortice.Direct3D12.HeapType.Default;
            }
        }

        internal static Vortice.Direct3D12.ShadingRate ConvertToDx12ShadingRate(in ERHIShadingRate shadingRate)
        {
            switch (shadingRate)
            {
                case ERHIShadingRate.Rate1x1:
                    return Vortice.Direct3D12.ShadingRate.Rate1x1;

                case ERHIShadingRate.Rate1x2:
                    return Vortice.Direct3D12.ShadingRate.Rate1x2;

                case ERHIShadingRate.Rate2x1:
                    return Vortice.Direct3D12.ShadingRate.Rate2x1;

                case ERHIShadingRate.Rate2x2:
                    return Vortice.Direct3D12.ShadingRate.Rate2x2;

                case ERHIShadingRate.Rate2x4:
                    return Vortice.Direct3D12.ShadingRate.Rate2x4;

                case ERHIShadingRate.Rate4x2:
                    return Vortice.Direct3D12.ShadingRate.Rate4x2;

                default:
                    return Vortice.Direct3D12.ShadingRate.Rate4x4;
            }
        }

        internal static Vortice.Direct3D12.ShadingRateCombiner ConvertToDx12ShadingRateCombiner(in ERHIShadingRateCombiner shadingRateCombiner)
        {
            switch (shadingRateCombiner)
            {
                case ERHIShadingRateCombiner.Min:
                    return Vortice.Direct3D12.ShadingRateCombiner.Min;

                case ERHIShadingRateCombiner.Max:
                    return Vortice.Direct3D12.ShadingRateCombiner.Max;

                case ERHIShadingRateCombiner.Sum:
                    return Vortice.Direct3D12.ShadingRateCombiner.Sum;

                case ERHIShadingRateCombiner.Override:
                    return Vortice.Direct3D12.ShadingRateCombiner.Override;

                default:
                    return Vortice.Direct3D12.ShadingRateCombiner.Passthrough;
            }
        }

        internal static Vortice.Direct3D12.FillMode ConvertToDx12FillMode(in ERHIFillMode fillMode)
        {
            switch (fillMode)
            {
                case ERHIFillMode.Solid:
                    return Vortice.Direct3D12.FillMode.Solid;

                case ERHIFillMode.Wireframe:
                    return Vortice.Direct3D12.FillMode.Wireframe;

                default:
                    return Vortice.Direct3D12.FillMode.Solid;
            }
        }

        internal static Vortice.Direct3D12.CullMode ConvertToDx12CullMode(in ERHICullMode cullMode)
        {
            switch (cullMode)
            {
                case ERHICullMode.None:
                    return Vortice.Direct3D12.CullMode.None;

                case ERHICullMode.Back:
                    return Vortice.Direct3D12.CullMode.Back;

                case ERHICullMode.Front:
                    return Vortice.Direct3D12.CullMode.Front;

                default:
                    return Vortice.Direct3D12.CullMode.Back;
            }
        }

        internal static Vortice.Direct3D12.BlendOperation ConvertToDx12BlendOp(in ERHIBlendOp blendOp)
        {
            switch (blendOp)
            {
                case ERHIBlendOp.Add:
                    return Vortice.Direct3D12.BlendOperation.Add;

                case ERHIBlendOp.Substract:
                    return Vortice.Direct3D12.BlendOperation.Subtract;

                case ERHIBlendOp.ReverseSubstract:
                    return Vortice.Direct3D12.BlendOperation.RevSubtract;

                case ERHIBlendOp.Min:
                    return Vortice.Direct3D12.BlendOperation.Min;

                case ERHIBlendOp.Max:
                    return Vortice.Direct3D12.BlendOperation.Max;

                default:
                    return Vortice.Direct3D12.BlendOperation.Add;
            }
        }

        internal static Vortice.Direct3D12.Blend ConvertToDx12BlendMode(in ERHIBlendMode blendMode)
        {
            switch (blendMode)
            {
                case ERHIBlendMode.Zero:
                    return Vortice.Direct3D12.Blend.Zero;

                case ERHIBlendMode.One:
                    return Vortice.Direct3D12.Blend.One;

                case ERHIBlendMode.SrcColor:
                    return Vortice.Direct3D12.Blend.SourceColor;

                case ERHIBlendMode.OneMinusSrcColor:
                    return Vortice.Direct3D12.Blend.InverseSourceColor;

                case ERHIBlendMode.SrcAlpha:
                    return Vortice.Direct3D12.Blend.SourceAlpha;

                case ERHIBlendMode.OneMinusSrcAlpha:
                    return Vortice.Direct3D12.Blend.InverseSourceAlpha;

                case ERHIBlendMode.DstColor:
                    return Vortice.Direct3D12.Blend.DestinationColor;

                case ERHIBlendMode.OneMinusDstColor:
                    return Vortice.Direct3D12.Blend.InverseDestinationColor;

                case ERHIBlendMode.DstAlpha:
                    return Vortice.Direct3D12.Blend.DestinationAlpha;

                case ERHIBlendMode.OneMinusDstAlpha:
                    return Vortice.Direct3D12.Blend.InverseDestinationAlpha;

                case ERHIBlendMode.SrcAlphaSaturate:
                    return Vortice.Direct3D12.Blend.SourceAlphaSaturate;

                case ERHIBlendMode.BlendFactor:
                    return Vortice.Direct3D12.Blend.BlendFactor;

                case ERHIBlendMode.InverseBlendFactor:
                    return Vortice.Direct3D12.Blend.InverseBlendFactor;

                case ERHIBlendMode.SecondarySourceColor:
                    return Vortice.Direct3D12.Blend.Source1Color;

                case ERHIBlendMode.InverseSecondarySourceColor:
                    return Vortice.Direct3D12.Blend.InverseSource1Color;

                case ERHIBlendMode.SecondarySourceAlpha:
                    return Vortice.Direct3D12.Blend.Source1Alpha;

                case ERHIBlendMode.InverseSecondarySourceAlpha:
                    return Vortice.Direct3D12.Blend.InverseSource1Alpha;

                default:
                    return Vortice.Direct3D12.Blend.Zero;
            }
        }

        internal static byte ConvertToDx12WriteChannel(in ERHIColorWriteChannel writeChannel)
        {
            byte result = 0;

            if ((writeChannel & ERHIColorWriteChannel.Red) != 0) result |= (byte)Vortice.Direct3D12.ColorWriteEnable.Red;
            if ((writeChannel & ERHIColorWriteChannel.Green) != 0) result |= (byte)Vortice.Direct3D12.ColorWriteEnable.Green;
            if ((writeChannel & ERHIColorWriteChannel.Blue) != 0) result |= (byte)Vortice.Direct3D12.ColorWriteEnable.Blue;
            if ((writeChannel & ERHIColorWriteChannel.Alpha) != 0) result |= (byte)Vortice.Direct3D12.ColorWriteEnable.Alpha;

            return result;
        }

        internal static Vortice.Direct3D12.ResourceStates ConvertToDx12BufferStateByFlag(in ERHIBufferUsage bufferFlag)
        {
            /*Dictionary<ERHIBufferUsage, Vortice.Direct3D12.ResourceStates> stateRules = new Dictionary<ERHIBufferUsage, Vortice.Direct3D12.ResourceStates>();
            stateRules.Add(ERHIBufferUsage.CopySrc, Vortice.Direct3D12.ResourceStates.CopySource);
            stateRules.Add(ERHIBufferUsage.CopyDst, Vortice.Direct3D12.ResourceStates.CopyDest);
            stateRules.Add(ERHIBufferUsage.Index, Vortice.Direct3D12.ResourceStates.GenericRead);
            stateRules.Add(ERHIBufferUsage.Vertex, Vortice.Direct3D12.ResourceStates.GenericRead);
            stateRules.Add(ERHIBufferUsage.Uniform, Vortice.Direct3D12.ResourceStates.GenericRead);
            stateRules.Add(ERHIBufferUsage.Indirect, Vortice.Direct3D12.ResourceStates.GenericRead);
            stateRules.Add(ERHIBufferUsage.StorageResource, Vortice.Direct3D12.ResourceStates.UnorderedAccess);*/

            Vortice.Direct3D12.ResourceStates result = Vortice.Direct3D12.ResourceStates.Common;
            /*foreach (KeyValuePair<ERHIBufferUsage, Vortice.Direct3D12.ResourceStates> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static Vortice.Direct3D12.ResourceStates ConvertToDx12TextureStateByFlag(in ERHITextureUsage textureflag)
        {
            /*Dictionary<ERHITextureUsage, Vortice.Direct3D12.ResourceStates> stateRules = new Dictionary<ERHITextureUsage, Vortice.Direct3D12.ResourceStates>();
            stateRules.Add(ERHITextureUsage.CopySrc, Vortice.Direct3D12.ResourceStates.CopySource);
            stateRules.Add(ERHITextureUsage.CopyDst, Vortice.Direct3D12.ResourceStates.CopyDest);
            stateRules.Add(ERHITextureUsage.DepthAttachment, Vortice.Direct3D12.ResourceStates.DepthWrite);
            stateRules.Add(ERHITextureUsage.ColorAttachment, Vortice.Direct3D12.ResourceStates.RenderTarget);
            stateRules.Add(ERHITextureUsage.ShaderResource, Vortice.Direct3D12.ResourceStates.Common);
            stateRules.Add(ERHITextureUsage.StorageResource, Vortice.Direct3D12.ResourceStates.UnorderedAccess);*/

            Vortice.Direct3D12.ResourceStates result = Vortice.Direct3D12.ResourceStates.Common;
            /*foreach (KeyValuePair<ERHITextureUsage, Vortice.Direct3D12.ResourceStates> rule in stateRules)
            {
                if ((textureUsages & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }*/

            return result;
        }

        internal static Vortice.Direct3D12.ResourceFlags ConvertToDx12BufferFlag(in ERHIBufferUsage bufferflag)
        {
            Dictionary<ERHIBufferUsage, Vortice.Direct3D12.ResourceFlags> stateRules = new Dictionary<ERHIBufferUsage, Vortice.Direct3D12.ResourceFlags>();
            stateRules.Add(ERHIBufferUsage.UnorderedAccess, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess);

            Vortice.Direct3D12.ResourceFlags result = Vortice.Direct3D12.ResourceFlags.None;
            foreach (KeyValuePair<ERHIBufferUsage, Vortice.Direct3D12.ResourceFlags> rule in stateRules)
            {
                if ((bufferflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static Vortice.Direct3D12.ResourceFlags ConvertToDx12TextureFlag(in ERHITextureUsage textureflag)
        {
            Dictionary<ERHITextureUsage, Vortice.Direct3D12.ResourceFlags> stateRules = new Dictionary<ERHITextureUsage, Vortice.Direct3D12.ResourceFlags>();
            stateRules.Add(ERHITextureUsage.DepthStencil, Vortice.Direct3D12.ResourceFlags.AllowDepthStencil);
            stateRules.Add(ERHITextureUsage.RenderTarget, Vortice.Direct3D12.ResourceFlags.AllowRenderTarget);
            stateRules.Add(ERHITextureUsage.UnorderedAccess, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess);
            stateRules.Add(ERHITextureUsage.RasterizerOrdered, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess);

            Vortice.Direct3D12.ResourceFlags result = Vortice.Direct3D12.ResourceFlags.None;
            foreach (KeyValuePair<ERHITextureUsage, Vortice.Direct3D12.ResourceFlags> rule in stateRules)
            {
                if ((textureflag & rule.Key) == rule.Key)
                {
                    result |= rule.Value;
                }
            }

            return result;
        }

        internal static Vortice.Direct3D12.ResourceDimension ConvertToDx12TextureDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2D:
                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.Texture2DMS:
                case ERHITextureDimension.Texture2DArrayMS:
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    return Vortice.Direct3D12.ResourceDimension.Texture2D;

                case ERHITextureDimension.Texture3D:
                    return Vortice.Direct3D12.ResourceDimension.Texture3D;

                default:
                    return Vortice.Direct3D12.ResourceDimension.Unknown;
            }
        }

        internal static Vortice.Direct3D12.ResourceStates ConvertToDx12BufferState(in ERHIBufferState state)
        {
            if (state == ERHIBufferState.Undefine)
                return Vortice.Direct3D12.ResourceStates.Common;

            Vortice.Direct3D12.ResourceStates result = Vortice.Direct3D12.ResourceStates.Common;

            if ((state & ERHIBufferState.CopyDst) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((state & ERHIBufferState.CopySrc) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((state & ERHIBufferState.IndexBuffer) != 0) result |= Vortice.Direct3D12.ResourceStates.IndexBuffer;
            if ((state & ERHIBufferState.VertexBuffer) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((state & ERHIBufferState.ConstantBuffer) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((state & ERHIBufferState.IndirectArgument) != 0) result |= Vortice.Direct3D12.ResourceStates.IndirectArgument;
            if ((state & ERHIBufferState.ShaderResource) != 0) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((state & ERHIBufferState.UnorderedAccess) != 0) result |= Vortice.Direct3D12.ResourceStates.UnorderedAccess;
            if ((state & ERHIBufferState.AccelStructRead) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((state & ERHIBufferState.AccelStructWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((state & ERHIBufferState.AccelStructBuildInput) != 0) result |= Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((state & ERHIBufferState.AccelStructBuildBlast) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;

            return result;
        }

        internal static Vortice.Direct3D12.ResourceStates ConvertToDx12ResourceStateFormStorageMode(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.HostUpload:
                    return Vortice.Direct3D12.ResourceStates.GenericRead;

                case ERHIStorageMode.Readback:
                    return Vortice.Direct3D12.ResourceStates.CopyDest;

                default:
                    return Vortice.Direct3D12.ResourceStates.Common;
            }
        }

        internal static Vortice.Direct3D12.ResourceStates ConvertToDx12TextureState(in ERHITextureState state)
        {
            if (state == ERHITextureState.Undefine)
            {
                return Vortice.Direct3D12.ResourceStates.Common;
            }

            Vortice.Direct3D12.ResourceStates result = Vortice.Direct3D12.ResourceStates.Common;

            if ((state & ERHITextureState.Present) != 0) result |= Vortice.Direct3D12.ResourceStates.Present;
            //if ((state & ERHITextureState.GenericRead) != 0) result |= Vortice.Direct3D12.ResourceStates.GenericRead;
            if ((state & ERHITextureState.CopyDst) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((state & ERHITextureState.CopySrc) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((state & ERHITextureState.ResolveDst) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveDest;
            if ((state & ERHITextureState.ResolveSrc) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveSource;
            if ((state & ERHITextureState.DepthRead) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthRead;
            if ((state & ERHITextureState.DepthWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthWrite;
            if ((state & ERHITextureState.RenderTarget) != 0) result |= Vortice.Direct3D12.ResourceStates.RenderTarget;
            if ((state & ERHITextureState.ShaderResource) != 0) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((state & ERHITextureState.UnorderedAccess) != 0) result |= Vortice.Direct3D12.ResourceStates.UnorderedAccess;
            if ((state & ERHITextureState.ShadingRateSurface) != 0) result |= Vortice.Direct3D12.ResourceStates.ShadingRateSource;

            return result;
        }

        internal static Vortice.Direct3D12.RaytracingGeometryFlags ConvertToDx12AccelStructGeometryFlag(in ERHIAccelStructGeometryFlag geometryFlag)
        {
            if (geometryFlag == ERHIAccelStructGeometryFlag.None)
            {
                return Vortice.Direct3D12.RaytracingGeometryFlags.None;
            }

            Vortice.Direct3D12.RaytracingGeometryFlags result = Vortice.Direct3D12.RaytracingGeometryFlags.None;

            if ((geometryFlag & ERHIAccelStructGeometryFlag.Opaque) != 0) result |= Vortice.Direct3D12.RaytracingGeometryFlags.Opaque;
            if ((geometryFlag & ERHIAccelStructGeometryFlag.NoDuplicateAnyhitInverseOcation) != 0) result |= Vortice.Direct3D12.RaytracingGeometryFlags.NoDuplicateAnyHitInvocation;

            return result;
        }

        internal static Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags ConvertToDx12AccelStructGeometryFlag(in ERHIAccelStructFlag buildFlag)
        {
            if (buildFlag == ERHIAccelStructFlag.None)
            {
                return Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.None;
            }

            Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags result = Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.None;

            if ((buildFlag & ERHIAccelStructFlag.AllowUpdate) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.AllowUpdate;
            if ((buildFlag & ERHIAccelStructFlag.PerformUpdate) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.PerformUpdate;
            if ((buildFlag & ERHIAccelStructFlag.MinimizeMemory) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.MinimizeMemory;
            if ((buildFlag & ERHIAccelStructFlag.PreferFastTrace) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.PreferFastTrace; 
            if ((buildFlag & ERHIAccelStructFlag.PreferFastBuild) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.PreferFastBuild;
            if ((buildFlag & ERHIAccelStructFlag.AllowCompaction) != 0) result |= Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.AllowCompaction;

            return result;
        }

        internal static Vortice.Direct3D.PrimitiveTopology ConvertToDx12PrimitiveTopology(in ERHIPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return Vortice.Direct3D.PrimitiveTopology.PointList;

                case ERHIPrimitiveTopology.LineList:
                    return Vortice.Direct3D.PrimitiveTopology.LineList;

                case ERHIPrimitiveTopology.LineStrip:
                    return Vortice.Direct3D.PrimitiveTopology.LineStrip;

                case ERHIPrimitiveTopology.TriangleList:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleList;

                case ERHIPrimitiveTopology.TriangleStrip:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleStrip;

                case ERHIPrimitiveTopology.LineListAdj:
                    return Vortice.Direct3D.PrimitiveTopology.LineListAdjacency;

                case ERHIPrimitiveTopology.LineStripAdj:
                    return Vortice.Direct3D.PrimitiveTopology.LineStripAdjacency;

                case ERHIPrimitiveTopology.TriangleListAdj:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleListAdjacency;

                case ERHIPrimitiveTopology.TriangleStripAdj:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleStripAdjacency;

                default:
                    return Vortice.Direct3D.PrimitiveTopology.Undefined;
            }
        }

        internal static Vortice.Direct3D12.PrimitiveTopologyType ConvertToDx12PrimitiveTopologyType(in ERHIPrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return Vortice.Direct3D12.PrimitiveTopologyType.Point;

                case ERHIPrimitiveTopology.LineList:
                case ERHIPrimitiveTopology.LineStrip:
                case ERHIPrimitiveTopology.LineListAdj:
                case ERHIPrimitiveTopology.LineStripAdj:
                    return Vortice.Direct3D12.PrimitiveTopologyType.Line;

                case ERHIPrimitiveTopology.TriangleList:
                case ERHIPrimitiveTopology.TriangleStrip:
                case ERHIPrimitiveTopology.TriangleListAdj:
                case ERHIPrimitiveTopology.TriangleStripAdj:
                    return Vortice.Direct3D12.PrimitiveTopologyType.Triangle;

                default:
                    return Vortice.Direct3D12.PrimitiveTopologyType.Undefined;
            }
        }

        internal static unsafe Vortice.Direct3D12.BlendDescription CreateDx12BlendState(in RHIBlendStateDescriptor blendStateDescriptor)
        {
            Vortice.Direct3D12.BlendDescription blendDescription = new Vortice.Direct3D12.BlendDescription();
            blendDescription.AlphaToCoverageEnable = blendStateDescriptor.AlphaToCoverage;
            blendDescription.IndependentBlendEnable = blendStateDescriptor.IndependentBlend;
            fixed (RHIBlendDescriptor* blendDescriptorPtr = &blendStateDescriptor.BlendDescriptor0)
            {
                for (int i = 0; i < 8; i++)
                {
                    ref readonly RHIBlendDescriptor source = ref blendDescriptorPtr[blendStateDescriptor.IndependentBlend ? i : 0];
                    blendDescription.RenderTarget[i].BlendEnable = source.BlendEnable;
                    blendDescription.RenderTarget[i].LogicOpEnable = false;
                    blendDescription.RenderTarget[i].BlendOperation = ConvertToDx12BlendOp(source.BlendOpColor);
                    blendDescription.RenderTarget[i].SourceBlend = ConvertToDx12BlendMode(source.SrcBlendColor);
                    blendDescription.RenderTarget[i].DestinationBlend = ConvertToDx12BlendMode(source.DstBlendColor);
                    blendDescription.RenderTarget[i].BlendOperationAlpha = ConvertToDx12BlendOp(source.BlendOpAlpha);
                    blendDescription.RenderTarget[i].SourceBlendAlpha = ConvertToDx12BlendMode(source.SrcBlendAlpha);
                    blendDescription.RenderTarget[i].DestinationBlendAlpha = ConvertToDx12BlendMode(source.DstBlendAlpha);
                    blendDescription.RenderTarget[i].LogicOp = Vortice.Direct3D12.LogicOp.Noop;
                    blendDescription.RenderTarget[i].RenderTargetWriteMask = (Vortice.Direct3D12.ColorWriteEnable)ConvertToDx12WriteChannel(source.ColorWriteChannel);
                }
            }
            return blendDescription;
        }

        internal static Vortice.Direct3D12.RasterizerDescription CreateDx12RasterizerState(in RHIRasterizerStateDescriptor description, bool bMultisample)
        {
            Vortice.Direct3D12.RasterizerDescription rasterDescription;
            rasterDescription.FillMode = ConvertToDx12FillMode(description.FillMode);
            rasterDescription.CullMode = ConvertToDx12CullMode(description.CullMode);
            rasterDescription.ForcedSampleCount = 0;
            rasterDescription.MultisampleEnable = bMultisample;
            rasterDescription.DepthBias = (int)description.DepthBias;
            rasterDescription.DepthBiasClamp = description.DepthBiasClamp;
            rasterDescription.DepthClipEnable = description.DepthClipEnable;
            rasterDescription.SlopeScaledDepthBias = description.SlopeScaledDepthBias;
            rasterDescription.AntialiasedLineEnable = description.AntialiasedLineEnable;
            rasterDescription.FrontCounterClockwise = description.FrontCounterClockwise;
            rasterDescription.ConservativeRaster = description.ConservativeRaster ? Vortice.Direct3D12.ConservativeRasterizationMode.On : Vortice.Direct3D12.ConservativeRasterizationMode.Off;
            return rasterDescription;
        }

        internal static Vortice.Direct3D12.StencilOperation ConvertToDx12StencilOp(in ERHIStencilOp stencilOp)
        {
            switch (stencilOp)
            {
                case ERHIStencilOp.Keep:
                    return Vortice.Direct3D12.StencilOperation.Keep;

                case ERHIStencilOp.Zero:
                    return Vortice.Direct3D12.StencilOperation.Zero;

                case ERHIStencilOp.Replace:
                    return Vortice.Direct3D12.StencilOperation.Replace;

                case ERHIStencilOp.IncrementSaturation:
                    return Vortice.Direct3D12.StencilOperation.IncrementSaturate;

                case ERHIStencilOp.DecrementSaturation:
                    return Vortice.Direct3D12.StencilOperation.DecrementSaturate;

                case ERHIStencilOp.Invert:
                    return Vortice.Direct3D12.StencilOperation.Invert;

                case ERHIStencilOp.Increment:
                    return Vortice.Direct3D12.StencilOperation.Increment;

                case ERHIStencilOp.Decrement:
                    return Vortice.Direct3D12.StencilOperation.Decrement;
            }
            return Vortice.Direct3D12.StencilOperation.Keep;
        }

        internal static Vortice.Direct3D12.ComparisonFunction ConvertToDx12Comparison(in ERHIComparisonMode comparisonMode)
        {
            switch (comparisonMode)
            {
                case ERHIComparisonMode.Never:
                    return Vortice.Direct3D12.ComparisonFunction.Never;

                case ERHIComparisonMode.Less:
                    return Vortice.Direct3D12.ComparisonFunction.Less;

                case ERHIComparisonMode.Equal:
                    return Vortice.Direct3D12.ComparisonFunction.Equal;

                case ERHIComparisonMode.LessEqual:
                    return Vortice.Direct3D12.ComparisonFunction.LessEqual;

                case ERHIComparisonMode.Greater:
                    return Vortice.Direct3D12.ComparisonFunction.Greater;

                case ERHIComparisonMode.NotEqual:
                    return Vortice.Direct3D12.ComparisonFunction.NotEqual;

                case ERHIComparisonMode.GreaterEqual:
                    return Vortice.Direct3D12.ComparisonFunction.GreaterEqual;

                case ERHIComparisonMode.Always:
                    return Vortice.Direct3D12.ComparisonFunction.Always;
            }
            return 0;
        }

        internal static Vortice.Direct3D12.DepthStencilDescription CreateDx12DepthStencilState(in RHIDepthStencilStateDescriptor depthStencilStateDescriptor)
        {
            if (!depthStencilStateDescriptor.DepthEnable && !depthStencilStateDescriptor.StencilEnable)
            {
                return Vortice.Direct3D12.DepthStencilDescription.None;
            }
            Vortice.Direct3D12.DepthStencilDescription depthStencilDescription = new Vortice.Direct3D12.DepthStencilDescription
            {
                DepthEnable = depthStencilStateDescriptor.DepthEnable,
                DepthFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.ComparisonMode),
                DepthWriteMask = depthStencilStateDescriptor.DepthWriteMask ? Vortice.Direct3D12.DepthWriteMask.All : Vortice.Direct3D12.DepthWriteMask.Zero,
                StencilEnable = depthStencilStateDescriptor.StencilEnable,
                StencilReadMask = depthStencilStateDescriptor.StencilReadMask,
                StencilWriteMask = depthStencilStateDescriptor.StencilWriteMask
            };
            Vortice.Direct3D12.DepthStencilOperationDescription frontFaceDescription = new Vortice.Direct3D12.DepthStencilOperationDescription
            {
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.FrontFace.ComparisonMode),
                StencilFailOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.FrontFace.StencilFailOp),
                StencilPassOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.FrontFace.StencilPassOp),
                StencilDepthFailOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.FrontFace.StencilDepthFailOp)
            };
            depthStencilDescription.FrontFace = frontFaceDescription;

            Vortice.Direct3D12.DepthStencilOperationDescription backFaceDescription = new Vortice.Direct3D12.DepthStencilOperationDescription
            {
                StencilFunc = ConvertToDx12Comparison(depthStencilStateDescriptor.BackFace.ComparisonMode),
                StencilFailOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.BackFace.StencilFailOp),
                StencilPassOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.BackFace.StencilPassOp),
                StencilDepthFailOp = ConvertToDx12StencilOp(depthStencilStateDescriptor.BackFace.StencilDepthFailOp)
            };
            depthStencilDescription.BackFace = backFaceDescription;
            return depthStencilDescription;
        }

        internal static Vortice.DXGI.Format ConvertToDx12SemanticFormat(in ERHISemanticFormat format)
        {
            switch (format)
            {
                case ERHISemanticFormat.Byte:
                    return Vortice.DXGI.Format.R8_SInt;

                case ERHISemanticFormat.Byte2:
                    return Vortice.DXGI.Format.R8G8_SInt;

                case ERHISemanticFormat.Byte4:
                    return Vortice.DXGI.Format.R8G8B8A8_SInt;

                case ERHISemanticFormat.UByte:
                    return Vortice.DXGI.Format.R8_UInt;

                case ERHISemanticFormat.UByte2:
                    return Vortice.DXGI.Format.R8G8_UInt;

                case ERHISemanticFormat.UByte4:
                    return Vortice.DXGI.Format.R8G8B8A8_UInt;

                case ERHISemanticFormat.ByteNormalized:
                    return Vortice.DXGI.Format.R8_SNorm;

                case ERHISemanticFormat.Byte2Normalized:
                    return Vortice.DXGI.Format.R8G8_SNorm;

                case ERHISemanticFormat.Byte4Normalized:
                    return Vortice.DXGI.Format.R8G8B8A8_SNorm;

                case ERHISemanticFormat.UByteNormalized:
                    return Vortice.DXGI.Format.R8_UNorm;

                case ERHISemanticFormat.UByte2Normalized:
                    return Vortice.DXGI.Format.R8G8_UNorm;

                case ERHISemanticFormat.UByte4Normalized:
                    return Vortice.DXGI.Format.R8G8B8A8_UNorm;

                case ERHISemanticFormat.Short:
                    return Vortice.DXGI.Format.R16_SInt;

                case ERHISemanticFormat.Short2:
                    return Vortice.DXGI.Format.R16G16_SInt;

                case ERHISemanticFormat.Short4:
                    return Vortice.DXGI.Format.R16G16B16A16_SInt;

                case ERHISemanticFormat.UShort:
                    return Vortice.DXGI.Format.R16_UInt;

                case ERHISemanticFormat.UShort2:
                    return Vortice.DXGI.Format.R16G16_UInt;

                case ERHISemanticFormat.UShort4:
                    return Vortice.DXGI.Format.R16G16B16A16_UInt;

                case ERHISemanticFormat.ShortNormalized:
                    return Vortice.DXGI.Format.R16_SNorm;

                case ERHISemanticFormat.Short2Normalized:
                    return Vortice.DXGI.Format.R16G16_SNorm;

                case ERHISemanticFormat.Short4Normalized:
                    return Vortice.DXGI.Format.R16G16B16A16_SNorm;

                case ERHISemanticFormat.UShortNormalized:
                    return Vortice.DXGI.Format.R16_UNorm;

                case ERHISemanticFormat.UShort2Normalized:
                    return Vortice.DXGI.Format.R16G16_UNorm;

                case ERHISemanticFormat.UShort4Normalized:
                    return Vortice.DXGI.Format.R16G16B16A16_UNorm;

                case ERHISemanticFormat.Int:
                    return Vortice.DXGI.Format.R32_SInt;

                case ERHISemanticFormat.Int2:
                    return Vortice.DXGI.Format.R32G32_SInt;

                case ERHISemanticFormat.Int3:
                    return Vortice.DXGI.Format.R32G32B32_SInt;

                case ERHISemanticFormat.Int4:
                    return Vortice.DXGI.Format.R32G32B32A32_SInt;

                case ERHISemanticFormat.UInt:
                    return Vortice.DXGI.Format.R32_UInt;

                case ERHISemanticFormat.UInt2:
                    return Vortice.DXGI.Format.R32G32_UInt;

                case ERHISemanticFormat.UInt3:
                    return Vortice.DXGI.Format.R32G32B32_UInt;

                case ERHISemanticFormat.UInt4:
                    return Vortice.DXGI.Format.R32G32B32A32_UInt;

                case ERHISemanticFormat.Half:
                    return Vortice.DXGI.Format.R16_Float;

                case ERHISemanticFormat.Half2:
                    return Vortice.DXGI.Format.R16G16_Float;

                case ERHISemanticFormat.Half4:
                    return Vortice.DXGI.Format.R16G16B16A16_Float;

                case ERHISemanticFormat.Float:
                    return Vortice.DXGI.Format.R32_Float;

                case ERHISemanticFormat.Float2:
                    return Vortice.DXGI.Format.R32G32_Float;

                case ERHISemanticFormat.Float3:
                    return Vortice.DXGI.Format.R32G32B32_Float;

                case ERHISemanticFormat.Float4:
                    return Vortice.DXGI.Format.R32G32B32A32_Float;
            }
            return Vortice.DXGI.Format.Unknown;
        }

        //convert dxgi format to pixel format
        internal static Vortice.DXGI.Format ConvertToDx12Format(in ERHIPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case ERHIPixelFormat.R8_UNorm:
                case ERHIPixelFormat.R8_SNorm:
                case ERHIPixelFormat.R8_UInt:
                case ERHIPixelFormat.R8_SInt:
                    return Vortice.DXGI.Format.R8_Typeless;

                case ERHIPixelFormat.R16_UInt:
                case ERHIPixelFormat.R16_SInt:
                case ERHIPixelFormat.R16_Float:
                    return Vortice.DXGI.Format.R16_Typeless;

                case ERHIPixelFormat.R8G8_UInt:
                case ERHIPixelFormat.R8G8_SInt:
                case ERHIPixelFormat.R8G8_UNorm:
                case ERHIPixelFormat.R8G8_SNorm:
                    return Vortice.DXGI.Format.R8G8_Typeless;

                case ERHIPixelFormat.R32_UInt:
                case ERHIPixelFormat.R32_SInt:
                case ERHIPixelFormat.R32_Float:
                    return Vortice.DXGI.Format.R32_Typeless;

                case ERHIPixelFormat.R16G16_UInt:
                case ERHIPixelFormat.R16G16_SInt:
                case ERHIPixelFormat.R16G16_Float:
                    return Vortice.DXGI.Format.R16G16_Typeless;

                case ERHIPixelFormat.R8G8B8A8_UInt:
                case ERHIPixelFormat.R8G8B8A8_SInt:
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return Vortice.DXGI.Format.R8G8B8A8_Typeless;

                case ERHIPixelFormat.B8G8R8A8_UNorm:
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return Vortice.DXGI.Format.B8G8R8A8_Typeless;

                case ERHIPixelFormat.R99GB99_E5_Float:
                    return Vortice.DXGI.Format.R9G9B9E5_SharedExp;

                case ERHIPixelFormat.R10G10B10A2_UInt:
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return Vortice.DXGI.Format.R10G10B10A2_Typeless;

                case ERHIPixelFormat.R11G11B10_Float:
                    return Vortice.DXGI.Format.R11G11B10_Float;

                case ERHIPixelFormat.RG32_UInt:
                case ERHIPixelFormat.RG32_SInt:
                case ERHIPixelFormat.RG32_Float:
                    return Vortice.DXGI.Format.R32G32_Typeless;

                case ERHIPixelFormat.R16G16B16A16_UInt:
                case ERHIPixelFormat.R16G16B16A16_SInt:
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return Vortice.DXGI.Format.R16G16B16A16_Typeless;

                case ERHIPixelFormat.R32G32B32A32_UInt:
                case ERHIPixelFormat.R32G32B32A32_SInt:
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return Vortice.DXGI.Format.R32G32B32A32_Typeless;

                case ERHIPixelFormat.D16_UNorm:
                    return Vortice.DXGI.Format.D16_UNorm;

                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return Vortice.DXGI.Format.D24_UNorm_S8_UInt;

                case ERHIPixelFormat.D32_Float:
                    return Vortice.DXGI.Format.D32_Float;

                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return Vortice.DXGI.Format.D32_Float_S8X24_UInt;

                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return Vortice.DXGI.Format.BC1_UNorm_SRgb;

                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return Vortice.DXGI.Format.BC1_UNorm;

                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return Vortice.DXGI.Format.BC1_UNorm;

                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return Vortice.DXGI.Format.BC2_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return Vortice.DXGI.Format.BC2_UNorm;

                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return Vortice.DXGI.Format.BC3_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return Vortice.DXGI.Format.BC3_UNorm;

                case ERHIPixelFormat.R_BC4_UNorm:
                    return Vortice.DXGI.Format.BC4_UNorm;

                case ERHIPixelFormat.R_BC4_SNorm:
                    return Vortice.DXGI.Format.BC4_SNorm;

                case ERHIPixelFormat.RG_BC5_UNorm:
                    return Vortice.DXGI.Format.BC5_UNorm;

                case ERHIPixelFormat.RG_BC5_SNorm:
                    return Vortice.DXGI.Format.BC5_SNorm;

                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return Vortice.DXGI.Format.BC6H_Uf16;

                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return Vortice.DXGI.Format.BC6H_Sf16;

                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return Vortice.DXGI.Format.BC7_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return Vortice.DXGI.Format.BC7_UNorm;

                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return Vortice.DXGI.Format.Unknown;

                case ERHIPixelFormat.YUV2:
                    return Vortice.DXGI.Format.YUY2;
            }
            return Vortice.DXGI.Format.Unknown;
        }

        internal static Vortice.DXGI.Format ConvertToDx12ViewFormat(in ERHIPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case ERHIPixelFormat.R8_UNorm:
                    return Vortice.DXGI.Format.R8_UNorm;

                case ERHIPixelFormat.R8_SNorm:
                    return Vortice.DXGI.Format.R8_SNorm;

                case ERHIPixelFormat.R8_UInt:
                    return Vortice.DXGI.Format.R8_UInt;

                case ERHIPixelFormat.R8_SInt:
                    return Vortice.DXGI.Format.R8_SInt;

                case ERHIPixelFormat.R16_UInt:
                    return Vortice.DXGI.Format.R16_UInt;

                case ERHIPixelFormat.R16_SInt:
                    return Vortice.DXGI.Format.R16_SInt;

                case ERHIPixelFormat.R16_Float:
                    return Vortice.DXGI.Format.R16_Float;

                case ERHIPixelFormat.R8G8_UNorm:
                    return Vortice.DXGI.Format.R8G8_UNorm;

                case ERHIPixelFormat.R8G8_SNorm:
                    return Vortice.DXGI.Format.R8G8_SNorm;

                case ERHIPixelFormat.R8G8_UInt:
                    return Vortice.DXGI.Format.R8G8_UInt;

                case ERHIPixelFormat.R8G8_SInt:
                    return Vortice.DXGI.Format.R8G8_SInt;

                case ERHIPixelFormat.R32_UInt:
                    return Vortice.DXGI.Format.R32_UInt;

                case ERHIPixelFormat.R32_SInt:
                    return Vortice.DXGI.Format.R32_SInt;

                case ERHIPixelFormat.R32_Float:
                    return Vortice.DXGI.Format.R32_Float;

                case ERHIPixelFormat.R16G16_UInt:
                    return Vortice.DXGI.Format.R16G16_UInt;

                case ERHIPixelFormat.R16G16_SInt:
                    return Vortice.DXGI.Format.R16G16_SInt;

                case ERHIPixelFormat.R16G16_Float:
                    return Vortice.DXGI.Format.R16G16_Float;

                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return Vortice.DXGI.Format.R8G8B8A8_UNorm;

                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return Vortice.DXGI.Format.R8G8B8A8_UNorm_SRgb;

                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return Vortice.DXGI.Format.R8G8B8A8_SNorm;

                case ERHIPixelFormat.R8G8B8A8_UInt:
                    return Vortice.DXGI.Format.R8G8B8A8_UInt;

                case ERHIPixelFormat.R8G8B8A8_SInt:
                    return Vortice.DXGI.Format.R8G8B8A8_SInt;

                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return Vortice.DXGI.Format.B8G8R8A8_UNorm;

                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return Vortice.DXGI.Format.B8G8R8A8_UNorm_SRgb;

                case ERHIPixelFormat.R99GB99_E5_Float:
                    return Vortice.DXGI.Format.R9G9B9E5_SharedExp;

                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return Vortice.DXGI.Format.R10G10B10A2_UInt;

                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return Vortice.DXGI.Format.R10G10B10A2_UNorm;

                case ERHIPixelFormat.R11G11B10_Float:
                    return Vortice.DXGI.Format.R11G11B10_Float;

                case ERHIPixelFormat.RG32_UInt:
                    return Vortice.DXGI.Format.R32G32_UInt;

                case ERHIPixelFormat.RG32_SInt:
                    return Vortice.DXGI.Format.R32G32_SInt;

                case ERHIPixelFormat.RG32_Float:
                    return Vortice.DXGI.Format.R32G32_Float;

                case ERHIPixelFormat.R16G16B16A16_UInt:
                    return Vortice.DXGI.Format.R16G16B16A16_UInt;

                case ERHIPixelFormat.R16G16B16A16_SInt:
                    return Vortice.DXGI.Format.R16G16B16A16_SInt;

                case ERHIPixelFormat.R16G16B16A16_Float:
                    return Vortice.DXGI.Format.R16G16B16A16_Float;

                case ERHIPixelFormat.R32G32B32A32_UInt:
                    return Vortice.DXGI.Format.R32G32B32A32_UInt;

                case ERHIPixelFormat.R32G32B32A32_SInt:
                    return Vortice.DXGI.Format.R32G32B32A32_SInt;

                case ERHIPixelFormat.R32G32B32A32_Float:
                    return Vortice.DXGI.Format.R32G32B32A32_Float;

                case ERHIPixelFormat.D16_UNorm:
                    return Vortice.DXGI.Format.D16_UNorm;

                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return Vortice.DXGI.Format.D24_UNorm_S8_UInt;

                case ERHIPixelFormat.D32_Float:
                    return Vortice.DXGI.Format.D32_Float;

                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return Vortice.DXGI.Format.D32_Float_S8X24_UInt;

                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return Vortice.DXGI.Format.BC1_UNorm_SRgb;

                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return Vortice.DXGI.Format.BC1_UNorm;

                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return Vortice.DXGI.Format.BC1_UNorm;

                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return Vortice.DXGI.Format.BC2_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return Vortice.DXGI.Format.BC2_UNorm;

                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return Vortice.DXGI.Format.BC3_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return Vortice.DXGI.Format.BC3_UNorm;

                case ERHIPixelFormat.R_BC4_UNorm:
                    return Vortice.DXGI.Format.BC4_UNorm;

                case ERHIPixelFormat.R_BC4_SNorm:
                    return Vortice.DXGI.Format.BC4_SNorm;

                case ERHIPixelFormat.RG_BC5_UNorm:
                    return Vortice.DXGI.Format.BC5_UNorm;

                case ERHIPixelFormat.RG_BC5_SNorm:
                    return Vortice.DXGI.Format.BC5_SNorm;

                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return Vortice.DXGI.Format.BC6H_Uf16;

                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return Vortice.DXGI.Format.BC6H_Sf16;

                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return Vortice.DXGI.Format.BC7_UNorm_SRgb;

                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return Vortice.DXGI.Format.BC7_UNorm;

                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return Vortice.DXGI.Format.Unknown;

                case ERHIPixelFormat.YUV2:
                    return Vortice.DXGI.Format.YUY2;
            }
            return Vortice.DXGI.Format.Unknown;
        }

        internal static Vortice.DXGI.Format ConvertToDx12IndexFormat(in ERHIBufferFormat format)
        {
            return (format == ERHIBufferFormat.UInt16) ? Vortice.DXGI.Format.R16_UInt : ((format != ERHIBufferFormat.UInt32) ? Vortice.DXGI.Format.Unknown : Vortice.DXGI.Format.R32_UInt);
        }

        internal static Vortice.DXGI.SampleDescription ConvertToDx12SampleCount(in ERHISampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case ERHISampleCount.None:
                    return new Vortice.DXGI.SampleDescription(1, 0);

                case ERHISampleCount.Count2:
                    return new Vortice.DXGI.SampleDescription(2, 0);

                case ERHISampleCount.Count4:
                    return new Vortice.DXGI.SampleDescription(4, 0);

                case ERHISampleCount.Count8:
                    return new Vortice.DXGI.SampleDescription(8, 0);
            }
            return new Vortice.DXGI.SampleDescription(0, 0);
        }
        
        internal static Vortice.Direct3D12.DepthStencilViewFlags GetDx12DSVFlag(in bool bDepthReadOnly, in bool bStencilReadOnly)
        {
            Vortice.Direct3D12.DepthStencilViewFlags outFlag = Vortice.Direct3D12.DepthStencilViewFlags.None;

            if (bDepthReadOnly)
            {
                outFlag |= Vortice.Direct3D12.DepthStencilViewFlags.ReadOnlyDepth;
            }

            if (bStencilReadOnly)
            {
                outFlag |= Vortice.Direct3D12.DepthStencilViewFlags.ReadOnlyStencil;
            }

            return outFlag;
        }

        internal static Vortice.Direct3D12.DepthStencilViewDimension ConvertToDx12TextureDSVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return Vortice.Direct3D12.DepthStencilViewDimension.Texture2DMultisampled;

                case ERHITextureDimension.Texture2DArray:
                    return Vortice.Direct3D12.DepthStencilViewDimension.Texture2DArray;

                case ERHITextureDimension.Texture2DArrayMS:
                    return Vortice.Direct3D12.DepthStencilViewDimension.Texture2DMultisampledArray;
            }
            return Vortice.Direct3D12.DepthStencilViewDimension.Texture2D;
        }

        internal static Vortice.Direct3D12.RenderTargetViewDimension ConvertToDx12TextureRTVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return Vortice.Direct3D12.RenderTargetViewDimension.Texture2DMultisampled;

                case ERHITextureDimension.Texture2DArray:
                    return Vortice.Direct3D12.RenderTargetViewDimension.Texture2DArray;

                case ERHITextureDimension.Texture2DArrayMS:
                    return Vortice.Direct3D12.RenderTargetViewDimension.Texture2DMultisampledArray;

                case ERHITextureDimension.Texture3D:
                    return Vortice.Direct3D12.RenderTargetViewDimension.Texture3D;
            }
            return Vortice.Direct3D12.RenderTargetViewDimension.Texture2D;
        }

        internal static Vortice.Direct3D12.ShaderResourceViewDimension ConvertToDx12TextureSRVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DMultisampled;

                case ERHITextureDimension.Texture2DArray:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DArray;

                case ERHITextureDimension.Texture2DArrayMS:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DMultisampledArray;

                case ERHITextureDimension.TextureCube:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.TextureCube;

                case ERHITextureDimension.TextureCubeArray:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.TextureCubeArray;

                case ERHITextureDimension.Texture3D:
                    return Vortice.Direct3D12.ShaderResourceViewDimension.Texture3D;
            }
            return Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D;
        }

        internal static Vortice.Direct3D12.UnorderedAccessViewDimension ConvertToDx12TextureUAVDimension(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2DMS:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2DMultisampled;

                case ERHITextureDimension.Texture2DArray:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2DArray;

                case ERHITextureDimension.Texture2DArrayMS:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2DMultisampledArray;

                case ERHITextureDimension.TextureCube:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2DArray;

                case ERHITextureDimension.TextureCubeArray:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2DArray;

                case ERHITextureDimension.Texture3D:
                    return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture3D;
            }
            return Vortice.Direct3D12.UnorderedAccessViewDimension.Texture2D;
        }

        internal static byte[] ConvertToDx12SemanticNameByte(this ERHISemanticType type)
        {
            string semanticName = string.Empty;

            switch (type)
            {
                case ERHISemanticType.Color:
                    semanticName = "COLOR";
                    break;

                case ERHISemanticType.Position:
                    semanticName = "POSITION";
                    break;

                case ERHISemanticType.TexCoord:
                    semanticName = "TEXCOORD";
                    break;

                case ERHISemanticType.Normal:
                    semanticName = "NORMAL";
                    break;

                case ERHISemanticType.Tangent:
                    semanticName = "TANGENT";
                    break;

                case ERHISemanticType.Binormal:
                    semanticName = "BINORMAL";
                    break;

                case ERHISemanticType.BlendIndices:
                    semanticName = "BLENDINDICES";
                    break;

                case ERHISemanticType.BlendWeights:
                    semanticName = "BLENDWEIGHTS";
                    break;
            }

            return Encoding.ASCII.GetBytes(semanticName);
        }

        internal static Vortice.Direct3D12.InputClassification ConvertToDx12InputSlotClass(this ERHIVertexStepMode stepMode)
        {
            return ((stepMode == ERHIVertexStepMode.PerVertex) || (stepMode != ERHIVertexStepMode.PerInstance)) ? Vortice.Direct3D12.InputClassification.PerVertexData : Vortice.Direct3D12.InputClassification.PerInstanceData;
        }

        internal static int GetDx12VertexLayoutCount(in Span<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            int num = 0;
            for (int i = 0; i < vertexLayouts.Length; ++i)
            {
                num += vertexLayouts[i].VertexElements.Length;
            }

            return num;
        }

        internal static void ConvertToDx12VertexLayout(in Span<RHIVertexLayoutDescriptor> vertexLayouts, in Span<Vortice.Direct3D12.InputElementDescription> inputElementsView)
        {
            int slot = 0;
            int index = 0;

            while (slot < vertexLayouts.Length)
            {
                ref RHIVertexLayoutDescriptor vertexLayout = ref vertexLayouts[slot];
                Span<RHIVertexElementDescriptor> vertexElements = vertexLayout.VertexElements.Span;

                int num6 = 0;

                while (true)
                {
                    if (num6 >= vertexElements.Length)
                    {
                        slot++;
                        break;
                    }
                    ref RHIVertexElementDescriptor vertexElement = ref vertexElements[num6];
                    byte[] semanticByte = ConvertToDx12SemanticNameByte(vertexElement.Type);
                    ref Vortice.Direct3D12.InputElementDescription element = ref inputElementsView[index];
                    element.Format = ConvertToDx12SemanticFormat(vertexElement.Format);
                    element.Slot = (uint)slot;
                    element.SemanticName = Encoding.ASCII.GetString(semanticByte);
                    element.SemanticIndex = vertexElement.Slot;
                    element.Classification = ConvertToDx12InputSlotClass(vertexLayout.StepMode);
                    element.AlignedByteOffset = vertexElement.Offset;
                    element.InstanceDataStepRate = vertexLayout.StepMode == ERHIVertexStepMode.PerInstance ? vertexLayout.StepRate : 0;

                    ++num6;
                    ++index;
                }
            }
        }

        internal static Vortice.Direct3D12.DescriptorRangeType ConvertToDx12BindType(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.AccelStruct:
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                    return Vortice.Direct3D12.DescriptorRangeType.ShaderResourceView;

                case ERHIBindType.Sampler:
                    return Vortice.Direct3D12.DescriptorRangeType.Sampler;

                case ERHIBindType.UniformBuffer:
                    return Vortice.Direct3D12.DescriptorRangeType.ConstantBufferView;

                case ERHIBindType.StorageBuffer:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView;

                default:
                    throw new ArgumentOutOfRangeException(nameof(bindType), bindType, "Unsupported DX12 argument-table bind type.");
            }
        }

        internal static Vortice.Direct3D12.DescriptorRangeFlags GetDx12DescriptorRangeFlags(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.AccelStruct:
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                    return Vortice.Direct3D12.DescriptorRangeFlags.DescriptorsVolatile | Vortice.Direct3D12.DescriptorRangeFlags.DataStaticWhileSetAtExecute;

                case ERHIBindType.Sampler:
                    return Vortice.Direct3D12.DescriptorRangeFlags.DescriptorsVolatile;

                case ERHIBindType.UniformBuffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return Vortice.Direct3D12.DescriptorRangeFlags.DescriptorsVolatile | Vortice.Direct3D12.DescriptorRangeFlags.DataVolatile;

                default:
                    throw new ArgumentOutOfRangeException(nameof(bindType), bindType, "Unsupported DX12 argument-table bind type.");
            }
        }

        internal static Vortice.Direct3D12.ShaderVisibility ConvertToDx12ShaderVisibility(in ERHIShaderStageMask stages)
        {
            if (stages == ERHIShaderStageMask.None || (stages & ~ERHIShaderStageMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stages),
                    stages,
                    "DX12 argument-table shader-stage visibility must be a non-empty known mask.");
            }

            return stages switch
            {
                ERHIShaderStageMask.Task => Vortice.Direct3D12.ShaderVisibility.Amplification,
                ERHIShaderStageMask.Mesh => Vortice.Direct3D12.ShaderVisibility.Mesh,
                ERHIShaderStageMask.Vertex => Vortice.Direct3D12.ShaderVisibility.Vertex,
                ERHIShaderStageMask.Fragment => Vortice.Direct3D12.ShaderVisibility.Pixel,
                _ => Vortice.Direct3D12.ShaderVisibility.All
            };
        }

        internal static Vortice.Direct3D12.ClearFlags GetDx12ClearFlagByDSA(in RHIDepthStencilAttachmentDescriptor depthStencilAttachment)
        {
            Vortice.Direct3D12.ClearFlags result = new Vortice.Direct3D12.ClearFlags();

            if (depthStencilAttachment.DepthLoadOp == ERHILoadAction.Clear)
            {
                result |= Vortice.Direct3D12.ClearFlags.Depth;
            }

            if (depthStencilAttachment.StencilLoadOp == ERHILoadAction.Clear)
            {
                result |= Vortice.Direct3D12.ClearFlags.Stencil;
            }
            return result;
        }

        internal static Vortice.Direct3D12.HitGroupType ConverteToDx12HitGroupType(in ERHIHitGroupType type)
        {
            switch (type)
            {
                case ERHIHitGroupType.Procedural:
                    return Vortice.Direct3D12.HitGroupType.ProceduralPrimitive;

                default:
                    return Vortice.Direct3D12.HitGroupType.Triangles;
            }
        }

        internal static bool IsIndexBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.IndexBuffer) == ERHIBufferUsage.IndexBuffer;
        }

        internal static bool IsVertexBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.VertexBuffer) == ERHIBufferUsage.VertexBuffer;
        }

        internal static bool IsConstantBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.UniformBuffer) == ERHIBufferUsage.UniformBuffer;
        }

        internal static bool IsAccelStruct(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct;
        }

        internal static bool IsShaderResourceBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.ShaderResource) == ERHIBufferUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessBuffer(in ERHIBufferUsage bufferflag)
        {
            return (bufferflag & ERHIBufferUsage.UnorderedAccess) == ERHIBufferUsage.UnorderedAccess;
        }

        internal static bool IsDepthStencilTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.DepthStencil) == ERHITextureUsage.DepthStencil;
        }

        internal static bool IsRenderTargetTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.RenderTarget) == ERHITextureUsage.RenderTarget;
        }

        internal static bool IsShaderResourceTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & ERHITextureUsage.ShaderResource) == ERHITextureUsage.ShaderResource;
        }

        internal static bool IsUnorderedAccessTexture(in ERHITextureUsage textureFlag)
        {
            return (textureFlag & (ERHITextureUsage.UnorderedAccess | ERHITextureUsage.RasterizerOrdered)) != 0;
        }

        internal static void FillTexture2DSRV(ref Vortice.Direct3D12.Texture2DShaderResourceView srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2D)
            {
                return;
            }
            srv.MostDetailedMip = (uint)descriptor.BaseMipLevel;
            srv.MipLevels = (uint)descriptor.MipCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DArraySRV(ref Vortice.Direct3D12.Texture2DArrayShaderResourceView srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2DArray)
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.FirstArraySlice = descriptor.BaseArraySlice;
            srv.ArraySize = descriptor.ArrayCount;
            srv.PlaneSlice = 0;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeSRV(ref Vortice.Direct3D12.TextureCubeShaderResourceView srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.TextureCube)
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTextureCubeArraySRV(ref Vortice.Direct3D12.TextureCubeArrayShaderResourceView srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.TextureCubeArray)
            {
                return;
            }
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.MipLevels = descriptor.MipCount;
            srv.NumCubes = descriptor.ArrayCount;
            srv.First2DArrayFace = descriptor.BaseArraySlice;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture3DSRV(ref Vortice.Direct3D12.Texture3DShaderResourceView srv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture3D)
            {
                return;
            }
            srv.MipLevels = descriptor.MipCount;
            srv.MostDetailedMip = descriptor.BaseMipLevel;
            srv.ResourceMinLODClamp = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DUAV(ref Vortice.Direct3D12.Texture2DUnorderedAccessView uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2D)
            {
                return;
            }
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayUAV(ref Vortice.Direct3D12.Texture2DArrayUnorderedAccessView uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2DArray)
            {
                return;
            }
            uav.ArraySize = descriptor.ArrayCount;
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstArraySlice = descriptor.BaseArraySlice;
            uav.PlaneSlice = 0;
        }

        internal static void FillTexture3DUAV(ref Vortice.Direct3D12.Texture3DUnorderedAccessView uav, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture3D)
            {
                return;
            }
            uav.WSize = descriptor.ArrayCount;
            uav.MipSlice = descriptor.BaseMipLevel;
            uav.FirstWSlice = descriptor.BaseArraySlice;
        }

        internal static void FillTexture2DRTV(ref Vortice.Direct3D12.Texture2DRenderTargetView rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2D)
            {
                return;
            }
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture2DArrayRTV(ref Vortice.Direct3D12.Texture2DArrayRenderTargetView rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2DArray)
            {
                return;
            }
            rtv.ArraySize = descriptor.ArrayCount;
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.FirstArraySlice = descriptor.BaseArraySlice;
            rtv.PlaneSlice = 0;
        }

        internal static void FillTexture3DRTV(ref Vortice.Direct3D12.Texture3DRenderTargetView rtv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture3D)
            {
                return;
            }
            rtv.WSize = descriptor.ArrayCount;
            rtv.MipSlice = descriptor.BaseMipLevel;
            rtv.FirstWSlice = descriptor.BaseArraySlice;
        }

        internal static void FillTexture2DDSV(ref Vortice.Direct3D12.Texture2DDepthStencilView dsv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2D)
            {
                return;
            }
            dsv.MipSlice = descriptor.BaseMipLevel;
        }

        internal static void FillTexture2DArrayDSV(ref Vortice.Direct3D12.Texture2DArrayDepthStencilView dsv, in RHITextureViewDescriptor descriptor, in ERHITextureDimension dimension)
        {
            if (dimension != ERHITextureDimension.Texture2DArray)
            {
                return;
            }
            dsv.MipSlice = descriptor.BaseMipLevel;
            dsv.FirstArraySlice = descriptor.BaseArraySlice;
            dsv.ArraySize = descriptor.ArrayCount;
        }
    }
#pragma warning restore CA1416
}
