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

    internal static void RemoteProtocolPeerConvergesThroughApplicationProof()
    {
        Check.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            default,
            out ProtocolLoopback? created,
            out _, UltraSimGameProtocol.Manifest));
        using ProtocolLoopback loopback = created!;
        DirtyEntitySpatialFanout fanout = Create(maximumEntities: 4, maximumRecipients: 1);
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

    private static DirtyEntitySpatialFanout Create(
        int maximumEntities,
        int maximumRecipients,
        int maximumRows = 64) => new(
        new StateEpoch(1),
        new DirtyEntitySpatialFanoutLimits(
            maximumEntities, maximumRecipients, 256, maximumRows, 16, 8),
        new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000, 10, 20, 5, 100));

    private static DirtyEntityLatestState Present(int index, ulong version, int x, int z) =>
        new(new ReplicationEntityId(checked((uint)index), 1), version, true, true,
            GameReplicationHorizon.Far, Point(x, z),
            new GameSimulationState { TickMarker = checked((int)version), InputVelocity = index });

    private static ReplicationSpatialPoint Point(int x, int z) => new(x, z);
}
