using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal static class Pf5R1HistoryAudit
{
    private const int Seed = 0x5F51_0001;
    private const string MeasurementBase =
        "61638fc70291bc117a5e933bf48bb04a19de0141";
    private const string CorrectnessCandidate =
        "af9fcb740c02eb08efa8d4985d929b50b135504e";
    private static readonly ulong[] s_histogramUpperBounds =
        [0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1_024, 2_048, 4_096, 8_192, ulong.MaxValue];
    private static readonly ChunkInterestProfile s_interest = new(
        24_000, 25_000,
        48_000, 49_000,
        72_000, 73_000);

    internal static void Run(string[] args)
    {
        int recipients = ReadInt(args, "--recipients", 64);
        int ratedRecipients = ReadInt(args, "--rated-recipients", 128);
        int ticks = ReadInt(args, "--ticks", 180);
        int fullFlowWarmup = ReadInt(args, "--full-flow-warmup", 30);
        int fullFlowTicks = ReadInt(args, "--full-flow-ticks", 270);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r1-history-audit.json";
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        if (recipients is < 2 or > 512 || ratedRecipients is < 2 or > 192 ||
            ticks < 20 || fullFlowWarmup < 1 ||
            fullFlowTicks < 10)
            throw new ArgumentOutOfRangeException(nameof(args));

        string[] scenarios = focused
            ? ["synchronized", "combined-divergence", "history-pressure"]
            :
            [
                "quiet",
                "synchronized",
                "staggered-joins",
                "cadence-phases",
                "controlled-loss",
                "controlled-reorder",
                "proof-delay",
                "temporary-stalls",
                "aoi-churn",
                "reconnect",
                "history-pressure",
                "combined-divergence",
            ];

        var semantic = new List<Pf5R1ScenarioReport>(scenarios.Length);
        foreach (string scenario in scenarios)
        {
            Console.WriteLine($"PF5-R1 semantic audit {scenario}: constructing...");
            using var run = new SemanticScenario(
                scenario, recipients, ticks, Seed,
                PreparedChunkPublicationReadModel.HistoryDerivedComparison);
            Pf5R1ScenarioReport row = run.Run();
            semantic.Add(row);
            Console.WriteLine($"PF5-R1 {scenario}: requests={row.DeltaRangeRequests:N0}, " +
                $"repeat={row.RepeatRangePercent:F2}%, nodes/request={row.HistoryNodesPerRequest:F2}, " +
                $"records/request={row.RecordsPerRequest:F2}, snapshots={row.SnapshotOccurrences:N0}");
        }

        Pf5R1TopologyReport[] topology = focused
            ? [RunFixedTopology(1, 512), RunFixedTopology(512, 1)]
            :
            [
                RunFixedTopology(1, 512),
                RunFixedTopology(8, 64),
                RunFixedTopology(64, 8),
                RunFixedTopology(512, 1),
            ];
        Pf5R1HistoryInterferenceReport interference = RunHistoryInterference();

        P03NetworkWorkload workload = P03NetworkWorkload.ChunkProductionDense(
                ratedRecipients)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
        P03WholeFlowRow uninstrumented;
        P03WholeFlowRow instrumented;
        P03WholeFlowRow uninstrumentedAfter;
        using (var harness = new P03WholeFlowHarness(
                   workload, P03NetworkProfile.ChunkPublication, 2,
                   stageTimingEnabled: false,
                   gameDetailedAttribution: false,
                   compression: RemoteChunkBundleCompressionPolicy.Selected,
                   historyAudit: false,
                   legacyRangeCensus: false))
            uninstrumented = harness.Run(fullFlowWarmup, fullFlowTicks);
        using (var harness = new P03WholeFlowHarness(
                   workload, P03NetworkProfile.ChunkPublication, 2,
                   stageTimingEnabled: false,
                   gameDetailedAttribution: false,
                   compression: RemoteChunkBundleCompressionPolicy.Selected,
                   historyAudit: true,
                   legacyRangeCensus: false))
            instrumented = harness.Run(fullFlowWarmup, fullFlowTicks);
        using (var harness = new P03WholeFlowHarness(
                   workload, P03NetworkProfile.ChunkPublication, 2,
                   stageTimingEnabled: false,
                   gameDetailedAttribution: false,
                   compression: RemoteChunkBundleCompressionPolicy.Selected,
                   historyAudit: false,
                   legacyRangeCensus: false))
            uninstrumentedAfter = harness.Run(fullFlowWarmup, fullFlowTicks);
        ChunkPublicationHistoryAuditSnapshot fullAudit =
            instrumented.ProfileCounters.HistoryAudit ??
            throw new InvalidOperationException("PF5-R1 whole-flow history audit was absent.");
        Require(fullAudit.DeltaRequests != 0 && fullAudit.ShadowContentMismatches == 0 &&
            !fullAudit.RangeTableOverflowed, "PF5-R1 whole-flow shadow oracle did not reconcile.");
        double controlP95 = (uninstrumented.Timing.NetworkAndRemoteReplication.P95 +
            uninstrumentedAfter.Timing.NetworkAndRemoteReplication.P95) / 2d;
        var fullFlow = new Pf5R1FullFlowComparison(
            uninstrumented,
            instrumented,
            uninstrumentedAfter,
            controlP95,
            ToSummary(fullAudit),
            controlP95 == 0 ? 0 :
                (instrumented.Timing.NetworkAndRemoteReplication.P95 - controlP95) /
                controlP95 * 100d);

        var report = new Pf5R1AuditReport(
            "ultrasim-concept-demo-pf5-r1-history-audit-v1",
            DateTimeOffset.UtcNow,
            MeasurementBase,
            ReadGitSha(),
            CorrectnessCandidate,
            ProductionSemanticsChanged: false,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            Seed,
            new Pf5R1Policy(
                "ChunkPublication", "Message129WhenBeneficial",
                "LZ4 message148 canonical>=384 and saving>=32",
                "Immediate/Near/Far 1/2/4", "CrossLane cap 2", 60, 64, 29, 2),
            semantic.ToArray(), topology, interference, fullFlow,
            CreateDisposition(semantic, topology),
            s_histogramUpperBounds);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    internal static Pf5R1ScenarioReport RunPreparedScenarioForPf5R2(
        string scenario,
        int recipients,
        int ticks,
        int seed)
    {
        using var run = new SemanticScenario(
            scenario, recipients, ticks, seed,
            PreparedChunkPublicationReadModel.PreparedLatestState);
        return run.Run();
    }

    private static Pf5R1TopologyReport RunFixedTopology(int hotChunks, int fanout)
    {
        const int sourceRecords = 512;
        Require(hotChunks * fanout == sourceRecords,
            "The fixed topology matrix must retain 512 recipient-publication relations.");
        using var source = new SourceFixture(sourceRecords, sourceRecords, 4_096,
            static index => (index * 8, 0));
        var baseline = new ChunkPublicationCursor[hotChunks];
        var snapshots = new WorldPublicRecord[2];
        for (int chunk = 0; chunk < hotChunks; chunk++)
        {
            SpatialChunkKey key = source.Keys[chunk];
            Require(source.Store.TryCopySnapshot(key, snapshots, out ChunkPublicationBatch batch) ==
                ChunkPublicationReadStatus.Snapshot, "Topology baseline snapshot failed.");
            baseline[chunk] = batch.Cursor;
        }
        source.Store.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(4_096));
        source.MutateAll(1);
        var deltas = new WorldPublicDeltaRecord[2];
        long started = Stopwatch.GetTimestamp();
        for (int chunk = 0; chunk < hotChunks; chunk++)
        for (int recipient = 0; recipient < fanout; recipient++)
        {
            Require(source.Store.TryCopyDelta(source.Keys[chunk], baseline[chunk], deltas,
                    out ChunkPublicationBatch batch) == ChunkPublicationReadStatus.Delta &&
                batch.Count == 1, "Fixed-topology source delta diverged.");
        }
        ulong wallTicks = checked((ulong)(Stopwatch.GetTimestamp() - started));
        ChunkPublicationHistoryAuditSnapshot audit = source.Store.GetHistoryAuditSnapshot();
        Require(audit.DeltaRequests == sourceRecords && audit.DistinctRanges == hotChunks &&
            audit.DeltaRecordsEmitted == sourceRecords &&
            audit.ShadowContentMismatches == 0, "Fixed-topology work did not remain constant.");
        return new Pf5R1TopologyReport(
            hotChunks, fanout, sourceRecords, sourceRecords, sourceRecords,
            audit.DeltaRequests, audit.DistinctRanges, audit.RepeatedRangeRequests,
            audit.HistoryNodesExamined, audit.DeltaRecordsEmitted,
            audit.CompletePreparationTicks, audit.UniqueRangePreparationTicks,
            audit.ShadowReusablePreparationTicks, wallTicks);
    }

    private static Pf5R1HistoryInterferenceReport RunHistoryInterference()
    {
        using var source = new SourceFixture(2, 2, 8,
            static index => (index * 8, 0));
        var rows = new WorldPublicRecord[2];
        Require(source.Store.TryCopySnapshot(source.Keys[1], rows, out ChunkPublicationBatch baseline) ==
            ChunkPublicationReadStatus.Snapshot, "Interference baseline failed.");
        source.Store.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(64));
        source.MutateOne(1, 1);
        ulong unrelatedMutations = 0;
        for (int tick = 2; tick <= 24; tick++)
        {
            source.MutateOne(0, tick);
            unrelatedMutations++;
        }
        var deltas = new WorldPublicDeltaRecord[2];
        ChunkPublicationReadStatus status = source.Store.TryCopyDelta(
            source.Keys[1], baseline.Cursor, deltas, out ChunkPublicationBatch result);
        ChunkPublicationHistoryAuditSnapshot audit = source.Store.GetHistoryAuditSnapshot();
        ChunkPublicationHistoryRetentionAudit cold = audit.Retention.Single(
            value => value.ChunkKey == source.Keys[1]);
        Require(status == ChunkPublicationReadStatus.SnapshotRequired &&
            result.HistoryFloor.Value > baseline.Cursor.Revision.Value,
            "Unrelated hot-region pressure did not reproduce global-history loss.");
        return new Pf5R1HistoryInterferenceReport(
            8, unrelatedMutations, source.Keys[1], baseline.Cursor.Revision.Value,
            result.HistoryFloor.Value, status.ToString(), audit.PressureEvictions,
            audit.HistoryFloorAdvances, cold.RetainedEntries,
            "Hot mutations in another chunk evicted the cold chunk's needed delta from the single global pool.");
    }

    private static Pf5R1HistorySummary ToSummary(ChunkPublicationHistoryAuditSnapshot audit)
    {
        ulong uniqueRecords = 0;
        ulong repeatedRecordInstances = 0;
        foreach (ChunkPublicationHistoryRangeAudit range in audit.Ranges)
        {
            uniqueRecords += checked((ulong)range.RecordCount);
            repeatedRecordInstances += checked((range.Requests - 1) * (ulong)range.RecordCount);
        }
        return new Pf5R1HistorySummary(
            audit.DeltaRequests, audit.DistinctRanges, audit.RepeatedRangeRequests,
            Percent(audit.RepeatedRangeRequests, audit.DeltaRequests),
            audit.DeltaRecordsEmitted, uniqueRecords, repeatedRecordInstances,
            Percent(repeatedRecordInstances, audit.DeltaRecordsEmitted),
            audit.HistoryNodesExamined, audit.HistoryNodesSkipped,
            audit.HistoryNodesAfterCursor, audit.DuplicateMutations,
            audit.DuplicateMutationsCollapsed, audit.CurrentRecordLookups,
            audit.RecordsSorted, audit.HistoryWalkTicks, audit.CollapseTicks,
            audit.CurrentStateLookupTicks, audit.SortTicks,
            audit.CompletePreparationTicks, audit.UniqueRangePreparationTicks,
            audit.ShadowReusablePreparationTicks,
            Percent(audit.ShadowReusablePreparationTicks, audit.CompletePreparationTicks),
            audit.ShadowContentMismatches, audit.RangeTableOverflowed,
            audit.HistoryNodesExaminedHistogram,
            audit.HistoryNodesAfterCursorHistogram,
            audit.DuplicateMutationHistogram,
            audit.RecordsEmittedHistogram,
            audit.RangeLengthHistogram,
            audit.Ranges,
            audit.Retention);
    }

    private static Pf5R1ArchitectureDisposition CreateDisposition(
        List<Pf5R1ScenarioReport> scenarios,
        Pf5R1TopologyReport[] topology)
    {
        ulong maximumChunkRelationships = scenarios.Count == 0 ? 0 :
            scenarios.Max(static row => row.RecipientChunkRelationshipHighWater);
        ulong maximumEntityRelationships = scenarios.Count == 0 ? 0 :
            scenarios.Max(static row => row.EstimatedActiveRecipientEntityRelationships);
        return new Pf5R1ArchitectureDisposition(
            "Outcome B — reject history-derived latest-state as the fundamental steady-state model.",
            "A complete current-state introduction is expected for first knowledge of an AOI scope; " +
            "a current-state rebase is expected when retained history can no longer bridge a gap. " +
            "The audit targets recipient-specific history reconstruction after a proven baseline.",
            "ChunkPublicationStore owns packed current rows plus one globally bounded, per-mutation " +
            "history pool. A recipient owns per-chunk applied revision. Every ordinary delta request " +
            "walks that chunk's retained list from its head, skips entries at/before the cursor, " +
            "collapses later mutations by public slot, rereads current positive rows, and sorts output.",
            "A new subscription has TrackingOrigin/current interest but HasKnowledge=false and no " +
            "applied cursor. It copies current relevant chunk truth at revision R0; only local commit " +
            "or a matching authenticated remote proof advances applied knowledge to R0. Mutations " +
            "after R0 remain delta debt. History-floor loss performs the same current-state operation " +
            "as an explicit rebase but is diagnosed separately.",
            [
                "source truth: source epoch, chunk key, from/to revisions, immutable current records, content hash",
                "recipient obligation: session/generation, delivery ID, queue admission, proof ticket, applied revision, absence debt, retry/recovery state",
            ],
            "V2 encoded an authoritative entity payload once, retained/coalesced latest state by " +
            "(item, scope), and reused immutable payloads across scope subscribers. Per connection it " +
            "still filtered relevant latest snapshots and tracked last-sent—not applied—sequences. " +
            "Its source preparation is useful precedent; its sent-based knowledge and different " +
            "transport/scheduler semantics are not acceptable V3 semantics.",
            [
                "latest/current state: public transform, prefab/body identity, orientation, visibility/ground-item flags, public presentation/equipment metadata, owner-private current state",
                "event/occurrence history: Chat occurrences, Game application events, transactional results, and any future fact whose intermediate occurrence must be observed",
            ],
            "Positive current truth may be superseded by a later proven current version. Removal is " +
            "not discoverable by scanning only present rows, so an alternative requires explicit " +
            "recipient-local absence debt or immutable source tombstone/removal publication with " +
            "independent exact proof. PF4B positive supersession cannot erase negative obligations.",
            "Capture Introduction(C,R0) while proven knowledge is none; retain every semantically " +
            "required mutation/removal newer than R0; proving R0 may establish only R0; AOI leave, " +
            "epoch reset, or reconnect must fence the old offer and must never inherit its proof.",
            new Pf5R1RecipientKnowledgeMemoryEstimate(
                maximumChunkRelationships,
                maximumEntityRelationships,
                checked(maximumChunkRelationships * 16),
                checked(maximumChunkRelationships * 24),
                checked(maximumChunkRelationships * 32),
                checked(maximumEntityRelationships * 16),
                checked(maximumEntityRelationships * 24),
                checked(maximumEntityRelationships * 32),
                "Design estimates only; array/object headers, indexes, alignment, proof debt, and capacity slack are excluded."),
            topology.Length == 0 ? 0 : topology.Max(static row => row.RecipientsPerHotChunk),
            "Design the explicit Introduction/Rebase + shared immutable current/latest source " +
            "publication model. Retain bounded history only as an ordinary lag bridge and retain " +
            "independent recipient queue/delivery/proof/absence state. Do not add a range cache first.",
            "PAUSED — PF6 should resume after the replication-model design/implementation checkpoint " +
            "re-establishes the accepted correctness and product gates.");
    }

    private sealed class SemanticScenario : IDisposable
    {
        private const int ChunksPerRegion = 8;
        private const int RecordsPerChunk = 20;
        private readonly string _id;
        private readonly int _recipientCount;
        private readonly int _ticks;
        private readonly int _seed;
        private readonly SourceFixture _source;
        private readonly RecipientState?[] _recipients;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes =
            new OfflineDirectChunkSubscriptionChange[512];
        private readonly bool _quiet;
        private readonly bool _staggered;
        private readonly bool _cadencePhases;
        private readonly bool _loss;
        private readonly bool _reorder;
        private readonly bool _proofDelay;
        private readonly bool _stalls;
        private readonly bool _aoiChurn;
        private readonly bool _reconnect;
        private ulong _applicationBytes;
        private ulong _preparedPublications;
        private ulong _proofsApplied;
        private ulong _proofsStale;
        private ulong _proofsLost;
        private ulong _reconnects;
        private ulong _packagingTicks;
        private ulong _proofTicks;
        private ulong _snapshotOccurrences;
        private ulong _snapshotRecords;
        private ulong _snapshotApplicationBytes;
        private readonly ulong[] _snapshotReasons =
            new ulong[Enum.GetValues<OfflineDirectChunkSnapshotReason>().Length];
        private readonly ulong[] _snapshotReasonRecords =
            new ulong[Enum.GetValues<OfflineDirectChunkSnapshotReason>().Length];
        private readonly ulong[] _snapshotReasonSourceTicks =
            new ulong[Enum.GetValues<OfflineDirectChunkSnapshotReason>().Length];
        private ulong _retiredSourceReadTicks;
        private ulong _retiredMaterializationTicks;
        private ulong _retiredMaterializedRecords;
        private ulong _retiredProofValidationTicks;
        private ulong _retiredCursorTicks;
        private ulong _relationshipHighWater;

        internal SemanticScenario(
            string id,
            int recipientCount,
            int ticks,
            int seed,
            PreparedChunkPublicationReadModel readModel)
        {
            _id = id;
            _recipientCount = recipientCount;
            _ticks = ticks;
            _seed = seed;
            _quiet = id == "quiet";
            _staggered = id is "staggered-joins" or "combined-divergence";
            _cadencePhases = id is "cadence-phases" or "combined-divergence";
            _loss = id is "controlled-loss" or "combined-divergence";
            _reorder = id is "controlled-reorder" or "combined-divergence";
            _proofDelay = id is "proof-delay" or "combined-divergence";
            _stalls = id is "temporary-stalls" or "history-pressure" or
                "combined-divergence";
            _aoiChurn = id is "aoi-churn" or "combined-divergence";
            _reconnect = id is "reconnect" or "combined-divergence";
            int historyCapacity = id == "history-pressure" ? 256 :
                id == "combined-divergence" ? 512 : 32_768;
            _source = new SourceFixture(
                ChunksPerRegion * 2 * RecordsPerChunk,
                ChunksPerRegion * 2,
                historyCapacity,
                static index =>
                {
                    int chunk = index / RecordsPerChunk;
                    int region = chunk / ChunksPerRegion;
                    int local = chunk % ChunksPerRegion;
                    return (region * 24 + local % 4, local / 4);
                },
                readModel);
            _source.Store.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(65_536));
            _source.Store.ConfigurePreparedPublicationAttribution(
                readModel == PreparedChunkPublicationReadModel.PreparedLatestState);
            _recipients = new RecipientState[recipientCount];
            if (!_staggered)
                for (int index = 0; index < recipientCount; index++)
                    Activate(index, reconnect: false, tick: 0);
        }

        internal Pf5R1ScenarioReport Run()
        {
            long wallStarted = Stopwatch.GetTimestamp();
            for (int tick = 1; tick <= _ticks; tick++)
            {
                for (int recipient = 0; recipient < _recipientCount; recipient++)
                    if (_recipients[recipient] is null &&
                        (!_staggered || tick >= 1 + recipient * Math.Max(1, _ticks / 3) /
                            _recipientCount))
                        Activate(recipient, reconnect: false, tick);

                if (_reconnect && tick == _ticks / 2)
                    for (int recipient = 0; recipient < _recipientCount; recipient += 8)
                    {
                        Retire(recipient);
                        Activate(recipient, reconnect: true, tick);
                        _reconnects++;
                    }

                if (!_quiet)
                {
                    int region = tick & 1;
                    _source.MutateChunks(region * ChunksPerRegion, ChunksPerRegion, tick);
                }

                for (int recipient = 0; recipient < _recipientCount; recipient++)
                {
                    RecipientState? state = _recipients[recipient];
                    if (state is null) continue;
                    if (_aoiChurn && tick % 30 == 0 && ((recipient + tick / 30) & 1) == 0)
                    {
                        state.Region ^= 1;
                        Reconcile(state);
                    }
                    DeliverProofs(state, tick, final: false);
                    if (ShouldStall(recipient, tick) ||
                        _cadencePhases && (tick + recipient) % 4 != 0)
                        continue;
                    ScheduleDirty(state, tick);
                }
                CountRelationships();
            }

            // Deliver newest outstanding work first where applicable, then perform one exact
            // current-source catch-up per populated subscribed chunk.
            for (int recipient = 0; recipient < _recipientCount; recipient++)
            {
                RecipientState? state = _recipients[recipient];
                if (state is null) continue;
                DeliverProofs(state, int.MaxValue, final: true);
                CatchUp(state, checked((ulong)_ticks + 1));
                Collect(state);
            }
            ulong wallTicks = checked((ulong)(Stopwatch.GetTimestamp() - wallStarted));
            ChunkPublicationHistoryAuditSnapshot audit = _source.Store.GetHistoryAuditSnapshot();
            PreparedChunkPublicationSnapshot prepared =
                _source.Store.GetPreparedPublicationSnapshot();
            Require(audit.ShadowContentMismatches == 0 && !audit.RangeTableOverflowed,
                $"{_id} shadow range oracle diverged or overflowed.");
            Pf5R1HistorySummary summary = ToSummary(audit);
            ulong activeRelationships = 0;
            ulong publicEntityRelationships = 0;
            int active = 0;
            var relationshipsPerRecipient = new int[_recipientCount];
            var recipientsPerChunk = new int[_source.Keys.Length];
            for (int index = 0; index < _recipients.Length; index++)
            {
                RecipientState? state = _recipients[index];
                if (state is null) continue;
                active++;
                int populated = CountPopulatedSubscriptions(state);
                relationshipsPerRecipient[active - 1] = populated;
                activeRelationships += checked((ulong)populated);
                publicEntityRelationships += checked((ulong)(populated * RecordsPerChunk));
                for (int chunk = 0; chunk < _source.Keys.Length; chunk++)
                    if (TryFindSubscription(state.Recipient, _source.Keys[chunk], out _))
                        recipientsPerChunk[chunk]++;
            }
            Array.Resize(ref relationshipsPerRecipient, active);
            Pf5R1RelationshipDistribution relationshipDistribution =
                RelationshipDistribution(relationshipsPerRecipient, recipientsPerChunk);
            return new Pf5R1ScenarioReport(
                _id, _seed, _recipientCount, active, _ticks,
                summary.DeltaRangeRequests, summary.UniqueRanges,
                summary.RepeatedRanges, summary.RepeatRangePercent,
                summary.RecordInstances, summary.UniqueRangeRecords,
                summary.RepeatedRecordInstances, summary.RepeatRecordPercent,
                summary.DeltaRangeRequests == 0 ? 0 :
                    (double)summary.HistoryNodesExamined / summary.DeltaRangeRequests,
                summary.DeltaRangeRequests == 0 ? 0 :
                    (double)summary.RecordInstances / summary.DeltaRangeRequests,
                summary, _preparedPublications, _applicationBytes,
                _packagingTicks, _proofTicks, _proofsApplied, _proofsStale,
                _proofsLost, _reconnects, _snapshotOccurrences, _snapshotRecords,
                _snapshotApplicationBytes,
                SnapshotReasonRows(), audit.SnapshotReads, audit.SnapshotRecords,
                audit.HistoryEntriesProduced, audit.HistoryOccupancyHighWater,
                audit.PressureEvictions, audit.HistoryFloorAdvances,
                activeRelationships, _relationshipHighWater,
                publicEntityRelationships, relationshipDistribution, wallTicks,
                _retiredSourceReadTicks, _retiredMaterializationTicks,
                _retiredMaterializedRecords, _retiredProofValidationTicks,
                _retiredCursorTicks,
                "Semantic/proof diagnostic harness; no UDP clock. The real-UDP selected-policy control is reported separately.",
                prepared);
        }

        private void Activate(int index, bool reconnect, int tick)
        {
            var state = new RecipientState(index, reconnect);
            _recipients[index] = state;
            state.Region = _aoiChurn ? index & 1 : 0;
            Reconcile(state);
            Bootstrap(state, checked((ulong)Math.Max(0, tick)));
        }

        private void Reconcile(RecipientState state)
        {
            WorldPosition anchor = RegionAnchor(state.Region);
            Require(state.Recipient.Reconcile(
                    _source.Store, GameWorldState.DefaultSpace, anchor, _changes,
                    out OfflineDirectChunkReconcileResult result) ==
                OfflineDirectChunkReconcileStatus.Reconciled,
                $"{_id} recipient reconciliation failed.");
            _ = result;
        }

        private void Bootstrap(RecipientState state, ulong step)
        {
            int firstChunk = state.Region * ChunksPerRegion;
            for (int index = 0; index < ChunksPerRegion; index++)
            {
                int subscription = FindSubscription(state.Recipient,
                    _source.Keys[firstChunk + index]);
                OfflineDirectChunkWireEncodeStatus status = state.Encoder.TryPrepareForRepresentation(
                    state.Recipient, _source.Store, subscription, step,
                    state.NextDeliveryId++, state.Scratch,
                    out OfflineDirectChunkWireEncodeResult result);
                Require(status is OfflineDirectChunkWireEncodeStatus.Snapshot or
                    OfflineDirectChunkWireEncodeStatus.Absence,
                    $"{_id} bootstrap did not produce current truth ({status}).");
                _snapshotApplicationBytes += checked((ulong)result.Written);
                Require(state.Recipient.CommitAppliedProof(result.Ticket) ==
                    OfflineDirectChunkAppliedProofStatus.Applied,
                    $"{_id} bootstrap proof failed.");
            }
        }

        private void ScheduleDirty(RecipientState state, int tick)
        {
            int dirtyRegion = tick & 1;
            int firstChunk = dirtyRegion * ChunksPerRegion;
            for (int index = 0; index < ChunksPerRegion; index++)
            {
                SpatialChunkKey key = _source.Keys[firstChunk + index];
                if (!TryFindSubscription(state.Recipient, key, out int subscription)) continue;
                long started = Stopwatch.GetTimestamp();
                OfflineDirectChunkWireEncodeStatus status = state.Encoder.TryPrepareForRepresentation(
                    state.Recipient, _source.Store, subscription, checked((ulong)tick),
                    state.NextDeliveryId++, state.Scratch,
                    out OfflineDirectChunkWireEncodeResult result);
                ulong elapsed = checked((ulong)(Stopwatch.GetTimestamp() - started));
                _packagingTicks += elapsed;
                if (status is OfflineDirectChunkWireEncodeStatus.NotDue or
                    OfflineDirectChunkWireEncodeStatus.NoPayload)
                    continue;
                Require(status is OfflineDirectChunkWireEncodeStatus.Delta or
                    OfflineDirectChunkWireEncodeStatus.Snapshot or
                    OfflineDirectChunkWireEncodeStatus.Absence,
                    $"{_id} source preparation failed with {status}.");
                _preparedPublications++;
                _applicationBytes += checked((ulong)result.Written);
                if (status is OfflineDirectChunkWireEncodeStatus.Snapshot or
                    OfflineDirectChunkWireEncodeStatus.Absence)
                    _snapshotApplicationBytes += checked((ulong)result.Written);
                if (_loss && Deterministic(state.Id, index, tick, 17) == 0)
                {
                    _proofsLost++;
                    continue;
                }
                int delay = _proofDelay ? 8 : _reorder
                    ? Deterministic(state.Id, index, tick, 4)
                    : 0;
                state.Enqueue(result.Ticket, checked(tick + delay));
            }
        }

        private void DeliverProofs(RecipientState state, int tick, bool final)
        {
            if (_reorder || final)
            {
                for (int index = state.PendingCount - 1; index >= 0; index--)
                    if (final || state.Pending[index].DueTick <= tick)
                        ApplyProof(state, index);
            }
            else
            {
                int index = 0;
                while (index < state.PendingCount)
                {
                    if (state.Pending[index].DueTick > tick) { index++; continue; }
                    ApplyProof(state, index);
                }
            }
        }

        private void ApplyProof(RecipientState state, int index)
        {
            PendingProof pending = state.Pending[index];
            long started = Stopwatch.GetTimestamp();
            OfflineDirectChunkAppliedProofStatus status =
                state.Recipient.CommitAppliedProof(pending.Ticket);
            _proofTicks += checked((ulong)(Stopwatch.GetTimestamp() - started));
            if (status == OfflineDirectChunkAppliedProofStatus.Applied) _proofsApplied++;
            else if (status is OfflineDirectChunkAppliedProofStatus.Stale or
                     OfflineDirectChunkAppliedProofStatus.Duplicate) _proofsStale++;
            else throw new InvalidOperationException($"{_id} proof became invalid.");
            state.RemovePending(index);
        }

        private void CatchUp(RecipientState state, ulong step)
        {
            for (int chunk = 0; chunk < _source.Keys.Length; chunk++)
            {
                SpatialChunkKey key = _source.Keys[chunk];
                if (!TryFindSubscription(state.Recipient, key, out int subscription)) continue;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    OfflineDirectChunkWireEncodeStatus status =
                        state.Encoder.TryPrepareForRepresentation(
                            state.Recipient, _source.Store, subscription,
                            checked(step + (ulong)attempt),
                            state.NextDeliveryId++, state.Scratch,
                            out OfflineDirectChunkWireEncodeResult result);
                    if (status == OfflineDirectChunkWireEncodeStatus.NotDue) continue;
                    if (status == OfflineDirectChunkWireEncodeStatus.NoPayload) break;
                    Require(status is OfflineDirectChunkWireEncodeStatus.Delta or
                        OfflineDirectChunkWireEncodeStatus.Snapshot or
                        OfflineDirectChunkWireEncodeStatus.Absence,
                        $"{_id} final catch-up failed with {status}.");
                    OfflineDirectChunkAppliedProofStatus proof =
                        state.Recipient.CommitAppliedProof(result.Ticket);
                    Require(proof is OfflineDirectChunkAppliedProofStatus.Applied or
                        OfflineDirectChunkAppliedProofStatus.Duplicate,
                        $"{_id} final catch-up proof failed with {proof}.");
                }
            }
        }

        private bool ShouldStall(int recipient, int tick) => _stalls &&
            recipient % 4 == 0 && tick >= _ticks / 3 && tick < _ticks * 2 / 3;

        private void Retire(int index)
        {
            RecipientState? state = _recipients[index];
            if (state is null) return;
            Collect(state);
            _recipients[index] = null;
        }

        private void Collect(RecipientState state)
        {
            OfflineDirectChunkWireEncoderAttribution encoder = state.Encoder.GetAttribution();
            _retiredSourceReadTicks += encoder.SourceReadTicks;
            _retiredMaterializationTicks += encoder.DeltaMaterializationTicks;
            _retiredMaterializedRecords += encoder.DeltaRecordsMaterialized;
            _retiredProofValidationTicks += encoder.ProofValidationTicks;
            _retiredCursorTicks += encoder.CursorAdvancementTicks;
            OfflineDirectChunkSnapshotReasonAuditSnapshot snapshots =
                state.Recipient.GetSnapshotReasonAudit();
            foreach (OfflineDirectChunkSnapshotReasonObservation row in snapshots.Reasons)
            {
                _snapshotReasons[(int)row.Reason] += row.Occurrences;
                _snapshotReasonRecords[(int)row.Reason] += row.RecordsCopied;
                _snapshotReasonSourceTicks[(int)row.Reason] += row.SourceCopyTicks;
                _snapshotOccurrences += row.Occurrences;
                _snapshotRecords += row.RecordsCopied;
            }
        }

        private Pf5R1SnapshotReasonRow[] SnapshotReasonRows()
        {
            var rows = new List<Pf5R1SnapshotReasonRow>();
            for (int index = 1; index < _snapshotReasons.Length; index++)
                if (_snapshotReasons[index] != 0)
                    rows.Add(new Pf5R1SnapshotReasonRow(
                        ((OfflineDirectChunkSnapshotReason)index).ToString(),
                        _snapshotReasons[index],
                        _snapshotReasonRecords[index],
                        _snapshotReasonSourceTicks[index]));
            return rows.ToArray();
        }

        private int CountPopulatedSubscriptions(RecipientState state)
        {
            int count = 0;
            for (int chunk = 0; chunk < _source.Keys.Length; chunk++)
                if (TryFindSubscription(state.Recipient, _source.Keys[chunk], out _)) count++;
            return count;
        }

        private void CountRelationships()
        {
            ulong total = 0;
            for (int index = 0; index < _recipients.Length; index++)
                if (_recipients[index] is { } state)
                    total += checked((ulong)CountPopulatedSubscriptions(state));
            _relationshipHighWater = Math.Max(_relationshipHighWater, total);
        }

        private static WorldPosition RegionAnchor(int region) =>
            Position(region * 24 + 1, 0, 8_000, 8_000);

        private static Pf5R1RelationshipDistribution RelationshipDistribution(
            int[] perRecipient,
            int[] perChunk)
        {
            Array.Sort(perRecipient);
            Array.Sort(perChunk);
            return new Pf5R1RelationshipDistribution(
                Quantile(perRecipient, 0.50), Quantile(perRecipient, 0.95),
                perRecipient.Length == 0 ? 0 : perRecipient[^1],
                Quantile(perChunk, 0.50), Quantile(perChunk, 0.95),
                perChunk.Length == 0 ? 0 : perChunk[^1]);
        }

        private static int Quantile(int[] sorted, double quantile)
        {
            if (sorted.Length == 0) return 0;
            int index = Math.Clamp((int)Math.Ceiling(sorted.Length * quantile) - 1,
                0, sorted.Length - 1);
            return sorted[index];
        }

        public void Dispose() => _source.Dispose();
    }

    private sealed class RecipientState
    {
        internal RecipientState(int id, bool reconnect)
        {
            Id = id;
            Recipient = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(512, 64, 64),
                s_interest,
                OfflineDirectChunkCadence.EvidenceProfile,
                reconnect
                    ? OfflineDirectChunkSnapshotReason.ReconnectGenerationReset
                    : OfflineDirectChunkSnapshotReason.FirstSubscriptionIntroduction);
            Recipient.ResetTelemetryWindow(enableReconcileAttribution: true);
            Recipient.ConfigureSnapshotReasonAudit(true);
            Encoder = new OfflineDirectChunkWireEncoder(
                checked((ulong)id + 1 + (reconnect ? 10_000UL : 0)), 64);
            Encoder.ResetAttribution(enabled: true);
            Scratch = new byte[UltraSim.Game.Protocol.UltraSimGameProtocol.MaximumMessageBytes];
            Pending = new PendingProof[4_096];
        }

        internal int Id { get; }
        internal OfflineDirectChunkRecipient Recipient { get; }
        internal OfflineDirectChunkWireEncoder Encoder { get; }
        internal byte[] Scratch { get; }
        internal PendingProof[] Pending { get; }
        internal int PendingCount { get; private set; }
        internal ulong NextDeliveryId { get; set; } = 1;
        internal int Region { get; set; }

        internal void Enqueue(in OfflineDirectChunkDeliveryTicket ticket, int dueTick)
        {
            if (PendingCount == Pending.Length)
                throw new InvalidOperationException("PF5-R1 pending proof capacity exhausted.");
            Pending[PendingCount++] = new PendingProof(ticket, dueTick);
        }

        internal void RemovePending(int index)
        {
            PendingCount--;
            Pending[index] = Pending[PendingCount];
            Pending[PendingCount] = default;
        }
    }

    private readonly record struct PendingProof(
        OfflineDirectChunkDeliveryTicket Ticket,
        int DueTick);

    private sealed class SourceFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _entities;
        private readonly WorldPublicChange[] _changes;
        private readonly int[] _entityChunks;
        private readonly int _recordsPerChunk;

        internal SourceFixture(
            int entityCount,
            int chunkCount,
            int historyCapacity,
            Func<int, (int X, int Z)> chunkForEntity,
            PreparedChunkPublicationReadModel readModel =
                PreparedChunkPublicationReadModel.HistoryDerivedComparison)
        {
            if (entityCount <= 0 || chunkCount <= 0 || entityCount % chunkCount != 0)
                throw new ArgumentOutOfRangeException(nameof(entityCount));
            _recordsPerChunk = entityCount / chunkCount;
            _world = new EcsWorld(GameSchema.CreateSchema(), new EcsWorldOptions(
                new EntityCapacity(entityCount), 2, 4,
                lifecycleRecordCapacity: checked(entityCount * 2)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16,
                Math.Max(1, (entityCount + 255) / 256));
            _world.Seal();
            _world.Initialize();
            _entities = new Entity[entityCount];
            _changes = new WorldPublicChange[entityCount];
            _entityChunks = new int[entityCount];
            Keys = new SpatialChunkKey[chunkCount];
            for (int index = 0; index < entityCount; index++)
            {
                Require(_world.TryCreate(empty, out _entities[index]),
                    "PF5-R1 source entity creation failed.");
                (int x, int z) = chunkForEntity(index);
                int chunk = index / _recordsPerChunk;
                _entityChunks[index] = chunk;
                Keys[chunk] = new SpatialChunkKey(GameWorldState.DefaultSpace, x, z);
                _changes[index] = Present(index, 0);
            }
            Store = new ChunkPublicationStore(PublicationEpoch.Create(),
                new ChunkPublicationLimits(entityCount, checked(entityCount * 2), 64,
                    historyCapacity),
                readModel);
            Require(Store.TryCommit(_changes, out _), "PF5-R1 initial source commit failed.");
        }

        internal ChunkPublicationStore Store { get; }
        internal SpatialChunkKey[] Keys { get; }

        internal void MutateAll(int tick)
        {
            for (int index = 0; index < _entities.Length; index++)
                _changes[index] = Present(index, tick);
            Require(Store.TryCommit(_changes, out _), "PF5-R1 full mutation failed.");
        }

        internal void MutateOne(int chunk, int tick)
        {
            int entity = chunk * _recordsPerChunk + tick % _recordsPerChunk;
            _changes[0] = Present(entity, tick);
            Require(Store.TryCommit(_changes.AsSpan(0, 1), out _),
                "PF5-R1 single mutation failed.");
        }

        internal void MutateChunks(int firstChunk, int chunkCount, int tick)
        {
            for (int offset = 0; offset < chunkCount; offset++)
            {
                int chunk = firstChunk + offset;
                int entity = chunk * _recordsPerChunk + (tick + offset) % _recordsPerChunk;
                _changes[offset] = Present(entity, tick);
            }
            Array.Sort(_changes, 0, chunkCount,
                WorldPublicChangeEntityComparer.Instance);
            Require(Store.TryCommit(_changes.AsSpan(0, chunkCount), out _),
                "PF5-R1 striped mutation failed.");
        }

        private WorldPublicChange Present(int entity, int tick)
        {
            SpatialChunkKey key = Keys[_entityChunks[entity]];
            int localX = 1_000 + entity % _recordsPerChunk * 100 + tick % 73;
            return WorldPublicChange.Present(_entities[entity],
                Position(key.Coord.X, key.Coord.Z, localX, 2_000 + entity % 31),
                new WorldPublicDescriptor(
                    GameWorldPublicPrefabIds.Character,
                    new WorldYaw(unchecked((ushort)(tick + entity))),
                    WorldPublicStateFlags.Visible));
        }

        public void Dispose() => _world.Dispose();
    }

    private sealed class WorldPublicChangeEntityComparer : IComparer<WorldPublicChange>
    {
        internal static WorldPublicChangeEntityComparer Instance { get; } = new();
        public int Compare(WorldPublicChange x, WorldPublicChange y) =>
            x.Entity.Index.CompareTo(y.Entity.Index);
    }

    private static int FindSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey key)
    {
        if (TryFindSubscription(recipient, key, out int index)) return index;
        throw new InvalidOperationException($"PF5-R1 subscription {key} was absent.");
    }

    private static bool TryFindSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey key,
        out int result)
    {
        OfflineDirectChunkSubscriptionView subscriptions = recipient.Subscriptions;
        int low = 0;
        int high = subscriptions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = subscriptions[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) { result = middle; return true; }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        result = -1;
        return false;
    }

    private static int Deterministic(int recipient, int chunk, int tick, int modulus)
    {
        unchecked
        {
            uint value = (uint)Seed;
            value = (value ^ (uint)recipient) * 16_777_619u;
            value = (value ^ (uint)chunk) * 16_777_619u;
            value = (value ^ (uint)tick) * 16_777_619u;
            return (int)(value % (uint)modulus);
        }
    }

    private static WorldPosition Position(
        int chunkX, int chunkZ, int localX, int localZ) =>
        new(chunkX, chunkZ, checked((ushort)localX), checked((ushort)localZ), 0);

    private static double Percent(ulong part, ulong whole) =>
        whole == 0 ? 0 : part * 100d / whole;

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? text = ReadText(args, name);
        return text is null
            ? fallback
            : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? item = args.FirstOrDefault(value => value.StartsWith(prefix,
            StringComparison.Ordinal));
        return item?[prefix.Length..];
    }

    private static string ReadGitSha()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start git.");
        string value = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? value : "unknown";
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}

internal sealed record Pf5R1AuditReport(
    string Schema,
    DateTimeOffset CapturedAt,
    string MeasurementBase,
    string Commit,
    string FeatureCompleteCorrectnessCandidate,
    bool ProductionSemanticsChanged,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessors,
    long StopwatchFrequency,
    int Seed,
    Pf5R1Policy ProductionPolicy,
    Pf5R1ScenarioReport[] SemanticScenarios,
    Pf5R1TopologyReport[] FixedTotalTopology,
    Pf5R1HistoryInterferenceReport CrossChunkHistoryPressure,
    Pf5R1FullFlowComparison RealUdpProductionShaped,
    Pf5R1ArchitectureDisposition ArchitectureDisposition,
    ulong[] HistogramUpperBounds);

internal sealed record Pf5R1ArchitectureDisposition(
    string Outcome,
    string BaselineAndRebaseInterpretation,
    string CurrentModel,
    string CurrentIntroductionPath,
    string[] SourceTruthVersusRecipientObligation,
    string V2Comparison,
    string[] LatestStateVersusEventHistory,
    string RemovalAndAbsenceRequirement,
    string IntroductionRaceInvariant,
    Pf5R1RecipientKnowledgeMemoryEstimate RecipientKnowledgeMemoryEstimate,
    int MaximumFixedTopologyFanout,
    string RecommendedNextCheckpoint,
    string Pf6Disposition);

internal readonly record struct Pf5R1RecipientKnowledgeMemoryEstimate(
    ulong MeasuredChunkRelationshipsHighWater,
    ulong EstimatedEntityRelationshipsHighWater,
    ulong PackedChunkRecords16Bytes,
    ulong PackedChunkRecords24Bytes,
    ulong PackedChunkRecords32Bytes,
    ulong PackedEntityRecords16Bytes,
    ulong PackedEntityRecords24Bytes,
    ulong PackedEntityRecords32Bytes,
    string Qualification);

internal readonly record struct Pf5R1Policy(
    string Replication,
    string Bundling,
    string Compression,
    string Cadence,
    string Aggregation,
    int AuthorityHz,
    int ParticipantCap,
    int GameManifest,
    int TransportRevision);

internal sealed record Pf5R1ScenarioReport(
    string Scenario,
    int Seed,
    int ConfiguredRecipients,
    int ActiveRecipients,
    int Ticks,
    ulong DeltaRangeRequests,
    int UniqueRanges,
    ulong RepeatedRanges,
    double RepeatRangePercent,
    ulong RecordInstances,
    ulong UniqueRangeRecords,
    ulong RepeatedRecordInstances,
    double RepeatRecordPercent,
    double HistoryNodesPerRequest,
    double RecordsPerRequest,
    Pf5R1HistorySummary SourceHistory,
    ulong PreparedPublications,
    ulong CanonicalApplicationBytes,
    ulong SourcePlusRecipientPackagingTicks,
    ulong ProofCommitTicks,
    ulong ProofsApplied,
    ulong StaleOrDuplicateProofs,
    ulong DeliberatelyLostProofs,
    ulong Reconnects,
    ulong SnapshotOccurrences,
    ulong SnapshotRecords,
    ulong SnapshotCanonicalApplicationBytes,
    Pf5R1SnapshotReasonRow[] SnapshotReasons,
    ulong SourceSnapshotReads,
    ulong SourceSnapshotRecords,
    ulong HistoryEntriesProduced,
    int HistoryOccupancyHighWater,
    ulong PressureEvictions,
    ulong HistoryFloorAdvances,
    ulong ActiveRecipientChunkRelationships,
    ulong RecipientChunkRelationshipHighWater,
    ulong EstimatedActiveRecipientEntityRelationships,
    Pf5R1RelationshipDistribution RelationshipDistribution,
    ulong ScenarioWallTicks,
    ulong EncoderSourceReadTicks,
    ulong DeltaMaterializationTicks,
    ulong DeltaRecordsMaterialized,
    ulong ProofValidationTicks,
    ulong CursorAdvancementTicks,
    string ClockScope,
    PreparedChunkPublicationSnapshot PreparedSource = default);

internal readonly record struct Pf5R1SnapshotReasonRow(
    string Reason,
    ulong Occurrences,
    ulong RecordsCopied,
    ulong SourceCopyTicks);

internal readonly record struct Pf5R1RelationshipDistribution(
    int ChunksPerRecipientP50,
    int ChunksPerRecipientP95,
    int ChunksPerRecipientMax,
    int RecipientsPerChunkP50,
    int RecipientsPerChunkP95,
    int RecipientsPerChunkMax);

internal sealed record Pf5R1HistorySummary(
    ulong DeltaRangeRequests,
    int UniqueRanges,
    ulong RepeatedRanges,
    double RepeatRangePercent,
    ulong RecordInstances,
    ulong UniqueRangeRecords,
    ulong RepeatedRecordInstances,
    double RepeatRecordPercent,
    ulong HistoryNodesExamined,
    ulong HistoryNodesSkipped,
    ulong HistoryNodesAfterCursor,
    ulong DuplicateMutations,
    ulong DuplicateMutationsCollapsed,
    ulong CurrentRecordLookups,
    ulong RecordsSorted,
    ulong HistoryWalkTicks,
    ulong CollapseTicks,
    ulong CurrentStateLookupTicks,
    ulong SortTicks,
    ulong CompletePreparationTicks,
    ulong PrepareEachUniqueRangeOnceTicks,
    ulong ShadowReusablePreparationTicks,
    double ShadowReusablePreparationPercent,
    ulong ShadowContentMismatches,
    bool RangeTableOverflowed,
    ulong[] HistoryNodesExaminedHistogram,
    ulong[] HistoryNodesAfterCursorHistogram,
    ulong[] DuplicateMutationHistogram,
    ulong[] RecordsEmittedHistogram,
    ulong[] RangeLengthHistogram,
    ChunkPublicationHistoryRangeAudit[] RawRanges,
    ChunkPublicationHistoryRetentionAudit[] Retention);

internal readonly record struct Pf5R1TopologyReport(
    int HotChunks,
    int RecipientsPerHotChunk,
    int AuthoritativeSourceMutations,
    int DirtyChunks,
    int RecipientPublicationRelationships,
    ulong DeltaRequests,
    int UniqueRanges,
    ulong RepeatedRanges,
    ulong HistoryNodesExamined,
    ulong RecordsEmitted,
    ulong ActualSourcePreparationTicks,
    ulong PrepareEachRangeOnceTicks,
    ulong ShadowReusablePreparationTicks,
    ulong WallTicks);

internal readonly record struct Pf5R1HistoryInterferenceReport(
    int GlobalHistoryCapacity,
    ulong UnrelatedHotChunkMutations,
    SpatialChunkKey ColdChunk,
    ulong ColdRecipientRevision,
    ulong ColdHistoryFloorAfterPressure,
    string ReadStatus,
    ulong PressureEvictions,
    ulong HistoryFloorAdvances,
    int ColdRetainedEntries,
    string Finding);

internal sealed record Pf5R1FullFlowComparison(
    P03WholeFlowRow UninstrumentedBefore,
    P03WholeFlowRow Instrumented,
    P03WholeFlowRow UninstrumentedAfter,
    double ControlNetworkAndReplicationP95Mean,
    Pf5R1HistorySummary InstrumentedSourceHistory,
    double NetworkAndReplicationP95InstrumentationDeltaPercent);
