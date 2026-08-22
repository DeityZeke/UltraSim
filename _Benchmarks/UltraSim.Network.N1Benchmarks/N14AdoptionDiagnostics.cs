using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Spatial;

internal enum N14DirtyTopology
{
    SharedWindow,
    DispersedWindows,
}

internal static class N14AdoptionDiagnostics
{
    internal static N14AdoptionEvidence Capture(
        int recipients,
        int mixedCadenceRecipients,
        int roamingSteps,
        int subscriptionCapacity,
        int warmups,
        int samples)
    {
        int[] sharedDirtyCounts;
        DirtyDensityEvidence[] sharedDensity;
        using (var fixture = new DirtyTopologyFixture(
            recipients, subscriptionCapacity, N14DirtyTopology.SharedWindow,
            new OfflineDirectChunkCadence(1, 1, 1)))
        {
            sharedDirtyCounts = DensityCounts(fixture.TargetChunkCount);
            sharedDensity = MeasureDensity(fixture, sharedDirtyCounts, warmups, samples);
        }

        DirtyDensityEvidence[] dispersedDensity;
        using (var fixture = new DirtyTopologyFixture(
            recipients, subscriptionCapacity, N14DirtyTopology.DispersedWindows,
            new OfflineDirectChunkCadence(1, 1, 1)))
        {
            dispersedDensity = MeasureDensity(
                fixture, DensityCounts(fixture.TargetChunkCount), warmups, samples);
        }

        MixedCadenceTraversalEvidence[] mixedCadence;
        using (var fixture = new DirtyTopologyFixture(
            mixedCadenceRecipients,
            subscriptionCapacity,
            N14DirtyTopology.SharedWindow,
            OfflineDirectChunkCadence.EvidenceProfile))
        {
            mixedCadence = MeasureMixedCadence(
                fixture, DensityCounts(fixture.TargetChunkCount), warmups, samples);
        }

        RoamingInterestEvidence[] roaming =
        [
            MeasureRoaming(
                "ordinary-cardinal-travel",
                roamingSteps,
                subscriptionCapacity,
                step => (8_000L + (long)step * 100, 8_000L)),
            MeasureRoaming(
                "chunk-boundary-oscillation",
                roamingSteps,
                subscriptionCapacity,
                step => ((step & 1) == 0 ? 31_999L : 32_000L, 8_000L)),
            MeasureRoaming(
                "chunk-corner-oscillation",
                roamingSteps,
                subscriptionCapacity,
                step => ((step & 1) == 0 ? 31_999L : 32_000L,
                    (step & 1) == 0 ? 31_999L : 32_000L)),
        ];

        return new N14AdoptionEvidence(
            recipients,
            mixedCadenceRecipients,
            roamingSteps,
            sharedDensity,
            dispersedDensity,
            mixedCadence,
            roaming,
            "Semantic-only diagnostics. Dirty density counts publication scopes selected from " +
            "the shared window or one home publication scope per dispersed recipient. Mixed " +
            "cadence measures the bounded pass-marker traversal over the retained flat array.");
    }

    private static DirtyDensityEvidence[] MeasureDensity(
        DirtyTopologyFixture fixture,
        int[] counts,
        int warmups,
        int samples)
    {
        var result = new DirtyDensityEvidence[counts.Length];
        for (int index = 0; index < counts.Length; index++)
            result[index] = fixture.MeasureDensity(counts[index], warmups, samples);
        return result;
    }

    private static MixedCadenceTraversalEvidence[] MeasureMixedCadence(
        DirtyTopologyFixture fixture,
        int[] counts,
        int warmups,
        int samples)
    {
        var result = new MixedCadenceTraversalEvidence[counts.Length];
        for (int index = 0; index < counts.Length; index++)
            result[index] = fixture.MeasureMixedCadence(counts[index], warmups, samples);
        return result;
    }

    private static int[] DensityCounts(int maximum)
    {
        int[] candidates = [1, 8, 32, 128, maximum];
        var result = new int[candidates.Length];
        int count = 0;
        for (int index = 0; index < candidates.Length; index++)
        {
            int value = Math.Min(candidates[index], maximum);
            if (count == 0 || result[count - 1] != value) result[count++] = value;
        }
        return result[..count];
    }

    private static RoamingInterestEvidence MeasureRoaming(
        string path,
        int steps,
        int subscriptionCapacity,
        Func<int, (long X, long Z)> positionAt)
    {
        var source = new ChunkPublicationStore(
            PublicationEpoch.Create(), new ChunkPublicationLimits(1, 2, 1, 4));
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 1, 1),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        var changes = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
        var budgets = new int[steps + 1];
        (long initialX, long initialZ) = positionAt(0);
        Require(WorldPosition.TryFromGlobalMillimeters(initialX, 0, initialZ, out WorldPosition anchor));
        Require(recipient.Reconcile(
            source, GameWorldState.DefaultSpace, anchor, changes,
            out OfflineDirectChunkReconcileResult initial) == OfflineDirectChunkReconcileStatus.Reconciled);
        OfflineDirectChunkRecipientSnapshot before = recipient.GetSnapshot();
        int budgetCount = 1;
        budgets[0] = before.StableInterestMovementBudgetMillimeters;
        long totalChanges = 0;
        int maximumChanges = 0;

        for (int step = 1; step <= steps; step++)
        {
            (long x, long z) = positionAt(step);
            Require(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out anchor));
            ulong fullBefore = recipient.GetSnapshot().FullInterestQueries;
            Require(recipient.Reconcile(
                source, GameWorldState.DefaultSpace, anchor, changes,
                out OfflineDirectChunkReconcileResult reconciled) ==
                OfflineDirectChunkReconcileStatus.Reconciled);
            OfflineDirectChunkRecipientSnapshot snapshot = recipient.GetSnapshot();
            if (snapshot.FullInterestQueries != fullBefore)
                budgets[budgetCount++] = snapshot.StableInterestMovementBudgetMillimeters;
            totalChanges += reconciled.ChangeCount;
            maximumChanges = Math.Max(maximumChanges, reconciled.ChangeCount);
        }

        OfflineDirectChunkRecipientSnapshot after = recipient.GetSnapshot();
        Array.Sort(budgets, 0, budgetCount);
        ulong fullQueries = after.FullInterestQueries - before.FullInterestQueries;
        ulong fastPaths = after.StableInterestFastPaths - before.StableInterestFastPaths;
        return new RoamingInterestEvidence(
            path,
            steps,
            fullQueries,
            fastPaths,
            steps == 0 ? 0 : fullQueries / (double)steps,
            steps == 0 ? 0 : fastPaths / (double)steps,
            budgets[Percentile(budgetCount, .50)],
            budgets[Percentile(budgetCount, .95)],
            budgets[budgetCount - 1],
            totalChanges,
            maximumChanges,
            after.SubscriptionCount);
    }

    private static int Percentile(int count, double fraction) =>
        Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("N1.4 adoption diagnostic guard failed.");
    }

    private sealed class DirtyTopologyFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _entities;
        private readonly SpatialChunkKey[] _targetKeys;
        private readonly ChunkPublicationStore _source;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly WorldPublicChange[] _changes;
        private readonly OfflineDirectChunkSubscriptionChange[] _reconcileChanges;
        private readonly N14DirtyTopology _topology;
        private readonly int _recipients;
        private bool _side;
        private ulong _cadenceStep;

        internal DirtyTopologyFixture(
            int recipients,
            int subscriptionCapacity,
            N14DirtyTopology topology,
            OfflineDirectChunkCadence cadence)
        {
            if (recipients <= 0) throw new ArgumentOutOfRangeException(nameof(recipients));
            _reconcileChanges = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
            _recipients = recipients;
            _topology = topology;
            WorldPosition[] anchors = BuildAnchors(recipients, topology);
            _targetKeys = BuildTargetKeys(anchors, topology);
            int capacity = _targetKeys.Length;
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(
                    new EntityCapacity(capacity), 2, 4,
                    lifecycleRecordCapacity: checked(capacity * 2)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 4);
            _world.Seal();
            _world.Initialize();
            _entities = new Entity[capacity];
            _changes = new WorldPublicChange[capacity];
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character,
                WorldYaw.Zero,
                WorldPublicStateFlags.Visible);
            for (int index = 0; index < capacity; index++)
            {
                Require(_world.TryCreate(empty, out _entities[index]));
                _changes[index] = WorldPublicChange.Present(
                    _entities[index], Position(_targetKeys[index], 8_000), descriptor);
            }
            _source = new ChunkPublicationStore(
                PublicationEpoch.Create(),
                new ChunkPublicationLimits(
                    capacity,
                    checked(capacity * 2),
                    1,
                    checked(capacity * 32)));
            Require(_source.TryCommit(_changes, out _));
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(
                    recipients,
                    checked(recipients * subscriptionCapacity)));

            for (int index = 0; index < recipients; index++)
            {
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 1, 1),
                    GameChunkInterestProfile.Default,
                    cadence);
                Require(_scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
                Require(recipient.Reconcile(
                    _source,
                    GameWorldState.DefaultSpace,
                    anchors[index],
                    _reconcileChanges,
                    out OfflineDirectChunkReconcileResult result) ==
                    OfflineDirectChunkReconcileStatus.Reconciled);
                Require(_scheduler.TryApplyReconciliation(
                    id,
                    _reconcileChanges.AsSpan(0, result.ChangeCount),
                    result));
            }
            int incoming = 0;
            while (_scheduler.TryDequeueDue(0, out _)) incoming++;
            Require(incoming == _scheduler.GetSnapshot().MembershipCount);
        }

        internal int TargetChunkCount => _targetKeys.Length;

        internal DirtyDensityEvidence MeasureDensity(int dirtyChunks, int warmups, int samples)
        {
            var observations = new double[samples];
            var drains = new double[samples];
            ulong fanout = 0;
            ulong examined = 0;
            ulong deferrals = 0;
            int maximumPending = 0;
            long maximumAllocated = 0;
            for (int iteration = 0; iteration < warmups + samples; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                CommitDirty(dirtyChunks);
                ChunkPublicationWorkSchedulerSnapshot before = _scheduler.GetSnapshot();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Require(_scheduler.ObserveCommit(_source) ==
                    ChunkPublicationCommitObservationStatus.Observed);
                long observed = Stopwatch.GetTimestamp();
                ChunkPublicationWorkSchedulerSnapshot queued = _scheduler.GetSnapshot();
                int dequeued = DrainDue(_cadenceStep++, 1);
                long stopped = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                ChunkPublicationWorkSchedulerSnapshot after = _scheduler.GetSnapshot();
                Require(after.PendingWorkCount == 0);
                if (iteration < warmups) continue;
                int sample = iteration - warmups;
                observations[sample] = ToNanoseconds(observed - started);
                drains[sample] = ToNanoseconds(stopped - observed);
                fanout += queued.DirtyRecipientFanout - before.DirtyRecipientFanout;
                examined += after.PendingMembershipsExamined - before.PendingMembershipsExamined;
                deferrals += after.CadenceDeferrals - before.CadenceDeferrals;
                maximumPending = Math.Max(maximumPending, queued.PendingWorkCount);
                maximumAllocated = Math.Max(maximumAllocated, allocated);
                Require(dequeued == queued.PendingWorkCount);
            }
            Array.Sort(observations);
            Array.Sort(drains);
            double averageFanout = fanout / (double)samples;
            return new DirtyDensityEvidence(
                _topology.ToString(),
                _recipients,
                _targetKeys.Length,
                dirtyChunks,
                dirtyChunks / (double)_targetKeys.Length,
                averageFanout,
                observations[Percentile(samples, .50)],
                observations[Percentile(samples, .95)],
                drains[Percentile(samples, .50)],
                drains[Percentile(samples, .95)],
                averageFanout == 0 ? 0 :
                    drains[Percentile(samples, .95)] / averageFanout,
                examined / (double)samples,
                deferrals / (double)samples,
                maximumPending,
                maximumAllocated,
                _scheduler.GetSnapshot().ReservedBytes);
        }

        internal MixedCadenceTraversalEvidence MeasureMixedCadence(
            int dirtyChunks,
            int warmups,
            int samples)
        {
            var elapsed = new double[samples];
            ulong fanout = 0;
            ulong examined = 0;
            ulong deferrals = 0;
            ulong dequeuedTotal = 0;
            int maximumPending = 0;
            long maximumAllocated = 0;
            for (int iteration = 0; iteration < warmups + samples; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                CommitDirty(dirtyChunks);
                ChunkPublicationWorkSchedulerSnapshot before = _scheduler.GetSnapshot();
                Require(_scheduler.ObserveCommit(_source) ==
                    ChunkPublicationCommitObservationStatus.Observed);
                ChunkPublicationWorkSchedulerSnapshot queued = _scheduler.GetSnapshot();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                int dequeued = DrainDue(_cadenceStep, 4);
                _cadenceStep += 4;
                long stopped = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                ChunkPublicationWorkSchedulerSnapshot after = _scheduler.GetSnapshot();
                Require(after.PendingWorkCount == 0 && dequeued == queued.PendingWorkCount);
                if (iteration < warmups) continue;
                int sample = iteration - warmups;
                elapsed[sample] = ToNanoseconds(stopped - started);
                fanout += queued.DirtyRecipientFanout - before.DirtyRecipientFanout;
                examined += after.PendingMembershipsExamined - before.PendingMembershipsExamined;
                deferrals += after.CadenceDeferrals - before.CadenceDeferrals;
                dequeuedTotal += after.DueWorkDequeued - before.DueWorkDequeued;
                maximumPending = Math.Max(maximumPending, queued.PendingWorkCount);
                maximumAllocated = Math.Max(maximumAllocated, allocated);
            }
            Array.Sort(elapsed);
            double averageDequeued = dequeuedTotal / (double)samples;
            return new MixedCadenceTraversalEvidence(
                _recipients,
                _targetKeys.Length,
                dirtyChunks,
                dirtyChunks / (double)_targetKeys.Length,
                fanout / (double)samples,
                averageDequeued,
                examined / (double)samples,
                averageDequeued == 0 ? 0 : examined / (double)dequeuedTotal,
                deferrals / (double)samples,
                elapsed[Percentile(samples, .50)],
                elapsed[Percentile(samples, .95)],
                averageDequeued == 0 ? 0 : elapsed[Percentile(samples, .95)] / averageDequeued,
                maximumPending,
                maximumAllocated);
        }

        private void CommitDirty(int count)
        {
            _side = !_side;
            ushort localX = checked((ushort)(_side ? 8_001 : 8_000));
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character,
                WorldYaw.Zero,
                WorldPublicStateFlags.Visible);
            for (int index = 0; index < count; index++)
                _changes[index] = WorldPublicChange.Present(
                    _entities[index], Position(_targetKeys[index], localX), descriptor);
            Require(_source.TryCommit(_changes.AsSpan(0, count), out _));
        }

        private int DrainDue(ulong firstStep, int steps)
        {
            int count = 0;
            for (ulong offset = 0; offset < checked((ulong)steps); offset++)
                while (_scheduler.TryDequeueDue(firstStep + offset, out _)) count++;
            return count;
        }

        private static WorldPosition[] BuildAnchors(int recipients, N14DirtyTopology topology)
        {
            var anchors = new WorldPosition[recipients];
            for (int index = 0; index < recipients; index++)
            {
                int chunkX = topology == N14DirtyTopology.SharedWindow ? 0 : checked(index * 32);
                anchors[index] = new WorldPosition(chunkX, 0, 8_000, 8_000, 0);
            }
            return anchors;
        }

        private static SpatialChunkKey[] BuildTargetKeys(
            WorldPosition[] anchors,
            N14DirtyTopology topology)
        {
            if (topology == N14DirtyTopology.DispersedWindows)
            {
                var keys = new SpatialChunkKey[anchors.Length];
                for (int index = 0; index < keys.Length; index++)
                    keys[index] = new SpatialChunkKey(GameWorldState.DefaultSpace, anchors[index].Chunk);
                return keys;
            }

            var interest = new ChunkInterestEntry[1_024];
            Require(ChunkInterestQuery.TryQuery(
                GameWorldState.DefaultSpace,
                anchors[0],
                GameChunkInterestProfile.Default,
                ReadOnlySpan<ChunkInterestEntry>.Empty,
                interest,
                out int count,
                out _) == ChunkInterestQueryStatus.Success);
            var result = new SpatialChunkKey[count];
            for (int index = 0; index < count; index++) result[index] = interest[index].Key;
            return result;
        }

        private static WorldPosition Position(SpatialChunkKey key, ushort localX) =>
            new(key.Coord.X, key.Coord.Z, localX, 8_000, 0);

        public void Dispose() => _world.Dispose();
    }

    private static double ToNanoseconds(long stopwatchTicks) =>
        stopwatchTicks * 1_000_000_000d / Stopwatch.Frequency;
}

internal readonly record struct DirtyDensityEvidence(
    string Topology,
    int Recipients,
    int TargetPublicationChunks,
    int DirtyChunks,
    double DirtyFraction,
    double AverageDirtyRecipientFanout,
    double ObserveP50Nanoseconds,
    double ObserveP95Nanoseconds,
    double DrainP50Nanoseconds,
    double DrainP95Nanoseconds,
    double DrainP95NanosecondsPerWork,
    double AveragePendingMembershipsExamined,
    double AverageCadenceDeferrals,
    int MaximumPendingWork,
    long MaximumAllocatedBytes,
    long SchedulerReservedBytes);

internal readonly record struct MixedCadenceTraversalEvidence(
    int Recipients,
    int TargetPublicationChunks,
    int DirtyChunks,
    double DirtyFraction,
    double AverageDirtyRecipientFanout,
    double AverageDueDequeued,
    double AveragePendingMembershipsExamined,
    double AveragePendingMembershipsExaminedPerWork,
    double AverageCadenceDeferrals,
    double DrainP50Nanoseconds,
    double DrainP95Nanoseconds,
    double DrainP95NanosecondsPerWork,
    int MaximumPendingWork,
    long MaximumAllocatedBytes);

internal readonly record struct RoamingInterestEvidence(
    string Path,
    int AuthoritySteps,
    ulong FullInterestQueries,
    ulong StableInterestFastPaths,
    double FullQueryRate,
    double StableFastPathRate,
    int StableBudgetP50Millimeters,
    int StableBudgetP95Millimeters,
    int StableBudgetMaximumMillimeters,
    long TopologyChanges,
    int MaximumChangesPerStep,
    int FinalSubscriptionCount);

internal readonly record struct N14AdoptionEvidence(
    int DirtyDensityRecipients,
    int MixedCadenceRecipients,
    int RoamingSteps,
    DirtyDensityEvidence[] SharedWindowDirtyDensity,
    DirtyDensityEvidence[] DispersedWindowDirtyDensity,
    MixedCadenceTraversalEvidence[] MixedCadenceTraversal,
    RoamingInterestEvidence[] RoamingInterest,
    string Scope);
