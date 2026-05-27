using System;

namespace SharpGPU.Mathematics
{
    public struct Rect : IEquatable<Rect>
    {
        public uint left;
        public uint top;
        public uint right;
        public uint bottom;

        public Rect(in uint left, in uint top, in uint right, in uint bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }

        public bool Equals(Rect other) => left == other.left && top == other.top && right == other.right && bottom == other.bottom;
        public override bool Equals(object? obj) => obj is Rect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(left, top, right, bottom);
        public static bool operator ==(Rect left, Rect right) => left.Equals(right);
        public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
    }

    public struct Viewport : IEquatable<Viewport>
    {
        public uint TopLeftX;
        public uint TopLeftY;
        public uint Width;
        public uint Height;
        public float MinDepth;
        public float MaxDepth;

        public Viewport(in uint topLeftX, in uint topLeftY, in uint width, in uint height, in float minDepth = 0f, in float maxDepth = 1f)
        {
            TopLeftX = topLeftX;
            TopLeftY = topLeftY;
            Width = width;
            Height = height;
            MinDepth = minDepth;
            MaxDepth = maxDepth;
        }

        public bool Equals(Viewport other)
        {
            return TopLeftX == other.TopLeftX
                && TopLeftY == other.TopLeftY
                && Width == other.Width
                && Height == other.Height
                && MinDepth.Equals(other.MinDepth)
                && MaxDepth.Equals(other.MaxDepth);
        }

        public override bool Equals(object? obj) => obj is Viewport other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth);
        public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);
        public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);
    }
}
