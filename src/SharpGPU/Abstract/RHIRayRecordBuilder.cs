using System;
using System.Buffers.Binary;

namespace Infinity.Graphics
{
    public sealed class RHIRayRecordBuilder
    {
        private byte[] m_Buffer;
        private int m_Length;

        public int Length => m_Length;

        public RHIRayRecordBuilder(int initialCapacity = 64)
        {
            if (initialCapacity <= 0)
            {
                initialCapacity = 64;
            }

            m_Buffer = new byte[initialCapacity];
            m_Length = 0;
        }

        public void Clear()
        {
            m_Length = 0;
        }

        public void WriteU32(in uint value)
        {
            EnsureCapacity(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(m_Buffer.AsSpan(m_Length, sizeof(uint)), value);
            m_Length += sizeof(uint);
        }

        public void WriteU64(in ulong value)
        {
            EnsureCapacity(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(m_Buffer.AsSpan(m_Length, sizeof(ulong)), value);
            m_Length += sizeof(ulong);
        }

        public void WriteF32(in float value)
        {
            WriteU32(BitConverter.SingleToUInt32Bits(value));
        }

        public void WriteBytes(in ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return;
            }

            EnsureCapacity(bytes.Length);
            bytes.CopyTo(m_Buffer.AsSpan(m_Length));
            m_Length += bytes.Length;
        }

        public void WriteBytes(in ReadOnlyMemory<byte> bytes)
        {
            WriteBytes(bytes.Span);
        }

        public void AlignTo(in int alignment, in byte fill = 0)
        {
            if (alignment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be greater than zero.");
            }

            int alignedLength = ((m_Length + alignment - 1) / alignment) * alignment;
            int padding = alignedLength - m_Length;
            if (padding <= 0)
            {
                return;
            }

            EnsureCapacity(padding);
            if (fill != 0)
            {
                m_Buffer.AsSpan(m_Length, padding).Fill(fill);
            }
            m_Length = alignedLength;
        }

        public void WriteResourceIndex(in uint argumentTableIndex, in uint slot, in uint arrayIndex)
        {
            // Standard token payload layout: {tableIndex, slot, arrayIndex}.
            WriteU32(argumentTableIndex);
            WriteU32(slot);
            WriteU32(arrayIndex);
        }

        public ReadOnlyMemory<byte> ToMemory()
        {
            return new ReadOnlyMemory<byte>(ToArray());
        }

        public byte[] ToArray()
        {
            if (m_Length == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] result = new byte[m_Length];
            Buffer.BlockCopy(m_Buffer, 0, result, 0, m_Length);
            return result;
        }

        private void EnsureCapacity(in int appendLength)
        {
            int required = m_Length + appendLength;
            if (required <= m_Buffer.Length)
            {
                return;
            }

            int newCapacity = m_Buffer.Length;
            while (newCapacity < required)
            {
                newCapacity *= 2;
            }

            Array.Resize(ref m_Buffer, newCapacity);
        }
    }
}
