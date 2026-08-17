using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Network;
using UltraSim.Network.Comparison;
using UltraSim.Server.Runtime;

internal static class DenseReplicationSuite
{
    internal static void Run(string[] args)
    {
        string requested = ReadText(args, "--replication-profile") ?? "all";
        int clients = ReadInt(args, "--clients", 400);
        int ticks = ReadInt(args, "--ticks", 120);
        int warmup = ReadInt(args, "--warmup", 30);
        int dirtyPercent = ReadInt(args, "--dirty-percent", 2);
        string requestedTraffic = ReadText(args, "--replication-traffic") ?? "full";
        bool phaseAttribution = ReadBool(args, "--phase-attribution", false);
        bool worldStateAttribution = ReadBool(
            args, "--worldstate-attribution", phaseAttribution);
        SharedUdpSocketIoMode socketIoMode = ParseSocketIoMode(
            ReadText(args, "--shared-socket-io"));
        string output = ReadText(args, "--output") ??
            "_Benchmarks/Baselines/network-v3-spread-dense-acceptance.json";
        if (clients <= 0 || clients > 1_024 || ticks < 30 || warmup < 1 ||
            dirtyPercent is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(args));

        ReplicationWorkload[] baseProfiles = requested.ToLowerInvariant() switch
        {
            "spread_mix" => [ReplicationWorkload.Spread(clients, ticks, warmup, dirtyPercent)],
            "dense_mix" => [ReplicationWorkload.Dense(clients, ticks, warmup, dirtyPercent)],
            "all" =>
            [
                ReplicationWorkload.Dense(clients, ticks, warmup, dirtyPercent),
                ReplicationWorkload.Spread(clients, ticks, warmup, dirtyPercent),
            ],
            _ => throw new ArgumentException($"Unknown replication profile '{requested}'."),
        };

        ReplicationTraffic[] traffic = requestedTraffic.ToLowerInvariant() switch
        {
            "full" => [ReplicationTraffic.Full],
            "worldstate-only" => [ReplicationTraffic.WorldStateOnly],
            "event-only" => [ReplicationTraffic.EventOnly],
            "updates-only" => [ReplicationTraffic.UpdatesOnly],
            "causal" => [ReplicationTraffic.Full, ReplicationTraffic.WorldStateOnly,
                ReplicationTraffic.EventOnly, ReplicationTraffic.UpdatesOnly],
            _ => throw new ArgumentException($"Unknown replication traffic mode '{requestedTraffic}'."),
        };
        ReplicationWorkload[] profiles = baseProfiles
            .SelectMany(profile => traffic.Select(profile.WithTraffic))
            .ToArray();

        var results = new List<ReplicationWorkloadResult>(profiles.Length);
        foreach (ReplicationWorkload profile in profiles)
        {
            Console.WriteLine($"REPLICATION {profile.Id}: constructing {profile.Clients} clients, " +
                $"{profile.Entities} entities...");
            using var harness = new ReplicationWorkloadHarness(
                profile, phaseAttribution, worldStateAttribution, socketIoMode);
            ReplicationWorkloadResult result = harness.Run();
            Validate(result);
            results.Add(result);
            Console.WriteLine($"REPLICATION {profile.Id}: tick p95 {result.TickMilliseconds.P95:F3} ms, " +
                $"relations {result.Relations:N0}, freshness p95/max " +
                $"{result.AllWorldStateFreshnessMilliseconds.P95:F2}/{result.AllWorldStateFreshnessMilliseconds.Max:F2} ms, " +
                $"event p95 {result.EventApplicationMilliseconds.P95:F2} ms, " +
                $"pending max {result.PendingRelations.Max:F0}, wire {result.WireBytes / 1048576d:F2} MiB");
        }

        var report = new ReplicationSuiteReport(
            "ultrasim-network-v3-spread-dense-acceptance-v3",
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            Environment.ProcessorCount,
            "V3-only integrated acceptance. Real shared-listener UDP, secure protocol, maintained AOI, " +
            "independent unreliable WorldState, reliable application Events, application ACKs, and retained client projection. " +
            "No V2 ratio is claimed because V2 does not own equivalent semantics. " +
            "Traffic controls preserve topology, relations, dirty updates, capacities, cadence, and transport; " +
            "phase attribution is opt-in and excluded from ordinary accepted timing.",
            results);
        NetworkBotComparisonIo.Write(output, report);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static void Validate(ReplicationWorkloadResult result)
    {
        if (!result.Converged)
            throw new InvalidOperationException($"{result.Profile.Id} did not converge to exact latest recipient state.");
        if (result.NonviableRecipients != 0 || result.CapacityDrops != 0 ||
            result.RetainedByteCapacityFailures != 0)
            throw new InvalidOperationException(
                $"{result.Profile.Id} exceeded an admitted bound or marked a recipient nonviable.");
        if (result.ApplicationEventsPublished != result.ApplicationEventsApplied)
            throw new InvalidOperationException(
                $"{result.Profile.Id} lost application Event continuity: " +
                $"{result.ApplicationEventsPublished} published, {result.ApplicationEventsApplied} applied.");
        if (result.PendingRelations.Max > result.Relations)
            throw new InvalidOperationException($"{result.Profile.Id} reported impossible relation debt.");
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? value = ReadText(args, name);
        return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool ReadBool(string[] args, string name, bool fallback)
    {
        string? value = ReadText(args, name);
        return value is null ? fallback : bool.Parse(value);
    }

    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? match = args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return match is null ? null : match[prefix.Length..];
    }

    private static SharedUdpSocketIoMode ParseSocketIoMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "platform-batch" or "native-send-batch" =>
                SharedUdpSocketIoMode.PlatformBatch,
            "platform-send-receive-batch" or "native-send-receive-batch" =>
                SharedUdpSocketIoMode.PlatformSendReceiveBatch,
            "scalar" => SharedUdpSocketIoMode.Scalar,
            _ => throw new ArgumentException($"Unsupported shared socket I/O mode '{value}'."),
        };
}

internal sealed class ReplicationWorkloadHarness : IDisposable
{
    private const int TickHz = 60;
    private const int RecordsPerBundle = 24;
    private const int BundlesPerRecipientTick = 2;
    private const int WorldStateByteBudget = 1_200;
    private const ulong ApplicationEpoch = 1;

    private static readonly UdpProtocolLimits s_udpLimits = new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 65_536,
        SocketReceiveBufferBytes: 65_536,
        MaximumHandshakePumps: 16_384,
        CookieBucketMilliseconds: 30_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 5_000,
        KeepAliveIntervalMilliseconds: 5_000);

    private readonly ReplicationWorkload _profile;
    private readonly bool _phaseAttribution;
    private readonly bool _worldStateAttribution;
    private readonly UltraSimGameComposition _composition;
    private readonly ServerRuntime _runtime;
    private readonly AdmissionService _admission;
    private readonly GameProjectionService _projection;
    private readonly GameInterestService _interest;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly SharedUdpProtocolHost _host;
    private readonly ReplicationBot[] _bots;
    private readonly ReplicationSpatialPoint[] _positions;
    private readonly ulong[] _versions;
    private readonly long[] _versionCreatedAt;
    private readonly int[] _versionCreatedTick;
    private readonly ulong[] _lastSeenVersions;
    private readonly byte[] _relationTiers;
    private readonly RemoteWorldEntitySnapshot[] _clientScratch;
    private readonly GameRecipientEntityRelationSnapshot[] _relationScratch;
    private readonly long[] _eventPublishedAt;
    private readonly int[] _eventPublishedTick;
    private readonly LatencyHistogram _immediateMs = new(0.25, 8_192);
    private readonly LatencyHistogram _nearMs = new(0.25, 8_192);
    private readonly LatencyHistogram _farMs = new(0.25, 8_192);
    private readonly LatencyHistogram _allMs = new(0.25, 8_192);
    private readonly LatencyHistogram _immediateTicks = new(1, 2_048, percentileBucketOffset: 0);
    private readonly LatencyHistogram _nearTicks = new(1, 2_048, percentileBucketOffset: 0);
    private readonly LatencyHistogram _farTicks = new(1, 2_048, percentileBucketOffset: 0);
    private readonly LatencyHistogram _eventMs = new(0.25, 8_192);
    private readonly LatencyHistogram _eventTicks = new(1, 2_048, percentileBucketOffset: 0);
    private readonly LatencyHistogram[] _clientFreshness;
    private readonly List<double> _tickMilliseconds;
    private readonly List<double> _diagnosticMilliseconds;
    private readonly List<double> _pendingRelations;
    private readonly List<double> _retainedBytes;
    private readonly List<double> _reliableQueued;
    private readonly List<double> _independentQueued;
    private readonly List<double> _receivedQueued;
    private readonly List<double> _inFlightPackets;
    private readonly List<double> _retainedPayloadRatio;
    private readonly List<double> _authorityUpdateMilliseconds;
    private readonly List<double> _eventPublishMilliseconds;
    private readonly List<double> _worldScheduleMilliseconds;
    private readonly List<double> _eventScheduleMilliseconds;
    private readonly List<double> _serverApplicationMilliseconds;
    private readonly List<double> _clientApplicationMilliseconds;
    private readonly List<double> _combinedProtocolPumpMilliseconds;
    private readonly List<double> _serverProtocolMilliseconds;
    private readonly List<double> _clientProtocolMilliseconds;
    private readonly List<double> _serverRoleMilliseconds;
    private readonly List<double> _clientRoleMilliseconds;
    private readonly ReplicationBatchCensus _batchCensus = new();
    private readonly WorldStateAcknowledgementCensus _worldStateAcknowledgementCensus = new();
    private int _dirtyCursor;
    private int _currentAuthorityTick;
    private readonly int _maximumRecipientScope;
    private ulong _eventsPublished;
    private ulong _eventsApplied;
    private long _tickServerApplicationTicks;
    private long _tickClientApplicationTicks;
    private long _tickProtocolPumpTicks;
    private bool _disposed;

    internal ReplicationWorkloadHarness(
        ReplicationWorkload profile,
        bool phaseAttribution,
        bool worldStateAttribution = false,
        SharedUdpSocketIoMode socketIoMode = SharedUdpSocketIoMode.PlatformBatch)
    {
        _profile = profile;
        _phaseAttribution = phaseAttribution;
        _worldStateAttribution = worldStateAttribution;
        _tickMilliseconds = new List<double>(profile.Ticks);
        _diagnosticMilliseconds = new List<double>(profile.Ticks);
        _pendingRelations = new List<double>(profile.Ticks);
        _retainedBytes = new List<double>(profile.Ticks);
        _reliableQueued = new List<double>(profile.Ticks);
        _independentQueued = new List<double>(profile.Ticks);
        _receivedQueued = new List<double>(profile.Ticks);
        _inFlightPackets = new List<double>(profile.Ticks);
        _retainedPayloadRatio = new List<double>(profile.Ticks);
        _authorityUpdateMilliseconds = new List<double>(profile.Ticks);
        _eventPublishMilliseconds = new List<double>(profile.Ticks);
        _worldScheduleMilliseconds = new List<double>(profile.Ticks);
        _eventScheduleMilliseconds = new List<double>(profile.Ticks);
        _serverApplicationMilliseconds = new List<double>(profile.Ticks);
        _clientApplicationMilliseconds = new List<double>(profile.Ticks);
        _combinedProtocolPumpMilliseconds = new List<double>(profile.Ticks);
        _serverProtocolMilliseconds = new List<double>(profile.Ticks);
        _clientProtocolMilliseconds = new List<double>(profile.Ticks);
        _serverRoleMilliseconds = new List<double>(profile.Ticks);
        _clientRoleMilliseconds = new List<double>(profile.Ticks);
        _clientFreshness = Enumerable.Range(0, profile.Clients)
            .Select(_ => new LatencyHistogram(0.25, 8_192)).ToArray();

        _composition = new UltraSimGameComposition(
            maximumChunksPerContentArchetype: Math.Max(16, (profile.Clients + 255) / 256),
            runtimeProfile: GameRuntimeProfile.Networked);
        _runtime = new ServerRuntime(_composition, new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(
                profile.Clients + 64,
                new PersistenceIdentityOptions(new PersistentWorldId(0xD3_0001)),
                GameRuntimeProfile.Networked),
            WorldClockOptions.Default));
        _runtime.Initialize();
        _runtime.Start();
        _admission = new AdmissionService(_runtime.Game, _composition);
        for (int index = 0; index < profile.Clients; index++)
        {
            AdmissionResult entered = _admission.JoinAccountless(Participant(index));
            Check(entered.IsEntered, $"Participant {index} did not enter authority.");
        }

        var projectionLimits = new GameReplicationLimits(
            MaximumEntities: profile.Clients + 64,
            MaximumParticipants: profile.Clients,
            MaximumLifecycleFactsPerCapture: Math.Max(4_096, profile.Clients * 2),
            MaximumDeltasPerCapture: Math.Max(8_192, profile.Clients * 4),
            LifecycleHistoryCapacity: Math.Max(8_192, profile.Clients * 4),
            ApplicationEventCapacity: Math.Max(1_024, profile.Clients * 4),
            AuthoritativeInputCapacity: Math.Max(64, profile.Clients));
        _projection = new GameProjectionService(_runtime.Game, _admission, projectionLimits);
        for (int index = 0; index < profile.Clients; index++)
            Check(_projection.SynchronizeParticipant(Participant(index)),
                $"Participant {index} was not visible to projection.");
        GameProjectionCaptureResult baseline = _projection.RebuildBaseline();
        Check(baseline.Status == GameProjectionCaptureStatus.BaselineRebuilt,
            "Application Event projection baseline failed.");

        GameInterestProfile interestProfile = GameInterestProfile.Reference;
        int farCells = checked((int)(((long)interestProfile.FarRadiusMillimeters +
            interestProfile.RegionEdgeMillimeters - 1) / interestProfile.RegionEdgeMillimeters));
        int edgeWidth = checked(farCells * 2 + 1);
        int maximumEdges = checked(edgeWidth * edgeWidth * profile.Clients + edgeWidth);
        var interestLimits = new GameInterestLimits(
            MaximumEntities: profile.Entities,
            MaximumRecipients: profile.Clients,
            MaximumRegions: checked(maximumEdges + profile.Entities),
            MaximumRegionInterestEdges: maximumEdges,
            MaximumRecipientEntityRelations: checked(profile.Entities * profile.Clients),
            MaximumOutstandingBundles: checked(profile.Clients * 8),
            MaximumRecordsPerBundle: RecordsPerBundle);
        _interest = new GameInterestService(_projection.StateEpoch, interestLimits, interestProfile);

        _positions = new ReplicationSpatialPoint[profile.Entities];
        _versions = new ulong[profile.Entities];
        _versionCreatedAt = new long[profile.Entities];
        _versionCreatedTick = new int[profile.Entities];
        _lastSeenVersions = new ulong[checked(profile.Clients * profile.Entities)];
        _relationTiers = new byte[checked(profile.Clients * profile.Entities)];
        _clientScratch = new RemoteWorldEntitySnapshot[profile.Entities];
        _relationScratch = new GameRecipientEntityRelationSnapshot[checked(profile.Clients * profile.Entities)];
        int maximumEvents = checked(profile.Clients * (profile.WarmupTicks + profile.Ticks + 256) / 10 + 1_024);
        _eventPublishedAt = new long[maximumEvents];
        _eventPublishedTick = new int[maximumEvents];

        for (int index = 0; index < profile.Clients; index++)
            Check(_interest.SetRecipientAnchor(Participant(index), profile.Anchor(index), 0) ==
                GameInterestUpdateStatus.Success, "Recipient interest setup exceeded its bound.");
        for (int index = 0; index < profile.Entities; index++)
        {
            _positions[index] = profile.EntityPoint(index);
            _versions[index] = 1;
            _versionCreatedTick[index] = -1;
            Check(_interest.UpsertEntity(Projected(index, 1), 1, _positions[index],
                    GameReplicationHorizon.Far, 0) == GameInterestUpdateStatus.Success,
                "Entity interest setup exceeded its bound.");
        }
        _maximumRecipientScope = MeasureMaximumRecipientScope();

        _protocolLimits = new NetworkProtocolLimits(
            MaximumReliableMessages: 32,
            MaximumReceivedMessages: 32,
            MaximumMessageBytes: UltraSim.Game.Protocol.UltraSimGameProtocol.MaximumMessageBytes,
            MaximumFragmentsPerMessage: 64,
            SentPacketCapacity: 256,
            MaximumInboundPacketsPerPump: 64,
            MaximumPacketsPerPump: 64,
            MinimumRetransmissionSteps: 2,
            MaximumRetransmissionSteps: 128,
            InitialCongestionWindowPackets: 8,
            MaximumCongestionWindowPackets: 128,
            MaximumIndependentUnreliableMessages: 8,
            ReliablePayloadByteCapacity: 65_536,
            ReceivedPayloadByteCapacity: 65_536);
        var sharedLimits = new SharedUdpProtocolLimits(
            MaximumSessions: profile.Clients,
            MaximumQueuedDatagramsPerSession: 16,
            MaximumInboundDatagramsPerPump: Math.Max(4_096, profile.Clients * 32),
            SocketIoMode: socketIoMode,
            MaximumNativeSendBatchDatagrams: 32,
            MaximumNativeReceiveBatchDatagrams: 32);
        _host = new SharedUdpProtocolHost(
            _protocolLimits, s_udpLimits, sharedLimits,
            UltraSim.Game.Protocol.UltraSimGameProtocol.Manifest);
        _bots = new ReplicationBot[profile.Clients];
        ulong eventFloor = baseline.Frame.ApplicationEventNextSequence;
        for (int index = 0; index < profile.Clients; index++)
        {
            Check(_host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _),
                $"Secure UDP session {index} could not be established.");
            ParticipantId participant = Participant(index);
            _bots[index] = new ReplicationBot(
                session!,
                new RemoteWorldStateServer(session!.Server, _interest, participant,
                    ApplicationEpoch, RecordsPerBundle, _maximumRecipientScope),
                new RemoteWorldStateClient(session.Client, ApplicationEpoch, _projection.StateEpoch,
                    _maximumRecipientScope, _maximumRecipientScope, RecordsPerBundle, 64),
                new RemoteApplicationEventServer(session.Server, _projection, participant,
                    ApplicationEpoch, eventFloor),
                new RemoteApplicationEventClient(session.Client, ApplicationEpoch,
                    participant.Value, _projection.StateEpoch, eventFloor, 16));
        }
    }

    internal ReplicationWorkloadResult Run()
    {
        DrainInitialState();
        ScanFreshness(authorityTick: 0, measure: false);
        long nextDeadline = Stopwatch.GetTimestamp();
        for (int tick = 1; tick <= _profile.WarmupTicks; tick++)
        {
            RunActiveTick(tick, measure: false);
            WaitForRealtimeTick(ref nextDeadline);
        }
        _ = DrainState(_profile.WarmupTicks, ref nextDeadline, measure: false);
        ScanFreshness(_profile.WarmupTicks, measure: false);
        nextDeadline = Stopwatch.GetTimestamp();

        ResetMeasurement();
        if (_phaseAttribution)
        {
            ResetProtocolStageTiming(enabled: true);
        }
        if (_worldStateAttribution)
        {
            _interest.ResetAttribution(enabled: true);
            for (int index = 0; index < _bots.Length; index++)
            {
                _bots[index].WorldServer.ResetAttribution(enabled: true);
                _bots[index].WorldClient.ResetAcknowledgementCensus(enabled: true);
            }
        }
        GameInterestSnapshot interestBefore = _interest.GetSnapshot();
        GameProjectionSnapshot projectionBefore = _projection.GetSnapshot();
        EventTotals eventBefore = CaptureEventTotals();
        TransportTotals networkBefore = CaptureTransportTotals();
        SharedUdpListenerSnapshot listenerBefore = _host.GetSnapshot().Listener;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        using Process process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long wallStarted = Stopwatch.GetTimestamp();
        long peakHeap = GC.GetTotalMemory(false);
        long peakWorking = process.WorkingSet64;
        long peakPrivate = process.PrivateMemorySize64;

        for (int tick = 1; tick <= _profile.Ticks; tick++)
        {
            RunActiveTick(tick, measure: true);
            peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
            if ((tick & 7) == 0)
            {
                process.Refresh();
                peakWorking = Math.Max(peakWorking, process.WorkingSet64);
                peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            }
            WaitForRealtimeTick(ref nextDeadline);
        }

        int settleTicks = DrainState(_profile.Ticks, ref nextDeadline, measure: true);
        long wallEnded = Stopwatch.GetTimestamp();
        process.Refresh();
        peakWorking = Math.Max(peakWorking, process.WorkingSet64);
        peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
        peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
        TimeSpan cpuAfter = process.TotalProcessorTime;
        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        GameInterestSnapshot interestAfter = _interest.GetSnapshot();
        GameProjectionSnapshot projectionAfter = _projection.GetSnapshot();
        EventTotals eventAfter = CaptureEventTotals();
        TransportTotals networkAfter = CaptureTransportTotals();
        SharedUdpProtocolHostSnapshot hostAfter = _host.GetSnapshot();
        ReplicationWorldStateAttribution worldAttribution = CaptureWorldStateAttribution();

        bool converged = ValidateConvergence();
        NetworkMetricSummary[] clientP95 = _clientFreshness
            .Select(value => value.Summary()).ToArray();
        double deadline = 1000d / TickHz;
        int misses = _tickMilliseconds.Count(value => value > deadline);
        int longestMiss = LongestMissStreak(_tickMilliseconds, deadline);
        long applicationAdapterBytes = 0;
        for (int index = 0; index < _bots.Length; index++)
            applicationAdapterBytes = checked(applicationAdapterBytes +
                _bots[index].WorldServer.GetSnapshot().ReservedBytes +
                _bots[index].WorldClient.GetSnapshot().ReservedBytes);
        long knownReserved = checked(hostAfter.KnownReservedBytes + interestAfter.ReservedBytes +
            _projection.GetSnapshot().ReservedBytes + applicationAdapterBytes);

        ulong relationsCopied = interestAfter.SemanticRecordCopies - interestBefore.SemanticRecordCopies;
        ulong semanticBytes = interestAfter.SemanticBytesCopied - interestBefore.SemanticBytesCopied;
        ulong constructions = interestAfter.UniqueSemanticConstructions -
            interestBefore.UniqueSemanticConstructions;
        ulong bundles = interestAfter.BundlesBuilt - interestBefore.BundlesBuilt;
        ulong dataPackets = networkAfter.DataPackets - networkBefore.DataPackets;
        ulong wireBytes = networkAfter.WireBytes - networkBefore.WireBytes;
        ulong wireDatagrams = networkAfter.WireDatagrams - networkBefore.WireDatagrams;
        ulong retransmittedBytes = networkAfter.RetransmittedWireBytes - networkBefore.RetransmittedWireBytes;
        ulong capacityDrops = networkAfter.CapacityDrops - networkBefore.CapacityDrops;
        ulong byteFailures = networkAfter.ByteCapacityFailures - networkBefore.ByteCapacityFailures;
        ulong compactionBytes = networkAfter.CompactionBytesMoved - networkBefore.CompactionBytesMoved;
        ulong publishedDuringMeasure = _eventsPublished;
        ulong appliedDuringMeasure = _eventsApplied;
        double wallMs = Stopwatch.GetElapsedTime(wallStarted, wallEnded).TotalMilliseconds;
        double cpuMs = (cpuAfter - cpuBefore).TotalMilliseconds;
        NetworkMetricSummary retained = NetworkMetricSummary.From(_retainedBytes);

        var result = new ReplicationWorkloadResult(
            _profile,
            interestAfter.RelationCount,
            _maximumRecipientScope,
            converged,
            settleTicks,
            NetworkMetricSummary.From(_tickMilliseconds),
            NetworkMetricSummary.From(_diagnosticMilliseconds),
            _immediateMs.Summary(),
            _nearMs.Summary(),
            _farMs.Summary(),
            _allMs.Summary(),
            _immediateTicks.Summary(),
            _nearTicks.Summary(),
            _farTicks.Summary(),
            _eventMs.Summary(),
            _eventTicks.Summary(),
            NetworkMetricSummary.From(clientP95.Select(value => value.P95).ToArray()),
            clientP95.Max(value => value.P95),
            clientP95.Max(value => value.Max),
            NetworkMetricSummary.From(_pendingRelations),
            retained,
            NetworkMetricSummary.From(_reliableQueued),
            NetworkMetricSummary.From(_independentQueued),
            NetworkMetricSummary.From(_receivedQueued),
            NetworkMetricSummary.From(_inFlightPackets),
            NetworkMetricSummary.From(_retainedPayloadRatio),
            interestAfter.NonviableRecipientCount,
            misses,
            longestMiss,
            publishedDuringMeasure,
            appliedDuringMeasure,
            constructions,
            relationsCopied,
            semanticBytes,
            bundles,
            eventAfter.RecordsExamined - eventBefore.RecordsExamined,
            eventAfter.RecordsSkipped - eventBefore.RecordsSkipped,
            projectionAfter.RetainedApplicationEventHighWater,
            projectionAfter.ApplicationEventMinimumCursorScanEntries -
                projectionBefore.ApplicationEventMinimumCursorScanEntries,
            projectionAfter.ApplicationEventMinimumCursorFullRescans -
                projectionBefore.ApplicationEventMinimumCursorFullRescans,
            projectionAfter.ApplicationEventMinimumCursorFastPathUpdates -
                projectionBefore.ApplicationEventMinimumCursorFastPathUpdates,
            projectionAfter.ApplicationEventConsumerLookupComparisons -
                projectionBefore.ApplicationEventConsumerLookupComparisons,
            projectionAfter.ApplicationEventConsumerLookupAttempts -
                projectionBefore.ApplicationEventConsumerLookupAttempts,
            projectionAfter.ApplicationEventParticipantIndexHits -
                projectionBefore.ApplicationEventParticipantIndexHits,
            projectionAfter.ApplicationEventNoRelevantFastForwards -
                projectionBefore.ApplicationEventNoRelevantFastForwards,
            wireDatagrams,
            wireBytes,
            dataPackets,
            networkAfter.AcknowledgementPackets - networkBefore.AcknowledgementPackets,
            networkAfter.Retransmissions - networkBefore.Retransmissions,
            retransmittedBytes,
            capacityDrops,
            byteFailures,
            networkAfter.Compactions - networkBefore.Compactions,
            compactionBytes,
            knownReserved,
            peakHeap,
            peakWorking,
            peakPrivate,
            allocatedAfter - allocatedBefore,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            cpuMs,
            wallMs == 0 ? 0 : cpuMs / wallMs,
            listenerBefore,
            hostAfter.Listener,
            _batchCensus.CreateReport(),
            new ReplicationPhaseAttribution(
                _phaseAttribution,
                NetworkMetricSummary.From(_authorityUpdateMilliseconds),
                NetworkMetricSummary.From(_eventPublishMilliseconds),
                NetworkMetricSummary.From(_worldScheduleMilliseconds),
                NetworkMetricSummary.From(_eventScheduleMilliseconds),
                NetworkMetricSummary.From(_serverApplicationMilliseconds),
                NetworkMetricSummary.From(_clientApplicationMilliseconds),
                NetworkMetricSummary.From(_combinedProtocolPumpMilliseconds),
                NetworkMetricSummary.From(_serverProtocolMilliseconds),
                NetworkMetricSummary.From(_clientProtocolMilliseconds),
                NetworkMetricSummary.From(_serverRoleMilliseconds),
                NetworkMetricSummary.From(_clientRoleMilliseconds)),
            CaptureServerProtocolAttribution(),
            worldAttribution,
            new ReplicationAmplification(
                constructions == 0 ? 0 : relationsCopied / (double)constructions,
                relationsCopied == 0 ? 0 : bundles / (double)relationsCopied,
                bundles == 0 ? 0 : dataPackets / (double)bundles,
                relationsCopied == 0 ? 0 : wireBytes / (double)relationsCopied,
                semanticBytes == 0 ? 0 : wireBytes / (double)semanticBytes,
                wireDatagrams == 0 ? 0 : wireBytes / (double)wireDatagrams,
                wireBytes == 0 ? 0 : retransmittedBytes / (double)wireBytes));
        if (_phaseAttribution)
        {
            ResetProtocolStageTiming(enabled: false);
        }
        if (_worldStateAttribution)
        {
            _interest.ResetAttribution(enabled: false);
            for (int index = 0; index < _bots.Length; index++)
            {
                _bots[index].WorldServer.ResetAttribution(enabled: false);
                _bots[index].WorldClient.ResetAcknowledgementCensus(enabled: false);
            }
        }
        return result;
    }

    private void RunActiveTick(int tick, bool measure)
    {
        _currentAuthorityTick = tick;
        long started = Stopwatch.GetTimestamp();
        ReplicationProtocolTiming protocolBefore = measure && _phaseAttribution
            ? CaptureProtocolStageTiming()
            : default;
        _tickServerApplicationTicks = 0;
        _tickClientApplicationTicks = 0;
        _tickProtocolPumpTicks = 0;
        long phaseStarted = Stopwatch.GetTimestamp();
        int dirty = Math.Max(1, _profile.Entities * _profile.DirtyPercent / 100);
        for (int offset = 0; offset < dirty; offset++)
        {
            int entity = (_dirtyCursor + offset) % _profile.Entities;
            ulong version = checked(++_versions[entity]);
            ReplicationSpatialPoint position = _positions[entity];
            position = new ReplicationSpatialPoint(
                checked(position.XMillimeters + ((version & 1) == 0 ? 1 : -1)),
                position.ZMillimeters);
            _positions[entity] = position;
            _versionCreatedAt[entity] = Stopwatch.GetTimestamp();
            _versionCreatedTick[entity] = tick;
            Check(_interest.UpsertEntity(Projected(entity, tick), version, position,
                    GameReplicationHorizon.Far, SemanticMilliseconds(tick)) ==
                GameInterestUpdateStatus.Success, "Canonical WorldState update failed.");
        }
        _dirtyCursor = (_dirtyCursor + dirty) % _profile.Entities;
        RecordPhase(_authorityUpdateMilliseconds, phaseStarted, measure && _phaseAttribution);

        phaseStarted = Stopwatch.GetTimestamp();
        if (_profile.EventsEnabled) PublishEvents(tick);
        RecordPhase(_eventPublishMilliseconds, phaseStarted, measure && _phaseAttribution);

        long now = SemanticMilliseconds(tick);
        phaseStarted = Stopwatch.GetTimestamp();
        if (_profile.WorldStateEnabled) ScheduleWorldState(now);
        RecordPhase(_worldScheduleMilliseconds, phaseStarted, measure && _phaseAttribution);

        phaseStarted = Stopwatch.GetTimestamp();
        if (_profile.EventsEnabled) ScheduleEvents();
        RecordPhase(_eventScheduleMilliseconds, phaseStarted, measure && _phaseAttribution);
        for (int pass = 0; pass < 4; pass++) PumpAndDispatch(measure);
        if (measure)
        {
            _tickMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (_phaseAttribution)
            {
                ReplicationProtocolTiming protocolAfter = CaptureProtocolStageTiming();
                ReplicationProtocolTiming protocol = protocolAfter.Subtract(protocolBefore);
                double authority = _authorityUpdateMilliseconds[^1];
                double eventPublish = _eventPublishMilliseconds[^1];
                double worldSchedule = _worldScheduleMilliseconds[^1];
                double eventSchedule = _eventScheduleMilliseconds[^1];
                double serverApplication = ToMilliseconds(_tickServerApplicationTicks);
                double clientApplication = ToMilliseconds(_tickClientApplicationTicks);
                double serverProtocol = ToMilliseconds(protocol.ServerTicks);
                double clientProtocol = ToMilliseconds(protocol.ClientTicks);
                _serverApplicationMilliseconds.Add(serverApplication);
                _clientApplicationMilliseconds.Add(clientApplication);
                _combinedProtocolPumpMilliseconds.Add(ToMilliseconds(_tickProtocolPumpTicks));
                _serverProtocolMilliseconds.Add(serverProtocol);
                _clientProtocolMilliseconds.Add(clientProtocol);
                _serverRoleMilliseconds.Add(authority + eventPublish + worldSchedule + eventSchedule +
                    serverApplication + serverProtocol);
                _clientRoleMilliseconds.Add(clientApplication + clientProtocol);
            }
        }

        long diagnosticStarted = Stopwatch.GetTimestamp();
        ScanFreshness(tick, measure);
        if (measure)
        {
            _diagnosticMilliseconds.Add(Stopwatch.GetElapsedTime(diagnosticStarted).TotalMilliseconds);
            GameInterestSnapshot interest = _interest.GetSnapshot();
            _pendingRelations.Add(interest.PendingRelationCount);
            TransportTotals totals = CaptureTransportTotals();
            _retainedBytes.Add(totals.RetainedBytes);
            _reliableQueued.Add(totals.ReliableQueued);
            _independentQueued.Add(totals.IndependentQueued);
            _receivedQueued.Add(totals.ReceivedQueued);
            _inFlightPackets.Add(totals.InFlightPackets);
            _retainedPayloadRatio.Add(totals.RetainedCapacityBytes == 0
                ? 0
                : totals.RetainedBytes / (double)totals.RetainedCapacityBytes);
        }
    }

    private void PublishEvents(int tick)
    {
        int count = Math.Max(1, _profile.Clients / 20);
        int start = checked((tick - 1) * count % _profile.Clients);
        for (int offset = 0; offset < count; offset++)
        {
            int client = (start + offset) % _profile.Clients;
            Check(_projection.TryPublishApplicationEvent(
                    Participant(client), kind: 1, subject: checked((ulong)tick),
                    payloadHash: checked((ulong)client + 1), out GameApplicationEvent value),
                "Application Event retention exceeded its admitted bound.");
            int sequence = checked((int)value.Sequence);
            Check(sequence < _eventPublishedAt.Length, "Event timing storage exhausted.");
            _eventPublishedAt[sequence] = Stopwatch.GetTimestamp();
            _eventPublishedTick[sequence] = tick;
            _eventsPublished++;
        }
    }

    private void ScheduleWorldState(long nowMilliseconds)
    {
        for (int index = 0; index < _bots.Length; index++)
        {
            ReplicationBot bot = _bots[index];
            for (int bundle = 0; bundle < BundlesPerRecipientTick; bundle++)
            {
                GameWorldStateBuildStatus status = bot.WorldServer.TrySchedule(
                    nowMilliseconds, WorldStateByteBudget);
                if (status != GameWorldStateBuildStatus.Success) break;
            }
        }
    }

    private void ScheduleEvents()
    {
        for (int index = 0; index < _bots.Length; index++)
            _ = _bots[index].EventServer.TrySchedule();
    }

    private void ScheduleApplication(long nowMilliseconds)
    {
        if (_profile.WorldStateEnabled) ScheduleWorldState(nowMilliseconds);
        if (_profile.EventsEnabled) ScheduleEvents();
    }

    private void PumpAndDispatch(bool captureBatch)
    {
        if (captureBatch)
            for (int index = 0; index < _bots.Length; index++)
            {
                _batchCensus.Add(_bots[index].Session.Client.InspectUnreliableBatchOpportunity());
                _batchCensus.Add(_bots[index].Session.Server.InspectUnreliableBatchOpportunity());
            }
        long protocolStarted = _phaseAttribution ? Stopwatch.GetTimestamp() : 0;
        _host.Pump();
        if (_phaseAttribution)
            _tickProtocolPumpTicks = checked(_tickProtocolPumpTicks +
                Stopwatch.GetTimestamp() - protocolStarted);
        for (int index = 0; index < _bots.Length; index++)
        {
            ReplicationBot bot = _bots[index];
            long applicationStarted = _phaseAttribution ? Stopwatch.GetTimestamp() : 0;
            while (bot.Session.Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                _ = bot.WorldServer.TryProcess(in message, payload) ||
                    bot.EventServer.TryProcess(in message, payload);
                _ = bot.Session.Server.TryConsumeReceived();
            }
            if (_phaseAttribution)
                _tickServerApplicationTicks = checked(_tickServerApplicationTicks +
                    Stopwatch.GetTimestamp() - applicationStarted);

            applicationStarted = _phaseAttribution ? Stopwatch.GetTimestamp() : 0;
            while (bot.Session.Client.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                _ = bot.WorldClient.TryProcess(in message, payload) ||
                    bot.EventClient.TryProcess(in message, payload);
                _ = bot.Session.Client.TryConsumeReceived();
            }
            if (_phaseAttribution)
                _worldStateAcknowledgementCensus.Add(
                    bot.WorldClient.InspectAcknowledgementFlushOpportunity());
            _ = bot.WorldClient.TryFlushAcknowledgements();
            _ = bot.EventClient.TryFlushAcknowledgement();
            while (bot.EventClient.TryConsumeNext(out RemoteApplicationEventSnapshot value))
            {
                int sequence = checked((int)value.Sequence);
                Check(sequence < _eventPublishedAt.Length && _eventPublishedAt[sequence] != 0,
                    "Applied Event did not have a publication timestamp.");
                _eventMs.Observe(Stopwatch.GetElapsedTime(_eventPublishedAt[sequence]).TotalMilliseconds);
                _eventTicks.Observe(Math.Max(0,
                    _currentAuthorityTick - _eventPublishedTick[sequence]));
                _eventsApplied++;
            }
            if (_phaseAttribution)
                _tickClientApplicationTicks = checked(_tickClientApplicationTicks +
                    Stopwatch.GetTimestamp() - applicationStarted);
        }
    }

    private void DrainInitialState()
    {
        for (int round = 0; round < 2_048; round++)
        {
            // Every causal control begins from the same fully materialized client scope.
            ScheduleWorldState(checked(100L + round * 17L));
            if (_profile.EventsEnabled) ScheduleEvents();
            for (int pass = 0; pass < 4; pass++) PumpAndDispatch(captureBatch: false);
            GameInterestSnapshot snapshot = _interest.GetSnapshot();
            if (snapshot.PendingRelationCount == 0 && snapshot.OutstandingBundleCount == 0)
            {
                CaptureRelationTiers();
                return;
            }
            Thread.Sleep(1);
        }
        throw new InvalidOperationException("Initial recipient state did not converge.");
    }

    private int DrainState(int authorityTick, ref long nextDeadline, bool measure)
    {
        _currentAuthorityTick = authorityTick;
        for (int settle = 1; settle <= 240; settle++)
        {
            ScheduleApplication(SemanticMilliseconds(authorityTick + settle));
            for (int pass = 0; pass < 4; pass++) PumpAndDispatch(captureBatch: measure);
            ScanFreshness(authorityTick, measure);
            GameInterestSnapshot snapshot = _interest.GetSnapshot();
            bool worldComplete = !_profile.WorldStateEnabled ||
                (snapshot.PendingRelationCount == 0 && snapshot.OutstandingBundleCount == 0);
            bool eventsComplete = !_profile.EventsEnabled ||
                _bots.All(bot => !bot.EventServer.GetSnapshot().AwaitingApplication);
            if (worldComplete && eventsComplete)
                return settle;
            WaitForRealtimeTick(ref nextDeadline);
        }
        return 240;
    }

    private void ScanFreshness(int authorityTick, bool measure)
    {
        for (int client = 0; client < _bots.Length; client++)
        {
            int count = _bots[client].WorldClient.CopyActiveEntitiesUnordered(_clientScratch);
            int baseIndex = checked(client * _profile.Entities);
            for (int index = 0; index < count; index++)
            {
                ref readonly RemoteWorldEntitySnapshot state = ref _clientScratch[index];
                int entity = checked((int)state.Entity.Index);
                ulong prior = _lastSeenVersions[baseIndex + entity];
                if (state.CanonicalVersion == prior) continue;
                _lastSeenVersions[baseIndex + entity] = state.CanonicalVersion;
                if (!measure || _versionCreatedTick[entity] < 0 ||
                    state.CanonicalVersion != _versions[entity]) continue;
                double milliseconds = Stopwatch.GetElapsedTime(_versionCreatedAt[entity]).TotalMilliseconds;
                double ticks = Math.Max(0, authorityTick - _versionCreatedTick[entity]);
                _allMs.Observe(milliseconds);
                _clientFreshness[client].Observe(milliseconds);
                switch ((GameInterestTier)_relationTiers[baseIndex + entity])
                {
                    case GameInterestTier.Immediate:
                        _immediateMs.Observe(milliseconds);
                        _immediateTicks.Observe(ticks);
                        break;
                    case GameInterestTier.Near:
                        _nearMs.Observe(milliseconds);
                        _nearTicks.Observe(ticks);
                        break;
                    case GameInterestTier.Far:
                        _farMs.Observe(milliseconds);
                        _farTicks.Observe(ticks);
                        break;
                }
            }
        }
    }

    private bool ValidateConvergence()
    {
        if (!_profile.WorldStateEnabled)
            return _eventsPublished == _eventsApplied;
        int relationCount = _interest.CopyRelations(_relationScratch);
        int relationIndex = 0;
        for (int client = 0; client < _bots.Length; client++)
        {
            int count = _bots[client].WorldClient.CopyActiveEntities(_clientScratch);
            int expected = 0;
            while (relationIndex < relationCount &&
                   _relationScratch[relationIndex].Participant == Participant(client))
            {
                ref readonly GameRecipientEntityRelationSnapshot relation = ref _relationScratch[relationIndex++];
                if (!relation.DesiredPresent) continue;
                expected++;
                int entity = checked((int)relation.Entity.Index);
                if (_lastSeenVersions[checked(client * _profile.Entities + entity)] != _versions[entity])
                    return false;
            }
            if (count != expected) return false;
        }
        return relationIndex == relationCount;
    }

    private void CaptureRelationTiers()
    {
        Array.Clear(_relationTiers);
        int count = _interest.CopyRelations(_relationScratch);
        for (int index = 0; index < count; index++)
        {
            ref readonly GameRecipientEntityRelationSnapshot relation = ref _relationScratch[index];
            int client = checked((int)relation.Participant.Value - 1);
            int entity = checked((int)relation.Entity.Index);
            _relationTiers[checked(client * _profile.Entities + entity)] = (byte)relation.Tier;
        }
    }

    private int MeasureMaximumRecipientScope()
    {
        int count = _interest.CopyRelations(_relationScratch);
        int maximum = 0;
        int current = 0;
        ParticipantId participant = default;
        for (int index = 0; index < count; index++)
        {
            ref readonly GameRecipientEntityRelationSnapshot relation = ref _relationScratch[index];
            if (relation.Participant != participant)
            {
                maximum = Math.Max(maximum, current);
                participant = relation.Participant;
                current = 0;
            }
            if (relation.DesiredPresent) current++;
        }
        maximum = Math.Max(maximum, current);
        Check(maximum > 0, "No recipient owns an admitted interest scope.");
        return maximum;
    }

    private void ResetMeasurement()
    {
        _eventsPublished = 0;
        _eventsApplied = 0;
        _immediateMs.Clear();
        _nearMs.Clear();
        _farMs.Clear();
        _allMs.Clear();
        _immediateTicks.Clear();
        _nearTicks.Clear();
        _farTicks.Clear();
        _eventMs.Clear();
        _eventTicks.Clear();
        foreach (LatencyHistogram value in _clientFreshness) value.Clear();
        _tickMilliseconds.Clear();
        _diagnosticMilliseconds.Clear();
        _pendingRelations.Clear();
        _retainedBytes.Clear();
        _reliableQueued.Clear();
        _independentQueued.Clear();
        _receivedQueued.Clear();
        _inFlightPackets.Clear();
        _retainedPayloadRatio.Clear();
        _batchCensus.Clear();
        _worldStateAcknowledgementCensus.Clear();
        _authorityUpdateMilliseconds.Clear();
        _eventPublishMilliseconds.Clear();
        _worldScheduleMilliseconds.Clear();
        _eventScheduleMilliseconds.Clear();
        _serverApplicationMilliseconds.Clear();
        _clientApplicationMilliseconds.Clear();
        _combinedProtocolPumpMilliseconds.Clear();
        _serverProtocolMilliseconds.Clear();
        _clientProtocolMilliseconds.Clear();
        _serverRoleMilliseconds.Clear();
        _clientRoleMilliseconds.Clear();
    }

    private void ResetProtocolStageTiming(bool enabled)
    {
        for (int index = 0; index < _bots.Length; index++)
        {
            _bots[index].Session.Client.ResetStageTiming(enabled);
            _bots[index].Session.Server.ResetStageTiming(enabled);
        }
        _host.ResetListenerStageTiming(enabled);
    }

    private ReplicationWorldStateAttribution CaptureWorldStateAttribution()
    {
        if (!_worldStateAttribution) return default;
        GameInterestAttributionSnapshot interest = _interest.GetAttributionSnapshot();
        ulong classifiedUpserts = SaturatingAdd(
            interest.StableFarFastPathUpserts, interest.GeneralPathUpserts);
        double stableFarFastPathEligibilityPercent = classifiedUpserts == 0
            ? 0
            : interest.StableFarFastPathUpserts * 100d / classifiedUpserts;
        ulong[] histogram = new ulong[RecordsPerBundle + 1];
        _interest.CopyBundleRecordHistogram(histogram);
        ulong[] cadenceDelayHistogram = new ulong[GameInterestService.DeferredDelayHistogramLength];
        ulong[] retryDelayHistogram = new ulong[GameInterestService.DeferredDelayHistogramLength];
        _interest.CopyDeferredDelayHistograms(cadenceDelayHistogram, retryDelayHistogram);
        ulong selectedRecords = SaturatingAdd(
            SaturatingAdd(interest.PendingSelectedImmediate, interest.PendingSelectedNear),
            interest.PendingSelectedFar);
        ulong deferredExaminations = SaturatingAdd(
            interest.PendingSkippedCadence,
            interest.PendingSkippedAwaitingRetry);
        double skippedDeferredDebtPerSelectedRecord = selectedRecords == 0
            ? 0
            : deferredExaminations / (double)selectedRecords;
        ulong tierOverheadTicks = interest.TierSelectionTicks >= interest.PendingTraversalTicks
            ? interest.TierSelectionTicks - interest.PendingTraversalTicks
            : 0;
        ulong scheduleCalls = 0;
        ulong buildTicks = 0;
        ulong recordCopies = 0;
        ulong recordCopyTicks = 0;
        ulong encodedMessages = 0;
        ulong encodedBytes = 0;
        ulong codecTicks = 0;
        ulong queueAttempts = 0;
        ulong queueAccepted = 0;
        ulong queueTicks = 0;
        ulong acknowledgementMessages = 0;
        ulong acknowledgementTicks = 0;
        for (int index = 0; index < _bots.Length; index++)
        {
            RemoteWorldStateServerAttributionSnapshot adapter =
                _bots[index].WorldServer.GetAttributionSnapshot();
            scheduleCalls = SaturatingAdd(scheduleCalls, adapter.ScheduleCalls);
            buildTicks = SaturatingAdd(buildTicks, adapter.BuildTicks);
            recordCopies = SaturatingAdd(recordCopies, adapter.RecordCopies);
            recordCopyTicks = SaturatingAdd(recordCopyTicks, adapter.RecordCopyTicks);
            encodedMessages = SaturatingAdd(encodedMessages, adapter.EncodedMessages);
            encodedBytes = SaturatingAdd(encodedBytes, adapter.EncodedBytes);
            codecTicks = SaturatingAdd(codecTicks, adapter.CodecTicks);
            queueAttempts = SaturatingAdd(queueAttempts, adapter.QueueAttempts);
            queueAccepted = SaturatingAdd(queueAccepted, adapter.QueueAccepted);
            queueTicks = SaturatingAdd(queueTicks, adapter.QueueTicks);
            acknowledgementMessages = SaturatingAdd(
                acknowledgementMessages, adapter.AcknowledgementMessages);
            acknowledgementTicks = SaturatingAdd(
                acknowledgementTicks, adapter.AcknowledgementTicks);
        }
        return new(
            true,
            interest,
            stableFarFastPathEligibilityPercent,
            ToMilliseconds(interest.ExpirationSweepTicks),
            skippedDeferredDebtPerSelectedRecord,
            ToMilliseconds(interest.PendingSelectableTicks),
            ToMilliseconds(interest.PendingAlreadySelectedTicks),
            ToMilliseconds(interest.PendingCadenceDeferredTicks),
            ToMilliseconds(interest.PendingRetryDeferredTicks),
            ToMilliseconds(interest.PendingTraversalTicks),
            ToMilliseconds(tierOverheadTicks),
            ToMilliseconds(SaturatingAdd(
                interest.RecordMaterializationTicks,
                interest.BundleCommitTicks)),
            cadenceDelayHistogram,
            retryDelayHistogram,
            histogram,
            scheduleCalls,
            ToMilliseconds(buildTicks),
            recordCopies,
            ToMilliseconds(recordCopyTicks),
            encodedMessages,
            encodedBytes,
            ToMilliseconds(codecTicks),
            queueAttempts,
            queueAccepted,
            ToMilliseconds(queueTicks),
            acknowledgementMessages,
            ToMilliseconds(acknowledgementTicks),
            _worldStateAcknowledgementCensus.CreateReport());
    }

    private ReplicationProtocolTiming CaptureProtocolStageTiming()
    {
        ulong client = 0;
        ulong server = 0;
        for (int index = 0; index < _bots.Length; index++)
        {
            NetworkProtocolStageTiming clientTiming = _bots[index].Session.Client.GetStageTiming();
            NetworkProtocolStageTiming serverTiming = _bots[index].Session.Server.GetStageTiming();
            client = SaturatingAdd(client, SaturatingAdd(clientTiming.EmitTicks, clientTiming.InboundTicks));
            server = SaturatingAdd(server, SaturatingAdd(serverTiming.EmitTicks, serverTiming.InboundTicks));
        }
        SharedUdpListenerSnapshot listener = _host.GetSnapshot().Listener;
        server = SaturatingAdd(server, SaturatingAdd(listener.SendSystemCallTicks,
            SaturatingAdd(listener.ReceivePollTicks, listener.ReceiveSystemCallTicks)));
        return new(client, server);
    }

    private ReplicationServerProtocolAttribution CaptureServerProtocolAttribution()
    {
        if (!_phaseAttribution) return default;
        NetworkProtocolStageTiming server = default;
        for (int index = 0; index < _bots.Length; index++)
            server = Add(server, _bots[index].Session.Server.GetStageTiming());
        SharedUdpListenerSnapshot listener = _host.GetSnapshot().Listener;
        ulong inboundBookkeeping = Remaining(
            server.InboundTicks,
            server.EndpointReceiveTicks,
            server.SecureReceiveTicks,
            server.FrameAdmissionTicks);
        ulong emitBookkeeping = Remaining(
            server.EmitTicks,
            server.SelectionTicks,
            server.FrameEncodeTicks,
            server.SecureSendTicks,
            server.EndpointSendTicks);
        return new(
            true,
            ToMilliseconds(listener.ReceivePollTicks),
            ToMilliseconds(listener.ReceiveSystemCallTicks),
            ToMilliseconds(listener.ReceiveCensus.RoutingTicks),
            ToMilliseconds(server.EndpointReceiveTicks),
            ToMilliseconds(server.SecureReceiveTicks),
            ToMilliseconds(server.FrameAdmissionTicks),
            ToMilliseconds(inboundBookkeeping),
            ToMilliseconds(server.SelectionTicks),
            ToMilliseconds(server.FrameEncodeTicks),
            ToMilliseconds(server.SecureSendTicks),
            ToMilliseconds(server.EndpointSendTicks),
            ToMilliseconds(emitBookkeeping),
            ToMilliseconds(listener.SendSystemCallTicks),
            server.InboundCalls,
            server.EmptyInboundCalls,
            server.InboundDatagrams,
            server.EmitCalls,
            server.IdleEmitCalls,
            listener.ReceiveCensus);
    }

    private static NetworkProtocolStageTiming Add(
        NetworkProtocolStageTiming left,
        NetworkProtocolStageTiming right) => new(
            SaturatingAdd(left.EmitTicks, right.EmitTicks),
            SaturatingAdd(left.EmitCalls, right.EmitCalls),
            SaturatingAdd(left.IdleEmitCalls, right.IdleEmitCalls),
            SaturatingAdd(left.SelectionTicks, right.SelectionTicks),
            SaturatingAdd(left.FrameEncodeTicks, right.FrameEncodeTicks),
            SaturatingAdd(left.SecureSendTicks, right.SecureSendTicks),
            SaturatingAdd(left.EndpointSendTicks, right.EndpointSendTicks),
            SaturatingAdd(left.InboundTicks, right.InboundTicks),
            SaturatingAdd(left.InboundCalls, right.InboundCalls),
            SaturatingAdd(left.EmptyInboundCalls, right.EmptyInboundCalls),
            SaturatingAdd(left.InboundDatagrams, right.InboundDatagrams),
            SaturatingAdd(left.EndpointReceiveTicks, right.EndpointReceiveTicks),
            SaturatingAdd(left.SecureReceiveTicks, right.SecureReceiveTicks),
            SaturatingAdd(left.FrameAdmissionTicks, right.FrameAdmissionTicks),
            SaturatingAdd(left.ReliableCompactionTicks, right.ReliableCompactionTicks),
            SaturatingAdd(left.ReceivedCompactionTicks, right.ReceivedCompactionTicks));

    private static ulong Remaining(ulong total, params ulong[] parts)
    {
        ulong sum = 0;
        for (int index = 0; index < parts.Length; index++)
            sum = SaturatingAdd(sum, parts[index]);
        return total >= sum ? total - sum : 0;
    }

    private static void RecordPhase(List<double> destination, long started, bool measure)
    {
        if (measure) destination.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static double ToMilliseconds(long ticks) => ticks * 1_000d / Stopwatch.Frequency;
    private static double ToMilliseconds(ulong ticks) => ticks * 1_000d / Stopwatch.Frequency;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private TransportTotals CaptureTransportTotals()
    {
        TransportTotals total = default;
        for (int index = 0; index < _bots.Length; index++)
        {
            SharedUdpProtocolSessionSnapshot session = _bots[index].Session.GetSnapshot();
            total.Add(session.Client, session.ClientDatagrams);
            total.Add(session.Server, default);
        }
        SharedUdpListenerSnapshot listener = _host.GetSnapshot().Listener;
        total.WireDatagrams = checked(total.WireDatagrams + listener.Sent);
        total.WireBytes = checked(total.WireBytes + listener.SentBytes);
        total.CapacityDrops = checked(total.CapacityDrops + listener.CapacityDrops);
        total.SendCalls = checked(total.SendCalls + listener.SendCalls);
        return total;
    }

    private EventTotals CaptureEventTotals()
    {
        EventTotals total = default;
        for (int index = 0; index < _bots.Length; index++)
        {
            RemoteApplicationEventServerSnapshot snapshot = _bots[index].EventServer.GetSnapshot();
            total.RecordsExamined = checked(total.RecordsExamined + snapshot.EventRecordsExamined);
            total.RecordsSkipped = checked(total.RecordsSkipped + snapshot.EventRecordsSkipped);
        }
        return total;
    }

    private static ParticipantId Participant(int index) => new(checked((ulong)index + 1));

    private static GameProjectedEntity Projected(int index, int marker) => new(
        new ReplicationEntityId(checked((uint)index), 1),
        true,
        new GameSimulationState { TickMarker = marker, InputVelocity = marker });

    private static long SemanticMilliseconds(int tick) => checked(tick * 1_000L / TickHz);

    private static void WaitForRealtimeTick(ref long nextDeadline)
    {
        nextDeadline = checked(nextDeadline + Stopwatch.Frequency / TickHz);
        while (true)
        {
            long remaining = nextDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            if (remaining > Stopwatch.Frequency / 200)
            {
                int sleep = checked((int)Math.Max(1,
                    remaining * 1_000 / Stopwatch.Frequency - 1));
                Thread.Sleep(sleep);
            }
            else
                Thread.SpinWait(64);
        }
    }

    private static int LongestMissStreak(IReadOnlyList<double> values, double budget)
    {
        int current = 0;
        int longest = 0;
        for (int index = 0; index < values.Count; index++)
        {
            current = values[index] > budget ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bots is not null)
            for (int index = 0; index < _bots.Length; index++) _bots[index]?.Dispose();
        _host?.Dispose();
        _projection?.Dispose();
        _admission?.Dispose();
        _runtime?.Dispose();
    }

    private sealed class ReplicationBot : IDisposable
    {
        internal ReplicationBot(
            SharedUdpProtocolSession session,
            RemoteWorldStateServer worldServer,
            RemoteWorldStateClient worldClient,
            RemoteApplicationEventServer eventServer,
            RemoteApplicationEventClient eventClient)
        {
            Session = session;
            WorldServer = worldServer;
            WorldClient = worldClient;
            EventServer = eventServer;
            EventClient = eventClient;
        }

        internal SharedUdpProtocolSession Session { get; }
        internal RemoteWorldStateServer WorldServer { get; }
        internal RemoteWorldStateClient WorldClient { get; }
        internal RemoteApplicationEventServer EventServer { get; }
        internal RemoteApplicationEventClient EventClient { get; }

        public void Dispose()
        {
            EventServer.Dispose();
            Session.Dispose();
        }
    }

    private struct TransportTotals
    {
        internal ulong WireDatagrams;
        internal ulong WireBytes;
        internal ulong DataPackets;
        internal ulong AcknowledgementPackets;
        internal ulong Retransmissions;
        internal ulong RetransmittedWireBytes;
        internal ulong CapacityDrops;
        internal ulong ByteCapacityFailures;
        internal ulong Compactions;
        internal ulong CompactionBytesMoved;
        internal ulong SendCalls;
        internal long RetainedBytes;
        internal long RetainedCapacityBytes;
        internal int ReliableQueued;
        internal int IndependentQueued;
        internal int ReceivedQueued;
        internal int InFlightPackets;

        internal void Add(NetworkProtocolSnapshot protocol, UdpDatagramEndpointSnapshot datagrams)
        {
            DataPackets = checked(DataPackets + protocol.DataPacketsSent);
            AcknowledgementPackets = checked(AcknowledgementPackets + protocol.AcknowledgementPacketsSent);
            Retransmissions = checked(Retransmissions + protocol.ReliableRetransmissions);
            RetransmittedWireBytes = checked(RetransmittedWireBytes + protocol.ReliableRetransmittedWireBytes);
            CapacityDrops = checked(CapacityDrops + protocol.CapacityDrops + datagrams.CapacityDrops);
            WireDatagrams = checked(WireDatagrams + datagrams.Sent);
            WireBytes = checked(WireBytes + datagrams.SentBytes);
            SendCalls = checked(SendCalls + datagrams.SendCalls);
            NetworkRetainedPayloadSnapshot payload = protocol.RetainedPayloads;
            RetainedBytes = checked(RetainedBytes + payload.TotalBytes);
            RetainedCapacityBytes = checked(RetainedCapacityBytes + payload.TotalCapacityBytes);
            ReliableQueued = checked(ReliableQueued + protocol.ReliableQueued);
            IndependentQueued = checked(IndependentQueued + protocol.IndependentUnreliableQueued);
            ReceivedQueued = checked(ReceivedQueued + protocol.ReceivedQueued);
            InFlightPackets = checked(InFlightPackets + protocol.InFlightPackets);
            ByteCapacityFailures = checked(ByteCapacityFailures + payload.ByteCapacityFailures);
            Compactions = checked(Compactions + payload.ReliableCompactions + payload.ReceivedCompactions);
            CompactionBytesMoved = checked(CompactionBytesMoved + payload.ReliableCompactionBytesMoved +
                payload.ReceivedCompactionBytesMoved);
        }
    }

    private struct EventTotals
    {
        internal ulong RecordsExamined;
        internal ulong RecordsSkipped;
    }
}

internal sealed class LatencyHistogram
{
    private readonly double _width;
    private readonly double _percentileBucketOffset;
    private readonly long[] _buckets;
    private long _count;
    private double _sum;
    private double _max;

    internal LatencyHistogram(double width, int buckets, double percentileBucketOffset = 0.5)
    {
        _width = width;
        _percentileBucketOffset = percentileBucketOffset;
        _buckets = new long[buckets];
    }

    internal void Observe(double value)
    {
        if (!double.IsFinite(value) || value < 0) return;
        int bucket = Math.Min(_buckets.Length - 1, checked((int)(value / _width)));
        _buckets[bucket]++;
        _count++;
        _sum += value;
        _max = Math.Max(_max, value);
    }

    internal NetworkMetricSummary Summary() => _count == 0
        ? default
        : new(_sum / _count, Percentile(0.50), Percentile(0.95), Percentile(0.99), _max);

    internal void Clear()
    {
        Array.Clear(_buckets);
        _count = 0;
        _sum = 0;
        _max = 0;
    }

    private double Percentile(double percentile)
    {
        long target = Math.Max(1, checked((long)Math.Ceiling(_count * percentile)));
        long seen = 0;
        for (int index = 0; index < _buckets.Length; index++)
        {
            seen += _buckets[index];
            if (seen >= target) return (index + _percentileBucketOffset) * _width;
        }
        return _max;
    }
}

internal sealed class ReplicationBatchCensus
{
    private ulong _inspections;
    private ulong _nonempty;
    private ulong _twoOrMore;
    private ulong _threeOrMore;
    private ulong _readyFrames;
    private ulong _plaintextBytes;
    private ulong _greedyDatagrams;
    private int _maxFrames;

    internal void Add(NetworkUnreliableBatchOpportunity value)
    {
        _inspections++;
        if (value.ReadyFrames == 0) return;
        _nonempty++;
        if (value.ReadyFrames >= 2) _twoOrMore++;
        if (value.ReadyFrames >= 3) _threeOrMore++;
        _readyFrames = checked(_readyFrames + (ulong)value.ReadyFrames);
        _plaintextBytes = checked(_plaintextBytes + (ulong)value.PlaintextBytes);
        _greedyDatagrams = checked(_greedyDatagrams + (ulong)value.GreedyDatagrams);
        _maxFrames = Math.Max(_maxFrames, value.ReadyFrames);
    }

    internal void Clear()
    {
        _inspections = 0;
        _nonempty = 0;
        _twoOrMore = 0;
        _threeOrMore = 0;
        _readyFrames = 0;
        _plaintextBytes = 0;
        _greedyDatagrams = 0;
        _maxFrames = 0;
    }

    internal ReplicationBatchOpportunityReport CreateReport() => new(
        _inspections,
        _nonempty,
        _twoOrMore,
        _threeOrMore,
        _readyFrames,
        _plaintextBytes,
        _nonempty == 0 ? 0 : _readyFrames / (double)_nonempty,
        _nonempty == 0 ? 0 : _plaintextBytes / (double)_nonempty,
        _greedyDatagrams,
        _maxFrames);
}

internal sealed class WorldStateAcknowledgementCensus
{
    private const int AssumedMaximumEntriesPerBatch = 32;
    private readonly LatencyHistogram _produced = new(1, 65, percentileBucketOffset: 0);
    private readonly LatencyHistogram _pending = new(1, 65, percentileBucketOffset: 0);
    private ulong _flushCalls;
    private ulong _nonempty;
    private ulong _twoOrMore;
    private ulong _threeOrMore;
    private ulong _fourOrMore;
    private ulong _producedCount;
    private ulong _producedBytes;
    private ulong _applied;
    private ulong _rejected;
    private ulong _capacityExceeded;
    private ulong _theoreticalMessages;

    internal void Add(RemoteWorldStateAcknowledgementFlushOpportunity value)
    {
        _flushCalls++;
        _pending.Observe(value.PendingAcknowledgements);
        if (value.ProducedAcknowledgements == 0) return;
        _nonempty++;
        _produced.Observe(value.ProducedAcknowledgements);
        if (value.ProducedAcknowledgements >= 2) _twoOrMore++;
        if (value.ProducedAcknowledgements >= 3) _threeOrMore++;
        if (value.ProducedAcknowledgements >= 4) _fourOrMore++;
        _producedCount = checked(_producedCount + (ulong)value.ProducedAcknowledgements);
        _producedBytes = checked(_producedBytes + (ulong)value.EncodedBytes);
        _applied = checked(_applied + (ulong)value.Applied);
        _rejected = checked(_rejected + (ulong)value.Rejected);
        _capacityExceeded = checked(_capacityExceeded + (ulong)value.CapacityExceeded);
        _theoreticalMessages = checked(_theoreticalMessages +
            (ulong)((value.ProducedAcknowledgements + AssumedMaximumEntriesPerBatch - 1) /
                AssumedMaximumEntriesPerBatch));
    }

    internal void Clear()
    {
        _produced.Clear();
        _pending.Clear();
        _flushCalls = 0;
        _nonempty = 0;
        _twoOrMore = 0;
        _threeOrMore = 0;
        _fourOrMore = 0;
        _producedCount = 0;
        _producedBytes = 0;
        _applied = 0;
        _rejected = 0;
        _capacityExceeded = 0;
        _theoreticalMessages = 0;
    }

    internal WorldStateAcknowledgementCensusReport CreateReport() => new(
        _flushCalls,
        _nonempty,
        _produced.Summary(),
        _pending.Summary(),
        _twoOrMore,
        _threeOrMore,
        _fourOrMore,
        _producedCount,
        _producedBytes,
        _applied,
        _rejected,
        _capacityExceeded,
        _theoreticalMessages,
        AssumedMaximumEntriesPerBatch);
}

internal sealed record ReplicationSuiteReport(
    string Schema,
    DateTimeOffset TimestampUtc,
    string Runtime,
    int LogicalProcessors,
    string Methodology,
    IReadOnlyList<ReplicationWorkloadResult> Results);

internal sealed record ReplicationWorkloadResult(
    ReplicationWorkload Profile,
    int Relations,
    int MaximumRecipientScope,
    bool Converged,
    int SettleTicks,
    NetworkMetricSummary TickMilliseconds,
    NetworkMetricSummary DiagnosticProbeMilliseconds,
    NetworkMetricSummary ImmediateFreshnessMilliseconds,
    NetworkMetricSummary NearFreshnessMilliseconds,
    NetworkMetricSummary FarFreshnessMilliseconds,
    NetworkMetricSummary AllWorldStateFreshnessMilliseconds,
    NetworkMetricSummary ImmediateFreshnessTicks,
    NetworkMetricSummary NearFreshnessTicks,
    NetworkMetricSummary FarFreshnessTicks,
    NetworkMetricSummary EventApplicationMilliseconds,
    NetworkMetricSummary EventApplicationTicks,
    NetworkMetricSummary PerClientFreshnessP95Milliseconds,
    double WorstClientFreshnessP95Milliseconds,
    double WorstClientFreshnessMaximumMilliseconds,
    NetworkMetricSummary PendingRelations,
    NetworkMetricSummary RetainedPayloadBytes,
    NetworkMetricSummary AggregateReliableQueued,
    NetworkMetricSummary AggregateIndependentQueued,
    NetworkMetricSummary AggregateReceivedQueued,
    NetworkMetricSummary AggregateInFlightPackets,
    NetworkMetricSummary RetainedPayloadCapacityRatio,
    int NonviableRecipients,
    int TickDeadlineMisses,
    int LongestDeadlineMissStreak,
    ulong ApplicationEventsPublished,
    ulong ApplicationEventsApplied,
    ulong SemanticConstructions,
    ulong SemanticRecordCopies,
    ulong SemanticRecordBytes,
    ulong BundlesBuilt,
    ulong ApplicationEventRecordsExamined,
    ulong ApplicationEventRecordsSkipped,
    int ApplicationEventRetainedHighWater,
    ulong ApplicationEventMinimumCursorScanEntries,
    ulong ApplicationEventMinimumCursorFullRescans,
    ulong ApplicationEventMinimumCursorFastPathUpdates,
    ulong ApplicationEventConsumerLookupComparisons,
    ulong ApplicationEventConsumerLookupAttempts,
    ulong ApplicationEventParticipantIndexHits,
    ulong ApplicationEventNoRelevantFastForwards,
    ulong WireDatagrams,
    ulong WireBytes,
    ulong DataPackets,
    ulong AcknowledgementPackets,
    ulong Retransmissions,
    ulong RetransmittedWireBytes,
    ulong CapacityDrops,
    ulong RetainedByteCapacityFailures,
    ulong RetainedPayloadCompactions,
    ulong RetainedPayloadCompactionBytesMoved,
    long KnownReservedBytes,
    long PeakManagedHeapBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double ProcessCpuMilliseconds,
    double ProcessCpuCoreUtilization,
    SharedUdpListenerSnapshot ListenerBefore,
    SharedUdpListenerSnapshot ListenerAfter,
    ReplicationBatchOpportunityReport BatchOpportunity,
    ReplicationPhaseAttribution PhaseAttribution,
    ReplicationServerProtocolAttribution ServerProtocolAttribution,
    ReplicationWorldStateAttribution WorldStateAttribution,
    ReplicationAmplification Amplification);

internal readonly record struct ReplicationPhaseAttribution(
    bool Enabled,
    NetworkMetricSummary AuthorityUpdateMilliseconds,
    NetworkMetricSummary EventPublishMilliseconds,
    NetworkMetricSummary WorldStateScheduleMilliseconds,
    NetworkMetricSummary EventScheduleMilliseconds,
    NetworkMetricSummary ServerApplicationMilliseconds,
    NetworkMetricSummary ClientApplicationMilliseconds,
    NetworkMetricSummary CombinedProtocolPumpMilliseconds,
    NetworkMetricSummary ServerProtocolMilliseconds,
    NetworkMetricSummary ClientProtocolMilliseconds,
    NetworkMetricSummary ServerRoleTotalMilliseconds,
    NetworkMetricSummary ClientRoleTotalMilliseconds);

internal readonly record struct ReplicationServerProtocolAttribution(
    bool Enabled,
    double ListenerReceivePollMilliseconds,
    double ListenerReceiveSystemCallMilliseconds,
    double ListenerReceiveRoutingMilliseconds,
    double PeerEndpointReceiveMilliseconds,
    double PeerSecureReceiveMilliseconds,
    double PeerFrameAdmissionMilliseconds,
    double PeerInboundBookkeepingMilliseconds,
    double PeerSelectionMilliseconds,
    double PeerFrameEncodeMilliseconds,
    double PeerSecureSendMilliseconds,
    double ListenerOutboundAdmissionMilliseconds,
    double PeerEmitBookkeepingMilliseconds,
    double ListenerSendSystemCallMilliseconds,
    ulong PeerInboundCalls,
    ulong PeerEmptyInboundCalls,
    ulong PeerInboundDatagrams,
    ulong PeerEmitCalls,
    ulong PeerIdleEmitCalls,
    SharedUdpReceiveCensusSnapshot ReceiveCensus);

internal readonly record struct ReplicationWorldStateAttribution(
    bool Enabled,
    GameInterestAttributionSnapshot Interest,
    double StableFarFastPathEligibilityPercent,
    double ExpirationSweepMilliseconds,
    double SkippedDeferredDebtPerSelectedRecord,
    double PendingSelectableMilliseconds,
    double PendingAlreadySelectedMilliseconds,
    double PendingCadenceDeferredMilliseconds,
    double PendingRetryDeferredMilliseconds,
    double PendingTraversalMilliseconds,
    double TierSelectionOverheadMilliseconds,
    double RecordMaterializationAndBundleCommitMilliseconds,
    IReadOnlyList<ulong>? CadenceDeferredDelayHistogram,
    IReadOnlyList<ulong>? RetryDeferredDelayHistogram,
    IReadOnlyList<ulong>? BundleRecordHistogram,
    ulong AdapterScheduleCalls,
    double AdapterBuildMilliseconds,
    ulong AdapterRecordCopies,
    double AdapterRecordCopyMilliseconds,
    ulong AdapterEncodedMessages,
    ulong AdapterEncodedBytes,
    double AdapterCodecMilliseconds,
    ulong AdapterQueueAttempts,
    ulong AdapterQueueAccepted,
    double AdapterQueueMilliseconds,
    ulong AdapterAcknowledgementMessages,
    double AdapterAcknowledgementMilliseconds,
    WorldStateAcknowledgementCensusReport AcknowledgementCensus);

internal readonly record struct WorldStateAcknowledgementCensusReport(
    ulong FlushCalls,
    ulong NonemptyProducedFlushes,
    NetworkMetricSummary ProducedPerNonemptyFlush,
    NetworkMetricSummary PendingPerFlush,
    ulong FlushesWithTwoOrMoreProduced,
    ulong FlushesWithThreeOrMoreProduced,
    ulong FlushesWithFourOrMoreProduced,
    ulong ProducedAcknowledgements,
    ulong ProducedEncodedBytes,
    ulong Applied,
    ulong Rejected,
    ulong CapacityExceeded,
    ulong TheoreticalGreedyBatchMessages,
    int AssumedMaximumEntriesPerBatch);

internal readonly record struct ReplicationProtocolTiming(ulong ClientTicks, ulong ServerTicks)
{
    internal ReplicationProtocolTiming Subtract(ReplicationProtocolTiming baseline) => new(
        ClientTicks >= baseline.ClientTicks ? ClientTicks - baseline.ClientTicks : 0,
        ServerTicks >= baseline.ServerTicks ? ServerTicks - baseline.ServerTicks : 0);
}

internal readonly record struct ReplicationBatchOpportunityReport(
    ulong Inspections,
    ulong NonemptyInspections,
    ulong InspectionsWithTwoOrMoreFrames,
    ulong InspectionsWithThreeOrMoreFrames,
    ulong ReadyFrames,
    ulong PlaintextBytes,
    double MeanReadyFramesWhenNonempty,
    double MeanPlaintextBytesWhenNonempty,
    ulong TheoreticalGreedyDatagrams,
    int MaximumReadyFrames);

internal readonly record struct ReplicationAmplification(
    double RecipientCopiesPerSemanticConstruction,
    double BundlesPerRecipientCopy,
    double DataPacketsPerBundle,
    double WireBytesPerRecipientCopy,
    double WireBytesPerWorldSemanticByte,
    double MeanWireBytesPerDatagram,
    double RetransmittedWireFraction);

internal enum ReplicationTraffic : byte
{
    Full = 0,
    WorldStateOnly = 1,
    EventOnly = 2,
    UpdatesOnly = 3,
}

internal sealed record ReplicationWorkload(
    string Id,
    int Clients,
    int Entities,
    int Ticks,
    int WarmupTicks,
    int DirtyPercent,
    int SpacingMillimeters,
    string SpatialShape,
    ReplicationTraffic Traffic = ReplicationTraffic.Full)
{
    internal static ReplicationWorkload Dense(int clients, int ticks, int warmup, int dirty) =>
        new("dense_mix", clients, 1_000, ticks, warmup, dirty, 4_000,
            "One 126 m hotspot; all recipients distributed through the hotspot; every entity lies inside the 192 m Far envelope.");

    internal static ReplicationWorkload Spread(int clients, int ticks, int warmup, int dirty) =>
        new("spread_mix", clients, 5_000, ticks, warmup, dirty, 12_000,
            "One 768 m distributed grid; recipients are distributed through the world; maintained relations follow local 192 m envelopes.");

    internal ReplicationWorkload WithTraffic(ReplicationTraffic traffic) => this with
    {
        Id = traffic == ReplicationTraffic.Full
            ? Id
            : $"{Id}/{traffic.ToString().ToLowerInvariant()}",
        Traffic = traffic,
    };

    internal bool WorldStateEnabled =>
        Traffic is ReplicationTraffic.Full or ReplicationTraffic.WorldStateOnly;

    internal bool EventsEnabled =>
        Traffic is ReplicationTraffic.Full or ReplicationTraffic.EventOnly;

    internal ReplicationSpatialPoint EntityPoint(int entity)
    {
        int width = checked((int)Math.Ceiling(Math.Sqrt(Entities)));
        return new(
            checked((entity % width) * SpacingMillimeters),
            checked((entity / width) * SpacingMillimeters));
    }

    internal ReplicationSpatialPoint Anchor(int client)
    {
        int width = checked((int)Math.Ceiling(Math.Sqrt(Entities)));
        int entity = Clients == 1 ? 0 : checked(client * (Entities - 1) / (Clients - 1));
        return new(
            checked((entity % width) * SpacingMillimeters),
            checked((entity / width) * SpacingMillimeters));
    }
}
