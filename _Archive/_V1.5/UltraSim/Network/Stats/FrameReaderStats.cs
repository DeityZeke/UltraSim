using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class FrameReaderStats
    {
        private long _appendedBytes;
        private long _consumedFrames;
        private long _consumedBytes;
        private long _invalidFrames;
        private long _droppedBytes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordAppend(int bytes) => Interlocked.Add(ref _appendedBytes, bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordConsume(int payloadBytes)
        {
            Interlocked.Increment(ref _consumedFrames);
            Interlocked.Add(ref _consumedBytes, payloadBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordInvalidFrame(int droppedBytes)
        {
            Interlocked.Increment(ref _invalidFrames);
            if (droppedBytes > 0)
                Interlocked.Add(ref _droppedBytes, droppedBytes);
        }

        public FrameReaderStatsSnapshot Snapshot() => new FrameReaderStatsSnapshot(
            appendedBytes: Volatile.Read(ref _appendedBytes),
            consumedFrames: Volatile.Read(ref _consumedFrames),
            consumedBytes: Volatile.Read(ref _consumedBytes),
            invalidFrames: Volatile.Read(ref _invalidFrames),
            droppedBytes: Volatile.Read(ref _droppedBytes));

        public void Reset()
        {
            Interlocked.Exchange(ref _appendedBytes, 0);
            Interlocked.Exchange(ref _consumedFrames, 0);
            Interlocked.Exchange(ref _consumedBytes, 0);
            Interlocked.Exchange(ref _invalidFrames, 0);
            Interlocked.Exchange(ref _droppedBytes, 0);
        }
    }
}
