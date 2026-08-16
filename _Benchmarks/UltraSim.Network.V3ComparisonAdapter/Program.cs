using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using UltraSim.Network;
using UltraSim.Network.Comparison;

AdapterOptions options = AdapterOptions.Parse(args);
NetworkBotScenario scenario = options.CreateScenario();
if (options.DiagnosticSuite)
{
    RunDiagnosticSuite(options, scenario);
    return;
}
using var harness = new V3OwnerMovementHarness(
    scenario, options.Topology,
    new V3HarnessProfile(options.Layer, options.ActiveRatio, options.DeepTiming),
    options.SharedSocketIoMode);
NetworkBotStageResult result = harness.Run();
NetworkBotComparisonIo.Write(options.Output, NetworkBotAdapterReport.Create(result.Generation, result));
Console.WriteLine($"{result.Generation} {scenario.Id}/{scenario.Clients}: tick p95 {result.TickMilliseconds.P95:F3} ms, " +
    $"ping p95 {result.PingMilliseconds.P95:F3} ms, reserved {result.ReservedNetworkBytes / 1048576d:F2} MiB");
Console.WriteLine(Path.GetFullPath(options.Output));

static void RunDiagnosticSuite(AdapterOptions options, NetworkBotScenario scenario)
{
    V3PeerCostCase[] cases =
    [
        new("idle", V3DiagnosticLayer.Idle, 0),
        new("full-active-10", V3DiagnosticLayer.Full, 0.10),
        new("full-active-35", V3DiagnosticLayer.Full, 0.35),
        new("control", V3DiagnosticLayer.Control, 1),
        new("input", V3DiagnosticLayer.Input, 1),
        new("input-owner", V3DiagnosticLayer.InputOwner, 1),
        new("full", V3DiagnosticLayer.Full, 1),
    ];
    var observations = new List<V3PeerCostObservation>(cases.Length * 2);
    for (int pass = 0; pass < 2; pass++)
    {
        IEnumerable<V3PeerCostCase> ordered = pass == 0 ? cases : cases.Reverse();
        int order = 0;
        foreach (V3PeerCostCase item in ordered)
        {
            using var diagnostic = new V3OwnerMovementHarness(
                scenario, options.Topology,
                new V3HarnessProfile(item.Layer, item.ActiveRatio, DeepTiming: true),
                options.SharedSocketIoMode);
            NetworkBotStageResult measured = diagnostic.Run();
            observations.Add(new(pass == 0 ? "forward" : "reverse", order++, item, measured));
            Console.WriteLine($"DIAGNOSTIC {item.Id}/{(pass == 0 ? "forward" : "reverse")}: " +
                $"tick p95 {measured.TickMilliseconds.P95:F3} ms, " +
                $"server p95 {measured.ServerPumpMilliseconds.P95:F3} ms, " +
                $"CPU {measured.ProcessCpuCoreUtilization:F3} cores");
        }
    }
    V3PeerCostSummary[] summaries = cases.Select(item =>
    {
        V3PeerCostObservation[] values = observations.Where(value => value.Case == item).ToArray();
        string canonicalHash = values[0].Result.CanonicalOwnerStateHash;
        bool canonicalMatches = values.All(value =>
            StringComparer.Ordinal.Equals(value.Result.CanonicalOwnerStateHash, canonicalHash));
        if (!canonicalMatches)
            throw new InvalidOperationException(
                $"Diagnostic case '{item.Id}' produced different canonical results across case orders.");
        return new V3PeerCostSummary(
            item.Id,
            item.Layer,
            item.ActiveRatio,
            checked((int)Math.Round(scenario.Clients * item.ActiveRatio,
                MidpointRounding.AwayFromZero)),
            values.Average(value => value.Result.TickMilliseconds.P95),
            values.Min(value => value.Result.TickMilliseconds.P95),
            values.Max(value => value.Result.TickMilliseconds.P95),
            values.Average(value => value.Result.ServerPumpMilliseconds.P95),
            values.Average(value => value.Result.ClientPumpMilliseconds.P95),
            values.Average(value => value.Result.ProcessCpuCoreUtilization),
            values.Average(value => (double)value.Result.WireBytesSent),
            values.Average(value => (double)value.Result.SocketSendCalls),
            canonicalMatches,
            canonicalHash);
    }).ToArray();
    var report = new V3PeerCostDiagnosticReport(
        "ultrasim-network-v3-peer-cost-attribution-v1",
        DateTimeOffset.UtcNow,
        scenario,
        options.Topology,
        "Two passes in forward/reverse case order. All cases retain the same established secure connection population and capacities; only admitted application work differs.",
        summaries,
        observations);
    NetworkBotComparisonIo.Write(options.Output, report);
    Console.WriteLine(Path.GetFullPath(options.Output));
}

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
    private readonly SharedUdpSocketIoMode _sharedSocketIoMode;
    private readonly SharedUdpProtocolHost? _sharedHost;
    private readonly V3HarnessProfile _profile;
    private readonly int _activeBots;
    private readonly long _realtimeTickIntervalStopwatchTicks;
    private readonly List<double> _handshakeMilliseconds;
    private BatchOpportunityCensus _batchCensus;
    private long _handshakeAttempts;
    private long _handshakeSuccesses;
    private long _handshakeAllocatedBytes;
    private ulong _handshakeWireDatagrams;
    private ulong _handshakeWireBytes;
    private bool _disposed;

    internal V3OwnerMovementHarness(
        NetworkBotScenario scenario,
        V3UdpTopology topology,
        V3HarnessProfile profile,
        SharedUdpSocketIoMode sharedSocketIoMode = SharedUdpSocketIoMode.Scalar)
    {
        _scenario = scenario.Validate();
        _topology = topology;
        _sharedSocketIoMode = sharedSocketIoMode;
        _profile = profile.Validate();
        _activeBots = checked((int)Math.Round(
            scenario.Clients * profile.ActiveRatio, MidpointRounding.AwayFromZero));
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
                    MaximumInboundDatagramsPerPump: checked(scenario.Clients * 16),
                    SocketIoMode: sharedSocketIoMode,
                    MaximumNativeSendBatchDatagrams: 64),
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
        if (_profile.DeepTiming) WarmDiagnosticProfile();
    }

    private void WarmDiagnosticProfile()
    {
        int inputInterval = _scenario.TickHz / _scenario.InputHz;
        for (int tick = 0; tick < 8; tick++)
        {
            for (int index = 0; index < _activeBots; index++)
            {
                if (_profile.SendsInput && tick % inputInterval == index % inputInterval)
                    _ = _bots[index].TryQueueInput(tick);
                if (_profile.SendsControl && tick % _scenario.TickHz == index % _scenario.TickHz)
                    _ = _bots[index].TryQueuePing();
            }
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].ProcessServer(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].AdvanceAuthority(_scenario.TickHz);
            for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                _ = _bots[index].QueueOwnerState(checked((ulong)tick + 1));
            PumpAll(2);
            for (int index = 0; index < _bots.Length; index++)
                _ = _bots[index].ProcessClient([], []);
            PumpAll(1);
        }
        for (int index = 0; index < _bots.Length; index++)
            _bots[index].ResetMeasurementHistory();
    }

    internal NetworkBotStageResult Run()
    {
        _batchCensus = default;
        if (_profile.DeepTiming) ResetProtocolStageTiming(enabled: true);
        DiagnosticCounterBaseline diagnosticBaseline = _profile.DeepTiming
            ? CaptureDiagnosticCounterBaseline()
            : default;
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
        List<double>? intentQueueMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? serverIngressMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? authorityMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? ownerQueueMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? forwardPumpMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? clientApplyMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? returnPumpMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
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
            long phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            for (int index = 0; index < _activeBots; index++)
            {
                Bot bot = _bots[index];
                if (_profile.SendsInput && tick % inputInterval == index % inputInterval &&
                    bot.TryQueueInput(tick))
                    logicalInputs++;
                if (_profile.SendsControl && tick % _scenario.TickHz == index % _scenario.TickHz)
                    bot.TryQueuePing();
            }
            RecordPhase(intentQueueMs, phaseStarted);

            long tickStart = Stopwatch.GetTimestamp();
            long serverStart = tickStart;
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].ProcessServer(_scenario.TickHz);
            RecordPhase(serverIngressMs, phaseStarted);
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].AdvanceAuthority(_scenario.TickHz);
            RecordPhase(authorityMs, phaseStarted);
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                if (_bots[index].QueueOwnerState(checked((ulong)tick + 1)))
                    ownerMessagesQueued++;
            RecordPhase(ownerQueueMs, phaseStarted);
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            PumpAll(2);
            RecordPhase(forwardPumpMs, phaseStarted);
            long serverEnd = Stopwatch.GetTimestamp();
            long clientStart = Stopwatch.GetTimestamp();
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
            RecordPhase(clientApplyMs, phaseStarted);
            phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            PumpAll(1);
            RecordPhase(returnPumpMs, phaseStarted);
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
            for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                _bots[index].QueueOwnerState(
                    checked((ulong)_scenario.StageTicks + (ulong)settle + 1));
            PumpAll(2);
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
            PumpAll(1);
        }

        ProtocolStageTimingSides protocolStageTiming = _profile.DeepTiming
            ? CaptureProtocolStageTiming()
            : default;
        if (_profile.DeepTiming) ResetProtocolStageTiming(enabled: false);

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
        ulong listenerSendCalls = 0;
        ulong listenerReceivePollCalls = 0;
        ulong listenerReceiveCalls = 0;
        ulong listenerNativeSendCalls = 0;
        ulong listenerNativeSentDatagrams = 0;
        if (_sharedHost is not null)
        {
            sharedSnapshot = _sharedHost.GetSnapshot();
            listenerSendCalls = sharedSnapshot.Listener.SendCalls;
            listenerReceivePollCalls = sharedSnapshot.Listener.ReceivePollCalls;
            listenerReceiveCalls = sharedSnapshot.Listener.ReceiveCalls;
            listenerNativeSendCalls = sharedSnapshot.Listener.NativeSendCalls;
            listenerNativeSentDatagrams = sharedSnapshot.Listener.NativeSentDatagrams;
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
        if (_profile.DeepTiming)
        {
            datagrams -= diagnosticBaseline.Datagrams;
            bytes -= diagnosticBaseline.Bytes;
            retransmissions -= diagnosticBaseline.Retransmissions;
            retransmittedWireBytes -= diagnosticBaseline.RetransmittedWireBytes;
            dataPacketsSent -= diagnosticBaseline.DataPacketsSent;
            acknowledgementPacketsSent -= diagnosticBaseline.AcknowledgementPacketsSent;
            keepAlivePacketsSent -= diagnosticBaseline.KeepAlivePacketsSent;
            encryptedPlaintextBytes -= diagnosticBaseline.EncryptedPlaintextBytes;
            decryptedPlaintextBytes -= diagnosticBaseline.DecryptedPlaintextBytes;
            reliableFragmentsSent -= diagnosticBaseline.ReliableFragmentsSent;
            authenticationFailures -= diagnosticBaseline.AuthenticationFailures;
            replayRejections -= diagnosticBaseline.ReplayRejections;
            socketSendCalls -= diagnosticBaseline.SocketSendCalls;
            socketReceivePollCalls -= diagnosticBaseline.SocketReceivePollCalls;
            socketReceiveCalls -= diagnosticBaseline.SocketReceiveCalls;
            socketWouldBlockEvents -= diagnosticBaseline.SocketWouldBlockEvents;
            listenerSendCalls -= diagnosticBaseline.ListenerSendCalls;
            listenerReceivePollCalls -= diagnosticBaseline.ListenerReceivePollCalls;
            listenerReceiveCalls -= diagnosticBaseline.ListenerReceiveCalls;
            listenerNativeSendCalls -= diagnosticBaseline.ListenerNativeSendCalls;
            listenerNativeSentDatagrams -= diagnosticBaseline.ListenerNativeSentDatagrams;
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
        ulong steadyWireBytes = _profile.DeepTiming
            ? bytes
            : bytes >= _handshakeWireBytes ? bytes - _handshakeWireBytes : 0;
        var result = new NetworkBotStageResult
        {
            Generation = (_topology == V3UdpTopology.SharedListener
                ? _sharedSocketIoMode == SharedUdpSocketIoMode.PlatformBatch
                    ? "V3-shared-listener-native-send-batch"
                    : "V3-shared-listener"
                : "V3-connected-pairs") +
                (_profile.IsComparisonDefault ? string.Empty : $"-{_profile.Layer}-{_activeBots}"),
            Scenario = _scenario,
            EvidenceProfile = new(
                _profile.IsComparisonDefault
                    ? "network-bot-owner-movement-v1"
                    : "network-v3-peer-cost-diagnostic-v1",
                _profile.IsComparisonDefault
                    ? "Deterministic phased owner input, authoritative latest control, owner correction, and ping; arrival-driven position remains diagnostic."
                    : _profile.DescribeSemantics(),
                "workload bounds fixed; implementation reservations reported separately",
                $"fixed {_scenario.TickHz} Hz; {_scenario.InputHz} Hz phased input; realtime",
                _topology == V3UdpTopology.SharedListener
                    ? _sharedSocketIoMode == SharedUdpSocketIoMode.PlatformBatch
                        ? "one shared server UDP listener with bounded platform send batching, one client socket and two secure protocol peers per bot"
                        : "one shared server UDP listener, one client socket and two secure protocol peers per bot"
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
                OwnerInputFreshness: _profile.SendsInput && _profile.SendsOwner,
                WorldStateFreshnessByTier: false,
                ApplicationEventLatency: false,
                PerClientFairness: _profile.SendsControl ||
                    (_profile.SendsInput && _profile.SendsOwner),
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
                DeepPhaseTiming: _profile.DeepTiming),
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
            CanonicalValidation = _profile.IsComparisonDefault
                ? "Exact client-applied owner identity, acknowledged input sequence, and velocity. Position remains diagnostic because V2 integrates arrival-relative input."
                : "V3-only causal diagnostic; canonical state is retained for repeatability but no V2 semantic ratio is claimed.",
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
                ["shared_listener_uses_native_send_batching"] =
                    sharedSnapshot.Listener.UsesNativeSendBatching ? 1 : 0,
                ["shared_listener_send_calls"] = listenerSendCalls,
                ["shared_listener_receive_poll_calls"] = listenerReceivePollCalls,
                ["shared_listener_receive_calls"] = listenerReceiveCalls,
                ["non_listener_endpoint_send_calls"] = socketSendCalls >= listenerSendCalls
                    ? socketSendCalls - listenerSendCalls
                    : 0,
                ["non_listener_endpoint_receive_calls"] = socketReceiveCalls >= listenerReceiveCalls
                    ? socketReceiveCalls - listenerReceiveCalls
                    : 0,
                ["client_endpoint_metrics_measured"] = _sharedHost is null ? 0 : 1,
                ["client_endpoint_send_calls"] = _sharedHost is null ||
                    socketSendCalls < listenerSendCalls
                        ? 0
                        : socketSendCalls - listenerSendCalls,
                ["client_endpoint_receive_calls"] = _sharedHost is null ||
                    socketReceiveCalls < listenerReceiveCalls
                        ? 0
                        : socketReceiveCalls - listenerReceiveCalls,
                ["shared_listener_native_send_calls"] = listenerNativeSendCalls,
                ["shared_listener_native_sent_datagrams"] = listenerNativeSentDatagrams,
                ["shared_listener_datagrams_per_native_send_call"] = listenerNativeSendCalls == 0
                    ? 0
                    : listenerNativeSentDatagrams / (double)listenerNativeSendCalls,
                ["shared_listener_native_send_batch_p50"] =
                    sharedSnapshot.Listener.NativeSendBatchP50,
                ["shared_listener_native_send_batch_p95"] =
                    sharedSnapshot.Listener.NativeSendBatchP95,
                ["shared_listener_native_send_batch_max"] =
                    sharedSnapshot.Listener.MaximumNativeSendBatch,
                ["shared_listener_partial_native_send_calls"] =
                    sharedSnapshot.Listener.PartialNativeSendCalls,
                ["shared_listener_native_send_would_block"] =
                    sharedSnapshot.Listener.NativeSendWouldBlockEvents,
                ["shared_listener_maximum_outbound_queued"] =
                    sharedSnapshot.Listener.MaximumOutboundQueuedDatagrams,
                ["diagnostic_active_clients"] = _activeBots,
                ["diagnostic_active_ratio"] = _profile.ActiveRatio,
            },
        };
        if (_profile.DeepTiming)
        {
            var diagnostics = (Dictionary<string, double>)result.Diagnostics;
            AddPhaseDiagnostics(diagnostics, "client_intent_queue", intentQueueMs!);
            AddPhaseDiagnostics(diagnostics, "server_application_ingress", serverIngressMs!);
            AddPhaseDiagnostics(diagnostics, "authority_advance", authorityMs!);
            AddPhaseDiagnostics(diagnostics, "owner_state_queue", ownerQueueMs!);
            AddPhaseDiagnostics(diagnostics, "forward_protocol_pump", forwardPumpMs!);
            AddPhaseDiagnostics(diagnostics, "client_application_apply", clientApplyMs!);
            AddPhaseDiagnostics(diagnostics, "return_protocol_pump", returnPumpMs!);
            AddBatchOpportunityDiagnostics(diagnostics, dataPacketsSent, reliableFragmentsSent);
            AddProtocolStageTimingDiagnostics(diagnostics, protocolStageTiming, dataPacketsSent);
        }
        return result;
    }

    private static void RecordPhase(List<double>? values, long started)
    {
        if (values is not null)
            values.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static void AddPhaseDiagnostics(
        Dictionary<string, double> diagnostics,
        string key,
        IReadOnlyList<double> values)
    {
        NetworkMetricSummary summary = NetworkMetricSummary.From(values);
        diagnostics[key + "_mean_ms"] = summary.Mean;
        diagnostics[key + "_p95_ms"] = summary.P95;
        diagnostics[key + "_max_ms"] = summary.Max;
    }

    private void PumpAll(int count)
    {
        for (int pass = 0; pass < count; pass++)
        {
            if (_profile.DeepTiming) CaptureBatchOpportunities();
            if (_sharedHost is not null) _sharedHost.Pump();
            else
                for (int index = 0; index < _bots.Length; index++) _bots[index].PairSession!.Pump();
        }
    }

    private void CaptureBatchOpportunities()
    {
        for (int index = 0; index < _bots.Length; index++)
        {
            _batchCensus.Add(_bots[index].Client.InspectUnreliableBatchOpportunity());
            _batchCensus.Add(_bots[index].Server.InspectUnreliableBatchOpportunity());
        }
    }

    private void ResetProtocolStageTiming(bool enabled)
    {
        for (int index = 0; index < _bots.Length; index++)
        {
            _bots[index].Client.ResetStageTiming(enabled);
            _bots[index].Server.ResetStageTiming(enabled);
        }
        _sharedHost?.ResetListenerStageTiming(enabled);
    }

    private ProtocolStageTimingSides CaptureProtocolStageTiming()
    {
        ProtocolStageTimingTotal client = default;
        ProtocolStageTimingTotal server = default;
        for (int index = 0; index < _bots.Length; index++)
        {
            client.Add(_bots[index].Client.GetStageTiming());
            server.Add(_bots[index].Server.GetStageTiming());
        }
        SharedUdpListenerSnapshot listener = _sharedHost?.GetSnapshot().Listener ?? default;
        return new(client, server, new(
            listener.SendSystemCallTicks,
            listener.ReceivePollTicks,
            listener.ReceiveSystemCallTicks));
    }

    private static void AddProtocolStageTimingDiagnostics(
        Dictionary<string, double> diagnostics,
        ProtocolStageTimingSides timing,
        ulong dataPackets)
    {
        double ToMilliseconds(ulong ticks) => ticks * 1000d / Stopwatch.Frequency;
        double ToMicrosecondsPerDataPacket(ulong ticks) => dataPackets == 0
            ? 0
            : ticks * 1_000_000d / Stopwatch.Frequency / dataPackets;
        ProtocolStageTimingTotal total = timing.Client;
        total.Add(timing.Server);
        AddPeer(string.Empty, total);
        AddPeer("client_peer_", timing.Client);
        AddPeer("server_peer_", timing.Server);
        Add("listener_socket_send", timing.Listener.SendSystemCallTicks);
        Add("listener_socket_receive_poll", timing.Listener.ReceivePollTicks);
        Add("listener_socket_receive", timing.Listener.ReceiveSystemCallTicks);

        void AddPeer(string prefix, ProtocolStageTimingTotal value)
        {
            ulong attributedEmit = SaturatingAdd(
                SaturatingAdd(value.SelectionTicks, value.FrameEncodeTicks),
                SaturatingAdd(value.SecureSendTicks, value.EndpointSendTicks));
            ulong attributedInbound = SaturatingAdd(
                value.EndpointReceiveTicks,
                SaturatingAdd(value.SecureReceiveTicks, value.FrameAdmissionTicks));
            ulong emitResidual = value.EmitTicks >= attributedEmit
                ? value.EmitTicks - attributedEmit
                : 0;
            ulong inboundResidual = value.InboundTicks >= attributedInbound
                ? value.InboundTicks - attributedInbound
                : 0;

            Add(prefix + "emit_total", value.EmitTicks);
            Add(prefix + "select_schedule", value.SelectionTicks);
            Add(prefix + "frame_encode", value.FrameEncodeTicks);
            Add(prefix + "secure_frame_encrypt", value.SecureSendTicks);
            Add(prefix + "endpoint_send", value.EndpointSendTicks);
            Add(prefix + "emit_residual", emitResidual);
            Add(prefix + "inbound_total", value.InboundTicks);
            Add(prefix + "endpoint_receive", value.EndpointReceiveTicks);
            Add(prefix + "secure_parse_decrypt", value.SecureReceiveTicks);
            Add(prefix + "frame_decode_admission", value.FrameAdmissionTicks);
            Add(prefix + "inbound_residual", inboundResidual);
        }

        void Add(string key, ulong ticks)
        {
            diagnostics["protocol_stage_" + key + "_total_ms"] = ToMilliseconds(ticks);
            diagnostics["protocol_stage_" + key + "_us_per_data_packet"] =
                ToMicrosecondsPerDataPacket(ticks);
        }
    }

    private void AddBatchOpportunityDiagnostics(
        Dictionary<string, double> diagnostics,
        ulong dataPacketsSent,
        ulong reliableFragmentsSent)
    {
        ulong unreliablePackets = dataPacketsSent >= reliableFragmentsSent
            ? dataPacketsSent - reliableFragmentsSent
            : 0;
        diagnostics["batch_peer_emit_opportunities_inspected"] = _batchCensus.Inspections;
        diagnostics["batch_opportunities_with_unreliable"] = _batchCensus.WithReadyFrames;
        diagnostics["batch_unreliable_frames_ready_observed"] = _batchCensus.ReadyFrames;
        diagnostics["batch_unreliable_plaintext_bytes_observed"] = _batchCensus.PlaintextBytes;
        diagnostics["batch_co_batchable_frames_observed"] = _batchCensus.CoBatchableFrames;
        diagnostics["batch_co_batchable_plaintext_bytes_observed"] =
            _batchCensus.CoBatchablePlaintextBytes;
        diagnostics["batch_opportunities_2_plus"] = _batchCensus.TwoPlus;
        diagnostics["batch_opportunities_3_plus"] = _batchCensus.ThreePlus;
        diagnostics["batch_opportunities_4_plus"] = _batchCensus.FourPlus;
        diagnostics["batch_maximum_ready_frames"] = _batchCensus.MaximumReadyFrames;
        diagnostics["batch_greedy_datagrams_observed"] = _batchCensus.GreedyDatagrams;
        diagnostics["actual_unreliable_data_packets"] = unreliablePackets;
        diagnostics["batch_mean_frames_when_nonempty"] = _batchCensus.WithReadyFrames == 0
            ? 0
            : _batchCensus.ReadyFrames / (double)_batchCensus.WithReadyFrames;
        diagnostics["batch_multi_frame_opportunity_percent"] = _batchCensus.WithReadyFrames == 0
            ? 0
            : _batchCensus.TwoPlus * 100d / _batchCensus.WithReadyFrames;
    }

    private DiagnosticCounterBaseline CaptureDiagnosticCounterBaseline()
    {
        DiagnosticCounterBaseline result = default;
        for (int index = 0; index < _bots.Length; index++)
        {
            BenchmarkTransportSnapshot snapshot = _bots[index].GetTransportSnapshot();
            result = result.Add(snapshot);
        }
        if (_sharedHost is not null)
            result = result.Add(_sharedHost.GetSnapshot().Listener);
        return result;
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

        internal void ResetMeasurementHistory()
        {
            _inputAcknowledgements.Clear();
            _pingAcknowledgements.Clear();
            Array.Clear(_inputSentAt);
            Array.Clear(_inputSentSequence);
            Array.Clear(_pingSentAt);
            Array.Clear(_pingSentSequence);
        }

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

internal enum V3DiagnosticLayer : byte
{
    Idle = 0,
    Control = 1,
    Input = 2,
    InputOwner = 3,
    Full = 4,
}

internal readonly record struct V3HarnessProfile(
    V3DiagnosticLayer Layer,
    double ActiveRatio,
    bool DeepTiming)
{
    internal bool SendsControl => Layer is V3DiagnosticLayer.Control or V3DiagnosticLayer.Full;
    internal bool SendsInput => Layer is V3DiagnosticLayer.Input or
        V3DiagnosticLayer.InputOwner or V3DiagnosticLayer.Full;
    internal bool SendsOwner => Layer is V3DiagnosticLayer.InputOwner or V3DiagnosticLayer.Full;
    internal bool IsComparisonDefault => Layer == V3DiagnosticLayer.Full &&
        ActiveRatio == 1 && !DeepTiming;

    internal V3HarnessProfile Validate()
    {
        if (!Enum.IsDefined(Layer)) throw new ArgumentOutOfRangeException(nameof(Layer));
        if (!double.IsFinite(ActiveRatio) || ActiveRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ActiveRatio));
        return this;
    }

    internal string DescribeSemantics() =>
        $"V3 causal peer-cost profile: layer={Layer}; active_ratio={ActiveRatio:R}; " +
        "all connections remain established and all peer pumps retain the same bounds. " +
        "No cross-generation semantic ratio is claimed.";
}

internal sealed record V3PeerCostCase(
    string Id,
    V3DiagnosticLayer Layer,
    double ActiveRatio);

internal sealed record V3PeerCostObservation(
    string Pass,
    int Order,
    V3PeerCostCase Case,
    NetworkBotStageResult Result);

internal sealed record V3PeerCostSummary(
    string Id,
    V3DiagnosticLayer Layer,
    double ActiveRatio,
    int ActiveClients,
    double TickP95MeanMilliseconds,
    double TickP95MinimumMilliseconds,
    double TickP95MaximumMilliseconds,
    double ServerP95MeanMilliseconds,
    double ClientP95MeanMilliseconds,
    double ProcessCpuCoreUtilizationMean,
    double WireBytesMean,
    double SocketSendCallsMean,
    bool CanonicalStateMatchesAcrossPasses,
    string CanonicalStateHash);

internal sealed record V3PeerCostDiagnosticReport(
    string ProtocolVersion,
    DateTimeOffset TimestampUtc,
    NetworkBotScenario Scenario,
    V3UdpTopology Topology,
    string Method,
    IReadOnlyList<V3PeerCostSummary> Summaries,
    IReadOnlyList<V3PeerCostObservation> Observations);

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

internal struct BatchOpportunityCensus
{
    internal ulong Inspections;
    internal ulong WithReadyFrames;
    internal ulong ReadyFrames;
    internal ulong PlaintextBytes;
    internal ulong CoBatchableFrames;
    internal ulong CoBatchablePlaintextBytes;
    internal ulong TwoPlus;
    internal ulong ThreePlus;
    internal ulong FourPlus;
    internal ulong GreedyDatagrams;
    internal int MaximumReadyFrames;

    internal void Add(NetworkUnreliableBatchOpportunity opportunity)
    {
        Inspections = checked(Inspections + 1);
        if (opportunity.ReadyFrames == 0) return;
        WithReadyFrames = checked(WithReadyFrames + 1);
        ReadyFrames = checked(ReadyFrames + (ulong)opportunity.ReadyFrames);
        PlaintextBytes = checked(PlaintextBytes + (ulong)opportunity.PlaintextBytes);
        GreedyDatagrams = checked(GreedyDatagrams + (ulong)opportunity.GreedyDatagrams);
        MaximumReadyFrames = Math.Max(MaximumReadyFrames, opportunity.ReadyFrames);
        if (opportunity.ReadyFrames < 2) return;
        TwoPlus = checked(TwoPlus + 1);
        if (opportunity.ReadyFrames >= 3) ThreePlus = checked(ThreePlus + 1);
        if (opportunity.ReadyFrames >= 4) FourPlus = checked(FourPlus + 1);
        CoBatchableFrames = checked(CoBatchableFrames + (ulong)opportunity.ReadyFrames);
        CoBatchablePlaintextBytes = checked(
            CoBatchablePlaintextBytes + (ulong)opportunity.PlaintextBytes);
    }
}

internal struct ProtocolStageTimingTotal
{
    internal ulong EmitTicks;
    internal ulong SelectionTicks;
    internal ulong FrameEncodeTicks;
    internal ulong SecureSendTicks;
    internal ulong EndpointSendTicks;
    internal ulong InboundTicks;
    internal ulong EndpointReceiveTicks;
    internal ulong SecureReceiveTicks;
    internal ulong FrameAdmissionTicks;

    internal void Add(NetworkProtocolStageTiming value)
    {
        EmitTicks = Add(EmitTicks, value.EmitTicks);
        SelectionTicks = Add(SelectionTicks, value.SelectionTicks);
        FrameEncodeTicks = Add(FrameEncodeTicks, value.FrameEncodeTicks);
        SecureSendTicks = Add(SecureSendTicks, value.SecureSendTicks);
        EndpointSendTicks = Add(EndpointSendTicks, value.EndpointSendTicks);
        InboundTicks = Add(InboundTicks, value.InboundTicks);
        EndpointReceiveTicks = Add(EndpointReceiveTicks, value.EndpointReceiveTicks);
        SecureReceiveTicks = Add(SecureReceiveTicks, value.SecureReceiveTicks);
        FrameAdmissionTicks = Add(FrameAdmissionTicks, value.FrameAdmissionTicks);
    }

    internal void Add(ProtocolStageTimingTotal value)
    {
        EmitTicks = Add(EmitTicks, value.EmitTicks);
        SelectionTicks = Add(SelectionTicks, value.SelectionTicks);
        FrameEncodeTicks = Add(FrameEncodeTicks, value.FrameEncodeTicks);
        SecureSendTicks = Add(SecureSendTicks, value.SecureSendTicks);
        EndpointSendTicks = Add(EndpointSendTicks, value.EndpointSendTicks);
        InboundTicks = Add(InboundTicks, value.InboundTicks);
        EndpointReceiveTicks = Add(EndpointReceiveTicks, value.EndpointReceiveTicks);
        SecureReceiveTicks = Add(SecureReceiveTicks, value.SecureReceiveTicks);
        FrameAdmissionTicks = Add(FrameAdmissionTicks, value.FrameAdmissionTicks);
    }

    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal readonly record struct SharedListenerStageTiming(
    ulong SendSystemCallTicks,
    ulong ReceivePollTicks,
    ulong ReceiveSystemCallTicks);

internal readonly record struct ProtocolStageTimingSides(
    ProtocolStageTimingTotal Client,
    ProtocolStageTimingTotal Server,
    SharedListenerStageTiming Listener);

internal readonly record struct DiagnosticCounterBaseline(
    ulong Datagrams,
    ulong Bytes,
    ulong Retransmissions,
    ulong RetransmittedWireBytes,
    ulong DataPacketsSent,
    ulong AcknowledgementPacketsSent,
    ulong KeepAlivePacketsSent,
    ulong EncryptedPlaintextBytes,
    ulong DecryptedPlaintextBytes,
    ulong ReliableFragmentsSent,
    ulong AuthenticationFailures,
    ulong ReplayRejections,
    ulong SocketSendCalls,
    ulong SocketReceivePollCalls,
    ulong SocketReceiveCalls,
    ulong SocketWouldBlockEvents,
    ulong ListenerSendCalls,
    ulong ListenerReceivePollCalls,
    ulong ListenerReceiveCalls,
    ulong ListenerNativeSendCalls,
    ulong ListenerNativeSentDatagrams)
{
    internal DiagnosticCounterBaseline Add(BenchmarkTransportSnapshot value) => this with
    {
        Datagrams = checked(Datagrams + value.ClientDatagrams.Sent + value.ServerDatagrams.Sent),
        Bytes = checked(Bytes + value.ClientDatagrams.SentBytes + value.ServerDatagrams.SentBytes),
        Retransmissions = checked(Retransmissions + value.Client.ReliableRetransmissions +
            value.Server.ReliableRetransmissions),
        RetransmittedWireBytes = checked(RetransmittedWireBytes +
            value.Client.ReliableRetransmittedWireBytes + value.Server.ReliableRetransmittedWireBytes),
        DataPacketsSent = checked(DataPacketsSent + value.Client.DataPacketsSent + value.Server.DataPacketsSent),
        AcknowledgementPacketsSent = checked(AcknowledgementPacketsSent +
            value.Client.AcknowledgementPacketsSent + value.Server.AcknowledgementPacketsSent),
        KeepAlivePacketsSent = checked(KeepAlivePacketsSent + value.Client.KeepAlivePacketsSent +
            value.Server.KeepAlivePacketsSent),
        EncryptedPlaintextBytes = checked(EncryptedPlaintextBytes + value.Client.EncryptedPlaintextBytes +
            value.Server.EncryptedPlaintextBytes),
        DecryptedPlaintextBytes = checked(DecryptedPlaintextBytes + value.Client.DecryptedPlaintextBytes +
            value.Server.DecryptedPlaintextBytes),
        ReliableFragmentsSent = checked(ReliableFragmentsSent + value.Client.ReliableFragmentsSent +
            value.Server.ReliableFragmentsSent),
        AuthenticationFailures = checked(AuthenticationFailures + value.Client.AuthenticationFailures +
            value.Server.AuthenticationFailures),
        ReplayRejections = checked(ReplayRejections + value.Client.ReplayRejections +
            value.Server.ReplayRejections),
        SocketSendCalls = checked(SocketSendCalls + value.ClientDatagrams.SendCalls +
            value.ServerDatagrams.SendCalls),
        SocketReceivePollCalls = checked(SocketReceivePollCalls + value.ClientDatagrams.ReceivePollCalls +
            value.ServerDatagrams.ReceivePollCalls),
        SocketReceiveCalls = checked(SocketReceiveCalls + value.ClientDatagrams.ReceiveCalls +
            value.ServerDatagrams.ReceiveCalls),
        SocketWouldBlockEvents = checked(SocketWouldBlockEvents + value.ClientDatagrams.WouldBlockEvents +
            value.ServerDatagrams.WouldBlockEvents),
    };

    internal DiagnosticCounterBaseline Add(SharedUdpListenerSnapshot value) => this with
    {
        SocketSendCalls = checked(SocketSendCalls + value.SendCalls),
        SocketReceivePollCalls = checked(SocketReceivePollCalls + value.ReceivePollCalls),
        SocketReceiveCalls = checked(SocketReceiveCalls + value.ReceiveCalls),
        SocketWouldBlockEvents = checked(SocketWouldBlockEvents + value.WouldBlockEvents),
        ListenerSendCalls = checked(ListenerSendCalls + value.SendCalls),
        ListenerReceivePollCalls = checked(ListenerReceivePollCalls + value.ReceivePollCalls),
        ListenerReceiveCalls = checked(ListenerReceiveCalls + value.ReceiveCalls),
        ListenerNativeSendCalls = checked(ListenerNativeSendCalls + value.NativeSendCalls),
        ListenerNativeSentDatagrams = checked(
            ListenerNativeSentDatagrams + value.NativeSentDatagrams),
    };
}

internal sealed record AdapterOptions(
    string Output,
    string Scenario,
    int Clients,
    int TickHz,
    int InputHz,
    int StageTicks,
    int SettleTicks,
    double SpawnSpacing,
    V3UdpTopology Topology,
    SharedUdpSocketIoMode SharedSocketIoMode,
    double ActiveRatio,
    V3DiagnosticLayer Layer,
    bool DeepTiming,
    bool DiagnosticSuite)
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
        ParseTopology(Get(args, "--topology=")),
        ParseSharedSocketIoMode(Get(args, "--shared-socket-io=")),
        ParseDouble(Get(args, "--active-ratio="), 1),
        ParseLayer(Get(args, "--diagnostic-layer=")),
        Has(args, "--deep-timing"),
        Has(args, "--diagnostic-suite"));

    internal NetworkBotScenario CreateScenario() => new NetworkBotScenario(
        Scenario, Clients, TickHz, InputHz, StageTicks, SettleTicks,
        SpawnSpacing,
        ObserverState: false,
        OrderedEvents: false).Validate();

    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static bool Has(string[] args, string value) =>
        args.Any(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate, value));
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
    private static SharedUdpSocketIoMode ParseSharedSocketIoMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "scalar" => SharedUdpSocketIoMode.Scalar,
            "platform-batch" or "native-send-batch" => SharedUdpSocketIoMode.PlatformBatch,
            _ => throw new ArgumentException($"Unsupported shared socket I/O mode '{value}'."),
        };
    private static V3DiagnosticLayer ParseLayer(string? value) => value?.ToLowerInvariant() switch
    {
        null or "full" => V3DiagnosticLayer.Full,
        "idle" => V3DiagnosticLayer.Idle,
        "control" => V3DiagnosticLayer.Control,
        "input" => V3DiagnosticLayer.Input,
        "input-owner" => V3DiagnosticLayer.InputOwner,
        _ => throw new ArgumentException($"Unsupported diagnostic layer '{value}'."),
    };
}
