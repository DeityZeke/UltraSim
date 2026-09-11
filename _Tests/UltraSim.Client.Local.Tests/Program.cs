using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Client.StandardUi;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inspection;
using UltraSim.Game.Input;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

if (args is ["--ui-shutdown-backpressure-child"])
{
    RunShutdownBackpressureChild();
    return;
}

var tests = new (string Name, Action Body)[]
{
    ("AuthorityTicksWhilePresentationIsStalled", AuthorityTicksWhilePresentationIsStalled),
    ("PublicDebtWaitsForClientApplicationProof", PublicDebtWaitsForClientApplicationProof),
    ("SequencedCommandsCrossOwnerThread", SequencedCommandsCrossOwnerThread),
    ("ChatOccurrencesApplyWithoutProtocolLoopback", ChatOccurrencesApplyWithoutProtocolLoopback),
    ("GuestIdentityDrivesAuthoritativeBodyAndChatLabel", GuestIdentityDrivesAuthoritativeBodyAndChatLabel),
    ("ChatIngressRejectsUnboundedText", ChatIngressRejectsUnboundedText),
    ("RejectedClientApplicationRetainsChatDebt", RejectedClientApplicationRetainsChatDebt),
    ("UiOperationsAndActionsUseBoundedLocalBridge", UiOperationsAndActionsUseBoundedLocalBridge),
    ("ProofBackpressureMustNotReapplyUiOperation", ProofBackpressureMustNotReapplyUiOperation),
    ("ProofBackpressureMustSurviveUiBridgeReplacement", ProofBackpressureMustSurviveUiBridgeReplacement),
    ("ReentrantBridgePumpCannotRejectSuccessfulOpen", ReentrantBridgePumpCannotRejectSuccessfulOpen),
    ("RejectedCallbackBridgeDisposeIsAtomic", RejectedCallbackBridgeDisposeIsAtomic),
    ("LocalUiCapacityMustNotFaultAuthority", LocalUiCapacityMustNotFaultAuthority),
    ("ControllerCloseFailureMustRemainRecoverable", ControllerCloseFailureMustRemainRecoverable),
    ("LocalShutdownMustContainUiCloseBackpressure", LocalShutdownMustContainUiCloseBackpressure),
    ("ServerMonitorIsReadOnlyLocalAndRefreshesSlowly", ServerMonitorIsReadOnlyLocalAndRefreshesSlowly),
    ("PreinstalledInventoryUsesExactHotOpenAndFreshSessionCache", PreinstalledInventoryUsesExactHotOpenAndFreshSessionCache),
    ("ConceptDemoWeaponUsesPacketFreeWorldInventoryAndPrimaryPath", ConceptDemoWeaponUsesPacketFreeWorldInventoryAndPrimaryPath),
    ("ConceptDemoSignUsesAuthoritativeInformationalUi", ConceptDemoSignUsesAuthoritativeInformationalUi),
    ("SaveLoadReplacesGenerationAndRebuildsPublicLineage", SaveLoadReplacesGenerationAndRebuildsPublicLineage),
    ("DistantInterestReentryInvalidatesOldPublicDebt", DistantInterestReentryInvalidatesOldPublicDebt),
};

foreach ((string name, Action body) in tests)
{
    if (args is ["--test", string selected] &&
        !string.Equals(name, selected, StringComparison.Ordinal))
        continue;
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} local client-session tests passed.");

static void ProofBackpressureMustNotReapplyUiOperation()
{
    LocalPlayableSessionLimits limits = LocalPlayableSessionLimits.Default with
    {
        UiOperationCapacity = 2,
        UiProofCapacity = 1,
    };
    using Fixture fixture = Fixture.Create(limits);
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);

    True(fixture.Session.TryRequestOpenInventory(1));
    True(fixture.Session.TryRequestOpenInventory(2));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2);

    _ = bridge.Pump(2);
    Equal(2, renderer.Created);
    DriveUntil(() =>
    {
        bridge.Pump(2);
        LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
        return snapshot.UiOperationsApplied == 2;
    }, failure: () => fixture.Session.GetSnapshot().ToString());
    Equal(0UL, fixture.Session.GetSnapshot().UiOperationsRejected);
    Equal(2, client.OpenViewCount);
    Equal(2, renderer.Created);

    UiViewInstanceId second = renderer.Instances[1];
    UiRowId firstRow = client.GetRows(second, InventoryUi.Items)[0].Id;
    client.SubmitAction(second, InventoryUi.Select, firstRow);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(second, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });
}

static void ProofBackpressureMustSurviveUiBridgeReplacement()
{
    LocalPlayableSessionLimits limits = LocalPlayableSessionLimits.Default with
    {
        UiOperationCapacity = 2,
        UiProofCapacity = 1,
    };
    using Fixture fixture = Fixture.Create(limits);
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    LocalQueuedUiClient original = fixture.Session.AttachUiClient(client);

    True(fixture.Session.TryRequestOpenInventory(1));
    True(fixture.Session.TryRequestOpenInventory(2));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2);

    _ = original.Pump(2);
    Equal(2, renderer.Created);
    original.Dispose();

    using var unrelatedProjection = new UiClientRuntime(
        new MemoryRenderer(), new UiClientLimits(8, 8));
    Throws<InvalidOperationException>(() =>
        fixture.Session.AttachUiClient(unrelatedProjection));

    using LocalQueuedUiClient replacement = fixture.Session.AttachUiClient(client);
    DriveUntil(() =>
    {
        replacement.Pump(2);
        LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
        return snapshot.UiOperationsApplied + snapshot.UiOperationsRejected == 2;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    Equal(2UL, fixture.Session.GetSnapshot().UiOperationsApplied);
    Equal(0UL, fixture.Session.GetSnapshot().UiOperationsRejected);
    Equal(2, client.OpenViewCount);
    Equal(2, renderer.Created);

    UiViewInstanceId second = renderer.Instances[1];
    UiRowId firstRow = client.GetRows(second, InventoryUi.Items)[0].Id;
    client.SubmitAction(second, InventoryUi.Select, firstRow);
    DriveUntil(() =>
    {
        replacement.Pump();
        return string.Equals(
            client.GetValue(second, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });
}

static void ReentrantBridgePumpCannotRejectSuccessfulOpen()
{
    using Fixture fixture = Fixture.Create();
    var renderer = new CallbackRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);
    renderer.OnCreate = () =>
    {
        try { _ = bridge.Pump(); }
        catch (InvalidOperationException) { renderer.CallbackRejected = true; }
    };

    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1);
    _ = bridge.Pump();
    DriveUntil(() =>
    {
        bridge.Pump();
        LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
        return snapshot.UiOperationsApplied + snapshot.UiOperationsRejected == 1;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    True(renderer.CallbackRejected,
        "The reentrant client pump was not rejected at the bridge boundary.");
    Equal(1UL, fixture.Session.GetSnapshot().UiOperationsApplied);
    Equal(0UL, fixture.Session.GetSnapshot().UiOperationsRejected);
    Equal(1, client.OpenViewCount);
    Equal(1, renderer.Created);

    UiViewInstanceId view = renderer.Instances[0];
    UiRowId firstRow = client.GetRows(view, InventoryUi.Items)[0].Id;
    client.SubmitAction(view, InventoryUi.Select, firstRow);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(view, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });
}

static void RejectedCallbackBridgeDisposeIsAtomic()
{
    using Fixture fixture = Fixture.Create();
    var renderer = new CallbackRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);
    renderer.OnCreate = () =>
    {
        try { bridge.Dispose(); }
        catch (InvalidOperationException) { renderer.CallbackRejected = true; }
    };

    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1);
    _ = bridge.Pump();
    DriveUntil(() =>
    {
        bridge.Pump();
        return fixture.Session.GetSnapshot().UiOperationsApplied == 1;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    True(renderer.CallbackRejected,
        "Bridge disposal from the renderer callback was not rejected.");
    UiViewInstanceId view = renderer.Instances[0];
    UiRowId firstRow = client.GetRows(view, InventoryUi.Items)[0].Id;
    client.SubmitAction(view, InventoryUi.Select, firstRow);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(view, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });

    bridge.Dispose();
    using LocalQueuedUiClient replacement = fixture.Session.AttachUiClient(client);
    ulong priorSequence = client.GetView(view).LastSequence;
    client.SubmitAction(view, InventoryUi.Select, firstRow);
    DriveUntil(() =>
    {
        replacement.Pump();
        return client.GetView(view).LastSequence > priorSequence;
    });
}

static void LocalUiCapacityMustNotFaultAuthority()
{
    LocalPlayableSessionLimits limits = LocalPlayableSessionLimits.Default with
    {
        UiOperationCapacity = 2,
        UiProofCapacity = 2,
    };
    using Fixture fixture = Fixture.CreateConcept(limits);
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);

    for (ulong sequence = 1; sequence <= 2; sequence++)
    {
        True(fixture.Session.TryRequestOpenInventory(sequence));
        ulong expected = sequence;
        DriveUntil(() =>
        {
            bridge.Pump();
            return fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
                view.LastAdministrativeSequence == expected && client.OpenViewCount == (int)expected;
        });
    }

    var input = new ClientWorldInputSequencer(fixture.Session.ControlSession);
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Interact,
            true),
        out GamePlayerCommand use));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        use, LocalPlayableSelection.InformationalSign, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == use.Sequence);
    LocalPlayableSessionSnapshot afterSign = fixture.Session.GetSnapshot();
    True(afterSign.IsRunning, afterSign.Fault);
    True(afterSign.Fault is null, afterSign.Fault);
    Equal(2, client.OpenViewCount);

    True(fixture.Session.TryRequestOpenServerMonitor(3));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 3);
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView rejected));
    True(!rejected.LastAdministrativeSucceeded);
    True(fixture.Session.GetSnapshot().IsRunning, fixture.Session.GetSnapshot().Fault);

    UiViewInstanceId first = renderer.Instances[0];
    client.SubmitAction(first, InventoryUi.Close);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1;
    });

    True(fixture.Session.TryRequestOpenServerMonitor(4));
    DriveUntil(() =>
    {
        bridge.Pump();
        return fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastAdministrativeSequence == 4 && view.LastAdministrativeSucceeded &&
            client.OpenViewCount == 2;
    }, failure: () => fixture.Session.GetSnapshot().ToString());
}

static void ControllerCloseFailureMustRemainRecoverable()
{
    {
        var signPort = new RecoveringSemanticPort();
        using var sign = new InformationalSignUiController(signPort);
        _ = sign.Open(GameInformationalSigns.ConceptDemoWelcome);
        signPort.FailNextClose = true;
        Throws<InvalidOperationException>(() => sign.Close());
        True(sign.HasOpenView);
        True(sign.Close());
        Equal(0, signPort.OpenViews);

        var monitorPort = new RecoveringSemanticPort();
        using var monitor = new ServerMonitorUiController(monitorPort);
        _ = monitor.Open(CreateMonitorSnapshot());
        monitorPort.FailNextUnbind = true;
        Throws<InvalidOperationException>(() => monitor.Close());
        True(!monitor.HasOpenView);
        Equal(0, monitorPort.OpenViews);
    }

    static ServerMonitorSnapshot CreateMonitorSnapshot() => new(
        revision: 1,
        generationId: 1,
        simulationTick: 1,
        simulationFrequencyHz: 30,
        generationUptimeSeconds: 1,
        recentTickMicroseconds: 1,
        p95TickMicroseconds: 1,
        entityCount: 1,
        maximumParticipants: 1,
        joins: 1,
        leaves: 0,
        testMarker: null,
        participants: []);
}

static void LocalShutdownMustContainUiCloseBackpressure()
{
    string executable = Environment.ProcessPath ??
        throw new InvalidOperationException("The current test executable path is unavailable.");
    using var process = Process.Start(new ProcessStartInfo(executable, "--ui-shutdown-backpressure-child")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    }) ?? throw new InvalidOperationException("The shutdown regression child did not start.");
    if (!process.WaitForExit(15_000))
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("The shutdown regression child did not exit.");
    }
    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    True(process.ExitCode == 0, $"Shutdown child exited {process.ExitCode}: {output}");
}

static void RunShutdownBackpressureChild()
{
    string saveRoot = Path.Combine(Path.GetTempPath(), $"ultrasim-ui-shutdown-{Guid.NewGuid():N}");
    Directory.CreateDirectory(saveRoot);
    try
    {
        LocalPlayableSessionLimits limits = LocalPlayableSessionLimits.Default with
        {
            UiOperationCapacity = 2,
            UiProofCapacity = 2,
        };
        var session = LocalPlayableSession.CreateCabin(saveRoot, limits);
        session.WaitUntilReady(TimeSpan.FromSeconds(10));
        True(session.TryRequestOpenInventory(1));
        True(session.TryRequestOpenInventory(2));
        DriveUntil(() => session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastAdministrativeSequence == 2);
        session.Dispose();
    }
    finally
    {
        if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, recursive: true);
    }
}

static void AuthorityTicksWhilePresentationIsStalled()
{
    using Fixture fixture = Fixture.Create();
    ulong before = fixture.Session.GetSnapshot().AuthorityTicks;
    Thread.Sleep(90);
    LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
    ulong after = snapshot.AuthorityTicks;
    True(after >= before + 3, $"Authority did not advance independently: {before}->{after}.");
    Console.WriteLine(
        $"EVIDENCE F0.0b authorityTicks={before}->{after} " +
        $"knownReservedBytes={snapshot.KnownReservedBytes}");
}

static void PublicDebtWaitsForClientApplicationProof()
{
    using Fixture fixture = Fixture.Create();
    Thread.Sleep(80);
    LocalPlayableSessionSnapshot stalled = fixture.Session.GetSnapshot();
    True(stalled.PendingWorldDeliveries > 0, "No bounded public debt accumulated while the client was stalled.");
    Equal(0UL, stalled.WorldDeliveriesApplied);

    ClientWorldSemanticCache cache = fixture.CreateCache();
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(cache);
        return fixture.Session.GetSnapshot().WorldDeliveriesApplied != 0 &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.Character) &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.CabinDoor) &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.WorldChest);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
}

static void SequencedCommandsCrossOwnerThread()
{
    using Fixture fixture = Fixture.Create();
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView before));
    var sequencer = new ClientWorldInputSequencer(fixture.Session.ControlSession);
    True(sequencer.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None,
            true),
        out GamePlayerCommand command));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        command, LocalPlayableSelection.None, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == command.Sequence &&
        view.PlayerPosition.GlobalXMillimeters > before.PlayerPosition.GlobalXMillimeters);
}

static void ChatOccurrencesApplyWithoutProtocolLoopback()
{
    using Fixture fixture = Fixture.Create();
    var chat = new ClientChatState(fixture.Session.ApplicationSessionEpoch);
    True(fixture.Session.TrySubmitChat(1, ClientChatSpeechMode.Say, "local hello"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(chat);
        return chat.GetSnapshot().Applied == 1;
    });
    var rows = new ClientChatOccurrence[4];
    Equal(1, chat.CopyRecent(rows));
    Equal("local hello", rows[0].Text);
    True(rows[0].IsSpatial);

    True(fixture.Session.TrySubmitChat(2, ClientChatSpeechMode.Say, "/where"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(chat);
        return chat.GetSnapshot().Applied == 2;
    });
    Equal(2, chat.CopyRecent(rows));
    True(rows[1].Text.StartsWith("Where:", StringComparison.Ordinal));
    Equal(ClientChatOccurrenceKind.PrivateSystem, rows[1].Kind);
}

static void GuestIdentityDrivesAuthoritativeBodyAndChatLabel()
{
    using Fixture fixture = Fixture.CreateGuest(
        new GameGuestAdmissionRequest(GameGuestBody.HumanMale, "  Demo Éowyn  "));
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view));
    Equal(new GameGuestIdentity(GameGuestBody.HumanMale, "Demo Éowyn"), view.GuestIdentity);

    ClientWorldSemanticCache cache = fixture.CreateCache();
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(cache);
        return ContainsPrefab(cache, GameWorldPublicPrefabIds.CharacterMale);
    });
    True(!ContainsPrefab(cache, GameWorldPublicPrefabIds.Character));

    var chat = new ClientChatState(fixture.Session.ApplicationSessionEpoch);
    True(fixture.Session.TrySubmitChat(1, ClientChatSpeechMode.Say, "identity check"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(chat);
        return chat.GetSnapshot().Applied == 1;
    });
    var rows = new ClientChatOccurrence[1];
    Equal(1, chat.CopyRecent(rows));
    Equal("Demo Éowyn", rows[0].DisplayLabel);
}

static void RejectedClientApplicationRetainsChatDebt()
{
    using Fixture fixture = Fixture.Create();
    var wrongLineage = new ClientChatState(fixture.Session.ApplicationSessionEpoch + 1);
    True(fixture.Session.TrySubmitChat(1, ClientChatSpeechMode.Say, "retain me"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(wrongLineage, maximumDeliveries: 1);
        return fixture.Session.GetSnapshot().ChatDeliveriesRejected != 0;
    });
    Equal(0UL, fixture.Session.GetSnapshot().ChatDeliveriesApplied);

    var correctLineage = new ClientChatState(fixture.Session.ApplicationSessionEpoch);
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(correctLineage, maximumDeliveries: 1);
        return correctLineage.GetSnapshot().Applied == 1 &&
            fixture.Session.GetSnapshot().ChatDeliveriesApplied == 1;
    });
    var rows = new ClientChatOccurrence[1];
    Equal(1, correctLineage.CopyRecent(rows));
    Equal("retain me", rows[0].Text);
}

static void ChatIngressRejectsUnboundedText()
{
    using Fixture fixture = Fixture.Create();
    True(!fixture.Session.TrySubmitChat(
        1, ClientChatSpeechMode.Say, new string('x', 257)));
    True(!fixture.Session.TrySubmitChat(
        2, ClientChatSpeechMode.Say, "\ud800"));
    Equal(0, fixture.Session.GetSnapshot().PendingChatDeliveries);
}

static void UiOperationsAndActionsUseBoundedLocalBridge()
{
    using Fixture fixture = Fixture.Create();
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);
    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1 && fixture.Session.GetSnapshot().UiOperationsApplied != 0;
    }, failure: () =>
    {
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view);
        return $"session={fixture.Session.GetSnapshot()} authority={view} views={client.OpenViewCount} created={renderer.Created}";
    });
    Equal(1, renderer.Created);

    UiViewInstanceId view = renderer.LastInstance;
    ReadOnlySpan<UiListRow> items = client.GetRows(view, InventoryUi.Items);
    True(items.Length != 0);
    client.SubmitAction(view, InventoryUi.Select, items[0].Id);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(view, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });
}

static void ServerMonitorIsReadOnlyLocalAndRefreshesSlowly()
{
    using Fixture fixture = Fixture.CreateConcept();
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView before));
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);

    True(fixture.Session.TryRequestOpenServerMonitor(1));
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1 &&
            fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView current) &&
            current.LastAdministrativeSequence == 1 &&
            current.LastAdministrativeKind == LocalAdministrativeOperationKind.OpenServerMonitor &&
            current.LastAdministrativeSucceeded;
    });

    UiViewInstanceId monitor = renderer.LastInstance;
    Equal("UltraSim Server Monitor — Read Only",
        client.GetValue(monitor, ServerMonitorUi.Title).Text!);
    Equal(1, ServerMonitorUi.Definition.ActionCount);
    string summary = client.GetValue(monitor, ServerMonitorUi.Summary).Text ?? string.Empty;
    True(summary.Contains("1/1 entered", StringComparison.Ordinal));
    True(summary.Contains("ECS entities", StringComparison.Ordinal));
    string timing = client.GetValue(monitor, ServerMonitorUi.Timing).Text ?? string.Empty;
    True(timing.Contains("Authoritative tick:", StringComparison.Ordinal));
    ReadOnlySpan<UiListRow> participants = client.GetRows(monitor, ServerMonitorUi.Participants);
    Equal(1, participants.Length);
    string row = participants[0].Values[0].Text ?? string.Empty;
    True(row.Contains("Local", StringComparison.Ordinal));
    True(row.Contains("Chunk (", StringComparison.Ordinal));
    True(!row.Contains("127.0.0.1", StringComparison.Ordinal));

    ulong initialModelSequence = client.GetView(monitor).LastSequence;
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.GetView(monitor).LastSequence > initialModelSequence;
    }, milliseconds: 3_000);
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView after));
    Equal(before.PlayerPosition, after.PlayerPosition);
    Equal(before.LastCommandSequence, after.LastCommandSequence);

    client.SubmitAction(monitor, ServerMonitorUi.Close);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 0;
    });
    True(UiDefinitionArtifactCodec.TryEncodeCanonical(
        ServerMonitorUi.Definition, out byte[] monitorDefinitionBytes));
    Console.WriteLine(
        $"EVIDENCE D0.6 local_monitor definition={ServerMonitorUi.Definition.Key}/" +
        $"v{ServerMonitorUi.Definition.Version}/{ServerMonitorUi.Definition.Hash} " +
        $"bytes={monitorDefinitionBytes.Length} rows={participants.Length} latency=Local " +
        $"refresh_sequence={initialModelSequence}->{initialModelSequence + 1} read_only=pass");
}

static void PreinstalledInventoryUsesExactHotOpenAndFreshSessionCache()
{
    using Fixture fixture = Fixture.Create();
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    Equal(0, client.CachedDefinitionCount);
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(
        client,
        ClientStandardUiLibrary.Resolve);
    Equal(1, client.CachedDefinitionCount);
    CompiledUiDefinition standard = ClientStandardUiLibrary.InventoryDefinition;
    Equal(InventoryUi.Definition.Key, standard.Key);
    Equal(InventoryUi.Definition.Version, standard.Version);
    Equal(InventoryUi.Definition.Hash, standard.Hash);

    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1 &&
            fixture.Session.GetSnapshot().UiHotOpensApplied == 1;
    });
    LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
    Equal(0UL, snapshot.UiColdOpensApplied);
    Equal(1UL, snapshot.UiHotOpensApplied);

    UiViewInstanceId view = renderer.LastInstance;
    ReadOnlySpan<UiListRow> items = client.GetRows(view, InventoryUi.Items);
    True(items.Length != 0);
    client.SubmitAction(view, InventoryUi.Select, items[0].Id);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(view, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });

    using Fixture freshFixture = Fixture.Create();
    var freshRenderer = new MemoryRenderer();
    using var freshClient = new UiClientRuntime(freshRenderer, new UiClientLimits(8, 8));
    Equal(0, freshClient.CachedDefinitionCount);
    Console.WriteLine(
        $"EVIDENCE F0.5 local_standard definition={standard.Key}/v{standard.Version} " +
        $"hash={standard.Hash} hot_opens={snapshot.UiHotOpensApplied} " +
        $"fresh_cached={freshClient.CachedDefinitionCount}");
}

static void ConceptDemoWeaponUsesPacketFreeWorldInventoryAndPrimaryPath()
{
    using Fixture fixture = Fixture.CreateConcept();
    ClientWorldSemanticCache cache = fixture.CreateCacheAround(new SpatialChunkCoord(0, 0));
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(cache);
        return TryFindPublic(cache, GameWorldPublicPrefabIds.IronSword, out _);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
    True(TryFindPublic(
        cache, GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord worldSword));
    Equal(
        WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem,
        worldSword.StateFlags);
    var publicMetadata = new ClientPublicPresentationMetadata[64];
    DriveUntil(() =>
    {
        int count = fixture.Session.CopyPublicPresentationMetadata(publicMetadata);
        return count > 0 && publicMetadata.AsSpan(0, count).ToArray().Any(candidate =>
            candidate.Id == worldSword.Id &&
            (candidate.Flags & ClientPublicPresentationFlags.ItemProperties) != 0 &&
            InspectionPresentationDefaults.TryFormat(
                candidate.DisplayLabel,
                candidate.InspectionProperties,
                out InspectionPresentation? presentation) &&
            presentation!.DetailText.Contains("Damage ", StringComparison.Ordinal) &&
            presentation.DetailText.Contains("Durability ", StringComparison.Ordinal));
    });

    var input = new ClientWorldInputSequencer(fixture.Session.ControlSession);
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Interact,
            true),
        out GamePlayerCommand pickup));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        pickup,
        LocalPlayableSelection.IronSword,
        false,
        default,
        false,
        worldSword.Id)));
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(cache);
        return fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == pickup.Sequence &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            !ContainsPublic(cache, worldSword.Id);
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(
        client,
        ClientStandardUiLibrary.Resolve);
    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    UiViewInstanceId inventory = renderer.LastInstance;
    UiRowId swordRow = FindRow(
        client.GetRows(inventory, InventoryUi.Items),
        "Iron Sword");
    client.SubmitAction(inventory, InventoryUi.Select, swordRow);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.GetActionState(inventory, InventoryUi.Equip).Enabled;
    });
    string details = client.GetValue(inventory, InventoryUi.Details).Text ?? string.Empty;
    True(details.Contains("Damage ", StringComparison.Ordinal));
    True(details.Contains("Quality ", StringComparison.Ordinal));
    True(details.Contains("Durability ", StringComparison.Ordinal));

    client.SubmitAction(inventory, InventoryUi.Equip);
    DriveUntil(() =>
    {
        bridge.Pump();
        fixture.Session.PumpWorld(cache);
        UiListRow row = client.GetRows(inventory, InventoryUi.Items)
            .ToArray().Single(candidate => candidate.Id == swordRow);
        return string.Equals(row.Values[2].Text, "Main Hand", StringComparison.Ordinal) &&
            TryFindPublic(cache, GameWorldPublicPrefabIds.IronSword, out _);
    });

    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Primary,
            true),
        out GamePlayerCommand primary));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        primary, LocalPlayableSelection.None, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == primary.Sequence &&
        view.LastCommandResult.Primary == GameWorldActionStatus.Accepted);

    client.SubmitAction(inventory, InventoryUi.Select, swordRow);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.GetActionState(inventory, InventoryUi.Drop).Enabled;
    });
    client.SubmitAction(inventory, InventoryUi.Drop);
    DriveUntil(() =>
    {
        bridge.Pump();
        fixture.Session.PumpWorld(cache);
        return !client.GetRows(inventory, InventoryUi.Items)
                .ToArray().Any(candidate => candidate.Id == swordRow) &&
            TryFindPublic(cache, GameWorldPublicPrefabIds.IronSword, out _);
    });

    Console.WriteLine(
        $"EVIDENCE I0.12 local_packet_free pickup={pickup.Sequence} " +
        $"primary={primary.Sequence} details='{details}'");
}

static void ConceptDemoSignUsesAuthoritativeInformationalUi()
{
    using Fixture fixture = Fixture.CreateConcept();
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);
    var input = new ClientWorldInputSequencer(fixture.Session.ControlSession);
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Interact,
            true),
        out GamePlayerCommand use));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        use,
        LocalPlayableSelection.InformationalSign,
        false,
        default,
        false)));
    DriveUntil(() =>
    {
        bridge.Pump();
        return fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == use.Sequence &&
            view.LastSelection == LocalPlayableSelection.InformationalSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            client.OpenViewCount == 1;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    UiViewInstanceId signView = renderer.LastInstance;
    True(string.Equals(
        GameInformationalSigns.ConceptDemoWelcome.Title,
        client.GetValue(signView, InformationalSignUi.Title).Text,
        StringComparison.Ordinal));
    True(string.Equals(
        GameInformationalSigns.ConceptDemoWelcome.Body,
        client.GetValue(signView, InformationalSignUi.Body).Text,
        StringComparison.Ordinal));

    client.SubmitAction(signView, InformationalSignUi.Close);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 0;
    }, failure: () => fixture.Session.GetSnapshot().ToString());

    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
        out GamePlayerCommand release));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        release, LocalPlayableSelection.None, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == release.Sequence);

    True(fixture.Session.TryRequestRelocation(1, GamePlayableWorld.FallbackLabApproachPosition));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1 && view.LastAdministrativeSucceeded);
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Interact,
            true),
        out GamePlayerCommand labUse));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        labUse,
        LocalPlayableSelection.FallbackLabSign,
        false,
        default,
        false)));
    DriveUntil(() =>
    {
        bridge.Pump();
        return fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
            view.LastCommandSequence == labUse.Sequence &&
            view.LastSelection == LocalPlayableSelection.FallbackLabSign &&
            view.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
            client.OpenViewCount == 1;
    }, failure: () => fixture.Session.GetSnapshot().ToString());
    UiViewInstanceId labView = renderer.LastInstance;
    True(string.Equals(
        GameInformationalSigns.FallbackLab.Title,
        client.GetValue(labView, InformationalSignUi.Title).Text,
        StringComparison.Ordinal));
    True(string.Equals(
        GameInformationalSigns.FallbackLab.Body,
        client.GetValue(labView, InformationalSignUi.Body).Text,
        StringComparison.Ordinal));
    client.SubmitAction(labView, InformationalSignUi.Close);
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 0;
    });
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
        out GamePlayerCommand labRelease));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        labRelease, LocalPlayableSelection.None, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == labRelease.Sequence);

    True(fixture.Session.TryRequestRelocation(2, GamePlayableWorld.CentralFarGatePosition));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2 && view.LastAdministrativeSucceeded);
    True(input.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop,
            GamePlayerActions.Interact,
            true),
        out GamePlayerCommand distantUse));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        distantUse,
        LocalPlayableSelection.InformationalSign,
        false,
        default,
        false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == distantUse.Sequence);
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView distant));
    Equal(GameWorldActionStatus.OutOfRange, distant.LastCommandResult.Interaction);
    Equal(0, client.OpenViewCount);
}

static void SaveLoadReplacesGenerationAndRebuildsPublicLineage()
{
    using Fixture fixture = Fixture.Create();
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView initial));
    True(fixture.Session.TryRequestSave(1));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1 && view.LastAdministrativeSucceeded);

    True(fixture.Session.TryRequestRelocation(2, GamePlayableWorld.ClearCrossChunkPosition));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2 && view.LastAdministrativeSucceeded &&
        view.PlayerPosition.Chunk == GamePlayableWorld.ClearCrossChunkPosition.Chunk);

    True(fixture.Session.TryRequestLoad(3));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 3 && view.LastAdministrativeSucceeded &&
        view.GenerationId > initial.GenerationId);
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView restored));
    Equal(GamePlayableWorld.PlayerStart, restored.PlayerPosition);

    ClientWorldSemanticCache replacement = fixture.CreateCache();
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(replacement);
        return ContainsPrefab(replacement, GameWorldPublicPrefabIds.Character) &&
            ContainsPrefab(replacement, GameWorldPublicPrefabIds.CabinDoor) &&
            ContainsPrefab(replacement, GameWorldPublicPrefabIds.WorldChest);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
}

static void DistantInterestReentryInvalidatesOldPublicDebt()
{
    using Fixture fixture = Fixture.CreateConcept();
    Thread.Sleep(90);
    True(fixture.Session.GetSnapshot().PendingWorldDeliveries > 0,
        "The stalled central client did not retain public delivery debt.");

    True(fixture.Session.TryRequestRelocation(1, GamePlayableWorld.FarGateDestination));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1 && view.LastAdministrativeSucceeded &&
        view.PlayerPosition == GamePlayableWorld.FarGateDestination);
    ClientWorldSemanticCache far = fixture.CreateCacheAround(
        GamePlayableWorld.FarGateDestination.Chunk);
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(far);
        return ContainsPrefab(far, GameWorldPublicPrefabIds.FarGate);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
    LocalPlayableSessionSnapshot outbound = fixture.Session.GetSnapshot();
    True(outbound.WorldDeliveriesInvalidated > 0,
        "Leaving the central interest set did not invalidate its retained delivery lineage.");
    Equal(0UL, outbound.WorldDeliveriesRejected);

    True(fixture.Session.TryRequestRelocation(2, GamePlayableWorld.CentralGateDestination));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2 && view.LastAdministrativeSucceeded &&
        view.PlayerPosition == GamePlayableWorld.CentralGateDestination);
    ClientWorldSemanticCache central = fixture.CreateCacheAround(
        GamePlayableWorld.CentralGateDestination.Chunk);
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(central);
        return ContainsPrefab(central, GameWorldPublicPrefabIds.FarGate);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
    Equal(0UL, fixture.Session.GetSnapshot().WorldDeliveriesRejected);
}

static bool ContainsPrefab(ClientWorldSemanticCache cache, uint prefab)
{
    var chunks = new ClientSemanticChunkInfo[25];
    if (!cache.TryCopyResidentInfo(chunks, out int count, out _)) return false;
    for (int index = 0; index < count; index++)
    {
        if (!cache.TryGetResidentView(chunks[index].Handle, out ClientSemanticChunkView view)) continue;
        foreach (ref readonly WorldPublicRecord row in view.PublicRecords)
            if (row.PrefabId == prefab) return true;
    }
    return false;
}

static bool ContainsPublic(ClientWorldSemanticCache cache, WorldPublicEntityId id)
{
    var chunks = new ClientSemanticChunkInfo[25];
    if (!cache.TryCopyResidentInfo(chunks, out int count, out _)) return false;
    for (int index = 0; index < count; index++)
    {
        if (!cache.TryGetResidentView(chunks[index].Handle, out ClientSemanticChunkView view)) continue;
        foreach (ref readonly WorldPublicRecord row in view.PublicRecords)
            if (row.Id == id) return true;
    }
    return false;
}

static bool TryFindPublic(
    ClientWorldSemanticCache cache,
    uint prefab,
    out WorldPublicRecord record)
{
    var chunks = new ClientSemanticChunkInfo[25];
    if (cache.TryCopyResidentInfo(chunks, out int count, out _))
    {
        for (int index = 0; index < count; index++)
        {
            if (!cache.TryGetResidentView(
                    chunks[index].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord candidate in view.PublicRecords)
            {
                if (candidate.PrefabId != prefab) continue;
                record = candidate;
                return true;
            }
        }
    }
    record = default;
    return false;
}

static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
{
    foreach (ref readonly UiListRow row in rows)
        if (row.Values.Length != 0 &&
            string.Equals(row.Values[0].Text, name, StringComparison.Ordinal))
            return row.Id;
    throw new InvalidOperationException($"UI row '{name}' was not found.");
}

static void DriveUntil(Func<bool> condition, int milliseconds = 5_000, Func<string>? failure = null)
{
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
        if (condition()) return;
        Thread.Sleep(2);
    }
    throw new InvalidOperationException(
        $"The local-session condition did not converge. {failure?.Invoke()}");
}

static void True(bool value, string? message = null)
{
    if (!value) throw new InvalidOperationException(message ?? "Expected true.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class Fixture : IDisposable
{
    private readonly string _saveRoot;

    private Fixture(string saveRoot, LocalPlayableSession session)
    {
        _saveRoot = saveRoot;
        Session = session;
    }

    internal LocalPlayableSession Session { get; }

    internal static Fixture Create()
        => Create(LocalPlayableSessionLimits.Default);

    internal static Fixture Create(LocalPlayableSessionLimits limits)
    {
        string saveRoot = Path.Combine(Path.GetTempPath(), $"ultrasim-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(saveRoot);
        LocalPlayableSession session = LocalPlayableSession.CreateCabin(saveRoot, limits);
        session.WaitUntilReady(TimeSpan.FromSeconds(10));
        return new Fixture(saveRoot, session);
    }

    internal static Fixture CreateConcept()
        => CreateConcept(LocalPlayableSessionLimits.Default);

    internal static Fixture CreateConcept(LocalPlayableSessionLimits limits)
    {
        string saveRoot = Path.Combine(Path.GetTempPath(), $"ultrasim-local-concept-{Guid.NewGuid():N}");
        Directory.CreateDirectory(saveRoot);
        LocalPlayableSession session = LocalPlayableSession.CreateConceptDemo(saveRoot, limits: limits);
        session.WaitUntilReady(TimeSpan.FromSeconds(10));
        return new Fixture(saveRoot, session);
    }

    internal static Fixture CreateGuest(in GameGuestAdmissionRequest request)
    {
        string saveRoot = Path.Combine(Path.GetTempPath(), $"ultrasim-local-guest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(saveRoot);
        LocalPlayableSession session = LocalPlayableSession.CreateCabin(saveRoot, request);
        session.WaitUntilReady(TimeSpan.FromSeconds(10));
        return new Fixture(saveRoot, session);
    }

    internal ClientWorldSemanticCache CreateCache()
    {
        Require(Session.TryGetLatestView(out LocalPlayableAuthorityView view));
        var cache = new ClientWorldSemanticCache(
            view.PreparedWorld,
            new ClientWorldSemanticCacheLimits(25, 16, 16, 16, 64));
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
        {
            var key = new SpatialChunkKey(
                GameWorldState.DefaultSpace,
                new SpatialChunkCoord(x, z));
            Require(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            Require(cache.TryStageFromBaseWorld(handle, [], default, out _));
            Require(cache.TryPublish(handle, out _));
        }
        return cache;
    }

    internal ClientWorldSemanticCache CreateCacheAround(SpatialChunkCoord center)
    {
        Require(Session.TryGetLatestView(out LocalPlayableAuthorityView view));
        var cache = new ClientWorldSemanticCache(
            view.PreparedWorld,
            new ClientWorldSemanticCacheLimits(25, 16, 16, 16, 64));
        for (int z = center.Z - 2; z <= center.Z + 2; z++)
        for (int x = center.X - 2; x <= center.X + 2; x++)
        {
            var key = new SpatialChunkKey(GameWorldState.DefaultSpace, x, z);
            Require(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            Require(cache.TryStageFromBaseWorld(handle, [], default, out _));
            Require(cache.TryPublish(handle, out _));
        }
        return cache;
    }

    public void Dispose()
    {
        Session.Dispose();
        if (Directory.Exists(_saveRoot)) Directory.Delete(_saveRoot, recursive: true);
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Fixture setup failed.");
    }
}

sealed class MemoryRenderer : IUiRenderer
{
    internal int Created;
    internal UiViewInstanceId LastInstance;
    internal List<UiViewInstanceId> Instances { get; } = [];

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Created++;
        LastInstance = instance;
        Instances.Add(instance);
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) { }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

sealed class CallbackRenderer : IUiRenderer
{
    internal Action? OnCreate;
    internal bool CallbackRejected;
    internal int Created;
    internal List<UiViewInstanceId> Instances { get; } = [];

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Created++;
        Instances.Add(instance);
        OnCreate?.Invoke();
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) { }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

sealed class RecoveringSemanticPort : IUiSemanticPort
{
    private readonly HashSet<UiViewInstanceId> _views = [];
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private ulong _nextView = 1;

    internal bool FailNextClose { get; set; }
    internal bool FailNextUnbind { get; set; }
    internal int OpenViews => _views.Count;

    public UiViewInstanceId OpenView(UiOpenView operation) => Allocate(operation.Instance);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => Allocate(operation.Instance);
    public void UpdateView(UiUpdateView operation)
    {
        if (!_views.Contains(operation.Instance))
            throw new InvalidOperationException("View is not open.");
    }
    public bool CloseView(UiCloseView operation)
    {
        if (FailNextClose)
        {
            FailNextClose = false;
            throw new InvalidOperationException("Injected close failure.");
        }
        _actions.Remove(operation.Instance);
        return _views.Remove(operation.Instance);
    }
    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance)
    {
        if (FailNextUnbind)
        {
            FailNextUnbind = false;
            throw new InvalidOperationException("Injected unbind failure.");
        }
        _actions.Remove(instance);
    }

    private UiViewInstanceId Allocate(UiViewInstanceId requested)
    {
        UiViewInstanceId instance = requested.IsValid
            ? requested
            : new UiViewInstanceId(_nextView++);
        _views.Add(instance);
        return instance;
    }
}
