using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class LoopbackNetworkTransport : INetworkTransport
    {
        private const int InitialQueueCapacity = 256;

        private readonly object _sync = new object();
        private readonly Queue<QueuedDatagram> _pendingInbound = new Queue<QueuedDatagram>(InitialQueueCapacity);
        private readonly List<QueuedDatagram> _activeInbound = new List<QueuedDatagram>(InitialQueueCapacity);
        private readonly int _maxDatagramSize;
        private readonly int _maxPendingInboundDatagrams;
        private readonly TransportStats? _stats;
        private readonly bool _threadSafe;
        private bool _running;
        private bool _disposed;

        private struct QueuedDatagram
        {
            public int ConnectionId;
            public ChannelType Channel;
            public byte[] Buffer;
            public int Length;
        }

        public LoopbackNetworkTransport(
            int maxDatagramSize = 65535,
            int maxPendingInboundDatagrams = 65536,
            TransportStats? stats = null,
            bool threadSafe = true)
        {
            if (maxDatagramSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDatagramSize));
            if (maxPendingInboundDatagrams <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPendingInboundDatagrams));

            _maxDatagramSize = maxDatagramSize;
            _maxPendingInboundDatagrams = maxPendingInboundDatagrams;
            _stats = stats;
            _threadSafe = threadSafe;
        }

        public bool IsRunning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_threadSafe)
                    return _running;

                lock (_sync)
                    return _running;
            }
        }

        public void Start()
        {
            if (!_threadSafe)
            {
                ThrowIfDisposed();
                _running = true;
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                _running = true;
            }
        }

        public void Stop()
        {
            if (!_threadSafe)
            {
                if (_disposed)
                    return;

                _running = false;
                ReleaseActiveInbound_NoLock();
                ClearPending_NoLock();
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                    return;

                _running = false;
                ReleaseActiveInbound_NoLock();
                ClearPending_NoLock();
            }
        }

        public int PollInbound(Span<InboundDatagram> destination)
        {
            long startTimestamp = 0;
            if (_stats != null)
                startTimestamp = Stopwatch.GetTimestamp();

            int count;
            int byteCount;
            if (!_threadSafe)
            {
                ThrowIfDisposed();
                count = PollInbound_NoLock(destination, out byteCount);
            }
            else
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    count = PollInbound_NoLock(destination, out byteCount);
                }
            }

            if (_stats != null)
            {
                _stats.RecordPoll(
                    count,
                    byteCount,
                    Stopwatch.GetTimestamp() - startTimestamp);
            }

            return count;
        }

        public void EnqueueOutbound(in OutboundDatagram datagram)
        {
            if (!_threadSafe)
            {
                ThrowIfDisposed();
                EnqueueOutbound_NoLock(in datagram);
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                EnqueueOutbound_NoLock(in datagram);
            }
        }

        public void FlushOutbound()
        {
            if (_stats == null)
                return;

            long startTimestamp = Stopwatch.GetTimestamp();
            // Loopback transport has no external socket flush.
            _stats.RecordFlush(Stopwatch.GetTimestamp() - startTimestamp);
        }

        public void CompleteInboundConsume()
        {
            if (!_threadSafe)
            {
                ThrowIfDisposed();
                ReleaseActiveInbound_NoLock();
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                ReleaseActiveInbound_NoLock();
            }
        }

        public void Dispose()
        {
            if (!_threadSafe)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _running = false;
                ReleaseActiveInbound_NoLock();
                ClearPending_NoLock();
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _running = false;
                ReleaseActiveInbound_NoLock();
                ClearPending_NoLock();
            }
        }

        private int PollInbound_NoLock(Span<InboundDatagram> destination, out int byteCount)
        {
            ReleaseActiveInbound_NoLock();

            int count = 0;
            byteCount = 0;
            if (_running)
            {
                while (count < destination.Length && _pendingInbound.Count > 0)
                {
                    QueuedDatagram datagram = _pendingInbound.Dequeue();
                    _activeInbound.Add(datagram);
                    destination[count++] = new InboundDatagram(
                        datagram.ConnectionId,
                        datagram.Channel,
                        new ReadOnlyMemory<byte>(datagram.Buffer, 0, datagram.Length));
                    byteCount += datagram.Length;
                }
            }

            return count;
        }

        private void EnqueueOutbound_NoLock(in OutboundDatagram datagram)
        {
            if (!_running)
                return;

            int length = datagram.Payload.Length;
            if (length > _maxDatagramSize)
                throw new InvalidOperationException($"Datagram too large. Length={length}, Max={_maxDatagramSize}.");

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            datagram.Payload.Span.CopyTo(buffer.AsSpan(0, length));

            if (_pendingInbound.Count >= _maxPendingInboundDatagrams)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw new InvalidOperationException(
                    $"Loopback pending inbound queue exceeded cap {_maxPendingInboundDatagrams}.");
            }

            _pendingInbound.Enqueue(new QueuedDatagram
            {
                ConnectionId = datagram.ConnectionId,
                Channel = datagram.Channel,
                Buffer = buffer,
                Length = length
            });

            _stats?.RecordEnqueue(length);
        }

        private void ReleaseActiveInbound_NoLock()
        {
            for (int i = 0; i < _activeInbound.Count; i++)
                ArrayPool<byte>.Shared.Return(_activeInbound[i].Buffer);

            _activeInbound.Clear();
        }

        private void ClearPending_NoLock()
        {
            while (_pendingInbound.Count > 0)
            {
                var datagram = _pendingInbound.Dequeue();
                ArrayPool<byte>.Shared.Return(datagram.Buffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LoopbackNetworkTransport));
        }
    }
}
