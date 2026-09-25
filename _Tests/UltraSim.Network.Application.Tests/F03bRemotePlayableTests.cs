using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Client.StandardUi;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.Inspection;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Hosting;
using UltraSim.Server.ConceptDemo;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.UI;

internal static class F03bRemotePlayableTests
{
    internal const int F03bTestCount = 5;
    internal const int A03TestCount = 7;
    internal const int A04TestCount = 5;
    internal const int A05TestCount = 1;
    internal const int A06TestCount = 2;
    internal const int A07TestCount = 2;
    internal const int F12TestCount = 1;
    internal const int W7TestCount = 11;
    internal const int F15TestCount = 1;
    internal const int F04TestCount = 2;
    internal const int F05TestCount = 1;
    internal const int TestCount = F03bTestCount + A03TestCount + A04TestCount + A05TestCount +
        A06TestCount + A07TestCount + F04TestCount + F05TestCount + F12TestCount +
        F15TestCount + W7TestCount + 1;

    internal static void RunAll()
    {
        Run("RemotePlayableProtocolIsCanonicalAndTrustMinimal",
            RemotePlayableProtocolIsCanonicalAndTrustMinimal);
        Run("RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl",
            RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl);
        Run("RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi",
            RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi);
        Run("RemotePlayableReconnectRejectsPriorApplicationLineage",
            RemotePlayableReconnectRejectsPriorApplicationLineage);
        Run("OneShotUiRequestSurvivesClientQueueBackpressure",
            OneShotUiRequestSurvivesClientQueueBackpressure);
        Run("RequiredContentProtocolIsCanonicalAndBounded",
            RequiredContentProtocolIsCanonicalAndBounded);
        Run("W7ConceptDemoPacksAreIndependentExactPackagesOutsideBootstrap",
            W7ConceptDemoPacksAreIndependentExactPackagesOutsideBootstrap);
        Run("W7AuthenticatedManifestProofRetainsZeroPackComposition",
            W7AuthenticatedManifestProofRetainsZeroPackComposition);
        Run("W7ClientRejectsStaleContradictoryAndWrongManifestAdvertisements",
            W7ClientRejectsStaleContradictoryAndWrongManifestAdvertisements);
        Run("W7CrossLaneAdmissionWaitsForStructuralProof",
            W7CrossLaneAdmissionWaitsForStructuralProof);
        Run("W7PackGrantFollowsManifestAndExactRequestLineage",
            W7PackGrantFollowsManifestAndExactRequestLineage);
        Run("W7FarIslandDemandRetainsWestPreparedView",
            W7FarIslandDemandRetainsWestPreparedView);
        Run("W7ClientRejectsStaleAndContradictoryPackGrants",
            W7ClientRejectsStaleAndContradictoryPackGrants);
        Run("W7PendingPackAcquisitionCancelsWithSession",
            W7PendingPackAcquisitionCancelsWithSession);
        Run("W7FailedFarPackAcquisitionRollsBackWithoutFalseKnowledge",
            W7FailedFarPackAcquisitionRollsBackWithoutFalseKnowledge);
        Run("W7ActualFarGateAndSeamTraversal",
            W7ActualFarGateAndSeamTraversal);
        Run("W7UnsupportedStructuralOverlayFailsClosed",
            W7UnsupportedStructuralOverlayFailsClosed);
        Run("F12SemanticEntryPrecedesOptionalActorPresentation",
            F12SemanticEntryPrecedesOptionalActorPresentation);
        Run("F15UserPresentationChangesNeverChangeSemanticActivation",
            F15UserPresentationChangesNeverChangeSemanticActivation);
        Run("ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle",
            ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle);
        Run("ContentAcquisitionRunsOutsideProtocolPumpAndCancelsWithSession",
            ContentAcquisitionRunsOutsideProtocolPumpAndCancelsWithSession);
        Run("WarmCacheReconnectReusesBytesButProvesFreshSession",
            WarmCacheReconnectReusesBytesButProvesFreshSession);
        Run("FailedContentAcquisitionCannotUnlockGuestSemantics",
            FailedContentAcquisitionCannotUnlockGuestSemantics);
        Run("PriorSessionContentProofCannotUnlockReconnect",
            PriorSessionContentProofCannotUnlockReconnect);
        Run("MismatchedSourceCandidateCannotPublish",
            MismatchedSourceCandidateCannotPublish);
        Run("RemoteDefaultTransfersNothingAndPreservesGameSemantics",
            RemoteDefaultTransfersNothingAndPreservesGameSemantics);
        Run("RemoteReplaceOverServerHttpMatchesLocalOracle",
            RemoteReplaceOverServerHttpMatchesLocalOracle);
        Run("RemoteReplaceOverExternalHttpsMatchesServerAndLocal",
            RemoteReplaceOverExternalHttpsMatchesServerAndLocal);
        Run("RemoteAddOverExternalHttpsMatchesLocalOracle",
            RemoteAddOverExternalHttpsMatchesLocalOracle);
        Run("HttpSourcePolicyAndEnvelopeFailureStayFailClosed",
            HttpSourcePolicyAndEnvelopeFailureStayFailClosed);
        Run("RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs",
            RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs);
        Run("UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps",
            UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps);
        Run("ContentBackedRemoteInventoryUsesHotOpenAndGameActions",
            ContentBackedRemoteInventoryUsesHotOpenAndGameActions);
        Run("StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority",
            StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority);
        Run("StreamedWorldPackCapacityFailurePublishesNothing",
            StreamedWorldPackCapacityFailurePublishesNothing);
        Run("PublicPresentationMetadataProtocolIsCanonicalAndBounded",
            PublicPresentationMetadataProtocolIsCanonicalAndBounded);
        Run("RemoteGuestPublishesColdPresentationMetadataSeparatelyFromMovement",
            RemoteGuestPublishesColdPresentationMetadataSeparatelyFromMovement);
        Run("PreinstalledStandardInventoryConvergesOnRemoteHotOpenAndGameAction",
            PreinstalledStandardInventoryConvergesOnRemoteHotOpenAndGameAction);
        Run("F18aPresentedMotionPrecedesImpairedOwnerState",
            F18aPresentedMotionPrecedesImpairedOwnerState);
    }

    private static void OneShotUiRequestSurvivesClientQueueBackpressure()
    {
        var requests = new RemotePlayableOneShotRequests();

        requests.RequestServerMonitor();
        Equal(RemotePlayableCommandFlags.OpenServerMonitorRequested, requests.Pending);
        requests.ObserveSubmission(submitted: false);
        Equal(RemotePlayableCommandFlags.OpenServerMonitorRequested, requests.Pending);
        requests.ObserveSubmission(submitted: true);
        Equal(RemotePlayableCommandFlags.None, requests.Pending);

        requests.RequestServerMonitor();
        requests.RequestInventory();
        Equal(RemotePlayableCommandFlags.OpenInventoryRequested, requests.Pending);
        requests.RequestServerMonitor();
        Equal(RemotePlayableCommandFlags.OpenServerMonitorRequested, requests.Pending);
    }

    private static void F18aPresentedMotionPrecedesImpairedOwnerState()
    {
        // The proxy sits outside the authenticated UDP endpoints. These measurements include
        // actual command submission, Game authority and owner-state delivery, not a mock ACK.
        MeasureMotion(oneWayDelayMilliseconds: 0, jitterMilliseconds: 0, dropPeriod: 0);
        MeasureMotion(oneWayDelayMilliseconds: 50, jitterMilliseconds: 0, dropPeriod: 0);
        MeasureMotion(oneWayDelayMilliseconds: 50, jitterMilliseconds: 15, dropPeriod: 0);
        MeasureMotion(oneWayDelayMilliseconds: 50, jitterMilliseconds: 15, dropPeriod: 53);
    }

    private static void MeasureMotion(int oneWayDelayMilliseconds,
        int jitterMilliseconds, int dropPeriod)
    {
        using var fixture = RemotePlayableFixture.CreateMotionImpaired(
            oneWayDelayMilliseconds, jitterMilliseconds, dropPeriod);
        fixture.Admit("Temporal Guest", GameGuestBody.HumanFemale);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().StateSequence != 0);
        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.Character, out _));
        RemotePlayableClientSnapshot initial = fixture.Playable.GetSnapshot();
        WorldPosition initialPosition = initial.Position;
        var temporal = new ClientControlledMotionPresentation();
        var publicInterpolation = new ClientRemoteActorInterpolation();
        long started = MonotonicMicroseconds();
        temporal.ObserveOwner(initial.ApplicationSessionEpoch, initial.ControlSession,
            initial.ServerGenerationId, initial.PublicActor, initial.StateSequence,
            initial.LastProcessedCommandSequence, initial.Position, initial.Orientation,
            false, started);
        var direct = new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None, true);
        True(fixture.Playable.TrySubmitCommand(in direct, default,
            RemotePlayableCommandFlags.None, 0, 0, out ulong moveSequence));
        True(temporal.ObserveAdmittedCommand(moveSequence, direct.Locomotion, started));
        long firstPresented = -1;
        long firstOwner = -1;
        long errorAtFirstOwnerMillimeters = -1;
        ulong lastOwnerSequence = initial.StateSequence;
        long lastOwnerArrival = started;
        long lastOwnerX = initialPosition.GlobalXMillimeters;
        long maximumOwnerGap = 0;
        long maximumOwnerStep = 0;
        int ownerUpdates = 0;
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 4_000 && firstOwner < 0)
        {
            fixture.DriveSteps(1);
            long now = MonotonicMicroseconds();
            RemotePlayableClientSnapshot owner = fixture.Playable.GetSnapshot();
            if (owner.StateSequence > lastOwnerSequence)
            {
                maximumOwnerGap = Math.Max(maximumOwnerGap, now - lastOwnerArrival);
                maximumOwnerStep = Math.Max(maximumOwnerStep,
                    Math.Abs(owner.Position.GlobalXMillimeters - lastOwnerX));
                lastOwnerArrival = now;
                lastOwnerX = owner.Position.GlobalXMillimeters;
                ownerUpdates++;
                temporal.ObserveOwner(owner.ApplicationSessionEpoch, owner.ControlSession,
                    owner.ServerGenerationId, owner.PublicActor, owner.StateSequence,
                    owner.LastProcessedCommandSequence, owner.Position, owner.Orientation,
                    owner.Locomotion == RemotePlayableActionStatus.Blocked, now);
                lastOwnerSequence = owner.StateSequence;
            }
            ClientResolvedMotionPose presented = temporal.Sample(now);
            if (fixture.TryFindPublic(GameWorldPublicPrefabIds.Character,
                    out WorldPublicRecord publicActor) &&
                publicActor.Id == initial.PublicActor)
            {
                _ = publicInterpolation.Observe(owner.ServerGenerationId, publicActor.Id,
                    publicActor.EntityPublicVersion,
                    ClientMotionPose.FromAuthority(publicActor.Position,
                        publicActor.Orientation), now);
                _ = publicInterpolation.TrySample(publicActor.Id, now, out _);
            }
            if (firstPresented < 0 &&
                presented.Pose.GlobalXMillimeters - initialPosition.GlobalXMillimeters >= 20)
                firstPresented = now - started;
            if (owner.Position.GlobalXMillimeters - initialPosition.GlobalXMillimeters >= 20)
            {
                firstOwner = now - started;
                errorAtFirstOwnerMillimeters = ClientMotionPose.HorizontalDistanceMillimeters(
                    presented.Pose,
                    ClientMotionPose.FromAuthority(owner.Position, owner.Orientation));
            }
        }
        True(firstPresented >= 0 && firstOwner >= 0);
        if (oneWayDelayMilliseconds >= 50)
            True(firstPresented + 40_000 < firstOwner);

        // Continue past the interpolation delay: the first owner round trip is shorter than
        // the deliberately delayed public-pose presentation window on the zero-delay control.
        var publicWindow = Stopwatch.StartNew();
        while (publicWindow.ElapsedMilliseconds < 1_500)
        {
            fixture.DriveSteps(1);
            long now = MonotonicMicroseconds();
            RemotePlayableClientSnapshot owner = fixture.Playable.GetSnapshot();
            if (owner.StateSequence > lastOwnerSequence)
            {
                maximumOwnerGap = Math.Max(maximumOwnerGap, now - lastOwnerArrival);
                maximumOwnerStep = Math.Max(maximumOwnerStep,
                    Math.Abs(owner.Position.GlobalXMillimeters - lastOwnerX));
                lastOwnerArrival = now;
                lastOwnerX = owner.Position.GlobalXMillimeters;
                ownerUpdates++;
                temporal.ObserveOwner(owner.ApplicationSessionEpoch, owner.ControlSession,
                    owner.ServerGenerationId, owner.PublicActor, owner.StateSequence,
                    owner.LastProcessedCommandSequence, owner.Position, owner.Orientation,
                    owner.Locomotion == RemotePlayableActionStatus.Blocked, now);
                lastOwnerSequence = owner.StateSequence;
            }
            _ = temporal.Sample(now);
            if (!fixture.TryFindPublic(GameWorldPublicPrefabIds.Character,
                    out WorldPublicRecord publicActor) ||
                publicActor.Id != initial.PublicActor)
                continue;
            _ = publicInterpolation.Observe(initial.ServerGenerationId, publicActor.Id,
                publicActor.EntityPublicVersion,
                ClientMotionPose.FromAuthority(publicActor.Position,
                    publicActor.Orientation), now);
            _ = publicInterpolation.TrySample(publicActor.Id, now, out _);
        }
        long steadyCorrectionMax =
            temporal.GetSnapshot().MaximumCorrectionErrorMillimeters;

        bool reversalBeforeOwner = false;
        if (oneWayDelayMilliseconds == 50 && jitterMilliseconds == 0)
        {
            fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
            var reverse = new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(-GamePlayerCommand.MaximumAxisMagnitude, 0),
                GamePlayerActions.None, true);
            long reverseAccepted = MonotonicMicroseconds();
            long beforeReverse = temporal.Sample(reverseAccepted).Pose.GlobalXMillimeters;
            ulong ownerBeforeReverse = fixture.Playable.GetSnapshot().StateSequence;
            True(fixture.Playable.TrySubmitCommand(in reverse, default,
                RemotePlayableCommandFlags.None, 0, 0, out ulong reverseSequence));
            True(temporal.ObserveAdmittedCommand(reverseSequence, reverse.Locomotion,
                reverseAccepted));
            Thread.Sleep(25); // No Network pump: this is a deliberate local-before-RTT observation.
            long afterReverse = temporal.Sample(MonotonicMicroseconds()).Pose.GlobalXMillimeters;
            reversalBeforeOwner = afterReverse < beforeReverse &&
                fixture.Playable.GetSnapshot().StateSequence == ownerBeforeReverse;
            True(reversalBeforeOwner);
            _ = DriveMotionUntilAcknowledged(fixture, temporal,
                ref lastOwnerSequence, reverseSequence);
        }
        long reversalCorrectionMax =
            temporal.GetSnapshot().MaximumCorrectionErrorMillimeters;

        var stop = new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true);
        fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
        True(fixture.Playable.TrySubmitCommand(in stop, default,
            RemotePlayableCommandFlags.None, 0, 0, out ulong stopSequence));
        True(temporal.ObserveAdmittedCommand(stopSequence, stop.Locomotion,
            MonotonicMicroseconds()));
        RemotePlayableClientSnapshot finalOwner = DriveMotionUntilAcknowledged(
            fixture, temporal, ref lastOwnerSequence, stopSequence);
        long stopAcknowledgedAt = MonotonicMicroseconds();
        long errorAtStopAcknowledgementMillimeters =
            ClientMotionPose.HorizontalDistanceMillimeters(
                temporal.Sample(stopAcknowledgedAt).Pose,
                ClientMotionPose.FromAuthority(finalOwner.Position, finalOwner.Orientation));
        long settleMicroseconds;
        while (true)
        {
            long now = MonotonicMicroseconds();
            long error = ClientMotionPose.HorizontalDistanceMillimeters(
                temporal.Sample(now).Pose,
                ClientMotionPose.FromAuthority(finalOwner.Position, finalOwner.Orientation));
            if (error <= ClientControlledMotionPresentation.CorrectionDeadbandMillimeters)
            {
                settleMicroseconds = now - stopAcknowledgedAt;
                break;
            }
            True(now - stopAcknowledgedAt <= 500_000);
            Thread.Sleep(2);
        }
        long remainingCorrectionMicroseconds =
            (ClientControlledMotionPresentation.CorrectionDurationMilliseconds + 2) * 1_000L -
            (MonotonicMicroseconds() - stopAcknowledgedAt);
        if (remainingCorrectionMicroseconds > 0)
            Thread.Sleep((int)Math.Ceiling(remainingCorrectionMicroseconds / 1_000d));
        ClientResolvedMotionPose finalPresented = temporal.Sample(MonotonicMicroseconds());
        Equal(finalOwner.Position.GlobalXMillimeters,
            finalPresented.Pose.GlobalXMillimeters);
        Equal(finalOwner.Position.GlobalZMillimeters,
            finalPresented.Pose.GlobalZMillimeters);
        long stopCorrectionMax =
            temporal.GetSnapshot().MaximumCorrectionErrorMillimeters;
        bool destinationBeforeOwner = false;
        if (oneWayDelayMilliseconds == 50 && jitterMilliseconds == 0)
        {
            fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
            var destination = new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDestination(
                    finalOwner.Position.GlobalXMillimeters + 1_500,
                    finalOwner.Position.GlobalZMillimeters), GamePlayerActions.None, true);
            long destinationAccepted = MonotonicMicroseconds();
            ulong ownerBeforeDestination = fixture.Playable.GetSnapshot().StateSequence;
            True(fixture.Playable.TrySubmitCommand(in destination, default,
                RemotePlayableCommandFlags.None, 0, 0, out ulong destinationSequence));
            True(temporal.ObserveAdmittedCommand(destinationSequence,
                destination.Locomotion, destinationAccepted));
            Thread.Sleep(25);
            ClientResolvedMotionPose destinationPresentation =
                temporal.Sample(MonotonicMicroseconds());
            destinationBeforeOwner =
                destinationPresentation.Pose.GlobalXMillimeters >
                    finalOwner.Position.GlobalXMillimeters &&
                fixture.Playable.GetSnapshot().StateSequence == ownerBeforeDestination;
            True(destinationBeforeOwner);
            RemotePlayableClientSnapshot destinationOwner = DriveMotionUntilAcknowledged(
                fixture, temporal, ref lastOwnerSequence, destinationSequence);
            True(temporal.Sample(MonotonicMicroseconds()).Pose.GlobalXMillimeters -
                destinationOwner.Position.GlobalXMillimeters <=
                ClientControlledMotionPolicy.ConceptDemo.MaximumLeadMillimeters);
            fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
            True(fixture.Playable.TrySubmitCommand(in stop, default,
                RemotePlayableCommandFlags.None, 0, 0, out ulong destinationStopSequence));
            _ = DriveMotionUntilAcknowledged(fixture, temporal,
                ref lastOwnerSequence, destinationStopSequence);
        }
        ClientControlledMotionSnapshot metrics = temporal.GetSnapshot();
        ClientRemoteActorInterpolationSnapshot publicMetrics =
            publicInterpolation.GetSnapshot();
        True(publicMetrics.SamplesReceived >= 2);
        True(publicMetrics.InterpolatedFrames > 0);
        Console.WriteLine($"EVIDENCE F1.8A UDP one_way_ms={oneWayDelayMilliseconds} " +
            $"jitter_ms={jitterMilliseconds} drop_period={dropPeriod} " +
            $"input_to_present_us={firstPresented} input_to_owner_us={firstOwner} " +
            $"error_at_first_owner_mm={errorAtFirstOwnerMillimeters} " +
            $"error_at_stop_ack_mm={errorAtStopAcknowledgementMillimeters} " +
            $"stop_settle_to_20mm_us={settleMicroseconds} " +
            $"lead_max_mm={metrics.MaximumSpeculativeLeadMillimeters} " +
            $"smooth={metrics.SmoothCorrections} snaps={metrics.SnapCorrections} " +
            $"correction_max_mm={metrics.MaximumCorrectionErrorMillimeters} " +
            $"correction_steady_reverse_stop_mm={steadyCorrectionMax}/" +
            $"{reversalCorrectionMax}/{stopCorrectionMax} " +
            $"correction_mean_mm={metrics.MeanCorrectionErrorMillimeters} " +
            $"clamps={metrics.PredictionClamps} history_high={metrics.HistoryHighWater} " +
            $"owner_updates={ownerUpdates} owner_gap_max_us={maximumOwnerGap} " +
            $"owner_step_max_mm={maximumOwnerStep} " +
            $"public_samples={publicMetrics.SamplesReceived} " +
            $"interpolated_frames={publicMetrics.InterpolatedFrames} " +
            $"held_frames={publicMetrics.HeldFrames} gap_resets={publicMetrics.GapResets} " +
            $"public_age_max_us={publicMetrics.MaximumNewestSampleAgeMicroseconds} " +
            $"reversal_before_owner={reversalBeforeOwner} " +
            $"destination_before_owner={destinationBeforeOwner} " +
            $"proxy_forwarded={fixture.MotionForwarded} " +
            $"proxy_dropped={fixture.MotionDropped}");
        if (dropPeriod != 0) True(fixture.MotionDropped > 0);
    }

    private static RemotePlayableClientSnapshot DriveMotionUntilAcknowledged(
        RemotePlayableFixture fixture, ClientControlledMotionPresentation temporal,
        ref ulong lastOwnerSequence, ulong commandSequence)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 5_000)
        {
            fixture.DriveSteps(1);
            long now = MonotonicMicroseconds();
            RemotePlayableClientSnapshot owner = fixture.Playable.GetSnapshot();
            if (owner.StateSequence > lastOwnerSequence)
            {
                temporal.ObserveOwner(owner.ApplicationSessionEpoch, owner.ControlSession,
                    owner.ServerGenerationId, owner.PublicActor, owner.StateSequence,
                    owner.LastProcessedCommandSequence, owner.Position, owner.Orientation,
                    owner.Locomotion == RemotePlayableActionStatus.Blocked, now);
                lastOwnerSequence = owner.StateSequence;
            }
            _ = temporal.Sample(now);
            if (owner.LastProcessedCommandSequence >= commandSequence) return owner;
        }
        throw new InvalidOperationException(
            $"F1.8A command {commandSequence} did not receive an owner acknowledgement.");
    }

    private static long MonotonicMicroseconds() =>
        checked((long)((Int128)Stopwatch.GetTimestamp() * 1_000_000 / Stopwatch.Frequency));

    private static void PreinstalledStandardInventoryConvergesOnRemoteHotOpenAndGameAction()
    {
        CompiledUiDefinition standard = ClientStandardUiLibrary.InventoryDefinition;
        Equal(InventoryUi.Definition.Key, standard.Key);
        Equal(InventoryUi.Definition.Version, standard.Version);
        Equal(InventoryUi.Definition.Hash, standard.Hash);
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(standard, out byte[] standardBytes));
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(
            InventoryUi.Definition, out byte[] authoredBytes));
        True(standardBytes.AsSpan().SequenceEqual(authoredBytes));

        using var remote = RemotePlayableFixture.CreateWithStandardUi();
        Equal(0, remote.UiClient.CachedDefinitionCount);
        remote.Admit("Standard UI Guest", GameGuestBody.HumanFemale);
        True(remote.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        remote.DriveUntil(() => remote.UiClient.OpenViewCount == 1);
        RemoteUiClientSnapshot ui = remote.SessionSnapshot.Ui;
        Equal(1UL, ui.StandardDefinitionsResolved);
        Equal(0UL, ui.ContentDefinitionsResolved);
        Equal(1, remote.UiClient.CachedDefinitionCount);

        var view = new UiViewInstanceId(1);
        ReadOnlySpan<UiListRow> rows = remote.UiClient.GetRows(view, InventoryUi.Items);
        True(rows.Length != 0);
        ulong before = remote.UiClient.GetView(view).LastSequence;
        remote.UiClient.SubmitAction(view, InventoryUi.Select, rows[0].Id);
        remote.DriveUntil(() => remote.UiClient.GetView(view).LastSequence > before);
        Equal("Item selected.", remote.UiClient.GetValue(view, InventoryUi.Status).Text);
        Console.WriteLine(
            $"EVIDENCE F0.5 remote_standard bytes={standardBytes.Length} " +
            $"definition={standard.Key}/v{standard.Version} hash={standard.Hash} " +
            $"standard_resolutions={ui.StandardDefinitionsResolved} " +
            $"content_resolutions={ui.ContentDefinitionsResolved} " +
            $"action_model_sequence={remote.UiClient.GetView(view).LastSequence}");
    }

    private static void PublicPresentationMetadataProtocolIsCanonicalAndBounded()
    {
        var header = new RemotePublicPresentationMetadataHeader
        {
            ApplicationSessionEpoch = 11,
            ServerGenerationId = 12,
            DeliveryId = 13,
            MetadataRevision = 1,
            PublicEntityIndex = 7,
            PublicEntityGeneration = 3,
            Operation = RemotePublicPresentationOperation.Present,
            Flags = RemotePublicPresentationFlags.NameplateEligible,
        };
        var bytes = new byte[UltraSimGameProtocol.MaximumPublicPresentationMessageBytes + 1];
        True(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "Remote Éowyn", null, out int written));
        True(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out RemotePublicPresentationMetadataHeader decoded,
            out string label, out InspectionPropertySet? decodedProperties));
        Equal(header, decoded);
        Equal("Remote Éowyn", label);
        True(decodedProperties is null);
        bytes[written] = 0;
        False(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written + 1), out _, out _, out _));
        False(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, " bad", null, out _));
        False(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "bad\uD800", null, out _));

        header.Flags = RemotePublicPresentationFlags.NameplateEligible |
            RemotePublicPresentationFlags.ActorState;
        header.MainHandPrefabId = GameWorldPublicPrefabIds.IronSword;
        header.VerticalPhase = RemotePublicActorVerticalPhase.Grounded;
        header.ActorAction = RemotePublicActorActionKind.Primary;
        header.ActorActionSequence = 41;
        True(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "Remote Éowyn", null, out written));
        True(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out decoded, out label, out decodedProperties));
        Equal(header, decoded);
        header.ActorActionSequence = 0;
        False(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "Remote Éowyn", null, out _));
        header.ActorActionSequence = 41;

        header.Flags = RemotePublicPresentationFlags.ItemProperties;
        header.MainHandPrefabId = 0;
        header.VerticalPhase = RemotePublicActorVerticalPhase.None;
        header.ActorAction = RemotePublicActorActionKind.None;
        header.ActorActionSequence = 0;
        InspectionPropertySet properties = GameItemGenerationState.BuildInspectionProperties(
            new GameWeaponInstance
            {
                DamageMinimum = 3,
                DamageMaximum = 8,
                CurrentDurability = 45,
                MaximumDurability = 45,
                Quality = 61,
                Element = GameWeaponElement.Fire,
                SpawnOrdinal = 1,
            });
        True(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "Iron Sword", properties, out written));
        True(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out decoded, out label, out decodedProperties));
        Equal("Iron Sword", label);
        True(properties.Equals(decodedProperties));
        True(InspectionPresentationDefaults.TryFormat(
            label, decodedProperties, out InspectionPresentation? presentation));
        True(presentation!.DetailText.Contains("Damage 3–8", StringComparison.Ordinal));
        InspectionPropertyFact[] reorderedFacts = properties.Facts.ToArray();
        Array.Reverse(reorderedFacts);
        True(InspectionPropertySet.TryCreate(reorderedFacts, out InspectionPropertySet? reordered));
        var reorderedBytes = new byte[bytes.Length];
        True(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            reorderedBytes, in header, "Iron Sword", reordered, out int reorderedWritten));
        Equal(written, reorderedWritten);
        True(bytes.AsSpan(0, written).SequenceEqual(reorderedBytes.AsSpan(0, reorderedWritten)));
        False(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "Iron Sword", null, out _));
        int firstKindOffset = RemotePublicPresentationMetadataHeaderCodec.EncodedSize +
            sizeof(byte) + System.Text.Encoding.UTF8.GetByteCount("Iron Sword") +
            sizeof(byte) + sizeof(ushort);
        byte originalKind = bytes[firstKindOffset];
        bytes[firstKindOffset] = byte.MaxValue;
        False(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out _, out _, out _));
        bytes[firstKindOffset] = originalKind;
        int secondIdOffset = firstKindOffset + sizeof(byte) + sizeof(long) * 2;
        byte secondIdHigh = bytes[secondIdOffset];
        byte secondIdLow = bytes[secondIdOffset + 1];
        bytes[secondIdOffset] = 0;
        bytes[secondIdOffset + 1] = 1;
        False(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out _, out _, out _));
        bytes[secondIdOffset] = secondIdHigh;
        bytes[secondIdOffset + 1] = secondIdLow;

        header.Operation = RemotePublicPresentationOperation.Remove;
        header.Flags = RemotePublicPresentationFlags.None;
        True(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, string.Empty, null, out written));
        True(UltraSimGameProtocol.TryReadPublicPresentationMetadata(
            bytes.AsSpan(0, written), out decoded, out label, out decodedProperties));
        Equal(string.Empty, label);
        True(decodedProperties is null);
        False(UltraSimGameProtocol.TryWritePublicPresentationMetadata(
            bytes, in header, "must be empty", null, out _));

        var proof = new RemotePublicPresentationAppliedMessage
        {
            ApplicationSessionEpoch = 11,
            ServerGenerationId = 12,
            DeliveryId = 13,
            Status = RemotePublicPresentationApplyStatus.Applied,
        };
        True(UltraSimGameProtocol.IsValidPublicPresentationApplied(in proof));
        proof.Status = 0;
        False(UltraSimGameProtocol.IsValidPublicPresentationApplied(in proof));
        Equal(NetworkLane.Control,
            RemotePublicPresentationMetadataHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control,
            RemotePublicPresentationAppliedMessageCodec.Descriptor.Lane);
    }

    private static void RemoteGuestPublishesColdPresentationMetadataSeparatelyFromMovement()
    {
        using var fixture = RemotePlayableFixture.Create();
        fixture.Admit("Remote Bob", GameGuestBody.HumanMale);
        RemotePlayableClientSnapshot admitted = fixture.Playable.GetSnapshot();
        fixture.DriveUntil(() => fixture.PublicPresentationState.TryGet(
            admitted.PublicActor, out _));

        True(fixture.PublicPresentationState.TryGet(
            admitted.PublicActor, out ClientPublicPresentationMetadata metadata));
        Equal("Remote Bob", metadata.DisplayLabel);
        Equal(admitted.ServerGenerationId, metadata.ServerGenerationId);
        Equal(ClientPublicPresentationFlags.NameplateEligible, metadata.Flags);
        RemotePublicPresentationMetadataClientSnapshot client =
            fixture.SessionSnapshot.PublicPresentation;
        True(client.Applied >= 1);
        True(client.ProofsQueued >= 1);

        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.CharacterMale, out _));
        True(fixture.TryFindPublic(
            GameWorldPublicPrefabIds.CharacterMale, out WorldPublicRecord moving));
        Equal(admitted.PublicActor, moving.Id);
        True(fixture.Chat.TrySubmit("anchored speech", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied != 0);
        var rows = new ClientChatOccurrence[1];
        Equal(1, fixture.ChatState.CopyRecent(rows));
        Equal(admitted.PublicActor, rows[0].PublicAnchor);

        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword) &&
            fixture.PublicPresentationState.TryGet(sword.Id, out _));
        True(fixture.TryFindPublic(
            GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord publicSword));
        Equal(
            WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem,
            publicSword.StateFlags);
        True(fixture.PublicPresentationState.TryGet(
            publicSword.Id, out ClientPublicPresentationMetadata item));
        Equal(ClientPublicPresentationFlags.ItemProperties, item.Flags);
        Equal("Iron Sword", item.DisplayLabel);
        True(InspectionPresentationDefaults.TryFormat(
            item.DisplayLabel,
            item.InspectionProperties,
            out InspectionPresentation? itemPresentation));
        True(itemPresentation!.DetailText.Contains("Damage ", StringComparison.Ordinal));
        True(itemPresentation.DetailText.Contains("Quality ", StringComparison.Ordinal));
        True(itemPresentation.DetailText.Contains("Durability ", StringComparison.Ordinal));
    }

    private static void RemotePlayableProtocolIsCanonicalAndTrustMinimal()
    {
        var requested = new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Remote Alice");
        var encoded = new byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
        True(UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
            encoded, 11, 1, in requested, out int written));
        True(UltraSimGameProtocol.TryReadGuestAdmissionRequest(
            encoded.AsSpan(0, written), out RemoteGuestAdmissionRequestHeader header,
            out GameGuestAdmissionRequest decoded));
        Equal(11UL, header.ApplicationSessionEpoch);
        Equal(1UL, header.AttemptSequence);
        Equal(requested, decoded);
        encoded[written] = 0;
        False(UltraSimGameProtocol.TryReadGuestAdmissionRequest(
            encoded.AsSpan(0, written + 1), out _, out _));

        var command = new GamePlayerCommand(
            new GameControlSessionEpoch(22), 1,
            GameLocomotionUpdate.CreateDirect(1_000, -1_000),
            GamePlayerActions.Interact,
            GamePlayerActions.Interact);
        RemotePlayableCommandMessage remote = UltraSimGameProtocol.CreatePlayableCommand(
            11, in command, new RemotePublicEntityTarget(7, 3),
            RemotePlayableCommandFlags.FaceRequested, 100, 200);
        True(UltraSimGameProtocol.TryCreatePlayableCommand(
            in remote, out GamePlayerCommand decodedCommand,
            out RemotePublicEntityTarget decodedTarget));
        Equal(command, decodedCommand);
        Equal(new RemotePublicEntityTarget(7, 3), decodedTarget);

        RemotePlayableCommandMessage malformed = remote;
        malformed.TargetPublicEntityGeneration = 0;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));
        malformed = remote;
        malformed.Flags = (RemotePlayableCommandFlags)0x80;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));
        malformed = remote;
        malformed.Flags = RemotePlayableCommandFlags.OpenInventoryRequested |
            RemotePlayableCommandFlags.OpenServerMonitorRequested;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));
        malformed = remote;
        malformed.Flags = RemotePlayableCommandFlags.None;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));

        string[] ingressFields = typeof(RemoteGuestAdmissionRequestHeader)
            .GetFields()
            .Select(static field => field.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Equal("ApplicationSessionEpoch,AttemptSequence,Body", string.Join(',', ingressFields));
        Equal(NetworkLane.Event, RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Event, RemoteGuestAdmissionResultHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control, RemoteGuestAdmissionAppliedMessageCodec.Descriptor.Lane);
        Equal(NetworkLane.Event, RemotePlayableCommandMessageCodec.Descriptor.Lane);
        Equal(NetworkLane.OwnerState, RemotePlayableOwnerStateMessageCodec.Descriptor.Lane);
    }

    private static void RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl()
    {
        using var fixture = RemotePlayableFixture.Create();
        True(fixture.Playable.TrySubmitGuest(new GameGuestAdmissionRequest(
            GameGuestBody.HumanFemale, "invalid\u0001name")));
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().State == RemotePlayableClientState.Rejected);
        RemotePlayableClientSnapshot rejected = fixture.Playable.GetSnapshot();
        False(rejected.Participant.IsValid);
        False(rejected.ControlSession.IsValid);
        False(rejected.PublicActor.IsValid);
        True(rejected.Detail?.Contains("control", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi()
    {
        using var fixture = RemotePlayableFixture.Create();
        fixture.Admit("Remote Alice", GameGuestBody.HumanFemale);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().StateSequence != 0);
        True(fixture.SessionSnapshot.SemanticConsumersActive);
        Equal(0UL, fixture.SessionSnapshot.RejectedPreAdmissionMessages);

        RemotePlayableClientSnapshot admitted = fixture.Playable.GetSnapshot();
        Equal("Remote Alice", admitted.GuestIdentity.DisplayName);
        Equal(GameGuestBody.HumanFemale, admitted.GuestIdentity.Body);
        True(admitted.Participant.IsValid);
        True(admitted.ControlSession.IsValid);
        True(admitted.PublicActor.IsValid);

        ulong beforeInvalidClientIntent = fixture.Playable.Input.Snapshot().LastSequence;
        False(fixture.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
            flags: (RemotePlayableCommandFlags)0x80));
        Equal(beforeInvalidClientIntent, fixture.Playable.Input.Snapshot().LastSequence);

        fixture.DriveUntil(() =>
            fixture.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.CabinDoor, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.WorldChest, out _));
        Equal(0UL, fixture.World.GetSnapshot().RejectedDeliveries);

        WorldPosition before = admitted.Position;
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None,
            true)));
        ulong movementSequence = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() =>
        {
            RemotePlayableClientSnapshot current = fixture.Playable.GetSnapshot();
            return current.LastProcessedCommandSequence >= movementSequence &&
                current.Position.GlobalXMillimeters > before.GlobalXMillimeters;
        });

        True(fixture.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        ulong inventorySequence = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() => fixture.UiClient.OpenViewCount == 1 &&
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence >= inventorySequence);

        // Replaying the exact retained Event must not execute the command twice or open a second
        // owner-private view. Packet reliability is not the authority fence.
        var duplicateCommand = new GamePlayerCommand(
            admitted.ControlSession,
            inventorySequence,
            GameLocomotionUpdate.Stop,
            GamePlayerActions.None,
            GamePlayerActions.None);
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch,
            in duplicateCommand,
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        fixture.DriveSteps(256);
        Equal(1, fixture.UiClient.OpenViewCount);

        True(fixture.Chat.TrySubmit("remote hello", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 1);
        True(fixture.Chat.TrySubmit("/where", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 2);
        var rows = new ClientChatOccurrence[2];
        Equal(2, fixture.ChatState.CopyRecent(rows));
        Equal(ClientChatOccurrenceKind.SpatialSpeech, rows[0].Kind);
        Equal("Remote Alice", rows[0].DisplayLabel);
        Equal("remote hello", rows[0].Text);
        Equal(ClientChatOccurrenceKind.PrivateSystem, rows[1].Kind);
        True(rows[1].Text.StartsWith("Where:", StringComparison.Ordinal));
        Equal("System", rows[1].DisplayLabel);

        // A stale application epoch is rejected before it can advance the authoritative command
        // fence. The next legitimate command may reuse that numeric sequence in this session.
        ulong nextSequence = fixture.Playable.Input.Snapshot().LastSequence + 1;
        var stale = new GamePlayerCommand(
            admitted.ControlSession,
            nextSequence,
            GameLocomotionUpdate.Unchanged,
            GamePlayerActions.None,
            GamePlayerActions.None);
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch + 1, in stale));
        var future = stale with { Sequence = nextSequence + 100 };
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch, in future));
        fixture.DriveSteps(128);
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true)));
        Equal(nextSequence, fixture.Playable.Input.Snapshot().LastSequence);
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence == nextSequence);
    }

    private static void RemotePlayableReconnectRejectsPriorApplicationLineage()
    {
        ulong oldApplication;
        GameControlSessionEpoch oldControl;
        using (var old = RemotePlayableFixture.Create())
        {
            old.Admit("First Guest", GameGuestBody.HumanMale);
            RemotePlayableClientSnapshot snapshot = old.Playable.GetSnapshot();
            oldApplication = snapshot.ApplicationSessionEpoch;
            oldControl = snapshot.ControlSession;
        }

        using var current = RemotePlayableFixture.Create();
        current.Admit("Second Guest", GameGuestBody.HumanFemale);
        RemotePlayableClientSnapshot fresh = current.Playable.GetSnapshot();
        True(fresh.ApplicationSessionEpoch != oldApplication);
        True(fresh.ControlSession != oldControl);

        var obsolete = new GamePlayerCommand(
            oldControl,
            1,
            GameLocomotionUpdate.Stop,
            GamePlayerActions.None,
            GamePlayerActions.None);
        current.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            oldApplication, in obsolete));
        current.DriveSteps(128);

        True(current.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true)));
        current.DriveUntil(() =>
            current.Playable.GetSnapshot().LastProcessedCommandSequence == 1);
    }

    private static void RequiredContentProtocolIsCanonicalAndBounded()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        ContentPackageAcquisition acquisition = RemoteReplaceAcquisition(oracle);
        RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [acquisition]);
        var requirement = new RemoteContentRequirement(77, 1, authorization);
        var bytes = new byte[UltraSimGameProtocol.MaximumContentRequirementMessageBytes];
        True(UltraSimGameProtocol.TryWriteContentRequirement(
            bytes, in requirement, out int written));
        True(UltraSimGameProtocol.TryReadContentRequirement(
            bytes.AsSpan(0, written), out RemoteContentRequirement decoded));
        Equal(77UL, decoded.ApplicationSessionEpoch);
        Equal(1UL, decoded.RequirementSequence);
        Equal(CabinContentOracle.FixtureRealm.High, decoded.Authorization.RealmHigh);
        Equal(oracle.Replace.Plan.Activation.High, decoded.Authorization.ActivationHigh);
        Equal(oracle.Replace.Plan.Packages[0].PackageId.Value,
            decoded.Authorization.Packages[0].PackageId);
        Equal(RemoteContentSourceKind.ExternalHttps,
            decoded.Authorization.Packages[0].Sources[0].Kind);
        bytes[written] = 0;
        False(UltraSimGameProtocol.TryReadContentRequirement(
            bytes.AsSpan(0, written + 1), out _));

        var presentation = new RemotePresentationAdvertisement(77, 1, authorization);
        True(UltraSimGameProtocol.TryWritePresentationAdvertisement(
            bytes, in presentation, out int presentationWritten));
        True(UltraSimGameProtocol.TryReadPresentationAdvertisement(
            bytes.AsSpan(0, presentationWritten), out RemotePresentationAdvertisement decodedPresentation));
        Equal(77UL, decodedPresentation.ApplicationSessionEpoch);
        Equal(authorization.PlanHash, decodedPresentation.Authorization.PlanHash);
        Equal(authorization.ExpectedResolvedCatalogFingerprint,
            decodedPresentation.Authorization.ExpectedResolvedCatalogFingerprint);
        byte[] malformedPresentation = bytes.AsSpan(0, presentationWritten).ToArray();
        malformedPresentation.AsSpan(presentationWritten -
            UltraSimGameProtocol.PresentationExpectedResultBytes).Clear();
        False(UltraSimGameProtocol.TryReadPresentationAdvertisement(
            malformedPresentation, out _));
        False(UltraSimGameProtocol.TryReadPresentationAdvertisement(
            bytes.AsSpan(0, presentationWritten + 1), out _));
        Equal(NetworkLane.Event, RemotePresentationAdvertisementHeaderCodec.Descriptor.Lane);

        False(UltraSimGameProtocol.IsValidContentApplied(default));
        RemoteContentHash plan = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Plan.PlanHash);
        RemoteContentHash resolved = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Catalog.Fingerprint);
        var proof = new RemoteContentSetAppliedMessage
        {
            ApplicationSessionEpoch = 77,
            RequirementSequence = 1,
            ApplicationRealmHigh = CabinContentOracle.FixtureRealm.High,
            ApplicationRealmLow = CabinContentOracle.FixtureRealm.Low,
            ActivationHigh = oracle.Replace.Plan.Activation.High,
            ActivationLow = oracle.Replace.Plan.Activation.Low,
            PlanHash0 = plan.Part0,
            PlanHash1 = plan.Part1,
            PlanHash2 = plan.Part2,
            PlanHash3 = plan.Part3,
            ResolvedHash0 = resolved.Part0,
            ResolvedHash1 = resolved.Part1,
            ResolvedHash2 = resolved.Part2,
            ResolvedHash3 = resolved.Part3,
            Status = RemoteContentApplyStatus.Applied,
        };
        True(UltraSimGameProtocol.IsValidContentApplied(in proof));
        Equal(NetworkLane.Event, RemoteContentRequirementHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control, RemoteContentSetAppliedMessageCodec.Descriptor.Lane);
        Equal((ushort)33, UltraSimGameProtocol.Manifest.Version);

        True(ContentActivationPlan.TryCreate(
            ContentScopeKey.Local, oracle.Replace.Plan.Activation,
            oracle.Replace.Plan.RequiredBaseCatalogFingerprint, oracle.Replace.Plan.Packages,
            out ContentActivationPlan? localPlan, out _));
        bool localRejected = false;
        try
        {
            _ = RemoteContentAuthorizationProjection.Create(
                localPlan!, oracle.Replace.Catalog.Fingerprint, [acquisition]);
        }
        catch (ArgumentException)
        {
            localRejected = true;
        }
        True(localRejected);
    }

    private static void W7AuthenticatedManifestProofRetainsZeroPackComposition()
    {
        ApplicationRealmId realm = new(0x5707, 0x200);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source, canonicalStructural: true);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralManifest.State ==
            RemoteStructuralManifestClientState.Applied);
        RemotePlayableSessionSnapshot session = fixture.SessionSnapshot;
        True(session.StructuralManifestRequired);
        Equal(deployment.Structural.Manifest.Fingerprint,
            session.StructuralManifest.ManifestFingerprint);
        Equal(new WorldCompositionRevision(1), session.StructuralManifest.Revision);
        Equal(2, session.StructuralManifest.ManifestEntryCount);
        True(session.StructuralManifest.PreparedPackCount is 0 or 1);
        Equal(ConceptDemoStructuralSource.WestCoord, session.StructuralManifest.InitialPack);
        True(session.StructuralManifest.ProofsQueued >= 1);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady &&
            fixture.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        session = fixture.SessionSnapshot;
        Equal(1, session.StructuralPacks.PreparedPackCount);
        Equal(deployment.Structural.Manifest.Fingerprint,
            session.StructuralWorld.ManifestFingerprint);
        Equal(new WorldCompositionRevision(1),
            session.StructuralWorld.WorldCompositionRevision);
        Equal(2, session.StructuralWorld.ManifestEntryCount);
        Equal(1, session.StructuralWorld.PreparedPackCount);
        True(session.StructuralWorld.InitialStructuralReady);
        False(session.StructuralWorld.WorldPackFingerprint.IsValid);
        True(session.StructuralPacks.SemanticResidentChunks is > 0 and < 117);
        Equal(1UL, session.StructuralPacks.GrantsAccepted);
        Equal(1UL, session.StructuralPacks.ProofsQueued);
        Equal(RemotePlayableClientState.AwaitingAdmission, session.Playable.State);
        fixture.Admit("Manifest Proof", GameGuestBody.HumanMale);
        Equal(RemotePlayableClientState.Active, fixture.Playable.GetSnapshot().State);
        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.CharacterMale, out _));
        Equal(0UL, fixture.World.GetSnapshot().RejectedDeliveries);
    }

    private static void W7ClientRejectsStaleContradictoryAndWrongManifestAdvertisements()
    {
        ApplicationRealmId realm = new(0x5707, 0x206);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady);
        ulong epoch = fixture.SessionSnapshot.StructuralManifest.ApplicationSessionEpoch;
        var advertisement = deployment.StructuralAuthorization.CreateAdvertisement(epoch, 1);
        var client = new RemoteStructuralManifestClient(
            fixture.ClientPeer, fixture.Content!, epoch);

        Deliver(client, advertisement with
        {
            Header = advertisement.Header with { ApplicationSessionEpoch = epoch + 1 }
        });
        Equal(RemoteStructuralManifestClientState.AwaitingAdvertisement,
            client.GetSnapshot().State);
        Equal(1UL, client.GetSnapshot().RejectedAdvertisements);
        True(client.Composition is null);

        Deliver(client, advertisement);
        Equal(RemoteStructuralManifestClientState.AwaitingContent,
            client.GetSnapshot().State);
        Deliver(client, advertisement with
        {
            Header = advertisement.Header with
            { ManifestHash0 = advertisement.Header.ManifestHash0 ^ 1 }
        });
        Equal(RemoteStructuralManifestClientState.Rejected, client.GetSnapshot().State);
        Equal(2UL, client.GetSnapshot().RejectedAdvertisements);
        True(client.Composition is null);

        var wrongFingerprint = new RemoteStructuralManifestClient(
            fixture.ClientPeer, fixture.Content!, epoch);
        Deliver(wrongFingerprint, advertisement with
        {
            Header = advertisement.Header with
            { ManifestHash0 = advertisement.Header.ManifestHash0 ^ 1 }
        });
        False(wrongFingerprint.TryProgress());
        Equal(ContentWorldStructuralManifestResolutionStatus.FingerprintMismatch,
            wrongFingerprint.GetSnapshot().Resolution);
        True(wrongFingerprint.Composition is null);

        static void Deliver(RemoteStructuralManifestClient target,
            RemoteStructuralWorldAdvertisement candidate)
        {
            byte[] payload = new byte[UltraSimGameProtocol.MaximumStructuralAdvertisementBytes];
            True(UltraSimGameProtocol.TryWriteStructuralAdvertisement(payload, in candidate,
                out int written));
            var delivered = new NetworkDeliveredMessage(NetworkLane.Control,
                RemoteStructuralWorldAdvertisementHeaderCodec.Descriptor.Id,
                RemoteStructuralWorldAdvertisementHeaderCodec.Descriptor.Version,
                default, written);
            True(target.TryProcess(in delivered, payload.AsSpan(0, written)));
        }
    }

    private static void W7FarIslandDemandRetainsWestPreparedView()
    {
        ApplicationRealmId realm = new(0x5707, 0x201);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralWorld.State ==
            RemoteStructuralWorldState.Ready);
        fixture.Admit("Far Island", GameGuestBody.HumanMale);
        ClientWorldSemanticCache cache = fixture.Cache;
        PreparedWorldComposition initial = cache.PreparedComposition!;
        True(initial.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord,
            out PreparedWorldPack west));
        False(initial.TryGetPreparedPack(ConceptDemoStructuralSource.EastCoord, out _));
        var far = GamePlayableWorld.FarGateDestination.ToChunkKey(cache.StructuralSpace);
        True(cache.TryProbeStructuralChunk(far, out WorldStructuralChunkProbe before));
        Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, before.Status);
        Equal((long)deployment.Snapshot.WestPackRawBytes,
            fixture.SessionSnapshot.StructuralPacks.BytesFetched);

        // A desired Far-Island window names the missing manifest Pack; cancellation of the
        // provisional clamped plan leaves all old semantic and renderer borrowers untouched.
        True(fixture.StructuralPacks.TryPreparePresentation(
            GamePlayableWorld.FarGateDestination,
            out _, out ClientStructuralResidencyPlan provisional));
        fixture.StructuralPacks.CancelPresentation(provisional);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.PreparedPackCount == 2);
        True(ReferenceEquals(cache, fixture.Cache));
        PreparedWorldComposition prepared = cache.PreparedComposition!;
        True(prepared.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord,
            out PreparedWorldPack retained));
        True(ReferenceEquals(west.View, retained.View));
        True(prepared.TryGetPreparedPack(ConceptDemoStructuralSource.EastCoord, out _));
        Equal(initial.Manifest.Fingerprint, prepared.Manifest.Fingerprint);
        Equal(initial.Revision, prepared.Revision);
        True(cache.TryProbeStructuralChunk(far, out WorldStructuralChunkProbe after));
        Equal(WorldStructuralChunkStatus.PreparedChunk, after.Status);

        True(fixture.StructuralPacks.TryPreparePresentation(
            GamePlayableWorld.FarGateDestination,
            out SpatialChunkKey center, out ClientStructuralResidencyPlan plan));
        Equal(GamePlayableWorld.FarIslandCenterChunkX, center.Coord.X);
        fixture.StructuralPacks.CommitPresentation(plan, center);
        fixture.DriveUntil(() => fixture.StructuralPacks.WindowProofsQueued >= 2);
        True(fixture.Cache.GetSnapshot().ResidentChunks < 117 + 45);
        Equal(0UL, fixture.World.GetSnapshot().RejectedDeliveries);

        long coldBytes = fixture.SessionSnapshot.StructuralPacks.BytesFetched;
        ulong coldAcquisitions = fixture.SessionSnapshot.StructuralPacks.Acquisitions;
        True(coldBytes > deployment.Snapshot.WestPackRawBytes);
        True(fixture.StructuralPacks.TryPreparePresentation(
            GamePlayableWorld.PlayerStart,
            out SpatialChunkKey central, out ClientStructuralResidencyPlan centralPlan));
        fixture.StructuralPacks.CommitPresentation(centralPlan, central);
        fixture.DriveUntil(() => fixture.StructuralPacks.WindowProofsQueued >= 3);
        True(fixture.StructuralPacks.TryRetireOutsidePresentation(central));
        True(cache.TryProbeStructuralChunk(far, out WorldStructuralChunkProbe retired));
        Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, retired.Status);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.RetirementsQueued == 1);
        True(fixture.StructuralPacks.TryPreparePresentation(
            GamePlayableWorld.FarGateDestination,
            out _, out ClientStructuralResidencyPlan warmPlan));
        fixture.StructuralPacks.CancelPresentation(warmPlan);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.Acquisitions > coldAcquisitions &&
            fixture.SessionSnapshot.StructuralPacks.PreparedPackCount == 2);
        Equal(coldBytes, fixture.SessionSnapshot.StructuralPacks.BytesFetched);
        PreparedWorldComposition warmed = cache.PreparedComposition!;
        True(warmed.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord,
            out PreparedWorldPack warmedWest));
        True(ReferenceEquals(west.View, warmedWest.View));
        Equal(initial.Manifest.Fingerprint, warmed.Manifest.Fingerprint);
        Equal(initial.Revision, warmed.Revision);
    }

    private static void W7ClientRejectsStaleAndContradictoryPackGrants()
    {
        ApplicationRealmId realm = new(0x5707, 0x204);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady);
        RemoteStructuralPackClient packs = fixture.StructuralPacks;
        ClientWorldSemanticCache cache = fixture.Cache;
        PreparedWorldComposition initial = cache.PreparedComposition!;
        True(packs.TryRequest(ConceptDemoStructuralSource.EastCoord));
        True(packs.TryProgress());
        Equal(2UL, packs.GetSnapshot().RequestsQueued);
        RemoteStructuralManifestClientSnapshot manifest =
            fixture.SessionSnapshot.StructuralManifest;
        var request = new RemoteStructuralPackRequestMessage
        {
            ApplicationSessionEpoch = manifest.ApplicationSessionEpoch,
            StructuralSequence = manifest.StructuralSequence,
            PackRequestSequence = 2,
            PackX = ConceptDemoStructuralSource.EastCoord.X,
            PackZ = ConceptDemoStructuralSource.EastCoord.Z,
        };
        True(deployment.StructuralAuthorization.TryCreateGrant(in request,
            out RemoteStructuralPackGrant? exact));
        ulong rejected = packs.GetSnapshot().RejectedGrants;

        RemoteStructuralPackGrantHeader header = exact!.Header;
        header.ApplicationSessionEpoch++;
        Reject(header);
        header = exact.Header;
        header.StructuralSequence++;
        Reject(header);
        header = exact.Header;
        header.PackRequestSequence++;
        Reject(header);
        header = exact.Header;
        header.PackX = ConceptDemoStructuralSource.WestCoord.X;
        Reject(header);
        header = exact.Header;
        header.PackHash0 ^= 1;
        Reject(header);
        Equal(1, cache.PreparedComposition!.PreparedPackCount);
        True(ReferenceEquals(initial, cache.PreparedComposition));
        fixture.DriveUntil(() => packs.GetSnapshot().PreparedPackCount == 2);
        Equal(rejected, packs.GetSnapshot().RejectedGrants);
        Equal(2UL, packs.GetSnapshot().GrantsAccepted);
        Equal(initial.Manifest.Fingerprint,
            cache.PreparedComposition!.Manifest.Fingerprint);
        Deliver(exact);
        Equal(rejected, packs.GetSnapshot().RejectedGrants);
        Deliver(new RemoteStructuralPackGrant(exact.Header,
            "server.concept-demo.pack-east-contradictory", exact.Sources));
        Equal(++rejected, packs.GetSnapshot().RejectedGrants);
        True(packs.TryRetirePack(ConceptDemoStructuralSource.EastCoord));

        void Reject(RemoteStructuralPackGrantHeader forgedHeader)
        {
            var forged = new RemoteStructuralPackGrant(forgedHeader,
                exact!.PackageId, exact.Sources);
            Deliver(forged);
            Equal(++rejected, packs.GetSnapshot().RejectedGrants);
            Equal(1, cache.PreparedComposition!.PreparedPackCount);
        }

        void Deliver(RemoteStructuralPackGrant forged)
        {
            byte[] payload = new byte[UltraSimGameProtocol.MaximumStructuralGrantBytes];
            True(UltraSimGameProtocol.TryWriteStructuralPackGrant(payload, forged,
                out int written));
            var delivered = new NetworkDeliveredMessage(NetworkLane.Control,
                RemoteStructuralPackGrantHeaderCodec.Descriptor.Id,
                RemoteStructuralPackGrantHeaderCodec.Descriptor.Version, default, written);
            True(packs.TryProcess(in delivered, payload.AsSpan(0, written)));
        }
    }

    private static void W7PendingPackAcquisitionCancelsWithSession()
    {
        ApplicationRealmId realm = new(0x5707, 0x205);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var http = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var source = new CancellableEastPackSource(
            http, deployment.Structural.East.Package);
        var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);
        try
        {
            fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady);
            ClientWorldSemanticCache cache = fixture.Cache;
            PreparedWorldComposition initial = cache.PreparedComposition!;
            True(fixture.StructuralPacks.TryPreparePresentation(
                GamePlayableWorld.FarGateDestination,
                out _, out ClientStructuralResidencyPlan provisional));
            fixture.StructuralPacks.CancelPresentation(provisional);
            fixture.DriveUntil(() => source.EastFetchStarted);
            Equal(1, cache.PreparedComposition!.PreparedPackCount);
            True(ReferenceEquals(initial, cache.PreparedComposition));
        }
        finally { fixture.Dispose(); }
        True(source.CancellationObserved);
        False(source.EastFetchCompleted);
    }

    private static void W7FailedFarPackAcquisitionRollsBackWithoutFalseKnowledge()
    {
        ApplicationRealmId realm = new(0x5707, 0x203);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var http = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        var source = new RejectEastPackSource(http, deployment.Structural.East.Package);
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady);
        fixture.Admit("Pack Failure", GameGuestBody.HumanFemale);
        ClientWorldSemanticCache cache = fixture.Cache;
        PreparedWorldComposition initial = cache.PreparedComposition!;
        True(initial.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord,
            out PreparedWorldPack west));
        ClientWorldSemanticCacheSnapshot baseline = cache.GetSnapshot();
        long bytesBefore = fixture.SessionSnapshot.StructuralPacks.BytesFetched;
        True(fixture.StructuralPacks.TryPreparePresentation(
            GamePlayableWorld.FarGateDestination,
            out _, out ClientStructuralResidencyPlan provisional));
        fixture.StructuralPacks.CancelPresentation(provisional);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.Acquisitions >= 2 &&
            fixture.SessionSnapshot.StructuralPacks.PendingRequests == 0);
        Equal(1, cache.PreparedComposition!.PreparedPackCount);
        True(cache.PreparedComposition.TryGetPreparedPack(
            ConceptDemoStructuralSource.WestCoord, out PreparedWorldPack retained));
        True(ReferenceEquals(west.View, retained.View));
        Equal(initial.Manifest.Fingerprint, cache.PreparedComposition.Manifest.Fingerprint);
        Equal(initial.Revision, cache.PreparedComposition.Revision);
        Equal(baseline.ResidentChunks, cache.GetSnapshot().ResidentChunks);
        Equal(bytesBefore, fixture.SessionSnapshot.StructuralPacks.BytesFetched);
        SpatialChunkKey far = GamePlayableWorld.FarGateDestination
            .ToChunkKey(cache.StructuralSpace);
        True(cache.TryProbeStructuralChunk(far, out WorldStructuralChunkProbe probe));
        Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, probe.Status);
        Equal(0UL, fixture.World.GetSnapshot().RejectedDeliveries);
    }

    private static void W7ActualFarGateAndSeamTraversal()
    {
        ApplicationRealmId realm = new(0x5707, 0x202);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source, sharedAuthority: true);
        fixture.EnableStructuralTracking();
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.InitialReady);
        fixture.Admit("Gate Traveller", GameGuestBody.HumanFemale);
        ClientWorldSemanticCache cache = fixture.Cache;
        WorldStructuralManifestFingerprint manifest = cache.PreparedComposition!.Manifest.Fingerprint;
        WorldCompositionRevision revision = cache.PreparedComposition.Revision;

        // This controlled setup bypasses unrelated woods/cabin route-finding. The Gate
        // interaction, distant authority relocation, structural demand, and seam walking below
        // still pass through the real shared Game application and authenticated UDP path.
        fixture.RelocateForGateSetup(GamePlayableWorld.CentralFarGatePosition);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position ==
            GamePlayableWorld.CentralFarGatePosition);
        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.FarGate, out WorldPublicRecord gate) &&
            gate.Position == GamePlayableWorld.CentralFarGatePosition);
        True(fixture.TryFindPublic(GameWorldPublicPrefabIds.FarGate,
            GamePlayableWorld.CentralFarGatePosition, out WorldPublicRecord centralGate));
        Interact(centralGate);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position.Chunk.X ==
            GamePlayableWorld.FarIslandCenterChunkX);
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.PreparedPackCount == 2 &&
            fixture.StructuralPacks.WindowProofsQueued >= 2);
        True(ReferenceEquals(cache, fixture.Cache));
        Equal(manifest, cache.PreparedComposition!.Manifest.Fingerprint);
        Equal(revision, cache.PreparedComposition.Revision);

        long seam = (long)GamePlayableWorld.FarIslandCenterChunkX *
            GameWorldSpatialProfile.WorldChunkEdgeUnits;
        MoveTo(Position(seam - 500, 16_000));
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position.Chunk.X == 127);
        MoveTo(Position(seam + 500, 16_000));
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position.Chunk.X == 128);
        MoveTo(Position(seam - 500, 16_000));
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position.Chunk.X == 127);

        MoveTo(GamePlayableWorld.FarReturnGatePosition);
        fixture.DriveUntil(() => fixture.TryFindPublic(GameWorldPublicPrefabIds.FarGate,
            GamePlayableWorld.FarReturnGatePosition, out _));
        True(fixture.TryFindPublic(GameWorldPublicPrefabIds.FarGate,
            GamePlayableWorld.FarReturnGatePosition, out WorldPublicRecord returnGate));
        Interact(returnGate);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().Position.Chunk.X == 0);
        fixture.DriveUntil(() => fixture.ServerSnapshot.StructuralWindowCenter.Coord.X == 0);
        fixture.DriveUntil(() => fixture.ServerInterestAnchor.Chunk.X == 0);
        var infos = new ClientSemanticChunkInfo[cache.Limits.MaximumResidentChunks];
        fixture.DriveUntil(() =>
        {
            True(cache.TryCopyResidentInfo(infos, out int count, out _));
            for (int i = 0; i < count; i++)
            {
                WorldPackAddressing.ToPackLocal(infos[i].Key.Coord,
                    out WorldPackCoord owner, out _);
                if (owner == ConceptDemoStructuralSource.EastCoord &&
                    infos[i].PublicRecordCount != 0) return false;
            }
            return true;
        });
        True(fixture.StructuralPacks.TryPreparePresentation(
            fixture.Playable.GetSnapshot().Position,
            out SpatialChunkKey settledCenter, out ClientStructuralResidencyPlan settledPlan));
        fixture.StructuralPacks.CommitPresentation(settledPlan, settledCenter);
        fixture.DriveUntil(() => fixture.StructuralPacks.TryRetireOutsidePresentation(
            settledCenter));
        fixture.DriveUntil(() => fixture.SessionSnapshot.StructuralPacks.RetirementsQueued != 0);
        True(cache.TryProbeStructuralChunk(GamePlayableWorld.FarReturnGatePosition
            .ToChunkKey(cache.StructuralSpace), out WorldStructuralChunkProbe retired));
        Equal(WorldStructuralChunkStatus.KnownPackButNotPrepared, retired.Status);
        True(ReferenceEquals(cache, fixture.Cache));
        Equal(manifest, cache.PreparedComposition!.Manifest.Fingerprint);
        Equal(revision, cache.PreparedComposition.Revision);
        True(fixture.ServerSnapshot.InterestRetirementsQueued > 0);
        True(fixture.ServerSnapshot.InterestRetirementsQueued < 128);
        fixture.DriveSteps(32);
        RemoteChunkWorldStateClientSnapshot worldSnapshot = fixture.World.GetSnapshot();
        True(worldSnapshot.RetiredInterestCount > 0);
        Console.WriteLine("EVIDENCE W7 actual_gate_seam_return=" +
            $"public_retirements={worldSnapshot.RetiredInterestCount} " +
            $"stale_inflight_rejections={worldSnapshot.RejectedDeliveries} " +
            $"missing_resident={worldSnapshot.MissingResidentDeliveryAttempts} " +
            $"missing_prepared={worldSnapshot.MissingResidentPreparedChunkAttempts}");
        Equal(worldSnapshot.RejectedDeliveries,
            worldSnapshot.MissingResidentDeliveryAttempts);
        Equal(worldSnapshot.RejectedDeliveries,
            worldSnapshot.MissingResidentPreparedChunkAttempts);
        True(worldSnapshot.RejectedDeliveries <= 4);

        void MoveTo(WorldPosition destination)
        {
            fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
            True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDestination(destination.GlobalXMillimeters,
                    destination.GlobalZMillimeters), GamePlayerActions.None, true)));
            fixture.DriveUntil(() =>
            {
                WorldPosition current = fixture.Playable.GetSnapshot().Position;
                long dx = current.GlobalXMillimeters - destination.GlobalXMillimeters;
                long dz = current.GlobalZMillimeters - destination.GlobalZMillimeters;
                return dx * dx + dz * dz < 1_000L * 1_000L;
            });
        }

        void Interact(in WorldPublicRecord gate)
        {
            fixture.DriveUntil(() => !fixture.Playable.GetSnapshot().CommandRetainedLocally);
            True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true), gate.Id));
        }

        static WorldPosition Position(long x, long z)
        {
            True(WorldPosition.TryFromGlobalMillimeters(x, 1_000, z,
                out WorldPosition position));
            return position;
        }
    }

    private static void W7UnsupportedStructuralOverlayFailsClosed()
    {
        ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
        PreparedWorldComposition baseWorld = source.Prepare(ConceptDemoStructuralSource.CreateBinding());
        var authorization = new RemoteStructuralWorldAuthorization(
            source.Manifest, ConceptDemoStructuralContent.ManifestDefinitionValue,
            GameWorldState.DefaultSpace, new WorldCompositionRevision(1),
            ConceptDemoStructuralSource.WestCoord,
            GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace));
        True(authorization.MatchesUnmodifiedAuthority(baseWorld));
        True(baseWorld.TryGetPreparedPack(ConceptDemoStructuralSource.WestCoord,
            out PreparedWorldPack west));
        True(baseWorld.TryGetPreparedPack(ConceptDemoStructuralSource.EastCoord,
            out PreparedWorldPack east));
        SpatialChunkKey changedKey = default;
        for (int index = 0; index < west.View.BaseWorld.ChunkCount; index++)
        {
            True(west.View.BaseWorld.TryGetChunkKey(index, out SpatialChunkKey key));
            if (!west.View.BaseWorld.TryGetFrozenStatics(key,
                    out WorldFrozenStaticChunkView statics) || statics.Count == 0) continue;
            changedKey = key;
            break;
        }
        True(changedKey.Space.IsValid);
        var builder = new WorldMutableOverlayBuilder(west.View.BaseWorld);
        True(builder.TryReplaceFrozenStatics(changedKey, []));
        True(builder.TryBuild(out WorldMutableOverlay? overlay));
        True(overlay!.ChangedChunkCount != 0);
        True(PreparedWorldComposition.TryCreate(baseWorld.Space, baseWorld.Revision,
            source.Manifest,
            [new PreparedWorldPack(ConceptDemoStructuralSource.WestCoord,
                new WorldPreparedView(west.View.BaseWorld, overlay)), east],
            out PreparedWorldComposition? changed, out _));
        True(changed!.TryValidateComplete(out _));
        False(authorization.MatchesUnmodifiedAuthority(changed));
        False(authorization.MatchesUnmodifiedAuthority(null));
    }

    private static void W7CrossLaneAdmissionWaitsForStructuralProof()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        RemoteContentAuthorization content = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint,
            [RemoteReplaceAcquisition(oracle)]);
        ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
        var structural = new RemoteStructuralWorldAuthorization(
            source.Manifest, ConceptDemoStructuralContent.ManifestDefinitionValue,
            GameWorldState.DefaultSpace, new WorldCompositionRevision(1),
            ConceptDemoStructuralSource.WestCoord,
            GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace));
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default, new DeterministicDatagramLimits(2_048, 1_200),
            default, out ProtocolLoopback? loop, out _, UltraSimGameProtocol.Manifest));
        using (loop)
        using (var server = new RemoteGuestPlayableApplication(
            loop!.Server, CabinContentOracle.FixtureRealm, new ParticipantId(1), 77,
            contentAuthorization: content, structuralAuthorization: structural))
        {
            RemoteContentSetAppliedMessage contentProof = CreateAppliedProof(oracle, 77);
            Span<byte> contentBytes = stackalloc byte[RemoteContentSetAppliedMessageCodec.EncodedSize];
            True(RemoteContentSetAppliedMessageCodec.TryWriteExact(contentBytes, in contentProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteContentSetAppliedMessageCodec.Descriptor.Id,
                RemoteContentSetAppliedMessageCodec.Descriptor.Version, default,
                contentBytes.Length), contentBytes));
            True(server.GetSnapshot().ContentApplied);

            var guest = new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "Cross Lane");
            Span<byte> guestBytes = stackalloc byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
            True(UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
                guestBytes, 77, 1, in guest, out int guestLength));
            var guestMessage = new NetworkDeliveredMessage(
                NetworkLane.Event, RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Id,
                RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Version, default, guestLength);
            True(server.TryProcess(in guestMessage, guestBytes[..guestLength]));
            False(server.GetSnapshot().GuestAdmitted);
            // Repeated Event delivery must not create a second attempt or mutate Game.
            True(server.TryProcess(in guestMessage, guestBytes[..guestLength]));
            False(server.GetSnapshot().GuestAdmitted);

            RemoteStructuralWorldAdvertisement advertisement =
                structural.CreateAdvertisement(77, 1);
            RemoteStructuralWorldAdvertisementHeader header = advertisement.Header;
            var structuralProof = new RemoteStructuralManifestAppliedMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                ManifestHash0 = header.ManifestHash0,
                ManifestHash1 = header.ManifestHash1,
                ManifestHash2 = header.ManifestHash2,
                ManifestHash3 = header.ManifestHash3,
                Status = RemoteStructuralApplyStatus.Applied,
            };
            Span<byte> structuralBytes = stackalloc byte[
                RemoteStructuralManifestAppliedMessageCodec.EncodedSize];
            True(RemoteStructuralManifestAppliedMessageCodec.TryWriteExact(
                structuralBytes, in structuralProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralManifestAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralManifestAppliedMessageCodec.Descriptor.Version, default,
                structuralBytes.Length), structuralBytes));
            True(server.GetSnapshot().StructuralManifestApplied);
            True(server.GetSnapshot().GuestAdmitted);
            True(server.TryProcess(in guestMessage, guestBytes[..guestLength]));
            True(server.GetSnapshot().GuestAdmitted);
        }
    }

    private static void W7PackGrantFollowsManifestAndExactRequestLineage()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        ApplicationRealmId realm = CabinContentOracle.FixtureRealm;
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        RemoteContentAuthorization content = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint,
            [RemoteReplaceAcquisition(oracle)]);
        RemoteStructuralWorldAuthorization structural = deployment.StructuralAuthorization;
        Equal(2, structural.AvailablePackageCount);
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default, new DeterministicDatagramLimits(2_048, 1_200),
            default, out ProtocolLoopback? loop, out _, UltraSimGameProtocol.Manifest));
        using (loop)
        using (var server = new RemoteGuestPlayableApplication(
            loop!.Server, realm, new ParticipantId(1), 77,
            contentAuthorization: content, structuralAuthorization: structural))
        {
            Span<byte> requestBytes = stackalloc byte[RemoteStructuralPackRequestMessageCodec.EncodedSize];
            var request = new RemoteStructuralPackRequestMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                PackRequestSequence = 1,
                PackX = ConceptDemoStructuralSource.WestCoord.X,
                PackZ = ConceptDemoStructuralSource.WestCoord.Z,
            };
            var requestMessage = new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralPackRequestMessageCodec.Descriptor.Id,
                RemoteStructuralPackRequestMessageCodec.Descriptor.Version, default,
                requestBytes.Length);
            True(RemoteStructuralPackRequestMessageCodec.TryWriteExact(requestBytes, in request));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(0UL, server.GetSnapshot().StructuralPackRequests);
            Equal(1UL, server.GetSnapshot().RejectedStructuralPackMessages);

            RemoteContentSetAppliedMessage contentProof = CreateAppliedProof(oracle, 77);
            Span<byte> contentBytes = stackalloc byte[RemoteContentSetAppliedMessageCodec.EncodedSize];
            True(RemoteContentSetAppliedMessageCodec.TryWriteExact(contentBytes, in contentProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteContentSetAppliedMessageCodec.Descriptor.Id,
                RemoteContentSetAppliedMessageCodec.Descriptor.Version, default,
                contentBytes.Length), contentBytes));
            RemoteStructuralWorldAdvertisementHeader header =
                structural.CreateAdvertisement(77, 1).Header;
            var manifestProof = new RemoteStructuralManifestAppliedMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                ManifestHash0 = header.ManifestHash0,
                ManifestHash1 = header.ManifestHash1,
                ManifestHash2 = header.ManifestHash2,
                ManifestHash3 = header.ManifestHash3,
                Status = RemoteStructuralApplyStatus.Applied,
            };
            Span<byte> manifestBytes = stackalloc byte[
                RemoteStructuralManifestAppliedMessageCodec.EncodedSize];
            True(RemoteStructuralManifestAppliedMessageCodec.TryWriteExact(
                manifestBytes, in manifestProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralManifestAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralManifestAppliedMessageCodec.Descriptor.Version, default,
                manifestBytes.Length), manifestBytes));

            True(server.TryProcess(in requestMessage, requestBytes));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(1UL, server.GetSnapshot().StructuralPackRequests);
            Equal(0UL, server.GetSnapshot().StructuralPackGrantsQueued);
            True(server.TryProgress());
            Equal(1UL, server.GetSnapshot().StructuralPackGrantsQueued);
            RemoteStructuralPackGrant? grant = null;
            for (int step = 0; step < 256 && grant is null; step++)
            {
                loop.Pump();
                while (loop.Client.TryPeekReceived(
                           out NetworkDeliveredMessage delivered, out ReadOnlySpan<byte> payload))
                {
                    try
                    {
                        if (delivered.MessageId == RemoteStructuralPackGrantHeaderCodec.Descriptor.Id)
                            True(UltraSimGameProtocol.TryReadStructuralPackGrant(payload, out grant));
                    }
                    finally { _ = loop.Client.TryConsumeReceived(); }
                }
            }
            True(grant is not null);
            Equal(deployment.Structural.West.Package.Value!, grant!.PackageId);
            Equal(ConceptDemoStructuralSource.WestCoord.X, grant.Header.PackX);
            Equal(ConceptDemoStructuralSource.WestCoord.Z, grant.Header.PackZ);
            Equal(deployment.Structural.West.ManifestHash.Part0,
                grant.Header.PackageManifestHash0);
            True(deployment.Structural.Manifest.TryGetPack(
                ConceptDemoStructuralSource.WestCoord, out WorldPackFingerprint west));
            Equal(west.Part0, grant.Header.PackHash0);
            SpatialChunkKey initialCenter = structural.InitialCenter;
            var windowProof = new RemoteStructuralWindowAppliedMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                WindowSequence = 1,
                CenterChunkX = initialCenter.Coord.X,
                CenterChunkZ = initialCenter.Coord.Z,
                ResidentRadiusChunks = 2,
                ResidentMask = (1UL << 25) - 1,
            };
            Span<byte> windowBytes = stackalloc byte[
                RemoteStructuralWindowAppliedMessageCodec.EncodedSize];
            True(RemoteStructuralWindowAppliedMessageCodec.TryWriteExact(
                windowBytes, in windowProof));
            var windowMessage = new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralWindowAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralWindowAppliedMessageCodec.Descriptor.Version, default,
                windowBytes.Length);
            True(server.TryProcess(in windowMessage, windowBytes));
            Equal(0UL, server.GetSnapshot().StructuralWindowSequence);
            Equal(1UL, server.GetSnapshot().RejectedStructuralWindowProofs);
            var packProof = new RemoteStructuralPackAppliedMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                PackRequestSequence = 1,
                PackX = grant.Header.PackX,
                PackZ = grant.Header.PackZ,
                PackHash0 = grant.Header.PackHash0,
                PackHash1 = grant.Header.PackHash1,
                PackHash2 = grant.Header.PackHash2,
                PackHash3 = grant.Header.PackHash3,
                Status = RemoteStructuralApplyStatus.Applied,
            };
            Span<byte> proofBytes = stackalloc byte[RemoteStructuralPackAppliedMessageCodec.EncodedSize];
            True(RemoteStructuralPackAppliedMessageCodec.TryWriteExact(proofBytes, in packProof));
            var proofMessage = new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralPackAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralPackAppliedMessageCodec.Descriptor.Version, default,
                proofBytes.Length);
            True(server.TryProcess(in proofMessage, proofBytes));
            True(server.TryProcess(in proofMessage, proofBytes));
            Equal(1UL, server.GetSnapshot().StructuralPackProofsAccepted);
            True(server.TryProcess(in windowMessage, windowBytes));
            True(server.TryProcess(in windowMessage, windowBytes));
            Equal(1UL, server.GetSnapshot().StructuralWindowSequence);
            Equal(initialCenter, server.GetSnapshot().StructuralWindowCenter);
            windowProof.ResidentRadiusChunks = 8;
            True(RemoteStructuralWindowAppliedMessageCodec.TryWriteExact(
                windowBytes, in windowProof));
            True(server.TryProcess(in windowMessage, windowBytes));
            Equal(2UL, server.GetSnapshot().RejectedStructuralWindowProofs);

            request.PackRequestSequence = 2;
            request.PackX = 100;
            True(RemoteStructuralPackRequestMessageCodec.TryWriteExact(requestBytes, in request));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(2UL, server.GetSnapshot().RejectedStructuralPackMessages);
            request.PackX = ConceptDemoStructuralSource.EastCoord.X;
            True(RemoteStructuralPackRequestMessageCodec.TryWriteExact(requestBytes, in request));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(2UL, server.GetSnapshot().StructuralPackRequests);
            packProof.PackRequestSequence = 2;
            packProof.PackX = ConceptDemoStructuralSource.EastCoord.X;
            True(RemoteStructuralPackAppliedMessageCodec.TryWriteExact(proofBytes, in packProof));
            True(server.TryProcess(in proofMessage, proofBytes));
            Equal(3UL, server.GetSnapshot().RejectedStructuralPackMessages);

            True(server.TryProgress());
            RemoteStructuralPackGrant? eastGrant = null;
            for (int step = 0; step < 256 && eastGrant is null; step++)
            {
                loop.Pump();
                while (loop.Client.TryPeekReceived(
                           out NetworkDeliveredMessage delivered, out ReadOnlySpan<byte> payload))
                {
                    try
                    {
                        if (delivered.MessageId == RemoteStructuralPackGrantHeaderCodec.Descriptor.Id &&
                            UltraSimGameProtocol.TryReadStructuralPackGrant(payload,
                                out RemoteStructuralPackGrant? candidate) &&
                            candidate!.Header.PackRequestSequence == 2)
                            eastGrant = candidate;
                    }
                    finally { _ = loop.Client.TryConsumeReceived(); }
                }
            }
            True(eastGrant is not null);
            packProof.PackHash0 = eastGrant!.Header.PackHash0;
            packProof.PackHash1 = eastGrant.Header.PackHash1;
            packProof.PackHash2 = eastGrant.Header.PackHash2;
            packProof.PackHash3 = eastGrant.Header.PackHash3;
            True(RemoteStructuralPackAppliedMessageCodec.TryWriteExact(proofBytes, in packProof));
            True(server.TryProcess(in proofMessage, proofBytes));
            Equal(2UL, server.GetSnapshot().StructuralPackProofsAccepted);
            var retired = new RemoteStructuralPackRetiredMessage
            {
                ApplicationSessionEpoch = 77,
                StructuralSequence = 1,
                PackRequestSequence = 2,
                PackX = packProof.PackX,
                PackZ = packProof.PackZ,
                PackHash0 = packProof.PackHash0,
                PackHash1 = packProof.PackHash1,
                PackHash2 = packProof.PackHash2,
                PackHash3 = packProof.PackHash3,
            };
            Span<byte> retiredBytes = stackalloc byte[
                RemoteStructuralPackRetiredMessageCodec.EncodedSize];
            True(RemoteStructuralPackRetiredMessageCodec.TryWriteExact(
                retiredBytes, in retired));
            var retiredMessage = new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralPackRetiredMessageCodec.Descriptor.Id,
                RemoteStructuralPackRetiredMessageCodec.Descriptor.Version, default,
                retiredBytes.Length);
            True(server.TryProcess(in retiredMessage, retiredBytes));
            True(server.TryProcess(in retiredMessage, retiredBytes));
            Equal(1UL, server.GetSnapshot().StructuralPackRetirements);
            retired.PackHash0++;
            True(RemoteStructuralPackRetiredMessageCodec.TryWriteExact(
                retiredBytes, in retired));
            True(server.TryProcess(in retiredMessage, retiredBytes));
            Equal(4UL, server.GetSnapshot().RejectedStructuralPackMessages);

            // Authenticated but stale/contradictory controls cannot reopen knowledge from a
            // previous lifetime or claim a different Pack under one request identity.
            ulong rejectedManifest = server.GetSnapshot().RejectedStructuralManifestProofs;
            manifestProof.ApplicationSessionEpoch = 76;
            True(RemoteStructuralManifestAppliedMessageCodec.TryWriteExact(
                manifestBytes, in manifestProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralManifestAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralManifestAppliedMessageCodec.Descriptor.Version, default,
                manifestBytes.Length), manifestBytes));
            Equal(rejectedManifest + 1, server.GetSnapshot().RejectedStructuralManifestProofs);
            manifestProof.ApplicationSessionEpoch = 77;
            manifestProof.ManifestHash0 ^= 1;
            True(RemoteStructuralManifestAppliedMessageCodec.TryWriteExact(
                manifestBytes, in manifestProof));
            True(server.TryProcess(new NetworkDeliveredMessage(
                NetworkLane.Control, RemoteStructuralManifestAppliedMessageCodec.Descriptor.Id,
                RemoteStructuralManifestAppliedMessageCodec.Descriptor.Version, default,
                manifestBytes.Length), manifestBytes));
            Equal(rejectedManifest + 2, server.GetSnapshot().RejectedStructuralManifestProofs);
            True(server.GetSnapshot().StructuralManifestApplied);

            ulong rejectedPacks = server.GetSnapshot().RejectedStructuralPackMessages;
            request.ApplicationSessionEpoch = 76;
            request.PackRequestSequence = 3;
            request.PackX = ConceptDemoStructuralSource.WestCoord.X;
            True(RemoteStructuralPackRequestMessageCodec.TryWriteExact(requestBytes, in request));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(rejectedPacks + 1, server.GetSnapshot().RejectedStructuralPackMessages);
            Equal(2UL, server.GetSnapshot().StructuralPackRequests);

            packProof.ApplicationSessionEpoch = 76;
            True(RemoteStructuralPackAppliedMessageCodec.TryWriteExact(proofBytes, in packProof));
            True(server.TryProcess(in proofMessage, proofBytes));
            packProof.ApplicationSessionEpoch = 77;
            packProof.PackHash0 ^= 1;
            True(RemoteStructuralPackAppliedMessageCodec.TryWriteExact(proofBytes, in packProof));
            True(server.TryProcess(in proofMessage, proofBytes));
            Equal(rejectedPacks + 3, server.GetSnapshot().RejectedStructuralPackMessages);
            Equal(2UL, server.GetSnapshot().StructuralPackProofsAccepted);

            ulong rejectedWindows = server.GetSnapshot().RejectedStructuralWindowProofs;
            windowProof.ApplicationSessionEpoch = 76;
            True(RemoteStructuralWindowAppliedMessageCodec.TryWriteExact(
                windowBytes, in windowProof));
            True(server.TryProcess(in windowMessage, windowBytes));
            Equal(rejectedWindows + 1, server.GetSnapshot().RejectedStructuralWindowProofs);
            Equal(1UL, server.GetSnapshot().StructuralWindowSequence);

            // A contradictory same-lineage request faults this application. Run it last:
            // it is stronger than a soft rejection and must stop grant progression.
            request.ApplicationSessionEpoch = 77;
            request.PackRequestSequence = 2;
            True(RemoteStructuralPackRequestMessageCodec.TryWriteExact(requestBytes, in request));
            True(server.TryProcess(in requestMessage, requestBytes));
            Equal(2UL, server.GetSnapshot().StructuralPackRequests);
            ulong grantsBeforeFault = server.GetSnapshot().StructuralPackGrantsQueued;
            True(server.TryProgress());
            Equal(grantsBeforeFault, server.GetSnapshot().StructuralPackGrantsQueued);
        }
    }

    private static void W7ConceptDemoPacksAreIndependentExactPackagesOutsideBootstrap()
    {
        ApplicationRealmId realm = new(0x5707, 0x100);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        ConceptDemoStructuralSource authored = GamePlayableWorld.CreateConceptDemoStructuralSource();
        Equal(authored.Manifest.Fingerprint, deployment.Structural.Manifest.Fingerprint);
        Equal(authored.West.Fingerprint,
            GetPackFingerprint(deployment.Structural.Manifest, ConceptDemoStructuralSource.WestCoord));
        Equal(authored.East.Fingerprint,
            GetPackFingerprint(deployment.Structural.Manifest, ConceptDemoStructuralSource.EastCoord));
        Equal(1, deployment.Authorization.Packages.Count);
        False(deployment.Authorization.Packages.Any(package =>
            package.PackageId == deployment.Structural.West.Package.Value ||
            package.PackageId == deployment.Structural.East.Package.Value));

        string root = TemporaryDirectory("ultrasim-w7-independent-packs");
        try
        {
            True(ContentCache.TryOpen(root, out ContentCache? cache, out _));
            using (cache)
            using (var source = new HttpContentPackageSource(
                FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true)))
            {
                ContentScopeKey scope = ContentScopeKey.ForRealm(realm);
                AcquireAndVerify(deployment.Structural.West, authored.West,
                    ConceptDemoStructuralSource.WestCoord);
                AcquireAndVerify(deployment.Structural.East, authored.East,
                    ConceptDemoStructuralSource.EastCoord);
                ContentCacheSnapshot cold = cache!.GetSnapshot();
                Equal(2, cold.RealmManifestCount);
                Equal(0, cold.ActivationCount);
                Equal(2, cold.BlobCount);
                AcquireAndVerify(deployment.Structural.West, authored.West,
                    ConceptDemoStructuralSource.WestCoord, warm: true);
                AcquireAndVerify(deployment.Structural.East, authored.East,
                    ConceptDemoStructuralSource.EastCoord, warm: true);
                Equal(0, cache.GetSnapshot().ActivationCount);

                void AcquireAndVerify(ContentPackageAcquisition acquisition,
                    WorldPack expected, WorldPackCoord coord, bool warm = false)
                {
                    True(ContentActivationAcquirer.TryAcquireExactPackage(
                        cache!, scope, acquisition, source, out VerifiedContentPackage? package,
                        out ContentActivationAcquisitionResult result));
                    True(result.Applied);
                    if (warm)
                    {
                        Equal(0L, result.BytesFetched);
                        Equal(1, result.PackagesReused);
                    }
                    else
                    {
                        True(result.BytesFetched > 0);
                        Equal(1, result.PackagesFetched);
                    }
                    Equal(1, package!.Manifest.Files.Count);
                    ContentFileDescriptor file = package.Manifest.Files[0];
                    Equal(ContentFileKind.WorldPack, file.Kind);
                    True(cache!.TryOpenBlob(file, out ContentBlobLease? blob, out _));
                    using (blob)
                    {
                        True(WorldPack.TryOpen(blob!.Stream, WorldPackLimits.Default,
                            expected.ContentFingerprint, out WorldPack? opened, out _));
                        Equal(expected.Fingerprint, opened!.Fingerprint);
                        True(opened.TryValidateSpatialWindow(coord, out _, out _));
                    }
                    WorldContentBinding binding =
                        RemoteConceptDemoWorldProfile.CreateRequiredBinding().ContentBinding;
                    True(ContentWorldPackResolver.TryResolveExactPackage(
                        cache, scope, acquisition.Package, acquisition.ManifestHash,
                        expected.Fingerprint, coord, GameWorldState.DefaultSpace, binding,
                        out BoundWorldPack? bound, out ContentWorldPackResolutionResult resolution));
                    True(resolution.IsResolved);
                    Equal(expected.Fingerprint, bound!.Fingerprint);
                    WorldPackFingerprint otherFingerprint = expected.Fingerprint ==
                        authored.West.Fingerprint
                        ? authored.East.Fingerprint : authored.West.Fingerprint;
                    False(ContentWorldPackResolver.TryResolveExactPackage(
                        cache, scope, acquisition.Package, acquisition.ManifestHash,
                        otherFingerprint, coord, GameWorldState.DefaultSpace, binding,
                        out _, out resolution));
                    Equal(ContentWorldPackResolutionStatus.PackFingerprintMismatch,
                        resolution.Status);
                    False(ContentWorldPackResolver.TryResolveExactPackage(
                        cache, scope, acquisition.Package, acquisition.ManifestHash,
                        expected.Fingerprint, coord, new SpatialSpaceId(2), binding,
                        out _, out resolution));
                    Equal(ContentWorldPackResolutionStatus.PackSpaceMismatch,
                        resolution.Status);
                    var incompleteBinding = new WorldContentBinding(
                        binding.Fingerprint, [GameTerrainMaterialIds.Grass]);
                    False(ContentWorldPackResolver.TryResolveExactPackage(
                        cache, scope, acquisition.Package, acquisition.ManifestHash,
                        expected.Fingerprint, coord, GameWorldState.DefaultSpace,
                        incompleteBinding, out _, out resolution));
                    Equal(ContentWorldPackResolutionStatus.ContentBindingFailure,
                        resolution.Status);
                    False(ContentWorldPackResolver.TryResolveExactPackage(
                        cache, scope, acquisition.Package, acquisition.ManifestHash,
                        expected.Fingerprint, new WorldPackCoord(99, 99),
                        GameWorldState.DefaultSpace, binding,
                        out _, out resolution));
                    Equal(ContentWorldPackResolutionStatus.PackWindowMismatch, resolution.Status);
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static WorldPackFingerprint GetPackFingerprint(
            WorldStructuralManifest manifest, WorldPackCoord coord)
        {
            True(manifest.TryGetPack(coord, out WorldPackFingerprint fingerprint));
            return fingerprint;
        }
    }

    private static void F12SemanticEntryPrecedesOptionalActorPresentation()
    {
        ApplicationRealmId realm = new(0xF12, 0x100);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        Equal(1, deployment.Authorization.Packages.Count);
        Equal(2, deployment.PresentationAuthorization.Packages.Count);
        True(deployment.Snapshot.PlanHash != deployment.Snapshot.PresentationPlanHash);
        using var http = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var source = new BlockedActorPackageSource(
            http, deployment.Authorization.Packages[0].PackageId);
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source);

        fixture.DriveUntil(() => fixture.Content!.Applied &&
            fixture.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        AuthorizedSemanticActivation semanticBefore = fixture.Content!.SemanticActivation;
        True(semanticBefore.IsValid);
        Equal(deployment.Snapshot.PlanHash, semanticBefore.PlanHash);
        Equal(deployment.Snapshot.CatalogFingerprint, semanticBefore.ResolvedFingerprint);
        fixture.Admit("Presentation Optional", GameGuestBody.HumanFemale);
        fixture.DriveUntil(() => source.ArtFetchStarted);
        RemotePlayableSessionSnapshot before = fixture.SessionSnapshot;
        True(before.RequiredContentApplied);
        True(before.SemanticConsumersActive);
        Equal(2, before.Presentation.Generation.PrimitiveSelections);
        Equal(0, before.Presentation.Generation.RealmSelections);
        fixture.DriveUntil(() => fixture.TryFindPublic(
            GameWorldPublicPrefabIds.Character, out _));
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        fixture.DriveUntil(() => fixture.UiClient.OpenViewCount == 1);
        True(fixture.Chat.TrySubmit("art is optional", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 1);

        source.ReleaseArt();
        fixture.DriveUntil(() => fixture.SessionSnapshot.Presentation.Generation.RealmSelections == 2);
        RemotePlayableSessionSnapshot after = fixture.SessionSnapshot;
        Equal(semanticBefore, fixture.Content.SemanticActivation);
        Equal(before.StructuralManifest.ManifestFingerprint,
            after.StructuralManifest.ManifestFingerprint);
        Equal(before.Playable.Participant, after.Playable.Participant);
        True(before.Presentation.Generation.Generation !=
            after.Presentation.Generation.Generation);
        Equal(2, after.Presentation.Generation.RealmSelections);
        using var unavailable = new BlockedActorPackageSource(
            http, deployment.Authorization.Packages[0].PackageId, rejectArt: true);
        using var noArt = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, unavailable);
        noArt.DriveUntil(() => noArt.Content!.Applied &&
            noArt.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        noArt.Admit("No Actor Art", GameGuestBody.HumanMale);
        noArt.DriveUntil(() => unavailable.ArtFetchStarted);
        Equal(2, noArt.SessionSnapshot.Presentation.Generation.PrimitiveSelections);
        Equal(semanticBefore.PlanHash, noArt.Content!.SemanticActivation.PlanHash);
        Equal(semanticBefore.ResolvedFingerprint,
            noArt.Content.SemanticActivation.ResolvedFingerprint);
        True(semanticBefore.ApplicationSessionEpoch !=
            noArt.Content.SemanticActivation.ApplicationSessionEpoch);
        using var corruptSource = new BlockedActorPackageSource(
            http, deployment.Authorization.Packages[0].PackageId, corruptArt: true);
        using var corruptArt = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, corruptSource);
        corruptArt.DriveUntil(() => corruptArt.Content!.Applied &&
            corruptArt.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        corruptArt.Admit("Corrupt Actor Art", GameGuestBody.HumanFemale);
        corruptArt.DriveUntil(() => corruptSource.ArtFetchStarted &&
            !corruptArt.SessionSnapshot.Presentation.Acquiring);
        Equal(2, corruptArt.SessionSnapshot.Presentation.Generation.PrimitiveSelections);
        Equal(semanticBefore.PlanHash, corruptArt.Content!.SemanticActivation.PlanHash);
        Equal(semanticBefore.ResolvedFingerprint,
            corruptArt.Content.SemanticActivation.ResolvedFingerprint);
        using var localFallbackSource = new BlockedActorPackageSource(
            http, deployment.Authorization.Packages[0].PackageId, rejectArt: true);
        using var withLocal = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, localFallbackSource, installLocalPresentation: true);
        withLocal.DriveUntil(() => withLocal.Content!.Applied &&
            withLocal.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        withLocal.Admit("Local Actor Art", GameGuestBody.HumanFemale);
        withLocal.DriveUntil(() => localFallbackSource.ArtFetchStarted);
        Equal(2, withLocal.SessionSnapshot.Presentation.Generation.LocalSelections);
        Equal(0, withLocal.SessionSnapshot.Presentation.Generation.RealmSelections);
        Equal(semanticBefore.PlanHash, withLocal.Content!.SemanticActivation.PlanHash);
        Equal(semanticBefore.ResolvedFingerprint,
            withLocal.Content.SemanticActivation.ResolvedFingerprint);
        True(before.Presentation.Generation.Generation !=
            withLocal.SessionSnapshot.Presentation.Generation.Generation);

        using var staleSource = new BlockedActorPackageSource(
            http, deployment.Authorization.Packages[0].PackageId);
        using var stale = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, staleSource);
        stale.DriveUntil(() => stale.Content!.Applied &&
            stale.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        stale.Admit("Stale Art", GameGuestBody.HumanFemale);
        stale.DriveUntil(() => staleSource.ArtFetchStarted);
        PresentationGenerationId preReset =
            stale.SessionSnapshot.Presentation.Generation.Generation;
        stale.Presentation!.ResetToLocalFallback();
        staleSource.ReleaseArt();
        stale.DriveUntil(() => !stale.SessionSnapshot.Presentation.Acquiring);
        Equal(2, stale.SessionSnapshot.Presentation.Generation.PrimitiveSelections);
        True(stale.SessionSnapshot.Presentation.Generation.Generation != preReset);
        Equal(semanticBefore.PlanHash, stale.Content!.SemanticActivation.PlanHash);
        Console.WriteLine(
            $"EVIDENCE F1.2 semantic_plan={semanticBefore.PlanHash} " +
            $"semantic_catalog={semanticBefore.ResolvedFingerprint} " +
            $"presentation_plan={deployment.Snapshot.PresentationPlanHash} " +
            $"primitive_generation={before.Presentation.Generation.Generation.Value} " +
            $"realm_generation={after.Presentation.Generation.Generation.Value} " +
            $"semantic_payload_bytes={fixture.Content.GetSnapshot().Acquisition.BytesFetched} " +
            $"actor_payload_bytes={after.Presentation.Acquisition.BytesFetched} " +
            $"actor_source_bytes={deployment.Snapshot.ActorPresentationBytes} " +
            "unavailable_art_semantic_entry=pass corrupt_art_fallback=pass " +
            "local_fallback=pass stale_cancel=pass");
    }

    private static void F15UserPresentationChangesNeverChangeSemanticActivation()
    {
        ApplicationRealmId realm = new(0xF15, 0x500);
        using ConceptDemoContentDeployment deployment = ConceptDemoContentDeployment.Create(
            realm, new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(deployment.Snapshot.ArchiveBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithConceptDeployment(
            realm, deployment, source, installLocalPresentation: true);
        fixture.DriveUntil(() => fixture.Content!.Applied &&
            fixture.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        fixture.Admit("User Pack Proof", GameGuestBody.HumanFemale);
        fixture.DriveUntil(() =>
            fixture.SessionSnapshot.Presentation.Generation.RealmSelections == 2);

        AuthorizedSemanticActivation semantic = fixture.Content!.SemanticActivation;
        WorldStructuralManifestFingerprint world =
            fixture.SessionSnapshot.StructuralManifest.ManifestFingerprint;
        var participant = fixture.Playable.GetSnapshot().Participant;
        (ContentPackageManifest manifest, ContentFilePayload[] payloads) =
            CreateF15UserActorPack();
        (ContentPackageManifest noOpManifest, ContentFilePayload[] noOpPayloads) =
            CreateF15UserActorPack("user.remote-proof-unused", "otherworld:human/x");
        True(fixture.ContentStore!.TryInstallUserResourcePack(
            manifest, payloads, out InstalledUserResourcePack installed,
            out ContentCacheError installError));
        Equal(ContentCacheError.None, installError);
        True(fixture.ContentStore.TrySetEnabledUserResourcePacks(
            [installed.Identity], out ContentCacheError enableError));
        Equal(ContentCacheError.None, enableError);
        fixture.DriveUntil(() =>
            fixture.SessionSnapshot.Presentation.Generation.UserSelections == 1);
        PresentationGenerationSnapshot enabled =
            fixture.SessionSnapshot.Presentation.Generation;
        Equal(semantic, fixture.Content.SemanticActivation);
        Equal(world, fixture.SessionSnapshot.StructuralManifest.ManifestFingerprint);
        Equal(participant, fixture.Playable.GetSnapshot().Participant);

        True(fixture.ContentStore.TryInstallUserResourcePack(
            noOpManifest, noOpPayloads, out InstalledUserResourcePack noOp,
            out ContentCacheError noOpInstallError));
        Equal(ContentCacheError.None, noOpInstallError);
        True(fixture.ContentStore.TrySetEnabledUserResourcePacks(
            [installed.Identity, noOp.Identity], out ContentCacheError noOpEnableError));
        Equal(ContentCacheError.None, noOpEnableError);
        fixture.DriveSteps(4);
        Equal(enabled.Generation,
            fixture.SessionSnapshot.Presentation.Generation.Generation);

        True(fixture.ContentStore.TrySetEnabledUserResourcePacks(
            [], out ContentCacheError disableError));
        Equal(ContentCacheError.None, disableError);
        fixture.DriveUntil(() =>
            fixture.SessionSnapshot.Presentation.Generation.UserSelections == 0 &&
            fixture.SessionSnapshot.Presentation.Generation.RealmSelections == 2);
        PresentationGenerationSnapshot disabled =
            fixture.SessionSnapshot.Presentation.Generation;
        True(enabled.Generation != disabled.Generation);
        Equal(semantic, fixture.Content.SemanticActivation);
        Equal(world, fixture.SessionSnapshot.StructuralManifest.ManifestFingerprint);
        Equal(participant, fixture.Playable.GetSnapshot().Participant);
        Console.WriteLine(
            $"EVIDENCE F1.5 remote_semantic_plan={semantic.PlanHash} " +
            $"remote_semantic_catalog={semantic.ResolvedFingerprint} " +
            $"user_pack={installed.Identity.PackageId}@{installed.Identity.ManifestHash} " +
            $"enabled_generation={enabled.Generation.Value} " +
            $"disabled_generation={disabled.Generation.Value} " +
            "application_epoch_unchanged=true world_unchanged=true participant_unchanged=true");
    }

    private static void ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);

        False(fixture.TrySubmitGuest("Too Early", GameGuestBody.HumanFemale));
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Applied);
        RemoteContentClient content = fixture.Content!;
        RemoteContentClientSnapshot applied = content.GetSnapshot();
        Equal(oracle.Replace.Catalog.Fingerprint, applied.ResolvedCatalogFingerprint);
        Equal(1, applied.Acquisition.PackagesFetched);
        Equal(0, applied.Acquisition.PackagesReused);
        Equal(1, applied.Acquisition.FilesFetched);
        True(applied.Acquisition.BytesFetched > 0);
        Equal(1, source.Fetches);

        fixture.Admit("Remote Content Alice", GameGuestBody.HumanFemale);
        True(fixture.SessionSnapshot.RequiredContentApplied);
        True(fixture.SessionSnapshot.SemanticConsumersActive);
        Equal(oracle.Replace.Catalog.Fingerprint, content.Catalog.Fingerprint);
        Console.WriteLine(
            $"A0.3 COLD packages_fetched={applied.Acquisition.PackagesFetched} " +
            $"files_fetched={applied.Acquisition.FilesFetched} bytes={applied.Acquisition.BytesFetched} " +
            $"fingerprint={applied.ResolvedCatalogFingerprint}");
    }

    private static void ContentAcquisitionRunsOutsideProtocolPumpAndCancelsWithSession()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var completingSource = new CancellableBlockingSource(oracle);
        using (var fixture = RemotePlayableFixture.CreateWithContent(oracle, completingSource))
        {
            fixture.DriveUntil(() => completingSource.Started);
            Equal(RemoteContentClientState.Acquiring, fixture.Content!.GetSnapshot().State);
            False(fixture.SessionSnapshot.RequiredContentApplied);
            False(fixture.TrySubmitGuest("Too Early", GameGuestBody.HumanFemale));

            var pump = Stopwatch.StartNew();
            fixture.DriveSteps(1);
            pump.Stop();
            if (pump.Elapsed >= TimeSpan.FromSeconds(1))
                throw new InvalidOperationException(
                    $"A blocked content source held the protocol pump for {pump.Elapsed}.");
            Equal(ClientDirectConnectState.Established, fixture.DirectState);

            completingSource.Release();
            fixture.DriveUntil(() => fixture.Content!.Applied);
            Equal(1, completingSource.Fetches);
            True(fixture.Content.GetSnapshot().ProofsQueued != 0);
        }

        var cancelledSource = new CancellableBlockingSource(oracle);
        var cancelled = RemotePlayableFixture.CreateWithContent(oracle, cancelledSource);
        cancelled.DriveUntil(() => cancelledSource.Started);
        var disposal = Stopwatch.StartNew();
        cancelled.Dispose();
        disposal.Stop();
        True(cancelledSource.CancellationObserved);
        if (disposal.Elapsed >= TimeSpan.FromSeconds(2))
            throw new InvalidOperationException(
                $"Content acquisition cancellation took {disposal.Elapsed}.");
    }

    private static void WarmCacheReconnectReusesBytesButProvesFreshSession()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        string root = TemporaryDirectory("ultrasim-a03-warm");
        ulong firstEpoch;
        try
        {
            var coldSource = new CabinReplaceSource(oracle);
            using (var first = RemotePlayableFixture.CreateWithContent(
                       oracle, coldSource, root, preserveContentRoot: true))
            {
                first.DriveUntil(() => first.Content!.Applied);
                firstEpoch = first.SessionSnapshot.Content.ApplicationSessionEpoch;
                Equal(1, coldSource.Fetches);
                first.Admit("Cold Guest", GameGuestBody.HumanFemale);
            }

            var warmSource = new CabinReplaceSource(oracle);
            using (var second = RemotePlayableFixture.CreateWithContent(
                       oracle, warmSource, root, preserveContentRoot: true))
            {
                second.DriveUntil(() => second.Content!.Applied);
                RemoteContentClientSnapshot warm = second.Content!.GetSnapshot();
                True(warm.ApplicationSessionEpoch != firstEpoch);
                Equal(0, warmSource.Fetches);
                Equal(0, warm.Acquisition.PackagesFetched);
                Equal(1, warm.Acquisition.PackagesReused);
                True(warm.ProofsQueued >= 1);
                Equal(oracle.Replace.Catalog.Fingerprint, warm.ResolvedCatalogFingerprint);
                second.Admit("Warm Guest", GameGuestBody.HumanMale);
                Console.WriteLine(
                    $"A0.3 WARM packages_reused={warm.Acquisition.PackagesReused} " +
                    $"bytes=0 fresh_epoch={warm.ApplicationSessionEpoch}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void FailedContentAcquisitionCannotUnlockGuestSemantics()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle, fail: true);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.SourceFailure,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        False(fixture.TrySubmitGuest("Blocked Guest", GameGuestBody.HumanFemale));
        fixture.QueueRawGuest("Hostile Bypass", GameGuestBody.HumanFemale);
        fixture.DriveSteps(512);
        Equal(RemotePlayableClientState.AwaitingAdmission, fixture.Playable.GetSnapshot().State);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void PriorSessionContentProofCannotUnlockReconnect()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        RemoteContentSetAppliedMessage stale;
        using (var first = RemotePlayableFixture.CreateWithContent(
                   oracle, new CabinReplaceSource(oracle)))
        {
            first.DriveUntil(() => first.Content!.Applied);
            stale = CreateAppliedProof(oracle, first.Content!.GetSnapshot().ApplicationSessionEpoch);
        }

        using var current = RemotePlayableFixture.CreateWithContent(
            oracle, new CabinReplaceSource(oracle, fail: true));
        current.DriveUntil(() =>
            current.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        True(current.Content!.GetSnapshot().ApplicationSessionEpoch != stale.ApplicationSessionEpoch);
        current.QueueRaw(in stale);
        current.QueueRawGuest("Stale Proof Guest", GameGuestBody.HumanFemale);
        current.DriveSteps(512);
        Equal(RemotePlayableClientState.AwaitingAdmission, current.Playable.GetSnapshot().State);
        False(current.SessionSnapshot.SemanticConsumersActive);
    }

    private static void MismatchedSourceCandidateCannotPublish()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle, wrongPackage: true);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);
        fixture.DriveUntil(() =>
            fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.CandidateMismatch,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        ContentCacheSnapshot cache = fixture.ContentStore!.GetSnapshot();
        Equal(0, cache.BlobCount);
        Equal(0, cache.RealmManifestCount);
        Equal(0, cache.RealmActivationCount);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void RemoteDefaultTransfersNothingAndPreservesGameSemantics()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new RejectingSource();
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Default, [], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(oracle.Default.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal(0, content.Acquisition.PackagesRequired);
        Equal(0, content.Acquisition.PackagesFetched);
        Equal(0, content.Acquisition.FilesFetched);
        Equal(0L, content.Acquisition.BytesFetched);
        Equal(0, source.Fetches);
        AssertCatalogEqual(oracle.Default.Catalog, fixture.Content.Catalog);
        AssertBasicRemoteGameUnaffected(fixture, "Default Guest");
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Default source=none transfer=0 " +
            $"plan={oracle.Default.Plan.PlanHash} fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void RemoteReplaceOverServerHttpMatchesLocalOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/cabin-replace.uspkg", archive)]);
        var acquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/cabin-replace.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Replace.Catalog, fixture.Content!.Catalog);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(ContentSourceKind.ServerHostedStatic, content.Acquisition.SelectedSource.Kind);
        Equal(1, content.Acquisition.PackagesFetched);
        Equal(1, content.Acquisition.FilesFetched);
        Equal(oracle.ReplacePackage.Payloads.Sum(static value => (long)value.Bytes.Length),
            content.Acquisition.BytesFetched);
        Equal((long)archive.Length, source.GetSnapshot().ArchiveBytesRead);
        Equal(1UL, host.GetSnapshot().Served);
        AssertBasicRemoteGameUnaffected(fixture, "HTTP Replace Guest");
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Replace source=ServerHostedStatic " +
            $"package={oracle.ReplacePackage.Manifest.PackageId} manifest={oracle.ReplacePackage.Manifest.ManifestHash} " +
            $"file={oracle.ReplacePackage.Manifest.Files[0].Sha256} " +
            $"plan={oracle.Replace.Plan.PlanHash} " +
            $"payload_bytes={content.Acquisition.BytesFetched} archive_bytes={archive.Length} " +
            $"fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void RemoteReplaceOverExternalHttpsMatchesServerAndLocal()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        ContentHash localFingerprint;
        ContentPackageAcquisition localAcquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.Local, "packages/replace.uspkg");
        string localRoot = TemporaryDirectory("ultrasim-a04-local-source");
        Directory.CreateDirectory(Path.Combine(localRoot, "packages"));
        try
        {
            File.WriteAllBytes(Path.Combine(localRoot, "packages", "replace.uspkg"), archive);
            var localSource = new LocalContentPackageSource(localRoot);
            using var local = RemotePlayableFixture.CreateWithContent(
                oracle, oracle.Replace, [localAcquisition], localSource);
            local.DriveUntil(() => local.Content!.Applied && local.SessionSnapshot.RequiredContentApplied);
            AssertCatalogEqual(oracle.Replace.Catalog, local.Content!.Catalog);
            localFingerprint = local.Content.GetSnapshot().ResolvedCatalogFingerprint;
        }
        finally
        {
            if (Directory.Exists(localRoot)) Directory.Delete(localRoot, recursive: true);
        }

        using var host = new LoopbackHttpsContentHost("/content/cabin-replace.uspkg", archive);
        var externalAcquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ExternalHttps, host.ResourceUri.ToString());
        using var externalSource = new HttpContentPackageSource(
            host.CreatePinnedCertificateValidation(), FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
        using var external = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [externalAcquisition], externalSource);
        external.DriveUntil(() =>
            external.Content!.Applied && external.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Replace.Catalog, external.Content!.Catalog);
        RemoteContentClientSnapshot content = external.Content.GetSnapshot();
        Equal(ContentSourceKind.ExternalHttps, content.Acquisition.SelectedSource.Kind);
        Equal(localFingerprint, content.ResolvedCatalogFingerprint);
        Equal(oracle.Replace.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal((long)archive.Length, externalSource.GetSnapshot().ArchiveBytesRead);
        AssertBasicRemoteGameUnaffected(external, "HTTPS Replace Guest");

        RemoteContentAuthorization localAuthorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [localAcquisition]);
        RemoteContentAuthorization externalAuthorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [externalAcquisition]);
        Equal(localAuthorization.PlanHash, externalAuthorization.PlanHash);
        Equal(localAuthorization.ExpectedResolvedCatalogFingerprint,
            externalAuthorization.ExpectedResolvedCatalogFingerprint);
        True(localAuthorization.Packages[0].Sources[0].Locator !=
            externalAuthorization.Packages[0].Sources[0].Locator);
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Replace source=ExternalHttps archive_bytes={archive.Length} " +
            $"manifest={oracle.ReplacePackage.Manifest.ManifestHash} plan={oracle.Replace.Plan.PlanHash} " +
            $"fingerprint={content.ResolvedCatalogFingerprint} local_source_parity=true");
    }

    private static void RemoteAddOverExternalHttpsMatchesLocalOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.AddPackage);
        using var host = new LoopbackHttpsContentHost("/content/cabin-add.uspkg", archive);
        var acquisition = Acquisition(
            oracle.AddPackage, ContentSourceKind.ExternalHttps, host.ResourceUri.ToString());
        using var source = new HttpContentPackageSource(
            host.CreatePinnedCertificateValidation(), FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Add, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Add.Catalog, fixture.Content!.Catalog);
        foreach (LogicalContentId id in new[]
                 {
                     CabinContentIds.BlueGlowMaterial,
                     CabinContentIds.BlueGlowTexture,
                     CabinContentIds.StreamedBadgeIcon,
                 })
        {
            True(fixture.Content.Catalog.TryGet(id, out ResolvedContentEntry? entry));
            Equal(oracle.AddPackage.Manifest.PackageId, entry!.OwnerPackageId);
        }
        AssertBasicRemoteGameUnaffected(fixture, "HTTPS Add Guest");
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Add source=ExternalHttps files={content.Acquisition.FilesFetched} " +
            $"package={oracle.AddPackage.Manifest.PackageId} manifest={oracle.AddPackage.Manifest.ManifestHash} " +
            $"file0={oracle.AddPackage.Manifest.Files[0].Sha256} " +
            $"file1={oracle.AddPackage.Manifest.Files[1].Sha256} " +
            $"plan={oracle.Add.Plan.PlanHash} " +
            $"payload_bytes={content.Acquisition.BytesFetched} archive_bytes={archive.Length} " +
            $"fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void HttpSourcePolicyAndEnvelopeFailureStayFailClosed()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        archive[^1] ^= 0x7F;
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/damaged.uspkg", archive)]);
        var acquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/damaged.uspkg").ToString());

        using (var denied = new HttpContentPackageSource())
        {
            var deniedRequest = new ContentPackageFetchRequest(
                CabinContentOracle.FixtureRealm, oracle.ReplacePackage.Manifest.PackageId,
                oracle.ReplacePackage.Manifest.ManifestHash, acquisition.Sources[0]);
            False(denied.TryFetch(in deniedRequest, out _, out ContentSourceFetchError policyError));
            Equal(ContentSourceFetchError.InvalidRequest, policyError);
            Equal(0L, denied.GetSnapshot().ArchiveBytesRead);
        }

        using (var bounded = new HttpContentPackageSource(new ContentPackageSourceLimits(
                   maximumArchiveBytes: archive.Length - 1,
                   requestTimeoutMilliseconds: 10_000,
                   allowInsecureServerHostedHttp: true,
                   allowPrivateNetworkTargets: true)))
        {
            var boundedRequest = new ContentPackageFetchRequest(
                CabinContentOracle.FixtureRealm, oracle.ReplacePackage.Manifest.PackageId,
                oracle.ReplacePackage.Manifest.ManifestHash, acquisition.Sources[0]);
            False(bounded.TryFetch(in boundedRequest, out _, out ContentSourceFetchError capacityError));
            Equal(ContentSourceFetchError.CapacityExceeded, capacityError);
            Equal(0L, bounded.GetSnapshot().ArchiveBytesRead);
        }

        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [acquisition], source);
        fixture.DriveUntil(() =>
            fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.SourceFailure,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        Equal(ContentSourceFetchError.SourceFailure,
            fixture.Content.GetSnapshot().Acquisition.SourceError);
        ContentCacheSnapshot cache = fixture.ContentStore!.GetSnapshot();
        Equal(0, cache.BlobCount);
        Equal(0, cache.RealmManifestCount);
        Equal(0, cache.RealmActivationCount);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        True(ContentPackageManifestEnvelope.TryEncodeCanonical(
            oracle.AddPackage.Manifest, out byte[] manifestBytes));
        var resources = new List<StaticContentHttpResource>
        {
            new("/content/add/manifest.usmanifest", manifestBytes),
        };
        foreach (ContentFilePayload payload in oracle.AddPackage.Payloads)
        {
            ContentFileDescriptor descriptor = oracle.AddPackage.Manifest.Files.Single(
                value => value.Path == payload.Path);
            resources.Add(new StaticContentHttpResource(
                $"/content/add/blobs/{descriptor.Sha256}.blob", payload.Bytes));
        }
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()), resources);
        string root = new Uri(host.BaseUri, "content/add/").ToString();
        var acquisition = Acquisition(
            oracle.AddPackage, ContentSourceKind.ServerHostedStatic, root);
        int maximumBytes = Math.Max(manifestBytes.Length,
            oracle.AddPackage.Payloads.Max(static value => value.Bytes.Length));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(maximumBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Add, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Applied);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(oracle.Add.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal(oracle.AddPackage.Payloads.Count, content.Acquisition.FilesFetched);
        Equal(1, content.Acquisition.SourceAttempts);
        Equal(oracle.AddPackage.Payloads.Sum(static value => (long)value.Bytes.Length),
            content.Acquisition.BytesFetched);
        ContentPackageSourceSnapshot sourceSnapshot = source.GetSnapshot();
        Equal((long)manifestBytes.Length, sourceSnapshot.ManifestBytesRead);
        Equal(content.Acquisition.BytesFetched, sourceSnapshot.PayloadBytesRead);
        True(sourceSnapshot.PeakConcurrentRequests is >= 1 and <= 4);
        Console.WriteLine($"EVIDENCE A0.5 granular_http files={content.Acquisition.FilesFetched} " +
            $"payload_bytes={content.Acquisition.BytesFetched} manifest_bytes={sourceSnapshot.ManifestBytesRead} " +
            $"peak_parallel={sourceSnapshot.PeakConcurrentRequests} fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        UiContentFixture fixture = CreateInventoryUiContent(oracle);
        byte[] archive = Archive(fixture.Manifest, fixture.Payloads);
        string localRoot = TemporaryDirectory("ultrasim-a06-local-source");
        string cacheRoot = TemporaryDirectory("ultrasim-a06-warm-cache");
        Directory.CreateDirectory(Path.Combine(localRoot, "packages"));
        try
        {
            File.WriteAllBytes(Path.Combine(localRoot, "packages", "inventory.uspkg"), archive);
            var localAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.Local, "packages/inventory.uspkg");
            var localSource = new LocalContentPackageSource(localRoot);
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, localAcquisition, localSource, cacheRoot,
                out ContentHash localFingerprint, out ContentActivationAcquisitionResult cold,
                preserveCacheRoot: true);
            Equal(1, cold.FilesFetched);

            // Persistent bytes reopen, but catalog/application publication is performed again.
            using (ContentCache warmCache = OpenCache(cacheRoot))
            {
                var unavailable = new RejectingSource();
                True(ContentActivationAcquirer.TryAcquireAndPublish(
                    warmCache, oracle.BaseCatalog, fixture.Plan, [localAcquisition], unavailable,
                    false, out ContentCatalogLease? lease,
                    out ContentActivationAcquisitionResult warm));
                using (lease)
                {
                    Equal(0, warm.FilesFetched);
                    AssertInventoryDefinition(lease!, fixture.Definition);
                }
                Equal(0, unavailable.Fetches);
            }

            using var httpHost = new StaticContentHttpHost(
                new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
                [new StaticContentHttpResource("/content/inventory.uspkg", archive)]);
            var httpAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.ServerHostedStatic,
                httpHost.GetUri("/content/inventory.uspkg").ToString());
            using var httpSource = new HttpContentPackageSource(
                FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, httpAcquisition, httpSource,
                TemporaryDirectory("ultrasim-a06-http-cache"),
                out ContentHash httpFingerprint, out _);

            using var httpsHost = new LoopbackHttpsContentHost(
                "/content/inventory.uspkg", archive);
            var httpsAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.ExternalHttps,
                httpsHost.ResourceUri.ToString());
            using var httpsSource = new HttpContentPackageSource(
                httpsHost.CreatePinnedCertificateValidation(),
                FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, httpsAcquisition, httpsSource,
                TemporaryDirectory("ultrasim-a06-https-cache"),
                out ContentHash httpsFingerprint, out _);

            Equal(fixture.Catalog.Fingerprint, localFingerprint);
            Equal(localFingerprint, httpFingerprint);
            Equal(localFingerprint, httpsFingerprint);
            Console.WriteLine(
                $"EVIDENCE A0.6 ui_definition bytes={fixture.ArtifactBytes.Length} " +
                $"definition={fixture.Definition.Key}/v{fixture.Definition.Version} " +
                $"hash={fixture.Definition.Hash} artifact_sha={ContentHash.Compute(fixture.ArtifactBytes)} " +
                $"manifest={fixture.Manifest.ManifestHash} fingerprint={localFingerprint} " +
                "origins=local,warm-cache,server-http,external-https");
        }
        finally
        {
            if (Directory.Exists(localRoot)) Directory.Delete(localRoot, recursive: true);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static void ContentBackedRemoteInventoryUsesHotOpenAndGameActions()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        UiContentFixture content = CreateInventoryUiContent(oracle);
        byte[] archive = Archive(content.Manifest, content.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/inventory.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            content.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/inventory.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var remote = RemotePlayableFixture.CreateWithContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            content.Plan,
            content.Catalog.Fingerprint,
            [acquisition],
            source,
            [UiDefinitionIdentity.From(content.Definition)]);
        remote.DriveUntil(() =>
            remote.Content!.Applied && remote.SessionSnapshot.RequiredContentApplied);
        remote.Admit("UI Content Guest", GameGuestBody.HumanFemale);
        True(remote.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        remote.DriveUntil(() => remote.UiClient.OpenViewCount == 1);
        RemoteUiClientSnapshot ui = remote.SessionSnapshot.Ui;
        Equal(1UL, ui.ContentDefinitionsResolved);
        Equal(1, remote.UiClient.CachedDefinitionCount);

        var view = new UiViewInstanceId(1);
        _ = remote.UiClient.GetView(view);
        ReadOnlySpan<UiListRow> rows = remote.UiClient.GetRows(view, InventoryUi.Items);
        True(rows.Length != 0);
        ulong before = remote.UiClient.GetView(view).LastSequence;
        remote.UiClient.SubmitAction(view, InventoryUi.Select, rows[0].Id);
        remote.DriveUntil(() => remote.UiClient.GetView(view).LastSequence > before);
        Equal("Item selected.", remote.UiClient.GetValue(view, InventoryUi.Status).Text);
        Console.WriteLine(
            $"EVIDENCE A0.6 remote_inventory hot_content_resolutions={ui.ContentDefinitionsResolved} " +
            $"cached_definitions={remote.UiClient.CachedDefinitionCount} " +
            $"action_model_sequence={remote.UiClient.GetView(view).LastSequence}");
    }

    private static void StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        WorldContentFixture world = CreateConceptWorldContent(oracle);
        byte[] archive = Archive(world.Manifest, world.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/concept-world.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            world.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/concept-world.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        var requiredWorld = new RemoteRequiredWorldBinding(
            world.DefinitionId,
            world.Pack.Fingerprint,
            world.Binding,
            new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096));
        using var remote = RemotePlayableFixture.CreateWithWorldContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            world.Plan,
            world.Catalog.Fingerprint,
            [acquisition],
            source,
            requiredWorld);

        Equal(RemoteStructuralWorldState.AwaitingAppliedCatalog,
            remote.SessionSnapshot.StructuralWorld.State);
        False(remote.TrySubmitGuest("Too Early", GameGuestBody.HumanFemale));
        remote.DriveUntil(() =>
            remote.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        RemotePlayableSessionSnapshot ready = remote.SessionSnapshot;
        True(ready.RequiredContentApplied);
        True(ready.StructuralWorld.Resolution.IsResolved);
        Equal(ClientSemanticChunkError.None, ready.StructuralWorld.PublicationError);
        Equal(world.Pack.Fingerprint, ready.StructuralWorld.WorldPackFingerprint);
        Equal(world.Pack.ContentFingerprint, ready.StructuralWorld.WorldContentFingerprint);
        Equal(world.Pack.ChunkCount, ready.StructuralWorld.PublishedStructuralChunks);
        Equal(world.Pack.ChunkCount, remote.Cache.GetSnapshot().ResidentChunks);
        Equal(1, remote.Content!.GetSnapshot().Acquisition.FilesFetched);
        Equal((long)world.Bytes.Length, remote.Content.GetSnapshot().Acquisition.BytesFetched);

        ClientWorldSemanticCache local = RemotePlayableFixture.CreatePreinstalledWorldCache();
        ClientWorldSemanticCacheSnapshot streamedSnapshot = remote.Cache.GetSnapshot();
        ClientWorldSemanticCacheSnapshot localSnapshot = local.GetSnapshot();
        Equal(localSnapshot.BaseWorldFingerprint, streamedSnapshot.BaseWorldFingerprint);
        Equal(localSnapshot.WorldSemanticHash, streamedSnapshot.WorldSemanticHash);
        Equal(localSnapshot.ResidentChunks, streamedSnapshot.ResidentChunks);
        Equal(local.BaseWorld.FrozenStaticCount, remote.Cache.BaseWorld.FrozenStaticCount);
        Equal(local.BaseWorld.MultiInstanceCount, remote.Cache.BaseWorld.MultiInstanceCount);
        Equal(local.BaseWorld.MultiFragmentCount, remote.Cache.BaseWorld.MultiFragmentCount);
        Equal(local.BaseWorld.AdditionalTerrainSurfaceFragmentCount,
            remote.Cache.BaseWorld.AdditionalTerrainSurfaceFragmentCount);

        remote.Admit("Streamed World Guest", GameGuestBody.HumanFemale);
        remote.DriveUntil(() =>
            remote.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            remote.World.GetSnapshot().AppliedSnapshots != 0);
        Console.WriteLine(
            $"EVIDENCE A0.7 worldpack_bytes={world.Bytes.Length} " +
            $"blob_sha={ContentHash.Compute(world.Bytes)} manifest={world.Manifest.ManifestHash} " +
            $"plan={world.Plan.PlanHash} catalog={world.Catalog.Fingerprint} " +
            $"pack={world.Pack.Fingerprint} " +
            $"content={world.Pack.ContentFingerprint} chunks={world.Pack.ChunkCount} " +
            $"statics={world.Pack.FrozenStaticCount} multis={world.Pack.MultiInstanceCount}/" +
            $"{world.Pack.MultiFragmentCount} surfaces={world.Pack.AdditionalTerrainSurfaceFragmentCount} " +
            $"semantic={streamedSnapshot.WorldSemanticHash} dynamic_snapshots=" +
            $"{remote.World.GetSnapshot().AppliedSnapshots}");
    }

    private static void StreamedWorldPackCapacityFailurePublishesNothing()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        WorldContentFixture world = CreateConceptWorldContent(oracle);
        byte[] archive = Archive(world.Manifest, world.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/concept-world.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            world.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/concept-world.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        var requiredWorld = new RemoteRequiredWorldBinding(
            world.DefinitionId,
            world.Pack.Fingerprint,
            world.Binding,
            new ClientWorldSemanticCacheLimits(
                world.Pack.ChunkCount - 1, 256, 32, 64, 4_096));
        using var remote = RemotePlayableFixture.CreateWithWorldContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            world.Plan,
            world.Catalog.Fingerprint,
            [acquisition],
            source,
            requiredWorld);
        remote.DriveUntil(() =>
            remote.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Rejected);
        RemotePlayableSessionSnapshot rejected = remote.SessionSnapshot;
        True(rejected.RequiredContentApplied);
        True(rejected.StructuralWorld.Resolution.IsResolved);
        Equal(ClientSemanticChunkError.CapacityExceeded,
            rejected.StructuralWorld.PublicationError);
        Equal(0, rejected.StructuralWorld.PublishedStructuralChunks);
        Equal(world.Pack.Fingerprint, rejected.StructuralWorld.WorldPackFingerprint);
        False(remote.TrySubmitGuest("No Partial World", GameGuestBody.HumanFemale));
    }

    private static WorldContentFixture CreateConceptWorldContent(CabinContentOracle oracle)
    {
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        var bytes = new byte[pack.Length];
        pack.CopyTo(bytes);
        True(ContentFilePath.TryCreate(
            "world/concept-demo.usworldpack", out ContentFilePath path));
        True(ContentFileDescriptor.TryCreate(
            path, ContentFileKind.WorldPack, bytes.Length, ContentHash.Compute(bytes),
            default, out ContentFileDescriptor file));
        True(LogicalContentId.TryCreate(
            "server-world:world/concept-demo", out LogicalContentId definitionId));
        True(ContentDefinitionDescriptor.TryCreateFileDefinition(
            definitionId, ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
            path, out ContentDefinitionDescriptor? definition));
        True(ContentPackageId.TryCreate(
            "server.world.concept-demo", out ContentPackageId packageId));
        True(ContentNamespaceId.TryCreate(
            "server-world", out ContentNamespaceId contentNamespace));
        True(ContentPackageManifest.TryCreate(
            1, 1, packageId, contentNamespace,
            "Concept Demo canonical world", "1.0.0",
            ContentTrustClass.ServerAuthorizedPassive,
            [file], [definition!], out ContentPackageManifest? manifest, out _));
        var payloads = new[] { new ContentFilePayload(path, bytes) };
        True(ContentPackageValidator.TryValidatePayloads(
            manifest!, payloads, out VerifiedContentPackage? verified, out _));
        var requirement = new ContentActivationPackage(
            packageId, manifest!.ManifestHash, ContentRequirementPolicy.Required);
        True(ContentActivationPlan.TryCreate(
            CabinContentOracle.FixtureRealm,
            new ContentActivationId(0x4130_2E37_2D57_4F52UL, 1),
            oracle.BaseCatalog.Fingerprint,
            [requirement], out ContentActivationPlan? plan, out _));
        True(ContentCatalogResolver.TryResolve(
            oracle.BaseCatalog, plan!, [verified!], out ResolvedContentCatalog? catalog,
            out ContentCatalogResolutionError resolutionError));
        Equal(ContentCatalogResolutionError.None, resolutionError);
        return new WorldContentFixture(
            definitionId, pack, bytes, CreateWorldBinding(), manifest, payloads, plan!, catalog!);
    }

    private static WorldContentBinding CreateWorldBinding() => new(
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

    private static UiContentFixture CreateInventoryUiContent(CabinContentOracle oracle)
    {
        CompiledUiDefinition definition = InventoryUi.Definition;
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(definition, out byte[] artifact));
        True(ContentUiDefinitionPath.TryCreate(
            UiDefinitionIdentity.From(definition), out ContentFilePath path));
        True(ContentFileDescriptor.TryCreate(
            path, ContentFileKind.UiDefinition, artifact.Length, ContentHash.Compute(artifact),
            default, out ContentFileDescriptor file));
        True(ContentPackageId.TryCreate(
            "server.ui.standard", out ContentPackageId packageId));
        True(ContentNamespaceId.TryCreate(
            "server-ui", out ContentNamespaceId contentNamespace));
        True(ContentPackageManifest.TryCreate(
            1, 1, packageId, contentNamespace,
            "Standard remote UI definitions", "1.0.0",
            ContentTrustClass.ServerAuthorizedPassive,
            [file], [], out ContentPackageManifest? manifest, out _));
        var payloads = new[] { new ContentFilePayload(path, artifact) };
        True(ContentPackageValidator.TryValidatePayloads(
            manifest!, payloads, out VerifiedContentPackage? verified, out _));
        var requirement = new ContentActivationPackage(
            packageId, manifest!.ManifestHash, ContentRequirementPolicy.Required);
        True(ContentActivationPlan.TryCreate(
            CabinContentOracle.FixtureRealm,
            new ContentActivationId(0x4130_2E36_2D55_4900UL, 1),
            oracle.BaseCatalog.Fingerprint,
            [requirement], out ContentActivationPlan? plan, out _));
        True(ContentCatalogResolver.TryResolve(
            oracle.BaseCatalog, plan!, [verified!], out ResolvedContentCatalog? catalog,
            out ContentCatalogResolutionError resolutionError));
        Equal(ContentCatalogResolutionError.None, resolutionError);
        return new UiContentFixture(definition, artifact, manifest, payloads, plan!, catalog!);
    }

    private static void ResolveUiDefinition(
        ContentBaseCatalog baseCatalog,
        UiContentFixture fixture,
        ContentPackageAcquisition acquisition,
        IContentPackageSource source,
        string cacheRoot,
        out ContentHash fingerprint,
        out ContentActivationAcquisitionResult acquisitionResult,
        bool preserveCacheRoot = false)
    {
        try
        {
            using ContentCache cache = OpenCache(cacheRoot);
            True(ContentActivationAcquirer.TryAcquireAndPublish(
                cache, baseCatalog, fixture.Plan, [acquisition], source,
                false, out ContentCatalogLease? lease, out acquisitionResult));
            using (lease)
            {
                fingerprint = lease!.Catalog.Fingerprint;
                AssertInventoryDefinition(lease, fixture.Definition);
            }
        }
        finally
        {
            if (!preserveCacheRoot && Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static void AssertInventoryDefinition(
        ContentCatalogLease lease,
        CompiledUiDefinition expected)
    {
        True(ContentUiDefinitionResolver.TryResolve(
            lease, UiDefinitionIdentity.From(expected), out CompiledUiDefinition? actual,
            out ContentUiDefinitionResolutionError error));
        Equal(ContentUiDefinitionResolutionError.None, error);
        Equal(UiDefinitionIdentity.From(expected), UiDefinitionIdentity.From(actual!));
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(actual!, out byte[] actualBytes));
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(expected, out byte[] expectedBytes));
        True(actualBytes.AsSpan().SequenceEqual(expectedBytes));
    }

    private static ContentCache OpenCache(string root)
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError error));
        Equal(ContentCacheError.None, error);
        return cache!;
    }

    private static byte[] Archive(CabinLocalContentPackage package)
    {
        True(ContentPackageArchive.TryEncodeCanonical(
            package.Manifest, package.Payloads, out byte[] archive));
        return archive;
    }

    private static byte[] Archive(
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload> payloads)
    {
        True(ContentPackageArchive.TryEncodeCanonical(manifest, payloads, out byte[] archive));
        return archive;
    }

    private static ContentPackageAcquisition Acquisition(
        CabinLocalContentPackage package,
        ContentSourceKind kind,
        string locator) => new(
            package.Manifest.PackageId,
            package.Manifest.ManifestHash,
            [new ContentSourceDescriptor(kind, locator)]);

    private static ContentPackageAcquisition Acquisition(
        ContentPackageManifest package,
        ContentSourceKind kind,
        string locator) => new(
            package.PackageId,
            package.ManifestHash,
            [new ContentSourceDescriptor(kind, locator)]);

    private static ContentPackageSourceLimits FixtureHttpLimits(
        int archiveBytes,
        bool allowInsecureHttp) => new(
            maximumArchiveBytes: archiveBytes,
            requestTimeoutMilliseconds: 10_000,
            allowInsecureServerHostedHttp: allowInsecureHttp,
            allowPrivateNetworkTargets: true);

    private static void AssertCatalogEqual(
        ResolvedContentCatalog expected,
        ResolvedContentCatalog actual)
    {
        Equal(expected.Fingerprint, actual.Fingerprint);
        Equal(expected.PlanHash, actual.PlanHash);
        Equal(expected.RequiredBaseCatalogFingerprint, actual.RequiredBaseCatalogFingerprint);
        Equal(expected.Entries.Count, actual.Entries.Count);
        for (int index = 0; index < expected.Entries.Count; index++)
        {
            ResolvedContentEntry left = expected.Entries[index];
            ResolvedContentEntry right = actual.Entries[index];
            Equal(left.Id, right.Id);
            Equal(left.Kind, right.Kind);
            Equal(left.OwnerPackageId, right.OwnerPackageId);
            Equal(left.OwnerManifestHash, right.OwnerManifestHash);
            Equal(left.Definition.DefinitionHash, right.Definition.DefinitionHash);
            True(left.ReferencedFiles.SequenceEqual(right.ReferencedFiles));
        }
    }

    private static void AssertBasicRemoteGameUnaffected(
        RemotePlayableFixture fixture,
        string displayName)
    {
        fixture.Admit(displayName, GameGuestBody.HumanFemale);
        fixture.DriveUntil(() =>
            fixture.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.CabinDoor, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.WorldChest, out _));
        WorldPosition before = fixture.Playable.GetSnapshot().Position;
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None, true)));
        ulong movement = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence >= movement &&
            fixture.Playable.GetSnapshot().Position.GlobalXMillimeters > before.GlobalXMillimeters);
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        fixture.DriveUntil(() => fixture.UiClient.OpenViewCount == 1);
        True(fixture.Chat.TrySubmit("content parity", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 1);
    }

    private static (ContentPackageManifest Manifest, ContentFilePayload[] Payloads)
        CreateF15UserActorPack(
            string packageValue = "user.remote-proof-x",
            string logicalValue = ConceptDemoActorPresentationProfile.HumanXDefinitionId)
    {
        string? repository = Directory.GetCurrentDirectory();
        while (repository is not null &&
               !File.Exists(Path.Combine(repository, "verify.sh")))
            repository = Directory.GetParent(repository)?.FullName;
        if (repository is null)
            throw new InvalidOperationException("The repository root was not found.");
        byte[] archive = File.ReadAllBytes(Path.Combine(
            repository, "Client", "OfficialContent", OfficialLocalPresentation.ArchiveFileName));
        True(OfficialLocalPresentation.TryDecodeArchive(
            archive, out FetchedContentPackage? official));
        byte[] actor = official!.Payloads.Single(payload => string.Equals(
            payload.Path.Value, OfficialLocalPresentation.HumanXPath,
            StringComparison.Ordinal)).Bytes.ToArray();
        True(ContentPackageId.TryCreate(packageValue, out ContentPackageId packageId));
        True(ContentNamespaceId.TryCreate("userpack", out ContentNamespaceId contentNamespace));
        True(ContentFilePath.TryCreate("actors/remote-proof-x.glb", out ContentFilePath path));
        True(ContentFileDescriptor.TryCreate(
            path, ContentFileKind.ActorGltfBinary, actor.Length, ContentHash.Compute(actor),
            default, out ContentFileDescriptor file));
        True(LogicalContentId.TryCreate(logicalValue, out LogicalContentId logicalId));
        True(ContentDefinitionDescriptor.TryCreateFileDefinition(
            logicalId, ContentDefinitionKind.ActorPresentation,
            ContentDefinitionOperation.Replace, path,
            out ContentDefinitionDescriptor? definition));
        True(ContentPackageManifest.TryCreateUserResourcePack(
            1, 1, packageId, contentNamespace,
            "Remote semantic-invariance user presentation", "1",
            [file], [definition!], out ContentPackageManifest? manifest, out _));
        return (manifest!, [new ContentFilePayload(path, actor)]);
    }

    private static int ReserveLoopbackPort()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        return port;
    }

    private static RemoteContentSetAppliedMessage CreateAppliedProof(
        CabinContentOracle oracle,
        ulong applicationSessionEpoch)
    {
        RemoteContentHash plan = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Plan.PlanHash);
        RemoteContentHash resolved = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Catalog.Fingerprint);
        return new RemoteContentSetAppliedMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            RequirementSequence = 1,
            ApplicationRealmHigh = CabinContentOracle.FixtureRealm.High,
            ApplicationRealmLow = CabinContentOracle.FixtureRealm.Low,
            ActivationHigh = oracle.Replace.Plan.Activation.High,
            ActivationLow = oracle.Replace.Plan.Activation.Low,
            PlanHash0 = plan.Part0,
            PlanHash1 = plan.Part1,
            PlanHash2 = plan.Part2,
            PlanHash3 = plan.Part3,
            ResolvedHash0 = resolved.Part0,
            ResolvedHash1 = resolved.Part1,
            ResolvedHash2 = resolved.Part2,
            ResolvedHash3 = resolved.Part3,
            Status = RemoteContentApplyStatus.Applied,
        };
    }

    private static ContentPackageAcquisition RemoteReplaceAcquisition(CabinContentOracle oracle) =>
        new(
            oracle.ReplacePackage.Manifest.PackageId,
            oracle.ReplacePackage.Manifest.ManifestHash,
            [new ContentSourceDescriptor(
                ContentSourceKind.ExternalHttps,
                "https://content.example/ultrasim/cabin/replace")]);

    private static string TemporaryDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"{prefix}-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected false.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private sealed record UiContentFixture(
        CompiledUiDefinition Definition,
        byte[] ArtifactBytes,
        ContentPackageManifest Manifest,
        ContentFilePayload[] Payloads,
        ContentActivationPlan Plan,
        ResolvedContentCatalog Catalog);

    private sealed record WorldContentFixture(
        LogicalContentId DefinitionId,
        WorldPack Pack,
        byte[] Bytes,
        WorldContentBinding Binding,
        ContentPackageManifest Manifest,
        ContentFilePayload[] Payloads,
        ContentActivationPlan Plan,
        ResolvedContentCatalog Catalog);

    private sealed class CabinReplaceSource : IContentPackageSource
    {
        private readonly CabinContentOracle _oracle;
        private readonly bool _fail;
        private readonly bool _wrongPackage;

        internal CabinReplaceSource(
            CabinContentOracle oracle,
            bool fail = false,
            bool wrongPackage = false)
        {
            _oracle = oracle;
            _fail = fail;
            _wrongPackage = wrongPackage;
        }

        internal int Fetches { get; private set; }

        public bool CanFetch(ContentSourceKind kind) => kind == ContentSourceKind.ExternalHttps;

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error)
        {
            Fetches++;
            package = null;
            if (_fail)
            {
                error = ContentSourceFetchError.Unavailable;
                return false;
            }
            if (!request.IsValid || request.PackageId != _oracle.ReplacePackage.Manifest.PackageId ||
                request.ManifestHash != _oracle.ReplacePackage.Manifest.ManifestHash ||
                request.Source.Kind != ContentSourceKind.ExternalHttps ||
                request.Source.Locator != "https://content.example/ultrasim/cabin/replace")
            {
                error = ContentSourceFetchError.InvalidRequest;
                return false;
            }
            package = _wrongPackage
                ? new FetchedContentPackage(
                    _oracle.AddPackage.Manifest, _oracle.AddPackage.Payloads)
                : new FetchedContentPackage(
                    _oracle.ReplacePackage.Manifest, _oracle.ReplacePackage.Payloads);
            error = ContentSourceFetchError.None;
            return true;
        }
    }

    private sealed class RejectingSource : IContentPackageSource
    {
        internal int Fetches { get; private set; }

        public bool CanFetch(ContentSourceKind kind) => false;

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error)
        {
            Fetches++;
            package = null;
            error = ContentSourceFetchError.UnsupportedSource;
            return false;
        }
    }

    private sealed class RejectEastPackSource : IContentPackageSource
    {
        private readonly HttpContentPackageSource _http;
        private readonly ContentPackageId _east;

        internal RejectEastPackSource(HttpContentPackageSource http, ContentPackageId east)
        {
            _http = http;
            _east = east;
        }

        public bool CanFetch(ContentSourceKind kind) => _http.CanFetch(kind);

        public bool TryFetch(in ContentPackageFetchRequest request,
            out FetchedContentPackage? package, out ContentSourceFetchError error) =>
            TryFetch(in request, CancellationToken.None, out package, out error);

        public bool TryFetch(in ContentPackageFetchRequest request,
            CancellationToken cancellationToken,
            out FetchedContentPackage? package, out ContentSourceFetchError error)
        {
            if (request.PackageId == _east)
            {
                package = null;
                error = ContentSourceFetchError.Unavailable;
                return false;
            }
            return _http.TryFetch(in request, cancellationToken, out package, out error);
        }
    }

    private sealed class CancellableEastPackSource : IContentPackageSource, IDisposable
    {
        private readonly HttpContentPackageSource _http;
        private readonly ContentPackageId _east;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _cancellationObserved;
        private int _eastFetchCompleted;

        internal CancellableEastPackSource(HttpContentPackageSource http, ContentPackageId east)
        {
            _http = http;
            _east = east;
        }

        internal bool EastFetchStarted => _started.IsSet;
        internal bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;
        internal bool EastFetchCompleted => Volatile.Read(ref _eastFetchCompleted) != 0;
        public bool CanFetch(ContentSourceKind kind) => _http.CanFetch(kind);

        public bool TryFetch(in ContentPackageFetchRequest request,
            out FetchedContentPackage? package, out ContentSourceFetchError error) =>
            TryFetch(in request, CancellationToken.None, out package, out error);

        public bool TryFetch(in ContentPackageFetchRequest request,
            CancellationToken cancellationToken,
            out FetchedContentPackage? package, out ContentSourceFetchError error)
        {
            if (request.PackageId == _east)
            {
                _started.Set();
                try { _release.Wait(cancellationToken); }
                catch (OperationCanceledException)
                {
                    Volatile.Write(ref _cancellationObserved, 1);
                    throw;
                }
                Interlocked.Exchange(ref _eastFetchCompleted, 1);
            }
            return _http.TryFetch(in request, cancellationToken, out package, out error);
        }

        public void Dispose()
        {
            _release.Set();
            _started.Dispose();
            _release.Dispose();
        }
    }

    private sealed class BlockedActorPackageSource : IContentPackageSource, IDisposable
    {
        private readonly HttpContentPackageSource _http;
        private readonly string _semanticPackage;
        private readonly ManualResetEventSlim _artStarted = new(false);
        private readonly ManualResetEventSlim _artRelease = new(false);

        private readonly bool _rejectArt;
        private readonly bool _corruptArt;

        internal BlockedActorPackageSource(
            HttpContentPackageSource http, string semanticPackage,
            bool rejectArt = false, bool corruptArt = false)
        {
            if (rejectArt && corruptArt) throw new ArgumentException("Choose one art fault.");
            _http = http;
            _semanticPackage = semanticPackage;
            _rejectArt = rejectArt;
            _corruptArt = corruptArt;
        }

        internal bool ArtFetchStarted => _artStarted.IsSet;
        internal void ReleaseArt() => _artRelease.Set();
        public bool CanFetch(ContentSourceKind kind) => _http.CanFetch(kind);

        public bool TryFetch(in ContentPackageFetchRequest request,
            out FetchedContentPackage? package, out ContentSourceFetchError error) =>
            TryFetch(in request, CancellationToken.None, out package, out error);

        public bool TryFetch(in ContentPackageFetchRequest request,
            CancellationToken cancellationToken,
            out FetchedContentPackage? package, out ContentSourceFetchError error)
        {
            bool actorPackage = request.PackageId.Value != _semanticPackage &&
                !request.PackageId.Value!.StartsWith(
                    "server.concept-demo.pack-", StringComparison.Ordinal);
            if (actorPackage)
            {
                _artStarted.Set();
                if (_rejectArt)
                {
                    package = null;
                    error = ContentSourceFetchError.Unavailable;
                    return false;
                }
                if (!_corruptArt) _artRelease.Wait(cancellationToken);
            }
            if (!_http.TryFetch(in request, cancellationToken, out package, out error))
                return false;
            if (_corruptArt && actorPackage)
            {
                ContentFilePayload[] payloads = package!.Payloads.ToArray();
                byte[] bytes = payloads[0].Bytes.ToArray();
                bytes[^1] ^= 0x80;
                payloads[0] = new ContentFilePayload(payloads[0].Path, bytes);
                package = new FetchedContentPackage(package.Manifest, payloads);
            }
            return true;
        }

        public void Dispose()
        {
            _artRelease.Set();
            _artStarted.Dispose();
            _artRelease.Dispose();
        }
    }

    private sealed class CancellableBlockingSource : IContentPackageSource
    {
        private readonly CabinContentOracle _oracle;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _fetches;
        private int _cancellationObserved;

        internal CancellableBlockingSource(CabinContentOracle oracle) => _oracle = oracle;

        internal bool Started => _started.IsSet;
        internal bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;
        internal int Fetches => Volatile.Read(ref _fetches);
        internal void Release() => _release.Set();

        public bool CanFetch(ContentSourceKind kind) => kind == ContentSourceKind.ExternalHttps;

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error) =>
            TryFetch(in request, CancellationToken.None, out package, out error);

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            CancellationToken cancellationToken,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error)
        {
            Interlocked.Increment(ref _fetches);
            _started.Set();
            try
            {
                _release.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref _cancellationObserved, 1);
                throw;
            }
            package = new FetchedContentPackage(
                _oracle.ReplacePackage.Manifest, _oracle.ReplacePackage.Payloads);
            error = ContentSourceFetchError.None;
            return true;
        }
    }

    private sealed class CapturingApplicationFactory(
        IHostedUdpSessionApplicationFactory inner) : IHostedUdpSessionApplicationFactory
    {
        internal RemoteGuestPlayableApplication? Application { get; private set; }

        public IHostedUdpSessionApplication Create(
            ConnectionId connection, NetworkProtocolPeer peer)
        {
            IHostedUdpSessionApplication application = inner.Create(connection, peer);
            Application = application as RemoteGuestPlayableApplication ??
                throw new InvalidOperationException("The shared Game application was not captured.");
            return application;
        }
    }

    private sealed class RemotePlayableFixture : IDisposable
    {
        private static readonly NetworkProtocolLimits Protocol = NetworkProtocolLimits.Default;
        private static readonly UdpProtocolLimits Udp = new(
            MaximumDatagramBytes: 1_472,
            SocketSendBufferBytes: 128 * 1024,
            SocketReceiveBufferBytes: 128 * 1024,
            MaximumHandshakePumps: 4_096,
            CookieBucketMilliseconds: 1_000,
            IdleTimeoutMilliseconds: 30_000,
            DrainTimeoutMilliseconds: 1_000,
            KeepAliveIntervalMilliseconds: 5_000);
        private static readonly SharedUdpProtocolLimits Shared = new(
            MaximumSessions: 1,
            MaximumQueuedDatagramsPerSession: 64,
            MaximumInboundDatagramsPerPump: 256,
            SocketIoMode: SharedUdpSocketIoMode.Scalar,
            AggregateSocketSendBufferBytes: 256 * 1024,
            AggregateSocketReceiveBufferBytes: 256 * 1024);
        private static readonly HostedUdpAdmissionLimits Admission = new(
            MaximumValidatedCandidates: 2,
            MaximumTrackedSourceAddresses: 8,
            MaximumChallengesPerAddressPerBucket: 64,
            MaximumValidationsPerAddressPerBucket: 64,
            ValidatedCandidateTimeoutMilliseconds: 5_000);
        private static readonly HostedUdpOwnerShardingLimits Owners = new(
            MaximumOwners: 1,
            MaximumConnections: 1,
            MaximumPacketSlots: 256,
            MaximumQueuedPacketsPerOwner: 128,
            MaximumDatagramBytes: 1_472);

        private readonly string _trustDirectory;
        private readonly NetworkServerIdentity _identity;
        private readonly HostedUdpAdmissionHost _host;
        private readonly SharedPlayableAuthority? _authority;
        private readonly CapturingApplicationFactory? _applicationCapture;
        private readonly ClientDirectConnectSession _direct;
        private readonly RemotePlayableSession _session;
        private readonly F18aUdpMotionProxy? _motionProxy;
        private readonly ContentCache? _contentCache;
        private readonly string? _contentRoot;
        private readonly bool _preserveContentRoot;
        private bool _trackStructuralPresentation;
        private ulong _trackedOwnerStateSequence;
        private int _trackedPreparedPackCount;
        private bool _disposed;

        private RemotePlayableFixture(
            string trustDirectory,
            NetworkServerIdentity identity,
            HostedUdpAdmissionHost host,
            ClientDirectConnectSession direct,
            RemotePlayableSession session,
            ContentCache? contentCache = null,
            string? contentRoot = null,
            bool preserveContentRoot = false,
            F18aUdpMotionProxy? motionProxy = null,
            SharedPlayableAuthority? authority = null,
            CapturingApplicationFactory? applicationCapture = null)
        {
            _trustDirectory = trustDirectory;
            _identity = identity;
            _host = host;
            _authority = authority;
            _applicationCapture = applicationCapture;
            _direct = direct;
            _session = session;
            _motionProxy = motionProxy;
            _contentCache = contentCache;
            _contentRoot = contentRoot;
            _preserveContentRoot = preserveContentRoot;
        }

        internal RemotePlayableClient Playable => _session.Playable;
        internal RemoteChunkWorldStateClient World => _session.World;
        internal ClientChatState ChatState => _session.ChatState;
        internal RemoteChatClient Chat => _session.Chat;
        internal ClientPublicPresentationMetadataState PublicPresentationState =>
            _session.PublicPresentationState;
        internal UiClientRuntime UiClient => _session.UiClient;
        internal RemoteContentClient? Content => _session.Content;
        internal RemotePresentationClient? Presentation => _session.Presentation;
        internal ClientDirectConnectState DirectState => _direct.State;
        internal ContentCache? ContentStore => _contentCache;
        internal ClientWorldSemanticCache Cache => _session.WorldCache;
        internal RemoteStructuralPackClient StructuralPacks =>
            _session.StructuralPacks ?? throw new InvalidOperationException("No structural Packs.");
        internal NetworkProtocolPeer ClientPeer => _direct.Peer ??
            throw new InvalidOperationException("The authenticated client peer is unavailable.");
        internal RemotePlayableSessionSnapshot SessionSnapshot => _session.GetSnapshot();
        internal RemoteGuestPlayableServerSnapshot ServerSnapshot =>
            _applicationCapture?.Application?.GetSnapshot() ??
            throw new InvalidOperationException("No captured shared application exists.");
        internal bool ServerSubscribedTo(SpatialChunkKey key) =>
            _applicationCapture?.Application?.IsSubscribedTo(key) ?? false;
        internal WorldPosition ServerInterestAnchor =>
            _applicationCapture?.Application is { } application &&
            application.TryGetPublicInterestAnchor(out WorldPosition anchor)
                ? anchor : default;
        internal int AuthoritativePublicCount(SpatialChunkKey key)
        {
            SharedPlayableAuthority authority = _authority ??
                throw new InvalidOperationException("No shared authority exists.");
            var rows = new WorldPublicRecord[64];
            True(authority.Runtime.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, int>(
                (world, generation, game) =>
                {
                    ChunkPublicationReadStatus status = game.WorldState.Publications
                        .TryCopySnapshot(key, rows, out ChunkPublicationBatch batch);
                    return status == ChunkPublicationReadStatus.Snapshot ? batch.Count : 0;
                }, out int count));
            return count;
        }
        internal long MotionForwarded => _motionProxy?.Forwarded ?? 0;
        internal long MotionDropped => _motionProxy?.Dropped ?? 0;

        internal void EnableStructuralTracking() => _trackStructuralPresentation = true;

        internal void RelocateForGateSetup(in WorldPosition position)
        {
            SharedPlayableAuthority authority = _authority ??
                throw new InvalidOperationException("The gate setup requires shared authority.");
            var participants = new SharedPlayableParticipant[8];
            Equal(1, authority.CopyParticipants(participants));
            WorldPosition requested = position;
            True(authority.Runtime.Game.TryUseActiveGeneration<
                IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, generation, game) => game.WorldActions.RelocateActor(
                    world, game.WorldState, participants[0].Entities.Player,
                    requested, out _), out GameWorldActionStatus status));
            Equal(GameWorldActionStatus.Accepted, status);
        }

        internal static RemotePlayableFixture Create()
            => CreateCore(new UltraSim.Game.Application.ApplicationRealmId(0xF03B, 1));

        internal static RemotePlayableFixture CreateMotionImpaired(
            int oneWayDelayMilliseconds, int jitterMilliseconds = 0, int dropPeriod = 0)
            => CreateCore(new UltraSim.Game.Application.ApplicationRealmId(0xF18A, 1),
                motionDelayMilliseconds: oneWayDelayMilliseconds,
                motionJitterMilliseconds: jitterMilliseconds,
                motionDropPeriod: dropPeriod);

        internal static RemotePlayableFixture CreateWithStandardUi()
            => CreateCore(
                new UltraSim.Game.Application.ApplicationRealmId(0xF050, 1),
                standardUiDefinitions:
                    [UiDefinitionIdentity.From(InventoryUi.Definition)],
                standardUiDefinitionResolver: ClientStandardUiLibrary.Resolve);

        internal static RemotePlayableFixture CreateWithConceptDeployment(
            ApplicationRealmId realm,
            ConceptDemoContentDeployment deployment,
            IContentPackageSource source,
            bool installLocalPresentation = false,
            bool canonicalStructural = true,
            bool sharedAuthority = false)
        {
            string root = TemporaryDirectory("ultrasim-f12-cache");
            if (!ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError error))
                throw new InvalidOperationException($"The F1.2 cache could not open: {error}.");
            try
            {
                ContentScopeActivationKey? localKey = null;
                if (installLocalPresentation)
                {
                    True(RemoteContentAuthorizationProjection.TryProject(
                        deployment.PresentationAuthorization,
                        out ContentActivationPlan? realmPresentation,
                        out ContentPackageAcquisition[] actorSources));
                    True(ContentActivationPlan.TryCreate(
                        ContentScopeKey.Local,
                        new ContentActivationId(0xF12, 0x4),
                        ContentBaseCatalog.Empty.Fingerprint,
                        realmPresentation!.Packages,
                        out ContentActivationPlan? localPlan, out _));
                    using var localHttp = new HttpContentPackageSource(
                        FixtureHttpLimits(deployment.Snapshot.ArchiveBytes,
                            allowInsecureHttp: true));
                    True(ContentActivationAcquirer.TryAcquireAndPublish(
                        cache!, ContentBaseCatalog.Empty, localPlan!, actorSources,
                        localHttp, persistentlyPinned: false,
                        out ContentCatalogLease? localCatalog,
                        out ContentActivationAcquisitionResult result));
                    localCatalog!.Dispose();
                    localKey = new ContentScopeActivationKey(
                        localPlan!.ScopeActivation, localPlan.PlanHash);
                }
                var binding = new RemoteRequiredContentBinding(
                    realm, cache!, ContentBaseCatalog.Empty, source, localKey);
                return CreateCore(realm, deployment.Authorization, binding,
                    cache, root, preserveContentRoot: false,
                    contentBackedUiDefinitions:
                    [UiDefinitionIdentity.From(InventoryUi.Definition),
                     UiDefinitionIdentity.From(ServerMonitorUi.Definition)],
                    requiredWorld: canonicalStructural ? null :
                        RemoteConceptDemoWorldProfile.CreateRequiredBinding(),
                    canonicalWorld: canonicalStructural ?
                        RemoteConceptDemoWorldProfile.CreateStructuralBinding() : null,
                    presentationAuthorization: deployment.PresentationAuthorization,
                    structuralAuthorization: deployment.StructuralAuthorization,
                    sharedAuthority: sharedAuthority);
            }
            catch
            {
                cache!.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                throw;
            }
        }

        internal static RemotePlayableFixture CreateWithContent(
            CabinContentOracle oracle,
            IContentPackageSource source,
            string? contentRoot = null,
            bool preserveContentRoot = false)
            => CreateWithContent(
                oracle, oracle.Replace, [RemoteReplaceAcquisition(oracle)], source,
                contentRoot, preserveContentRoot);

        internal static RemotePlayableFixture CreateWithContent(
            CabinContentOracle oracle,
            CabinResolvedContent expected,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            string? contentRoot = null,
            bool preserveContentRoot = false)
        {
            contentRoot ??= TemporaryDirectory("ultrasim-a03-cache");
            if (!ContentCache.TryOpen(contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.3 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    expected.Plan, expected.Catalog.Fingerprint, acquisitions);
                var binding = new RemoteRequiredContentBinding(
                    CabinContentOracle.FixtureRealm, contentCache!, oracle.BaseCatalog, source);
                return CreateCore(
                    CabinContentOracle.FixtureRealm, authorization, binding,
                    contentCache, contentRoot, preserveContentRoot);
            }
            catch
            {
                contentCache!.Dispose();
                if (!preserveContentRoot && Directory.Exists(contentRoot))
                    Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        internal static RemotePlayableFixture CreateWithContent(
            UltraSim.Game.Application.ApplicationRealmId realm,
            ContentBaseCatalog baseCatalog,
            ContentActivationPlan plan,
            ContentHash expectedFingerprint,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            IEnumerable<UiDefinitionIdentity> contentBackedUiDefinitions)
        {
            string contentRoot = TemporaryDirectory("ultrasim-a06-cache");
            if (!ContentCache.TryOpen(
                    contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.6 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    plan, expectedFingerprint, acquisitions);
                var binding = new RemoteRequiredContentBinding(
                    realm, contentCache!, baseCatalog, source);
                return CreateCore(
                    realm, authorization, binding, contentCache, contentRoot,
                    preserveContentRoot: false,
                    contentBackedUiDefinitions: contentBackedUiDefinitions);
            }
            catch
            {
                contentCache!.Dispose();
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        internal static RemotePlayableFixture CreateWithWorldContent(
            UltraSim.Game.Application.ApplicationRealmId realm,
            ContentBaseCatalog baseCatalog,
            ContentActivationPlan plan,
            ContentHash expectedFingerprint,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            RemoteRequiredWorldBinding requiredWorld)
        {
            string contentRoot = TemporaryDirectory("ultrasim-a07-cache");
            if (!ContentCache.TryOpen(
                    contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.7 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    plan, expectedFingerprint, acquisitions);
                var contentBinding = new RemoteRequiredContentBinding(
                    realm, contentCache!, baseCatalog, source);
                return CreateCore(
                    realm, authorization, contentBinding, contentCache, contentRoot,
                    preserveContentRoot: false,
                    requiredWorld: requiredWorld);
            }
            catch
            {
                contentCache!.Dispose();
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        private static RemotePlayableFixture CreateCore(
            UltraSim.Game.Application.ApplicationRealmId realm,
            RemoteContentAuthorization? contentAuthorization = null,
            RemoteRequiredContentBinding? requiredContent = null,
            ContentCache? contentCache = null,
            string? contentRoot = null,
            bool preserveContentRoot = false,
            IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
            RemoteRequiredWorldBinding? requiredWorld = null,
            RemoteRequiredStructuralWorldBinding? canonicalWorld = null,
            IEnumerable<UiDefinitionIdentity>? standardUiDefinitions = null,
            Func<UiDefinitionCacheKey, CompiledUiDefinition?>? standardUiDefinitionResolver = null,
            RemoteContentAuthorization? presentationAuthorization = null,
            RemoteStructuralWorldAuthorization? structuralAuthorization = null,
            int motionDelayMilliseconds = -1,
            int motionJitterMilliseconds = 0,
            int motionDropPeriod = 0,
            bool sharedAuthority = false)
        {
            string trustDirectory = Path.Combine(Path.GetTempPath(),
                $"ultrasim-f03b-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(trustDirectory);
            var identity = NetworkServerIdentity.CreateEphemeral();
            SharedPlayableAuthority? authority = sharedAuthority
                ? SharedPlayableAuthority.CreateConceptDemo() : null;
            try
            {
                IHostedUdpSessionApplicationFactory applications = authority is null
                    ? new RemoteGuestPlayableApplicationFactory(
                        realm, contentAuthorization, contentBackedUiDefinitions,
                        standardUiDefinitions: standardUiDefinitions,
                        presentationAuthorization: presentationAuthorization,
                        structuralAuthorization: structuralAuthorization)
                    : new SharedRemoteGuestPlayableApplicationFactory(
                        authority, realm, contentAuthorization,
                        contentBackedUiDefinitions,
                        standardUiDefinitions: standardUiDefinitions,
                        presentationAuthorization: presentationAuthorization,
                        structuralAuthorization: structuralAuthorization);
                CapturingApplicationFactory? captured = authority is null ? null :
                    new CapturingApplicationFactory(applications);
                if (captured is not null) applications = captured;
                IPEndPoint bind = new(IPAddress.Loopback, 0);
                var host = authority is null
                    ? new HostedUdpAdmissionHost(bind, identity, Protocol, Udp, Shared,
                        Admission, Owners, applications, UltraSimGameProtocol.Manifest)
                    : new HostedUdpAdmissionHost(bind, identity, Protocol, Udp, Shared,
                        Admission, applications, UltraSimGameProtocol.Manifest);
                try
                {
                    F18aUdpMotionProxy? motionProxy = motionDelayMilliseconds < 0 ? null :
                        new F18aUdpMotionProxy(host.LocalEndPoint,
                            motionDelayMilliseconds, motionJitterMilliseconds,
                            motionDropPeriod);
                    var trust = new ClientServerTrustRegistry();
                    var store = new ClientServerTrustFileStore(
                        Path.Combine(trustDirectory, "servers.json"));
                    var direct = new ClientDirectConnectSession(trust, store, Protocol, Udp);
                    try
                    {
                        ClientDeploymentKeyFingerprint fingerprint =
                            ClientDeploymentKeyFingerprint.Compute(identity.PublicKey);
                        True(direct.TryStart((motionProxy?.EndPoint ?? host.LocalEndPoint).ToString(),
                            fingerprint.ToString()));
                        DriveDirectUntil(host, direct, ClientDirectConnectState.Established,
                            delayedPath: motionDelayMilliseconds > 0);
                        ClientDirectConnectSnapshot connected = direct.GetSnapshot();
                        NetworkProtocolPeer peer = direct.Peer ??
                            throw new InvalidOperationException("The established client peer was absent.");
                        RemotePlayableSession session;
                        if (requiredWorld is null && canonicalWorld is null)
                        {
                            ClientWorldSemanticCache cache = CreatePreinstalledWorldCache();
                            session = new RemotePlayableSession(
                                peer,
                                connected.ApplicationSessionEpoch,
                                cache,
                                new NullRenderer(),
                                new ClientChatLimits(16, 256, 128),
                                new UiClientLimits(8, 8),
                                maximumPendingWorldProofs: 256,
                                requiredContent: requiredContent,
                                standardUiDefinitionResolver: standardUiDefinitionResolver);
                        }
                        else if (canonicalWorld is not null)
                        {
                            session = new RemotePlayableSession(
                                peer,
                                connected.ApplicationSessionEpoch,
                                requiredContent ?? throw new InvalidOperationException(
                                    "The canonical World requires a content binding."),
                                canonicalWorld,
                                new NullRenderer(),
                                new ClientChatLimits(16, 256, 128),
                                new UiClientLimits(8, 8),
                                maximumPendingWorldProofs: 256,
                                standardUiDefinitionResolver: standardUiDefinitionResolver);
                        }
                        else
                        {
                            session = new RemotePlayableSession(
                                peer,
                                connected.ApplicationSessionEpoch,
                                requiredContent ?? throw new InvalidOperationException(
                                    "The streamed World requires a content binding."),
                                requiredWorld ?? throw new InvalidOperationException(
                                    "The legacy World binding was absent."),
                                new NullRenderer(),
                                new ClientChatLimits(16, 256, 128),
                                new UiClientLimits(8, 8),
                                maximumPendingWorldProofs: 256,
                                standardUiDefinitionResolver: standardUiDefinitionResolver,
                                requireStructuralManifest: structuralAuthorization is not null);
                        }
                        return new RemotePlayableFixture(
                            trustDirectory, identity, host, direct, session,
                            contentCache, contentRoot, preserveContentRoot, motionProxy,
                            authority, captured);
                    }
                    catch
                    {
                        direct.Dispose();
                        motionProxy?.Dispose();
                        throw;
                    }
                }
                catch
                {
                    host.Dispose();
                    throw;
                }
            }
            catch
            {
                authority?.Dispose();
                identity.Dispose();
                if (Directory.Exists(trustDirectory))
                    Directory.Delete(trustDirectory, recursive: true);
                throw;
            }
        }

        internal void Admit(string name, GameGuestBody body)
        {
            if (_session.GetSnapshot().StructuralManifestRequired)
                DriveUntil(() => _session.GetSnapshot().StructuralManifest.State ==
                    RemoteStructuralManifestClientState.Applied);
            True(TrySubmitGuest(name, body));
            DriveUntil(() => Playable.GetSnapshot().State == RemotePlayableClientState.Active);
        }

        internal bool TrySubmitGuest(string name, GameGuestBody body) =>
            _session.TrySubmitGuest(new GameGuestAdmissionRequest(body, name));

        internal void DriveUntil(Func<bool> condition, int maximumSteps = 100_000)
        {
            for (int step = 0; step < maximumSteps; step++)
            {
                Pump();
                if (condition()) return;
                if ((step & 15) == 0) Thread.Sleep(1);
                else Thread.Yield();
            }
            if (_applicationCapture?.Application is { } application)
            {
                RemoteGuestPlayableServerSnapshot server = application.GetSnapshot();
                var resident = new ClientSemanticChunkInfo[Cache.Limits.MaximumResidentChunks];
                int eastResidents = 0, eastPublic = 0;
                if (Cache.TryCopyResidentInfo(resident, out int residentCount, out _))
                    for (int i = 0; i < residentCount; i++)
                    {
                        WorldPackAddressing.ToPackLocal(resident[i].Key.Coord,
                            out WorldPackCoord owner, out _);
                        if (owner != ConceptDemoStructuralSource.EastCoord) continue;
                        eastResidents++;
                        eastPublic += resident[i].PublicRecordCount;
                        Console.WriteLine($"W7 timeout east={resident[i].Key} " +
                            $"public={resident[i].PublicRecordCount} " +
                            $"serverSub={ServerSubscribedTo(resident[i].Key)}");
                    }
                Console.WriteLine($"W7 timeout: ticks={server.AuthorityTicks} " +
                    $"drives={server.Timing.ReplicationReadiness.DrivePasses} " +
                    $"pending={server.Timing.ReplicationReadiness.Scheduler.PendingWorkCount} " +
                    $"due={server.Timing.ReplicationReadiness.Scheduler.DueWorkDequeued} " +
                    $"refresh={server.Timing.ReplicationReadiness.Scheduler.SourceRefreshes} " +
                    $"outstanding={server.World.OutstandingDeliveries} " +
                    $"snapshotActive={server.World.SnapshotTransferActive} " +
                    $"next={server.World.NextDeliveryId} " +
                    $"window={server.StructuralWindowCenter} " +
                    $"anchor={ServerInterestAnchor} " +
                    $"client={Playable.GetSnapshot().Position} " +
                    $"retired={World.GetSnapshot().RetiredInterestCount} " +
                    $"east={eastResidents}/{eastPublic}");
            }
            throw new InvalidOperationException(
                $"Remote playable did not converge. direct={_direct.GetSnapshot()} " +
                $"playable={Playable.GetSnapshot()} world={_session.GetSnapshot().World} " +
                $"chat={Chat.GetSnapshot()} session={_session.GetSnapshot()}");
        }

        internal void DriveSteps(int count)
        {
            for (int step = 0; step < count; step++)
            {
                Pump();
                if ((step & 15) == 0) Thread.Sleep(1);
            }
        }

        internal void QueueRaw(in RemotePlayableCommandMessage message)
        {
            Span<byte> payload = stackalloc byte[RemotePlayableCommandMessageCodec.EncodedSize];
            True(RemotePlayableCommandMessageCodec.TryWriteExact(payload, in message));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(RemotePlayableCommandMessageCodec.Descriptor.Id, payload));
        }

        internal void QueueRaw(in RemoteContentSetAppliedMessage message)
        {
            Span<byte> payload = stackalloc byte[RemoteContentSetAppliedMessageCodec.EncodedSize];
            True(RemoteContentSetAppliedMessageCodec.TryWriteExact(payload, in message));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(RemoteContentSetAppliedMessageCodec.Descriptor.Id, payload));
        }

        internal void QueueRawGuest(string name, GameGuestBody body)
        {
            var request = new GameGuestAdmissionRequest(body, name);
            Span<byte> payload = stackalloc byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
            True(UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
                payload, _direct.GetSnapshot().ApplicationSessionEpoch, 1, in request, out int written));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(
                    RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Id, payload[..written]));
        }

        internal bool TryFindPublic(uint prefab, out WorldPublicRecord found)
        {
            Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
            if (!Cache.TryCopyResidentInfo(chunks, out int count, out _))
            {
                found = default;
                return false;
            }
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!Cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.PrefabId != prefab) continue;
                    found = record;
                    return true;
                }
            }
            found = default;
            return false;
        }

        internal bool TryFindPublic(uint prefab, WorldPosition position,
            out WorldPublicRecord found)
        {
            Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
            if (Cache.TryCopyResidentInfo(chunks, out int count, out _))
                for (int chunk = 0; chunk < count; chunk++)
                    if (Cache.TryGetResidentView(chunks[chunk].Handle,
                            out ClientSemanticChunkView view))
                        foreach (WorldPublicRecord record in view.PublicRecords)
                            if (record.PrefabId == prefab && record.Position == position)
                            {
                                found = record;
                                return true;
                            }
            found = default;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            _contentCache?.Dispose();
            if (_direct.State == ClientDirectConnectState.Established)
            {
                _direct.BeginDisconnect();
                for (int step = 0; step < 20_000 &&
                     _direct.State != ClientDirectConnectState.Closed; step++)
                {
                    _direct.Pump();
                    _host.Pump();
                    Thread.Yield();
                }
            }
            _direct.Dispose();
            _host.Dispose();
            _authority?.Dispose();
            _motionProxy?.Dispose();
            _identity.Dispose();
            if (Directory.Exists(_trustDirectory))
                Directory.Delete(_trustDirectory, recursive: true);
            if (!_preserveContentRoot && _contentRoot is not null && Directory.Exists(_contentRoot))
                Directory.Delete(_contentRoot, recursive: true);
        }

        private void Pump()
        {
            _direct.Pump();
            _host.Pump();
            _ = _authority?.TickDue();
            _direct.Pump();
            _ = _session.ProcessReceived();
            if (_trackStructuralPresentation &&
                Playable.GetSnapshot() is { State: RemotePlayableClientState.Active } owner)
            {
                RemoteStructuralPackClient packs = StructuralPacks;
                int prepared = _session.GetSnapshot().StructuralPacks.PreparedPackCount;
                if (owner.StateSequence != _trackedOwnerStateSequence ||
                    prepared != _trackedPreparedPackCount)
                {
                    _trackedOwnerStateSequence = owner.StateSequence;
                    _trackedPreparedPackCount = prepared;
                    if (packs.TryPreparePresentation(owner.Position,
                            out SpatialChunkKey center,
                            out ClientStructuralResidencyPlan plan))
                        packs.CommitPresentation(plan, center);
                }
            }
            if (_motionProxy is not null) Thread.Sleep(1);
        }

        internal static ClientWorldSemanticCache CreatePreinstalledWorldCache()
        {
            WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
            WorldContentBinding binding = CreateWorldBinding();
            True(pack.TryBind(binding, out BoundWorldPack? bound, out _));
            var cache = new ClientWorldSemanticCache(
                bound!, new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096));
            for (int index = 0; index < pack.ChunkCount; index++)
            {
                True(pack.TryGetChunkKey(index, out var key));
                True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
                True(cache.TryStageFromBaseWorld(handle, [], default, out _));
                True(cache.TryPublish(handle, out _));
            }
            return cache;
        }

        private static void DriveDirectUntil(
            HostedUdpAdmissionHost host,
            ClientDirectConnectSession client,
            ClientDirectConnectState expected,
            bool delayedPath = false)
        {
            for (int iteration = 0; iteration < 40_000; iteration++)
            {
                client.Pump();
                host.Pump();
                if (client.State == expected) return;
                if (client.State == ClientDirectConnectState.Failed)
                    throw new InvalidOperationException(
                        $"Direct Connect failed: {client.GetSnapshot()}");
                if (delayedPath) Thread.Sleep(1);
                else Thread.Yield();
            }
            throw new InvalidOperationException($"Direct Connect did not reach {expected}.");
        }

        private sealed class NullRenderer : IUiRenderer
        {
            public IUiRenderHandle Create(
                UiViewInstanceId instance,
                CompiledUiDefinition definition,
                UiModelView model,
                Action<UiActionRequest> submitAction) => new Handle();

            public void Apply(
                IUiRenderHandle handle,
                CompiledUiDefinition definition,
                UiModelView previous,
                UiModelView current,
                UiModelDelta delta,
                Action<UiActionRequest> submitAction) { }

            public void Close(IUiRenderHandle handle) { }

            private sealed class Handle : IUiRenderHandle;
        }
    }
}
