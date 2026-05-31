// Copyright (c) Amer Koleci and contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D11;

public partial struct SubresourceData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubresourceData"/> struct.
    /// </summary>
    /// <param name="dataPointer">The dataPointer.</param>
    /// <param name="rowPitch">The row pitch.</param>
    /// <param name="slicePitch">The slice pitch.</param>
    public SubresourceData(IntPtr dataPointer, uint rowPitch = 0, uint slicePitch = 0)
    {
        DataPointer = dataPointer;
        RowPitch = rowPitch;
        SlicePitch = slicePitch;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubresourceData"/> struct.
    /// </summary>
    /// <param name="dataPointer">The dataPointer.</param>
    /// <param name="rowPitch">The row pitch.</param>
    /// <param name="slicePitch">The slice pitch.</param>
    public unsafe SubresourceData(void* dataPointer, uint rowPitch = 0, uint slicePitch = 0)
    {
        DataPointer = new IntPtr(dataPointer);
        RowPitch = rowPitch;
        SlicePitch = slicePitch;
    }
}
