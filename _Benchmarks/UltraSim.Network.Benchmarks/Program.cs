using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Network;

string outputPath = Path.Combine("_Benchmarks", "results", "network-benchmarks.json");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
        outputPath = args[i + 1];
}

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "NetworkKernel",
    kernelName: "UltraSim.Network",
    scenarios: CreateScenarios());
MicroBenchmark.WriteReport(report, outputPath);
Console.WriteLine($"Wrote network benchmark report to {outputPath}");

static IEnumerable<BenchmarkScenario> CreateScenarios()
{
    yield return CreateFanoutScenario("Fanout_SharedFrame_64Frames_128Subs", frameCount: 64, subscriberCount: 128, shared: true);
    yield return CreateFanoutScenario("Fanout_NaiveRebuild_64Frames_128Subs", frameCount: 64, subscriberCount: 128, shared: false);
    yield return CreateFanoutScenario("Fanout_SharedFrame_256Frames_512Subs", frameCount: 256, subscriberCount: 512, shared: true);
    yield return CreateFanoutScenario("Fanout_NaiveRebuild_256Frames_512Subs", frameCount: 256, subscriberCount: 512, shared: false);
    yield return CreateScheduleAndFanoutScenario("ScheduleAndFanout_64Items_128Subs", itemCount: 64, subscriberCount: 128);
    yield return CreateScheduleAndFanoutScenario("ScheduleAndFanout_256Items_512Subs", itemCount: 256, subscriberCount: 512);
    yield return CreateServerRuntimePublishScenario("ServerRuntimePublish_32Systems_16Players_16Accounts", systemCount: 32, playerCount: 16, accountCount: 16);
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_64Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 64);
    yield return CreateAoiScenario("Aoi_Update_512Connections_120Ticks", connectionCount: 512, ticks: 120);
    yield return CreateAoiScenario("Aoi_Update_2048Connections_120Ticks", connectionCount: 2048, ticks: 120);
}

static BenchmarkScenario CreateFanoutScenario(string name, int frameCount, int subscriberCount, bool shared)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "NetworkFanout",
        Comparable = false,
        Scale = frameCount * subscriberCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunFanoutScenario(frameCount, subscriberCount, shared),
    };
}

static BenchmarkScenario CreateAoiScenario(string name, int connectionCount, int ticks)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "NetworkAoi",
        Comparable = false,
        Scale = connectionCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunAoiScenario(connectionCount, ticks),
    };
}

static BenchmarkScenario CreateScheduleAndFanoutScenario(string name, int itemCount, int subscriberCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "NetworkScheduledFanout",
        Comparable = false,
        Scale = itemCount * subscriberCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunScheduleAndFanoutScenario(itemCount, subscriberCount),
    };
}

static BenchmarkScenario CreateServerRuntimePublishScenario(string name, int systemCount, int playerCount, int accountCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "ServerNetworkPublish",
        Comparable = false,
        Scale = systemCount + playerCount + accountCount + 2,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunServerRuntimePublishScenario(systemCount, playerCount, accountCount),
    };
}

static BenchmarkScenario CreateServerRuntimePublishAndFanoutScenario(string name, int systemCount, int playerCount, int accountCount, int subscriberCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "ServerNetworkFanout",
        Comparable = false,
        Scale = (systemCount + playerCount + accountCount + 2) * subscriberCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunServerRuntimePublishAndFanoutScenario(systemCount, playerCount, accountCount, subscriberCount),
    };
}

static ScenarioMeasurement RunFanoutScenario(int frameCount, int subscriberCount, bool shared)
{
    ScheduledWorkItem[] items = new ScheduledWorkItem[frameCount];
    SubscriptionKey[] scopeKeys = new SubscriptionKey[frameCount];
    for (int i = 0; i < frameCount; i++)
    {
        scopeKeys[i] = new SubscriptionKey((ulong)(i + 1));
        byte[] payloadBytes = new byte[24];
        for (int j = 0; j < payloadBytes.Length; j++)
            payloadBytes[j] = (byte)((i + j) & 0xFF);

        bool latest = (i & 1) == 0;
        items[i] = latest
            ? new ScheduledWorkItem(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                Sequence: (ulong)(i + 1),
                ItemKey: (ulong)(1000 + i),
                ScopeKey: scopeKeys[i],
                Payload: ImmutablePayloadBuffer.FromOwned(payloadBytes))
            : new ScheduledWorkItem(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: (ulong)(i + 1),
                ItemKey: 0,
                ScopeKey: scopeKeys[i],
                Payload: ImmutablePayloadBuffer.FromOwned(payloadBytes));
    }

    var registry = new ConnectionSubscriptionRegistry();
    for (int i = 0; i < frameCount; i++)
    {
        for (int subscriber = 0; subscriber < subscriberCount; subscriber++)
            registry.Subscribe(new ConnectionId(subscriber + 1), scopeKeys[i]);
    }

    var assembler = new OutboundDatagramAssembler();
    long checksum = 0;
    int operations;

    if (shared)
    {
        var bridge = new ScheduledWorkFanoutBridge(registry, assembler);
        var subscriberScratch = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[frameCount * subscriberCount];
        ScheduledWorkFanoutResult result = bridge.Dispatch(
            items,
            subscriberScratch,
            output);

        operations = result.DatagramCount;
        for (int i = 0; i < result.DatagramCount; i++)
            checksum += output[i].Payload.Length + output[i].ConnectionId.Value;
    }
    else
    {
        var subscribers = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[1];
        int datagramCount = 0;
        for (int i = 0; i < frameCount; i++)
        {
            int count = registry.CopySubscribers(scopeKeys[i], subscribers);
            for (int subscriber = 0; subscriber < count; subscriber++)
            {
                OutboundAssemblyResult result = assembler.Assemble(
                    subscribers[subscriber],
                    items.AsSpan(i, 1),
                    output);
                datagramCount += result.Count;
                checksum += output[0].Payload.Length + output[0].ConnectionId.Value;
            }
        }

        operations = datagramCount;
    }

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunScheduleAndFanoutScenario(int itemCount, int subscriberCount)
{
    var latest = new LatestStateStream();
    var ordered = new OrderedEventStream(Math.Max(8, itemCount));
    var registry = new ConnectionSubscriptionRegistry();

    for (int i = 0; i < itemCount; i++)
    {
        SubscriptionKey scopeKey = new((ulong)(5000 + i));
        byte[] payloadBytes = new byte[24];
        for (int j = 0; j < payloadBytes.Length; j++)
            payloadBytes[j] = (byte)((i + j) & 0xFF);

        if ((i & 1) == 0)
            latest.Publish((ulong)(1000 + i), scopeKey, ImmutablePayloadBuffer.FromOwned(payloadBytes));
        else
            ordered.Publish(scopeKey, ImmutablePayloadBuffer.FromOwned(payloadBytes));

        for (int subscriber = 0; subscriber < subscriberCount; subscriber++)
            registry.Subscribe(new ConnectionId(subscriber + 1), scopeKey);
    }

    var bridge = new ConnectionStreamFanoutBridge(
        new ConnectionStreamScheduler(latest, ordered),
        new ScheduledWorkFanoutBridge(registry));
    var scheduled = new ScheduledWorkItem[itemCount];
    var subscribers = new ConnectionId[subscriberCount];
    var output = new OutboundDatagram[itemCount * subscriberCount];

    ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
        default,
        ConnectionSendBudget.Unlimited,
        scheduled,
        subscribers,
        output);

    long checksum = 0;
    for (int i = 0; i < result.Fanout.DatagramCount; i++)
        checksum += output[i].Payload.Length + output[i].ConnectionId.Value;

    return new ScenarioMeasurement(result.Fanout.DatagramCount, checksum);
}

static ScenarioMeasurement RunAoiScenario(int connectionCount, int ticks)
{
    var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f, 39.5f);
    var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
    var registry = new ConnectionSubscriptionRegistry();

    long checksum = 0;
    for (int tick = 0; tick < ticks; tick++)
    {
        for (int i = 0; i < connectionCount; i++)
        {
            float x = (i % 64) * 3f + tick * 0.5f;
            float y = (i / 64) * 3f + ((tick + i) % 7) * 0.25f;
            AoiSubscriptionUpdateResult result = maintainer.UpdateConnection(
                new ConnectionId(i + 1),
                new WorldPosition2(x, y),
                registry);
            checksum += result.DesiredScopeCount + result.AddedCount - result.RemovedCount;
        }
    }

    for (int i = 0; i < connectionCount; i++)
        checksum += maintainer.RemoveConnection(new ConnectionId(i + 1), registry);

    return new ScenarioMeasurement(connectionCount * ticks, checksum);
}

static ScenarioMeasurement RunServerRuntimePublishScenario(int systemCount, int playerCount, int accountCount)
{
    using var publisher = CreateServerRuntimeNetworkPublisher(systemCount, playerCount, accountCount);
    ServerRuntimeNetworkPublishResult result = publisher.PublishCurrentState();

    int operations = result.StatusCount + result.MetricsCount + result.SystemCount + result.PlayerCount + result.AccountCount;
    long checksum = result.StatusCount + result.MetricsCount + result.SystemCount * 10L + result.PlayerCount * 100L + result.AccountCount * 1000L;
    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunServerRuntimePublishAndFanoutScenario(int systemCount, int playerCount, int accountCount, int subscriberCount)
{
    using var publisher = CreateServerRuntimeNetworkPublisher(systemCount, playerCount, accountCount);
    publisher.PublishCurrentState();

    var registry = new ConnectionSubscriptionRegistry();
    for (int subscriber = 0; subscriber < subscriberCount; subscriber++)
    {
        ConnectionId connection = new(subscriber + 1);
        registry.Subscribe(connection, ServerRuntimeNetworkScopes.OperatorStatus);
        registry.Subscribe(connection, ServerRuntimeNetworkScopes.OperatorSystems);
        registry.Subscribe(connection, ServerRuntimeNetworkScopes.OperatorPlayers);
        registry.Subscribe(connection, ServerRuntimeNetworkScopes.OperatorAccounts);
    }

    var bridge = new ConnectionStreamFanoutBridge(
        new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
        new ScheduledWorkFanoutBridge(registry));
    int itemCount = systemCount + playerCount + accountCount + 2;
    var scheduled = new ScheduledWorkItem[itemCount];
    var subscribers = new ConnectionId[subscriberCount];
    var output = new OutboundDatagram[itemCount * subscriberCount];

    ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
        default,
        ConnectionSendBudget.Unlimited,
        scheduled,
        subscribers,
        output);

    long checksum = 0;
    for (int i = 0; i < result.Fanout.DatagramCount; i++)
        checksum += output[i].Payload.Length + output[i].ConnectionId.Value;

    return new ScenarioMeasurement(result.Fanout.DatagramCount, checksum);
}

static ServerRuntimeNetworkPublisher CreateServerRuntimeNetworkPublisher(int systemCount, int playerCount, int accountCount)
{
    IServerApplication application = new ServerApplication(
        new ServerApplicationOptions("bench-runtime"),
        new ServerApplicationHooks
        {
            IsAcceptingConnections = _ => true,
            GetActiveSessions = _ => playerCount,
            GetSystemRuntimeSnapshots = _ => CreateSystems(systemCount),
            GetPlayerSessions = _ => CreatePlayers(playerCount),
            GetAccountSummaries = _ => CreateAccounts(accountCount),
        });
    application.Start();
    return new ServerRuntimeNetworkPublisher(application);
}

static IReadOnlyList<ServerSystemRuntimeSnapshot> CreateSystems(int count)
{
    var snapshots = new ServerSystemRuntimeSnapshot[count];
    for (int i = 0; i < count; i++)
    {
        snapshots[i] = new ServerSystemRuntimeSnapshot(
            $"System{i}",
            (i & 1) == 0 ? "Movement" : "Logic",
            Enabled: true,
            Manual: false,
            PeriodTicks: (i % 4) + 1,
            StartOffset: i % 3,
            LastRunTick: (uint)(100 + i),
            UpdateCount: 1000 + i,
            LastUpdateMilliseconds: 0.1 + i * 0.001,
            AverageUpdateMilliseconds: 0.2 + i * 0.001,
            PeakUpdateMilliseconds: 0.4 + i * 0.001,
            MinUpdateMilliseconds: 0.05,
            ControlCount: i % 2);
    }

    return snapshots;
}

static IReadOnlyList<ServerPlayerSessionSnapshot> CreatePlayers(int count)
{
    var snapshots = new ServerPlayerSessionSnapshot[count];
    for (int i = 0; i < count; i++)
        snapshots[i] = new ServerPlayerSessionSnapshot($"session-{i}", $"Player{i}", $"acct-{i}", "Authenticated", $"zone=island-{i % 4}");
    return snapshots;
}

static IReadOnlyList<ServerAccountSummarySnapshot> CreateAccounts(int count)
{
    var snapshots = new ServerAccountSummarySnapshot[count];
    for (int i = 0; i < count; i++)
        snapshots[i] = new ServerAccountSummarySnapshot($"acct-{i}", $"Player{i}", IsOnline: (i & 1) == 0, IsBanned: false, Detail: $"role={(i == 0 ? "operator" : "player")}");
    return snapshots;
}
