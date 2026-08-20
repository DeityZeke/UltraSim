// Shared/Network/Buffers/BufferPool.cs
// High-performance buffer pooling using ArrayPool

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Network.Shared.Network
{
    /// <summary>
    /// Static buffer pool wrapper around ArrayPool for network operations.
    /// Thread-safe, zero-allocation after warmup.
    /// </summary>
    public static class BufferPool
    {
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Rent a buffer of at least the specified size.
        /// Buffer may be larger than requested.
        /// MUST be returned via Return() when done.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] Rent(int minimumSize)
        {
            return Pool.Rent(minimumSize);
        }

        /// <summary>
        /// Return a rented buffer to the pool.
        /// Buffer contents may or may not be cleared based on clearArray parameter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(byte[] buffer, bool clearArray = false)
        {
            if (buffer != null)
            {
                Pool.Return(buffer, clearArray);
            }
        }

        /// <summary>
        /// Rent a buffer and wrap it in a disposable scope.
        /// Automatically returns to pool when disposed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RentedBuffer RentScoped(int minimumSize)
        {
            return new RentedBuffer(Pool.Rent(minimumSize));
        }

        /// <summary>
        /// RAII wrapper for rented buffers. Automatically returns on dispose.
        /// Use with 'using' statement for automatic cleanup.
        /// </summary>
        public readonly struct RentedBuffer : IDisposable
        {
            public readonly byte[] Buffer;

            public int Length => Buffer.Length;

            public Span<byte> Span => Buffer.AsSpan();

            public Memory<byte> Memory => Buffer.AsMemory();

            internal RentedBuffer(byte[] buffer)
            {
                Buffer = buffer;
            }

            public void Dispose()
            {
                Return(Buffer);
            }

            // Implicit conversion to span for convenience
            public static implicit operator Span<byte>(RentedBuffer rented) => rented.Buffer;
            public static implicit operator ReadOnlySpan<byte>(RentedBuffer rented) => rented.Buffer;
        }
    }

    /// <summary>
    /// A segment of a pooled buffer with offset and length tracking.
    /// Does NOT own the buffer - caller must manage lifetime.
    /// </summary>
    public readonly struct BufferSegment
    {
        public readonly byte[] Array;
        public readonly int Offset;
        public readonly int Count;

        public BufferSegment(byte[] array, int offset, int count)
        {
            Array = array;
            Offset = offset;
            Count = count;
        }

        public BufferSegment(byte[] array) : this(array, 0, array.Length) { }

        public Span<byte> Span => Array.AsSpan(Offset, Count);
        public ReadOnlySpan<byte> ReadOnlySpan => Array.AsSpan(Offset, Count);
        public Memory<byte> Memory => Array.AsMemory(Offset, Count);
        public ReadOnlyMemory<byte> ReadOnlyMemory => Array.AsMemory(Offset, Count);

        public ArraySegment<byte> ToArraySegment() => new(Array, Offset, Count);

        public bool IsEmpty => Array == null || Count == 0;

        public static BufferSegment Empty => default;
    }
}
