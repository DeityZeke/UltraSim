using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using UltraSim.Network;
using UltraSim.Network.Comparison;

AdapterOptions options = AdapterOptions.Parse(args);
NetworkBotScenario scenario = options.CreateScenario();
using var harness = new V3OwnerMovementHarness(scenario, options.Topology);
NetworkBotStageResult result = harness.Run();
NetworkBotComparisonIo.Write(options.Output, NetworkBotAdapterReport.Create(result.Generation, result));
Console.WriteLine($"{result.Generation} {scenario.Id}/{scenario.Clients}: tick p95 {result.TickMilliseconds.P95:F3} ms, " +
    $"ping p95 {result.PingMilliseconds.P95:F3} ms, reserved {result.ReservedNetworkBytes / 1048576d:F2} MiB");
Console.WriteLine(Path.GetFullPath(options.Output));

internal sealed class V3OwnerMovementHarness : IDisposable
{
    private static readonly NetworkMessageDescriptor InputDescriptor =
        new(new NetworkMessageId(1), "ultrasim.benchmark.owner-input", 1,
            NetworkLane.Input, 10, 0xA001);
    private static readonly NetworkMessageDescriptor OwnerDescriptor =
        new(new NetworkMessageId(2), "ultrasim.benchmark.owner-state", 1,
            NetworkLane.OwnerState, 30, 0xA002);
    private static readonly NetworkMessageDescriptor PingDescriptor =
        new(new NetworkMessageId(3), "ultrasim.benchmark.ping", 1,
            NetworkLane.Control, 2, 0xA003);
    private static readonly NetworkMessageDescriptor PongDescriptor =
        new(new NetworkMessageId(4), "ultrasim.benchmark.pong", 1,
            NetworkLane.Control, 2, 0xA004);
    private static readonly NetworkProtocolManifest Manifest = NetworkProtocolManifest.Create(
        1, [InputDescriptor, OwnerDescriptor, PingDescriptor, PongDescriptor]);
    private static readonly NetworkProtocolLimits ProtocolLimits = new(
        MaximumReliableMessages: 8,
        MaximumReceivedMessages: 8,
        MaximumMessageBytes: 128,
        MaximumFragmentsPerMessage: 1,
        SentPacketCapacity: 64,
        MaximumInboundPacketsPerPump: 16,
        MaximumPacketsPerPump: 16,
        MinimumRetransmissionSteps: 2,
        MaximumRetransmissionSteps: 64,
        InitialCongestionWindowPackets: 4,
        MaximumCongestionWindowPackets: 32,
        MaximumIndependentUnreliableMessages: 4);
    private static readonly UdpProtocolLimits UdpLimits = new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 16_384,
        SocketReceiveBufferBytes: 16_384,
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 30_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 5_000,
        KeepAliveIntervalMilliseconds: 5_000);

    private readonly NetworkBotScenario _scenario;
    private readonly Bot[] _bots;
    private readonly NetworkServerIdentity _identity;
    private readonly V3UdpTopology _topology;
    private readonly SharedUdpProtocolHost? _sharedHost;
    private readonly long _realtimeTickIntervalStopwatchTicks;
    private readonly List<double> _handshakeMilliseconds;
    private long _handshakeAttempts;
    private long _handshakeSuccesses;
    private long _handshakeAllocatedBytes;
    private ulong _handshakeWireDatagrams;
    private ulong _handshakeWireBytes;
    private bool _disposed;

    internal V3OwnerMovementHarness(NetworkBotScenario scenario, V3UdpTopology topology)
    {
        _scenario = scenario.Validate();
        _topology = topology;
        _realtimeTickIntervalStopwatchTicks = checked((long)Math.Round(
            Stopwatch.Frequency / (double)_scenario.TickHz,
            MidpointRounding.AwayFromZero));
        _bots = new Bot[scenario.Clients];
        _handshakeMilliseconds = new List<double>(scenario.Clients);
        _identity = NetworkServerIdentity.CreateEphemeral();
        ReadOnlyMemory<byte> trusted = _identity.PublicKey.ToArray();
        if (topology == V3UdpTopology.SharedListener)
            _sharedHost = new SharedUdpProtocolHost(
                ProtocolLimits, UdpLimits,
                new SharedUdpProtocolLimits(
                    scenario.Clients,
                    MaximumQueuedDatagramsPerSession: 4,
                    MaximumInboundDatagramsPerPump: checked(scenario.Clients * 16)),
                Manifest, _identity, trusted);
        const int width = 32; // frozen V2 bot-harness spawn-grid default
        for (int index = 0; index < _bots.Length; index++)
        {
            _handshakeAttempts++;
            long handshakeStarted = Stopwatch.GetTimestamp();
            long handshakeAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            UdpProtocolSession? pair = null;
            SharedUdpProtocolSession? shared = null;
            if (topology == V3UdpTopology.ConnectedPairs)
            {
                if (!UdpProtocolSession.TryCreate(
                        ProtocolLimits, UdpLimits, out pair, out _,
                        Manifest, _identity, trusted) || pair is null)
                    throw new InvalidOperationException($"V3 UDP bot {index} did not establish.");
            }
            else if (!_sharedHost!.TryAddLoopbackSession(out shared, out _) || shared is null)
                throw new InvalidOperationException($"V3 shared-listener bot {index} did not establish.");
            _handshakeSuccesses++;
            _handshakeMilliseconds.Add(
                Stopwatch.GetElapsedTime(handshakeStarted).TotalMilliseconds);
            _handshakeAllocatedBytes = checked(_handshakeAllocatedBytes +
                GC.GetAllocatedBytesForCurrentThread() - handshakeAllocatedBefore);
            UdpDatagramEndpointSnapshot handshakeClient = pair?.GetSnapshot().ClientDatagrams ??
                shared!.GetSnapshot().ClientDatagrams;
            UdpDatagramEndpointSnapshot handshakeServer = pair?.GetSnapshot().ServerDatagrams ??
                shared!.GetSnapshot().ServerDatagrams;
            _handshakeWireDatagrams = SaturatingAdd(_handshakeWireDatagrams,
                SaturatingAdd(handshakeClient.Sent, handshakeServer.Sent));
            _handshakeWireBytes = SaturatingAdd(_handshakeWireBytes,
                SaturatingAdd(handshakeClient.SentBytes, handshakeServer.SentBytes));
            float x = (float)((index % width) * scenario.SpawnSpacingMeters);
            float y = (float)((index / width) * scenario.SpawnSpacingMeters);
            _bots[index] = new Bot(index, pair, shared, x, y,
                Math.Max(8, scenario.StageTicks / (scenario.TickHz / scenario.InputHz) + 8));
        }
        for (int tick = 0; tick < scenario.SettleTicks; tick++)
        {
            PumpAll(1);
            DrainAll();
        }
    }

    internal NetworkBotStageResult Run()
    {
        using Process process = Process.GetCurrentProcess();
        var tickMs = new List<double>(_scenario.StageTicks);
        var serverMs = new List<double>(_scenario.StageTicks);
        var clientMs = new List<double>(_scenario.StageTicks);
        var pingMs = new List<double>(Math.Max(1, _scenario.Clients * _scenario.StageTicks / _scenario.TickHz));
        var ackMs = new List<double>(Math.Max(1, _scenario.Clients * _scenario.StageTicks /
            (_scenario.TickHz / _scenario.InputHz)));
        var reliableOccupancy = new List<int>(_scenario.StageTicks);
        var independentOccupancy = new List<int>(_scenario.StageTicks);
        var receivedOccupancy = new List<int>(_scenario.StageTicks);
        var inFlightOccupancy = new List<int>(_scenario.StageTicks);
        long logicalInputs = 0;
        long ownerApplied = 0;
        long ownerMessagesQueued = 0;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        int maxReliable = 0;
        int maxUnreliable = 0;
        int maxReceived = 0;
        int maxInFlight = 0;
        long pressure50 = 0;
        long pressure75 = 0;
        long pressure90 = 0;
        long pressure100 = 0;
        double occupancySum = 0;
        double maximumOccupancy = 0;
        long peakManagedHeap = GC.GetTotalMemory(false);
        process.Refresh();
        long peakWorkingSet = process.WorkingSet64;
        long peakPrivateBytes = process.PrivateMemorySize64;
        long deadlineMisses = 0;
        int deadlineStreak = 0;
        int longestDeadlineStreak = 0;
        int inputInterval = _scenario.TickHz / _scenario.InputHz;
        double budgetMs = 1000d / _scenario.TickHz;
        long nextDeadline = Stopwatch.GetTimestamp();
        process.Refresh();
        TimeSpan processCpuBefore = process.TotalProcessorTime;
        long measuredWallStarted = Stopwatch.GetTimestamp();

        for (int tick = 0; tick < _scenario.StageTicks; tick++)
        {
            for (int index = 0; index < _bots.Length; index++)
            {
                Bot bot = _bots[index];
                if (tick % inputInterval == index % inputInterval && bot.TryQueueInput(tick))
                    logicalInputs++;
                if (tick % _scenario.TickHz == index % _scenario.TickHz)
                    bot.TryQueuePing();
            }

            long tickStart = Stopwatch.GetTimestamp();
            long serverStart = tickStart;
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].ProcessServer(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].AdvanceAuthority(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                if (_bots[index].QueueOwnerState(checked((ulong)tick + 1)))
                    ownerMessagesQueued++;
            PumpAll(2);
            long serverEnd = Stopwatch.GetTimestamp();
            long clientStart = Stopwatch.GetTimestamp();
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
            PumpAll(1);
            long end = Stopwatch.GetTimestamp();

            double elapsed = Stopwatch.GetElapsedTime(tickStart, end).TotalMilliseconds;
            tickMs.Add(elapsed);
            serverMs.Add(Stopwatch.GetElapsedTime(serverStart, serverEnd).TotalMilliseconds);
            clientMs.Add(Stopwatch.GetElapsedTime(clientStart, end).TotalMilliseconds);
            if (elapsed > budgetMs)
            {
                deadlineMisses++;
                deadlineStreak++;
                longestDeadlineStreak = Math.Max(longestDeadlineStreak, deadlineStreak);
            }
            else deadlineStreak = 0;

            OccupancyTick pressure = SamplePressure(
                ref maxReliable, ref maxUnreliable, ref maxReceived, ref maxInFlight);
            reliableOccupancy.Add(pressure.Reliable);
            independentOccupancy.Add(pressure.Independent);
            receivedOccupancy.Add(pressure.Received);
            inFlightOccupancy.Add(pressure.InFlight);
            double occupancy = pressure.MaximumRatio;
            occupancySum += occupancy;
            maximumOccupancy = Math.Max(maximumOccupancy, occupancy);
            if (occupancy >= 0.50) pressure50++;
            if (occupancy >= 0.75) pressure75++;
            if (occupancy >= 0.90) pressure90++;
            if (occupancy >= 1.00) pressure100++;
            WaitForRealtimeTick(ref nextDeadline);
        }

        process.Refresh();
        double processCpuMilliseconds =
            (process.TotalProcessorTime - processCpuBefore).TotalMilliseconds;
        double measuredWallMilliseconds =
            Stopwatch.GetElapsedTime(measuredWallStarted).TotalMilliseconds;

        long allocatedAfter = GC.GetTotalAllocatedBytes(false);
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        // Match the V2 harness's untimed post-stage settle: no new intent is
        // created, but admitted input and current control state may converge.
        for (int settle = 0; settle < _scenario.SettleTicks; settle++)
        {
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].ProcessServer(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].AdvanceAuthority(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].QueueOwnerState(checked((ulong)_scenario.StageTicks + (ulong)settle + 1));
            PumpAll(2);
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
            PumpAll(1);
        }

        ulong datagrams = 0;
        ulong bytes = 0;
        ulong retransmissions = 0;
        ulong retransmittedWireBytes = 0;
        ulong capacityDrops = 0;
        ulong dataPacketsSent = 0;
        ulong acknowledgementPacketsSent = 0;
        ulong keepAlivePacketsSent = 0;
        ulong encryptedPlaintextBytes = 0;
        ulong decryptedPlaintextBytes = 0;
        ulong reliableFragmentsSent = 0;
        ulong authenticationFailures = 0;
        ulong replayRejections = 0;
        ulong socketSendCalls = 0;
        ulong socketReceivePollCalls = 0;
        ulong socketReceiveCalls = 0;
        ulong socketWouldBlockEvents = 0;
        int maximumSentDatagramBytes = 0;
        long reserved = 0;
        long socketBuffers = 0;
        for (int index = 0; index < _bots.Length; index++)
        {
            BenchmarkTransportSnapshot snapshot = _bots[index].GetTransportSnapshot();
            datagrams = SaturatingAdd(datagrams,
                SaturatingAdd(snapshot.ClientDatagrams.Sent, snapshot.ServerDatagrams.Sent));
            bytes = SaturatingAdd(bytes,
                SaturatingAdd(snapshot.ClientDatagrams.SentBytes, snapshot.ServerDatagrams.SentBytes));
            retransmissions = SaturatingAdd(retransmissions,
                SaturatingAdd(snapshot.Client.ReliableRetransmissions, snapshot.Server.ReliableRetransmissions));
            retransmittedWireBytes = SaturatingAdd(retransmittedWireBytes,
                SaturatingAdd(snapshot.Client.ReliableRetransmittedWireBytes,
                    snapshot.Server.ReliableRetransmittedWireBytes));
            dataPacketsSent = SaturatingAdd(dataPacketsSent,
                SaturatingAdd(snapshot.Client.DataPacketsSent, snapshot.Server.DataPacketsSent));
            acknowledgementPacketsSent = SaturatingAdd(acknowledgementPacketsSent,
                SaturatingAdd(snapshot.Client.AcknowledgementPacketsSent,
                    snapshot.Server.AcknowledgementPacketsSent));
            keepAlivePacketsSent = SaturatingAdd(keepAlivePacketsSent,
                SaturatingAdd(snapshot.Client.KeepAlivePacketsSent, snapshot.Server.KeepAlivePacketsSent));
            encryptedPlaintextBytes = SaturatingAdd(encryptedPlaintextBytes,
                SaturatingAdd(snapshot.Client.EncryptedPlaintextBytes, snapshot.Server.EncryptedPlaintextBytes));
            decryptedPlaintextBytes = SaturatingAdd(decryptedPlaintextBytes,
                SaturatingAdd(snapshot.Client.DecryptedPlaintextBytes, snapshot.Server.DecryptedPlaintextBytes));
            reliableFragmentsSent = SaturatingAdd(reliableFragmentsSent,
                SaturatingAdd(snapshot.Client.ReliableFragmentsSent, snapshot.Server.ReliableFragmentsSent));
            authenticationFailures = SaturatingAdd(authenticationFailures,
                SaturatingAdd(snapshot.Client.AuthenticationFailures, snapshot.Server.AuthenticationFailures));
            replayRejections = SaturatingAdd(replayRejections,
                SaturatingAdd(snapshot.Client.ReplayRejections, snapshot.Server.ReplayRejections));
            socketSendCalls = SaturatingAdd(socketSendCalls,
                SaturatingAdd(snapshot.ClientDatagrams.SendCalls, snapshot.ServerDatagrams.SendCalls));
            socketReceivePollCalls = SaturatingAdd(socketReceivePollCalls,
                SaturatingAdd(snapshot.ClientDatagrams.ReceivePollCalls,
                    snapshot.ServerDatagrams.ReceivePollCalls));
            socketReceiveCalls = SaturatingAdd(socketReceiveCalls,
                SaturatingAdd(snapshot.ClientDatagrams.ReceiveCalls, snapshot.ServerDatagrams.ReceiveCalls));
            socketWouldBlockEvents = SaturatingAdd(socketWouldBlockEvents,
                SaturatingAdd(snapshot.ClientDatagrams.WouldBlockEvents,
                    snapshot.ServerDatagrams.WouldBlockEvents));
            maximumSentDatagramBytes = Math.Max(maximumSentDatagramBytes,
                Math.Max(snapshot.ClientDatagrams.MaximumSentDatagramBytes,
                    snapshot.ServerDatagrams.MaximumSentDatagramBytes));
            capacityDrops = SaturatingAdd(capacityDrops,
                SaturatingAdd(snapshot.Client.CapacityDrops + snapshot.ClientDatagrams.CapacityDrops,
                    snapshot.Server.CapacityDrops + snapshot.ServerDatagrams.CapacityDrops));
            if (_sharedHost is null)
            {
                reserved = checked(reserved + snapshot.Client.ReservedBytes + snapshot.Server.ReservedBytes);
                socketBuffers = checked(socketBuffers + snapshot.ConfiguredSocketBufferBytes);
            }
        }
        SharedUdpProtocolHostSnapshot sharedSnapshot = default;
        if (_sharedHost is not null)
        {
            sharedSnapshot = _sharedHost.GetSnapshot();
            reserved = sharedSnapshot.KnownReservedBytes;
            socketBuffers = sharedSnapshot.ConfiguredSocketBufferBytes;
            capacityDrops = SaturatingAdd(capacityDrops, sharedSnapshot.Listener.CapacityDrops);
            socketSendCalls = SaturatingAdd(socketSendCalls, sharedSnapshot.Listener.SendCalls);
            socketReceivePollCalls = SaturatingAdd(
                socketReceivePollCalls, sharedSnapshot.Listener.ReceivePollCalls);
            socketReceiveCalls = SaturatingAdd(socketReceiveCalls, sharedSnapshot.Listener.ReceiveCalls);
            socketWouldBlockEvents = SaturatingAdd(
                socketWouldBlockEvents, sharedSnapshot.Listener.WouldBlockEvents);
            maximumSentDatagramBytes = Math.Max(
                maximumSentDatagramBytes, sharedSnapshot.Listener.MaximumSentDatagramBytes);
        }
        peakManagedHeap = Math.Max(peakManagedHeap, GC.GetTotalMemory(false));
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
        peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
        IReadOnlyList<NetworkCanonicalOwnerState> canonicalStates = CaptureCanonicalStates();
        long defaultReservedPerClient = checked(2 * NetworkProtocolLimits.Default.EstimateReservedPayloadBytes(
            new DeterministicDatagramLimits(UdpLimits.MaximumDatagramBytes, UdpLimits.MaximumDatagramBytes),
            Manifest.Count));
        long normalizedLogicalBytes = checked(
            logicalInputs * 10 + ownerMessagesQueued * 30 + pingMs.Count * 4L);
        NetworkMetricSummary inputAckTicks = NetworkMetricSummary.From(
            ackMs.Select(value => value * _scenario.TickHz / 1000d).ToArray());
        NetworkMetricSummary perClientInputAckP95 = NetworkMetricSummary.From(
            _bots.Select(bot => bot.InputAcknowledgements.P95).ToArray());
        NetworkMetricSummary perClientPingP95 = NetworkMetricSummary.From(
            _bots.Select(bot => bot.PingAcknowledgements.P95).ToArray());
        double observationSeconds =
            (_scenario.StageTicks + _scenario.SettleTicks) / (double)_scenario.TickHz;
        ulong steadyWireBytes = bytes >= _handshakeWireBytes ? bytes - _handshakeWireBytes : 0;
        return new NetworkBotStageResult
        {
            Generation = _topology == V3UdpTopology.SharedListener
                ? "V3-shared-listener"
                : "V3-connected-pairs",
            Scenario = _scenario,
            EvidenceProfile = new(
                "network-bot-owner-movement-v1",
                "Deterministic phased owner input, authoritative latest control, owner correction, and ping; arrival-driven position remains diagnostic.",
                "workload bounds fixed; implementation reservations reported separately",
                $"fixed {_scenario.TickHz} Hz; {_scenario.InputHz} Hz phased input; realtime",
                _topology == V3UdpTopology.SharedListener
                    ? "one shared server UDP listener, one client socket and two secure protocol peers per bot"
                    : "one connected localhost UDP socket pair and two secure protocol peers per bot",
                "process-wide allocated bytes and GC collections; physical memory where exposed"),
            ReadyClients = _bots.Length,
            TickMilliseconds = NetworkMetricSummary.From(tickMs),
            ServerPumpMilliseconds = NetworkMetricSummary.From(serverMs),
            ClientPumpMilliseconds = NetworkMetricSummary.From(clientMs),
            PingMilliseconds = NetworkMetricSummary.From(pingMs),
            InputAcknowledgementMilliseconds = NetworkMetricSummary.From(ackMs),
            InputAcknowledgementTicks = inputAckTicks,
            PerClientInputAckP95Milliseconds = perClientInputAckP95,
            PerClientPingP95Milliseconds = perClientPingP95,
            HandshakeMilliseconds = NetworkMetricSummary.From(_handshakeMilliseconds),
            MetricCoverage = new(
                OwnerInputFreshness: true,
                WorldStateFreshnessByTier: false,
                ApplicationEventLatency: false,
                PerClientFairness: true,
                TickDeadlineCompliance: true,
                BoundedOccupancy: true,
                WireBytes: true,
                SocketSyscalls: true,
                SecurityBytes: true,
                ProcessMemory: true,
                ManagedAllocation: true,
                ProcessCpu: true,
                Handshake: true,
                InterestAmplification: false,
                BaselineRecovery: false,
                DeepPhaseTiming: false),
            TickDeadlineMisses = deadlineMisses,
            LongestDeadlineMissStreak = longestDeadlineStreak,
            LogicalInputs = logicalInputs,
            OwnerStatesApplied = ownerApplied,
            WorldStateRecordsApplied = 0,
            ApplicationEventsApplied = 0,
            WireDatagramsSent = datagrams,
            WireBytesSent = bytes,
            RetransmittedPackets = retransmissions,
            RetransmittedWireBytes = retransmittedWireBytes,
            ReservedNetworkBytes = reserved,
            ConfiguredSocketBufferBytes = socketBuffers,
            ManagedHeapBytes = peakManagedHeap,
            WorkingSetBytes = peakWorkingSet,
            PrivateBytes = peakPrivateBytes,
            AllocatedBytes = allocatedAfter - allocatedBefore,
            Gen0Collections = gen0After - gen0Before,
            Gen1Collections = gen1After - gen1Before,
            Gen2Collections = gen2After - gen2Before,
            ProcessCpuMilliseconds = processCpuMilliseconds,
            ProcessCpuCoreUtilization = measuredWallMilliseconds <= 0
                ? 0
                : processCpuMilliseconds / measuredWallMilliseconds,
            MaximumReliableQueued = maxReliable,
            MaximumUnreliableQueued = maxUnreliable,
            MaximumReceivedQueued = maxReceived,
            MaximumInFlightPackets = maxInFlight,
            ReliableOccupancy = NetworkOccupancySummary.From(
                reliableOccupancy, ProtocolLimits.MaximumReliableMessages),
            IndependentUnreliableOccupancy = NetworkOccupancySummary.From(
                independentOccupancy, ProtocolLimits.MaximumIndependentUnreliableMessages),
            ReceivedOccupancy = NetworkOccupancySummary.From(
                receivedOccupancy, ProtocolLimits.MaximumReceivedMessages),
            InFlightOccupancy = NetworkOccupancySummary.From(
                inFlightOccupancy, ProtocolLimits.MaximumCongestionWindowPackets),
            CapacityPressureTicks50 = pressure50,
            CapacityPressureTicks75 = pressure75,
            CapacityPressureTicks90 = pressure90,
            CapacityPressureTicks100 = pressure100,
            CapacityDrops = capacityDrops,
            HandshakeAttempts = _handshakeAttempts,
            HandshakeSuccesses = _handshakeSuccesses,
            HandshakeAllocatedBytes = _handshakeAllocatedBytes,
            HandshakeWireDatagrams = _handshakeWireDatagrams,
            HandshakeWireBytes = _handshakeWireBytes,
            SocketSendCalls = socketSendCalls,
            SocketReceivePollCalls = socketReceivePollCalls,
            SocketReceiveCalls = socketReceiveCalls,
            SocketWouldBlockEvents = socketWouldBlockEvents,
            MaximumSentDatagramBytes = maximumSentDatagramBytes,
            CanonicalOwnerStateHash = NetworkBotCanonicalState.ComputeHash(canonicalStates),
            CanonicalOwnerStates = canonicalStates,
            CanonicalValidation = "Exact client-applied owner identity, acknowledged input sequence, and velocity. Position remains diagnostic because V2 integrates arrival-relative input.",
            Diagnostics = new Dictionary<string, double>
            {
                ["reserved_bytes_per_client"] = reserved / (double)_bots.Length,
                ["configured_socket_bytes_per_client"] = socketBuffers / (double)_bots.Length,
                ["wire_bytes_per_input"] = logicalInputs == 0 ? 0 : bytes / (double)logicalInputs,
                ["mean_wire_bytes_per_datagram"] = datagrams == 0 ? 0 : bytes / (double)datagrams,
                ["normalized_logical_application_bytes"] = normalizedLogicalBytes,
                ["owner_state_messages_queued"] = ownerMessagesQueued,
                ["wire_bytes_per_normalized_logical_byte"] = normalizedLogicalBytes == 0
                    ? 0
                    : bytes / (double)normalizedLogicalBytes,
                ["wire_datagrams_per_applied_owner_state"] = ownerApplied == 0
                    ? 0
                    : datagrams / (double)ownerApplied,
                ["server_wall_us_per_client_tick"] = tickMs.Count == 0 ? 0 :
                    serverMs.Average() * 1000d / _bots.Length,
                ["combined_process_cpu_us_per_client_tick"] = tickMs.Count == 0 ? 0 :
                    processCpuMilliseconds * 1000d / (_bots.Length * tickMs.Count),
                ["combined_process_cpu_machine_percent"] = measuredWallMilliseconds <= 0 ? 0 :
                    processCpuMilliseconds / (measuredWallMilliseconds * Environment.ProcessorCount) * 100d,
                ["network_bytes_per_client_second"] = observationSeconds <= 0 ? 0 :
                    steadyWireBytes / (_bots.Length * observationSeconds),
                ["tick_deadline_miss_percent"] = tickMs.Count == 0 ? 0 :
                    deadlineMisses * 100d / tickMs.Count,
                ["tick_slo_headroom_ms"] = budgetMs - NetworkMetricSummary.From(tickMs).P95,
                ["mean_bounded_occupancy_ratio"] = tickMs.Count == 0 ? 0 : occupancySum / tickMs.Count,
                ["maximum_bounded_occupancy_ratio"] = maximumOccupancy,
                ["worst_client_input_ack_p95_ms"] = _bots.Max(bot => bot.InputAcknowledgements.P95),
                ["worst_client_input_ack_max_ms"] = _bots.Max(bot => bot.InputAcknowledgements.Max),
                ["worst_client_ping_p95_ms"] = _bots.Max(bot => bot.PingAcknowledgements.P95),
                ["worst_client_ping_max_ms"] = _bots.Max(bot => bot.PingAcknowledgements.Max),
                ["data_packets_sent"] = dataPacketsSent,
                ["acknowledgement_packets_sent"] = acknowledgementPacketsSent,
                ["keep_alive_packets_sent"] = keepAlivePacketsSent,
                ["encrypted_plaintext_bytes"] = encryptedPlaintextBytes,
                ["decrypted_plaintext_bytes"] = decryptedPlaintextBytes,
                ["reliable_fragments_sent"] = reliableFragmentsSent,
                ["authentication_failures"] = authenticationFailures,
                ["replay_rejections"] = replayRejections,
                ["retransmitted_wire_fraction"] = bytes == 0 ? 0 :
                    retransmittedWireBytes / (double)bytes,
                ["mean_datagram_fill_ratio"] = datagrams == 0 ? 0 :
                    bytes / (double)datagrams / UdpLimits.MaximumDatagramBytes,
                ["maximum_datagram_fill_ratio"] =
                    maximumSentDatagramBytes / (double)UdpLimits.MaximumDatagramBytes,
                ["datagrams_per_send_syscall"] = socketSendCalls == 0 ? 0 :
                    datagrams / (double)socketSendCalls,
                ["bytes_per_send_syscall"] = socketSendCalls == 0 ? 0 :
                    bytes / (double)socketSendCalls,
                ["handshake_success_rate"] = _handshakeAttempts == 0 ? 0 :
                    _handshakeSuccesses / (double)_handshakeAttempts,
                ["handshake_allocated_bytes_per_join"] = _handshakeSuccesses == 0 ? 0 :
                    _handshakeAllocatedBytes / (double)_handshakeSuccesses,
                ["handshake_wire_bytes_per_join"] = _handshakeSuccesses == 0 ? 0 :
                    _handshakeWireBytes / (double)_handshakeSuccesses,
                ["default_protocol_reserved_bytes_per_client_estimate"] = defaultReservedPerClient,
                ["default_protocol_reserved_bytes_total_estimate"] =
                    checked(defaultReservedPerClient * (long)_bots.Length),
                ["shared_listener_registered_paths"] = sharedSnapshot.Listener.RegisteredPaths,
                ["shared_listener_maximum_queued_datagrams"] = sharedSnapshot.Listener.MaximumQueuedDatagrams,
                ["shared_listener_unknown_path_datagrams"] = sharedSnapshot.Listener.UnknownPathDatagrams,
                ["shared_listener_capacity_drops"] = sharedSnapshot.Listener.CapacityDrops,
                ["shared_listener_reserved_bytes"] = sharedSnapshot.Listener.ReservedBytes,
            },
        };
    }

    private void PumpAll(int count)
    {
        for (int pass = 0; pass < count; pass++)
        {
            if (_sharedHost is not null) _sharedHost.Pump();
            else
                for (int index = 0; index < _bots.Length; index++) _bots[index].PairSession!.Pump();
        }
    }

    private void WaitForRealtimeTick(ref long nextTickDeadline)
    {
        nextTickDeadline += _realtimeTickIntervalStopwatchTicks;
        while (true)
        {
            long remaining = nextTickDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            if (remaining > Stopwatch.Frequency / 200)
            {
                int sleepMilliseconds = (int)Math.Max(
                    1,
                    ((remaining * 1000) / Stopwatch.Frequency) - 1);
                Thread.Sleep(sleepMilliseconds);
                continue;
            }
            Thread.SpinWait(64);
        }
    }

    private void DrainAll()
    {
        for (int index = 0; index < _bots.Length; index++)
        {
            _bots[index].ProcessServer(_scenario.TickHz);
            _bots[index].ProcessClient([], []);
        }
    }

    private OccupancyTick SamplePressure(
        ref int maxReliable,
        ref int maxUnreliable,
        ref int maxReceived,
        ref int maxInFlight)
    {
        int reliable = 0;
        int independent = 0;
        int received = 0;
        int inFlight = 0;
        for (int index = 0; index < _bots.Length; index++)
        {
            BenchmarkTransportSnapshot snapshot = _bots[index].GetTransportSnapshot();
            reliable = Math.Max(reliable,
                Math.Max(snapshot.Client.ReliableQueued, snapshot.Server.ReliableQueued));
            independent = Math.Max(independent,
                Math.Max(snapshot.Client.IndependentUnreliableQueued,
                    snapshot.Server.IndependentUnreliableQueued));
            received = Math.Max(received,
                Math.Max(snapshot.Client.ReceivedQueued, snapshot.Server.ReceivedQueued));
            inFlight = Math.Max(inFlight,
                Math.Max(snapshot.Client.InFlightPackets, snapshot.Server.InFlightPackets));
        }
        maxReliable = Math.Max(maxReliable, reliable);
        maxUnreliable = Math.Max(maxUnreliable, independent);
        maxReceived = Math.Max(maxReceived, received);
        maxInFlight = Math.Max(maxInFlight, inFlight);
        double maximum = Math.Max(
            Math.Max(reliable / (double)ProtocolLimits.MaximumReliableMessages,
                independent / (double)ProtocolLimits.MaximumIndependentUnreliableMessages),
            Math.Max(received / (double)ProtocolLimits.MaximumReceivedMessages,
                inFlight / (double)ProtocolLimits.MaximumCongestionWindowPackets));
        return new(reliable, independent, received, inFlight, maximum);
    }

    private IReadOnlyList<NetworkCanonicalOwnerState> CaptureCanonicalStates()
    {
        var rows = new NetworkCanonicalOwnerState[_bots.Length];
        for (int index = 0; index < _bots.Length; index++)
        {
            Bot bot = _bots[index];
            rows[index] = new(
                index,
                checked((uint)index + 1),
                bot.LastAppliedInput,
                BitConverter.SingleToUInt32Bits(bot.ClientX),
                BitConverter.SingleToUInt32Bits(bot.ClientY),
                BitConverter.SingleToUInt32Bits(bot.ClientVelocityX),
                BitConverter.SingleToUInt32Bits(bot.ClientVelocityY));
        }
        return rows;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int index = 0; index < _bots.Length; index++) _bots[index]?.Dispose();
        _sharedHost?.Dispose();
        _identity.Dispose();
    }

    private sealed class Bot : IDisposable
    {
        private readonly long[] _inputSentAt = new long[256];
        private readonly ushort[] _inputSentSequence = new ushort[256];
        private readonly long[] _pingSentAt = new long[256];
        private readonly ushort[] _pingSentSequence = new ushort[256];
        private readonly List<double> _inputAcknowledgements;
        private readonly List<double> _pingAcknowledgements;
        private ushort _nextInput = 1;
        private ushort _nextPing = 1;
        private ushort _serverInput;
        private float _serverX;
        private float _serverY;
        private float _serverVelocityX;
        private float _serverVelocityY;
        private bool _hasServerInput;

        internal Bot(
            int index,
            UdpProtocolSession? pairSession,
            SharedUdpProtocolSession? sharedSession,
            float x,
            float y,
            int expectedInputSamples)
        {
            if ((pairSession is null) == (sharedSession is null))
                throw new ArgumentException("Exactly one UDP topology session is required.");
            Index = index;
            PairSession = pairSession;
            SharedSession = sharedSession;
            Client = pairSession?.Client ?? sharedSession!.Client;
            Server = pairSession?.Server ?? sharedSession!.Server;
            _serverX = ClientX = x;
            _serverY = ClientY = y;
            _inputAcknowledgements = new List<double>(expectedInputSamples);
            _pingAcknowledgements = new List<double>(Math.Max(4, expectedInputSamples / 8));
        }

        internal int Index { get; }
        internal UdpProtocolSession? PairSession { get; }
        internal SharedUdpProtocolSession? SharedSession { get; }
        internal NetworkProtocolPeer Client { get; }
        internal NetworkProtocolPeer Server { get; }
        internal ushort LastAppliedInput { get; private set; }
        internal float ClientX { get; private set; }
        internal float ClientY { get; private set; }
        internal float ClientVelocityX { get; private set; }
        internal float ClientVelocityY { get; private set; }
        internal NetworkMetricSummary InputAcknowledgements => NetworkMetricSummary.From(_inputAcknowledgements);
        internal NetworkMetricSummary PingAcknowledgements => NetworkMetricSummary.From(_pingAcknowledgements);

        internal bool TryQueueInput(int tick)
        {
            ushort sequence = _nextInput;
            if (sequence == 0) return false;
            short x = checked((short)(40 + (tick + Index) % 40));
            short y = checked((short)(20 + (tick * 3 + Index) % 30));
            Span<byte> bytes = stackalloc byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[2..], checked((uint)tick));
            BinaryPrimitives.WriteInt16LittleEndian(bytes[6..], x);
            BinaryPrimitives.WriteInt16LittleEndian(bytes[8..], y);
            if (Client.TryQueue(InputDescriptor.Id, bytes) != NetworkQueueStatus.Accepted) return false;
            int slot = sequence & 255;
            _inputSentSequence[slot] = sequence;
            _inputSentAt[slot] = Stopwatch.GetTimestamp();
            _nextInput = sequence == ushort.MaxValue ? (ushort)1 : checked((ushort)(sequence + 1));
            return true;
        }

        internal bool TryQueuePing()
        {
            ushort sequence = _nextPing;
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, sequence);
            if (Client.TryQueue(PingDescriptor.Id, bytes) != NetworkQueueStatus.Accepted) return false;
            int slot = sequence & 255;
            _pingSentSequence[slot] = sequence;
            _pingSentAt[slot] = Stopwatch.GetTimestamp();
            _nextPing = sequence == ushort.MaxValue ? (ushort)1 : checked((ushort)(sequence + 1));
            return true;
        }

        internal void ProcessServer(int tickHz)
        {
            while (Server.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (message.MessageId == InputDescriptor.Id && payload.Length == 10)
                {
                    ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    if (SequenceNewer(sequence, _serverInput))
                    {
                        _serverInput = sequence;
                        _serverVelocityX = BinaryPrimitives.ReadInt16LittleEndian(payload[6..]) / 100f;
                        _serverVelocityY = BinaryPrimitives.ReadInt16LittleEndian(payload[8..]) / 100f;
                        _hasServerInput = true;
                    }
                }
                else if (message.MessageId == PingDescriptor.Id && payload.Length == 2)
                    _ = Server.TryQueue(PongDescriptor.Id, payload);
                _ = Server.TryConsumeReceived();
            }
        }

        internal void AdvanceAuthority(int tickHz)
        {
            if (!_hasServerInput) return;
            _serverX += _serverVelocityX / tickHz;
            _serverY += _serverVelocityY / tickHz;
        }

        internal bool QueueOwnerState(ulong serverTick)
        {
            Span<byte> bytes = stackalloc byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)Index + 1));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[4..], serverTick);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], _serverInput);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[14..], BitConverter.SingleToUInt32Bits(_serverX));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[18..], BitConverter.SingleToUInt32Bits(_serverY));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[22..], BitConverter.SingleToUInt32Bits(_serverVelocityX));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[26..], BitConverter.SingleToUInt32Bits(_serverVelocityY));
            return Server.TryQueue(OwnerDescriptor.Id, bytes) == NetworkQueueStatus.Accepted;
        }

        internal int ProcessClient(List<double> pingMs, List<double> ackMs)
        {
            int applied = 0;
            while (Client.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (message.MessageId == OwnerDescriptor.Id && payload.Length == 30)
                {
                    ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
                    if (SequenceNewerOrEqual(sequence, LastAppliedInput))
                    {
                        LastAppliedInput = sequence;
                        ClientX = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[14..]));
                        ClientY = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[18..]));
                        ClientVelocityX = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[22..]));
                        ClientVelocityY = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[26..]));
                        int slot = sequence & 255;
                        if (_inputSentSequence[slot] == sequence && _inputSentAt[slot] != 0)
                        {
                            double elapsed = Stopwatch.GetElapsedTime(_inputSentAt[slot]).TotalMilliseconds;
                            ackMs.Add(elapsed);
                            _inputAcknowledgements.Add(elapsed);
                            _inputSentAt[slot] = 0;
                        }
                        applied++;
                    }
                }
                else if (message.MessageId == PongDescriptor.Id && payload.Length == 2)
                {
                    ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    int slot = sequence & 255;
                    if (_pingSentSequence[slot] == sequence && _pingSentAt[slot] != 0)
                    {
                        double elapsed = Stopwatch.GetElapsedTime(_pingSentAt[slot]).TotalMilliseconds;
                        pingMs.Add(elapsed);
                        _pingAcknowledgements.Add(elapsed);
                        _pingSentAt[slot] = 0;
                    }
                }
                _ = Client.TryConsumeReceived();
            }
            return applied;
        }

        internal BenchmarkTransportSnapshot GetTransportSnapshot()
        {
            if (PairSession is not null)
            {
                UdpProtocolSessionSnapshot value = PairSession.GetSnapshot();
                return new(value.Client, value.Server, value.ClientDatagrams,
                    value.ServerDatagrams, value.ConfiguredSocketBufferBytes);
            }
            SharedUdpProtocolSessionSnapshot shared = SharedSession!.GetSnapshot();
            return new(shared.Client, shared.Server, shared.ClientDatagrams,
                shared.ServerDatagrams, 0);
        }

        public void Dispose() => PairSession?.Dispose();

        private static bool SequenceNewer(ushort candidate, ushort current) =>
            current == 0 || unchecked((short)(candidate - current)) > 0;

        private static bool SequenceNewerOrEqual(ushort candidate, ushort current) =>
            current == 0 || unchecked((short)(candidate - current)) >= 0;
    }
}

internal enum V3UdpTopology : byte
{
    ConnectedPairs = 0,
    SharedListener = 1,
}

internal readonly record struct BenchmarkTransportSnapshot(
    NetworkProtocolSnapshot Client,
    NetworkProtocolSnapshot Server,
    UdpDatagramEndpointSnapshot ClientDatagrams,
    UdpDatagramEndpointSnapshot ServerDatagrams,
    long ConfiguredSocketBufferBytes);

internal readonly record struct OccupancyTick(
    int Reliable,
    int Independent,
    int Received,
    int InFlight,
    double MaximumRatio);

internal sealed record AdapterOptions(
    string Output,
    string Scenario,
    int Clients,
    int TickHz,
    int InputHz,
    int StageTicks,
    int SettleTicks,
    double SpawnSpacing,
    V3UdpTopology Topology)
{
    internal static AdapterOptions Parse(string[] args) => new(
        Get(args, "--output=") ?? "v3-network-bot-comparison.json",
        Get(args, "--scenario=") ?? "owner_movement",
        ParseInt(Get(args, "--clients="), 32),
        ParseInt(Get(args, "--tick-hz="), 60),
        ParseInt(Get(args, "--input-hz="), 20),
        ParseInt(Get(args, "--stage-ticks="), 120),
        ParseInt(Get(args, "--settle-ticks="), 30),
        ParseDouble(Get(args, "--spawn-spacing="), 12),
        ParseTopology(Get(args, "--topology=")));

    internal NetworkBotScenario CreateScenario() => new NetworkBotScenario(
        Scenario, Clients, TickHz, InputHz, StageTicks, SettleTicks,
        SpawnSpacing,
        ObserverState: false,
        OrderedEvents: false).Validate();

    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    private static V3UdpTopology ParseTopology(string? value) => value?.ToLowerInvariant() switch
    {
        null or "connected-pairs" => V3UdpTopology.ConnectedPairs,
        "shared-listener" => V3UdpTopology.SharedListener,
        _ => throw new ArgumentException($"Unsupported V3 UDP topology '{value}'."),
    };
}
