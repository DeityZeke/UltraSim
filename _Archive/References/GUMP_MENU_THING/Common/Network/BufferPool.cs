
using System;
using System.Buffers;

using Godot;

namespace Common.Network
{
    public static class BufferPool
    {
        private static readonly ArrayPool<byte> m_Pool = ArrayPool<byte>.Shared;

        public static byte[] Rent(int size) => m_Pool.Rent(size);
        public static void Return(byte[] buffer) => m_Pool.Return(buffer);
    }
}