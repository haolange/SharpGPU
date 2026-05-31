// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12;

public partial struct WorkGraphDescription : IStateSubObjectDescription, IStateSubObjectDescriptionMarshal
{
    StateSubObjectType IStateSubObjectDescription.SubObjectType => StateSubObjectType.WorkGraph;

    IntPtr IStateSubObjectDescriptionMarshal.__MarshalAlloc(Dictionary<StateSubObject, IntPtr> subObjectLookup)
    {
        __Native native = default;
        __MarshalTo(ref native);

        IntPtr nativePtr = Marshal.AllocHGlobal(Marshal.SizeOf<__Native>());
        Marshal.StructureToPtr(native, nativePtr, false);
        return nativePtr;
    }

    void IStateSubObjectDescriptionMarshal.__MarshalFree(ref IntPtr pDesc)
    {
        if (pDesc == IntPtr.Zero)
        {
            return;
        }

        __Native native = Marshal.PtrToStructure<__Native>(pDesc);
        __MarshalFree(ref native);
        Marshal.FreeHGlobal(pDesc);
        pDesc = IntPtr.Zero;
    }
}
