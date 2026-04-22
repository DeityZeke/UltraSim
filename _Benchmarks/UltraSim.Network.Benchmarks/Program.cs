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
        var projected = new ScopedPublishedFrame[frameCount];
        int projectedCount = ScheduledWorkPublishedFrameProjector.Project(items, scopeKeys, projected);
        var dispatcher = new SubscriptionFanoutDispatcher(registry, assembler);
        var subscriberScratch = new ConnectionId[subscriberCount];
        var output = new OutboundDatagram[frameCount * subscriberCount];
        SubscriptionFanoutDispatchResult result = dispatcher.Dispatch(
            projected.AsSpan(0, projectedCount),
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
