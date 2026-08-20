using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltraSim.Network
{
    // Mo2: each counter field sits on its own 64-byte cache line to prevent false sharing when
    // RecordEnqueue, RecordPoll, RecordDrop, and RecordFlush are called from different threads.
    [StructLayout(LayoutKind.Explicit)]
    public sealed class TransportStats
    {
        [FieldOffset(0)]   private long _outboundPackets;
        [FieldOffset(64)]  private long _outboundBytes;
        [FieldOffset(128)] private long _inboundPackets;
        [FieldOffset(192)] private long _inboundBytes;
        [FieldOffset(256)] private long _droppedPackets;
        [FieldOffset(320)] private long _droppedBytes;
        [FieldOffset(384)] private long _pollCalls;
        [FieldOffset(448)] private long _pollTicks;
        [FieldOffset(512)] private long _flushCalls;
        [FieldOffset(576)] private long _flushTicks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordEnqueue(int bytes)
        {
            Interlocked.Increment(ref _outboundPackets);
            Interlocked.Add(ref _outboundBytes, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordPoll(int packets, int bytes, long elapsedTicks)
        {
            Interlocked.Increment(ref _pollCalls);
            Interlocked.Add(ref _pollTicks, elapsedTicks);

            if (packets > 0)
                Interlocked.Add(ref _inboundPackets, packets);

            if (bytes > 0)
                Interlocked.Add(ref _inboundBytes, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDrop(int bytes)
        {
            Interlocked.Increment(ref _droppedPackets);
            if (bytes > 0)
                Interlocked.Add(ref _droppedBytes, bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordFlush(long elapsedTicks)
        {
            Interlocked.Increment(ref _flushCalls);
            Interlocked.Add(ref _flushTicks, elapsedTicks);
        }

        public TransportStatsSnapshot Snapshot() => new TransportStatsSnapshot(
            outboundPackets: Volatile.Read(ref _outboundPackets),
            outboundBytes: Volatile.Read(ref _outboundBytes),
            inboundPackets: Volatile.Read(ref _inboundPackets),
            inboundBytes: Volatile.Read(ref _inboundBytes),
            droppedPackets: Volatile.Read(ref _droppedPackets),
            droppedBytes: Volatile.Read(ref _droppedBytes),
            pollCalls: Volatile.Read(ref _pollCalls),
            pollTicks: Volatile.Read(ref _pollTicks),
            flushCalls: Volatile.Read(ref _flushCalls),
            flushTicks: Volatile.Read(ref _flushTicks));

        public void Reset()
        {
            Interlocked.Exchange(ref _outboundPackets, 0);
            Interlocked.Exchange(ref _outboundBytes, 0);
            Interlocked.Exchange(ref _inboundPackets, 0);
            Interlocked.Exchange(ref _inboundBytes, 0);
            Interlocked.Exchange(ref _droppedPackets, 0);
            Interlocked.Exchange(ref _droppedBytes, 0);
            Interlocked.Exchange(ref _pollCalls, 0);
            Interlocked.Exchange(ref _pollTicks, 0);
            Interlocked.Exchange(ref _flushCalls, 0);
            Interlocked.Exchange(ref _flushTicks, 0);
        }
    }
}
