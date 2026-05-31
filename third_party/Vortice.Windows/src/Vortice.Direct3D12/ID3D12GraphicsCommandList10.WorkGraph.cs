// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12;

public unsafe partial class ID3D12GraphicsCommandList10
{
    public void SetWorkGraphProgram(in SetWorkGraphDescription workGraph)
    {
        SetWorkGraphDescription description = workGraph;
        SetProgramDescription.__Native native = default;
        native.Type = ProgramType.WorkGraph;
        description.__MarshalTo(ref native.WorkGraph);
        try
        {
            ((delegate* unmanaged[Stdcall]<System.IntPtr, void*, void>)this[84])(NativePointer, &native);
        }
        finally
        {
            description.__MarshalFree(ref native.WorkGraph);
        }
    }
}
