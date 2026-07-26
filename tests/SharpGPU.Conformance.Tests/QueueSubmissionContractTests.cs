using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class QueueSubmissionContractTests
{
    [Fact]
    public void Submit_RequiresWorkOrSynchronization()
    {
        object owner = new object();
        using FakeCommandQueue queue = new FakeCommandQueue(owner);

        Assert.Throws<ArgumentException>(() => queue.Submit(default));
    }

    [Fact]
    public void BinarySemaphore_RequiresSignalBeforeWaitAndConsumptionBeforeResignal()
    {
        object owner = new object();
        using FakeCommandQueue queue = new FakeCommandQueue(owner);
        using FakeSemaphore semaphore = new FakeSemaphore(owner);

        RHIQueueSubmitDescriptor waitBeforeSignal = new RHIQueueSubmitDescriptor(
            waitSemaphores: new[]
            {
                new RHIQueueSemaphoreWait(semaphore, ERHIStageMask.Compute)
            });
        Assert.Throws<InvalidOperationException>(() => queue.Submit(in waitBeforeSignal));

        RHIQueueSubmitDescriptor signal = new RHIQueueSubmitDescriptor(
            signalSemaphores: new RHISemaphore[] { semaphore });
        queue.Submit(in signal);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(in signal));

        RHIQueueSubmitDescriptor wait = new RHIQueueSubmitDescriptor(
            waitSemaphores: new[]
            {
                new RHIQueueSemaphoreWait(semaphore, ERHIStageMask.Compute)
            });
        queue.Submit(in wait);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(in wait));

        queue.Submit(in signal);
    }

    [Fact]
    public void Fence_RequiresCompletionBeforeResetAndResetBeforeReuse()
    {
        object owner = new object();
        using FakeCommandQueue queue = new FakeCommandQueue(owner);
        using FakeFence fence = new FakeFence(owner);
        RHIQueueSubmitDescriptor submit = new RHIQueueSubmitDescriptor(completionFence: fence);

        Assert.Equal(ERHIFenceStatus.NotReady, fence.Status);
        Assert.Throws<InvalidOperationException>(() => fence.Wait());
        fence.Reset();

        queue.Submit(in submit);
        Assert.Throws<InvalidOperationException>(() => fence.Reset());
        Assert.Throws<InvalidOperationException>(() => queue.Submit(in submit));

        fence.Wait();
        Assert.Equal(ERHIFenceStatus.Success, fence.Status);
        fence.Reset();
        Assert.Equal(ERHIFenceStatus.NotReady, fence.Status);

        queue.Submit(in submit);
    }

    [Fact]
    public void Submit_RejectsCrossDeviceDisposedDuplicateAndEmptyStageSynchronization()
    {
        object owner = new object();
        object otherOwner = new object();
        using FakeCommandQueue queue = new FakeCommandQueue(owner);
        using FakeSemaphore local = new FakeSemaphore(owner);
        using FakeSemaphore foreign = new FakeSemaphore(otherOwner);
        using FakeSemaphore disposed = new FakeSemaphore(owner);
        disposed.Dispose();

        Assert.Throws<ArgumentException>(() => queue.Submit(new RHIQueueSubmitDescriptor(
            signalSemaphores: new RHISemaphore[] { foreign })));
        Assert.Throws<ObjectDisposedException>(() => queue.Submit(new RHIQueueSubmitDescriptor(
            signalSemaphores: new RHISemaphore[] { disposed })));
        Assert.Throws<ArgumentException>(() => queue.Submit(new RHIQueueSubmitDescriptor(
            signalSemaphores: new RHISemaphore[] { local, local })));
        Assert.Throws<ArgumentException>(() => queue.Submit(new RHIQueueSubmitDescriptor(
            waitSemaphores: new[]
            {
                new RHIQueueSemaphoreWait(local, ERHIStageMask.None)
            })));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Submit(new RHIQueueSubmitDescriptor(
            waitSemaphores: new[]
            {
                new RHIQueueSemaphoreWait(local, (ERHIStageMask)(1UL << 40))
            })));
    }

    [Fact]
    public void CommandBuffer_RequiresExplicitRecordingEncoderEndAndSingleSubmission()
    {
        object owner = new object();
        using FakeCommandQueue queue = new FakeCommandQueue(owner);
        using FakeCommandBuffer commandBuffer = new FakeCommandBuffer(queue);
        RHIQueueSubmitDescriptor submit = new RHIQueueSubmitDescriptor(
            commandBuffers: new RHICommandBuffer[] { commandBuffer });

        Assert.Throws<InvalidOperationException>(() => queue.Submit(in submit));
        Assert.Throws<InvalidOperationException>(() => commandBuffer.End());
        Assert.Throws<InvalidOperationException>(() => commandBuffer.BeginTransferForTest());

        commandBuffer.Begin("state-contract");
        Assert.Throws<InvalidOperationException>(() => commandBuffer.Begin("nested"));
        commandBuffer.BeginTransferForTest();
        Assert.Throws<InvalidOperationException>(() => commandBuffer.BeginComputeForTest());
        Assert.Throws<InvalidOperationException>(() => commandBuffer.End());
        Assert.Throws<InvalidOperationException>(() => commandBuffer.EndComputeForTest());
        commandBuffer.EndTransferForTest();
        commandBuffer.End();

        RHIQueueSubmitDescriptor duplicate = new RHIQueueSubmitDescriptor(
            commandBuffers: new RHICommandBuffer[] { commandBuffer, commandBuffer });
        Assert.Throws<ArgumentException>(() => queue.Submit(in duplicate));

        queue.Submit(in submit);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(in submit));

        commandBuffer.Begin("reuse-after-caller-fence-discipline");
        commandBuffer.End();
        queue.Submit(in submit);

        commandBuffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => commandBuffer.Begin("disposed"));
    }

    private sealed class FakeCommandQueue : RHICommandQueue
    {
        private readonly object m_Owner;

        internal FakeCommandQueue(object owner)
        {
            m_Owner = owner;
            m_PipelineType = ERHIPipelineType.Compute;
        }

        public override ulong Frequency => 1;
        protected override object DeviceIdentity => m_Owner;

        public override RHICommandBuffer CreateCommandBuffer()
        {
            throw new NotSupportedException();
        }

        public override void Submit(in RHIQueueSubmitDescriptor descriptor)
        {
            ValidateSubmit(in descriptor);
            ReserveSubmit(in descriptor);
            CommitSubmit(in descriptor);
        }
    }

    private sealed class FakeCommandBuffer : RHICommandBuffer
    {
        internal FakeCommandBuffer(RHICommandQueue queue)
        {
            m_CommandQueue = queue;
        }

        public override void Begin(string name)
        {
            ValidateCanBegin();
            MarkBeginSucceeded();
        }

        public override void End()
        {
            ValidateCanEnd();
            MarkEndSucceeded();
        }

        internal void BeginTransferForTest()
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Transfer);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Transfer);
        }

        internal void EndTransferForTest()
        {
            ValidateCanEndEncoder(ERHICommandEncoderKind.Transfer);
            MarkEncoderEndSucceeded();
        }

        internal void BeginComputeForTest()
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Compute);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Compute);
        }

        internal void EndComputeForTest()
        {
            ValidateCanEndEncoder(ERHICommandEncoderKind.Compute);
            MarkEncoderEndSucceeded();
        }

        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndTransferPass() => throw new NotSupportedException();
        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndComputePass() => throw new NotSupportedException();
        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndRaytracingPass() => throw new NotSupportedException();
        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndRasterPass() => throw new NotSupportedException();
        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndMLPass() => throw new NotSupportedException();
        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor) =>
            throw new NotSupportedException();
        public override void EndWorkGraphPass() => throw new NotSupportedException();
        public override RHITransferEncoder GetTransferEncoder() => throw new NotSupportedException();
        public override RHIComputeEncoder GetComputeEncoder() => throw new NotSupportedException();
        public override RHIRaytracingEncoder GetRaytracingEncoder() => throw new NotSupportedException();
        public override RHIRasterEncoder GetRasterEncoder() => throw new NotSupportedException();
        public override RHIMLEncoder GetMLEncoder() => throw new NotSupportedException();
        public override RHIWorkGraphEncoder GetWorkGraphEncoder() => throw new NotSupportedException();
    }

    private sealed class FakeFence : RHIFence
    {
        internal FakeFence(object owner) : base(owner)
        {
        }

        public override ERHIFenceStatus Status =>
            IsSignalKnownComplete ? ERHIFenceStatus.Success : ERHIFenceStatus.NotReady;

        public override void Reset()
        {
            if (BeginReset())
            {
                CompleteReset();
            }
        }

        public override ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            MarkSignaled();
            return ERHIFenceStatus.Success;
        }
    }

    private sealed class FakeSemaphore : RHISemaphore
    {
        internal FakeSemaphore(object owner) : base(owner)
        {
        }
    }
}
