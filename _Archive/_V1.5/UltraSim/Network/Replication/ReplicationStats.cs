using System;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class ReplicationStats
    {
        private readonly long[] _sentByLane = new long[Enum.GetValues<ReplicationLane>().Length];
        private readonly long[] _deferredByLane = new long[Enum.GetValues<ReplicationLane>().Length];
        private readonly long[] _droppedByLane = new long[Enum.GetValues<ReplicationLane>().Length];
        private readonly long[] _coalescedByLane = new long[Enum.GetValues<ReplicationLane>().Length];
        private readonly bool _threadSafe;

        private long _dirtyDrained;
        private long _deltasEncoded;
        private long _encodedBytes;
        private long _interestedSelected;
        private long _packetsSent;
        private long _bytesSent;
        private long _replayTicksPruned;
        private long _interestFilterCalls;
        private long _interestCacheLookups;
        private long _interestCacheHits;
        private long _interestCacheMisses;
        private long _fanoutReuseLookups;
        private long _fanoutReuseHits;
        private long _fanoutReuseMisses;
        private long _fanoutReuseBytesSaved;
        private long _sendAttempts;

        public ReplicationStats(bool threadSafe = true)
        {
            _threadSafe = threadSafe;
        }

        public void RecordDirtyDrained(int count)
        {
            if (count <= 0)
                return;

            if (_threadSafe)
                Interlocked.Add(ref _dirtyDrained, count);
            else
                _dirtyDrained += count;
        }

        public void RecordEncoded(int bytes)
        {
            RecordEncodedBatch(1, bytes);
        }

        public void RecordEncodedBatch(int deltaCount, int totalBytes)
        {
            if (deltaCount <= 0)
                return;

            if (_threadSafe)
            {
                Interlocked.Add(ref _deltasEncoded, deltaCount);
                Interlocked.Add(ref _encodedBytes, totalBytes);
            }
            else
            {
                _deltasEncoded += deltaCount;
                _encodedBytes += totalBytes;
            }
        }

        public void RecordInterestedSelected()
        {
            RecordInterestedSelected(1);
        }

        public void RecordInterestedSelected(int count)
        {
            if (count <= 0)
                return;

            if (_threadSafe)
                Interlocked.Add(ref _interestedSelected, count);
            else
                _interestedSelected += count;
        }

        public void RecordSent(ReplicationLane lane, int bytes)
        {
            if (!TryGetLaneIndex(lane, out int laneIndex))
                return;

            if (_threadSafe)
            {
                Interlocked.Increment(ref _packetsSent);
                Interlocked.Add(ref _bytesSent, bytes);
                Interlocked.Increment(ref _sentByLane[laneIndex]);
            }
            else
            {
                _packetsSent++;
                _bytesSent += bytes;
                _sentByLane[laneIndex]++;
            }
        }

        public void RecordDeferred(ReplicationLane lane)
        {
            if (!TryGetLaneIndex(lane, out int laneIndex))
                return;

            if (_threadSafe)
                Interlocked.Increment(ref _deferredByLane[laneIndex]);
            else
                _deferredByLane[laneIndex]++;
        }

        public void RecordDropped(ReplicationLane lane)
        {
            if (!TryGetLaneIndex(lane, out int laneIndex))
                return;

            if (_threadSafe)
                Interlocked.Increment(ref _droppedByLane[laneIndex]);
            else
                _droppedByLane[laneIndex]++;
        }

        public void RecordCoalesced(ReplicationLane lane)
        {
            if (!TryGetLaneIndex(lane, out int laneIndex))
                return;

            if (_threadSafe)
                Interlocked.Increment(ref _coalescedByLane[laneIndex]);
            else
                _coalescedByLane[laneIndex]++;
        }

        public void RecordReplayTicksPruned(int count)
        {
            if (count <= 0)
                return;

            if (_threadSafe)
                Interlocked.Add(ref _replayTicksPruned, count);
            else
                _replayTicksPruned += count;
        }

        public void RecordInterestFilterCall()
        {
            if (_threadSafe)
                Interlocked.Increment(ref _interestFilterCalls);
            else
                _interestFilterCalls++;
        }

        public void RecordInterestCacheHit()
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _interestCacheLookups);
                Interlocked.Increment(ref _interestCacheHits);
            }
            else
            {
                _interestCacheLookups++;
                _interestCacheHits++;
            }
        }

        public void RecordInterestCacheMiss()
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _interestCacheLookups);
                Interlocked.Increment(ref _interestCacheMisses);
            }
            else
            {
                _interestCacheLookups++;
                _interestCacheMisses++;
            }
        }

        public void RecordFanoutReuseMiss()
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _fanoutReuseLookups);
                Interlocked.Increment(ref _fanoutReuseMisses);
            }
            else
            {
                _fanoutReuseLookups++;
                _fanoutReuseMisses++;
            }
        }

        public void RecordFanoutReuseHit(long bytesSaved)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _fanoutReuseLookups);
                Interlocked.Increment(ref _fanoutReuseHits);
                if (bytesSaved > 0)
                    Interlocked.Add(ref _fanoutReuseBytesSaved, bytesSaved);
            }
            else
            {
                _fanoutReuseLookups++;
                _fanoutReuseHits++;
                if (bytesSaved > 0)
                    _fanoutReuseBytesSaved += bytesSaved;
            }
        }

        public void RecordSendAttempt()
        {
            if (_threadSafe)
                Interlocked.Increment(ref _sendAttempts);
            else
                _sendAttempts++;
        }

        public ReplicationStatsSnapshot Snapshot()
        {
            if (_threadSafe)
            {
                return new ReplicationStatsSnapshot(
                    dirtyDrained: Interlocked.Read(ref _dirtyDrained),
                    deltasEncoded: Interlocked.Read(ref _deltasEncoded),
                    encodedBytes: Interlocked.Read(ref _encodedBytes),
                    interestedSelected: Interlocked.Read(ref _interestedSelected),
                    packetsSent: Interlocked.Read(ref _packetsSent),
                    bytesSent: Interlocked.Read(ref _bytesSent),
                    replayTicksPruned: Interlocked.Read(ref _replayTicksPruned),
                    interestFilterCalls: Interlocked.Read(ref _interestFilterCalls),
                    interestCacheLookups: Interlocked.Read(ref _interestCacheLookups),
                    interestCacheHits: Interlocked.Read(ref _interestCacheHits),
                    interestCacheMisses: Interlocked.Read(ref _interestCacheMisses),
                    fanoutReuseLookups: Interlocked.Read(ref _fanoutReuseLookups),
                    fanoutReuseHits: Interlocked.Read(ref _fanoutReuseHits),
                    fanoutReuseMisses: Interlocked.Read(ref _fanoutReuseMisses),
                    fanoutReuseBytesSaved: Interlocked.Read(ref _fanoutReuseBytesSaved),
                    sendAttempts: Interlocked.Read(ref _sendAttempts),
                    sentByLane: CopyLaneArrayThreadSafe(_sentByLane),
                    deferredByLane: CopyLaneArrayThreadSafe(_deferredByLane),
                    droppedByLane: CopyLaneArrayThreadSafe(_droppedByLane),
                    coalescedByLane: CopyLaneArrayThreadSafe(_coalescedByLane));
            }

            Thread.MemoryBarrier();
            return new ReplicationStatsSnapshot(
                dirtyDrained: _dirtyDrained,
                deltasEncoded: _deltasEncoded,
                encodedBytes: _encodedBytes,
                interestedSelected: _interestedSelected,
                packetsSent: _packetsSent,
                bytesSent: _bytesSent,
                replayTicksPruned: _replayTicksPruned,
                interestFilterCalls: _interestFilterCalls,
                interestCacheLookups: _interestCacheLookups,
                interestCacheHits: _interestCacheHits,
                interestCacheMisses: _interestCacheMisses,
                fanoutReuseLookups: _fanoutReuseLookups,
                fanoutReuseHits: _fanoutReuseHits,
                fanoutReuseMisses: _fanoutReuseMisses,
                fanoutReuseBytesSaved: _fanoutReuseBytesSaved,
                sendAttempts: _sendAttempts,
                sentByLane: CopyLaneArrayUnsafe(_sentByLane),
                deferredByLane: CopyLaneArrayUnsafe(_deferredByLane),
                droppedByLane: CopyLaneArrayUnsafe(_droppedByLane),
                coalescedByLane: CopyLaneArrayUnsafe(_coalescedByLane));
        }

        public void Reset()
        {
            if (_threadSafe)
            {
                Interlocked.Exchange(ref _dirtyDrained, 0);
                Interlocked.Exchange(ref _deltasEncoded, 0);
                Interlocked.Exchange(ref _encodedBytes, 0);
                Interlocked.Exchange(ref _interestedSelected, 0);
                Interlocked.Exchange(ref _packetsSent, 0);
                Interlocked.Exchange(ref _bytesSent, 0);
                Interlocked.Exchange(ref _replayTicksPruned, 0);
                Interlocked.Exchange(ref _interestFilterCalls, 0);
                Interlocked.Exchange(ref _interestCacheLookups, 0);
                Interlocked.Exchange(ref _interestCacheHits, 0);
                Interlocked.Exchange(ref _interestCacheMisses, 0);
                Interlocked.Exchange(ref _fanoutReuseLookups, 0);
                Interlocked.Exchange(ref _fanoutReuseHits, 0);
                Interlocked.Exchange(ref _fanoutReuseMisses, 0);
                Interlocked.Exchange(ref _fanoutReuseBytesSaved, 0);
                Interlocked.Exchange(ref _sendAttempts, 0);

                ClearLaneArrayThreadSafe(_sentByLane);
                ClearLaneArrayThreadSafe(_deferredByLane);
                ClearLaneArrayThreadSafe(_droppedByLane);
                ClearLaneArrayThreadSafe(_coalescedByLane);
                return;
            }

            _dirtyDrained = 0;
            _deltasEncoded = 0;
            _encodedBytes = 0;
            _interestedSelected = 0;
            _packetsSent = 0;
            _bytesSent = 0;
            _replayTicksPruned = 0;
            _interestFilterCalls = 0;
            _interestCacheLookups = 0;
            _interestCacheHits = 0;
            _interestCacheMisses = 0;
            _fanoutReuseLookups = 0;
            _fanoutReuseHits = 0;
            _fanoutReuseMisses = 0;
            _fanoutReuseBytesSaved = 0;
            _sendAttempts = 0;
            Array.Clear(_sentByLane);
            Array.Clear(_deferredByLane);
            Array.Clear(_droppedByLane);
            Array.Clear(_coalescedByLane);
        }

        private static long[] CopyLaneArrayThreadSafe(long[] source)
        {
            var copy = new long[source.Length];
            for (int i = 0; i < source.Length; i++)
                copy[i] = Interlocked.Read(ref source[i]);

            return copy;
        }

        private static long[] CopyLaneArrayUnsafe(long[] source)
        {
            var copy = new long[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static void ClearLaneArrayThreadSafe(long[] source)
        {
            for (int i = 0; i < source.Length; i++)
                Interlocked.Exchange(ref source[i], 0);
        }

        private bool TryGetLaneIndex(ReplicationLane lane, out int laneIndex)
        {
            laneIndex = (int)lane;
            return (uint)laneIndex < (uint)_sentByLane.Length;
        }
    }
}
