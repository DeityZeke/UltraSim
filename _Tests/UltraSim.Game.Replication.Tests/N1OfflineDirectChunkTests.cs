using System.Buffers.Binary;
using System.Runtime.InteropServices;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication.Tests;

internal static class N1OfflineDirectChunkTests
{
    private static readonly ChunkInterestProfile Interest = new(
        1_000, 1_500,
        15_000, 15_500,
        23_000, 23_500);

    internal static ReplicationProfileConformanceObservation CaptureProfileConformance()
    {
        using var fixture = new Fixture(historyCapacity: 8, entityCount: 2);
        WorldPosition firstPosition = Position(0, 0, 2_000, 2_000);
        WorldPosition currentPosition = Position(0, 0, 2_100, 2_000);
        SpatialChunkKey includedKey = Key(0, 0);
        SpatialChunkKey excludedKey = Key(10, 10);
        fixture.Present(0, firstPosition, GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(10, 10, 2_000, 2_000), GameWorldPublicPrefabIds.WoodlandAnimal);

        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 4, 4),
            Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(
                fixture.Store,
                GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000),
                changes,
                out _));
        int included = FindSubscription(recipient, includedKey);
        bool disclosure = !ContainsSubscription(recipient, excludedKey);

        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, included, 0, out OfflineDirectChunkRead first));
        True(first.Snapshot.Length == 1);
        WorldPublicRecord firstRecord = first.Snapshot[0];
        OfflineDirectChunkDeliveryTicket lost = recipient.ReleaseReadForRemote(1);
        bool sentDidNotAdvance = !recipient.Subscriptions[included].HasKnowledge;

        fixture.Present(0, currentPosition, GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, included, 1, out OfflineDirectChunkRead current));
        True(current.Snapshot.Length == 1);
        WorldPublicRecord currentRecord = current.Snapshot[0];
        OfflineDirectChunkDeliveryTicket retry = recipient.ReleaseReadForRemote(2);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipient.CommitAppliedProof(retry));
        bool staleProofFenced = recipient.CommitAppliedProof(lost) ==
            OfflineDirectChunkAppliedProofStatus.Stale;
        OfflineDirectChunkSubscription applied = recipient.Subscriptions[included];

        return new ReplicationProfileConformanceObservation(
            GamePublicReplicationProfileId.ChunkPublication,
            currentRecord.Position == currentPosition &&
                currentRecord.LastChangedRevision == retry.Cursor.Revision,
            disclosure,
            firstRecord.Id == currentRecord.Id &&
                currentRecord.EntityPublicVersion > firstRecord.EntityPublicVersion,
            sentDidNotAdvance,
            staleProofFenced,
            applied.HasKnowledge && applied.Cursor == retry.Cursor,
            recipient.GetStorageSnapshot().TotalReservedBytes);
    }

    internal static void SubscriptionTopologyAndClientEquivalence()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(1, 0, 2_000, 3_000), GameWorldPublicPrefabIds.WoodlandAnimal);

        var n1 = new N1ClientLink(fixture, OfflineDirectChunkCadence.EvidenceProfile);
        var local = new DirectSnapshotClient(fixture);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        OfflineDirectChunkReconcileResult initial = n1.Synchronize(anchor, 0);
        local.Synchronize(anchor);
        Equal(initial.SubscriptionCount, initial.ChangeCount);
        True(initial.SourceReset);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);

        WorldPosition stable = Position(0, 0, 8_100, 8_000);
        OfflineDirectChunkReconcileResult sameWindow = n1.Synchronize(stable, 4);
        local.Synchronize(stable);
        Equal(0, sameWindow.ChangeCount);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);

        WorldPosition crossed = Position(1, 0, 100, 8_000);
        OfflineDirectChunkReconcileResult crossing = n1.Synchronize(crossed, 8);
        local.Synchronize(crossed);
        True(crossing.ChangeCount > 0 && crossing.ChangeCount < crossing.SubscriptionCount);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);
        True(n1.Recipient.GetSnapshot().ReservedBytes > 0);
    }

    internal static void MigrationOrderingPreservesOnePublicIdentity()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        SpatialChunkKey sourceKey = Key(0, 0);
        SpatialChunkKey destinationKey = Key(1, 0);
        fixture.Present(0, Position(0, 0, 14_000, 8_000), GameWorldPublicPrefabIds.Character);
        var n1 = new N1ClientLink(fixture, new OfflineDirectChunkCadence(1, 1, 1));
        var local = new DirectSnapshotClient(fixture);
        WorldPosition anchor = Position(0, 0, 15_000, 8_000);
        n1.Synchronize(anchor, 0);
        local.Synchronize(anchor);

        fixture.Present(0, Position(1, 0, 1_000, 8_000), GameWorldPublicPrefabIds.Character);
        n1.Synchronize(anchor, 1, reverseReads: true); // B Present before A Remove.
        local.Synchronize(anchor);
        AssertOneIdentityIn(n1.Cache, destinationKey);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);

        fixture.Present(0, Position(0, 0, 13_000, 8_000), GameWorldPublicPrefabIds.Character);
        n1.Synchronize(anchor, 2); // A Present before B Remove.
        local.Synchronize(anchor);
        AssertOneIdentityIn(n1.Cache, sourceKey);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);
    }

    internal static void HistoryFallbackAndEpochReplacementConverge()
    {
        using var fixture = new Fixture(historyCapacity: 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var n1 = new N1ClientLink(fixture, new OfflineDirectChunkCadence(1, 1, 1));
        var local = new DirectSnapshotClient(fixture);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        n1.Synchronize(anchor, 0);
        local.Synchronize(anchor);

        for (int update = 0; update < 8; update++)
            fixture.Present(0, Position(0, 0, 2_100 + update, 2_000), GameWorldPublicPrefabIds.Character);
        n1.Synchronize(anchor, 1);
        local.Synchronize(anchor);
        True(n1.Recipient.GetSnapshot().SnapshotFallbacks > 0);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);

        PublicationEpoch prior = fixture.Store.Epoch;
        fixture.ReplaceStore(historyCapacity: 2);
        fixture.Present(1, Position(1, 0, 3_000, 3_000), GameWorldPublicPrefabIds.WoodlandAnimal);
        OfflineDirectChunkReconcileResult reset = n1.Synchronize(anchor, 2, sourceResetExpected: true);
        local.Synchronize(anchor);
        True(reset.SourceReset && fixture.Store.Epoch != prior);
        Equal(fixture.Store.Epoch, n1.Cache.GetSnapshot().PublicationEpoch);
        Equal(n1.Cache.GetSnapshot().WorldSemanticHash, local.Cache.GetSnapshot().WorldSemanticHash);
        AssertOneIdentityIn(n1.Cache, Key(1, 0));
    }

    internal static void CapacityAndReadCommitAreTransactional()
    {
        using var fixture = new Fixture(historyCapacity: 8);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var tooSmall = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1, 4, 4), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> changes = stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.SubscriptionCapacityExceeded,
            tooSmall.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        Equal(0, tooSmall.GetSnapshot().SubscriptionCount);

        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 4, 4), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> none = [];
        Equal(OfflineDirectChunkReconcileStatus.ChangeDestinationTooSmall,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), none, out OfflineDirectChunkReconcileResult rejected));
        True(rejected.RequiredChanges > 0);
        Equal(0, recipient.GetSnapshot().SubscriptionCount);

        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        int occupied = FindSubscription(recipient, Key(0, 0));
        OfflineDirectChunkReadStatus status = recipient.TryBeginRead(fixture.Store, occupied, 0, out _);
        Equal(OfflineDirectChunkReadStatus.Snapshot, status);
        Equal(OfflineDirectChunkReconcileStatus.ReadPending,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        recipient.CancelRead();
        False(recipient.Subscriptions[occupied].HasKnowledge);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, occupied, 0, out _));
        recipient.CommitRead();
        True(recipient.Subscriptions[occupied].HasKnowledge);
    }

    internal static void WarmedStableRecipientAllocatesZero()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> changes = stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        Drain(recipient, fixture.Store, 0);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        Drain(recipient, fixture.Store, 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong step = 2; step < 102; step++)
        {
            Equal(OfflineDirectChunkReconcileStatus.Reconciled,
                recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
            Drain(recipient, fixture.Store, step);
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

        before = GC.GetAllocatedBytesForCurrentThread();
        recipient.ResetForReuse();
        Equal(0, recipient.GetSnapshot().SubscriptionCount);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        Drain(recipient, fixture.Store, 102);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    internal static void DirtySchedulerSkipsCleanScopesAndFansOutOnce()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(2, 128));
        var first = NewScheduledRecipient();
        var second = NewScheduledRecipient();
        True(scheduler.TryRegister(first, out ChunkReplicationRecipientId firstId));
        True(scheduler.TryRegister(second, out ChunkReplicationRecipientId secondId));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ApplyScheduledReconcile(scheduler, firstId, first, fixture.Store, anchor, changes);
        ApplyScheduledReconcile(scheduler, secondId, second, fixture.Store, anchor, changes);
        DrainScheduledReads(scheduler, first, firstId, second, secondId, fixture.Store, 0);

        ChunkPublicationWorkSchedulerSnapshot clean = scheduler.GetSnapshot();
        Equal(0, clean.PendingWorkCount);
        Equal(clean.MembershipCount, clean.CurrentCleanMembershipCount);
        Equal(ChunkPublicationCommitObservationStatus.NoNewCommit,
            scheduler.ObserveCommit(fixture.Store));
        Equal(0, scheduler.GetSnapshot().PendingWorkCount);

        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(1, fixture.Store.LastCommittedDirtyChunks.Length);
        Equal(Key(0, 0), fixture.Store.LastCommittedDirtyChunks[0]);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store));
        ChunkPublicationWorkSchedulerSnapshot dirty = scheduler.GetSnapshot();
        Equal(2, dirty.PendingWorkCount);
        Equal((ulong)1, dirty.DirtyChunksObserved);
        Equal((ulong)2, dirty.DirtyRecipientFanout);

        int due = 0;
        while (scheduler.TryDequeueDue(0, out ChunkPublicationWork work))
        {
            Equal(Key(0, 0), work.Key);
            Equal(ChunkPublicationWorkKind.Dirty, work.Kind);
            OfflineDirectChunkRecipient recipient = work.Recipient == firstId ? first : second;
            int subscription = FindSubscription(recipient, work.Key);
            Equal(OfflineDirectChunkReadStatus.Delta,
                recipient.TryBeginRead(fixture.Store, subscription, 0, out _));
            recipient.CommitRead();
            due++;
        }
        Equal(2, due);
        Equal(0, scheduler.GetSnapshot().PendingWorkCount);

        fixture.CommitEmpty();
        Equal(0, fixture.Store.LastCommittedDirtyChunks.Length);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store));
        Equal(0, scheduler.GetSnapshot().PendingWorkCount);
    }

    internal static void StableInterestBudgetSkipsOnlyProvablyUnchangedTopology()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default,
            new OfflineDirectChunkCadence(1, 1, 1));
        var desired = new ChunkInterestEntry[1_024];
        var oracle = new ChunkInterestEntry[1_024];
        var changes = new OfflineDirectChunkSubscriptionChange[1_024];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(
                GameWorldState.DefaultSpace,
                anchor,
                GameChunkInterestProfile.Default,
                [],
                desired,
                out int expectedCount,
                out _,
                out int stableBudget));
        True(stableBudget > 0);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        Equal(expectedCount, recipient.Subscriptions.Length);
        OfflineDirectChunkRecipientSnapshot initial = recipient.GetSnapshot();

        True(WorldPosition.TryFromGlobalMillimeters(
            anchor.GlobalXMillimeters + stableBudget,
            anchor.YMillimeters,
            anchor.GlobalZMillimeters,
            out WorldPosition within));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, within, changes,
                out OfflineDirectChunkReconcileResult unchanged));
        Equal(0, unchanged.ChangeCount);
        OfflineDirectChunkRecipientSnapshot fast = recipient.GetSnapshot();
        Equal(initial.FullInterestQueries, fast.FullInterestQueries);
        Equal(initial.StableInterestFastPaths + 1, fast.StableInterestFastPaths);

        Equal(ChunkInterestQueryStatus.Success,
            ChunkInterestQuery.TryQuery(
                GameWorldState.DefaultSpace,
                within,
                GameChunkInterestProfile.Default,
                desired.AsSpan(0, expectedCount),
                oracle,
                out int oracleCount,
                out _,
                out _));
        Equal(expectedCount, oracleCount);

        True(WorldPosition.TryFromGlobalMillimeters(
            anchor.GlobalXMillimeters + stableBudget + 1L,
            anchor.YMillimeters,
            anchor.GlobalZMillimeters,
            out WorldPosition outside));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, outside, changes, out _));
        Equal(fast.FullInterestQueries + 1, recipient.GetSnapshot().FullInterestQueries);
    }

    internal static void StableInterestBudgetMatchesRandomizedExactOracle()
    {
        var previous = new ChunkInterestEntry[1_024];
        var current = new ChunkInterestEntry[1_024];
        var oracle = new ChunkInterestEntry[1_024];
        int previousCount = 0;
        var random = new Random(0x1A04_2026);
        True(WorldPosition.TryFromGlobalMillimeters(8_000, 0, 8_000, out WorldPosition anchor));

        for (int iteration = 0; iteration < 2_000; iteration++)
        {
            if (iteration % 11 == 0)
            {
                long x = random.Next(-160_000, 160_001);
                long z = random.Next(-160_000, 160_001);
                True(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out anchor));
            }
            Equal(ChunkInterestQueryStatus.Success,
                ChunkInterestQuery.TryQuery(
                    GameWorldState.DefaultSpace,
                    anchor,
                    GameChunkInterestProfile.Default,
                    previous.AsSpan(0, previousCount),
                    current,
                    out int currentCount,
                    out _,
                    out int budget));

            int deltaX = budget == 0 ? 0 : random.Next(-budget, budget + 1);
            int remaining = budget - Math.Abs(deltaX);
            int deltaZ = remaining == 0 ? 0 : random.Next(-remaining, remaining + 1);
            True(WorldPosition.TryFromGlobalMillimeters(
                anchor.GlobalXMillimeters + deltaX,
                0,
                anchor.GlobalZMillimeters + deltaZ,
                out WorldPosition moved));
            Equal(ChunkInterestQueryStatus.Success,
                ChunkInterestQuery.TryQuery(
                    GameWorldState.DefaultSpace,
                    moved,
                    GameChunkInterestProfile.Default,
                    current.AsSpan(0, currentCount),
                    oracle,
                    out int oracleCount,
                    out _));
            True(current.AsSpan(0, currentCount).SequenceEqual(oracle.AsSpan(0, oracleCount)));

            (previous, oracle) = (oracle, previous);
            previousCount = oracleCount;
            anchor = moved;
        }
    }

    internal static void DirtySchedulerFencesTopologySourceGapsAndStaleRecipients()
    {
        using var fixture = new Fixture(historyCapacity: 64);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var recipient = NewScheduledRecipient();
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 64));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition firstAnchor = Position(0, 0, 8_000, 8_000);
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store, firstAnchor, changes);
        DrainScheduledReads(scheduler, recipient, id, null, default, fixture.Store, 0);

        WorldPosition crossed = Position(1, 0, 100, 8_000);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, crossed, changes,
                out OfflineDirectChunkReconcileResult crossing));
        True(crossing.ChangeCount > 0);
        True(scheduler.TryApplyReconciliation(id, changes[..crossing.ChangeCount], crossing));
        ChunkPublicationWorkSchedulerSnapshot afterCrossing = scheduler.GetSnapshot();
        Equal(crossing.SubscriptionCount, afterCrossing.MembershipCount);
        True(afterCrossing.PendingWorkCount > 0);
        DrainScheduledReads(scheduler, recipient, id, null, default, fixture.Store, 1);

        fixture.Present(0, Position(0, 0, 2_200, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(0, Position(0, 0, 2_300, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(ChunkPublicationCommitObservationStatus.CommitGapRequiresRefresh,
            scheduler.ObserveCommit(fixture.Store));
        scheduler.RefreshAllFromCurrentSource();
        Equal(recipient.Subscriptions.Length, scheduler.GetSnapshot().PendingWorkCount);
        DrainScheduledReads(scheduler, recipient, id, null, default, fixture.Store, 4);

        fixture.ReplaceStore(historyCapacity: 64);
        scheduler.ReplaceSource(fixture.Store);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, crossed, changes,
                out OfflineDirectChunkReconcileResult reset));
        True(reset.SourceReset);
        True(scheduler.TryApplyReconciliation(id, changes[..reset.ChangeCount], reset));
        Equal(recipient.Subscriptions.Length, scheduler.GetSnapshot().PendingWorkCount);

        True(scheduler.RemoveRecipient(id));
        Equal(0, scheduler.GetSnapshot().MembershipCount);
        False(scheduler.Requeue(new ChunkPublicationWork(
            id, Key(0, 0), ChunkInterestTier.Immediate, ChunkPublicationWorkKind.Dirty)));
        var replacement = NewScheduledRecipient();
        True(scheduler.TryRegister(replacement, out ChunkReplicationRecipientId replacementId));
        True(replacementId.Index == id.Index && replacementId.Generation > id.Generation);
    }

    internal static void ReconcileAttributionIsExplicitAndDormantByDefault()
    {
        using var fixture = new Fixture(historyCapacity: 16);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        var changes = new OfflineDirectChunkSubscriptionChange[1_024];

        recipient.ResetReconcileAttribution(enabled: true);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_100, 8_000), changes, out _));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(1, 0, 8_000, 8_000), changes, out _));
        OfflineDirectChunkReconcileAttribution attributed =
            recipient.GetReconcileAttribution();
        True(attributed.Enabled);
        Equal(2UL, attributed.ExactQueries);
        Equal(1UL, attributed.StableFastPaths);
        True(attributed.ExactQueryTicks > 0);
        True(attributed.NextBankBuildTicks > 0);

        recipient.ResetReconcileAttribution(enabled: false);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(1, 0, 8_100, 8_000), changes, out _));
        OfflineDirectChunkReconcileAttribution dormant = recipient.GetReconcileAttribution();
        False(dormant.Enabled);
        Equal(0UL, dormant.ExactQueries);
        Equal(0UL, dormant.StableFastPaths);
        Equal(0UL, dormant.ExactQueryTicks);
    }

    internal static void DirtySchedulerWarmedWorkDiscoveryAllocatesZero()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        var recipient = NewScheduledRecipient();
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 64));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store,
            Position(0, 0, 8_000, 8_000), changes);
        DrainScheduledWorkOnly(scheduler, 0);

        for (int warm = 0; warm < 8; warm++)
        {
            scheduler.RefreshAllFromCurrentSource();
            DrainScheduledWorkOnly(scheduler, checked((ulong)warm + 1));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++)
        {
            scheduler.RefreshAllFromCurrentSource();
            DrainScheduledWorkOnly(scheduler, checked((ulong)iteration + 32));
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    internal static void DirtySchedulerDoesNotRescanDeferredCadencePrefix()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 4);
        var cadence = OfflineDirectChunkCadence.EvidenceProfile;
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default,
            cadence);
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 1_024));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        var changes = new OfflineDirectChunkSubscriptionChange[1_024];
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store,
            Position(0, 0, 8_000, 8_000), changes);
        DrainScheduledWorkOnly(scheduler, 0);

        int queued = 0;
        for (int subscription = 0; subscription < recipient.Subscriptions.Length && queued < 4;
             subscription++)
        {
            OfflineDirectChunkSubscription candidate = recipient.Subscriptions[subscription];
            uint period = candidate.Interest.Tier switch
            {
                ChunkInterestTier.Immediate => cadence.ImmediateSteps,
                ChunkInterestTier.Near => cadence.NearSteps,
                ChunkInterestTier.Far => cadence.FarSteps,
                _ => throw new InvalidOperationException(),
            };
            if (candidate.CadencePhase == 0 || period == 1) continue;
            SpatialChunkKey key = candidate.Interest.Key;
            fixture.Present(
                queued,
                Position(key.Coord.X, key.Coord.Z, 8_000, 8_000),
                GameWorldPublicPrefabIds.Character);
            Equal(ChunkPublicationCommitObservationStatus.Observed,
                scheduler.ObserveCommit(fixture.Store));
            queued++;
        }
        Equal(4, queued);
        Equal(4, scheduler.GetSnapshot().PendingWorkCount);

        ChunkPublicationWorkSchedulerSnapshot before = scheduler.GetSnapshot();
        False(scheduler.TryDequeueDue(0, out _));
        ChunkPublicationWorkSchedulerSnapshot firstPass = scheduler.GetSnapshot();
        Equal((ulong)4,
            firstPass.PendingMembershipsExamined - before.PendingMembershipsExamined);
        False(scheduler.TryDequeueDue(0, out _));
        ChunkPublicationWorkSchedulerSnapshot repeated = scheduler.GetSnapshot();
        Equal((ulong)0,
            repeated.PendingMembershipsExamined - firstPass.PendingMembershipsExamined);

        int due = 0;
        for (ulong step = 1; step <= 3; step++)
            while (scheduler.TryDequeueDue(step, out _)) due++;
        Equal(4, due);
        Equal(0, scheduler.GetSnapshot().PendingWorkCount);
    }

    internal static void DirtySchedulerCompactIndexesSurviveDeterministicChurn()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        const int recipientCount = 4;
        var recipients = new OfflineDirectChunkRecipient[recipientCount];
        var ids = new ChunkReplicationRecipientId[recipientCount];
        var anchors = new WorldPosition[recipientCount];
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store,
            new ChunkPublicationWorkSchedulerLimits(recipientCount, recipientCount * 64));
        ChunkPublicationWorkSchedulerStorageSnapshot schedulerStorage =
            scheduler.GetStorageSnapshot();
        Equal(16, schedulerStorage.RecipientStateBytes);
        Equal(40, schedulerStorage.MembershipStateBytes);
        Equal(24, schedulerStorage.ChunkStateBytes);
        Equal(4, schedulerStorage.MembershipIndexSlotBytes);
        Equal(512, schedulerStorage.MembershipIndexCapacity);
        Equal(512, schedulerStorage.ChunkIndexCapacity);
        var changes = new OfflineDirectChunkSubscriptionChange[64];
        for (int index = 0; index < recipientCount; index++)
        {
            recipients[index] = NewScheduledRecipient();
            OfflineDirectChunkRecipientStorageSnapshot recipientStorage =
                recipients[index].GetStorageSnapshot();
            Equal(32, recipientStorage.RetainedSubscriptionBytes);
            Equal(64, recipientStorage.PublicSubscriptionViewBytes);
            True(scheduler.TryRegister(recipients[index], out ids[index]));
            anchors[index] = Position(index * 3, index * -2, 8_000, 8_000);
            ApplyScheduledReconcile(
                scheduler, ids[index], recipients[index], fixture.Store, anchors[index], changes);
        }
        DrainScheduledWorkOnly(scheduler, 0);

        uint random = 0xC0FFEEu;
        for (int operation = 1; operation <= 2_000; operation++)
        {
            random = unchecked(random * 1_664_525u + 1_013_904_223u);
            int selected = (int)(random % recipientCount);
            if (operation % 97 == 0)
            {
                ChunkReplicationRecipientId stale = ids[selected];
                True(scheduler.RemoveRecipient(stale));
                False(scheduler.Requeue(new ChunkPublicationWork(
                    stale, Key(0, 0), ChunkInterestTier.Immediate,
                    ChunkPublicationWorkKind.Dirty)));
                recipients[selected] = NewScheduledRecipient();
                True(scheduler.TryRegister(recipients[selected], out ids[selected]));
                True(ids[selected].Index == stale.Index && ids[selected].Generation > stale.Generation);
            }

            random = unchecked(random * 1_664_525u + 1_013_904_223u);
            int chunkX = (int)(random % 129) - 64;
            random = unchecked(random * 1_664_525u + 1_013_904_223u);
            int chunkZ = (int)(random % 129) - 64;
            anchors[selected] = Position(chunkX, chunkZ, 8_000, 8_000);
            ApplyScheduledReconcile(
                scheduler, ids[selected], recipients[selected], fixture.Store,
                anchors[selected], changes);

            if (operation % 10 != 0) continue;
            int expectedMemberships = 0;
            var uniqueChunks = new HashSet<SpatialChunkKey>();
            for (int recipientIndex = 0; recipientIndex < recipientCount; recipientIndex++)
            {
                OfflineDirectChunkSubscriptionView subscriptions =
                    recipients[recipientIndex].Subscriptions;
                expectedMemberships += subscriptions.Length;
                for (int subscriptionIndex = 0;
                     subscriptionIndex < subscriptions.Length;
                     subscriptionIndex++)
                {
                    OfflineDirectChunkSubscription subscription = subscriptions[subscriptionIndex];
                    uniqueChunks.Add(subscription.Interest.Key);
                    True(scheduler.Requeue(new ChunkPublicationWork(
                        ids[recipientIndex],
                        subscription.Interest.Key,
                        subscription.Interest.Tier,
                        ChunkPublicationWorkKind.Dirty)));
                }
            }
            ChunkPublicationWorkSchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Equal(expectedMemberships, snapshot.MembershipCount);
            Equal(uniqueChunks.Count, snapshot.SharedChunkCount);
            Equal(expectedMemberships, snapshot.PendingWorkCount);

            int drained = 0;
            while (scheduler.TryDequeueDue(checked((ulong)operation), out ChunkPublicationWork work))
            {
                True(work.Recipient.IsValid);
                True(FindSubscription(recipients[work.Recipient.Index], work.Key) >= 0);
                drained++;
            }
            Equal(expectedMemberships, drained);
            Equal(0, scheduler.GetSnapshot().PendingWorkCount);
        }
    }

    internal static void DirtySchedulerFeedsAcceptedRemoteChunkKeyPath()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var recipient = NewScheduledRecipient();
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 64));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store,
            Position(0, 0, 8_000, 8_000), changes);
        DrainScheduledReads(scheduler, recipient, id, null, default, fixture.Store, 0);

        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store));
        True(scheduler.TryDequeueDue(1, out ChunkPublicationWork work));
        Equal(Key(0, 0), work.Key);

        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? loopback,
            out _,
            UltraSimGameProtocol.Manifest));
        using ProtocolLoopback established = loopback!;
        var server = new RemoteChunkWorldStateServer(
            established.Server,
            recipient,
            fixture.Store,
            applicationSessionEpoch: 91,
            limits: new RemoteChunkWorldStateTransportLimits(8));
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(work.Key, 1));
        Equal(1, server.GetSnapshot().OutstandingDeliveries);
    }

    internal static void ChunkWireCodecsAreCanonicalTransactionalAndHostileSafe()
    {
        RemoteChunkPublicRecord[] snapshot =
        [
            new(2, 1, 7, 11, 100, 200, -300, 16_384, 1, 5),
            new(8, 3, 9, 12, 15_999, 0, 42, 49_152, 0, 6),
        ];
        byte[] bytes = new byte[4_096];
        True(UltraSimGameProtocol.TryWriteChunkSnapshot(
            bytes, 10, 20, 30, 1, -4, 7, 8, 4, snapshot, out int snapshotBytes));
        var decodedSnapshot = new RemoteChunkPublicRecord[2];
        True(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes), out RemoteChunkSnapshotHeader snapshotHeader,
            decodedSnapshot, out int snapshotCount));
        Equal(2, snapshotCount);
        True(snapshot.AsSpan().SequenceEqual(decodedSnapshot));
        Equal(8UL, snapshotHeader.Revision);

        RemoteChunkPublicRecord[] snapshotSentinel =
        [new(99, 9, 9, 9, 9, 9, 9, 9, 1, 9), new(100, 9, 9, 9, 9, 9, 9, 9, 1, 9)];
        RemoteChunkPublicRecord[] snapshotBefore = snapshotSentinel.ToArray();
        bytes[snapshotBytes - 1] ^= 0x40;
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes), out _, snapshotSentinel, out _));
        True(snapshotBefore.AsSpan().SequenceEqual(snapshotSentinel));
        bytes[snapshotBytes - 1] ^= 0x40;
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes - 1), out _, snapshotSentinel, out _));
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes + 1), out _, snapshotSentinel, out _));
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes), out _, snapshotSentinel.AsSpan(0, 1), out _));

        // Header schema/count and canonical row validation are independent hostile gates.
        bytes[40] ^= 1;
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes), out _, snapshotSentinel, out _));
        True(snapshotBefore.AsSpan().SequenceEqual(snapshotSentinel));
        bytes[40] ^= 1;
        BinaryPrimitives.WriteUInt16BigEndian(
            bytes.AsSpan(RemoteChunkSnapshotHeaderCodec.EncodedSize - sizeof(ushort)), 65);
        False(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshotBytes), out _, snapshotSentinel, out _));
        True(snapshotBefore.AsSpan().SequenceEqual(snapshotSentinel));
        BinaryPrimitives.WriteUInt16BigEndian(
            bytes.AsSpan(RemoteChunkSnapshotHeaderCodec.EncodedSize - sizeof(ushort)), 2);

        byte[] rejectedDestination = Enumerable.Repeat((byte)0xA5, 256).ToArray();
        byte[] rejectedBefore = rejectedDestination.ToArray();
        RemoteChunkPublicRecord[] duplicateSnapshot = [snapshot[0], snapshot[0]];
        False(UltraSimGameProtocol.TryWriteChunkSnapshot(
            rejectedDestination, 10, 20, 30, 1, -4, 7, 8, 4, duplicateSnapshot, out _));
        True(rejectedBefore.AsSpan().SequenceEqual(rejectedDestination));
        RemoteChunkPublicRecord[] invalidSnapshot =
            [snapshot[0] with { StateFlags = 2, LastChangedRevision = 9 }];
        False(UltraSimGameProtocol.TryWriteChunkSnapshot(
            rejectedDestination, 10, 20, 30, 1, -4, 7, 8, 4, invalidSnapshot, out _));
        True(rejectedBefore.AsSpan().SequenceEqual(rejectedDestination));

        byte[] overlap = new byte[256];
        Span<RemoteChunkPublicRecord> overlappingRecords =
            MemoryMarshal.Cast<byte, RemoteChunkPublicRecord>(overlap);
        overlappingRecords[0] = snapshot[0];
        byte[] overlapBefore = overlap.ToArray();
        False(UltraSimGameProtocol.TryWriteChunkSnapshot(
            overlap, 10, 20, 30, 1, 0, 0, 8, 4, overlappingRecords[..1], out _));
        True(overlapBefore.AsSpan().SequenceEqual(overlap));

        RemoteChunkDeltaRecord[] deltas =
        [
            new(RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Transform,
                2, 1, 8, 11, 101, 201, -299, 16_384, 1, 9),
            new(RemoteChunkPublicOperation.Remove, RemoteChunkPublicFieldMask.None,
                8, 3, 10, 0, 0, 0, 0, 0, 0, 0),
        ];
        True(UltraSimGameProtocol.TryWriteChunkDelta(
            bytes, 10, 20, 31, 1, -4, 7, 8, 10, 4, deltas, out int deltaBytes));
        var decodedDeltas = new RemoteChunkDeltaRecord[2];
        True(UltraSimGameProtocol.TryReadChunkDelta(
            bytes.AsSpan(0, deltaBytes), out RemoteChunkDeltaHeader deltaHeader,
            decodedDeltas, out int deltaCount));
        Equal(2, deltaCount);
        True(deltas.AsSpan().SequenceEqual(decodedDeltas));
        Equal(8UL, deltaHeader.BaseRevision);
        Equal(10UL, deltaHeader.CurrentRevision);

        RemoteChunkDeltaRecord[] deltaSentinel =
        [new(RemoteChunkPublicOperation.Remove, default, 99, 9, 9, 0, 0, 0, 0, 0, 0, 0)];
        RemoteChunkDeltaRecord[] deltaBefore = deltaSentinel.ToArray();
        bytes[deltaBytes - 1] ^= 1;

        RemoteChunkDeltaRecord[] duplicateDeltas = [deltas[0], deltas[0]];
        False(UltraSimGameProtocol.TryWriteChunkDelta(
            rejectedDestination, 10, 20, 31, 1, -4, 7, 8, 10, 4, duplicateDeltas, out _));
        True(rejectedBefore.AsSpan().SequenceEqual(rejectedDestination));
        RemoteChunkDeltaRecord[] invalidDeltas =
            [deltas[0] with { Operation = (RemoteChunkPublicOperation)99,
                Fields = (RemoteChunkPublicFieldMask)0x8000 }];
        False(UltraSimGameProtocol.TryWriteChunkDelta(
            rejectedDestination, 10, 20, 31, 1, -4, 7, 8, 10, 4, invalidDeltas, out _));
        True(rejectedBefore.AsSpan().SequenceEqual(rejectedDestination));
        byte[] deltaOverlap = new byte[256];
        Span<RemoteChunkDeltaRecord> overlappingDeltas =
            MemoryMarshal.Cast<byte, RemoteChunkDeltaRecord>(deltaOverlap);
        overlappingDeltas[0] = deltas[0];
        byte[] deltaOverlapBefore = deltaOverlap.ToArray();
        False(UltraSimGameProtocol.TryWriteChunkDelta(
            deltaOverlap, 10, 20, 31, 1, -4, 7, 8, 10, 4, overlappingDeltas[..1], out _));
        True(deltaOverlapBefore.AsSpan().SequenceEqual(deltaOverlap));
        False(UltraSimGameProtocol.TryReadChunkDelta(
            bytes.AsSpan(0, deltaBytes), out _, deltaSentinel, out _));
        True(deltaBefore.AsSpan().SequenceEqual(deltaSentinel));
        bytes[deltaBytes - 1] ^= 1;

        True(UltraSimGameProtocol.TryWriteChunkAbsence(
            bytes, 10, 20, 32, 1, -4, 7, out int absenceBytes));
        True(UltraSimGameProtocol.TryReadChunkAbsence(
            bytes.AsSpan(0, absenceBytes), out RemoteChunkAbsenceMessage absence));
        Equal(32UL, absence.DeliveryId);
        False(UltraSimGameProtocol.TryReadChunkAbsence(
            bytes.AsSpan(0, absenceBytes - 1), out _));

        var proof = new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = 10,
            PublicationEpoch = 20,
            DeliveryId = 32,
            SpatialSpaceId = 1,
            ChunkX = -4,
            ChunkZ = 7,
            Revision = 0,
            Kind = RemoteChunkPublicationKind.Absence,
            Status = RemoteChunkPublicationApplyStatus.Applied,
        };
        True(UltraSimGameProtocol.TryWriteChunkApplied(bytes, proof, out int proofBytes));
        True(UltraSimGameProtocol.TryReadChunkApplied(
            bytes.AsSpan(0, proofBytes), out RemoteChunkAppliedMessage decodedProof));
        Equal(proof.DeliveryId, decodedProof.DeliveryId);
        False(UltraSimGameProtocol.TryReadChunkApplied(
            bytes.AsSpan(0, proofBytes + 1), out _));
        proof.Revision = 1;
        False(UltraSimGameProtocol.TryWriteChunkApplied(bytes, proof, out _));

        Equal((ushort)12, UltraSimGameProtocol.Manifest.Version);
        Equal(10_804_745_783_046_976_666UL, UltraSimGameProtocol.Manifest.Fingerprint);
        Equal(6_148_331_928_277_823_537UL,
            UltraSimGameProtocol.ChunkPublicationSchemaFingerprint);
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkSnapshotHeaderCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkDeltaHeaderCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkAbsenceMessageCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkAppliedMessageCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkSnapshotBeginMessageCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkSnapshotSegmentHeaderCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkSnapshotCommitMessageCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteChunkSnapshotCancelMessageCodec.Descriptor.Id, out _));
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteDirtyChunkBundleHeaderCodec.Descriptor.Id, out _));
    }

    internal static void RemoteEncodingRequiresAppliedProofAndSuppressesKnownAbsence()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        var encoder = new OfflineDirectChunkWireEncoder(77, 8);
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        int occupied = FindSubscription(recipient, Key(0, 0));
        int absent = FindAbsentSubscription(recipient, Key(0, 0));
        byte[] bytes = new byte[4_096];

        Equal(OfflineDirectChunkWireEncodeStatus.Snapshot,
            encoder.TryEncode(recipient, fixture.Store, occupied, 0, 1, bytes,
                out OfflineDirectChunkWireEncodeResult snapshot));
        False(recipient.Subscriptions[occupied].HasKnowledge); // encoded/sent is not known
        True(snapshot.Ticket.IsValid);
        True(UltraSimGameProtocol.TryReadChunkSnapshot(
            bytes.AsSpan(0, snapshot.Written), out _, new RemoteChunkPublicRecord[8], out int count));
        Equal(1, count);
        RemoteChunkAppliedMessage proof = Proof(77, snapshot.Ticket,
            RemoteChunkPublicationApplyStatus.Applied);
        True(encoder.TryCommitAppliedProof(
            recipient, snapshot.Ticket, proof, out OfflineDirectChunkAppliedProofStatus applied));
        Equal(OfflineDirectChunkAppliedProofStatus.Applied, applied);
        True(recipient.Subscriptions[occupied].HasKnowledge);
        ulong snapshotRevision = recipient.Subscriptions[occupied].Cursor.Revision.Value;

        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkWireEncodeStatus.Delta,
            encoder.TryEncode(recipient, fixture.Store, occupied, 1, 2, bytes,
                out OfflineDirectChunkWireEncodeResult firstDelta));
        Equal(snapshotRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        RemoteChunkAppliedMessage wrong = Proof(77, firstDelta.Ticket,
            RemoteChunkPublicationApplyStatus.Applied);
        wrong.DeliveryId++;
        False(encoder.TryCommitAppliedProof(recipient, firstDelta.Ticket, wrong, out _));
        Equal(snapshotRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);

        fixture.Present(0, Position(0, 0, 2_200, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkWireEncodeStatus.Delta,
            encoder.TryEncode(recipient, fixture.Store, occupied, 2, 3, bytes,
                out OfflineDirectChunkWireEncodeResult newerDelta));
        True(encoder.TryCommitAppliedProof(recipient, newerDelta.Ticket,
            Proof(77, newerDelta.Ticket, RemoteChunkPublicationApplyStatus.Applied), out applied));
        Equal(OfflineDirectChunkAppliedProofStatus.Applied, applied);
        ulong newestRevision = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        True(newestRevision > snapshotRevision);
        True(encoder.TryCommitAppliedProof(recipient, firstDelta.Ticket,
            Proof(77, firstDelta.Ticket, RemoteChunkPublicationApplyStatus.Applied), out applied));
        Equal(OfflineDirectChunkAppliedProofStatus.Stale, applied);
        Equal(newestRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);

        Equal(OfflineDirectChunkWireEncodeStatus.Absence,
            encoder.TryEncode(recipient, fixture.Store, absent, 3, 4, bytes,
                out OfflineDirectChunkWireEncodeResult absence));
        False(recipient.Subscriptions[absent].HasKnowledge);
        True(encoder.TryCommitAppliedProof(recipient, absence.Ticket,
            Proof(77, absence.Ticket, RemoteChunkPublicationApplyStatus.Applied), out applied));
        Equal(OfflineDirectChunkAppliedProofStatus.Applied, applied);
        True(recipient.Subscriptions[absent].HasKnowledge);
        False(recipient.Subscriptions[absent].Cursor.IsValid);
        Equal(OfflineDirectChunkWireEncodeStatus.NoPayload,
            encoder.TryEncode(recipient, fixture.Store, absent, 4, 5, bytes, out var suppressed));
        Equal(0, suppressed.Written);

        fixture.Present(0, Position(0, 0, 2_300, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkWireEncodeStatus.EncodingFailed,
            encoder.TryEncode(recipient, fixture.Store, occupied, 5, 6, bytes.AsSpan(0, 1), out _));
        Equal(newestRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(OfflineDirectChunkWireEncodeStatus.Delta,
            encoder.TryEncode(recipient, fixture.Store, occupied, 5, 7, bytes, out _));
    }

    internal static void RejectedProofAndEpochReplacementNeverAdvanceKnowledge()
    {
        using var fixture = new Fixture(historyCapacity: 16);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        var encoder = new OfflineDirectChunkWireEncoder(88, 8);
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        int occupied = FindSubscription(recipient, Key(0, 0));
        byte[] bytes = new byte[4_096];
        Equal(OfflineDirectChunkWireEncodeStatus.Snapshot,
            encoder.TryEncode(recipient, fixture.Store, occupied, 0, 1, bytes,
                out OfflineDirectChunkWireEncodeResult delivery));

        RemoteChunkAppliedMessage rejected = Proof(
            88, delivery.Ticket, RemoteChunkPublicationApplyStatus.Rejected);
        False(encoder.TryCommitAppliedProof(recipient, delivery.Ticket, rejected, out _));
        False(recipient.Subscriptions[occupied].HasKnowledge);

        fixture.ReplaceStore(historyCapacity: 16);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes,
                out OfflineDirectChunkReconcileResult reset));
        True(reset.SourceReset);
        False(encoder.TryCommitAppliedProof(recipient, delivery.Ticket,
            Proof(88, delivery.Ticket, RemoteChunkPublicationApplyStatus.Applied), out _));
        False(recipient.Subscriptions[occupied].HasKnowledge);
    }

    internal static void WorldStateDeltaAndAbsenceAdvanceOnlyThroughRemoteProof()
    {
        using var fixture = new Fixture(historyCapacity: 64);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageInitial(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        int absent = FindAbsentSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 77,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 77, maximumDeltaRecords: 8, maximumPendingProofs: 16);

        ulong baselineRevision = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        fixture.Present(0, Position(0, 0, 2_500, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 1));
        Equal(baselineRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(1, server.GetSnapshot().OutstandingDeliveries);
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 1);
        True(recipient.Subscriptions[occupied].Cursor.Revision.Value > baselineRevision);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle occupiedHandle));
        True(cache.TryGetInfo(occupiedHandle, out ClientSemanticChunkInfo occupiedInfo));
        Equal(recipient.Subscriptions[occupied].Cursor, occupiedInfo.PublicationCursor);

        False(recipient.Subscriptions[absent].HasKnowledge);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedAbsence,
            server.TrySchedule(absent, 2));
        False(recipient.Subscriptions[absent].HasKnowledge);
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 2);
        True(recipient.Subscriptions[absent].HasKnowledge);
        False(recipient.Subscriptions[absent].Cursor.IsValid);
        Equal(RemoteChunkWorldStateScheduleStatus.NoPayload,
            server.TrySchedule(absent, 3));

        RemoteChunkWorldStateServerSnapshot transport = server.GetSnapshot();
        True(transport.MaximumDeltaRecordsPerDatagram > 0);
        True(transport.MaximumDeltaRecordsPerDatagram <
            UltraSimGameProtocol.MaximumChunkPublicationRecords);
        True(UltraSimGameProtocol.GetChunkDeltaEncodedBytes(
            transport.MaximumDeltaRecordsPerDatagram) <= transport.MaximumUnfragmentedMessageBytes);
        True(UltraSimGameProtocol.GetChunkDeltaEncodedBytes(
            transport.MaximumDeltaRecordsPerDatagram + 1) > transport.MaximumUnfragmentedMessageBytes);

        // The protocol's uint16 local field is broader than this game's 16 m chunk profile.
        // The client bridge must reject it before constructing/mutating WorldPosition state.
        True(cache.TryGetResidentView(occupiedHandle, out ClientSemanticChunkView beforeInvalid));
        WorldPublicRecord retained = beforeInvalid.PublicRecords[0];
        ulong currentRevision = occupiedInfo.PublicationCursor.Revision.Value;
        RemoteChunkDeltaRecord[] invalidLocal =
        [
            new(RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Transform,
                retained.Id.Index, retained.Id.Generation, retained.EntityPublicVersion + 1,
                retained.PrefabId, 16_000, retained.Position.Local.ZMillimeters,
                retained.Position.YMillimeters, retained.Orientation.Units,
                (ushort)retained.StateFlags, currentRevision + 1),
        ];
        byte[] hostile = new byte[256];
        True(UltraSimGameProtocol.TryWriteChunkDelta(
            hostile, 77, fixture.Store.Epoch.Value, 999,
            GameWorldState.DefaultSpace.Value, 0, 0,
            currentRevision, currentRevision + 1, currentRevision,
            invalidLocal, out int hostileBytes));
        RemoteChunkWorldStateClientSnapshot beforeHostile = client.GetSnapshot();
        True(client.TryProcess(new NetworkDeliveredMessage(
            NetworkLane.WorldState, RemoteChunkDeltaHeaderCodec.Descriptor.Id,
            RemoteChunkDeltaHeaderCodec.Descriptor.Version, default, hostileBytes),
            hostile.AsSpan(0, hostileBytes)));
        Equal(beforeHostile.RejectedDeliveries + 1, client.GetSnapshot().RejectedDeliveries);
        True(cache.TryGetInfo(occupiedHandle, out ClientSemanticChunkInfo afterHostile));
        Equal(occupiedInfo.PublicationCursor, afterHostile.PublicationCursor);
        Equal(occupiedInfo.SemanticHash, afterHostile.SemanticHash);

        server.SetNextDeliveryIdForTesting(ulong.MaxValue);
        fixture.Present(0, Position(0, 0, 2_750, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 10));
        Equal(0UL, server.GetSnapshot().NextDeliveryId);
        Equal(RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted,
            server.TrySchedule(occupied, 11));
    }

    internal static void ImpairmentAndSubscriptionReentryFenceDeliveryKnowledge()
    {
        using var fixture = new Fixture(historyCapacity: 128);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(
            new NetworkImpairmentOptions(0xC81A_1200, 220, 240, 80, 5));
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageInitial(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 91,
            new RemoteChunkWorldStateTransportLimits(64));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 91, maximumDeltaRecords: 8, maximumPendingProofs: 64);

        fixture.Present(0, Position(0, 0, 3_000, 2_000), GameWorldPublicPrefabIds.Character);
        ulong targetRevision = CurrentRevision(fixture.Store, Key(0, 0));
        for (int step = 0; step < 4_096 &&
            recipient.Subscriptions[occupied].Cursor.Revision.Value < targetRevision; step++)
        {
            RemoteChunkWorldStateScheduleStatus scheduled = server.TrySchedule(occupied, (ulong)step + 1);
            True(scheduled is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                RemoteChunkWorldStateScheduleStatus.NoPayload or
                RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
                RemoteChunkWorldStateScheduleStatus.TransportNotAccepting);
            Pump(loopback, server, client);
        }
        Equal(targetRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle handle));
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView view));
        Equal((ushort)3_000, view.PublicRecords[0].Position.Local.XMillimeters);
        True(server.GetSnapshot().DeltasQueued > 1);

        fixture.Present(0, Position(0, 0, 3_500, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 5_000));
        Equal(1, server.GetSnapshot().OutstandingDeliveries);
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition far = Position(4, 0, 8_000, 8_000);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            server.Reconcile(GameWorldState.DefaultSpace, far, changes,
                out OfflineDirectChunkReconcileResult left));
        True(left.ChangeCount > 0);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        ulong appliedBeforeDelayedTraffic = server.GetSnapshot().AppliedProofs;
        for (int step = 0; step < 64; step++) Pump(loopback, server, client);
        Equal(appliedBeforeDelayedTraffic, server.GetSnapshot().AppliedProofs);

        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            server.Reconcile(GameWorldState.DefaultSpace, anchor, changes, out _));
        int reentered = FindSubscription(recipient, Key(0, 0));
        False(recipient.Subscriptions[reentered].HasKnowledge);
        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TrySchedule(reentered, 6_000));
        False(recipient.Subscriptions[reentered].HasKnowledge);

        PublicationEpoch priorEpoch = fixture.Store.Epoch;
        fixture.ReplaceStore(historyCapacity: 128);
        server.ReplaceSource(fixture.Store);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            server.Reconcile(GameWorldState.DefaultSpace, anchor, changes,
                out OfflineDirectChunkReconcileResult reset));
        True(reset.SourceReset);
        True(reset.SourceEpoch != priorEpoch);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
    }

    internal static void OversizeWorldStateDeltaFailsClosedAtDatagramBoundary()
    {
        using var fixture = new Fixture(historyCapacity: 512, entityCount: 64);
        for (int index = 0; index < 64; index++)
            fixture.Present(index, Position(0, 0, 1_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 64);
        var cache = Cache(fixture, maximumPublicRecords: 64);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageInitial(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 101,
            new RemoteChunkWorldStateTransportLimits(8));
        RemoteChunkWorldStateServerSnapshot before = server.GetSnapshot();
        int changed = before.MaximumDeltaRecordsPerDatagram + 1;
        True(changed <= 64);
        ulong baselineRevision = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        for (int index = 0; index < changed; index++)
            fixture.Present(index, Position(0, 0, 4_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);

        Equal(RemoteChunkWorldStateScheduleStatus.PayloadTooLarge,
            server.TrySchedule(occupied, 1));
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        Equal(baselineRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(1UL, server.GetSnapshot().OversizeDeltas);
        Equal(0, loopback.Server.IndependentUnreliableQueued);
    }

    internal static void SnapshotTransactionAppliesAtomicallyAndRecoversOversize()
    {
        using var fixture = new Fixture(historyCapacity: 512, entityCount: 64);
        for (int index = 0; index < 64; index++)
            fixture.Present(index, Position(0, 0, 1_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 64);
        var cache = Cache(fixture, maximumPublicRecords: 64);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageBaseOnly(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 131,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 131, maximumDeltaRecords: 64, maximumPendingProofs: 32);

        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TrySchedule(occupied, 1));
        False(recipient.Subscriptions[occupied].HasKnowledge);
        Equal(1UL, server.GetSnapshot().SnapshotsQueued);
        Equal(2, server.GetSnapshot().SnapshotSegmentsQueued);
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 1);
        True(recipient.Subscriptions[occupied].HasKnowledge);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle handle));
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView initial));
        Equal(64, initial.PublicRecords.Length);

        int changed = server.GetSnapshot().MaximumDeltaRecordsPerDatagram + 1;
        for (int index = 0; index < changed; index++)
            fixture.Present(index, Position(0, 0, 4_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        ulong oldRevision = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        Equal(RemoteChunkWorldStateScheduleStatus.PayloadTooLarge,
            server.TrySchedule(occupied, 2));
        Equal(oldRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TryScheduleSnapshot(occupied, 3));
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 2);
        True(recipient.Subscriptions[occupied].Cursor.Revision.Value > oldRevision);
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView repaired));
        Equal(64, repaired.PublicRecords.Length);
        Equal((ushort)4_000, repaired.PublicRecords[0].Position.Local.XMillimeters);
        Equal(2UL, client.GetSnapshot().AppliedSnapshots);
    }

    internal static void SnapshotLineageFencesDelayedAbsenceAndSnapshot()
    {
        using var fixture = new Fixture(historyCapacity: 32);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        SpatialChunkKey key = Key(0, 0);
        True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
        True(cache.TryStageFromBaseWorld(handle, [], default, out _));
        True(cache.TryPublish(handle, out _));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 151, maximumDeltaRecords: 8, maximumPendingProofs: 16);
        ulong publication = fixture.Store.Epoch.Value;
        RemoteChunkPublicRecord present = RemoteRecord(
            index: 7, generation: 1, version: 1, localX: 2_000, revision: 1);

        DeliverSnapshotTransaction(
            client, loopback.Client, 151, publication, 11, key, 1, 1, [present], commit: true);
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView populated));
        Equal(1, populated.PublicRecords.Length);
        DeliverAbsence(client, 151, publication, 10, key);
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView afterOldAbsence));
        Equal(1, afterOldAbsence.PublicRecords.Length);

        RemoteChunkSnapshotCommitMessage delayed = DeliverSnapshotTransaction(
            client, loopback.Client, 151, publication, 20, key, 2, 1,
            [RemoteRecord(7, 1, 2, 3_000, 2)], commit: false);
        DeliverAbsence(client, 151, publication, 21, key);
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView absent));
        Equal(0, absent.PublicRecords.Length);
        DeliverCommit(client, in delayed);
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView afterOldSnapshot));
        Equal(0, afterOldSnapshot.PublicRecords.Length);
        True(client.GetSnapshot().SupersededDeliveries >= 2);
    }

    internal static void SnapshotTimeoutAndHistoryAdvanceRemainBounded()
    {
        using var fixture = new Fixture(historyCapacity: 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageBaseOnly(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 171,
            new RemoteChunkWorldStateTransportLimits(8, MaximumSnapshotTransferSteps: 3));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 171, maximumDeltaRecords: 8, maximumPendingProofs: 16);

        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TrySchedule(occupied, 1));
        True(server.ExpireSnapshotTransfer(4));
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        Equal(1UL, server.GetSnapshot().SnapshotTimeouts);
        for (int step = 0; step < 64; step++) Pump(loopback, server, client);
        False(client.GetSnapshot().SnapshotCandidateActive);
        False(recipient.Subscriptions[occupied].HasKnowledge);

        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TryScheduleSnapshot(occupied, 10));
        ulong capturedRevision = CurrentRevision(fixture.Store, Key(0, 0));
        for (int index = 0; index < 12; index++)
            fixture.Present(0, Position(0, 0, 3_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        ulong currentRevision = CurrentRevision(fixture.Store, Key(0, 0));
        True(currentRevision > capturedRevision);
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 1);
        Equal(capturedRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);

        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TrySchedule(occupied, 30));
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 2);
        Equal(currentRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        True(recipient.GetSnapshot().SnapshotFallbacks > 0);
    }

    internal static void SnapshotImpairmentAndLostProofConverge()
    {
        using var fixture = new Fixture(historyCapacity: 256, entityCount: 64);
        for (int index = 0; index < 64; index++)
            fixture.Present(index, Position(0, 0, 1_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(
            new NetworkImpairmentOptions(0xC81A_1200, 220, 240, 80, 5));
        var recipient = Recipient(maximumRecords: 64);
        var cache = Cache(fixture, maximumPublicRecords: 64);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageBaseOnly(fixture, recipient, cache, anchor);
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 191,
            new RemoteChunkWorldStateTransportLimits(16, MaximumSnapshotTransferSteps: 512));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 191, maximumDeltaRecords: 64, maximumPendingProofs: 32);

        for (int step = 0; step < 32_768 && !recipient.Subscriptions[occupied].HasKnowledge; step++)
        {
            if (step % 512 == 0)
            {
                RemoteChunkWorldStateScheduleStatus status = server.TrySchedule(
                    occupied, checked((ulong)step + 1));
                True(status is RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted or
                    RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy or
                    RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
                    RemoteChunkWorldStateScheduleStatus.TransportNotAccepting);
            }
            Pump(loopback, server, client);
        }
        True(recipient.Subscriptions[occupied].HasKnowledge);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle handle));
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView view));
        Equal(64, view.PublicRecords.Length);
        True(server.GetSnapshot().SnapshotsQueued >= 1);
        False(client.GetSnapshot().SnapshotCandidateActive);
    }

    internal static void BulkSnapshotDoesNotBlockOrdinaryWorldState()
    {
        using var fixture = new Fixture(historyCapacity: 256, entityCount: 64);
        for (int index = 0; index < 63; index++)
            fixture.Present(index, Position(0, 0, 1_000 + index, 2_000),
                GameWorldPublicPrefabIds.Character);
        fixture.Present(63, Position(1, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        NetworkProtocolLimits limits = NetworkProtocolLimits.Default with
        {
            MaximumPacketsPerPump = 1,
            MaximumInboundPacketsPerPump = 8,
        };
        using ProtocolLoopback loopback = CreateLoopback(default, limits);
        var recipient = Recipient(maximumRecords: 64);
        var cache = Cache(fixture, maximumPublicRecords: 64);
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        ReconcileAndStageBaseOnly(fixture, recipient, cache, anchor);
        int recovering = FindSubscription(recipient, Key(0, 0));
        int ordinary = FindSubscription(recipient, Key(1, 0));
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, ordinary, 0, out OfflineDirectChunkRead initial));
        True(cache.TryGetResidentHandle(Key(1, 0), out ClientSemanticChunkHandle ordinaryHandle));
        True(cache.TryApplySnapshot(
            ordinaryHandle, initial.Batch, initial.Snapshot, out _));
        recipient.CommitRead();

        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 211,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 211, maximumDeltaRecords: 64, maximumPendingProofs: 16);
        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted,
            server.TrySchedule(recovering, 1));
        fixture.Present(63, Position(1, 0, 3_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(ordinary, 1));

        ClientSemanticChunkView updated = default;
        for (int step = 0; step < 3; step++)
        {
            Pump(loopback, server, client);
            True(cache.TryGetResidentView(ordinaryHandle, out updated));
            if (updated.PublicRecords[0].Position.Local.XMillimeters == 3_000) break;
        }
        Equal((ushort)3_000, updated.PublicRecords[0].Position.Local.XMillimeters);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle recoveringHandle));
        True(cache.TryGetResidentView(recoveringHandle, out ClientSemanticChunkView stillRecovering));
        Equal(0, stillRecovering.PublicRecords.Length);
    }

    private static OfflineDirectChunkRecipient Recipient(int maximumRecords) => new(
        new OfflineDirectChunkRecipientLimits(64, maximumRecords, maximumRecords),
        Interest,
        new OfflineDirectChunkCadence(1, 1, 1));

    private static ClientWorldSemanticCache Cache(Fixture fixture, int maximumPublicRecords) => new(
        fixture.Bound,
        new ClientWorldSemanticCacheLimits(64, 32, 16, maximumPublicRecords, 128));

    private static ProtocolLoopback CreateLoopback(
        NetworkImpairmentOptions impairment,
        NetworkProtocolLimits? protocolLimits = null)
    {
        const int defaultUdpMaximumDatagramBytes = 1_472;
        True(ProtocolLoopback.TryCreate(
            protocolLimits ?? NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, defaultUdpMaximumDatagramBytes),
            impairment,
            out ProtocolLoopback? loopback,
            out _,
            UltraSimGameProtocol.Manifest));
        return loopback!;
    }

    private static void ReconcileAndStageInitial(
        Fixture fixture,
        OfflineDirectChunkRecipient recipient,
        ClientWorldSemanticCache cache,
        in WorldPosition anchor)
    {
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        var sourceRows = new WorldPublicRecord[64];
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
        {
            SpatialChunkKey key = recipient.Subscriptions[index].Interest.Key;
            ChunkPublicationReadStatus sourceStatus = fixture.Store.TryCopySnapshot(
                key, sourceRows, out _);
            True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            if (sourceStatus == ChunkPublicationReadStatus.Snapshot)
            {
                Equal(OfflineDirectChunkReadStatus.Snapshot,
                    recipient.TryBeginRead(fixture.Store, index, 0, out OfflineDirectChunkRead read));
                True(cache.TryStageFromBaseWorld(handle, read.Snapshot, read.Batch.Cursor, out _));
                True(cache.TryPublish(handle, out _));
                recipient.CommitRead();
            }
            else
            {
                Equal(ChunkPublicationReadStatus.ChunkAbsent, sourceStatus);
                True(cache.TryStageFromBaseWorld(handle, [], default, out _));
                True(cache.TryPublish(handle, out _));
            }
        }
    }

    private static void ReconcileAndStageBaseOnly(
        Fixture fixture,
        OfflineDirectChunkRecipient recipient,
        ClientWorldSemanticCache cache,
        in WorldPosition anchor)
    {
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
        {
            SpatialChunkKey key = recipient.Subscriptions[index].Interest.Key;
            True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            True(cache.TryStageFromBaseWorld(handle, [], default, out _));
            True(cache.TryPublish(handle, out _));
        }
    }

    private static RemoteChunkSnapshotCommitMessage DeliverSnapshotTransaction(
        RemoteChunkWorldStateClient client,
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        SpatialChunkKey key,
        ulong revision,
        ulong historyFloor,
        ReadOnlySpan<RemoteChunkPublicRecord> records,
        bool commit)
    {
        byte[] canonical = new byte[UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(records.Length)];
        True(UltraSimGameProtocol.TryWriteChunkSnapshot(
            canonical, applicationSessionEpoch, publicationEpoch, deliveryId,
            key.Space.Value, key.Coord.X, key.Coord.Z, revision, historyFloor,
            records, out int canonicalBytes));
        var decoded = new RemoteChunkPublicRecord[records.Length];
        True(UltraSimGameProtocol.TryReadChunkSnapshot(
            canonical.AsSpan(0, canonicalBytes), out RemoteChunkSnapshotHeader inner,
            decoded, out int decodedCount));
        Equal(records.Length, decodedCount);
        int segmentPayload = peer.MaximumUnfragmentedMessageBytes -
            RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize;
        int segmentCount = (canonicalBytes + segmentPayload - 1) / segmentPayload;
        var begin = new RemoteChunkSnapshotBeginMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            SchemaFingerprint = inner.SchemaFingerprint,
            Revision = revision,
            HistoryFloor = historyFloor,
            ContentHash = inner.ContentHash,
            EncodedBytes = checked((uint)canonicalBytes),
            RecordCount = checked((ushort)records.Length),
            SegmentCount = checked((ushort)segmentCount),
            SegmentPayloadBytes = checked((ushort)segmentPayload),
        };
        byte[] beginBytes = new byte[RemoteChunkSnapshotBeginMessageCodec.EncodedSize];
        True(UltraSimGameProtocol.TryWriteChunkSnapshotBegin(beginBytes, in begin, out int written));
        Deliver(client, NetworkLane.Bulk, RemoteChunkSnapshotBeginMessageCodec.Descriptor,
            beginBytes.AsSpan(0, written));

        byte[] segmentBytes = new byte[peer.MaximumUnfragmentedMessageBytes];
        for (int index = 0; index < segmentCount; index++)
        {
            int offset = index * segmentPayload;
            int count = Math.Min(segmentPayload, canonicalBytes - offset);
            var header = new RemoteChunkSnapshotSegmentHeader
            {
                ApplicationSessionEpoch = applicationSessionEpoch,
                PublicationEpoch = publicationEpoch,
                DeliveryId = deliveryId,
                SegmentIndex = checked((ushort)index),
                SegmentCount = checked((ushort)segmentCount),
                PayloadBytes = checked((ushort)count),
            };
            True(UltraSimGameProtocol.TryWriteChunkSnapshotSegment(
                segmentBytes, in header, canonical.AsSpan(offset, count), out written));
            Deliver(client, NetworkLane.Bulk, RemoteChunkSnapshotSegmentHeaderCodec.Descriptor,
                segmentBytes.AsSpan(0, written));
        }

        var completion = new RemoteChunkSnapshotCommitMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            Revision = revision,
            ContentHash = inner.ContentHash,
        };
        if (commit) DeliverCommit(client, in completion);
        return completion;
    }

    private static void DeliverCommit(
        RemoteChunkWorldStateClient client,
        in RemoteChunkSnapshotCommitMessage commit)
    {
        byte[] bytes = new byte[RemoteChunkSnapshotCommitMessageCodec.EncodedSize];
        True(UltraSimGameProtocol.TryWriteChunkSnapshotCommit(bytes, in commit, out int written));
        Deliver(client, NetworkLane.Bulk, RemoteChunkSnapshotCommitMessageCodec.Descriptor,
            bytes.AsSpan(0, written));
    }

    private static void DeliverAbsence(
        RemoteChunkWorldStateClient client,
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        SpatialChunkKey key)
    {
        byte[] bytes = new byte[RemoteChunkAbsenceMessageCodec.EncodedSize];
        True(UltraSimGameProtocol.TryWriteChunkAbsence(
            bytes, applicationSessionEpoch, publicationEpoch, deliveryId,
            key.Space.Value, key.Coord.X, key.Coord.Z, out int written));
        Deliver(client, NetworkLane.WorldState, RemoteChunkAbsenceMessageCodec.Descriptor,
            bytes.AsSpan(0, written));
    }

    private static void Deliver(
        RemoteChunkWorldStateClient client,
        NetworkLane lane,
        NetworkMessageDescriptor descriptor,
        ReadOnlySpan<byte> payload)
    {
        True(client.TryProcess(new NetworkDeliveredMessage(
            lane, descriptor.Id, descriptor.Version, default, payload.Length), payload));
    }

    private static RemoteChunkPublicRecord RemoteRecord(
        uint index,
        uint generation,
        ulong version,
        ushort localX,
        ulong revision) => new(
        index,
        generation,
        version,
        GameWorldPublicPrefabIds.Character,
        localX,
        2_000,
        0,
        0,
        (ushort)WorldPublicStateFlags.Visible,
        revision);

    private static ulong CurrentRevision(ChunkPublicationStore store, SpatialChunkKey key)
    {
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[64];
        Equal(ChunkPublicationReadStatus.Snapshot,
            store.TryCopySnapshot(key, rows, out ChunkPublicationBatch batch));
        return batch.Cursor.Revision.Value;
    }

    private static void Drive(
        ProtocolLoopback loopback,
        RemoteChunkWorldStateServer server,
        RemoteChunkWorldStateClient client,
        Func<bool> complete)
    {
        for (int step = 0; step < 4_096; step++)
        {
            if (complete()) return;
            Pump(loopback, server, client);
        }
        throw new InvalidOperationException("The N1.2 application proof did not converge.");
    }

    private static void Pump(
        ProtocolLoopback loopback,
        RemoteChunkWorldStateServer server,
        RemoteChunkWorldStateClient client)
    {
        _ = server.TryProgress();
        loopback.Pump();
        while (loopback.Client.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            _ = client.TryProcess(message, payload);
            True(loopback.Client.TryConsumeReceived());
        }
        _ = client.TryProgress();
        while (loopback.Server.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            _ = server.TryProcess(message, payload);
            True(loopback.Server.TryConsumeReceived());
        }
    }

    private static void Drain(
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore store,
        ulong cadenceStep)
    {
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
        {
            OfflineDirectChunkReadStatus status = recipient.TryBeginRead(store, index, cadenceStep, out _);
            if (status == OfflineDirectChunkReadStatus.NotDue) continue;
            True(status is OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.ChunkAbsent or
                OfflineDirectChunkReadStatus.Delta or
                OfflineDirectChunkReadStatus.NoChanges);
            recipient.CommitRead();
        }
    }

    private static OfflineDirectChunkRecipient NewScheduledRecipient() => new(
        new OfflineDirectChunkRecipientLimits(64, 8, 8),
        Interest,
        new OfflineDirectChunkCadence(1, 1, 1));

    private static void ApplyScheduledReconcile(
        ChunkPublicationWorkScheduler scheduler,
        ChunkReplicationRecipientId id,
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore store,
        in WorldPosition anchor,
        Span<OfflineDirectChunkSubscriptionChange> changes)
    {
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(store, GameWorldState.DefaultSpace, anchor, changes,
                out OfflineDirectChunkReconcileResult result));
        True(scheduler.TryApplyReconciliation(id, changes[..result.ChangeCount], result));
    }

    private static void DrainScheduledReads(
        ChunkPublicationWorkScheduler scheduler,
        OfflineDirectChunkRecipient first,
        ChunkReplicationRecipientId firstId,
        OfflineDirectChunkRecipient? second,
        ChunkReplicationRecipientId secondId,
        ChunkPublicationStore store,
        ulong cadenceStep)
    {
        while (scheduler.TryDequeueDue(cadenceStep, out ChunkPublicationWork work))
        {
            OfflineDirectChunkRecipient recipient = work.Recipient == firstId
                ? first
                : work.Recipient == secondId && second is not null
                    ? second
                    : throw new InvalidOperationException("Scheduled an unknown recipient.");
            int subscription = FindSubscription(recipient, work.Key);
            OfflineDirectChunkReadStatus status =
                recipient.TryBeginRead(store, subscription, cadenceStep, out _);
            True(status is OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.ChunkAbsent or
                OfflineDirectChunkReadStatus.Delta or
                OfflineDirectChunkReadStatus.NoChanges);
            recipient.CommitRead();
        }
    }

    private static void DrainScheduledWorkOnly(
        ChunkPublicationWorkScheduler scheduler,
        ulong cadenceStep)
    {
        while (scheduler.TryDequeueDue(cadenceStep, out _)) { }
    }

    private static void AssertOneIdentityIn(ClientWorldSemanticCache cache, SpatialChunkKey expectedKey)
    {
        int records = 0;
        SpatialChunkKey found = default;
        // Handles are private identities, so enumerate known authored keys rather than guessing slots.
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        {
            for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
            {
                SpatialChunkKey key = Key(x, z);
                if (!cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
                    !cache.TryGetResidentView(handle, out ClientSemanticChunkView view))
                    continue;
                for (int row = 0; row < view.PublicRecords.Length; row++)
                {
                    records++;
                    found = key;
                }
            }
        }
        Equal(1, records);
        Equal(expectedKey, found);
    }

    private static int FindSubscription(OfflineDirectChunkRecipient recipient, SpatialChunkKey key)
    {
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
            if (recipient.Subscriptions[index].Interest.Key == key) return index;
        throw new InvalidOperationException($"Subscription {key} was not found.");
    }

    private static bool ContainsSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey key)
    {
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
            if (recipient.Subscriptions[index].Interest.Key == key) return true;
        return false;
    }

    private static int FindAbsentSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey occupied)
    {
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
            if (recipient.Subscriptions[index].Interest.Key != occupied) return index;
        throw new InvalidOperationException("An absent subscription was not found.");
    }

    private static RemoteChunkAppliedMessage Proof(
        ulong applicationSessionEpoch,
        in OfflineDirectChunkDeliveryTicket ticket,
        RemoteChunkPublicationApplyStatus status) => new()
    {
        ApplicationSessionEpoch = applicationSessionEpoch,
        PublicationEpoch = ticket.SourceEpoch.Value,
        DeliveryId = ticket.DeliveryId,
        SpatialSpaceId = ticket.Key.Space.Value,
        ChunkX = ticket.Key.Coord.X,
        ChunkZ = ticket.Key.Coord.Z,
        Revision = ticket.Cursor.IsValid ? ticket.Cursor.Revision.Value : 0,
        Kind = ticket.Status switch
        {
            OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
            OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
            OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
            _ => throw new InvalidOperationException(),
        },
        Status = status,
    };

    private static SpatialChunkKey Key(int x, int z) =>
        new(GameWorldState.DefaultSpace, x, z);

    private static WorldPosition Position(int chunkX, int chunkZ, int localX, int localZ) =>
        new(chunkX, chunkZ, checked((ushort)localX), checked((ushort)localZ), 0);

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value) => True(!value);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private sealed class N1ClientLink
    {
        private readonly Fixture _fixture;
        private readonly OfflineDirectChunkSubscriptionChange[] _changes = new OfflineDirectChunkSubscriptionChange[64];

        internal N1ClientLink(Fixture fixture, OfflineDirectChunkCadence cadence)
        {
            _fixture = fixture;
            Recipient = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest, cadence);
            Cache = new ClientWorldSemanticCache(
                fixture.Bound,
                new ClientWorldSemanticCacheLimits(64, 32, 16, 8, 32));
        }

        internal OfflineDirectChunkRecipient Recipient { get; }
        internal ClientWorldSemanticCache Cache { get; }

        internal OfflineDirectChunkReconcileResult Synchronize(
            in WorldPosition anchor,
            ulong cadenceStep,
            bool reverseReads = false,
            bool sourceResetExpected = false)
        {
            Equal(OfflineDirectChunkReconcileStatus.Reconciled,
                Recipient.Reconcile(_fixture.Store, GameWorldState.DefaultSpace, anchor,
                    _changes, out OfflineDirectChunkReconcileResult result));
            if (sourceResetExpected) True(result.SourceReset);
            if (result.SourceReset) Cache.ResetPublicationEpoch(result.SourceEpoch);
            for (int index = 0; index < result.ChangeCount; index++)
            {
                OfflineDirectChunkSubscriptionChange change = _changes[index];
                if (change.Kind != OfflineDirectChunkSubscriptionChangeKind.Left) continue;
                True(Cache.TryGetResidentHandle(change.Key, out ClientSemanticChunkHandle handle));
                True(Cache.BeginRetire(handle));
                True(Cache.CompleteRetire(handle));
            }

            if (reverseReads)
            {
                for (int index = Recipient.Subscriptions.Length - 1; index >= 0; index--)
                    ApplyRead(index, cadenceStep);
            }
            else
            {
                for (int index = 0; index < Recipient.Subscriptions.Length; index++)
                    ApplyRead(index, cadenceStep);
            }
            return result;
        }

        private void ApplyRead(int index, ulong cadenceStep)
        {
            OfflineDirectChunkReadStatus status = Recipient.TryBeginRead(
                _fixture.Store, index, cadenceStep, out OfflineDirectChunkRead read);
            if (status == OfflineDirectChunkReadStatus.NotDue) return;
            True(status is OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.Delta or
                OfflineDirectChunkReadStatus.NoChanges or
                OfflineDirectChunkReadStatus.ChunkAbsent);
            SpatialChunkKey key = Recipient.Subscriptions[index].Interest.Key;
            bool resident = Cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle);
            bool applied;
            if (!resident)
            {
                applied = Cache.TryRequest(key, out handle, out _) &&
                    Cache.TryStageFromBaseWorld(
                        handle,
                        status == OfflineDirectChunkReadStatus.Snapshot ? read.Snapshot : [],
                        status == OfflineDirectChunkReadStatus.Snapshot ? read.Batch.Cursor : default,
                        out _) &&
                    Cache.TryPublish(handle, out _);
            }
            else
            {
                applied = status switch
                {
                    OfflineDirectChunkReadStatus.Snapshot =>
                        Cache.TryApplySnapshot(handle, read.Batch, read.Snapshot, out _),
                    OfflineDirectChunkReadStatus.Delta =>
                        Cache.TryApplyDelta(handle, read.Batch, read.Deltas, out _),
                    OfflineDirectChunkReadStatus.NoChanges => true,
                    OfflineDirectChunkReadStatus.ChunkAbsent =>
                        Cache.TryApplyChunkAbsent(handle, read.SourceEpoch, read.Batch, out _),
                    _ => false,
                };
            }
            if (!applied)
            {
                Recipient.CancelRead();
                throw new InvalidOperationException($"Client rejected N1.0 {status} for {key}.");
            }
            Recipient.CommitRead();
        }
    }

    private sealed class DirectSnapshotClient
    {
        private readonly Fixture _fixture;
        private ChunkInterestEntry[] _current = new ChunkInterestEntry[64];
        private ChunkInterestEntry[] _next = new ChunkInterestEntry[64];
        private readonly WorldPublicRecord[] _records = new WorldPublicRecord[8];
        private int _count;
        private PublicationEpoch _epoch;

        internal DirectSnapshotClient(Fixture fixture)
        {
            _fixture = fixture;
            Cache = new ClientWorldSemanticCache(
                fixture.Bound,
                new ClientWorldSemanticCacheLimits(64, 32, 16, 8, 32));
        }

        internal ClientWorldSemanticCache Cache { get; }

        internal void Synchronize(in WorldPosition anchor)
        {
            Equal(ChunkInterestQueryStatus.Success,
                ChunkInterestQuery.TryQuery(GameWorldState.DefaultSpace, anchor, Interest,
                    _current.AsSpan(0, _count), _next, out int nextCount, out _));
            if (_epoch != _fixture.Store.Epoch)
            {
                _epoch = _fixture.Store.Epoch;
                Cache.ResetPublicationEpoch(_epoch);
            }
            for (int index = 0; index < _count; index++)
            {
                SpatialChunkKey key = _current[index].Key;
                if (Contains(_next, nextCount, key)) continue;
                True(Cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle));
                True(Cache.BeginRetire(handle));
                True(Cache.CompleteRetire(handle));
            }
            for (int index = 0; index < nextCount; index++)
            {
                SpatialChunkKey key = _next[index].Key;
                ChunkPublicationReadStatus status = _fixture.Store.TryCopySnapshot(
                    key, _records, out ChunkPublicationBatch batch);
                bool resident = Cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle);
                if (!resident)
                {
                    True(Cache.TryRequest(key, out handle, out _));
                    True(Cache.TryStageFromBaseWorld(
                        handle,
                        status == ChunkPublicationReadStatus.Snapshot
                            ? _records.AsSpan(0, batch.Count)
                            : [],
                        status == ChunkPublicationReadStatus.Snapshot ? batch.Cursor : default,
                        out _));
                    True(Cache.TryPublish(handle, out _));
                }
                else if (status == ChunkPublicationReadStatus.Snapshot)
                {
                    True(Cache.TryApplySnapshot(handle, batch, _records.AsSpan(0, batch.Count), out _));
                }
                else
                {
                    Equal(ChunkPublicationReadStatus.ChunkAbsent, status);
                    True(Cache.TryApplyChunkAbsent(handle, _epoch, batch, out _));
                }
            }
            (_current, _next) = (_next, _current);
            _count = nextCount;
        }

        private static bool Contains(ChunkInterestEntry[] entries, int count, SpatialChunkKey key)
        {
            for (int index = 0; index < count; index++)
                if (entries[index].Key == key) return true;
            return false;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _entities;
        private readonly int _entityCapacity;

        internal Fixture(int historyCapacity, int entityCount = 4)
        {
            if (entityCount <= 0) throw new ArgumentOutOfRangeException(nameof(entityCount));
            WorldPack pack = GamePlayableWorld.CreateWorldPack();
            var binding = new WorldContentBinding(
                GameContent.WorldFingerprint,
                [
                    GameTerrainMaterialIds.Water,
                    GameTerrainMaterialIds.Sand,
                    GameTerrainMaterialIds.Grass,
                    GameTerrainMaterialIds.Rock,
                    GameTerrainMaterialIds.Snow,
                ],
                [
                    GameWorldStaticDefinitionIds.Tree,
                    GameWorldStaticDefinitionIds.Rock,
                    GameWorldStaticDefinitionIds.CabinWall,
                    GameWorldStaticDefinitionIds.CabinRoof,
                    GameWorldStaticDefinitionIds.Shelf,
                    GameWorldStaticDefinitionIds.Book,
            ]);
            True(pack.TryBind(binding, out BoundWorldPack? bound, out _));
            Bound = bound!;
            _entityCapacity = Math.Max(16, entityCount);
            _entities = new Entity[entityCount];
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(new EntityCapacity(_entityCapacity), 2, 4,
                    lifecycleRecordCapacity: Math.Max(32, entityCount)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
            _world.Seal();
            _world.Initialize();
            for (int index = 0; index < _entities.Length; index++)
                True(_world.TryCreate(empty, out _entities[index]));
            Store = CreateStore(historyCapacity);
        }

        internal BoundWorldPack Bound { get; }
        internal ChunkPublicationStore Store { get; private set; }

        internal void Present(int entity, in WorldPosition position, uint prefab)
        {
            var descriptor = new WorldPublicDescriptor(prefab, WorldYaw.Zero, WorldPublicStateFlags.Visible);
            WorldPublicChange change = WorldPublicChange.Present(_entities[entity], position, descriptor);
            True(Store.TryCommit([change], out _));
        }

        internal void CommitEmpty() => True(Store.TryCommit([], out _));

        internal void ReplaceStore(int historyCapacity) => Store = CreateStore(historyCapacity);

        private ChunkPublicationStore CreateStore(int historyCapacity) => new(
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(
                _entityCapacity, checked(_entityCapacity * 2), 2, historyCapacity));

        public void Dispose() => _world.Dispose();
    }
}
