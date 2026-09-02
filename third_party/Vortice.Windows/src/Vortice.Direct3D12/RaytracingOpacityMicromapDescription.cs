// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12;

/// <summary>
/// Describes one opacity micromap entry.
/// Layout matches <c>D3D12_RAYTRACING_OPACITY_MICROMAP_DESC</c> from Agility SDK 1.619.3
/// (<c>ByteOffset</c> plus 16-bit <c>SubdivisionLevel</c> / <c>Format</c> bitfields).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
public partial struct RaytracingOpacityMicromapDescription
{
    public uint ByteOffset;
    private uint _packedSubdivisionAndFormat;

    public uint SubdivisionLevel
    {
        get => _packedSubdivisionAndFormat & 0xFFFFu;
        set => _packedSubdivisionAndFormat = (_packedSubdivisionAndFormat & 0xFFFF0000u) | (value & 0xFFFFu);
    }

    public RaytracingOpacityMicromapFormat Format
    {
        get => (RaytracingOpacityMicromapFormat)((_packedSubdivisionAndFormat >> 16) & 0xFFFFu);
        set => _packedSubdivisionAndFormat = (_packedSubdivisionAndFormat & 0x0000FFFFu) | (((uint)value & 0xFFFFu) << 16);
    }
}
