
using System;

using Godot;

namespace Common.Network
{
    public class ByteRingBuffer
    {
        private readonly byte[] m_Buffer;
        private int m_Head; // next write
        private int m_Tail; // next read
        private int m_Count;

        public ByteRingBuffer(int capacity)
        {
            m_Buffer = new byte[capacity];
        }

        public int Capacity => m_Buffer.Length;
        public int Count => m_Count;
        public int Free => Capacity - m_Count;

        /// <summary>Write up to <paramref name="length"/> bytes from <paramref name="src"/> into the ring. Returns number actually written.</summary>
        public int Write(ReadOnlySpan<byte> src)
        {
            int toWrite = Math.Min(src.Length, Free);
            if (toWrite == 0) return 0;
            // if wraps
            int first = Math.Min(toWrite, Capacity - m_Head);
            src.Slice(0, first).CopyTo(m_Buffer.AsSpan(m_Head));
            int second = toWrite - first;
            if (second > 0)
                src.Slice(first, second).CopyTo(m_Buffer.AsSpan(0));
            m_Head = (m_Head + toWrite) % Capacity;
            m_Count += toWrite;
            return toWrite;
        }

        /// <summary>Peek up to <paramref name="length"/> bytes starting at tail, without advancing.</summary>
        public Span<byte> Peek(int length)
        {
            length = Math.Min(length, m_Count);
            if (length == 0) return Span<byte>.Empty;
            // but may wrap, so user should copy out in two pieces if needed.
            return m_Buffer.AsSpan(m_Tail, length);
        }

        /// <summary>Read (advance) <paramref name="length"/> bytes. Does NOT return data.</summary>
        public void Advance(int length)
        {
            length = Math.Min(length, m_Count);
            m_Tail = (m_Tail + length) % Capacity;
            m_Count -= length;
        }

        /// <summary>Copy out <paramref name="length"/> bytes into <paramref name="dst"/>, advancing the tail.</summary>
        public int Read(Span<byte> dst)
        {
            int toRead = Math.Min(dst.Length, m_Count);
            if (toRead == 0) return 0;
            int first = Math.Min(toRead, Capacity - m_Tail);
            m_Buffer.AsSpan(m_Tail, first).CopyTo(dst);
            int second = toRead - first;
            if (second > 0)
                m_Buffer.AsSpan(0, second).CopyTo(dst.Slice(first));
            Advance(toRead);
            return toRead;
        }
    }
}