using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Network;
using UltraSim.Server.Hosting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.UI;

internal static class Program
{
    public static void Main(string[] args)
    {
        var tests = new (string Name, Action Body)[]
        {
            ("RemoteWorkflowMatchesOfflineDirect", RemoteWorkflowMatchesOfflineDirect),
            ("LocalhostUdpWorkflowMatchesLoopbackAndOfflineDirect", LocalhostUdpWorkflowMatchesLoopbackAndOfflineDirect),
            ("RemoteWorkflowConvergesUnderDeterministicImpairment", RemoteWorkflowConvergesUnderImpairment),
            ("UnauthenticatedApplicationActionHasNoAuthority", UnauthenticatedApplicationActionHasNoAuthority),
            ("AuthenticationCapacityFailureIsContained", AuthenticationCapacityFailureIsContained),
            ("AuthenticationImplementationFailureDetachesWithoutDomainRejection", AuthenticationImplementationFailureDetachesWithoutDomainRejection),
            ("ControlAndCharacterSelectProgressThroughBulkBacklog", ControlAndCharacterSelectProgressThroughBulkBacklog),
            ("DuplicateAndStaleActionsCannotRepeatAuthority", DuplicateAndStaleActionsCannotRepeatAuthority),
            ("MalformedSemanticActionsAreContainedBeforeAuthority", MalformedSemanticActionsAreContainedBeforeAuthority),
            ("MalformedCharacterSelectActionsAreContainedBeforeAuthority", MalformedCharacterSelectActionsAreContainedBeforeAuthority),
            ("PresentationFailureAfterCommitDoesNotRollbackAuthority", PresentationFailureAfterCommitDoesNotRollbackAuthority),
            ("RejectedHotOpenRecoversThroughColdResnapshot", RejectedHotOpenRecoversThroughColdResnapshot),
            ("DisconnectDisposesRemoteClientProjection", DisconnectDisposesRemoteClientProjection),
            ("DisconnectCleanupContinuesAfterRendererCloseFailure", DisconnectCleanupContinuesAfterRendererCloseFailure),
            ("ReconnectStartsFreshApplicationSession", ReconnectStartsFreshApplicationSession),
            ("DelayedPriorDetachCannotRetireFreshUdpApplication", DelayedPriorDetachCannotRetireFreshUdpApplication),
            ("UdpDisconnectDrainsTransportAfterSemanticDetach", UdpDisconnectDrainsTransportAfterSemanticDetach),
            ("AuthoritativeInputCodecIsCanonicalAndTransactional", AuthoritativeInputCodecIsCanonicalAndTransactional),
            ("SupersedingInputMaySkipOlderPendingState", SupersedingInputMaySkipOlderPendingState),
            ("ControlledInputAndOwnerStateConvergeUnderLoss", ControlledInputAndOwnerStateConvergeUnderLoss),
            ("InputSequenceExpiryAndFutureBoundsAreAuthoritative", InputSequenceExpiryAndFutureBoundsAreAuthoritative),
            ("OwnerIdentityEpochAndClockReorderingAreFenced", OwnerIdentityEpochAndClockReorderingAreFenced),
            ("TombstoneIsCompleteWithoutPriorPresentState", TombstoneIsCompleteWithoutPriorPresentState),
            ("CadenceAnchorsApplyAtRevisionedTicks", CadenceAnchorsApplyAtRevisionedTicks),
            ("WarmedControlledInputStatePathAllocatesZero", WarmedControlledInputStatePathAllocatesZero),
            ("ControlledStateMatchesAcrossLoopbackAndUdp", ControlledStateMatchesAcrossLoopbackAndUdp),
            ("ControlledAuthorityDespawnPublishesTombstone", ControlledAuthorityDespawnPublishesTombstone),
            ("WorldStateCodecIsCanonicalAndTransactional", WorldStateCodecIsCanonicalAndTransactional),
            ("MaintainedWorldStateCrossesLoopbackWithApplicationAck", MaintainedWorldStateCrossesLoopbackWithApplicationAck),
            ("WorldStateTombstoneKnowledgeIsIndependentFromPresentation", WorldStateTombstoneKnowledgeIsIndependentFromPresentation),
            ("WorldStateClientCommitIsAtomicAtCapacityRegardlessOfRecordOrder", WorldStateClientCommitIsAtomicAtCapacityRegardlessOfRecordOrder),
            ("WorldStateCapacityFailureMakesRecipientExplicitlyNonviable", WorldStateCapacityFailureMakesRecipientExplicitlyNonviable),
            ("AtomicWorldBaselineCommitsCutoffThenPostCutoffState", AtomicWorldBaselineCommitsCutoffThenPostCutoffState),
            ("FreshProjectionEpochReplacesClientStateWithoutReconnect", FreshProjectionEpochReplacesClientStateWithoutReconnect),
            ("PersistencePublicationRebaselinesEstablishedConnection", PersistencePublicationRebaselinesEstablishedConnection),
            ("WorldBaselineConvergesUnderLossReorderAndDuplication", WorldBaselineConvergesUnderLossReorderAndDuplication),
            ("WorldBaselineCapacityRejectsWithoutPartialPublication", WorldBaselineCapacityRejectsWithoutPartialPublication),
            ("NewerBaselineCancelsOlderCandidate", NewerBaselineCancelsOlderCandidate),
            ("ReconnectStartsFreshAtomicWorldBaseline", ReconnectStartsFreshAtomicWorldBaseline),
            ("WorldBaselineCodecIsCanonicalAndTransactional", WorldBaselineCodecIsCanonicalAndTransactional),
            ("WorldBaselineHashFailurePreservesLiveProjection", WorldBaselineHashFailurePreservesLiveProjection),
            ("SameEpochBaselineFloorPreventsOmittedEntityResurrection", SameEpochBaselineFloorPreventsOmittedEntityResurrection),
        };

        foreach ((string name, Action body) in tests)
        {
            body();
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"All {tests.Length} Network N0.4 through N0.8 application tests passed.");
        if (args.Contains("--benchmark-n08", StringComparer.Ordinal))
            RunN08Benchmark(args);
        else if (args.Contains("--benchmark-n06", StringComparer.Ordinal))
            RunN06Benchmark(args);
        else if (args.Contains("--benchmark-udp", StringComparer.Ordinal))
            RunUdpBenchmark(args);
        else if (args.Contains("--benchmark", StringComparer.Ordinal))
            RunBenchmark(args);
    }

static void RemoteWorkflowMatchesOfflineDirect()
{
    ScenarioResult direct = RunOfflineDirect();
    ScenarioResult remote = RunRemote(default, disconnect: false);
    Equal(direct.Authority, remote.Authority,
        "ProtocolLoopback changed the authoritative inventory outcome.");
    Equal(direct.UiTrace, remote.UiTrace,
        "ProtocolLoopback changed the normalized semantic UI trace.");
    Equal(direct.ClientState, remote.ClientState,
        "ProtocolLoopback changed the eventual Client/Core state.");
}

static void LocalhostUdpWorkflowMatchesLoopbackAndOfflineDirect()
{
    ScenarioResult direct = RunOfflineDirect();
    ScenarioResult loopback = RunRemote(default, disconnect: false);
    ScenarioResult udp = RunRemoteUdp(disconnect: false);
    Equal(direct.Authority, udp.Authority,
        "Real UDP changed the authoritative inventory outcome.");
    Equal(loopback.UiTrace, udp.UiTrace,
        "Real UDP changed the normalized semantic UI trace.");
    Equal(direct.ClientState, udp.ClientState,
        "Real UDP changed complete retained Client/Core state.");
}

static void RemoteWorkflowConvergesUnderImpairment()
{
    ScenarioResult direct = RunOfflineDirect();
    ScenarioResult impaired = RunRemote(
        new NetworkImpairmentOptions(0xA11CE, 150, 125, 75, 4), disconnect: false);
    Equal(direct.Authority, impaired.Authority,
        "Loss/duplication/corruption/reordering changed authoritative state.");
    Equal(direct.ClientState, impaired.ClientState,
        "The impaired reliable workflow did not converge to the direct client state.");
}

static void DuplicateAndStaleActionsCannotRepeatAuthority()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    UiRowId bread = fixture.FindInventoryRow("Bread");
    fixture.Client.SubmitAction(fixture.InventoryView, InventoryUi.Select, bread);
    fixture.DriveUntil(() => fixture.Server!.Ui.GetSnapshot().AcceptedActions >= 2);

    // The bridge has assigned the preceding Play/Select sequences. Replaying
    // the exact semantic request at the application boundary must be rejected.
    var duplicate = new UiActionRequest(
        fixture.InventoryView,
        InventoryUi.Definition.Handle,
        InventoryUi.Split,
        clientSequence: 3);
    byte[] semantic = new byte[UltraSimGameProtocol.DefaultUiWireLimits.MaximumOperationBytes];
    True(UiSemanticWireCodec.TryWriteAction(
        semantic, duplicate, UltraSimGameProtocol.DefaultUiWireLimits, out int semanticBytes));
    byte[] message = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteUiAction(
        message, fixture.ApplicationSessionEpoch, semantic.AsSpan(0, semanticBytes), out int messageBytes));
    True(fixture.Server!.Ui.TryProcessAction(message.AsSpan(0, messageBytes)));
    False(fixture.Server.Ui.TryProcessAction(message.AsSpan(0, messageBytes)));

    string once = fixture.CaptureAuthority();
    False(fixture.Server.Ui.TryProcessAction(message.AsSpan(0, messageBytes)));
    Equal(once, fixture.CaptureAuthority(), "A duplicate UI action repeated an authority mutation.");
}

static void MalformedSemanticActionsAreContainedBeforeAuthority()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    string before = fixture.CaptureAuthority();
    UiRowId bread = fixture.FindInventoryRow("Bread");
    UiElementId wrongSource = InventoryUi.Definition.Elements.ToArray()
        .First(static element => element.Action == InventoryUi.Split).Id;

    UiActionRequest[] hostile =
    [
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            UiActionId.Invalid, clientSequence: 100),
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            InventoryUi.Select, bread, clientSequence: 101, source: wrongSource),
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            InventoryUi.Select, new UiRowId(ulong.MaxValue), clientSequence: 102),
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            InventoryUi.Split, clientSequence: 103),
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            InventoryUi.Split, bread, clientSequence: 104),
        new(fixture.InventoryView, InventoryUi.Definition.Handle,
            InventoryUi.Select, bread, UiValue.FromText("wrong"), clientSequence: 105),
    ];

    foreach (UiActionRequest request in hostile)
    {
        fixture.QueueRawAction(request);
        Equal(RemoteServerApplicationState.Active, fixture.Server!.GetSnapshot().State,
            "Malformed semantic intent escaped the application boundary.");
        Equal(before, fixture.CaptureAuthority(),
            "Malformed semantic intent reached authoritative Inventory state.");
    }

    // A later well-formed sequence still works, proving the session remains usable.
    fixture.QueueRawAction(new UiActionRequest(
        fixture.InventoryView, InventoryUi.Definition.Handle,
        InventoryUi.Select, bread, clientSequence: 106));
    fixture.DriveUntil(() =>
        fixture.Client.GetActionState(fixture.InventoryView, InventoryUi.Split).Enabled);
    fixture.QueueRawAction(new UiActionRequest(
        fixture.InventoryView, InventoryUi.Definition.Handle,
        InventoryUi.Split, clientSequence: 107));
    fixture.DriveUntil(() => fixture.ClientRowsForTesting() == "Bread|Bread|Iron Sword");
    True(fixture.CaptureAuthority().Contains("bread:4", StringComparison.Ordinal));
    True(fixture.CaptureAuthority().Contains("bread:1", StringComparison.Ordinal));
}

static void MalformedCharacterSelectActionsAreContainedBeforeAuthority()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    AdmissionResult waiting = fixture.AuthenticateToCharacterSelect();
    string before = fixture.CaptureAuthority();
    UiElementId wrongSource = CharacterSelectUi.Definition.Elements.ToArray()
        .First(static element => element.Action != CharacterSelectUi.Play).Id;

    UiActionRequest[] hostile =
    [
        new(waiting.CharacterSelectView, CharacterSelectUi.Definition.Handle,
            UiActionId.Invalid, new UiRowId(1), clientSequence: 50),
        new(waiting.CharacterSelectView, CharacterSelectUi.Definition.Handle,
            CharacterSelectUi.Play, new UiRowId(1), clientSequence: 51, source: wrongSource),
        new(waiting.CharacterSelectView, CharacterSelectUi.Definition.Handle,
            CharacterSelectUi.Play, new UiRowId(999), clientSequence: 52),
        new(waiting.CharacterSelectView, CharacterSelectUi.Definition.Handle,
            CharacterSelectUi.Play, new UiRowId(1), UiValue.FromText("wrong"), clientSequence: 53),
    ];

    foreach (UiActionRequest request in hostile)
    {
        fixture.QueueRawAction(request);
        True(fixture.Server!.Admission.TryGetSession(new ParticipantId(41), out AdmissionResult current) &&
            current.IsWaitingForCharacter,
            "Malformed CharacterSelect intent escaped its retained-model boundary.");
        Equal(before, fixture.CaptureAuthority(),
            "Malformed CharacterSelect intent reached authoritative state.");
    }

    fixture.QueueRawAction(new UiActionRequest(
        waiting.CharacterSelectView, CharacterSelectUi.Definition.Handle,
        CharacterSelectUi.Play, new UiRowId(1), clientSequence: 54));
    fixture.DriveUntil(() =>
        fixture.Server!.Admission.TryGetSession(new ParticipantId(41), out AdmissionResult entered) &&
        entered.IsEntered);
}

static void ControlledInputAndOwnerStateConvergeUnderLoss()
{
    using Fixture fixture = Fixture.CreateN06(
        new NetworkImpairmentOptions(0x6006, 250, 100, 0, 3));
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();

    fixture.QueueControlledVelocity(4);
    fixture.RepeatInputUntilServerObserves(sequence: 1);
    fixture.PublishTickAndConvergeOwnerState();
    RemoteControlledSimulationClientSnapshot moving = fixture.ControlledClientSnapshot;
    Equal(1UL, moving.LastProcessedInputSequence);
    Equal(4, moving.AuthoritativeInputVelocity);

    // The first stop datagram may be lost; the bounded redundancy bundle is
    // repeated until authority observes it. No reliable input queue is created.
    fixture.QueueControlledVelocity(0);
    fixture.RepeatInputUntilServerObserves(sequence: 2);
    fixture.PublishTickAndConvergeOwnerState();
    RemoteControlledSimulationClientSnapshot stopped = fixture.ControlledClientSnapshot;
    Equal(2UL, stopped.LastProcessedInputSequence);
    Equal(0, stopped.AuthoritativeInputVelocity);
    Equal(0, stopped.PendingInputs);
    Equal(fixture.ReadControlledSimulation(),
        (stopped.AuthoritativeTickMarker, stopped.AuthoritativeInputVelocity));
    True(fixture.Loopback!.GetSnapshot().Datagram.Dropped > 0,
        "The impairment fixture did not exercise packet loss.");
}

static void SupersedingInputMaySkipOlderPendingState()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();

    // The Input lane may replace an older bundle before transmission. Since
    // each sample is complete control state, losing sequence 1 is harmless:
    // the newest retained bundle contains sequences 2..4 and velocity 4 wins.
    fixture.QueueControlledVelocity(1);
    fixture.QueueControlledVelocity(2);
    fixture.QueueControlledVelocity(3);
    fixture.QueueControlledVelocity(4);
    fixture.RepeatInputUntilServerObserves(sequence: 4);
    fixture.PublishTickAndConvergeOwnerState();

    RemoteControlledSimulationClientSnapshot state = fixture.ControlledClientSnapshot;
    Equal(4UL, state.LastProcessedInputSequence);
    Equal(4, state.AuthoritativeInputVelocity);
    Equal(0, state.PendingInputs);
    Equal(4, fixture.ReadControlledSimulation().InputVelocity);
}

static void InputSequenceExpiryAndFutureBoundsAreAuthoritative()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();

    RemoteControlledSimulationServerSnapshot before = fixture.ControlledServerSnapshot;
    fixture.QueueControlledVelocity(5, targetTickOffset: 100);
    fixture.DriveUntil(() => fixture.ControlledServerSnapshot.RejectedInputSamples >
        before.RejectedInputSamples);
    fixture.QueueControlledVelocity(7, expiryOffsetMicroseconds: -1);
    fixture.DriveUntil(() => fixture.ControlledServerSnapshot.RejectedInputSamples >
        before.RejectedInputSamples + 1);

    fixture.QueueControlledVelocity(3);
    fixture.RepeatInputOnce(); // duplicate redundant samples are independently rejected
    fixture.DriveUntil(() => fixture.ControlledServerSnapshot.LastQueuedInputSequence == 3);
    fixture.PublishTickAndConvergeOwnerState();
    RemoteControlledSimulationClientSnapshot state = fixture.ControlledClientSnapshot;
    Equal(3UL, state.LastProcessedInputSequence);
    Equal(0, state.PendingInputs);
    Equal(3, state.AuthoritativeInputVelocity);
    True(fixture.ControlledServerSnapshot.RejectedInputSamples >= 3);
}

static void OwnerIdentityEpochAndClockReorderingAreFenced()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();
    RemoteControlledSimulationClientSnapshot initial = fixture.ControlledClientSnapshot;

    var future = new RemoteOwnerStateMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
        StateEpoch = initial.StateEpoch.Value,
        EntityIndex = initial.OwnerEntity.Index,
        EntityGeneration = initial.OwnerEntity.Generation,
        SimulationTick = initial.SimulationTick + 1,
        FrameSequence = initial.FrameSequence + 1,
        LastProcessedInputSequence = initial.LastProcessedInputSequence,
        TickMarker = initial.AuthoritativeTickMarker + 10,
        InputVelocity = 0,
        ClockRevision = initial.ClockRevision + 1,
        IsPresent = true,
    };
    fixture.QueueRawOwnerState(in future);
    fixture.DriveNetworkSteps(8);
    Equal(initial.FrameSequence, fixture.ControlledClientSnapshot.FrameSequence,
        "State using an unknown cadence revision published before its anchor.");
    fixture.QueueRawClockAnchor(initial.ClockRevision + 1, initial.SimulationTick + 1, 30);
    fixture.DriveUntil(() => fixture.ControlledClientSnapshot.FrameSequence == future.FrameSequence);
    Equal(30, fixture.ControlledClientSnapshot.SimulationFrequencyHz);

    RemoteControlledSimulationClientSnapshot anchored = fixture.ControlledClientSnapshot;
    var priorEpoch = future;
    priorEpoch.StateEpoch = anchored.StateEpoch.Value + 99;
    priorEpoch.FrameSequence++;
    fixture.QueueRawOwnerState(in priorEpoch);
    fixture.DriveNetworkSteps(8);
    Equal(anchored.FrameSequence, fixture.ControlledClientSnapshot.FrameSequence);

    var tombstone = future;
    tombstone.ClockRevision = anchored.ClockRevision;
    tombstone.FrameSequence = anchored.FrameSequence + 1;
    tombstone.IsPresent = false;
    fixture.QueueRawOwnerState(in tombstone);
    fixture.DriveUntil(() => !fixture.ControlledClientSnapshot.OwnerPresent);
    ClientEntitySlot oldSlot = anchored.OwnerSlot;
    Equal(0U, oldSlot.Index, "The one-owner client slot was not truly client-local and dense.");
    var reused = tombstone;
    reused.FrameSequence++;
    reused.EntityGeneration++;
    reused.IsPresent = true;
    fixture.QueueRawOwnerState(in reused);
    fixture.DriveUntil(() => fixture.ControlledClientSnapshot.OwnerPresent);
    True(fixture.ControlledClientSnapshot.OwnerSlot.Generation > oldSlot.Generation,
        "Client dense slot reuse did not advance its local generation.");
}

static void TombstoneIsCompleteWithoutPriorPresentState()
{
    using Fixture fixture = Fixture.CreateN06(default);
    const ulong applicationEpoch = 77;
    var client = new RemoteControlledSimulationClient(
        fixture.ClientPeer, applicationEpoch, maximumPendingInputs: 8);

    var anchor = new RemoteClockAnchorMessage
    {
        ApplicationSessionEpoch = applicationEpoch,
        StateEpoch = 91,
        ClockRevision = 1,
        EffectiveFromSimulationTick = 10,
        SimulationFrequencyHz = 60,
        WorldTimeMicroseconds = 1_000,
        WorldTimeFractionQuanta = 0,
    };
    Span<byte> anchorPayload = stackalloc byte[RemoteClockAnchorMessageCodec.EncodedSize];
    True(RemoteClockAnchorMessageCodec.TryWriteExact(anchorPayload, in anchor));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Control,
        RemoteClockAnchorMessageCodec.Descriptor.Id,
        RemoteClockAnchorMessageCodec.Descriptor.Version,
        default,
        anchorPayload.Length), anchorPayload));

    True(client.TryQueueInputVelocity(11, 2_000, 1));
    True(client.TryQueueInputVelocity(11, 2_000, 2));

    var tombstone = new RemoteOwnerStateMessage
    {
        ApplicationSessionEpoch = applicationEpoch,
        StateEpoch = 91,
        EntityIndex = 83_000,
        EntityGeneration = 9,
        SimulationTick = 10,
        FrameSequence = 10,
        LastProcessedInputSequence = 1,
        TickMarker = 0,
        InputVelocity = 0,
        ClockRevision = 1,
        IsPresent = false,
    };

    DeliverOwnerState(client, in tombstone);
    RemoteControlledSimulationClientSnapshot first = client.GetSnapshot();
    False(first.OwnerPresent);
    Equal(new ReplicationEntityId(83_000, 9), first.OwnerEntity);
    False(first.OwnerSlot.IsValid);
    Equal(10UL, first.FrameSequence);
    Equal(1UL, first.LastProcessedInputSequence);
    Equal(1, first.PendingInputs);

    tombstone.FrameSequence = 11;
    tombstone.SimulationTick = 11;
    tombstone.LastProcessedInputSequence = 2;
    DeliverOwnerState(client, in tombstone);
    RemoteControlledSimulationClientSnapshot repeated = client.GetSnapshot();
    False(repeated.OwnerPresent);
    Equal(11UL, repeated.FrameSequence);
    Equal(2UL, repeated.LastProcessedInputSequence);
    Equal(0, repeated.PendingInputs);

    // A delayed present sample for the tombstoned canonical generation cannot
    // resurrect it, even though no present sample was ever observed.
    var delayedPresent = tombstone;
    delayedPresent.FrameSequence = 12;
    delayedPresent.IsPresent = true;
    DeliverOwnerState(client, in delayedPresent);
    RemoteControlledSimulationClientSnapshot rejected = client.GetSnapshot();
    False(rejected.OwnerPresent);
    Equal(11UL, rejected.FrameSequence);
    Equal(repeated.RejectedMessages + 1, rejected.RejectedMessages);

    // A newer canonical lifetime reuses the one true local dense slot and
    // advances its independent client generation.
    delayedPresent.EntityGeneration = 10;
    DeliverOwnerState(client, in delayedPresent);
    RemoteControlledSimulationClientSnapshot newer = client.GetSnapshot();
    True(newer.OwnerPresent);
    Equal(0U, newer.OwnerSlot.Index);
    Equal(2U, newer.OwnerSlot.Generation);
}

static void WorldStateCodecIsCanonicalAndTransactional()
{
    RemoteWorldStateRecord[] source =
    [
        WorldRecord(1, 4, 2, 8, present: true, marker: 19),
        WorldRecord(2, 9, 3, 11, present: false, marker: 0),
    ];
    Span<byte> encoded = stackalloc byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteWorldStateBundle(
        encoded, 77, 81, 3, source, out int written));
    Span<RemoteWorldStateRecord> decoded = stackalloc RemoteWorldStateRecord[4];
    True(UltraSimGameProtocol.TryReadWorldStateBundle(
        encoded[..written], out RemoteWorldStateBundleHeader header, decoded, out int count));
    Equal(77UL, header.ApplicationSessionEpoch);
    Equal(81UL, header.StateEpoch);
    Equal(3UL, header.BundleId);
    Equal(2, count);
    Equal(source[0], decoded[0]);
    Equal(source[1], decoded[1]);

    // Every truncation and trailing data fail the exact dynamic codec.
    for (int length = 0; length < written; length++)
    {
        decoded.Fill(WorldRecord(99, 99, 1, 1, true, 1));
        False(UltraSimGameProtocol.TryReadWorldStateBundle(
            encoded[..length], out header, decoded, out count));
        Equal(default(RemoteWorldStateBundleHeader), header);
        Equal(0, count);
    }
    encoded[written] = 0xCC;
    False(UltraSimGameProtocol.TryReadWorldStateBundle(
        encoded[..(written + 1)], out header, decoded, out count));
    encoded[..sizeof(ulong)].Clear();
    False(UltraSimGameProtocol.TryReadWorldStateBundle(
        encoded[..written], out header, decoded, out count));
    Equal(default(RemoteWorldStateBundleHeader), header);
    Equal(0, count);

    RemoteWorldStateRecord duplicate = source[1] with { EntityIndex = source[0].EntityIndex };
    False(UltraSimGameProtocol.TryWriteWorldStateBundle(
        encoded, 77, 81, 4, [source[0], duplicate], out _));
    RemoteWorldStateRecord invalidTombstone = source[1] with { TickMarker = 1 };
    False(UltraSimGameProtocol.TryWriteWorldStateBundle(
        encoded, 77, 81, 5, [invalidTombstone], out _));
}

static void MaintainedWorldStateCrossesLoopbackWithApplicationAck()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    StateEpoch epoch = new(0x707);
    var interest = new GameInterestService(
        epoch,
        new GameInterestLimits(64, 4, 1_024, 1_024, 256, 32, 8),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(3, 1, 10), 1,
            new ReplicationSpatialPoint(1_000, 0), GameReplicationHorizon.Far, 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(9, 1, 20), 1,
            new ReplicationSpatialPoint(2_000, 0), GameReplicationHorizon.Far, 0));

    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch, 1);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, epoch,
        maximumKnownIdentities: 16, maximumActiveEntities: 8,
        maximumRecordsPerBundle: 8);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);

    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(0, 4_096));
    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(0, 4_096));
    Equal(GameWorldStateBuildStatus.NoData, server.TrySchedule(0, 4_096));
    Equal(2, fixture.ServerPeer.IndependentUnreliableQueued,
        "Independent relation subsets were coalesced instead of pipelined.");
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 2);
    RemoteWorldStateClientSnapshot clientSnapshot = client.GetSnapshot();
    Equal(2, clientSnapshot.ActiveEntityCount);
    Equal(0, clientSnapshot.PendingApplicationAcknowledgements);
    Equal(0, interest.GetSnapshot().PendingRelationCount);

    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(3, 1, 31), 2,
            new ReplicationSpatialPoint(1_500, 0), GameReplicationHorizon.Far, 1));
    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(1, 4_096));
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 3);
    Span<RemoteWorldEntitySnapshot> active = stackalloc RemoteWorldEntitySnapshot[8];
    Equal(2, client.CopyActiveEntities(active));
    Equal(3U, active[0].Entity.Index);
    Equal(31, active[0].TickMarker);
    Equal(2UL, active[0].CanonicalVersion);
}

static void WorldStateTombstoneKnowledgeIsIndependentFromPresentation()
{
    using Fixture fixture = Fixture.CreateN06(default);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, 77, new StateEpoch(91),
        maximumKnownIdentities: 4, maximumActiveEntities: 2);
    RemoteWorldStateRecord tombstone = WorldRecord(
        10, 83_000, 9, 5, present: false, marker: 0);
    Equal(RemoteWorldStateClientApplyStatus.Applied,
        client.ApplyDecodedBundle([tombstone]));
    Equal(1, client.GetSnapshot().KnownIdentityCount);
    Equal(0, client.GetSnapshot().ActiveEntityCount);

    RemoteWorldStateRecord delayed = WorldRecord(
        9, 83_000, 9, 4, present: true, marker: 44);
    Equal(RemoteWorldStateClientApplyStatus.Duplicate,
        client.ApplyDecodedBundle([delayed]));
    Equal(0, client.GetSnapshot().ActiveEntityCount);

    RemoteWorldStateRecord repeated = tombstone with { RelationSequence = 11 };
    Equal(RemoteWorldStateClientApplyStatus.Applied,
        client.ApplyDecodedBundle([repeated]));
    RemoteWorldStateRecord nextLifetime = WorldRecord(
        12, 83_000, 10, 1, present: true, marker: 50) with
    {
        XMillimeters = 100_000,
    };
    Equal(RemoteWorldStateClientApplyStatus.Applied,
        client.ApplyDecodedBundle([nextLifetime]));
    Equal(1, client.GetSnapshot().ActiveEntityCount);
    Equal(RemoteWorldStateClientApplyStatus.Duplicate,
        client.ApplyDecodedBundle([tombstone]));
    Span<RemoteWorldEntitySnapshot> retained = stackalloc RemoteWorldEntitySnapshot[2];
    Equal(1, client.CopyActiveEntities(retained));
    Equal(10U, retained[0].Entity.Generation);

    Span<ClientEntitySlot> visible = stackalloc ClientEntitySlot[2];
    Equal(0, client.CopyPresentationCandidates(new ClientWorldPoint(0, 0), 1_000, visible));
    Equal(1, client.GetSnapshot().ActiveEntityCount,
        "Camera selection incorrectly changed retained network knowledge.");
    Equal(1, client.CopyPresentationCandidates(
        new ClientWorldPoint(100_000, 0), 1_000, visible));
}

static void WorldStateClientCommitIsAtomicAtCapacityRegardlessOfRecordOrder()
{
    using Fixture fixture = Fixture.CreateN06(default);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, 77, new StateEpoch(92),
        maximumKnownIdentities: 2, maximumActiveEntities: 1);
    Equal(RemoteWorldStateClientApplyStatus.Applied,
        client.ApplyDecodedBundle([WorldRecord(1, 1, 1, 1, true, 10)]));
    Span<RemoteWorldEntitySnapshot> before = stackalloc RemoteWorldEntitySnapshot[1];
    Equal(1, client.CopyActiveEntities(before));

    // Allocation precedes release on the wire. Commit must still honor the
    // successful aggregate preflight without throwing or partially publishing.
    RemoteWorldStateRecord[] replacement =
    [
        WorldRecord(1, 2, 1, 1, true, 20),
        WorldRecord(2, 1, 1, 2, false, 0),
    ];
    Equal(RemoteWorldStateClientApplyStatus.Applied,
        client.ApplyDecodedBundle(replacement));
    Span<RemoteWorldEntitySnapshot> after = stackalloc RemoteWorldEntitySnapshot[1];
    Equal(1, client.CopyActiveEntities(after));
    Equal(2U, after[0].Entity.Index);
    Equal(before[0].Slot.Index, after[0].Slot.Index);
    True(after[0].Slot.Generation > before[0].Slot.Generation);

    RemoteWorldStateClientSnapshot stable = client.GetSnapshot();
    Equal(RemoteWorldStateClientApplyStatus.CapacityExceeded,
        client.ApplyDecodedBundle([WorldRecord(1, 3, 1, 1, true, 30)]));
    Equal(stable, client.GetSnapshot(), "Capacity rejection partially mutated client projection.");
}

static void WorldStateCapacityFailureMakesRecipientExplicitlyNonviable()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(42);
    StateEpoch epoch = new(0x708);
    var interest = new GameInterestService(
        epoch,
        new GameInterestLimits(16, 2, 1_024, 1_024, 64, 16, 4),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(1, 1, 1), 1,
            new ReplicationSpatialPoint(1_000, 0), GameReplicationHorizon.Far, 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(2, 1, 2), 1,
            new ReplicationSpatialPoint(2_000, 0), GameReplicationHorizon.Far, 0));

    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch, 4);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, epoch,
        maximumKnownIdentities: 2, maximumActiveEntities: 1,
        maximumRecordsPerBundle: 4);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);

    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(0, 4_096));
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationCapacityFailures == 1);
    Equal(1, interest.GetSnapshot().NonviableRecipientCount);
    Equal(GameWorldStateBuildStatus.RecipientNonviable,
        server.TrySchedule(1, 4_096));
    Equal(1UL, server.GetSnapshot().ApplicationCapacityFailures);
}

static void AtomicWorldBaselineCommitsCutoffThenPostCutoffState()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    StateEpoch epoch = new(0x801);
    var interest = new GameInterestService(
        epoch,
        new GameInterestLimits(64, 4, 1_024, 1_024, 256, 32, 8),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(3, 1, 10), 1,
            new ReplicationSpatialPoint(1_000, 0), GameReplicationHorizon.Far, 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(9, 1, 20), 1,
            new ReplicationSpatialPoint(2_000, 0), GameReplicationHorizon.Far, 0));

    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch,
        maximumRecordsPerBundle: 4, maximumBaselineRecords: 8);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, epoch,
        maximumKnownIdentities: 8, maximumActiveEntities: 8,
        maximumRecordsPerBundle: 4);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);

    // Baseline is captured at version 1. A version 2 write after the cutoff
    // must remain ordinary relation debt after the baseline application ACK.
    Equal(RemoteWorldBaselineProgressStatus.AwaitingClientPreparation,
        server.TryBeginBaseline(baselineRevision: 1, eventFloor: 7, recordsPerSegment: 1));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(3, 1, 31), 2,
            new ReplicationSpatialPoint(1_500, 0), GameReplicationHorizon.Far, 1));

    fixture.DriveUntil(() => client.GetSnapshot().BaselineCandidateActive);
    RemoteWorldStateClientSnapshot staging = client.GetSnapshot();
    True(staging.BaselineCandidateActive);
    Equal(0, staging.ActiveEntityCount,
        "Baseline Begin mutated the live projection before complete validation.");
    fixture.DriveNetworkSteps(1);
    Equal(0, client.GetSnapshot().ActiveEntityCount,
        "Prepared baseline publication occurred before Bulk data crossed the transport.");
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 1);

    RemoteWorldStateClientSnapshot committed = client.GetSnapshot();
    Equal(epoch, committed.StateEpoch);
    Equal(1UL, committed.BaselineRevision);
    Equal(7UL, committed.EventFloor);
    Equal(2, committed.ActiveEntityCount);
    Equal(1, interest.GetSnapshot().PendingRelationCount,
        "Baseline acknowledgement incorrectly cleared post-cutoff relation debt.");
    Span<RemoteWorldEntitySnapshot> baselineRows = stackalloc RemoteWorldEntitySnapshot[8];
    Equal(2, client.CopyActiveEntities(baselineRows));
    Equal(10, baselineRows[0].TickMarker);

    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(2, 4_096));
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 1);
    Span<RemoteWorldEntitySnapshot> currentRows = stackalloc RemoteWorldEntitySnapshot[8];
    Equal(2, client.CopyActiveEntities(currentRows));
    Equal(31, currentRows[0].TickMarker);
    Equal(0, interest.GetSnapshot().PendingRelationCount);
}

static void FreshProjectionEpochReplacesClientStateWithoutReconnect()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    GameInterestService Build(StateEpoch epoch, uint entityIndex, int marker)
    {
        var value = new GameInterestService(
            epoch,
            new GameInterestLimits(64, 4, 1_024, 1_024, 256, 32, 8),
            GameInterestProfile.Reference);
        Equal(GameInterestUpdateStatus.Success,
            value.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), 0));
        Equal(GameInterestUpdateStatus.Success,
            value.UpsertEntity(ProjectedWorldEntity(entityIndex, 1, marker), 1,
                new ReplicationSpatialPoint(1_000, 0), GameReplicationHorizon.Far, 0));
        return value;
    }

    var first = Build(new StateEpoch(0x810), 4, 10);
    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, first, participant, fixture.ApplicationSessionEpoch,
        maximumRecordsPerBundle: 4, maximumBaselineRecords: 8);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, first.StateEpoch,
        maximumKnownIdentities: 8, maximumActiveEntities: 8,
        maximumRecordsPerBundle: 4);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);
    _ = server.TryBeginBaseline(1, recordsPerSegment: 1);
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 1);
    Equal(4U, CopySingle(client).Entity.Index);

    var replacement = Build(new StateEpoch(0x811), 7, 70);
    server.ReplaceInterest(replacement);
    _ = server.TryBeginBaseline(1, recordsPerSegment: 1);
    fixture.DriveNetworkSteps(2);
    Equal(4U, CopySingle(client).Entity.Index,
        "A partially received replacement epoch changed live client state.");
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 2);
    RemoteWorldEntitySnapshot replaced = CopySingle(client);
    Equal(7U, replaced.Entity.Index);
    Equal(70, replaced.TickMarker);
    Equal(replacement.StateEpoch, client.GetSnapshot().StateEpoch);
    Equal(77UL, fixture.ClientBridge!.GetSnapshot().ApplicationSessionEpoch,
        "Projection replacement incorrectly replaced the healthy application session.");

    // A delayed complete record from the prior epoch is rejected after the
    // atomic swap and cannot revive the old projection.
    RemoteWorldStateRecord old = WorldRecord(99, 4, 1, 99, true, 999);
    Span<byte> payload = stackalloc byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteWorldStateBundle(
        payload, fixture.ApplicationSessionEpoch, first.StateEpoch.Value, 99, [old], out int written));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.WorldState,
        RemoteWorldStateBundleHeaderCodec.Descriptor.Id,
        RemoteWorldStateBundleHeaderCodec.Descriptor.Version,
        default,
        written), payload[..written]));
    Equal(7U, CopySingle(client).Entity.Index);
}

static RemoteWorldEntitySnapshot CopySingle(RemoteWorldStateClient client)
{
    Span<RemoteWorldEntitySnapshot> rows = stackalloc RemoteWorldEntitySnapshot[8];
    Equal(1, client.CopyActiveEntities(rows));
    return rows[0];
}

static void PersistencePublicationRebaselinesEstablishedConnection()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    using GameProjectionService projection = fixture.CreateProjection();
    True(projection.SynchronizeParticipant(participant));
    Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    GameInterestService initialInterest = BuildInterestFromProjection(projection, participant, 0);
    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, initialInterest, participant, fixture.ApplicationSessionEpoch,
        maximumRecordsPerBundle: 8, maximumBaselineRecords: 64);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, projection.StateEpoch,
        maximumKnownIdentities: 64, maximumActiveEntities: 64,
        maximumRecordsPerBundle: 8);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);
    _ = server.TryBeginBaseline(1, recordsPerSegment: 2);
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 1);
    ulong applicationEpoch = fixture.ClientBridge.GetSnapshot().ApplicationSessionEpoch;
    StateEpoch oldEpoch = client.GetSnapshot().StateEpoch;
    int savedMarker = fixture.ReadAliceSimulation().TickMarker;

    (ulong savedTick, ulong divergentTick, ulong restoredTick, ulong generation) =
        fixture.SaveDivergeRestore();
    True(divergentTick > savedTick);
    Equal(savedTick, restoredTick,
        "Persistence publication did not restore authoritative scheduler time.");
    True(generation > 1);
    Equal(savedMarker, fixture.ReadAliceSimulation().TickMarker,
        "Restored Alice did not recover the saved authoritative state.");
    True(fixture.Server.Admission.TryGetSession(participant, out AdmissionResult rebound) &&
        rebound.IsEntered && rebound.GenerationId == generation,
        "Admission did not atomically rebind Alice to the restored generation.");

    GameProjectionCaptureResult rebuilt = projection.RebuildBaseline();
    Equal(GameProjectionCaptureStatus.BaselineRebuilt, rebuilt.Status);
    True(projection.StateEpoch != oldEpoch);
    GameInterestService restoredInterest = BuildInterestFromProjection(projection, participant, 1);
    server.ReplaceInterest(restoredInterest);
    _ = server.TryBeginBaseline(rebuilt.Frame.BaselineRevision, recordsPerSegment: 2);
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 2);
    Equal(projection.StateEpoch, client.GetSnapshot().StateEpoch);
    Equal(applicationEpoch, fixture.ClientBridge.GetSnapshot().ApplicationSessionEpoch,
        "Whole-authority replacement incorrectly replaced healthy connection/application identity.");

    // Alice continues under the same connection after the restored generation
    // becomes the only committed projection epoch.
    fixture.TickRuntime();
    Equal(GameProjectionCaptureStatus.Success, projection.CaptureAfterTick().Status);
    True(projection.TryGetOwnerState(participant, out GameProjectionOwnerState owner));
    GameProjectedEntity alice = owner.Entity;
    Equal(GameInterestUpdateStatus.Success,
        restoredInterest.UpsertEntity(alice, 2,
            new ReplicationSpatialPoint(1_000, 0), GameReplicationHorizon.Far, 2));
    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(2, 4_096));
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 1);
    RemoteWorldEntitySnapshot continued = CopySingle(client);
    Equal(fixture.ReadAliceSimulation().TickMarker, continued.TickMarker);
}

static GameInterestService BuildInterestFromProjection(
    GameProjectionService projection,
    ParticipantId participant,
    long now)
{
    var interest = new GameInterestService(
        projection.StateEpoch,
        new GameInterestLimits(64, 4, 1_024, 1_024, 256, 32, 8),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, new ReplicationSpatialPoint(0, 0), now));
    Span<GameProjectedEntity> entities = stackalloc GameProjectedEntity[64];
    int count = projection.CopyEntities(entities);
    ulong version = 1;
    for (int index = 0; index < count; index++)
    {
        if (!entities[index].HasSimulationState) continue;
        Equal(GameInterestUpdateStatus.Success,
            interest.UpsertEntity(entities[index], version,
                new ReplicationSpatialPoint(1_000 + index * 10, 0),
                GameReplicationHorizon.Far, now));
    }
    return interest;
}

static void WorldBaselineConvergesUnderLossReorderAndDuplication()
{
    using Fixture fixture = Fixture.CreateN06(
        new NetworkImpairmentOptions(0xB453_11E, 180, 200, 40, 5));
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    var interest = new GameInterestService(
        new StateEpoch(0x820),
        new GameInterestLimits(64, 4, 1_024, 1_024, 512, 64, 8),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, default, 0));
    for (uint index = 1; index <= 12; index++)
        Equal(GameInterestUpdateStatus.Success,
            interest.UpsertEntity(ProjectedWorldEntity(index, 1, checked((int)index)), 1,
                new ReplicationSpatialPoint(checked((int)index * 100), 0),
                GameReplicationHorizon.Far, 0));
    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch,
        maximumRecordsPerBundle: 4, maximumBaselineRecords: 16);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, interest.StateEpoch,
        maximumKnownIdentities: 16, maximumActiveEntities: 16,
        maximumRecordsPerBundle: 4);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);
    _ = server.TryBeginBaseline(1, recordsPerSegment: 2);
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesApplied == 1, maximumSteps: 200_000);
    Equal(12, client.GetSnapshot().ActiveEntityCount);
    True(fixture.Loopback!.GetSnapshot().Datagram.Dropped > 0);
    True(fixture.Loopback.GetSnapshot().Datagram.Duplicated > 0);
}

static void WorldBaselineCapacityRejectsWithoutPartialPublication()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    ParticipantId participant = new(41);
    var interest = new GameInterestService(
        new StateEpoch(0x821),
        new GameInterestLimits(16, 2, 1_024, 1_024, 64, 16, 4),
        GameInterestProfile.Reference);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, default, 0));
    for (uint index = 1; index <= 2; index++)
        Equal(GameInterestUpdateStatus.Success,
            interest.UpsertEntity(ProjectedWorldEntity(index, 1, checked((int)index)), 1,
                new ReplicationSpatialPoint(checked((int)index * 100), 0),
                GameReplicationHorizon.Far, 0));
    var server = new RemoteWorldStateServer(
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch,
        maximumRecordsPerBundle: 4, maximumBaselineRecords: 4);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, interest.StateEpoch,
        maximumKnownIdentities: 1, maximumActiveEntities: 1,
        maximumRecordsPerBundle: 4);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);
    _ = server.TryBeginBaseline(1, recordsPerSegment: 1);
    fixture.DriveUntil(() => server.GetSnapshot().BaselinesRejected == 1);
    Equal(0, client.GetSnapshot().ActiveEntityCount);
    Equal(0, client.GetSnapshot().KnownIdentityCount);
    Equal(1, interest.GetSnapshot().NonviableRecipientCount);
}

static void NewerBaselineCancelsOlderCandidate()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, new StateEpoch(0x830),
        maximumKnownIdentities: 4, maximumActiveEntities: 4,
        maximumRecordsPerBundle: 2);
    RemoteWorldStateRecord oldRecord = WorldRecord(1, 1, 1, 1, true, 10);
    RemoteWorldStateRecord newRecord = WorldRecord(2, 2, 1, 1, true, 20);
    SendBegin(1, 1, oldRecord);
    True(client.GetSnapshot().BaselineCandidateActive);
    SendBegin(2, 2, newRecord);
    True(client.GetSnapshot().BaselineCandidateActive);
    SendSegmentAndCommit(1, 1, oldRecord); // delayed prior transaction
    True(client.GetSnapshot().BaselineCandidateActive);
    Equal(0, client.GetSnapshot().ActiveEntityCount);
    SendSegmentAndCommit(2, 2, newRecord);
    False(client.GetSnapshot().BaselineCandidateActive);
    Equal(2U, CopySingle(client).Entity.Index);

    void SendBegin(ulong id, ulong revision, RemoteWorldStateRecord record)
    {
        var begin = new RemoteWorldBaselineBeginMessage
        {
            ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
            StateEpoch = 0x830,
            BaselineId = id,
            BaselineRevision = revision,
            EventFloor = revision,
            RelationSequenceFloor = record.RelationSequence,
            SchemaFingerprint = UltraSimGameProtocol.WorldStateBaselineSchemaFingerprint,
            ContentHash = UltraSimGameProtocol.ComputeWorldBaselineHash([record]),
            RecordCount = 1,
            SegmentCount = 1,
            RecordsPerSegment = 1,
        };
        Span<byte> bytes = stackalloc byte[RemoteWorldBaselineBeginMessageCodec.EncodedSize];
        True(RemoteWorldBaselineBeginMessageCodec.TryWriteExact(bytes, in begin));
        True(client.TryProcess(new NetworkDeliveredMessage(
            NetworkLane.Control, RemoteWorldBaselineBeginMessageCodec.Descriptor.Id,
            RemoteWorldBaselineBeginMessageCodec.Descriptor.Version, default, bytes.Length), bytes));
    }

    void SendSegmentAndCommit(ulong id, ulong revision, RemoteWorldStateRecord record)
    {
        Span<byte> segment = stackalloc byte[UltraSimGameProtocol.MaximumMessageBytes];
        True(UltraSimGameProtocol.TryWriteWorldBaselineSegment(
            segment, fixture.ApplicationSessionEpoch, 0x830, id, 0, 1, [record], out int written));
        True(client.TryProcess(new NetworkDeliveredMessage(
            NetworkLane.Bulk, RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Id,
            RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Version, default, written),
            segment[..written]));
        var commit = new RemoteWorldBaselineCommitMessage
        {
            ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
            StateEpoch = 0x830,
            BaselineId = id,
            BaselineRevision = revision,
            ContentHash = UltraSimGameProtocol.ComputeWorldBaselineHash([record]),
        };
        Span<byte> bytes = stackalloc byte[RemoteWorldBaselineCommitMessageCodec.EncodedSize];
        True(RemoteWorldBaselineCommitMessageCodec.TryWriteExact(bytes, in commit));
        True(client.TryProcess(new NetworkDeliveredMessage(
            NetworkLane.Control, RemoteWorldBaselineCommitMessageCodec.Descriptor.Id,
            RemoteWorldBaselineCommitMessageCodec.Descriptor.Version, default, bytes.Length), bytes));
    }
}

static void ReconnectStartsFreshAtomicWorldBaseline()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    var interest = new GameInterestService(
        new StateEpoch(0x840),
        new GameInterestLimits(16, 2, 1_024, 1_024, 64, 16, 4),
        GameInterestProfile.Reference);
    ParticipantId participant = new(42);
    Equal(GameInterestUpdateStatus.Success,
        interest.SetRecipientAnchor(participant, default, 0));
    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(5, 1, 50), 1,
            new ReplicationSpatialPoint(100, 0), GameReplicationHorizon.Far, 0));
    RemoteWorldStateClientSnapshot reconnected = fixture.ReconnectWithWorldBaseline(interest, participant);
    Equal(interest.StateEpoch, reconnected.StateEpoch);
    Equal(1UL, reconnected.BaselineRevision);
    Equal(1, reconnected.ActiveEntityCount);
    Equal(RemoteServerApplicationState.Active, fixture.ReconnectedServerState);
}

static void WorldBaselineCodecIsCanonicalAndTransactional()
{
    RemoteWorldStateRecord[] source =
    [
        WorldRecord(1, 4, 2, 8, true, 19),
        WorldRecord(2, 9, 3, 11, true, 23),
    ];
    Span<byte> encoded = stackalloc byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteWorldBaselineSegment(
        encoded, 77, 81, 3, 0, 1, source, out int written));
    Span<RemoteWorldStateRecord> decoded = stackalloc RemoteWorldStateRecord[4];
    True(UltraSimGameProtocol.TryReadWorldBaselineSegment(
        encoded[..written], out RemoteWorldBaselineSegmentHeader header,
        decoded, out int count));
    Equal(2, count);
    Equal(source[0], decoded[0]);
    Equal(source[1], decoded[1]);
    Equal(UltraSimGameProtocol.ComputeWorldBaselineHash(source),
        UltraSimGameProtocol.ComputeWorldBaselineHash(decoded[..count]));

    for (int length = 0; length < written; length++)
    {
        decoded.Fill(WorldRecord(99, 99, 1, 1, true, 1));
        False(UltraSimGameProtocol.TryReadWorldBaselineSegment(
            encoded[..length], out header, decoded, out count));
        Equal(default(RemoteWorldBaselineSegmentHeader), header);
        Equal(0, count);
    }
    encoded[written] = 0xCC;
    False(UltraSimGameProtocol.TryReadWorldBaselineSegment(
        encoded[..(written + 1)], out header, decoded, out count));
    RemoteWorldStateRecord duplicate = source[1] with { EntityIndex = source[0].EntityIndex };
    False(UltraSimGameProtocol.TryWriteWorldBaselineSegment(
        encoded, 77, 81, 4, 0, 1, [source[0], duplicate], out _));
    False(UltraSimGameProtocol.TryWriteWorldBaselineSegment(
        encoded, 77, 81, 4, 0, 1,
        [source[0] with { IsPresent = false }], out _));
}

static void WorldBaselineHashFailurePreservesLiveProjection()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, new StateEpoch(0x850),
        maximumKnownIdentities: 4, maximumActiveEntities: 4,
        maximumRecordsPerBundle: 2);
    RemoteWorldStateRecord live = WorldRecord(1, 1, 1, 1, true, 10);
    Equal(RemoteWorldStateClientApplyStatus.Applied, client.ApplyDecodedBundle([live]));
    RemoteWorldStateRecord expected = WorldRecord(2, 2, 1, 1, true, 20);
    RemoteWorldStateRecord corrupted = expected with { TickMarker = 21 };
    ulong expectedHash = UltraSimGameProtocol.ComputeWorldBaselineHash([expected]);
    var begin = new RemoteWorldBaselineBeginMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
        StateEpoch = 0x850,
        BaselineId = 1,
        BaselineRevision = 1,
        RelationSequenceFloor = expected.RelationSequence,
        SchemaFingerprint = UltraSimGameProtocol.WorldStateBaselineSchemaFingerprint,
        ContentHash = expectedHash,
        RecordCount = 1,
        SegmentCount = 1,
        RecordsPerSegment = 1,
    };
    Span<byte> beginBytes = stackalloc byte[RemoteWorldBaselineBeginMessageCodec.EncodedSize];
    True(RemoteWorldBaselineBeginMessageCodec.TryWriteExact(beginBytes, in begin));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Control, RemoteWorldBaselineBeginMessageCodec.Descriptor.Id,
        RemoteWorldBaselineBeginMessageCodec.Descriptor.Version, default, beginBytes.Length), beginBytes));
    Span<byte> segment = stackalloc byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteWorldBaselineSegment(
        segment, fixture.ApplicationSessionEpoch, 0x850, 1, 0, 1, [corrupted], out int written));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Bulk, RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Id,
        RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Version, default, written), segment[..written]));
    var commit = new RemoteWorldBaselineCommitMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
        StateEpoch = 0x850,
        BaselineId = 1,
        BaselineRevision = 1,
        ContentHash = expectedHash,
    };
    Span<byte> commitBytes = stackalloc byte[RemoteWorldBaselineCommitMessageCodec.EncodedSize];
    True(RemoteWorldBaselineCommitMessageCodec.TryWriteExact(commitBytes, in commit));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Control, RemoteWorldBaselineCommitMessageCodec.Descriptor.Id,
        RemoteWorldBaselineCommitMessageCodec.Descriptor.Version, default, commitBytes.Length), commitBytes));
    False(client.GetSnapshot().BaselineCandidateActive);
    Equal(1U, CopySingle(client).Entity.Index);
    Equal(10, CopySingle(client).TickMarker);
    Equal(0UL, client.GetSnapshot().AppliedBaselines);
    True(client.GetSnapshot().RejectedBaselines > 0);
}

static void SameEpochBaselineFloorPreventsOmittedEntityResurrection()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, new StateEpoch(0x851),
        maximumKnownIdentities: 4, maximumActiveEntities: 4,
        maximumRecordsPerBundle: 2);
    RemoteWorldStateRecord prior = WorldRecord(5, 8, 1, 1, true, 80);
    Equal(RemoteWorldStateClientApplyStatus.Applied, client.ApplyDecodedBundle([prior]));

    ulong emptyHash = UltraSimGameProtocol.ComputeWorldBaselineHash([]);
    var begin = new RemoteWorldBaselineBeginMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
        StateEpoch = 0x851,
        BaselineId = 1,
        BaselineRevision = 1,
        RelationSequenceFloor = 10,
        SchemaFingerprint = UltraSimGameProtocol.WorldStateBaselineSchemaFingerprint,
        ContentHash = emptyHash,
        RecordCount = 0,
        SegmentCount = 0,
        RecordsPerSegment = 1,
    };
    Span<byte> beginBytes = stackalloc byte[RemoteWorldBaselineBeginMessageCodec.EncodedSize];
    True(RemoteWorldBaselineBeginMessageCodec.TryWriteExact(beginBytes, in begin));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Control, RemoteWorldBaselineBeginMessageCodec.Descriptor.Id,
        RemoteWorldBaselineBeginMessageCodec.Descriptor.Version, default, beginBytes.Length), beginBytes));
    var commit = new RemoteWorldBaselineCommitMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch,
        StateEpoch = 0x851,
        BaselineId = 1,
        BaselineRevision = 1,
        ContentHash = emptyHash,
    };
    Span<byte> commitBytes = stackalloc byte[RemoteWorldBaselineCommitMessageCodec.EncodedSize];
    True(RemoteWorldBaselineCommitMessageCodec.TryWriteExact(commitBytes, in commit));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.Control, RemoteWorldBaselineCommitMessageCodec.Descriptor.Id,
        RemoteWorldBaselineCommitMessageCodec.Descriptor.Version, default, commitBytes.Length), commitBytes));
    Equal(0, client.GetSnapshot().ActiveEntityCount);
    Equal(10UL, client.GetSnapshot().RelationSequenceFloor);

    RemoteWorldStateRecord delayed = prior with
    {
        RelationSequence = 9,
        CanonicalVersion = 2,
        TickMarker = 90,
    };
    Equal(RemoteWorldStateClientApplyStatus.Duplicate,
        client.ApplyDecodedBundle([delayed]));
    Equal(0, client.GetSnapshot().ActiveEntityCount,
        "A pre-cutoff present record resurrected an identity omitted by the repaired baseline.");
}

static RemoteWorldStateRecord WorldRecord(
    ulong relationSequence,
    uint entityIndex,
    uint entityGeneration,
    ulong canonicalVersion,
    bool present,
    int marker) => new(
        relationSequence,
        entityIndex,
        entityGeneration,
        canonicalVersion,
        present,
        present ? (byte)GameReplicationHorizon.Far : (byte)0,
        present ? checked((int)entityIndex * 10) : 0,
        present ? marker * 10 : 0,
        present ? marker : 0,
        present ? marker * 2 : 0);

static GameProjectedEntity ProjectedWorldEntity(uint index, uint generation, int marker) => new(
    new ReplicationEntityId(index, generation),
    HasSimulationState: true,
    new GameSimulationState { TickMarker = marker, InputVelocity = marker * 2 });

static void DeliverOwnerState(
    RemoteControlledSimulationClient client,
    in RemoteOwnerStateMessage state)
{
    Span<byte> payload = stackalloc byte[RemoteOwnerStateMessageCodec.EncodedSize];
    True(RemoteOwnerStateMessageCodec.TryWriteExact(payload, in state));
    True(client.TryProcess(new NetworkDeliveredMessage(
        NetworkLane.OwnerState,
        RemoteOwnerStateMessageCodec.Descriptor.Id,
        RemoteOwnerStateMessageCodec.Descriptor.Version,
        default,
        payload.Length), payload));
}

static void CadenceAnchorsApplyAtRevisionedTicks()
{
    using Fixture fixture = Fixture.CreateN06(default, allowVariableCadence: true);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();
    Equal(60, fixture.ControlledClientSnapshot.SimulationFrequencyHz);

    fixture.SetSimulationFrequency(30);
    fixture.TickAndConvergeOwnerState();
    RemoteControlledSimulationClientSnapshot thirty = fixture.ControlledClientSnapshot;
    Equal(30, thirty.SimulationFrequencyHz);
    Equal(thirty.SimulationTick, thirty.EffectiveFromSimulationTick);

    fixture.SetSimulationFrequency(60);
    fixture.TickAndConvergeOwnerState();
    RemoteControlledSimulationClientSnapshot sixty = fixture.ControlledClientSnapshot;
    Equal(60, sixty.SimulationFrequencyHz);
    Equal(thirty.ClockRevision + 1, sixty.ClockRevision);
    Equal(sixty.SimulationTick, sixty.EffectiveFromSimulationTick);
}

static void WarmedControlledInputStatePathAllocatesZero()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();
    for (int index = 0; index < 8; index++) fixture.RunControlledCycle(index & 1);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 32; index++) fixture.RunControlledCycle(index & 1);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
}

static void ControlledStateMatchesAcrossLoopbackAndUdp()
{
    (int TickMarker, int InputVelocity, ulong Ack) Run(bool udp)
    {
        using Fixture fixture = Fixture.CreateN06(default, udp: udp);
        fixture.AuthenticateAndEnter();
        fixture.EnableControlledSimulation();
        fixture.QueueControlledVelocity(2);
        fixture.RepeatInputUntilServerObserves(1);
        fixture.PublishTickAndConvergeOwnerState();
        fixture.QueueControlledVelocity(5);
        fixture.RepeatInputUntilServerObserves(2);
        fixture.PublishTickAndConvergeOwnerState();
        RemoteControlledSimulationClientSnapshot client = fixture.ControlledClientSnapshot;
        (int marker, int velocity) = fixture.ReadControlledSimulation();
        Equal((marker, velocity),
            (client.AuthoritativeTickMarker, client.AuthoritativeInputVelocity));
        return (marker, velocity, client.LastProcessedInputSequence);
    }

    Equal(Run(udp: false), Run(udp: true),
        "Replacing deterministic datagrams with UDP changed controlled simulation semantics.");
}

static void AuthoritativeInputCodecIsCanonicalAndTransactional()
{
    var epoch = new StateEpoch(9);
    RemoteAuthoritativeInputSample[] expected =
    [
        new(4, 20, 1_000, 3),
        new(5, 21, 1_001, -2),
        new(6, 22, 1_002, 0),
    ];
    byte[] encoded = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteAuthoritativeInputBundle(
        encoded, 7, epoch, expected, out int written));
    var actual = new RemoteAuthoritativeInputSample[UltraSimGameProtocol.MaximumInputRedundancy];
    True(UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
        encoded.AsSpan(0, written), out RemoteAuthoritativeInputBundleHeader header,
        actual, out int count));
    Equal(7UL, header.ApplicationSessionEpoch);
    Equal(epoch.Value, header.StateEpoch);
    Equal(expected.Length, count);
    True(expected.AsSpan().SequenceEqual(actual.AsSpan(0, count)));

    for (int length = 0; length < written; length++)
    {
        False(UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
            encoded.AsSpan(0, length), out _, actual, out int truncatedCount));
        Equal(0, truncatedCount);
    }
    encoded[written] = 0;
    False(UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
        encoded.AsSpan(0, written + 1), out _, actual, out _));

    int firstSample = RemoteAuthoritativeInputBundleHeaderCodec.EncodedSize;
    byte[] noncanonical = encoded.AsSpan(0, written).ToArray();
    BinaryPrimitives.WriteUInt64BigEndian(
        noncanonical.AsSpan(firstSample + 28, sizeof(ulong)), expected[0].Sequence);
    False(UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
        noncanonical, out _, actual, out _));
    noncanonical = encoded.AsSpan(0, written).ToArray();
    BinaryPrimitives.WriteInt64BigEndian(
        noncanonical.AsSpan(firstSample + 16, sizeof(long)), -1);
    False(UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
        noncanonical, out _, actual, out _));

    False(UltraSimGameProtocol.TryWriteAuthoritativeInputBundle(
        encoded, 7, epoch, expected.AsSpan(1, 2).ToArray().Reverse().ToArray(), out int unorderedBytes));
    Equal(0, unorderedBytes);
    False(UltraSimGameProtocol.TryWriteAuthoritativeInputBundle(
        encoded.AsSpan(0, written - 1), 7, epoch, expected, out int shortBytes));
    Equal(0, shortBytes);
}

static void ControlledAuthorityDespawnPublishesTombstone()
{
    using Fixture fixture = Fixture.CreateN06(default);
    fixture.AuthenticateAndEnter();
    fixture.EnableControlledSimulation();
    fixture.RetireControlledAuthority();
    False(fixture.ControlledClientSnapshot.OwnerPresent);
}

static void ControlAndCharacterSelectProgressThroughBulkBacklog()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    byte[] bulk = new byte[15_000];
    for (int index = 0; index < 4; index++)
        Equal(NetworkQueueStatus.Accepted, fixture.ServerPeer.TryQueue(
            RemoteUiOperationHeaderCodec.Descriptor.Id, bulk));
    fixture.AuthenticateAndEnter();
    Equal(RemoteClientApplicationState.Active, fixture.ClientBridge!.GetSnapshot().State);
    Equal(RemoteServerApplicationState.Active, fixture.Server!.GetSnapshot().State);
}

static void AuthenticationImplementationFailureDetachesWithoutDomainRejection()
{
    using Fixture fixture = Fixture.Create(
        remote: true,
        default,
        authenticator: ThrowingAuthenticator);
    int failures = 0;
    fixture.Server!.ApplicationFaulted += _ => failures++;
    True(fixture.ClientBridge!.TryBeginAuthentication(1, "test", "alice-token"));
    fixture.DriveUntil(() =>
        fixture.Server.GetSnapshot().State == RemoteServerApplicationState.Detached);
    Equal(1, failures);
    Equal(RemoteClientApplicationState.AwaitingAuthentication,
        fixture.ClientBridge.GetSnapshot().State);
}

static bool ThrowingAuthenticator(string provider, string credential, out TrustedIdentity identity)
{
    identity = default;
    throw new InvalidOperationException("Injected authentication implementation failure.");
}

static void AuthenticationCapacityFailureIsContained()
{
    NetworkProtocolLimits limits = NetworkProtocolLimits.Default with
    {
        MaximumReliableMessages = 1,
    };
    using Fixture fixture = Fixture.Create(remote: true, default, limits);
    string before = fixture.CaptureAuthority();
    True(fixture.ClientBridge!.TryBeginAuthentication(1, "test", "alice-token"));
    fixture.DriveUntil(() =>
        fixture.Server!.GetSnapshot().State == RemoteServerApplicationState.Detached);
    Equal(before, fixture.CaptureAuthority());
    Equal(RemoteClientApplicationState.AwaitingAuthentication,
        fixture.ClientBridge.GetSnapshot().State);
}

static void UnauthenticatedApplicationActionHasNoAuthority()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    string before = fixture.CaptureAuthority();
    var request = new UiActionRequest(
        new UiViewInstanceId(1),
        InventoryUi.Definition.Handle,
        InventoryUi.Split,
        clientSequence: 1);
    byte[] semantic = new byte[UltraSimGameProtocol.DefaultUiWireLimits.MaximumOperationBytes];
    True(UiSemanticWireCodec.TryWriteAction(
        semantic, request, UltraSimGameProtocol.DefaultUiWireLimits, out int semanticBytes));
    byte[] message = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    True(UltraSimGameProtocol.TryWriteUiAction(
        message, fixture.ApplicationSessionEpoch, semantic.AsSpan(0, semanticBytes), out int messageBytes));
    Equal(NetworkQueueStatus.Accepted, fixture.ClientPeer.TryQueue(
        RemoteUiActionHeaderCodec.Descriptor.Id, message.AsSpan(0, messageBytes)));
    for (int step = 0; step < 64; step++)
    {
        fixture.PumpTransport();
        fixture.ClientBridge!.ProcessReceived();
        fixture.Server!.ProcessReceived();
    }
    Equal(RemoteServerApplicationState.AwaitingAuthentication, fixture.Server!.GetSnapshot().State);
    Equal(before, fixture.CaptureAuthority(), "Unauthenticated UI traffic changed authority.");
    False(fixture.ClientBridge!.TryBeginAuthentication(0, "test", "alice-token"));
    False(fixture.ClientBridge.TryBeginAuthentication(1, new string('p', 2_000), "token"));
}

static void DisconnectDisposesRemoteClientProjection()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    True(fixture.Client.OpenViewCount == 1);
    fixture.Server!.Disconnect();
    fixture.DriveUntil(() =>
        fixture.ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
    Equal(0, fixture.Renderer.ActiveHandles, "Disconnect retained renderer handles.");
    Equal(fixture.Renderer.Creates, fixture.Renderer.Closes,
        "Disconnect did not close every created remote view.");
}

static void PresentationFailureAfterCommitDoesNotRollbackAuthority()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    UiRowId bread = fixture.FindInventoryRow("Bread");
    fixture.Client.SubmitAction(fixture.InventoryView, InventoryUi.Select, bread);
    fixture.DriveUntil(() =>
        fixture.Client.GetActionState(fixture.InventoryView, InventoryUi.Split).Enabled);
    fixture.Renderer.FailNextApply = true;
    fixture.Client.SubmitAction(fixture.InventoryView, InventoryUi.Split);
    fixture.DriveUntil(() => fixture.Server!.Ui.GetSnapshot().DesynchronizedViews == 1);
    True(fixture.CaptureAuthority().Contains("bread:4", StringComparison.Ordinal));
    True(fixture.CaptureAuthority().Contains("bread:1", StringComparison.Ordinal));
    Equal(RemoteClientApplicationState.Faulted, fixture.ClientBridge!.GetSnapshot().State);
}

static void ReconnectStartsFreshApplicationSession()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    True(fixture.ReconnectAndOpenCharacterSelect(),
        "Reconnect did not establish a fresh application/UI session.");
}

static void DelayedPriorDetachCannotRetireFreshUdpApplication()
{
    using Fixture fixture = Fixture.CreateUdp();
    var stale = new RemoteSessionDetachMessage
    {
        ApplicationSessionEpoch = fixture.ApplicationSessionEpoch - 1,
    };
    byte[] payload = new byte[RemoteSessionDetachMessageCodec.EncodedSize];
    True(RemoteSessionDetachMessageCodec.TryWriteExact(payload, in stale));
    Equal(NetworkQueueStatus.Accepted,
        fixture.ServerPeer.TryQueue(RemoteSessionDetachMessageCodec.Descriptor.Id, payload));
    for (int pump = 0; pump < 64; pump++)
    {
        fixture.PumpTransport();
        fixture.ClientBridge!.ProcessReceived();
        fixture.Server!.ProcessReceived();
    }
    Equal(RemoteClientApplicationState.AwaitingAuthentication,
        fixture.ClientBridge!.GetSnapshot().State,
        "A prior-session detach retired a fresh unauthenticated client binding.");
    fixture.AuthenticateAndEnter();
    Equal(RemoteClientApplicationState.Active, fixture.ClientBridge.GetSnapshot().State);
}

static void UdpDisconnectDrainsTransportAfterSemanticDetach()
{
    using Fixture fixture = Fixture.CreateUdp();
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    fixture.Server!.Disconnect();
    fixture.BeginTransportDrain();
    fixture.DriveUntil(() =>
        fixture.ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
    fixture.DriveUntil(() => fixture.IsTransportClosed);
    Equal(0, fixture.Renderer.ActiveHandles);
}

static void DisconnectCleanupContinuesAfterRendererCloseFailure()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    fixture.OpenSecondInventory();
    fixture.Renderer.FailNextClose = true;
    fixture.Server!.Disconnect();
    fixture.DriveUntil(() =>
        fixture.ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
    Equal(fixture.Renderer.Creates, fixture.Renderer.CloseAttempts,
        "Client disposal stopped after the first renderer cleanup failure.");
    True(fixture.Client.IsFaulted,
        "The client did not retain its best-effort renderer cleanup fault.");
}

static void RejectedHotOpenRecoversThroughColdResnapshot()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    fixture.AuthenticateAndEnter();
    True(fixture.RecoverMissingInventoryDefinition(),
        "A missing hot definition did not recover through an explicit cold resnapshot.");
}

static ScenarioResult RunOfflineDirect()
{
    using Fixture fixture = Fixture.Create(remote: false, default);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    fixture.SplitAndEquip();
    return fixture.CaptureResult();
}

static ScenarioResult RunRemote(NetworkImpairmentOptions impairment, bool disconnect)
{
    using Fixture fixture = Fixture.Create(remote: true, impairment);
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    fixture.SplitAndEquip();
    fixture.DriveUntil(() => fixture.ClientPeer.ReliableQueued == 0 &&
        fixture.ServerPeer.ReliableQueued == 0);
    ScenarioResult result = fixture.CaptureResult();
    if (disconnect)
    {
        fixture.Server!.Disconnect();
        fixture.DriveUntil(() =>
            fixture.ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
    }
    return result;
}

static ScenarioResult RunRemoteUdp(bool disconnect)
{
    using Fixture fixture = Fixture.CreateUdp();
    fixture.AuthenticateAndEnter();
    fixture.OpenInventory();
    fixture.SplitAndEquip();
    fixture.DriveUntil(() => fixture.ClientPeer.ReliableQueued == 0 &&
        fixture.ServerPeer.ReliableQueued == 0);
    ScenarioResult result = fixture.CaptureResult();
    if (disconnect)
    {
        fixture.Server!.Disconnect();
        fixture.BeginTransportDrain();
        fixture.DriveUntil(() => fixture.ClientBridge!.GetSnapshot().State ==
            RemoteClientApplicationState.Detached);
    }
    return result;
}

static void RunBenchmark(string[] args)
{
    const int samples = 11;
    _ = RunOfflineDirect();
    _ = RunRemote(default, disconnect: false);
    var direct = new double[samples];
    var remote = new double[samples];
    var steps = new ulong[samples];
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            direct[sample] = MeasureScenario(RunOfflineDirect, out _);
            remote[sample] = MeasureScenario(
                () => RunRemote(default, disconnect: false), out ScenarioResult result);
            steps[sample] = result.ProtocolSteps;
        }
        else
        {
            remote[sample] = MeasureScenario(
                () => RunRemote(default, disconnect: false), out ScenarioResult result);
            steps[sample] = result.ProtocolSteps;
            direct[sample] = MeasureScenario(RunOfflineDirect, out _);
        }
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Array.Sort(direct);
    Array.Sort(remote);
    Array.Sort(steps);

    using Fixture directFixture = Fixture.Create(remote: false, default);
    directFixture.AuthenticateAndEnter();
    directFixture.OpenInventory();
    using Fixture remoteFixture = Fixture.Create(remote: true, default);
    remoteFixture.AuthenticateAndEnter();
    remoteFixture.OpenInventory();
    directFixture.Renderer.RecordTrace = false;
    remoteFixture.Renderer.RecordTrace = false;
    directFixture.MeasureSelectionCycles(8);
    remoteFixture.MeasureSelectionCycles(8);
    const int interactionsPerSample = 64;
    var directInteraction = new double[samples];
    var remoteInteraction = new double[samples];
    long interactionAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            directInteraction[sample] = directFixture.MeasureSelectionCycles(interactionsPerSample);
            remoteInteraction[sample] = remoteFixture.MeasureSelectionCycles(interactionsPerSample);
        }
        else
        {
            remoteInteraction[sample] = remoteFixture.MeasureSelectionCycles(interactionsPerSample);
            directInteraction[sample] = directFixture.MeasureSelectionCycles(interactionsPerSample);
        }
    }
    long interactionAllocated = GC.GetAllocatedBytesForCurrentThread() - interactionAllocatedBefore;
    Array.Sort(directInteraction);
    Array.Sort(remoteInteraction);
    var report = new N04BenchmarkResult(
        "ultrasim-network-n0.4-application-loopback-v1",
        DateTimeOffset.UtcNow,
        Environment.Version.ToString(),
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Environment.ProcessorCount,
        samples,
        direct.Average(),
        Percentile(direct, 0.95),
        remote.Average(),
        Percentile(remote, 0.95),
        remote.Average() / direct.Average(),
        steps[steps.Length / 2],
        allocated,
        interactionsPerSample,
        directInteraction.Average() / interactionsPerSample,
        Percentile(directInteraction, 0.95) / interactionsPerSample,
        remoteInteraction.Average() / interactionsPerSample,
        Percentile(remoteInteraction, 0.95) / interactionsPerSample,
        remoteInteraction.Average() / directInteraction.Average(),
        interactionAllocated,
        "Cold result: complete world/fixture + secure loopback setup and real workflow. Established result: repeated real Inventory Select intent -> authoritative controller validation -> semantic model delta -> client apply -> application acknowledgement on one established session, 64 interactions/sample. 11 alternating-order samples. Allocation is reported honestly and includes current UI semantic model/delta work; it is not a Network hot-loop zero-allocation claim.");
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    int outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 < args.Length)
    {
        string path = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

static void RunUdpBenchmark(string[] args)
{
    const int samples = 11;
    _ = RunRemote(default, disconnect: false);
    _ = RunRemoteUdp(disconnect: false);
    var loopback = new double[samples];
    var udp = new double[samples];
    var udpPumps = new ulong[samples];
    long coldAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            loopback[sample] = MeasureScenario(
                () => RunRemote(default, disconnect: false), out _);
            udp[sample] = MeasureScenario(
                () => RunRemoteUdp(disconnect: false), out ScenarioResult result);
            udpPumps[sample] = result.ProtocolSteps;
        }
        else
        {
            udp[sample] = MeasureScenario(
                () => RunRemoteUdp(disconnect: false), out ScenarioResult result);
            udpPumps[sample] = result.ProtocolSteps;
            loopback[sample] = MeasureScenario(
                () => RunRemote(default, disconnect: false), out _);
        }
    }
    long coldAllocated = GC.GetAllocatedBytesForCurrentThread() - coldAllocatedBefore;
    Array.Sort(loopback);
    Array.Sort(udp);
    Array.Sort(udpPumps);

    using Fixture loopbackFixture = Fixture.Create(remote: true, default);
    loopbackFixture.AuthenticateAndEnter();
    loopbackFixture.OpenInventory();
    using Fixture udpFixture = Fixture.CreateUdp();
    udpFixture.AuthenticateAndEnter();
    udpFixture.OpenInventory();
    loopbackFixture.Renderer.RecordTrace = false;
    udpFixture.Renderer.RecordTrace = false;
    loopbackFixture.MeasureSelectionCycles(8);
    udpFixture.MeasureSelectionCycles(8);
    const int interactions = 64;
    var loopbackInteraction = new double[samples];
    var udpInteraction = new double[samples];
    long establishedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            loopbackInteraction[sample] = loopbackFixture.MeasureSelectionCycles(interactions);
            udpInteraction[sample] = udpFixture.MeasureSelectionCycles(interactions);
        }
        else
        {
            udpInteraction[sample] = udpFixture.MeasureSelectionCycles(interactions);
            loopbackInteraction[sample] = loopbackFixture.MeasureSelectionCycles(interactions);
        }
    }
    long establishedAllocated =
        GC.GetAllocatedBytesForCurrentThread() - establishedAllocatedBefore;
    Array.Sort(loopbackInteraction);
    Array.Sort(udpInteraction);
    var report = new N05BenchmarkResult(
        "ultrasim-network-n0.5-real-udp-application-v1",
        DateTimeOffset.UtcNow,
        Environment.Version.ToString(),
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Environment.ProcessorCount,
        Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
        Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
        samples,
        loopback.Average(),
        Percentile(loopback, 0.95),
        udp.Average(),
        Percentile(udp, 0.95),
        udp.Average() / loopback.Average(),
        udpPumps[udpPumps.Length / 2],
        coldAllocated,
        interactions,
        loopbackInteraction.Average() / interactions,
        Percentile(loopbackInteraction, 0.95) / interactions,
        udpInteraction.Average() / interactions,
        Percentile(udpInteraction, 0.95) / interactions,
        udpInteraction.Average() / loopbackInteraction.Average(),
        establishedAllocated,
        "Same N0.4 application workflow and semantic oracle over secure deterministic ProtocolLoopback versus connected localhost UDP. UDP is single-owner nonblocking socket I/O with monotonic-millisecond RTO. Cold allocation includes fixture, socket, handshake, UI, and application setup; established allocation includes application/UI work and is not a transport-only zero-allocation claim. The kernel suite separately proves warmed secure UDP delivery at 0 B.");
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    int outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 < args.Length)
    {
        string path = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

static void RunN06Benchmark(string[] args)
{
    const int samples = 21;
    const int cycles = 128;
    using Fixture loopback = Fixture.CreateN06(default);
    using Fixture udp = Fixture.CreateN06(default, udp: true);
    loopback.AuthenticateAndEnter();
    udp.AuthenticateAndEnter();
    loopback.EnableControlledSimulation();
    udp.EnableControlledSimulation();
    for (int index = 0; index < 8; index++)
    {
        loopback.RunControlledCycle(index & 1);
        udp.RunControlledCycle(index & 1);
    }

    var loopbackSamples = new double[samples];
    var udpSamples = new double[samples];
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            loopbackSamples[sample] = MeasureControlled(loopback, cycles);
            udpSamples[sample] = MeasureControlled(udp, cycles);
        }
        else
        {
            udpSamples[sample] = MeasureControlled(udp, cycles);
            loopbackSamples[sample] = MeasureControlled(loopback, cycles);
        }
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Array.Sort(loopbackSamples);
    Array.Sort(udpSamples);
    var report = new N06BenchmarkResult(
        "ultrasim-network-n0.6-controlled-input-owner-state-v2",
        DateTimeOffset.UtcNow,
        Environment.Version.ToString(),
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Environment.ProcessorCount,
        UltraSimGameProtocol.Manifest.Version,
        UltraSimGameProtocol.Manifest.Fingerprint,
        Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
        Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
        samples,
        cycles,
        loopbackSamples.Average() / cycles,
        Percentile(loopbackSamples, 0.95) / cycles,
        udpSamples.Average() / cycles,
        Percentile(udpSamples, 0.95) / cycles,
        udpSamples.Average() / loopbackSamples.Average(),
        allocated,
        "Post-audit semantic-hardened profile: remote Input contains only complete superseding velocity state, absence is a complete generation-fencing tombstone, and the one-owner client projection uses true local slot zero. One complete input decode/admission, authoritative scheduler tick, projection capture, revisioned owner state, secure transport, client reconciliation, and input acknowledgement per cycle. Fixed 60 Hz; no adaptive cadence; alternating paired samples; warmed caller path remains allocation-free.");
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    int outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 < args.Length)
    {
        string path = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

static void RunN08Benchmark(string[] args)
{
    const int samples = 21;
    const int entities = 256;
    using Fixture loopback = Fixture.CreateN06(default);
    using Fixture udp = Fixture.CreateN06(default, udp: true);
    (RemoteWorldStateServer Server, RemoteWorldStateClient Client) Prepare(Fixture fixture, ulong epoch)
    {
        fixture.AuthenticateAndEnter();
        ParticipantId participant = new(41);
        var interest = new GameInterestService(
            new StateEpoch(epoch),
            new GameInterestLimits(512, 2, 2_048, 2_048, 1_024, 64, 32),
            GameInterestProfile.Reference);
        Equal(GameInterestUpdateStatus.Success,
            interest.SetRecipientAnchor(participant, default, 0));
        for (uint index = 1; index <= entities; index++)
            Equal(GameInterestUpdateStatus.Success,
                interest.UpsertEntity(ProjectedWorldEntity(index, 1, checked((int)index)), 1,
                    new ReplicationSpatialPoint(checked((int)index * 10), 0),
                    GameReplicationHorizon.Far, 0));
        var server = new RemoteWorldStateServer(
            fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch,
            maximumRecordsPerBundle: 32, maximumBaselineRecords: entities);
        var client = new RemoteWorldStateClient(
            fixture.ClientPeer, fixture.ApplicationSessionEpoch, interest.StateEpoch,
            maximumKnownIdentities: entities, maximumActiveEntities: entities,
            maximumRecordsPerBundle: 32);
        fixture.Server!.BindProtocolHandler(server);
        fixture.ClientBridge!.BindProtocolHandler(client);
        return (server, client);
    }

    var loop = Prepare(loopback, 0x901);
    var real = Prepare(udp, 0x902);
    ulong loopRevision = 1;
    ulong udpRevision = 1;
    for (int warmup = 0; warmup < 3; warmup++)
    {
        _ = Measure(loopback, loop.Server, loop.Client, loopRevision++);
        _ = Measure(udp, real.Server, real.Client, udpRevision++);
    }
    var loopSamples = new double[samples];
    var udpSamples = new double[samples];
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            loopSamples[sample] = Measure(loopback, loop.Server, loop.Client, loopRevision++);
            udpSamples[sample] = Measure(udp, real.Server, real.Client, udpRevision++);
        }
        else
        {
            udpSamples[sample] = Measure(udp, real.Server, real.Client, udpRevision++);
            loopSamples[sample] = Measure(loopback, loop.Server, loop.Client, loopRevision++);
        }
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Array.Sort(loopSamples);
    Array.Sort(udpSamples);
    var report = new N08BenchmarkResult(
        "ultrasim-network-n0.8-atomic-world-baseline-v1",
        DateTimeOffset.UtcNow,
        Environment.Version.ToString(),
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Environment.ProcessorCount,
        UltraSimGameProtocol.Manifest.Version,
        UltraSimGameProtocol.Manifest.Fingerprint,
        samples,
        entities,
        32,
        loopSamples.Average(),
        Percentile(loopSamples, 0.95),
        udpSamples.Average(),
        Percentile(udpSamples, 0.95),
        udpSamples.Average() / loopSamples.Average(),
        allocated,
        loop.Server.GetSnapshot().ReservedBytes,
        real.Server.GetSnapshot().ReservedBytes,
        loop.Client.GetSnapshot().ReservedBytes,
        real.Client.GetSnapshot().ReservedBytes,
        "One complete 256-entity relevant-scope cutoff: server capture/hash, reliable Begin preparation, eight Bulk segments, reliable Commit, private client validation/swap, and application ACK. Alternating paired ProtocolLoopback/localhost UDP samples; cold/recovery mechanism measured after capacity warmup; fixed 60 Hz and no adaptive cadence.");
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    int outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 < args.Length)
    {
        string path = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    static double Measure(
        Fixture fixture,
        RemoteWorldStateServer server,
        RemoteWorldStateClient client,
        ulong revision)
    {
        ulong prior = server.GetSnapshot().BaselinesApplied;
        long started = Stopwatch.GetTimestamp();
        RemoteWorldBaselineProgressStatus status = server.TryBeginBaseline(
            revision, eventFloor: revision, recordsPerSegment: 32);
        True(status is RemoteWorldBaselineProgressStatus.AwaitingClientPreparation or
            RemoteWorldBaselineProgressStatus.Progressed);
        fixture.DriveUntilBaselineApplied(server, prior);
        Equal(revision, client.GetSnapshot().BaselineRevision);
        return (Stopwatch.GetTimestamp() - started) * (1_000_000_000d / Stopwatch.Frequency);
    }
}

static double MeasureControlled(Fixture fixture, int cycles)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < cycles; index++) fixture.RunControlledCycle(index & 1);
    return (Stopwatch.GetTimestamp() - started) * (1_000_000_000d / Stopwatch.Frequency);
}

static double MeasureScenario(Func<ScenarioResult> scenario, out ScenarioResult result)
{
    long started = Stopwatch.GetTimestamp();
    result = scenario();
    return (Stopwatch.GetTimestamp() - started) * (1_000_000_000d / Stopwatch.Frequency);
}

static double Percentile(double[] sorted, double fraction) =>
    sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];

sealed class Fixture : IDisposable
{
    private readonly bool _remote;
    private readonly UltraSimGameComposition _composition;
    private readonly InternalHost _host;
    private readonly ServerRuntime _runtime;
    private readonly FixtureKeys _keys;
    private readonly LocalUiPort? _localPort;
    private readonly AdmissionService? _localAdmission;
    private InventoryUiController? _inventory;
    private GameProjectionService? _projection;
    private RemoteControlledSimulationServer? _controlledServer;
    private RemoteControlledSimulationClient? _controlledClient;
    private readonly StringBuilder _clientSemanticCheckpoints = new();
    private bool _disposed;
    private readonly UdpProtocolSession? _udp;
    private ulong _networkPumps;
    private RemoteServerApplicationState _reconnectedServerState;

    private Fixture(
        bool remote,
        NetworkImpairmentOptions impairment,
        NetworkProtocolLimits? protocolLimits = null,
        RemoteIdentityAuthenticator? authenticator = null,
        bool udp = false,
        bool allowVariableCadence = false)
    {
        _remote = remote;
        _composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            runtimeProfile: remote ? GameRuntimeProfile.Networked : GameRuntimeProfile.Local);
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x90_0004));
        _host = new InternalHost(_composition, new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(512, identity,
                remote ? GameRuntimeProfile.Networked : GameRuntimeProfile.Local),
            WorldClockOptions.Default,
            allowVariableSimulationFrequency: allowVariableCadence));
        _host.Start();
        _runtime = _host.RuntimeForTesting;
        _keys = Populate(_runtime.Game);
        Renderer = new RecordingRenderer();
        Client = new UiClientRuntime(Renderer);

        if (remote)
        {
            if (udp)
            {
                True(UdpProtocolSession.TryCreate(
                    protocolLimits ?? NetworkProtocolLimits.Default,
                    new UdpProtocolLimits(512, 256 * 1024, 256 * 1024,
                        4_096, 30_000, 30_000, 5_000, 5_000),
                    out UdpProtocolSession? session,
                    out _,
                    UltraSimGameProtocol.Manifest));
                _udp = session!;
            }
            else
            {
                True(ProtocolLoopback.TryCreate(
                    protocolLimits ?? NetworkProtocolLimits.Default,
                    new DeterministicDatagramLimits(2_048, 512),
                    impairment,
                    out ProtocolLoopback? loopback,
                    out _,
                    UltraSimGameProtocol.Manifest));
                Loopback = loopback!;
            }
            ClientBridge = new RemoteUiClientBridge(ClientPeer, Client);
            ApplicationSessionEpoch = 77;
            Server = new RemoteGameApplicationSession(
                ServerPeer,
                _runtime.Game,
                _composition,
                authenticator ?? Authenticate,
                new ParticipantId(41),
                ApplicationSessionEpoch);
        }
        else
        {
            var link = new LocalSemanticLink(Client);
            _localPort = new LocalUiPort(link);
            _localAdmission = new AdmissionService(_runtime.Game, _composition, _localPort);
        }
    }

    internal RecordingRenderer Renderer { get; }
    internal UiClientRuntime Client { get; }
    internal ProtocolLoopback? Loopback { get; }
    internal NetworkProtocolPeer ClientPeer => Loopback?.Client ?? _udp!.Client;
    internal NetworkProtocolPeer ServerPeer => Loopback?.Server ?? _udp!.Server;
    internal RemoteUiClientBridge? ClientBridge { get; }
    internal RemoteGameApplicationSession? Server { get; }
    internal ulong ApplicationSessionEpoch { get; }
    internal UiViewInstanceId InventoryView { get; private set; }
    internal RemoteControlledSimulationClientSnapshot ControlledClientSnapshot =>
        _controlledClient?.GetSnapshot() ?? default;
    internal RemoteControlledSimulationServerSnapshot ControlledServerSnapshot =>
        _controlledServer?.GetSnapshot() ?? default;
    internal RemoteServerApplicationState ReconnectedServerState => _reconnectedServerState;

    internal GameProjectionService CreateProjection() => new(
        _runtime.Game,
        Server?.Admission ?? throw new InvalidOperationException("A remote Admission session is required."),
        new GameReplicationLimits(512, 8, 512, 1_024, 1_024, 64, 64));

    internal static Fixture Create(
        bool remote,
        NetworkImpairmentOptions impairment,
        NetworkProtocolLimits? protocolLimits = null,
        RemoteIdentityAuthenticator? authenticator = null) =>
        new(remote, impairment, protocolLimits, authenticator);

    internal static Fixture CreateUdp(
        NetworkProtocolLimits? protocolLimits = null,
        RemoteIdentityAuthenticator? authenticator = null) =>
        new(remote: true, default, protocolLimits, authenticator, udp: true);

    internal static Fixture CreateN06(
        NetworkImpairmentOptions impairment,
        bool allowVariableCadence = false,
        bool udp = false) =>
        new(remote: true, impairment, udp: udp,
            allowVariableCadence: allowVariableCadence);

    internal void AuthenticateAndEnter()
    {
        AdmissionResult waiting = AuthenticateToCharacterSelect();
        EnterWaitingCharacter(waiting);
    }

    internal AdmissionResult AuthenticateToCharacterSelect()
    {
        AdmissionService admission;
        if (_remote)
        {
            True(ClientBridge!.TryBeginAuthentication(1, "test", "alice-token"));
            DriveUntil(() => ClientBridge.GetSnapshot().State == RemoteClientApplicationState.Active &&
                Client.OpenViewCount == 1);
            admission = Server!.Admission;
        }
        else
        {
            AdmissionResult joined = _localAdmission!.JoinAccountBacked(
                new ParticipantId(41), new TrustedIdentity("test", "alice"));
            True(joined.IsWaitingForCharacter);
            admission = _localAdmission;
        }

        True(admission.TryGetSession(new ParticipantId(41), out AdmissionResult waiting) &&
            waiting.IsWaitingForCharacter);
        return waiting;
    }

    internal void EnterWaitingCharacter(AdmissionResult waiting)
    {
        AdmissionService admission = _remote ? Server!.Admission : _localAdmission!;
        ReadOnlySpan<UiListRow> rows = Client.GetRows(
            waiting.CharacterSelectView, CharacterSelectUi.Characters);
        Equal(1, rows.Length, "CharacterSelect did not contain Alice.");
        CaptureClientCheckpoint("character-select", waiting.CharacterSelectView);
        Client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, rows[0].Id);
        if (_remote)
            DriveUntil(() => admission.TryGetSession(new ParticipantId(41), out AdmissionResult value) &&
                value.IsEntered && Client.OpenViewCount == 0);
        True(admission.TryGetSession(new ParticipantId(41), out AdmissionResult entered) && entered.IsEntered);
    }

    internal void OpenInventory()
    {
        IUiSemanticPort port = _remote ? Server!.Ui : _localPort!;
        _inventory = new InventoryUiController(_runtime.Game, port);
        InventoryUiOpenResult opened = _inventory.Open(Resolve(_runtime.Game, _keys.Backpack));
        True(opened.Succeeded);
        InventoryView = opened.View;
        if (_remote) DriveUntil(() => Client.OpenViewCount == 1);
        Equal("Bread|Iron Sword", ClientRows());
        CaptureClientCheckpoint("inventory-open", InventoryView);
    }

    internal void EnableControlledSimulation()
    {
        if (!_remote || Server is null || ClientBridge is null)
            throw new InvalidOperationException("Controlled simulation requires an authenticated remote fixture.");
        _projection = new GameProjectionService(_runtime.Game, Server.Admission,
            new GameReplicationLimits(512, 8, 512, 1_024, 1_024, 64, 64));
        True(_projection.SynchronizeParticipant(new ParticipantId(41)));
        Equal(GameProjectionCaptureStatus.BaselineRebuilt,
            _projection.RebuildBaseline().Status);
        _controlledServer = new RemoteControlledSimulationServer(
            ServerPeer, _runtime.Game, _projection, new ParticipantId(41), ApplicationSessionEpoch);
        _controlledClient = new RemoteControlledSimulationClient(
            ClientPeer, ApplicationSessionEpoch, maximumPendingInputs: 64);
        Server.BindProtocolHandler(_controlledServer);
        ClientBridge.BindProtocolHandler(_controlledClient);
        TickAndConvergeOwnerState();
        True(ControlledClientSnapshot.OwnerPresent);
    }

    internal void QueueControlledVelocity(
        int inputVelocity,
        ulong targetTickOffset = 1,
        long expiryOffsetMicroseconds = 1_000_000)
    {
        True(_runtime.Game.TryCaptureClockSnapshot(out WorldClockSnapshot clock));
        True(_controlledClient!.TryQueueInputVelocity(
            checked(clock.SimulationTick + targetTickOffset),
            checked(clock.WorldTimeMicroseconds + expiryOffsetMicroseconds),
            inputVelocity));
    }

    internal void RepeatInputOnce() => True(_controlledClient!.TryRepeatPendingInputBundle());

    internal void RepeatInputUntilServerObserves(ulong sequence)
    {
        for (int step = 0; step < 50_000; step++)
        {
            PumpApplicationOnce();
            if (_controlledServer!.GetSnapshot().LastQueuedInputSequence >= sequence) return;
            if ((step & 7) == 7) _ = _controlledClient!.TryRepeatPendingInputBundle();
        }
        throw new InvalidOperationException(
            $"The redundant input bundle did not reach the server: {_controlledServer!.GetSnapshot()} / " +
            $"client={_controlledClient!.GetSnapshot()} / transport={Loopback!.GetSnapshot()}.");
    }

    internal void PublishTickAndConvergeOwnerState()
    {
        GameAuthoritativeIngressResult ingress = _controlledServer!.PublishInputs();
        Equal(GameAuthoritativeIngressStatus.Success, ingress.Status);
        TickAndConvergeOwnerState();
    }

    internal void TickAndConvergeOwnerState()
    {
        ulong previous = ControlledClientSnapshot.FrameSequence;
        _runtime.Tick();
        True(_controlledServer!.CaptureAndQueueOwnerState(out GameProjectionCaptureResult capture));
        True(capture.Succeeded);
        for (int step = 0; step < 50_000; step++)
        {
            PumpApplicationOnce();
            if (ControlledClientSnapshot.FrameSequence > previous) return;
            if ((step & 7) == 7) _ = _controlledServer.TryRepeatOwnerState();
        }
        throw new InvalidOperationException("The latest owner state did not converge.");
    }

    internal void RunControlledCycle(int velocity)
    {
        ulong sequence = checked(_controlledServer!.GetSnapshot().LastQueuedInputSequence + 1);
        True(_runtime.Game.TryCaptureClockSnapshot(out WorldClockSnapshot clock));
        True(_controlledClient!.TryQueueInputVelocity(
            checked(clock.SimulationTick + 1),
            checked(clock.WorldTimeMicroseconds + 1_000_000),
            velocity));
        for (int step = 0; _controlledServer.GetSnapshot().LastQueuedInputSequence < sequence; step++)
        {
            if (step == 50_000) throw new InvalidOperationException("Input did not converge.");
            PumpApplicationOnce();
            if ((step & 7) == 7) _ = _controlledClient.TryRepeatPendingInputBundle();
        }
        Equal(GameAuthoritativeIngressStatus.Success, _controlledServer.PublishInputs().Status);
        ulong frame = _controlledClient.GetSnapshot().FrameSequence;
        _runtime.Tick();
        True(_controlledServer.CaptureAndQueueOwnerState(out _));
        for (int step = 0; _controlledClient.GetSnapshot().FrameSequence <= frame; step++)
        {
            if (step == 50_000) throw new InvalidOperationException("Owner state did not converge.");
            PumpApplicationOnce();
            if ((step & 7) == 7) _ = _controlledServer.TryRepeatOwnerState();
        }
    }

    internal (int TickMarker, int InputVelocity) ReadControlledSimulation()
    {
        True(Server!.Admission.TryGetSession(new ParticipantId(41), out AdmissionResult entered));
        True(_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameSimulationState>(
            (world, _, game) =>
            {
                True(game.TryReadSimulationState(world, entered.Entity, out GameSimulationState state));
                return state;
            }, out GameSimulationState simulation));
        return (simulation.TickMarker, simulation.InputVelocity);
    }

    internal GameSimulationState ReadAliceSimulation()
    {
        (int marker, int velocity) = ReadControlledSimulation();
        return new GameSimulationState { TickMarker = marker, InputVelocity = velocity };
    }

    internal void TickRuntime() => _runtime.Tick();

    internal (ulong SavedTick, ulong DivergentTick, ulong RestoredTick, ulong Generation)
        SaveDivergeRestore()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ultrasim-network-n08-" + Guid.NewGuid().ToString("N"));
        try
        {
            var persistence = new GameRuntimePersistence(
                _runtime.Game,
                _composition,
                new PersistenceRestoreOptions(
                    new PersistenceLimits(
                        maximumArtifactBytes: 16 * 1024 * 1024,
                        maximumEntities: 100_000,
                        maximumShapes: 64,
                        maximumComponents: 64,
                        maximumSections: 256,
                        maximumComponentPayloadBytes: 8 * 1024 * 1024,
                        maximumTemporaryBytes: 64 * 1024 * 1024),
                    ChunkLayoutOptions.KiB16),
                new SaveSetCoordinator(root));
            ulong savedTick = _runtime.SimulationTick;
            GameSaveResult saved = persistence.TrySave(
                new SaveSetContext(1, 1_700_001_000_001),
                static capture => capture());
            True(saved.Succeeded);
            _runtime.Tick();
            _runtime.Tick();
            ulong divergentTick = _runtime.SimulationTick;
            GameLoadResult loaded = persistence.TryLoad();
            True(loaded.Succeeded);
            return (savedTick, divergentTick, _runtime.SimulationTick, loaded.GenerationId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal void SetSimulationFrequency(int frequency) => _runtime.SetSimulationFrequency(frequency);

    internal void RetireControlledAuthority()
    {
        True(_projection!.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(
            new ParticipantId(41), 1, GameSimulationCommandKind.RetireControlledEntity, 0)));
        Equal(GameAuthoritativeIngressStatus.Success, _controlledServer!.PublishInputs().Status);
        ulong frame = ControlledClientSnapshot.FrameSequence;
        _runtime.Tick();
        True(_controlledServer.CaptureAndQueueOwnerState(out _));
        for (int step = 0; step < 50_000; step++)
        {
            PumpApplicationOnce();
            if (ControlledClientSnapshot.FrameSequence > frame &&
                !ControlledClientSnapshot.OwnerPresent) return;
            if ((step & 7) == 7) _ = _controlledServer.TryRepeatOwnerState();
        }
        throw new InvalidOperationException("The controlled-entity tombstone did not converge.");
    }

    internal void QueueRawOwnerState(in RemoteOwnerStateMessage state)
    {
        Span<byte> payload = stackalloc byte[RemoteOwnerStateMessageCodec.EncodedSize];
        True(RemoteOwnerStateMessageCodec.TryWriteExact(payload, in state));
        Equal(NetworkQueueStatus.Accepted,
            ServerPeer.TryQueue(RemoteOwnerStateMessageCodec.Descriptor.Id, payload));
    }

    internal void QueueRawClockAnchor(uint revision, ulong effectiveTick, int frequency)
    {
        RemoteControlledSimulationClientSnapshot client = ControlledClientSnapshot;
        var anchor = new RemoteClockAnchorMessage
        {
            ApplicationSessionEpoch = ApplicationSessionEpoch,
            StateEpoch = client.StateEpoch.Value,
            ClockRevision = revision,
            EffectiveFromSimulationTick = effectiveTick,
            SimulationFrequencyHz = frequency,
            WorldTimeMicroseconds = client.WorldTimeMicroseconds,
            WorldTimeFractionQuanta = client.WorldTimeFractionQuanta,
        };
        Span<byte> payload = stackalloc byte[RemoteClockAnchorMessageCodec.EncodedSize];
        True(RemoteClockAnchorMessageCodec.TryWriteExact(payload, in anchor));
        Equal(NetworkQueueStatus.Accepted,
            ServerPeer.TryQueue(RemoteClockAnchorMessageCodec.Descriptor.Id, payload));
    }

    internal void DriveNetworkSteps(int count)
    {
        for (int step = 0; step < count; step++) PumpApplicationOnce();
    }

    internal void DriveUntilBaselineApplied(
        RemoteWorldStateServer server,
        ulong prior,
        int maximumSteps = 50_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (server.GetSnapshot().BaselinesApplied > prior) return;
            PumpApplicationOnce();
        }
        throw new InvalidOperationException("The atomic WorldState baseline did not converge.");
    }

    private void PumpApplicationOnce()
    {
        PumpTransport();
        ClientBridge!.ProcessReceived();
        Server!.ProcessReceived();
    }

    internal void SplitAndEquip()
    {
        UiRowId bread = FindInventoryRow("Bread");
        Client.SubmitAction(InventoryView, InventoryUi.Select, bread);
        if (_remote) DriveUntil(() => Client.GetActionState(InventoryView, InventoryUi.Split).Enabled);
        CaptureClientCheckpoint("bread-selected", InventoryView);
        Client.SubmitAction(InventoryView, InventoryUi.Split);
        if (_remote) DriveUntil(() => ClientRows() == "Bread|Bread|Iron Sword");
        CaptureClientCheckpoint("bread-split", InventoryView);

        UiRowId sword = FindInventoryRow("Iron Sword");
        Client.SubmitAction(InventoryView, InventoryUi.Select, sword);
        if (_remote) DriveUntil(() => Client.GetActionState(InventoryView, InventoryUi.Equip).Enabled);
        CaptureClientCheckpoint("sword-selected", InventoryView);
        Client.SubmitAction(InventoryView, InventoryUi.Equip);
        if (_remote) DriveUntil(() => ClientRows() == "Bread|Bread");
        Equal("Bread|Bread", ClientRows());
        CaptureClientCheckpoint("sword-equipped", InventoryView);

        True(_inventory!.Close(InventoryView));
        if (_remote) DriveUntil(() => Client.OpenViewCount == 0);
        InventoryUiOpenResult reopened = _inventory.OpenHot(Resolve(_runtime.Game, _keys.Backpack));
        True(reopened.Succeeded);
        InventoryView = reopened.View;
        if (_remote) DriveUntil(() => Client.OpenViewCount == 1);
        Equal("Bread|Bread", ClientRows());
        CaptureClientCheckpoint("inventory-hot-reopen", InventoryView);
    }

    internal void OpenSecondInventory()
    {
        InventoryUiOpenResult opened = _inventory!.Open(Resolve(_runtime.Game, _keys.Backpack));
        True(opened.Succeeded);
        if (_remote) DriveUntil(() => Client.OpenViewCount == 2);
    }

    internal UiRowId FindInventoryRow(string name)
    {
        foreach (UiListRow row in Client.GetRows(InventoryView, InventoryUi.Items))
            if (row.Values[0].Text == name) return row.Id;
        throw new InvalidOperationException($"Inventory row '{name}' was not found.");
    }

    internal ScenarioResult CaptureResult() => new(
        CaptureAuthority(),
        Renderer.Trace,
        _clientSemanticCheckpoints.ToString(),
        _networkPumps);

    internal string ClientRowsForTesting() => ClientRows();

    internal void QueueRawAction(UiActionRequest request)
    {
        if (!_remote) throw new InvalidOperationException("Raw remote intent requires a network transport.");
        byte[] semantic = new byte[UltraSimGameProtocol.DefaultUiWireLimits.MaximumOperationBytes];
        True(UiSemanticWireCodec.TryWriteAction(
            semantic, request, UltraSimGameProtocol.DefaultUiWireLimits, out int semanticBytes));
        byte[] message = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        True(UltraSimGameProtocol.TryWriteUiAction(
            message, ApplicationSessionEpoch, semantic.AsSpan(0, semanticBytes), out int messageBytes));
        ulong processed = Server!.GetSnapshot().MessagesProcessed;
        Equal(NetworkQueueStatus.Accepted, ClientPeer.TryQueue(
            RemoteUiActionHeaderCodec.Descriptor.Id, message.AsSpan(0, messageBytes)));
        DriveUntil(() => Server.GetSnapshot().MessagesProcessed > processed);
    }

    internal string CaptureAuthority()
    {
        True(_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, string>(
            (world, _, game) =>
            {
                Entity backpack = ResolveInWorld(world, _keys.Backpack);
                InventoryItemView[] rows = game.Inventory.Snapshot(world, backpack);
                var text = new StringBuilder();
                foreach (InventoryItemView row in rows.OrderBy(static row => row.PersistentId.Value))
                {
                    text.Append(row.Prefab.Value).Append(':')
                        .Append(row.Quantity).Append(':')
                        .Append((byte)row.EquipSlot).Append('|');
                }
                Entity sword = ResolveInWorld(world, _keys.Sword);
                True(game.Inventory.TryReadItem(world, sword, out InventoryItem item));
                text.Append("sword=").Append((byte)item.EquipSlot);
                return text.ToString();
            }, out string result));
        return result;
    }

    internal void DriveUntil(Func<bool> complete, int maximumSteps = 50_000)
    {
        if (!_remote) return;
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            PumpTransport();
            ClientBridge!.ProcessReceived();
            Server!.ProcessReceived();
        }
        throw new InvalidOperationException("The remote application workflow did not converge.");
    }

    internal void PumpTransport()
    {
        if (!_remote) return;
        if (Loopback is not null) Loopback.Pump();
        else _udp!.Pump();
        _networkPumps++;
    }

    internal void BeginTransportDrain() => _udp?.BeginDrain();
    internal bool IsTransportClosed => _udp?.State == UdpProtocolSessionState.Closed;

    internal double MeasureSelectionCycles(int count)
    {
        UiRowId bread = FindInventoryRow("Bread");
        UiRowId sword = FindInventoryRow("Iron Sword");
        if (_remote)
            DriveUntil(() => ClientPeer.ReliableQueued == 0 && ServerPeer.ReliableQueued == 0);
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < count; index++)
        {
            ulong accepted = _remote ? Server!.Ui.GetSnapshot().AcceptedActions : 0;
            ulong applied = _remote ? Server!.Ui.GetSnapshot().LastAppliedOperationId : 0;
            Client.SubmitAction(InventoryView, InventoryUi.Select, (index & 1) == 0 ? bread : sword);
            if (_remote)
            {
                DriveUntil(() => Server!.Ui.GetSnapshot().AcceptedActions > accepted &&
                    Server.Ui.GetSnapshot().LastAppliedOperationId > applied);
            }
        }
        return (Stopwatch.GetTimestamp() - started) * (1_000_000_000d / Stopwatch.Frequency);
    }

    internal bool ReconnectAndOpenCharacterSelect()
    {
        if (!_remote) return false;
        Server!.Disconnect();
        DriveUntil(() => ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
        Server.Dispose();
        ClientBridge!.Dispose();

        var renderer = new RecordingRenderer();
        var client = new UiClientRuntime(renderer);
        if (Loopback is null)
            throw new InvalidOperationException("The in-place reconnect fixture is specific to ProtocolLoopback.");
        using var bridge = new RemoteUiClientBridge(Loopback.Client, client);
        using var server = new RemoteGameApplicationSession(
            Loopback.Server,
            _runtime.Game,
            _composition,
            Authenticate,
            new ParticipantId(42),
            ApplicationSessionEpoch + 1);
        if (!bridge.TryBeginAuthentication(2, "test", "alice-token")) return false;
        for (int step = 0; step < 50_000; step++)
        {
            PumpTransport();
            bridge.ProcessReceived();
            server.ProcessReceived();
            if (bridge.GetSnapshot().State == RemoteClientApplicationState.Active &&
                client.OpenViewCount == 1)
            {
                RemoteUiClientSnapshot snapshot = bridge.GetSnapshot();
                return snapshot.ApplicationSessionEpoch == ApplicationSessionEpoch + 1 &&
                    snapshot.ParticipantId == 42 &&
                    renderer.Creates == 1 && client.CachedDefinitionCount == 1;
            }
        }
        return false;
    }

    internal RemoteWorldStateClientSnapshot ReconnectWithWorldBaseline(
        GameInterestService interest,
        ParticipantId participant)
    {
        if (!_remote || Loopback is null)
            throw new InvalidOperationException("The reconnect baseline fixture requires ProtocolLoopback.");
        Server!.Disconnect();
        DriveUntil(() => ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
        Server.Dispose();
        ClientBridge!.Dispose();

        var renderer = new RecordingRenderer();
        var uiClient = new UiClientRuntime(renderer);
        using var bridge = new RemoteUiClientBridge(Loopback.Client, uiClient);
        using var application = new RemoteGameApplicationSession(
            Loopback.Server,
            _runtime.Game,
            _composition,
            Authenticate,
            participant,
            ApplicationSessionEpoch + 1);
        True(bridge.TryBeginAuthentication(2, "test", "alice-token"));
        for (int step = 0; step < 50_000 &&
            bridge.GetSnapshot().State != RemoteClientApplicationState.Active; step++)
        {
            PumpTransport();
            bridge.ProcessReceived();
            application.ProcessReceived();
        }
        Equal(RemoteClientApplicationState.Active, bridge.GetSnapshot().State);

        var worldServer = new RemoteWorldStateServer(
            Loopback.Server, interest, participant, ApplicationSessionEpoch + 1,
            maximumRecordsPerBundle: 4, maximumBaselineRecords: 8);
        var worldClient = new RemoteWorldStateClient(
            Loopback.Client, ApplicationSessionEpoch + 1, interest.StateEpoch,
            maximumKnownIdentities: 8, maximumActiveEntities: 8,
            maximumRecordsPerBundle: 4);
        application.BindProtocolHandler(worldServer);
        bridge.BindProtocolHandler(worldClient);
        _ = worldServer.TryBeginBaseline(1, recordsPerSegment: 1);
        for (int step = 0; step < 50_000 &&
            worldServer.GetSnapshot().BaselinesApplied == 0; step++)
        {
            PumpTransport();
            bridge.ProcessReceived();
            application.ProcessReceived();
            _ = worldClient.TryFlushAcknowledgements();
            _ = worldServer.TryProgressBaseline();
        }
        Equal(1UL, worldServer.GetSnapshot().BaselinesApplied);
        _reconnectedServerState = application.GetSnapshot().State;
        return worldClient.GetSnapshot();
    }

    internal bool RecoverMissingInventoryDefinition()
    {
        if (!_remote) return false;
        _inventory = new InventoryUiController(_runtime.Game, Server!.Ui);
        Entity backpack = Resolve(_runtime.Game, _keys.Backpack);
        InventoryUiOpenResult missingHot = _inventory.OpenHot(backpack);
        if (!missingHot.Succeeded) return false;
        DriveUntil(() => Server.Ui.GetSnapshot().DesynchronizedViews == 1);
        if (Client.OpenViewCount != 0 || !_inventory.Close(missingHot.View)) return false;
        InventoryUiOpenResult cold = _inventory.Open(backpack);
        if (!cold.Succeeded) return false;
        InventoryView = cold.View;
        DriveUntil(() => Client.OpenViewCount == 1 && ClientRows() == "Bread|Iron Sword");
        return Server.Ui.GetSnapshot().DesynchronizedViews == 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _inventory?.Dispose(); } catch { }
        try { _projection?.Dispose(); } catch { }
        if (_remote)
        {
            try { Server?.Dispose(); } catch { }
            try { ClientBridge?.Dispose(); } catch { }
            Loopback?.Dispose();
            _udp?.Dispose();
        }
        else
        {
            _localAdmission?.Dispose();
            _localPort?.Dispose();
            Client.Dispose();
        }
        _host.Dispose();
    }

    private string ClientRows() => string.Join('|',
        Client.GetRows(InventoryView, InventoryUi.Items).ToArray()
            .Select(static row => row.Values[0].Text ?? string.Empty)
            .Order(StringComparer.Ordinal));

    private void CaptureClientCheckpoint(string stage, UiViewInstanceId instance)
    {
        UiViewInfo view = Client.GetView(instance);
        UiModel model = Client.SnapshotModel(instance);
        CompiledUiDefinition definition = model.Definition;
        StringBuilder text = _clientSemanticCheckpoints;
        AppendText(text, stage);
        text.Append("|open=").Append(Client.OpenViewCount)
            .Append("|cache-count=").Append(Client.CachedDefinitionCount)
            .Append("|view=").Append(view.Instance.Value)
            .Append("|sequence=").Append(view.LastSequence)
            .Append("|handle=").Append(view.Definition.High.ToString("x16"))
            .Append(view.Definition.Low.ToString("x16"));
        UiDefinitionCacheKey cache = view.CacheKey;
        text.Append("|cache=");
        AppendText(text, cache.ServerIdentity);
        AppendText(text, cache.CompositionIdentity);
        text.Append(cache.ProtocolVersion).Append(':');
        AppendText(text, cache.DefinitionKey.Value);
        text.Append(cache.DefinitionVersion).Append(':')
            .Append(cache.DefinitionHash).Append('|');

        foreach (UiSlotDefinition slot in definition.Slots)
        {
            text.Append("S").Append(slot.Id.Value).Append(':');
            AppendText(text, slot.Key);
            text.Append((byte)slot.ValueKind).Append(':');
            if (slot.ValueKind == UiSlotValueKind.List)
            {
                foreach (UiListRow row in model.GetRows(slot.Id))
                {
                    text.Append('R').Append(row.Id.Value).Append('[');
                    foreach (UiValue value in row.Values) AppendValue(text, value);
                    text.Append(']');
                }
            }
            else
            {
                AppendValue(text, model.Get(slot.Id));
            }
            text.Append('|');
        }
        foreach (UiActionDefinition action in definition.Actions)
        {
            UiActionState state = model.GetActionState(action.Id);
            text.Append("A").Append(action.Id.Value).Append(':');
            AppendText(text, action.Key);
            text.Append(state.Enabled ? '1' : '0').Append(':');
            AppendText(text, state.UnavailableReason ?? string.Empty);
            text.Append('|');
        }
        text.AppendLine();
    }

    private static void AppendValue(StringBuilder text, UiValue value)
    {
        text.Append((byte)value.Kind).Append(':');
        switch (value.Kind)
        {
            case UiValueKind.Text:
                AppendText(text, value.Text ?? string.Empty);
                break;
            case UiValueKind.Number:
                text.Append(value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case UiValueKind.Boolean:
                text.Append(value.Boolean ? '1' : '0');
                break;
        }
        text.Append(';');
    }

    private static void AppendText(StringBuilder text, string value) =>
        text.Append(value.Length).Append(':').Append(value).Append(';');

    private static bool Authenticate(
        string provider,
        string credential,
        out TrustedIdentity identity)
    {
        if (provider == "test" && credential == "alice-token")
        {
            identity = new TrustedIdentity("test", "alice");
            return true;
        }
        identity = default;
        return false;
    }
}

static FixtureKeys Populate(ServerGameFacet runtime)
{
    True(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, FixtureKeys>(
        (world, _, game) =>
        {
            True(world.TryInstantiate(game.Content.Character, out Entity alice));
            True(game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, alice, 8, out Entity backpack));
            True(game.Inventory.TryCreateItem(
                world, backpack, game.Content.Bread, 5, out Entity bread));
            True(game.Inventory.TryCreateItem(
                world, backpack, game.Content.IronSword, 1, out Entity sword));
            PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
            PersistentEntityId backpackId = world.GetOrAssignPersistentId(backpack);
            PersistentEntityId swordId = world.GetOrAssignPersistentId(sword);
            True(world.TryGetPrefabProvenance(alice, out PrefabKey prefab));
            var account = new AccountId(1);
            game.Accounting.AddAccount(new AccountRecord(account));
            game.Accounting.BindTrustedIdentity(new TrustedIdentity("test", "alice"), account);
            game.Accounting.AddCharacter(new CharacterRecord(
                new CharacterId(1), account, 0, "Alice", prefab,
                new PersistentEntityKey(world.PersistentWorldId, aliceId)));
            return new FixtureKeys(backpackId, swordId);
        }, out FixtureKeys keys));
    return keys;
}

static Entity Resolve(ServerGameFacet runtime, PersistentEntityId id)
{
    True(runtime.TryUseActiveGeneration(
        (world, _) => ResolveInWorld(world, id), out Entity entity));
    return entity;
}

static Entity ResolveInWorld(EcsWorld world, PersistentEntityId id)
{
    if (!world.TryResolvePersistentEntity(id, out Entity entity))
        throw new InvalidOperationException($"Persistent entity {id.Value} did not resolve.");
    return entity;
}

static void True(bool condition, string message = "Expected true.")
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message = "Expected false.") => True(!condition, message);

static void Equal<T>(T expected, T actual, string? message = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
}

readonly record struct FixtureKeys(PersistentEntityId Backpack, PersistentEntityId Sword);
readonly record struct ScenarioResult(
    string Authority,
    string UiTrace,
    string ClientState,
    ulong ProtocolSteps);

sealed record N04BenchmarkResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessorCount,
    int Samples,
    double OfflineDirectMeanNanoseconds,
    double OfflineDirectP95Nanoseconds,
    double ProtocolLoopbackMeanNanoseconds,
    double ProtocolLoopbackP95Nanoseconds,
    double ProtocolLoopbackToDirectRatio,
    ulong ProtocolStepsMedian,
    long ColdCallerAllocatedBytes,
    int EstablishedInteractionsPerSample,
    double EstablishedDirectMeanNanosecondsPerInteraction,
    double EstablishedDirectP95NanosecondsPerInteraction,
    double EstablishedProtocolMeanNanosecondsPerInteraction,
    double EstablishedProtocolP95NanosecondsPerInteraction,
    double EstablishedProtocolToDirectRatio,
    long EstablishedCallerAllocatedBytes,
    string Notes);

sealed record N06BenchmarkResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessorCount,
    ushort ProtocolVersion,
    ulong ProtocolFingerprint,
    string TieredCompilation,
    string ReadyToRun,
    int Samples,
    int CyclesPerSample,
    double ProtocolLoopbackMeanNanosecondsPerCycle,
    double ProtocolLoopbackP95NanosecondsPerCycle,
    double LocalhostUdpMeanNanosecondsPerCycle,
    double LocalhostUdpP95NanosecondsPerCycle,
    double UdpToLoopbackRatio,
    long CallerAllocatedBytes,
    string Notes);

sealed record N08BenchmarkResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessorCount,
    ushort ProtocolVersion,
    ulong ProtocolFingerprint,
    int Samples,
    int EntityCount,
    int RecordsPerSegment,
    double ProtocolLoopbackMeanNanoseconds,
    double ProtocolLoopbackP95Nanoseconds,
    double LocalhostUdpMeanNanoseconds,
    double LocalhostUdpP95Nanoseconds,
    double UdpToLoopbackRatio,
    long CallerAllocatedBytes,
    long LoopbackServerReservedBytes,
    long UdpServerReservedBytes,
    long LoopbackClientReservedBytes,
    long UdpClientReservedBytes,
    string Notes);

sealed record N05BenchmarkResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessorCount,
    string TieredCompilation,
    string ReadyToRun,
    int Samples,
    double ProtocolLoopbackMeanNanoseconds,
    double ProtocolLoopbackP95Nanoseconds,
    double LocalhostUdpMeanNanoseconds,
    double LocalhostUdpP95Nanoseconds,
    double UdpToLoopbackRatio,
    ulong UdpPumpsMedian,
    long ColdCallerAllocatedBytes,
    int EstablishedInteractionsPerSample,
    double EstablishedLoopbackMeanNanosecondsPerInteraction,
    double EstablishedLoopbackP95NanosecondsPerInteraction,
    double EstablishedUdpMeanNanosecondsPerInteraction,
    double EstablishedUdpP95NanosecondsPerInteraction,
    double EstablishedUdpToLoopbackRatio,
    long EstablishedCallerAllocatedBytes,
    string Notes);

sealed class LocalUiPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _handlers = [];

    internal LocalUiPort(LocalSemanticLink link)
    {
        _link = link;
        _link.ActionSubmitted += Forward;
    }

    public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
    public void UpdateView(UiUpdateView operation) => _link.UpdateView(operation);
    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);
    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _handlers.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _handlers.Remove(instance);

    public void Dispose()
    {
        _link.ActionSubmitted -= Forward;
        _handlers.Clear();
    }

    private void Forward(UiActionRequest request)
    {
        if (_handlers.TryGetValue(request.Instance, out Action<UiActionRequest>? handler))
            handler(request);
    }
}

sealed class RecordingRenderer : IUiRenderer
{
    private readonly List<string> _trace = [];

    internal int Creates { get; private set; }
    internal int Closes { get; private set; }
    internal int CloseAttempts { get; private set; }
    internal int ActiveHandles => Creates - Closes;
    internal string Trace => string.Join(';', _trace);
    internal bool FailNextApply { get; set; }
    internal bool FailNextClose { get; set; }
    internal bool RecordTrace { get; set; } = true;

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Creates++;
        if (RecordTrace) _trace.Add($"open:{definition.Key}:{instance.Value}");
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        if (FailNextApply)
        {
            FailNextApply = false;
            throw new InvalidOperationException("Injected renderer apply failure.");
        }
        if (RecordTrace)
        {
            string list = string.Join(',', delta.Lists.ToArray().Select(static patch => patch.Kind));
            _trace.Add($"update:{definition.Key}:{delta.Sequence}:{list}");
        }
    }

    public void Close(IUiRenderHandle handle)
    {
        CloseAttempts++;
        if (FailNextClose)
        {
            FailNextClose = false;
            throw new InvalidOperationException("Injected renderer close failure.");
        }
        Closes++;
        if (RecordTrace) _trace.Add("close");
    }

    private sealed class Handle : IUiRenderHandle;
}
}
