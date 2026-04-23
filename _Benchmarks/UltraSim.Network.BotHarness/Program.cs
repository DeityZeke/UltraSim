using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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

        Console.WriteLine($"Stages: {options.Stages}  Mode: {options.StageMode}  TickHz: {options.TickHz}  ActiveRatio: {options.ActiveRatio:0.##}  AoiRadii: {string.Join(",", options.AoiRadiiMeters)}");
        Console.WriteLine("Stage  Target Active  Ready  TickAvg  TickP95  TickP99  TickMax  PingAvg  PingP95  PingMax  PendMax  TickSLO  PingSLO");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------------------------");

        var rows = new JsonArray();
        using var harness = new MovementRampHarness(options);

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
                $"{result.PendingMax,7}  {(result.TickSloPass ? "PASS" : "FAIL"),7}  {(result.PingSloPass ? "PASS" : "FAIL"),7}");

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
}

internal sealed class MovementRampHarness : IDisposable
{
    private readonly HarnessArgs _options;
    private readonly PacketMetadataRegistry _metadata;
    private readonly UdpSocketNetworkTransport _serverTransport;
    private readonly World _world;
    private readonly ArchetypeSignature _movementSignature;
    private readonly ServerMovementSessionHost _serverHost;
    private readonly List<ClientHandle> _clients = [];
    private readonly List<Entity> _spawnedEntities = [];
    private uint _nextNetworkEntityId = 1;
    private long _clockTicks;
    private readonly PongProbePacket[] _pongScratch = new PongProbePacket[256];

    public MovementRampHarness(HarnessArgs options)
    {
        _options = options;
        _metadata = DefaultPacketMetadata.Create();

        _serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, options.Port),
            _metadata));
        _serverTransport.Start();

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<BenchMovementBody>();
        ComponentId movementEventId = builder.RegisterComponent<BenchMovementEventBody>();
        _world = new World(builder.Build());
        _movementSignature = new ArchetypeSignature([movementId, movementEventId]);

        var handshakePolicy = new HandshakePolicy(
            ProtocolVersion: 3,
            SchemaHash: 4,
            ContentHash: 5,
            RequiredFeatures: FeatureFlags.SharedPayloadFanout);

        _serverHost = new ServerMovementSessionHost(
            _world,
            _serverTransport,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession,
            handshakePolicy: handshakePolicy,
            ownerCorrectionPolicy: new ServerMovementOwnerCorrectionPolicy(
                _options.OwnerCorrectionDeadband,
                _options.OwnerCorrectionMaxSilentTicks));
        _serverHost.Start();
    }

    public int ActiveClientCount => _clients.Count;

    public StageResult RunStage(int stageIndex, int targetClients)
    {
        int connectAttempts = EnsureClientCount(targetClients);
        PumpUntilReady(_options.SettleTicks);

        int measuredTicks = _options.StageTicks;
        var tickSamplesMs = new List<double>(measuredTicks);
        long pendingSum = 0;
        int pendingMax = 0;
        var pingSamplesMs = new List<double>(Math.Max(32, _clients.Count * Math.Max(1, measuredTicks / Math.Max(1, _options.PingIntervalTicks))));
        long replicationDatagrams = 0;
        long replicationWireDatagrams = 0;
        long replicationPublishedItems = 0;
        long observerFramesReceived = 0;
        long observerFramesApplied = 0;
        long observerEventsReceived = 0;
        long observerEventsApplied = 0;
        long ownerCorrectionSamples = 0;
        double ownerCorrectionPositionErrorSum = 0d;
        double ownerCorrectionPositionErrorMax = 0d;
        double ownerCorrectionVelocityErrorSum = 0d;
        double ownerCorrectionVelocityErrorMax = 0d;
        long allocBefore = GC.GetTotalAllocatedBytes(true);

        StageReplicationContext? replication = _options.ObserverReplication
            ? CreateReplicationContext()
            : null;

        for (int tick = 0; tick < measuredTicks; tick++)
        {
            QueueInputsForTick((uint)tick);

            long start = Stopwatch.GetTimestamp();
            _clockTicks += 2;
            _serverHost.Pump(_clockTicks, _options.TickDeltaSeconds, (uint)(stageIndex * 100_000 + tick + 1));
            if (replication is not null)
            {
                UpdateObserverSubscriptions(replication);
                int published = PublishObserverReplication(replication);
                if (published > 0)
                {
                    replicationPublishedItems += published;
                    int dispatched = DispatchObserverReplication(replication, out int wireDatagrams);
                    replicationDatagrams += dispatched;
                    replicationWireDatagrams += wireDatagrams;
                }
            }
            long clientNowTicks = _clockTicks + 1;
            for (int i = 0; i < _clients.Count; i++)
            {
                ClientHandle client = _clients[i];
                if (client.Host.IsReady && _options.PingIntervalTicks > 0 && (tick % _options.PingIntervalTicks) == 0)
                    client.TrySendPing((uint)tick);

                client.Host.Pump(clientNowTicks);
                client.CollectPongSamples(clientNowTicks, _pongScratch, pingSamplesMs);
            }
            long end = Stopwatch.GetTimestamp();

            double elapsedMs = (end - start) * 1000.0 / Stopwatch.Frequency;
            tickSamplesMs.Add(elapsedMs);

            int pending = 0;
            for (int i = 0; i < _clients.Count; i++)
                pending += _clients[i].Host.Movement.Sequencer.PendingCount;

            pendingSum += pending;
            if (pending > pendingMax)
                pendingMax = pending;
        }

        long allocAfter = GC.GetTotalAllocatedBytes(false);
        TickWindowStats stats = new TickWindowStats(tickSamplesMs);
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
            PingAverageMs: pingStats.AverageMs,
            PingP95Ms: pingStats.P95Ms,
            PingMaxMs: pingStats.MaxMs,
            PendingAverage: tickSamplesMs.Count == 0 ? 0d : pendingSum / (double)tickSamplesMs.Count,
            PendingMax: pendingMax,
            AllocatedBytes: allocAfter - allocBefore,
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
            ObserverFramesReceived: observerFramesReceived,
            ObserverFramesApplied: observerFramesApplied,
            ObserverEventsReceived: observerEventsReceived,
            ObserverEventsApplied: observerEventsApplied,
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
            if (_world.Entities.IsAlive(_spawnedEntities[i]))
                _world.Destroy(_spawnedEntities[i]);
        }

        _spawnedEntities.Clear();
        _nextNetworkEntityId = 1;
        _clockTicks = 0;
    }

    public void Dispose()
    {
        Reset();
        _serverHost.Stop();
        _serverTransport.Dispose();
    }

    private int EnsureClientCount(int targetClients)
    {
        if (targetClients < _clients.Count)
            Reset();

        IPEndPoint serverEndPoint = (IPEndPoint)_serverTransport.LocalEndPoint!;
        int before = _clients.Count;
        while (_clients.Count < targetClients)
        {
            int clientIndex = _clients.Count;
            var transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(IPAddress.Loopback, 0),
                _metadata,
                serverEndPoint,
                new ConnectionId(1),
                AllowDynamicConnections: false));
            transport.Start();

            ClientHandle? handle = null;
            var observerState = new ClientObserverStateBridge(
                new ConnectionId(1),
                (itemKey, _, payload) => handle!.ApplyObserverState(itemKey, payload));
            var observerEvents = new ClientObserverEventBridge(
                new ConnectionId(1),
                (_, payload) => handle!.ApplyObserverEvent(payload));
            var host = new ClientMovementSessionHost(
                transport,
                new ConnectionId(1),
                new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression),
                observerState: observerState,
                observerEvents: observerEvents);
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
        for (int step = 0; step < settleTicks; step++)
        {
            _clockTicks += 2;
            _serverHost.Pump(_clockTicks, _options.TickDeltaSeconds, (uint)(10_000 + step));
            for (int i = 0; i < _clients.Count; i++)
                _clients[i].Host.Pump(_clockTicks + 1);

            bool allReady = _serverHost.BoundConnectionCount == _clients.Count;
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

    private ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
    {
        int spawnIndex = _spawnedEntities.Count;
        float spacing = _options.SpawnSpacingMeters;
        int width = Math.Max(1, _options.SpawnGridWidth);
        float startX = spacing <= 0f ? 0f : (spawnIndex % width) * spacing;
        float startY = spacing <= 0f ? 0f : (spawnIndex / width) * spacing;

        Entity entity = movementWorld.CreateDefault(_movementSignature);
        movementWorld.SetComponent(entity, new BenchMovementBody(startX, startY, 0f, 0f));
        movementWorld.SetComponent(entity, new BenchMovementEventBody(startX, startY, 0, 0, 0, 0));
        _spawnedEntities.Add(entity);
        return new ServerMovementSessionSpawn(entity, _nextNetworkEntityId++);
    }

    private StageReplicationContext CreateReplicationContext()
    {
        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, _options.AoiRadiiMeters);
        var publisher = new ServerEcsWorldNetworkPublisher(_world);
        var subscriptions = new ConnectionSubscriptionRegistry();
        var scheduler = new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream);
        var fanout = new ScheduledWorkFanoutBridge(subscriptions);
        return new StageReplicationContext(
            new ServerEcsChunkAoiScopeClassifier(policy),
            new ChunkAoiSubscriptionMaintainer(policy),
            subscriptions,
            publisher,
            new ConnectionStreamFanoutBridge(scheduler, fanout),
            new ConnectionId[Math.Max(64, _clients.Count)],
            new ScheduledWorkItem[Math.Max(4096, _clients.Count * 8)],
            new ConnectionId[Math.Max(4096, _clients.Count)],
            new OutboundDatagram[Math.Max(65536, _clients.Count * 128)]);
    }

    private void UpdateObserverSubscriptions(StageReplicationContext replication)
    {
        if (replication.BoundConnections.Length < _serverHost.BoundConnectionCount)
            replication.BoundConnections = new ConnectionId[_serverHost.BoundConnectionCount];

        int count = _serverHost.CopyBoundConnections(replication.BoundConnections);
        for (int i = 0; i < count; i++)
        {
            ConnectionId connectionId = replication.BoundConnections[i];
            if (!_serverHost.TryGetBoundEntity(connectionId, out Entity entity))
                continue;
            if (!_world.TryGetComponent(entity, out BenchMovementBody body))
                continue;

            replication.Maintainer.UpdateConnection(connectionId, new WorldPosition2(body.PositionX, body.PositionY), replication.Subscriptions);
        }
    }

    private int PublishObserverReplication(StageReplicationContext replication)
    {
        int published = replication.Publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<BenchMovementBody>(
                replication.Classifier,
                static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                static (_, body) => ServerRuntimeNetworkPayloadCodec.Encode(body)));

        if (_options.ObserverEvents)
        {
            published += replication.Publisher.PublishChunkAoiOrderedEvents(
                new ServerEcsChunkAoiOrderedEventRule<BenchMovementEventBody>(
                    replication.Classifier,
                    static (_, body) => new WorldPosition2(body.PositionX, body.PositionY),
                    static (_, body) => ServerRuntimeNetworkPayloadCodec.Encode(body)));
        }

        return published;
    }

    private int DispatchObserverReplication(StageReplicationContext replication, out int wireDatagrams)
    {
        wireDatagrams = 0;
        while (true)
        {
            ConnectionScheduledFanoutResult result = replication.Bridge.ScheduleAndDispatch(
                replication.ScheduleState,
                ConnectionSendBudget.Unlimited,
                replication.ScheduledWork,
                replication.SubscriberScratch,
                replication.Output);

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
            for (int i = 0; i < result.Fanout.DatagramCount; i++)
                _serverTransport.EnqueueOutbound(replication.Output[i]);
            _serverTransport.FlushOutbound();
            wireDatagrams = _serverTransport.LastFlushWireDatagramCount;
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

        if (_options.ObserverEvents &&
            (_options.ObserverEventEveryTicks <= 1 || (input.ClientTick % (uint)_options.ObserverEventEveryTicks) == 0))
        {
            movementWorld.SetComponent(movementEntity, new BenchMovementEventBody(
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

    private sealed class ClientHandle : IDisposable
    {
        private readonly Dictionary<ushort, long> _pingSentAt = new();
        private readonly List<double> _pingSamplesMs = [];
        private readonly Dictionary<ulong, BenchMovementBody> _observerStates = [];
        private readonly List<BenchMovementEventBody> _observerEvents = [];
        private ushort _nextPingSequence = 1;
        private ClientMovementState _predictedOwnerState;
        private uint _lastAuthoritativeServerTick;
        private readonly float _predictionJitterPercent;
        private uint _predictionStep;

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
        public int ObserverStateCount => _observerStates.Count;

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

        public void ApplyObserverState(ulong itemKey, ImmutablePayloadBuffer payload)
        {
            _observerStates[itemKey] = ServerRuntimeNetworkPayloadCodec.Decode<BenchMovementBody>(payload);
        }

        public void ApplyObserverEvent(ImmutablePayloadBuffer payload)
        {
            _observerEvents.Add(ServerRuntimeNetworkPayloadCodec.Decode<BenchMovementEventBody>(payload));
            if (_observerEvents.Count > 256)
                _observerEvents.RemoveRange(0, _observerEvents.Count - 256);
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
    }

    private sealed class StageReplicationContext
    {
        public StageReplicationContext(
            ServerEcsChunkAoiScopeClassifier classifier,
            ChunkAoiSubscriptionMaintainer maintainer,
            ConnectionSubscriptionRegistry subscriptions,
            ServerEcsWorldNetworkPublisher publisher,
            ConnectionStreamFanoutBridge bridge,
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
        public ConnectionScheduleState ScheduleState { get; set; }
        public ConnectionId[] BoundConnections { get; set; }
        public ScheduledWorkItem[] ScheduledWork { get; set; }
        public ConnectionId[] SubscriberScratch { get; }
        public OutboundDatagram[] Output { get; set; }
    }

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
    public bool ObserverEvents { get; set; }
    public int ObserverEventEveryTicks { get; set; } = 8;
    public float SpawnSpacingMeters { get; set; } = 4f;
    public int SpawnGridWidth { get; set; } = 32;
    public float[] AoiRadiiMeters { get; set; } = [7.5f];
    public float OwnerCorrectionDeadband { get; set; } = 0f;
    public uint OwnerCorrectionMaxSilentTicks { get; set; } = 1;
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

            if (arg == "--stages") options.Stages = Next() ?? options.Stages;
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
            else if (arg == "--observer-events") options.ObserverEvents = true;
            else if (arg == "--observer-event-every-ticks") options.ObserverEventEveryTicks = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--spawn-spacing-m") options.SpawnSpacingMeters = float.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--spawn-grid-width") options.SpawnGridWidth = int.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--aoi-radii-m") options.AoiRadiiMeters = ParseFloatList(Next()!, nameof(AoiRadiiMeters));
            else if (arg == "--owner-deadband") options.OwnerCorrectionDeadband = float.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-max-silent-ticks") options.OwnerCorrectionMaxSilentTicks = uint.Parse(Next()!, CultureInfo.InvariantCulture);
            else if (arg == "--owner-prediction-jitter-percent") options.OwnerPredictionJitterPercent = float.Parse(Next()!, CultureInfo.InvariantCulture);
        }

        options.TickHz = Math.Max(1, options.TickHz);
        options.ActiveRatio = Math.Clamp(options.ActiveRatio, 0d, 1d);
        options.ObserverEventEveryTicks = Math.Max(1, options.ObserverEventEveryTicks);
        if (options.AoiRadiiMeters.Length == 0)
            options.AoiRadiiMeters = [7.5f];
        if (float.IsNaN(options.OwnerCorrectionDeadband) || float.IsInfinity(options.OwnerCorrectionDeadband) || options.OwnerCorrectionDeadband < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerCorrectionDeadband));
        if (options.OwnerCorrectionMaxSilentTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(options.OwnerCorrectionMaxSilentTicks));
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

        return new JsonObject
        {
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
            ["observer_events"] = ObserverEvents,
            ["observer_event_every_ticks"] = ObserverEventEveryTicks,
            ["spawn_spacing_m"] = SpawnSpacingMeters,
            ["spawn_grid_width"] = SpawnGridWidth,
            ["aoi_radii_m"] = aoiRadii,
            ["owner_deadband"] = OwnerCorrectionDeadband,
            ["owner_max_silent_ticks"] = OwnerCorrectionMaxSilentTicks,
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
    double PingAverageMs,
    double PingP95Ms,
    double PingMaxMs,
    double PendingAverage,
    int PendingMax,
    long AllocatedBytes,
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
    long ObserverFramesReceived,
    long ObserverFramesApplied,
    long ObserverEventsReceived,
    long ObserverEventsApplied,
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
            ["ping_avg_ms"] = Math.Round(PingAverageMs, 3),
            ["ping_p95_ms"] = Math.Round(PingP95Ms, 3),
            ["ping_max_ms"] = Math.Round(PingMaxMs, 3),
            ["pending_avg"] = Math.Round(PendingAverage, 3),
            ["pending_max"] = PendingMax,
            ["alloc_bytes"] = AllocatedBytes,
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
            ["observer_frames_received"] = ObserverFramesReceived,
            ["observer_frames_applied"] = ObserverFramesApplied,
            ["observer_events_received"] = ObserverEventsReceived,
            ["observer_events_applied"] = ObserverEventsApplied,
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

internal readonly record struct BenchMovementBody(float PositionX, float PositionY, float VelocityX, float VelocityY) : IComponent<BenchMovementBody>;
internal readonly record struct BenchMovementEventBody(float PositionX, float PositionY, short MoveX, short MoveY, uint ClientTick, byte Flags) : IComponent<BenchMovementEventBody>;
