using System.Net;
using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Client;
using UltraSim.ECS;
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
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_128Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 128);
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_256Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 256);
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_512Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 512);
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_1024Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 1024);
    yield return CreateServerRuntimePublishAndFanoutScenario("ServerRuntimePublishAndFanout_32Systems_2048Subs", systemCount: 32, playerCount: 16, accountCount: 16, subscriberCount: 2048);
    yield return CreateEcsDirtyPublishScenario("EcsDirtyPublish_1024Entities", entityCount: 1024);
    yield return CreateEcsDirtyPublishAndFanoutScenario("EcsDirtyPublishAndFanout_1024Entities_128Subs", entityCount: 1024, subscriberCount: 128);
    yield return CreateEcsClassifiedPublishAndFanoutScenario("EcsClassifiedPublishAndFanout_1024Realtime_256Bulk_128Subs", realtimeEntityCount: 1024, bulkEventCount: 256, subscriberCount: 128);
    yield return CreateEcsAoiLatestStatePublishAndFanoutScenario("EcsAoiLatestStatePublishAndFanout_1024Entities_128Connections", entityCount: 1024, connectionCount: 128);
    yield return CreateEcsAoiClassifiedPublishAndFanoutScenario("EcsAoiClassifiedPublishAndFanout_1024Realtime_256Reliable_128Bulk_128Connections", realtimeEntityCount: 1024, reliableEventCount: 256, bulkEventCount: 128, connectionCount: 128);
    yield return CreateClientMovementLoopbackScenario("ClientMovementLoopback_1024Inputs", inputCount: 1024);
    yield return CreateServerMovementLoopbackScenario("ServerMovementLoopback_1024Inputs", inputCount: 1024);
    yield return CreateUdpMovementRoundTripScenario("UdpMovementRoundTrip_1024Inputs", inputCount: 1024);
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

static BenchmarkScenario CreateEcsDirtyPublishScenario(string name, int entityCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "EcsNetworkPublish",
        Comparable = false,
        Scale = entityCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunEcsDirtyPublishScenario(entityCount),
    };
}

static BenchmarkScenario CreateEcsDirtyPublishAndFanoutScenario(string name, int entityCount, int subscriberCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "EcsNetworkFanout",
        Comparable = false,
        Scale = entityCount * subscriberCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunEcsDirtyPublishAndFanoutScenario(entityCount, subscriberCount),
    };
}

static BenchmarkScenario CreateEcsClassifiedPublishAndFanoutScenario(string name, int realtimeEntityCount, int bulkEventCount, int subscriberCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "EcsNetworkClassifiedFanout",
        Comparable = false,
        Scale = (realtimeEntityCount + bulkEventCount) * subscriberCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunEcsClassifiedPublishAndFanoutScenario(realtimeEntityCount, bulkEventCount, subscriberCount),
    };
}

static BenchmarkScenario CreateEcsAoiLatestStatePublishAndFanoutScenario(string name, int entityCount, int connectionCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "EcsNetworkAoiFanout",
        Comparable = false,
        Scale = entityCount * connectionCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunEcsAoiLatestStatePublishAndFanoutScenario(entityCount, connectionCount),
    };
}

static BenchmarkScenario CreateEcsAoiClassifiedPublishAndFanoutScenario(string name, int realtimeEntityCount, int reliableEventCount, int bulkEventCount, int connectionCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "EcsNetworkAoiClassifiedFanout",
        Comparable = false,
        Scale = (realtimeEntityCount + reliableEventCount + bulkEventCount) * connectionCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunEcsAoiClassifiedPublishAndFanoutScenario(realtimeEntityCount, reliableEventCount, bulkEventCount, connectionCount),
    };
}

static BenchmarkScenario CreateClientMovementLoopbackScenario(string name, int inputCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "ClientNetworkLoopback",
        Comparable = false,
        Scale = inputCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunClientMovementLoopbackScenario(inputCount),
    };
}

static BenchmarkScenario CreateServerMovementLoopbackScenario(string name, int inputCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "ServerMovementLoopback",
        Comparable = false,
        Scale = inputCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunServerMovementLoopbackScenario(inputCount),
    };
}

static BenchmarkScenario CreateUdpMovementRoundTripScenario(string name, int inputCount)
{
    return new BenchmarkScenario
    {
        Name = name,
        Category = "UdpMovementRoundTrip",
        Comparable = false,
        Scale = inputCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunUdpMovementRoundTripScenario(inputCount),
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

static ScenarioMeasurement RunEcsDirtyPublishScenario(int entityCount)
{
    ServerEcsWorldNetworkPublisher publisher = CreateEcsWorldPublisher(entityCount);
    try
    {
        int published = publisher.PublishDirtyLatestState<BenchPosition>(
            scopeSelector: static (_, position) => new SubscriptionKey((ulong)(position.ScopeBucket + 1)),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position));

        return new ScenarioMeasurement(
            published,
            published * 17L + publisher.LatestStateStream.GetSnapshot().ItemCount);
    }
    finally
    {
        GC.KeepAlive(publisher);
    }
}

static ScenarioMeasurement RunEcsDirtyPublishAndFanoutScenario(int entityCount, int subscriberCount)
{
    ServerEcsWorldNetworkPublisher publisher = CreateEcsWorldPublisher(entityCount);
    try
    {
        int published = publisher.PublishDirtyLatestState<BenchPosition>(
            scopeSelector: static (_, position) => new SubscriptionKey((ulong)(position.ScopeBucket + 1)),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position));

        var registry = new ConnectionSubscriptionRegistry();
        for (int subscriber = 0; subscriber < subscriberCount; subscriber++)
        {
            ConnectionId connection = new(subscriber + 1);
            for (int bucket = 0; bucket < BenchPosition.ScopeCount; bucket++)
                registry.Subscribe(connection, new SubscriptionKey((ulong)(bucket + 1)));
        }

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(registry));
        var scheduled = new ScheduledWorkItem[published];
        var subscribers = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[published * subscriberCount];

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
    finally
    {
        GC.KeepAlive(publisher);
    }
}

static ScenarioMeasurement RunEcsClassifiedPublishAndFanoutScenario(int realtimeEntityCount, int bulkEventCount, int subscriberCount)
{
    ServerEcsWorldNetworkPublisher publisher = CreateClassifiedEcsWorldPublisher(realtimeEntityCount, bulkEventCount);
    try
    {
        int realtimePublished = publisher.PublishDirtyLatestState(
            new ServerEcsLatestStateRule<BenchPosition>(
                scopeSelector: static (_, position) => new SubscriptionKey((ulong)(position.ScopeBucket + 1)),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Realtime));
        int bulkPublished = publisher.PublishDirtyOrderedEvents(
            new ServerEcsOrderedEventRule<BenchChunkEvent>(
                scopeSelector: static (_, evt) => new SubscriptionKey((ulong)(5000 + evt.ScopeBucket)),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.Bulk));

        var registry = new ConnectionSubscriptionRegistry();
        for (int subscriber = 0; subscriber < subscriberCount; subscriber++)
        {
            ConnectionId connection = new(subscriber + 1);
            for (int bucket = 0; bucket < BenchPosition.ScopeCount; bucket++)
                registry.Subscribe(connection, new SubscriptionKey((ulong)(bucket + 1)));
            for (int bucket = 0; bucket < BenchChunkEvent.ScopeCount; bucket++)
                registry.Subscribe(connection, new SubscriptionKey((ulong)(5000 + bucket)));
        }

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(registry));
        int published = realtimePublished + bulkPublished;
        var scheduled = new ScheduledWorkItem[published];
        var subscribers = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[published * subscriberCount];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscribers,
            output);

        long checksum = 0;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
            checksum += output[i].Payload.Length + output[i].ConnectionId.Value + (int)output[i].PacketClass;

        return new ScenarioMeasurement(result.Fanout.DatagramCount, checksum);
    }
    finally
    {
        GC.KeepAlive(publisher);
    }
}

static ScenarioMeasurement RunEcsAoiLatestStatePublishAndFanoutScenario(int entityCount, int connectionCount)
{
    var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f, 39.5f);
    var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
    ServerEcsWorldNetworkPublisher publisher = CreateEcsAoiWorldPublisher(entityCount);
    try
    {
        int published = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<BenchPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.ScopeBucket * 8f),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Realtime));

        var registry = new ConnectionSubscriptionRegistry();
        var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
        for (int connection = 0; connection < connectionCount; connection++)
        {
            float x = (connection % 16) * 12f;
            float y = (connection / 16) * 12f;
            maintainer.UpdateConnection(new ConnectionId(connection + 1), new WorldPosition2(x, y), registry);
        }

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(registry));
        var scheduled = new ScheduledWorkItem[published];
        var subscribers = new ConnectionId[Math.Max(256, connectionCount)];
        var output = new OutboundDatagram[Math.Max(1024, published * connectionCount)];

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
    finally
    {
        GC.KeepAlive(publisher);
    }
}

static ScenarioMeasurement RunEcsAoiClassifiedPublishAndFanoutScenario(int realtimeEntityCount, int reliableEventCount, int bulkEventCount, int connectionCount)
{
    var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f, 39.5f);
    var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
    ServerEcsWorldNetworkPublisher publisher = CreateEcsAoiClassifiedWorldPublisher(realtimeEntityCount, reliableEventCount, bulkEventCount);
    try
    {
        int realtimePublished = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<BenchAoiPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Realtime));
        int reliablePublished = publisher.PublishChunkAoiOrderedEvents(
            new ServerEcsChunkAoiOrderedEventRule<BenchAoiEvent>(
                classifier,
                positionSelector: static (_, evt) => new WorldPosition2(evt.X, evt.Y),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.ReliableEvent));
        int bulkPublished = publisher.PublishChunkAoiOrderedEvents(
            new ServerEcsChunkAoiOrderedEventRule<BenchAoiBulkEvent>(
                classifier,
                positionSelector: static (_, evt) => new WorldPosition2(evt.X, evt.Y),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.Bulk));

        var registry = new ConnectionSubscriptionRegistry();
        var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
        for (int connection = 0; connection < connectionCount; connection++)
        {
            float x = (connection % 16) * 12f;
            float y = (connection / 16) * 12f;
            maintainer.UpdateConnection(new ConnectionId(connection + 1), new WorldPosition2(x, y), registry);
        }

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(registry));
        int published = realtimePublished + reliablePublished + bulkPublished;
        var scheduled = new ScheduledWorkItem[published];
        var subscribers = new ConnectionId[Math.Max(256, connectionCount)];
        var output = new OutboundDatagram[Math.Max(1024, published * connectionCount)];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscribers,
            output);

        long checksum = 0;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
            checksum += output[i].Payload.Length + output[i].ConnectionId.Value + (int)output[i].PacketClass;

        return new ScenarioMeasurement(result.Fanout.DatagramCount, checksum);
    }
    finally
    {
        GC.KeepAlive(publisher);
    }
}

static ScenarioMeasurement RunClientMovementLoopbackScenario(int inputCount)
{
    using var pair = new LoopbackTransportPair();
    pair.Client.Start();
    pair.Server.Start();

    var bridge = new ClientMovementNetworkBridge(pair.Client, new ConnectionId(1));
    long checksum = 0;

    for (int i = 0; i < inputCount; i++)
    {
        bridge.TrySendInput(
            new ClientMovementInput((short)(i % 200 - 100), (short)(100 - (i % 200)), (byte)(i & 3)),
            (uint)(1000 + i),
            new ClientMovementState(i, i * 2, 1.5f, 2.5f),
            out _);
    }

    pair.Client.FlushOutbound();
    var inbound = new InboundDatagram[Math.Max(32, inputCount)];
    int received = pair.Server.PollInbound(inbound);
    for (int i = 0; i < received; i++)
    {
        if (!PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out _, out ReadOnlySpan<byte> payload))
            continue;

        var reader = new UltraSim.Network.BitReader(payload);
        MovementInputPacket input = MovementInputPacket.Deserialize(ref reader);
        ImmutablePayloadBuffer response = PacketCodec.Encode(new MovementStatePacket(
            NetworkEntityId: 1,
            ServerTick: (uint)(2000 + i),
            LastProcessedInputSequence: input.InputSequence,
            PositionX: i,
            PositionY: i * 2,
            VelocityX: 1.5f,
            VelocityY: 2.5f));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementStatePacket.Plane,
            MovementStatePacket.PacketClass,
            MovementStatePacket.Channel,
            response));
    }

    pair.Server.FlushOutbound();
    int decoded = bridge.PollInbound();
    checksum += decoded + bridge.LastReceiveResult.LastProcessedInputSequence + bridge.LastReceiveResult.ServerTick;

    return new ScenarioMeasurement(received + decoded, checksum);
}

static ScenarioMeasurement RunServerMovementLoopbackScenario(int inputCount)
{
    var builder = new EcsSchemaBuilder();
    ComponentId movementId = builder.RegisterComponent<BenchMovementBody>();
    World world = new(builder.Build());
    ArchetypeSignature signature = new([movementId]);
    Entity entity = world.CreateDefault(signature);
    world.SetComponent(entity, new BenchMovementBody(0f, 0f, 0f, 0f));

    using var pair = new LoopbackTransportPair();
    pair.Client.Start();
    pair.Server.Start();

    void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
    {
        if (!movementWorld.TryGetComponent(movementEntity, out BenchMovementBody body))
            return;

        float velocityX = input.MoveX / 100f;
        float velocityY = input.MoveY / 100f;
        body = body with
        {
            VelocityX = velocityX,
            VelocityY = velocityY,
            PositionX = body.PositionX + velocityX * deltaSeconds,
            PositionY = body.PositionY + velocityY * deltaSeconds,
        };
        movementWorld.SetComponent(movementEntity, body);
    }

    bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
    {
        if (!movementWorld.TryGetComponent(movementEntity, out BenchMovementBody body))
        {
            packet = default;
            return false;
        }

        packet = new MovementStatePacket(
            networkEntityId,
            serverTick,
            lastProcessedInputSequence,
            body.PositionX,
            body.PositionY,
            body.VelocityX,
            body.VelocityY);
        return true;
    }

    var client = new ClientMovementNetworkBridge(pair.Client, new ConnectionId(1));
    var server = new ServerEcsMovementNetworkBridge(
        world,
        pair.Server,
        ApplyMovementInput,
        ProjectMovementState);
    server.Bind(new ConnectionId(1), entity, networkEntityId: 1);

    long checksum = 0;
    for (int i = 0; i < inputCount; i++)
    {
        client.TrySendInput(
            new ClientMovementInput((short)(i % 200 - 100), (short)(100 - (i % 200)), (byte)(i & 3)),
            (uint)(1000 + i),
            new ClientMovementState(i, i * 2, 1.5f, 2.5f),
            out _);
        pair.Client.FlushOutbound();
        checksum += server.PollInputs();
        checksum += server.AdvanceAndSend(0.016f, (uint)(2000 + i));
        checksum += client.PollInbound();
    }

    if (world.TryGetComponent(entity, out BenchMovementBody finalState))
        checksum += (long)(finalState.PositionX * 1000f) + (long)(finalState.PositionY * 1000f);

    checksum += client.LastReceiveResult.ServerTick + client.LastReceiveResult.LastProcessedInputSequence;
    return new ScenarioMeasurement(inputCount * 3, checksum);
}

static ScenarioMeasurement RunUdpMovementRoundTripScenario(int inputCount)
{
    PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
    using var serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
        new IPEndPoint(IPAddress.Loopback, 0),
        metadata));
    serverTransport.Start();
    var serverEndPoint = (IPEndPoint)serverTransport.LocalEndPoint!;

    using var clientTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
        new IPEndPoint(IPAddress.Loopback, 0),
        metadata,
        serverEndPoint,
        new ConnectionId(1),
        AllowDynamicConnections: false));
    clientTransport.Start();

    var handshakePolicy = new HandshakePolicy(
        ProtocolVersion: 3,
        SchemaHash: 4,
        ContentHash: 5,
        RequiredFeatures: FeatureFlags.SharedPayloadFanout);
    var handshakeHello = new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
    var serverControl = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, serverTransport, handshakePolicy: handshakePolicy);
    var clientControl = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, clientTransport, clientHello: handshakeHello);

    clientControl.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
    for (int i = 0; i < 8; i++)
    {
        serverControl.Pump(101 + i * 2);
        clientControl.Pump(102 + i * 2);
    }

    var builder = new EcsSchemaBuilder();
    ComponentId movementId = builder.RegisterComponent<BenchMovementBody>();
    World world = new(builder.Build());
    ArchetypeSignature signature = new([movementId]);
    Entity entity = world.CreateDefault(signature);
    world.SetComponent(entity, new BenchMovementBody(0f, 0f, 0f, 0f));

    void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
    {
        if (!movementWorld.TryGetComponent(movementEntity, out BenchMovementBody body))
            return;

        float velocityX = input.MoveX / 100f;
        float velocityY = input.MoveY / 100f;
        body = body with
        {
            VelocityX = velocityX,
            VelocityY = velocityY,
            PositionX = body.PositionX + velocityX * deltaSeconds,
            PositionY = body.PositionY + velocityY * deltaSeconds,
        };
        movementWorld.SetComponent(movementEntity, body);
    }

    bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
    {
        if (!movementWorld.TryGetComponent(movementEntity, out BenchMovementBody body))
        {
            packet = default;
            return false;
        }

        packet = new MovementStatePacket(
            networkEntityId,
            serverTick,
            lastProcessedInputSequence,
            body.PositionX,
            body.PositionY,
            body.VelocityX,
            body.VelocityY);
        return true;
    }

    var client = new ClientMovementNetworkBridge(clientTransport, new ConnectionId(1));
    var server = new ServerEcsMovementNetworkBridge(world, serverTransport, ApplyMovementInput, ProjectMovementState);
    server.Bind(new ConnectionId(1), entity, networkEntityId: 1);

    long checksum = 0;
    for (int i = 0; i < inputCount; i++)
    {
        client.TrySendInput(
            new ClientMovementInput((short)(i % 200 - 100), (short)(100 - (i % 200)), (byte)(i & 3)),
            (uint)(1000 + i),
            new ClientMovementState(i, i * 2, 1.5f, 2.5f),
            out _);
        clientTransport.FlushOutbound();
        checksum += PollUntilNonZero(static serverBridge => serverBridge.PollInputs(), server, maxAttempts: 256);
        checksum += server.AdvanceAndSend(0.016f, (uint)(2000 + i));
        checksum += PollUntilNonZero(static clientBridge => clientBridge.PollInbound(), client, maxAttempts: 256);
    }

    if (world.TryGetComponent(entity, out BenchMovementBody finalState))
        checksum += (long)(finalState.PositionX * 1000f) + (long)(finalState.PositionY * 1000f);

    checksum += client.LastReceiveResult.ServerTick + client.LastReceiveResult.LastProcessedInputSequence;
    return new ScenarioMeasurement(inputCount * 3, checksum);
}

static int PollUntilNonZero<TState>(Func<TState, int> poll, TState state, int maxAttempts)
{
    for (int i = 0; i < maxAttempts; i++)
    {
        int result = poll(state);
        if (result > 0)
            return result;

        Thread.SpinWait(64);
    }

    return 0;
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

static ServerEcsWorldNetworkPublisher CreateEcsWorldPublisher(int entityCount)
{
    var builder = new EcsSchemaBuilder();
    ComponentId positionId = builder.RegisterComponent<BenchPosition>();
    World world = new(builder.Build());
    ArchetypeSignature signature = new([positionId]);

    for (int i = 0; i < entityCount; i++)
    {
        Entity entity = world.CreateDefault(signature);
        world.SetComponent(entity, new BenchPosition(i, i % BenchPosition.ScopeCount));
    }

    return new ServerEcsWorldNetworkPublisher(world);
}

static ServerEcsWorldNetworkPublisher CreateClassifiedEcsWorldPublisher(int realtimeEntityCount, int bulkEventCount)
{
    var builder = new EcsSchemaBuilder();
    ComponentId positionId = builder.RegisterComponent<BenchPosition>();
    ComponentId eventId = builder.RegisterComponent<BenchChunkEvent>();
    World world = new(builder.Build());
    ArchetypeSignature positionSignature = new([positionId]);
    ArchetypeSignature eventSignature = new([eventId]);

    for (int i = 0; i < realtimeEntityCount; i++)
    {
        Entity entity = world.CreateDefault(positionSignature);
        world.SetComponent(entity, new BenchPosition(i, i % BenchPosition.ScopeCount));
    }

    for (int i = 0; i < bulkEventCount; i++)
    {
        Entity entity = world.CreateDefault(eventSignature);
        world.SetComponent(entity, new BenchChunkEvent(i, i % BenchChunkEvent.ScopeCount));
    }

    return new ServerEcsWorldNetworkPublisher(world);
}

static ServerEcsWorldNetworkPublisher CreateEcsAoiWorldPublisher(int entityCount)
{
    var builder = new EcsSchemaBuilder();
    ComponentId positionId = builder.RegisterComponent<BenchPosition>();
    World world = new(builder.Build());
    ArchetypeSignature signature = new([positionId]);

    for (int i = 0; i < entityCount; i++)
    {
        Entity entity = world.CreateDefault(signature);
        int x = (i % 32) * 8;
        int yBucket = i / 32;
        world.SetComponent(entity, new BenchPosition(x, yBucket));
    }

    return new ServerEcsWorldNetworkPublisher(world);
}

static ServerEcsWorldNetworkPublisher CreateEcsAoiClassifiedWorldPublisher(int realtimeEntityCount, int reliableEventCount, int bulkEventCount)
{
    var builder = new EcsSchemaBuilder();
    ComponentId positionId = builder.RegisterComponent<BenchAoiPosition>();
    ComponentId reliableEventId = builder.RegisterComponent<BenchAoiEvent>();
    ComponentId bulkEventId = builder.RegisterComponent<BenchAoiBulkEvent>();
    World world = new(builder.Build());
    ArchetypeSignature positionSignature = new([positionId]);
    ArchetypeSignature reliableSignature = new([reliableEventId]);
    ArchetypeSignature bulkSignature = new([bulkEventId]);

    for (int i = 0; i < realtimeEntityCount; i++)
    {
        Entity entity = world.CreateDefault(positionSignature);
        world.SetComponent(entity, new BenchAoiPosition(
            X: (i % 32) * 8,
            Y: (i / 32) * 8));
    }

    for (int i = 0; i < reliableEventCount; i++)
    {
        Entity entity = world.CreateDefault(reliableSignature);
        world.SetComponent(entity, new BenchAoiEvent(
            X: (i % 32) * 8,
            Y: (i / 32) * 8,
            EventCode: i));
    }

    for (int i = 0; i < bulkEventCount; i++)
    {
        Entity entity = world.CreateDefault(bulkSignature);
        world.SetComponent(entity, new BenchAoiBulkEvent(
            X: (i % 32) * 8,
            Y: (i / 32) * 8,
            EventCode: i));
    }

    return new ServerEcsWorldNetworkPublisher(world);
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

readonly record struct BenchPosition(int X, int ScopeBucket) : IComponent<BenchPosition>
{
    public const int ScopeCount = 16;
}

readonly record struct BenchChunkEvent(int ChunkId, int ScopeBucket) : IComponent<BenchChunkEvent>
{
    public const int ScopeCount = 8;
}

readonly record struct BenchMovementBody(float PositionX, float PositionY, float VelocityX, float VelocityY) : IComponent<BenchMovementBody>;
readonly record struct BenchAoiPosition(int X, int Y) : IComponent<BenchAoiPosition>;
readonly record struct BenchAoiEvent(int X, int Y, int EventCode) : IComponent<BenchAoiEvent>;
readonly record struct BenchAoiBulkEvent(int X, int Y, int EventCode) : IComponent<BenchAoiBulkEvent>;
