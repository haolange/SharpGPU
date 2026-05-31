// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Runtime.CompilerServices;

namespace Vortice.Direct3D12;

public unsafe partial class ID3D12GraphicsCommandList7
{
    public void Barrier(in GlobalBarrier globalBarrier)
    {
        GlobalBarrier nativeBarrier = globalBarrier;
        BarrierGroup.__Native nativeGroup = new()
        {
            Type = BarrierType.Global,
            NumBarriers = 1,
        };
        nativeGroup.Union.pGlobalBarriers = &nativeBarrier;
        Barrier(1, &nativeGroup);
    }

    public void Barrier(in BufferBarrier bufferBarrier)
    {
        BufferBarrier barrier = bufferBarrier;
        BufferBarrier.__Native nativeBarrier = default;
        barrier.__MarshalTo(ref nativeBarrier);
        try
        {
            BarrierGroup.__Native nativeGroup = new()
            {
                Type = BarrierType.Buffer,
                NumBarriers = 1,
            };
            nativeGroup.Union.pBufferBarriers = &nativeBarrier;
            Barrier(1, &nativeGroup);
        }
        finally
        {
            barrier.__MarshalFree(ref nativeBarrier);
        }
    }

    public void Barrier(in TextureBarrier textureBarrier)
    {
        TextureBarrier barrier = textureBarrier;
        TextureBarrier.__Native nativeBarrier = default;
        barrier.__MarshalTo(ref nativeBarrier);
        try
        {
            BarrierGroup.__Native nativeGroup = new()
            {
                Type = BarrierType.Texture,
                NumBarriers = 1,
            };
            nativeGroup.Union.pTextureBarriers = &nativeBarrier;
            Barrier(1, &nativeGroup);
        }
        finally
        {
            barrier.__MarshalFree(ref nativeBarrier);
        }
    }

    public void Barrier(BarrierGroup barrierGroup)
    {
        BarrierGroup.__Native native = default;
        barrierGroup.__MarshalTo(ref native);
        Barrier(1, &native);
        barrierGroup.__MarshalFree(ref native);
    }

    public void ResourceBarrier(BarrierGroup[] barrierGroups)
    {
        ResourceBarrier((uint)barrierGroups.Length, barrierGroups);
    }

    public unsafe void ResourceBarrier(uint numBarrierGroups, BarrierGroup[] barrierGroups)
    {
        Span<BarrierGroup.__Native> barrierGroupsNative = (uint)numBarrierGroups * (uint)sizeof(BarrierGroup.__Native) < 1024U ? stackalloc BarrierGroup.__Native[(int)numBarrierGroups] : new BarrierGroup.__Native[numBarrierGroups];

        for (int i = 0; i < numBarrierGroups; ++i)
        {
            barrierGroups[i].__MarshalTo(ref barrierGroupsNative[i]);
        }

        fixed (BarrierGroup.__Native* pBarrierGroups = barrierGroupsNative)
        {
            Barrier(numBarrierGroups, pBarrierGroups);
        }

        for (int i = 0; i < numBarrierGroups; ++i)
        {
            barrierGroups[i].__MarshalFree(ref barrierGroupsNative[i]);
        }
    }

    public void ResourceBarrier(Span<BarrierGroup> barrierGroups)
    {
        ResourceBarrier((uint)barrierGroups.Length, barrierGroups);
    }

    public unsafe void ResourceBarrier(uint numBarrierGroups, Span<BarrierGroup> barrierGroups)
    {
        Span<BarrierGroup.__Native> barrierGroupsNative = (uint)numBarrierGroups * (uint)sizeof(BarrierGroup.__Native) < 1024U ? stackalloc BarrierGroup.__Native[(int)numBarrierGroups] : new BarrierGroup.__Native[numBarrierGroups];

        for (int i = 0; i < numBarrierGroups; ++i)
        {
            barrierGroups[i].__MarshalTo(ref barrierGroupsNative[i]);
        }

        fixed (BarrierGroup.__Native* pBarrierGroups = barrierGroupsNative)
        {
            Barrier(numBarrierGroups, pBarrierGroups);
        }

        for (int i = 0; i < numBarrierGroups; ++i)
        {
            barrierGroups[i].__MarshalFree(ref barrierGroupsNative[i]);
        }
    }
}
