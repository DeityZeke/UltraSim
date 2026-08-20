using System;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public sealed class ServerNetworkReplicationBridge
    {
        public delegate void SupplementalBuildCallback(
            long tick,
            int workerIndex,
            int workerCount,
            NetworkReplicationPipeline.BuildSession session);

        private sealed class ParallelBuildWorkerContext
        {
            public ParallelBuildWorkerContext(NetworkReplicationPipeline pipeline)
            {
                Pipeline = pipeline;
                Sink = AppendPacket;
            }

            public NetworkReplicationPipeline Pipeline { get; }
            public ReplicationPacket[] Packets = Array.Empty<ReplicationPacket>();
            public int PacketCount;
            public int SentPackets;
            public ReplicationPacketSink Sink { get; }

            public void Reset()
            {
                PacketCount = 0;
                SentPackets = 0;
            }

            private void AppendPacket(in ReplicationPacket packet)
            {
                if (PacketCount == Packets.Length)
                    GrowPacketBuffer();

                Packets[PacketCount++] = packet;
            }

            private void GrowPacketBuffer()
            {
                int newSize = Packets.Length == 0 ? 4096 : Packets.Length << 1;
                Array.Resize(ref Packets, newSize);
            }
        }

        public readonly struct BridgeTimingSnapshot
        {
            public BridgeTimingSnapshot(
                long publishCalls,
                long publishElapsedTicksTotal,
                long publishElapsedTicksMax,
                long buildCalls,
                long buildElapsedTicksTotal,
                long buildElapsedTicksMax,
                long buildAllocatedBytesTotal,
                long buildAllocatedBytesMax,
                long buildGc0Collections,
                long buildGc1Collections,
                long buildGc2Collections,
                int buildWorkerCount,
                long parallelBuildCalls,
                long parallelBuildElapsedTicksTotal,
                long parallelBuildElapsedTicksMax)
            {
                PublishCalls = publishCalls;
                PublishElapsedTicksTotal = publishElapsedTicksTotal;
                PublishElapsedTicksMax = publishElapsedTicksMax;
                BuildCalls = buildCalls;
                BuildElapsedTicksTotal = buildElapsedTicksTotal;
                BuildElapsedTicksMax = buildElapsedTicksMax;
                BuildAllocatedBytesTotal = buildAllocatedBytesTotal;
                BuildAllocatedBytesMax = buildAllocatedBytesMax;
                BuildGc0Collections = buildGc0Collections;
                BuildGc1Collections = buildGc1Collections;
                BuildGc2Collections = buildGc2Collections;
                BuildWorkerCount = buildWorkerCount;
                ParallelBuildCalls = parallelBuildCalls;
                ParallelBuildElapsedTicksTotal = parallelBuildElapsedTicksTotal;
                ParallelBuildElapsedTicksMax = parallelBuildElapsedTicksMax;
            }

            public long PublishCalls { get; }
            public long PublishElapsedTicksTotal { get; }
            public long PublishElapsedTicksMax { get; }
            public long BuildCalls { get; }
            public long BuildElapsedTicksTotal { get; }
            public long BuildElapsedTicksMax { get; }
            public long BuildAllocatedBytesTotal { get; }
            public long BuildAllocatedBytesMax { get; }
            public long BuildGc0Collections { get; }
            public long BuildGc1Collections { get; }
            public long BuildGc2Collections { get; }
            public int BuildWorkerCount { get; }
            public long ParallelBuildCalls { get; }
            public long ParallelBuildElapsedTicksTotal { get; }
            public long ParallelBuildElapsedTicksMax { get; }
        }

        private readonly World _world;
        private readonly INetworkTransport _transport;
        private readonly NetworkReplicationPipeline _pipeline;
        private readonly NetworkReplicationPipeline[] _buildPipelines;
        private readonly ParallelBuildWorkerContext[]? _parallelBuildWorkerContexts;
        private readonly ServerNetworkEntityMap _entityMap;
        private readonly ServerReplicationCandidateFactory _candidateFactory;
        private readonly ServerReplicationCandidateFactory? _secondaryCandidateFactory;
        private readonly ServerReplicationPacketWriter _packetWriter;
        private readonly ServerReplicationPacketBatchWriter? _packetBatchWriter;
        private readonly SupplementalBuildCallback? _supplementalBuild;
        private readonly ReplicationPacketSink _packetSink;
        private readonly ReplicationPacketBatchSink _packetBatchSink;
        private readonly int _buildBatchPacketLimit;

        private Entity[] _dirtyScratch = new Entity[1024];
        private ReplicationCandidate[] _candidateScratch = new ReplicationCandidate[1024];
        private ReplicationPacket[] _pendingBatchPackets = Array.Empty<ReplicationPacket>();
        private int _pendingBatchPacketCount;
        private long _publishCalls;
        private long _publishElapsedTicksTotal;
        private long _publishElapsedTicksMax;
        private long _buildCalls;
        private long _buildElapsedTicksTotal;
        private long _buildElapsedTicksMax;
        private long _buildAllocatedBytesTotal;
        private long _buildAllocatedBytesMax;
        private long _buildGc0Collections;
        private long _buildGc1Collections;
        private long _buildGc2Collections;
        private long _parallelBuildCalls;
        private long _parallelBuildElapsedTicksTotal;
        private long _parallelBuildElapsedTicksMax;

        public ServerNetworkReplicationBridge(
            World world,
            INetworkTransport transport,
            NetworkReplicationPipeline pipeline,
            NetworkReplicationPipeline[]? buildPipelines = null,
            ServerNetworkEntityMap? entityMap = null,
            ServerReplicationCandidateFactory? candidateFactory = null,
            ServerReplicationCandidateFactory? secondaryCandidateFactory = null,
            ServerReplicationPacketWriter? packetWriter = null,
            ServerReplicationPacketBatchWriter? packetBatchWriter = null,
            SupplementalBuildCallback? supplementalBuild = null,
            ushort defaultStreamId = 1,
            ReplicationLane defaultLane = ReplicationLane.State,
            int buildBatchPacketLimit = 256)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(transport);
            ArgumentNullException.ThrowIfNull(pipeline);

            _world = world;
            _transport = transport;
            _pipeline = pipeline;
            _buildPipelines = buildPipelines == null || buildPipelines.Length == 0
                ? new[] { pipeline }
                : buildPipelines;
            if (_buildPipelines.Length > 1)
            {
                _parallelBuildWorkerContexts = new ParallelBuildWorkerContext[_buildPipelines.Length];
                for (int i = 0; i < _buildPipelines.Length; i++)
                    _parallelBuildWorkerContexts[i] = new ParallelBuildWorkerContext(_buildPipelines[i]);
            }
            _entityMap = entityMap ?? new ServerNetworkEntityMap();
            _candidateFactory = candidateFactory ?? CreateDefaultCandidateFactory(defaultStreamId, defaultLane);
            _secondaryCandidateFactory = secondaryCandidateFactory;
            _packetWriter = packetWriter ?? DefaultPacketWriter;
            _packetBatchWriter = packetBatchWriter;
            _supplementalBuild = supplementalBuild;
            _packetSink = SendPacket;
            _packetBatchSink = SendPacketBatch;
            _buildBatchPacketLimit = buildBatchPacketLimit <= 1 ? 1 : buildBatchPacketLimit;
        }

        public World World => _world;

        public INetworkTransport Transport => _transport;

        public NetworkReplicationPipeline Pipeline => _pipeline;

        public ServerNetworkEntityMap EntityMap => _entityMap;

        public BridgeTimingSnapshot GetTimingSnapshot() =>
            new BridgeTimingSnapshot(
                _publishCalls,
                _publishElapsedTicksTotal,
                _publishElapsedTicksMax,
                _buildCalls,
                _buildElapsedTicksTotal,
                _buildElapsedTicksMax,
                _buildAllocatedBytesTotal,
                _buildAllocatedBytesMax,
                _buildGc0Collections,
                _buildGc1Collections,
                _buildGc2Collections,
                _buildPipelines.Length,
                _parallelBuildCalls,
                _parallelBuildElapsedTicksTotal,
                _parallelBuildElapsedTicksMax);

        public void TrackConnection(int connectionId, long lastAckedTick = 0, uint groupId = 0) =>
            GetBuildPipeline(connectionId).TrackConnection(connectionId, lastAckedTick, groupId);

        public bool UntrackConnection(int connectionId) =>
            GetBuildPipeline(connectionId).UntrackConnection(connectionId);

        public bool SetConnectionGroup(int connectionId, uint groupId) =>
            GetBuildPipeline(connectionId).SetConnectionGroup(connectionId, groupId);

        public bool SetLastAckedTick(int connectionId, long lastAckedTick) =>
            GetBuildPipeline(connectionId).SetLastAckedTick(connectionId, lastAckedTick);

        public bool TryGetCursor(int connectionId, out ConnectionReplicationCursor cursor) =>
            GetBuildPipeline(connectionId).TryGetCursor(connectionId, out cursor);

        public bool TryGetBudgetDebt(int connectionId, long currentTick, out ConnectionReplicationDebt debt) =>
            GetBuildPipeline(connectionId).TryGetBudgetDebt(connectionId, currentTick, out debt);

        public ReplicationStatsSnapshot GetReplicationStatsSnapshot()
        {
            if (_buildPipelines.Length == 1)
                return _pipeline.Stats.Snapshot();

            ReplicationStatsSnapshot total = _pipeline.Stats.Snapshot();
            for (int i = 0; i < _buildPipelines.Length; i++)
                total = Combine(total, _buildPipelines[i].Stats.Snapshot());
            return total;
        }

        public NetworkReplicationPipeline.BuildTimingSnapshot GetPipelineTimingSnapshot()
        {
            if (_buildPipelines.Length == 1)
                return _pipeline.GetBuildTimingSnapshot();

            NetworkReplicationPipeline.BuildTimingSnapshot aggregate = default;
            for (int i = 0; i < _buildPipelines.Length; i++)
                aggregate = Combine(aggregate, _buildPipelines[i].GetBuildTimingSnapshot());
            return aggregate;
        }

        public ReplicationReplayWindow.ReplayWindowSnapshot GetReplayWindowSnapshot() =>
            _pipeline.GetReplayWindowSnapshot();

        public int PublishDirtyCandidates(long tick)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            try
            {
            if (!_world.DirtyTrackingEnabled)
                return 0;

            int copied = CopyDirtySnapshotEnsuringCapacity();
            if (copied == 0)
                return 0;

            int drained = _world.DrainDirtyTo(_dirtyScratch);
            if (drained <= 0)
                return 0;

            int candidateCapacity = _secondaryCandidateFactory == null ? drained : (drained << 1);
            EnsureCandidateCapacity(candidateCapacity);
            int candidateCount = 0;
            for (int i = 0; i < drained; i++)
            {
                ref readonly Entity entity = ref _dirtyScratch[i];
                if (!_world.IsAlive(entity))
                {
                    _entityMap.Remove(entity);
                    continue;
                }

                if (_candidateFactory(_world, in entity, tick, _entityMap, out ReplicationCandidate candidate))
                    _candidateScratch[candidateCount++] = candidate;
                if (_secondaryCandidateFactory != null &&
                    _secondaryCandidateFactory(_world, in entity, tick, _entityMap, out ReplicationCandidate secondaryCandidate))
                {
                    _candidateScratch[candidateCount++] = secondaryCandidate;
                }
            }

            if (candidateCount == 0)
                return 0;

            _pipeline.PublishCandidates(_candidateScratch.AsSpan(0, candidateCount));
            return candidateCount;
            }
            finally
            {
                RecordElapsed(startTimestamp, ref _publishCalls, ref _publishElapsedTicksTotal, ref _publishElapsedTicksMax);
            }
        }

        public int BuildAndEnqueue(long tick)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gc0Before = GC.CollectionCount(0);
            int gc1Before = GC.CollectionCount(1);
            int gc2Before = GC.CollectionCount(2);
            try
            {
                if (_parallelBuildWorkerContexts != null)
                    return BuildAndEnqueueParallel(tick);

                NetworkReplicationPipeline.SupplementalBuildCallback? supplementalBuild = _supplementalBuild == null
                    ? null
                    : session => _supplementalBuild(tick, 0, 1, session);

                if (_buildBatchPacketLimit <= 1)
                    return _pipeline.BuildTick(tick, _packetSink, supplementalBuild: supplementalBuild);

                if (_packetBatchWriter == null)
                    return _pipeline.BuildTickBatched(
                        tick,
                        _buildBatchPacketLimit,
                        _packetBatchSink,
                        supplementalBuild: supplementalBuild);

                _pendingBatchPacketCount = 0;
                int sent = _pipeline.BuildTickBatched(
                    tick,
                    _buildBatchPacketLimit,
                    _packetBatchSink,
                    supplementalBuild: supplementalBuild);
                FlushPendingBatchPackets();
                return sent;
            }
            finally
            {
                RecordAllocatedBytes(allocatedBefore, ref _buildAllocatedBytesTotal, ref _buildAllocatedBytesMax);
                _buildGc0Collections += GC.CollectionCount(0) - gc0Before;
                _buildGc1Collections += GC.CollectionCount(1) - gc1Before;
                _buildGc2Collections += GC.CollectionCount(2) - gc2Before;
                RecordElapsed(startTimestamp, ref _buildCalls, ref _buildElapsedTicksTotal, ref _buildElapsedTicksMax);
            }
        }

        private int BuildAndEnqueueParallel(long tick)
        {
            Debug.Assert(_parallelBuildWorkerContexts != null);

            long parallelBuildStartTimestamp = Stopwatch.GetTimestamp();
            try
            {
                _pipeline.PruneReplayWindow(tick);

                ParallelBuildWorkerContext[] workerContexts = _parallelBuildWorkerContexts;
                for (int i = 0; i < workerContexts.Length; i++)
                    workerContexts[i].Reset();

                System.Threading.Tasks.Parallel.For(0, workerContexts.Length, workerIndex =>
                {
                    ParallelBuildWorkerContext worker = workerContexts[workerIndex];
                    NetworkReplicationPipeline.SupplementalBuildCallback? supplementalBuild = _supplementalBuild == null
                        ? null
                        : session => _supplementalBuild(tick, workerIndex, workerContexts.Length, session);
                    worker.SentPackets = worker.Pipeline.BuildTick(
                        tick,
                        worker.Sink,
                        pruneReplayWindow: false,
                        supplementalBuild: supplementalBuild);
                });

                int sent = 0;
                for (int i = 0; i < workerContexts.Length; i++)
                {
                    ParallelBuildWorkerContext worker = workerContexts[i];
                    sent += worker.SentPackets;
                    FlushParallelWorkerPackets(worker);
                }

                return sent;
            }
            finally
            {
                RecordElapsed(
                    parallelBuildStartTimestamp,
                    ref _parallelBuildCalls,
                    ref _parallelBuildElapsedTicksTotal,
                    ref _parallelBuildElapsedTicksMax);
            }
        }

        public ServerReplicationTickResult ReplicateTick(long tick, bool flushOutbound)
        {
            int published = PublishDirtyCandidates(tick);
            int sent = BuildAndEnqueue(tick);
            if (flushOutbound)
                _transport.FlushOutbound();

            return new ServerReplicationTickResult(published, sent);
        }

        private int CopyDirtySnapshotEnsuringCapacity()
        {
            while (true)
            {
                // DrainDirtyTo clears on truncation, so we must size with CopyDirtyTo first.
                int copied = _world.CopyDirtyTo(_dirtyScratch);
                if (copied < _dirtyScratch.Length)
                    return copied;

                int newSize = _dirtyScratch.Length << 1;
                if (newSize < 1024)
                    newSize = 1024;
                _dirtyScratch = new Entity[newSize];
            }
        }

        private void EnsureCandidateCapacity(int needed)
        {
            if (_candidateScratch.Length >= needed)
                return;

            int newSize = _candidateScratch.Length;
            if (newSize < 1024)
                newSize = 1024;
            while (newSize < needed)
                newSize <<= 1;

            _candidateScratch = new ReplicationCandidate[newSize];
        }

        private void SendPacket(in ReplicationPacket packet) =>
            _packetWriter(_transport, in packet);

        private void SendPacketBatch(ReplicationPacket[] packets, int count)
        {
            if (_packetBatchWriter != null)
            {
                AccumulatePacketBatch(packets, count);
                return;
            }

            for (int i = 0; i < count; i++)
                _packetWriter(_transport, in packets[i]);
        }

        private void AccumulatePacketBatch(ReplicationPacket[] packets, int count)
        {
            if (count <= 0)
                return;

            EnsurePendingBatchCapacity(_pendingBatchPacketCount + count);
            Array.Copy(packets, 0, _pendingBatchPackets, _pendingBatchPacketCount, count);
            _pendingBatchPacketCount += count;
        }

        private void FlushPendingBatchPackets()
        {
            if (_packetBatchWriter == null || _pendingBatchPacketCount <= 0)
                return;

            _packetBatchWriter(_transport, _pendingBatchPackets, _pendingBatchPacketCount);
            _pendingBatchPacketCount = 0;
        }

        private void FlushParallelWorkerPackets(ParallelBuildWorkerContext worker)
        {
            if (worker.PacketCount <= 0)
                return;

            if (_packetBatchWriter != null)
            {
                _packetBatchWriter(_transport, worker.Packets, worker.PacketCount);
                worker.PacketCount = 0;
                return;
            }

            for (int i = 0; i < worker.PacketCount; i++)
                _packetWriter(_transport, in worker.Packets[i]);

            worker.PacketCount = 0;
        }

        private void EnsurePendingBatchCapacity(int needed)
        {
            if (_pendingBatchPackets.Length >= needed)
                return;

            int newSize = _pendingBatchPackets.Length;
            if (newSize < 64)
                newSize = 64;
            while (newSize < needed)
                newSize <<= 1;

            _pendingBatchPackets = new ReplicationPacket[newSize];
        }

        private NetworkReplicationPipeline GetBuildPipeline(int connectionId)
        {
            if (_buildPipelines.Length == 1)
                return _buildPipelines[0];

            uint unsignedConnectionId = unchecked((uint)connectionId);
            int index = (int)(unsignedConnectionId % (uint)_buildPipelines.Length);
            return _buildPipelines[index];
        }

        private static ReplicationStatsSnapshot Combine(ReplicationStatsSnapshot left, ReplicationStatsSnapshot right)
        {
            int laneCount = Enum.GetValues<ReplicationLane>().Length;
            long[] sentByLane = new long[laneCount];
            long[] deferredByLane = new long[laneCount];
            long[] droppedByLane = new long[laneCount];
            long[] coalescedByLane = new long[laneCount];
            for (int i = 0; i < laneCount; i++)
            {
                ReplicationLane lane = (ReplicationLane)i;
                sentByLane[i] = left.GetSentByLane(lane) + right.GetSentByLane(lane);
                deferredByLane[i] = left.GetDeferredByLane(lane) + right.GetDeferredByLane(lane);
                droppedByLane[i] = left.GetDroppedByLane(lane) + right.GetDroppedByLane(lane);
                coalescedByLane[i] = left.GetCoalescedByLane(lane) + right.GetCoalescedByLane(lane);
            }

            return new ReplicationStatsSnapshot(
                dirtyDrained: left.DirtyDrained + right.DirtyDrained,
                deltasEncoded: left.DeltasEncoded + right.DeltasEncoded,
                encodedBytes: left.EncodedBytes + right.EncodedBytes,
                interestedSelected: left.InterestedSelected + right.InterestedSelected,
                packetsSent: left.PacketsSent + right.PacketsSent,
                bytesSent: left.BytesSent + right.BytesSent,
                replayTicksPruned: left.ReplayTicksPruned + right.ReplayTicksPruned,
                interestFilterCalls: left.InterestFilterCalls + right.InterestFilterCalls,
                interestCacheLookups: left.InterestCacheLookups + right.InterestCacheLookups,
                interestCacheHits: left.InterestCacheHits + right.InterestCacheHits,
                interestCacheMisses: left.InterestCacheMisses + right.InterestCacheMisses,
                fanoutReuseLookups: left.FanoutReuseLookups + right.FanoutReuseLookups,
                fanoutReuseHits: left.FanoutReuseHits + right.FanoutReuseHits,
                fanoutReuseMisses: left.FanoutReuseMisses + right.FanoutReuseMisses,
                fanoutReuseBytesSaved: left.FanoutReuseBytesSaved + right.FanoutReuseBytesSaved,
                sendAttempts: left.SendAttempts + right.SendAttempts,
                sentByLane: sentByLane,
                deferredByLane: deferredByLane,
                droppedByLane: droppedByLane,
                coalescedByLane: coalescedByLane);
        }

        private static NetworkReplicationPipeline.BuildTimingSnapshot Combine(
            NetworkReplicationPipeline.BuildTimingSnapshot left,
            NetworkReplicationPipeline.BuildTimingSnapshot right)
        {
            return new NetworkReplicationPipeline.BuildTimingSnapshot(
                left.ReplayVisitCalls + right.ReplayVisitCalls,
                left.ReplayVisitElapsedTicksTotal + right.ReplayVisitElapsedTicksTotal,
                Math.Max(left.ReplayVisitElapsedTicksMax, right.ReplayVisitElapsedTicksMax),
                left.CoalesceCommitCalls + right.CoalesceCommitCalls,
                left.CoalesceCommitElapsedTicksTotal + right.CoalesceCommitElapsedTicksTotal,
                Math.Max(left.CoalesceCommitElapsedTicksMax, right.CoalesceCommitElapsedTicksMax),
                left.DeferredFlushCalls + right.DeferredFlushCalls,
                left.DeferredFlushElapsedTicksTotal + right.DeferredFlushElapsedTicksTotal,
                Math.Max(left.DeferredFlushElapsedTicksMax, right.DeferredFlushElapsedTicksMax),
                left.RecipientSelectionCalls + right.RecipientSelectionCalls,
                left.RecipientSelectionElapsedTicksTotal + right.RecipientSelectionElapsedTicksTotal,
                Math.Max(left.RecipientSelectionElapsedTicksMax, right.RecipientSelectionElapsedTicksMax),
                left.SelectedDispatchCalls + right.SelectedDispatchCalls,
                left.SelectedDispatchElapsedTicksTotal + right.SelectedDispatchElapsedTicksTotal,
                Math.Max(left.SelectedDispatchElapsedTicksMax, right.SelectedDispatchElapsedTicksMax),
                left.TrySendCalls + right.TrySendCalls,
                left.TrySendElapsedTicksTotal + right.TrySendElapsedTicksTotal,
                Math.Max(left.TrySendElapsedTicksMax, right.TrySendElapsedTicksMax));
        }

        private static ServerReplicationCandidateFactory CreateDefaultCandidateFactory(
            ushort streamId,
            ReplicationLane lane)
        {
            return (World _, in Entity entity, long tick, ServerNetworkEntityMap entityMap, out ReplicationCandidate candidate) =>
            {
                NetworkEntityId networkEntityId = entityMap.GetOrAssignNetworkEntityId(entity);
                candidate = new ReplicationCandidate(tick, networkEntityId, streamId, lane);
                return true;
            };
        }

        private static void DefaultPacketWriter(INetworkTransport transport, in ReplicationPacket packet)
        {
            transport.EnqueueOutbound(new OutboundDatagram(
                packet.ConnectionId,
                ChannelType.ReliableOrdered,
                packet.Payload));
        }

        private static void RecordElapsed(
            long startTimestamp,
            ref long callCount,
            ref long elapsedTicksTotal,
            ref long elapsedTicksMax)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            callCount++;
            elapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > elapsedTicksMax)
                elapsedTicksMax = elapsedTicks;
        }

        private static void RecordAllocatedBytes(
            long allocatedBefore,
            ref long allocatedBytesTotal,
            ref long allocatedBytesMax)
        {
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            allocatedBytesTotal += allocatedBytes;
            if (allocatedBytes > allocatedBytesMax)
                allocatedBytesMax = allocatedBytes;
        }
    }
}
