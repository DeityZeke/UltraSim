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
    internal static void ExplicitProfilesMeetSharedSemanticContract()
    {
        Equal(2, GamePublicReplicationProfiles.Count);
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
        Equal(GamePublicReplicationInterestModel.RecipientChunkSubscription, chunk.InterestModel);
        Equal(GamePublicReplicationSourceModel.ChunkRevisionHistory, chunk.SourceModel);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, maintained.Guarantees);
        Equal(GamePublicReplicationProfiles.RequiredGuarantees, chunk.Guarantees);

        Equal(maintained, GameInterestService.PublicReplicationProfile);
        Equal(maintained, RemoteWorldStateServer.PublicReplicationProfile);
        Equal(chunk, OfflineDirectChunkRecipient.PublicReplicationProfile);
        Equal(chunk, ChunkPublicationWorkScheduler.PublicReplicationProfile);
        Equal(chunk, RemoteChunkWorldStateServer.PublicReplicationProfile);

        AssertObservation(CaptureMaintainedEntityRelation());
        AssertObservation(N1OfflineDirectChunkTests.CaptureProfileConformance());
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

    private static GameProjectedEntity Projected(ReplicationEntityId id, int marker) => new(
        id,
        HasSimulationState: true,
        new GameSimulationState { TickMarker = marker, InputVelocity = marker * 2 });

    private static void AssertObservation(in ReplicationProfileConformanceObservation observation)
    {
        True(observation.Profile is GamePublicReplicationProfileId.MaintainedEntityRelation or
            GamePublicReplicationProfileId.ChunkPublication);
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
