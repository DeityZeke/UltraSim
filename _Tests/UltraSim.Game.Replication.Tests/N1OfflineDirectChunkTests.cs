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

    internal static void HistoryAuditMeasuresScanCollapseAndShadowReuse()
    {
        using var fixture = new Fixture(
            historyCapacity: 64,
            entityCount: 2,
            readModel: PreparedChunkPublicationReadModel.HistoryDerivedComparison);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var records = new WorldPublicRecord[4];
        Equal(ChunkPublicationReadStatus.Snapshot,
            fixture.Store.TryCopySnapshot(key, records, out ChunkPublicationBatch baseline));
        fixture.Store.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(64));
        for (int update = 0; update < 10; update++)
            fixture.Present(0, Position(0, 0, 2_100 + update, 2_000),
                GameWorldPublicPrefabIds.Character);

        var deltas = new WorldPublicDeltaRecord[4];
        Equal(ChunkPublicationReadStatus.Delta,
            fixture.Store.TryCopyDelta(key, baseline.Cursor, deltas, out ChunkPublicationBatch first));
        WorldPublicDeltaRecord expected = deltas[0];
        Equal(1, first.Count);
        Equal(ChunkPublicationReadStatus.Delta,
            fixture.Store.TryCopyDelta(key, baseline.Cursor, deltas, out ChunkPublicationBatch second));
        Equal(first, second);
        Equal(expected, deltas[0]);

        ChunkPublicationHistoryAuditSnapshot audit = fixture.Store.GetHistoryAuditSnapshot();
        True(audit.Enabled);
        Equal(2UL, audit.DeltaRequests);
        Equal(1, audit.DistinctRanges);
        Equal(1UL, audit.RepeatedRangeRequests);
        Equal(22UL, audit.HistoryNodesExamined);
        Equal(2UL, audit.HistoryNodesSkipped);
        Equal(20UL, audit.HistoryNodesAfterCursor);
        Equal(18UL, audit.DuplicateMutations);
        Equal(2UL, audit.DeltaRecordsEmitted);
        Equal(2UL, audit.CurrentRecordLookups);
        Equal(0UL, audit.ShadowContentMismatches);
        True(audit.ShadowReusablePreparationTicks > 0);
        Equal(1, audit.Ranges.Length);
        Equal(2UL, audit.Ranges[0].Requests);
        Equal(11UL, audit.Ranges[0].HistoryNodesExamined / audit.Ranges[0].Requests);
        Equal(9UL, audit.Ranges[0].DuplicateMutations / audit.Ranges[0].Requests);
        False(audit.RangeTableOverflowed);
    }

    internal static void IntroductionMutationAndRemovalCannotDisappear()
    {
        using var fixture = new Fixture(historyCapacity: 32, entityCount: 2);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        int subscription = FindSubscription(recipient, key);

        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, subscription, 0,
                out OfflineDirectChunkRead introduction));
        Equal(1, introduction.Snapshot.Length);
        ChunkPublicationRevision introducedRevision = introduction.Batch.Cursor.Revision;
        OfflineDirectChunkDeliveryTicket delayedIntroduction =
            recipient.ReleaseReadForRemote(11);
        False(recipient.Subscriptions[subscription].HasKnowledge);

        // The baseline is now in flight. A positive update and then removal must remain debt newer
        // than the baseline revision; neither may be hidden by proving the older introduction.
        fixture.Present(0, Position(0, 0, 2_500, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Remove(0);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipient.CommitAppliedProof(delayedIntroduction));
        Equal(introducedRevision, recipient.Subscriptions[subscription].Cursor.Revision);

        Equal(OfflineDirectChunkReadStatus.Delta,
            recipient.TryBeginRead(fixture.Store, subscription, 1,
                out OfflineDirectChunkRead afterProof));
        Equal(1, afterProof.Deltas.Length);
        Equal(WorldPublicOperation.Remove, afterProof.Deltas[0].Operation);
        True(afterProof.Batch.Cursor.Revision.Value > introducedRevision.Value);
        OfflineDirectChunkDeliveryTicket removal = recipient.ReleaseReadForRemote(12);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipient.CommitAppliedProof(removal));

        // Re-entry after proven absence/current removal starts with no inherited applied cursor.
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(10, 10, 8_000, 8_000), changes, out _));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        subscription = FindSubscription(recipient, key);
        False(recipient.Subscriptions[subscription].HasKnowledge);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, subscription, 2, out _));
        recipient.CommitRead();
    }

    internal static void SnapshotReasonsAndRecipientProofRemainIndependent()
    {
        using var fixture = new Fixture(historyCapacity: 2, entityCount: 2);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var first = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        var second = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1),
            OfflineDirectChunkSnapshotReason.ReconnectGenerationReset);
        first.ConfigureSnapshotReasonAudit(true);
        second.ConfigureSnapshotReasonAudit(true);
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            first.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            second.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes, out _));
        int firstSubscription = FindSubscription(first, key);
        int secondSubscription = FindSubscription(second, key);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            first.TryBeginRead(fixture.Store, firstSubscription, 0, out _));
        OfflineDirectChunkDeliveryTicket firstTicket = first.ReleaseReadForRemote(101);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            second.TryBeginRead(fixture.Store, secondSubscription, 0, out _));
        OfflineDirectChunkDeliveryTicket secondTicket = second.ReleaseReadForRemote(202);
        Equal(firstTicket.Cursor, secondTicket.Cursor);
        False(firstTicket.DeliveryId == secondTicket.DeliveryId);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied, first.CommitAppliedProof(firstTicket));
        True(first.Subscriptions[firstSubscription].HasKnowledge);
        False(second.Subscriptions[secondSubscription].HasKnowledge);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied, second.CommitAppliedProof(secondTicket));

        for (int update = 0; update < 6; update++)
            fixture.Present(0, Position(0, 0, 2_100 + update, 2_000),
                GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            first.TryBeginRead(fixture.Store, firstSubscription, 1, out _));
        first.CommitRead();
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            first.TryBeginSnapshotRead(fixture.Store, firstSubscription, out _));
        first.CommitRead();

        fixture.ReplaceStore(historyCapacity: 2);
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            first.Reconcile(fixture.Store, GameWorldState.DefaultSpace, anchor, changes,
                out OfflineDirectChunkReconcileResult reset));
        True(reset.SourceReset);
        firstSubscription = FindSubscription(first, key);
        Equal(OfflineDirectChunkReadStatus.ChunkAbsent,
            first.TryBeginRead(fixture.Store, firstSubscription, 2, out _));
        first.CommitRead();

        OfflineDirectChunkSnapshotReasonAuditSnapshot firstAudit =
            first.GetSnapshotReasonAudit();
        OfflineDirectChunkSnapshotReasonAuditSnapshot secondAudit =
            second.GetSnapshotReasonAudit();
        Equal(1UL, Reason(firstAudit,
            OfflineDirectChunkSnapshotReason.FirstSubscriptionIntroduction).Occurrences);
        Equal(1UL, Reason(firstAudit,
            OfflineDirectChunkSnapshotReason.HistoryFloorLost).Occurrences);
        Equal(1UL, Reason(firstAudit,
            OfflineDirectChunkSnapshotReason.ExplicitRecovery).Occurrences);
        Equal(1UL, Reason(firstAudit,
            OfflineDirectChunkSnapshotReason.SourceEpochReset).Occurrences);
        Equal(1UL, Reason(secondAudit,
            OfflineDirectChunkSnapshotReason.ReconnectGenerationReset).Occurrences);
        Equal(0UL, Reason(secondAudit,
            OfflineDirectChunkSnapshotReason.FirstSubscriptionIntroduction).Occurrences);

        static OfflineDirectChunkSnapshotReasonObservation Reason(
            OfflineDirectChunkSnapshotReasonAuditSnapshot snapshot,
            OfflineDirectChunkSnapshotReason reason) =>
            snapshot.Reasons.Single(value => value.Reason == reason);
    }

    internal static void PreparedLatestStateMatchesHistoryRecordsExactly()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 3);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var snapshots = new WorldPublicRecord[8];
        Equal(ChunkPublicationReadStatus.Snapshot,
            fixture.Store.TryCopySnapshot(key, snapshots, out ChunkPublicationBatch baseline));

        fixture.Present(0, Position(0, 0, 2_100, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(0, 0, 3_000, 2_000),
            GameWorldPublicPrefabIds.WoodlandAnimal);
        fixture.Present(0, Position(0, 0, 2_200, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Remove(1);

        var prepared = new WorldPublicDeltaRecord[8];
        var history = new WorldPublicDeltaRecord[8];
        Equal(ChunkPublicationReadStatus.Delta,
            fixture.Store.TryCopyDelta(key, baseline.Cursor, prepared,
                out ChunkPublicationBatch preparedBatch));
        Equal(ChunkPublicationReadStatus.Delta,
            fixture.Store.TryCopyHistoryDerivedComparison(key, baseline.Cursor, history,
                out ChunkPublicationBatch historyBatch));
        Equal(historyBatch, preparedBatch);
        Equal(historyBatch.Count, preparedBatch.Count);
        for (int index = 0; index < preparedBatch.Count; index++)
            Equal(history[index], prepared[index]);
        var preparedRemote = new RemoteChunkDeltaRecord[preparedBatch.Count];
        var historyRemote = new RemoteChunkDeltaRecord[historyBatch.Count];
        MaterializeRemote(prepared.AsSpan(0, preparedBatch.Count), preparedRemote);
        MaterializeRemote(history.AsSpan(0, historyBatch.Count), historyRemote);
        byte[] preparedBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        byte[] historyBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        True(UltraSimGameProtocol.TryWriteChunkDelta(
            preparedBytes, 7, fixture.Store.Epoch.Value, 8,
            key.Space.Value, key.Coord.X, key.Coord.Z,
            baseline.Cursor.Revision.Value, preparedBatch.Cursor.Revision.Value,
            preparedBatch.HistoryFloor.Value, preparedRemote, out int preparedWritten));
        True(UltraSimGameProtocol.TryWriteChunkDelta(
            historyBytes, 7, fixture.Store.Epoch.Value, 8,
            key.Space.Value, key.Coord.X, key.Coord.Z,
            baseline.Cursor.Revision.Value, historyBatch.Cursor.Revision.Value,
            historyBatch.HistoryFloor.Value, historyRemote, out int historyWritten));
        Equal(historyWritten, preparedWritten);
        True(preparedBytes.AsSpan(0, preparedWritten)
            .SequenceEqual(historyBytes.AsSpan(0, historyWritten)));

        PreparedChunkPublicationSnapshot census = fixture.Store.GetPreparedPublicationSnapshot();
        True(census.Selected);
        Equal(5UL, census.PreparedSourcePublications);
        Equal(1UL, census.RecipientRangeReads);
        Equal(4UL, census.ReferencedSourcePublications);
        Equal(1UL, census.HistoryDerivedSteadyStateCalls); // explicit comparison only

        static void MaterializeRemote(
            ReadOnlySpan<WorldPublicDeltaRecord> source,
            Span<RemoteChunkDeltaRecord> destination)
        {
            for (int index = 0; index < source.Length; index++)
            {
                ref readonly WorldPublicDeltaRecord delta = ref source[index];
                WorldPublicRecord current = delta.Current;
                destination[index] = new RemoteChunkDeltaRecord(
                    (RemoteChunkPublicOperation)delta.Operation,
                    (RemoteChunkPublicFieldMask)delta.Fields,
                    delta.Id.Index,
                    delta.Id.Generation,
                    delta.EntityPublicVersion,
                    current.PrefabId,
                    current.Position.Local.XMillimeters,
                    current.Position.Local.ZMillimeters,
                    current.Position.YMillimeters,
                    current.Orientation.Units,
                    (ushort)current.StateFlags,
                    current.LastChangedRevision.Value);
            }
        }
    }

    internal static void SharedPublicationsRetainIndependentRecipientProof()
    {
        const int recipientCount = 8;
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 2);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var recipients = new OfflineDirectChunkRecipient[recipientCount];
        var subscriptions = new int[recipientCount];
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        for (int index = 0; index < recipientCount; index++)
        {
            recipients[index] = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
                new OfflineDirectChunkCadence(1, 1, 1));
            Equal(OfflineDirectChunkReconcileStatus.Reconciled,
                recipients[index].Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                    Position(0, 0, 8_000, 8_000), changes, out _));
            subscriptions[index] = FindSubscription(recipients[index], key);
            Equal(OfflineDirectChunkReadStatus.Snapshot,
                recipients[index].TryBeginRead(fixture.Store, subscriptions[index], 0, out _));
            recipients[index].CommitRead();
        }
        PreparedChunkPublicationSnapshot before = fixture.Store.GetPreparedPublicationSnapshot();
        fixture.Present(0, Position(0, 0, 2_500, 2_000),
            GameWorldPublicPrefabIds.Character);

        var tickets = new OfflineDirectChunkDeliveryTicket[recipientCount];
        for (int index = 0; index < recipientCount; index++)
        {
            Equal(OfflineDirectChunkReadStatus.Delta,
                recipients[index].TryBeginRead(fixture.Store, subscriptions[index], 1,
                    out OfflineDirectChunkRead read));
            Equal(1, read.Deltas.Length);
            tickets[index] = recipients[index].ReleaseReadForRemote(checked((ulong)index + 1));
        }
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipients[0].CommitAppliedProof(tickets[0]));
        True(recipients[0].Subscriptions[subscriptions[0]].HasKnowledge);
        for (int index = 1; index < recipientCount; index++)
            False(recipients[index].Subscriptions[subscriptions[index]].Cursor == tickets[0].Cursor);
        for (int index = 1; index < recipientCount; index++)
            Equal(OfflineDirectChunkAppliedProofStatus.Applied,
                recipients[index].CommitAppliedProof(tickets[index]));

        PreparedChunkPublicationSnapshot after = fixture.Store.GetPreparedPublicationSnapshot();
        Equal(1UL, after.PreparedSourcePublications - before.PreparedSourcePublications);
        Equal((ulong)recipientCount, after.RecipientRangeReads - before.RecipientRangeReads);
        Equal((ulong)recipientCount,
            after.ReferencedSourcePublications - before.ReferencedSourcePublications);
        Equal(0UL, after.HistoryDerivedSteadyStateCalls);
    }

    internal static void LagCoverageRemovalAndOutOfOrderProofAreExact()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 3);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(0, 0, 3_000, 2_000),
            GameWorldPublicPrefabIds.WoodlandAnimal);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        int subscription = FindSubscription(recipient, key);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, subscription, 0, out _));
        recipient.CommitRead();
        ChunkPublicationRevision baseline = recipient.Subscriptions[subscription].Cursor.Revision;

        fixture.Present(0, Position(0, 0, 2_100, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(0, 0, 3_100, 2_000),
            GameWorldPublicPrefabIds.WoodlandAnimal);
        fixture.Present(0, Position(0, 0, 2_200, 2_000),
            GameWorldPublicPrefabIds.Character);
        fixture.Remove(1);
        Equal(OfflineDirectChunkReadStatus.Delta,
            recipient.TryBeginRead(fixture.Store, subscription, 1,
                out OfflineDirectChunkRead older));
        Equal(2, older.Deltas.Length);
        Equal(WorldPublicOperation.Present, older.Deltas[0].Operation);
        Equal(WorldPublicOperation.Remove, older.Deltas[1].Operation);
        WorldPublicEntityId removedIdentity = older.Deltas[1].Id;
        OfflineDirectChunkDeliveryTicket olderTicket = recipient.ReleaseReadForRemote(100);

        // A later positive for an unrelated live row may not discharge the retained
        // removal. The cumulative prepared result must still carry both facts.
        fixture.Present(0, Position(0, 0, 2_300, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkReadStatus.Delta,
            recipient.TryBeginRead(fixture.Store, subscription, 2,
                out OfflineDirectChunkRead newer));
        Equal(2, newer.Deltas.Length);
        Equal(WorldPublicOperation.Present, newer.Deltas[0].Operation);
        Equal(WorldPublicOperation.Remove, newer.Deltas[1].Operation);
        Equal(removedIdentity, newer.Deltas[1].Id);
        OfflineDirectChunkDeliveryTicket newerTicket = recipient.ReleaseReadForRemote(101);
        True(newerTicket.Cursor.Revision.Value > olderTicket.Cursor.Revision.Value);

        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipient.CommitAppliedProof(newerTicket));
        Equal(newerTicket.Cursor.Revision,
            recipient.Subscriptions[subscription].Cursor.Revision);
        Equal(OfflineDirectChunkAppliedProofStatus.Stale,
            recipient.CommitAppliedProof(olderTicket));
        True(recipient.Subscriptions[subscription].Cursor.Revision.Value > baseline.Value);
        Equal(OfflineDirectChunkReadStatus.NoChanges,
            recipient.TryBeginRead(fixture.Store, subscription, 3, out _));
        recipient.CommitRead();

        // Reuse of the exact public slot is different: a complete newer-generation
        // Present replaces the removed prior occupant while keeping generation order.
        fixture.Present(2, Position(0, 0, 4_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(OfflineDirectChunkReadStatus.Delta,
            recipient.TryBeginRead(fixture.Store, subscription, 4,
                out OfflineDirectChunkRead reused));
        Equal(1, reused.Deltas.Length);
        Equal(WorldPublicOperation.Present, reused.Deltas[0].Operation);
        Equal(removedIdentity.Index, reused.Deltas[0].Id.Index);
        True(reused.Deltas[0].Id.Generation > removedIdentity.Generation);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            recipient.CommitAppliedProof(recipient.ReleaseReadForRemote(102)));

        PreparedChunkPublicationSnapshot source = fixture.Store.GetPreparedPublicationSnapshot();
        Equal(0UL, source.HistoryDerivedSteadyStateCalls);
        True(source.TombstoneHighWater >= 1);
        True(source.CoveragePublicationHighWater >= 5);
    }

    internal static void PreparedRetentionEvictionRebasesSlowRecipient()
    {
        using var fixture = new Fixture(historyCapacity: 3, entityCount: 2);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(64, 8, 8), Interest,
            new OfflineDirectChunkCadence(1, 1, 1));
        recipient.ConfigureSnapshotReasonAudit(true);
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        int subscription = FindSubscription(recipient, key);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, subscription, 0, out _));
        recipient.CommitRead();
        for (int update = 0; update < 8; update++)
            fixture.Present(0, Position(0, 0, 2_100 + update, 2_000),
                GameWorldPublicPrefabIds.Character);

        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, subscription, 1,
                out OfflineDirectChunkRead rebase));
        Equal(1, rebase.Snapshot.Length);
        recipient.CommitRead();
        PreparedChunkPublicationSnapshot source = fixture.Store.GetPreparedPublicationSnapshot();
        True(source.RetentionEvictions >= 6);
        True(source.PressureEvictions >= 6);
        Equal(3, source.RetainedPublications);
        Equal(3, source.RetainedRecords);
        Equal(1UL, source.RebaseRequirements);
        OfflineDirectChunkSnapshotReasonAuditSnapshot reasons =
            recipient.GetSnapshotReasonAudit();
        Equal(1UL, reasons.Reasons.Single(value =>
            value.Reason == OfflineDirectChunkSnapshotReason.HistoryFloorLost).Occurrences);
    }

    internal static void PreparedFixedTopologySeparatesSourceFromRecipientScaling()
    {
        foreach ((int chunks, int recipientsPerChunk) in new[]
                 {
                     (1, 512),
                     (8, 64),
                     (64, 8),
                     (512, 1),
                 })
        {
            using var fixture = new Fixture(
                historyCapacity: Math.Max(1_024, chunks * 4), entityCount: chunks);
            fixture.PresentAllAcrossChunks(2_000, 1);
            var cursors = new ChunkPublicationCursor[chunks];
            var snapshot = new WorldPublicRecord[1];
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                SpatialChunkKey key = Key(chunk, 0);
                Equal(ChunkPublicationReadStatus.Snapshot,
                    fixture.Store.TryCopySnapshot(key, snapshot, out ChunkPublicationBatch batch));
                cursors[chunk] = batch.Cursor;
            }
            PreparedChunkPublicationSnapshot before =
                fixture.Store.GetPreparedPublicationSnapshot();
            fixture.PresentAllAcrossChunks(2_100, 1);
            var delta = new WorldPublicDeltaRecord[1];
            for (int chunk = 0; chunk < chunks; chunk++)
            for (int recipient = 0; recipient < recipientsPerChunk; recipient++)
            {
                Equal(ChunkPublicationReadStatus.Delta,
                    fixture.Store.TryCopyDelta(Key(chunk, 0), cursors[chunk], delta,
                        out ChunkPublicationBatch batch));
                Equal(1, batch.Count);
            }
            PreparedChunkPublicationSnapshot after =
                fixture.Store.GetPreparedPublicationSnapshot();
            Equal((ulong)chunks,
                after.PreparedSourcePublications - before.PreparedSourcePublications);
            Equal(512UL, after.RecipientRangeReads - before.RecipientRangeReads);
            Equal(512UL,
                after.ReferencedSourcePublications - before.ReferencedSourcePublications);
            Equal(0UL, after.HistoryDerivedSteadyStateCalls);
            Console.WriteLine($"EVIDENCE PF5-R2 topology chunks={chunks} " +
                $"recipients_per_chunk={recipientsPerChunk} source_preparations={chunks} " +
                "recipient_relationships=512 old_history_calls=0");
        }
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
        ChunkPublicationWorkSchedulerSnapshot observed = scheduler.GetSnapshot();
        Equal((ulong)2, observed.CommitsObserved);
        Equal((ulong)1, observed.EmptyCommitsObserved);
        Equal((ulong)1, observed.DirtyCommitsObserved);
    }

    internal static void DirtyFanoutAndTopologyCensusAreExactAndBounded()
    {
        const int recipientCount = 64;
        using var fixture = new Fixture(historyCapacity: 256);
        fixture.Store.ConfigureChangeCensus(enabled: true);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store,
            new ChunkPublicationWorkSchedulerLimits(recipientCount, recipientCount * 64));
        scheduler.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        var recipients = new OfflineDirectChunkRecipient[recipientCount];
        var ids = new ChunkReplicationRecipientId[recipientCount];
        var changes = new OfflineDirectChunkSubscriptionChange[64];
        WorldPosition anchor = Position(0, 0, 8_000, 8_000);
        for (int index = 0; index < recipientCount; index++)
        {
            recipients[index] = NewScheduledRecipient();
            True(scheduler.TryRegister(recipients[index], out ids[index]));
            ApplyScheduledReconcile(
                scheduler, ids[index], recipients[index], fixture.Store, anchor, changes);
        }
        DrainScheduledWorkOnly(scheduler, 0);
        scheduler.ResetTelemetryWindow();

        fixture.Present(0, Position(0, 0, 2_001, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store, 1));
        ChunkPublicationWorkSchedulerSnapshot snapshot = scheduler.GetSnapshot();
        Equal((ulong)recipientCount, snapshot.DirtyRecipientFanout);
        Equal((ulong)1, snapshot.Fanout.RecipientsPerDirtyChunk.Count);
        Equal((ulong)recipientCount, snapshot.Fanout.RecipientsPerDirtyChunk.Maximum);
        Equal((ulong)1, snapshot.Fanout.FanoutThirtyThreeToSixtyFour);
        Equal(checked((ulong)(recipientCount * recipients[0].Subscriptions.Length)),
            snapshot.Topology.SubscriptionsPerRecipient.Total);
        Equal((ulong)recipientCount,
            snapshot.Topology.RecipientsPerChunk.Maximum);

        ChunkPublicationStoreSnapshot source = fixture.Store.GetSnapshot();
        Equal((ulong)2, source.PresentChanges);
        Equal((ulong)1, source.CompleteRecords);
        Equal((ulong)1, source.PartialRecords);
        Equal((ulong)2, source.TransformChanges);

        scheduler.ConfigureTelemetry(GameTelemetryLevel.Off);
        fixture.Present(0, Position(0, 0, 2_002, 2_000),
            GameWorldPublicPrefabIds.Character);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store, 2));
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        Equal(GameTelemetryLevel.Off, scheduler.GetSnapshot().Fanout.Level);
    }

    internal static void ControlledFanoutSweepVisitsOnlyActualSubscribers()
    {
        const int samples = 64;
        int[] fanouts = [1, 4, 16, 32, 64, 128, 256, 384, 512, 640, 768];
        foreach (int fanout in fanouts)
        {
            using var fixture = new Fixture(historyCapacity: 64);
            fixture.Present(0, Position(0, 0, 2_000, 2_000),
                GameWorldPublicPrefabIds.Character);
            var scheduler = new ChunkPublicationWorkScheduler(
                fixture.Store,
                new ChunkPublicationWorkSchedulerLimits(fanout, checked(fanout * 64)));
            scheduler.ConfigureTelemetry(GameTelemetryLevel.Detailed);
            var recipients = new OfflineDirectChunkRecipient[fanout];
            var changes = new OfflineDirectChunkSubscriptionChange[64];
            WorldPosition anchor = Position(0, 0, 8_000, 8_000);
            for (int index = 0; index < fanout; index++)
            {
                recipients[index] = NewScheduledRecipient();
                True(scheduler.TryRegister(recipients[index], out var id));
                ApplyScheduledReconcile(
                    scheduler, id, recipients[index], fixture.Store, anchor, changes);
            }
            DrainScheduledWorkOnly(scheduler, 0);
            scheduler.ResetTelemetryWindow();
            var observeTicks = new long[samples];
            var drainTicks = new long[samples];
            int due = 0;
            for (int sample = 0; sample < samples; sample++)
            {
                fixture.Present(0, Position(0, 0, 2_001 + sample, 2_000),
                    GameWorldPublicPrefabIds.Character);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                Equal(ChunkPublicationCommitObservationStatus.Observed,
                    scheduler.ObserveCommit(fixture.Store, checked((ulong)sample + 1)));
                observeTicks[sample] = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                int sampleDue = 0;
                while (scheduler.TryDequeueDue(checked((ulong)sample + 1), out _)) sampleDue++;
                drainTicks[sample] = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                Equal(fanout, sampleDue);
                due += sampleDue;
            }
            ChunkPublicationWorkSchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Equal(checked(fanout * samples), due);
            Equal(checked((ulong)fanout * samples), snapshot.DirtyRecipientFanout);
            Equal((ulong)fanout, snapshot.Fanout.RecipientsPerDirtyChunk.Maximum);
            Array.Sort(observeTicks);
            Array.Sort(drainTicks);
            Console.WriteLine(
                $"EVIDENCE PF4E fanout={fanout} samples={samples} " +
                $"observe_ns={TimingEvidence(observeTicks)} " +
                $"drain_ns={TimingEvidence(drainTicks)} " +
                $"due={due} memberships={snapshot.MembershipCount} " +
                $"reserved={snapshot.ReservedBytes}");
        }
    }

    internal static void FixedSemanticWorkSeparatesTopologyShape()
    {
        const int recipients = 512;
        const int sourceRecords = 512;
        foreach (int hotChunks in new[] { 1, 8, 64, 512 })
        {
            using var fixture = new Fixture(historyCapacity: 2_048, entityCount: sourceRecords);
            fixture.Store.ConfigureChangeCensus(enabled: true);
            fixture.PresentAllAcrossChunks(localX: 2_000, chunkStride: 8);
            var scheduler = new ChunkPublicationWorkScheduler(
                fixture.Store,
                new ChunkPublicationWorkSchedulerLimits(recipients, recipients * 64));
            scheduler.ConfigureTelemetry(GameTelemetryLevel.Detailed);
            var subscriptions = new OfflineDirectChunkRecipient[recipients];
            var changes = new OfflineDirectChunkSubscriptionChange[64];
            for (int index = 0; index < recipients; index++)
            {
                subscriptions[index] = NewScheduledRecipient();
                True(scheduler.TryRegister(subscriptions[index], out var id));
                int target = index % hotChunks;
                ApplyScheduledReconcile(scheduler, id, subscriptions[index], fixture.Store,
                    Position(target * 8, 0, 8_000, 8_000), changes);
            }
            DrainScheduledWorkOnly(scheduler, 0);
            scheduler.ResetTelemetryWindow();

            fixture.PresentAllAcrossChunks(localX: 2_001, chunkStride: 8);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Equal(ChunkPublicationCommitObservationStatus.Observed,
                scheduler.ObserveCommit(fixture.Store, 1));
            long observe = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            int due = 0;
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (scheduler.TryDequeueDue(1, out _)) due++;
            long drain = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            ChunkPublicationWorkSchedulerSnapshot result = scheduler.GetSnapshot();
            ChunkPublicationStoreSnapshot source = fixture.Store.GetSnapshot();
            Equal(sourceRecords, fixture.Store.LastCommittedDirtyChunks.Length);
            Equal(recipients, due);
            Equal((ulong)recipients, result.DirtyRecipientFanout);
            Equal((ulong)(sourceRecords - hotChunks), result.DirtyChunksWithoutSubscribers);
            Equal((ulong)(recipients / hotChunks), result.Fanout.RecipientsPerDirtyChunk.Maximum);
            Equal((ulong)(sourceRecords * 2), source.PresentChanges);
            Console.WriteLine(
                $"EVIDENCE PF4E topology_hot_chunks={hotChunks} source_records={sourceRecords} " +
                $"dirty_chunks={result.DirtyChunksObserved} recipient_scopes={due} " +
                $"max_fanout={result.Fanout.RecipientsPerDirtyChunk.Maximum} " +
                $"observe_ns={ToNanoseconds(observe)} drain_ns={ToNanoseconds(drain)}");
        }
    }

    internal static void TopologyChurnIsIndependentFromSourceDirtyWork()
    {
        const int iterations = 128;
        using var fixture = new Fixture(historyCapacity: 64);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        (OfflineDirectChunkRecipientSnapshot Snapshot, long Ticks) stable =
            RunTopologyControl(fixture.Store, iterations, churn: false);
        (OfflineDirectChunkRecipientSnapshot Snapshot, long Ticks) churn =
            RunTopologyControl(fixture.Store, iterations, churn: true);
        Equal((ulong)1, fixture.Store.GetSnapshot().CommitCount);
        True(stable.Snapshot.StableInterestFastPaths > 0);
        True(churn.Snapshot.EnteredSubscriptions > stable.Snapshot.EnteredSubscriptions);
        True(churn.Snapshot.LeftSubscriptions > stable.Snapshot.LeftSubscriptions);
        True(churn.Snapshot.TierChanges > 0);
        Equal(churn.Snapshot.TierChanges,
            churn.Snapshot.ImmediateToNear + churn.Snapshot.NearToFar +
            churn.Snapshot.FarToNear + churn.Snapshot.NearToImmediate +
            churn.Snapshot.OtherTierChanges);
        Console.WriteLine(
            $"EVIDENCE PF4E topology_stable_ns={ToNanoseconds(stable.Ticks)} " +
            $"stable_fast={stable.Snapshot.StableInterestFastPaths} " +
            $"topology_churn_ns={ToNanoseconds(churn.Ticks)} " +
            $"enters={churn.Snapshot.EnteredSubscriptions} leaves={churn.Snapshot.LeftSubscriptions} " +
            $"tier_changes={churn.Snapshot.TierChanges} full_queries={churn.Snapshot.FullInterestQueries}");
    }

    private static (OfflineDirectChunkRecipientSnapshot Snapshot, long Ticks) RunTopologyControl(
        ChunkPublicationStore store,
        int iterations,
        bool churn)
    {
        var recipient = NewScheduledRecipient();
        var scheduler = new ChunkPublicationWorkScheduler(
            store, new ChunkPublicationWorkSchedulerLimits(1, 64));
        True(scheduler.TryRegister(recipient, out var id));
        var changes = new OfflineDirectChunkSubscriptionChange[64];
        recipient.ResetReconcileAttribution(enabled: true);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            WorldPosition anchor = churn
                ? Position((iteration & 1) == 0 ? 0 : 1, 0, 8_000, 8_000)
                : Position(0, 0, 8_000, 8_000);
            ApplyScheduledReconcile(scheduler, id, recipient, store, anchor, changes);
            DrainScheduledWorkOnly(scheduler, checked((ulong)iteration));
        }
        return (recipient.GetSnapshot(), System.Diagnostics.Stopwatch.GetTimestamp() - started);
    }

    private static string TimingEvidence(long[] orderedTicks) =>
        $"p50={ToNanoseconds(orderedTicks[31])},p95={ToNanoseconds(orderedTicks[60])}," +
        $"p99={ToNanoseconds(orderedTicks[63])},max={ToNanoseconds(orderedTicks[^1])}";

    private static long ToNanoseconds(long ticks) =>
        ticks * 1_000_000_000L / System.Diagnostics.Stopwatch.Frequency;

    internal static void DirtySchedulerRetryWaitsAndTelemetryResetPreservesDebt()
    {
        using var fixture = new Fixture(historyCapacity: 16);
        var recipient = NewScheduledRecipient();
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 64));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        Span<OfflineDirectChunkSubscriptionChange> changes =
            stackalloc OfflineDirectChunkSubscriptionChange[64];
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store,
            Position(0, 0, 8_000, 8_000), changes);

        True(scheduler.TryDequeueDue(10, out ChunkPublicationWork work));
        True(scheduler.Requeue(in work));
        while (scheduler.TryDequeueDue(10, out ChunkPublicationWork sibling))
        {
            False(sibling.Recipient == work.Recipient && sibling.Key == work.Key);
        }
        Equal(1, scheduler.PendingWorkCount);

        scheduler.ResetTelemetryWindow();
        ChunkPublicationWorkSchedulerSnapshot reset = scheduler.GetSnapshot();
        Equal(1, reset.PendingWorkCount);
        Equal(recipient.Subscriptions.Length, reset.MembershipCount);
        Equal((ulong)0, reset.IncomingQueued);
        Equal((ulong)0, reset.PendingMembershipsExamined);
        Equal((ulong)0, reset.DueWorkDequeued);

        True(scheduler.TryDequeueDue(11, out ChunkPublicationWork retried));
        Equal(work.Recipient, retried.Recipient);
        Equal(work.Key, retried.Key);
        Equal(work.Kind, retried.Kind);
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

    internal static void RecipientTelemetryWindowResetPreservesTopologyAndKnowledge()
    {
        using var fixture = new Fixture(historyCapacity: 16);
        fixture.Present(0, Position(0, 0, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        var changes = new OfflineDirectChunkSubscriptionChange[1_024];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes,
                out OfflineDirectChunkReconcileResult initial));
        int subscriptionCount = recipient.Subscriptions.Length;
        int present = FindSubscription(recipient, Key(0, 0));
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            recipient.TryBeginRead(fixture.Store, present, 0, out _));
        recipient.CommitRead();
        OfflineDirectChunkRecipientSnapshot before = recipient.GetSnapshot();
        True(before.EnteredSubscriptions > 0);
        True(before.SnapshotReads > 0);
        True(initial.ChangeCount > 0);

        recipient.ResetTelemetryWindow(enableReconcileAttribution: true);
        OfflineDirectChunkRecipientSnapshot reset = recipient.GetSnapshot();
        Equal(subscriptionCount, reset.SubscriptionCount);
        Equal(0UL, reset.Reconciliations);
        Equal(0UL, reset.EnteredSubscriptions);
        Equal(0UL, reset.LeftSubscriptions);
        Equal(0UL, reset.TierChanges);
        Equal(0UL, reset.SnapshotReads);
        Equal(0UL, reset.DeltaReads);
        Equal(0UL, reset.FullInterestQueries);
        Equal(0UL, reset.StableInterestFastPaths);
        True(recipient.Subscriptions[present].HasKnowledge);
        True(recipient.GetReconcileAttribution().Enabled);

        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            recipient.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_100, 8_000), changes, out _));
        OfflineDirectChunkRecipientSnapshot after = recipient.GetSnapshot();
        Equal(1UL, after.Reconciliations);
        Equal(1UL, after.StableInterestFastPaths);
        Equal(0UL, after.EnteredSubscriptions);
        Equal(0UL, after.LeftSubscriptions);
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
        Equal(56, schedulerStorage.MembershipStateBytes);
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

        RemoteChunkAppliedEnvelopeEntry[] envelopeEntries =
        [
            new(41, 1, -4, 7, 9, RemoteChunkPublicationKind.Delta,
                RemoteChunkPublicationApplyStatus.Applied),
            new(42, 1, -3, 7, 0, RemoteChunkPublicationKind.Absence,
                RemoteChunkPublicationApplyStatus.Rejected),
            // A duplicate delivery ID is structurally safe: the server still evaluates each
            // proof independently and may accept at most one matching outstanding delivery.
            new(41, 1, -4, 7, 9, RemoteChunkPublicationKind.Delta,
                RemoteChunkPublicationApplyStatus.Applied),
        ];
        byte[] envelopeBytes = new byte[1_405];
        True(UltraSimGameProtocol.TryWriteChunkAppliedEnvelope(
            envelopeBytes, 10, 20, envelopeEntries, out int envelopeLength));
        Equal(RemoteChunkAppliedEnvelopeHeaderCodec.EncodedSize +
            envelopeEntries.Length * RemoteChunkAppliedEnvelopeEntry.EncodedSize, envelopeLength);
        RemoteChunkAppliedEnvelopeEntry[] decodedEntries =
            Enumerable.Repeat(envelopeEntries[1], 4).ToArray();
        True(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
            envelopeBytes.AsSpan(0, envelopeLength), out RemoteChunkAppliedEnvelopeHeader envelope,
            decodedEntries, out int decodedEntryCount));
        Equal(10UL, envelope.ApplicationSessionEpoch);
        Equal(20UL, envelope.PublicationEpoch);
        Equal(3, decodedEntryCount);
        for (int index = 0; index < decodedEntryCount; index++)
            Equal(envelopeEntries[index], decodedEntries[index]);

        RemoteChunkAppliedEnvelopeEntry[] transactional =
            Enumerable.Repeat(envelopeEntries[1], 4).ToArray();
        RemoteChunkAppliedEnvelopeEntry[] transactionalBefore = transactional.ToArray();
        False(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
            envelopeBytes.AsSpan(0, envelopeLength - 1), out _, transactional, out _));
        True(transactionalBefore.AsSpan().SequenceEqual(transactional));
        byte savedKind = envelopeBytes[RemoteChunkAppliedEnvelopeHeaderCodec.EncodedSize +
            RemoteChunkAppliedEnvelopeEntry.EncodedSize - 2];
        envelopeBytes[RemoteChunkAppliedEnvelopeHeaderCodec.EncodedSize +
            RemoteChunkAppliedEnvelopeEntry.EncodedSize - 2] = 0x7F;
        False(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
            envelopeBytes.AsSpan(0, envelopeLength), out _, transactional, out _));
        True(transactionalBefore.AsSpan().SequenceEqual(transactional));
        envelopeBytes[RemoteChunkAppliedEnvelopeHeaderCodec.EncodedSize +
            RemoteChunkAppliedEnvelopeEntry.EncodedSize - 2] = savedKind;
        False(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
            envelopeBytes.AsSpan(0, envelopeLength), out _, transactional.AsSpan(0, 2), out _));
        Equal(40, UltraSimGameProtocol.GetMaximumChunkAppliedEnvelopeEntriesForPayload(1_405));
        Equal(1_377, UltraSimGameProtocol.GetChunkAppliedEnvelopeEncodedBytes(40));
        long proofEnvelopeAllocations = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            True(UltraSimGameProtocol.TryWriteChunkAppliedEnvelope(
                envelopeBytes, 10, 20, envelopeEntries, out int warmedLength));
            True(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
                envelopeBytes.AsSpan(0, warmedLength), out _, decodedEntries, out _));
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - proofEnvelopeAllocations);

        Equal((ushort)29, UltraSimGameProtocol.Manifest.Version);
        Equal(13_618_865_889_094_008_680UL, UltraSimGameProtocol.Manifest.Fingerprint);
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
            RemoteChunkAppliedEnvelopeHeaderCodec.Descriptor.Id, out _));
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
        True(UltraSimGameProtocol.Manifest.TryGet(
            RemoteCompressedDirtyChunkBundleHeaderCodec.Descriptor.Id, out _));
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

        foreach (int invalidSubscription in new[]
                 {
                     int.MinValue,
                     -1,
                     recipient.Subscriptions.Length,
                     int.MaxValue,
                 })
        {
            Equal(OfflineDirectChunkWireEncodeStatus.InvalidSubscription,
                encoder.TryEncode(recipient, fixture.Store, invalidSubscription, 0, 1, bytes,
                    out OfflineDirectChunkWireEncodeResult invalid));
            Equal(0, invalid.Written);
        }

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

    internal static void RemoteRemovalDeltaUsesCanonicalEmptyCurrent()
    {
        using var fixture = new Fixture(historyCapacity: 16, entityCount: 1);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.WoodlandAnimal);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        int occupied = FindSubscription(recipient, key);
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 78,
            new RemoteChunkWorldStateTransportLimits(8));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 78, maximumDeltaRecords: 8, maximumPendingProofs: 8);

        fixture.Remove(0);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 1));
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 1);

        Equal(1UL, client.GetSnapshot().AppliedDeltas);
        Equal(0UL, client.GetSnapshot().RejectedDeliveries);
        True(cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle));
        True(cache.TryGetResidentView(handle, out ClientSemanticChunkView view));
        Equal(0, view.PublicRecords.Length);
        Equal(recipient.Subscriptions[occupied].Cursor, view.Info.PublicationCursor);
    }

    internal static void SelectedProfileBundlesDueDeltasAndProvesEachEntry()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(1, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        SpatialChunkKey firstKey = Key(0, 0);
        SpatialChunkKey secondKey = Key(1, 0);
        int first = FindSubscription(recipient, firstKey);
        int second = FindSubscription(recipient, secondKey);
        True(first >= 0);
        True(second >= 0);

        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4C,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4C, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);
        RemoteChunkWorldStateServerSnapshot offMemory = server.GetSnapshot();
        Equal((long)loopback.Server.MaximumUnfragmentedMessageBytes, offMemory.BundleScratchBytes);
        True(offMemory.BundleEntryAndRecordStagingBytes > 0);
        True(offMemory.BundleTicketAndMetadataStagingBytes > 0);
        Equal(0L, offMemory.RetainedDetailedDiagnosticBytes);
        server.ConfigureTelemetry(GameTelemetryLevel.Basic);
        RemoteChunkWorldStateServerSnapshot basicMemory = server.GetSnapshot();
        Equal(0L, basicMemory.RetainedDetailedDiagnosticBytes);
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        RemoteChunkWorldStateServerSnapshot detailedMemory = server.GetSnapshot();
        True(detailedMemory.RetainedDetailedDiagnosticBytes > 0);
        server.ConfigureTelemetry(GameTelemetryLevel.Off);
        Equal(detailedMemory.RetainedDetailedDiagnosticBytes,
            server.GetSnapshot().RetainedDetailedDiagnosticBytes);
        Console.WriteLine(
            $"EVIDENCE PF4C memory bundle_scratch={offMemory.BundleScratchBytes} " +
            $"entry_record_staging={offMemory.BundleEntryAndRecordStagingBytes} " +
            $"ticket_metadata_staging={offMemory.BundleTicketAndMetadataStagingBytes} " +
            $"fresh_off_diagnostic={offMemory.RetainedDetailedDiagnosticBytes} " +
            $"fresh_basic_diagnostic={basicMemory.RetainedDetailedDiagnosticBytes} " +
            $"detailed_diagnostic={detailedMemory.RetainedDetailedDiagnosticBytes} " +
            $"detailed_to_off_retained={server.GetSnapshot().RetainedDetailedDiagnosticBytes}");
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        client.ConfigureTelemetry(NetworkTelemetryLevel.Detailed);
        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(1, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);

        ChunkPublicationWork[] works =
        [
            new(default, firstKey, ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty, LatestSourceStep: 10, FirstDueStep: 12),
            new(default, secondKey, ChunkInterestTier.Near,
                ChunkPublicationWorkKind.Dirty, LatestSourceStep: 10, FirstDueStep: 12),
        ];
        var statuses = new RemoteChunkWorldStateScheduleStatus[2];
        Equal(2, server.TryScheduleDueBundle(works, 13, statuses, out int messages));
        Equal(1, messages);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta, statuses[0]);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta, statuses[1]);
        Equal(2, server.GetSnapshot().OutstandingDeliveries);
        RemoteChunkBundleCensusSnapshot bundle = server.GetTimingSnapshot().Knowledge.Bundle;
        Equal(1UL, bundle.Bundles);
        Equal(2UL, bundle.Entries);
        Equal(1UL, bundle.GameMessagesSaved);
        True(bundle.StandaloneApplicationPayloadBytes > bundle.ApplicationPayloadBytes);

        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 2);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        Equal(2UL, client.GetSnapshot().AppliedDeltas);
        Equal(0UL, client.GetSnapshot().RejectedDeliveries);
        Equal(CurrentRevision(fixture.Store, firstKey),
            recipient.Subscriptions[first].Cursor.Revision.Value);
        Equal(CurrentRevision(fixture.Store, secondKey),
            recipient.Subscriptions[second].Cursor.Revision.Value);
        RemoteChunkFreshnessCensusSnapshot freshness =
            server.GetTimingSnapshot().Knowledge.Freshness;
        Equal(1UL, freshness.Immediate.Publications);
        Equal(2UL, freshness.Immediate.SourceToDueSteps.Maximum);
        Equal(1UL, freshness.Immediate.DueToNetworkQueueSteps.Maximum);
        Equal(3UL, freshness.Immediate.SourceToNetworkQueueSteps.Maximum);
        Equal(3UL, freshness.Immediate.SourceToProofSteps.Maximum);
        Equal(1UL, freshness.Near.Publications);
    }

    internal static void SelectedProfileBundleAdmissionIsAtomicAndSingletonStaysCanonical()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(1, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        NetworkProtocolLimits constrained = NetworkProtocolLimits.Default with
        {
            MaximumIndependentUnreliableMessages = 1,
        };
        using ProtocolLoopback loopback = CreateLoopback(default, constrained);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        SpatialChunkKey firstKey = Key(0, 0);
        SpatialChunkKey secondKey = Key(1, 0);
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4C1,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4C1, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);

        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        fixture.Present(1, Position(1, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        ChunkPublicationWork[] works =
        [
            new(default, firstKey, ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty, 10, 12),
            new(default, secondKey, ChunkInterestTier.Near,
                ChunkPublicationWorkKind.Dirty, 10, 12),
        ];
        var statuses = new RemoteChunkWorldStateScheduleStatus[2];

        // Occupy the one independent-state slot. A rejected 129 admission must not publish any
        // ticket, consume any delivery ID, or advance either recipient cursor.
        Equal(NetworkQueueStatus.Accepted, loopback.Server.TryQueue(
            RemoteChunkDeltaHeaderCodec.Descriptor.Id, new byte[] { 1 }));
        ulong firstCursor = recipient.Subscriptions[FindSubscription(recipient, firstKey)]
            .Cursor.Revision.Value;
        ulong secondCursor = recipient.Subscriptions[FindSubscription(recipient, secondKey)]
            .Cursor.Revision.Value;
        Equal(2, server.TryScheduleDueBundle(works, 13, statuses, out int rejectedMessages));
        Equal(0, rejectedMessages);
        Equal(RemoteChunkWorldStateScheduleStatus.TransportNotAccepting, statuses[0]);
        Equal(RemoteChunkWorldStateScheduleStatus.TransportNotAccepting, statuses[1]);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        Equal(1UL, server.GetSnapshot().NextDeliveryId);
        Equal(firstCursor, recipient.Subscriptions[FindSubscription(recipient, firstKey)]
            .Cursor.Revision.Value);
        Equal(secondCursor, recipient.Subscriptions[FindSubscription(recipient, secondKey)]
            .Cursor.Revision.Value);
        Equal(0UL, server.GetTimingSnapshot().Knowledge.Bundle.Bundles);

        for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
            loopback.Pump();
        True(loopback.Client.TryConsumeReceived());
        Equal(2, server.TryScheduleDueBundle(works, 14, statuses, out int acceptedMessages));
        Equal(1, acceptedMessages);
        Equal(2, server.GetSnapshot().OutstandingDeliveries);
        Equal(3UL, server.GetSnapshot().NextDeliveryId);
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 2);

        fixture.Present(0, Position(0, 0, 2_200, 2_000), GameWorldPublicPrefabIds.Character);
        ChunkPublicationWork[] singleton =
        [
            new(default, firstKey, ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty, 20, 20),
        ];
        statuses[0] = default;
        Equal(1, server.TryScheduleDueBundle(singleton, 20, statuses, out int singletonMessages));
        Equal(1, singletonMessages);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta, statuses[0]);
        for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
            loopback.Pump();
        True(loopback.Client.TryPeekReceived(out NetworkDeliveredMessage delivered, out _));
        Equal(RemoteChunkDeltaHeaderCodec.Descriptor.Id, delivered.MessageId);
        Equal(1UL, server.GetTimingSnapshot().Knowledge.Bundle.SingleCandidatesEmittedStandalone);
    }

    internal static void SingletonMtuBoundaryRetainsCanonicalStandalone()
    {
        const int recordCount = 30;
        int standaloneBytes = UltraSimGameProtocol.GetChunkDeltaEncodedBytes(recordCount);
        int hypotheticalBundleBytes = checked(
            RemoteDirtyChunkBundleHeaderCodec.EncodedSize +
            UltraSimGameProtocol.GetDirtyChunkBundleEntryEncodedBytes(
                RemoteChunkPublicationKind.Delta, recordCount));
        Equal(1_372, standaloneBytes);
        Equal(1_374, hypotheticalBundleBytes);

        int[] limits =
        [
            standaloneBytes - 1,
            standaloneBytes,
            standaloneBytes + 1,
            standaloneBytes + 2,
            1_405,
        ];
        RemoteChunkWorldStateScheduleStatus[] expected =
        [
            RemoteChunkWorldStateScheduleStatus.PayloadTooLarge,
            RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            RemoteChunkWorldStateScheduleStatus.QueuedDelta,
        ];

        for (int index = 0; index < limits.Length; index++)
            AssertSingletonMtuBoundary(limits[index], expected[index], recordCount,
                standaloneBytes, hypotheticalBundleBytes,
                refuseFirstAdmission: limits[index] == standaloneBytes);

        AssertAbsenceSingletonMtuBoundary();
    }

    internal static void PreparedBundleEncodingIsCanonicalAndBounded()
    {
        const ulong applicationEpoch = 0xF4E201;
        const ulong publicationEpoch = 0xF4E202;
        const ulong space = 1;
        RemoteChunkDeltaRecord[] records =
        [
            new(RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Complete,
                1, 1, 1, GameWorldPublicPrefabIds.Character, 10, 20, 30, 40,
                (ushort)WorldPublicStateFlags.Visible, 2),
            new(RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Complete,
                2, 1, 1, GameWorldPublicPrefabIds.Character, 11, 21, 31, 41,
                (ushort)WorldPublicStateFlags.Visible, 3),
        ];
        var entries = new RemoteDirtyChunkBundleEntry[2];
        for (int index = 0; index < entries.Length; index++)
        {
            ReadOnlySpan<RemoteChunkDeltaRecord> record = records.AsSpan(index, 1);
            True(UltraSimGameProtocol.TryGetChunkDeltaContentHash(
                publicationEpoch, space, index, 0, 1, checked((ulong)index + 2), 1,
                record, out ulong hash));
            entries[index] = new RemoteDirtyChunkBundleEntry(
                RemoteChunkPublicationKind.Delta, checked((ulong)index + 1), space,
                index, 0, 1, checked((ulong)index + 2), 1, hash, 1, index);
        }
        byte[] canonical = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        byte[] prepared = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        True(UltraSimGameProtocol.TryWriteDirtyChunkBundle(
            canonical, applicationEpoch, publicationEpoch, 1, entries, [], records,
            out int canonicalWritten));
        True(UltraSimGameProtocol.TryWritePreparedDirtyChunkBundle(
            prepared, applicationEpoch, publicationEpoch, 1, entries, records,
            out int preparedWritten));
        Equal(canonicalWritten, preparedWritten);
        True(canonical.AsSpan(0, canonicalWritten).SequenceEqual(
            prepared.AsSpan(0, preparedWritten)));

        RemoteDirtyChunkBundleEntry original = entries[1];
        entries[1] = original with { ContentHash = 0 };
        False(UltraSimGameProtocol.TryWritePreparedDirtyChunkBundle(
            prepared, applicationEpoch, publicationEpoch, 1, entries, records, out _));
        entries[1] = original with { RecordOffset = 0 };
        False(UltraSimGameProtocol.TryWritePreparedDirtyChunkBundle(
            prepared, applicationEpoch, publicationEpoch, 1, entries, records, out _));
        entries[1] = original with { DeliveryId = entries[0].DeliveryId };
        False(UltraSimGameProtocol.TryWritePreparedDirtyChunkBundle(
            prepared, applicationEpoch, publicationEpoch, 1, entries, records, out _));

        Console.WriteLine($"EVIDENCE PF4E-R2 prepared_bundle_bytes={preparedWritten} " +
            "canonical_equal=true invalid_hash_offset_delivery=rejected");
    }

    internal static void CompressedBundleCodecIsExactBoundedHostileSafeAndAllocationFree()
    {
        CreatePf4fCanonicalBundle(out byte[] canonical, out int canonicalBytes);
        byte[] compressed = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        byte[] decoded = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        ushort[] hashes = new ushort[
            UltraSimGameProtocol.DirtyChunkBundleCompressionHashEntries];
        True(UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
            compressed, canonical.AsSpan(0, canonicalBytes), hashes, out int compressedBytes));
        True(compressedBytes < canonicalBytes);
        Equal(RemoteCompressedDirtyChunkBundleHeaderCodec.EncodedSize, 5);
        True(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            compressed.AsSpan(0, compressedBytes), decoded, out int decodedBytes));
        Equal(canonicalBytes, decodedBytes);
        True(canonical.AsSpan(0, canonicalBytes).SequenceEqual(decoded.AsSpan(0, decodedBytes)));

        byte[] malformed = compressed.AsSpan(0, compressedBytes + 1).ToArray();
        malformed[compressedBytes] = 0x7f;
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            malformed, decoded, out _));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            compressed.AsSpan(0, compressedBytes - 1), decoded, out _));
        malformed.AsSpan().Clear();
        malformed[0] = (byte)RemoteChunkBundleCompressionFormat.Lz4BlockV1;
        BinaryPrimitives.WriteUInt16BigEndian(malformed.AsSpan(1),
            checked((ushort)canonicalBytes));
        BinaryPrimitives.WriteUInt16BigEndian(malformed.AsSpan(3),
            checked((ushort)(malformed.Length - 5)));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            malformed, decoded, out _));

        byte[] headerMutation = compressed.AsSpan(0, compressedBytes).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(headerMutation.AsSpan(1), 0);
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            headerMutation, decoded, out _));
        headerMutation = compressed.AsSpan(0, compressedBytes).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(headerMutation.AsSpan(1),
            checked((ushort)(UltraSimGameProtocol.
                MaximumCompressedDirtyChunkBundleDecodedBytes + 1)));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            headerMutation, decoded, out _));
        headerMutation = compressed.AsSpan(0, compressedBytes).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(headerMutation.AsSpan(1),
            checked((ushort)(canonicalBytes - 1)));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            headerMutation, decoded, out _));
        headerMutation = compressed.AsSpan(0, compressedBytes).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(headerMutation.AsSpan(1),
            checked((ushort)(canonicalBytes + 1)));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            headerMutation, decoded, out _));
        headerMutation = compressed.AsSpan(0, compressedBytes).ToArray();
        headerMutation[0] = byte.MaxValue;
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            headerMutation, decoded, out _));

        // A successful decompression never bypasses the canonical message-129 validator.
        byte[] badCanonical = canonical.AsSpan(0, canonicalBytes).ToArray();
        badCanonical[RemoteDirtyChunkBundleHeaderCodec.EncodedSize] = byte.MaxValue;
        True(UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
            compressed, badCanonical, hashes, out int badCompressedBytes));
        True(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            compressed.AsSpan(0, badCompressedBytes), decoded, out int badDecodedBytes));
        var entries = new RemoteDirtyChunkBundleEntry[
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        var snapshots = new RemoteChunkPublicRecord[
            UltraSimGameProtocol.MaximumChunkPublicationRecords];
        var deltas = new RemoteChunkDeltaRecord[
            UltraSimGameProtocol.MaximumChunkPublicationRecords];
        False(UltraSimGameProtocol.TryReadDirtyChunkBundle(
            decoded.AsSpan(0, badDecodedBytes), out _, entries, snapshots, deltas,
            out _, out _, out _));

        int firstEntry = RemoteDirtyChunkBundleHeaderCodec.EncodedSize;
        int entryBytes = UltraSimGameProtocol.GetDirtyChunkBundleEntryEncodedBytes(
            RemoteChunkPublicationKind.Delta, 2);
        byte[] invalidCanonical = canonical.AsSpan(0, canonicalBytes).ToArray();
        invalidCanonical.AsSpan(0, sizeof(ulong)).Clear();
        RejectCanonical(invalidCanonical);
        invalidCanonical = canonical.AsSpan(0, canonicalBytes).ToArray();
        invalidCanonical.AsSpan(firstEntry + sizeof(byte), sizeof(ulong)).Clear();
        RejectCanonical(invalidCanonical);
        invalidCanonical = canonical.AsSpan(0, canonicalBytes).ToArray();
        BinaryPrimitives.WriteUInt64BigEndian(
            invalidCanonical.AsSpan(firstEntry + entryBytes + sizeof(byte)), 1);
        RejectCanonical(invalidCanonical);
        invalidCanonical = canonical.AsSpan(0, canonicalBytes).ToArray();
        const int deltaRecordCountOffset = sizeof(byte) + sizeof(ulong) * 2 +
            sizeof(int) * 2 + sizeof(ulong) * 4;
        BinaryPrimitives.WriteUInt16BigEndian(
            invalidCanonical.AsSpan(firstEntry + deltaRecordCountOffset),
            UltraSimGameProtocol.MaximumChunkPublicationRecords);
        RejectCanonical(invalidCanonical);

        var fuzz = new Random(0xF4F);
        byte[] fuzzed = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        for (int iteration = 1; iteration <= 256; iteration++)
        {
            int length = 1 + iteration * (fuzzed.Length - 1) / 256;
            fuzz.NextBytes(fuzzed.AsSpan(0, length));
            if (UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
                    fuzzed.AsSpan(0, length), decoded, out int fuzzDecoded))
                False(UltraSimGameProtocol.TryReadDirtyChunkBundle(
                    decoded.AsSpan(0, fuzzDecoded), out _, entries, snapshots, deltas,
                    out _, out _, out _));
        }

        byte[] oversizedInput = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes + 1];
        oversizedInput[0] = (byte)RemoteChunkBundleCompressionFormat.Lz4BlockV1;
        BinaryPrimitives.WriteUInt16BigEndian(oversizedInput.AsSpan(1),
            checked((ushort)canonicalBytes));
        BinaryPrimitives.WriteUInt16BigEndian(oversizedInput.AsSpan(3),
            checked((ushort)(oversizedInput.Length - 5)));
        False(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            oversizedInput, decoded, out _));

        True(UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
            compressed, canonical.AsSpan(0, canonicalBytes), hashes, out compressedBytes));
        True(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
            compressed.AsSpan(0, compressedBytes), decoded, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_024; iteration++)
        {
            True(UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
                compressed, canonical.AsSpan(0, canonicalBytes), hashes, out compressedBytes));
            True(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
                compressed.AsSpan(0, compressedBytes), decoded, out decodedBytes));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated);
        Console.WriteLine($"EVIDENCE PF4F codec canonical={canonicalBytes} " +
            $"compressed={compressedBytes} saved={canonicalBytes - compressedBytes} " +
            $"wrapper={RemoteCompressedDirtyChunkBundleHeaderCodec.EncodedSize} " +
            $"alloc={allocated} hostile=bounded canonical_equal=true");

        void RejectCanonical(byte[] bytes)
        {
            True(UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
                compressed, bytes, hashes, out int encodedBytes));
            True(UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
                compressed.AsSpan(0, encodedBytes), decoded, out int recoveredBytes));
            Equal(bytes.Length, recoveredBytes);
            True(bytes.AsSpan().SequenceEqual(decoded.AsSpan(0, recoveredBytes)));
            False(UltraSimGameProtocol.TryReadDirtyChunkBundle(
                decoded.AsSpan(0, recoveredBytes), out _, entries, snapshots, deltas,
                out _, out _, out _));
        }
    }

    internal static void SelectedCompressionPreservesQueueTransactionAndProofs()
    {
        using var fixture = new Fixture(historyCapacity: 128, entityCount: 8);
        int[] chunkX = [-1, 0, 1, -1, 0, 1, -1, 0];
        int[] chunkZ = [-1, -1, -1, 0, 0, 0, 1, 1];
        for (int index = 0; index < 8; index++)
            fixture.Present(index, Position(chunkX[index], chunkZ[index], 2_000, 2_000),
                GameWorldPublicPrefabIds.Character);
        NetworkProtocolLimits constrained = NetworkProtocolLimits.Default with
        {
            MaximumIndependentUnreliableMessages = 1,
        };
        using ProtocolLoopback loopback = CreateLoopback(default, constrained);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        for (int index = 0; index < 8; index++)
            fixture.Present(index, Position(chunkX[index], chunkZ[index], 2_100, 2_000),
                GameWorldPublicPrefabIds.Character);
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4F01,
            new RemoteChunkWorldStateTransportLimits(
                32, 64, RemoteChunkBundleCompressionPolicy.Selected));
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4F01, maximumDeltaRecords: 8,
            maximumPendingProofs: 32);
        client.ConfigureTelemetry(NetworkTelemetryLevel.Detailed);
        var works = new ChunkPublicationWork[8];
        var statuses = new RemoteChunkWorldStateScheduleStatus[8];
        for (int index = 0; index < works.Length; index++)
            works[index] = new(default, Key(chunkX[index], chunkZ[index]),
                ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty, 1, 1);

        Equal(NetworkQueueStatus.Accepted, loopback.Server.TryQueue(
            RemoteChunkDeltaHeaderCodec.Descriptor.Id, new byte[] { 1 }));
        Equal(8, server.TryScheduleDueBundle(works, 1, statuses, out int refusedMessages));
        Equal(0, refusedMessages);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        Equal(1UL, server.GetSnapshot().NextDeliveryId);
        for (int index = 0; index < statuses.Length; index++)
            Equal(RemoteChunkWorldStateScheduleStatus.TransportNotAccepting, statuses[index]);
        for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
            loopback.Pump();
        True(loopback.Client.TryConsumeReceived());

        Equal(8, server.TryScheduleDueBundle(works, 2, statuses, out int acceptedMessages));
        Equal(1, acceptedMessages);
        Equal(8, server.GetSnapshot().OutstandingDeliveries);
        Equal(9UL, server.GetSnapshot().NextDeliveryId);
        for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
            loopback.Pump();
        True(loopback.Client.TryPeekReceived(
            out NetworkDeliveredMessage delivered, out ReadOnlySpan<byte> payload));
        Equal(RemoteCompressedDirtyChunkBundleHeaderCodec.Descriptor.Id, delivered.MessageId);
        True(client.TryProcess(delivered, payload));
        True(loopback.Client.TryConsumeReceived());
        Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 8);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        RemoteChunkCompressionCensusSnapshot sent =
            server.GetTimingSnapshot().Knowledge.Compression;
        RemoteChunkCompressionReceiveCensusSnapshot received =
            client.GetTimingSnapshot().CompressionCensus;
        Equal(2UL, sent.Attempts);
        Equal(1UL, sent.Selected);
        Equal(1UL, received.Messages);
        Equal(sent.CompressedRepresentationBytes, received.CompressedRepresentationBytes);
        Equal(sent.CanonicalBytesSelected, received.CanonicalDecodedBytes);
        True(sent.DirectBytesSaved >= 32);
        Console.WriteLine($"EVIDENCE PF4F transaction refused_attempts=1 " +
            $"selected={sent.Selected} canonical={sent.CanonicalBytesSelected} " +
            $"compressed={sent.CompressedRepresentationBytes} proofs=8 debt=0");
    }

    internal static void IndependentCompressedMessagesConvergeThroughImpairment()
    {
        using var fixture = new Fixture(historyCapacity: 128, entityCount: 8);
        int[] chunkX = [-1, 0, 1, -1, 0, 1, -1, 0];
        int[] chunkZ = [-1, -1, -1, 0, 0, 0, 1, 1];
        for (int index = 0; index < 8; index++)
            fixture.Present(index, Position(chunkX[index], chunkZ[index], 2_000, 2_000),
                GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(
            new NetworkImpairmentOptions(0xF4F0_1290, 220, 180, 40, 5));
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        for (int index = 0; index < 8; index++)
            fixture.Present(index, Position(chunkX[index], chunkZ[index], 2_200, 2_000),
                GameWorldPublicPrefabIds.Character);
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4F03,
            new RemoteChunkWorldStateTransportLimits(
                64, 64, RemoteChunkBundleCompressionPolicy.Selected));
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4F03, maximumDeltaRecords: 8,
            maximumPendingProofs: 64);
        client.ConfigureTelemetry(NetworkTelemetryLevel.Detailed);
        var works = new ChunkPublicationWork[8];
        var statuses = new RemoteChunkWorldStateScheduleStatus[8];
        for (int index = 0; index < works.Length; index++)
            works[index] = new(default, Key(chunkX[index], chunkZ[index]),
                ChunkInterestTier.Immediate, ChunkPublicationWorkKind.Dirty, 1, 1);

        for (int step = 1; step <= 8_192; step++)
        {
            bool converged = true;
            for (int index = 0; index < works.Length; index++)
                converged &= recipient.Subscriptions[
                    FindSubscription(recipient, works[index].Key)].Cursor.Revision.Value ==
                    CurrentRevision(fixture.Store, works[index].Key);
            if (converged && server.GetSnapshot().OutstandingDeliveries == 0) break;
            if ((step & 3) == 1)
            {
                int processed = server.TryScheduleDueBundle(
                    works, checked((ulong)step), statuses, out _);
                True(processed is >= 1 and <= 8);
                for (int index = 0; index < processed; index++)
                    True(statuses[index] is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                        RemoteChunkWorldStateScheduleStatus.NoPayload or
                        RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
                        RemoteChunkWorldStateScheduleStatus.TransportNotAccepting);
            }
            Pump(loopback, server, client);
        }

        for (int index = 0; index < works.Length; index++)
            Equal(CurrentRevision(fixture.Store, works[index].Key),
                recipient.Subscriptions[FindSubscription(recipient, works[index].Key)]
                    .Cursor.Revision.Value);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        RemoteChunkCompressionCensusSnapshot sent =
            server.GetTimingSnapshot().Knowledge.Compression;
        RemoteChunkCompressionReceiveCensusSnapshot received =
            client.GetTimingSnapshot().CompressionCensus;
        True(sent.Selected > 1);
        True(received.Messages > 0);
        Equal(0UL, received.Malformed);
        Equal(8UL, client.GetSnapshot().AppliedDeltas);
        True(client.GetSnapshot().DuplicateDeliveries > 0 || sent.Selected > received.Messages);
        Console.WriteLine($"EVIDENCE PF4F impairment sent={sent.Selected} " +
            $"received={received.Messages} duplicate={client.GetSnapshot().DuplicateDeliveries} " +
            "loss_reorder_delay_corruption=converged proofs=8 debt=0");
    }

    private static void CreatePf4fCanonicalBundle(out byte[] canonical, out int written)
    {
        const int entryCount = 8;
        const int recordsPerEntry = 2;
        const ulong publicationEpoch = 0xF4F02;
        var entries = new RemoteDirtyChunkBundleEntry[entryCount];
        var records = new RemoteChunkDeltaRecord[entryCount * recordsPerEntry];
        for (int entry = 0; entry < entryCount; entry++)
        {
            int offset = entry * recordsPerEntry;
            for (int record = 0; record < recordsPerEntry; record++)
                records[offset + record] = new RemoteChunkDeltaRecord(
                    RemoteChunkPublicOperation.Present,
                    record == 0 ? RemoteChunkPublicFieldMask.Complete :
                        RemoteChunkPublicFieldMask.Transform,
                    checked((uint)(offset + record + 1)), 1,
                    checked((ulong)(offset + record + 1)),
                    GameWorldPublicPrefabIds.Character,
                    checked((ushort)(2_000 + entry)), 2_000, 0,
                    checked((ushort)entry), (ushort)WorldPublicStateFlags.Visible, 2);
            True(UltraSimGameProtocol.TryGetChunkDeltaContentHash(
                publicationEpoch, 1, entry, 0, 1, 2, 1,
                records.AsSpan(offset, recordsPerEntry), out ulong hash));
            entries[entry] = new RemoteDirtyChunkBundleEntry(
                RemoteChunkPublicationKind.Delta, checked((ulong)entry + 1), 1,
                entry, 0, 1, 2, 1, hash, recordsPerEntry, offset);
        }
        canonical = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        True(UltraSimGameProtocol.TryWriteDirtyChunkBundle(
            canonical, 0xF4F01, publicationEpoch, 1, entries, [], records, out written));
        True(written >= RemoteChunkBundleCompressionPolicy.Selected.MinimumCanonicalBytes);
    }

    private static void AssertSingletonMtuBoundary(
        int maximumApplicationBytes,
        RemoteChunkWorldStateScheduleStatus expected,
        int recordCount,
        int standaloneBytes,
        int hypotheticalBundleBytes,
        bool refuseFirstAdmission)
    {
        using var fixture = new Fixture(historyCapacity: 128, entityCount: recordCount);
        fixture.PresentAll(2_000, GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopbackWithMaximumApplicationBytes(
            maximumApplicationBytes, refuseFirstAdmission ? 1 : null);
        Equal(maximumApplicationBytes, loopback.Server.MaximumUnfragmentedMessageBytes);
        var recipient = Recipient(maximumRecords: 32);
        var cache = Cache(fixture, maximumPublicRecords: 32);
        SpatialChunkKey key = Key(0, 0);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        int subscription = FindSubscription(recipient, key);
        True(subscription >= 0);
        ChunkPublicationCursor initialCursor = recipient.Subscriptions[subscription].Cursor;
        fixture.PresentAll(2_100, GameWorldPublicPrefabIds.Character);

        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4C01,
            new RemoteChunkWorldStateTransportLimits(32));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4C01, maximumDeltaRecords: 32,
            maximumPendingProofs: 8);
        ChunkPublicationWork[] work =
        [
            new(default, key, ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty, 1, 1),
        ];
        Span<RemoteChunkWorldStateScheduleStatus> statuses = stackalloc RemoteChunkWorldStateScheduleStatus[1];
        if (refuseFirstAdmission)
        {
            Equal(NetworkQueueStatus.Accepted, loopback.Server.TryQueue(
                RemoteChunkDeltaHeaderCodec.Descriptor.Id, new byte[] { 1 }));
            Equal(1, server.TryScheduleDueBundle(work, 1, statuses, out int refusedMessages));
            Equal(0, refusedMessages);
            Equal(RemoteChunkWorldStateScheduleStatus.TransportNotAccepting, statuses[0]);
            Equal(0, server.GetSnapshot().OutstandingDeliveries);
            Equal(1UL, server.GetSnapshot().NextDeliveryId);
            Equal(initialCursor, recipient.Subscriptions[subscription].Cursor);
            for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
                loopback.Pump();
            True(loopback.Client.TryConsumeReceived());
        }
        Equal(1, server.TryScheduleDueBundle(work, 1, statuses, out int messages));
        Equal(expected, statuses[0]);

        if (expected == RemoteChunkWorldStateScheduleStatus.PayloadTooLarge)
        {
            Equal(0, messages);
            Equal(0, server.GetSnapshot().OutstandingDeliveries);
            Equal(1UL, server.GetSnapshot().NextDeliveryId);
        }
        else
        {
            Equal(1, messages);
            Equal(1, server.GetSnapshot().OutstandingDeliveries);
            Equal(2UL, server.GetSnapshot().NextDeliveryId);
            for (int pump = 0; pump < 8 && loopback.Client.ReceivedQueued == 0; pump++)
                loopback.Pump();
            True(loopback.Client.TryPeekReceived(out NetworkDeliveredMessage delivered,
                out ReadOnlySpan<byte> payload));
            Equal(RemoteChunkDeltaHeaderCodec.Descriptor.Id, delivered.MessageId);
            Equal(standaloneBytes, payload.Length);
            _ = client.TryProcess(delivered, payload);
            True(loopback.Client.TryConsumeReceived());
            Drive(loopback, server, client, () => server.GetSnapshot().AppliedProofs == 1);
            Equal(0, server.GetSnapshot().OutstandingDeliveries);
            Equal(CurrentRevision(fixture.Store, key),
                recipient.Subscriptions[subscription].Cursor.Revision.Value);
        }

        Console.WriteLine(
            $"EVIDENCE PF4C-R1 mtu={maximumApplicationBytes} standalone={standaloneBytes} " +
            $"hypothetical_bundle={hypotheticalBundleBytes} status={statuses[0]} " +
            $"message={(messages == 0 ? 0 : RemoteChunkDeltaHeaderCodec.Descriptor.Id.Value)} " +
            $"next_delivery={server.GetSnapshot().NextDeliveryId} " +
            $"outstanding={server.GetSnapshot().OutstandingDeliveries} " +
            $"proofs={server.GetSnapshot().AppliedProofs} " +
            $"known_revision={recipient.Subscriptions[subscription].Cursor.Revision.Value}");
    }

    private static void AssertAbsenceSingletonMtuBoundary()
    {
        int standaloneBytes = RemoteChunkAbsenceMessageCodec.EncodedSize;
        int hypotheticalBundleBytes = checked(
            RemoteDirtyChunkBundleHeaderCodec.EncodedSize +
            UltraSimGameProtocol.GetDirtyChunkBundleEntryEncodedBytes(
                RemoteChunkPublicationKind.Absence, 0));
        True(hypotheticalBundleBytes > standaloneBytes);
        // UdpProtocolLimits admits no production datagram smaller than 256 bytes. After the
        // secure (44-byte) and Data-frame (23-byte) headers, even that floor has 189 application
        // bytes, so both absence representations fit and this boundary cannot occur for 121.
        const int minimumSupportedUdpDatagramBytes = 256;
        const int dataFrameHeaderBytes = 23;
        int minimumSupportedApplicationBytes = checked(
            minimumSupportedUdpDatagramBytes - NetworkSecureDatagramFraming.HeaderBytes -
            NetworkSecureDatagramFraming.TagBytes - dataFrameHeaderBytes);
        True(hypotheticalBundleBytes <= minimumSupportedApplicationBytes);
        Console.WriteLine(
            $"EVIDENCE PF4C-R1 absence_standalone={standaloneBytes} " +
            $"absence_hypothetical_bundle={hypotheticalBundleBytes} " +
            $"minimum_supported_application={minimumSupportedApplicationBytes} " +
            "boundary_possible=false");
    }

    internal static void SelectedProfileBundleEntriesApplyAndProveIndependently()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 3);
        for (int index = 0; index < 3; index++)
            fixture.Present(index, Position(index - 1, 0, 2_000, 2_000),
                GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4C2, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);

        var keys = new[] { Key(-1, 0), Key(0, 0), Key(1, 0) };
        var entries = new RemoteDirtyChunkBundleEntry[3];
        var records = new RemoteChunkDeltaRecord[3];
        var handles = new ClientSemanticChunkHandle[3];
        var initialX = new ushort[3];
        var initialRevisions = new ulong[3];
        for (int index = 0; index < 3; index++)
        {
            True(cache.TryGetResidentHandle(keys[index], out handles[index]));
            True(cache.TryGetResidentView(handles[index], out ClientSemanticChunkView view));
            WorldPublicRecord current = view.PublicRecords[0];
            initialX[index] = current.Position.Local.XMillimeters;
            ulong baseRevision = view.Info.PublicationCursor.Revision.Value;
            initialRevisions[index] = baseRevision;
            ulong suppliedBase = index == 1 ? baseRevision + 10 : baseRevision;
            ulong suppliedCurrent = suppliedBase + 1;
            records[index] = new RemoteChunkDeltaRecord(
                RemoteChunkPublicOperation.Present,
                RemoteChunkPublicFieldMask.Transform,
                current.Id.Index,
                current.Id.Generation,
                current.EntityPublicVersion + 1,
                current.PrefabId,
                checked((ushort)(current.Position.Local.XMillimeters + 100)),
                current.Position.Local.ZMillimeters,
                current.Position.YMillimeters,
                current.Orientation.Units,
                (ushort)current.StateFlags,
                suppliedCurrent);
            entries[index] = new RemoteDirtyChunkBundleEntry(
                RemoteChunkPublicationKind.Delta,
                checked((ulong)index + 1),
                keys[index].Space.Value,
                keys[index].Coord.X,
                keys[index].Coord.Z,
                suppliedBase,
                suppliedCurrent,
                Math.Max(1, suppliedBase),
                0,
                1,
                index);
        }

        byte[] payload = new byte[1_405];
        RemoteChunkDeltaRecord priorMiddle = records[1] with
        {
            LocalXMillimeters = checked((ushort)(initialX[1] + 50)),
            LastChangedRevision = initialRevisions[1] + 1,
        };
        True(UltraSimGameProtocol.TryWriteChunkDelta(
            payload, 0xF4C2, fixture.Store.Epoch.Value, 2,
            keys[1].Space.Value, keys[1].Coord.X, keys[1].Coord.Z,
            initialRevisions[1], initialRevisions[1] + 1, initialRevisions[1],
            new[] { priorMiddle }, out int priorBytes));
        True(client.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.WorldState, RemoteChunkDeltaHeaderCodec.Descriptor.Id,
                RemoteChunkDeltaHeaderCodec.Descriptor.Version, default, priorBytes),
            payload.AsSpan(0, priorBytes)));

        True(UltraSimGameProtocol.TryWriteDirtyChunkBundle(
            payload, 0xF4C2, fixture.Store.Epoch.Value, 1, entries, [], records,
            out int written));
        True(client.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.WorldState, RemoteDirtyChunkBundleHeaderCodec.Descriptor.Id,
                RemoteDirtyChunkBundleHeaderCodec.Descriptor.Version, default, written),
            payload.AsSpan(0, written)));
        RemoteChunkWorldStateClientSnapshot applied = client.GetSnapshot();
        Equal(3UL, applied.AppliedDeltas);
        Equal(0UL, applied.RejectedDeliveries);
        Equal(1UL, applied.ContradictoryDeliveries);
        for (int index = 0; index < 3; index++)
        {
            True(cache.TryGetResidentView(handles[index], out ClientSemanticChunkView view));
            Equal(index == 1 ? checked((ushort)(initialX[index] + 50)) :
                    checked((ushort)(initialX[index] + 100)),
                view.PublicRecords[0].Position.Local.XMillimeters);
        }

        True(client.TryProgress());
        for (int pump = 0; pump < 8 && loopback.Server.ReceivedQueued == 0; pump++)
            loopback.Pump();
        True(loopback.Server.TryPeekReceived(
            out NetworkDeliveredMessage proofMessage, out ReadOnlySpan<byte> proofPayload));
        Equal(RemoteChunkAppliedEnvelopeHeaderCodec.Descriptor.Id, proofMessage.MessageId);
        var proofs = new RemoteChunkAppliedEnvelopeEntry[4];
        True(UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
            proofPayload, out _, proofs, out int proofCount));
        Equal(4, proofCount);
        Equal(RemoteChunkPublicationApplyStatus.Applied, proofs[0].Status);
        Equal(RemoteChunkPublicationApplyStatus.Applied, proofs[1].Status);
        Equal(RemoteChunkPublicationApplyStatus.Contradictory, proofs[2].Status);
        Equal(RemoteChunkPublicationApplyStatus.Applied, proofs[3].Status);
    }

    internal static void CandidateCadenceTracksLatestCoalescedTruth()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 1);
        var cadence = new OfflineDirectChunkCadence(1, 3, 6);
        var recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default, cadence);
        var scheduler = new ChunkPublicationWorkScheduler(
            fixture.Store, new ChunkPublicationWorkSchedulerLimits(1, 1_024));
        True(scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
        var changes = new OfflineDirectChunkSubscriptionChange[1_024];
        ApplyScheduledReconcile(scheduler, id, recipient, fixture.Store,
            Position(0, 0, 8_000, 8_000), changes);
        DrainScheduledWorkOnly(scheduler, 0);

        int far = -1;
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
        {
            OfflineDirectChunkSubscription subscription = recipient.Subscriptions[index];
            if (subscription.Interest.Tier == ChunkInterestTier.Far &&
                subscription.CadencePhase != 0)
            {
                far = index;
                break;
            }
        }
        True(far >= 0);
        SpatialChunkKey key = recipient.Subscriptions[far].Interest.Key;
        fixture.Present(0, Position(key.Coord.X, key.Coord.Z, 2_000, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store, 10));
        fixture.Present(0, Position(key.Coord.X, key.Coord.Z, 2_100, 2_000),
            GameWorldPublicPrefabIds.Character);
        Equal(ChunkPublicationCommitObservationStatus.Observed,
            scheduler.ObserveCommit(fixture.Store, 11));

        ChunkPublicationWork work = default;
        ulong dueStep = 0;
        for (ulong step = 11; step <= 17; step++)
        {
            if (!scheduler.TryDequeueDue(step, out work)) continue;
            dueStep = step;
            break;
        }
        True(dueStep is >= 11 and <= 17);
        Equal(ChunkInterestTier.Far, work.Tier);
        Equal(11UL, work.LatestSourceStep);
        Equal(dueStep, work.FirstDueStep);
        True(dueStep - work.LatestSourceStep < cadence.FarSteps);

        // A blocked publication keeps the original due point; it does not become newly fresh.
        True(scheduler.Requeue(in work));
        False(scheduler.TryDequeueDue(dueStep, out _));
        True(scheduler.TryDequeueDue(dueStep + cadence.FarSteps, out ChunkPublicationWork retry));
        Equal(work.LatestSourceStep, retry.LatestSourceStep);
        Equal(work.FirstDueStep, retry.FirstDueStep);
    }

    internal static void SelectedProfileBundleWarmedPathAllocatesZero()
    {
        using var fixture = new Fixture(historyCapacity: 64, entityCount: 2);
        fixture.PresentPair(2_000);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4C3,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4C3, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);
        ChunkPublicationWork[] works =
        [
            new(default, Key(0, 0), ChunkInterestTier.Immediate,
                ChunkPublicationWorkKind.Dirty),
            new(default, Key(1, 0), ChunkInterestTier.Near,
                ChunkPublicationWorkKind.Dirty),
        ];
        var statuses = new RemoteChunkWorldStateScheduleStatus[2];
        ulong step = 0;

        for (int warm = 0; warm < 8; warm++) RunOne();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 64; iteration++) RunOne();
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

        void RunOne()
        {
            step++;
            fixture.PresentPair((step & 1) == 0 ? 2_100 : 2_200);
            works[0] = works[0] with { LatestSourceStep = step, FirstDueStep = step };
            works[1] = works[1] with { LatestSourceStep = step, FirstDueStep = step };
            Equal(2, server.TryScheduleDueBundle(works, step, statuses, out int messages));
            Equal(1, messages);
            ulong target = step * 2;
            for (int pump = 0; pump < 64 && server.GetSnapshot().AppliedProofs < target; pump++)
                Pump(loopback, server, client);
            Equal(target, server.GetSnapshot().AppliedProofs);
        }
    }

    internal static void CoalescedDeltaOverlapsLocallyAppliedUnprovedTruth()
    {
        using var fixture = new Fixture(historyCapacity: 64);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        int occupied = FindSubscription(recipient, Key(0, 0));
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xD312,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xD312, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        client.ConfigureTelemetry(NetworkTelemetryLevel.Detailed);

        ulong serverBase = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 1));
        DeliverToClientWithoutProof(loopback, server, client,
            () => client.GetSnapshot().AppliedDeltas == 1);
        Equal(1UL, client.GetSnapshot().AppliedDeltas);
        Equal(serverBase, recipient.Subscriptions[occupied].Cursor.Revision.Value);

        fixture.Present(0, Position(0, 0, 2_200, 2_000), GameWorldPublicPrefabIds.Character);
        ulong latest = CurrentRevision(fixture.Store, Key(0, 0));
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 2));
        DeliverToClientWithoutProof(loopback, server, client,
            () => client.GetSnapshot().AppliedDeltas == 2);
        RemoteChunkWorldStateClientSnapshot local = client.GetSnapshot();
        Equal(2UL, local.AppliedDeltas);
        Equal(0UL, local.RejectedDeliveries);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle handle));
        True(cache.TryGetInfo(handle, out ClientSemanticChunkInfo info));
        Equal(latest, info.PublicationCursor.Revision.Value);
        var clientProof = client.GetTimingSnapshot().ProofCensus;
        Equal(2UL, clientProof.EntriesGenerated);
        Equal(0UL, clientProof.EntriesQueued);
        Equal(2, clientProof.QueueCurrent);
        Equal(1UL, clientProof.PositiveSubsumptionOpportunities);
        RemoteChunkDeliveryCensusSnapshot clientDeliveries =
            client.GetTimingSnapshot().DeliveryCensus;
        Equal(2UL, clientDeliveries.AppliedDeltas);
        Equal(0UL, clientDeliveries.DuplicateDeliveries);
        Equal(0UL, clientDeliveries.SupersededDeliveries);
        RemoteChunkKnowledgeCensusSnapshot serverKnowledge =
            server.GetTimingSnapshot().Knowledge;
        Equal(2UL, serverKnowledge.Delta.Messages);
        Equal(1UL, serverKnowledge.Delta.OverlappingSameChunkMessages);
        Equal(2, serverKnowledge.Outstanding.Current);
        Equal(2, serverKnowledge.Outstanding.CurrentMaximumPerChunk);

        Drive(loopback, server, client, () =>
            server.GetSnapshot().OutstandingDeliveries == 0);
        Equal(latest, recipient.Subscriptions[occupied].Cursor.Revision.Value);
    }

    internal static void LaterExactProofAdvancesAndRetiresOnlySubsumedPositiveDebt()
    {
        using var fixture = new Fixture(historyCapacity: 64);
        fixture.Present(0, Position(0, 0, 2_000, 2_000), GameWorldPublicPrefabIds.Character);
        using ProtocolLoopback loopback = CreateLoopback(default);
        var recipient = Recipient(maximumRecords: 8);
        var cache = Cache(fixture, maximumPublicRecords: 8);
        ReconcileAndStageInitial(fixture, recipient, cache, Position(0, 0, 8_000, 8_000));
        SpatialChunkKey key = Key(0, 0);
        int occupied = FindSubscription(recipient, key);
        var server = new RemoteChunkWorldStateServer(
            loopback.Server, recipient, fixture.Store, 0xF4B,
            new RemoteChunkWorldStateTransportLimits(16));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xF4B, maximumDeltaRecords: 8,
            maximumPendingProofs: 16);
        server.ConfigureTelemetry(GameTelemetryLevel.Detailed);
        client.ConfigureTelemetry(NetworkTelemetryLevel.Detailed);

        ulong initial = recipient.Subscriptions[occupied].Cursor.Revision.Value;
        fixture.Present(0, Position(0, 0, 2_100, 2_000), GameWorldPublicPrefabIds.Character);
        ulong firstRevision = CurrentRevision(fixture.Store, key);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 1));
        DeliverToClientWithoutProof(loopback, server, client,
            () => client.GetSnapshot().AppliedDeltas == 1);

        fixture.Present(0, Position(0, 0, 2_200, 2_000), GameWorldPublicPrefabIds.Character);
        ulong laterRevision = CurrentRevision(fixture.Store, key);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 2));
        DeliverToClientWithoutProof(loopback, server, client,
            () => client.GetSnapshot().AppliedDeltas == 2);
        Equal(initial, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(2, server.GetSnapshot().OutstandingDeliveries);

        QueueProofEnvelope(loopback, server, new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = 0xF4B,
            PublicationEpoch = fixture.Store.Epoch.Value,
            DeliveryId = 2,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            Revision = laterRevision,
            Kind = RemoteChunkPublicationKind.Delta,
            Status = RemoteChunkPublicationApplyStatus.Applied,
        });
        Equal(laterRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
        RemoteChunkKnowledgeCensusSnapshot afterLater = server.GetTimingSnapshot().Knowledge;
        Equal(1UL, afterLater.Proof.EnvelopesReceived);
        Equal(1UL, afterLater.Proof.EntriesReceived);
        Equal(1UL, afterLater.Proof.AppliedEntries);
        Equal(1UL, afterLater.Proof.KnowledgeAdvancingEntries);
        Equal(1UL, afterLater.Proof.ExactOutstandingRetired);
        Equal(1UL, afterLater.Proof.SupersededOutstandingRetired);
        Equal(1UL, afterLater.Outstanding.InvalidatedByLaterAppliedTruth);
        Console.WriteLine(
            $"EVIDENCE PF4B timeline known_before={initial} source_a={firstRevision} " +
            $"source_b={laterRevision} delivered=1,2 client_applied=1,2 " +
            $"proofed=2 known_after={recipient.Subscriptions[occupied].Cursor.Revision.Value} " +
            $"exact_retired={afterLater.Proof.ExactOutstandingRetired} " +
            $"subsumed_retired={afterLater.Proof.SupersededOutstandingRetired}");

        ulong malformedBefore = server.GetSnapshot().MalformedProofs;
        QueueProofEnvelope(loopback, server, new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = 0xF4B,
            PublicationEpoch = fixture.Store.Epoch.Value,
            DeliveryId = 1,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            Revision = firstRevision,
            Kind = RemoteChunkPublicationKind.Delta,
            Status = RemoteChunkPublicationApplyStatus.Applied,
        });
        Equal(malformedBefore + 1, server.GetSnapshot().MalformedProofs);
        Equal(laterRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);

        fixture.Present(0, Position(0, 0, 2_300, 2_000), GameWorldPublicPrefabIds.Character);
        ulong thirdRevision = CurrentRevision(fixture.Store, key);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 3));
        fixture.Present(0, Position(0, 0, 2_400, 2_000), GameWorldPublicPrefabIds.Character);
        ulong fourthRevision = CurrentRevision(fixture.Store, key);
        Equal(RemoteChunkWorldStateScheduleStatus.QueuedDelta,
            server.TrySchedule(occupied, 4));
        Equal(2, server.GetSnapshot().OutstandingDeliveries);

        QueueProofEnvelope(loopback, server, new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = 0xF4B,
            PublicationEpoch = fixture.Store.Epoch.Value,
            DeliveryId = 4,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            Revision = fourthRevision,
            Kind = RemoteChunkPublicationKind.Delta,
            Status = RemoteChunkPublicationApplyStatus.Rejected,
        });
        Equal(1, server.GetSnapshot().OutstandingDeliveries);
        Equal(laterRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(1UL, server.GetTimingSnapshot().Knowledge.Proof.RejectedEntries);

        QueueProofEnvelope(loopback, server, new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = 0xF4B,
            PublicationEpoch = fixture.Store.Epoch.Value,
            DeliveryId = 3,
            SpatialSpaceId = key.Space.Value,
            ChunkX = key.Coord.X,
            ChunkZ = key.Coord.Z,
            Revision = thirdRevision,
            Kind = RemoteChunkPublicationKind.Delta,
            Status = RemoteChunkPublicationApplyStatus.Applied,
        });
        Equal(thirdRevision, recipient.Subscriptions[occupied].Cursor.Revision.Value);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
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
        Equal(RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy,
            server.TrySchedule(occupied, 2));
        Equal(0UL, server.GetSnapshot().SnapshotCancels);
        Equal(0UL, server.GetSnapshot().InvalidatedDeliveries);
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

    private static ProtocolLoopback CreateLoopbackWithMaximumApplicationBytes(
        int maximumApplicationBytes,
        int? maximumIndependentUnreliableMessages = null)
    {
        // Secure header + tag + canonical Data-frame header. The resulting public peer value is
        // asserted by every caller so this test cannot silently drift from Network framing.
        const int dataFrameHeaderBytes = 23;
        int maximumDatagramBytes = checked(
            maximumApplicationBytes + NetworkSecureDatagramFraming.HeaderBytes +
            NetworkSecureDatagramFraming.TagBytes + dataFrameHeaderBytes);
        True(ProtocolLoopback.TryCreate(
            maximumIndependentUnreliableMessages.HasValue
                ? NetworkProtocolLimits.Default with
                {
                    MaximumIndependentUnreliableMessages =
                        maximumIndependentUnreliableMessages.Value,
                }
                : NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, maximumDatagramBytes),
            default,
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

    private static void DrainClientWithoutProgress(
        ProtocolLoopback loopback,
        RemoteChunkWorldStateClient client)
    {
        while (loopback.Client.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            True(client.TryProcess(message, payload));
            True(loopback.Client.TryConsumeReceived());
        }
    }

    private static void DeliverToClientWithoutProof(
        ProtocolLoopback loopback,
        RemoteChunkWorldStateServer server,
        RemoteChunkWorldStateClient client,
        Func<bool> delivered)
    {
        for (int step = 0; step < 32; step++)
        {
            _ = server.TryProgress();
            loopback.Pump();
            DrainClientWithoutProgress(loopback, client);
            if (delivered()) return;
        }
        throw new InvalidOperationException("The unproved delta did not reach the client.");
    }

    private static void QueueProofEnvelope(
        ProtocolLoopback loopback,
        RemoteChunkWorldStateServer server,
        in RemoteChunkAppliedMessage proof)
    {
        Span<RemoteChunkAppliedEnvelopeEntry> entries =
            stackalloc RemoteChunkAppliedEnvelopeEntry[1];
        entries[0] = RemoteChunkAppliedEnvelopeEntry.FromMessage(in proof);
        Span<byte> payload = stackalloc byte[128];
        True(UltraSimGameProtocol.TryWriteChunkAppliedEnvelope(
            payload, proof.ApplicationSessionEpoch, proof.PublicationEpoch,
            entries, out int written));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(RemoteChunkAppliedEnvelopeHeaderCodec.Descriptor.Id,
                payload[..written]));
        for (int step = 0; step < 32; step++)
        {
            loopback.Pump();
            bool processed = false;
            while (loopback.Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> delivered))
            {
                _ = server.TryProcess(message, delivered);
                True(loopback.Server.TryConsumeReceived());
                processed = true;
            }
            if (processed) return;
        }
        throw new InvalidOperationException("The PF4B proof envelope did not reach the server.");
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
        private readonly PreparedChunkPublicationReadModel _readModel;

        internal Fixture(
            int historyCapacity,
            int entityCount = 4,
            PreparedChunkPublicationReadModel readModel =
                PreparedChunkPublicationReadModel.PreparedLatestState)
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
            _readModel = readModel;
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

        internal void PresentPair(int localX)
        {
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character, WorldYaw.Zero,
                WorldPublicStateFlags.Visible);
            Span<WorldPublicChange> changes = stackalloc WorldPublicChange[2];
            changes[0] = WorldPublicChange.Present(
                _entities[0], Position(0, 0, localX, 2_000), descriptor);
            changes[1] = WorldPublicChange.Present(
                _entities[1], Position(1, 0, localX, 2_000), descriptor);
            True(Store.TryCommit(changes, out _));
        }

        internal void PresentAll(int localX, uint prefab)
        {
            var descriptor = new WorldPublicDescriptor(
                prefab, WorldYaw.Zero, WorldPublicStateFlags.Visible);
            var changes = new WorldPublicChange[_entities.Length];
            for (int index = 0; index < changes.Length; index++)
                changes[index] = WorldPublicChange.Present(
                    _entities[index], Position(0, 0, localX + index, 2_000), descriptor);
            True(Store.TryCommit(changes, out _));
        }

        internal void PresentAllAcrossChunks(int localX, int chunkStride)
        {
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character, WorldYaw.Zero,
                WorldPublicStateFlags.Visible);
            var changes = new WorldPublicChange[_entities.Length];
            for (int index = 0; index < changes.Length; index++)
                changes[index] = WorldPublicChange.Present(
                    _entities[index], Position(index * chunkStride, 0, localX, 2_000),
                    descriptor);
            True(Store.TryCommit(changes, out _));
        }

        internal void Remove(int entity) =>
            True(Store.TryCommit([WorldPublicChange.Absent(_entities[entity])], out _));

        internal void CommitEmpty() => True(Store.TryCommit([], out _));

        internal void ReplaceStore(int historyCapacity) => Store = CreateStore(historyCapacity);

        private ChunkPublicationStore CreateStore(int historyCapacity) => new(
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(
                _entityCapacity, checked(_entityCapacity * 2), 2, historyCapacity),
            _readModel);

        public void Dispose() => _world.Dispose();
    }
}
