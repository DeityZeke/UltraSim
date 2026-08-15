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
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Network;
using UltraSim.Server.Hosting;
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
        };

        foreach ((string name, Action body) in tests)
        {
            body();
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"All {tests.Length} Network N0.4/N0.5/N0.6/N0.7 application tests passed.");
        if (args.Contains("--benchmark-n06", StringComparer.Ordinal))
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
        fixture.ServerPeer, interest, participant, fixture.ApplicationSessionEpoch, 8);
    var client = new RemoteWorldStateClient(
        fixture.ClientPeer, fixture.ApplicationSessionEpoch, epoch,
        maximumKnownIdentities: 16, maximumActiveEntities: 8,
        maximumRecordsPerBundle: 8);
    fixture.Server!.BindProtocolHandler(server);
    fixture.ClientBridge!.BindProtocolHandler(client);

    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(0, 4_096));
    Equal(GameWorldStateBuildStatus.OutstandingCapacityExceeded,
        server.TrySchedule(1, 4_096),
        "A different partial WorldState bundle could overwrite the unacknowledged family slot.");
    ulong retainedBundle = server.GetSnapshot().OutstandingBundleId;
    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(100, 4_096));
    Equal(retainedBundle, server.GetSnapshot().OutstandingBundleId,
        "WorldState retry changed semantic bundle identity instead of requeueing identical meaning.");
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 1);
    RemoteWorldStateClientSnapshot clientSnapshot = client.GetSnapshot();
    Equal(2, clientSnapshot.ActiveEntityCount);
    Equal(0, clientSnapshot.PendingApplicationAcknowledgements);
    Equal(0, interest.GetSnapshot().PendingRelationCount);

    Equal(GameInterestUpdateStatus.Success,
        interest.UpsertEntity(ProjectedWorldEntity(3, 1, 31), 2,
            new ReplicationSpatialPoint(1_500, 0), GameReplicationHorizon.Far, 1));
    Equal(GameWorldStateBuildStatus.Success, server.TrySchedule(1, 4_096));
    fixture.DriveUntil(() => server.GetSnapshot().ApplicationAcknowledgements == 2);
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
