using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using Infinity.Collections;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using Viewport = Infinity.Mathmatics.Viewport;

namespace Infinity.Graphics
{
#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal unsafe struct Dx12AttachmentInfo
    {
        public bool bDepthStencil;
        public Dx12DescriptorInfo AttachmentInfo;
    };

    internal unsafe class Dx12BlitEncoder : RHIBlitEncoder
    {
        public Dx12BlitEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(string name)
        {
            PushDebugGroup(name);
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            Dx12Buffer dx12SrcBuffer = srcBuffer as Dx12Buffer;
            Dx12Buffer dx12DstBuffer = dstBuffer as Dx12Buffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            dx12CommandBuffer.NativeCommandList->CopyBufferRegion(dx12DstBuffer.NativeResource, (ulong)dstOffset, dx12SrcBuffer.NativeResource, (ulong)srcOffset, (ulong)size);
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            /*Dx12Buffer srcBuffer = src.buffer as Dx12Buffer;
            Dx12Texture dstTexture = dst.texture as Dx12Texture;

            D3D12_TEXTURE_COPY_LOCATION textureLocation = new D3D12_TEXTURE_COPY_LOCATION();
            textureLocation.pResource = dstTexture.NativeResource;
            textureLocation.Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            textureLocation.SubresourceIndex = (uint)(dstTexture.Descriptor.mipCount * dst.arrayLayer + dst.mipLevel);

            dx12CommandBuffer.NativeCommandList->CopyTextureRegion(&textureLocation);*/

            throw new NotImplementedException();
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            //Dx12Texture srcTexture = src as Dx12Texture;
            //Dx12Buffer dstBuffer = dst as Dx12Buffer;

            throw new NotImplementedException();
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            //Dx12Texture srcTexture = src as Dx12Texture;
            //Dx12Texture dstTexture = dst as Dx12Texture;

            //D3D12_TEXTURE_COPY_LOCATION srcLocation = new D3D12_TEXTURE_COPY_LOCATION(srcTexture.NativeResource, new D3D12_PLACED_SUBRESOURCE_FOOTPRINT());
            //dx12CommandBuffer.NativeCommandList->CopyTextureRegion();

            throw new NotImplementedException();
        }

        public override void ResourceBarrier(in RHIBarrier barrier)
        {
            ID3D12Resource* resource = null;
            D3D12_RESOURCE_BARRIER resourceBarrier;

            switch (barrier.BarrierType)
            {
                case EBarrierType.UAV:
                    if (barrier.ResourceType == EResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferUAVBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureUAVBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                    break;

                case EBarrierType.Aliasing:
                    if (barrier.ResourceType == EResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferAliasingBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureAliasingBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitAliasing(null, resource);
                    break;

                case EBarrierType.Triansition:
                    D3D12_RESOURCE_STATES beforeState;
                    D3D12_RESOURCE_STATES afterState;
                    if (barrier.ResourceType == EResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferTransitionBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif

                        resource = buffer.NativeResource;
                        beforeState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferTransitionBarrier.Before);
                        afterState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferTransitionBarrier.After);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureTransitionBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif

                        resource = texture.NativeResource;
                        beforeState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureTransitionBarrier.Before);
                        afterState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureTransitionBarrier.After);
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(resource, beforeState, afterState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarrier(in Memory<RHIBarrier> barriers)
        {
            ID3D12Resource* resource;
            D3D12_RESOURCE_STATES beforeState;
            D3D12_RESOURCE_STATES afterState;
            D3D12_RESOURCE_BARRIER* resourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIBarrier barrier = ref barriers.Span[i];

                switch (barrier.BarrierType)
                {
                    case EBarrierType.UAV:
                        if (barrier.ResourceType == EResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferUAVBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureUAVBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                        break;

                    case EBarrierType.Aliasing:
                        if (barrier.ResourceType == EResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferAliasingBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureAliasingBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitAliasing(null, resource);
                        break;

                    case EBarrierType.Triansition:
                        if (barrier.ResourceType == EResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferTransitionBarrier.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif

                            resource = buffer.NativeResource;
                            beforeState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferTransitionBarrier.Before);
                            afterState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferTransitionBarrier.After);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureTransitionBarrier.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(buffer != null);
#endif

                            resource = texture.NativeResource;
                            beforeState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureTransitionBarrier.Before);
                            afterState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureTransitionBarrier.After);
                        }

                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitTransition(resource, beforeState, afterState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ResourceBarrier((uint)barriers.Length, resourceBarriers);
        }

        public override void BeginQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void EndQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void PushDebugGroup(string name)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->EndEvent();
        }

        internal override void EndPass()
        {
            PopDebugGroup();
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12ComputeEncoder : RHIComputeEncoder
    {
        public Dx12ComputeEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(string name)
        {
            PushDebugGroup(name);
        }

        public override void SetPipelineLayout(RHIPipelineLayout pipelineLayout)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_PipelineLayout = pipelineLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;
            dx12CommandBuffer.NativeCommandList->SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_Pipeline = pipeline;
            Dx12ComputePipeline dx12Pipeline = pipeline as Dx12ComputePipeline;
            dx12CommandBuffer.NativeCommandList->SetPipelineState(dx12Pipeline.NativePipelineState);
        }

        public override void SetBindTable(RHIBindTable bindTable, in uint tableIndex)
        {
            Dx12BindTable dx12BindTable = bindTable as Dx12BindTable;
            Dx12BindTableLayout dx12BindTableLayout = dx12BindTable.BindTableLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12BindTableLayout.Index, "error bindTable index");
#endif

            for (int i = 0; i < dx12BindTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;

                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12BindTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.All, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Compute, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ExecuteIndirect(dx12Device.DispatchComputeIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
        }

        public override void BeginQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void EndQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void PushDebugGroup(string name)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->EndEvent();
        }

        internal override void EndPass()
        {
            PopDebugGroup();
            m_Pipeline = null;
            m_PipelineLayout = null;
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12RaytracingEncoder : RHIRaytracingEncoder
    {
        public Dx12RaytracingEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(string name)
        {
            PushDebugGroup(name);
        }

        public override void SetPipelineLayout(RHIPipelineLayout pipelineLayout)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_PipelineLayout = pipelineLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;
            dx12CommandBuffer.NativeCommandList->SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_Pipeline = pipeline;
            Dx12RaytracingPipeline dx12Pipeline = pipeline as Dx12RaytracingPipeline;
            dx12CommandBuffer.NativeCommandList->SetPipelineState1(dx12Pipeline.NativePipelineState);
        }

        public override void SetBindTable(RHIBindTable bindTable, in uint tableIndex)
        {
            Dx12BindTable dx12BindTable = bindTable as Dx12BindTable;
            Dx12BindTableLayout dx12BindTableLayout = dx12BindTable.BindTableLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12BindTableLayout.Index, "error bindTable index");
#endif

            for (int i = 0; i < dx12BindTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;

                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12BindTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.All, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Compute, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct tlas)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12TopLevelAccelStruct topLevelAccelStruct = tlas as Dx12TopLevelAccelStruct;
            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC accelStrucDescription = topLevelAccelStruct.NativeAccelStrucDescriptor;
            dx12CommandBuffer.NativeCommandList->BuildRaytracingAccelerationStructure(&accelStrucDescription, 0, null);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct blas)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BottomLevelAccelStruct bottomLevelAccelStruct = blas as Dx12BottomLevelAccelStruct;
            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC accelStrucDescription = bottomLevelAccelStruct.NativeAccelStrucDescriptor;
            dx12CommandBuffer.NativeCommandList->BuildRaytracingAccelerationStructure(&accelStrucDescription, 0, null);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            Dx12FunctionTable dx12FunctionTable = functionTable as Dx12FunctionTable;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            D3D12_DISPATCH_RAYS_DESC dispatchRayDescriptor;
            dispatchRayDescriptor.Depth = depth;
            dispatchRayDescriptor.Width = width;
            dispatchRayDescriptor.Height = height;
            dispatchRayDescriptor.MissShaderTable.SizeInBytes = dx12FunctionTable.MissSize;
            dispatchRayDescriptor.MissShaderTable.StartAddress = dx12FunctionTable.MissAddress;
            dispatchRayDescriptor.MissShaderTable.StrideInBytes = dx12FunctionTable.MissStride;
            dispatchRayDescriptor.HitGroupTable.SizeInBytes = dx12FunctionTable.HitGroupSize;
            dispatchRayDescriptor.HitGroupTable.StartAddress = dx12FunctionTable.HitGroupAddress;
            dispatchRayDescriptor.HitGroupTable.StrideInBytes = dx12FunctionTable.HitGroupStride;
            dispatchRayDescriptor.RayGenerationShaderRecord.SizeInBytes = dx12FunctionTable.RayGenSize;
            dispatchRayDescriptor.RayGenerationShaderRecord.StartAddress = dx12FunctionTable.RayGenAddress;
            dispatchRayDescriptor.CallableShaderTable = default;

            dx12CommandBuffer.NativeCommandList->DispatchRays(&dispatchRayDescriptor);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ExecuteIndirect(dx12Device.DispatchRayIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
            throw new NotImplementedException();
        }

        public override void BeginQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void EndQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void PushDebugGroup(string name)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->EndEvent();
        }

        internal override void EndPass()
        {
            PopDebugGroup();
            m_Pipeline = null;
            m_PipelineLayout = null;
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12MeshletEncoder : RHIMeshletEncoder
    {
        protected byte m_SubPassIndex;
        protected TValueArray<Dx12AttachmentInfo> m_AttachmentInfos;

        public Dx12MeshletEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_AttachmentInfos = new TValueArray<Dx12AttachmentInfo>(5);
        }

        internal override void BeginPass(in RHIMeshletPassDescriptor descriptor)
        {
            m_SubPassIndex = 0;
            PushDebugGroup(descriptor.Name);

            if (descriptor.SubpassDescriptors != null)
            {
                throw new NotImplementedException("Dx12 is not support subpass. Please do not use subpass descriptors");
            }

            m_AttachmentInfos.Clear();
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            D3D12_CPU_DESCRIPTOR_HANDLE? dsvHandle = null;
            D3D12_CPU_DESCRIPTOR_HANDLE* rtvHandles = stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[descriptor.ColorAttachmentDescriptors.Length];

            // create render target views
            for (int i = 0; i < descriptor.ColorAttachmentDescriptors.Length; ++i)
            {
                Dx12Texture texture = descriptor.ColorAttachmentDescriptors.Span[i].RenderTarget as Dx12Texture;
                Debug.Assert(texture != null);

                /*RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.BaseArrayLayer = 0;
                    viewDescriptor.ArrayLayerCount = texture.Descriptor.Extent.z;
                    viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ETextureViewType.RenderTarget;
                    viewDescriptor.Dimension = ETextureDimension.Texture2D;
                }*/
                D3D12_RENDER_TARGET_VIEW_DESC desc = new D3D12_RENDER_TARGET_VIEW_DESC();
                desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                desc.ViewDimension = Dx12Utility.ConvertToDx12TextureRTVDimension(texture.Descriptor.Dimension);
                /*Dx12Utility.FillTexture2DRTV(ref desc.Texture2D, viewDescriptor);
                Dx12Utility.FillTexture3DRTV(ref desc.Texture3D, viewDescriptor);
                Dx12Utility.FillTexture2DArrayRTV(ref desc.Texture2DArray, viewDescriptor);*/

                Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
                {
                    dx12AttachmentInfo.bDepthStencil = false;
                    dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateRtvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                rtvHandles[i] = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice->CreateRenderTargetView(texture.NativeResource, &desc, rtvHandles[i]);
            }

            // create depth stencil view
            if (descriptor.DepthStencilAttachmentDescriptor != null)
            {
                Dx12Texture texture = descriptor.DepthStencilAttachmentDescriptor?.DepthStencilTarget as Dx12Texture;
                Debug.Assert(texture != null);

                /*RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.BaseArrayLayer = 0;
                    viewDescriptor.ArrayLayerCount = texture.Descriptor.Extent.z;
                    viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ETextureViewType.DepthStencil;
                    viewDescriptor.Dimension = ETextureDimension.Texture2D;
                }*/
                D3D12_DEPTH_STENCIL_VIEW_DESC desc = new D3D12_DEPTH_STENCIL_VIEW_DESC();
                desc.Flags = Dx12Utility.GetDx12DSVFlag(descriptor.DepthStencilAttachmentDescriptor.Value.DepthReadOnly, descriptor.DepthStencilAttachmentDescriptor.Value.StencilReadOnly);
                desc.Format = Dx12Utility.ConvertToDx12Format(texture.Descriptor.Format);
                desc.ViewDimension = Dx12Utility.ConvertToDx12TextureDSVDimension(texture.Descriptor.Dimension);

                Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
                {
                    dx12AttachmentInfo.bDepthStencil = true;
                    dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateDsvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                dsvHandle = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice->CreateDepthStencilView(texture.NativeResource, &desc, dsvHandle.Value);
            }

            // set render targets
            dx12CommandBuffer.NativeCommandList->OMSetRenderTargets((uint)descriptor.ColorAttachmentDescriptors.Length, rtvHandles, false, dsvHandle.HasValue ? (D3D12_CPU_DESCRIPTOR_HANDLE*)&dsvHandle : null);

            // clear render targets
            for (int i = 0; i < descriptor.ColorAttachmentDescriptors.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachmentDescriptors.Span[i];

                if (colorAttachmentDescriptor.LoadAction != ELoadAction.Clear)
                {
                    continue;
                }

                float4 clearValue = colorAttachmentDescriptor.ClearValue;
                dx12CommandBuffer.NativeCommandList->ClearRenderTargetView(rtvHandles[i], (float*)&clearValue, 0, null);
            }

            // clear depth stencil target
            if (dsvHandle.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor? depthStencilAttachmentDescriptor = descriptor.DepthStencilAttachmentDescriptor;
                if (depthStencilAttachmentDescriptor?.DepthLoadAction != ELoadAction.Clear && depthStencilAttachmentDescriptor?.StencilLoadAction != ELoadAction.Clear)
                {
                    return;
                }

                dx12CommandBuffer.NativeCommandList->ClearDepthStencilView(dsvHandle.Value, Dx12Utility.GetDx12ClearFlagByDSA(depthStencilAttachmentDescriptor.Value), depthStencilAttachmentDescriptor.Value.DepthClearValue, Convert.ToByte(depthStencilAttachmentDescriptor.Value.StencilClearValue), 0, null);
            }

            // set shading rate
            if (descriptor.ShadingRateDescriptor.HasValue)
            {
                if (descriptor.ShadingRateDescriptor.Value.ShadingRateTexture != null)
                {
                    D3D12_SHADING_RATE_COMBINER shadingRateCombiner = Dx12Utility.ConvertToDx12ShadingRateCombiner(descriptor.ShadingRateDescriptor.Value.ShadingRateCombiner);
                    Dx12Texture dx12Texture = descriptor.ShadingRateDescriptor.Value.ShadingRateTexture as Dx12Texture;
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), &shadingRateCombiner);
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRateImage(dx12Texture.NativeResource);
                }
                else
                {
                    D3D12_SHADING_RATE_COMBINER* shadingRateCombiners = stackalloc D3D12_SHADING_RATE_COMBINER[2] { D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX, D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX };
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), shadingRateCombiners);
                    //dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), null);
                }
            }
        }

        public override void SetScissor(in Rect rect)
        {
            RECT tempScissor = new RECT((int)rect.left, (int)rect.top, (int)rect.right, (int)rect.bottom);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->RSSetScissorRects(1, &tempScissor);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            throw new NotImplementedException();
        }

        public override void SetViewport(in Viewport viewport)
        {
            D3D12_VIEWPORT tempViewport = new D3D12_VIEWPORT(viewport.TopLeftX, viewport.TopLeftY, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->RSSetViewports(1, &tempViewport);
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            throw new NotImplementedException();
        }

        public override void SetStencilRef(in uint value)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->OMSetStencilRef(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            float4 tempValue = value;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->OMSetBlendFactor((float*)&tempValue);
        }

        public override void NextSubpass()
        {
            throw new NotImplementedException("Dx12 is not support subpass. Please do not use NextSubpass command in graphics encoder");
        }

        public override void SetPipelineLayout(RHIPipelineLayout pipelineLayout)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_PipelineLayout = pipelineLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;
            dx12CommandBuffer.NativeCommandList->SetGraphicsRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetPipeline(RHIMeshletPipeline pipeline)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_Pipeline = pipeline;
            Dx12MeshletPipeline dx12Pipeline = pipeline as Dx12MeshletPipeline;
            dx12CommandBuffer.NativeCommandList->SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList->IASetPrimitiveTopology(dx12Pipeline.PrimitiveTopology);
        }

        public override void SetBindTable(RHIBindTable bindTable, in uint tableIndex)
        {
            Dx12BindTable dx12BindTable = bindTable as Dx12BindTable;
            Dx12BindTableLayout dx12BindTableLayout = dx12BindTable.BindTableLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12BindTableLayout.Index, "error bindTable index");
#endif

            for (int i = 0; i < dx12BindTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;

                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12BindTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.All, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Vertex, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Fragment, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            throw new NotImplementedException();
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            throw new NotImplementedException();
        }

        /*public override void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer)
        {
            //Dx12IndirectCommandBuffer dx12IndirectCommandBuffer = indirectCommandBuffer as Dx12IndirectCommandBuffer;
            //dx12CommandBuffer.NativeCommandList->ExecuteIndirect(null, indirectCommandBuffer.Count, dx12IndirectCommandBuffer.NativeResource, indirectCommandBuffer.Offset, null, 0);
        }*/

        public override void BeginQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void EndQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void PushDebugGroup(string name)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->EndEvent();
        }

        internal override void EndPass()
        {
            PopDebugGroup();

            m_Pipeline = null;
            m_PipelineLayout = null;

            Dx12Device device = (m_CommandBuffer.CommandQueue as Dx12CommandQueue).Dx12Device;

            for (int i = 0; i < m_AttachmentInfos.length; ++i)
            {
                int index = m_AttachmentInfos[i].AttachmentInfo.Index;

                if (!m_AttachmentInfos[i].bDepthStencil)
                {
                    device.FreeRtvDescriptor(index);
                }
                else
                {
                    device.FreeDsvDescriptor(index);
                }
            }
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12GraphicsEncoder : RHIGraphicsEncoder
    {
        protected byte m_SubPassIndex;
        protected TValueArray<Dx12AttachmentInfo> m_AttachmentInfos;

        public Dx12GraphicsEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_SubPassIndex = 0;
            m_CommandBuffer = cmdBuffer;
            m_AttachmentInfos = new TValueArray<Dx12AttachmentInfo>(5);
        }

        internal override void BeginPass(in RHIGraphicsPassDescriptor descriptor)
        {
            m_SubPassIndex = 0;
            PushDebugGroup(descriptor.Name);

            if (descriptor.SubpassDescriptors != null)
            {
                throw new NotImplementedException("Dx12 is not support subpass. Please do not use subpass descriptors");
            }

            m_AttachmentInfos.Clear();
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            D3D12_CPU_DESCRIPTOR_HANDLE? dsvHandle = null;
            D3D12_CPU_DESCRIPTOR_HANDLE* rtvHandles = stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[descriptor.ColorAttachmentDescriptors.Length];

            // create render target views
            for (int i = 0; i < descriptor.ColorAttachmentDescriptors.Length; ++i)
            {
                Dx12Texture texture = descriptor.ColorAttachmentDescriptors.Span[i].RenderTarget as Dx12Texture;
                Debug.Assert(texture != null);

                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.BaseArrayLayer = 0;
                    viewDescriptor.ArrayLayerCount = texture.Descriptor.Extent.z;
                    viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ETextureViewType.Pending;
                    viewDescriptor.Dimension = texture.Descriptor.Dimension;
                }
                D3D12_RENDER_TARGET_VIEW_DESC desc = new D3D12_RENDER_TARGET_VIEW_DESC();
                desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                desc.ViewDimension = Dx12Utility.ConvertToDx12TextureRTVDimension(texture.Descriptor.Dimension);
                Dx12Utility.FillTexture2DRTV(ref desc.Texture2D, viewDescriptor);
                Dx12Utility.FillTexture3DRTV(ref desc.Texture3D, viewDescriptor);
                Dx12Utility.FillTexture2DArrayRTV(ref desc.Texture2DArray, viewDescriptor);

                Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
                {
                    dx12AttachmentInfo.bDepthStencil = false;
                    dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateRtvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                rtvHandles[i] = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice->CreateRenderTargetView(texture.NativeResource, &desc, rtvHandles[i]);
            }

            // create depth stencil view
            if (descriptor.DepthStencilAttachmentDescriptor != null)
            {
                Dx12Texture texture = descriptor.DepthStencilAttachmentDescriptor?.DepthStencilTarget as Dx12Texture;
                Debug.Assert(texture != null);

                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.BaseArrayLayer = 0;
                    viewDescriptor.ArrayLayerCount = texture.Descriptor.Extent.z;
                    viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ETextureViewType.Pending;
                    viewDescriptor.Dimension = texture.Descriptor.Dimension;
                }
                D3D12_DEPTH_STENCIL_VIEW_DESC desc = new D3D12_DEPTH_STENCIL_VIEW_DESC();
                desc.Flags = Dx12Utility.GetDx12DSVFlag(descriptor.DepthStencilAttachmentDescriptor.Value.DepthReadOnly, descriptor.DepthStencilAttachmentDescriptor.Value.StencilReadOnly);
                desc.Format = Dx12Utility.ConvertToDx12Format(texture.Descriptor.Format);
                desc.ViewDimension = Dx12Utility.ConvertToDx12TextureDSVDimension(texture.Descriptor.Dimension);
                /*Dx12Utility.FillTexture2DDSV(ref desc.Texture2D, viewDescriptor);
                Dx12Utility.FillTexture3DDSV(ref desc.Texture3D, viewDescriptor);
                Dx12Utility.FillTexture2DArrayDSV(ref desc.Texture2DArray, viewDescriptor);*/

                Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
                {
                    dx12AttachmentInfo.bDepthStencil = true;
                    dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateDsvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                dsvHandle = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice->CreateDepthStencilView(texture.NativeResource, &desc, dsvHandle.Value);
            }

            // set render targets
            dx12CommandBuffer.NativeCommandList->OMSetRenderTargets((uint)descriptor.ColorAttachmentDescriptors.Length, rtvHandles, false, dsvHandle.HasValue ? (D3D12_CPU_DESCRIPTOR_HANDLE*)&dsvHandle : null);

            // clear render targets
            for (int i = 0; i < descriptor.ColorAttachmentDescriptors.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachmentDescriptors.Span[i];

                if (colorAttachmentDescriptor.LoadAction != ELoadAction.Clear)
                {
                    continue;
                }

                float4 clearValue = colorAttachmentDescriptor.ClearValue;
                dx12CommandBuffer.NativeCommandList->ClearRenderTargetView(rtvHandles[i], (float*)&clearValue, 0, null);
            }

            // clear depth stencil target
            if (dsvHandle.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor? depthStencilAttachmentDescriptor = descriptor.DepthStencilAttachmentDescriptor;
                if (depthStencilAttachmentDescriptor?.DepthLoadAction != ELoadAction.Clear && depthStencilAttachmentDescriptor?.StencilLoadAction != ELoadAction.Clear)
                {
                    return;
                }

                dx12CommandBuffer.NativeCommandList->ClearDepthStencilView(dsvHandle.Value, Dx12Utility.GetDx12ClearFlagByDSA(depthStencilAttachmentDescriptor.Value), depthStencilAttachmentDescriptor.Value.DepthClearValue, Convert.ToByte(depthStencilAttachmentDescriptor.Value.StencilClearValue), 0, null);
            }

            // set shading rate
            if (descriptor.ShadingRateDescriptor.HasValue)
            {
                if (descriptor.ShadingRateDescriptor.Value.ShadingRateTexture != null)
                {
                    D3D12_SHADING_RATE_COMBINER shadingRateCombiner = Dx12Utility.ConvertToDx12ShadingRateCombiner(descriptor.ShadingRateDescriptor.Value.ShadingRateCombiner);
                    Dx12Texture dx12Texture = descriptor.ShadingRateDescriptor.Value.ShadingRateTexture as Dx12Texture;
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), &shadingRateCombiner);
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRateImage(dx12Texture.NativeResource);
                }
                else
                {
                    D3D12_SHADING_RATE_COMBINER* shadingRateCombiners = stackalloc D3D12_SHADING_RATE_COMBINER[2] { D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX, D3D12_SHADING_RATE_COMBINER.D3D12_SHADING_RATE_COMBINER_MAX };
                    dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), shadingRateCombiners);
                    //dx12CommandBuffer.NativeCommandList->RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(descriptor.ShadingRateDescriptor.Value.ShadingRate), null);
                }
            }
        }

        public override void SetScissor(in Rect rect)
        {
            RECT tempScissor = new RECT((int)rect.left, (int)rect.top, (int)rect.right, (int)rect.bottom);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->RSSetScissorRects(1, &tempScissor);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            throw new NotImplementedException();
        }

        public override void SetViewport(in Viewport viewport)
        {
            D3D12_VIEWPORT tempViewport = new D3D12_VIEWPORT(viewport.TopLeftX, viewport.TopLeftY, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->RSSetViewports(1, &tempViewport);
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            throw new NotImplementedException();
        }

        public override void SetStencilRef(in uint value)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->OMSetStencilRef(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            float4 tempValue = value;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->OMSetBlendFactor((float*)&tempValue);
        }

        public override void NextSubpass()
        {
            throw new NotImplementedException("Dx12 is not support subpass. Please do not use NextSubpass command in graphics encoder");
        }

        public override void SetPipelineLayout(RHIPipelineLayout pipelineLayout)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_PipelineLayout = pipelineLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;
            dx12CommandBuffer.NativeCommandList->SetGraphicsRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetPipeline(RHIGraphicsPipeline pipeline)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            m_Pipeline = pipeline;
            Dx12GraphicsPipeline dx12Pipeline = pipeline as Dx12GraphicsPipeline;
            dx12CommandBuffer.NativeCommandList->SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList->IASetPrimitiveTopology(dx12Pipeline.PrimitiveTopology);
        }

        public override void SetBindTable(RHIBindTable bindTable, in uint tableIndex)
        {
            Dx12BindTable dx12BindTable = bindTable as Dx12BindTable;
            Dx12BindTableLayout dx12BindTableLayout = dx12BindTable.BindTableLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12BindTableLayout.Index, "error bindTable index");
#endif

            for (int i = 0; i < dx12BindTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12PipelineLayout dx12PipelineLayout = m_PipelineLayout as Dx12PipelineLayout;

                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12BindTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.All, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Vertex, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(EFunctionStage.Fragment, dx12BindTableLayout.Index, bindInfo.BindSlot, bindInfo.BindType);
                if (parameter.HasValue)
                {
                    Debug.Assert(parameter.Value.BindType == bindInfo.BindType);
                    dx12CommandBuffer.NativeCommandList->SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12BindTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot = 0, in uint offset = 0)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
            Dx12GraphicsPipeline dx12Pipeline = m_Pipeline as Dx12GraphicsPipeline;

            D3D12_VERTEX_BUFFER_VIEW vertexBufferView = new D3D12_VERTEX_BUFFER_VIEW
            {
                SizeInBytes = buffer.SizeInBytes - offset,
                StrideInBytes = (uint)dx12Pipeline.VertexStrides[slot],
                BufferLocation = dx12Buffer.NativeResource->GetGPUVirtualAddress() + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->IASetVertexBuffers(slot, 1, &vertexBufferView);
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset, in EIndexFormat format)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
            D3D12_INDEX_BUFFER_VIEW indexBufferView = new D3D12_INDEX_BUFFER_VIEW
            {
                Format = Dx12Utility.ConvertToDx12IndexFormat(format),
                SizeInBytes = buffer.SizeInBytes - offset,
                BufferLocation = dx12Buffer.NativeResource->GetGPUVirtualAddress() + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->IASetIndexBuffer(&indexBufferView);
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->DrawIndexedInstanced(indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ExecuteIndirect(dx12Device.DrawIndirectSignature, 1, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->ExecuteIndirect(dx12Device.DrawIndexedIndirectSignature, 1, dx12Buffer.NativeResource, offset, null, 0);
        }

        /*public override void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer)
        {
            //Dx12IndirectCommandBuffer dx12IndirectCommandBuffer = indirectCommandBuffer as Dx12IndirectCommandBuffer;
            //dx12CommandBuffer.NativeCommandList->ExecuteIndirect(null, indirectCommandBuffer.Count, dx12IndirectCommandBuffer.NativeResource, indirectCommandBuffer.Offset, null, 0);
        }*/

        public override void BeginQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->BeginQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void EndQuery(RHIQuery query, in uint index)
        {
            uint num = index * 8;
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case EQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                case EQueryType.BinaryOcclusion:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_BINARY_OCCLUSION, index, 1, dx12Query.QueryResult, num);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList->EndQuery(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index);
                    dx12CommandBuffer.NativeCommandList->ResolveQueryData(dx12Query.QueryHeap, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, index, 1, dx12Query.QueryResult, num);
                    break;
            }
        }

        public override void PushDebugGroup(string name)
        {
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList->EndEvent();
        }

        internal override void EndPass()
        {
            PopDebugGroup();

            m_Pipeline = null;
            m_PipelineLayout = null;

            Dx12Device device = (m_CommandBuffer.CommandQueue as Dx12CommandQueue).Dx12Device;

            for (int i = 0; i < m_AttachmentInfos.length; ++i)
            {
                int index = m_AttachmentInfos[i].AttachmentInfo.Index;

                if (!m_AttachmentInfos[i].bDepthStencil)
                {
                    device.FreeRtvDescriptor(index);
                }
                else
                {
                    device.FreeDsvDescriptor(index);
                }
            }
        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS0414, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
}
