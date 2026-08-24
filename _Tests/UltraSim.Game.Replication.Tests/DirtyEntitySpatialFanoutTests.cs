using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Game.Replication.Tests;

internal static class DirtyEntitySpatialFanoutTests
{
    internal static void WorkShapeAndCleanSteadyState()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 64, maximumRecipients: 4);
        for (int index = 0; index < 32; index++)
        {
            Check.Equal(DirtyEntityUpdateStatus.Success,
                service.CommitEntity(Present(index, 1, index * 100_000, index * 100_000), 0));
        }
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out int initialProcessed));
        Check.Equal(32, initialProcessed);

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(new ParticipantId(1), Point(0, 0), 0));
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[8];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(new ParticipantId(1), 0, 4_096, records, out DirtyEntityBundle initial));
        Check.True(service.TryAcknowledgeBundle(new ParticipantId(1), initial.DeliveryBundleId, true));
        service.ResetAttribution(true);
        DirtyEntitySpatialFanoutSnapshot cleanBefore = service.GetSnapshot();
        Check.Equal(DirtyEntityProcessStatus.NoData,
            service.TryProcessDirtyEntities(100, out int cleanProcessed));
        Check.Equal(0, cleanProcessed);
        Check.Equal(DirtyEntityBuildStatus.NoData,
            service.TryBuildBundle(new ParticipantId(1), 100, 4_096, records, out _));

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 2, 100, 0), 100));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(100, out int dirtyProcessed));
        Check.Equal(1, dirtyProcessed);
        DirtyEntitySpatialFanoutSnapshot dirty = service.GetSnapshot();
        Check.Equal(cleanBefore.DirtyEntitiesProcessed + 1, dirty.DirtyEntitiesProcessed);
        Check.True(dirty.SpatialRecipientDiscoveryOperations >= cleanBefore.SpatialRecipientDiscoveryOperations + 1);
        Check.True(dirty.CandidateRecipientsExamined - cleanBefore.CandidateRecipientsExamined < 8);
        Check.True(dirty.AuthoritativeEntityChangesObserved >= 33);
        Console.WriteLine(
            $"P01 EVIDENCE clean_process=NoData clean_build=NoData one_dirty_processed={dirtyProcessed} " +
            $"candidate_recipients_delta={dirty.CandidateRecipientsExamined - cleanBefore.CandidateRecipientsExamined} " +
            $"semantic_rows={dirty.SemanticKnowledgeRowCount} reserved_bytes={dirty.ReservedBytes}");
    }

    internal static void ExistingAudienceAvoidsOldFootprintDiscovery()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 8, maximumRecipients: 4);
        service.SetRecipientAnchor(new ParticipantId(1), Point(0, 0), 0);
        service.SetRecipientAnchor(new ParticipantId(2), Point(100_000, 0), 0);
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out _));

        DirtyEntitySpatialFanoutSnapshot baseline = service.GetSnapshot();
        service.CommitEntity(Present(0, 2, 0, 0), 1);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(1, out _));
        DirtyEntitySpatialFanoutSnapshot sameFootprint = service.GetSnapshot();
        Check.Equal(baseline.SpatialRecipientDiscoveryOperations,
            sameFootprint.SpatialRecipientDiscoveryOperations);

        service.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(0, 1), 3, true, true, GameReplicationHorizon.Near,
            Point(0, 0), new GameSimulationState { TickMarker = 3 }), 2);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(2, out _));
        DirtyEntitySpatialFanoutSnapshot shrunk = service.GetSnapshot();
        Check.Equal(sameFootprint.SpatialRecipientDiscoveryOperations,
            shrunk.SpatialRecipientDiscoveryOperations);

        service.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(0, 1), 4, true, true, GameReplicationHorizon.Near,
            Point(100_000, 0), new GameSimulationState { TickMarker = 4 }), 3);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(3, out _));
        DirtyEntitySpatialFanoutSnapshot moved = service.GetSnapshot();
        Check.Equal(shrunk.SpatialRecipientDiscoveryOperations + 1,
            moved.SpatialRecipientDiscoveryOperations);

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.RemoveEntity(new ReplicationEntityId(0, 1), 5, 4));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(4, out _));
        DirtyEntitySpatialFanoutSnapshot despawned = service.GetSnapshot();
        Check.Equal(moved.SpatialRecipientDiscoveryOperations,
            despawned.SpatialRecipientDiscoveryOperations);
        Check.Equal(2, despawned.SemanticKnowledgeRowCount);
    }

    internal static void CoarseSpatialEdgesPreserveExactSemantics()
    {
        DirtyEntitySemanticKnowledgeSnapshot[]? expected = null;
        foreach (int edge in new[] { 16_000, 32_000, 64_000, 128_000 })
        {
            DirtyEntitySpatialFanout service = Create(
                maximumEntities: 8, maximumRecipients: 8, spatialRegionEdgeMillimeters: edge);
            int[] anchors = [0, 39_999, 40_001, -39_999, -40_001, 127_999, -127_999];
            for (int recipient = 0; recipient < anchors.Length; recipient++)
                Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(
                    new ParticipantId(checked((uint)recipient + 1)), Point(anchors[recipient], 0), 0));

            service.CommitEntity(Present(0, 1, 0, 0), 0);
            service.CommitEntity(Present(1, 1, 128_000, 0), 0);
            service.CommitEntity(Present(2, 1, -128_000, 0), 0);
            Check.Equal(DirtyEntityProcessStatus.Success,
                service.TryProcessDirtyEntities(0, out int initialized));
            Check.Equal(3, initialized);

            Check.True(service.SetRecipientAnchor(
                    new ParticipantId(3), Point(20_000, 0), 1) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
            service.CommitEntity(Present(0, 2, 1_000, 0), 1);
            Check.Equal(DirtyEntityProcessStatus.Success,
                service.TryProcessDirtyEntities(1, out int processed));
            Check.Equal(1, processed);

            var actual = new DirtyEntitySemanticKnowledgeSnapshot[64];
            int count = service.CopySemanticKnowledge(actual);
            Array.Resize(ref actual, count);
            if (expected is null)
            {
                expected = actual;
            }
            else
            {
                Check.Equal(expected.Length, actual.Length);
                for (int index = 0; index < actual.Length; index++)
                    Check.Equal(expected[index], actual[index]);
            }
        }
    }

    internal static void ChunkBatchUsesPointDiscoveryForSingleton()
    {
        var profile = new GameInterestProfile(
            16_000, 5_000, 6_000, 20_000, 32_000, 10, 20, 5, 100);
        var service = new DirtyEntityChunkBatchFanout(
            new StateEpoch(1),
            new DirtyEntitySpatialFanoutLimits(4, 2, 64, 16, 4, 4, 16_000),
            profile);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(new ParticipantId(1), Point(48_000, 0), 0));
        DirtyEntitySpatialFanoutSnapshot before = service.GetSnapshot();
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 1, 0, 0), 0));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out int processed));
        Check.Equal(1, processed);
        DirtyEntitySpatialFanoutSnapshot after = service.GetSnapshot();
        Check.Equal(before.SpatialRecipientDiscoveryOperations + 1,
            after.SpatialRecipientDiscoveryOperations);
        Check.Equal(before.CandidateRecipientsExamined, after.CandidateRecipientsExamined);
        Check.Equal(0, after.SemanticKnowledgeRowCount);
    }

    internal static void StateOnlyMutationsAvoidSpatialDiscovery()
    {
        Verify(Create(maximumEntities: 8, maximumRecipients: 2));
        Verify(CreateChunkBatch(maximumEntities: 8, maximumRecipients: 2));

        static void Verify(DirtyEntitySpatialFanout service)
        {
            service.SetRecipientAnchor(new ParticipantId(1), Point(0, 0), 0);
            service.CommitEntity(Present(0, 1, 0, 0), 0);
            service.TryProcessDirtyEntities(0, out _);
            DirtyEntitySpatialFanoutSnapshot before = service.GetSnapshot();
            for (ulong version = 2; version <= 32; version++)
            {
                service.CommitEntity(Present(0, version, 0, 0), checked((long)version));
                Check.Equal(DirtyEntityProcessStatus.Success,
                    service.TryProcessDirtyEntities(checked((long)version), out int processed));
                Check.Equal(1, processed);
            }
            DirtyEntitySpatialFanoutSnapshot after = service.GetSnapshot();
            Check.Equal(before.SpatialRecipientDiscoveryOperations,
                after.SpatialRecipientDiscoveryOperations);
            Check.Equal(before.SpatialEntityDiscoveryOperations,
                after.SpatialEntityDiscoveryOperations);
        }
    }

    internal static void ChunkBatchedDiscoveryCoalescesAndAllocatesZero()
    {
        DirtyEntityChunkBatchFanout service = CreateChunkBatch(
            maximumEntities: 64, maximumRecipients: 2, maximumRows: 128,
            maximumRecordsPerBundle: 32);
        ParticipantId first = new(1);
        ParticipantId second = new(2);
        service.SetRecipientAnchor(first, Point(0, 0), 0);
        service.SetRecipientAnchor(second, Point(20_000, 0), 0);
        for (int entity = 0; entity < 32; entity++)
            service.CommitEntity(Present(entity, 1, 1_000 + entity * 100, 1_000), 0);
        DirtyEntitySpatialFanoutSnapshot before = service.GetSnapshot();
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out int initialized));
        Check.Equal(32, initialized);
        DirtyEntitySpatialFanoutSnapshot initial = service.GetSnapshot();
        Check.Equal(before.SpatialRecipientDiscoveryOperations + 1,
            initial.SpatialRecipientDiscoveryOperations);

        DirtyEntityDeliveryRecord[] records = new DirtyEntityDeliveryRecord[32];
        Drain(first, 20);
        Drain(second, 20);
        for (int entity = 0; entity < 32; entity++)
            service.CommitEntity(Present(entity, 2, 1_100 + entity * 100, 1_000), 100);
        DirtyEntitySpatialFanoutSnapshot beforeMove = service.GetSnapshot();
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(100, out int moved));
        Check.Equal(32, moved);
        DirtyEntitySpatialFanoutSnapshot afterMove = service.GetSnapshot();
        Check.Equal(beforeMove.SpatialRecipientDiscoveryOperations + 1,
            afterMove.SpatialRecipientDiscoveryOperations);
        Drain(first, 120);
        Drain(second, 120);

        for (int warm = 0; warm < 4; warm++) RunPass(warm + 3, 200 + warm * 100);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < 64; pass++) RunPass(pass + 7, 1_000 + pass * 100);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check.Equal(0L, allocated);

        void RunPass(int version, int now)
        {
            for (int entity = 0; entity < 32; entity++)
                Check.Equal(DirtyEntityUpdateStatus.Success,
                    service.CommitEntity(Present(entity, checked((ulong)version),
                        1_000 + entity * 100 + (version & 1), 1_000), now));
            Check.Equal(DirtyEntityProcessStatus.Success,
                service.TryProcessDirtyEntities(now, out int processed));
            Check.Equal(32, processed);
            Drain(first, now + 20);
            Drain(second, now + 20);
        }

        void Drain(ParticipantId participant, int now)
        {
            while (service.TryBuildBundle(participant, now, 16_384, records,
                       out DirtyEntityBundle bundle) == DirtyEntityBuildStatus.Success)
                Check.True(service.TryAcknowledgeBundle(
                    participant, bundle.DeliveryBundleId, applied: true));
        }
    }

    internal static void ExactWireBudgetAndRecipientOutstandingIsolation()
    {
        DirtyEntitySpatialFanout service = Create(
            maximumEntities: 4, maximumRecipients: 2,
            maximumOutstandingBundles: 2, maximumRecordsPerBundle: 2);
        ParticipantId slow = new(1);
        ParticipantId healthy = new(2);
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(slow, Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(healthy, Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success, service.CommitEntity(Present(0, 1, 0, 0), 0));
        service.TryProcessDirtyEntities(0, out _);

        int exactBytes = RemoteDirtyEntityStateHeaderCodec.EncodedSize +
            DirtyEntityDeliveryRecord.MaximumEncodedBytes;
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];
        Check.Equal(DirtyEntityBuildStatus.ByteBudgetTooSmall,
            service.TryBuildBundle(slow, 0, exactBytes - 1, records, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(slow, 0, exactBytes, records, out DirtyEntityBundle retained));
        Check.Equal(exactBytes, retained.EstimatedEncodedBytes);
        Check.Equal(DirtyEntityBuildStatus.OutstandingCapacityExceeded,
            service.TryBuildBundle(slow, 0, exactBytes, records, out _));

        DirtyEntityDeliveryRecord delivered = records[0];
        Span<RemoteDirtyEntityRecord> remote = stackalloc RemoteDirtyEntityRecord[1];
        remote[0] = new RemoteDirtyEntityRecord(
            delivered.DeliveryId, delivered.Entity.Index, delivered.Entity.Generation,
            delivered.CanonicalVersion, delivered.IsPresent, (byte)delivered.Horizon,
            delivered.Position.XMillimeters, delivered.Position.ZMillimeters,
            delivered.SimulationState.TickMarker, delivered.SimulationState.InputVelocity);
        Span<byte> encoded = stackalloc byte[exactBytes];
        Check.True(UltraSimGameProtocol.TryWriteDirtyEntityBundle(
            encoded, 1, service.StateEpoch.Value, retained.DeliveryBundleId, remote, out int written));
        Check.Equal(exactBytes, written);

        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(healthy, 0, exactBytes, records, out DirtyEntityBundle progressed));
        Check.True(service.TryAcknowledgeBundle(healthy, progressed.DeliveryBundleId, true));
        Check.True(service.TryAcknowledgeBundle(slow, retained.DeliveryBundleId, true));
    }

    internal static void SameRegionEntityMutationKeepsSpatialListsAcyclic()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(participant, Point(0, 0), 0));
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];
        for (int step = 1; step <= 256; step++)
        {
            Check.Equal(DirtyEntityUpdateStatus.Success,
                service.CommitEntity(Present(0, checked((ulong)step), step % 50, 0), step));
            service.TryProcessDirtyEntities(step, out _);
            Check.True(service.SetRecipientAnchor(
                    participant, Point(step % 25, 0), step) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange);
            while (service.TryBuildBundle(participant, step, 4_096, records, out DirtyEntityBundle bundle) ==
                   DirtyEntityBuildStatus.Success)
                Check.True(service.TryAcknowledgeBundle(
                    participant, bundle.DeliveryBundleId, applied: true));
        }
        Check.Equal(1, service.GetSnapshot().AuthoritativeEntityCount);
    }

    internal static void MovementCleanTopologyRemovalAndReentry()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 8, maximumRecipients: 4);
        ParticipantId first = new(1);
        ParticipantId second = new(2);
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(first, Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(second, Point(50_000, 0), 0));
        ReplicationEntityId entity = new(0, 1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 1, 0, 0), 0));
        service.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[4];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(first, 0, 4_096, records, out DirtyEntityBundle firstPresent));
        Check.True(records[0].IsPresent && records[0].Entity == entity);
        Check.True(service.TryAcknowledgeBundle(first, firstPresent.DeliveryBundleId, true));

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 2, 50_000, 0), 1));
        service.TryProcessDirtyEntities(1, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(first, 1, 4_096, records, out DirtyEntityBundle firstRemove));
        Check.True(!records[0].IsPresent && records[0].Entity == entity);
        Check.True(service.TryAcknowledgeBundle(first, firstRemove.DeliveryBundleId, true));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(second, 1, 4_096, records, out DirtyEntityBundle secondPresent));
        Check.True(records[0].IsPresent && records[0].Entity == entity);
        Check.True(service.TryAcknowledgeBundle(second, secondPresent.DeliveryBundleId, true));

        // A clean teleport/leave/re-entry is resolved from entity cells and retained knowledge.
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(first, Point(50_000, 0), 2));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(first, 2, 4_096, records, out DirtyEntityBundle reentry));
        Check.True(records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(first, reentry.DeliveryBundleId, true));
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(first, Point(0, 0), 3));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(first, 3, 4_096, records, out DirtyEntityBundle cleanLeave));
        Check.True(!records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(first, cleanLeave.DeliveryBundleId, true));
        Check.Equal(DirtyEntityUpdateStatus.Success, service.SetRecipientAnchor(first, Point(50_000, 0), 4));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(first, 4, 4_096, records, out DirtyEntityBundle finalPresent));
        Check.True(records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(first, finalPresent.DeliveryBundleId, true));
    }

    internal static void HysteresisHorizonAndDespawn()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        ReplicationEntityId entity = new(0, 1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(participant, Point(0, 0), 0));
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 1, true, true, GameReplicationHorizon.Immediate,
                Point(4_500, 0), new GameSimulationState { TickMarker = 1 }), 0));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle first));
        Check.True(records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(participant, first.DeliveryBundleId, true));

        // 5.5m is outside Immediate enter (5m) but inside Immediate leave (6m).
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 2, true, true, GameReplicationHorizon.Immediate,
                Point(5_500, 0), new GameSimulationState { TickMarker = 2 }), 1));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(1, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 1, 4_096, records, out DirtyEntityBundle retained));
        Check.True(records[0].IsPresent && records[0].CanonicalVersion == 2);
        Check.True(service.TryAcknowledgeBundle(participant, retained.DeliveryBundleId, true));

        // Beyond leave, the same known identity must receive a removal.
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 3, true, true, GameReplicationHorizon.Immediate,
                Point(6_500, 0), new GameSimulationState { TickMarker = 3 }), 2));
        service.TryProcessDirtyEntities(2, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 2, 4_096, records, out DirtyEntityBundle removed));
        Check.True(!records[0].IsPresent && records[0].CanonicalVersion == 3);
        Check.True(service.TryAcknowledgeBundle(participant, removed.DeliveryBundleId, true));

        // A horizon expansion is a new admission at Near radius; shrinking it back
        // to Immediate preserves the existing same-generation obligation at 5.5m.
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 4, true, true, GameReplicationHorizon.Near,
                Point(5_500, 0), new GameSimulationState { TickMarker = 4 }), 3));
        service.TryProcessDirtyEntities(3, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 13, 4_096, records, out DirtyEntityBundle expanded));
        Check.True(records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(participant, expanded.DeliveryBundleId, true));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 5, true, true, GameReplicationHorizon.Immediate,
                Point(5_500, 0), new GameSimulationState { TickMarker = 5 }), 24));
        service.TryProcessDirtyEntities(24, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 34, 4_096, records, out DirtyEntityBundle shrunk));
        Check.True(records[0].IsPresent);
        Check.True(service.TryAcknowledgeBundle(participant, shrunk.DeliveryBundleId, true));

        Check.Equal(DirtyEntityUpdateStatus.Success, service.RemoveEntity(entity, 6, 35));
        service.TryProcessDirtyEntities(35, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 35, 4_096, records, out DirtyEntityBundle despawn));
        Check.True(!records[0].IsPresent && records[0].Entity == entity);
        Check.True(service.TryAcknowledgeBundle(participant, despawn.DeliveryBundleId, true));
    }

    internal static void CapacityFailureRetainsFootprint()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 2, maximumRows: 2);
        ParticipantId oldRecipient = new(1);
        ParticipantId fullRecipient = new(2);
        ReplicationEntityId moving = new(0, 1);
        ReplicationEntityId filler = new(1, 1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(oldRecipient, Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(fullRecipient, Point(50_000, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 1, 0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(1, 1, 50_000, 0), 0));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out _));
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[4];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(oldRecipient, 0, 4_096, records, out DirtyEntityBundle oldPresent));
        Check.True(service.TryAcknowledgeBundle(oldRecipient, oldPresent.DeliveryBundleId, true));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(fullRecipient, 0, 4_096, records, out DirtyEntityBundle fillerPresent));
        Check.True(service.TryAcknowledgeBundle(fullRecipient, fillerPresent.DeliveryBundleId, true));

        // B is relevant to the full recipient but its semantic row cannot fit.
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 2, 50_000, 0), 1));
        Check.Equal(DirtyEntityProcessStatus.CapacityExceeded,
            service.TryProcessDirtyEntities(1, out _));
        Check.Equal(1, service.GetSnapshot().DirtyEntityCount);

        // C supersedes B while the original A footprint must remain retained.
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 3, 100_000, 0), 2));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.RemoveEntity(filler, 2, 2));
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(2, out int processed));
        Check.Equal(2, processed);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(oldRecipient, 2, 4_096, records, out DirtyEntityBundle oldRemoval));
        Check.True(!records[0].IsPresent && records[0].Entity == moving);
        Check.True(service.TryAcknowledgeBundle(oldRecipient, oldRemoval.DeliveryBundleId, true));
        Check.Equal(0, service.GetSnapshot().DirtyEntityCount);
    }

    internal static void CoalescingCadenceDisclosureAndCapacity()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 8, maximumRecipients: 4);
        ParticipantId immediate = new(1);
        ParticipantId near = new(2);
        ParticipantId far = new(3);
        service.SetRecipientAnchor(immediate, Point(0, 0), 0);
        service.SetRecipientAnchor(near, Point(10_000, 0), 0);
        service.SetRecipientAnchor(far, Point(30_000, 0), 0);
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        service.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(1, 1), 1, true, false, GameReplicationHorizon.Far,
            Point(0, 0), new GameSimulationState { TickMarker = 99 }), 0);
        service.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[4];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(immediate, 0, 4_096, records, out DirtyEntityBundle immediateBundle));
        Check.True(service.TryAcknowledgeBundle(immediate, immediateBundle.DeliveryBundleId, true));
        Check.Equal(DirtyEntityBuildStatus.NoData,
            service.TryBuildBundle(near, 0, 4_096, records, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(near, 10, 4_096, records, out DirtyEntityBundle nearBundle));
        Check.True(service.TryAcknowledgeBundle(near, nearBundle.DeliveryBundleId, true));
        Check.Equal(DirtyEntityBuildStatus.NoData,
            service.TryBuildBundle(far, 10, 4_096, records, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(far, 20, 4_096, records, out DirtyEntityBundle farBundle));
        Check.True(service.TryAcknowledgeBundle(far, farBundle.DeliveryBundleId, true));

        Span<DirtyEntitySemanticKnowledgeSnapshot> knowledge = stackalloc DirtyEntitySemanticKnowledgeSnapshot[8];
        Check.Equal(3, service.CopySemanticKnowledge(knowledge));
        for (int index = 0; index < 3; index++)
            Check.Equal(new ReplicationEntityId(0, 1), knowledge[index].Entity);

        DirtyEntitySpatialFanout bounded = Create(maximumEntities: 4, maximumRecipients: 2, maximumRows: 1);
        bounded.SetRecipientAnchor(new ParticipantId(9), Point(0, 0), 0);
        bounded.CommitEntity(Present(0, 1, 0, 0), 0);
        bounded.CommitEntity(Present(1, 1, 100, 0), 0);
        Check.Equal(DirtyEntityProcessStatus.CapacityExceeded,
            bounded.TryProcessDirtyEntities(0, out _));
        Check.True(bounded.GetSnapshot().SemanticKnowledgeRowCount <= 1);

        DirtyEntitySpatialFanout deferred = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId deferredRecipient = new(7);
        deferred.SetRecipientAnchor(deferredRecipient, Point(0, 0), 0);
        deferred.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(0, 1), 1, true, true, GameReplicationHorizon.Near,
            Point(10_000, 0), new GameSimulationState { TickMarker = 1 }), 0);
        deferred.TryProcessDirtyEntities(0, out _);
        Check.Equal(DirtyEntityBuildStatus.NoData,
            deferred.TryBuildBundle(deferredRecipient, 0, 4_096, records, out _));
        deferred.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(0, 1), 2, true, true, GameReplicationHorizon.Near,
            Point(11_000, 0), new GameSimulationState { TickMarker = 2 }), 1);
        deferred.TryProcessDirtyEntities(1, out _);
        deferred.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(0, 1), 3, true, true, GameReplicationHorizon.Far,
            Point(12_000, 0), new GameSimulationState { TickMarker = 3 }), 9);
        deferred.TryProcessDirtyEntities(9, out _);
        Check.Equal(DirtyEntityBuildStatus.NoData,
            deferred.TryBuildBundle(deferredRecipient, 9, 4_096, records, out _));
        Check.Equal(DirtyEntityBuildStatus.Success,
            deferred.TryBuildBundle(deferredRecipient, 10, 4_096, records, out DirtyEntityBundle latest));
        Check.True(records[0].CanonicalVersion == 3);
        Check.True(deferred.TryAcknowledgeBundle(deferredRecipient, latest.DeliveryBundleId, true));
    }

    internal static void ProofTimeoutAndStaleApplied()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(participant, Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 1, 0, 0), 0));
        service.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle first));

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 2, 100, 0), 1));
        service.TryProcessDirtyEntities(1, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 1, 4_096, records, out DirtyEntityBundle newer));
        Check.True(newer.DeliveryBundleId != first.DeliveryBundleId);

        DirtyEntitySpatialFanoutSnapshot beforeStale = service.GetSnapshot();
        Check.True(!service.TryAcknowledgeBundle(participant, first.DeliveryBundleId, applied: true));
        DirtyEntitySpatialFanoutSnapshot afterStale = service.GetSnapshot();
        Check.Equal(beforeStale.ApplicationProofsApplied, afterStale.ApplicationProofsApplied);
        Check.Equal(beforeStale.StaleOrRejectedProofs + 1, afterStale.StaleOrRejectedProofs);
        Check.True(service.TryAcknowledgeBundle(participant, newer.DeliveryBundleId, applied: true));

        Check.Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(Present(0, 3, 200, 0), 2));
        service.TryProcessDirtyEntities(2, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 2, 4_096, records, out DirtyEntityBundle timedOut));
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 103, 4_096, records, out DirtyEntityBundle retry));
        Check.True(retry.DeliveryBundleId != timedOut.DeliveryBundleId);
        Check.True(service.GetSnapshot().TimedOutDeliveries >= 1);
        Check.True(service.TryAcknowledgeBundle(participant, retry.DeliveryBundleId, applied: true));
    }

    internal static void StaleBundleRearmsSiblingDebt()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        ReplicationEntityId firstEntity = new(0, 1);
        ReplicationEntityId siblingEntity = new(1, 1);
        service.SetRecipientAnchor(participant, Point(0, 0), 0);
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        service.CommitEntity(Present(1, 1, 0, 0), 0);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out _));
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[4];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle first));
        Check.Equal(2, first.RecordCount);

        // Supersede only A while B remains the exact sent obligation in the old bundle.
        service.CommitEntity(Present(0, 2, 100, 0), 1);
        Check.Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(1, out _));
        Check.True(!service.TryAcknowledgeBundle(participant, first.DeliveryBundleId, applied: true));

        Span<DirtyEntitySemanticKnowledgeSnapshot> knowledge = stackalloc DirtyEntitySemanticKnowledgeSnapshot[4];
        Check.Equal(2, service.CopySemanticKnowledge(knowledge));
        bool firstRearmed = false;
        bool siblingRearmed = false;
        for (int index = 0; index < knowledge.Length && knowledge[index].Entity.IsValid; index++)
        {
            if (knowledge[index].Entity == firstEntity)
                firstRearmed = knowledge[index].PendingCanonicalVersion == 2 && !knowledge[index].PendingWasSent;
            if (knowledge[index].Entity == siblingEntity)
                siblingRearmed = knowledge[index].PendingCanonicalVersion == 1 && !knowledge[index].PendingWasSent;
        }
        Check.True(firstRearmed && siblingRearmed);

        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 1, 4_096, records, out DirtyEntityBundle retry));
        bool sawNewestFirst = false;
        bool sawRearmedSibling = false;
        for (int index = 0; index < retry.RecordCount; index++)
        {
            sawNewestFirst |= records[index].Entity == firstEntity && records[index].CanonicalVersion == 2;
            sawRearmedSibling |= records[index].Entity == siblingEntity && records[index].CanonicalVersion == 1;
        }
        Check.True(sawNewestFirst && sawRearmedSibling);
        Check.True(service.TryAcknowledgeBundle(participant, retry.DeliveryBundleId, applied: true));
        Check.Equal(1UL, service.GetSnapshot().ApplicationProofsApplied);
        Check.Equal(2, service.CopySemanticKnowledge(knowledge));
        for (int index = 0; index < knowledge.Length && knowledge[index].Entity.IsValid; index++)
        {
            if (knowledge[index].Entity == firstEntity)
                Check.Equal((ulong)2, knowledge[index].KnownCanonicalVersion);
            if (knowledge[index].Entity == siblingEntity)
                Check.Equal((ulong)1, knowledge[index].KnownCanonicalVersion);
        }
    }

    internal static void StatusAndRemoteEpochRecovery()
    {
        DirtyEntitySpatialFanout bounded = Create(maximumEntities: 4, maximumRecipients: 1, maximumRows: 1);
        Check.Equal(DirtyEntityUpdateStatus.Success,
            bounded.SetRecipientAnchor(new ParticipantId(1), Point(0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            bounded.CommitEntity(Present(0, 1, 0, 0), 0));
        Check.Equal(DirtyEntityUpdateStatus.Success,
            bounded.CommitEntity(Present(1, 1, 100, 0), 0));
        Check.Equal(DirtyEntityProcessStatus.CapacityExceeded,
            bounded.ReconcileAllRecipients(0));
        Check.Equal(DirtyEntityProcessStatus.CapacityExceeded,
            bounded.TryProcessDirtyEntities(0, out _));

        Check.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        DirtyEntitySpatialFanout serverBounded = Create(
            maximumEntities: 4, maximumRecipients: 1, maximumRows: 1);
        serverBounded.SetRecipientAnchor(new ParticipantId(1), Point(0, 0), 0);
        serverBounded.CommitEntity(Present(0, 1, 0, 0), 0);
        serverBounded.CommitEntity(Present(1, 1, 100, 0), 0);
        var boundedServer = new RemoteDirtyEntitySpatialFanoutServer(
            loopback.Server, serverBounded, new ParticipantId(1), 77, 4);
        Check.Equal(DirtyEntityBuildStatus.SpatialCapacityExceeded,
            boundedServer.TrySchedule(0, 4_096));
        Check.Equal(DirtyEntityProcessStatus.CapacityExceeded,
            boundedServer.GetSnapshot().LastProcessStatus);

        DirtyEntitySpatialFanout fanout = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        fanout.SetRecipientAnchor(participant, Point(0, 0), 0);
        fanout.CommitEntity(Present(0, 1, 0, 0), 0);
        var server = new RemoteDirtyEntitySpatialFanoutServer(
            loopback.Server, fanout, participant, 77, 4);
        var client = new RemoteDirtyEntitySpatialFanoutClient(
            loopback.Client, 77, new StateEpoch(1), 8, 8, 4, 8);

        Check.Equal(DirtyEntityBuildStatus.Success,
            server.TrySchedule(0, 4_096));
        loopback.Pump();
        DeliverDirty(loopback, client, server);
        Check.Equal(1UL, fanout.GetSnapshot().ApplicationProofsApplied);
        Check.True(fanout.ReplaceSourceEpoch(new StateEpoch(2)));
        Check.True(client.ReplaceStateEpoch(new StateEpoch(2)));
        fanout.CommitEntity(Present(0, 1, 100, 0), 0);
        Check.Equal(DirtyEntityBuildStatus.Success,
            server.TrySchedule(0, 4_096));
        loopback.Pump();
        DeliverDirty(loopback, client, server);
        Check.Equal(new StateEpoch(2), client.GetSnapshot().StateEpoch);
        Check.Equal(2UL, fanout.GetSnapshot().ApplicationProofsApplied);
    }

    internal static void FamiliesCoexistAndPrivateDoesNotLeak()
    {
        DirtyEntitySpatialFanout fanout = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        fanout.SetRecipientAnchor(participant, Point(0, 0), 0);
        fanout.CommitEntity(Present(0, 1, 0, 0), 0);
        fanout.CommitEntity(new DirtyEntityLatestState(
            new ReplicationEntityId(1, 1), 1, true, false, GameReplicationHorizon.Far,
            Point(0, 0), new GameSimulationState { TickMarker = 99 }), 0);
        fanout.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntitySemanticKnowledgeSnapshot> knowledge = stackalloc DirtyEntitySemanticKnowledgeSnapshot[4];
        Check.Equal(1, fanout.CopySemanticKnowledge(knowledge));
        Check.Equal(new ReplicationEntityId(0, 1), knowledge[0].Entity);

        Check.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        var server = new RemoteDirtyEntitySpatialFanoutServer(
            loopback.Server, fanout, participant, 77, 4);
        Span<byte> controlPayload = stackalloc byte[RemoteClockAnchorMessageCodec.EncodedSize];
        RemoteClockAnchorMessage control = new()
        {
            ApplicationSessionEpoch = 77, StateEpoch = 1, ClockRevision = 1,
            EffectiveFromSimulationTick = 0, SimulationFrequencyHz = 60,
            WorldTimeMicroseconds = 0, WorldTimeFractionQuanta = 0,
        };
        Check.True(RemoteClockAnchorMessageCodec.TryWriteExact(controlPayload, in control));
        Check.Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(RemoteClockAnchorMessageCodec.Descriptor.Id, controlPayload));
        Span<byte> eventPayload = stackalloc byte[RemoteApplicationEventMessageCodec.EncodedSize];
        RemoteApplicationEventMessage applicationEvent = new()
        {
            ApplicationSessionEpoch = 77, StateEpoch = 1, Sequence = 1,
            ParticipantId = participant.Value, Kind = 1, Subject = 2, PayloadHash = 3,
        };
        Check.True(RemoteApplicationEventMessageCodec.TryWriteExact(eventPayload, in applicationEvent));
        Check.Equal(NetworkQueueStatus.Accepted,
            loopback.Client.TryQueue(RemoteApplicationEventMessageCodec.Descriptor.Id, eventPayload));
        loopback.Pump();
        loopback.Pump();
        int controlMessages = 0;
        int eventMessages = 0;
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message,
                   out ReadOnlySpan<byte> payload))
        {
            if (message.MessageId == RemoteClockAnchorMessageCodec.Descriptor.Id) controlMessages++;
            if (message.MessageId == RemoteApplicationEventMessageCodec.Descriptor.Id) eventMessages++;
            Check.True(!server.TryProcess(message, payload));
            Check.True(loopback.Server.TryConsumeReceived());
        }
        Check.Equal(1, controlMessages);
        Check.Equal(1, eventMessages);
    }

    internal static void EpochAndProofFencing()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        service.SetRecipientAnchor(participant, Point(0, 0), 0);
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        service.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle oldBundle));
        Check.True(service.ReplaceSourceEpoch(new StateEpoch(2)));
        Check.True(!service.TryAcknowledgeBundle(participant, oldBundle.DeliveryBundleId, true));
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        service.TryProcessDirtyEntities(0, out _);
        Check.Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle newBundle));
        Check.True(newBundle.DeliveryBundleId != oldBundle.DeliveryBundleId);
        Check.True(service.TryAcknowledgeBundle(participant, newBundle.DeliveryBundleId, true));
        Check.True(!service.TryAcknowledgeBundle(participant, newBundle.DeliveryBundleId, true));
        Check.True(service.GetSnapshot().StaleOrRejectedProofs + service.GetSnapshot().DuplicateProofs >= 2);
    }

    internal static void WarmedPathAllocatesZero()
    {
        DirtyEntitySpatialFanout service = Create(maximumEntities: 4, maximumRecipients: 1);
        ParticipantId participant = new(1);
        service.SetRecipientAnchor(participant, Point(0, 0), 0);
        service.CommitEntity(Present(0, 1, 0, 0), 0);
        service.TryProcessDirtyEntities(0, out _);
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[2];
        service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle first);
        service.TryAcknowledgeBundle(participant, first.DeliveryBundleId, true);
        service.CommitEntity(Present(0, 2, 0, 0), 1);
        service.TryProcessDirtyEntities(1, out _);
        service.TryBuildBundle(participant, 1, 4_096, records, out DirtyEntityBundle second);
        service.TryAcknowledgeBundle(participant, second.DeliveryBundleId, true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        service.CommitEntity(Present(0, 3, 100, 0), 2);
        service.TryProcessDirtyEntities(2, out _);
        service.TryBuildBundle(participant, 2, 4_096, records, out DirtyEntityBundle third);
        service.TryAcknowledgeBundle(participant, third.DeliveryBundleId, true);
        Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Check.True(service.GetSnapshot().ReservedBytes > 0);
    }

    internal static void RemoteProtocolPeerConvergesThroughApplicationProof() =>
        AssertRemoteProtocolPeerConverges(
            Create(maximumEntities: 4, maximumRecipients: 1));

    internal static void ChunkBatchRemoteProtocolPeerConvergesThroughApplicationProof() =>
        AssertRemoteProtocolPeerConverges(
            CreateChunkBatch(maximumEntities: 4, maximumRecipients: 1));

    private static void AssertRemoteProtocolPeerConverges(DirtyEntitySpatialFanout fanout)
    {
        Check.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        ParticipantId participant = new(1);
        fanout.SetRecipientAnchor(participant, Point(0, 0), 0);
        fanout.CommitEntity(Present(0, 1, 0, 0), 0);
        var server = new RemoteDirtyEntitySpatialFanoutServer(
            loopback.Server, fanout, participant, 77, 4);
        var client = new RemoteDirtyEntitySpatialFanoutClient(
            loopback.Client, 77, new StateEpoch(1), 8, 8, 4, 8);
        for (int step = 0; step < 4_096 && fanout.GetSnapshot().ApplicationProofsApplied == 0; step++)
        {
            server.TrySchedule(step, 4_096);
            loopback.Pump();
            while (loopback.Client.TryPeekReceived(out NetworkDeliveredMessage message,
                       out ReadOnlySpan<byte> payload))
            {
                client.TryProcess(message, payload);
                Check.True(loopback.Client.TryConsumeReceived());
            }
            client.TryFlushProofs();
            loopback.Pump();
            while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message,
                       out ReadOnlySpan<byte> payload))
            {
                server.TryProcess(message, payload);
                Check.True(loopback.Server.TryConsumeReceived());
            }
        }
        Check.Equal(1UL, fanout.GetSnapshot().ApplicationProofsApplied);
        Span<RemoteWorldEntitySnapshot> active = stackalloc RemoteWorldEntitySnapshot[2];
        Check.Equal(1, client.CopyActiveEntities(active));
        Check.Equal(new ReplicationEntityId(0, 1), active[0].Entity);
    }

    private static void DeliverDirty(
        ProtocolLoopback loopback,
        RemoteDirtyEntitySpatialFanoutClient client,
        RemoteDirtyEntitySpatialFanoutServer server)
    {
        loopback.Pump();
        while (loopback.Client.TryPeekReceived(out NetworkDeliveredMessage message,
                   out ReadOnlySpan<byte> payload))
        {
            Check.True(client.TryProcess(message, payload));
            Check.True(loopback.Client.TryConsumeReceived());
        }
        Check.True(client.TryFlushProofs());
        loopback.Pump();
        loopback.Pump();
        while (loopback.Server.TryPeekReceived(out NetworkDeliveredMessage message,
                   out ReadOnlySpan<byte> payload))
        {
            Check.True(server.TryProcess(message, payload));
            Check.True(loopback.Server.TryConsumeReceived());
        }
    }

    private static DirtyEntitySpatialFanout Create(
        int maximumEntities,
        int maximumRecipients,
        int maximumRows = 64,
        int maximumOutstandingBundles = 16,
        int maximumRecordsPerBundle = 8,
        int spatialRegionEdgeMillimeters = 128_000) => new(
        new StateEpoch(1),
        new DirtyEntitySpatialFanoutLimits(
            maximumEntities, maximumRecipients, 256, maximumRows,
            maximumOutstandingBundles, maximumRecordsPerBundle,
            spatialRegionEdgeMillimeters),
        new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000, 10, 20, 5, 100));

    private static DirtyEntityChunkBatchFanout CreateChunkBatch(
        int maximumEntities,
        int maximumRecipients,
        int maximumRows = 64,
        int maximumOutstandingBundles = 16,
        int maximumRecordsPerBundle = 8) => new(
        new StateEpoch(1),
        new DirtyEntitySpatialFanoutLimits(
            maximumEntities, maximumRecipients, 256, maximumRows,
            maximumOutstandingBundles, maximumRecordsPerBundle),
        new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000, 10, 20, 5, 100));

    private static DirtyEntityLatestState Present(int index, ulong version, int x, int z) =>
        new(new ReplicationEntityId(checked((uint)index), 1), version, true, true,
            GameReplicationHorizon.Far, Point(x, z),
            new GameSimulationState { TickMarker = checked((int)version), InputVelocity = index });

    private static ReplicationSpatialPoint Point(int x, int z) => new(x, z);
}
