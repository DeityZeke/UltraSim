using System.Reflection;
using System.Runtime.CompilerServices;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.Replication.Tests;
using UltraSim.Network;
using UltraSim.Server.Runtime;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("ReplicationIdentityIsGameOwnedGenerationalAndFixedWidth", ReplicationIdentityIsGameOwnedGenerationalAndFixedWidth),
    ("GameSemanticTraceIsBoundedDeterministicAndAllocationFree", GameSemanticTraceIsBoundedDeterministicAndAllocationFree),
    ("PF25TimingWindowsAreBoundedAndTelemetryOffIsDormant", TimingWindowsAreBoundedAndTelemetryOffIsDormant),
    ("PublicReplicationProfilesAreExplicitAndMeetSharedSemanticContract", ReplicationProfileConformanceTests.ExplicitProfilesMeetSharedSemanticContract),
    ("P04ConservativeSelectionKeepsDynamicProfilesSeparateFromStaticLayers", ReplicationProfileConformanceTests.ConservativeSelectionKeepsDynamicProfilesSeparateFromStaticLayers),
    ("P01DirtyEntityWorkShapeAndCleanSteadyState", DirtyEntitySpatialFanoutTests.WorkShapeAndCleanSteadyState),
    ("P03FDirtyEntityReadyQueueDrainsEligibleRecipientsOnly", DirtyEntitySpatialFanoutTests.ReadyQueueDrainsEligibleRecipientsOnly),
    ("P01DirtyEntityExistingAudienceAvoidsOldFootprintDiscovery", DirtyEntitySpatialFanoutTests.ExistingAudienceAvoidsOldFootprintDiscovery),
    ("P03EDirtyEntityCoarseSpatialEdgesPreserveExactSemantics", DirtyEntitySpatialFanoutTests.CoarseSpatialEdgesPreserveExactSemantics),
    ("P03EDirtyEntityRejectsUnboundedDiscoveryGeometry", DirtyEntitySpatialFanoutTests.RejectsUnboundedDiscoveryGeometry),
    ("P03EDirtyEntityLargeCustomGridArithmeticUsesWideIntermediates", DirtyEntitySpatialFanoutTests.LargeCustomGridArithmeticUsesWideIntermediates),
    ("P03EDirtyEntityChunkBatchUsesPointDiscoveryForSingleton", DirtyEntitySpatialFanoutTests.ChunkBatchUsesPointDiscoveryForSingleton),
    ("P03EDirtyEntityStateOnlyMutationsAvoidSpatialDiscovery", DirtyEntitySpatialFanoutTests.StateOnlyMutationsAvoidSpatialDiscovery),
    ("P03KDirtyEntityImmediateCadenceCanBeDeferred", DirtyEntitySpatialFanoutTests.ImmediateCadenceCanBeDeferred),
    ("P03DDirtyEntityChunkBatchCoalescesAndAllocatesZero", DirtyEntitySpatialFanoutTests.ChunkBatchedDiscoveryCoalescesAndAllocatesZero),
    ("P01DirtyEntityExactWireBudgetAndRecipientOutstandingIsolation", DirtyEntitySpatialFanoutTests.ExactWireBudgetAndRecipientOutstandingIsolation),
    ("P01DirtyEntitySameRegionMutationKeepsSpatialListsAcyclic", DirtyEntitySpatialFanoutTests.SameRegionEntityMutationKeepsSpatialListsAcyclic),
    ("P01DirtyEntityMovementCleanTopologyRemovalAndReentry", DirtyEntitySpatialFanoutTests.MovementCleanTopologyRemovalAndReentry),
    ("P01DirtyEntityHysteresisHorizonAndDespawn", DirtyEntitySpatialFanoutTests.HysteresisHorizonAndDespawn),
    ("P01DirtyEntityCapacityFailureRetainsFootprint", DirtyEntitySpatialFanoutTests.CapacityFailureRetainsFootprint),
    ("P01DirtyEntityCoalescingCadenceDisclosureAndCapacity", DirtyEntitySpatialFanoutTests.CoalescingCadenceDisclosureAndCapacity),
    ("P01DirtyEntityProofTimeoutAndStaleApplied", DirtyEntitySpatialFanoutTests.ProofTimeoutAndStaleApplied),
    ("P01DirtyEntityStaleBundleRearmsSiblingDebt", DirtyEntitySpatialFanoutTests.StaleBundleRearmsSiblingDebt),
    ("P01DirtyEntityStatusAndRemoteEpochRecovery", DirtyEntitySpatialFanoutTests.StatusAndRemoteEpochRecovery),
    ("P01DirtyEntityFamiliesCoexistAndPrivateDoesNotLeak", DirtyEntitySpatialFanoutTests.FamiliesCoexistAndPrivateDoesNotLeak),
    ("P01DirtyEntityEpochAndProofFencing", DirtyEntitySpatialFanoutTests.EpochAndProofFencing),
    ("P01DirtyEntityWarmedPathAllocatesZero", DirtyEntitySpatialFanoutTests.WarmedPathAllocatesZero),
    ("P01DirtyEntityRemoteProtocolPeerConvergesThroughProof", DirtyEntitySpatialFanoutTests.RemoteProtocolPeerConvergesThroughApplicationProof),
    ("P03DDirtyEntityChunkBatchRemoteProtocolPeerConvergesThroughProof", DirtyEntitySpatialFanoutTests.ChunkBatchRemoteProtocolPeerConvergesThroughApplicationProof),
    ("P02DirtyChunkWorkShapeAndCleanSteadyState", DirtyChunkSpatialFanoutTests.WorkShapeAndCleanSteadyState),
    ("P02DirtyChunkRecipientOutstandingIsolationAndReservedMemoryAreExact", DirtyChunkSpatialFanoutTests.RecipientOutstandingIsolationAndReservedMemoryAreExact),
    ("P02DirtyChunkSameChunkRowsCoalesceIntoOneDiscovery", DirtyChunkSpatialFanoutTests.SameChunkRowsCoalesceIntoOneDiscovery),
    ("P03GDirtyChunkSharedPagesReuseMatchingLineage", DirtyChunkSpatialFanoutTests.SharedPagesReuseMatchingCursorLineageAndInvalidateOnCommit),
    ("P03GDirtyChunkRecipientBundlesKeepIndependentProofs", DirtyChunkSpatialFanoutTests.RecipientBundlePacksChunksButProofsRemainIndependent),
    ("P03GDirtyChunkRecipientBundleCodecIsHostileSafe", DirtyChunkSpatialFanoutTests.RecipientBundleCodecIsCanonicalTransactionalAndBounded),
    ("P03GDirtyChunkRecipientBundleMatchesSingleTrace", DirtyChunkSpatialFanoutTests.RecipientBundleMatchesSingleDeliveryCursorTrace),
    ("P03GDirtyChunkFullBundleProofRequeuesRemainingDebt", DirtyChunkSpatialFanoutTests.FullBundleProofRequeuesRemainingDueDebt),
    ("P02DirtyChunkSpatialCandidatesAvoidFarRecipients", DirtyChunkSpatialFanoutTests.SpatialCandidatesAvoidFarRecipientsAndMatchOracle),
    ("P02DirtyChunkCoarseOccupiedRecipientBucketsPreserveDiscovery", DirtyChunkSpatialFanoutTests.CoarseOccupiedRecipientBucketsPreserveDiscovery),
    ("P02DirtyChunkGridCoversWorldAndSaturatedMovement", DirtyChunkSpatialFanoutTests.GridCoversWorldAndSaturatedMovement),
    ("P02DirtyChunkCleanEntryLeaveReentryAndTeleport", DirtyChunkSpatialFanoutTests.CleanEntryLeaveReentryAndTeleportAreFenced),
    ("P02DirtyChunkImmediateNearFarCadenceAndCoalescing", DirtyChunkSpatialFanoutTests.ImmediateNearFarCadenceAndDeferredCoalescing),
    ("P02DirtyChunkHistoryCapacityAndEpochRecovery", DirtyChunkSpatialFanoutTests.HistoryFallbackCapacityAndEpochRecoveryAreBounded),
    ("P02DirtyChunkCommitGapFailsClosed", DirtyChunkSpatialFanoutTests.CommitGapFailsClosedAndRetainsRecovery),
    ("P02DirtyChunkSourceAbsenceIsNotAoiLeave", DirtyChunkSpatialFanoutTests.SourceAbsenceIsDistinctFromAoiLeave),
    ("P02DirtyChunkQueueCapacityRetainsAndRecovers", DirtyChunkSpatialFanoutTests.DirtyQueueCapacityRetainsAndRecovers),
    ("P02DirtyChunkRecipientCapacityIsTransactional", DirtyChunkSpatialFanoutTests.RecipientReconciliationCapacityIsTransactional),
    ("P02DirtyChunkExistingRecipientExpansionIsTransactional", DirtyChunkSpatialFanoutTests.ExistingRecipientExpansionIsTransactional),
    ("P02DirtyChunkLostProofExpiresAndRetries", DirtyChunkSpatialFanoutTests.LostProofExpiresAndRetries),
    ("P02DirtyChunkDeferredRowDoesNotBlockImmediate", DirtyChunkSpatialFanoutTests.DeferredRowDoesNotBlockImmediate),
    ("P03FDirtyChunkDeferredReadyRecipientWaitsForNextCadence", DirtyChunkSpatialFanoutTests.DeferredReadyRecipientWaitsForNextCadence),
    ("P03FDirtyChunkReadyQueueSkipsQuotaBlockedRecipient", DirtyChunkSpatialFanoutTests.ReadyQueueSkipsQuotaBlockedRecipient),
    ("P02DirtyChunkDistributedDirtyWorkShape", DirtyChunkSpatialFanoutTests.DistributedDirtyWorkShape),
    ("P02DirtyChunkMatchesCanonicalChunkPublicationTrace", DirtyChunkSpatialFanoutTests.MatchesCanonicalChunkPublicationTrace),
    ("P02DirtyChunkWarmedPathAllocatesZero", DirtyChunkSpatialFanoutTests.WarmedPathAllocatesZero),
    ("P03GDirtyChunkWarmedSharedPageBundleAllocatesZero", DirtyChunkSpatialFanoutTests.WarmedSharedPageBundlePathAllocatesZero),
    ("P02DirtyChunkRemoteNetworkProtocolPeerConverges", DirtyChunkSpatialFanoutTests.RemoteNetworkProtocolPeerConverges),
    ("P03GDirtyChunkRemoteRecipientBundleConverges", DirtyChunkSpatialFanoutTests.RemoteRecipientBundleConvergesWithIndependentProofs),
    ("P03IDirtyChunkProofEnvelopeProcessesEntriesIndependently", DirtyChunkSpatialFanoutTests.RemoteProofEnvelopeProcessesEntriesIndependently),
    ("P03GDirtyChunkRecipientSpecificBundleCannotCrossRoute", DirtyChunkSpatialFanoutTests.RecipientSpecificBundleCannotDequeueAnotherParticipant),
    ("P02DirtyChunkRemoteLostProofRetriesAndDrains", DirtyChunkSpatialFanoutTests.RemoteLostProofRetriesAndDrains),
    ("P02DirtyChunkSnapshotTimeoutIsBounded", DirtyChunkSpatialFanoutTests.SnapshotTimeoutIsBounded),
    ("P02DirtyChunkSnapshotCancelBackpressureDoesNotOverlapLifetimes", DirtyChunkSpatialFanoutTests.SnapshotCancelBackpressureDoesNotOverlapLifetimes),
    ("P02DirtyChunkFamiliesCoexistAndPrivateDoesNotLeak", DirtyChunkSpatialFanoutTests.FamiliesCoexistAndPrivateDoesNotLeak),
    ("NetworkProfileIsExplicitAndLocalProfileRetainsSpatialLifecycle", NetworkProfileIsExplicit),
    ("BaselineAndOfflineDirectPublishCanonicalTruth", BaselineAndOfflineDirectPublishCanonicalTruth),
    ("ChangedConsumersIngressAndWarmedCaptureAreIndependentAndBounded", ChangedConsumersIngressAndAllocation),
    ("OwnerPublicationCapturesOnceBeforeMultiOwnerFanout", OwnerPublicationCapturesOnceBeforeMultiOwnerFanout),
    ("PostGraphLifecycleAndRapidMembershipChangesAreCaptured", PostGraphLifecycleAndRapidMembershipChanges),
    ("SameEpochSlotReuseAdvancesReplicationGeneration", SameEpochSlotReuseAdvancesReplicationGeneration),
    ("CapacityAndUnexpectedProjectionFailuresRemainContained", CapacityAndUnexpectedFailuresRemainContained),
    ("ParticipantCapacityRejectsExcessWithoutPoisoningProjection", ParticipantCapacityRejectsWithoutPoisoning),
    ("LifecycleHistoryOverflowRequiresBaseline", LifecycleHistoryOverflowRequiresBaseline),
    ("GenerationReplacementRebuildsEpochAndInvalidatesAccountlessAuthority", GenerationReplacementRebuildsEpoch),
    ("GenerationReplacementRebindsDurableAdmissionAuthority", GenerationReplacementRebindsDurableAdmission),
    ("ApplicationEventsAreIndependentBoundedFacts", ApplicationEventsAreIndependentBoundedFacts),
    ("ApplicationEventParticipantIndexSurvivesWrapChurnAndAllocatesZero", ApplicationEventParticipantIndexSurvivesWrapChurnAndAllocatesZero),
    ("FutureHistoryCursorsRejectWithoutThrowing", FutureHistoryCursorsRejectWithoutThrowing),
    ("CaptureSequenceDomainsPreflightBeforeCommit", CaptureSequenceDomainsPreflightBeforeCommit),
    ("WorkingSetStampRolloverIsExplicitInCheckedBuild", WorkingSetStampRolloverIsExplicitInCheckedBuild),
    ("OfflineDirectRejectsInvalidFramesAtomically", OfflineDirectRejectsInvalidFramesAtomically),
    ("CanonicalProjectionAndSemanticTraceAreDeterministic", CanonicalProjectionAndTraceAreDeterministic),
    ("MaintainedInterestMatchesBruteForceTierOracle", MaintainedInterestMatchesBruteForceTierOracle),
    ("ImmediateHysteresisAndRegionBoundariesAreIndependent", ImmediateHysteresisAndRegionBoundariesAreIndependent),
    ("StableFarUpdateUsesOneMaintainedRelationPass", StableFarUpdateUsesOneMaintainedRelationPass),
    ("StableFarFastPathPreservesImmediateHysteresis", StableFarFastPathPreservesImmediateHysteresis),
    ("ExactDistanceFarEntryWithinOneRegionUsesGeneralDiscovery", ExactDistanceFarEntryWithinOneRegionUsesGeneralDiscovery),
    ("ExactDistanceStateOnlyAndShrinkUseRetainedAudience", ExactDistanceStateOnlyAndShrinkUseRetainedAudience),
    ("StableFarFastPathRejectsEveryGeneralShape", StableFarFastPathRejectsEveryGeneralShape),
    ("RecipientRegionCrossingUpdatesOnlyEnvelopeDifference", RecipientRegionCrossingUpdatesOnlyEnvelopeDifference),
    ("NewestPendingStateAndPromotionCollapseCorrectly", NewestPendingStateAndPromotionCollapseCorrectly),
    ("UnsentPendingStateCoalescesWithoutMovingCadenceDebt", UnsentPendingStateCoalescesWithoutMovingCadenceDebt),
    ("ImmediateCadenceCanBeExplicitlyDeferred", ImmediateCadenceCanBeExplicitlyDeferred),
    ("SentPendingStateSupersedesWithoutLosingCadenceOrProofFencing", SentPendingStateSupersedesWithoutLosingCadenceOrProofFencing),
    ("ContinuouslyDirtyNearAndFarStateCannotStarve", ContinuouslyDirtyNearAndFarStateCannotStarve),
    ("SemanticClassTransitionsKeepExplicitRequeueAndLifetimeRules", SemanticClassTransitionsKeepExplicitRequeueAndLifetimeRules),
    ("PendingCoalescingSequenceExhaustionFailsClosed", PendingCoalescingSequenceExhaustionFailsClosed),
    ("FamilyPolicyNoSubscribersAndSharedSemanticConstructionAreExact", FamilyPolicyNoSubscribersAndSharedSemanticConstructionAreExact),
    ("ApplicationAckCannotClearNewerPendingState", ApplicationAckCannotClearNewerPendingState),
    ("CanonicalSlotReuseCollapsesOlderLifetimeDebt", CanonicalSlotReuseCollapsesOlderLifetimeDebt),
    ("LowerInterestTiersProgressUnderImmediateSaturation", LowerInterestTiersProgressUnderImmediateSaturation),
    ("SameCellMovementExaminesOnlyLocalImmediateNeighborhood", SameCellMovementExaminesOnlyLocalImmediateNeighborhood),
    ("SlowRecipientOutstandingQuotaDoesNotBlockPeerProgress", SlowRecipientOutstandingQuotaDoesNotBlockPeerProgress),
    ("InterestCapacityFailureIsExplicitlyNonviable", InterestCapacityFailureIsExplicitlyNonviable),
    ("ExtremeInterestCoordinatesAndTimeFailClosedWithoutOverflow", ExtremeInterestCoordinatesAndTimeFailClosedWithoutOverflow),
    ("BundleSelectionResumesWithinEachTier", BundleSelectionResumesWithinEachTier),
    ("BundleExpirationSweepCachesOnlyCompletedSemanticTime", BundleExpirationSweepCachesOnlyCompletedSemanticTime),
    ("PendingEligibilityAttributionClassifiesDeferredDebt", PendingEligibilityAttributionClassifiesDeferredDebt),
    ("WarmedMaintainedInterestSchedulingAllocatesZero", WarmedMaintainedInterestSchedulingAllocatesZero),
    ("N1OfflineDirectSubscriptionTopologyAndClientEquivalence", N1OfflineDirectChunkTests.SubscriptionTopologyAndClientEquivalence),
    ("N1OfflineDirectMigrationOrderingPreservesOnePublicIdentity", N1OfflineDirectChunkTests.MigrationOrderingPreservesOnePublicIdentity),
    ("N1OfflineDirectHistoryFallbackAndEpochReplacementConverge", N1OfflineDirectChunkTests.HistoryFallbackAndEpochReplacementConverge),
    ("N1OfflineDirectCapacityAndReadCommitAreTransactional", N1OfflineDirectChunkTests.CapacityAndReadCommitAreTransactional),
    ("N1OfflineDirectWarmedStableRecipientAllocatesZero", N1OfflineDirectChunkTests.WarmedStableRecipientAllocatesZero),
    ("PF5R1HistoryAuditMeasuresScanCollapseAndShadowReuse", N1OfflineDirectChunkTests.HistoryAuditMeasuresScanCollapseAndShadowReuse),
    ("PF5R1IntroductionMutationAndRemovalCannotDisappear", N1OfflineDirectChunkTests.IntroductionMutationAndRemovalCannotDisappear),
    ("PF5R1SnapshotReasonsAndRecipientProofRemainIndependent", N1OfflineDirectChunkTests.SnapshotReasonsAndRecipientProofRemainIndependent),
    ("PF5R2PreparedLatestStateMatchesHistoryRecordsExactly", N1OfflineDirectChunkTests.PreparedLatestStateMatchesHistoryRecordsExactly),
    ("PF5R2SharedPublicationsRetainIndependentRecipientProof", N1OfflineDirectChunkTests.SharedPublicationsRetainIndependentRecipientProof),
    ("PF5R2LagCoverageRemovalAndOutOfOrderProofAreExact", N1OfflineDirectChunkTests.LagCoverageRemovalAndOutOfOrderProofAreExact),
    ("PF5R2PreparedRetentionEvictionRebasesSlowRecipient", N1OfflineDirectChunkTests.PreparedRetentionEvictionRebasesSlowRecipient),
    ("PF5R2PreparedFixedTopologySeparatesSourceFromRecipientScaling", N1OfflineDirectChunkTests.PreparedFixedTopologySeparatesSourceFromRecipientScaling),
    ("N14DirtySchedulerSkipsCleanScopesAndFansOutOnce", N1OfflineDirectChunkTests.DirtySchedulerSkipsCleanScopesAndFansOutOnce),
    ("PF4EDirtyFanoutAndTopologyCensusAreExactAndBounded", N1OfflineDirectChunkTests.DirtyFanoutAndTopologyCensusAreExactAndBounded),
    ("PF4EControlledFanoutSweepVisitsOnlyActualSubscribers", N1OfflineDirectChunkTests.ControlledFanoutSweepVisitsOnlyActualSubscribers),
    ("PF4EFixedSemanticWorkSeparatesTopologyShape", N1OfflineDirectChunkTests.FixedSemanticWorkSeparatesTopologyShape),
    ("PF4ETopologyChurnIsIndependentFromSourceDirtyWork", N1OfflineDirectChunkTests.TopologyChurnIsIndependentFromSourceDirtyWork),
    ("PF4ADirtySchedulerRetryWaitsAndTelemetryResetPreservesDebt", N1OfflineDirectChunkTests.DirtySchedulerRetryWaitsAndTelemetryResetPreservesDebt),
    ("N14StableInterestBudgetSkipsOnlyProvablyUnchangedTopology", N1OfflineDirectChunkTests.StableInterestBudgetSkipsOnlyProvablyUnchangedTopology),
    ("N14StableInterestBudgetMatchesRandomizedExactOracle", N1OfflineDirectChunkTests.StableInterestBudgetMatchesRandomizedExactOracle),
    ("N14ReconcileAttributionIsExplicitAndDormantByDefault", N1OfflineDirectChunkTests.ReconcileAttributionIsExplicitAndDormantByDefault),
    ("PF4ERecipientTelemetryWindowResetPreservesTopologyAndKnowledge", N1OfflineDirectChunkTests.RecipientTelemetryWindowResetPreservesTopologyAndKnowledge),
    ("N14DirtySchedulerFencesTopologySourceGapsAndStaleRecipients", N1OfflineDirectChunkTests.DirtySchedulerFencesTopologySourceGapsAndStaleRecipients),
    ("N14DirtySchedulerWarmedWorkDiscoveryAllocatesZero", N1OfflineDirectChunkTests.DirtySchedulerWarmedWorkDiscoveryAllocatesZero),
    ("N14DirtySchedulerDoesNotRescanDeferredCadencePrefix", N1OfflineDirectChunkTests.DirtySchedulerDoesNotRescanDeferredCadencePrefix),
    ("N14DirtySchedulerCompactIndexesSurviveDeterministicChurn", N1OfflineDirectChunkTests.DirtySchedulerCompactIndexesSurviveDeterministicChurn),
    ("N14DirtySchedulerFeedsAcceptedRemoteChunkKeyPath", N1OfflineDirectChunkTests.DirtySchedulerFeedsAcceptedRemoteChunkKeyPath),
    ("N11ChunkWireCodecsAreCanonicalTransactionalAndHostileSafe", N1OfflineDirectChunkTests.ChunkWireCodecsAreCanonicalTransactionalAndHostileSafe),
    ("N11RemoteEncodingRequiresAppliedProofAndSuppressesKnownAbsence", N1OfflineDirectChunkTests.RemoteEncodingRequiresAppliedProofAndSuppressesKnownAbsence),
    ("N11RejectedProofAndEpochReplacementNeverAdvanceKnowledge", N1OfflineDirectChunkTests.RejectedProofAndEpochReplacementNeverAdvanceKnowledge),
    ("N12WorldStateDeltaAndAbsenceAdvanceOnlyThroughRemoteProof", N1OfflineDirectChunkTests.WorldStateDeltaAndAbsenceAdvanceOnlyThroughRemoteProof),
    ("PF4CSelectedProfileBundlesDueDeltasAndProvesEachEntry", N1OfflineDirectChunkTests.SelectedProfileBundlesDueDeltasAndProvesEachEntry),
    ("PF4CSelectedProfileBundleAdmissionIsAtomicAndSingletonStaysCanonical", N1OfflineDirectChunkTests.SelectedProfileBundleAdmissionIsAtomicAndSingletonStaysCanonical),
    ("PF4CSelectedProfileBundleEntriesApplyAndProveIndependently", N1OfflineDirectChunkTests.SelectedProfileBundleEntriesApplyAndProveIndependently),
    ("PF4CCandidateCadenceTracksLatestCoalescedTruth", N1OfflineDirectChunkTests.CandidateCadenceTracksLatestCoalescedTruth),
    ("PF4CSelectedProfileBundleWarmedPathAllocatesZero", N1OfflineDirectChunkTests.SelectedProfileBundleWarmedPathAllocatesZero),
    ("PF5R4PacketBoundaryCandidateIsPreparedOnce", N1OfflineDirectChunkTests.PacketBoundaryCandidateIsPreparedOnce),
    ("PF5R4PacketBoundaryQueueRefusalIsTransactional", N1OfflineDirectChunkTests.PacketBoundaryQueueRefusalIsTransactional),
    ("PF5R6CompatibleBodySharingKeepsRecipientProofIndependent", N1OfflineDirectChunkTests.CompatibleBodySharingKeepsRecipientProofIndependent),
    ("PF5R6BodyStoreIsBoundedCollisionSafeAndAllocationFree", N1OfflineDirectChunkTests.BodyStoreIsBoundedCollisionSafeAndAllocationFree),
    ("PF5R7DetailedTelemetryOffStopsOutstandingSlabWork", N1OfflineDirectChunkTests.DetailedTelemetryOffStopsOutstandingSlabWork),
    ("PF5R9DetailedTelemetryOffStopsBundleSlabWork", N1OfflineDirectChunkTests.DetailedTelemetryOffStopsBundleSlabWork),
    ("PF4ER2PreparedBundleEncodingIsCanonicalAndBounded", N1OfflineDirectChunkTests.PreparedBundleEncodingIsCanonicalAndBounded),
    ("PF4FCompressedBundleCodecIsExactBoundedHostileSafeAndAllocationFree", N1OfflineDirectChunkTests.CompressedBundleCodecIsExactBoundedHostileSafeAndAllocationFree),
    ("PF4FSelectedCompressionPreservesQueueTransactionAndProofs", N1OfflineDirectChunkTests.SelectedCompressionPreservesQueueTransactionAndProofs),
    ("PF5R5SingletonCompressionIsExactTransactionalAndBounded", N1OfflineDirectChunkTests.SingletonCompressionIsExactTransactionalAndBounded),
    ("PF5R6SingletonCompressionLazyStandaloneWritesOnlyOnFallback", N1OfflineDirectChunkTests.SingletonCompressionLazyStandaloneWritesOnlyOnFallback),
    ("PF5R5SingletonCompressionMtuAndProfitabilityAreExact", N1OfflineDirectChunkTests.SingletonCompressionMtuAndProfitabilityAreExact),
    ("PF5R5SingletonCompressionPoorDataFallsBack", N1OfflineDirectChunkTests.SingletonCompressionPoorDataFallsBack),
    ("PF5R5SingletonCompressionPreservesRemovalAndMasks", N1OfflineDirectChunkTests.SingletonCompressionPreservesRemovalAndMasks),
    ("PF5R5SingletonCompressionConvergesThroughImpairment", N1OfflineDirectChunkTests.SingletonCompressionConvergesThroughImpairment),
    ("PF5R5SingletonCompressionWarmedPathAllocatesZero", N1OfflineDirectChunkTests.SingletonCompressionWarmedPathAllocatesZero),
    ("PF4FIndependentCompressedMessagesConvergeThroughImpairment", N1OfflineDirectChunkTests.IndependentCompressedMessagesConvergeThroughImpairment),
    ("PF4CR1SingletonMtuBoundaryRetainsCanonicalStandalone", N1OfflineDirectChunkTests.SingletonMtuBoundaryRetainsCanonicalStandalone),
    ("N12RemoteRemovalDeltaUsesCanonicalEmptyCurrent", N1OfflineDirectChunkTests.RemoteRemovalDeltaUsesCanonicalEmptyCurrent),
    ("N12CoalescedDeltaOverlapsLocallyAppliedUnprovedTruth", N1OfflineDirectChunkTests.CoalescedDeltaOverlapsLocallyAppliedUnprovedTruth),
    ("PF4BLaterExactProofAdvancesAndRetiresOnlySubsumedPositiveDebt", N1OfflineDirectChunkTests.LaterExactProofAdvancesAndRetiresOnlySubsumedPositiveDebt),
    ("N12ImpairmentAndSubscriptionReentryFenceDeliveryKnowledge", N1OfflineDirectChunkTests.ImpairmentAndSubscriptionReentryFenceDeliveryKnowledge),
    ("N12OversizeWorldStateDeltaFailsClosedAtDatagramBoundary", N1OfflineDirectChunkTests.OversizeWorldStateDeltaFailsClosedAtDatagramBoundary),
    ("N13SnapshotTransactionAppliesAtomicallyAndRecoversOversize", N1OfflineDirectChunkTests.SnapshotTransactionAppliesAtomicallyAndRecoversOversize),
    ("N13SnapshotLineageFencesDelayedAbsenceAndSnapshot", N1OfflineDirectChunkTests.SnapshotLineageFencesDelayedAbsenceAndSnapshot),
    ("N13SnapshotTimeoutAndHistoryAdvanceRemainBounded", N1OfflineDirectChunkTests.SnapshotTimeoutAndHistoryAdvanceRemainBounded),
    ("N13SnapshotImpairmentAndLostProofConverge", N1OfflineDirectChunkTests.SnapshotImpairmentAndLostProofConverge),
    ("N13BulkSnapshotDoesNotBlockOrdinaryWorldState", N1OfflineDirectChunkTests.BulkSnapshotDoesNotBlockOrdinaryWorldState),
    ("PF1ConfiguredParticipantCapacityReservesParticipantEntities", M00SharedPlayableAuthorityTests.ConfiguredParticipantCapacityReservesParticipantEntities),
    ("M00OneGenerationOwnsIndependentParticipantsAndSurvivesLeave", M00SharedPlayableAuthorityTests.OneGenerationOwnsIndependentParticipantsAndSurvivesLeave),
    ("I012SpawnedWeaponRetainsIdentityAcrossWorldInventoryEquipAndAnotherOwner", M00SharedPlayableAuthorityTests.SpawnedWeaponRetainsIdentityAcrossWorldInventoryEquipAndAnotherOwner),
};

static void ReplicationIdentityIsGameOwnedGenerationalAndFixedWidth()
{
    Check.Equal(8, Unsafe.SizeOf<ReplicationEntityId>());
    Check.Equal(8, Unsafe.SizeOf<StateEpoch>());
    var canonical = new ReplicationEntityId(31, 4);
    Check.Equal(canonical, new ReplicationEntityId(31, 4));
    Check.False(new ReplicationEntityId(31, 5) == canonical);
    Check.Throws<ArgumentOutOfRangeException>(() => _ = new ReplicationEntityId(0, 0));
    Check.Throws<ArgumentOutOfRangeException>(() => _ = new StateEpoch(0));
}

static void GameSemanticTraceIsBoundedDeterministicAndAllocationFree()
{
    var first = new GameSemanticTrace(2);
    var second = new GameSemanticTrace(2);
    var state = new GameSemanticTraceEntry(
        GameSemanticTraceKind.ProjectionStatePublished, new StateEpoch(9), 4, 10, 20, 30);
    var lifecycle = new GameSemanticTraceEntry(
        GameSemanticTraceKind.ProjectionLifecyclePublished, new StateEpoch(9), 5, 11, 21, 31);
    var rejected = new GameSemanticTraceEntry(
        GameSemanticTraceKind.ApplicationEventPublished, new StateEpoch(9), 6, 12, 22, 32);

    Check.True(first.TryAppend(state));
    Check.True(first.TryAppend(lifecycle));
    Check.True(second.TryAppend(state));
    Check.True(second.TryAppend(lifecycle));
    Check.False(first.TryAppend(rejected));
    Check.Equal(2, first.Count);
    Check.True(first.CanonicalEquals(second));
    Check.Equal(first.ComputeDigest(), second.ComputeDigest());
    Check.True(first.ReservedBytes >= first.Count * Unsafe.SizeOf<GameSemanticTraceEntry>());

    var reverse = new GameSemanticTrace(2);
    Check.True(reverse.TryAppend(lifecycle));
    Check.True(reverse.TryAppend(state));
    Check.False(first.CanonicalEquals(reverse));
    Check.False(first.ComputeDigest() == reverse.ComputeDigest());

    first.Clear();
    Check.Equal(0, first.Count);
    Check.Equal(2, first.Capacity);

    var warm = new GameSemanticTrace(128);
    Check.True(warm.TryAppend(state));
    _ = warm.ComputeDigest();
    warm.Clear();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < warm.Capacity; index++)
    {
        Check.True(warm.TryAppend(new GameSemanticTraceEntry(
            GameSemanticTraceKind.ProjectionStatePublished,
            new StateEpoch(9), unchecked((ulong)index), 1, 2, 3)));
    }
    _ = warm.ComputeDigest();
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void TimingWindowsAreBoundedAndTelemetryOffIsDormant()
{
    var operations = new GameOperationTimingWindow(GameTelemetryLevel.Off);
    for (int index = 0; index < 32; index++)
        operations.RecordDetailed(operations.StartDetailed());
    Check.Equal(default, operations.Capture());

    operations.Configure(GameTelemetryLevel.Detailed);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 1_024; index++)
        operations.RecordDetailed(operations.StartDetailed());
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    GameTimingDistribution operationSnapshot = operations.Capture();
    Check.Equal(1_024UL, operationSnapshot.Count);
    Check.True(operationSnapshot.P50Microseconds <= operationSnapshot.P95Microseconds);
    Check.True(operationSnapshot.P95Microseconds <= operationSnapshot.P99Microseconds);
    Check.True(operationSnapshot.P99Microseconds <= operationSnapshot.MaximumMicroseconds);

    var ticks = new GameTickTimingWindow();
    ticks.Configure(GameTelemetryLevel.Basic);
    long interval = Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 60);
    long scheduled = System.Diagnostics.Stopwatch.GetTimestamp();
    for (int index = 0; index < 1_024; index++)
    {
        long actual = checked(scheduled + (index % 7));
        ticks.RecordScheduledStart(scheduled, actual, interval, catchUp: index % 97 == 0);
        ticks.RecordElapsed(ticks.Start());
        scheduled = checked(scheduled + interval);
    }
    GameTickTimingSnapshot tickSnapshot = ticks.Capture();
    Check.Equal(1_024UL, tickSnapshot.SampleCount);
    Check.Equal(1_023UL, tickSnapshot.IntervalSampleCount);
    Check.Equal(1_024UL, tickSnapshot.LatenessSampleCount);
    Check.True(tickSnapshot.CatchUpTicks > 0);

    ticks.Configure(GameTelemetryLevel.Off);
    ticks.RecordScheduledStart(scheduled, scheduled, interval, catchUp: false);
    ticks.RecordElapsed(ticks.Start());
    Check.Equal(default(GameTickTimingSnapshot), ticks.Capture());
}

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} five-profile game-replication tests passed.");

static void NetworkProfileIsExplicit()
{
    EcsWorldOptions local = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x9101)),
        GameRuntimeProfile.Local);
    EcsWorldOptions networked = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x9102)),
        GameRuntimeProfile.Networked);
    Check.Equal(UltraSimGameComposition.SpatialLifecycleRecordCapacity, local.LifecycleRecordCapacity);
    Check.Equal(UltraSimGameComposition.NetworkLifecycleRecordCapacity, networked.LifecycleRecordCapacity);

    using var fixture = new Fixture(GameRuntimeProfile.Local, 0x9103, 64);
    using var projection = new GameProjectionService(fixture.Runtime.Game, fixture.Admission,
        Limits(maximumEntities: 64));
    GameProjectionCaptureResult result = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.ReplicationNotConfigured, result.Status);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void MaintainedInterestMatchesBruteForceTierOracle()
{
    var service = CreateInterest();
    ParticipantId alice = new(1);
    ParticipantId bob = new(2);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(alice, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(bob, Point(30, 0), 0));

    (GameProjectedEntity Entity, ReplicationSpatialPoint Position, GameReplicationHorizon Horizon)[] entities =
    [
        (Projected(0, 1, 10), Point(3, 0), GameReplicationHorizon.Far),
        (Projected(1, 1, 20), Point(8, 0), GameReplicationHorizon.Far),
        (Projected(2, 1, 30), Point(25, 0), GameReplicationHorizon.Near),
        (Projected(3, 1, 40), Point(35, 0), GameReplicationHorizon.None),
        // Across the 10 m region boundary but still within Alice's exact R0.
        (Projected(4, 1, 50), Point(-1, 0), GameReplicationHorizon.Far),
    ];
    for (int index = 0; index < entities.Length; index++)
        Check.Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(entities[index].Entity, 1, entities[index].Position,
                entities[index].Horizon, 0));

    var actual = new GameRecipientEntityRelationSnapshot[32];
    int count = service.CopyRelations(actual);
    var expected = new List<(ParticipantId, ReplicationEntityId, GameInterestTier)>();
    AddOracle(expected, alice, Point(0, 0), entities);
    AddOracle(expected, bob, Point(30, 0), entities);
    expected.Sort(static (left, right) =>
    {
        int participant = left.Item1.CompareTo(right.Item1);
        return participant != 0 ? participant : left.Item2.Index.CompareTo(right.Item2.Index);
    });
    Check.Equal(expected.Count, count);
    for (int index = 0; index < count; index++)
    {
        Check.Equal(expected[index].Item1, actual[index].Participant);
        Check.Equal(expected[index].Item2, actual[index].Entity);
        Check.Equal(expected[index].Item3, actual[index].Tier);
        Check.True(actual[index].DesiredPresent);
    }
}

static void ImmediateHysteresisAndRegionBoundariesAreIndependent()
{
    var service = CreateInterest();
    ParticipantId participant = new(3);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    GameProjectedEntity first = Projected(5, 1, 1);
    service.UpsertEntity(first, 1, Point(4, 0), GameReplicationHorizon.Far, 0);
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, first.Entity).Tier);

    // Outside the 5 m enter radius but inside the 6 m leave radius: retain R0.
    service.UpsertEntity(Projected(5, 1, 2), 2, Millimeters(5_500, 0), GameReplicationHorizon.Far, 1);
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, first.Entity).Tier);
    service.UpsertEntity(Projected(5, 1, 3), 3, Millimeters(6_100, 0), GameReplicationHorizon.Far, 2);
    Check.Equal(GameInterestTier.Near, Relation(service, participant, first.Entity).Tier);
    service.UpsertEntity(Projected(5, 1, 4), 4, Millimeters(5_500, 0), GameReplicationHorizon.Far, 3);
    Check.Equal(GameInterestTier.Near, Relation(service, participant, first.Entity).Tier);
    service.UpsertEntity(Projected(5, 1, 5), 5, Millimeters(4_900, 0), GameReplicationHorizon.Far, 4);
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, first.Entity).Tier);

    GameProjectedEntity crossBoundary = Projected(6, 1, 1);
    service.UpsertEntity(crossBoundary, 1, Point(-1, 0), GameReplicationHorizon.Far, 4);
    Check.Equal(GameInterestTier.Immediate,
        Relation(service, participant, crossBoundary.Entity).Tier);
}

static void StableFarUpdateUsesOneMaintainedRelationPass()
{
    var service = CreateInterest();
    GameProjectedEntity entity = Projected(13, 1, 1);
    // The entity deliberately predates both recipients. Region-edge creation
    // must establish the complete maintained relation set without relying on a
    // later entity discovery scan.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(entity, 1, Point(35, 0), GameReplicationHorizon.Far, 0));
    ParticipantId alice = new(101);
    ParticipantId bob = new(102);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(alice, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(bob, Point(30, 0), 0));
    Check.Equal(2, service.GetSnapshot().RelationCount);

    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(13, 1, 2), 2, Point(35, 0),
            GameReplicationHorizon.Far, 1));
    GameInterestAttributionSnapshot first = service.GetAttributionSnapshot();
    Check.Equal(1UL, first.EntityUpserts);
    Check.Equal(1UL, first.StableFarFastPathUpserts);
    Check.Equal(0UL, first.GeneralPathUpserts);
    Check.Equal(2UL, first.FastPathRelationVisits);
    Check.Equal(2UL, first.FastPathReverseEdgeVisitsAvoided);
    Check.Equal(2UL, first.FastPathSecondRelationVisitsAvoided);
    Check.Equal(2UL, first.FastPathRelationEnsureLookupsAvoided);
    Check.Equal(0UL, first.ReverseRegionEdgeVisits);
    Check.Equal(0UL, first.RelationEnsureLookups);
    Check.Equal(0UL, first.PendingRelationVisits);
    Check.Equal(2UL, first.FastPathPendingVersionUpdates);
    Check.Equal(2UL, Relation(service, alice, entity.Entity).PendingCanonicalVersion);
    Check.Equal(2UL, Relation(service, bob, entity.Entity).PendingCanonicalVersion);

    // Remove then recreate Alice's applicable region edge after the entity
    // exists. Edge maintenance must relink the relation before the next stable
    // update, which can still skip reverse discovery safely.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(alice, Point(100, 0), 2));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(alice, Point(10, 0), 3));
    Check.True(Relation(service, alice, entity.Entity).DesiredPresent);
    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(13, 1, 3), 3, Point(35, 0),
            GameReplicationHorizon.Far, 4));
    GameInterestAttributionSnapshot afterEdgeChurn = service.GetAttributionSnapshot();
    Check.Equal(1UL, afterEdgeChurn.StableFarFastPathUpserts);
    Check.Equal(2UL, afterEdgeChurn.FastPathRelationVisits);
    Check.Equal(0UL, afterEdgeChurn.ReverseRegionEdgeVisits);
    Check.Equal(0UL, afterEdgeChurn.PendingRelationVisits);
    Check.Equal(3UL, Relation(service, alice, entity.Entity).PendingCanonicalVersion);
    Check.Equal(3UL, Relation(service, bob, entity.Entity).PendingCanonicalVersion);
}

static void StableFarFastPathPreservesImmediateHysteresis()
{
    var service = CreateInterest();
    ParticipantId participant = new(103);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    GameProjectedEntity entity = Projected(14, 1, 1);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(entity, 1, Point(4, 0), GameReplicationHorizon.Far, 0));

    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(14, 1, 2), 2, Millimeters(5_500, 0),
            GameReplicationHorizon.Far, 1));
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, entity.Entity).Tier);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(14, 1, 3), 3, Millimeters(6_100, 0),
            GameReplicationHorizon.Far, 2));
    Check.Equal(GameInterestTier.Near, Relation(service, participant, entity.Entity).Tier);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(14, 1, 4), 4, Millimeters(5_500, 0),
            GameReplicationHorizon.Far, 3));
    Check.Equal(GameInterestTier.Near, Relation(service, participant, entity.Entity).Tier);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(14, 1, 5), 5, Millimeters(4_900, 0),
            GameReplicationHorizon.Far, 4));
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, entity.Entity).Tier);

    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(4UL, attribution.StableFarFastPathUpserts);
    Check.Equal(0UL, attribution.GeneralPathUpserts);
    Check.Equal(4UL, attribution.FastPathRelationVisits);
    Check.Equal(2UL, attribution.FastPathTierChanges);
    Check.Equal(1UL, attribution.FastPathImmediateEnters);
    Check.Equal(1UL, attribution.FastPathImmediateLeaves);
    Check.Equal(4UL, attribution.FastPathPendingVersionUpdates);
    Check.Equal(0UL, attribution.ReverseRegionEdgeVisits);
    Check.Equal(0UL, attribution.PendingRelationVisits);
}

static void ExactDistanceFarEntryWithinOneRegionUsesGeneralDiscovery()
{
    var service = new GameInterestService(
        new StateEpoch(7),
        new GameInterestLimits(8, 1, 128, 128, 8, 8, 8),
        InterestProfile(),
        GameInterestAdmissionMode.ExactDistance);
    ParticipantId participant = new(105);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    GameProjectedEntity entity = Projected(6, 1, 1);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(entity, 1, Millimeters(40_100, 0),
            GameReplicationHorizon.Far, 0));
    Check.Equal(0, service.GetSnapshot().RelationCount);

    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(6, 1, 2), 2, Millimeters(39_400, 0),
            GameReplicationHorizon.Far, 1));
    Check.Equal(GameInterestTier.Far,
        Relation(service, participant, entity.Entity).Tier);
    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(0UL, attribution.StableFarFastPathUpserts);
    Check.Equal(1UL, attribution.GeneralPathUpserts);
    Check.True(attribution.ReverseRegionEdgeVisits > 0);
}

static void ExactDistanceStateOnlyAndShrinkUseRetainedAudience()
{
    var service = new GameInterestService(
        new StateEpoch(8),
        new GameInterestLimits(8, 1, 128, 128, 8, 8, 8),
        InterestProfile(),
        GameInterestAdmissionMode.ExactDistance);
    ParticipantId participant = new(106);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    GameProjectedEntity entity = Projected(7, 1, 1);
    ReplicationSpatialPoint unchanged = Millimeters(39_400, 0);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(entity, 1, unchanged, GameReplicationHorizon.Far, 0));
    Check.True(Relation(service, participant, entity.Entity).DesiredPresent);

    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(7, 1, 2), 2, unchanged,
            GameReplicationHorizon.Far, 1));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(7, 1, 3), 3, unchanged,
            GameReplicationHorizon.Near, 2));
    Check.True(!Relation(service, participant, entity.Entity).DesiredPresent);

    // Expansion can acquire a new audience, so it must return to exact discovery.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(7, 1, 4), 4, unchanged,
            GameReplicationHorizon.Far, 3));
    Check.True(Relation(service, participant, entity.Entity).DesiredPresent);
    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(2UL, attribution.StableFarFastPathUpserts);
    Check.Equal(1UL, attribution.GeneralPathUpserts);
    Check.True(attribution.ReverseRegionEdgeVisits > 0);
}

static void StableFarFastPathRejectsEveryGeneralShape()
{
    var service = CreateInterest();
    ParticipantId participant = new(104);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(15, 1, 1), 1, Point(35, 0),
            GameReplicationHorizon.Far, 0));

    service.ResetAttribution(enabled: true);
    // Same-region horizon contraction and expansion both require general
    // discovery semantics.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(15, 1, 2), 2, Point(35, 0),
            GameReplicationHorizon.Near, 1));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(15, 1, 3), 3, Point(35, 0),
            GameReplicationHorizon.Far, 2));
    // Region movement and canonical lifetime replacement likewise cannot use
    // the stable predicate.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(15, 1, 4), 4, Point(25, 0),
            GameReplicationHorizon.Far, 3));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(15, 2, 5), 1, Point(25, 0),
            GameReplicationHorizon.Far, 4));

    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(4UL, attribution.EntityUpserts);
    Check.Equal(0UL, attribution.StableFarFastPathUpserts);
    Check.Equal(4UL, attribution.GeneralPathUpserts);
    Check.True(attribution.ReverseRegionEdgeVisits > 0);
    Check.True(Relation(service, participant, new ReplicationEntityId(15, 2)).DesiredPresent);
}

static void RecipientRegionCrossingUpdatesOnlyEnvelopeDifference()
{
    var service = CreateInterest();
    ParticipantId participant = new(4);
    service.SetRecipientAnchor(participant, Point(1, 1), 0);
    GameInterestSnapshot initial = service.GetSnapshot();
    Check.Equal(81, initial.RegionInterestEdgeCount);

    service.SetRecipientAnchor(participant, Point(2, 1), 1);
    GameInterestSnapshot sameCell = service.GetSnapshot();
    Check.Equal(0, sameCell.LastRegionEdgesAdded);
    Check.Equal(0, sameCell.LastRegionEdgesRemoved);
    Check.Equal(0, sameCell.LastRegionEdgesRetiered);

    service.SetRecipientAnchor(participant, Point(11, 1), 2);
    GameInterestSnapshot crossed = service.GetSnapshot();
    Check.Equal(9, crossed.LastRegionEdgesAdded);
    Check.Equal(9, crossed.LastRegionEdgesRemoved);
    Check.True(crossed.LastRegionEdgesRetiered > 0);
    Check.Equal(initial.RegionInterestEdgeCount, crossed.RegionInterestEdgeCount);
}

static void NewestPendingStateAndPromotionCollapseCorrectly()
{
    var service = CreateInterest();
    ParticipantId participant = new(5);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    GameProjectedEntity entity = Projected(7, 1, 1);
    service.UpsertEntity(entity, 1, Point(35, 0), GameReplicationHorizon.Far, 0);
    service.UpsertEntity(Projected(7, 1, 2), 2, Point(35, 0), GameReplicationHorizon.Far, 1);
    service.UpsertEntity(Projected(7, 1, 3), 3, Point(35, 0), GameReplicationHorizon.Far, 2);
    GameRecipientEntityRelationSnapshot pending = Relation(service, participant, entity.Entity);
    Check.Equal(3UL, pending.PendingCanonicalVersion);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 2, 1_024, records, out _));
    service.SetRecipientAnchor(participant, Point(34, 0), 3);
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, entity.Entity).Tier);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 3, 1_024, records, out GameWorldStateBundle bundle));
    Check.Equal(1, bundle.RecordCount);
    Check.Equal(3UL, records[0].CanonicalVersion);
}

static void UnsentPendingStateCoalescesWithoutMovingCadenceDebt()
{
    var service = CadenceInterest();
    ParticipantId participant = new(95);
    var entity = new ReplicationEntityId(1, 1);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
            GameReplicationHorizon.Far, 0));
    GameRecipientEntityRelationSnapshot initial = Relation(service, participant, entity);
    Check.Equal(GameInterestTier.Near, initial.Tier);
    Check.Equal(9L, initial.EligibleAtMilliseconds);
    Check.Equal(0L, initial.PendingSinceMilliseconds);
    Check.False(initial.PendingSequenceWasSent);

    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 2), 2, Point(15, 0),
            GameReplicationHorizon.Far, 1));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 3), 3, Point(15, 0),
            GameReplicationHorizon.Far, 8));
    GameRecipientEntityRelationSnapshot coalesced = Relation(service, participant, entity);
    Check.Equal(3UL, coalesced.PendingCanonicalVersion);
    Check.Equal(initial.PendingRelationSequence, coalesced.PendingRelationSequence);
    Check.Equal(initial.PendingSinceMilliseconds, coalesced.PendingSinceMilliseconds);
    Check.Equal(initial.EligibleAtMilliseconds, coalesced.EligibleAtMilliseconds);
    Check.False(coalesced.PendingSequenceWasSent);

    GameInterestSnapshot beforeSend = service.GetSnapshot();
    Check.Equal(3UL, beforeSend.PendingWrites);
    Check.Equal(1UL, beforeSend.PendingObligationsCreated);
    Check.Equal(2UL, beforeSend.PendingUnsentCoalesces);
    Check.Equal(0UL, beforeSend.PendingSentSupersessions);
    Check.Equal(1UL, beforeSend.RelationSequenceAllocations);
    Check.Equal(0UL, beforeSend.PendingListRelinks);
    Check.Equal(2UL, beforeSend.EligibilityDeadlinesPreserved);
    Check.Equal(0UL, beforeSend.SameTierCoalescesMovedEligibilityLater);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[1];
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 8, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 9, 1_024, records, out GameWorldStateBundle bundle));
    Check.Equal(3UL, records[0].CanonicalVersion);
    GameRecipientEntityRelationSnapshot sent = Relation(service, participant, entity);
    Check.True(sent.PendingSequenceWasSent);
    Check.Equal(19L, sent.EligibleAtMilliseconds);
    Check.Equal(1UL, service.GetSnapshot().EligibilityDeadlinesAdvancedAfterSend);
    Check.True(service.TryAcknowledgeBundle(participant, bundle.BundleId, applied: true));
}

static void ImmediateCadenceCanBeExplicitlyDeferred()
{
    GameInterestProfile profile = InterestProfile() with
    {
        ImmediateIntervalMilliseconds = 32,
    };
    var service = new GameInterestService(
        new StateEpoch(1),
        new GameInterestLimits(4, 1, 256, 256, 8, 4, 2),
        profile);
    ParticipantId participant = new(97);
    var entity = new ReplicationEntityId(1, 1);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 1), 1, Point(1, 0),
            GameReplicationHorizon.Far, 0));
    Check.Equal(GameInterestTier.Immediate, Relation(service, participant, entity).Tier);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[2];
    long firstEligible = Relation(service, participant, entity).EligibleAtMilliseconds;
    Check.True(firstEligible >= 0 && firstEligible < 32);
    if (firstEligible > 0)
    {
        Check.Equal(GameWorldStateBuildStatus.NoData,
            service.TryBuildBundle(participant, firstEligible - 1, 1_024, records, out _));
    }
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, firstEligible, 1_024, records,
            out GameWorldStateBundle first));
    Check.True(service.TryAcknowledgeBundle(participant, first.BundleId, applied: true));

    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 2), 2, Point(1, 0),
            GameReplicationHorizon.Far, firstEligible + 1));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, firstEligible + 31, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, firstEligible + 32, 1_024, records, out _));
}

static void SentPendingStateSupersedesWithoutLosingCadenceOrProofFencing()
{
    var service = CadenceInterest();
    ParticipantId participant = new(95);
    var entity = new ReplicationEntityId(1, 1);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[1];
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 9, 1_024, records, out GameWorldStateBundle oldBundle));
    ulong oldSequence = records[0].RelationSequence;
    GameRecipientEntityRelationSnapshot sent = Relation(service, participant, entity);
    Check.Equal(19L, sent.EligibleAtMilliseconds);
    Check.True(sent.PendingSequenceWasSent);

    service.UpsertEntity(Projected(1, 1, 2), 2, Point(15, 0),
        GameReplicationHorizon.Far, 10);
    GameRecipientEntityRelationSnapshot superseded = Relation(service, participant, entity);
    Check.Equal(2UL, superseded.PendingCanonicalVersion);
    Check.True(superseded.PendingRelationSequence > oldSequence);
    Check.Equal(sent.PendingSinceMilliseconds, superseded.PendingSinceMilliseconds);
    Check.Equal(sent.EligibleAtMilliseconds, superseded.EligibleAtMilliseconds);
    Check.False(superseded.PendingSequenceWasSent);
    Check.Equal(1UL, service.GetSnapshot().PendingSentSupersessions);

    Check.True(service.TryAcknowledgeBundle(participant, oldBundle.BundleId, applied: true));
    Check.False(service.TryAcknowledgeBundle(participant, oldBundle.BundleId, applied: true));
    Check.Equal(2UL, Relation(service, participant, entity).PendingCanonicalVersion);
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 18, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 19, 1_024, records, out GameWorldStateBundle current));
    Check.Equal(2UL, records[0].CanonicalVersion);
    Check.True(service.TryAcknowledgeBundle(participant, current.BundleId, applied: true));

    // A transport handoff failure still means this sequence was observable in
    // principle; the next state must fence it rather than reusing its identity.
    service.UpsertEntity(Projected(1, 1, 3), 3, Point(15, 0),
        GameReplicationHorizon.Far, 20);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 29, 1_024, records, out GameWorldStateBundle abandoned));
    ulong abandonedSequence = records[0].RelationSequence;
    Check.True(service.AbandonBundle(abandoned.BundleId));
    service.UpsertEntity(Projected(1, 1, 4), 4, Point(15, 0),
        GameReplicationHorizon.Far, 30);
    Check.True(Relation(service, participant, entity).PendingRelationSequence > abandonedSequence);
}

static void ContinuouslyDirtyNearAndFarStateCannotStarve()
{
    var service = CadenceInterest();
    ParticipantId participant = new(95);
    var near = new ReplicationEntityId(1, 1);
    var far = new ReplicationEntityId(4, 1);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    service.UpsertEntity(Projected(4, 1, 1), 1, Point(35, 0),
        GameReplicationHorizon.Far, 0);
    Check.Equal(9L, Relation(service, participant, near).EligibleAtMilliseconds);
    Check.Equal(18L, Relation(service, participant, far).EligibleAtMilliseconds);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[2];
    int nearDeliveries = 0;
    int farDeliveries = 0;
    long maximumNearAge = 0;
    long maximumFarAge = 0;
    for (int tick = 1; tick <= 20; tick++)
    {
        long now = checked(tick * 16L); // deliberately skips both phase offsets
        ulong version = checked((ulong)tick + 1UL);
        service.UpsertEntity(Projected(1, 1, tick + 1), version, Point(15, 0),
            GameReplicationHorizon.Far, now);
        service.UpsertEntity(Projected(4, 1, tick + 1), version, Point(35, 0),
            GameReplicationHorizon.Far, now);
        GameWorldStateBuildStatus status = service.TryBuildBundle(
            participant, now, 4_096, records, out GameWorldStateBundle bundle);
        if (status == GameWorldStateBuildStatus.NoData) continue;
        Check.Equal(GameWorldStateBuildStatus.Success, status);
        for (int index = 0; index < bundle.RecordCount; index++)
        {
            Check.Equal(version, records[index].CanonicalVersion);
            if (records[index].Entity == near)
            {
                nearDeliveries++;
                maximumNearAge = Math.Max(maximumNearAge,
                    now - Relation(service, participant, near).PendingSinceMilliseconds);
            }
            else if (records[index].Entity == far)
            {
                farDeliveries++;
                maximumFarAge = Math.Max(maximumFarAge,
                    now - Relation(service, participant, far).PendingSinceMilliseconds);
            }
        }
        Check.True(service.TryAcknowledgeBundle(participant, bundle.BundleId, applied: true));
    }
    Check.True(nearDeliveries >= 8);
    Check.True(farDeliveries >= 6);
    Check.True(maximumNearAge <= 26);
    Check.True(maximumFarAge <= 36);
    GameInterestSnapshot snapshot = service.GetSnapshot();
    Check.True(snapshot.PendingUnsentCoalesces > 0);
    Check.Equal(0UL, snapshot.SameTierCoalescesMovedEligibilityLater);
}

static void PendingCoalescingSequenceExhaustionFailsClosed()
{
    var service = CadenceInterest();
    ParticipantId participant = new(95);
    var entity = new ReplicationEntityId(1, 1);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    SetNextRelationSequence(service, ulong.MaxValue);

    // An unsent rewrite consumes no identity and remains healthy.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 2), 2, Point(15, 0),
            GameReplicationHorizon.Far, 1));
    Check.Equal(0, service.GetSnapshot().NonviableRecipientCount);
    GameRecipientEntityRelationSnapshot unsent = Relation(service, participant, entity);
    Check.Equal(2UL, unsent.PendingCanonicalVersion);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[1];
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 9, 1_024, records, out _));
    ulong sentSequence = records[0].RelationSequence;
    Check.Equal(GameInterestUpdateStatus.RecipientNonviable,
        service.UpsertEntity(Projected(1, 1, 3), 3, Point(15, 0),
            GameReplicationHorizon.Far, 10));
    GameRecipientEntityRelationSnapshot failed = Relation(service, participant, entity);
    Check.Equal(2UL, failed.PendingCanonicalVersion);
    Check.Equal(sentSequence, failed.PendingRelationSequence);
    Check.Equal(1, service.GetSnapshot().NonviableRecipientCount);
}

static void SemanticClassTransitionsKeepExplicitRequeueAndLifetimeRules()
{
    var service = CadenceInterest();
    ParticipantId participant = new(95);
    var firstLifetime = new ReplicationEntityId(1, 1);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    GameRecipientEntityRelationSnapshot near = Relation(service, participant, firstLifetime);
    Check.Equal(GameInterestTier.Near, near.Tier);

    // Near -> Far is an explicit scheduling-class transition: retain the
    // obligation identity but relink it into the new tier exactly once.
    service.SetRecipientAnchor(participant, Point(-20, 0), 1);
    GameRecipientEntityRelationSnapshot far = Relation(service, participant, firstLifetime);
    Check.Equal(GameInterestTier.Far, far.Tier);
    Check.Equal(near.PendingRelationSequence, far.PendingRelationSequence);
    Check.True(service.GetSnapshot().PendingListRelinks > 0);

    // Presence changes are semantic operations, not payload coalesces.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.RemoveEntity(firstLifetime, canonicalVersion: 2, nowMilliseconds: 2));
    GameRecipientEntityRelationSnapshot absent = Relation(service, participant, firstLifetime);
    Check.False(absent.DesiredPresent);
    Check.True(absent.PendingRelationSequence > far.PendingRelationSequence);

    // Canonical slot reuse creates a newer lifetime and collapses the obsolete
    // unsatisfied lifetime according to the existing generation rule.
    var secondLifetime = new ReplicationEntityId(1, 2);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 2, 2), 1, Point(15, 0),
            GameReplicationHorizon.Far, 3));
    Check.False(RelationOrDefault(service, participant, firstLifetime).Participant.IsValid);
    GameRecipientEntityRelationSnapshot current = Relation(service, participant, secondLifetime);
    Check.True(current.DesiredPresent);
    Check.True(current.PendingRelationSequence > absent.PendingRelationSequence);

    GameInterestSnapshot snapshot = service.GetSnapshot();
    Check.Equal(0UL, snapshot.SameTierCoalescesMovedEligibilityLater);
    Check.True(snapshot.PendingObligationsCreated >= 3);
}

static void FamilyPolicyNoSubscribersAndSharedSemanticConstructionAreExact()
{
    var service = CreateInterest();
    GameProjectedEntity noSubscriber = Projected(8, 1, 1);
    service.UpsertEntity(noSubscriber, 1, Point(0, 0), GameReplicationHorizon.Far, 0);
    Check.Equal(0UL, service.GetSnapshot().UniqueSemanticConstructions);

    ParticipantId first = new(6);
    ParticipantId second = new(7);
    service.SetRecipientAnchor(first, Point(0, 0), 0);
    service.SetRecipientAnchor(second, Point(0, 1), 0);
    GameProjectedEntity nearOnly = Projected(9, 1, 1);
    service.UpsertEntity(nearOnly, 1, Point(25, 0), GameReplicationHorizon.Near, 0);
    Check.True(RelationOrDefault(service, first, nearOnly.Entity).DesiredPresent);
    GameProjectedEntity staticContent = Projected(10, 1, 1);
    service.UpsertEntity(staticContent, 1, Point(1, 0), GameReplicationHorizon.None, 0);
    Check.False(RelationOrDefault(service, first, staticContent.Entity).DesiredPresent);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[16];
    DrainRecipient(service, first, 100, records);
    DrainRecipient(service, second, 100, records);
    GameInterestSnapshot snapshot = service.GetSnapshot();
    // Both recipients demanded identical entity/version semantics, built once.
    Check.Equal(2UL, snapshot.UniqueSemanticConstructions);
    Check.True(snapshot.SemanticRecordCopies >= 4);
}

static void ApplicationAckCannotClearNewerPendingState()
{
    var service = CreateInterest();
    ParticipantId participant = new(8);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    GameProjectedEntity entity = Projected(11, 1, 1);
    service.UpsertEntity(entity, 1, Point(1, 0), GameReplicationHorizon.Far, 0);
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 0, 1_024, records, out GameWorldStateBundle oldBundle));
    ulong oldRelationSequence = records[0].RelationSequence;

    service.UpsertEntity(Projected(11, 1, 2), 2, Point(1, 0), GameReplicationHorizon.Far, 1);
    Check.True(service.TryAcknowledgeBundle(participant, oldBundle.BundleId, applied: true));
    GameRecipientEntityRelationSnapshot relation = Relation(service, participant, entity.Entity);
    Check.Equal(2UL, relation.PendingCanonicalVersion);
    Check.True(relation.PendingRelationSequence > oldRelationSequence);

    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 1, 1_024, records, out GameWorldStateBundle latest));
    Check.Equal(2UL, records[0].CanonicalVersion);

    service.UpsertEntity(Projected(11, 1, 3), 3, Point(1, 0), GameReplicationHorizon.Far, 2);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 2, 1_024, records, out GameWorldStateBundle newest));
    Check.Equal(3UL, records[0].CanonicalVersion);

    // Rejecting an older in-flight attempt must not reopen a newer attempt that
    // already carries the current relation sequence.
    Check.True(service.TryAcknowledgeBundle(participant, latest.BundleId, applied: false));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 3, 1_024, records, out _));
    Check.True(service.TryAcknowledgeBundle(participant, newest.BundleId, applied: true));
    Check.Equal(0UL, Relation(service, participant, entity.Entity).PendingCanonicalVersion);

    service.UpsertEntity(Projected(11, 1, 4), 4, Point(1, 0), GameReplicationHorizon.Far, 4);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 4, 1_024, records, out GameWorldStateBundle fourth));
    service.UpsertEntity(Projected(11, 1, 5), 5, Point(1, 0), GameReplicationHorizon.Far, 5);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 5, 1_024, records, out GameWorldStateBundle fifth));
    Check.True(service.TryAcknowledgeBundle(participant, fifth.BundleId, applied: true));
    Check.Equal(5UL, Relation(service, participant, entity.Entity).KnownCanonicalVersion);
    Check.True(service.TryAcknowledgeBundle(participant, fourth.BundleId, applied: true));
    GameRecipientEntityRelationSnapshot converged = Relation(service, participant, entity.Entity);
    Check.Equal(5UL, converged.KnownCanonicalVersion);
    Check.Equal(0UL, converged.PendingCanonicalVersion);
}

static void CanonicalSlotReuseCollapsesOlderLifetimeDebt()
{
    var service = CreateInterest();
    ParticipantId participant = new(80);
    var first = new ReplicationEntityId(50, 1);
    var second = new ReplicationEntityId(50, 2);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(50, 1, 1), 1, Point(1, 0), GameReplicationHorizon.Far, 0));
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 0, 1_024, records, out GameWorldStateBundle initial));
    Check.True(service.TryAcknowledgeBundle(participant, initial.BundleId, applied: true));

    Check.Equal(GameInterestUpdateStatus.Success, service.RemoveEntity(first, 2, 1));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 1, 1_024, records, out GameWorldStateBundle oldTombstone));
    Check.False(records[0].IsPresent);
    Check.Equal(first, records[0].Entity);

    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(50, 2, 2), 1, Point(1, 0), GameReplicationHorizon.Far, 2));
    Span<GameRecipientEntityRelationSnapshot> relations = stackalloc GameRecipientEntityRelationSnapshot[4];
    Check.Equal(1, service.CopyRelations(relations));
    Check.Equal(second, relations[0].Entity);
    Check.Equal(1UL, relations[0].PendingCanonicalVersion);

    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 2, 1_024, records, out GameWorldStateBundle current));
    Check.Equal(1, current.RecordCount);
    Check.True(records[0].IsPresent);
    Check.Equal(second, records[0].Entity);

    // The delayed old-lifetime acknowledgement references a retired relation
    // generation and cannot clear or rewrite the new lifetime.
    Check.True(service.TryAcknowledgeBundle(participant, oldTombstone.BundleId, applied: true));
    Check.Equal(1UL, Relation(service, participant, second).PendingCanonicalVersion);
    Check.True(service.TryAcknowledgeBundle(participant, current.BundleId, applied: true));
    Check.Equal(0UL, Relation(service, participant, second).PendingCanonicalVersion);

    var hiddenFirst = new ReplicationEntityId(51, 1);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(51, 1, 1), 1, Point(1, 0), GameReplicationHorizon.Far, 3));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 3, 1_024, records, out GameWorldStateBundle hiddenInitial));
    Check.True(service.TryAcknowledgeBundle(participant, hiddenInitial.BundleId, applied: true));
    Check.Equal(GameInterestUpdateStatus.Success, service.RemoveEntity(hiddenFirst, 2, 4));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(51, 2, 1), 1, Point(1, 0), GameReplicationHorizon.None, 5));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 5, 1_024, records, out GameWorldStateBundle retainedTombstone));
    Check.False(records[0].IsPresent);
    Check.Equal(hiddenFirst, records[0].Entity);
    Check.True(service.TryAcknowledgeBundle(participant, retainedTombstone.BundleId, applied: true));
}

static void LowerInterestTiersProgressUnderImmediateSaturation()
{
    var service = new GameInterestService(
        new StateEpoch(3),
        new GameInterestLimits(32, 2, 512, 1_024, 128, 64, 1),
        InterestProfile());
    ParticipantId participant = new(82);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    for (uint index = 1; index <= 8; index++)
        Check.Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(index, 1, 1), 1,
                Millimeters(500 + checked((int)index), 0), GameReplicationHorizon.Far, 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(20, 1, 1), 1, Point(10, 0), GameReplicationHorizon.Far, 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(21, 1, 1), 1, Point(30, 0), GameReplicationHorizon.Far, 0));

    Span<GameWorldStateRecord> record = stackalloc GameWorldStateRecord[1];
    Span<ulong> immediateVersions = stackalloc ulong[9];
    immediateVersions.Fill(1);
    bool nearProgress = false;
    bool farProgress = false;
    for (int iteration = 0; iteration < 14 && (!nearProgress || !farProgress); iteration++)
    {
        Check.Equal(GameWorldStateBuildStatus.Success,
            service.TryBuildBundle(participant, 100 + iteration, 1_024, record,
                out GameWorldStateBundle bundle));
        uint index = record[0].Entity.Index;
        nearProgress |= index == 20;
        farProgress |= index == 21;
        Check.True(service.TryAcknowledgeBundle(participant, bundle.BundleId, applied: true));
        if (index is >= 1 and <= 8)
        {
            ulong version = ++immediateVersions[(int)index];
            Check.Equal(GameInterestUpdateStatus.Success,
                service.UpsertEntity(Projected(index, 1, checked((int)version)), version,
                    Millimeters(500 + checked((int)index), 0), GameReplicationHorizon.Far,
                    100 + iteration));
        }
    }
    Check.True(nearProgress);
    Check.True(farProgress);
}

static void SameCellMovementExaminesOnlyLocalImmediateNeighborhood()
{
    var service = new GameInterestService(
        new StateEpoch(4),
        new GameInterestLimits(256, 2, 512, 1_024, 512, 64, 16),
        InterestProfile());
    ParticipantId participant = new(83);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 1), 1, Point(1, 0), GameReplicationHorizon.Far, 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(2, 1, 1), 1, Point(6, 0), GameReplicationHorizon.Far, 0));
    for (uint index = 3; index < 203; index++)
        Check.Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(index, 1, 1), 1,
                Point(30, 30), GameReplicationHorizon.Far, 0));

    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(1, 0), 1));
    GameInterestSnapshot snapshot = service.GetSnapshot();
    Check.Equal(0, snapshot.LastRegionEdgesAdded);
    Check.Equal(0, snapshot.LastRegionEdgesRemoved);
    Check.True(snapshot.RelationCount >= 202);
    Check.True(snapshot.LastRelationsExamined < 16);
    Check.Equal(GameInterestTier.Immediate,
        Relation(service, participant, new ReplicationEntityId(1, 1)).Tier);
}

static void SlowRecipientOutstandingQuotaDoesNotBlockPeerProgress()
{
    var service = new GameInterestService(
        new StateEpoch(2),
        new GameInterestLimits(8, 2, 256, 256, 8, 2, 2),
        InterestProfile());
    ParticipantId slow = new(81);
    ParticipantId healthy = new(82);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(slow, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(healthy, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(3, 1, 1), 1,
            Point(1, 0), GameReplicationHorizon.Far, 0));
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[2];
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(slow, 0, 1_024, records, out GameWorldStateBundle retained));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildNextBundle(0, 1_024, records, out GameWorldStateBundle peer));
    Check.Equal(healthy, peer.Participant);
    Check.True(service.TryAcknowledgeBundle(healthy, peer.BundleId, applied: true));
    Check.Equal(1UL,
        Relation(service, slow, new ReplicationEntityId(3, 1)).PendingCanonicalVersion);
    Check.True(service.AbandonBundle(retained.BundleId));
    Check.Equal(1, service.GetSnapshot().PendingRelationCount);
}

static void InterestCapacityFailureIsExplicitlyNonviable()
{
    var limits = new GameInterestLimits(8, 1, 128, 128, 1, 4, 4);
    var service = new GameInterestService(new StateEpoch(1), limits, InterestProfile());
    service.UpsertEntity(Projected(0, 1, 1), 1, Point(1, 0), GameReplicationHorizon.Far, 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(2, 0), GameReplicationHorizon.Far, 0);
    GameInterestUpdateStatus status = service.SetRecipientAnchor(new ParticipantId(9), Point(0, 0), 0);
    Check.True(status is GameInterestUpdateStatus.CapacityExceeded or GameInterestUpdateStatus.RecipientNonviable);
    Check.Equal(1, service.GetSnapshot().NonviableRecipientCount);
    service.ResetAttribution(enabled: true);
    Check.Equal(GameInterestUpdateStatus.RecipientNonviable,
        service.UpsertEntity(Projected(0, 1, 2), 2, Point(1, 0),
            GameReplicationHorizon.Far, 1));
    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(0UL, attribution.StableFarFastPathUpserts);
    Check.Equal(1UL, attribution.GeneralPathUpserts);
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
    Check.Equal(GameWorldStateBuildStatus.RecipientNonviable,
        service.TryBuildBundle(new ParticipantId(9), 0, 1_024, records, out _));
}

static void ExtremeInterestCoordinatesAndTimeFailClosedWithoutOverflow()
{
    var edgeProfile = new GameInterestProfile(1, 1, 1, 1, 1, 1, 1, 1, 2);
    var edge = new GameInterestService(
        new StateEpoch(3),
        new GameInterestLimits(2, 1, 16, 16, 4, 2, 2),
        edgeProfile);
    Check.Equal(GameInterestUpdateStatus.InvalidInput,
        edge.SetRecipientAnchor(new ParticipantId(90),
            new ReplicationSpatialPoint(int.MaxValue, int.MaxValue), 0));
    Check.Equal(0, edge.GetSnapshot().RecipientCount);

    var timeProfile = new GameInterestProfile(10, 1, 1, 10, 20, 7, 11, 1, 2);
    var time = new GameInterestService(
        new StateEpoch(4),
        new GameInterestLimits(2, 1, 64, 64, 4, 2, 2),
        timeProfile);
    Check.Equal(GameInterestUpdateStatus.Success,
        time.SetRecipientAnchor(new ParticipantId(91), new ReplicationSpatialPoint(0, 0), long.MaxValue));
    Check.Equal(GameInterestUpdateStatus.Success,
        time.UpsertEntity(Projected(1, 1, 1), 1,
            new ReplicationSpatialPoint(15, 0), GameReplicationHorizon.Far, long.MaxValue));
    Check.Equal(long.MaxValue,
        Relation(time, new ParticipantId(91), new ReplicationEntityId(1, 1)).EligibleAtMilliseconds);
}

static void WarmedMaintainedInterestSchedulingAllocatesZero()
{
    var service = CreateInterest();
    ParticipantId participant = new(10);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    GameProjectedEntity entity = Projected(12, 1, 0);
    service.UpsertEntity(entity, 1, Point(1, 0), GameReplicationHorizon.Far, 0);
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[4];
    DrainRecipient(service, participant, 0, records);
    for (int version = 2; version < 8; version++)
    {
        service.UpsertEntity(Projected(12, 1, version), (ulong)version,
            Point(1, 0), GameReplicationHorizon.Far, version);
        DrainRecipient(service, participant, version, records);
    }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int version = 8; version < 72; version++)
    {
        service.UpsertEntity(Projected(12, 1, version), (ulong)version,
            Point(1, 0), GameReplicationHorizon.Far, version);
        DrainRecipient(service, participant, version, records);
    }
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

    var cadence = CadenceInterest();
    ParticipantId cadenceParticipant = new(95);
    cadence.SetRecipientAnchor(cadenceParticipant, Point(0, 0), 0);
    cadence.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    cadence.UpsertEntity(Projected(1, 1, 2), 2, Point(15, 0),
        GameReplicationHorizon.Far, 1);
    before = GC.GetAllocatedBytesForCurrentThread();
    for (int version = 3; version < 1_003; version++)
        cadence.UpsertEntity(Projected(1, 1, version), checked((ulong)version),
            Point(15, 0), GameReplicationHorizon.Far, 1);
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    Check.Equal(1UL, cadence.GetSnapshot().RelationSequenceAllocations);
}

static void BundleExpirationSweepCachesOnlyCompletedSemanticTime()
{
    var profile = new GameInterestProfile(
        RegionEdgeMillimeters: 10_000,
        ImmediateEnterRadiusMillimeters: 5_000,
        ImmediateLeaveRadiusMillimeters: 6_000,
        NearRadiusMillimeters: 20_000,
        FarRadiusMillimeters: 40_000,
        NearIntervalMilliseconds: 10,
        FarIntervalMilliseconds: 20,
        ApplicationAckRetryMilliseconds: 100,
        ApplicationAckTimeoutMilliseconds: 100);
    var service = new GameInterestService(
        new StateEpoch(5),
        new GameInterestLimits(4, 2, 512, 1_024, 8, 4, 1),
        profile);
    ParticipantId first = new(93);
    ParticipantId second = new(94);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(first, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(second, Point(0, 0), 0));
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 1), 1, Point(1, 0),
            GameReplicationHorizon.Far, 0));
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[1];

    service.ResetAttribution(enabled: true);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(first, 0, 1_024, records, out GameWorldStateBundle firstBundle));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(second, 0, 1_024, records, out GameWorldStateBundle secondBundle));
    Check.True(service.TryAcknowledgeBundle(first, firstBundle.BundleId, applied: true));
    Check.True(service.AbandonBundle(secondBundle.BundleId));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(first, 0, 1_024, records, out _));

    GameInterestAttributionSnapshot cached = service.GetAttributionSnapshot();
    Check.Equal(3UL, cached.ExpirationRequests);
    Check.Equal(1UL, cached.ExpirationSweeps);
    Check.Equal(1UL, cached.ExpirationDistinctTimeRequests);
    Check.Equal(2UL, cached.ExpirationRepeatedSameTimeRequests);
    Check.Equal(2UL, cached.ExpirationSweepCacheHits);
    Check.Equal(4UL, cached.ExpirationSlotsExamined);
    Check.Equal(0UL, cached.ExpirationActiveSlotsExamined);
    Check.Equal(0UL, cached.ExpirationActualExpirations);
    Check.Equal(2, cached.ExpirationMaximumBundleHigh);

    // A different semantic time always sweeps. The exact timeout boundary releases
    // the old attempt before selection, restoring retry eligibility in the same call.
    Check.Equal(GameInterestUpdateStatus.Success,
        service.UpsertEntity(Projected(1, 1, 2), 2, Point(1, 0),
            GameReplicationHorizon.Far, 10));
    service.ResetAttribution(enabled: true);
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(first, 10, 1_024, records, out GameWorldStateBundle retained));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(first, 109, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(first, 110, 1_024, records, out GameWorldStateBundle retried));
    Check.False(retained.BundleId == retried.BundleId);
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(first, 110, 1_024, records, out _));

    GameInterestAttributionSnapshot boundary = service.GetAttributionSnapshot();
    Check.Equal(4UL, boundary.ExpirationRequests);
    Check.Equal(3UL, boundary.ExpirationSweeps);
    Check.Equal(3UL, boundary.ExpirationDistinctTimeRequests);
    Check.Equal(1UL, boundary.ExpirationRepeatedSameTimeRequests);
    Check.Equal(1UL, boundary.ExpirationSweepCacheHits);
    Check.Equal(12UL, boundary.ExpirationSlotsExamined);
    Check.Equal(1UL, boundary.ExpirationActualExpirations);

    // Large forward and backward time jumps both invalidate the same-time cache.
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(first, 1_000, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(first, 500, 1_024, records, out _));
    GameInterestAttributionSnapshot jumped = service.GetAttributionSnapshot();
    Check.Equal(5UL, jumped.ExpirationSweeps);
    Check.Equal(2UL, jumped.ExpirationActualExpirations);

    // Repeated same-time cache hits remain allocation-free after warmup.
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(first, 500, 1_024, records, out _));
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 10_000; index++)
        Check.Equal(GameWorldStateBuildStatus.NoData,
            service.TryBuildBundle(first, 500, 1_024, records, out _));
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void PendingEligibilityAttributionClassifiesDeferredDebt()
{
    var profile = new GameInterestProfile(10_000, 5_000, 6_000, 20_000, 40_000,
        NearIntervalMilliseconds: 10,
        FarIntervalMilliseconds: 20,
        ApplicationAckRetryMilliseconds: 100,
        ApplicationAckTimeoutMilliseconds: 200);
    var service = new GameInterestService(
        new StateEpoch(6),
        new GameInterestLimits(8, 1, 512, 1_024, 16, 8, 1),
        profile);
    ParticipantId participant = new(95);
    service.SetRecipientAnchor(participant, Point(0, 0), 0);
    service.UpsertEntity(Projected(1, 1, 1), 1, Point(15, 0),
        GameReplicationHorizon.Far, 0);
    service.UpsertEntity(Projected(4, 1, 1), 1, Point(35, 0),
        GameReplicationHorizon.Far, 0);
    GameRecipientEntityRelationSnapshot near = Relation(
        service, participant, new ReplicationEntityId(1, 1));
    GameRecipientEntityRelationSnapshot far = Relation(
        service, participant, new ReplicationEntityId(4, 1));
    Check.Equal(9L, near.EligibleAtMilliseconds);
    Check.Equal(18L, far.EligibleAtMilliseconds);

    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[1];
    service.ResetAttribution(enabled: true);
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 8, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 9, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 17, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 18, 1_024, records, out _));
    Check.Equal(GameWorldStateBuildStatus.NoData,
        service.TryBuildBundle(participant, 19, 1_024, records, out _));

    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.True(attribution.PendingSkippedCadence >= 2);
    Check.Equal(3UL, attribution.NoDataAllDeferredBuilds);
    Check.True(attribution.CadenceDeferredDelayMaximumMilliseconds > 0);
    Check.True(attribution.PendingSelectableTicks > 0);
    Check.True(attribution.PendingTraversalTicks > 0);
    Check.True(attribution.TierSelectionTicks >= attribution.PendingTraversalTicks);
    Check.Equal(5UL, attribution.ReadinessGateChecks);
    Check.True(attribution.ReadinessDeadlineRecomputes > 0);
    Check.True(attribution.ReadinessRelationsExamined > 0);
    Check.Equal(3UL, attribution.ReadinessNoDataShortCircuits);
    Check.True(attribution.ReadinessTierSkips > 0);

    ulong examinedBeforeCachedNoData = attribution.ReadinessRelationsExamined;
    ulong shortCircuitsBefore = attribution.ReadinessNoDataShortCircuits;
    for (int attempt = 0; attempt < 100; attempt++)
        Check.Equal(GameWorldStateBuildStatus.NoData,
            service.TryBuildBundle(participant, 19, 1_024, records, out _));
    GameInterestAttributionSnapshot cached = service.GetAttributionSnapshot();
    Check.Equal(examinedBeforeCachedNoData, cached.ReadinessRelationsExamined);
    Check.Equal(shortCircuitsBefore + 100, cached.ReadinessNoDataShortCircuits);

    Span<ulong> cadence = stackalloc ulong[GameInterestService.DeferredDelayHistogramLength];
    Span<ulong> retry = stackalloc ulong[GameInterestService.DeferredDelayHistogramLength];
    Check.Equal(GameInterestService.DeferredDelayHistogramLength,
        service.CopyDeferredDelayHistograms(cadence, retry));
    ulong cadenceCount = 0;
    ulong retryCount = 0;
    for (int index = 0; index < cadence.Length; index++)
    {
        cadenceCount += cadence[index];
        retryCount += retry[index];
    }
    Check.Equal(attribution.PendingSkippedCadence, cadenceCount);
    Check.Equal(attribution.PendingSkippedAwaitingRetry, retryCount);
}

static void BundleSelectionResumesWithinEachTier()
{
    var service = CreateInterest();
    ParticipantId participant = new(92);
    Check.Equal(GameInterestUpdateStatus.Success,
        service.SetRecipientAnchor(participant, Point(0, 0), 0));
    Span<GameWorldStateRecord> records = stackalloc GameWorldStateRecord[16];
    for (int index = 1; index <= 16; index++)
        Check.Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(checked((uint)index), 1, 1), 1, Point(1, 0),
                GameReplicationHorizon.Far, 0));
    DrainRecipient(service, participant, 0, records);

    service.ResetAttribution(enabled: true);
    for (int index = 1; index <= 16; index++)
        Check.Equal(GameInterestUpdateStatus.Success,
            service.UpsertEntity(Projected(checked((uint)index), 1, 2), 2, Point(1, 0),
                GameReplicationHorizon.Far, 1));
    Check.Equal(GameWorldStateBuildStatus.Success,
        service.TryBuildBundle(participant, 1, 4_096, records, out GameWorldStateBundle bundle));
    Check.Equal(16, bundle.RecordCount);
    GameInterestAttributionSnapshot attribution = service.GetAttributionSnapshot();
    Check.Equal(16UL, attribution.PendingImmediateExamined);
    Check.Equal(16UL, attribution.PendingSelectedImmediate);
    Check.Equal(0UL, attribution.PendingSkippedAlreadySelected);
    Span<ulong> histogram = stackalloc ulong[17];
    Check.Equal(17, service.CopyBundleRecordHistogram(histogram));
    Check.Equal(1UL, histogram[16]);
}

static void BaselineAndOfflineDirectPublishCanonicalTruth()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9201, 64);
    ParticipantId participant = new(11);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    Check.True(entered.IsEntered);
    using var projection = fixture.CreateProjection();
    Check.True(projection.SynchronizeParticipant(participant));
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, baseline.Status);
    Check.True(baseline.Frame.StateEpoch.IsValid);
    Check.Equal(1, projection.GetSnapshot().EntityCount);
    Check.Equal(1, projection.GetSnapshot().ParticipantCount);

    var offline = new OfflineDirectGameProjection(64, 8, 512);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyBaseline(projection, baseline.Frame));
    OfflineProjectionSnapshot snapshot = offline.GetSnapshot();
    Check.Equal(baseline.Frame.StateEpoch, snapshot.StateEpoch);
    Check.Equal(1, snapshot.EntityCount);
    Check.Equal(1, snapshot.ParticipantCount);
    Span<GameProjectionParticipant> bindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, offline.CopyParticipants(bindings));
    Check.Equal(participant, bindings[0].Participant);
    Check.True(bindings[0].ControlledEntity.IsValid);
}

static void ChangedConsumersIngressAndAllocation()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9301, 64);
    ParticipantId participant = new(21);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 4_096);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));

    ChangeCursor independent = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, _) => { independent = world.CreateChangeCursorFromNow(); return true; }, out bool cursorReady));
    Check.True(cursorReady);

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 1, 7)));
    GameAuthoritativeIngressResult ingress = projection.PublishAuthoritativeIngress();
    Check.Equal(GameAuthoritativeIngressStatus.Success, ingress.Status);
    Check.Equal(1, ingress.Accepted);
    fixture.Runtime.Tick();
    GameProjectionCaptureResult captured = projection.CaptureAfterTick();
    Check.True(captured.Succeeded);
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];
    int deltaCount = projection.CopyLastFrameDeltas(deltas);
    Check.True(deltaCount >= 1);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, captured.Frame, deltas[..deltaCount]));
    Check.Equal(8, ReadSimulation(fixture, entered.Entity).TickMarker);

    int independentlyDelivered = 0;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) =>
        {
            var visitor = new CountingChangedVisitor();
            independentlyDelivered = game.Replication!.ExecuteChanges(world, ref independent, ref visitor);
            return true;
        }, out bool independentRead));
    Check.True(independentRead);
    Check.Equal(1, independentlyDelivered);

    // Warm delegates, scheduler paths, projection staging, and OfflineDirect before allocation measurement.
    for (int index = 0; index < 4; index++)
        TickCaptureApply(fixture, projection, offline, deltas);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 16; index++)
        TickCaptureApply(fixture, projection, offline, deltas);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check.Equal(0L, allocated);

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 1, 99)));
    GameAuthoritativeIngressResult duplicate = projection.PublishAuthoritativeIngress();
    Check.Equal(0, duplicate.Accepted);
    Check.Equal(1, duplicate.Rejected);
    Check.False(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(
        participant,
        2,
        (GameSimulationCommandKind)byte.MaxValue,
        0)));

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 3, 1)));
    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 2, 100)));
    GameAuthoritativeIngressResult ordered = projection.PublishAuthoritativeIngress();
    Check.Equal(1, ordered.Accepted);
    Check.Equal(1, ordered.Rejected);
    TickCaptureApply(fixture, projection, offline, deltas);

    for (ulong sequence = 4; sequence < 8; sequence++)
    {
        Check.True(projection.TryEnqueueAuthoritativeInput(
            new GameAuthoritativeInput(participant, sequence, 1)));
        Check.Equal(GameAuthoritativeIngressStatus.Success,
            projection.PublishAuthoritativeIngress().Status);
        TickCaptureApply(fixture, projection, offline, deltas);
    }
    before = GC.GetAllocatedBytesForCurrentThread();
    for (ulong sequence = 8; sequence < 72; sequence++)
    {
        Check.True(projection.TryEnqueueAuthoritativeInput(
            new GameAuthoritativeInput(participant, sequence, 1)));
        Check.Equal(GameAuthoritativeIngressStatus.Success,
            projection.PublishAuthoritativeIngress().Status);
        TickCaptureApply(fixture, projection, offline, deltas);
    }
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void OwnerPublicationCapturesOnceBeforeMultiOwnerFanout()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9302, 64);
    ParticipantId firstParticipant = new(22);
    ParticipantId secondParticipant = new(23);
    Check.True(fixture.Admission.JoinAccountless(firstParticipant).IsEntered);
    Check.True(fixture.Admission.JoinAccountless(secondParticipant).IsEntered);
    using var projection = fixture.CreateProjection();
    Check.True(projection.SynchronizeParticipant(firstParticipant));
    Check.True(projection.SynchronizeParticipant(secondParticipant));
    Check.True(projection.RebuildBaseline().Succeeded);

    var datagrams = new DeterministicDatagramLimits(256, 512);
    Check.True(ProtocolLoopback.TryCreate(
        NetworkProtocolLimits.Default, datagrams, default,
        out ProtocolLoopback? firstLink, out _, UltraSimGameProtocol.Manifest));
    Check.True(ProtocolLoopback.TryCreate(
        NetworkProtocolLimits.Default, datagrams, default,
        out ProtocolLoopback? secondLink, out _, UltraSimGameProtocol.Manifest));
    using (firstLink)
    using (secondLink)
    {
        var first = new RemoteControlledSimulationServer(
            firstLink!.Server, fixture.Runtime.Game, projection, firstParticipant, 1);
        var second = new RemoteControlledSimulationServer(
            secondLink!.Server, fixture.Runtime.Game, projection, secondParticipant, 1);
        ulong before = projection.GetSnapshot().FrameSequence;
        fixture.Runtime.Tick();
        RemoteControlledSimulationServer[] owners = [first, second];
        Check.True(RemoteControlledSimulationServer.CaptureAndQueueOwnerStates(
            projection, fixture.Runtime.Game, owners, out GameProjectionCaptureResult capture));
        Check.Equal(before + 1, capture.Frame.FrameSequence);
        Check.Equal(capture.Frame.FrameSequence, projection.GetSnapshot().FrameSequence);
        Check.Equal(1UL, first.GetSnapshot().OwnerStatesQueued);
        Check.Equal(1UL, second.GetSnapshot().OwnerStatesQueued);
    }
}

static void PostGraphLifecycleAndRapidMembershipChanges()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9401, 64);
    ParticipantId participant = new(31);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 2_048);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 1, GameSimulationCommandKind.RemoveSimulationState, 0));
    GameProjectionCaptureResult removed = projection.CaptureAfterTick();
    int removedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..removedCount], GameProjectionDeltaKind.ComponentRemoved);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, removed.Frame, deltas[..removedCount]));

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 2, GameSimulationCommandKind.AddSimulationState, 41));
    GameProjectionCaptureResult added = projection.CaptureAfterTick();
    int addedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..addedCount], GameProjectionDeltaKind.ComponentAdded);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, added.Frame, deltas[..addedCount]));
    Check.True(ReadSimulation(fixture, entered.Entity).TickMarker >= 41);

    // Remove then add before one capture: both committed topology facts remain
    // ordered while canonical state folds to the final present value.
    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 3, GameSimulationCommandKind.RemoveSimulationState, 0));
    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 4, GameSimulationCommandKind.AddSimulationState, 73));
    GameProjectionCaptureResult folded = projection.CaptureAfterTick();
    int foldedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..foldedCount], GameProjectionDeltaKind.ComponentRemoved);
    Check.Contains(deltas[..foldedCount], GameProjectionDeltaKind.ComponentAdded);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, folded.Frame, deltas[..foldedCount]));

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 5, GameSimulationCommandKind.RetireControlledEntity, 0));
    GameProjectionCaptureResult destroyed = projection.CaptureAfterTick();
    int destroyedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..destroyedCount], GameProjectionDeltaKind.Destroyed);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, destroyed.Frame, deltas[..destroyedCount]));
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(0, offline.GetSnapshot().EntityCount);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void CapacityAndUnexpectedFailuresRemainContained()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9501, 64);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 8, 1, 1, 8, 8, 8));
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    fixture.Admission.JoinAccountless(new ParticipantId(41));
    fixture.Admission.JoinAccountless(new ParticipantId(42));
    GameProjectionCaptureResult overflow = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.CaptureResyncRequired, overflow.Status);
    Check.True(overflow.RequiresBaseline);
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
    Check.Equal(GameProjectionCaptureStatus.RebuildRequired, projection.CaptureAfterTick().Status);
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    Check.Equal(2, projection.GetSnapshot().EntityCount);

    using var faultFixture = new Fixture(GameRuntimeProfile.Networked, 0x9502, 64);
    faultFixture.Admission.JoinAccountless(new ParticipantId(43));
    using var faulted = faultFixture.CreateProjection();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, faulted.RebuildBaseline().Status);
    faultFixture.Runtime.Tick();
    faulted.FaultInjector = point =>
    {
        if (point == GameProjectionFaultPoint.BeforeCaptureCommit)
            throw new InvalidOperationException("injected derived-state failure");
    };
    GameProjectionCaptureResult failure = faulted.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.Faulted, failure.Status);
    Check.True(faulted.IsFaulted);
    Check.Equal(ServerRuntimeState.Running, faultFixture.Runtime.State);
}

static void ParticipantCapacityRejectsWithoutPoisoning()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9503, 64);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 1, 64, 128, 64, 8, 8));
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    ParticipantId admitted = new(44);
    ParticipantId excess = new(45);
    Check.True(fixture.Admission.JoinAccountless(admitted).IsEntered);
    Check.True(fixture.Admission.JoinAccountless(excess).IsEntered);
    Check.True(projection.SynchronizeParticipant(admitted));
    Check.False(projection.SynchronizeParticipant(excess));
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    Check.True(capture.Succeeded);
    Check.Equal(1, projection.GetSnapshot().ParticipantCount);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void SameEpochSlotReuseAdvancesReplicationGeneration()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9504, 64);
    using var projection = fixture.CreateProjection();
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Entity first = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) => world.TryInstantiate(game.Content.Character, out first), out bool created));
    Check.True(created);
    Check.True(projection.CaptureAfterTick().Succeeded);
    Span<GameProjectedEntity> entities = stackalloc GameProjectedEntity[2];
    Check.Equal(1, projection.CopyEntities(entities));
    ReplicationEntityId firstProjection = entities[0].Entity;

    Entity replacement = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) => world.TryDestroy(first) &&
            world.TryInstantiate(game.Content.Character, out replacement), out bool replaced));
    Check.True(replaced);
    Check.Equal(first.Index, replacement.Index);
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    Check.True(capture.Succeeded);
    Check.Equal(baseline.Frame.StateEpoch, capture.Frame.StateEpoch);
    Check.Equal(1, projection.CopyEntities(entities));
    Check.Equal(firstProjection.Index, entities[0].Entity.Index);
    Check.Equal(firstProjection.Generation + 1, entities[0].Entity.Generation);
}

static void LifecycleHistoryOverflowRequiresBaseline()
{
    const int population = UltraSimGameComposition.NetworkLifecycleRecordCapacity + 1;
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9601, population + 16);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(population + 16, 8, population + 8, population + 8, 32, 8, 8));
    ParticipantId participant = new(46);
    Check.True(fixture.Admission.JoinAccountless(participant).IsEntered);
    Check.True(projection.SynchronizeParticipant(participant));
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, baseline.Status);
    var offline = new OfflineDirectGameProjection(population + 16, 8, 64);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Check.True(projection.TryPublishApplicationEvent(participant, 1, 10, 100, out _));
    Span<GameApplicationEvent> eventScratch = stackalloc GameApplicationEvent[2];
    GameProjectionHistoryReadResult firstRead = projection.ReadApplicationEvents(0, eventScratch[..1]);
    Check.Equal(GameProjectionHistoryReadStatus.Success, firstRead.Status);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyApplicationEvents(eventScratch[..firstRead.Count]));
    Check.True(projection.TryPublishApplicationEvent(participant, 2, 20, 200, out GameApplicationEvent retained));
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, generationId, game) =>
        {
            for (int index = 0; index < population; index++)
            {
                if (!world.TryInstantiate(game.Content.Character, out Entity transient) ||
                    !world.TryDestroy(transient)) return false;
            }
            return true;
        }, out bool populated));
    Check.True(populated);
    GameProjectionCaptureResult overflow = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.LifecycleResyncRequired, overflow.Status);
    Check.True(overflow.RequiresBaseline);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
    GameProjectionCaptureResult repaired = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, repaired.Status);
    Check.Equal(baseline.Frame.StateEpoch, repaired.Frame.StateEpoch);
    Check.True(repaired.Frame.BaselineRevision > baseline.Frame.BaselineRevision);
    Check.Equal(1, projection.GetSnapshot().EntityCount);
    GameProjectionHistoryReadResult retainedRead = projection.ReadApplicationEvents(firstRead.NextSequence, eventScratch);
    Check.Equal(GameProjectionHistoryReadStatus.Success, retainedRead.Status);
    Check.Equal(1, retainedRead.Count);
    Check.Equal(retained, eventScratch[0]);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, repaired.Frame));
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyApplicationEvents(eventScratch[..retainedRead.Count]));
}

static void ApplicationEventsAreIndependentBoundedFacts()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9701, 64);
    ParticipantId participant = new(51);
    fixture.Admission.JoinAccountless(participant);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 8, 64, 128, 8, 2, 8));
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.True(projection.TryPublishApplicationEvent(participant, 1, 10, 100, out GameApplicationEvent first));
    Check.True(projection.TryPublishApplicationEvent(participant, 2, 20, 200, out GameApplicationEvent second));
    Check.False(projection.TryPublishApplicationEvent(participant, 3, 30, 300, out _));
    Span<GameApplicationEvent> events = stackalloc GameApplicationEvent[2];
    GameProjectionHistoryReadResult read = projection.ReadApplicationEvents(0, events);
    Check.Equal(GameProjectionHistoryReadStatus.Success, read.Status);
    Check.Equal(first, events[0]);
    Check.Equal(second, events[1]);
    var offline = new OfflineDirectGameProjection(64, 8, 32);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyApplicationEvents(events));
    projection.DiscardApplicationEventsThrough(read.NextSequence);
    Check.True(projection.TryPublishApplicationEvent(participant, 3, 30, 300, out _));
}

static void ApplicationEventParticipantIndexSurvivesWrapChurnAndAllocatesZero()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x970A, 64);
    ParticipantId[] participants = [new(51), new(52), new(53), new(54)];
    for (int index = 0; index < participants.Length; index++)
        Check.True(fixture.Admission.JoinAccountless(participants[index]).IsEntered);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 8, 64, 128, 8, 64, 8));
    for (int index = 0; index < participants.Length; index++)
        Check.True(projection.SynchronizeParticipant(participants[index]));
    ulong floor = projection.RebuildBaseline().Frame.ApplicationEventNextSequence;
    ulong[] cursors = new ulong[participants.Length];
    Array.Fill(cursors, floor);
    for (int index = 0; index < participants.Length; index++)
        Check.True(projection.TryRegisterApplicationEventConsumer(participants[index], floor));

    Span<GameApplicationEvent> scratch = stackalloc GameApplicationEvent[1];
    Check.True(projection.TryPublishApplicationEvent(
        participants[1], 1, 1, 1, out GameApplicationEvent onlyForSecond));
    GameProjectionHistoryReadResult absent = projection.ReadApplicationEvents(
        participants[0], cursors[0], scratch);
    Check.Equal(GameProjectionHistoryReadStatus.NoData, absent.Status);
    Check.Equal(onlyForSecond.Sequence + 1, absent.NextPublishedSequence);
    cursors[0] = absent.NextPublishedSequence;
    Check.True(projection.TryAdvanceApplicationEventConsumer(participants[0], cursors[0]));
    Check.Equal(1UL, projection.GetSnapshot().ApplicationEventNoRelevantFastForwards);

    GameProjectionHistoryReadResult relevant = projection.ReadApplicationEvents(
        participants[1], cursors[1], scratch);
    Check.Equal(GameProjectionHistoryReadStatus.Success, relevant.Status);
    Check.Equal(onlyForSecond, scratch[0]);
    cursors[1] = relevant.NextSequence;
    Check.True(projection.TryAdvanceApplicationEventConsumer(participants[1], cursors[1]));
    for (int index = 2; index < participants.Length; index++)
    {
        GameProjectionHistoryReadResult skipped = projection.ReadApplicationEvents(
            participants[index], cursors[index], scratch);
        Check.Equal(GameProjectionHistoryReadStatus.NoData, skipped.Status);
        cursors[index] = skipped.NextPublishedSequence;
        Check.True(projection.TryAdvanceApplicationEventConsumer(participants[index], cursors[index]));
    }
    Check.Equal(0, projection.GetSnapshot().RetainedApplicationEvents);

    Check.True(projection.UnregisterApplicationEventConsumer(participants[3]));
    Check.True(projection.TryRegisterApplicationEventConsumer(participants[3], cursors[3]));

    for (int round = 0; round < 80; round++)
    {
        GameApplicationEvent lastPublished = default;
        for (int index = 0; index < participants.Length; index++)
            Check.True(projection.TryPublishApplicationEvent(
                participants[index], checked((ushort)(index + 1)), checked((ulong)round + 10),
                checked((ulong)index + 100), out lastPublished));
        ulong published = checked(lastPublished.Sequence + 1);
        for (int index = 0; index < participants.Length; index++)
        {
            GameProjectionHistoryReadResult read = projection.ReadApplicationEvents(
                participants[index], cursors[index], scratch);
            Check.Equal(GameProjectionHistoryReadStatus.Success, read.Status);
            Check.Equal(participants[index], scratch[0].Participant);
            cursors[index] = read.NextSequence;
            Check.True(projection.TryAdvanceApplicationEventConsumer(participants[index], cursors[index]));
            GameProjectionHistoryReadResult tail = projection.ReadApplicationEvents(
                participants[index], cursors[index], scratch);
            Check.Equal(GameProjectionHistoryReadStatus.NoData, tail.Status);
            Check.Equal(published, tail.NextPublishedSequence);
            cursors[index] = published;
            Check.True(projection.TryAdvanceApplicationEventConsumer(participants[index], published));
        }
    }
    GameProjectionSnapshot afterWrap = projection.GetSnapshot();
    Check.Equal(0, afterWrap.RetainedApplicationEvents);
    Check.True(afterWrap.ApplicationEventParticipantIndexHits >= 321);
    Check.Equal(0UL, afterWrap.ApplicationEventConsumerLookupComparisons);
    Check.True(afterWrap.ApplicationEventConsumerLookupAttempts > 0);
    Check.True(afterWrap.ApplicationEventMinimumCursorFastPathUpdates >
        afterWrap.ApplicationEventMinimumCursorFullRescans);

    _ = projection.ReadApplicationEvents(participants[0], cursors[0], scratch);
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int iteration = 0; iteration < 10_000; iteration++)
    {
        GameProjectionHistoryReadResult read = projection.ReadApplicationEvents(
            participants[0], cursors[0], scratch);
        Check.Equal(GameProjectionHistoryReadStatus.NoData, read.Status);
        Check.True(projection.TryAdvanceApplicationEventConsumer(participants[0], cursors[0]));
    }
    Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
}

static void FutureHistoryCursorsRejectWithoutThrowing()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9702, 64);
    using var projection = fixture.CreateProjection();
    Check.True(projection.RebuildBaseline().Succeeded);
    Span<GameProjectionLifecycleFact> lifecycle = stackalloc GameProjectionLifecycleFact[1];
    GameProjectionHistoryReadResult lifecycleFuture = projection.ReadLifecycle(1, lifecycle);
    Check.Equal(GameProjectionHistoryReadStatus.FutureSequence, lifecycleFuture.Status);
    Check.Equal(0, lifecycleFuture.Count);
    Check.Equal(0UL, lifecycleFuture.NextPublishedSequence);

    Span<GameApplicationEvent> events = stackalloc GameApplicationEvent[1];
    GameProjectionHistoryReadResult eventFuture = projection.ReadApplicationEvents(1, events);
    Check.Equal(GameProjectionHistoryReadStatus.FutureSequence, eventFuture.Status);
    Check.Equal(0, eventFuture.Count);
    Check.Equal(0UL, eventFuture.NextPublishedSequence);
}

static void CaptureSequenceDomainsPreflightBeforeCommit()
{
    VerifySequenceExhaustion(frame: ulong.MaxValue, delta: 0, lifecycle: 0, overwritten: 0, 0x9703);
    VerifySequenceExhaustion(frame: 0, delta: ulong.MaxValue, lifecycle: 0, overwritten: 0, 0x9704);
    VerifySequenceExhaustion(frame: 0, delta: 0, lifecycle: ulong.MaxValue, overwritten: 0, 0x9705);

    static void VerifySequenceExhaustion(
        ulong frame,
        ulong delta,
        ulong lifecycle,
        ulong overwritten,
        ulong worldIdentity)
    {
        using var fixture = new Fixture(GameRuntimeProfile.Networked, worldIdentity, 64);
        using var projection = fixture.CreateProjection();
        Check.True(projection.RebuildBaseline().Succeeded);
        fixture.Admission.JoinAccountless(new ParticipantId(55));
        projection.SetSequenceStateForTest(frame, delta, lifecycle, overwritten);
        GameProjectionSnapshot before = projection.GetSnapshot();
        fixture.Runtime.Tick();
        GameProjectionCaptureResult rejected = projection.CaptureAfterTick();
        Check.Equal(GameProjectionCaptureStatus.CapacityExceeded, rejected.Status);
        Check.False(projection.IsFaulted);
        GameProjectionSnapshot after = projection.GetSnapshot();
        Check.Equal(before.EntityCount, after.EntityCount);
        Check.Equal(before.FrameSequence, after.FrameSequence);
        Check.Equal(before.LifecycleOverwrittenCount, after.LifecycleOverwrittenCount);
        Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
    }
}

static void WorkingSetStampRolloverIsExplicitInCheckedBuild()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9706, 64);
    ParticipantId participant = new(56);
    Check.True(fixture.Admission.JoinAccountless(participant).IsEntered);
    using var projection = fixture.CreateProjection();
    Check.True(projection.SynchronizeParticipant(participant));
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, baseline.Status);
    var offline = new OfflineDirectGameProjection(64, 8, 128);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyBaseline(projection, baseline.Frame));
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];

    // Leave generation-1 stamps behind so rollover must clear them before
    // generation 1 is reused for the same canonical entity.
    fixture.Runtime.Tick();
    GameProjectionCaptureResult first = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.Success, first.Status);
    int firstCount = projection.CopyLastFrameDeltas(deltas);
    Check.True(firstCount > 0);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, first.Frame, deltas[..firstCount]));

    projection.SetWorkingStampForTest(uint.MaxValue);
    fixture.Runtime.Tick();
    GameProjectionCaptureResult rollover = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.Success, rollover.Status);
    int rolloverCount = projection.CopyLastFrameDeltas(deltas);
    Check.True(rolloverCount > 0);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, rollover.Frame, deltas[..rolloverCount]));

    fixture.Runtime.Tick();
    GameProjectionCaptureResult following = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.Success, following.Status);
    int followingCount = projection.CopyLastFrameDeltas(deltas);
    Check.True(followingCount > 0);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, following.Frame, deltas[..followingCount]));
    Check.False(projection.IsFaulted);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void GenerationReplacementRebindsDurableAdmission()
{
    const ulong persistentWorld = 0x9682;
    var ui = new TestUiPort();
    using var fixture = new Fixture(GameRuntimeProfile.Networked, persistentWorld, 64, ui);
    ParticipantId participant = new(48);
    AccountId accountId = new(480);
    CharacterId characterId = new(481);
    var identity = new TrustedLocalIdentity("network-rebind");
    CharacterRecord character = PopulateDurableCharacter(
        fixture.Runtime,
        accountId,
        characterId,
        identity);
    AdmissionResult waiting = fixture.Admission.JoinAccountBacked(participant, identity);
    Check.True(waiting.IsWaitingForCharacter);
    ui.Submit(new UiActionRequest(
        waiting.CharacterSelectView,
        CharacterSelectUi.Definition.Handle,
        CharacterSelectUi.Play,
        new UiRowId(characterId.Value)));
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult entered));
    Check.True(entered.IsEntered);

    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Span<GameProjectionParticipant> beforeBindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, projection.CopyParticipants(beforeBindings));

    GameStaging staging = fixture.Composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    Check.True(staging.World.TryInstantiate(staging.Content.Character, out Entity reboundEntity));
    PersistentEntityId reboundPersistentId = staging.World.GetOrAssignPersistentId(reboundEntity);
    Check.Equal(character.PersistentEntityKey.Entity, reboundPersistentId);
    var accounting = new AccountingStore();
    accounting.AddAccount(new AccountRecord(accountId));
    accounting.BindTrustedIdentity(identity, accountId);
    accounting.AddCharacter(character);
    var candidate = new GameGenerationState(
        accounting,
        staging.Inventory,
        staging.Items,
        staging.Content,
        staging.WorldState,
        staging.SimulationLookup,
        staging.Replication,
        staging.WorldActions);
    Check.True(fixture.Runtime.Game.TryPublishPreparedGeneration(
        staging.World,
        candidate,
        out ulong replacementGeneration));
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult rebound));
    Check.True(rebound.IsEntered);
    Check.Equal(replacementGeneration, rebound.GenerationId);
    Check.Equal(reboundEntity, rebound.Entity);

    GameProjectionCaptureResult replacement = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, replacement.Status);
    Span<GameProjectionParticipant> afterBindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, projection.CopyParticipants(afterBindings));
    Check.Equal(participant, afterBindings[0].Participant);
    Check.True(afterBindings[0].ControlledEntity.IsValid);
    Check.Equal(replacementGeneration, afterBindings[0].ServerGenerationId);
    Check.False(replacement.Frame.StateEpoch == baseline.Frame.StateEpoch);
}

static void GenerationReplacementRebuildsEpoch()
{
    const ulong persistentWorld = 0x9681;
    using var fixture = new Fixture(GameRuntimeProfile.Networked, persistentWorld, 64);
    ParticipantId participant = new(47);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 128);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));

    GameStaging staging = fixture.Composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    var candidate = new GameGenerationState(
        new UltraSim.Game.Accounting.AccountingStore(),
        staging.Inventory,
        staging.Items,
        staging.Content,
        staging.WorldState,
        staging.SimulationLookup,
        staging.Replication,
        staging.WorldActions);
    Check.True(fixture.Runtime.Game.TryPublishPreparedGeneration(
        staging.World,
        candidate,
        out ulong replacementGeneration));
    Check.True(replacementGeneration > baseline.Frame.ServerGenerationId);
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult rejected));
    Check.True(rejected.IsRejected);
    Check.Equal(AdmissionRejection.GenerationReplaced, rejected.Rejection);

    GameProjectionCaptureResult replacement = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, replacement.Status);
    Check.False(replacement.Frame.StateEpoch == baseline.Frame.StateEpoch);
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(0, projection.GetSnapshot().ParticipantCount);
    Check.Equal(OfflineProjectionApplyStatus.BaselineRequired,
        offline.ApplyFrame(projection, replacement.Frame, ReadOnlySpan<GameProjectionDelta>.Empty));
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyBaseline(projection, replacement.Frame));
    Check.Equal(0, offline.GetSnapshot().EntityCount);
    Check.False(entered.Entity == default);
}

static void OfflineDirectRejectsInvalidFramesAtomically()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9801, 64);
    fixture.Admission.JoinAccountless(new ParticipantId(61));
    using var projection = fixture.CreateProjection();
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 128);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    OfflineProjectionSnapshot before = offline.GetSnapshot();
    var invalid = new GameProjectionFrame(
        baseline.Frame.StateEpoch,
        baseline.Frame.ServerGenerationId,
        baseline.Frame.SimulationTick,
        baseline.Frame.BaselineRevision,
        99,
        100,
        baseline.Frame.NextDeltaSequence,
        0,
        0, 0, 0, 0);
    Check.Equal(OfflineProjectionApplyStatus.BaselineRequired,
        offline.ApplyFrame(projection, invalid, ReadOnlySpan<GameProjectionDelta>.Empty));
    Check.Equal(before, offline.GetSnapshot());

    fixture.Runtime.Tick();
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];
    int count = projection.CopyLastFrameDeltas(deltas);
    Check.True(count > 0);
    GameProjectionDelta original = deltas[0];
    deltas[0] = original with
    {
        SimulationState = new GameSimulationState
        {
            TickMarker = original.SimulationState.TickMarker + 1,
        },
    };
    Check.Equal(OfflineProjectionApplyStatus.InvalidDelta,
        offline.ApplyFrame(projection, capture.Frame, deltas[..count]));
    Check.Equal(before, offline.GetSnapshot());
    deltas[0] = original;

    GameProjectionFrame alteredHistory = capture.Frame with
    {
        LifecycleNextSequence = capture.Frame.LifecycleNextSequence + 1,
    };
    Check.Equal(OfflineProjectionApplyStatus.InvalidDelta,
        offline.ApplyFrame(projection, alteredHistory, deltas[..count]));
    Check.Equal(before, offline.GetSnapshot());
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, capture.Frame, deltas[..count]));
}

static void CanonicalProjectionAndTraceAreDeterministic()
{
    OfflineProjectionSnapshot left = RunDeterministicScenario(0x9901);
    OfflineProjectionSnapshot right = RunDeterministicScenario(0x9902);
    Check.Equal(left.EntityCount, right.EntityCount);
    Check.Equal(left.ParticipantCount, right.ParticipantCount);
    Check.Equal(left.CanonicalHash, right.CanonicalHash);
    Check.Equal(left.SemanticTraceDigest, right.SemanticTraceDigest);
}

static OfflineProjectionSnapshot RunDeterministicScenario(ulong worldIdentity)
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, worldIdentity, 64);
    ParticipantId participant = new(71);
    fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 512);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];
    for (int tick = 0; tick < 5; tick++)
    {
        if (tick == 2)
        {
            Check.True(projection.TryEnqueueAuthoritativeInput(
                new GameAuthoritativeInput(participant, 1, 5)));
            Check.Equal(GameAuthoritativeIngressStatus.Success,
                projection.PublishAuthoritativeIngress().Status);
        }
        fixture.Runtime.Tick();
        GameProjectionCaptureResult frame = projection.CaptureAfterTick();
        int count = projection.CopyLastFrameDeltas(deltas);
        Check.Equal(OfflineProjectionApplyStatus.Success,
            offline.ApplyFrame(projection, frame.Frame, deltas[..count]));
    }
    return offline.GetSnapshot();
}

static void TickCaptureApply(
    Fixture fixture,
    GameProjectionService projection,
    OfflineDirectGameProjection offline,
    Span<GameProjectionDelta> scratch)
{
    fixture.Runtime.Tick();
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    if (!capture.Succeeded) throw new InvalidOperationException("Capture failed during allocation gate.");
    int count = projection.CopyLastFrameDeltas(scratch);
    if (offline.ApplyFrame(projection, capture.Frame, scratch[..count]) != OfflineProjectionApplyStatus.Success)
        throw new InvalidOperationException("Offline apply failed during allocation gate.");
}

static void EnqueueAndTick(
    Fixture fixture,
    GameProjectionService projection,
    in GameAuthoritativeInput input)
{
    Check.True(projection.TryEnqueueAuthoritativeInput(input));
    GameAuthoritativeIngressResult ingress = projection.PublishAuthoritativeIngress();
    Check.Equal(GameAuthoritativeIngressStatus.Success, ingress.Status);
    Check.Equal(1, ingress.Accepted);
    fixture.Runtime.Tick();
}

static GameSimulationState ReadSimulation(Fixture fixture, Entity entity)
{
    GameSimulationState result = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) => game.TryReadSimulationState(world, entity, out result), out bool read));
    Check.True(read);
    return result;
}

static CharacterRecord PopulateDurableCharacter(
    ServerRuntime runtime,
    AccountId accountId,
    CharacterId characterId,
    TrustedLocalIdentity identity)
{
    CharacterRecord record = default;
    Check.True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) =>
        {
            if (!world.TryInstantiate(game.Content.Character, out Entity entity)) return false;
            var key = new PersistentEntityKey(world.PersistentWorldId, world.GetOrAssignPersistentId(entity));
            game.Accounting.AddAccount(new AccountRecord(accountId));
            game.Accounting.BindTrustedIdentity(identity, accountId);
            record = new CharacterRecord(
                characterId,
                accountId,
                slot: 0,
                displayName: "Network Character",
                GamePrefabKeys.Character,
                key);
            game.Accounting.AddCharacter(record);
            return true;
        }, out bool populated));
    Check.True(populated);
    return record;
}

static GameInterestService CreateInterest() => new(
    new StateEpoch(1),
    new GameInterestLimits(
        MaximumEntities: 64,
        MaximumRecipients: 8,
        MaximumRegions: 512,
        MaximumRegionInterestEdges: 1_024,
        MaximumRecipientEntityRelations: 512,
        MaximumOutstandingBundles: 64,
        MaximumRecordsPerBundle: 16),
    InterestProfile());

static GameInterestService CadenceInterest() => new(
    new StateEpoch(6),
    new GameInterestLimits(
        MaximumEntities: 8,
        MaximumRecipients: 1,
        MaximumRegions: 512,
        MaximumRegionInterestEdges: 1_024,
        MaximumRecipientEntityRelations: 16,
        MaximumOutstandingBundles: 64,
        MaximumRecordsPerBundle: 8),
    new GameInterestProfile(
        RegionEdgeMillimeters: 10_000,
        ImmediateEnterRadiusMillimeters: 5_000,
        ImmediateLeaveRadiusMillimeters: 6_000,
        NearRadiusMillimeters: 20_000,
        FarRadiusMillimeters: 40_000,
        NearIntervalMilliseconds: 10,
        FarIntervalMilliseconds: 20,
        ApplicationAckRetryMilliseconds: 100,
        ApplicationAckTimeoutMilliseconds: 200));

static void SetNextRelationSequence(GameInterestService service, ulong value)
{
    FieldInfo field = typeof(GameInterestService).GetField(
        "_nextRelationSequence",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Relation-sequence field was not found.");
    field.SetValue(service, value);
}

static GameInterestProfile InterestProfile() => new(
    RegionEdgeMillimeters: 10_000,
    ImmediateEnterRadiusMillimeters: 5_000,
    ImmediateLeaveRadiusMillimeters: 6_000,
    NearRadiusMillimeters: 20_000,
    FarRadiusMillimeters: 40_000,
    NearIntervalMilliseconds: 10,
    FarIntervalMilliseconds: 20,
    ApplicationAckRetryMilliseconds: 5,
    ApplicationAckTimeoutMilliseconds: 100);

static ReplicationSpatialPoint Point(int xMeters, int zMeters) =>
    new(checked(xMeters * 1_000), checked(zMeters * 1_000));

static ReplicationSpatialPoint Millimeters(int x, int z) => new(x, z);

static GameProjectedEntity Projected(uint index, uint generation, int marker) => new(
    new ReplicationEntityId(index, generation),
    HasSimulationState: true,
    new GameSimulationState
    {
        TickMarker = marker,
        InputVelocity = marker * 2,
    });

static void AddOracle(
    List<(ParticipantId Participant, ReplicationEntityId Entity, GameInterestTier Tier)> destination,
    ParticipantId participant,
    in ReplicationSpatialPoint anchor,
    ReadOnlySpan<(GameProjectedEntity Entity, ReplicationSpatialPoint Position, GameReplicationHorizon Horizon)> entities)
{
    GameInterestProfile profile = InterestProfile();
    int anchorRegionX = FloorDivide(anchor.XMillimeters, profile.RegionEdgeMillimeters);
    int anchorRegionZ = FloorDivide(anchor.ZMillimeters, profile.RegionEdgeMillimeters);
    int nearCells = DivideRoundUp(profile.NearRadiusMillimeters, profile.RegionEdgeMillimeters);
    int farCells = DivideRoundUp(profile.FarRadiusMillimeters, profile.RegionEdgeMillimeters);
    for (int index = 0; index < entities.Length; index++)
    {
        ref readonly var candidate = ref entities[index];
        int regionX = FloorDivide(candidate.Position.XMillimeters, profile.RegionEdgeMillimeters);
        int regionZ = FloorDivide(candidate.Position.ZMillimeters, profile.RegionEdgeMillimeters);
        int regionDistance = Math.Max(Math.Abs(regionX - anchorRegionX), Math.Abs(regionZ - anchorRegionZ));
        GameInterestTier coarse = regionDistance <= nearCells
            ? GameInterestTier.Near
            : regionDistance <= farCells ? GameInterestTier.Far : GameInterestTier.None;
        GameInterestTier tier = Within(anchor, candidate.Position, profile.ImmediateEnterRadiusMillimeters) &&
            Allows(candidate.Horizon, GameInterestTier.Immediate)
            ? GameInterestTier.Immediate
            : Allows(candidate.Horizon, coarse) ? coarse : GameInterestTier.None;
        if (tier != GameInterestTier.None)
            destination.Add((participant, candidate.Entity.Entity, tier));
    }

    static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    static int DivideRoundUp(int value, int divisor) =>
        checked((int)(((long)value + divisor - 1L) / divisor));

    static bool Within(in ReplicationSpatialPoint left, in ReplicationSpatialPoint right, int radius)
    {
        Int128 dx = (Int128)left.XMillimeters - right.XMillimeters;
        Int128 dz = (Int128)left.ZMillimeters - right.ZMillimeters;
        Int128 r = radius;
        return dx * dx + dz * dz <= r * r;
    }

    static bool Allows(GameReplicationHorizon horizon, GameInterestTier tier) => tier switch
    {
        GameInterestTier.Immediate => horizon >= GameReplicationHorizon.Immediate,
        GameInterestTier.Near => horizon >= GameReplicationHorizon.Near,
        GameInterestTier.Far => horizon >= GameReplicationHorizon.Far,
        _ => false,
    };
}

static GameRecipientEntityRelationSnapshot Relation(
    GameInterestService service,
    ParticipantId participant,
    ReplicationEntityId entity)
{
    GameRecipientEntityRelationSnapshot result = RelationOrDefault(service, participant, entity);
    if (!result.Participant.IsValid)
        throw new InvalidOperationException($"Expected relation {participant}/{entity}.");
    return result;
}

static GameRecipientEntityRelationSnapshot RelationOrDefault(
    GameInterestService service,
    ParticipantId participant,
    ReplicationEntityId entity)
{
    Span<GameRecipientEntityRelationSnapshot> relations = stackalloc GameRecipientEntityRelationSnapshot[512];
    int count = service.CopyRelations(relations);
    for (int index = 0; index < count; index++)
        if (relations[index].Participant == participant && relations[index].Entity == entity)
            return relations[index];
    return default;
}

static void DrainRecipient(
    GameInterestService service,
    ParticipantId participant,
    long nowMilliseconds,
    Span<GameWorldStateRecord> records)
{
    while (true)
    {
        GameWorldStateBuildStatus status = service.TryBuildBundle(
            participant,
            nowMilliseconds,
            4_096,
            records,
            out GameWorldStateBundle bundle);
        if (status == GameWorldStateBuildStatus.NoData) return;
        if (status != GameWorldStateBuildStatus.Success)
            throw new InvalidOperationException($"Unexpected bundle status {status}.");
        if (!service.TryAcknowledgeBundle(participant, bundle.BundleId, applied: true))
            throw new InvalidOperationException("Expected application acknowledgement to succeed.");
    }
}

static GameReplicationLimits Limits(int maximumEntities) =>
    new(maximumEntities, 8, 128, 256, 256, 64, 64);

sealed class Fixture : IDisposable
{
    internal Fixture(
        GameRuntimeProfile profile,
        ulong worldIdentity,
        int entityCapacity,
        IUiSemanticPort? ui = null)
    {
        Profile = profile;
        Composition = new UltraSimGameComposition(runtimeProfile: profile);
        EcsWorldOptions world = UltraSimGameComposition.CreateWorldOptions(
            entityCapacity,
            new PersistenceIdentityOptions(new PersistentWorldId(worldIdentity)),
            profile);
        Runtime = new ServerRuntime(Composition, new ServerRuntimeOptions(world, WorldClockOptions.Default));
        Runtime.Initialize();
        Runtime.Start();
        Admission = new AdmissionService(Runtime.Game, Composition, ui);
    }

    internal GameRuntimeProfile Profile { get; }
    internal UltraSimGameComposition Composition { get; }
    internal ServerRuntime Runtime { get; }
    internal AdmissionService Admission { get; }

    internal GameProjectionService CreateProjection() =>
        new(Runtime.Game, Admission,
            new GameReplicationLimits(RuntimeOptions.MaximumEntities, 8, 128, 256, 256, 64, 64));

    private EntityCapacity RuntimeOptions => Runtime.Game.TryUseActiveGeneration(
        static (world, _) => world.Options.EntityCapacity,
        out EntityCapacity capacity) ? capacity : throw new InvalidOperationException("Runtime is not running.");

    public void Dispose()
    {
        Admission.Dispose();
        Runtime.Dispose();
    }
}

sealed class TestUiPort : IUiSemanticPort
{
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private ulong _nextView = 1;

    public UiViewInstanceId OpenView(UiOpenView operation) => new(_nextView++);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => new(_nextView++);
    public void UpdateView(UiUpdateView operation) { }
    public bool CloseView(UiCloseView operation) => true;
    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);
    internal void Submit(in UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? action)) action(request);
    }
}

struct CountingChangedVisitor : IReadOnlyChunkJob<GameSimulationState>
{
    public void Execute(in ReadOnlyComponentChunk<GameSimulationState> chunk)
    {
    }
}

static class Check
{
    internal static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    internal static void False(bool condition)
    {
        if (condition) throw new InvalidOperationException("Expected false.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void Throws<TException>(Action body) where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    internal static void Contains(ReadOnlySpan<GameProjectionDelta> values, GameProjectionDeltaKind kind)
    {
        foreach (ref readonly GameProjectionDelta value in values)
            if (value.Kind == kind) return;
        throw new InvalidOperationException($"Expected projection delta {kind}.");
    }
}
