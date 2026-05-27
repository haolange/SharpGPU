using System;
using System.Runtime.InteropServices;

namespace SharpGPU.Mathematics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct bool4 : IEquatable<bool4>
    {
        public bool x;
        public bool y;
        public bool z;
        public bool w;

        public bool4(bool x, bool y, bool z, bool w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public bool4(bool value)
            : this(value, value, value, value)
        {
        }

        public bool Equals(bool4 other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override bool Equals(object? obj) => obj is bool4 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public static implicit operator bool4(bool value) => new bool4(value);
        public static bool operator ==(bool4 left, bool4 right) => left.Equals(right);
        public static bool operator !=(bool4 left, bool4 right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct uint2 : IEquatable<uint2>
    {
        public uint x;
        public uint y;

        public uint2 xy => this;

        public uint2(uint x, uint y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(uint2 other) => x == other.x && y == other.y;
        public override bool Equals(object? obj) => obj is uint2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(uint2 left, uint2 right) => left.Equals(right);
        public static bool operator !=(uint2 left, uint2 right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct uint3 : IEquatable<uint3>
    {
        public uint x;
        public uint y;
        public uint z;

        public uint2 xy => new uint2(x, y);

        public uint3(uint x, uint y, uint z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public uint3(uint2 xy, uint z)
        {
            x = xy.x;
            y = xy.y;
            this.z = z;
        }

        public bool Equals(uint3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is uint3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static bool operator ==(uint3 left, uint3 right) => left.Equals(right);
        public static bool operator !=(uint3 left, uint3 right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct int3 : IEquatable<int3>
    {
        public int x;
        public int y;
        public int z;

        public int3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(int3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is int3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static bool operator ==(int3 left, int3 right) => left.Equals(right);
        public static bool operator !=(int3 left, int3 right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct float4 : IEquatable<float4>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public float4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public bool Equals(float4 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        public override bool Equals(object? obj) => obj is float4 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public static bool operator ==(float4 left, float4 right) => left.Equals(right);
        public static bool operator !=(float4 left, float4 right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct float4x4 : IEquatable<float4x4>
    {
        public float4 c0;
        public float4 c1;
        public float4 c2;
        public float4 c3;

        public float4x4(float4 c0, float4 c1, float4 c2, float4 c3)
        {
            this.c0 = c0;
            this.c1 = c1;
            this.c2 = c2;
            this.c3 = c3;
        }

        public bool Equals(float4x4 other) => c0 == other.c0 && c1 == other.c1 && c2 == other.c2 && c3 == other.c3;
        public override bool Equals(object? obj) => obj is float4x4 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(c0, c1, c2, c3);
        public static bool operator ==(float4x4 left, float4x4 right) => left.Equals(right);
        public static bool operator !=(float4x4 left, float4x4 right) => !left.Equals(right);
    }
}
