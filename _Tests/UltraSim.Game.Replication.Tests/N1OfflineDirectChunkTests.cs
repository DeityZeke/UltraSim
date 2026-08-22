using System.Buffers.Binary;
using System.Runtime.InteropServices;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication.Tests;

internal static class N1OfflineDirectChunkTests
{
    private static readonly ChunkInterestProfile Interest = new(
        1_000, 1_500,
        15_000, 15_500,
        23_000, 23_500);

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

        Equal((ushort)10, UltraSimGameProtocol.Manifest.Version);
        Equal(16_305_363_788_555_709_530UL, UltraSimGameProtocol.Manifest.Fingerprint);
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
        private readonly Entity[] _entities = new Entity[4];

        internal Fixture(int historyCapacity)
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
            _world = new EcsWorld(
                GameSchema.CreateSchema(),
                new EcsWorldOptions(new EntityCapacity(16), 2, 4, lifecycleRecordCapacity: 32));
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

        internal void ReplaceStore(int historyCapacity) => Store = CreateStore(historyCapacity);

        private static ChunkPublicationStore CreateStore(int historyCapacity) => new(
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(16, 32, 2, historyCapacity));

        public void Dispose() => _world.Dispose();
    }
}
