using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Network;

namespace UltraSim.Game.Replication.Tests;

internal readonly record struct ReplicationProfileConformanceObservation(
    GamePublicReplicationProfileId Profile,
    bool AuthoritativeSourceTruth,
    bool RecipientDisclosure,
    bool StableIdentityAndLifetime,
    bool SentDoesNotAdvanceKnowledge,
    bool ImpairmentRecovery,
    bool EventualSemanticConvergence,
    long ReservedBytes);

internal static class ReplicationProfileConformanceTests
{
    internal static void ConservativeSelectionKeepsDynamicProfilesSeparateFromStaticLayers()
    {
        False(GamePublicReplicationProfiles.RuntimeOwnershipTransitionsSupported);
        Equal(GamePublicReplicationProfileId.ChunkPublication,
            GamePublicReplicationProfiles.Selected.Id);
        Equal(GamePublicReplicationProfileDisposition.SelectedCurrentGame,
            GamePublicReplicationProfiles.Selected.Disposition);

        int selectedCount = 0;
        int retainedAlternativeCount = 0;
        int experimentalReferenceCount = 0;
        foreach (GamePublicReplicationProfileId id in Enum.GetValues<GamePublicReplicationProfileId>())
        {
            True(GamePublicReplicationProfiles.TryGet(id, out GamePublicReplicationProfileDescriptor profile));
            Equal(GamePublicReplicationDomain.DynamicPublicState, profile.Domain);
            switch (profile.Disposition)
            {
                case GamePublicReplicationProfileDisposition.SelectedCurrentGame:
                    selectedCount++;
                    Equal(GamePublicReplicationProfileId.ChunkPublication, profile.Id);
                    break;
                case GamePublicReplicationProfileDisposition.RetainedAlternative:
                    retainedAlternativeCount++;
                    True(profile.Id is GamePublicReplicationProfileId.MaintainedEntityRelation or
                        GamePublicReplicationProfileId.DirtyEntitySpatialFanout);
                    break;
                case GamePublicReplicationProfileDisposition.ExperimentalReference:
                    experimentalReferenceCount++;
                    True(profile.Id is GamePublicReplicationProfileId.DirtyChunkSpatialFanout or
                        GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown profile disposition {profile.Disposition}.");
            }
        }

        Equal(1, selectedCount);
        Equal(2, retainedAlternativeCount);
        Equal(2, experimentalReferenceCount);
    }

    internal static void ExplicitProfilesMeetSharedSemanticContract()
    {
        Equal(5, GamePublicReplicationProfiles.Count);
        Equal(GamePublicReplicationProfiles.Count,
            Enum.GetValues<GamePublicReplicationProfileId>().Length);
        Equal(GamePublicReplicationProfileId.ChunkPublication,
            GamePublicReplicationProfiles.Selected.Id);
        True(GamePublicReplicationProfiles.TryGet(
            GamePublicReplicationProfileId.MaintainedEntityRelation,
            out GamePublicReplicationProfileDescriptor maintained));
        True(GamePublicReplicationProfiles.TryGet(
            GamePublicReplicationProfileId.ChunkPublication,
            out GamePublicReplicationProfileDescriptor chunk));
        False(GamePublicReplicationProfiles.TryGet(
            (GamePublicReplicationProfileId)byte.MaxValue, out _));

        Equal(GamePublicReplicationInterestModel.RecipientEntityRelation, maintained.InterestModel);
        Equal(GamePublicReplicationSourceModel.EntityLatestState, maintained.SourceModel);
        Equal(GamePublicReplicationDomain.DynamicPublicState, maintained.Domain);
        Equal(GamePublicReplicationInterestModel.RecipientChunkSubscription, chunk.InterestModel);
        Equal(GamePublicReplicationSourceModel.ChunkRevisionHistory, chunk.SourceModel);
        Equal(GamePublicReplicationDomain.DynamicPublicState, chunk.Domain);
        True(GamePublicReplicationProfiles.TryGet(
            GamePublicReplicationProfileId.DirtyEntitySpatialFanout,
            out GamePublicReplicationProfileDescriptor dirty));
        Equal(GamePublicReplicationInterestModel.DirtyEntitySpatialDiscovery, dirty.InterestModel);
        Equal(GamePublicReplicationSourceModel.EntityLatestState, dirty.SourceModel);
        True(GamePublicReplicationProfiles.TryGet(
            GamePublicReplicationProfileId.DirtyChunkSpatialFanout,
            out GamePublicReplicationProfileDescriptor dirtyChunk));
        Equal(GamePublicReplicationInterestModel.DirtyChunkSpatialDiscovery,
            dirtyChunk.InterestModel);
        Equal(GamePublicReplicationSourceModel.ChunkRevisionHistory, dirtyChunk.SourceModel);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, maintained.Guarantees);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, chunk.Guarantees);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, dirty.Guarantees);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, dirtyChunk.Guarantees);
        True(GamePublicReplicationProfiles.TryGet(
            GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout,
            out GamePublicReplicationProfileDescriptor dirtyEntityBatch));
        Equal(GamePublicReplicationInterestModel.DirtyEntityChunkBatchDiscovery,
            dirtyEntityBatch.InterestModel);
        Equal(GamePublicReplicationSourceModel.EntityLatestState, dirtyEntityBatch.SourceModel);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, dirtyEntityBatch.Guarantees);

        Equal(maintained, GameInterestService.PublicReplicationProfile);
        Equal(maintained, RemoteWorldStateServer.PublicReplicationProfile);
        Equal(chunk, OfflineDirectChunkRecipient.PublicReplicationProfile);
        Equal(chunk, ChunkPublicationWorkScheduler.PublicReplicationProfile);
        Equal(chunk, RemoteChunkWorldStateServer.PublicReplicationProfile);
        Equal(dirty, DirtyEntitySpatialFanout.PublicReplicationProfile);
        Equal(dirtyChunk, DirtyChunkSpatialFanout.PublicReplicationProfile);
        Equal(dirtyEntityBatch, DirtyEntityChunkBatchFanout.PublicReplicationProfile);

        AssertObservation(CaptureMaintainedEntityRelation());
        AssertObservation(N1OfflineDirectChunkTests.CaptureProfileConformance());
        AssertObservation(CaptureDirtyEntitySpatialFanout());
        AssertObservation(DirtyChunkSpatialFanoutTests.CaptureProfileConformance());
        AssertObservation(CaptureDirtyEntityChunkBatchFanout());
    }

    private static ReplicationProfileConformanceObservation CaptureMaintainedEntityRelation()
    {
        var service = new GameInterestService(
            new StateEpoch(0xC001),
            new GameInterestLimits(
                MaximumEntities: 4,
                MaximumRecipients: 1,
                MaximumRegions: 128,
                MaximumRegionInterestEdges: 128,
                MaximumRecipientEntityRelations: 4,
                MaximumOutstandingBundles: 4,
                MaximumRecordsPerBundle: 4),
            new GameInterestProfile(
                RegionEdgeMillimeters: 10_000,
                ImmediateEnterRadiusMillimeters: 5_000,
                ImmediateLeaveRadiusMillimeters: 6_000,
                NearRadiusMillimeters: 20_000,
                FarRadiusMillimeters: 40_000,
                NearIntervalMilliseconds: 10,
                FarIntervalMilliseconds: 20,
                ApplicationAckRetryMilliseconds: 5,
                ApplicationAckTimeoutMilliseconds: 100));
        var participant = new ParticipantId(0xC001);
        Equal(GameInterestUpdateStatus.Success,
            service.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));

        var includedId = new ReplicationEntityId(0, 1);
        var excludedId = new ReplicationEntityId(1, 1);
        Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(includedId, 10), 1,
                new ReplicationSpatialPoint(0, 0), GameReplicationHorizon.Far, 0));
        Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(excludedId, 20), 1,
                new ReplicationSpatialPoint(100_000, 100_000), GameReplicationHorizon.Far, 0));
        Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(includedId, 30), 2,
                new ReplicationSpatialPoint(100, 0), GameReplicationHorizon.Far, 0));

        Span<GameRecipientEntityRelationSnapshot> relations =
            stackalloc GameRecipientEntityRelationSnapshot[4];
        int relationCount = service.CopyRelations(relations);
        bool disclosure = relationCount == 1 && relations[0].Entity == includedId;
        bool knownBeforeSend = relations[0].KnownCanonicalVersion != 0;

        Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
        Equal(GameWorldStateBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out GameWorldStateBundle first));
        bool currentTruth = first.RecordCount == 1 && records[0].Entity == includedId &&
            records[0].CanonicalVersion == 2 && records[0].SimulationState.TickMarker == 30;
        ReplicationEntityId firstIdentity = records[0].Entity;

        relationCount = service.CopyRelations(relations);
        bool sentDidNotAdvance = relationCount == 1 && !knownBeforeSend &&
            relations[0].KnownCanonicalVersion == 0 && relations[0].PendingSequenceWasSent;
        True(service.TryAcknowledgeBundle(participant, first.BundleId, applied: false));
        relationCount = service.CopyRelations(relations);
        bool rejectionPreservedDebt = relationCount == 1 && relations[0].KnownCanonicalVersion == 0;

        Equal(GameWorldStateBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out GameWorldStateBundle retry));
        bool stableIdentity = retry.RecordCount == 1 && records[0].Entity == firstIdentity;
        True(service.TryAcknowledgeBundle(participant, retry.BundleId, applied: true));
        relationCount = service.CopyRelations(relations);
        bool converged = relationCount == 1 && relations[0].KnownCanonicalVersion == 2 &&
            relations[0].DesiredPresent;

        return new ReplicationProfileConformanceObservation(
            GamePublicReplicationProfileId.MaintainedEntityRelation,
            currentTruth,
            disclosure,
            stableIdentity,
            sentDidNotAdvance,
            rejectionPreservedDebt,
            converged,
            service.GetSnapshot().ReservedBytes);
    }

    private static ReplicationProfileConformanceObservation CaptureDirtyEntitySpatialFanout() =>
        CaptureDirtyEntityFanout(
            new DirtyEntitySpatialFanout(
                new StateEpoch(0xD001),
                new DirtyEntitySpatialFanoutLimits(8, 4, 128, 32, 8, 4),
                new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000, 10, 20, 5, 100)),
            GamePublicReplicationProfileId.DirtyEntitySpatialFanout);

    private static ReplicationProfileConformanceObservation CaptureDirtyEntityChunkBatchFanout() =>
        CaptureDirtyEntityFanout(
            new DirtyEntityChunkBatchFanout(
                new StateEpoch(0xD002),
                new DirtyEntitySpatialFanoutLimits(8, 4, 128, 32, 8, 4),
                new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000, 10, 20, 5, 100)),
            GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout);

    private static ReplicationProfileConformanceObservation CaptureDirtyEntityFanout(
        DirtyEntitySpatialFanout service,
        GamePublicReplicationProfileId profile)
    {
        ParticipantId participant = new(0xD001);
        Equal(DirtyEntityUpdateStatus.Success,
            service.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));

        ReplicationEntityId entity = new(0, 1);
        Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 1, true, true, GameReplicationHorizon.Far,
                new ReplicationSpatialPoint(0, 0),
                new GameSimulationState { TickMarker = 10, InputVelocity = 20 }), 0));
        Equal(DirtyEntityProcessStatus.Success,
            service.TryProcessDirtyEntities(0, out int processed));
        Equal(1, processed);

        Span<DirtyEntitySemanticKnowledgeSnapshot> rows = stackalloc DirtyEntitySemanticKnowledgeSnapshot[4];
        int rowCount = service.CopySemanticKnowledge(rows);
        bool disclosure = rowCount == 1 && rows[0].Entity == entity && rows[0].DesiredPresent;
        Span<DirtyEntityDeliveryRecord> records = stackalloc DirtyEntityDeliveryRecord[4];
        Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle first));
        bool currentTruth = first.RecordCount == 1 && records[0].Entity == entity &&
            records[0].CanonicalVersion == 1 && records[0].SimulationState.TickMarker == 10;
        bool sentDidNotAdvance = service.CopySemanticKnowledge(rows) == 1 &&
            rows[0].KnownCanonicalVersion == 0 && rows[0].PendingWasSent;

        Equal(DirtyEntityUpdateStatus.Success,
            service.CommitEntity(new DirtyEntityLatestState(
                entity, 2, true, true, GameReplicationHorizon.Far,
                new ReplicationSpatialPoint(100, 0),
                new GameSimulationState { TickMarker = 20, InputVelocity = 40 }), 0));
        service.TryProcessDirtyEntities(0, out _);
        False(service.TryAcknowledgeBundle(participant, first.DeliveryBundleId, applied: false));
        Equal(DirtyEntityBuildStatus.Success,
            service.TryBuildBundle(participant, 0, 4_096, records, out DirtyEntityBundle retry));
        bool superseded = retry.DeliveryBundleId != first.DeliveryBundleId &&
            records[0].CanonicalVersion == 2;
        True(service.TryAcknowledgeBundle(participant, retry.DeliveryBundleId, applied: true));
        rowCount = service.CopySemanticKnowledge(rows);
        bool converged = rowCount == 1 && rows[0].KnownCanonicalVersion == 2 && rows[0].KnownPresent;

        return new ReplicationProfileConformanceObservation(
            profile,
            currentTruth, disclosure, records[0].Entity == entity, sentDidNotAdvance,
            superseded, converged, service.GetSnapshot().ReservedBytes);
    }

    private static GameProjectedEntity Projected(ReplicationEntityId id, int marker) => new(
        id,
        HasSimulationState: true,
        new GameSimulationState { TickMarker = marker, InputVelocity = marker * 2 });

    private static void AssertObservation(in ReplicationProfileConformanceObservation observation)
    {
        True(observation.Profile is GamePublicReplicationProfileId.MaintainedEntityRelation or
            GamePublicReplicationProfileId.ChunkPublication or
            GamePublicReplicationProfileId.DirtyEntitySpatialFanout or
            GamePublicReplicationProfileId.DirtyChunkSpatialFanout or
            GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout);
        True(observation.AuthoritativeSourceTruth);
        True(observation.RecipientDisclosure);
        True(observation.StableIdentityAndLifetime);
        True(observation.SentDoesNotAdvanceKnowledge);
        True(observation.ImpairmentRecovery);
        True(observation.EventualSemanticConvergence);
        True(observation.ReservedBytes > 0);
    }

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
}
