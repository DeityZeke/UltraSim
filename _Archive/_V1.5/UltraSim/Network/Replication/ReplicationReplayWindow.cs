using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class ReplicationReplayWindow : IDisposable
    {
        public readonly struct ReplayWindowSnapshot
        {
            public ReplayWindowSnapshot(
                int occupiedBucketCount,
                int totalCriticalCount,
                int totalStateCount,
                int totalCosmeticCount,
                int totalUsedBytes,
                int maxBucketDeltaCount,
                int maxBucketUsedBytes)
            {
                OccupiedBucketCount = occupiedBucketCount;
                TotalCriticalCount = totalCriticalCount;
                TotalStateCount = totalStateCount;
                TotalCosmeticCount = totalCosmeticCount;
                TotalUsedBytes = totalUsedBytes;
                MaxBucketDeltaCount = maxBucketDeltaCount;
                MaxBucketUsedBytes = maxBucketUsedBytes;
            }

            public int OccupiedBucketCount { get; }
            public int TotalCriticalCount { get; }
            public int TotalStateCount { get; }
            public int TotalCosmeticCount { get; }
            public int TotalUsedBytes { get; }
            public int MaxBucketDeltaCount { get; }
            public int MaxBucketUsedBytes { get; }
            public int TotalDeltaCount => TotalCriticalCount + TotalStateCount + TotalCosmeticCount;
        }

        public delegate void RangeVisitor<TState>(ref TState state, in EncodedReplicationDelta delta);
        public delegate bool RangeVisitorWhile<TState>(ref TState state, in EncodedReplicationDelta delta);
        internal delegate void BucketRangeVisitor<TState>(
            ref TState state,
            long tick,
            byte[] buffer,
            ReadOnlySpan<DeltaEntry> deltas);
        internal delegate bool BucketRangeVisitorWhile<TState>(
            ref TState state,
            long tick,
            byte[] buffer,
            ReadOnlySpan<DeltaEntry> deltas);

        internal struct DeltaEntry
        {
            public NetworkEntityId EntityId;
            public ushort StreamId;
            public ReplicationLane Lane;
            public ulong CoalesceKey;
            public ulong SentKeyHash;
            public int Start;
            public int Length;
        }

        private sealed class TickBucket
        {
            public long Tick = long.MinValue;
            public byte[] Buffer = Array.Empty<byte>();
            public bool BufferFromPool;
            public int UsedBytes;
            public DeltaEntry[] Deltas = Array.Empty<DeltaEntry>();
            public bool DeltasFromPool;
            public int DeltaCount;
            public int CriticalCount;
            public int StateCount;
            public int CosmeticCount;
        }

        private readonly TickBucket[] _buckets;
        private int _occupiedBucketCount;
        private readonly int _maxReplayTicks;
        private readonly long _bucketMask;
        private readonly int _initialBucketBytes;
        private TickBucket? _cachedBucket;
        private long _cachedTick = long.MinValue;
        private long _maxTickSeen;
        private bool _hasMaxTickSeen;
        private int _totalCriticalCount;
        private int _totalStateCount;
        private int _totalCosmeticCount;

        public ReplicationReplayWindow(int maxReplayTicks, int initialBucketBytes = 4096)
        {
            if (maxReplayTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxReplayTicks));
            if (initialBucketBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialBucketBytes));

            // Round up to the next power of two so GetBucketIndex can use a bitmask instead of
            // a modulo division. The larger window is conservative (keeps more history).
            maxReplayTicks = NextPowerOfTwo(maxReplayTicks);
            _maxReplayTicks = maxReplayTicks;
            _bucketMask = (long)(maxReplayTicks - 1);
            _initialBucketBytes = initialBucketBytes;
            _buckets = new TickBucket[maxReplayTicks];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new TickBucket();
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;
            // Bit-twiddling: round up to next PoT.
            uint v = (uint)(value - 1);
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return (int)(v + 1);
        }

        public int MaxReplayTicks => _maxReplayTicks;

        public ReplayWindowSnapshot Snapshot()
        {
            int totalUsedBytes = 0;
            int maxBucketDeltaCount = 0;
            int maxBucketUsedBytes = 0;

            for (int i = 0; i < _buckets.Length; i++)
            {
                TickBucket bucket = _buckets[i];
                if (bucket.Tick == long.MinValue)
                    continue;

                totalUsedBytes += bucket.UsedBytes;
                if (bucket.DeltaCount > maxBucketDeltaCount)
                    maxBucketDeltaCount = bucket.DeltaCount;
                if (bucket.UsedBytes > maxBucketUsedBytes)
                    maxBucketUsedBytes = bucket.UsedBytes;
            }

            return new ReplayWindowSnapshot(
                _occupiedBucketCount,
                _totalCriticalCount,
                _totalStateCount,
                _totalCosmeticCount,
                totalUsedBytes,
                maxBucketDeltaCount,
                maxBucketUsedBytes);
        }

        public void Add(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlySpan<byte> payload)
        {
            if (_hasMaxTickSeen)
            {
                long oldestTickToKeep = ComputeOldestTickToKeep(_maxTickSeen, _maxReplayTicks);
                if (tick < oldestTickToKeep)
                    return;

                if (tick > _maxTickSeen)
                {
                    _maxTickSeen = tick;
                }
            }
            else
            {
                _maxTickSeen = tick;
                _hasMaxTickSeen = true;
            }

            TickBucket bucket;
            if (tick == _cachedTick && _cachedBucket != null)
            {
                bucket = _cachedBucket;
            }
            else
            {
                int index = GetBucketIndex(tick);
                bucket = _buckets[index];

                if (bucket.Tick != tick)
                {
                    bool hadOccupant = bucket.Tick != long.MinValue;
                    if (hadOccupant)
                        SubtractBucketCounts(bucket);
                    ResetBucket(bucket);
                    bucket.Tick = tick;
                    if (!hadOccupant)
                        _occupiedBucketCount++;
                    EnsureBucketCapacity(bucket, Math.Max(_initialBucketBytes, payload.Length));
                }

                _cachedBucket = bucket;
                _cachedTick = tick;
            }

            int start = bucket.UsedBytes;
            EnsureBucketCapacity(bucket, start + payload.Length);
            payload.CopyTo(bucket.Buffer.AsSpan(start, payload.Length));
            bucket.UsedBytes += payload.Length;

            EnsureDeltaCapacity(bucket, bucket.DeltaCount + 1);
            bucket.Deltas[bucket.DeltaCount++] = new DeltaEntry
            {
                EntityId = entityId,
                StreamId = streamId,
                Lane = lane,
                CoalesceKey = coalesceKey,
                SentKeyHash = ReplicationDeltaSentKey.Compute(
                    tick,
                    entityId,
                    streamId,
                    lane,
                    coalesceKey,
                    payload),
                Start = start,
                Length = payload.Length,
            };

            switch (lane)
            {
                case ReplicationLane.Critical:
                    bucket.CriticalCount++;
                    _totalCriticalCount++;
                    break;
                case ReplicationLane.State:
                    bucket.StateCount++;
                    _totalStateCount++;
                    break;
                default:
                    bucket.CosmeticCount++;
                    _totalCosmeticCount++;
                    break;
            }
        }

        public int Prune(long currentTick)
        {
            long minTickToKeep = currentTick - _maxReplayTicks + 1;
            int removed = 0;

            for (int i = 0; i < _buckets.Length; i++)
            {
                TickBucket bucket = _buckets[i];
                long tick = bucket.Tick;
                if (tick == long.MinValue || tick >= minTickToKeep)
                    continue;

                SubtractBucketCounts(bucket);
                ResetBucket(bucket);
                if (ReferenceEquals(bucket, _cachedBucket))
                {
                    _cachedBucket = null;
                    _cachedTick = long.MinValue;
                }
                removed++;
            }

            if (removed > 0)
            {
                _occupiedBucketCount -= removed;
                Debug.Assert(_occupiedBucketCount >= 0, "Occupied bucket count underflowed during prune.");
                if (_occupiedBucketCount < 0)
                    _occupiedBucketCount = 0;
            }

            return removed;
        }

        public void CopyRange(long minTickInclusive, long maxTickInclusive, List<EncodedReplicationDelta> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;
            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick)
                {
                    for (int i = 0; i < bucket.DeltaCount; i++)
                    {
                        DeltaEntry entry = bucket.Deltas[i];
                        destination.Add(new EncodedReplicationDelta(
                            tick: tick,
                            entityId: entry.EntityId,
                            streamId: entry.StreamId,
                            lane: entry.Lane,
                            coalesceKey: entry.CoalesceKey,
                            sentKeyHash: entry.SentKeyHash,
                            payload: new ReadOnlyMemory<byte>(bucket.Buffer, entry.Start, entry.Length)));
                    }
                }

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        public void VisitRange<TState>(
            long minTickInclusive,
            long maxTickInclusive,
            ref TState state,
            RangeVisitor<TState> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;

            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick)
                {
                    for (int i = 0; i < bucket.DeltaCount; i++)
                    {
                        DeltaEntry entry = bucket.Deltas[i];
                        var delta = new EncodedReplicationDelta(
                            tick: tick,
                            entityId: entry.EntityId,
                            streamId: entry.StreamId,
                            lane: entry.Lane,
                            coalesceKey: entry.CoalesceKey,
                            sentKeyHash: entry.SentKeyHash,
                            payload: new ReadOnlyMemory<byte>(bucket.Buffer, entry.Start, entry.Length));
                        visitor(ref state, in delta);
                    }
                }

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        public void VisitRangeWhile<TState>(
            long minTickInclusive,
            long maxTickInclusive,
            ref TState state,
            RangeVisitorWhile<TState> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;

            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick)
                {
                    for (int i = 0; i < bucket.DeltaCount; i++)
                    {
                        DeltaEntry entry = bucket.Deltas[i];
                        var delta = new EncodedReplicationDelta(
                            tick: tick,
                            entityId: entry.EntityId,
                            streamId: entry.StreamId,
                            lane: entry.Lane,
                            coalesceKey: entry.CoalesceKey,
                            sentKeyHash: entry.SentKeyHash,
                            payload: new ReadOnlyMemory<byte>(bucket.Buffer, entry.Start, entry.Length));
                        if (!visitor(ref state, in delta))
                            return;
                    }
                }

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        public void CountLanesInRange(
            long minTickInclusive,
            long maxTickInclusive,
            out int criticalCount,
            out int stateCount,
            out int cosmeticCount)
        {
            criticalCount = 0;
            stateCount = 0;
            cosmeticCount = 0;

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;
            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick)
                {
                    criticalCount += bucket.CriticalCount;
                    stateCount += bucket.StateCount;
                    cosmeticCount += bucket.CosmeticCount;
                }

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        internal void VisitRangeBuckets<TState>(
            long minTickInclusive,
            long maxTickInclusive,
            ref TState state,
            BucketRangeVisitor<TState> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;

            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick)
                    visitor(ref state, tick, bucket.Buffer, bucket.Deltas.AsSpan(0, bucket.DeltaCount));

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        internal void VisitRangeBucketsWhile<TState>(
            long minTickInclusive,
            long maxTickInclusive,
            ref TState state,
            BucketRangeVisitorWhile<TState> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);

            if (minTickInclusive > maxTickInclusive)
                return;
            if (_occupiedBucketCount == 0)
                return;

            long lowerBound = maxTickInclusive - _maxReplayTicks + 1;
            if (minTickInclusive < lowerBound)
                minTickInclusive = lowerBound;

            long tick = minTickInclusive;
            while (true)
            {
                TickBucket bucket = _buckets[GetBucketIndex(tick)];
                if (bucket.Tick == tick && !visitor(ref state, tick, bucket.Buffer, bucket.Deltas.AsSpan(0, bucket.DeltaCount)))
                    return;

                if (tick == maxTickInclusive)
                    break;
                tick++;
            }
        }

        private static long ComputeOldestTickToKeep(long latestTick, int maxReplayTicks)
        {
            long window = maxReplayTicks - 1L;
            if (latestTick <= long.MinValue + window)
                return long.MinValue;

            return latestTick - window;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBucketIndex(long tick)
        {
            // _bucketMask = _maxReplayTicks - 1, where _maxReplayTicks is a power of two.
            // Bitwise AND correctly maps any long (positive or negative) into [0, _maxReplayTicks).
            return (int)(tick & _bucketMask);
        }

        private static void EnsureBucketCapacity(TickBucket bucket, int required)
        {
            if (bucket.Buffer.Length >= required)
                return;

            int newSize = bucket.Buffer.Length == 0 ? 256 : bucket.Buffer.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            if (bucket.UsedBytes > 0)
                bucket.Buffer.AsSpan(0, bucket.UsedBytes).CopyTo(newBuffer);

            if (bucket.BufferFromPool)
                ArrayPool<byte>.Shared.Return(bucket.Buffer);

            bucket.Buffer = newBuffer;
            bucket.BufferFromPool = true;
        }

        private static void EnsureDeltaCapacity(TickBucket bucket, int required)
        {
            if (bucket.Deltas.Length >= required)
                return;

            int newSize = bucket.Deltas.Length == 0 ? 32 : bucket.Deltas.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            DeltaEntry[] newDeltas = ArrayPool<DeltaEntry>.Shared.Rent(newSize);
            if (bucket.DeltaCount > 0)
                bucket.Deltas.AsSpan(0, bucket.DeltaCount).CopyTo(newDeltas);

            if (bucket.DeltasFromPool)
                ArrayPool<DeltaEntry>.Shared.Return(bucket.Deltas);

            bucket.Deltas = newDeltas;
            bucket.DeltasFromPool = true;
        }

        private static void ResetBucket(TickBucket bucket)
        {
            bucket.Tick = long.MinValue;
            bucket.UsedBytes = 0;
            bucket.DeltaCount = 0;
            bucket.CriticalCount = 0;
            bucket.StateCount = 0;
            bucket.CosmeticCount = 0;
        }

        private void SubtractBucketCounts(TickBucket bucket)
        {
            _totalCriticalCount -= bucket.CriticalCount;
            _totalStateCount -= bucket.StateCount;
            _totalCosmeticCount -= bucket.CosmeticCount;
            Debug.Assert(_totalCriticalCount >= 0, "Critical lane count underflowed.");
            Debug.Assert(_totalStateCount >= 0, "State lane count underflowed.");
            Debug.Assert(_totalCosmeticCount >= 0, "Cosmetic lane count underflowed.");
            if (_totalCriticalCount < 0)
                _totalCriticalCount = 0;
            if (_totalStateCount < 0)
                _totalStateCount = 0;
            if (_totalCosmeticCount < 0)
                _totalCosmeticCount = 0;
        }

        public void Dispose()
        {
            for (int i = 0; i < _buckets.Length; i++)
            {
                TickBucket bucket = _buckets[i];
                if (bucket.BufferFromPool)
                {
                    ArrayPool<byte>.Shared.Return(bucket.Buffer);
                    bucket.BufferFromPool = false;
                }

                if (bucket.DeltasFromPool)
                {
                    ArrayPool<DeltaEntry>.Shared.Return(bucket.Deltas);
                    bucket.DeltasFromPool = false;
                }

                bucket.Buffer = Array.Empty<byte>();
                bucket.Deltas = Array.Empty<DeltaEntry>();
                bucket.Tick = long.MinValue;
                bucket.UsedBytes = 0;
                bucket.DeltaCount = 0;
                bucket.CriticalCount = 0;
                bucket.StateCount = 0;
                bucket.CosmeticCount = 0;
            }

            _cachedBucket = null;
            _cachedTick = long.MinValue;
            _occupiedBucketCount = 0;
            _maxTickSeen = 0;
            _hasMaxTickSeen = false;
            _totalCriticalCount = 0;
            _totalStateCount = 0;
            _totalCosmeticCount = 0;
        }
    }
}
