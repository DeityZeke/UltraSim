using System;
using System.Buffers;
using System.Threading;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.Server.Engines.Chunking;
using UltraSim.Server.Engines.Networking;
using UltraSim.Shared.Engines.Networking;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Server.Gameplay
{
    public sealed class ServerPlayerReplicationService
    {
        public readonly struct ReplicationTimingSnapshot
        {
            public ReplicationTimingSnapshot(
                long aoiRefreshCalls,
                long aoiRefreshElapsedTicksTotal,
                long aoiRefreshElapsedTicksMax,
                long bridgeCalls,
                long bridgeElapsedTicksTotal,
                long bridgeElapsedTicksMax,
                long ownerPayloadRents,
                long ownerPayloadRentBytes,
                long observerPayloadMisses,
                long observerPayloadRentBytes,
                long batchPayloadRents,
                long batchPayloadRentBytes,
                long ownerTransformCalls,
                long ownerTransformAllocatedBytesTotal,
                long ownerTransformAllocatedBytesMax,
                long observerTransformCalls,
                long observerTransformAllocatedBytesTotal,
                long observerTransformAllocatedBytesMax,
                long batchFinalizeCalls,
                long batchFinalizeAllocatedBytesTotal,
                long batchFinalizeAllocatedBytesMax,
                long observerCandidatesPublished,
                long observerCandidatesCadenceSkipped,
                long observerChunkPublishCalls,
                long observerChunkPublishElapsedTicksTotal,
                long observerChunkPublishElapsedTicksMax,
                long observerChunkPublishedDeltas,
                long observerChunkPublishedBytes,
                int observerChunkActiveWindows,
                int observerChunkActiveDeltas,
                int observerChunkActiveBytes,
                int observerChunkMaxWindowDeltas,
                int observerChunkMaxWindowBytes,
                long observerChunkBuildCalls,
                long observerChunkBuildElapsedTicksTotal,
                long observerChunkBuildElapsedTicksMax,
                long observerChunkBuiltConnections,
                long observerChunkSentPackets,
                long observerChunkVisitedDeltas,
                long observerChunkCoalescedReplaced,
                long observerChunkBudgetDropped,
                long ownerCorrectionsSent,
                long ownerCorrectionPayloadBytes,
                long observerCorrectionsSent,
                long observerCorrectionPayloadBytes,
                long lodChecks,
                long lodSent,
                long lodGated,
                long lodNearSent,
                long lodRing1Sent,
                long lodRing2Sent,
                long lodNearChecks,
                long lodRing1Checks,
                long lodRing2Checks)
            {
                AoiRefreshCalls = aoiRefreshCalls;
                AoiRefreshElapsedTicksTotal = aoiRefreshElapsedTicksTotal;
                AoiRefreshElapsedTicksMax = aoiRefreshElapsedTicksMax;
                BridgeCalls = bridgeCalls;
                BridgeElapsedTicksTotal = bridgeElapsedTicksTotal;
                BridgeElapsedTicksMax = bridgeElapsedTicksMax;
                OwnerPayloadRents = ownerPayloadRents;
                OwnerPayloadRentBytes = ownerPayloadRentBytes;
                ObserverPayloadMisses = observerPayloadMisses;
                ObserverPayloadRentBytes = observerPayloadRentBytes;
                BatchPayloadRents = batchPayloadRents;
                BatchPayloadRentBytes = batchPayloadRentBytes;
                OwnerTransformCalls = ownerTransformCalls;
                OwnerTransformAllocatedBytesTotal = ownerTransformAllocatedBytesTotal;
                OwnerTransformAllocatedBytesMax = ownerTransformAllocatedBytesMax;
                ObserverTransformCalls = observerTransformCalls;
                ObserverTransformAllocatedBytesTotal = observerTransformAllocatedBytesTotal;
                ObserverTransformAllocatedBytesMax = observerTransformAllocatedBytesMax;
                BatchFinalizeCalls = batchFinalizeCalls;
                BatchFinalizeAllocatedBytesTotal = batchFinalizeAllocatedBytesTotal;
                BatchFinalizeAllocatedBytesMax = batchFinalizeAllocatedBytesMax;
                ObserverCandidatesPublished = observerCandidatesPublished;
                ObserverCandidatesCadenceSkipped = observerCandidatesCadenceSkipped;
                ObserverChunkPublishCalls = observerChunkPublishCalls;
                ObserverChunkPublishElapsedTicksTotal = observerChunkPublishElapsedTicksTotal;
                ObserverChunkPublishElapsedTicksMax = observerChunkPublishElapsedTicksMax;
                ObserverChunkPublishedDeltas = observerChunkPublishedDeltas;
                ObserverChunkPublishedBytes = observerChunkPublishedBytes;
                ObserverChunkActiveWindows = observerChunkActiveWindows;
                ObserverChunkActiveDeltas = observerChunkActiveDeltas;
                ObserverChunkActiveBytes = observerChunkActiveBytes;
                ObserverChunkMaxWindowDeltas = observerChunkMaxWindowDeltas;
                ObserverChunkMaxWindowBytes = observerChunkMaxWindowBytes;
                ObserverChunkBuildCalls = observerChunkBuildCalls;
                ObserverChunkBuildElapsedTicksTotal = observerChunkBuildElapsedTicksTotal;
                ObserverChunkBuildElapsedTicksMax = observerChunkBuildElapsedTicksMax;
                ObserverChunkBuiltConnections = observerChunkBuiltConnections;
                ObserverChunkSentPackets = observerChunkSentPackets;
                ObserverChunkVisitedDeltas = observerChunkVisitedDeltas;
                ObserverChunkCoalescedReplaced = observerChunkCoalescedReplaced;
                ObserverChunkBudgetDropped = observerChunkBudgetDropped;
                OwnerCorrectionsSent = ownerCorrectionsSent;
                OwnerCorrectionPayloadBytes = ownerCorrectionPayloadBytes;
                ObserverCorrectionsSent = observerCorrectionsSent;
                ObserverCorrectionPayloadBytes = observerCorrectionPayloadBytes;
                LodChecks = lodChecks;
                LodSent = lodSent;
                LodGated = lodGated;
                LodNearSent = lodNearSent;
                LodRing1Sent = lodRing1Sent;
                LodRing2Sent = lodRing2Sent;
                LodNearChecks = lodNearChecks;
                LodRing1Checks = lodRing1Checks;
                LodRing2Checks = lodRing2Checks;
            }

            public long AoiRefreshCalls { get; }
            public long AoiRefreshElapsedTicksTotal { get; }
            public long AoiRefreshElapsedTicksMax { get; }
            public long BridgeCalls { get; }
            public long BridgeElapsedTicksTotal { get; }
            public long BridgeElapsedTicksMax { get; }
            public long OwnerPayloadRents { get; }
            public long OwnerPayloadRentBytes { get; }
            public long ObserverPayloadMisses { get; }
            public long ObserverPayloadRentBytes { get; }
            public long BatchPayloadRents { get; }
            public long BatchPayloadRentBytes { get; }
            public long OwnerTransformCalls { get; }
            public long OwnerTransformAllocatedBytesTotal { get; }
            public long OwnerTransformAllocatedBytesMax { get; }
            public long ObserverTransformCalls { get; }
            public long ObserverTransformAllocatedBytesTotal { get; }
            public long ObserverTransformAllocatedBytesMax { get; }
            public long BatchFinalizeCalls { get; }
            public long BatchFinalizeAllocatedBytesTotal { get; }
            public long BatchFinalizeAllocatedBytesMax { get; }
            public long ObserverCandidatesPublished { get; }
            public long ObserverCandidatesCadenceSkipped { get; }
            public long ObserverChunkPublishCalls { get; }
            public long ObserverChunkPublishElapsedTicksTotal { get; }
            public long ObserverChunkPublishElapsedTicksMax { get; }
            public long ObserverChunkPublishedDeltas { get; }
            public long ObserverChunkPublishedBytes { get; }
            public int ObserverChunkActiveWindows { get; }
            public int ObserverChunkActiveDeltas { get; }
            public int ObserverChunkActiveBytes { get; }
            public int ObserverChunkMaxWindowDeltas { get; }
            public int ObserverChunkMaxWindowBytes { get; }
            public long ObserverChunkBuildCalls { get; }
            public long ObserverChunkBuildElapsedTicksTotal { get; }
            public long ObserverChunkBuildElapsedTicksMax { get; }
            public long ObserverChunkBuiltConnections { get; }
            public long ObserverChunkSentPackets { get; }
            public long ObserverChunkVisitedDeltas { get; }
            public long ObserverChunkCoalescedReplaced { get; }
            public long ObserverChunkBudgetDropped { get; }
            public long OwnerCorrectionsSent { get; }
            public long OwnerCorrectionPayloadBytes { get; }
            public long ObserverCorrectionsSent { get; }
            public long ObserverCorrectionPayloadBytes { get; }
            public long LodChecks { get; }
            public long LodSent { get; }
            public long LodGated { get; }
            public long LodNearSent { get; }
            public long LodRing1Sent { get; }
            public long LodRing2Sent { get; }
            /// <summary>Raw ring-0 checks (all, gate-independent). Divide by (sessions × ticks) for avg entities per connection per tick at ring 0.</summary>
            public long LodNearChecks { get; }
            /// <summary>Raw ring-1 checks (all, gate-independent).</summary>
            public long LodRing1Checks { get; }
            /// <summary>Raw ring-2 checks (all, gate-independent).</summary>
            public long LodRing2Checks { get; }
        }

        private struct ObserverChunkBuildState
        {
            public ServerPlayerReplicationService Service;
            public NetworkReplicationPipeline.BuildSession Session;
            public int ConnectionId;
            public int SentPackets;
            public int VisitedDeltas;
            public bool SentAny;
        }

        private const ushort LegacyCorrectionStreamId = 1;
        private const ushort OwnerCorrectionStreamId = 2;
        private const ushort ObserverCorrectionStreamId = 3;
        private const int CorrectionPayloadBytes = EntityFloat3StateYawAckOwnerTick32Codec.PayloadBytes;
        private const byte ServerCorrectionBatchMessageType = 0xD6;
        private const int CorrectionBatchHeaderBytes = 3;
        private const int CorrectionBatchEntryHeaderBytes = 2;
        private const int MaxCorrectionBatchPayloadBytes = 60 * 1024;
        private const int MaxCorrectionBatchEntries = 2048;
        private readonly ServerPlayerSessionWorld _playerWorld;
        private readonly INetworkTransport _transport;
        private readonly NetworkSessionManager _sessions;
        private readonly HashSet<int> _trackedConnections = new HashSet<int>();
        private readonly Dictionary<int, HashSet<ulong>> _visibleByConnection = new Dictionary<int, HashSet<ulong>>();
        private readonly Dictionary<int, ChunkCoord> _centerChunkByConnection = new Dictionary<int, ChunkCoord>();
        private readonly Dictionary<int, HashSet<ChunkCoord>> _subscribedChunksByConnection = new Dictionary<int, HashSet<ChunkCoord>>();
        private readonly Dictionary<int, Dictionary<ChunkCoord, int>> _subscribedChunkVersionsByConnection = new Dictionary<int, Dictionary<ChunkCoord, int>>();
        private readonly Dictionary<int, Dictionary<ulong, long>> _pendingDespawnTickByConnection = new Dictionary<int, Dictionary<ulong, long>>();
        private readonly HashSet<ulong> _desiredVisibleScratch = new HashSet<ulong>();
        private readonly Dictionary<ChunkCoord, HashSet<int>> _chunkSubscribers = new Dictionary<ChunkCoord, HashSet<int>>();
        private readonly HashSet<ChunkCoord> _subscribedChunksScratch = new HashSet<ChunkCoord>();
        private readonly List<ChunkCoord> _removedChunksScratch = new List<ChunkCoord>(16);
        private readonly List<ulong> _visibilityRemovedScratch = new List<ulong>(64);
        private readonly List<ulong> _pendingDespawnsDueScratch = new List<ulong>(64);
        private int[] _trackedConnectionSnapshot = Array.Empty<int>();
        private int _trackedConnectionSnapshotCount;
        private Entity[] _dirtyPlayerScratch = new Entity[256];
        private readonly Dictionary<PayloadSliceKey, byte[]> _observerPayloadCache = new Dictionary<PayloadSliceKey, byte[]>();
        private readonly List<byte[]> _observerPayloadPendingReturn = new List<byte[]>(256);
        private readonly List<byte[]> _correctionPayloadPendingReturn = new List<byte[]>(256);
        private readonly Dictionary<int, CorrectionBatchAccumulator> _correctionBatchByConnection = new Dictionary<int, CorrectionBatchAccumulator>();
        private readonly List<int> _correctionBatchConnectionIds = new List<int>();
        private readonly Stack<CorrectionBatchAccumulator> _correctionBatchAccumulatorPool = new Stack<CorrectionBatchAccumulator>();
        private int[] _multicastConnectionIdScratch = Array.Empty<int>();
        private readonly Dictionary<ulong, ServerPublishedCorrectionState> _publishedCorrectionByNetworkId = new Dictionary<ulong, ServerPublishedCorrectionState>();
        private readonly HashSet<ulong> _forcePublishNetworkIds = new HashSet<ulong>();
        private readonly HashSet<ulong> _publishEligibleThisTick = new HashSet<ulong>();
        private readonly SpatialChunkIndex? _spatialChunks;
        private readonly ObserverChunkReplayIndex? _observerChunkReplay;
        private bool _spatialChunksInitialized;
        private long _aoiRefreshCalls;
        private long _aoiRefreshElapsedTicksTotal;
        private long _aoiRefreshElapsedTicksMax;
        private long _bridgeCalls;
        private long _bridgeElapsedTicksTotal;
        private long _bridgeElapsedTicksMax;
        private long _ownerPayloadRents;
        private long _ownerPayloadRentBytes;
        private long _observerPayloadMisses;
        private long _observerPayloadRentBytes;
        private long _ownerCorrectionsSent;
        private long _ownerCorrectionPayloadBytes;
        private long _observerCorrectionsSent;
        private long _observerCorrectionPayloadBytes;
        // LOD gate telemetry — written from parallel build workers, requires Interlocked.
        private long _lodChecks;
        private long _lodSent;
        private long _lodGated;
        private long _lodNearSent;
        private long _lodRing1Sent;
        private long _lodRing2Sent;
        // Raw ring classification counts (all checks, regardless of gate outcome).
        // lod_near_checks + lod_ring1_checks + lod_ring2_checks == lod_checks.
        private long _lodNearChecks;
        private long _lodRing1Checks;
        private long _lodRing2Checks;
        private long _ownerTransformCalls;
        private long _ownerTransformAllocatedBytesTotal;
        private long _ownerTransformAllocatedBytesMax;
        private long _observerTransformCalls;
        private long _observerTransformAllocatedBytesTotal;
        private long _observerTransformAllocatedBytesMax;
        private long _batchFinalizeCalls;
        private long _batchFinalizeAllocatedBytesTotal;
        private long _batchFinalizeAllocatedBytesMax;
        private long _observerCandidatesPublished;
        private long _observerCandidatesCadenceSkipped;
        private long _observerChunkBuildCalls;
        private long _observerChunkBuildElapsedTicksTotal;
        private long _observerChunkBuildElapsedTicksMax;
        private long _observerChunkBuiltConnections;
        private long _observerChunkSentPackets;
        private long _observerChunkVisitedDeltas;
        private long _observerChunkCoalescedReplaced;
        private long _observerChunkBudgetDropped;
        private int _lastObserverChunkPublishedThisTick;

        public ServerPlayerReplicationService(ServerPlayerSessionWorld playerWorld, INetworkTransport transport)
            : this(
                playerWorld,
                transport,
                sessions: new NetworkSessionManager(),
                enableChunkAoi: false,
                chunkSizeGridUnits: 40,
                chunkAoiRadius: 1,
                enableRecipientSelectorFastPath: true,
                enableRecipientSelectorChunkGroupCacheKey: true,
                enablePriorityLod: true,
                priorityLodNearCadenceTicks: 1,
                priorityLodOuterCadenceTicks: 2,
                priorityLodRing1MaxChunks: 3,
                priorityLodFarCadenceTicks: 3,
                enableDenseObserverCadence: false,
                denseObserverCadenceStartSubscribers: 64,
                denseObserverCadenceStepSubscribers: 32,
                denseObserverCadenceMaxTicks: 4,
                enableDenseObserverRecipientCap: false,
                denseObserverRecipientCapStartSubscribers: 64,
                denseObserverRecipientCapMaxRecipients: 24,
                denseObserverRecipientPriorityChunkRadius: 0,
                aoiDespawnGraceTicks: 0,
                enableDualCorrectionStreams: false,
                enableCorrectionBatching: true,
                enableCorrectionDeadband: true,
                correctionDeadbandUnits: 0,
                correctionMaxSilentTicks: 1,
                replicationBuildBatchPacketLimit: 64,
                maxReplicationBuildWorkers: 0,
                observerStreamCadenceTicks: 1,
                enableObserverSpatialFirstBuild: false)
        {
        }

        public ServerPlayerReplicationService(
            ServerPlayerSessionWorld playerWorld,
            INetworkTransport transport,
            NetworkSessionManager sessions,
            bool enableChunkAoi,
            int chunkSizeGridUnits,
            int chunkAoiRadius,
            bool enableRecipientSelectorFastPath,
            bool enableRecipientSelectorChunkGroupCacheKey,
            bool enablePriorityLod,
            int priorityLodNearCadenceTicks,
            int priorityLodOuterCadenceTicks,
            int priorityLodRing1MaxChunks,
            int priorityLodFarCadenceTicks,
            bool enableDenseObserverCadence,
            int denseObserverCadenceStartSubscribers,
            int denseObserverCadenceStepSubscribers,
            int denseObserverCadenceMaxTicks,
            bool enableDenseObserverRecipientCap,
            int denseObserverRecipientCapStartSubscribers,
            int denseObserverRecipientCapMaxRecipients,
            int denseObserverRecipientPriorityChunkRadius,
            int aoiDespawnGraceTicks,
            bool enableDualCorrectionStreams,
            bool enableCorrectionBatching,
            bool enableCorrectionDeadband,
            int correctionDeadbandUnits,
            int correctionMaxSilentTicks,
            int replicationBuildBatchPacketLimit,
            int maxReplicationBuildWorkers,
            int observerStreamCadenceTicks,
            bool enableObserverSpatialFirstBuild)
        {
            _playerWorld = playerWorld ?? throw new ArgumentNullException(nameof(playerWorld));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            EnableChunkAoi = enableChunkAoi;
            ChunkSizeGridUnits = Math.Max(1, chunkSizeGridUnits);
            ChunkAoiRadius = Math.Max(0, chunkAoiRadius);
            EnableRecipientSelectorFastPath = enableRecipientSelectorFastPath;
            EnableRecipientSelectorChunkGroupCacheKey = enableRecipientSelectorChunkGroupCacheKey;
            EnablePriorityLod = enablePriorityLod;
            PriorityLodNearCadenceTicks = Math.Max(1, priorityLodNearCadenceTicks);
            PriorityLodOuterCadenceTicks = Math.Max(1, priorityLodOuterCadenceTicks);
            PriorityLodRing1MaxChunks = Math.Max(1, priorityLodRing1MaxChunks);
            PriorityLodFarCadenceTicks = Math.Max(1, priorityLodFarCadenceTicks);
            EnableDenseObserverCadence = enableDenseObserverCadence;
            DenseObserverCadenceStartSubscribers = Math.Max(1, denseObserverCadenceStartSubscribers);
            DenseObserverCadenceStepSubscribers = Math.Max(1, denseObserverCadenceStepSubscribers);
            DenseObserverCadenceMaxTicks = Math.Max(1, denseObserverCadenceMaxTicks);
            EnableDenseObserverRecipientCap = enableDenseObserverRecipientCap;
            DenseObserverRecipientCapStartSubscribers = Math.Max(1, denseObserverRecipientCapStartSubscribers);
            DenseObserverRecipientCapMaxRecipients = Math.Max(1, denseObserverRecipientCapMaxRecipients);
            DenseObserverRecipientPriorityChunkRadius = Math.Max(0, denseObserverRecipientPriorityChunkRadius);
            AoiDespawnGraceTicks = Math.Max(0, aoiDespawnGraceTicks);
            EnableDualCorrectionStreams = enableDualCorrectionStreams;
            EnableCorrectionBatching = enableCorrectionBatching;
            EnableCorrectionDeadband = enableCorrectionDeadband;
            CorrectionDeadbandUnits = Math.Max(0, correctionDeadbandUnits);
            CorrectionMaxSilentTicks = Math.Max(1, correctionMaxSilentTicks);
            ReplicationBuildBatchPacketLimit = Math.Max(1, replicationBuildBatchPacketLimit);
            MaxReplicationBuildWorkers = maxReplicationBuildWorkers < -1 ? -1 : maxReplicationBuildWorkers;
            ObserverStreamCadenceTicks = Math.Max(1, observerStreamCadenceTicks);
            // Spatial-first build is mutually exclusive with per-entity LOD/cadence features.
            // Benchmarks show that adding per-delta chunk lookups inside AppendObserverChunkPacket
            // (required to apply LOD/cadence gates in the spatial path) costs more than the
            // connection-centric cache benefit of spatial-first provides. When LOD or dense
            // cadence is enabled, the standard entity-centric pipeline with SelectRecipientsForDelta
            // is faster at current chunk topology (~30 subscribers/chunk, AoI radius=1).
            // Re-evaluate if AoI radius grows significantly or if chunk lookup is made O(1) without
            // a dictionary (e.g., by encoding chunk coord in the replay delta).
            EnableObserverSpatialFirstBuild = enableObserverSpatialFirstBuild &&
                enableChunkAoi &&
                enableDualCorrectionStreams &&
                !enableDenseObserverCadence &&
                !enablePriorityLod &&
                !enableDenseObserverRecipientCap;
            _spatialChunks = EnableChunkAoi ? new SpatialChunkIndex(ChunkSizeGridUnits) : null;
            if (EnableObserverSpatialFirstBuild)
                _observerChunkReplay = new ObserverChunkReplayIndex(64, EntityFloat3StateYawTick32Codec.PayloadBytes * 8);
            Bridge = CreateBridge();
        }

        public ServerNetworkReplicationBridge Bridge { get; private set; }

        public int LastPublishedCandidates { get; private set; }

        public int LastSentPackets { get; private set; }

        public int TrackedConnectionCount => _trackedConnections.Count;

        /// <summary>
        /// Returns the average, min, and max number of visible entities per tracked connection,
        /// based on the current AOI visibility state. Returns zeros when chunk AOI is disabled
        /// or no connections are tracked.
        /// </summary>
        public (double Avg, int Min, int Max) GetVisiblePeerStats()
        {
            if (!EnableChunkAoi || _visibleByConnection.Count == 0)
                return (0, 0, 0);

            long total = 0;
            int min = int.MaxValue;
            int max = 0;
            foreach (HashSet<ulong> visible in _visibleByConnection.Values)
            {
                int count = visible.Count;
                total += count;
                if (count < min) min = count;
                if (count > max) max = count;
            }
            double avg = (double)total / _visibleByConnection.Count;
            return (avg, min == int.MaxValue ? 0 : min, max);
        }

        public double GetAvgVisiblePeersPerConnection()
        {
            (double avg, _, _) = GetVisiblePeerStats();
            return avg;
        }

public bool EnableChunkAoi { get; }

        public int ChunkSizeGridUnits { get; }

        public int ChunkAoiRadius { get; }

        public bool EnableRecipientSelectorFastPath { get; }

        public bool EnableRecipientSelectorChunkGroupCacheKey { get; }

        public bool EnablePriorityLod { get; }

        public int PriorityLodNearCadenceTicks { get; }

        public int PriorityLodOuterCadenceTicks { get; }

        public int PriorityLodRing1MaxChunks { get; }

        public int PriorityLodFarCadenceTicks { get; }

        public bool EnableDenseObserverCadence { get; }

        public int DenseObserverCadenceStartSubscribers { get; }

        public int DenseObserverCadenceStepSubscribers { get; }

        public int DenseObserverCadenceMaxTicks { get; }

        public bool EnableDenseObserverRecipientCap { get; }

        public int DenseObserverRecipientCapStartSubscribers { get; }

        public int DenseObserverRecipientCapMaxRecipients { get; }

        public int DenseObserverRecipientPriorityChunkRadius { get; }

        public int AoiDespawnGraceTicks { get; }

        public bool EnableDualCorrectionStreams { get; }

        public bool EnableCorrectionBatching { get; }

        public bool EnableCorrectionDeadband { get; }

        public int CorrectionDeadbandUnits { get; }

        public int CorrectionMaxSilentTicks { get; }

        public int ReplicationBuildBatchPacketLimit { get; }

        public int MaxReplicationBuildWorkers { get; }

        public int ObserverStreamCadenceTicks { get; }

        public bool EnableObserverSpatialFirstBuild { get; }

        public ReplicationTimingSnapshot GetTimingSnapshot() =>
            CreateReplicationTimingSnapshot();

        private ReplicationTimingSnapshot CreateReplicationTimingSnapshot()
        {
            ObserverChunkReplayIndex.Snapshot observerChunkReplay = _observerChunkReplay?.GetSnapshot() ?? default;
            return
            new ReplicationTimingSnapshot(
                _aoiRefreshCalls,
                _aoiRefreshElapsedTicksTotal,
                _aoiRefreshElapsedTicksMax,
                _bridgeCalls,
                _bridgeElapsedTicksTotal,
                _bridgeElapsedTicksMax,
                _ownerPayloadRents,
                _ownerPayloadRentBytes,
                _observerPayloadMisses,
                _observerPayloadRentBytes,
                0,
                0,
                _ownerTransformCalls,
                _ownerTransformAllocatedBytesTotal,
                _ownerTransformAllocatedBytesMax,
                _observerTransformCalls,
                _observerTransformAllocatedBytesTotal,
                _observerTransformAllocatedBytesMax,
                _batchFinalizeCalls,
                _batchFinalizeAllocatedBytesTotal,
                _batchFinalizeAllocatedBytesMax,
                _observerCandidatesPublished,
                _observerCandidatesCadenceSkipped,
                observerChunkReplay.PublishCalls,
                observerChunkReplay.PublishElapsedTicksTotal,
                observerChunkReplay.PublishElapsedTicksMax,
                observerChunkReplay.PublishedDeltas,
                observerChunkReplay.PublishedBytes,
                observerChunkReplay.ActiveChunkWindows,
                observerChunkReplay.ActiveDeltas,
                observerChunkReplay.ActiveBytes,
                observerChunkReplay.MaxWindowDeltas,
                observerChunkReplay.MaxWindowBytes,
                _observerChunkBuildCalls,
                _observerChunkBuildElapsedTicksTotal,
                _observerChunkBuildElapsedTicksMax,
                _observerChunkBuiltConnections,
                _observerChunkSentPackets,
                _observerChunkVisitedDeltas,
                _observerChunkCoalescedReplaced,
                _observerChunkBudgetDropped,
                _ownerCorrectionsSent,
                _ownerCorrectionPayloadBytes,
                _observerCorrectionsSent,
                _observerCorrectionPayloadBytes,
                Interlocked.Read(ref _lodChecks),
                Interlocked.Read(ref _lodSent),
                Interlocked.Read(ref _lodGated),
                Interlocked.Read(ref _lodNearSent),
                Interlocked.Read(ref _lodRing1Sent),
                Interlocked.Read(ref _lodRing2Sent),
                Interlocked.Read(ref _lodNearChecks),
                Interlocked.Read(ref _lodRing1Checks),
                Interlocked.Read(ref _lodRing2Checks));
        }

        public ReplicationStatsSnapshot GetReplicationStatsSnapshot() =>
            Bridge.GetReplicationStatsSnapshot();

        public ServerNetworkReplicationBridge.BridgeTimingSnapshot GetBridgeTimingSnapshot() =>
            Bridge.GetTimingSnapshot();

        public NetworkReplicationPipeline.BuildTimingSnapshot GetPipelineTimingSnapshot() =>
            Bridge.GetPipelineTimingSnapshot();

        public ReplicationReplayWindow.ReplayWindowSnapshot GetReplayWindowSnapshot() =>
            Bridge.GetReplayWindowSnapshot();

        public void Reset()
        {
            ReturnObserverPayloadBuffers();
            _trackedConnections.Clear();
            _visibleByConnection.Clear();
            _centerChunkByConnection.Clear();
            _subscribedChunksByConnection.Clear();
            _subscribedChunkVersionsByConnection.Clear();
            _pendingDespawnTickByConnection.Clear();
            _desiredVisibleScratch.Clear();
            _chunkSubscribers.Clear();
            _subscribedChunksScratch.Clear();
            _observerPayloadCache.Clear();
            _correctionBatchByConnection.Clear();
            _correctionBatchConnectionIds.Clear();
            _publishedCorrectionByNetworkId.Clear();
            _forcePublishNetworkIds.Clear();
            _publishEligibleThisTick.Clear();
            _spatialChunks?.Clear();
            _observerChunkReplay?.Reset();
            _spatialChunksInitialized = false;
            _trackedConnectionSnapshotCount = 0;
            LastPublishedCandidates = 0;
            LastSentPackets = 0;
            _aoiRefreshCalls = 0;
            _aoiRefreshElapsedTicksTotal = 0;
            _aoiRefreshElapsedTicksMax = 0;
            _bridgeCalls = 0;
            _bridgeElapsedTicksTotal = 0;
            _bridgeElapsedTicksMax = 0;
            _ownerTransformCalls = 0;
            _ownerTransformAllocatedBytesTotal = 0;
            _ownerTransformAllocatedBytesMax = 0;
            _observerTransformCalls = 0;
            _observerTransformAllocatedBytesTotal = 0;
            _observerTransformAllocatedBytesMax = 0;
            _batchFinalizeCalls = 0;
            _batchFinalizeAllocatedBytesTotal = 0;
            _batchFinalizeAllocatedBytesMax = 0;
            _observerCandidatesPublished = 0;
            _observerCandidatesCadenceSkipped = 0;
            _observerChunkBuildCalls = 0;
            _observerChunkBuildElapsedTicksTotal = 0;
            _observerChunkBuildElapsedTicksMax = 0;
            _observerChunkBuiltConnections = 0;
            _observerChunkSentPackets = 0;
            _observerChunkVisitedDeltas = 0;
            _observerChunkCoalescedReplaced = 0;
            _observerChunkBudgetDropped = 0;
            Bridge = CreateBridge();
        }

        public void TrackConnection(int connectionId, long snapshotTick = 0)
        {
            if (_trackedConnections.Add(connectionId))
            {
                Bridge.TrackConnection(connectionId, lastAckedTick: snapshotTick);
                if (EnableChunkAoi)
                {
                    _visibleByConnection[connectionId] = new HashSet<ulong>();
                    _subscribedChunksByConnection[connectionId] = new HashSet<ChunkCoord>();
                    _subscribedChunkVersionsByConnection[connectionId] = new Dictionary<ChunkCoord, int>(16);
                    _pendingDespawnTickByConnection[connectionId] = new Dictionary<ulong, long>();
                }
                ForcePublishAllKnownPlayers();
            }
        }

        public void UntrackConnection(int connectionId)
        {
            _trackedConnections.Remove(connectionId);
            _visibleByConnection.Remove(connectionId);
            _centerChunkByConnection.Remove(connectionId);
            if (_subscribedChunksByConnection.TryGetValue(connectionId, out HashSet<ChunkCoord>? subscribedChunks))
                RemoveConnectionFromChunkSubscribers(connectionId, subscribedChunks);
            _subscribedChunksByConnection.Remove(connectionId);
            _subscribedChunkVersionsByConnection.Remove(connectionId);
            _pendingDespawnTickByConnection.Remove(connectionId);
            if (_correctionBatchByConnection.Remove(connectionId, out CorrectionBatchAccumulator? pooledAccumulator))
            {
                pooledAccumulator.Reset();
                pooledAccumulator.ResetReusableBatchPayloadBuffers();
                _correctionBatchAccumulatorPool.Push(pooledAccumulator);
            }
            _correctionBatchConnectionIds.Remove(connectionId);
            Bridge.UntrackConnection(connectionId);
        }

        public void MarkAllPlayersDirty()
        {
            _playerWorld.World.Query<PlayerState>((Entity entity, ref PlayerState _) =>
            {
                _playerWorld.World.MarkDirty<PlayerState>(entity);
            });
        }

        public void ReplicateTick(long tick)
        {
            ReturnObserverPayloadBuffers();
            ReturnCorrectionPayloadBuffers();
            ResetReusableCorrectionBatchPayloadBuffers();
            _observerPayloadCache.Clear();
            _publishEligibleThisTick.Clear();
            _observerChunkReplay?.Prune(tick);
            _lastObserverChunkPublishedThisTick = 0;
            MarkCorrectionAgeExpiredEntitiesDirty(tick);
            long aoiStart = Stopwatch.GetTimestamp();
            RefreshChunkVisibility(tick);
            RecordElapsed(aoiStart, ref _aoiRefreshCalls, ref _aoiRefreshElapsedTicksTotal, ref _aoiRefreshElapsedTicksMax);
            CaptureTrackedConnectionSnapshot();
            try
            {
                long bridgeStart = Stopwatch.GetTimestamp();
                ServerReplicationTickResult result = Bridge.ReplicateTick(tick, flushOutbound: false);
                RecordElapsed(bridgeStart, ref _bridgeCalls, ref _bridgeElapsedTicksTotal, ref _bridgeElapsedTicksMax);
                LastPublishedCandidates = result.PublishedCandidates + _lastObserverChunkPublishedThisTick;
                LastSentPackets = result.SentPackets;
            }
            finally
            {
                _forcePublishNetworkIds.Clear();
                ResetCorrectionBatchAccumulators();
            }
        }

        private void CaptureTrackedConnectionSnapshot()
        {
            int trackedCount = _trackedConnections.Count;
            if (_trackedConnectionSnapshot.Length < trackedCount)
            {
                int newSize = _trackedConnectionSnapshot.Length == 0 ? 64 : _trackedConnectionSnapshot.Length;
                while (newSize < trackedCount)
                    newSize <<= 1;
                Array.Resize(ref _trackedConnectionSnapshot, newSize);
            }

            int write = 0;
            foreach (int connectionId in _trackedConnections)
                _trackedConnectionSnapshot[write++] = connectionId;
            _trackedConnectionSnapshotCount = write;
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
            ref long callCount,
            ref long allocatedBytesTotal,
            ref long allocatedBytesMax)
        {
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            callCount++;
            allocatedBytesTotal += allocatedBytes;
            if (allocatedBytes > allocatedBytesMax)
                allocatedBytesMax = allocatedBytes;
        }

        public void HandleSessionDisconnected(long sessionId, int connectionId)
        {
            if (_playerWorld.TryGetEntityForSession(sessionId, out Entity entity) &&
                Bridge.EntityMap.TryGetNetworkEntityId(entity, out NetworkEntityId entityId))
            {
                BroadcastDespawn(entityId, connectionId);
                Bridge.EntityMap.Remove(entity);
                _spatialChunks?.RemoveEntity(entityId.Value);
                _publishedCorrectionByNetworkId.Remove(entityId.Value);
                _forcePublishNetworkIds.Remove(entityId.Value);
            }

            UntrackConnection(connectionId);
        }

        public int CollectLocalChatRecipients(ulong senderNetworkEntityId, List<int> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (!EnableChunkAoi || _visibleByConnection.Count == 0)
            {
                foreach (int connectionId in _trackedConnections)
                {
                    if (_sessions.TryGetSessionIdByConnection(connectionId, out _))
                        destination.Add(connectionId);
                }

                return destination.Count;
            }

            foreach ((int connectionId, HashSet<ulong> visible) in _visibleByConnection)
            {
                if (!_sessions.TryGetSessionIdByConnection(connectionId, out _))
                    continue;

                if (visible.Contains(senderNetworkEntityId))
                {
                    destination.Add(connectionId);
                    continue;
                }

                if (_pendingDespawnTickByConnection.TryGetValue(connectionId, out Dictionary<ulong, long>? pendingDespawns) &&
                    pendingDespawns.ContainsKey(senderNetworkEntityId))
                {
                    destination.Add(connectionId);
                }
            }

            return destination.Count;
        }

        private void BroadcastDespawn(NetworkEntityId entityId, int excludedConnectionId)
        {
            byte[] payload = new byte[ServerDespawnCodec.PayloadBytes];
            if (!ServerDespawnCodec.TryEncode(payload, entityId, out int written))
                return;

            foreach (int connectionId in _trackedConnections)
            {
                if (connectionId == excludedConnectionId)
                    continue;

                _transport.EnqueueOutbound(new OutboundDatagram(
                    connectionId,
                    ChannelType.ReliableOrdered,
                    new ReadOnlyMemory<byte>(payload, 0, written)));
            }
        }

        private bool CreateCandidate(
            World world,
            in Entity entity,
            long tick,
            ServerNetworkEntityMap entityMap,
            out ReplicationCandidate candidate)
        {
            if (!world.TryGetComponent(entity, out PlayerState state) || state.OwnerConnectionId <= 0)
            {
                candidate = default;
                return false;
            }

            NetworkEntityId networkEntityId = entityMap.GetOrAssignNetworkEntityId(entity);
            if (!ShouldPublishServerCorrection(networkEntityId, in state, tick))
            {
                candidate = default;
                return false;
            }

            _publishEligibleThisTick.Add(networkEntityId.Value);
            PublishObserverChunkReplayCandidate(tick, networkEntityId, in state);
            ushort streamId = EnableDualCorrectionStreams ? OwnerCorrectionStreamId : LegacyCorrectionStreamId;
            candidate = new ReplicationCandidate(tick, networkEntityId, streamId, ReplicationLane.Critical);
            return true;
        }

        private bool CreateObserverCandidate(
            World world,
            in Entity entity,
            long tick,
            ServerNetworkEntityMap entityMap,
            out ReplicationCandidate candidate)
        {
            candidate = default;
            if (!EnableDualCorrectionStreams)
                return false;
            if (!world.TryGetComponent(entity, out PlayerState state) || state.OwnerConnectionId <= 0)
                return false;

            NetworkEntityId networkEntityId = entityMap.GetOrAssignNetworkEntityId(entity);
            if (!_publishEligibleThisTick.Contains(networkEntityId.Value))
                return false;
            if (ObserverStreamCadenceTicks > 1 &&
                (tick % ObserverStreamCadenceTicks) != 0)
            {
                _observerCandidatesCadenceSkipped++;
                return false;
            }

            candidate = new ReplicationCandidate(tick, networkEntityId, ObserverCorrectionStreamId, ReplicationLane.State);
            _observerCandidatesPublished++;
            return true;
        }

        private void PublishObserverChunkReplayCandidate(long tick, NetworkEntityId networkEntityId, in PlayerState state)
        {
            if (_observerChunkReplay == null || _spatialChunks == null)
                return;
            if (ObserverStreamCadenceTicks > 1 && (tick % ObserverStreamCadenceTicks) != 0)
            {
                _observerCandidatesCadenceSkipped++;
                return;
            }

            if (!MovementQuantization.TryQuantizePosition(state.X, out int quantizedX) ||
                !MovementQuantization.TryQuantizePosition(state.Z, out int quantizedZ))
            {
                return;
            }

            if (!_spatialChunks.TryGetChunkCoord(networkEntityId.Value, out ChunkCoord chunk))
                return;

            Span<byte> payload = stackalloc byte[EntityFloat3StateYawTick32Codec.PayloadBytes];
            ushort quantizedYaw = MovementQuantization.QuantizeYaw(state.YawRadians);
            if (!EntityFloat3StateYawTick32Codec.TryEncode(
                    payload,
                    networkEntityId,
                    quantizedX,
                    quantizedZ,
                    quantizedYaw,
                    state.ActorDefinitionId,
                    tick,
                    out int written))
            {
                return;
            }

            _observerCandidatesPublished++;
            _lastObserverChunkPublishedThisTick++;
            _observerChunkReplay.Publish(
                chunk,
                tick,
                networkEntityId,
                ObserverCorrectionStreamId,
                ReplicationLane.State,
                coalesceKey: 0,
                payload.Slice(0, written));
        }

        private int EncodeCorrection(in ReplicationCandidate candidate, Span<byte> destination)
        {
            if (!Bridge.EntityMap.TryGetEntity(candidate.EntityId, out Entity entity))
                return 0;

            if (!_playerWorld.World.TryGetComponent(entity, out PlayerState state))
                return 0;

            if (!MovementQuantization.TryQuantizePosition(state.X, out int quantizedX) ||
                !MovementQuantization.TryQuantizePosition(state.Z, out int quantizedZ))
                return 0;

            ushort quantizedYaw = MovementQuantization.QuantizeYaw(state.YawRadians);
            if (candidate.StreamId == ObserverCorrectionStreamId)
            {
                return EntityFloat3StateYawTick32Codec.TryEncode(
                    destination,
                    candidate.EntityId,
                    quantizedX,
                    quantizedZ,
                    quantizedYaw,
                    state.ActorDefinitionId,
                    candidate.Tick,
                    out int observerWritten)
                    ? observerWritten
                    : 0;
            }

            return EntityFloat3StateYawAckOwnerTick32Codec.TryEncode(
                destination,
                candidate.EntityId,
                quantizedX,
                quantizedZ,
                quantizedYaw,
                state.ActorDefinitionId,
                candidate.Tick,
                authoritativeSequence: 0,
                state.OwnerConnectionId,
                out int written)
                ? written
                : 0;
        }

        private ServerNetworkReplicationBridge CreateBridge()
        {
            int resolvedBuildWorkers = ResolveReplicationBuildWorkerCount();
            if (resolvedBuildWorkers <= 1)
            {
                var pipeline = CreatePipeline(replayWindow: null, parallelWorkerIndex: -1, parallelWorkerCount: 1);
                return new ServerNetworkReplicationBridge(
                    _playerWorld.World,
                    _transport,
                    pipeline,
                    entityMap: new ServerNetworkEntityMap(),
                    candidateFactory: CreateCandidate,
                    secondaryCandidateFactory: EnableDualCorrectionStreams && !EnableObserverSpatialFirstBuild ? CreateObserverCandidate : null,
                    packetWriter: WriteCorrectionPacket,
                    packetBatchWriter: WriteCorrectionPacketBatch,
                    supplementalBuild: EnableObserverSpatialFirstBuild ? DispatchObserverSpatialBuild : null,
                    buildBatchPacketLimit: ReplicationBuildBatchPacketLimit);
            }

            var replayWindow = new ReplicationReplayWindow(64, initialBucketBytes: CorrectionPayloadBytes * 8);
            var publishPipeline = CreatePipeline(replayWindow, parallelWorkerIndex: -1, parallelWorkerCount: 1);
            var buildPipelines = new NetworkReplicationPipeline[resolvedBuildWorkers];
            for (int i = 0; i < buildPipelines.Length; i++)
                buildPipelines[i] = CreatePipeline(replayWindow, i, buildPipelines.Length);

            return new ServerNetworkReplicationBridge(
                _playerWorld.World,
                _transport,
                publishPipeline,
                buildPipelines: buildPipelines,
                entityMap: new ServerNetworkEntityMap(),
                candidateFactory: CreateCandidate,
                secondaryCandidateFactory: EnableDualCorrectionStreams && !EnableObserverSpatialFirstBuild ? CreateObserverCandidate : null,
                packetWriter: WriteCorrectionPacket,
                packetBatchWriter: WriteCorrectionPacketBatch,
                supplementalBuild: EnableObserverSpatialFirstBuild ? DispatchObserverSpatialBuild : null,
                buildBatchPacketLimit: ReplicationBuildBatchPacketLimit);
        }

        private void DispatchObserverSpatialBuild(
            long tick,
            int workerIndex,
            int workerCount,
            NetworkReplicationPipeline.BuildSession session)
        {
            if (_observerChunkReplay == null || _trackedConnections.Count <= 0)
                return;

            long startTimestamp = Stopwatch.GetTimestamp();
            long sentPackets = 0;
            long builtConnections = 0;
            long visitedDeltas = 0;
            try
            {
                for (int trackedIndex = 0; trackedIndex < _trackedConnectionSnapshotCount; trackedIndex++)
                {
                    int connectionId = _trackedConnectionSnapshot[trackedIndex];
                    if (workerCount > 1 &&
                        ((int)((uint)connectionId % (uint)workerCount)) != workerIndex)
                    {
                        continue;
                    }

                    if (!_subscribedChunksByConnection.TryGetValue(connectionId, out HashSet<ChunkCoord>? subscribedChunks) ||
                        subscribedChunks.Count <= 0)
                    {
                        continue;
                    }

                    if (!session.TryGetConnectionStartTick(connectionId, out long startTick) || startTick > tick)
                        continue;

                    var state = new ObserverChunkBuildState
                    {
                        Service = this,
                        Session = session,
                        ConnectionId = connectionId,
                        SentPackets = 0,
                        VisitedDeltas = 0,
                        SentAny = false,
                    };
                    _observerChunkReplay.VisitRangeForChunks(
                        subscribedChunks,
                        startTick,
                        tick,
                        ref state,
                        static (ref ObserverChunkBuildState visitorState, in EncodedReplicationDelta delta) =>
                            visitorState.Service.AppendObserverChunkPacket(ref visitorState, in delta));

                    visitedDeltas += state.VisitedDeltas;
                    if (state.SentAny)
                        builtConnections++;
                    sentPackets += state.SentPackets;
                }
            }
            finally
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                System.Threading.Interlocked.Increment(ref _observerChunkBuildCalls);
                System.Threading.Interlocked.Add(ref _observerChunkBuildElapsedTicksTotal, elapsedTicks);
                long observedMax;
                while (elapsedTicks > (observedMax = _observerChunkBuildElapsedTicksMax) &&
                    System.Threading.Interlocked.CompareExchange(
                        ref _observerChunkBuildElapsedTicksMax,
                        elapsedTicks,
                        observedMax) != observedMax)
                {
                }
                System.Threading.Interlocked.Add(ref _observerChunkBuiltConnections, builtConnections);
                System.Threading.Interlocked.Add(ref _observerChunkSentPackets, sentPackets);
                System.Threading.Interlocked.Add(ref _observerChunkVisitedDeltas, visitedDeltas);
            }
        }

        private void AppendObserverChunkPacket(ref ObserverChunkBuildState state, in EncodedReplicationDelta delta)
        {
            if (delta.StreamId != ObserverCorrectionStreamId)
                return;

            state.VisitedDeltas++;

            if (TryGetOwnerConnectionId(delta.EntityId.Value, out int ownerConnectionId) &&
                ownerConnectionId == state.ConnectionId)
            {
                return;
            }

            if (state.Session.TryDispatchSelectedRecipient(state.ConnectionId, in delta))
            {
                state.SentPackets++;
                state.SentAny = true;
            }
        }

        private NetworkReplicationPipeline CreatePipeline(
            ReplicationReplayWindow? replayWindow,
            int parallelWorkerIndex,
            int parallelWorkerCount)
        {
            bool useRecipientSelector = EnableChunkAoi && EnableRecipientSelectorFastPath;
            ReplicationRecipientSelector? recipientSelector = null;
            if (useRecipientSelector)
            {
                if (parallelWorkerIndex >= 0 && parallelWorkerCount > 1)
                {
                    recipientSelector = (in EncodedReplicationDelta delta, Span<int> destination) =>
                        SelectRecipientsForDeltaForWorker(parallelWorkerIndex, parallelWorkerCount, in delta, destination);
                }
                else
                {
                    recipientSelector = SelectRecipientsForDelta;
                }
            }

            return new NetworkReplicationPipeline(
                maxReplayTicks: 64,
                budget: new ReplicationBudget(64 * 1024, 256, 256),
                overflowPolicy: new ReplicationOverflowPolicy(),
                encoder: EncodeCorrection,
                interestFilter: PassesInterestFilter,
                recipientSelector: recipientSelector,
                recipientSelectionKeySelector: useRecipientSelector ? SelectRecipientSelectionCacheKey : null,
                maxEncodedPayloadBytes: CorrectionPayloadBytes,
                assumePublishedCandidatesOrdered: true,
                assumeInterestFilterAlwaysTrue: !EnableChunkAoi,
                assumeRecipientSelectorDefinesInterest: useRecipientSelector,
                assumeRecipientSelectorEntityScoped: useRecipientSelector,
                assumeInterestFilterEntityScoped: EnableChunkAoi,
                replayWindow: replayWindow);
        }

        private int ResolveReplicationBuildWorkerCount()
        {
            if (MaxReplicationBuildWorkers == 0)
                return 1;
            if (MaxReplicationBuildWorkers == 1)
                return 1;
            if (MaxReplicationBuildWorkers == -1)
            {
                int autoWorkers = Math.Max(1, Environment.ProcessorCount - 1);
                return Math.Clamp(autoWorkers, 1, 8);
            }

            return Math.Clamp(MaxReplicationBuildWorkers, 1, 16);
        }

        private bool PassesInterestFilter(int connectionId, in EncodedReplicationDelta delta)
        {
            if (delta.StreamId == OwnerCorrectionStreamId)
            {
                return TryGetOwnerConnectionId(delta.EntityId.Value, out int ownerConnectionId) &&
                    ownerConnectionId == connectionId;
            }

            if (delta.StreamId == ObserverCorrectionStreamId &&
                TryGetOwnerConnectionId(delta.EntityId.Value, out int observerOwnerConnectionId) &&
                observerOwnerConnectionId == connectionId)
            {
                return false;
            }

            if (!EnableChunkAoi)
                return true;

            return _visibleByConnection.TryGetValue(connectionId, out HashSet<ulong>? visible) &&
                visible.Contains(delta.EntityId.Value);
        }

        private void RefreshChunkVisibility(long currentTick)
        {
            if (!EnableChunkAoi || _spatialChunks == null)
                return;

            if (!_spatialChunksInitialized)
            {
                _spatialChunks.Clear();
                _playerWorld.World.Query<PlayerState>((Entity entity, ref PlayerState state) =>
                {
                    NetworkEntityId entityId = Bridge.EntityMap.TryGetNetworkEntityId(entity, out NetworkEntityId existing)
                        ? existing
                        : Bridge.EntityMap.GetOrAssignNetworkEntityId(entity);

                    ChunkCoord chunk = _spatialChunks.GetChunkForGridPosition(
                        WorldToGrid(state.X),
                        WorldToGrid(state.Z));
                    _spatialChunks.UpsertEntity(entityId.Value, chunk);
                });
                _spatialChunksInitialized = true;
            }
            else
            {
                int dirtyCount = CopyDirtyPlayersEnsuringCapacity();
                for (int i = 0; i < dirtyCount; i++)
                {
                    Entity entity = _dirtyPlayerScratch[i];
                    if (!entity.IsValid() || !_playerWorld.World.IsAlive(entity))
                        continue;
                    if (!_playerWorld.World.TryGetComponent(entity, out PlayerState state))
                        continue;

                    NetworkEntityId entityId = Bridge.EntityMap.TryGetNetworkEntityId(entity, out NetworkEntityId existing)
                        ? existing
                        : Bridge.EntityMap.GetOrAssignNetworkEntityId(entity);

                    ChunkCoord chunk = _spatialChunks.GetChunkForGridPosition(
                        WorldToGrid(state.X),
                        WorldToGrid(state.Z));
                    _spatialChunks.UpsertEntity(entityId.Value, chunk);
                }
            }

            foreach (int connectionId in _trackedConnections)
            {
                if (!_sessions.TryGetSessionIdByConnection(connectionId, out long sessionId) ||
                    !_playerWorld.TryGetEntityForSession(sessionId, out Entity controlledEntity) ||
                    !_playerWorld.World.TryGetComponent(controlledEntity, out PlayerState controlledState))
                {
                    continue;
                }

                ChunkCoord center = _spatialChunks.GetChunkForGridPosition(
                    WorldToGrid(controlledState.X),
                    WorldToGrid(controlledState.Z));

                bool centerChanged = !_centerChunkByConnection.TryGetValue(connectionId, out ChunkCoord previousCenter) ||
                    previousCenter != center;
                _centerChunkByConnection[connectionId] = center;

                if (!_subscribedChunksByConnection.TryGetValue(connectionId, out HashSet<ChunkCoord>? subscribedChunks))
                {
                    subscribedChunks = new HashSet<ChunkCoord>();
                    _subscribedChunksByConnection[connectionId] = subscribedChunks;
                }

                if (!_subscribedChunkVersionsByConnection.TryGetValue(connectionId, out Dictionary<ChunkCoord, int>? subscribedChunkVersions))
                {
                    subscribedChunkVersions = new Dictionary<ChunkCoord, int>(16);
                    _subscribedChunkVersionsByConnection[connectionId] = subscribedChunkVersions;
                }

                if (centerChanged)
                {
                    _subscribedChunksScratch.Clear();
                    ChunkCoord.FillSquare(center, ChunkAoiRadius, _subscribedChunksScratch);
                    ReplaceSubscribedChunks(connectionId, subscribedChunks, _subscribedChunksScratch);
                }

                if (!_visibleByConnection.TryGetValue(connectionId, out HashSet<ulong>? previousVisible))
                {
                    previousVisible = new HashSet<ulong>();
                    _visibleByConnection[connectionId] = previousVisible;
                }

                bool subscribedChunkMembershipChanged = RefreshSubscribedChunkMembershipVersions(subscribedChunks, subscribedChunkVersions, _spatialChunks);
                if (centerChanged || subscribedChunkMembershipChanged)
                {
                    _desiredVisibleScratch.Clear();
                    foreach (ChunkCoord subscribedChunk in subscribedChunks)
                    {
                        if (!_spatialChunks.TryGetChunk(subscribedChunk, out SpatialChunk? spatialChunk))
                            continue;

                        foreach (ulong networkEntityId in spatialChunk.Members)
                            _desiredVisibleScratch.Add(networkEntityId);
                    }

                    DiffAndApplyVisibility(connectionId, previousVisible, _desiredVisibleScratch, currentTick);
                    ProcessPendingDespawns(connectionId, previousVisible, _desiredVisibleScratch, currentTick);
                }
                else
                {
                    _desiredVisibleScratch.Clear();
                    ProcessPendingDespawns(connectionId, previousVisible, _desiredVisibleScratch, currentTick);
                }
            }
        }

        private int CopyDirtyPlayersEnsuringCapacity()
        {
            while (true)
            {
                int copied = _playerWorld.World.CopyDirtyTo<PlayerState>(_dirtyPlayerScratch);
                if (copied < _dirtyPlayerScratch.Length)
                    return copied;

                int newSize = _dirtyPlayerScratch.Length << 1;
                if (newSize <= _dirtyPlayerScratch.Length)
                    newSize = _dirtyPlayerScratch.Length + 1024;
                _dirtyPlayerScratch = new Entity[newSize];
            }
        }

        private bool RefreshSubscribedChunkMembershipVersions(
            HashSet<ChunkCoord> subscribedChunks,
            Dictionary<ChunkCoord, int> subscribedChunkVersions,
            SpatialChunkIndex spatialChunks)
        {
            bool changed = false;
            foreach (ChunkCoord chunk in subscribedChunks)
            {
                int version = 0;
                if (spatialChunks.TryGetChunk(chunk, out SpatialChunk? spatialChunk))
                    version = spatialChunk.Version;

                if (!subscribedChunkVersions.TryGetValue(chunk, out int cachedVersion) || cachedVersion != version)
                {
                    subscribedChunkVersions[chunk] = version;
                    changed = true;
                }
            }

            if (subscribedChunkVersions.Count == subscribedChunks.Count)
                return changed;

            List<ChunkCoord> removed = _removedChunksScratch;
            removed.Clear();
            foreach ((ChunkCoord chunk, int _) in subscribedChunkVersions)
            {
                if (subscribedChunks.Contains(chunk))
                    continue;

                removed.Add(chunk);
            }

            if (removed.Count == 0)
                return changed;

            for (int i = 0; i < removed.Count; i++)
                subscribedChunkVersions.Remove(removed[i]);
            return true;
        }

        private void ReplaceSubscribedChunks(int connectionId, HashSet<ChunkCoord> current, HashSet<ChunkCoord> desired)
        {
            foreach (ChunkCoord chunk in current)
            {
                if (desired.Contains(chunk))
                    continue;

                RemoveConnectionFromChunkSubscriber(connectionId, chunk);
            }

            foreach (ChunkCoord chunk in desired)
            {
                if (current.Contains(chunk))
                    continue;

                AddConnectionToChunkSubscriber(connectionId, chunk);
            }

            current.Clear();
            foreach (ChunkCoord chunk in desired)
                current.Add(chunk);
        }

        private void AddConnectionToChunkSubscriber(int connectionId, ChunkCoord chunk)
        {
            if (!_chunkSubscribers.TryGetValue(chunk, out HashSet<int>? subscribers))
            {
                subscribers = new HashSet<int>();
                _chunkSubscribers[chunk] = subscribers;
            }

            subscribers.Add(connectionId);
        }

        private void RemoveConnectionFromChunkSubscribers(int connectionId, IEnumerable<ChunkCoord> chunks)
        {
            foreach (ChunkCoord chunk in chunks)
                RemoveConnectionFromChunkSubscriber(connectionId, chunk);
        }

        private void RemoveConnectionFromChunkSubscriber(int connectionId, ChunkCoord chunk)
        {
            if (!_chunkSubscribers.TryGetValue(chunk, out HashSet<int>? subscribers))
                return;

            subscribers.Remove(connectionId);
            if (subscribers.Count == 0)
                _chunkSubscribers.Remove(chunk);
        }

        private bool DiffAndApplyVisibility(
            int connectionId,
            HashSet<ulong> previousVisible,
            HashSet<ulong> desiredVisible,
            long currentTick)
        {
            bool changed = false;
            List<ulong> removed = _visibilityRemovedScratch;
            removed.Clear();

            foreach (ulong networkEntityId in previousVisible)
            {
                if (desiredVisible.Contains(networkEntityId))
                    continue;

                removed.Add(networkEntityId);
            }

            if (removed.Count > 0)
            {
                changed = true;
                Dictionary<ulong, long>? pending = null;
                if (AoiDespawnGraceTicks > 0)
                {
                    if (!_pendingDespawnTickByConnection.TryGetValue(connectionId, out pending))
                    {
                        pending = new Dictionary<ulong, long>();
                        _pendingDespawnTickByConnection[connectionId] = pending;
                    }
                }

                for (int i = 0; i < removed.Count; i++)
                {
                    ulong networkEntityId = removed[i];
                    if (AoiDespawnGraceTicks > 0 && pending != null)
                    {
                        if (!pending.ContainsKey(networkEntityId))
                            pending[networkEntityId] = currentTick + AoiDespawnGraceTicks;
                    }
                    else
                    {
                        previousVisible.Remove(networkEntityId);
                        SendDespawnToConnection(connectionId, new NetworkEntityId(networkEntityId));
                    }
                }
            }

            foreach (ulong networkEntityId in desiredVisible)
            {
                if (_pendingDespawnTickByConnection.TryGetValue(connectionId, out Dictionary<ulong, long>? pending))
                    pending.Remove(networkEntityId);
                if (previousVisible.Add(networkEntityId))
                {
                    _forcePublishNetworkIds.Add(networkEntityId);
                    changed = true;
                }
            }

            return changed;
        }

        private bool ProcessPendingDespawns(
            int connectionId,
            HashSet<ulong> previousVisible,
            HashSet<ulong> desiredVisible,
            long currentTick)
        {
            if (!_pendingDespawnTickByConnection.TryGetValue(connectionId, out Dictionary<ulong, long>? pending) ||
                pending.Count == 0)
            {
                return false;
            }

            List<ulong> due = _pendingDespawnsDueScratch;
            due.Clear();
            foreach ((ulong networkEntityId, long dueTick) in pending)
            {
                if (desiredVisible.Contains(networkEntityId))
                {
                    due.Add(networkEntityId);
                    continue;
                }

                if (currentTick < dueTick)
                    continue;

                due.Add(networkEntityId);
                previousVisible.Remove(networkEntityId);
                SendDespawnToConnection(connectionId, new NetworkEntityId(networkEntityId));
            }

            if (due.Count == 0)
                return false;

            for (int i = 0; i < due.Count; i++)
                pending.Remove(due[i]);

            return true;
        }

        private void SendDespawnToConnection(int connectionId, NetworkEntityId entityId)
        {
            byte[] payload = new byte[ServerDespawnCodec.PayloadBytes];
            if (!ServerDespawnCodec.TryEncode(payload, entityId, out int written))
                return;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId,
                ChannelType.ReliableOrdered,
                new ReadOnlyMemory<byte>(payload, 0, written)));
        }

        private static int WorldToGrid(float meters) =>
            (int)MathF.Floor(meters / SharedGameplayConstants.WorldUnitsPerGrid);

        private int SelectRecipientsForDelta(in EncodedReplicationDelta delta, Span<int> destination) =>
            SelectRecipientsForDeltaCore(in delta, destination, parallelWorkerIndex: -1, parallelWorkerCount: 1);

        private int SelectRecipientsForDeltaForWorker(
            int parallelWorkerIndex,
            int parallelWorkerCount,
            in EncodedReplicationDelta delta,
            Span<int> destination) =>
            SelectRecipientsForDeltaCore(in delta, destination, parallelWorkerIndex, parallelWorkerCount);

        private int SelectRecipientsForDeltaCore(
            in EncodedReplicationDelta delta,
            Span<int> destination,
            int parallelWorkerIndex,
            int parallelWorkerCount)
        {
            if (!EnableChunkAoi || !EnableRecipientSelectorFastPath || _spatialChunks == null)
                return -1;

            if (delta.StreamId == OwnerCorrectionStreamId)
            {
                if (!TryGetOwnerConnectionId(delta.EntityId.Value, out int ownerConnectionId))
                    return 0;
                if (!ConnectionMatchesReplicationBuildWorker(ownerConnectionId, parallelWorkerIndex, parallelWorkerCount))
                    return 0;
                if (destination.Length < 1)
                    return 1;
                destination[0] = ownerConnectionId;
                return 1;
            }

            if (!_spatialChunks.TryGetChunkCoord(delta.EntityId.Value, out ChunkCoord chunk))
                return -1;

            if (!_chunkSubscribers.TryGetValue(chunk, out HashSet<int>? subscribers) || subscribers.Count <= 0)
                return 0;

            bool observerOnlyStream = delta.StreamId == ObserverCorrectionStreamId;
            int ownerConnectionIdForObserver = 0;
            bool hasOwnerForObserver = observerOnlyStream && TryGetOwnerConnectionId(delta.EntityId.Value, out ownerConnectionIdForObserver);

            int required = 0;
            foreach (int connectionId in subscribers)
            {
                if (!ConnectionMatchesReplicationBuildWorker(connectionId, parallelWorkerIndex, parallelWorkerCount))
                    continue;
                if (hasOwnerForObserver && connectionId == ownerConnectionIdForObserver)
                    continue;
                required++;
            }
            if (destination.Length < required)
                return required;

            int denseCadenceTicks = GetDenseObserverCadenceTicks(subscribers.Count);
            int denseRecipientCap = GetDenseObserverRecipientCap(subscribers.Count, parallelWorkerIndex, parallelWorkerCount);
            int maxRecipients = denseRecipientCap > 0 ? Math.Min(denseRecipientCap, required) : required;
            int written = 0;
            if (denseRecipientCap > 0)
            {
                foreach (int connectionId in subscribers)
                {
                    if (!ConnectionMatchesReplicationBuildWorker(connectionId, parallelWorkerIndex, parallelWorkerCount))
                        continue;
                    if (hasOwnerForObserver && connectionId == ownerConnectionIdForObserver)
                        continue;
                    if (!PassesPriorityLodGate(connectionId, chunk, delta))
                        continue;
                    if (!IsDenseObserverPriorityRecipient(connectionId, ownerConnectionIdForObserver, chunk))
                        continue;
                    if (!PassesDenseObserverCadence(connectionId, delta.EntityId.Value, delta.Tick, denseCadenceTicks))
                        continue;

                    destination[written++] = connectionId;
                    if (written >= maxRecipients)
                        return written;
                }
            }

            foreach (int connectionId in subscribers)
            {
                if (written >= maxRecipients)
                    break;
                if (!ConnectionMatchesReplicationBuildWorker(connectionId, parallelWorkerIndex, parallelWorkerCount))
                    continue;
                if (hasOwnerForObserver && connectionId == ownerConnectionIdForObserver)
                    continue;
                if (ContainsRecipient(destination, written, connectionId))
                    continue;
                if (!PassesPriorityLodGate(connectionId, chunk, delta))
                    continue;
                if (!PassesDenseObserverCadence(connectionId, delta.EntityId.Value, delta.Tick, denseCadenceTicks))
                    continue;
                destination[written++] = connectionId;
            }

            return written;
        }

        private static bool ConnectionMatchesReplicationBuildWorker(
            int connectionId,
            int parallelWorkerIndex,
            int parallelWorkerCount)
        {
            if (parallelWorkerIndex < 0 || parallelWorkerCount <= 1)
                return true;

            uint unsignedConnectionId = unchecked((uint)connectionId);
            return (int)(unsignedConnectionId % (uint)parallelWorkerCount) == parallelWorkerIndex;
        }

        private ulong SelectRecipientSelectionCacheKey(in EncodedReplicationDelta delta)
        {
            if (!EnableChunkAoi || !EnableRecipientSelectorFastPath || _spatialChunks == null)
                return 0;

            if (delta.StreamId == OwnerCorrectionStreamId || delta.StreamId == ObserverCorrectionStreamId)
                return MakeEntityScopedSelectorKey(delta.EntityId.Value, delta.StreamId);

            if (EnableDenseObserverCadence || EnablePriorityLod)
                return MakeEntityScopedSelectorKey(delta.EntityId.Value, delta.StreamId);

            if (!_spatialChunks.TryGetChunkCoord(delta.EntityId.Value, out ChunkCoord chunk))
                return 0;

            if (!EnableRecipientSelectorChunkGroupCacheKey)
                return MakeEntityScopedSelectorKey(delta.EntityId.Value, delta.StreamId);

            unchecked
            {
                ulong x = (uint)chunk.X;
                ulong z = (uint)chunk.Z;
                ulong key = (x << 32) | z;
                key ^= ((ulong)delta.StreamId << 48);
                return key == 0 ? 1UL : key;
            }
        }

        private static ulong MakeEntityScopedSelectorKey(ulong entityId, ushort streamId)
        {
            unchecked
            {
                uint entityLow = (uint)entityId;
                ulong key = ((ulong)entityLow << 16) | streamId;
                return key == 0 ? 1UL : key;
            }
        }

        private void WriteCorrectionPacketBatch(INetworkTransport transport, ReplicationPacket[] packets, int count)
        {
            if (count <= 0)
                return;

            if (EnableCorrectionBatching)
            {
                for (int i = 0; i < count; i++)
                {
                    ref readonly ReplicationPacket packet = ref packets[i];
                    if (packet.StreamId == OwnerCorrectionStreamId)
                    {
                        _ownerCorrectionsSent++;
                        _ownerCorrectionPayloadBytes += packet.Payload.Length;
                    }
                    else if (packet.StreamId == ObserverCorrectionStreamId)
                    {
                        _observerCorrectionsSent++;
                        _observerCorrectionPayloadBytes += packet.Payload.Length;
                    }
                    CorrectionBatchAccumulator accumulator = GetOrCreateCorrectionBatchAccumulator(packet.ConnectionId);
                    if (accumulator.Count == 0)
                    {
                        if (!TryGetReplicationPayloadForAccumulatorSingle(accumulator, in packet, out ReadOnlyMemory<byte> payload, out byte[]? rentedPayloadBuffer))
                        {
                            FlushSingleAccumulator(transport, accumulator);
                            WriteCorrectionPacket(transport, in packet);
                            continue;
                        }

                        accumulator.SinglePayload = payload;
                        accumulator.SinglePayloadRentedBuffer = rentedPayloadBuffer;
                        accumulator.Count = 1;
                        continue;
                    }

                    if (accumulator.Count == 1)
                    {
                        if (!CanAppendCorrectionBatchPayload(accumulator.Buffer, 1, packet.Payload.Length))
                        {
                            FlushAndResetAccumulator(transport, accumulator);
                            if (!TryGetReplicationPayloadForRecipient(in packet, out ReadOnlyMemory<byte> payload, out byte[]? rentedPayloadBuffer))
                            {
                                WriteCorrectionPacket(transport, in packet);
                                continue;
                            }

                            accumulator.SinglePayload = payload;
                            accumulator.SinglePayloadRentedBuffer = rentedPayloadBuffer;
                            accumulator.Count = 1;
                            continue;
                        }

                        BeginCorrectionBatch(accumulator.Buffer);
                        AppendCorrectionBatchPayload(accumulator.Buffer, accumulator.SinglePayload.Span);
                        ReturnTemporaryPayload(accumulator.SinglePayloadRentedBuffer);
                        if (!TryAppendReplicationPayloadForBatch(accumulator.Buffer, in packet))
                        {
                            FlushAndResetAccumulator(transport, accumulator);
                            WriteCorrectionPacket(transport, in packet);
                            continue;
                        }
                        accumulator.SinglePayload = default;
                        accumulator.SinglePayloadRentedBuffer = null;
                        accumulator.Count = 2;
                        continue;
                    }

                    if (!CanAppendCorrectionBatchPayload(accumulator.Buffer, accumulator.Count, packet.Payload.Length))
                    {
                        FlushAndResetAccumulator(transport, accumulator);
                        if (!TryGetReplicationPayloadForRecipient(in packet, out ReadOnlyMemory<byte> payload, out byte[]? rentedPayloadBuffer))
                        {
                            WriteCorrectionPacket(transport, in packet);
                            continue;
                        }

                        accumulator.SinglePayload = payload;
                        accumulator.SinglePayloadRentedBuffer = rentedPayloadBuffer;
                        accumulator.Count = 1;
                        continue;
                    }

                    if (!TryAppendReplicationPayloadForBatch(accumulator.Buffer, in packet))
                    {
                        FlushAndResetAccumulator(transport, accumulator);
                        WriteCorrectionPacket(transport, in packet);
                        continue;
                    }
                    accumulator.Count++;
                }

                for (int i = 0; i < _correctionBatchConnectionIds.Count; i++)
                {
                    int connectionId = _correctionBatchConnectionIds[i];
                    if (_correctionBatchByConnection.TryGetValue(connectionId, out CorrectionBatchAccumulator? accumulator))
                        FlushSingleAccumulator(transport, accumulator);
                }

                return;
            }

            TcpFramedNetworkTransport? tcpTransport = transport as TcpFramedNetworkTransport;
            for (int i = 0; i < count; )
            {
                ref readonly ReplicationPacket first = ref packets[i];
                if (first.StreamId == OwnerCorrectionStreamId)
                {
                    _ownerCorrectionsSent++;
                    _ownerCorrectionPayloadBytes += first.Payload.Length;
                }
                else if (first.StreamId == ObserverCorrectionStreamId)
                {
                    _observerCorrectionsSent++;
                    _observerCorrectionPayloadBytes += first.Payload.Length;
                }
                if (tcpTransport == null || first.StreamId != ObserverCorrectionStreamId)
                {
                    WriteCorrectionPacket(transport, in first);
                    i++;
                    continue;
                }

                int j = i + 1;
                while (j < count &&
                       packets[j].StreamId == ObserverCorrectionStreamId &&
                       packets[j].EntityId == first.EntityId &&
                       packets[j].Tick == first.Tick)
                {
                    j++;
                }

                int runLength = j - i;
                if (runLength == 1)
                {
                    WriteCorrectionPacket(transport, in first);
                    i++;
                    continue;
                }

                if (_multicastConnectionIdScratch.Length < runLength)
                    _multicastConnectionIdScratch = new int[runLength];

                for (int k = 0; k < runLength; k++)
                    _multicastConnectionIdScratch[k] = packets[i + k].ConnectionId;

                if (!tcpTransport.TryEnqueueOutboundBorrowedMulticast(
                    _multicastConnectionIdScratch.AsSpan(0, runLength),
                    ChannelType.UnreliableSequenced,
                    first.Payload))
                {
                    for (int k = 0; k < runLength; k++)
                        WriteCorrectionPacket(transport, in packets[i + k]);
                }

                i = j;
            }
        }

        private void WriteCorrectionPacket(INetworkTransport transport, in ReplicationPacket packet)
        {
            TcpFramedNetworkTransport? tcpTransport = transport as TcpFramedNetworkTransport;
            if (packet.StreamId == ObserverCorrectionStreamId)
            {
                if (tcpTransport != null &&
                    tcpTransport.TryEnqueueOutboundBorrowed(
                        packet.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        packet.Payload))
                {
                    return;
                }

                transport.EnqueueOutbound(new OutboundDatagram(
                    packet.ConnectionId,
                    ChannelType.UnreliableSequenced,
                    packet.Payload));
                return;
            }

            bool ownerPacket = IsOwnerRecipientPacket(in packet);

            if (ownerPacket &&
                TryGetOwnerPatch(in packet, out ushort ownerAck))
            {
                if (tcpTransport != null &&
                    tcpTransport.TryEnqueueOutboundBorrowedPatchedUInt16(
                        packet.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        packet.Payload,
                        patchOffset: EntityFloat3StateYawAckOwnerTick32Codec.AuthoritativeSequenceOffset,
                        patchValue: ownerAck))
                {
                    return;
                }

                int payloadLength = packet.Payload.Length;
                byte[] rented = ArrayPool<byte>.Shared.Rent(payloadLength);
                try
                {
                    packet.Payload.Span.CopyTo(rented.AsSpan(0, payloadLength));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        rented.AsSpan(
                            EntityFloat3StateYawAckOwnerTick32Codec.AuthoritativeSequenceOffset,
                            sizeof(ushort)),
                        ownerAck);
                    transport.EnqueueOutbound(new OutboundDatagram(
                        packet.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        new ReadOnlyMemory<byte>(rented, 0, payloadLength)));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
                return;
            }

            if (!ownerPacket &&
                TryGetObserverPayloadForFanout(in packet, out ReadOnlyMemory<byte> observerPayload))
            {
                if (tcpTransport != null &&
                    tcpTransport.TryEnqueueOutboundBorrowed(
                        packet.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        observerPayload))
                {
                    return;
                }

                transport.EnqueueOutbound(new OutboundDatagram(
                    packet.ConnectionId,
                    ChannelType.UnreliableSequenced,
                    observerPayload));
                return;
            }

            if (tcpTransport != null &&
                tcpTransport.TryEnqueueOutboundBorrowed(
                    packet.ConnectionId,
                    ChannelType.UnreliableSequenced,
                    packet.Payload))
            {
                return;
            }

            transport.EnqueueOutbound(new OutboundDatagram(
                packet.ConnectionId,
                ChannelType.UnreliableSequenced,
                packet.Payload));
        }

        private bool TryGetOwnerPatch(in ReplicationPacket packet, out ushort ownerAck)
        {
            ownerAck = 0;
            if (packet.Payload.Length < EntityFloat3StateYawAckOwnerTick32Codec.PayloadBytes)
                return false;

            int ownerConnectionId = BinaryPrimitives.ReadUInt16LittleEndian(
                packet.Payload.Span.Slice(
                    EntityFloat3StateYawAckOwnerTick32Codec.OwnerConnectionIdOffset,
                    sizeof(ushort)));
            if (ownerConnectionId != packet.ConnectionId)
                return false;

            return _playerWorld.TryGetLastInputAck(ownerConnectionId, out ownerAck);
        }

        private static bool IsOwnerRecipientPacket(in ReplicationPacket packet)
        {
            if (packet.Payload.Length < EntityFloat3StateYawAckOwnerTick32Codec.PayloadBytes)
                return false;

            int ownerConnectionId = BinaryPrimitives.ReadUInt16LittleEndian(
                packet.Payload.Span.Slice(
                    EntityFloat3StateYawAckOwnerTick32Codec.OwnerConnectionIdOffset,
                    sizeof(ushort)));
            return ownerConnectionId == packet.ConnectionId;
        }

        private bool TryGetReplicationPayloadForRecipient(in ReplicationPacket packet, out ReadOnlyMemory<byte> payload, out byte[]? rentedPayloadBuffer)
        {
            payload = default;
            rentedPayloadBuffer = null;
            if (packet.StreamId == ObserverCorrectionStreamId)
            {
                payload = packet.Payload;
                return true;
            }

            bool ownerPacket = IsOwnerRecipientPacket(in packet);
            if (ownerPacket && TryGetOwnerPatch(in packet, out ushort ownerAck))
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                int payloadLength = packet.Payload.Length;
                byte[] patched = ArrayPool<byte>.Shared.Rent(payloadLength);
                _ownerPayloadRents++;
                _ownerPayloadRentBytes += payloadLength;
                packet.Payload.Span.CopyTo(patched.AsSpan(0, payloadLength));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    patched.AsSpan(
                        EntityFloat3StateYawAckOwnerTick32Codec.AuthoritativeSequenceOffset,
                        sizeof(ushort)),
                    ownerAck);
                payload = new ReadOnlyMemory<byte>(patched, 0, payloadLength);
                rentedPayloadBuffer = patched;
                RecordAllocatedBytes(
                    allocatedBefore,
                    ref _ownerTransformCalls,
                    ref _ownerTransformAllocatedBytesTotal,
                    ref _ownerTransformAllocatedBytesMax);
                return true;
            }

            if (!ownerPacket && TryGetObserverPayloadForFanout(in packet, out ReadOnlyMemory<byte> observerPayload))
            {
                payload = observerPayload;
                return true;
            }

            payload = packet.Payload;
            return true;
        }

        private bool TryGetReplicationPayloadForAccumulatorSingle(
            CorrectionBatchAccumulator accumulator,
            in ReplicationPacket packet,
            out ReadOnlyMemory<byte> payload,
            out byte[]? rentedPayloadBuffer)
        {
            payload = default;
            rentedPayloadBuffer = null;

            if (TryGetOwnerPatch(in packet, out ushort ownerAck))
            {
                int payloadLength = packet.Payload.Length;
                if (payloadLength == accumulator.OwnerSinglePayloadScratch.Length)
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    byte[] scratch = accumulator.OwnerSinglePayloadScratch;
                    packet.Payload.Span.CopyTo(scratch);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        scratch.AsSpan(
                            EntityFloat3StateYawAckOwnerTick32Codec.AuthoritativeSequenceOffset,
                            sizeof(ushort)),
                        ownerAck);
                    payload = new ReadOnlyMemory<byte>(scratch, 0, payloadLength);
                    RecordAllocatedBytes(
                        allocatedBefore,
                        ref _ownerTransformCalls,
                        ref _ownerTransformAllocatedBytesTotal,
                        ref _ownerTransformAllocatedBytesMax);
                    return true;
                }
            }

            return TryGetReplicationPayloadForRecipient(in packet, out payload, out rentedPayloadBuffer);
        }

        private bool TryAppendReplicationPayloadForBatch(ArrayBufferWriter<byte> writer, in ReplicationPacket packet)
        {
            if (TryGetOwnerPatch(in packet, out ushort ownerAck))
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                int payloadLength = packet.Payload.Length;
                Span<byte> patched = payloadLength <= 256
                    ? stackalloc byte[payloadLength]
                    : new byte[payloadLength];
                packet.Payload.Span.CopyTo(patched);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    patched.Slice(
                        EntityFloat3StateYawAckOwnerTick32Codec.AuthoritativeSequenceOffset,
                        sizeof(ushort)),
                    ownerAck);
                AppendCorrectionBatchPayload(writer, patched);
                RecordAllocatedBytes(
                    allocatedBefore,
                    ref _ownerTransformCalls,
                    ref _ownerTransformAllocatedBytesTotal,
                    ref _ownerTransformAllocatedBytesMax);
                return true;
            }

            if (!TryGetReplicationPayloadForRecipient(in packet, out ReadOnlyMemory<byte> payload, out byte[]? rentedPayloadBuffer))
                return false;

            AppendCorrectionBatchPayload(writer, payload.Span);
            ReturnTemporaryPayload(rentedPayloadBuffer);
            return true;
        }

        private bool TryGetObserverPayloadForFanout(in ReplicationPacket packet, out ReadOnlyMemory<byte> payload)
        {
            payload = default;
            if (packet.Payload.Length < EntityFloat3StateYawAckOwnerTick32Codec.PayloadBytes)
                return false;
            if (!MemoryMarshal.TryGetArray(packet.Payload, out ArraySegment<byte> slice) || slice.Array == null)
                return false;

            var key = new PayloadSliceKey(slice.Array, slice.Offset, slice.Count);
            if (_observerPayloadCache.TryGetValue(key, out byte[]? cached))
            {
                payload = new ReadOnlyMemory<byte>(cached, 0, EntityFloat3StateYawTick32Codec.PayloadBytes);
                return true;
            }

            if (!EntityFloat3StateYawAckOwnerTick32Codec.TryDecode(
                    packet.Payload.Span,
                    out NetworkEntityId entityId,
                    out int valueX,
                    out int valueZ,
                    out ushort quantizedYaw,
                    out ushort actorDefinitionId,
                    out long serverTick,
                    out _,
                    out _))
            {
                return false;
            }

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] observerPayload = ArrayPool<byte>.Shared.Rent(EntityFloat3StateYawTick32Codec.PayloadBytes);
            _observerPayloadMisses++;
            _observerPayloadRentBytes += EntityFloat3StateYawTick32Codec.PayloadBytes;
            if (!EntityFloat3StateYawTick32Codec.TryEncode(
                    observerPayload,
                    entityId,
                    valueX,
                    valueZ,
                    quantizedYaw,
                    actorDefinitionId,
                    serverTick,
                    out _))
            {
                return false;
            }

            _observerPayloadPendingReturn.Add(observerPayload);
            _observerPayloadCache[key] = observerPayload;
            payload = new ReadOnlyMemory<byte>(observerPayload, 0, EntityFloat3StateYawTick32Codec.PayloadBytes);
            RecordAllocatedBytes(
                allocatedBefore,
                ref _observerTransformCalls,
                ref _observerTransformAllocatedBytesTotal,
                ref _observerTransformAllocatedBytesMax);
            return true;
        }

        private void ReturnObserverPayloadBuffers()
        {
            if (_observerPayloadPendingReturn.Count == 0)
                return;

            for (int i = 0; i < _observerPayloadPendingReturn.Count; i++)
                ArrayPool<byte>.Shared.Return(_observerPayloadPendingReturn[i]);

            _observerPayloadPendingReturn.Clear();
        }

        private void ReturnCorrectionPayloadBuffers()
        {
            if (_correctionPayloadPendingReturn.Count == 0)
                return;

            for (int i = 0; i < _correctionPayloadPendingReturn.Count; i++)
                ArrayPool<byte>.Shared.Return(_correctionPayloadPendingReturn[i]);

            _correctionPayloadPendingReturn.Clear();
        }

        private bool ShouldPublishServerCorrection(NetworkEntityId networkEntityId, in PlayerState state, long tick)
        {
            if (_forcePublishNetworkIds.Remove(networkEntityId.Value))
            {
                _publishedCorrectionByNetworkId[networkEntityId.Value] = new ServerPublishedCorrectionState(
                    state.X,
                    state.Y,
                    state.Z,
                    state.YawRadians,
                    tick,
                    state.OwnerConnectionId);
                return true;
            }

            if (!EnableCorrectionDeadband)
            {
                _publishedCorrectionByNetworkId[networkEntityId.Value] = new ServerPublishedCorrectionState(
                    state.X,
                    state.Y,
                    state.Z,
                    state.YawRadians,
                    tick,
                    state.OwnerConnectionId);
                return true;
            }

            float deadbandMeters = CorrectionDeadbandUnits * SharedGameplayConstants.WorldUnitsPerGrid;
            if (!_publishedCorrectionByNetworkId.TryGetValue(networkEntityId.Value, out ServerPublishedCorrectionState lastPublished))
            {
                _publishedCorrectionByNetworkId[networkEntityId.Value] = new ServerPublishedCorrectionState(
                    state.X,
                    state.Y,
                    state.Z,
                    state.YawRadians,
                    tick,
                    state.OwnerConnectionId);
                return true;
            }

            bool ownerChanged = lastPublished.OwnerConnectionId != state.OwnerConnectionId;
            float dx = Math.Abs(state.X - lastPublished.X);
            float dy = Math.Abs(state.Y - lastPublished.Y);
            float dz = Math.Abs(state.Z - lastPublished.Z);
            float yawDelta = Math.Abs(WrapAngleRadians(state.YawRadians - lastPublished.YawRadians));
            long ageTicks = tick - lastPublished.Tick;
            bool exceedsDeadband =
                dx > deadbandMeters ||
                dy > MovementProtocolConstants.PositionChangeEpsilonMeters ||
                dz > deadbandMeters ||
                yawDelta > MovementProtocolConstants.YawChangeEpsilonRadians;
            bool forcedByAge = ageTicks >= CorrectionMaxSilentTicks;

            if (!ownerChanged && !exceedsDeadband && !forcedByAge)
                return false;

            _publishedCorrectionByNetworkId[networkEntityId.Value] = new ServerPublishedCorrectionState(
                state.X,
                state.Y,
                state.Z,
                state.YawRadians,
                tick,
                state.OwnerConnectionId);
            return true;
        }

        private void MarkCorrectionAgeExpiredEntitiesDirty(long tick)
        {
            if (!EnableCorrectionDeadband || CorrectionMaxSilentTicks <= 0)
                return;

            _playerWorld.World.Query<PlayerState>((Entity entity, ref PlayerState state) =>
            {
                if (!Bridge.EntityMap.TryGetNetworkEntityId(entity, out NetworkEntityId entityId))
                    return;
                if (!_publishedCorrectionByNetworkId.TryGetValue(entityId.Value, out ServerPublishedCorrectionState published))
                    return;
                if ((tick - published.Tick) < CorrectionMaxSilentTicks)
                    return;

                _forcePublishNetworkIds.Add(entityId.Value);
                _playerWorld.World.MarkDirty<PlayerState>(entity);
            });
        }

        private void ForcePublishAllKnownPlayers()
        {
            int playerCount = _playerWorld.PlayerCount;
            if (playerCount > 0 && _publishedCorrectionByNetworkId.Count >= playerCount)
            {
                foreach (ulong networkEntityId in _publishedCorrectionByNetworkId.Keys)
                    _forcePublishNetworkIds.Add(networkEntityId);
                return;
            }

            _playerWorld.World.Query<PlayerState>((Entity entity, ref PlayerState state) =>
            {
                if (state.OwnerConnectionId <= 0)
                    return;

                NetworkEntityId entityId = Bridge.EntityMap.GetOrAssignNetworkEntityId(entity);
                _forcePublishNetworkIds.Add(entityId.Value);
            });
        }

        private bool TryGetOwnerConnectionId(ulong networkEntityId, out int ownerConnectionId)
        {
            ownerConnectionId = 0;
            if (!Bridge.EntityMap.TryGetEntity(new NetworkEntityId(networkEntityId), out Entity entity))
                return false;
            if (!_playerWorld.World.TryGetComponent(entity, out PlayerState state))
                return false;

            ownerConnectionId = state.OwnerConnectionId;
            return ownerConnectionId > 0;
        }

        private int GetDenseObserverCadenceTicks(int subscriberCount)
        {
            if (!EnableDenseObserverCadence || subscriberCount < DenseObserverCadenceStartSubscribers)
                return 1;

            int cadence = 2 + ((subscriberCount - DenseObserverCadenceStartSubscribers) / DenseObserverCadenceStepSubscribers);
            if (cadence > DenseObserverCadenceMaxTicks)
                cadence = DenseObserverCadenceMaxTicks;
            return cadence < 1 ? 1 : cadence;
        }

        private int GetDenseObserverRecipientCap(int subscriberCount, int parallelWorkerIndex, int parallelWorkerCount)
        {
            if (!EnableDenseObserverRecipientCap || subscriberCount < DenseObserverRecipientCapStartSubscribers)
                return 0;

            int cap = DenseObserverRecipientCapMaxRecipients;
            if (cap >= subscriberCount)
                return 0;

            // Distribute cap evenly across parallel workers so the total recipients
            // across all workers equals cap, not cap*workerCount.
            // Worker i gets floor(cap/N) + 1 if i < cap%N, else floor(cap/N).
            if (parallelWorkerCount > 1 && parallelWorkerIndex >= 0)
            {
                int perWorker = cap / parallelWorkerCount;
                int remainder = cap % parallelWorkerCount;
                cap = perWorker + (parallelWorkerIndex < remainder ? 1 : 0);
            }

            return cap;
        }

        private bool IsDenseObserverPriorityRecipient(int connectionId, int ownerConnectionId, in ChunkCoord entityChunk)
        {
            if (connectionId == ownerConnectionId)
                return true;
            if (!_centerChunkByConnection.TryGetValue(connectionId, out ChunkCoord centerChunk))
                return false;

            int dx = centerChunk.X - entityChunk.X;
            if (dx < 0)
                dx = -dx;
            int dz = centerChunk.Z - entityChunk.Z;
            if (dz < 0)
                dz = -dz;
            return Math.Max(dx, dz) <= DenseObserverRecipientPriorityChunkRadius;
        }

        private bool PassesDenseObserverCadence(int connectionId, ulong entityId, long tick, int cadenceTicks)
        {
            if (cadenceTicks <= 1)
                return true;

            int phase = ComputeObserverRecipientPhase(entityId, connectionId, cadenceTicks);
            long tickMod = ((tick % cadenceTicks) + cadenceTicks) % cadenceTicks;
            return tickMod == phase;
        }

        private bool PassesPriorityLodGate(int connectionId, in ChunkCoord entityChunk, in EncodedReplicationDelta delta)
        {
            if (!EnablePriorityLod)
                return true;
            if (!_centerChunkByConnection.TryGetValue(connectionId, out ChunkCoord centerChunk))
                return true;

            int dx = Math.Abs(centerChunk.X - entityChunk.X);
            int dz = Math.Abs(centerChunk.Z - entityChunk.Z);
            int ring = Math.Max(dx, dz);

            // Ring 0 (same chunk): every tick — always passes, no phase check needed.
            // Ring 1 (1–Ring1Max chunks): cadence 2 (~31 Hz at 62 Hz tick rate).
            // Ring 2 (Ring1Max+1+ chunks): cadence 3 (~21 Hz at 62 Hz tick rate).
            int cadenceTicks;
            if (ring <= 0)
                cadenceTicks = PriorityLodNearCadenceTicks;
            else if (ring <= PriorityLodRing1MaxChunks)
                cadenceTicks = PriorityLodOuterCadenceTicks;
            else
                cadenceTicks = PriorityLodFarCadenceTicks;

            Interlocked.Increment(ref _lodChecks);
            if (ring == 0)
                Interlocked.Increment(ref _lodNearChecks);
            else if (ring <= PriorityLodRing1MaxChunks)
                Interlocked.Increment(ref _lodRing1Checks);
            else
                Interlocked.Increment(ref _lodRing2Checks);

            bool passes = cadenceTicks <= 1;
            if (!passes)
            {
                int phase = ComputePriorityLodPhase(connectionId, delta.EntityId.Value, cadenceTicks);
                long tickMod = ((delta.Tick % cadenceTicks) + cadenceTicks) % cadenceTicks;
                passes = tickMod == phase;
            }

            if (passes)
            {
                Interlocked.Increment(ref _lodSent);
                if (ring == 0)
                    Interlocked.Increment(ref _lodNearSent);
                else if (ring <= PriorityLodRing1MaxChunks)
                    Interlocked.Increment(ref _lodRing1Sent);
                else
                    Interlocked.Increment(ref _lodRing2Sent);
            }
            else
            {
                Interlocked.Increment(ref _lodGated);
            }

            return passes;
        }

        private static int ComputeObserverRecipientPhase(ulong entityId, int connectionId, int cadenceTicks)
        {
            if (cadenceTicks <= 1)
                return 0;

            unchecked
            {
                ulong hash = entityId * 11400714819323198485UL;
                hash ^= (uint)connectionId * 7046029254386353131UL;
                return (int)(hash % (ulong)cadenceTicks);
            }
        }

        private static int ComputePriorityLodPhase(int connectionId, ulong entityNetworkId, int cadenceTicks)
        {
            if (cadenceTicks <= 1)
                return 0;

            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)connectionId) * 16777619u;
                hash = (hash ^ (uint)entityNetworkId) * 16777619u;
                hash = (hash ^ (uint)(entityNetworkId >> 32)) * 16777619u;
                return (int)(hash % (uint)cadenceTicks);
            }
        }

        private static bool ContainsRecipient(Span<int> destination, int count, int connectionId)
        {
            for (int i = 0; i < count; i++)
            {
                if (destination[i] == connectionId)
                    return true;
            }

            return false;
        }

        private CorrectionBatchAccumulator GetOrCreateCorrectionBatchAccumulator(int connectionId)
        {
            if (_correctionBatchByConnection.TryGetValue(connectionId, out CorrectionBatchAccumulator? existing))
                return existing;

            CorrectionBatchAccumulator created = _correctionBatchAccumulatorPool.TryPop(out CorrectionBatchAccumulator? pooled)
                ? pooled
                : new CorrectionBatchAccumulator();
            created.ConnectionId = connectionId;
            _correctionBatchByConnection.Add(connectionId, created);
            _correctionBatchConnectionIds.Add(connectionId);
            return created;
        }

        private void FlushSingleAccumulator(INetworkTransport transport, CorrectionBatchAccumulator accumulator)
        {
            if (accumulator.Count <= 0)
                return;

            if (accumulator.Count == 1)
            {
                byte[]? rentedPayloadBuffer = accumulator.SinglePayloadRentedBuffer;
                if (transport is TcpFramedNetworkTransport tcpTransport &&
                    tcpTransport.TryEnqueueOutboundBorrowed(
                        accumulator.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        accumulator.SinglePayload))
                {
                    if (rentedPayloadBuffer != null)
                    {
                        _correctionPayloadPendingReturn.Add(rentedPayloadBuffer);
                        accumulator.SinglePayloadRentedBuffer = null;
                    }
                }
                else
                {
                    transport.EnqueueOutbound(new OutboundDatagram(
                        accumulator.ConnectionId,
                        ChannelType.UnreliableSequenced,
                        accumulator.SinglePayload));
                    ReturnTemporaryPayload(rentedPayloadBuffer);
                    accumulator.SinglePayloadRentedBuffer = null;
                }
                return;
            }

            TryEnqueueCorrectionBatchPayload(transport, accumulator, accumulator.Buffer, accumulator.Count);
        }

        private void FlushAndResetAccumulator(INetworkTransport transport, CorrectionBatchAccumulator accumulator)
        {
            FlushSingleAccumulator(transport, accumulator);
            accumulator.Reset();
        }

        private void ResetCorrectionBatchAccumulators()
        {
            for (int i = 0; i < _correctionBatchConnectionIds.Count; i++)
            {
                int connectionId = _correctionBatchConnectionIds[i];
                if (_correctionBatchByConnection.TryGetValue(connectionId, out CorrectionBatchAccumulator? accumulator))
                    accumulator.Reset();
            }
        }

        private void ResetReusableCorrectionBatchPayloadBuffers()
        {
            for (int i = 0; i < _correctionBatchConnectionIds.Count; i++)
            {
                int connectionId = _correctionBatchConnectionIds[i];
                if (_correctionBatchByConnection.TryGetValue(connectionId, out CorrectionBatchAccumulator? accumulator))
                    accumulator.ResetReusableBatchPayloadBuffers();
            }
        }

        private static void BeginCorrectionBatch(ArrayBufferWriter<byte> writer)
        {
            writer.Clear();
            Span<byte> header = writer.GetSpan(CorrectionBatchHeaderBytes);
            header[0] = ServerCorrectionBatchMessageType;
            header[1] = 0;
            header[2] = 0;
            writer.Advance(CorrectionBatchHeaderBytes);
        }

        private static void AppendCorrectionBatchPayload(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = writer.GetSpan(CorrectionBatchEntryHeaderBytes);
            BinaryPrimitives.WriteUInt16LittleEndian(header, checked((ushort)payload.Length));
            writer.Advance(CorrectionBatchEntryHeaderBytes);
            payload.CopyTo(writer.GetSpan(payload.Length));
            writer.Advance(payload.Length);
        }

        private static bool CanAppendCorrectionBatchPayload(ArrayBufferWriter<byte> writer, int existingEntries, int payloadLength)
        {
            if (existingEntries >= MaxCorrectionBatchEntries)
                return false;

            int nextWrittenCount = writer.WrittenCount + CorrectionBatchEntryHeaderBytes + payloadLength;
            return nextWrittenCount <= MaxCorrectionBatchPayloadBytes;
        }

        private bool TryEnqueueCorrectionBatchPayload(
            INetworkTransport transport,
            CorrectionBatchAccumulator accumulator,
            ArrayBufferWriter<byte> writer,
            int entryCount)
        {
            if (writer.WrittenCount <= CorrectionBatchHeaderBytes)
                return false;
            if (entryCount <= 1 || entryCount > ushort.MaxValue)
                return false;

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int payloadLength = writer.WrittenCount;
            byte[] payload = accumulator.AcquireBatchPayloadBuffer(payloadLength);
            writer.WrittenSpan.CopyTo(payload.AsSpan(0, payloadLength));
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(1, 2),
                checked((ushort)entryCount));
            ReadOnlyMemory<byte> payloadMemory = new ReadOnlyMemory<byte>(payload, 0, payloadLength);
            if (transport is TcpFramedNetworkTransport tcpTransport &&
                tcpTransport.TryEnqueueOutboundBorrowed(
                    accumulator.ConnectionId,
                    ChannelType.UnreliableSequenced,
                    payloadMemory))
            {
                RecordAllocatedBytes(
                    allocatedBefore,
                    ref _batchFinalizeCalls,
                    ref _batchFinalizeAllocatedBytesTotal,
                    ref _batchFinalizeAllocatedBytesMax);
                return true;
            }

            transport.EnqueueOutbound(new OutboundDatagram(
                accumulator.ConnectionId,
                ChannelType.UnreliableSequenced,
                payloadMemory));
            RecordAllocatedBytes(
                allocatedBefore,
                ref _batchFinalizeCalls,
                ref _batchFinalizeAllocatedBytesTotal,
                ref _batchFinalizeAllocatedBytesMax);
            return true;
        }

        private static void ReturnTemporaryPayload(byte[]? rentedPayloadBuffer)
        {
            if (rentedPayloadBuffer != null)
                ArrayPool<byte>.Shared.Return(rentedPayloadBuffer);
        }

        private static float WrapAngleRadians(float radians)
        {
            while (radians > MathF.PI)
                radians -= MathF.Tau;
            while (radians < -MathF.PI)
                radians += MathF.Tau;
            return radians;
        }

        private sealed class CorrectionBatchAccumulator
        {
            private const int InitialReusableBatchPayloadBytes = 1024;
            private readonly List<byte[]> _reusableBatchPayloadBuffers = new List<byte[]>(2);
            private int _reusableBatchPayloadBufferCountInUse;

            public int ConnectionId { get; set; }
            public int Count { get; set; }
            public ReadOnlyMemory<byte> SinglePayload { get; set; }
            public byte[]? SinglePayloadRentedBuffer { get; set; }
            public byte[] OwnerSinglePayloadScratch { get; } = new byte[CorrectionPayloadBytes];
            public ArrayBufferWriter<byte> Buffer { get; } = new ArrayBufferWriter<byte>(512);

            public byte[] AcquireBatchPayloadBuffer(int requiredLength)
            {
                int index = _reusableBatchPayloadBufferCountInUse++;
                if (index < _reusableBatchPayloadBuffers.Count)
                {
                    byte[] existing = _reusableBatchPayloadBuffers[index];
                    if (existing.Length >= requiredLength)
                        return existing;

                    int resizedLength = Math.Max(existing.Length * 2, requiredLength);
                    byte[] resized = new byte[resizedLength];
                    _reusableBatchPayloadBuffers[index] = resized;
                    return resized;
                }

                int capacity = Math.Max(InitialReusableBatchPayloadBytes, requiredLength);
                byte[] created = new byte[capacity];
                _reusableBatchPayloadBuffers.Add(created);
                return created;
            }

            public void ResetReusableBatchPayloadBuffers() => _reusableBatchPayloadBufferCountInUse = 0;

            public void Reset()
            {
                ReturnTemporaryPayload(SinglePayloadRentedBuffer);
                Count = 0;
                SinglePayload = default;
                SinglePayloadRentedBuffer = null;
                Buffer.Clear();
            }
        }

        private readonly record struct ServerPublishedCorrectionState(
            float X,
            float Y,
            float Z,
            float YawRadians,
            long Tick,
            int OwnerConnectionId);

        private readonly record struct PayloadSliceKey(byte[] Buffer, int Offset, int Length);
    }
}
