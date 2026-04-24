using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using UltraSim.Client;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Network;

HarnessProgram.Run(args);

internal static class HarnessProgram
{
    public static int Run(string[] args)
    {
        HarnessArgs options = HarnessArgs.Parse(args);
        List<int> stages = HarnessArgs.ParseStages(options.Stages);

        string reportDir = Path.IsPathRooted(options.ReportDir)
            ? options.ReportDir
            : Path.Combine(Directory.GetCurrentDirectory(), options.ReportDir);
        Directory.CreateDirectory(reportDir);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string jsonPath = Path.Combine(reportDir, $"network_bot_harness_{stamp}.json");

        Console.WriteLine(
            $"Stages: {options.Stages}  Mode: {options.StageMode}  TickHz: {options.TickHz}  ActiveRatio: {options.ActiveRatio:0.##}  " +
            $"AoiRadii: {string.Join(",", options.AoiRadiiMeters)}  EventCap: {options.OrderedEventCapacity}  ReplayCap: {options.ReplayBufferCapacityPerConnection}");
        Console.WriteLine("Stage  Target Active  Ready  TickAvg  TickP95  TickP99  TickMax  PingAvg  PingP95  PingMax  PendMax    GC(0/1/2)  TickSLO  PingSLO");
        Console.WriteLine("--------------------------------------------------------------------------------------------------------------------------------");

        var rows = new JsonArray();
        using var harness = CreateHarness(options);

        for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
        {
            int stageValue = stages[stageIndex];
            int target = ResolveTarget(options.StageMode, stageValue, harness.ActiveClientCount);

            if (options.StageMode.Equals("independent", StringComparison.OrdinalIgnoreCase))
            {
                harness.Reset();
                target = stageValue;
            }

            StageResult result = harness.RunStage(stageIndex, target);
            rows.Add(result.ToJson());

            Console.WriteLine(
                $"{stageIndex,5}  {result.TargetClients,6} {result.ActiveClients,6}  {result.ReadyClients,5}  " +
                $"{result.TickAverageMs,7:0.000}  {result.TickP95Ms,7:0.000}  {result.TickP99Ms,7:0.000}  {result.TickMaxMs,7:0.000}  " +
                $"{result.PingAverageMs,7:0.000}  {result.PingP95Ms,8:0.000}  {result.PingMaxMs,7:0.000}  " +
                $"{result.PendingMax,7}  {result.Gen0Collections,4}/{result.Gen1Collections,2}/{result.Gen2Collections,2}  {(result.TickSloPass ? "PASS" : "FAIL"),7}  {(result.PingSloPass ? "PASS" : "FAIL"),7}");

            if (ShouldStop(options.StopOn, result))
                break;
        }

        var report = new JsonObject
        {
            ["timestamp_utc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["harness"] = "udp-session-host-ramp",
            ["args"] = options.ToJson(),
            ["stages"] = rows,
        };

        File.WriteAllText(jsonPath, report.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        }));
        Console.WriteLine($"Wrote harness report to {jsonPath}");
        return 0;
    }

    private static int ResolveTarget(string mode, int stageValue, int activeClients)
    {
        if (mode.Equals("additive", StringComparison.OrdinalIgnoreCase))
            return activeClients + stageValue;

        return stageValue;
    }

    private static bool ShouldStop(string stopOn, StageResult result)
    {
        return stopOn.ToLowerInvariant() switch
        {
            "tick" => !result.TickSloPass,
            "ping" => !result.PingSloPass,
            "none" => false,
            _ => !result.TickSloPass || !result.PingSloPass,
        };
    }

    private static IRampHarness CreateHarness(HarnessArgs options)
    {
        return options.Role.ToLowerInvariant() switch
        {
            "client" => new MovementRampHarness(options, clientOnly: true),
            _ => new MovementRampHarness(options, clientOnly: false),
        };
    }
}

internal interface IRampHarness : IDisposable
{
    int ActiveClientCount { get; }
    void Reset();
    StageResult RunStage(int stageIndex, int targetClients);
}

internal sealed class MovementRampHarness : IRampHarness
{
    private readonly HarnessArgs _options;
    private readonly bool _clientOnly;
    private readonly PacketMetadataRegistry _metadata;
    private readonly UdpSocketNetworkTransport? _serverTransport;
    private readonly World? _world;
    private readonly ArchetypeSignature _movementSignature;
    private readonly ServerMovementSessionHost? _serverHost;
    private readonly List<ClientHandle> _clients = [];
    private readonly List<Entity> _spawnedEntities = [];
    private readonly List<PendingChatSubmission> _pendingChatSubmissions = [];
    private uint _nextNetworkEntityId = 1;
    private long _clockTicks;
    private readonly PongProbePacket[] _pongScratch = new PongProbePacket[256];
    private readonly long _realtimeTickIntervalStopwatchTicks;

    public MovementRampHarness(HarnessArgs options, bool clientOnly)
    {
        _options = options;
        _clientOnly = clientOnly;
        _realtimeTickIntervalStopwatchTicks = options.RealtimePacing
            ? Math.Max(1L, (long)Math.Round(Stopwatch.Frequency / (double)options.TickHz, MidpointRounding.AwayFromZero))
            : 0L;
        _metadata = DefaultPacketMetadata.Create();

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<BenchMovementBody>();
        ComponentId movementVisualId = builder.RegisterComponent<BenchMovementVisualBody>();
        ComponentId movementBulkId = builder.RegisterComponent<BenchMovementBulkBody>();
        ComponentId movementEventId = builder.RegisterComponent<BenchMovementEventBody>();
        _world = clientOnly ? null : new World(builder.Build());
        _movementSignature = new ArchetypeSignature([movementId, movementVisualId, movementBulkId, movementEventId]);

        if (!clientOnly)
        {
            _serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(IPAddress.Loopback, options.Port),
                _metadata,
                EnableAsyncFlush: options.AsyncServerFlush,
                EnablePollProfiling: options.TransportPollProfiling));
            _serverTransport.Start();

            var handshakePolicy = new HandshakePolicy(
                ProtocolVersion: 3,
                SchemaHash: 4,
                ContentHash: 5,
                RequiredFeatures: FeatureFlags.SharedPayloadFanout);

            _serverHost = new ServerMovementSessionHost(
                _world!,
                _serverTransport,
                ApplyMovementInput,
                ProjectMovementState,
                SpawnSession,
                handshakePolicy: handshakePolicy,
                runtimeOptions: new ServerNetworkSessionRuntimeOptions(
                    MaxConnections: ServerNetworkSessionRuntimeOptions.Default.MaxConnections,
                    MaxSnapshotItems: ServerNetworkSessionRuntimeOptions.Default.MaxSnapshotItems,
                    MaxScheduledItems: ServerNetworkSessionRuntimeOptions.Default.MaxScheduledItems,
                    MaxReplayItems: ServerNetworkSessionRuntimeOptions.Default.MaxReplayItems,
                    MaxDatagrams: ServerNetworkSessionRuntimeOptions.Default.MaxDatagrams,
                    OrderedEventCapacity: options.OrderedEventCapacity,
                    ReplayBufferCapacityPerConnection: options.ReplayBufferCapacityPerConnection,
                    RetransmitPolicy: ServerNetworkSessionRuntimeOptions.Default.RetransmitPolicy),
                ownerCorrectionPolicy: new ServerMovementOwnerCorrectionPolicy(
                    _options.OwnerCorrectionDeadband,
                    _options.OwnerCorrectionMaxSilentTicks,
                    _options.OwnerCorrectionPhaseCadenceTicks));
            _serverHost.Start();
        }
    }

    public int ActiveClientCount => _clients.Count;

    public StageResult RunStage(int stageIndex, int targetClients)
    {
        int connectAttempts = EnsureClientCount(targetClients);
        PumpUntilReady(_options.SettleTicks);

        int measuredTicks = _options.StageTicks;
        var tickSamplesMs = new List<double>(measuredTicks);
        var serverPumpSamplesMs = new List<double>(measuredTicks);
        var serverPollSamplesMs = new List<double>(measuredTicks);
        var serverPollPendingDrainSamplesMs = new List<double>(measuredTicks);
        var serverPollSocketReceiveSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleUnpackSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleIndexSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleMaterializeSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleSliceSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleDatagramCreateSamplesMs = new List<double>(measuredTicks);
        var serverPollBundleEnqueueSamplesMs = new List<double>(measuredTicks);
        var serverPollPayloadCopySamplesMs = new List<double>(measuredTicks);
        var serverPollEnvelopeParseSamplesMs = new List<double>(measuredTicks);
        var serverPollAsyncReceiveSamplesMs = new List<double>(measuredTicks);
        var serverPollAsyncQueuedSamples = new List<double>(measuredTicks);
        var serverControlSamplesMs = new List<double>(measuredTicks);
        var serverControlEventsSamplesMs = new List<double>(measuredTicks);
        var serverRuntimeInboundSamplesMs = new List<double>(measuredTicks);
        var serverMovementInboundSamplesMs = new List<double>(measuredTicks);
        var serverSweepSamplesMs = new List<double>(measuredTicks);
        var serverLifecycleSamplesMs = new List<double>(measuredTicks);
        var serverMovementAdvanceSamplesMs = new List<double>(measuredTicks);
        var serverRuntimeDispatchSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushDrainSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushBundleSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushSendSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushAsyncPreviousSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushAsyncPreviousSendSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushAsyncHandoffWaitSamplesMs = new List<double>(measuredTicks);
        var serverControlFlushAsyncPendingSamples = new List<double>(measuredTicks);
        var serverControlFlushWireDatagramSamples = new List<double>(measuredTicks);
        var publishSamplesMs = new List<double>(measuredTicks);
        var dispatchSamplesMs = new List<double>(measuredTicks);
        var dispatchScheduleSamplesMs = new List<double>(measuredTicks);
        var dispatchFanoutSamplesMs = new List<double>(measuredTicks);
        var dispatchFlushSamplesMs = new List<double>(measuredTicks);
        var flushDrainSamplesMs = new List<double>(measuredTicks);
        var flushBundleSamplesMs = new List<double>(measuredTicks);
        var flushSendSamplesMs = new List<double>(measuredTicks);
        var flushConnectionSamples = new List<double>(measuredTicks);
        var flushWireDatagramSamples = new List<double>(measuredTicks);
        var asyncFlushPreviousSamplesMs = new List<double>(measuredTicks);
        var asyncFlushPreviousSendSamplesMs = new List<double>(measuredTicks);
        var asyncFlushHandoffWaitSamplesMs = new List<double>(measuredTicks);
        var asyncFlushPendingSamples = new List<double>(measuredTicks);
        var asyncFlushPreviousWireDatagramSamples = new List<double>(measuredTicks);
        var clientPumpSamplesMs = new List<double>(measuredTicks);
        var clientPollSamplesMs = new List<double>(measuredTicks);
        var clientPollPendingDrainSamplesMs = new List<double>(measuredTicks);
        var clientPollSocketReceiveSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleUnpackSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleIndexSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleMaterializeSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleSliceSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleDatagramCreateSamplesMs = new List<double>(measuredTicks);
        var clientPollBundleEnqueueSamplesMs = new List<double>(measuredTicks);
        var clientPollPayloadCopySamplesMs = new List<double>(measuredTicks);
        var clientPollEnvelopeParseSamplesMs = new List<double>(measuredTicks);
        var clientPollAsyncReceiveSamplesMs = new List<double>(measuredTicks);
        var clientPollAsyncQueuedSamples = new List<double>(measuredTicks);
        var clientControlSamplesMs = new List<double>(measuredTicks);
        var clientMovementSamplesMs = new List<double>(measuredTicks);
        var clientObserverStateSamplesMs = new List<double>(measuredTicks);
        var clientObserverStateDecodeSamplesMs = new List<double>(measuredTicks);
        var clientObserverStateApplySamplesMs = new List<double>(measuredTicks);
        var clientObserverEventSamplesMs = new List<double>(measuredTicks);
        long pendingSum = 0;
        int pendingMax = 0;
        var pingSamplesMs = new List<double>(Math.Max(32, _clients.Count * Math.Max(1, measuredTicks / Math.Max(1, _options.PingIntervalTicks))));
        long replicationDatagrams = 0;
        long replicationWireDatagrams = 0;
        long replicationBundledWireDatagrams = 0;
        long replicationSingleWireDatagrams = 0;
        long replicationPublishedItems = 0;
        long observerFramesReceived = 0;
        long observerFramesApplied = 0;
        long observerEventsReceived = 0;
        long observerEventsApplied = 0;
        long chatMessagesSubmitted = 0;
        long chatMessagesReceived = 0;
        long chatMessagesApplied = 0;
        long ownerCorrectionSent = 0;
        long ownerCorrectionSamples = 0;
        double ownerCorrectionPositionErrorSum = 0d;
        double ownerCorrectionPositionErrorMax = 0d;
        double ownerCorrectionVelocityErrorSum = 0d;
        double ownerCorrectionVelocityErrorMax = 0d;
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        StageReplicationContext? replication = !_clientOnly && _options.ObserverReplication
            ? CreateReplicationContext()
            : null;
        long nextTickDeadline = Stopwatch.GetTimestamp();

        for (int tick = 0; tick < measuredTicks; tick++)
        {
            QueueInputsForTick((uint)tick);
            QueueChatForTick((uint)tick, syntheticReplication: replication is not null);

            long start = Stopwatch.GetTimestamp();
            _clockTicks += 2;
            uint publishTick = (uint)(stageIndex * 100_000 + tick + 1);
            bool deferServerFlush = replication is not null;
            long serverPumpStart = Stopwatch.GetTimestamp();
            _serverHost?.Pump(_clockTicks, _options.TickDeltaSeconds, publishTick, flushOutbound: !deferServerFlush);
            long serverPumpEnd = Stopwatch.GetTimestamp();
            serverPumpSamplesMs.Add((serverPumpEnd - serverPumpStart) * 1000.0 / Stopwatch.Frequency);
            if (_serverHost is not null)
            {
                ServerPumpProfileSnapshot serverProfile = _serverHost.LastPumpProfile;
                serverPollSamplesMs.Add(serverProfile.PollElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollPendingDrainSamplesMs.Add(serverProfile.PollPendingDrainElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollSocketReceiveSamplesMs.Add(serverProfile.PollSocketReceiveElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleUnpackSamplesMs.Add(serverProfile.PollBundleUnpackElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleIndexSamplesMs.Add(serverProfile.PollBundleIndexElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleMaterializeSamplesMs.Add(serverProfile.PollBundleMaterializeElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleSliceSamplesMs.Add(serverProfile.PollBundleSliceElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleDatagramCreateSamplesMs.Add(serverProfile.PollBundleDatagramCreateElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollBundleEnqueueSamplesMs.Add(serverProfile.PollBundleEnqueueElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollPayloadCopySamplesMs.Add(serverProfile.PollPayloadCopyElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollEnvelopeParseSamplesMs.Add(serverProfile.PollEnvelopeParseElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollAsyncReceiveSamplesMs.Add(serverProfile.PollAsyncReceiveElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverPollAsyncQueuedSamples.Add(serverProfile.PollAsyncQueuedDatagramCount);
                serverControlSamplesMs.Add(serverProfile.ControlElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlEventsSamplesMs.Add(serverProfile.ControlEventElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverRuntimeInboundSamplesMs.Add(serverProfile.RuntimeInboundElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverMovementInboundSamplesMs.Add(serverProfile.MovementInboundElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverSweepSamplesMs.Add(serverProfile.SweepElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverLifecycleSamplesMs.Add(serverProfile.LifecycleElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverMovementAdvanceSamplesMs.Add(serverProfile.MovementAdvanceElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverRuntimeDispatchSamplesMs.Add(serverProfile.RuntimeDispatchElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushSamplesMs.Add(serverProfile.ControlFlushElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushDrainSamplesMs.Add(serverProfile.ControlFlushDrainElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushBundleSamplesMs.Add(serverProfile.ControlFlushBundleElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushSendSamplesMs.Add(serverProfile.ControlFlushSendElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushAsyncPreviousSamplesMs.Add(serverProfile.ControlFlushAsyncPreviousElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushAsyncPreviousSendSamplesMs.Add(serverProfile.ControlFlushAsyncPreviousSendElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushAsyncHandoffWaitSamplesMs.Add(serverProfile.ControlFlushAsyncHandoffWaitElapsedTicks * 1000.0 / Stopwatch.Frequency);
                serverControlFlushAsyncPendingSamples.Add(serverProfile.ControlFlushAsyncPendingBatchCount);
                serverControlFlushWireDatagramSamples.Add(serverProfile.ControlFlushWireDatagramCount);
                ownerCorrectionSent += _serverHost.Movement.LastSentStateCount;
            }
            else
            {
                serverPollSamplesMs.Add(0d);
                serverPollPendingDrainSamplesMs.Add(0d);
                serverPollSocketReceiveSamplesMs.Add(0d);
                serverPollBundleUnpackSamplesMs.Add(0d);
                serverPollBundleIndexSamplesMs.Add(0d);
                serverPollBundleMaterializeSamplesMs.Add(0d);
                serverPollBundleSliceSamplesMs.Add(0d);
                serverPollBundleDatagramCreateSamplesMs.Add(0d);
                serverPollBundleEnqueueSamplesMs.Add(0d);
                serverPollPayloadCopySamplesMs.Add(0d);
                serverPollEnvelopeParseSamplesMs.Add(0d);
                serverPollAsyncReceiveSamplesMs.Add(0d);
                serverPollAsyncQueuedSamples.Add(0d);
                serverControlSamplesMs.Add(0d);
                serverControlEventsSamplesMs.Add(0d);
                serverRuntimeInboundSamplesMs.Add(0d);
                serverMovementInboundSamplesMs.Add(0d);
                serverSweepSamplesMs.Add(0d);
                serverLifecycleSamplesMs.Add(0d);
                serverMovementAdvanceSamplesMs.Add(0d);
                serverRuntimeDispatchSamplesMs.Add(0d);
                serverControlFlushSamplesMs.Add(0d);
                serverControlFlushDrainSamplesMs.Add(0d);
                serverControlFlushBundleSamplesMs.Add(0d);
                serverControlFlushSendSamplesMs.Add(0d);
                serverControlFlushAsyncPreviousSamplesMs.Add(0d);
                serverControlFlushAsyncPreviousSendSamplesMs.Add(0d);
                serverControlFlushAsyncHandoffWaitSamplesMs.Add(0d);
                serverControlFlushAsyncPendingSamples.Add(0d);
                serverControlFlushWireDatagramSamples.Add(0d);
            }

            if (replication is not null)
            {
                UpdateObserverSubscriptions(replication);
                long publishStart = Stopwatch.GetTimestamp();
                int published = PublishObserverReplication(replication, publishTick);
                long publishEnd = Stopwatch.GetTimestamp();
                publishSamplesMs.Add((publishEnd - publishStart) * 1000.0 / Stopwatch.Frequency);
                if (published > 0)
                {
                    replicationPublishedItems += published;
                    long dispatchStart = Stopwatch.GetTimestamp();
                    int dispatched = DispatchObserverReplication(
                        replication,
                        out int wireDatagrams,
                        out double dispatchScheduleMs,
                        out double dispatchFanoutMs,
                        out double dispatchFlushMs,
                        out double flushDrainMs,
                        out double flushBundleMs,
                        out double flushSendMs,
                        out int flushConnectionCount,
                        out int flushWireDatagramCount,
                        out double asyncFlushPreviousMs,
                        out double asyncFlushPreviousSendMs,
                        out double asyncFlushHandoffWaitMs,
                        out int asyncFlushPendingCount,
                        out int asyncFlushPreviousWireDatagramCount,
                        out int bundledWireDatagrams,
                        out int singleWireDatagrams,
                        flushOutbound: true);
                    long dispatchEnd = Stopwatch.GetTimestamp();
                    replicationDatagrams += dispatched;
                    replicationWireDatagrams += wireDatagrams;
                    replicationBundledWireDatagrams += bundledWireDatagrams;
                    replicationSingleWireDatagrams += singleWireDatagrams;
                    dispatchSamplesMs.Add((dispatchEnd - dispatchStart) * 1000.0 / Stopwatch.Frequency);
                    dispatchScheduleSamplesMs.Add(dispatchScheduleMs);
                    dispatchFanoutSamplesMs.Add(dispatchFanoutMs);
                    dispatchFlushSamplesMs.Add(dispatchFlushMs);
                    flushDrainSamplesMs.Add(flushDrainMs);
                    flushBundleSamplesMs.Add(flushBundleMs);
                    flushSendSamplesMs.Add(flushSendMs);
                    flushConnectionSamples.Add(flushConnectionCount);
                    flushWireDatagramSamples.Add(flushWireDatagramCount);
                    asyncFlushPreviousSamplesMs.Add(asyncFlushPreviousMs);
                    asyncFlushPreviousSendSamplesMs.Add(asyncFlushPreviousSendMs);
                    asyncFlushHandoffWaitSamplesMs.Add(asyncFlushHandoffWaitMs);
                    asyncFlushPendingSamples.Add(asyncFlushPendingCount);
                    asyncFlushPreviousWireDatagramSamples.Add(asyncFlushPreviousWireDatagramCount);
                }
                else
                {
                    dispatchSamplesMs.Add(0d);
                    dispatchScheduleSamplesMs.Add(0d);
                    dispatchFanoutSamplesMs.Add(0d);
                    dispatchFlushSamplesMs.Add(0d);
                    flushDrainSamplesMs.Add(0d);
                    flushBundleSamplesMs.Add(0d);
                    flushSendSamplesMs.Add(0d);
                    flushConnectionSamples.Add(0d);
                    flushWireDatagramSamples.Add(0d);
                    asyncFlushPreviousSamplesMs.Add(0d);
                    asyncFlushPreviousSendSamplesMs.Add(0d);
                    asyncFlushHandoffWaitSamplesMs.Add(0d);
                    asyncFlushPendingSamples.Add(0d);
                    asyncFlushPreviousWireDatagramSamples.Add(0d);
                }
            }
            else
            {
                publishSamplesMs.Add(0d);
                dispatchSamplesMs.Add(0d);
                dispatchScheduleSamplesMs.Add(0d);
                dispatchFanoutSamplesMs.Add(0d);
                dispatchFlushSamplesMs.Add(0d);
                flushDrainSamplesMs.Add(0d);
                flushBundleSamplesMs.Add(0d);
                flushSendSamplesMs.Add(0d);
                flushConnectionSamples.Add(0d);
                flushWireDatagramSamples.Add(0d);
                asyncFlushPreviousSamplesMs.Add(0d);
                asyncFlushPreviousSendSamplesMs.Add(0d);
                asyncFlushHandoffWaitSamplesMs.Add(0d);
                asyncFlushPendingSamples.Add(0d);
                asyncFlushPreviousWireDatagramSamples.Add(0d);
            }
            long clientNowTicks = _clockTicks + 1;
            long clientPumpStart = Stopwatch.GetTimestamp();
            double clientPollMs = 0d;
            double clientPollPendingDrainMs = 0d;
            double clientPollSocketReceiveMs = 0d;
            double clientPollBundleUnpackMs = 0d;
            double clientPollBundleIndexMs = 0d;
            double clientPollBundleMaterializeMs = 0d;
            double clientPollBundleSliceMs = 0d;
            double clientPollBundleDatagramCreateMs = 0d;
            double clientPollBundleEnqueueMs = 0d;
            double clientPollPayloadCopyMs = 0d;
            double clientPollEnvelopeParseMs = 0d;
            double clientPollAsyncReceiveMs = 0d;
            double clientPollAsyncQueued = 0d;
            double clientControlMs = 0d;
            double clientMovementMs = 0d;
            double clientObserverStateMs = 0d;
            double clientObserverStateDecodeMs = 0d;
            double clientObserverStateApplyMs = 0d;
            double clientObserverEventMs = 0d;
            for (int i = 0; i < _clients.Count; i++)
            {
                ClientHandle client = _clients[i];
                if (client.Host.IsReady && _options.PingIntervalTicks > 0 && (tick % _options.PingIntervalTicks) == 0)
                    client.TrySendPing((uint)tick);

                client.Host.Pump(clientNowTicks);
                ClientPumpProfileSnapshot profile = client.Host.LastPumpProfile;
                clientPollMs += profile.PollElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollPendingDrainMs += profile.PollPendingDrainElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollSocketReceiveMs += profile.PollSocketReceiveElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleUnpackMs += profile.PollBundleUnpackElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleIndexMs += profile.PollBundleIndexElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleMaterializeMs += profile.PollBundleMaterializeElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleSliceMs += profile.PollBundleSliceElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleDatagramCreateMs += profile.PollBundleDatagramCreateElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollBundleEnqueueMs += profile.PollBundleEnqueueElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollPayloadCopyMs += profile.PollPayloadCopyElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollEnvelopeParseMs += profile.PollEnvelopeParseElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollAsyncReceiveMs += profile.PollAsyncReceiveElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientPollAsyncQueued += profile.PollAsyncQueuedDatagramCount;
                clientControlMs += profile.ControlElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientMovementMs += profile.MovementElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientObserverStateMs += profile.ObserverStateElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientObserverStateDecodeMs += profile.ObserverStateDecodeElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientObserverStateApplyMs += profile.ObserverStateApplyElapsedTicks * 1000.0 / Stopwatch.Frequency;
                clientObserverEventMs += profile.ObserverEventElapsedTicks * 1000.0 / Stopwatch.Frequency;
                client.CollectPongSamples(clientNowTicks, _pongScratch, pingSamplesMs);
            }
            long clientPumpEnd = Stopwatch.GetTimestamp();
            clientPumpSamplesMs.Add((clientPumpEnd - clientPumpStart) * 1000.0 / Stopwatch.Frequency);
            clientPollSamplesMs.Add(clientPollMs);
            clientPollPendingDrainSamplesMs.Add(clientPollPendingDrainMs);
            clientPollSocketReceiveSamplesMs.Add(clientPollSocketReceiveMs);
            clientPollBundleUnpackSamplesMs.Add(clientPollBundleUnpackMs);
            clientPollBundleIndexSamplesMs.Add(clientPollBundleIndexMs);
            clientPollBundleMaterializeSamplesMs.Add(clientPollBundleMaterializeMs);
            clientPollBundleSliceSamplesMs.Add(clientPollBundleSliceMs);
            clientPollBundleDatagramCreateSamplesMs.Add(clientPollBundleDatagramCreateMs);
            clientPollBundleEnqueueSamplesMs.Add(clientPollBundleEnqueueMs);
            clientPollPayloadCopySamplesMs.Add(clientPollPayloadCopyMs);
            clientPollEnvelopeParseSamplesMs.Add(clientPollEnvelopeParseMs);
            clientPollAsyncReceiveSamplesMs.Add(clientPollAsyncReceiveMs);
            clientPollAsyncQueuedSamples.Add(clientPollAsyncQueued);
            clientControlSamplesMs.Add(clientControlMs);
            clientMovementSamplesMs.Add(clientMovementMs);
            clientObserverStateSamplesMs.Add(clientObserverStateMs);
            clientObserverStateDecodeSamplesMs.Add(clientObserverStateDecodeMs);
            clientObserverStateApplySamplesMs.Add(clientObserverStateApplyMs);
            clientObserverEventSamplesMs.Add(clientObserverEventMs);
            long end = Stopwatch.GetTimestamp();

            double elapsedMs = (end - start) * 1000.0 / Stopwatch.Frequency;
            tickSamplesMs.Add(elapsedMs);

            int pending = 0;
            for (int i = 0; i < _clients.Count; i++)
                pending += _clients[i].Host.Movement.Sequencer.PendingCount;

            pendingSum += pending;
            if (pending > pendingMax)
                pendingMax = pending;

            WaitForRealtimeTick(ref nextTickDeadline);
        }

        long allocAfter = GC.GetTotalAllocatedBytes(false);
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);
        TickWindowStats stats = new TickWindowStats(tickSamplesMs);
        TickWindowStats serverPumpStats = new TickWindowStats(serverPumpSamplesMs);
        TickWindowStats serverPollStats = new TickWindowStats(serverPollSamplesMs);
        TickWindowStats serverPollPendingDrainStats = new TickWindowStats(serverPollPendingDrainSamplesMs);
        TickWindowStats serverPollSocketReceiveStats = new TickWindowStats(serverPollSocketReceiveSamplesMs);
        TickWindowStats serverPollBundleUnpackStats = new TickWindowStats(serverPollBundleUnpackSamplesMs);
        TickWindowStats serverPollBundleIndexStats = new TickWindowStats(serverPollBundleIndexSamplesMs);
        TickWindowStats serverPollBundleMaterializeStats = new TickWindowStats(serverPollBundleMaterializeSamplesMs);
        TickWindowStats serverPollBundleSliceStats = new TickWindowStats(serverPollBundleSliceSamplesMs);
        TickWindowStats serverPollBundleDatagramCreateStats = new TickWindowStats(serverPollBundleDatagramCreateSamplesMs);
        TickWindowStats serverPollBundleEnqueueStats = new TickWindowStats(serverPollBundleEnqueueSamplesMs);
        TickWindowStats serverPollPayloadCopyStats = new TickWindowStats(serverPollPayloadCopySamplesMs);
        TickWindowStats serverPollEnvelopeParseStats = new TickWindowStats(serverPollEnvelopeParseSamplesMs);
        TickWindowStats serverPollAsyncReceiveStats = new TickWindowStats(serverPollAsyncReceiveSamplesMs);
        TickWindowStats serverPollAsyncQueuedStats = new TickWindowStats(serverPollAsyncQueuedSamples);
        TickWindowStats serverControlStats = new TickWindowStats(serverControlSamplesMs);
        TickWindowStats serverControlEventsStats = new TickWindowStats(serverControlEventsSamplesMs);
        TickWindowStats serverRuntimeInboundStats = new TickWindowStats(serverRuntimeInboundSamplesMs);
        TickWindowStats serverMovementInboundStats = new TickWindowStats(serverMovementInboundSamplesMs);
        TickWindowStats serverSweepStats = new TickWindowStats(serverSweepSamplesMs);
        TickWindowStats serverLifecycleStats = new TickWindowStats(serverLifecycleSamplesMs);
        TickWindowStats serverMovementAdvanceStats = new TickWindowStats(serverMovementAdvanceSamplesMs);
        TickWindowStats serverRuntimeDispatchStats = new TickWindowStats(serverRuntimeDispatchSamplesMs);
        TickWindowStats serverControlFlushStats = new TickWindowStats(serverControlFlushSamplesMs);
        TickWindowStats serverControlFlushDrainStats = new TickWindowStats(serverControlFlushDrainSamplesMs);
        TickWindowStats serverControlFlushBundleStats = new TickWindowStats(serverControlFlushBundleSamplesMs);
        TickWindowStats serverControlFlushSendStats = new TickWindowStats(serverControlFlushSendSamplesMs);
        TickWindowStats serverControlFlushAsyncPreviousStats = new TickWindowStats(serverControlFlushAsyncPreviousSamplesMs);
        TickWindowStats serverControlFlushAsyncPreviousSendStats = new TickWindowStats(serverControlFlushAsyncPreviousSendSamplesMs);
        TickWindowStats serverControlFlushAsyncHandoffWaitStats = new TickWindowStats(serverControlFlushAsyncHandoffWaitSamplesMs);
        TickWindowStats serverControlFlushAsyncPendingStats = new TickWindowStats(serverControlFlushAsyncPendingSamples);
        TickWindowStats serverControlFlushWireDatagramStats = new TickWindowStats(serverControlFlushWireDatagramSamples);
        TickWindowStats publishStats = new TickWindowStats(publishSamplesMs);
        TickWindowStats dispatchStats = new TickWindowStats(dispatchSamplesMs);
        TickWindowStats dispatchScheduleStats = new TickWindowStats(dispatchScheduleSamplesMs);
        TickWindowStats dispatchFanoutStats = new TickWindowStats(dispatchFanoutSamplesMs);
        TickWindowStats dispatchFlushStats = new TickWindowStats(dispatchFlushSamplesMs);
        TickWindowStats flushDrainStats = new TickWindowStats(flushDrainSamplesMs);
        TickWindowStats flushBundleStats = new TickWindowStats(flushBundleSamplesMs);
        TickWindowStats flushSendStats = new TickWindowStats(flushSendSamplesMs);
        TickWindowStats flushConnectionStats = new TickWindowStats(flushConnectionSamples);
        TickWindowStats flushWireDatagramStats = new TickWindowStats(flushWireDatagramSamples);
        TickWindowStats asyncFlushPreviousStats = new TickWindowStats(asyncFlushPreviousSamplesMs);
        TickWindowStats asyncFlushPreviousSendStats = new TickWindowStats(asyncFlushPreviousSendSamplesMs);
        TickWindowStats asyncFlushHandoffWaitStats = new TickWindowStats(asyncFlushHandoffWaitSamplesMs);
        TickWindowStats asyncFlushPendingStats = new TickWindowStats(asyncFlushPendingSamples);
        TickWindowStats asyncFlushPreviousWireDatagramStats = new TickWindowStats(asyncFlushPreviousWireDatagramSamples);
        TickWindowStats clientPumpStats = new TickWindowStats(clientPumpSamplesMs);
        TickWindowStats clientPollStats = new TickWindowStats(clientPollSamplesMs);
        TickWindowStats clientPollPendingDrainStats = new TickWindowStats(clientPollPendingDrainSamplesMs);
        TickWindowStats clientPollSocketReceiveStats = new TickWindowStats(clientPollSocketReceiveSamplesMs);
        TickWindowStats clientPollBundleUnpackStats = new TickWindowStats(clientPollBundleUnpackSamplesMs);
        TickWindowStats clientPollBundleIndexStats = new TickWindowStats(clientPollBundleIndexSamplesMs);
        TickWindowStats clientPollBundleMaterializeStats = new TickWindowStats(clientPollBundleMaterializeSamplesMs);
        TickWindowStats clientPollBundleSliceStats = new TickWindowStats(clientPollBundleSliceSamplesMs);
        TickWindowStats clientPollBundleDatagramCreateStats = new TickWindowStats(clientPollBundleDatagramCreateSamplesMs);
        TickWindowStats clientPollBundleEnqueueStats = new TickWindowStats(clientPollBundleEnqueueSamplesMs);
        TickWindowStats clientPollPayloadCopyStats = new TickWindowStats(clientPollPayloadCopySamplesMs);
        TickWindowStats clientPollEnvelopeParseStats = new TickWindowStats(clientPollEnvelopeParseSamplesMs);
        TickWindowStats clientPollAsyncReceiveStats = new TickWindowStats(clientPollAsyncReceiveSamplesMs);
        TickWindowStats clientPollAsyncQueuedStats = new TickWindowStats(clientPollAsyncQueuedSamples);
        TickWindowStats clientControlStats = new TickWindowStats(clientControlSamplesMs);
        TickWindowStats clientMovementStats = new TickWindowStats(clientMovementSamplesMs);
        TickWindowStats clientObserverStateStats = new TickWindowStats(clientObserverStateSamplesMs);
        TickWindowStats clientObserverStateDecodeStats = new TickWindowStats(clientObserverStateDecodeSamplesMs);
        TickWindowStats clientObserverStateApplyStats = new TickWindowStats(clientObserverStateApplySamplesMs);
        TickWindowStats clientObserverEventStats = new TickWindowStats(clientObserverEventSamplesMs);
        TickWindowStats pingStats = new TickWindowStats(pingSamplesMs);
        int readyClients = 0;
        var perClientPing = new JsonArray();
        for (int i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Host.IsReady)
                readyClients++;
            observerFramesReceived += _clients[i].ObserverFramesReceived;
            observerFramesApplied += _clients[i].ObserverFramesApplied;
            observerEventsReceived += _clients[i].ObserverEventsReceived;
            observerEventsApplied += _clients[i].ObserverEventsApplied;
            chatMessagesSubmitted += _clients[i].ChatMessagesSubmitted;
            chatMessagesReceived += _clients[i].ChatMessagesReceived;
            chatMessagesApplied += _clients[i].ChatMessagesApplied;
            ClientMovementCorrectionSnapshot correction = _clients[i].Host.Movement.CorrectionStats.Snapshot();
            ownerCorrectionSamples += correction.SampleCount;
            ownerCorrectionPositionErrorSum += correction.AveragePositionError * correction.SampleCount;
            ownerCorrectionVelocityErrorSum += correction.AverageVelocityError * correction.SampleCount;
            if (correction.MaxPositionError > ownerCorrectionPositionErrorMax)
                ownerCorrectionPositionErrorMax = correction.MaxPositionError;
            if (correction.MaxVelocityError > ownerCorrectionVelocityErrorMax)
                ownerCorrectionVelocityErrorMax = correction.MaxVelocityError;
            perClientPing.Add(_clients[i].BuildPingJson());
        }

        return new StageResult(
            StageIndex: stageIndex,
            TargetClients: targetClients,
            ActiveClients: _clients.Count,
            ReadyClients: readyClients,
            TickAverageMs: stats.AverageMs,
            TickP95Ms: stats.P95Ms,
            TickP99Ms: stats.P99Ms,
            TickMaxMs: stats.MaxMs,
            ServerPumpAverageMs: serverPumpStats.AverageMs,
            ServerPumpP95Ms: serverPumpStats.P95Ms,
            ServerPollAverageMs: serverPollStats.AverageMs,
            ServerPollP95Ms: serverPollStats.P95Ms,
            ServerPollPendingDrainAverageMs: serverPollPendingDrainStats.AverageMs,
            ServerPollPendingDrainP95Ms: serverPollPendingDrainStats.P95Ms,
            ServerPollSocketReceiveAverageMs: serverPollSocketReceiveStats.AverageMs,
            ServerPollSocketReceiveP95Ms: serverPollSocketReceiveStats.P95Ms,
            ServerPollBundleUnpackAverageMs: serverPollBundleUnpackStats.AverageMs,
            ServerPollBundleUnpackP95Ms: serverPollBundleUnpackStats.P95Ms,
            ServerPollBundleIndexAverageMs: serverPollBundleIndexStats.AverageMs,
            ServerPollBundleIndexP95Ms: serverPollBundleIndexStats.P95Ms,
            ServerPollBundleMaterializeAverageMs: serverPollBundleMaterializeStats.AverageMs,
            ServerPollBundleMaterializeP95Ms: serverPollBundleMaterializeStats.P95Ms,
            ServerPollBundleSliceAverageMs: serverPollBundleSliceStats.AverageMs,
            ServerPollBundleSliceP95Ms: serverPollBundleSliceStats.P95Ms,
            ServerPollBundleDatagramCreateAverageMs: serverPollBundleDatagramCreateStats.AverageMs,
            ServerPollBundleDatagramCreateP95Ms: serverPollBundleDatagramCreateStats.P95Ms,
            ServerPollBundleEnqueueAverageMs: serverPollBundleEnqueueStats.AverageMs,
            ServerPollBundleEnqueueP95Ms: serverPollBundleEnqueueStats.P95Ms,
            ServerPollPayloadCopyAverageMs: serverPollPayloadCopyStats.AverageMs,
            ServerPollPayloadCopyP95Ms: serverPollPayloadCopyStats.P95Ms,
            ServerPollEnvelopeParseAverageMs: serverPollEnvelopeParseStats.AverageMs,
            ServerPollEnvelopeParseP95Ms: serverPollEnvelopeParseStats.P95Ms,
            ServerPollAsyncReceiveAverageMs: serverPollAsyncReceiveStats.AverageMs,
            ServerPollAsyncReceiveP95Ms: serverPollAsyncReceiveStats.P95Ms,
            ServerPollAsyncQueuedAverage: serverPollAsyncQueuedStats.AverageMs,
            ServerPollAsyncQueuedP95: serverPollAsyncQueuedStats.P95Ms,
            ServerControlAverageMs: serverControlStats.AverageMs,
            ServerControlP95Ms: serverControlStats.P95Ms,
            ServerControlEventsAverageMs: serverControlEventsStats.AverageMs,
            ServerControlEventsP95Ms: serverControlEventsStats.P95Ms,
            ServerRuntimeInboundAverageMs: serverRuntimeInboundStats.AverageMs,
            ServerRuntimeInboundP95Ms: serverRuntimeInboundStats.P95Ms,
            ServerMovementInboundAverageMs: serverMovementInboundStats.AverageMs,
            ServerMovementInboundP95Ms: serverMovementInboundStats.P95Ms,
            ServerSweepAverageMs: serverSweepStats.AverageMs,
            ServerSweepP95Ms: serverSweepStats.P95Ms,
            ServerLifecycleAverageMs: serverLifecycleStats.AverageMs,
            ServerLifecycleP95Ms: serverLifecycleStats.P95Ms,
            ServerMovementAdvanceAverageMs: serverMovementAdvanceStats.AverageMs,
            ServerMovementAdvanceP95Ms: serverMovementAdvanceStats.P95Ms,
            ServerRuntimeDispatchAverageMs: serverRuntimeDispatchStats.AverageMs,
            ServerRuntimeDispatchP95Ms: serverRuntimeDispatchStats.P95Ms,
            ServerControlFlushAverageMs: serverControlFlushStats.AverageMs,
            ServerControlFlushP95Ms: serverControlFlushStats.P95Ms,
            ServerControlFlushDrainAverageMs: serverControlFlushDrainStats.AverageMs,
            ServerControlFlushDrainP95Ms: serverControlFlushDrainStats.P95Ms,
            ServerControlFlushBundleAverageMs: serverControlFlushBundleStats.AverageMs,
            ServerControlFlushBundleP95Ms: serverControlFlushBundleStats.P95Ms,
            ServerControlFlushSendAverageMs: serverControlFlushSendStats.AverageMs,
            ServerControlFlushSendP95Ms: serverControlFlushSendStats.P95Ms,
            ServerControlFlushAsyncPreviousAverageMs: serverControlFlushAsyncPreviousStats.AverageMs,
            ServerControlFlushAsyncPreviousP95Ms: serverControlFlushAsyncPreviousStats.P95Ms,
            ServerControlFlushAsyncPreviousSendAverageMs: serverControlFlushAsyncPreviousSendStats.AverageMs,
            ServerControlFlushAsyncPreviousSendP95Ms: serverControlFlushAsyncPreviousSendStats.P95Ms,
            ServerControlFlushAsyncHandoffWaitAverageMs: serverControlFlushAsyncHandoffWaitStats.AverageMs,
            ServerControlFlushAsyncHandoffWaitP95Ms: serverControlFlushAsyncHandoffWaitStats.P95Ms,
            ServerControlFlushAsyncPendingAverage: serverControlFlushAsyncPendingStats.AverageMs,
            ServerControlFlushAsyncPendingP95: serverControlFlushAsyncPendingStats.P95Ms,
            ServerControlFlushWireDatagramsAverage: serverControlFlushWireDatagramStats.AverageMs,
            ServerControlFlushWireDatagramsP95: serverControlFlushWireDatagramStats.P95Ms,
            PublishAverageMs: publishStats.AverageMs,
            PublishP95Ms: publishStats.P95Ms,
            DispatchAverageMs: dispatchStats.AverageMs,
            DispatchP95Ms: dispatchStats.P95Ms,
            DispatchScheduleAverageMs: dispatchScheduleStats.AverageMs,
            DispatchScheduleP95Ms: dispatchScheduleStats.P95Ms,
            DispatchFanoutAverageMs: dispatchFanoutStats.AverageMs,
            DispatchFanoutP95Ms: dispatchFanoutStats.P95Ms,
            DispatchFlushAverageMs: dispatchFlushStats.AverageMs,
            DispatchFlushP95Ms: dispatchFlushStats.P95Ms,
            FlushDrainAverageMs: flushDrainStats.AverageMs,
            FlushDrainP95Ms: flushDrainStats.P95Ms,
            FlushBundleAverageMs: flushBundleStats.AverageMs,
            FlushBundleP95Ms: flushBundleStats.P95Ms,
            FlushSendAverageMs: flushSendStats.AverageMs,
            FlushSendP95Ms: flushSendStats.P95Ms,
            FlushConnectionsAverage: flushConnectionStats.AverageMs,
            FlushConnectionsP95: flushConnectionStats.P95Ms,
            FlushWireDatagramsAverage: flushWireDatagramStats.AverageMs,
            FlushWireDatagramsP95: flushWireDatagramStats.P95Ms,
            AsyncFlushPreviousAverageMs: asyncFlushPreviousStats.AverageMs,
            AsyncFlushPreviousP95Ms: asyncFlushPreviousStats.P95Ms,
            AsyncFlushPreviousSendAverageMs: asyncFlushPreviousSendStats.AverageMs,
            AsyncFlushPreviousSendP95Ms: asyncFlushPreviousSendStats.P95Ms,
            AsyncFlushHandoffWaitAverageMs: asyncFlushHandoffWaitStats.AverageMs,
            AsyncFlushHandoffWaitP95Ms: asyncFlushHandoffWaitStats.P95Ms,
            AsyncFlushPendingAverage: asyncFlushPendingStats.AverageMs,
            AsyncFlushPendingP95: asyncFlushPendingStats.P95Ms,
            AsyncFlushPreviousWireDatagramsAverage: asyncFlushPreviousWireDatagramStats.AverageMs,
            AsyncFlushPreviousWireDatagramsP95: asyncFlushPreviousWireDatagramStats.P95Ms,
            ClientPumpAverageMs: clientPumpStats.AverageMs,
            ClientPumpP95Ms: clientPumpStats.P95Ms,
            ClientPollAverageMs: clientPollStats.AverageMs,
            ClientPollP95Ms: clientPollStats.P95Ms,
            ClientPollPendingDrainAverageMs: clientPollPendingDrainStats.AverageMs,
            ClientPollPendingDrainP95Ms: clientPollPendingDrainStats.P95Ms,
            ClientPollSocketReceiveAverageMs: clientPollSocketReceiveStats.AverageMs,
            ClientPollSocketReceiveP95Ms: clientPollSocketReceiveStats.P95Ms,
            ClientPollBundleUnpackAverageMs: clientPollBundleUnpackStats.AverageMs,
            ClientPollBundleUnpackP95Ms: clientPollBundleUnpackStats.P95Ms,
            ClientPollBundleIndexAverageMs: clientPollBundleIndexStats.AverageMs,
            ClientPollBundleIndexP95Ms: clientPollBundleIndexStats.P95Ms,
            ClientPollBundleMaterializeAverageMs: clientPollBundleMaterializeStats.AverageMs,
            ClientPollBundleMaterializeP95Ms: clientPollBundleMaterializeStats.P95Ms,
            ClientPollBundleSliceAverageMs: clientPollBundleSliceStats.AverageMs,
            ClientPollBundleSliceP95Ms: clientPollBundleSliceStats.P95Ms,
            ClientPollBundleDatagramCreateAverageMs: clientPollBundleDatagramCreateStats.AverageMs,
            ClientPollBundleDatagramCreateP95Ms: clientPollBundleDatagramCreateStats.P95Ms,
            ClientPollBundleEnqueueAverageMs: clientPollBundleEnqueueStats.AverageMs,
            ClientPollBundleEnqueueP95Ms: clientPollBundleEnqueueStats.P95Ms,
            ClientPollPayloadCopyAverageMs: clientPollPayloadCopyStats.AverageMs,
            ClientPollPayloadCopyP95Ms: clientPollPayloadCopyStats.P95Ms,
            ClientPollEnvelopeParseAverageMs: clientPollEnvelopeParseStats.AverageMs,
            ClientPollEnvelopeParseP95Ms: clientPollEnvelopeParseStats.P95Ms,
            ClientPollAsyncReceiveAverageMs: clientPollAsyncReceiveStats.AverageMs,
            ClientPollAsyncReceiveP95Ms: clientPollAsyncReceiveStats.P95Ms,
            ClientPollAsyncQueuedAverage: clientPollAsyncQueuedStats.AverageMs,
            ClientPollAsyncQueuedP95: clientPollAsyncQueuedStats.P95Ms,
            ClientControlAverageMs: clientControlStats.AverageMs,
            ClientControlP95Ms: clientControlStats.P95Ms,
            ClientMovementAverageMs: clientMovementStats.AverageMs,
            ClientMovementP95Ms: clientMovementStats.P95Ms,
            ClientObserverStateAverageMs: clientObserverStateStats.AverageMs,
            ClientObserverStateP95Ms: clientObserverStateStats.P95Ms,
            ClientObserverStateDecodeAverageMs: clientObserverStateDecodeStats.AverageMs,
            ClientObserverStateDecodeP95Ms: clientObserverStateDecodeStats.P95Ms,
            ClientObserverStateApplyAverageMs: clientObserverStateApplyStats.AverageMs,
            ClientObserverStateApplyP95Ms: clientObserverStateApplyStats.P95Ms,
            ClientObserverEventAverageMs: clientObserverEventStats.AverageMs,
            ClientObserverEventP95Ms: clientObserverEventStats.P95Ms,
            PingAverageMs: pingStats.AverageMs,
            PingP95Ms: pingStats.P95Ms,
            PingMaxMs: pingStats.MaxMs,
            PendingAverage: tickSamplesMs.Count == 0 ? 0d : pendingSum / (double)tickSamplesMs.Count,
            PendingMax: pendingMax,
            AllocatedBytes: allocAfter - allocBefore,
            Gen0Collections: gen0After - gen0Before,
            Gen1Collections: gen1After - gen1Before,
            Gen2Collections: gen2After - gen2Before,
            ConnectAttempts: connectAttempts,
            ConnectSuccess: Math.Min(connectAttempts, readyClients),
            SloTickP95Ms: _options.SloTickP95Ms,
            SloPendingMax: _options.SloPendingMax,
            SloPingP95Ms: _options.SloPingP95Ms,
            PerClientPing: perClientPing,
            ObserverReplicationEnabled: _options.ObserverReplication,
            ReplicationPublishedItems: replicationPublishedItems,
            ReplicationDatagrams: replicationDatagrams,
            ReplicationWireDatagrams: replicationWireDatagrams,
            ReplicationBundledWireDatagrams: replicationBundledWireDatagrams,
            ReplicationSingleWireDatagrams: replicationSingleWireDatagrams,
            ObserverFramesReceived: observerFramesReceived,
            ObserverFramesApplied: observerFramesApplied,
            ObserverEventsReceived: observerEventsReceived,
            ObserverEventsApplied: observerEventsApplied,
            ChatMessagesSubmitted: chatMessagesSubmitted,
            ChatMessagesReceived: chatMessagesReceived,
            ChatMessagesApplied: chatMessagesApplied,
            OwnerCorrectionSent: ownerCorrectionSent,
            OwnerCorrectionSamples: ownerCorrectionSamples,
            OwnerCorrectionAveragePositionError: ownerCorrectionSamples == 0 ? 0d : ownerCorrectionPositionErrorSum / ownerCorrectionSamples,
            OwnerCorrectionMaxPositionError: ownerCorrectionPositionErrorMax,
            OwnerCorrectionAverageVelocityError: ownerCorrectionSamples == 0 ? 0d : ownerCorrectionVelocityErrorSum / ownerCorrectionSamples,
            OwnerCorrectionMaxVelocityError: ownerCorrectionVelocityErrorMax,
            ObserverStateItems: CountObserverStateItems());
    }

    public void Reset()
    {
        for (int i = 0; i < _clients.Count; i++)
            _clients[i].Dispose();
        _clients.Clear();

        for (int i = 0; i < _spawnedEntities.Count; i++)
        {
            if (_world is not null && _world.Entities.IsAlive(_spawnedEntities[i]))
                _world.Destroy(_spawnedEntities[i]);
        }

        _spawnedEntities.Clear();
        _pendingChatSubmissions.Clear();
        _nextNetworkEntityId = 1;
        _clockTicks = 0;
    }

    public void Dispose()
    {
        Reset();
        _serverHost?.Stop();
        _serverTransport?.Dispose();
    }

    private int EnsureClientCount(int targetClients)
    {
        if (targetClients < _clients.Count)
            Reset();

        IPEndPoint serverEndPoint = _clientOnly
            ? new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port)
            : (IPEndPoint)_serverTransport!.LocalEndPoint!;
        int before = _clients.Count;
        while (_clients.Count < targetClients)
        {
            int clientIndex = _clients.Count;
            var transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(IPAddress.Loopback, 0),
                _metadata,
                serverEndPoint,
                new ConnectionId(1),
                AllowDynamicConnections: false,
                EnableAsyncReceive: _options.AsyncClientReceive,
                EnablePollProfiling: _options.TransportPollProfiling));
            transport.Start();

            ClientHandle? handle = null;
            var observerState = ClientObserverStateBridge.CreateSpanFastPath(
                new ConnectionId(1),
                (itemKey, _, payload) => handle!.ApplyObserverState(itemKey, payload));
            var chatEvents = _options.ChatEnabled
                ? new ClientChatEventBridge(_ => handle!.ApplyChatMessage())
                : null;
            var observerEvents = ClientObserverEventBridge.CreateSpanFastPath(
                new ConnectionId(1),
                (_, payload) => handle!.ApplyObserverEvent(payload));
            observerEvents.AllowSequenceGaps = _options.ObserverReplication;
            if (chatEvents is not null)
                observerEvents.ChatEvents = chatEvents;
            var host = new ClientMovementSessionHost(
                transport,
                new ConnectionId(1),
                new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression),
                observerState: observerState,
                observerEvents: observerEvents,
                chatEvents: chatEvents);
            host.Start();
            float spacing = _options.SpawnSpacingMeters;
            int width = Math.Max(1, _options.SpawnGridWidth);
            float startX = spacing <= 0f ? 0f : (clientIndex % width) * spacing;
            float startY = spacing <= 0f ? 0f : (clientIndex / width) * spacing;
            handle = new ClientHandle(
                clientIndex,
                transport,
                host,
                new ClientMovementState(startX, startY, 0f, 0f),
                _options.OwnerPredictionJitterPercent);
            host.BeginHandshake(_clockTicks + 100 + clientIndex);
            _clients.Add(handle);
        }

        return Math.Max(0, _clients.Count - before);
    }

    private void PumpUntilReady(int settleTicks)
    {
        long nextTickDeadline = Stopwatch.GetTimestamp();
        for (int step = 0; step < settleTicks; step++)
        {
            _clockTicks += 2;
            _serverHost?.Pump(_clockTicks, _options.TickDeltaSeconds, (uint)(10_000 + step));
            for (int i = 0; i < _clients.Count; i++)
                _clients[i].Host.Pump(_clockTicks + 1);

            bool allReady = _clientOnly || _serverHost!.BoundConnectionCount == _clients.Count;
            if (allReady)
            {
                for (int i = 0; i < _clients.Count; i++)
                {
                    if (!_clients[i].Host.IsReady)
                    {
                        allReady = false;
                        break;
                    }
                }
            }

            if (allReady)
                return;

            WaitForRealtimeTick(ref nextTickDeadline);
        }
    }

    private void QueueInputsForTick(uint clientTick)
    {
        int activeLimit = Math.Max(0, Math.Min(_clients.Count, (int)Math.Round(_clients.Count * _options.ActiveRatio, MidpointRounding.AwayFromZero)));
        for (int i = 0; i < _clients.Count; i++)
        {
            ClientHandle client = _clients[i];
            if (!client.Host.IsReady)
                continue;
            if (i >= activeLimit)
                continue;

            short moveX = (short)(80 + ((int)clientTick + i) % 40);
            short moveY = (short)(20 + (((int)clientTick * 3) + i) % 30);
            var input = new ClientMovementInput(moveX, moveY, (byte)(clientTick & 3));
            ClientMovementState predictedState = client.PredictNextState(input, _options.TickDeltaSeconds);
            client.Host.TrySendInput(
                input,
                clientTick,
                predictedState,
                out _);
        }
    }

    private void QueueChatForTick(uint clientTick, bool syntheticReplication)
    {
        if (!_options.ChatEnabled)
            return;

        int activeLimit = Math.Max(0, Math.Min(_clients.Count, (int)Math.Round(_clients.Count * _options.ChatActiveRatio, MidpointRounding.AwayFromZero)));
        for (int i = 0; i < activeLimit; i++)
        {
            ClientHandle client = _clients[i];
            if (!client.Host.IsReady)
                continue;

            if (syntheticReplication)
            {
                if (client.TryBuildChatMessage(clientTick, _options, out string text))
                    _pendingChatSubmissions.Add(new PendingChatSubmission(client.ClientIndex, text));
            }
            else
            {
                client.TrySendChat(clientTick, _options);
            }
        }
    }

    private ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
    {
        int spawnIndex = _spawnedEntities.Count;
        float spacing = _options.SpawnSpacingMeters;
        int width = Math.Max(1, _options.SpawnGridWidth);
        float startX = spacing <= 0f ? 0f : (spawnIndex % width) * spacing;
        float startY = spacing <= 0f ? 0f : (spawnIndex / width) * spacing;

        Entity entity = movementWorld.CreateDefault(_movementSignature);
        movementWorld.SetComponent(entity, new BenchMovementBody(startX, startY, 0f, 0f));
        movementWorld.SetComponent(entity, new BenchMovementVisualBody(startX, startY, 0f, 0f));
        movementWorld.SetComponent(entity, new BenchMovementBulkBody(startX, startY, 0f, 0f, 0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
        movementWorld.SetComponent(entity, new BenchMovementEventBody(GetHarnessObserverSlot(entity), startX, startY, 0, 0, 0, 0));
        _spawnedEntities.Add(entity);
        return new ServerMovementSessionSpawn(entity, _nextNetworkEntityId++);
    }

    private StageReplicationContext CreateReplicationContext()
    {
        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, _options.AoiRadiiMeters);
        var publisher = new ServerEcsWorldNetworkPublisher(_world!);
        var subscriptions = new ConnectionSubscriptionRegistry();
        var scheduler = new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream);
        var fanout = new ScheduledWorkFanoutBridge(
            subscriptions,
            enableLatestStateBatching: _options.LatestStateBatching,
            enableLatestStateRecipientPhasing: _options.LatestStateRecipientPhasing,
            latestStateConnectionPhasingCadenceTicks: checked((uint)_options.LatestStateConnectionPhasingCadenceTicks));
        return new StageReplicationContext(
            new ServerEcsChunkAoiScopeClassifier(policy),
            new ChunkAoiSubscriptionMaintainer(policy),
            subscriptions,
            publisher,
            new ConnectionStreamFanoutBridge(scheduler, fanout),
            new ServerEcsChunkAoiRingCadencePolicy(ConvertRingCadence(_options.RingCadenceTicks)),
            new ConnectionId[Math.Max(64, _clients.Count)],
            new ScheduledWorkItem[Math.Max(4096, _clients.Count * 8)],
            new ConnectionId[Math.Max(4096, _clients.Count)],
            new OutboundDatagram[Math.Max(65536, _clients.Count * 128)]);
    }

    private void UpdateObserverSubscriptions(StageReplicationContext replication)
    {
        if (replication.BoundConnections.Length < _serverHost!.BoundConnectionCount)
            replication.BoundConnections = new ConnectionId[_serverHost.BoundConnectionCount];

        int count = _serverHost.CopyBoundConnections(replication.BoundConnections);
        for (int i = 0; i < count; i++)
        {
            ConnectionId connectionId = replication.BoundConnections[i];
            if (!_serverHost.TryGetBoundEntity(connectionId, out Entity entity))
                continue;
            if (!_world!.TryGetComponent(entity, out BenchMovementBody body))
                continue;

            replication.Maintainer.UpdateConnection(connectionId, new WorldPosition2(body.PositionX, body.PositionY), replication.Subscriptions);
            replication.Subscriptions.Subscribe(connectionId, ServerChatScopes.System);
        }
    }

    private int PublishObserverReplication(StageReplicationContext replication, uint publishTick)
    {
        int published = replication.Publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<BenchMovementBody>(
                replication.Classifier,
                static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                static (_, body) => HarnessPayloadCodec.Encode(body),
                static (entity, _) => ComposeHarnessMovementItemKey(entity),
                ringCadence: replication.RingCadence,
                recipientPhasing: _options.LatestStateRecipientPhasing),
            publishTick);

        if (_options.ObserverVisualState && !_options.ObserverVisualStateSuppressPublish)
        {
            published += replication.Publisher.PublishChunkAoiLatestState(
                new ServerEcsChunkAoiLatestStateRule<BenchMovementVisualBody>(
                    replication.Classifier,
                    static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                    static (_, body) => HarnessPayloadCodec.Encode(body),
                    static (entity, _) => ComposeHarnessVisualItemKey(entity),
                    ringCadence: replication.RingCadence,
                    recipientPhasing: _options.LatestStateRecipientPhasing),
                publishTick);
        }

        if (_options.ObserverBulkState && !_options.ObserverBulkStateSuppressPublish)
        {
            published += replication.Publisher.PublishChunkAoiLatestState(
                new ServerEcsChunkAoiLatestStateRule<BenchMovementBulkBody>(
                    replication.Classifier,
                    static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                    static (_, body) => HarnessPayloadCodec.Encode(body),
                    static (entity, _) => ComposeHarnessBulkItemKey(entity),
                    ringCadence: replication.RingCadence,
                    packetClass: PacketClass.Bulk,
                    recipientPhasing: _options.LatestStateRecipientPhasing),
                publishTick);
        }

        if (_options.ObserverEvents && !_options.ObserverEventsSuppressPublish)
        {
            published += replication.Publisher.PublishChunkAoiOrderedEvents(
                new ServerEcsChunkAoiOrderedEventRule<BenchMovementEventBody>(
                    replication.Classifier,
                    static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                    static (_, body) => HarnessPayloadCodec.Encode(body)));
        }

        if (_pendingChatSubmissions.Count > 0)
        {
            for (int i = 0; i < _pendingChatSubmissions.Count; i++)
            {
                PendingChatSubmission submission = _pendingChatSubmissions[i];
                replication.Publisher.OrderedEventStream.Publish(
                    ServerChatScopes.System,
                    PacketClass.ReliableEvent,
                    PacketCodec.Encode(new ChatMessageEventPacket(
                        MessageId: unchecked(((ulong)publishTick << 32) | (uint)i),
                        ChannelId: ServerChatChannelIds.System,
                        SenderEntityId: (ulong)(submission.ClientIndex + 1),
                        SenderName: $"bot {submission.ClientIndex}",
                        Text: submission.Text,
                        ServerTick: publishTick,
                        Flags: ChatMessageFlags.System)));
                published++;
            }

            _pendingChatSubmissions.Clear();
        }

        return published;
    }

    private int DispatchObserverReplication(
        StageReplicationContext replication,
        out int wireDatagrams,
        out double dispatchScheduleMs,
        out double dispatchFanoutMs,
        out double dispatchFlushMs,
        out double flushDrainMs,
        out double flushBundleMs,
        out double flushSendMs,
        out int flushConnectionCount,
        out int flushWireDatagramCount,
        out double asyncFlushPreviousMs,
        out double asyncFlushPreviousSendMs,
        out double asyncFlushHandoffWaitMs,
        out int asyncFlushPendingCount,
        out int asyncFlushPreviousWireDatagramCount,
        out int bundledWireDatagrams,
        out int singleWireDatagrams,
        bool flushOutbound = true)
    {
        wireDatagrams = 0;
        long dispatchScheduleTicks = 0;
        long dispatchFanoutTicks = 0;
        long dispatchFlushTicks = 0;
        while (true)
        {
            ConnectionScheduledFanoutResult result = replication.Bridge.ScheduleAndDispatch(
                replication.ScheduleState,
                ConnectionSendBudget.Unlimited,
                replication.ScheduledWork,
                replication.SubscriberScratch,
                replication.Output);
            dispatchScheduleTicks += result.ScheduleElapsedTicks;
            dispatchFanoutTicks += result.FanoutElapsedTicks;

            if (result.Fanout.DatagramCount == replication.Output.Length)
            {
                replication.Output = new OutboundDatagram[replication.Output.Length << 1];
                continue;
            }

            if (result.Schedule.Count == replication.ScheduledWork.Length)
            {
                replication.ScheduledWork = new ScheduledWorkItem[replication.ScheduledWork.Length << 1];
                continue;
            }

            replication.ScheduleState = result.Schedule.NextState;
            long flushStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < result.Fanout.DatagramCount; i++)
                _serverTransport!.EnqueueOutbound(replication.Output[i]);
            if (flushOutbound)
                _serverTransport!.FlushOutbound();
            long flushEnd = Stopwatch.GetTimestamp();
            dispatchFlushTicks += flushEnd - flushStart;
            TransportFlushProfileSnapshot flushProfile = _serverTransport!.LastFlushProfile;
            wireDatagrams = flushProfile.AsyncEnabled
                ? flushProfile.AsyncPreviousWireDatagramCount
                : _serverTransport.LastFlushWireDatagramCount;
            dispatchScheduleMs = dispatchScheduleTicks * 1000.0 / Stopwatch.Frequency;
            dispatchFanoutMs = dispatchFanoutTicks * 1000.0 / Stopwatch.Frequency;
            dispatchFlushMs = dispatchFlushTicks * 1000.0 / Stopwatch.Frequency;
            flushDrainMs = flushProfile.DrainElapsedTicks * 1000.0 / Stopwatch.Frequency;
            flushBundleMs = flushProfile.BundleElapsedTicks * 1000.0 / Stopwatch.Frequency;
            flushSendMs = flushProfile.SendElapsedTicks * 1000.0 / Stopwatch.Frequency;
            flushConnectionCount = flushProfile.ConnectionCount;
            flushWireDatagramCount = flushProfile.AsyncEnabled
                ? flushProfile.AsyncPreviousWireDatagramCount
                : flushProfile.WireDatagramCount;
            asyncFlushPreviousMs = flushProfile.AsyncPreviousFlushElapsedTicks * 1000.0 / Stopwatch.Frequency;
            asyncFlushPreviousSendMs = flushProfile.AsyncPreviousSendElapsedTicks * 1000.0 / Stopwatch.Frequency;
            asyncFlushHandoffWaitMs = flushProfile.AsyncHandoffWaitElapsedTicks * 1000.0 / Stopwatch.Frequency;
            asyncFlushPendingCount = flushProfile.AsyncPendingBatchCount;
            asyncFlushPreviousWireDatagramCount = flushProfile.AsyncPreviousWireDatagramCount;
            bundledWireDatagrams = flushProfile.AsyncEnabled
                ? flushProfile.AsyncPreviousBundledWireDatagramCount
                : flushProfile.BundledWireDatagramCount;
            singleWireDatagrams = flushProfile.AsyncEnabled
                ? flushProfile.AsyncPreviousSingleWireDatagramCount
                : flushProfile.SingleWireDatagramCount;
            return result.Fanout.DatagramCount;
        }
    }

    private void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
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

        if (_options.ObserverVisualState && !_options.ObserverVisualStateSuppressPublish)
        {
            float headingDegrees = MathF.Atan2(velocityY, velocityX) * (180f / MathF.PI);
            float speed = MathF.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
            movementWorld.SetComponent(movementEntity, new BenchMovementVisualBody(
                body.PositionX,
                body.PositionY,
                speed,
                headingDegrees));
        }

        if (_options.ObserverBulkState &&
            !_options.ObserverBulkStateSuppressPublish &&
            (_options.ObserverBulkEveryTicks <= 1 || (input.ClientTick % (uint)_options.ObserverBulkEveryTicks) == 0))
        {
            float speed = MathF.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
            float headingDegrees = MathF.Atan2(velocityY, velocityX) * (180f / MathF.PI);
            movementWorld.SetComponent(movementEntity, new BenchMovementBulkBody(
                body.PositionX,
                body.PositionY,
                speed,
                headingDegrees,
                input.ClientTick,
                body.PositionX * 0.1f,
                body.PositionY * 0.1f,
                speed * 0.25f,
                speed * 0.5f,
                headingDegrees * 0.01f,
                headingDegrees * 0.02f,
                velocityX * velocityY,
                (body.PositionX + body.PositionY) * 0.05f));
        }

        if (_options.ObserverEvents &&
            !_options.ObserverEventsSuppressPublish &&
            (_options.ObserverEventEveryTicks <= 1 || (input.ClientTick % (uint)_options.ObserverEventEveryTicks) == 0))
        {
            movementWorld.SetComponent(movementEntity, new BenchMovementEventBody(
                GetHarnessObserverSlot(movementEntity),
                body.PositionX,
                body.PositionY,
                input.MoveX,
                input.MoveY,
                input.ClientTick,
                input.Flags));
        }
    }

    private static bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
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

    private static ulong ComposeHarnessVisualItemKey(Entity entity)
    {
        const ulong familyTag = 0xB24F_0000_0000_0000UL;
        ulong packed = entity.Packed & 0x0000_FFFF_FFFF_FFFFUL;
        ulong key = familyTag | packed;
        return key == 0 ? 1UL : key;
    }

    private static ulong ComposeHarnessMovementItemKey(Entity entity)
    {
        const ulong familyTag = 0xA17E_0000_0000_0000UL;
        ulong packed = entity.Packed & 0x0000_FFFF_FFFF_FFFFUL;
        ulong key = familyTag | packed;
        return key == 0 ? 1UL : key;
    }

    private static ulong ComposeHarnessBulkItemKey(Entity entity)
    {
        const ulong familyTag = 0xC31D_0000_0000_0000UL;
        ulong packed = entity.Packed & 0x0000_FFFF_FFFF_FFFFUL;
        ulong key = familyTag | packed;
        return key == 0 ? 1UL : key;
    }

    private static uint GetHarnessObserverSlot(Entity entity)
    {
        return unchecked((uint)entity.Packed);
    }

    private static uint[] ConvertRingCadence(int[] cadenceTicks)
    {
        var converted = new uint[cadenceTicks.Length];
        for (int i = 0; i < cadenceTicks.Length; i++)
            converted[i] = checked((uint)cadenceTicks[i]);
        return converted;
    }

    private void WaitForRealtimeTick(ref long nextTickDeadline)
    {
        if (_realtimeTickIntervalStopwatchTicks <= 0)
            return;

        nextTickDeadline += _realtimeTickIntervalStopwatchTicks;
        while (true)
        {
            long remaining = nextTickDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return;

            if (remaining > Stopwatch.Frequency / 200)
            {
                int sleepMs = (int)Math.Max(1, ((remaining * 1000) / Stopwatch.Frequency) - 1);
                Thread.Sleep(sleepMs);
                continue;
            }

            Thread.SpinWait(64);
        }
    }

    private sealed class ClientHandle : IDisposable
    {
        private readonly Dictionary<ushort, long> _pingSentAt = new();
        private readonly List<double> _pingSamplesMs = [];
        private BenchMovementBodyWire[] _observerStates = new BenchMovementBodyWire[128];
        private bool[] _observerStateOccupied = new bool[128];
        private int _observerStateCount;
        private BenchMovementVisualBodyWire[] _observerVisualStates = new BenchMovementVisualBodyWire[128];
        private bool[] _observerVisualStateOccupied = new bool[128];
        private int _observerVisualStateCount;
        private BenchMovementBulkBodyWire[] _observerBulkStates = new BenchMovementBulkBodyWire[128];
        private bool[] _observerBulkStateOccupied = new bool[128];
        private int _observerBulkStateCount;
        private BenchMovementEventBodyWire[] _observerEvents = new BenchMovementEventBodyWire[128];
        private bool[] _observerEventOccupied = new bool[128];
        private int _observerEventCount;
        private ushort _nextPingSequence = 1;
        private ClientMovementState _predictedOwnerState;
        private uint _lastAuthoritativeServerTick;
        private readonly float _predictionJitterPercent;
        private uint _predictionStep;
        private uint _nextChatTick;
        private uint _chatSequence;

        public ClientHandle(
            int clientIndex,
            UdpSocketNetworkTransport transport,
            ClientMovementSessionHost host,
            ClientMovementState initialPredictedOwnerState,
            float predictionJitterPercent)
        {
            ClientIndex = clientIndex;
            Transport = transport;
            Host = host;
            _predictedOwnerState = initialPredictedOwnerState;
            _predictionJitterPercent = predictionJitterPercent;
        }

        public int ClientIndex { get; }
        public UdpSocketNetworkTransport Transport { get; }
        public ClientMovementSessionHost Host { get; }
        public long ObserverFramesReceived { get; private set; }
        public long ObserverFramesApplied { get; private set; }
        public long ObserverEventsReceived { get; private set; }
        public long ObserverEventsApplied { get; private set; }
        public long ChatMessagesSubmitted { get; private set; }
        public long ChatMessagesReceived { get; private set; }
        public long ChatMessagesApplied { get; private set; }
        public int ObserverStateCount => _observerStateCount + _observerVisualStateCount + _observerBulkStateCount;

        public ClientMovementState PredictNextState(in ClientMovementInput input, float deltaSeconds)
        {
            _predictionStep++;
            float jitterX = SampleSignedJitter(ClientIndex, _predictionStep, axisSalt: 0x51a3, _predictionJitterPercent);
            float jitterY = SampleSignedJitter(ClientIndex, _predictionStep, axisSalt: 0x9c47, _predictionJitterPercent);
            float velocityX = (input.MoveX / 100f) * (1f + jitterX);
            float velocityY = (input.MoveY / 100f) * (1f + jitterY);
            float positionBiasX = jitterX * 0.1f;
            float positionBiasY = jitterY * 0.1f;
            _predictedOwnerState = new ClientMovementState(
                _predictedOwnerState.PositionX + velocityX * deltaSeconds + positionBiasX,
                _predictedOwnerState.PositionY + velocityY * deltaSeconds + positionBiasY,
                velocityX,
                velocityY);
            return _predictedOwnerState;
        }

        public void TrySendPing(uint clientTick)
        {
            ushort sequence = _nextPingSequence++;
            if (Host.Control.SendPingProbe(Host.ConnectionId, sequence, clientTick))
                _pingSentAt[sequence] = Stopwatch.GetTimestamp();
        }

        public void CollectPongSamples(long nowTicks, Span<PongProbePacket> scratch, List<double> aggregateSamples)
        {
            while (true)
            {
                int count = Host.Control.DrainReceivedPongs(scratch);
                if (count <= 0)
                    break;

                for (int i = 0; i < count; i++)
                {
                    PongProbePacket pong = scratch[i];
                    if (!_pingSentAt.Remove(pong.Sequence, out long sentAt))
                        continue;

                    double rttMs = (Stopwatch.GetTimestamp() - sentAt) * 1000.0 / Stopwatch.Frequency;
                    _pingSamplesMs.Add(rttMs);
                    aggregateSamples.Add(rttMs);
                }

                if (count < scratch.Length)
                    break;
            }

            if (Host.ObserverState is not null)
            {
                ObserverFramesReceived += Host.ObserverState.LastReceivedFrameCount;
                ObserverFramesApplied += Host.ObserverState.LastAppliedFrameCount;
            }

            if (Host.ObserverEvents is not null)
            {
                ObserverEventsReceived += Host.ObserverEvents.LastReceivedEventCount;
                ObserverEventsApplied += Host.ObserverEvents.LastAppliedEventCount;
            }

            if (Host.ChatEvents is not null)
            {
                ChatMessagesReceived += Host.ChatEvents.LastReceivedMessageCount;
                ChatMessagesApplied += Host.ChatEvents.LastAppliedMessageCount;
            }

            ClientMovementStateReceiveResult ownerResult = Host.Movement.LastReceiveResult;
            if (ownerResult.ServerTick != 0 && ownerResult.ServerTick != _lastAuthoritativeServerTick)
            {
                _predictedOwnerState = ownerResult.AuthoritativeState;
                _lastAuthoritativeServerTick = ownerResult.ServerTick;
            }
        }

        public JsonObject BuildPingJson()
        {
            TickWindowStats stats = new TickWindowStats(_pingSamplesMs.Count == 0 ? [] : new List<double>(_pingSamplesMs));
            return new JsonObject
            {
                ["client_index"] = ClientIndex,
                ["sample_count"] = _pingSamplesMs.Count,
                ["ping_avg_ms"] = Math.Round(stats.AverageMs, 3),
                ["ping_p95_ms"] = Math.Round(stats.P95Ms, 3),
                ["ping_p99_ms"] = Math.Round(stats.P99Ms, 3),
                ["ping_max_ms"] = Math.Round(stats.MaxMs, 3),
            };
        }

        public void Dispose()
        {
            Host.Stop();
            Transport.Dispose();
        }

        public void ApplyObserverState(ulong itemKey, ReadOnlySpan<byte> payload)
        {
            ulong familyTag = itemKey & 0xFFFF_0000_0000_0000UL;
            int slot = checked((int)(itemKey & 0x0000_0000_FFFF_FFFFUL));
            EnsureObserverCapacity(slot);

            if (familyTag == 0xC31D_0000_0000_0000UL)
            {
                if (!_observerBulkStateOccupied[slot])
                {
                    _observerBulkStateOccupied[slot] = true;
                    _observerBulkStateCount++;
                }

                _observerBulkStates[slot] = HarnessPayloadCodec.ReadBulkWire(payload);
                return;
            }

            if (familyTag == 0xB24F_0000_0000_0000UL)
            {
                if (!_observerVisualStateOccupied[slot])
                {
                    _observerVisualStateOccupied[slot] = true;
                    _observerVisualStateCount++;
                }

                _observerVisualStates[slot] = HarnessPayloadCodec.ReadVisualWire(payload);
                return;
            }

            if (!_observerStateOccupied[slot])
            {
                _observerStateOccupied[slot] = true;
                _observerStateCount++;
            }

            _observerStates[slot] = HarnessPayloadCodec.ReadMovementWire(payload);
        }

        public void ApplyObserverEvent(ReadOnlySpan<byte> payload)
        {
            BenchMovementEventBodyWire observerEvent = HarnessPayloadCodec.ReadEventWire(payload);
            int slot = checked((int)observerEvent.ObserverSlot);
            EnsureObserverCapacity(slot);
            if (!_observerEventOccupied[slot])
            {
                _observerEventOccupied[slot] = true;
                _observerEventCount++;
            }

            _observerEvents[slot] = observerEvent;
        }

        public void ApplyChatMessage()
        {
        }

        public void TrySendChat(uint clientTick, HarnessArgs options)
        {
            if (TryBuildChatMessage(clientTick, options, out string text))
                Host.TrySendChat(text);
        }

        public bool TryBuildChatMessage(uint clientTick, HarnessArgs options, out string text)
        {
            if (_nextChatTick == 0)
                _nextChatTick = clientTick + SampleChatDelayTicks(options, unchecked((uint)ClientIndex));
            if (clientTick < _nextChatTick)
            {
                text = string.Empty;
                return false;
            }

            text = $"hello from bot {ClientIndex} #{_chatSequence}";
            ChatMessagesSubmitted++;
            _chatSequence++;
            _nextChatTick = clientTick + SampleChatDelayTicks(options, unchecked((uint)(ClientIndex * 397) ^ _chatSequence));
            return true;
        }

        private static uint SampleChatDelayTicks(HarnessArgs options, uint salt)
        {
            int minTicks = Math.Max(1, (int)Math.Round(options.ChatMinIntervalSeconds * options.TickHz, MidpointRounding.AwayFromZero));
            int maxTicks = Math.Max(minTicks, (int)Math.Round(options.ChatMaxIntervalSeconds * options.TickHz, MidpointRounding.AwayFromZero));
            uint range = checked((uint)(maxTicks - minTicks + 1));
            uint value = salt ^ 0x9E37_79B9u;
            value ^= value >> 16;
            value *= 0x7FEB_352Du;
            value ^= value >> 15;
            value *= 0x846C_A68Bu;
            value ^= value >> 16;
            return checked((uint)minTicks) + (value % range);
        }

        private static float SampleSignedJitter(int clientIndex, uint step, int axisSalt, float magnitude)
        {
            if (magnitude <= 0f)
                return 0f;

            uint value = unchecked((uint)(clientIndex * 0x45d9f3b) ^ (step * 0x27d4eb2d) ^ (uint)axisSalt);
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            float unit = (value & 0xFFFF) / 65535f;
            return ((unit * 2f) - 1f) * magnitude;
        }

        private void EnsureObserverCapacity(int slot)
        {
            if ((uint)slot < (uint)_observerStates.Length)
                return;

            int newLength = _observerStates.Length;
            while (newLength <= slot)
                newLength <<= 1;

            Array.Resize(ref _observerStates, newLength);
            Array.Resize(ref _observerStateOccupied, newLength);
            Array.Resize(ref _observerVisualStates, newLength);
            Array.Resize(ref _observerVisualStateOccupied, newLength);
            Array.Resize(ref _observerBulkStates, newLength);
            Array.Resize(ref _observerBulkStateOccupied, newLength);
            Array.Resize(ref _observerEvents, newLength);
            Array.Resize(ref _observerEventOccupied, newLength);
        }

    }

    private sealed class StageReplicationContext
    {
        public StageReplicationContext(
            ServerEcsChunkAoiScopeClassifier classifier,
            ChunkAoiSubscriptionMaintainer maintainer,
            ConnectionSubscriptionRegistry subscriptions,
            ServerEcsWorldNetworkPublisher publisher,
            ConnectionStreamFanoutBridge bridge,
            ServerEcsChunkAoiRingCadencePolicy ringCadence,
            ConnectionId[] boundConnections,
            ScheduledWorkItem[] scheduledWork,
            ConnectionId[] subscriberScratch,
            OutboundDatagram[] output)
        {
            Classifier = classifier;
            Maintainer = maintainer;
            Subscriptions = subscriptions;
            Publisher = publisher;
            Bridge = bridge;
            RingCadence = ringCadence;
            BoundConnections = boundConnections;
            ScheduledWork = scheduledWork;
            SubscriberScratch = subscriberScratch;
            Output = output;
        }

        public ServerEcsChunkAoiScopeClassifier Classifier { get; }
        public ChunkAoiSubscriptionMaintainer Maintainer { get; }
        public ConnectionSubscriptionRegistry Subscriptions { get; }
        public ServerEcsWorldNetworkPublisher Publisher { get; }
        public ConnectionStreamFanoutBridge Bridge { get; }
        public ServerEcsChunkAoiRingCadencePolicy RingCadence { get; }
        public ConnectionScheduleState ScheduleState { get; set; }
        public ConnectionId[] BoundConnections { get; set; }
        public ScheduledWorkItem[] ScheduledWork { get; set; }
        public ConnectionId[] SubscriberScratch { get; }
        public OutboundDatagram[] Output { get; set; }
    }

    private readonly record struct PendingChatSubmission(
        int ClientIndex,
        string Text);

    private int CountObserverStateItems()
    {
        int total = 0;
        for (int i = 0; i < _clients.Count; i++)
            total += _clients[i].ObserverStateCount;
        return total;
    }
}

internal sealed class HarnessArgs
{
    public string Role { get; set; } = "combined";
    public string Host { get; set; } = "127.0.0.1";
    public string Stages { get; set; } = "32,64,128,256";
    public string StageMode { get; set; } = "independent";
    public double StageDurationSeconds { get; set; } = 30.0;
    public double SettleSeconds { get; set; } = 3.0;
    public int TickHz { get; set; } = 62;
    public double ActiveRatio { get; set; } = 1.0;
    public int Port { get; set; } = 0;
    public string ReportDir { get; set; } = "_Benchmarks/results";
    public double SloTickP95Ms { get; set; } = 4.0;
    public double SloPingP95Ms { get; set; } = 50.0;
    public int SloPendingMax { get; set; } = 128;
    public double PingIntervalSeconds { get; set; } = 1.0;
    public string StopOn { get; set; } = "either";
    public bool ObserverReplication { get; set; }
    public bool ObserverVisualState { get; set; }
    public bool ObserverBulkState { get; set; }
    public bool ObserverEvents { get; set; }
    public bool ObserverVisualStateSuppressPublish { get; set; }
    public bool ObserverBulkStateSuppressPublish { get; set; }
    public bool ObserverEventsSuppressPublish { get; set; }
    public bool LatestStateBatching { get; set; }
    public bool LatestStateRecipientPhasing { get; set; }
    public int LatestStateConnectionPhasingCadenceTicks { get; set; } = 1;
    public bool AsyncServerFlush { get; set; } = true;
    public bool AsyncClientReceive { get; set; }
    public bool TransportPollProfiling { get; set; }
    public bool RealtimePacing { get; set; }
    public bool ChatEnabled { get; set; }
    public double ChatActiveRatio { get; set; } = 1.0;
    public double ChatMinIntervalSeconds { get; set; } = 2.0;
    public double ChatMaxIntervalSeconds { get; set; } = 5.0;
    public int ObserverBulkEveryTicks { get; set; } = 16;
    public int ObserverEventEveryTicks { get; set; } = 8;
    public float SpawnSpacingMeters { get; set; } = 4f;
    public int SpawnGridWidth { get; set; } = 32;
    public float[] AoiRadiiMeters { get; set; } = [7.5f];
    public int[] RingCadenceTicks { get; set; } = [1, 2, 3];
    public int OrderedEventCapacity { get; set; } = ServerNetworkSessionRuntimeOptions.Default.OrderedEventCapacity;
    public int ReplayBufferCapacityPerConnection { get; set; } = ServerNetworkSessionRuntimeOptions.Default.ReplayBufferCapacityPerConnection;
    public float OwnerCorrectionDeadband { get; set; } = 0f;
    public uint OwnerCorrectionMaxSilentTicks { get; set; } = 1;
    public uint OwnerCorrectionPhaseCadenceTicks { get; set; } = 1;
    public float OwnerPredictionJitterPercent { get; set; } = 0f;

    public int StageTicks => Math.Max(1, (int)Math.Round(StageDurationSeconds * TickHz, MidpointRounding.AwayFromZero));
    public int SettleTicks => Math.Max(1, (int)Math.Round(SettleSeconds * TickHz, MidpointRounding.AwayFromZero));
    public int PingIntervalTicks => Math.Max(1, (int)Math.Round(PingIntervalSeconds * TickHz, MidpointRounding.AwayFromZero));
    public float TickDeltaSeconds => 1f / TickHz;

    public static HarnessArgs Parse(string[] args)
    {
        var options = new HarnessArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            if (arg == "--role") options.Role = Next() ?? options.Role;
            else if (arg == "--host") options.Host = Next() ?? options.Host;
            else if (arg == "--stages") options.Stages = Next() ?? options.Stages;
            else if (arg == "--stage-mode") options.StageMode = Next() ?? options.StageMode;
            else if (arg == "--stage-duration") options.StageDurationSeconds = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--settle-seconds") options.SettleSeconds = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--tick-hz") options.TickHz = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--active-ratio") options.ActiveRatio = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--port") options.Port = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--report-dir") options.ReportDir = Next() ?? options.ReportDir;
            else if (arg == "--slo-tick-p95-ms") options.SloTickP95Ms = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--slo-ping-p95-ms") options.SloPingP95Ms = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--slo-pending-max") options.SloPendingMax = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--ping-interval-s") options.PingIntervalSeconds = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--stop-on") options.StopOn = Next() ?? options.StopOn;
            else if (arg == "--observer-replication") options.ObserverReplication = true;
            else if (arg == "--observer-visual-state") options.ObserverVisualState = true;
            else if (arg == "--observer-bulk-state") options.ObserverBulkState = true;
            else if (arg == "--observer-events") options.ObserverEvents = true;
            else if (arg == "--observer-visual-state-suppress-publish") options.ObserverVisualStateSuppressPublish = true;
            else if (arg == "--observer-bulk-state-suppress-publish") options.ObserverBulkStateSuppressPublish = true;
            else if (arg == "--observer-events-suppress-publish") options.ObserverEventsSuppressPublish = true;
            else if (arg == "--disable-latest-state-batching") options.LatestStateBatching = false;
            else if (arg == "--enable-latest-state-batching") options.LatestStateBatching = true;
            else if (arg == "--disable-latest-state-recipient-phasing") options.LatestStateRecipientPhasing = false;
            else if (arg == "--enable-latest-state-recipient-phasing") options.LatestStateRecipientPhasing = true;
            else if (arg == "--latest-state-connection-phasing-cadence-ticks") options.LatestStateConnectionPhasingCadenceTicks = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--disable-async-server-flush") options.AsyncServerFlush = false;
            else if (arg == "--enable-async-server-flush") options.AsyncServerFlush = true;
            else if (arg == "--disable-async-client-receive") options.AsyncClientReceive = false;
            else if (arg == "--enable-async-client-receive") options.AsyncClientReceive = true;
            else if (arg == "--disable-transport-poll-profiling") options.TransportPollProfiling = false;
            else if (arg == "--enable-transport-poll-profiling") options.TransportPollProfiling = true;
            else if (arg == "--disable-realtime-pacing") options.RealtimePacing = false;
            else if (arg == "--enable-realtime-pacing") options.RealtimePacing = true;
            else if (arg == "--chat-enabled") options.ChatEnabled = true;
            else if (arg == "--chat-active-ratio") options.ChatActiveRatio = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--chat-min-interval-s") options.ChatMinIntervalSeconds = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--chat-max-interval-s") options.ChatMaxIntervalSeconds = double.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--observer-bulk-every-ticks") options.ObserverBulkEveryTicks = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--observer-event-every-ticks") options.ObserverEventEveryTicks = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--spawn-spacing-m") options.SpawnSpacingMeters = float.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--spawn-grid-width") options.SpawnGridWidth = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--aoi-radii-m") options.AoiRadiiMeters = ParseFloatList(Next()!, nameof(AoiRadiiMeters));
            else if (arg == "--ring-cadence-ticks") options.RingCadenceTicks = ParseIntList(Next()!, nameof(RingCadenceTicks));
            else if (arg == "--ordered-event-capacity") options.OrderedEventCapacity = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--replay-buffer-capacity") options.ReplayBufferCapacityPerConnection = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-deadband") options.OwnerCorrectionDeadband = float.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-max-silent-ticks") options.OwnerCorrectionMaxSilentTicks = uint.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-phase-cadence-ticks") options.OwnerCorrectionPhaseCadenceTicks = uint.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-prediction-jitter-percent") options.OwnerPredictionJitterPercent = float.Parse(Next()!, CultureInfo.InvariantCulture);
        }

        options.TickHz = Math.Max(1, options.TickHz);
        options.ActiveRatio = Math.Clamp(options.ActiveRatio, 0d, 1d);
        options.LatestStateConnectionPhasingCadenceTicks = Math.Max(1, options.LatestStateConnectionPhasingCadenceTicks);
        options.ChatActiveRatio = Math.Clamp(options.ChatActiveRatio, 0d, 1d);
        options.ChatMinIntervalSeconds = Math.Max(0.001d, options.ChatMinIntervalSeconds);
        options.ChatMaxIntervalSeconds = Math.Max(options.ChatMinIntervalSeconds, options.ChatMaxIntervalSeconds);
        options.ObserverBulkEveryTicks = Math.Max(1, options.ObserverBulkEveryTicks);
        options.ObserverEventEveryTicks = Math.Max(1, options.ObserverEventEveryTicks);
        if (options.AoiRadiiMeters.Length == 0)
            options.AoiRadiiMeters = [7.5f];
        if (options.RingCadenceTicks.Length == 0)
            options.RingCadenceTicks = [1, 2, 3];
        if (options.OrderedEventCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.OrderedEventCapacity));
        if (options.ReplayBufferCapacityPerConnection <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ReplayBufferCapacityPerConnection));
        if (float.IsNaN(options.OwnerCorrectionDeadband) || float.IsInfinity(options.OwnerCorrectionDeadband) || options.OwnerCorrectionDeadband < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerCorrectionDeadband));
        if (options.OwnerCorrectionMaxSilentTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerCorrectionMaxSilentTicks));
        if (options.OwnerCorrectionPhaseCadenceTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerCorrectionPhaseCadenceTicks));
        if (float.IsNaN(options.OwnerPredictionJitterPercent) || float.IsInfinity(options.OwnerPredictionJitterPercent) || options.OwnerPredictionJitterPercent < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerPredictionJitterPercent));
        return options;
    }

    public static List<int> ParseStages(string raw)
    {
        var stages = new List<int>();
        foreach (string part in raw.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
                stages.Add(int.Parse(trimmed, CultureInfo.InvariantCulture));
        }

        if (stages.Count == 0)
            throw new ArgumentException("No stages parsed.", nameof(raw));

        return stages;
    }

    public JsonObject ToJson()
    {
        var aoiRadii = new JsonArray();
        for (int i = 0; i < AoiRadiiMeters.Length; i++)
            aoiRadii.Add(JsonValue.Create(AoiRadiiMeters[i]));
        var ringCadence = new JsonArray();
        for (int i = 0; i < RingCadenceTicks.Length; i++)
            ringCadence.Add(JsonValue.Create(RingCadenceTicks[i]));

        return new JsonObject
        {
            ["role"] = Role,
            ["host"] = Host,
            ["stages"] = Stages,
            ["stage_mode"] = StageMode,
            ["stage_duration_seconds"] = StageDurationSeconds,
            ["settle_seconds"] = SettleSeconds,
            ["tick_hz"] = TickHz,
            ["active_ratio"] = ActiveRatio,
            ["port"] = Port,
            ["slo_tick_p95_ms"] = SloTickP95Ms,
            ["slo_ping_p95_ms"] = SloPingP95Ms,
            ["slo_pending_max"] = SloPendingMax,
            ["ping_interval_seconds"] = PingIntervalSeconds,
            ["stop_on"] = StopOn,
            ["observer_replication"] = ObserverReplication,
            ["observer_visual_state"] = ObserverVisualState,
            ["observer_visual_state_suppress_publish"] = ObserverVisualStateSuppressPublish,
            ["observer_bulk_state"] = ObserverBulkState,
            ["observer_bulk_state_suppress_publish"] = ObserverBulkStateSuppressPublish,
            ["observer_bulk_every_ticks"] = ObserverBulkEveryTicks,
            ["observer_events"] = ObserverEvents,
            ["observer_events_suppress_publish"] = ObserverEventsSuppressPublish,
            ["observer_event_every_ticks"] = ObserverEventEveryTicks,
            ["latest_state_batching"] = LatestStateBatching,
            ["latest_state_recipient_phasing"] = LatestStateRecipientPhasing,
            ["latest_state_connection_phasing_cadence_ticks"] = LatestStateConnectionPhasingCadenceTicks,
            ["async_server_flush"] = AsyncServerFlush,
            ["async_client_receive"] = AsyncClientReceive,
            ["transport_poll_profiling"] = TransportPollProfiling,
            ["realtime_pacing"] = RealtimePacing,
            ["chat_enabled"] = ChatEnabled,
            ["chat_active_ratio"] = ChatActiveRatio,
            ["chat_min_interval_seconds"] = ChatMinIntervalSeconds,
            ["chat_max_interval_seconds"] = ChatMaxIntervalSeconds,
            ["spawn_spacing_m"] = SpawnSpacingMeters,
            ["spawn_grid_width"] = SpawnGridWidth,
            ["aoi_radii_m"] = aoiRadii,
            ["ring_cadence_ticks"] = ringCadence,
            ["ordered_event_capacity"] = OrderedEventCapacity,
            ["replay_buffer_capacity"] = ReplayBufferCapacityPerConnection,
            ["owner_deadband"] = OwnerCorrectionDeadband,
            ["owner_max_silent_ticks"] = OwnerCorrectionMaxSilentTicks,
            ["owner_phase_cadence_ticks"] = OwnerCorrectionPhaseCadenceTicks,
            ["owner_prediction_jitter_percent"] = OwnerPredictionJitterPercent,
        };
    }

    private static float[] ParseFloatList(string raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Value cannot be empty.", name);

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Value cannot be empty.", name);

        var values = new float[parts.Length];
        float previous = 0f;
        for (int i = 0; i < parts.Length; i++)
        {
            float value = float.Parse(parts[i], CultureInfo.InvariantCulture);
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "AOI radii must be positive finite numbers.");
            if (i > 0 && value <= previous)
                throw new ArgumentException("AOI radii must be strictly increasing.", name);
            values[i] = value;
            previous = value;
        }

        return values;
    }

    private static int[] ParseIntList(string raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Value cannot be empty.", name);

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Value cannot be empty.", name);

        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            int value = int.Parse(parts[i], CultureInfo.InvariantCulture);
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name, "Ring cadence ticks must be positive integers.");
            values[i] = value;
        }

        return values;
    }
}

internal readonly record struct StageResult(
    int StageIndex,
    int TargetClients,
    int ActiveClients,
    int ReadyClients,
    double TickAverageMs,
    double TickP95Ms,
    double TickP99Ms,
    double TickMaxMs,
    double ServerPumpAverageMs,
    double ServerPumpP95Ms,
    double ServerPollAverageMs,
    double ServerPollP95Ms,
    double ServerPollPendingDrainAverageMs,
    double ServerPollPendingDrainP95Ms,
    double ServerPollSocketReceiveAverageMs,
    double ServerPollSocketReceiveP95Ms,
    double ServerPollBundleUnpackAverageMs,
    double ServerPollBundleUnpackP95Ms,
    double ServerPollBundleIndexAverageMs,
    double ServerPollBundleIndexP95Ms,
    double ServerPollBundleMaterializeAverageMs,
    double ServerPollBundleMaterializeP95Ms,
    double ServerPollBundleSliceAverageMs,
    double ServerPollBundleSliceP95Ms,
    double ServerPollBundleDatagramCreateAverageMs,
    double ServerPollBundleDatagramCreateP95Ms,
    double ServerPollBundleEnqueueAverageMs,
    double ServerPollBundleEnqueueP95Ms,
    double ServerPollPayloadCopyAverageMs,
    double ServerPollPayloadCopyP95Ms,
    double ServerPollEnvelopeParseAverageMs,
    double ServerPollEnvelopeParseP95Ms,
    double ServerPollAsyncReceiveAverageMs,
    double ServerPollAsyncReceiveP95Ms,
    double ServerPollAsyncQueuedAverage,
    double ServerPollAsyncQueuedP95,
    double ServerControlAverageMs,
    double ServerControlP95Ms,
    double ServerControlEventsAverageMs,
    double ServerControlEventsP95Ms,
    double ServerRuntimeInboundAverageMs,
    double ServerRuntimeInboundP95Ms,
    double ServerMovementInboundAverageMs,
    double ServerMovementInboundP95Ms,
    double ServerSweepAverageMs,
    double ServerSweepP95Ms,
    double ServerLifecycleAverageMs,
    double ServerLifecycleP95Ms,
    double ServerMovementAdvanceAverageMs,
    double ServerMovementAdvanceP95Ms,
    double ServerRuntimeDispatchAverageMs,
    double ServerRuntimeDispatchP95Ms,
    double ServerControlFlushAverageMs,
    double ServerControlFlushP95Ms,
    double ServerControlFlushDrainAverageMs,
    double ServerControlFlushDrainP95Ms,
    double ServerControlFlushBundleAverageMs,
    double ServerControlFlushBundleP95Ms,
    double ServerControlFlushSendAverageMs,
    double ServerControlFlushSendP95Ms,
    double ServerControlFlushAsyncPreviousAverageMs,
    double ServerControlFlushAsyncPreviousP95Ms,
    double ServerControlFlushAsyncPreviousSendAverageMs,
    double ServerControlFlushAsyncPreviousSendP95Ms,
    double ServerControlFlushAsyncHandoffWaitAverageMs,
    double ServerControlFlushAsyncHandoffWaitP95Ms,
    double ServerControlFlushAsyncPendingAverage,
    double ServerControlFlushAsyncPendingP95,
    double ServerControlFlushWireDatagramsAverage,
    double ServerControlFlushWireDatagramsP95,
    double PublishAverageMs,
    double PublishP95Ms,
    double DispatchAverageMs,
    double DispatchP95Ms,
    double DispatchScheduleAverageMs,
    double DispatchScheduleP95Ms,
    double DispatchFanoutAverageMs,
    double DispatchFanoutP95Ms,
    double DispatchFlushAverageMs,
    double DispatchFlushP95Ms,
    double FlushDrainAverageMs,
    double FlushDrainP95Ms,
    double FlushBundleAverageMs,
    double FlushBundleP95Ms,
    double FlushSendAverageMs,
    double FlushSendP95Ms,
    double FlushConnectionsAverage,
    double FlushConnectionsP95,
    double FlushWireDatagramsAverage,
    double FlushWireDatagramsP95,
    double AsyncFlushPreviousAverageMs,
    double AsyncFlushPreviousP95Ms,
    double AsyncFlushPreviousSendAverageMs,
    double AsyncFlushPreviousSendP95Ms,
    double AsyncFlushHandoffWaitAverageMs,
    double AsyncFlushHandoffWaitP95Ms,
    double AsyncFlushPendingAverage,
    double AsyncFlushPendingP95,
    double AsyncFlushPreviousWireDatagramsAverage,
    double AsyncFlushPreviousWireDatagramsP95,
    double ClientPumpAverageMs,
    double ClientPumpP95Ms,
    double ClientPollAverageMs,
    double ClientPollP95Ms,
    double ClientPollPendingDrainAverageMs,
    double ClientPollPendingDrainP95Ms,
    double ClientPollSocketReceiveAverageMs,
    double ClientPollSocketReceiveP95Ms,
    double ClientPollBundleUnpackAverageMs,
    double ClientPollBundleUnpackP95Ms,
    double ClientPollBundleIndexAverageMs,
    double ClientPollBundleIndexP95Ms,
    double ClientPollBundleMaterializeAverageMs,
    double ClientPollBundleMaterializeP95Ms,
    double ClientPollBundleSliceAverageMs,
    double ClientPollBundleSliceP95Ms,
    double ClientPollBundleDatagramCreateAverageMs,
    double ClientPollBundleDatagramCreateP95Ms,
    double ClientPollBundleEnqueueAverageMs,
    double ClientPollBundleEnqueueP95Ms,
    double ClientPollPayloadCopyAverageMs,
    double ClientPollPayloadCopyP95Ms,
    double ClientPollEnvelopeParseAverageMs,
    double ClientPollEnvelopeParseP95Ms,
    double ClientPollAsyncReceiveAverageMs,
    double ClientPollAsyncReceiveP95Ms,
    double ClientPollAsyncQueuedAverage,
    double ClientPollAsyncQueuedP95,
    double ClientControlAverageMs,
    double ClientControlP95Ms,
    double ClientMovementAverageMs,
    double ClientMovementP95Ms,
    double ClientObserverStateAverageMs,
    double ClientObserverStateP95Ms,
    double ClientObserverStateDecodeAverageMs,
    double ClientObserverStateDecodeP95Ms,
    double ClientObserverStateApplyAverageMs,
    double ClientObserverStateApplyP95Ms,
    double ClientObserverEventAverageMs,
    double ClientObserverEventP95Ms,
    double PingAverageMs,
    double PingP95Ms,
    double PingMaxMs,
    double PendingAverage,
    int PendingMax,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ConnectAttempts,
    int ConnectSuccess,
    double SloTickP95Ms,
    int SloPendingMax,
    double SloPingP95Ms,
    JsonArray PerClientPing,
    bool ObserverReplicationEnabled,
    long ReplicationPublishedItems,
    long ReplicationDatagrams,
    long ReplicationWireDatagrams,
    long ReplicationBundledWireDatagrams,
    long ReplicationSingleWireDatagrams,
    long ObserverFramesReceived,
    long ObserverFramesApplied,
    long ObserverEventsReceived,
    long ObserverEventsApplied,
    long ChatMessagesSubmitted,
    long ChatMessagesReceived,
    long ChatMessagesApplied,
    long OwnerCorrectionSent,
    long OwnerCorrectionSamples,
    double OwnerCorrectionAveragePositionError,
    double OwnerCorrectionMaxPositionError,
    double OwnerCorrectionAverageVelocityError,
    double OwnerCorrectionMaxVelocityError,
    int ObserverStateItems)
{
    public double ConnectSuccessRate => ConnectAttempts <= 0 ? 1d : ConnectSuccess / (double)ConnectAttempts;
    public bool TickSloPass => TickP95Ms <= SloTickP95Ms && PendingMax <= SloPendingMax;
    public bool PingSloPass => PingP95Ms <= SloPingP95Ms;

    public JsonObject ToJson()
    {
        return new JsonObject
        {
            ["stage_index"] = StageIndex,
            ["target_clients"] = TargetClients,
            ["active_clients"] = ActiveClients,
            ["ready_clients"] = ReadyClients,
            ["tick_avg_ms"] = Math.Round(TickAverageMs, 3),
            ["tick_p95_ms"] = Math.Round(TickP95Ms, 3),
            ["tick_p99_ms"] = Math.Round(TickP99Ms, 3),
            ["tick_max_ms"] = Math.Round(TickMaxMs, 3),
            ["server_pump_avg_ms"] = Math.Round(ServerPumpAverageMs, 3),
            ["server_pump_p95_ms"] = Math.Round(ServerPumpP95Ms, 3),
            ["server_poll_avg_ms"] = Math.Round(ServerPollAverageMs, 3),
            ["server_poll_p95_ms"] = Math.Round(ServerPollP95Ms, 3),
            ["server_poll_pending_drain_avg_ms"] = Math.Round(ServerPollPendingDrainAverageMs, 3),
            ["server_poll_pending_drain_p95_ms"] = Math.Round(ServerPollPendingDrainP95Ms, 3),
            ["server_poll_socket_recv_avg_ms"] = Math.Round(ServerPollSocketReceiveAverageMs, 3),
            ["server_poll_socket_recv_p95_ms"] = Math.Round(ServerPollSocketReceiveP95Ms, 3),
            ["server_poll_bundle_unpack_avg_ms"] = Math.Round(ServerPollBundleUnpackAverageMs, 3),
            ["server_poll_bundle_unpack_p95_ms"] = Math.Round(ServerPollBundleUnpackP95Ms, 3),
            ["server_poll_bundle_index_avg_ms"] = Math.Round(ServerPollBundleIndexAverageMs, 3),
            ["server_poll_bundle_index_p95_ms"] = Math.Round(ServerPollBundleIndexP95Ms, 3),
            ["server_poll_bundle_materialize_avg_ms"] = Math.Round(ServerPollBundleMaterializeAverageMs, 3),
            ["server_poll_bundle_materialize_p95_ms"] = Math.Round(ServerPollBundleMaterializeP95Ms, 3),
            ["server_poll_bundle_slice_avg_ms"] = Math.Round(ServerPollBundleSliceAverageMs, 3),
            ["server_poll_bundle_slice_p95_ms"] = Math.Round(ServerPollBundleSliceP95Ms, 3),
            ["server_poll_bundle_datagram_create_avg_ms"] = Math.Round(ServerPollBundleDatagramCreateAverageMs, 3),
            ["server_poll_bundle_datagram_create_p95_ms"] = Math.Round(ServerPollBundleDatagramCreateP95Ms, 3),
            ["server_poll_bundle_enqueue_avg_ms"] = Math.Round(ServerPollBundleEnqueueAverageMs, 3),
            ["server_poll_bundle_enqueue_p95_ms"] = Math.Round(ServerPollBundleEnqueueP95Ms, 3),
            ["server_poll_payload_copy_avg_ms"] = Math.Round(ServerPollPayloadCopyAverageMs, 3),
            ["server_poll_payload_copy_p95_ms"] = Math.Round(ServerPollPayloadCopyP95Ms, 3),
            ["server_poll_envelope_parse_avg_ms"] = Math.Round(ServerPollEnvelopeParseAverageMs, 3),
            ["server_poll_envelope_parse_p95_ms"] = Math.Round(ServerPollEnvelopeParseP95Ms, 3),
            ["server_poll_async_receive_avg_ms"] = Math.Round(ServerPollAsyncReceiveAverageMs, 3),
            ["server_poll_async_receive_p95_ms"] = Math.Round(ServerPollAsyncReceiveP95Ms, 3),
            ["server_poll_async_queued_avg"] = Math.Round(ServerPollAsyncQueuedAverage, 3),
            ["server_poll_async_queued_p95"] = Math.Round(ServerPollAsyncQueuedP95, 3),
            ["server_control_avg_ms"] = Math.Round(ServerControlAverageMs, 3),
            ["server_control_p95_ms"] = Math.Round(ServerControlP95Ms, 3),
            ["server_control_events_avg_ms"] = Math.Round(ServerControlEventsAverageMs, 3),
            ["server_control_events_p95_ms"] = Math.Round(ServerControlEventsP95Ms, 3),
            ["server_runtime_inbound_avg_ms"] = Math.Round(ServerRuntimeInboundAverageMs, 3),
            ["server_runtime_inbound_p95_ms"] = Math.Round(ServerRuntimeInboundP95Ms, 3),
            ["server_movement_inbound_avg_ms"] = Math.Round(ServerMovementInboundAverageMs, 3),
            ["server_movement_inbound_p95_ms"] = Math.Round(ServerMovementInboundP95Ms, 3),
            ["server_sweep_avg_ms"] = Math.Round(ServerSweepAverageMs, 3),
            ["server_sweep_p95_ms"] = Math.Round(ServerSweepP95Ms, 3),
            ["server_lifecycle_avg_ms"] = Math.Round(ServerLifecycleAverageMs, 3),
            ["server_lifecycle_p95_ms"] = Math.Round(ServerLifecycleP95Ms, 3),
            ["server_movement_advance_avg_ms"] = Math.Round(ServerMovementAdvanceAverageMs, 3),
            ["server_movement_advance_p95_ms"] = Math.Round(ServerMovementAdvanceP95Ms, 3),
            ["server_runtime_dispatch_avg_ms"] = Math.Round(ServerRuntimeDispatchAverageMs, 3),
            ["server_runtime_dispatch_p95_ms"] = Math.Round(ServerRuntimeDispatchP95Ms, 3),
            ["server_control_flush_avg_ms"] = Math.Round(ServerControlFlushAverageMs, 3),
            ["server_control_flush_p95_ms"] = Math.Round(ServerControlFlushP95Ms, 3),
            ["server_control_flush_drain_avg_ms"] = Math.Round(ServerControlFlushDrainAverageMs, 3),
            ["server_control_flush_drain_p95_ms"] = Math.Round(ServerControlFlushDrainP95Ms, 3),
            ["server_control_flush_bundle_avg_ms"] = Math.Round(ServerControlFlushBundleAverageMs, 3),
            ["server_control_flush_bundle_p95_ms"] = Math.Round(ServerControlFlushBundleP95Ms, 3),
            ["server_control_flush_send_avg_ms"] = Math.Round(ServerControlFlushSendAverageMs, 3),
            ["server_control_flush_send_p95_ms"] = Math.Round(ServerControlFlushSendP95Ms, 3),
            ["server_control_flush_async_previous_avg_ms"] = Math.Round(ServerControlFlushAsyncPreviousAverageMs, 3),
            ["server_control_flush_async_previous_p95_ms"] = Math.Round(ServerControlFlushAsyncPreviousP95Ms, 3),
            ["server_control_flush_async_previous_send_avg_ms"] = Math.Round(ServerControlFlushAsyncPreviousSendAverageMs, 3),
            ["server_control_flush_async_previous_send_p95_ms"] = Math.Round(ServerControlFlushAsyncPreviousSendP95Ms, 3),
            ["server_control_flush_async_handoff_wait_avg_ms"] = Math.Round(ServerControlFlushAsyncHandoffWaitAverageMs, 3),
            ["server_control_flush_async_handoff_wait_p95_ms"] = Math.Round(ServerControlFlushAsyncHandoffWaitP95Ms, 3),
            ["server_control_flush_async_pending_avg"] = Math.Round(ServerControlFlushAsyncPendingAverage, 3),
            ["server_control_flush_async_pending_p95"] = Math.Round(ServerControlFlushAsyncPendingP95, 3),
            ["server_control_flush_wire_datagrams_avg"] = Math.Round(ServerControlFlushWireDatagramsAverage, 3),
            ["server_control_flush_wire_datagrams_p95"] = Math.Round(ServerControlFlushWireDatagramsP95, 3),
            ["publish_avg_ms"] = Math.Round(PublishAverageMs, 3),
            ["publish_p95_ms"] = Math.Round(PublishP95Ms, 3),
            ["dispatch_avg_ms"] = Math.Round(DispatchAverageMs, 3),
            ["dispatch_p95_ms"] = Math.Round(DispatchP95Ms, 3),
            ["dispatch_schedule_avg_ms"] = Math.Round(DispatchScheduleAverageMs, 3),
            ["dispatch_schedule_p95_ms"] = Math.Round(DispatchScheduleP95Ms, 3),
            ["dispatch_fanout_avg_ms"] = Math.Round(DispatchFanoutAverageMs, 3),
            ["dispatch_fanout_p95_ms"] = Math.Round(DispatchFanoutP95Ms, 3),
            ["dispatch_flush_avg_ms"] = Math.Round(DispatchFlushAverageMs, 3),
            ["dispatch_flush_p95_ms"] = Math.Round(DispatchFlushP95Ms, 3),
            ["flush_drain_avg_ms"] = Math.Round(FlushDrainAverageMs, 3),
            ["flush_drain_p95_ms"] = Math.Round(FlushDrainP95Ms, 3),
            ["flush_bundle_avg_ms"] = Math.Round(FlushBundleAverageMs, 3),
            ["flush_bundle_p95_ms"] = Math.Round(FlushBundleP95Ms, 3),
            ["flush_send_avg_ms"] = Math.Round(FlushSendAverageMs, 3),
            ["flush_send_p95_ms"] = Math.Round(FlushSendP95Ms, 3),
            ["flush_connections_avg"] = Math.Round(FlushConnectionsAverage, 3),
            ["flush_connections_p95"] = Math.Round(FlushConnectionsP95, 3),
            ["flush_wire_datagrams_avg"] = Math.Round(FlushWireDatagramsAverage, 3),
            ["flush_wire_datagrams_p95"] = Math.Round(FlushWireDatagramsP95, 3),
            ["async_flush_previous_avg_ms"] = Math.Round(AsyncFlushPreviousAverageMs, 3),
            ["async_flush_previous_p95_ms"] = Math.Round(AsyncFlushPreviousP95Ms, 3),
            ["async_flush_previous_send_avg_ms"] = Math.Round(AsyncFlushPreviousSendAverageMs, 3),
            ["async_flush_previous_send_p95_ms"] = Math.Round(AsyncFlushPreviousSendP95Ms, 3),
            ["async_flush_handoff_wait_avg_ms"] = Math.Round(AsyncFlushHandoffWaitAverageMs, 3),
            ["async_flush_handoff_wait_p95_ms"] = Math.Round(AsyncFlushHandoffWaitP95Ms, 3),
            ["async_flush_pending_avg"] = Math.Round(AsyncFlushPendingAverage, 3),
            ["async_flush_pending_p95"] = Math.Round(AsyncFlushPendingP95, 3),
            ["async_flush_previous_wire_datagrams_avg"] = Math.Round(AsyncFlushPreviousWireDatagramsAverage, 3),
            ["async_flush_previous_wire_datagrams_p95"] = Math.Round(AsyncFlushPreviousWireDatagramsP95, 3),
            ["client_pump_avg_ms"] = Math.Round(ClientPumpAverageMs, 3),
            ["client_pump_p95_ms"] = Math.Round(ClientPumpP95Ms, 3),
            ["client_poll_avg_ms"] = Math.Round(ClientPollAverageMs, 3),
            ["client_poll_p95_ms"] = Math.Round(ClientPollP95Ms, 3),
            ["client_poll_pending_drain_avg_ms"] = Math.Round(ClientPollPendingDrainAverageMs, 3),
            ["client_poll_pending_drain_p95_ms"] = Math.Round(ClientPollPendingDrainP95Ms, 3),
            ["client_poll_socket_recv_avg_ms"] = Math.Round(ClientPollSocketReceiveAverageMs, 3),
            ["client_poll_socket_recv_p95_ms"] = Math.Round(ClientPollSocketReceiveP95Ms, 3),
            ["client_poll_bundle_unpack_avg_ms"] = Math.Round(ClientPollBundleUnpackAverageMs, 3),
            ["client_poll_bundle_unpack_p95_ms"] = Math.Round(ClientPollBundleUnpackP95Ms, 3),
            ["client_poll_bundle_index_avg_ms"] = Math.Round(ClientPollBundleIndexAverageMs, 3),
            ["client_poll_bundle_index_p95_ms"] = Math.Round(ClientPollBundleIndexP95Ms, 3),
            ["client_poll_bundle_materialize_avg_ms"] = Math.Round(ClientPollBundleMaterializeAverageMs, 3),
            ["client_poll_bundle_materialize_p95_ms"] = Math.Round(ClientPollBundleMaterializeP95Ms, 3),
            ["client_poll_bundle_slice_avg_ms"] = Math.Round(ClientPollBundleSliceAverageMs, 3),
            ["client_poll_bundle_slice_p95_ms"] = Math.Round(ClientPollBundleSliceP95Ms, 3),
            ["client_poll_bundle_datagram_create_avg_ms"] = Math.Round(ClientPollBundleDatagramCreateAverageMs, 3),
            ["client_poll_bundle_datagram_create_p95_ms"] = Math.Round(ClientPollBundleDatagramCreateP95Ms, 3),
            ["client_poll_bundle_enqueue_avg_ms"] = Math.Round(ClientPollBundleEnqueueAverageMs, 3),
            ["client_poll_bundle_enqueue_p95_ms"] = Math.Round(ClientPollBundleEnqueueP95Ms, 3),
            ["client_poll_payload_copy_avg_ms"] = Math.Round(ClientPollPayloadCopyAverageMs, 3),
            ["client_poll_payload_copy_p95_ms"] = Math.Round(ClientPollPayloadCopyP95Ms, 3),
            ["client_poll_envelope_parse_avg_ms"] = Math.Round(ClientPollEnvelopeParseAverageMs, 3),
            ["client_poll_envelope_parse_p95_ms"] = Math.Round(ClientPollEnvelopeParseP95Ms, 3),
            ["client_poll_async_receive_avg_ms"] = Math.Round(ClientPollAsyncReceiveAverageMs, 3),
            ["client_poll_async_receive_p95_ms"] = Math.Round(ClientPollAsyncReceiveP95Ms, 3),
            ["client_poll_async_queued_avg"] = Math.Round(ClientPollAsyncQueuedAverage, 3),
            ["client_poll_async_queued_p95"] = Math.Round(ClientPollAsyncQueuedP95, 3),
            ["client_control_avg_ms"] = Math.Round(ClientControlAverageMs, 3),
            ["client_control_p95_ms"] = Math.Round(ClientControlP95Ms, 3),
            ["client_movement_avg_ms"] = Math.Round(ClientMovementAverageMs, 3),
            ["client_movement_p95_ms"] = Math.Round(ClientMovementP95Ms, 3),
            ["client_observer_state_avg_ms"] = Math.Round(ClientObserverStateAverageMs, 3),
            ["client_observer_state_p95_ms"] = Math.Round(ClientObserverStateP95Ms, 3),
            ["client_observer_state_decode_avg_ms"] = Math.Round(ClientObserverStateDecodeAverageMs, 3),
            ["client_observer_state_decode_p95_ms"] = Math.Round(ClientObserverStateDecodeP95Ms, 3),
            ["client_observer_state_apply_avg_ms"] = Math.Round(ClientObserverStateApplyAverageMs, 3),
            ["client_observer_state_apply_p95_ms"] = Math.Round(ClientObserverStateApplyP95Ms, 3),
            ["client_observer_event_avg_ms"] = Math.Round(ClientObserverEventAverageMs, 3),
            ["client_observer_event_p95_ms"] = Math.Round(ClientObserverEventP95Ms, 3),
            ["ping_avg_ms"] = Math.Round(PingAverageMs, 3),
            ["ping_p95_ms"] = Math.Round(PingP95Ms, 3),
            ["ping_max_ms"] = Math.Round(PingMaxMs, 3),
            ["pending_avg"] = Math.Round(PendingAverage, 3),
            ["pending_max"] = PendingMax,
            ["alloc_bytes"] = AllocatedBytes,
            ["gc_gen0_collections"] = Gen0Collections,
            ["gc_gen1_collections"] = Gen1Collections,
            ["gc_gen2_collections"] = Gen2Collections,
            ["connect_attempts"] = ConnectAttempts,
            ["connect_success"] = ConnectSuccess,
            ["connect_success_rate"] = Math.Round(ConnectSuccessRate, 4),
            ["tick_slo_pass"] = TickSloPass,
            ["ping_slo_pass"] = PingSloPass,
            ["per_client_ping"] = PerClientPing,
            ["observer_replication"] = ObserverReplicationEnabled,
            ["replication_published_items"] = ReplicationPublishedItems,
            ["replication_datagrams"] = ReplicationDatagrams,
            ["replication_wire_datagrams"] = ReplicationWireDatagrams,
            ["replication_bundled_wire_datagrams"] = ReplicationBundledWireDatagrams,
            ["replication_single_wire_datagrams"] = ReplicationSingleWireDatagrams,
            ["observer_frames_received"] = ObserverFramesReceived,
            ["observer_frames_applied"] = ObserverFramesApplied,
            ["observer_events_received"] = ObserverEventsReceived,
            ["observer_events_applied"] = ObserverEventsApplied,
            ["chat_messages_submitted"] = ChatMessagesSubmitted,
            ["chat_messages_received"] = ChatMessagesReceived,
            ["chat_messages_applied"] = ChatMessagesApplied,
            ["owner_correction_sent"] = OwnerCorrectionSent,
            ["owner_correction_samples"] = OwnerCorrectionSamples,
            ["owner_correction_avg_position_error"] = Math.Round(OwnerCorrectionAveragePositionError, 6),
            ["owner_correction_max_position_error"] = Math.Round(OwnerCorrectionMaxPositionError, 6),
            ["owner_correction_avg_velocity_error"] = Math.Round(OwnerCorrectionAverageVelocityError, 6),
            ["owner_correction_max_velocity_error"] = Math.Round(OwnerCorrectionMaxVelocityError, 6),
            ["observer_state_items"] = ObserverStateItems,
        };
    }
}

internal readonly struct TickWindowStats
{
    public TickWindowStats(List<double> samples)
    {
        if (samples.Count == 0)
        {
            AverageMs = 0d;
            P95Ms = 0d;
            P99Ms = 0d;
            MaxMs = 0d;
            return;
        }

        samples.Sort();
        double sum = 0d;
        for (int i = 0; i < samples.Count; i++)
            sum += samples[i];

        AverageMs = sum / samples.Count;
        P95Ms = Percentile(samples, 0.95);
        P99Ms = Percentile(samples, 0.99);
        MaxMs = samples[^1];
    }

    public double AverageMs { get; }
    public double P95Ms { get; }
    public double P99Ms { get; }
    public double MaxMs { get; }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0d;
        if (sorted.Count == 1)
            return sorted[0];

        double rank = (sorted.Count - 1) * percentile;
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return sorted[lower];

        double weight = rank - lower;
        return (sorted[lower] * (1.0 - weight)) + (sorted[upper] * weight);
    }
}

internal static class HarnessPayloadCodec
{
    public static ImmutablePayloadBuffer Encode(BenchMovementBody value)
    {
        byte[] bytes = new byte[16];
        WriteSingle(bytes, 0, value.PositionX);
        WriteSingle(bytes, 4, value.PositionY);
        WriteSingle(bytes, 8, value.VelocityX);
        WriteSingle(bytes, 12, value.VelocityY);
        return ImmutablePayloadBuffer.FromOwned(bytes);
    }

    public static ImmutablePayloadBuffer Encode(BenchMovementVisualBody value)
    {
        byte[] bytes = new byte[16];
        WriteSingle(bytes, 0, value.PositionX);
        WriteSingle(bytes, 4, value.PositionY);
        WriteSingle(bytes, 8, value.Speed);
        WriteSingle(bytes, 12, value.HeadingDegrees);
        return ImmutablePayloadBuffer.FromOwned(bytes);
    }

    public static ImmutablePayloadBuffer Encode(BenchMovementBulkBody value)
    {
        byte[] bytes = new byte[52];
        WriteSingle(bytes, 0, value.PositionX);
        WriteSingle(bytes, 4, value.PositionY);
        WriteSingle(bytes, 8, value.Speed);
        WriteSingle(bytes, 12, value.HeadingDegrees);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), value.ClientTick);
        WriteSingle(bytes, 20, value.SampleA);
        WriteSingle(bytes, 24, value.SampleB);
        WriteSingle(bytes, 28, value.SampleC);
        WriteSingle(bytes, 32, value.SampleD);
        WriteSingle(bytes, 36, value.SampleE);
        WriteSingle(bytes, 40, value.SampleF);
        WriteSingle(bytes, 44, value.SampleG);
        WriteSingle(bytes, 48, value.SampleH);
        return ImmutablePayloadBuffer.FromOwned(bytes);
    }

    public static ImmutablePayloadBuffer Encode(BenchMovementEventBody value)
    {
        byte[] bytes = new byte[21];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), value.ObserverSlot);
        WriteSingle(bytes, 4, value.PositionX);
        WriteSingle(bytes, 8, value.PositionY);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12, 2), value.MoveX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14, 2), value.MoveY);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), value.ClientTick);
        bytes[20] = value.Flags;
        return ImmutablePayloadBuffer.FromOwned(bytes);
    }

    public static BenchMovementBody DecodeMovement(ImmutablePayloadBuffer payload)
    {
        ReadOnlySpan<byte> span = payload.Span;
        return new BenchMovementBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12));
    }

    public static BenchMovementBodyWire ReadMovementWire(ReadOnlySpan<byte> span)
    {
        return MemoryMarshal.Read<BenchMovementBodyWire>(span);
    }

    public static void DecodeMovementInto(ReadOnlySpan<byte> span, ref BenchMovementBody destination)
    {
        destination = new BenchMovementBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12));
    }

    public static BenchMovementVisualBody DecodeVisual(ImmutablePayloadBuffer payload)
    {
        ReadOnlySpan<byte> span = payload.Span;
        return new BenchMovementVisualBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12));
    }

    public static BenchMovementVisualBodyWire ReadVisualWire(ReadOnlySpan<byte> span)
    {
        return MemoryMarshal.Read<BenchMovementVisualBodyWire>(span);
    }

    public static void DecodeVisualInto(ReadOnlySpan<byte> span, ref BenchMovementVisualBody destination)
    {
        destination = new BenchMovementVisualBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12));
    }

    public static BenchMovementBulkBody DecodeBulk(ImmutablePayloadBuffer payload)
    {
        ReadOnlySpan<byte> span = payload.Span;
        return new BenchMovementBulkBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            ReadSingle(span, 20),
            ReadSingle(span, 24),
            ReadSingle(span, 28),
            ReadSingle(span, 32),
            ReadSingle(span, 36),
            ReadSingle(span, 40),
            ReadSingle(span, 44),
            ReadSingle(span, 48));
    }

    public static BenchMovementBulkBodyWire ReadBulkWire(ReadOnlySpan<byte> span)
    {
        return MemoryMarshal.Read<BenchMovementBulkBodyWire>(span);
    }

    public static void DecodeBulkInto(ReadOnlySpan<byte> span, ref BenchMovementBulkBody destination)
    {
        destination = new BenchMovementBulkBody(
            ReadSingle(span, 0),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            ReadSingle(span, 12),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            ReadSingle(span, 20),
            ReadSingle(span, 24),
            ReadSingle(span, 28),
            ReadSingle(span, 32),
            ReadSingle(span, 36),
            ReadSingle(span, 40),
            ReadSingle(span, 44),
            ReadSingle(span, 48));
    }

    public static BenchMovementEventBody DecodeEvent(ImmutablePayloadBuffer payload)
    {
        ReadOnlySpan<byte> span = payload.Span;
        return new BenchMovementEventBody(
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4)),
            ReadSingle(span, 4),
            ReadSingle(span, 8),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(12, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(14, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            span[20]);
    }

    public static BenchMovementEventBodyWire ReadEventWire(ReadOnlySpan<byte> span)
    {
        return MemoryMarshal.Read<BenchMovementEventBodyWire>(span);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    private static float ReadSingle(ReadOnlySpan<byte> span, int offset)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4))));
    }
}

internal readonly record struct BenchMovementBody(float PositionX, float PositionY, float VelocityX, float VelocityY) : IComponent<BenchMovementBody>;
internal readonly record struct BenchMovementVisualBody(float PositionX, float PositionY, float Speed, float HeadingDegrees) : IComponent<BenchMovementVisualBody>;
internal readonly record struct BenchMovementBulkBody(float PositionX, float PositionY, float Speed, float HeadingDegrees, uint ClientTick, float SampleA, float SampleB, float SampleC, float SampleD, float SampleE, float SampleF, float SampleG, float SampleH) : IComponent<BenchMovementBulkBody>;
internal readonly record struct BenchMovementEventBody(uint ObserverSlot, float PositionX, float PositionY, short MoveX, short MoveY, uint ClientTick, byte Flags) : IComponent<BenchMovementEventBody>;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BenchMovementBodyWire
{
    public readonly float PositionX;
    public readonly float PositionY;
    public readonly float VelocityX;
    public readonly float VelocityY;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BenchMovementVisualBodyWire
{
    public readonly float PositionX;
    public readonly float PositionY;
    public readonly float Speed;
    public readonly float HeadingDegrees;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BenchMovementBulkBodyWire
{
    public readonly float PositionX;
    public readonly float PositionY;
    public readonly float Speed;
    public readonly float HeadingDegrees;
    public readonly uint ClientTick;
    public readonly float SampleA;
    public readonly float SampleB;
    public readonly float SampleC;
    public readonly float SampleD;
    public readonly float SampleE;
    public readonly float SampleF;
    public readonly float SampleG;
    public readonly float SampleH;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BenchMovementEventBodyWire
{
    public readonly uint ObserverSlot;
    public readonly float PositionX;
    public readonly float PositionY;
    public readonly short MoveX;
    public readonly short MoveY;
    public readonly uint ClientTick;
    public readonly byte Flags;
}
