using System;
using SharpMetal.Metal;
using System.Threading;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
    internal sealed class MetalFence : RHIFence
    {
        internal MTLSharedEvent NativeEvent => m_NativeEvent;

        private MTLSharedEvent m_NativeEvent;
        private long m_NextSignalValue;
        private long m_TargetValue;

        internal MetalFence(MetalDevice device) : base(device)
        {
            m_NativeEvent = device.NativeDevice.NewSharedEvent();
            if (m_NativeEvent.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal completion fences require MTLSharedEvent support.");
            }

            m_NextSignalValue = 0;
            m_TargetValue = 0;
        }

        public override ERHIFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return ERHIFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return ERHIFenceStatus.NotReady;
                }

                ulong targetValue = (ulong)Volatile.Read(ref m_TargetValue);
                bool complete = targetValue != 0 && m_NativeEvent.SignaledValue >= targetValue;
                if (complete)
                {
                    MarkSignaled();
                }

                return complete ? ERHIFenceStatus.Success : ERHIFenceStatus.NotReady;
            }
        }

        public override void Reset()
        {
            if (IsSignalPending)
            {
                _ = Status;
            }

            if (!BeginReset())
            {
                return;
            }

            CompleteReset();
        }

        public override ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return ERHIFenceStatus.Success;
            }

            ulong targetValue = (ulong)Volatile.Read(ref m_TargetValue);
            if (targetValue == 0)
            {
                throw new InvalidOperationException("The fence has no native signal value.");
            }

            ulong timeoutMilliseconds;
            if (timeoutNanoseconds == ulong.MaxValue)
            {
                timeoutMilliseconds = ulong.MaxValue;
            }
            else
            {
                timeoutMilliseconds = timeoutNanoseconds / 1_000_000UL;
                if ((timeoutNanoseconds % 1_000_000UL) != 0)
                {
                    ++timeoutMilliseconds;
                }
            }

            m_NativeEvent.WaitUntilSignaledValue(targetValue, timeoutMilliseconds);
            if (m_NativeEvent.SignaledValue < targetValue)
            {
                return ERHIFenceStatus.NotReady;
            }

            MarkSignaled();
            return ERHIFenceStatus.Success;
        }

        internal ulong PrepareSignalValue()
        {
            if (!IsSignalPending)
            {
                throw new InvalidOperationException("The fence signal must be reserved before obtaining a native value.");
            }

            long signalValue = Interlocked.Increment(ref m_NextSignalValue);
            Volatile.Write(ref m_TargetValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            if (m_NativeEvent.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeEvent);
                m_NativeEvent = default;
            }
        }
    }

    internal sealed class MetalSemaphore : RHISemaphore
    {
        public MTLSharedEvent NativeEvent => m_NativeEvent;
        internal ulong LastSignaledValue => (ulong)Volatile.Read(ref m_LastSignaledValue);

        private MTLSharedEvent m_NativeEvent;
        private long m_NextSignalValue;
        private long m_LastSignaledValue;

        internal MetalSemaphore(MetalDevice device) : base(device)
        {
            m_NativeEvent = device.NativeDevice.NewSharedEvent();
            if (m_NativeEvent.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal binary semaphores require MTLSharedEvent support.");
            }

            m_NextSignalValue = 0;
            m_LastSignaledValue = 0;
        }

        internal ulong PrepareSignalValue()
        {
            long signalValue = Interlocked.Increment(ref m_NextSignalValue);
            Volatile.Write(ref m_LastSignaledValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            if (m_NativeEvent.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeEvent);
                m_NativeEvent = default;
            }
        }
    }

    internal sealed class MetalHeap : RHIHeap
    {
        internal SharpMetal.Metal.MTLHeap NativeHeap { get { ThrowIfDisposed(); return m_NativeHeap; } }

        private readonly MetalDevice m_MetalDevice;
        private SharpMetal.Metal.MTLHeap m_NativeHeap;

        internal MetalHeap(MetalDevice device, in RHIHeapDescription descriptor)
            : this(device, descriptor, MTLHeapType.Placement)
        {
        }

        internal MetalHeap(MetalDevice device, in RHIHeapDescription descriptor, MTLHeapType heapType)
            : base(device, descriptor, 1UL)
        {
            m_MetalDevice = device;
            m_NativeHeap = CreateNativeHeap(device, descriptor, heapType, applyStorageOptions: true);
        }

        /// <summary>
        /// WWDC25 ML sample heap: MTLHeapTypePlacement + size only (no ResourceOptions).
        /// </summary>
        internal static MetalHeap CreateMachineLearningIntermediates(MetalDevice device, ulong sizeInBytes)
        {
            RHIResourceMemoryRequirements requirements = new RHIResourceMemoryRequirements(
                device,
                sizeInBytes,
                1,
                ERHIStorageMode.GPULocal,
                1,
                ERHIMemoryResourceKind.Buffer);
            RHIHeapDescription descriptor = new RHIHeapDescription(sizeInBytes, requirements);
            return new MetalHeap(device, descriptor, MTLHeapType.Placement, applyStorageOptions: false);
        }

        private MetalHeap(MetalDevice device, in RHIHeapDescription descriptor, MTLHeapType heapType, bool applyStorageOptions)
            : base(device, descriptor, 1UL)
        {
            m_MetalDevice = device;
            m_NativeHeap = CreateNativeHeap(device, descriptor, heapType, applyStorageOptions);
        }

        private static SharpMetal.Metal.MTLHeap CreateNativeHeap(
            MetalDevice device,
            in RHIHeapDescription descriptor,
            MTLHeapType heapType,
            bool applyStorageOptions)
        {
            MTLHeapDescriptor nativeDescriptor = MTLHeapDescriptor.New();
            nativeDescriptor.Size = descriptor.Size;
            nativeDescriptor.Type = heapType;
            if (applyStorageOptions)
            {
                nativeDescriptor.ResourceOptions = MetalUtility.ConvertToMetalResourceOptions(descriptor.StorageMode);
                nativeDescriptor.StorageMode = (MTLStorageMode)(((ulong)nativeDescriptor.ResourceOptions >> 4) & 0xF);
                nativeDescriptor.CpuCacheMode = (MTLCPUCacheMode)((ulong)nativeDescriptor.ResourceOptions & 0xF);
                if (MetalSparseMemoryUtility.RequiresPlacementSparseCompatibility(
                        descriptor.Compatibility))
                {
                    nativeDescriptor.MaxCompatiblePlacementSparsePageSize =
                        MetalSparseMemoryUtility.SparsePageSize;
                }
                else if (descriptor.Compatibility.NativeAllocationFlags != 0)
                {
                    ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
                    throw new ArgumentException(
                        "The Metal heap compatibility contains unknown native allocation flags.",
                        nameof(descriptor));
                }
            }

            SharpMetal.Metal.MTLHeap nativeHeap;
            try
            {
                nativeHeap = device.NativeDevice.NewHeap(nativeDescriptor);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }

            if (nativeHeap.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLDevice failed to create a placement heap.",
                    ERHIDeviceState.Operational);
            }

            return nativeHeap;
        }

        protected override void Release()
        {
            if (m_NativeHeap.NativePtr != IntPtr.Zero)
            {
                m_MetalDevice.RemoveResidencyAllocation(m_NativeHeap);
                ObjectiveCRuntime.Release(m_NativeHeap);
                m_NativeHeap = default;
            }
        }
    }
}