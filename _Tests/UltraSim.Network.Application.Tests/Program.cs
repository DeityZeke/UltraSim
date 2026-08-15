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
            ("RemoteWorkflowConvergesUnderDeterministicImpairment", RemoteWorkflowConvergesUnderImpairment),
            ("UnauthenticatedApplicationActionHasNoAuthority", UnauthenticatedApplicationActionHasNoAuthority),
            ("AuthenticationCapacityFailureIsContained", AuthenticationCapacityFailureIsContained),
            ("AuthenticationImplementationFailureDetachesWithoutDomainRejection", AuthenticationImplementationFailureDetachesWithoutDomainRejection),
            ("ControlAndCharacterSelectProgressThroughBulkBacklog", ControlAndCharacterSelectProgressThroughBulkBacklog),
            ("DuplicateAndStaleActionsCannotRepeatAuthority", DuplicateAndStaleActionsCannotRepeatAuthority),
            ("PresentationFailureAfterCommitDoesNotRollbackAuthority", PresentationFailureAfterCommitDoesNotRollbackAuthority),
            ("RejectedHotOpenRecoversThroughColdResnapshot", RejectedHotOpenRecoversThroughColdResnapshot),
            ("DisconnectDisposesRemoteClientProjection", DisconnectDisposesRemoteClientProjection),
            ("DisconnectCleanupContinuesAfterRendererCloseFailure", DisconnectCleanupContinuesAfterRendererCloseFailure),
            ("ReconnectStartsFreshApplicationSession", ReconnectStartsFreshApplicationSession),
        };

        foreach ((string name, Action body) in tests)
        {
            body();
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"All {tests.Length} Network N0.4 application tests passed.");
        if (args.Contains("--benchmark", StringComparer.Ordinal))
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

static void ControlAndCharacterSelectProgressThroughBulkBacklog()
{
    using Fixture fixture = Fixture.Create(remote: true, default);
    byte[] bulk = new byte[15_000];
    for (int index = 0; index < 4; index++)
        Equal(NetworkQueueStatus.Accepted, fixture.Loopback!.Server.TryQueue(
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
    Equal(NetworkQueueStatus.Accepted, fixture.Loopback!.Client.TryQueue(
        RemoteUiActionHeaderCodec.Descriptor.Id, message.AsSpan(0, messageBytes)));
    for (int step = 0; step < 64; step++)
    {
        fixture.Loopback.Pump();
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
    fixture.DriveUntil(() => fixture.Loopback!.Client.ReliableQueued == 0 &&
        fixture.Loopback.Server.ReliableQueued == 0);
    ScenarioResult result = fixture.CaptureResult();
    if (disconnect)
    {
        fixture.Server!.Disconnect();
        fixture.DriveUntil(() =>
            fixture.ClientBridge!.GetSnapshot().State == RemoteClientApplicationState.Detached);
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
    private bool _disposed;
    private readonly ulong _initialNetworkStep;

    private Fixture(
        bool remote,
        NetworkImpairmentOptions impairment,
        NetworkProtocolLimits? protocolLimits = null,
        RemoteIdentityAuthenticator? authenticator = null)
    {
        _remote = remote;
        _composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            runtimeProfile: remote ? GameRuntimeProfile.Networked : GameRuntimeProfile.Local);
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x90_0004));
        _host = new InternalHost(_composition, new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(512, identity,
                remote ? GameRuntimeProfile.Networked : GameRuntimeProfile.Local),
            WorldClockOptions.Default));
        _host.Start();
        _runtime = _host.RuntimeForTesting;
        _keys = Populate(_runtime.Game);
        Renderer = new RecordingRenderer();
        Client = new UiClientRuntime(Renderer);

        if (remote)
        {
            True(ProtocolLoopback.TryCreate(
                protocolLimits ?? NetworkProtocolLimits.Default,
                new DeterministicDatagramLimits(2_048, 512),
                impairment,
                out ProtocolLoopback? loopback,
                out _,
                UltraSimGameProtocol.Manifest));
            Loopback = loopback!;
            _initialNetworkStep = Loopback.GetSnapshot().Step;
            ClientBridge = new RemoteUiClientBridge(Loopback.Client, Client);
            ApplicationSessionEpoch = 77;
            Server = new RemoteGameApplicationSession(
                Loopback.Server,
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
    internal RemoteUiClientBridge? ClientBridge { get; }
    internal RemoteGameApplicationSession? Server { get; }
    internal ulong ApplicationSessionEpoch { get; }
    internal UiViewInstanceId InventoryView { get; private set; }

    internal static Fixture Create(
        bool remote,
        NetworkImpairmentOptions impairment,
        NetworkProtocolLimits? protocolLimits = null,
        RemoteIdentityAuthenticator? authenticator = null) =>
        new(remote, impairment, protocolLimits, authenticator);

    internal void AuthenticateAndEnter()
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
        ReadOnlySpan<UiListRow> rows = Client.GetRows(
            waiting.CharacterSelectView, CharacterSelectUi.Characters);
        Equal(1, rows.Length, "CharacterSelect did not contain Alice.");
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
    }

    internal void SplitAndEquip()
    {
        UiRowId bread = FindInventoryRow("Bread");
        Client.SubmitAction(InventoryView, InventoryUi.Select, bread);
        if (_remote) DriveUntil(() => Client.GetActionState(InventoryView, InventoryUi.Split).Enabled);
        Client.SubmitAction(InventoryView, InventoryUi.Split);
        if (_remote) DriveUntil(() => ClientRows() == "Bread|Bread|Iron Sword");

        UiRowId sword = FindInventoryRow("Iron Sword");
        Client.SubmitAction(InventoryView, InventoryUi.Select, sword);
        if (_remote) DriveUntil(() => Client.GetActionState(InventoryView, InventoryUi.Equip).Enabled);
        Client.SubmitAction(InventoryView, InventoryUi.Equip);
        if (_remote) DriveUntil(() => ClientRows() == "Bread|Bread");
        Equal("Bread|Bread", ClientRows());

        True(_inventory!.Close(InventoryView));
        if (_remote) DriveUntil(() => Client.OpenViewCount == 0);
        InventoryUiOpenResult reopened = _inventory.OpenHot(Resolve(_runtime.Game, _keys.Backpack));
        True(reopened.Succeeded);
        InventoryView = reopened.View;
        if (_remote) DriveUntil(() => Client.OpenViewCount == 1);
        Equal("Bread|Bread", ClientRows());
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
        ClientRows(),
        Loopback is null ? 0 : Loopback.GetSnapshot().Step - _initialNetworkStep);

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
            Loopback!.Pump();
            ClientBridge!.ProcessReceived();
            Server!.ProcessReceived();
        }
        throw new InvalidOperationException("The remote application workflow did not converge.");
    }

    internal double MeasureSelectionCycles(int count)
    {
        UiRowId bread = FindInventoryRow("Bread");
        UiRowId sword = FindInventoryRow("Iron Sword");
        if (_remote)
            DriveUntil(() => Loopback!.Client.ReliableQueued == 0 && Loopback.Server.ReliableQueued == 0);
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
        using var bridge = new RemoteUiClientBridge(Loopback!.Client, client);
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
            Loopback.Pump();
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
        if (_remote)
        {
            try { Server?.Dispose(); } catch { }
            try { ClientBridge?.Dispose(); } catch { }
            Loopback?.Dispose();
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
