using System;
using System.Collections.Generic;
using System.Diagnostics;
using UltraSim.Network;
using UltraSim.Server.Engines.Chunking;

namespace UltraSim.Server.Gameplay
{
    public sealed class ObserverChunkReplayIndex : IDisposable
    {
        public readonly struct Snapshot
        {
            public Snapshot(
                long publishCalls,
                long publishElapsedTicksTotal,
                long publishElapsedTicksMax,
                long publishedDeltas,
                long publishedBytes,
                int activeChunkWindows,
                int activeDeltas,
                int activeBytes,
                int maxWindowDeltas,
                int maxWindowBytes)
            {
                PublishCalls = publishCalls;
                PublishElapsedTicksTotal = publishElapsedTicksTotal;
                PublishElapsedTicksMax = publishElapsedTicksMax;
                PublishedDeltas = publishedDeltas;
                PublishedBytes = publishedBytes;
                ActiveChunkWindows = activeChunkWindows;
                ActiveDeltas = activeDeltas;
                ActiveBytes = activeBytes;
                MaxWindowDeltas = maxWindowDeltas;
                MaxWindowBytes = maxWindowBytes;
            }

            public long PublishCalls { get; }
            public long PublishElapsedTicksTotal { get; }
            public long PublishElapsedTicksMax { get; }
            public long PublishedDeltas { get; }
            public long PublishedBytes { get; }
            public int ActiveChunkWindows { get; }
            public int ActiveDeltas { get; }
            public int ActiveBytes { get; }
            public int MaxWindowDeltas { get; }
            public int MaxWindowBytes { get; }
        }

        private readonly Dictionary<ChunkCoord, ReplicationReplayWindow> _windowByChunk =
            new Dictionary<ChunkCoord, ReplicationReplayWindow>(128);
        private readonly int _maxReplayTicks;
        private readonly int _initialBucketBytes;
        private long _publishCalls;
        private long _publishElapsedTicksTotal;
        private long _publishElapsedTicksMax;
        private long _publishedDeltas;
        private long _publishedBytes;

        public ObserverChunkReplayIndex(int maxReplayTicks, int initialBucketBytes)
        {
            _maxReplayTicks = Math.Max(1, maxReplayTicks);
            _initialBucketBytes = Math.Max(1, initialBucketBytes);
        }

        public Snapshot GetSnapshot()
        {
            int activeChunkWindows = 0;
            int activeDeltas = 0;
            int activeBytes = 0;
            int maxWindowDeltas = 0;
            int maxWindowBytes = 0;

            foreach (ReplicationReplayWindow window in _windowByChunk.Values)
            {
                ReplicationReplayWindow.ReplayWindowSnapshot snapshot = window.Snapshot();
                if (snapshot.TotalDeltaCount <= 0)
                    continue;

                activeChunkWindows++;
                activeDeltas += snapshot.TotalDeltaCount;
                activeBytes += snapshot.TotalUsedBytes;
                if (snapshot.TotalDeltaCount > maxWindowDeltas)
                    maxWindowDeltas = snapshot.TotalDeltaCount;
                if (snapshot.TotalUsedBytes > maxWindowBytes)
                    maxWindowBytes = snapshot.TotalUsedBytes;
            }

            return new Snapshot(
                _publishCalls,
                _publishElapsedTicksTotal,
                _publishElapsedTicksMax,
                _publishedDeltas,
                _publishedBytes,
                activeChunkWindows,
                activeDeltas,
                activeBytes,
                maxWindowDeltas,
                maxWindowBytes);
        }

        public void VisitRangeForChunks<TState>(
            HashSet<ChunkCoord> chunks,
            long minTickInclusive,
            long maxTickInclusive,
            ref TState state,
            ReplicationReplayWindow.RangeVisitor<TState> visitor)
        {
            ArgumentNullException.ThrowIfNull(chunks);
            ArgumentNullException.ThrowIfNull(visitor);

            foreach (ChunkCoord chunk in chunks)
            {
                if (!_windowByChunk.TryGetValue(chunk, out ReplicationReplayWindow? window))
                    continue;

                window.VisitRange(minTickInclusive, maxTickInclusive, ref state, visitor);
            }
        }

        public void Publish(
            in ChunkCoord chunk,
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlySpan<byte> payload)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            try
            {
                if (!_windowByChunk.TryGetValue(chunk, out ReplicationReplayWindow? window))
                {
                    window = new ReplicationReplayWindow(_maxReplayTicks, _initialBucketBytes);
                    _windowByChunk.Add(chunk, window);
                }

                window.Add(tick, entityId, streamId, lane, coalesceKey, payload);
                _publishedDeltas++;
                _publishedBytes += payload.Length;
            }
            finally
            {
                RecordElapsed(startTimestamp);
            }
        }

        public void Prune(long currentTick)
        {
            if (_windowByChunk.Count <= 0)
                return;

            ChunkCoord[]? emptyChunks = null;
            int emptyCount = 0;
            foreach ((ChunkCoord chunk, ReplicationReplayWindow window) in _windowByChunk)
            {
                window.Prune(currentTick);
                if (window.Snapshot().TotalDeltaCount > 0)
                    continue;

                emptyChunks ??= new ChunkCoord[_windowByChunk.Count];
                emptyChunks[emptyCount++] = chunk;
            }

            if (emptyCount <= 0 || emptyChunks == null)
                return;

            for (int i = 0; i < emptyCount; i++)
            {
                ChunkCoord chunk = emptyChunks[i];
                if (_windowByChunk.Remove(chunk, out ReplicationReplayWindow? window))
                    window.Dispose();
            }
        }

        public void Reset()
        {
            foreach (ReplicationReplayWindow window in _windowByChunk.Values)
                window.Dispose();

            _windowByChunk.Clear();
            _publishCalls = 0;
            _publishElapsedTicksTotal = 0;
            _publishElapsedTicksMax = 0;
            _publishedDeltas = 0;
            _publishedBytes = 0;
        }

        public void Dispose() => Reset();

        private void RecordElapsed(long startTimestamp)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            _publishCalls++;
            _publishElapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > _publishElapsedTicksMax)
                _publishElapsedTicksMax = elapsedTicks;
        }
    }
}
