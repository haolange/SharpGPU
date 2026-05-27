using System;

namespace SharpGPU.Collections
{
    [Serializable]
    public class TArray<T>
    {
        public int length;

        private T[] m_Array;

        public ref T this[int index] => ref m_Array[index];

        public TArray()
            : this(64)
        {
        }

        public TArray(in int capacity = 64)
        {
            length = 0;
            m_Array = new T[capacity];
        }

        public void Clear()
        {
            length = 0;
        }

        public int Add(in T value)
        {
            if (length >= m_Array.Length)
            {
                var newArray = new T[m_Array.Length * 2];
                Array.Copy(m_Array, newArray, m_Array.Length);
                m_Array = newArray;
            }

            m_Array[length] = value;
            int cacheIndex = length;
            ++length;

            return cacheIndex;
        }
    }
}
