using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Network;

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

static ScenarioMeasurement RunFanoutScenario(int frameCount, int subscriberCount, bool shared)
{
    ScheduledWorkItem[] items = new ScheduledWorkItem[frameCount];
    SubscriptionKey[] scopeKeys = new SubscriptionKey[frameCount];
    for (int i = 0; i < frameCount; i++)
    {
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
                Payload: ImmutablePayloadBuffer.FromOwned(payloadBytes))
            : new ScheduledWorkItem(
                ScheduledWorkKind.OrderedEvent,
                PacketClass.ReliableEvent,
                Sequence: (ulong)(i + 1),
                ItemKey: 0,
                Payload: ImmutablePayloadBuffer.FromOwned(payloadBytes));
        scopeKeys[i] = new SubscriptionKey((ulong)(i + 1));
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
        var projected = new ScopedPublishedFrame[frameCount];
        var subscriberScratch = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[frameCount * subscriberCount];
        ScheduledWorkFanoutResult result = bridge.Dispatch(
            items,
            scopeKeys,
            projected,
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
