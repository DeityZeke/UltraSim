using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public readonly struct LoopbackLinkProfile
    {
        public readonly int BaseLatencyMs;
        public readonly int JitterMs;
        public readonly double DropChance;

        public LoopbackLinkProfile(int baseLatencyMs = 0, int jitterMs = 0, double dropChance = 0d)
        {
            if (baseLatencyMs < 0)
                throw new ArgumentOutOfRangeException(nameof(baseLatencyMs));
            if (jitterMs < 0)
                throw new ArgumentOutOfRangeException(nameof(jitterMs));
            if (dropChance < 0d || dropChance > 1d)
                throw new ArgumentOutOfRangeException(nameof(dropChance));

            BaseLatencyMs = baseLatencyMs;
            JitterMs = jitterMs;
            DropChance = dropChance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int SampleDelayMs(ref uint randomState)
        {
            int jitter = 0;
            if (JitterMs > 0)
                jitter = NextIntInclusive(ref randomState, -JitterMs, JitterMs);

            int delay = BaseLatencyMs + jitter;
            return delay < 0 ? 0 : delay;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ShouldDrop(ref uint randomState)
        {
            if (DropChance <= 0d)
                return false;
            if (DropChance >= 1d)
                return true;

            return NextUnitDouble(ref randomState) < DropChance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextIntInclusive(ref uint state, int minInclusive, int maxInclusive)
        {
            uint range = (uint)(maxInclusive - minInclusive + 1);
            return minInclusive + (int)(NextRandom(ref state) % range);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NextUnitDouble(ref uint state)
        {
            return (NextRandom(ref state) & 0xFFFFFF) / 16777216d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint NextRandom(ref uint state)
        {
            if (state == 0)
                state = 0xA341316Cu;

            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }
    }

    public sealed class LoopbackTransportPair : IDisposable
    {
        private const int InitialQueueCapacity = 256;

        private readonly SharedState _shared;
        private readonly EndpointTransport _endpointA;
        private readonly EndpointTransport _endpointB;
        private bool _disposed;

        public LoopbackTransportPair(
            LoopbackLinkProfile aToB = default,
            LoopbackLinkProfile bToA = default,
            int maxDatagramSize = 65535,
            TransportStats? endpointAStats = null,
            TransportStats? endpointBStats = null,
            bool threadSafe = true,
            uint randomSeed = 0xC0FFEEu)
        {
            if (maxDatagramSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDatagramSize));

            _shared = new SharedState(
                maxDatagramSize,
                aToB,
                bToA,
                threadSafe,
                randomSeed);

            _endpointA = new EndpointTransport(_shared, endpointIndex: 0, endpointAStats);
            _endpointB = new EndpointTransport(_shared, endpointIndex: 1, endpointBStats);
        }

        public INetworkTransport EndpointA => _endpointA;
        public INetworkTransport EndpointB => _endpointB;

        public void Start()
        {
            _endpointA.Start();
            _endpointB.Start();
        }

        public void Stop()
        {
            _endpointA.Stop();
            _endpointB.Stop();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _endpointA.Dispose();
            _endpointB.Dispose();
        }

        private readonly struct DatagramPriority : IComparable<DatagramPriority>
        {
            public readonly long DueTick;
            public readonly long Sequence;

            public DatagramPriority(long dueTick, long sequence)
            {
                DueTick = dueTick;
                Sequence = sequence;
            }

            public int CompareTo(DatagramPriority other)
            {
                int dueCompare = DueTick.CompareTo(other.DueTick);
                if (dueCompare != 0)
                    return dueCompare;
                return Sequence.CompareTo(other.Sequence);
            }
        }

        private struct QueuedDatagram
        {
            public int ConnectionId;
            public ChannelType Channel;
            public byte[] Buffer;
            public int Length;
        }

        private sealed class EndpointState
        {
            public readonly PriorityQueue<QueuedDatagram, DatagramPriority> PendingInbound =
                new PriorityQueue<QueuedDatagram, DatagramPriority>(InitialQueueCapacity);
            public readonly List<QueuedDatagram> ActiveInbound = new List<QueuedDatagram>(InitialQueueCapacity);
            public bool Running;
            public bool Disposed;
        }

        private sealed class SharedState
        {
            public enum ScheduleResult : byte
            {
                NotRunning = 0,
                Dropped = 1,
                Queued = 2,
            }

            public readonly object Sync = new object();
            public readonly int MaxDatagramSize;
            public readonly bool ThreadSafe;
            public readonly EndpointState[] Endpoints;
            private readonly LoopbackLinkProfile _aToB;
            private readonly LoopbackLinkProfile _bToA;
            private uint _aToBRandom;
            private uint _bToARandom;
            private long _sequence;

            public SharedState(
                int maxDatagramSize,
                LoopbackLinkProfile aToB,
                LoopbackLinkProfile bToA,
                bool threadSafe,
                uint randomSeed)
            {
                MaxDatagramSize = maxDatagramSize;
                ThreadSafe = threadSafe;
                Endpoints = new[] { new EndpointState(), new EndpointState() };
                _aToB = aToB;
                _bToA = bToA;
                _aToBRandom = randomSeed != 0 ? randomSeed : 0xA341316Cu;
                _bToARandom = (_aToBRandom ^ 0x9E3779B9u) + 1u;
                _sequence = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ScheduleResult TryScheduleOutbound(int sourceEndpoint, in OutboundDatagram datagram)
            {
                int destinationEndpoint = sourceEndpoint == 0 ? 1 : 0;
                EndpointState source = Endpoints[sourceEndpoint];
                EndpointState destination = Endpoints[destinationEndpoint];
                if (!source.Running || !destination.Running)
                    return ScheduleResult.NotRunning;

                int length = datagram.Payload.Length;
                if (length > MaxDatagramSize)
                    throw new InvalidOperationException($"Datagram too large. Length={length}, Max={MaxDatagramSize}.");

                ref uint randomState = ref (sourceEndpoint == 0 ? ref _aToBRandom : ref _bToARandom);
                LoopbackLinkProfile profile = sourceEndpoint == 0 ? _aToB : _bToA;
                if (profile.ShouldDrop(ref randomState))
                    return ScheduleResult.Dropped;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                datagram.Payload.Span.CopyTo(buffer.AsSpan(0, length));

                long dueTick = Stopwatch.GetTimestamp() + MillisecondsToStopwatchTicks(profile.SampleDelayMs(ref randomState));
                long sequence = ++_sequence;
                destination.PendingInbound.Enqueue(new QueuedDatagram
                {
                    ConnectionId = datagram.ConnectionId,
                    Channel = datagram.Channel,
                    Buffer = buffer,
                    Length = length
                }, new DatagramPriority(dueTick, sequence));
                return ScheduleResult.Queued;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static long MillisecondsToStopwatchTicks(int milliseconds)
            {
                if (milliseconds <= 0)
                    return 0;
                return (Stopwatch.Frequency * milliseconds) / 1000L;
            }
        }

        private sealed class EndpointTransport : INetworkTransport
        {
            private readonly SharedState _shared;
            private readonly int _endpointIndex;
            private readonly TransportStats? _stats;

            public EndpointTransport(SharedState shared, int endpointIndex, TransportStats? stats)
            {
                _shared = shared;
                _endpointIndex = endpointIndex;
                _stats = stats;
            }

            public void Start()
            {
                ExecuteLocked(static state => state.Running = true);
            }

            public void Stop()
            {
                ExecuteLocked(state =>
                {
                    if (state.Disposed)
                        return;

                    state.Running = false;
                    ReleaseActiveInbound_NoLock(state);
                    ClearPending_NoLock(state);
                });
            }

            public int PollInbound(Span<InboundDatagram> destination)
            {
                long startTimestamp = 0;
                if (_stats != null)
                    startTimestamp = Stopwatch.GetTimestamp();

                int count;
                int bytes;
                if (_shared.ThreadSafe)
                {
                    lock (_shared.Sync)
                    {
                        EndpointState state = _shared.Endpoints[_endpointIndex];
                        ThrowIfDisposed(state);
                        count = PollInbound_NoLock(state, destination, out bytes);
                    }
                }
                else
                {
                    EndpointState state = _shared.Endpoints[_endpointIndex];
                    ThrowIfDisposed(state);
                    count = PollInbound_NoLock(state, destination, out bytes);
                }

                _stats?.RecordPoll(
                    count,
                    bytes,
                    Stopwatch.GetTimestamp() - startTimestamp);
                return count;
            }

            public void EnqueueOutbound(in OutboundDatagram datagram)
            {
                if (_shared.ThreadSafe)
                {
                    lock (_shared.Sync)
                    {
                        EndpointState state = _shared.Endpoints[_endpointIndex];
                        ThrowIfDisposed(state);
                        SharedState.ScheduleResult result = _shared.TryScheduleOutbound(_endpointIndex, in datagram);
                        if (result == SharedState.ScheduleResult.Queued)
                            _stats?.RecordEnqueue(datagram.Payload.Length);
                        else if (result == SharedState.ScheduleResult.Dropped)
                            _stats?.RecordDrop(datagram.Payload.Length);
                    }
                }
                else
                {
                    EndpointState state = _shared.Endpoints[_endpointIndex];
                    ThrowIfDisposed(state);
                    SharedState.ScheduleResult result = _shared.TryScheduleOutbound(_endpointIndex, in datagram);
                    if (result == SharedState.ScheduleResult.Queued)
                        _stats?.RecordEnqueue(datagram.Payload.Length);
                    else if (result == SharedState.ScheduleResult.Dropped)
                        _stats?.RecordDrop(datagram.Payload.Length);
                }
            }

            public void FlushOutbound()
            {
                if (_stats == null)
                    return;

                long startTimestamp = Stopwatch.GetTimestamp();
                _stats.RecordFlush(Stopwatch.GetTimestamp() - startTimestamp);
            }

            public void CompleteInboundConsume()
            {
                ExecuteLocked(state =>
                {
                    ThrowIfDisposed(state);
                    ReleaseActiveInbound_NoLock(state);
                });
            }

            public void Dispose()
            {
                ExecuteLocked(state =>
                {
                    if (state.Disposed)
                        return;

                    state.Disposed = true;
                    state.Running = false;
                    ReleaseActiveInbound_NoLock(state);
                    ClearPending_NoLock(state);
                });
            }

            private int PollInbound_NoLock(
                EndpointState endpoint,
                Span<InboundDatagram> destination,
                out int byteCount)
            {
                ReleaseActiveInbound_NoLock(endpoint);

                int count = 0;
                byteCount = 0;
                if (!endpoint.Running)
                    return 0;

                long now = Stopwatch.GetTimestamp();
                while (count < destination.Length && endpoint.PendingInbound.Count > 0)
                {
                    if (!endpoint.PendingInbound.TryPeek(out QueuedDatagram _, out DatagramPriority priority))
                        break;
                    if (priority.DueTick > now)
                        break;

                    QueuedDatagram datagram = endpoint.PendingInbound.Dequeue();
                    endpoint.ActiveInbound.Add(datagram);
                    destination[count++] = new InboundDatagram(
                        datagram.ConnectionId,
                        datagram.Channel,
                        new ReadOnlyMemory<byte>(datagram.Buffer, 0, datagram.Length));
                    byteCount += datagram.Length;
                }

                return count;
            }

            private static void ReleaseActiveInbound_NoLock(EndpointState endpoint)
            {
                for (int i = 0; i < endpoint.ActiveInbound.Count; i++)
                    ArrayPool<byte>.Shared.Return(endpoint.ActiveInbound[i].Buffer);

                endpoint.ActiveInbound.Clear();
            }

            private static void ClearPending_NoLock(EndpointState endpoint)
            {
                while (endpoint.PendingInbound.Count > 0)
                {
                    QueuedDatagram datagram = endpoint.PendingInbound.Dequeue();
                    ArrayPool<byte>.Shared.Return(datagram.Buffer);
                }
            }

            private static void ThrowIfDisposed(EndpointState endpoint)
            {
                if (endpoint.Disposed)
                    throw new ObjectDisposedException(nameof(LoopbackTransportPair));
            }

            private void ExecuteLocked(Action<EndpointState> action)
            {
                if (_shared.ThreadSafe)
                {
                    lock (_shared.Sync)
                    {
                        action(_shared.Endpoints[_endpointIndex]);
                    }
                }
                else
                {
                    action(_shared.Endpoints[_endpointIndex]);
                }
            }
        }
    }
}
