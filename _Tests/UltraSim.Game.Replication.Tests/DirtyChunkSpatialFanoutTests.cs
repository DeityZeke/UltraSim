using UltraSim.ECS;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication.Tests;

internal static class DirtyChunkSpatialFanoutTests
{
    internal static ReplicationProfileConformanceObservation CaptureProfileConformance()
    {
        using var fixture = new Fixture(8, 4);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, interest: new ChunkInterestProfile(
            5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId participant = new(0xD002);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(DirtyChunkProcessStatus.Success,
            service.TryProcessDirtyChunks(0, out int processed));
        Equal(1, processed);
        DirtyChunkSpatialFanoutSnapshot disclosed = service.GetSnapshot();
        True(disclosed.PendingChunkObligationCount > 0);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 0, out DirtyChunkDelivery first));
        bool currentTruth = first.Status == OfflineDirectChunkReadStatus.Snapshot &&
            first.Snapshot.Length == 1 && first.Key == key;
        DirtyChunkSpatialFanoutSnapshot sent = service.GetSnapshot();
        bool sentDidNotAdvance = sent.ApplicationProofsApplied == 0 &&
            sent.OutstandingDeliveryCount == 1;
        Equal(DirtyChunkAppliedProofStatus.Rejected,
            service.TryAcknowledgeDelivery(participant, first.DeliveryId, applied: false));
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 1, out DirtyChunkDelivery retry));
        bool stableIdentity = retry.Ticket.Key == first.Ticket.Key;
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, retry.DeliveryId, applied: true));
        DirtyChunkSpatialFanoutSnapshot applied = service.GetSnapshot();
        return new ReplicationProfileConformanceObservation(
            GamePublicReplicationProfileId.DirtyChunkSpatialFanout,
            currentTruth,
            disclosed.RecipientChunkKnowledgeRowCount > 0,
            stableIdentity,
            sentDidNotAdvance,
            applied.DuplicateStaleRejectedProofs > 0,
            applied.ApplicationProofsApplied == 1 && applied.PendingChunkObligationCount == 0,
            applied.ReservedBytes);
    }

    internal static void WorkShapeAndCleanSteadyState()
    {
        using var fixture = new Fixture(32, 16);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 8);
        ParticipantId participant = new(1);
        SpatialChunkKey key = Key(0, 0);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(DirtyChunkProcessStatus.Success, service.TryProcessDirtyChunks(0, out _));
        Apply(service, participant, key, 0);
        DirtyChunkSpatialFanoutSnapshot cleanBefore = service.GetSnapshot();
        Equal(DirtyChunkProcessStatus.NoData, service.TryProcessDirtyChunks(1, out int cleanProcessed));
        Equal(0, cleanProcessed);
        DirtyChunkSpatialFanoutSnapshot cleanAfter = service.GetSnapshot();
        Equal(cleanBefore.SpatialRecipientDiscoveryOperations,
            cleanAfter.SpatialRecipientDiscoveryOperations);
        Equal(cleanBefore.PublicationReads, cleanAfter.PublicationReads);

        fixture.Present(0, Position(0, 0, 2_100, 2_000));
        Equal(DirtyChunkProcessStatus.Success, service.TryProcessDirtyChunks(2, out int dirtyProcessed));
        Equal(1, dirtyProcessed);
        DirtyChunkSpatialFanoutSnapshot dirty = service.GetSnapshot();
        Equal(cleanAfter.SpatialRecipientDiscoveryOperations + 1,
            dirty.SpatialRecipientDiscoveryOperations);
        Apply(service, participant, key, 2);
    }

    internal static void RecipientOutstandingIsolationAndReservedMemoryAreExact()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        fixture.Present(1, Position(1, 0, 2_000, 2_000));
        ChunkInterestProfile interest = new(20_000, 21_000, 21_000, 22_000, 22_000, 24_000);
        var service = Create(
            fixture, maximumRecipients: 2, maximumRecipientChunkRows: 128,
            maximumOutstandingDeliveries: 2, interest: interest);
        long reservedBeforeAdmission = service.GetSnapshot().ReservedBytes;
        ParticipantId slow = new(1);
        ParticipantId healthy = new(2);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(slow, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(healthy, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(reservedBeforeAdmission, service.GetSnapshot().ReservedBytes);

        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(slow, Key(0, 0), 0, out DirtyChunkDelivery retained));
        Equal(DirtyChunkBuildStatus.CapacityExceeded,
            service.TryBuildDelivery(slow, Key(1, 0), 0, out _));
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(healthy, Key(0, 0), 0, out DirtyChunkDelivery progressed));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(healthy, progressed.DeliveryId, true));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(slow, retained.DeliveryId, true));
    }

    internal static void SameChunkRowsCoalesceIntoOneDiscovery()
    {
        using var fixture = new Fixture(64, 16);
        WorldPosition[] initialPositions = new WorldPosition[8];
        for (int index = 0; index < initialPositions.Length; index++)
            initialPositions[index] = Position(0, 0, 2_000 + index, 2_000);
        fixture.PresentBatch(initialPositions);
        var service = Create(fixture);
        ParticipantId participant = new(2);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, participant, key, 0);
        DirtyChunkSpatialFanoutSnapshot before = service.GetSnapshot();

        WorldPosition[] positions = new WorldPosition[8];
        for (int index = 0; index < positions.Length; index++)
            positions[index] = Position(0, 0, 3_000 + index, 2_000);
        fixture.PresentBatch(positions);
        Equal(DirtyChunkProcessStatus.Success, service.TryProcessDirtyChunks(1, out int processed));
        Equal(1, processed);
        DirtyChunkSpatialFanoutSnapshot after = service.GetSnapshot();
        Equal(before.SpatialRecipientDiscoveryOperations + 1,
            after.SpatialRecipientDiscoveryOperations);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 1, out DirtyChunkDelivery delivery));
        True(delivery.Deltas.Length >= 1 || delivery.Snapshot.Length >= 1);
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, delivery.DeliveryId, true));
    }

    internal static void SpatialCandidatesAvoidFarRecipientsAndMatchOracle()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 8);
        ParticipantId near = new(3);
        ParticipantId far = new(4);
        service.SetRecipientAnchor(near, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.SetRecipientAnchor(far, GameWorldState.DefaultSpace,
            Position(1_000, 1_000, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        DirtyChunkSpatialFanoutSnapshot snapshot = service.GetSnapshot();
        Equal(1UL, snapshot.SpatialRecipientDiscoveryOperations);
        Equal(1UL, snapshot.InterestedRecipientsDiscovered);
        True(snapshot.CandidateRecipientsExamined < (ulong)snapshot.RecipientCount);
        Apply(service, near, Key(0, 0), 0);
        Equal(DirtyChunkBuildStatus.NoData,
            service.TryBuildDelivery(far, Key(0, 0), 0, out _));
    }

    internal static void CoarseOccupiedRecipientBucketsPreserveDiscovery()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        int[] edges = [16_000, 32_000, 64_000, 128_000];
        ulong previousCells = ulong.MaxValue;
        ulong expectedInterested = 0;
        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            DirtyChunkSpatialFanout service = Create(
                fixture, maximumRecipients: 8, spatialRegionEdgeMillimeters: edges[edgeIndex]);
            for (int recipient = 0; recipient < 8; recipient++)
            {
                int chunk = recipient - 4;
                Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
                    service.SetRecipientAnchor(new ParticipantId((ulong)(recipient + 1)),
                        GameWorldState.DefaultSpace, Position(chunk, 0, 8_000, 8_000), 0));
            }
            Equal(DirtyChunkProcessStatus.Success,
                service.TryProcessDirtyChunks(0, out int processed));
            Equal(1, processed);
            DirtyChunkSpatialFanoutSnapshot snapshot = service.GetSnapshot();
            if (edgeIndex == 0) expectedInterested = snapshot.InterestedRecipientsDiscovered;
            else Equal(expectedInterested, snapshot.InterestedRecipientsDiscovered);
            True(snapshot.RecipientSpatialCellsExamined < previousCells);
            previousCells = snapshot.RecipientSpatialCellsExamined;
        }
    }

    internal static void CleanEntryLeaveReentryAndTeleportAreFenced()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture);
        ParticipantId participant = new(5);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 0, out DirtyChunkDelivery first));
        True(first.Status == OfflineDirectChunkReadStatus.Snapshot);
        True(service.RemoveRecipient(participant));
        Equal(DirtyChunkAppliedProofStatus.Stale,
            service.TryAcknowledgeDelivery(participant, first.DeliveryId, true));
        DirtyChunkSpatialFanoutSnapshot afterLeave = service.GetSnapshot();
        Equal(0, afterLeave.PendingChunkObligationCount);

        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 1);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 1, out DirtyChunkDelivery reentry));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, reentry.DeliveryId, true));

        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(20, 20, 8_000, 8_000), 2);
        Equal(DirtyChunkBuildStatus.NoData,
            service.TryBuildDelivery(participant, key, 2, out _));
    }

    internal static void ImmediateNearFarCadenceAndDeferredCoalescing()
    {
        using var fixture = new Fixture(64, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 8,
            cadence: new OfflineDirectChunkCadence(1, 2, 4));
        ParticipantId immediate = new(6);
        ParticipantId near = new(7);
        ParticipantId far = new(8);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(immediate, GameWorldState.DefaultSpace,
            Position(0, 0, 0, 0), 0);
        service.SetRecipientAnchor(near, GameWorldState.DefaultSpace,
            Position(5, 0, 0, 0), 0);
        service.SetRecipientAnchor(far, GameWorldState.DefaultSpace,
            Position(10, 0, 0, 0), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, immediate, key, 0);
        ApplyAtAvailableStep(service, near, key, 0);
        ApplyAtAvailableStep(service, far, key, 0);
        DirtyChunkSpatialFanoutSnapshot beforeDirty = service.GetSnapshot();
        fixture.Present(0, Position(0, 0, 2_100, 2_000));
        service.TryProcessDirtyChunks(1, out _);
        DirtyChunkSpatialFanoutSnapshot discovered = service.GetSnapshot();
        Equal(beforeDirty.SpatialRecipientDiscoveryOperations + 1,
            discovered.SpatialRecipientDiscoveryOperations);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(immediate, key, 1, out DirtyChunkDelivery now));
        service.TryAcknowledgeDelivery(immediate, now.DeliveryId, true);
        DirtyChunkBuildStatus deferred = service.TryBuildDelivery(near, key, 1, out _);
        True(deferred is DirtyChunkBuildStatus.NotDue or DirtyChunkBuildStatus.Success);
        DirtyChunkSpatialFanoutSnapshot afterCadence = service.GetSnapshot();
        Equal(discovered.SpatialRecipientDiscoveryOperations,
            afterCadence.SpatialRecipientDiscoveryOperations);

        // A second mutation coalesces the deferred obligation; it never queues a second key.
        fixture.Present(0, Position(0, 0, 2_200, 2_000));
        service.ObserveDirtyChunks();
        fixture.Present(0, Position(0, 0, 2_300, 2_000));
        service.ObserveDirtyChunks();
        DirtyChunkSpatialFanoutSnapshot coalesced = service.GetSnapshot();
        Equal(1, coalesced.DirtyChunkCount);
        True(coalesced.DirtyChunksCoalescedBeforeDrain > 0);
        service.TryProcessDirtyChunks(4, out _);
        ApplyAtAvailableStep(service, near, key, 4);
        ApplyAtAvailableStep(service, far, key, 4);
    }

    internal static void HistoryFallbackCapacityAndEpochRecoveryAreBounded()
    {
        using var fixture = new Fixture(2, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 2);
        ParticipantId participant = new(9);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, participant, key, 0);
        for (int update = 0; update < 4; update++)
        {
            fixture.Present(0, Position(0, 0, 2_100 + update, 2_000));
            service.ObserveDirtyChunks();
        }
        service.TryProcessDirtyChunks(1, out _);
        Apply(service, participant, key, 1);
        True(service.GetSnapshot().SnapshotFallbacks > 0);

        ChunkPublicationStore replacement = new(
            new PublicationEpoch(0xBEEF),
            new ChunkPublicationLimits(4, 8, 2, 8));
        var descriptor = new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible);
        replacement.TryCommit([WorldPublicChange.Present(
            fixture.Entity(0), Position(0, 0, 2_000, 2_000), descriptor)], out _);
        True(service.ReplaceSource(replacement));
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 2, out DirtyChunkDelivery epoch));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, epoch.DeliveryId, true));
    }

    internal static void CommitGapFailsClosedAndRetainsRecovery()
    {
        using var fixture = new Fixture(16, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumDirtyChunks: 4);
        ParticipantId participant = new(10);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, participant, Key(0, 0), 0);
        fixture.Present(0, Position(0, 0, 2_100, 2_000));
        fixture.Present(0, Position(0, 0, 2_200, 2_000));
        Equal(DirtyChunkProcessStatus.CommitGapRequiresRecovery,
            service.TryProcessDirtyChunks(1, out _));
        True(service.GetSnapshot().CommitGapRecoveries > 0);
        ApplyAtAvailableStep(service, participant, Key(0, 0), 1);
    }

    internal static void SourceAbsenceIsDistinctFromAoiLeave()
    {
        using var fixture = new Fixture(16, 1);
        SpatialChunkKey key = Key(0, 0);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 2,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId participant = new(13);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, participant, key, 0);

        fixture.Present(0, Position(1, 0, 2_000, 2_000));
        service.TryProcessDirtyChunks(1, out _);
        Apply(service, participant, key, 1);
        fixture.Present(0, Position(2, 0, 2_000, 2_000));
        Equal(DirtyChunkProcessStatus.CommitGapRequiresRecovery,
            service.TryProcessDirtyChunks(2, out _));
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 2, out DirtyChunkDelivery absent));
        Equal(OfflineDirectChunkReadStatus.ChunkAbsent, absent.Status);
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, absent.DeliveryId, true));

        True(service.RemoveRecipient(participant));
        Equal(0, service.GetSnapshot().PendingChunkObligationCount);
    }

    internal static void DirtyQueueCapacityRetainsAndRecovers()
    {
        using var fixture = new Fixture(16, 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 2, maximumDirtyChunks: 1,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        Equal(DirtyChunkProcessStatus.Success, service.ObserveDirtyChunks());
        Equal(1, service.GetSnapshot().DirtyChunkCount);
        fixture.Present(1, Position(1, 0, 2_000, 2_000));
        Equal(DirtyChunkProcessStatus.CapacityExceeded, service.ObserveDirtyChunks());
        Equal(DirtyChunkProcessStatus.CapacityExceeded,
            service.TryProcessDirtyChunks(0, out int firstProcessed));
        Equal(1, firstProcessed);
        Equal(0, service.GetSnapshot().DirtyChunkCount);
        Equal(DirtyChunkProcessStatus.Success,
            service.TryProcessDirtyChunks(1, out int recoveredProcessed));
        Equal(1, recoveredProcessed);
        Equal(0, service.GetSnapshot().DirtyChunkCount);
    }

    internal static void RecipientReconciliationCapacityIsTransactional()
    {
        using var fixture = new Fixture(16, 1);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 2, maximumRecipientChunkRows: 1,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId first = new(14);
        ParticipantId second = new(15);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(first, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(DirtyChunkRecipientUpdateStatus.CapacityExceeded,
            service.SetRecipientAnchor(second, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        DirtyChunkSpatialFanoutSnapshot snapshot = service.GetSnapshot();
        Equal(1, snapshot.RecipientCount);
        Equal(1, snapshot.RecipientChunkKnowledgeRowCount);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(first, Key(0, 0), 0, out DirtyChunkDelivery delivery));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(first, delivery.DeliveryId, true));
    }

    internal static void ExistingRecipientExpansionIsTransactional()
    {
        using var fixture = new Fixture(16, 2);
        var interest = new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000);
        var service = Create(fixture, maximumRecipients: 2, maximumRecipientChunkRows: 3,
            interest: interest);
        ParticipantId first = new(16);
        ParticipantId second = new(17);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(first, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), 0));
        Equal(1, service.GetSnapshot().RecipientChunkKnowledgeRowCount);
        Equal(DirtyChunkRecipientUpdateStatus.Reconciled,
            service.SetRecipientAnchor(second, GameWorldState.DefaultSpace,
                Position(0, 0, 15_500, 8_000), 0));
        // R2 owns (0,0) and (1,0); R1 owns (0,0). R1's move retains (0,0) and enters (1,0).
        Equal(3, service.GetSnapshot().RecipientChunkKnowledgeRowCount);
        Equal(DirtyChunkRecipientUpdateStatus.CapacityExceeded,
            service.SetRecipientAnchor(first, GameWorldState.DefaultSpace,
                Position(0, 0, 15_500, 8_000), 1));
        DirtyChunkSpatialFanoutSnapshot snapshot = service.GetSnapshot();
        Equal(2, snapshot.RecipientCount);
        Equal(3, snapshot.RecipientChunkKnowledgeRowCount);
        Equal(3, snapshot.PendingChunkObligationCount);
    }

    internal static void LostProofExpiresAndRetries()
    {
        using var fixture = new Fixture(16, 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumDeliveryAgeSteps: 3);
        ParticipantId participant = new(18);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 0, out DirtyChunkDelivery lost));
        Equal(1, service.GetSnapshot().OutstandingDeliveryCount);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, 3, out DirtyChunkDelivery retry));
        True(retry.DeliveryId != lost.DeliveryId);
        DirtyChunkSpatialFanoutSnapshot expired = service.GetSnapshot();
        Equal(1UL, expired.DeliveryTimeouts);
        Equal(DirtyChunkAppliedProofStatus.Stale,
            service.TryAcknowledgeDelivery(participant, lost.DeliveryId, applied: true));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, retry.DeliveryId, applied: true));
        Equal(1UL, service.GetSnapshot().ApplicationProofsApplied);
    }

    internal static void DeferredRowDoesNotBlockImmediate()
    {
        using var fixture = new Fixture(32, 2);
        fixture.Present(0, Position(-1, 0, 8_000, 8_000));
        fixture.Present(1, Position(0, 0, 8_000, 8_000));
        var service = Create(fixture, maximumRecipients: 1,
            cadence: new OfflineDirectChunkCadence(1, 2, 4),
            interest: new ChunkInterestProfile(8_000, 8_500, 9_000, 10_000, 14_000, 16_000));
        ParticipantId participant = new(19);
        SpatialChunkKey far = Key(-1, 0);
        SpatialChunkKey immediate = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 15_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        while (service.TryBuildDelivery(participant, 0, out DirtyChunkDelivery initial) ==
               DirtyChunkBuildStatus.Success)
            Equal(DirtyChunkAppliedProofStatus.Applied,
                service.TryAcknowledgeDelivery(participant, initial.DeliveryId, true));

        fixture.PresentBatch([
            Position(-1, 0, 8_100, 8_000),
            Position(0, 0, 8_100, 8_000)]);
        service.TryProcessDirtyChunks(1, out _);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, 1, out DirtyChunkDelivery delivery));
        Equal(immediate, delivery.Key);
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, delivery.DeliveryId, true));
    }

    internal static void DistributedDirtyWorkShape()
    {
        using var fixture = new Fixture(32, 8);
        fixture.PresentBatch([
            Position(0, 0, 2_000, 2_000),
            Position(1, 0, 2_000, 2_000),
            Position(2, 0, 2_000, 2_000)]);
        var interest = new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000);
        var service = Create(fixture, maximumRecipients: 3, interest: interest);
        ParticipantId first = new(20);
        ParticipantId second = new(21);
        ParticipantId third = new(22);
        service.SetRecipientAnchor(first, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.SetRecipientAnchor(second, GameWorldState.DefaultSpace,
            Position(1, 0, 8_000, 8_000), 0);
        service.SetRecipientAnchor(third, GameWorldState.DefaultSpace,
            Position(2, 0, 8_000, 8_000), 0);
        Equal(DirtyChunkProcessStatus.Success,
            service.TryProcessDirtyChunks(0, out int processed));
        Equal(3, processed);
        DirtyChunkSpatialFanoutSnapshot snapshot = service.GetSnapshot();
        Equal(3UL, snapshot.SpatialRecipientDiscoveryOperations);
        Equal(3UL, snapshot.InterestedRecipientsDiscovered);
        True(snapshot.CandidateRecipientsExamined <= 9);
        Apply(service, first, Key(0, 0), 0);
        Apply(service, second, Key(1, 0), 0);
        Apply(service, third, Key(2, 0), 0);
    }

    internal static void MatchesCanonicalChunkPublicationTrace()
    {
        using var fixture = new Fixture(32, 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var interest = new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000);
        var cadence = new OfflineDirectChunkCadence(1, 1, 1);
        var canonical = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(4, 16, 16), interest, cadence);
        Span<OfflineDirectChunkSubscriptionChange> changes = stackalloc OfflineDirectChunkSubscriptionChange[8];
        Equal(OfflineDirectChunkReconcileStatus.Reconciled,
            canonical.Reconcile(fixture.Store, GameWorldState.DefaultSpace,
                Position(0, 0, 8_000, 8_000), changes, out _));
        var service = Create(fixture, maximumRecipients: 1, cadence: cadence, interest: interest);
        ParticipantId participant = new(23);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Equal(OfflineDirectChunkReadStatus.Snapshot,
            canonical.TryBeginRead(fixture.Store, 0, 0, out OfflineDirectChunkRead canonicalFirst));
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, Key(0, 0), 0, out DirtyChunkDelivery dirtyFirst));
        Equal(canonicalFirst.Status, dirtyFirst.Status);
        Equal(canonicalFirst.Batch.Cursor, dirtyFirst.Cursor);
        Equal(canonicalFirst.Snapshot.Length, dirtyFirst.Snapshot.Length);
        Equal(canonicalFirst.Snapshot[0], dirtyFirst.Snapshot[0]);
        OfflineDirectChunkDeliveryTicket canonicalTicket = canonical.ReleaseReadForRemote(1);
        Equal(OfflineDirectChunkAppliedProofStatus.Applied,
            canonical.CommitAppliedProof(canonicalTicket));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, dirtyFirst.DeliveryId, true));

        fixture.Present(0, Position(0, 0, 2_100, 2_000));
        canonical.TryBeginRead(fixture.Store, 0, 1, out OfflineDirectChunkRead canonicalSecond);
        service.TryProcessDirtyChunks(1, out _);
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, Key(0, 0), 1, out DirtyChunkDelivery dirtySecond));
        Equal(canonicalSecond.Status, dirtySecond.Status);
        Equal(canonicalSecond.Batch.Cursor, dirtySecond.Cursor);
        Equal(canonicalSecond.Deltas.Length, dirtySecond.Deltas.Length);
        Equal(canonicalSecond.Deltas[0], dirtySecond.Deltas[0]);
    }

    internal static void WarmedPathAllocatesZero()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 2);
        ParticipantId participant = new(11);
        SpatialChunkKey key = Key(0, 0);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        service.TryProcessDirtyChunks(0, out _);
        Apply(service, participant, key, 0);
        fixture.Present(0, Position(0, 0, 2_001, 2_000));
        service.TryProcessDirtyChunks(1, out _);
        Apply(service, participant, key, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong step = 2; step < 32; step++)
        {
            fixture.Present(0, Position(0, 0, checked((int)(2_000 + step)), 2_000));
            service.TryProcessDirtyChunks(step, out _);
            Apply(service, participant, key, step);
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    internal static void RemoteNetworkProtocolPeerConverges()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        ChunkInterestProfile singleChunk = new(5_000, 5_500, 5_500, 6_000, 6_000, 7_000);
        var service = Create(fixture, maximumRecipients: 2, interest: singleChunk);
        ParticipantId participant = new(12);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _,
            UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyChunkSpatialFanoutServer(
            loopback.Server, service, participant, 0xD202);
        var cache = new ClientWorldSemanticCache(
            fixture.Bound, new ClientWorldSemanticCacheLimits(8, 32, 16, 16, 32));
        True(cache.TryRequest(Key(0, 0), out ClientSemanticChunkHandle residentHandle, out _));
        True(cache.TryStageFromBaseWorld(residentHandle, [], default, out _));
        True(cache.TryPublish(residentHandle, out _));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xD202, maximumDeltaRecords: 16, maximumPendingProofs: 16);

        for (ulong step = 0; step < 64 && server.GetSnapshot().ApplicationProofs == 0; step++)
        {
            server.TrySchedule(step);
            server.TryProgress();
            loopback.Pump();
            DrainClient(loopback, client);
            client.TryProgress();
            DrainServer(loopback, server);
        }
        Equal(1UL, server.GetSnapshot().ApplicationProofs);
        True(cache.TryGetResidentHandle(Key(0, 0), out ClientSemanticChunkHandle handle));
        True(cache.TryGetInfo(handle, out ClientSemanticChunkInfo info));
        Equal(service.Source.Epoch, info.PublicationCursor.Epoch);
    }

    internal static void RemoteLostProofRetriesAndDrains()
    {
        using var fixture = new Fixture(32, 4);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 1,
            maximumOutstandingDeliveries: 1, maximumDeliveryAgeSteps: 2,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId participant = new(24);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyChunkSpatialFanoutServer(
            loopback.Server, service, participant, 0xD204);
        var cache = new ClientWorldSemanticCache(
            fixture.Bound, new ClientWorldSemanticCacheLimits(8, 32, 16, 16, 32));
        True(cache.TryRequest(Key(0, 0), out ClientSemanticChunkHandle residentHandle, out _));
        True(cache.TryStageFromBaseWorld(residentHandle, [], default, out _));
        True(cache.TryPublish(residentHandle, out _));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xD204, maximumDeltaRecords: 16, maximumPendingProofs: 16);

        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(0));
        True(server.TryProgress());
        loopback.Pump();
        loopback.Pump();
        DrainClient(loopback, client);
        for (int step = 0; step < 4; step++)
        {
            loopback.Pump();
            DrainClient(loopback, client);
        }
        client.TryProgress(); // Application succeeds; its proof remains deliberately undelivered.
        Equal(1, server.GetSnapshot().OutstandingDeliveries);

        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(2));
        True(server.TryProgress());
        RemoteDirtyChunkSpatialFanoutServerSnapshot retried = server.GetSnapshot();
        Equal(1, retried.OutstandingDeliveries);
        Equal(1UL, retried.DeliveryTimeouts);

        for (int step = 0; step < 8 && server.GetSnapshot().ApplicationProofs == 0; step++)
        {
            loopback.Pump();
            DrainClient(loopback, client); // Repeated semantic truth is a safe duplicate.
            client.TryProgress();
            DrainServer(loopback, server); // The old proof is stale at the adapter ledger.
        }
        Equal(1UL, server.GetSnapshot().ApplicationProofs);
        Equal(1UL, service.GetSnapshot().ApplicationProofsApplied);
        Equal(0, server.GetSnapshot().OutstandingDeliveries);
    }

    internal static void SnapshotTimeoutIsBounded()
    {
        using var fixture = new Fixture(16, 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 1,
            maximumOutstandingDeliveries: 1, maximumDeliveryAgeSteps: 2,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId participant = new(25);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyChunkSpatialFanoutServer(
            loopback.Server, service, participant, 0xD205);
        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(0));
        Equal(1, server.GetSnapshot().OutstandingDeliveries);
        True(server.GetSnapshot().SnapshotTransferActive);
        Equal(1, server.ExpireOutstandingDeliveries(2));
        RemoteDirtyChunkSpatialFanoutServerSnapshot expired = server.GetSnapshot();
        Equal(0, expired.OutstandingDeliveries);
        Equal(1UL, expired.SnapshotTimeouts);
        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(2));
    }

    internal static void SnapshotCancelBackpressureDoesNotOverlapLifetimes()
    {
        using var fixture = new Fixture(32, 16);
        WorldPosition[] positions = new WorldPosition[16];
        for (int index = 0; index < positions.Length; index++)
            positions[index] = Position(0, 0, 2_000 + index, 2_000);
        fixture.PresentBatch(positions);
        var service = Create(fixture, maximumRecipients: 1,
            maximumOutstandingDeliveries: 1, maximumDeliveryAgeSteps: 2,
            interest: new ChunkInterestProfile(5_000, 5_500, 5_500, 6_000, 6_000, 7_000));
        ParticipantId participant = new(27);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);

        NetworkProtocolLimits constrained = NetworkProtocolLimits.Default with
        {
            MaximumReliableMessages = 3,
        };
        True(ProtocolLoopback.TryCreate(
            constrained,
            new DeterministicDatagramLimits(256, 512),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyChunkSpatialFanoutServer(
            loopback.Server, service, participant, 0xD207);
        var cache = new ClientWorldSemanticCache(
            fixture.Bound, new ClientWorldSemanticCacheLimits(8, 32, 16, 16, 32));
        True(cache.TryRequest(Key(0, 0), out ClientSemanticChunkHandle residentHandle, out _));
        True(cache.TryStageFromBaseWorld(residentHandle, [], default, out _));
        True(cache.TryPublish(residentHandle, out _));
        var client = new RemoteChunkWorldStateClient(
            loopback.Client, cache, 0xD207, maximumDeltaRecords: 16, maximumPendingProofs: 16);

        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(0));
        True(!server.TryProgress()); // Begin plus segments fill the reliable queue before commit.
        True(loopback.Server.ReliableQueued >= 1);
        True(server.GetSnapshot().SnapshotTransferActive);

        Equal(1, server.ExpireOutstandingDeliveries(2));
        RemoteDirtyChunkSpatialFanoutServerSnapshot expired = server.GetSnapshot();
        Equal(0, expired.OutstandingDeliveries);
        Equal(1UL, expired.SnapshotTimeouts);
        True(expired.SnapshotCancelPending);
        True(!expired.SnapshotTransferActive);

        // The retained cancel is a live snapshot lifetime. No second snapshot may start
        // while network pressure prevents that cancellation from being emitted.
        Equal(DirtyChunkBuildStatus.CapacityExceeded, server.TrySchedule(3));
        Equal(DirtyChunkBuildStatus.CapacityExceeded, server.TrySchedule(4));
        RemoteDirtyChunkSpatialFanoutServerSnapshot blocked = server.GetSnapshot();
        True(blocked.SnapshotCancelPending);
        True(!blocked.SnapshotTransferActive);
        Equal(0, blocked.OutstandingDeliveries);

        for (int attempt = 0; attempt < 16 && loopback.Server.ReliableQueued != 0; attempt++)
        {
            loopback.Pump();
            DrainClient(loopback, client);
            client.TryProgress();
            DrainServer(loopback, server);
        }
        True(loopback.Server.ReliableQueued == 0);
        True(server.TryProgress()); // Cancel(A) is now retained no longer.
        True(!server.GetSnapshot().SnapshotCancelPending);

        for (int attempt = 0; attempt < 16; attempt++)
        {
            loopback.Pump();
            DrainClient(loopback, client);
            client.TryProgress();
            DrainServer(loopback, server);
        }
        True(!client.GetSnapshot().SnapshotCandidateActive);

        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(5));
        for (int attempt = 0; attempt < 64 && server.GetSnapshot().ApplicationProofs == 0; attempt++)
        {
            server.TryProgress();
            loopback.Pump();
            DrainClient(loopback, client);
            client.TryProgress();
            DrainServer(loopback, server);
        }

        RemoteDirtyChunkSpatialFanoutServerSnapshot converged = server.GetSnapshot();
        Equal(1UL, converged.ApplicationProofs);
        Equal(0, converged.OutstandingDeliveries);
        True(!converged.SnapshotCancelPending);
        True(!converged.SnapshotTransferActive);
        Equal(1UL, client.GetSnapshot().AppliedSnapshots);
        Equal(0, loopback.Server.ReliableQueued);
    }

    internal static void FamiliesCoexistAndPrivateDoesNotLeak()
    {
        using var fixture = new Fixture(16, 2);
        fixture.Present(0, Position(0, 0, 2_000, 2_000));
        var service = Create(fixture, maximumRecipients: 1);
        ParticipantId participant = new(26);
        service.SetRecipientAnchor(participant, GameWorldState.DefaultSpace,
            Position(0, 0, 8_000, 8_000), 0);
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyChunkSpatialFanoutServer(
            loopback.Server, service, participant, 0xD206);
        Equal(DirtyChunkBuildStatus.Success, server.TrySchedule(0));

        Span<byte> controlPayload = stackalloc byte[RemoteClockAnchorMessageCodec.EncodedSize];
        RemoteClockAnchorMessage control = new()
        {
            ApplicationSessionEpoch = 0xD206, StateEpoch = 1, ClockRevision = 1,
            EffectiveFromSimulationTick = 0, SimulationFrequencyHz = 60,
            WorldTimeMicroseconds = 0, WorldTimeFractionQuanta = 0,
        };
        True(RemoteClockAnchorMessageCodec.TryWriteExact(controlPayload, in control));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(RemoteClockAnchorMessageCodec.Descriptor.Id, controlPayload));
        Span<byte> eventPayload = stackalloc byte[RemoteApplicationEventMessageCodec.EncodedSize];
        RemoteApplicationEventMessage applicationEvent = new()
        {
            ApplicationSessionEpoch = 0xD206, StateEpoch = 1, Sequence = 1,
            ParticipantId = participant.Value, Kind = 1, Subject = 2, PayloadHash = 3,
        };
        True(RemoteApplicationEventMessageCodec.TryWriteExact(eventPayload, in applicationEvent));
        Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(RemoteApplicationEventMessageCodec.Descriptor.Id, eventPayload));
        loopback.Pump();
        loopback.Pump();
        int controlMessages = 0;
        int eventMessages = 0;
        while (loopback.Server.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            if (message.MessageId == RemoteClockAnchorMessageCodec.Descriptor.Id) controlMessages++;
            if (message.MessageId == RemoteApplicationEventMessageCodec.Descriptor.Id) eventMessages++;
            True(!server.TryProcess(message, payload));
            True(loopback.Server.TryConsumeReceived());
        }
        Equal(1, controlMessages);
        Equal(1, eventMessages);
    }

    private static void Apply(DirtyChunkSpatialFanout service, ParticipantId participant,
        SpatialChunkKey key, ulong step)
    {
        Equal(DirtyChunkBuildStatus.Success,
            service.TryBuildDelivery(participant, key, step, out DirtyChunkDelivery delivery));
        Equal(DirtyChunkAppliedProofStatus.Applied,
            service.TryAcknowledgeDelivery(participant, delivery.DeliveryId, true));
    }

    private static void ApplyAtAvailableStep(DirtyChunkSpatialFanout service,
        ParticipantId participant, SpatialChunkKey key, ulong step)
    {
        for (ulong attempt = step; attempt < step + 8; attempt++)
        {
            DirtyChunkBuildStatus status = service.TryBuildDelivery(
                participant, key, attempt, out DirtyChunkDelivery delivery);
            if (status == DirtyChunkBuildStatus.NotDue) continue;
            Equal(DirtyChunkBuildStatus.Success, status);
            Equal(DirtyChunkAppliedProofStatus.Applied,
                service.TryAcknowledgeDelivery(participant, delivery.DeliveryId, true));
            return;
        }
        throw new InvalidOperationException("Chunk cadence did not become eligible.");
    }

    private static void DrainClient(ProtocolLoopback loopback, RemoteChunkWorldStateClient client)
    {
        while (loopback.Client.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            True(client.TryProcess(message, payload));
            True(loopback.Client.TryConsumeReceived());
        }
    }

    private static void DrainServer(
        ProtocolLoopback loopback,
        RemoteDirtyChunkSpatialFanoutServer server)
    {
        while (loopback.Server.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            True(server.TryProcess(message, payload));
            True(loopback.Server.TryConsumeReceived());
        }
    }

    private static DirtyChunkSpatialFanout Create(
        Fixture fixture,
        int maximumRecipients = 4,
        int maximumDirtyChunks = 64,
        int maximumRecipientChunkRows = 4_096,
        int maximumOutstandingDeliveries = 64,
        ulong maximumDeliveryAgeSteps = 1_024,
        OfflineDirectChunkCadence cadence = default,
        ChunkInterestProfile interest = default,
        int spatialRegionEdgeMillimeters = 128_000) =>
        new(fixture.Store,
            new DirtyChunkSpatialFanoutLimits(
                maximumRecipients,
                maximumRecipientChunkRows,
                512,
                maximumDirtyChunks,
                maximumOutstandingDeliveries,
                16,
                16,
                spatialRegionEdgeMillimeters,
                maximumDeliveryAgeSteps),
            interest == default ? GameChunkInterestProfile.Default : interest,
            cadence == default ? new OfflineDirectChunkCadence(1, 1, 1) : cadence);

    private static SpatialChunkKey Key(int x, int z) =>
        new(GameWorldState.DefaultSpace, x, z);

    private static WorldPosition Position(int chunkX, int chunkZ, int localX, int localZ) =>
        new(chunkX, chunkZ, checked((ushort)localX), checked((ushort)localZ), 0);

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly Entity[] _entities;

        internal Fixture(int historyCapacity, int entityCapacity)
        {
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
            _entities = new Entity[entityCapacity];
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(new EntityCapacity(entityCapacity), 2, 4,
                    lifecycleRecordCapacity: Math.Max(32, entityCapacity)));
            ArchetypeHandle empty = _world.RegisterArchetype(
                ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
            _world.Seal();
            _world.Initialize();
            for (int index = 0; index < entityCapacity; index++)
                True(_world.TryCreate(empty, out _entities[index]));
            Store = new ChunkPublicationStore(
                new PublicationEpoch(0xD100 + (ulong)historyCapacity),
                new ChunkPublicationLimits(entityCapacity, entityCapacity * 2, 2, historyCapacity));
        }

        internal ChunkPublicationStore Store { get; }

        internal BoundWorldPack Bound { get; }

        internal Entity Entity(int index) => _entities[index];

        internal void Present(int index, in WorldPosition position)
        {
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible);
            True(Store.TryCommit(
                [WorldPublicChange.Present(_entities[index], position, descriptor)], out _));
        }

        internal void PresentBatch(ReadOnlySpan<WorldPosition> positions)
        {
            WorldPublicChange[] changes = new WorldPublicChange[positions.Length];
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible);
            for (int index = 0; index < positions.Length; index++)
                changes[index] = WorldPublicChange.Present(_entities[index], positions[index], descriptor);
            True(Store.TryCommit(changes, out _));
        }

        public void Dispose() => _world.Dispose();
    }
}
