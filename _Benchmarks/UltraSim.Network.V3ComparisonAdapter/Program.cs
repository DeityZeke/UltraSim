using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;
using UltraSim.Network.Comparison;

if (args.Contains("--replication-suite", StringComparer.Ordinal))
{
    DenseReplicationSuite.Run(args);
    return;
}

if (args.Contains("--p03-whole-flow", StringComparer.Ordinal))
{
    P03WholeFlowNetworkSuite.Run(args);
    return;
}

if (args.Contains("--pf5-r2-r2-scaling", StringComparer.Ordinal))
{
    Pf5R2R2ScalingAttribution.Run(args);
    return;
}

if (args.Contains("--pf5-r4-sort-micro", StringComparer.Ordinal))
{
    Pf5R4SortMicrobenchmark.Run(args);
    return;
}

AdapterOptions options = AdapterOptions.Parse(args);
if (options.CadenceSuite)
{
    RunCadenceSuite(options);
    return;
}
NetworkBotScenario scenario = options.CreateScenario();
if (options.DiagnosticSuite)
{
    RunDiagnosticSuite(options, scenario);
    return;
}
if (options.PooledTailWindows > 0)
{
    RunPooledTail(options, scenario);
    return;
}
using var harness = new V3OwnerMovementHarness(
    scenario, options.Topology,
    new V3HarnessProfile(options.Layer, options.ActiveRatio, options.DeepTiming),
    options.SharedSocketIoMode,
    sparseRegionEdgeMillimeters: options.SparseRegionEdgeMillimeters,
    sparseCapacityMode: options.SparseCapacityMode,
    sparseExactAdmission: options.SparseExactAdmission,
    unreliableFrameAggregation: options.UnreliableFrameAggregation,
    compactionSettings: options.CompactionSettings);
NetworkBotStageResult result = harness.Run();
NetworkBotComparisonIo.Write(options.Output, NetworkBotAdapterReport.Create(result.Generation, result));
if (options.CompactionSettings.Enabled)
{
    bool compactionAccepted = CompactionAcceptancePassed(result);
    Console.WriteLine($"N0.10 compaction acceptance: {compactionAccepted}");
    if (!compactionAccepted)
        throw new InvalidOperationException(
            "Long-lived WorldState compaction run failed one or more bounded acceptance gates.");
}
Console.WriteLine($"{result.Generation} {scenario.Id}/{scenario.Clients}: tick p95 {result.TickMilliseconds.P95:F3} ms, " +
    $"ping p95 {result.PingMilliseconds.P95:F3} ms, reserved {result.ReservedNetworkBytes / 1048576d:F2} MiB");
Console.WriteLine(Path.GetFullPath(options.Output));

static bool CompactionAcceptancePassed(NetworkBotStageResult result)
{
    double Diagnostic(string key) => result.Diagnostics.TryGetValue(key, out double value) ? value : 0;
    return result.CapacityDrops == 0 &&
        Diagnostic("retained_payload_byte_capacity_failures") == 0 &&
        Diagnostic("shared_listener_capacity_drops") == 0 &&
        Diagnostic("world_state_nonviable_recipients") == 0 &&
        Diagnostic("world_state_compaction_scheduler_pending") == 0 &&
        Diagnostic("world_state_compaction_baselines_in_progress") == 0 &&
        Diagnostic("world_state_compaction_requests_rejected") == 0 &&
        Diagnostic("world_state_compaction_scheduler_requests_rejected") == 0 &&
        Diagnostic("world_state_baselines_rejected") == 0 &&
        Diagnostic("world_state_baselines_timed_out") == 0 &&
        Diagnostic("world_state_baselines_abandoned") == 0 &&
        Diagnostic("world_state_compaction_scheduler_started") ==
            Diagnostic("world_state_baselines_applied");
}

static void RunPooledTail(AdapterOptions options, NetworkBotScenario scenario)
{
    if (!scenario.ObserverState)
        throw new ArgumentException("Pooled tail mode is admitted only for the sparse observer workload.");
    if (options.PooledTailWindows <= 0)
        throw new ArgumentOutOfRangeException(nameof(options.PooledTailWindows));

    var tickSamples = new List<double>(checked(options.PooledTailWindows * scenario.StageTicks));
    var serverSamples = new List<double>(checked(options.PooledTailWindows * scenario.StageTicks));
    var windows = new List<PooledTailWindowReport>(options.PooledTailWindows);
    var setupMilliseconds = new List<double>(options.PooledTailWindows);
    string? canonicalHash = null;
    bool allWindowsAccepted = true;
    ulong totalCapacityDrops = 0;
    double maximumNonviable = 0;
    double maximumByteFailures = 0;
    long totalGen0 = 0;
    long totalGen1 = 0;
    long totalGen2 = 0;
    long totalDeadlineMisses = 0;
    long maximumDeadlineMisses = 0;
    ulong totalRetransmissions = 0;
    ulong maximumRetransmissions = 0;
    long totalAllocatedBytes = 0;
    long maximumAllocatedBytes = 0;
    for (int index = 0; index < options.PooledTailWindows; index++)
    {
        long setupStarted = Stopwatch.GetTimestamp();
        using var harness = new V3OwnerMovementHarness(
            scenario, options.Topology,
            new V3HarnessProfile(options.Layer, options.ActiveRatio, options.DeepTiming),
            options.SharedSocketIoMode,
            sparseRegionEdgeMillimeters: options.SparseRegionEdgeMillimeters,
            sparseCapacityMode: options.SparseCapacityMode,
            sparseExactAdmission: options.SparseExactAdmission,
            unreliableFrameAggregation: options.UnreliableFrameAggregation,
            compactionSettings: options.CompactionSettings);
        setupMilliseconds.Add(Stopwatch.GetElapsedTime(setupStarted).TotalMilliseconds);
        NetworkBotStageResult result = harness.Run();
        if (result.TickSamples is null || result.ServerPumpSamples is null)
            throw new InvalidOperationException("Pooled tail mode requires raw stage samples.");
        tickSamples.AddRange(result.TickSamples);
        serverSamples.AddRange(result.ServerPumpSamples);
        canonicalHash ??= result.CanonicalOwnerStateHash;
        bool canonical = StringComparer.Ordinal.Equals(canonicalHash, result.CanonicalOwnerStateHash);
        double nonviable = Diagnostic(result, "world_state_nonviable_recipients");
        double byteFailures = Diagnostic(result, "retained_payload_byte_capacity_failures");
        bool accepted = canonical && result.CapacityDrops == 0 && nonviable == 0 && byteFailures == 0;
        allWindowsAccepted &= accepted;
        totalCapacityDrops = checked(totalCapacityDrops + result.CapacityDrops);
        maximumNonviable = Math.Max(maximumNonviable, nonviable);
        maximumByteFailures = Math.Max(maximumByteFailures, byteFailures);
        totalGen0 += result.Gen0Collections;
        totalGen1 += result.Gen1Collections;
        totalGen2 += result.Gen2Collections;
        totalDeadlineMisses = checked(totalDeadlineMisses + result.TickDeadlineMisses);
        maximumDeadlineMisses = Math.Max(maximumDeadlineMisses, result.TickDeadlineMisses);
        totalRetransmissions = checked(totalRetransmissions + result.RetransmittedPackets);
        maximumRetransmissions = Math.Max(maximumRetransmissions, result.RetransmittedPackets);
        totalAllocatedBytes = checked(totalAllocatedBytes + result.AllocatedBytes);
        maximumAllocatedBytes = Math.Max(maximumAllocatedBytes, result.AllocatedBytes);
        windows.Add(new(
            index + 1,
            result.TickMilliseconds.P95,
            result.ServerPumpMilliseconds.P95,
            result.ImmediateWorldStateTicks.P95,
            result.NearWorldStateTicks.P95,
            result.FarWorldStateTicks.P95,
            result.InputAcknowledgementMilliseconds.P95,
            Diagnostic(result, "worst_client_input_ack_p95_ms"),
            result.PerClientWorldStateP95Milliseconds.Max,
            result.TickDeadlineMisses,
            result.RetransmittedPackets,
            result.AllocatedBytes,
            result.CapacityDrops,
            nonviable,
            byteFailures,
            result.Gen0Collections + result.Gen1Collections + result.Gen2Collections,
            result.CanonicalOwnerStateHash,
            Diagnostic(result, "unreliable_frames_consumed"),
            Diagnostic(result, "unreliable_data_datagrams_sent"),
            Diagnostic(result, "unreliable_sent_datagrams_two_or_more_frames"),
            accepted));
        Console.WriteLine($"POOLED WINDOW {index + 1}/{options.PooledTailWindows}: " +
            $"tick p95 {result.TickMilliseconds.P95:F3} ms, " +
            $"server p95 {result.ServerPumpMilliseconds.P95:F3} ms, accepted {accepted}");
    }

    var report = new PooledTailReport(
        "ultrasim-network-v3-pooled-tail-v1",
        DateTimeOffset.UtcNow,
        scenario,
        options.PooledTailWindows,
        tickSamples.Count,
        options.UnreliableFrameAggregation,
        options.SparseCapacityMode.ToString(),
        PooledMetricSummary.From(tickSamples),
        PooledMetricSummary.From(serverSamples),
        windows,
        allWindowsAccepted,
        canonicalHash ?? string.Empty,
        new(
            windows.Max(window => window.ImmediateWorldStateTicksP95),
            windows.Max(window => window.NearWorldStateTicksP95),
            windows.Max(window => window.FarWorldStateTicksP95),
            windows.Max(window => window.InputAcknowledgementP95Milliseconds),
            windows.Max(window => window.WorstClientInputAcknowledgementP95Milliseconds),
            windows.Max(window => window.WorstClientWorldStateP95Milliseconds)),
        new(totalCapacityDrops, maximumNonviable, maximumByteFailures,
            totalGen0, totalGen1, totalGen2,
            totalDeadlineMisses, maximumDeadlineMisses,
            totalRetransmissions, maximumRetransmissions,
            totalAllocatedBytes, maximumAllocatedBytes),
        new(
            SumDiagnostic(windows, resultKey: "unreliable_frames_consumed"),
            SumDiagnostic(windows, resultKey: "unreliable_data_datagrams_sent"),
            SumDiagnostic(windows, resultKey: "unreliable_sent_datagrams_two_or_more_frames")),
        setupMilliseconds.Sum(),
        setupMilliseconds.Max());
    NetworkBotComparisonIo.Write(options.Output, report);
    Console.WriteLine($"POOLED TAIL: {tickSamples.Count} measured ticks; " +
        $"tick p50/p95/p99/p99.9/max {report.TickMilliseconds.P50:F3}/" +
        $"{report.TickMilliseconds.P95:F3}/{report.TickMilliseconds.P99:F3}/" +
        $"{report.TickMilliseconds.P999:F3}/{report.TickMilliseconds.Max:F3} ms; " +
        $"server p95 {report.ServerPumpMilliseconds.P95:F3} ms; accepted {allWindowsAccepted}");
    Console.WriteLine(Path.GetFullPath(options.Output));

    static double Diagnostic(NetworkBotStageResult result, string key) =>
        result.Diagnostics.TryGetValue(key, out double value) ? value : 0;

    static double SumDiagnostic(
        IReadOnlyList<PooledTailWindowReport> reports,
        string resultKey) => resultKey switch
        {
            "unreliable_frames_consumed" => reports.Sum(report => report.UnreliableFramesConsumed),
            "unreliable_data_datagrams_sent" => reports.Sum(report => report.UnreliableDataDatagramsSent),
            _ => reports.Sum(report => report.UnreliableDatagramsTwoOrMoreFrames),
        };
}

static void RunCadenceSuite(AdapterOptions options)
{
    V3CadenceProfile[] cases =
    [
        V3CadenceProfile.Fixed("fixed-60-60", 60, 60),
        V3CadenceProfile.Fixed("network-outruns-60-120", 60, 120),
        V3CadenceProfile.Fixed("network-outruns-60-240", 60, 240),
        V3CadenceProfile.Fixed("reduced-30-60", 30, 60),
        V3CadenceProfile.Fixed("mismatch-30-120", 30, 120),
        V3CadenceProfile.Fixed("extreme-20-120", 20, 120),
        V3CadenceProfile.Fixed("authority-outruns-120-60", 120, 60),
        V3CadenceProfile.Transition("transition-60-30-60", 60, 30, 120),
        V3CadenceProfile.Transition("transition-60-20-60", 60, 20, 120),
        V3CadenceProfile.Stalled("authority-stall-250ms", V3CadenceStall.Authority),
        V3CadenceProfile.Stalled("network-stall-250ms", V3CadenceStall.Network),
    ];
    if (options.CadenceCase is not null)
        cases = cases.Where(value => StringComparer.OrdinalIgnoreCase.Equals(
            value.Id, options.CadenceCase)).ToArray();
    if (cases.Length == 0)
        throw new ArgumentException($"Unknown cadence case '{options.CadenceCase}'.");
    var observations = new List<V3CadenceObservation>(cases.Length);
    foreach (V3CadenceProfile cadence in cases)
    {
        var scenario = new NetworkBotScenario(
            "owner_movement",
            options.Clients,
            cadence.WallClockHz,
            InputHz: 20,
            StageTicks: checked(cadence.WallClockHz * cadence.DurationSeconds),
            SettleTicks: Math.Max(30, cadence.WallClockHz / 2),
            SpawnSpacingMeters: options.SpawnSpacing,
            ObserverState: false,
            OrderedEvents: false).Validate();
        using var harness = new V3OwnerMovementHarness(
            scenario,
            options.Topology,
            new V3HarnessProfile(V3DiagnosticLayer.Full, 1, DeepTiming: false),
            options.SharedSocketIoMode,
            cadence);
        NetworkBotStageResult result = harness.Run();
        double ownerPublicationsPerStep =
            result.Diagnostics["owner_publications_per_authoritative_step_per_client"];
        if (ownerPublicationsPerStep != 1)
            throw new InvalidOperationException(
                $"Cadence case '{cadence.Id}' duplicated or omitted owner publication work: {ownerPublicationsPerStep:R} per authoritative step/client.");
        long expectedInputs = checked((long)options.Clients * 20 * cadence.DurationSeconds);
        if (result.LogicalInputs != expectedInputs)
            throw new InvalidOperationException(
                $"Cadence case '{cadence.Id}' admitted {result.LogicalInputs} input samples; expected {expectedInputs}.");
        if (result.CapacityDrops != 0)
            throw new InvalidOperationException(
                $"Cadence case '{cadence.Id}' exceeded an admitted bounded capacity ({result.CapacityDrops} drops).");
        if (cadence.HasStall && result.Diagnostics["stall_recovery_observed"] != 1)
            throw new InvalidOperationException(
                $"Cadence case '{cadence.Id}' failed to recover within its bounded observation.");
        observations.Add(new(cadence, result));
        Console.WriteLine($"CADENCE {cadence.Id}: authority " +
            $"{result.Diagnostics["authority_steps_per_wall_second"]:F1}/s, network " +
            $"{result.Diagnostics["network_pumps_per_wall_second"]:F1}/s, " +
            $"ack p95 {result.InputAcknowledgementMilliseconds.P95:F2} ms, " +
            $"max owner age {result.Diagnostics["owner_state_age_max_ticks"]:F0} steps, " +
            $"recovery {result.Diagnostics["stall_recovery_wall_ticks"]:F0} wall ticks, " +
            $"canonical {result.CanonicalOwnerStateHash}");
    }
    string canonicalHash = observations[0].Result.CanonicalOwnerStateHash;
    bool canonicalMatches = observations.All(observation =>
        StringComparer.Ordinal.Equals(observation.Result.CanonicalOwnerStateHash, canonicalHash));
    if (!canonicalMatches)
    {
        for (int observationIndex = 1; observationIndex < observations.Count; observationIndex++)
        {
            V3CadenceObservation candidate = observations[observationIndex];
            for (int client = 0; client < observations[0].Result.CanonicalOwnerStates.Count; client++)
            {
                NetworkCanonicalOwnerState control = observations[0].Result.CanonicalOwnerStates[client];
                NetworkCanonicalOwnerState current = candidate.Result.CanonicalOwnerStates[client];
                if (control.LastProcessedInputSequence != current.LastProcessedInputSequence ||
                    control.VelocityXBits != current.VelocityXBits ||
                    control.VelocityYBits != current.VelocityYBits)
                {
                    Console.Error.WriteLine(
                        $"CADENCE MISMATCH {candidate.Cadence.Id} client {client}: " +
                        $"control={control}, candidate={current}");
                    break;
                }
            }
        }
        throw new InvalidOperationException(
            "Cadence acceptance cases did not converge to the same canonical owner state.");
    }
    var report = new V3CadenceAcceptanceReport(
        "ultrasim-network-v3-cadence-acceptance-v1",
        DateTimeOffset.UtcNow,
        options.Topology,
        options.SharedSocketIoMode,
        "Three wall-clock seconds per case; 20 Hz input sampling; independent fixed-point authority and Network pump accumulators; 250 ms one-sided stalls begin after one second. Fixed-60 comparison behavior is unchanged.",
        canonicalMatches,
        canonicalHash,
        observations);
    NetworkBotComparisonIo.Write(options.Output, report);
    Console.WriteLine(Path.GetFullPath(options.Output));
}

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
                options.SharedSocketIoMode,
                sparseRegionEdgeMillimeters: options.SparseRegionEdgeMillimeters,
                sparseCapacityMode: options.SparseCapacityMode,
                sparseExactAdmission: options.SparseExactAdmission,
                unreliableFrameAggregation: options.UnreliableFrameAggregation,
                compactionSettings: options.CompactionSettings);
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

internal readonly record struct PooledMetricSummary(
    double Mean,
    double P50,
    double P95,
    double P99,
    double P999,
    double Max)
{
    internal static PooledMetricSummary From(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return default;
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        return new(sorted.Average(), Percentile(sorted, .50), Percentile(sorted, .95),
            Percentile(sorted, .99), Percentile(sorted, .999), sorted[^1]);
    }

    private static double Percentile(ReadOnlySpan<double> sorted, double percentile)
    {
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}

internal sealed record PooledTailReport(
    string Schema,
    DateTimeOffset RecordedUtc,
    NetworkBotScenario Scenario,
    int WindowCount,
    int MeasuredTicks,
    bool UnreliableFrameAggregation,
    string SparseCapacityProfile,
    PooledMetricSummary TickMilliseconds,
    PooledMetricSummary ServerPumpMilliseconds,
    IReadOnlyList<PooledTailWindowReport> Windows,
    bool AllWindowsAccepted,
    string CanonicalOwnerStateHash,
    PooledTailFreshnessSummary Freshness,
    PooledTailPressureSummary Pressure,
    PooledTailAggregationSummary Aggregation,
    double ResetSetupMillisecondsTotal,
    double ResetSetupMillisecondsMaximum);

internal sealed record PooledTailFreshnessSummary(
    double ImmediateWorldStateTicksP95Maximum,
    double NearWorldStateTicksP95Maximum,
    double FarWorldStateTicksP95Maximum,
    double InputAcknowledgementP95MillisecondsMaximum,
    double WorstClientInputAcknowledgementP95MillisecondsMaximum,
    double WorstClientWorldStateP95MillisecondsMaximum);

internal sealed record PooledTailPressureSummary(
    ulong CapacityDrops,
    double MaximumNonviableRecipients,
    double MaximumByteCapacityFailures,
    long Gen0Collections,
    long Gen1Collections,
    long Gen2Collections,
    long TotalTickDeadlineMisses,
    long MaximumTickDeadlineMisses,
    ulong TotalRetransmittedPackets,
    ulong MaximumRetransmittedPackets,
    long TotalAllocatedBytes,
    long MaximumAllocatedBytes);

internal sealed record PooledTailAggregationSummary(
    double UnreliableFramesConsumed,
    double UnreliableDataDatagramsSent,
    double UnreliableDatagramsTwoOrMoreFrames);

internal sealed record PooledTailWindowReport(
    int Window,
    double TickP95Milliseconds,
    double ServerP95Milliseconds,
    double ImmediateWorldStateTicksP95,
    double NearWorldStateTicksP95,
    double FarWorldStateTicksP95,
    double InputAcknowledgementP95Milliseconds,
    double WorstClientInputAcknowledgementP95Milliseconds,
    double WorstClientWorldStateP95Milliseconds,
    long TickDeadlineMisses,
    ulong RetransmittedPackets,
    long AllocatedBytes,
    ulong CapacityDrops,
    double NonviableRecipients,
    double ByteCapacityFailures,
    int GarbageCollections,
    string CanonicalOwnerStateHash,
    double UnreliableFramesConsumed,
    double UnreliableDataDatagramsSent,
    double UnreliableDatagramsTwoOrMoreFrames,
    bool Accepted);

internal sealed class V3OwnerMovementHarness : IDisposable
{
    private const int SparseWorldRecordsPerBundle = 24;
    private const int SparseWorldByteBudget = 1_200;
    private const ulong SparseApplicationEpoch = 1;
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
    private static readonly NetworkProtocolManifest SparseManifest = NetworkProtocolManifest.Create(
        10,
        [
            InputDescriptor,
            OwnerDescriptor,
            PingDescriptor,
            PongDescriptor,
            RemoteWorldStateBundleHeaderCodec.Descriptor with
            {
                MaximumEncodedBytes = RemoteWorldStateBundleHeaderCodec.EncodedSize +
                    SparseWorldRecordsPerBundle * RemoteWorldStateRecord.EncodedSize,
            },
            RemoteWorldStateAppliedMessageCodec.Descriptor,
        ]);
    private static readonly NetworkProtocolManifest SparseAggregatedManifest =
        NetworkProtocolManifest.Create(
            11, SparseManifest.Messages.ToArray(),
            maximumUnreliableDataFramesPerDatagram: 7);
    private static readonly NetworkProtocolManifest SparseCompactionManifest =
        NetworkProtocolManifest.Create(
            12,
            [
                InputDescriptor,
                OwnerDescriptor,
                PingDescriptor,
                PongDescriptor,
                RemoteWorldStateBundleHeaderCodec.Descriptor with
                {
                    MaximumEncodedBytes = RemoteWorldStateBundleHeaderCodec.EncodedSize +
                        SparseWorldRecordsPerBundle * RemoteWorldStateRecord.EncodedSize,
                },
                RemoteWorldStateAppliedMessageCodec.Descriptor,
                RemoteWorldBaselineBeginMessageCodec.Descriptor,
                RemoteWorldBaselineSegmentHeaderCodec.Descriptor with
                {
                    MaximumEncodedBytes = UltraSimGameProtocol.MaximumMessageBytes,
                },
                RemoteWorldBaselineCommitMessageCodec.Descriptor,
                RemoteWorldBaselineAppliedMessageCodec.Descriptor,
                RemoteWorldBaselineCancelMessageCodec.Descriptor,
                RemoteWorldStateCompactionRequestMessageCodec.Descriptor,
            ]);
    private static readonly NetworkProtocolManifest SparseCompactionAggregatedManifest =
        NetworkProtocolManifest.Create(
            13, SparseCompactionManifest.Messages.ToArray(),
            maximumUnreliableDataFramesPerDatagram: 7);
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
    private static readonly NetworkProtocolLimits SparseProtocolLimits = new(
        MaximumReliableMessages: 16,
        MaximumReceivedMessages: 32,
        MaximumMessageBytes: 1_472,
        MaximumFragmentsPerMessage: 2,
        SentPacketCapacity: 128,
        MaximumInboundPacketsPerPump: 64,
        MaximumPacketsPerPump: 64,
        MinimumRetransmissionSteps: 2,
        MaximumRetransmissionSteps: 64,
        InitialCongestionWindowPackets: 8,
        MaximumCongestionWindowPackets: 64,
        MaximumIndependentUnreliableMessages: 8,
        ReliablePayloadByteCapacity: 16_384,
        ReceivedPayloadByteCapacity: 32_768);
    private static readonly NetworkProtocolLimits SparseAggregatedProtocolLimits =
        SparseProtocolLimits with { MaximumUnreliableFramesPerDatagram = 7 };
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
    private readonly NetworkProtocolManifest _manifest;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly bool _unreliableFrameAggregation;
    private readonly SparseCompactionSettings _compactionSettings;
    private readonly SparseObserverWorkload? _observer;
    private readonly V3CadenceProfile? _cadence;
    private readonly int _activeBots;
    private readonly int _sparseRegionEdgeMillimeters;
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
        SharedUdpSocketIoMode sharedSocketIoMode = SharedUdpSocketIoMode.Scalar,
        V3CadenceProfile? cadence = null,
        int sparseRegionEdgeMillimeters = 10_000,
        SparseCapacityMode sparseCapacityMode = SparseCapacityMode.Auto,
        bool sparseExactAdmission = false,
        bool unreliableFrameAggregation = false,
        SparseCompactionSettings compactionSettings = default)
    {
        _scenario = scenario.Validate();
        compactionSettings.Validate();
        _topology = topology;
        _sharedSocketIoMode = sharedSocketIoMode;
        _profile = profile.Validate();
        if (unreliableFrameAggregation && !_scenario.ObserverState)
            throw new ArgumentException(
                "Unreliable frame aggregation is only admitted by the sparse observer profile.",
                nameof(unreliableFrameAggregation));
        if (sparseRegionEdgeMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(sparseRegionEdgeMillimeters));
        _sparseRegionEdgeMillimeters = sparseRegionEdgeMillimeters;
        _unreliableFrameAggregation = unreliableFrameAggregation;
        _compactionSettings = compactionSettings;
        _manifest = scenario.ObserverState
            ? compactionSettings.Enabled
                ? unreliableFrameAggregation ? SparseCompactionAggregatedManifest : SparseCompactionManifest
                : unreliableFrameAggregation ? SparseAggregatedManifest : SparseManifest
            : Manifest;
        _protocolLimits = scenario.ObserverState
            ? unreliableFrameAggregation ? SparseAggregatedProtocolLimits : SparseProtocolLimits
            : ProtocolLimits;
        _cadence = cadence?.Validate();
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
                _protocolLimits, UdpLimits,
                new SharedUdpProtocolLimits(
                    scenario.Clients,
                    MaximumQueuedDatagramsPerSession: 4,
                    MaximumInboundDatagramsPerPump: checked(scenario.Clients * 16),
                    SocketIoMode: sharedSocketIoMode,
                    MaximumNativeSendBatchDatagrams: 64),
                _manifest, _identity, trusted);
        int width = scenario.SpawnGridWidth;
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
                        _protocolLimits, UdpLimits, out pair, out _,
                        _manifest, _identity, trusted) || pair is null)
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
                Math.Max(8, scenario.StageTicks / (scenario.TickHz / scenario.InputHz) + 8),
                scenario.StageTicks);
        }
        if (scenario.ObserverState)
        {
            _observer = new SparseObserverWorkload(
                scenario, _bots, PumpAll, SparseApplicationEpoch,
                SparseWorldRecordsPerBundle, SparseWorldByteBudget,
                _sparseRegionEdgeMillimeters, sparseCapacityMode,
                sparseExactAdmission
                    ? GameInterestAdmissionMode.ExactDistance
                    : GameInterestAdmissionMode.CoarseEnvelope,
                compactionSettings);
            _observer.DrainInitialState();
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
        _observer?.ResetMeasurement();
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
        var reliableRetainedBytes = new List<double>(_scenario.StageTicks);
        var independentRetainedBytes = new List<double>(_scenario.StageTicks);
        var receivedRetainedBytes = new List<double>(_scenario.StageTicks);
        var totalRetainedBytes = new List<double>(_scenario.StageTicks);
        List<double>? intentQueueMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? serverIngressMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? authorityMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? interestUpdateMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? ownerQueueMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? worldScheduleMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? forwardPumpMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? clientApplyMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? returnPumpMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? serverProtocolMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? clientProtocolMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? serverRoleMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        List<double>? clientRoleMs = _profile.DeepTiming ? new(_scenario.StageTicks) : null;
        long logicalInputs = 0;
        long ownerApplied = 0;
        long ownerMessagesQueued = 0;
        long authoritySteps = 0;
        long networkPumps = 0;
        int inputAccumulator = 0;
        int authorityAccumulator = 0;
        int networkAccumulator = 0;
        int cadenceInputOrdinal = 0;
        var ownerStateAgeTicks = new List<double>(
            _cadence is null ? 0 : checked(_scenario.StageTicks * _activeBots));
        ushort[] recoveryTargets = _cadence?.HasStall == true
            ? new ushort[_activeBots]
            : [];
        bool recoveryTargetsCaptured = false;
        int stallRecoveryWallTicks = -1;
        ulong precedingWireBytes = 0;
        ulong precedingRetransmissions = 0;
        ulong recoveryBurstWireBytes = 0;
        ulong recoveryBurstRetransmissions = 0;
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
        long bytePressure50 = 0;
        long bytePressure75 = 0;
        long bytePressure90 = 0;
        long bytePressure100 = 0;
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
            ProtocolStageTimingSides protocolBefore = _profile.DeepTiming
                ? CaptureProtocolStageTiming()
                : default;
            if (_cadence is { HasStall: true } cadenceAtBoundary &&
                tick == cadenceAtBoundary.StallEndWallTick)
            {
                for (int index = 0; index < _activeBots; index++)
                    recoveryTargets[index] = _bots[index].LatestQueuedInput;
                recoveryTargetsCaptured = true;
            }
            long phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
            if (_cadence is null)
            {
                for (int index = 0; index < _activeBots; index++)
                {
                    Bot bot = _bots[index];
                    if (_profile.SendsInput && tick % inputInterval == index % inputInterval &&
                        bot.TryQueueInput(tick))
                        logicalInputs++;
                    if (_profile.SendsControl && tick % _scenario.TickHz == index % _scenario.TickHz)
                        bot.TryQueuePing();
                }
            }
            else
            {
                inputAccumulator = checked(inputAccumulator + 20);
                if (inputAccumulator >= _scenario.TickHz)
                {
                    inputAccumulator -= _scenario.TickHz;
                    for (int index = 0; index < _activeBots; index++)
                        if (_profile.SendsInput && _bots[index].TryQueueInput(cadenceInputOrdinal))
                            logicalInputs++;
                    cadenceInputOrdinal++;
                }
                if (_profile.SendsControl && tick % _scenario.TickHz == 0)
                    for (int index = 0; index < _activeBots; index++)
                        _bots[index].TryQueuePing();
            }
            RecordPhase(intentQueueMs, phaseStarted);

            long tickStart = Stopwatch.GetTimestamp();
            long serverStart = tickStart;
            long serverEnd;
            long clientStart;
            if (_cadence is null)
            {
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                for (int index = 0; index < _bots.Length; index++)
                    _bots[index].ProcessServer(_scenario.TickHz);
                RecordPhase(serverIngressMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                for (int index = 0; index < _bots.Length; index++)
                    _bots[index].AdvanceAuthority(_scenario.TickHz);
                authoritySteps++;
                RecordPhase(authorityMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                _observer?.UpdateChangedAuthority(checked((int)authoritySteps));
                RecordPhase(interestUpdateMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                    if ((!_scenario.ObserverState || _bots[index].ShouldQueueSparseOwner(checked((ulong)authoritySteps))) &&
                        _bots[index].QueueOwnerState(checked((ulong)authoritySteps)))
                        ownerMessagesQueued++;
                if (_scenario.ObserverState)
                    for (int index = _activeBots; index < _bots.Length; index++)
                        if (_bots[index].ShouldQueueSparseOwner(checked((ulong)authoritySteps)) &&
                            _bots[index].QueueOwnerState(checked((ulong)authoritySteps)))
                            ownerMessagesQueued++;
                RecordPhase(ownerQueueMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                _observer?.Schedule(checked((int)authoritySteps));
                RecordPhase(worldScheduleMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                PumpAll(2);
                networkPumps += 2;
                RecordPhase(forwardPumpMs, phaseStarted);
                serverEnd = Stopwatch.GetTimestamp();
                clientStart = Stopwatch.GetTimestamp();
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                for (int index = 0; index < _bots.Length; index++)
                    ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
                RecordPhase(clientApplyMs, phaseStarted);
                phaseStarted = _profile.DeepTiming ? Stopwatch.GetTimestamp() : 0;
                PumpAll(1);
                networkPumps++;
                RecordPhase(returnPumpMs, phaseStarted);
            }
            else
            {
                V3CadenceProfile cadence = _cadence.Value;
                int authorityHz = cadence.AuthorityHzAt(tick, _scenario.StageTicks);
                if (!cadence.IsAuthorityStalled(tick))
                    authorityAccumulator = checked(authorityAccumulator + authorityHz);
                while (authorityAccumulator >= cadence.WallClockHz)
                {
                    authorityAccumulator -= cadence.WallClockHz;
                    for (int index = 0; index < _bots.Length; index++)
                        _bots[index].ProcessServer(authorityHz);
                    for (int index = 0; index < _bots.Length; index++)
                        _bots[index].AdvanceAuthority(authorityHz);
                    authoritySteps++;
                    for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                        if (_bots[index].QueueOwnerState(checked((ulong)authoritySteps)))
                            ownerMessagesQueued++;
                }
                serverEnd = Stopwatch.GetTimestamp();
                clientStart = serverEnd;
                if (!cadence.IsNetworkStalled(tick))
                    networkAccumulator = checked(networkAccumulator + cadence.NetworkHz);
                while (networkAccumulator >= cadence.WallClockHz)
                {
                    networkAccumulator -= cadence.WallClockHz;
                    PumpAll(1);
                    networkPumps++;
                    for (int index = 0; index < _bots.Length; index++)
                        ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
                }
            }
            _observer?.ObserveCompactionProgress(checked((int)authoritySteps));
            long end = Stopwatch.GetTimestamp();

            if (_profile.DeepTiming && _cadence is null)
            {
                ProtocolStageTimingSides protocol =
                    CaptureProtocolStageTiming().Subtract(protocolBefore);
                double serverProtocol = protocol.Server.TotalMilliseconds +
                    protocol.Listener.TotalMilliseconds;
                double clientProtocol = protocol.Client.TotalMilliseconds;
                serverProtocolMs!.Add(serverProtocol);
                clientProtocolMs!.Add(clientProtocol);
                serverRoleMs!.Add(
                    serverIngressMs![^1] + authorityMs![^1] + interestUpdateMs![^1] +
                    ownerQueueMs![^1] + worldScheduleMs![^1] + serverProtocol);
                clientRoleMs!.Add(intentQueueMs![^1] + clientApplyMs![^1] + clientProtocol);
            }

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
            reliableRetainedBytes.Add(pressure.ReliableRetainedBytes);
            independentRetainedBytes.Add(pressure.IndependentRetainedBytes);
            receivedRetainedBytes.Add(pressure.ReceivedRetainedBytes);
            totalRetainedBytes.Add(pressure.TotalRetainedBytes);
            double occupancy = pressure.MaximumRatio;
            occupancySum += occupancy;
            maximumOccupancy = Math.Max(maximumOccupancy, occupancy);
            if (occupancy >= 0.50) pressure50++;
            if (occupancy >= 0.75) pressure75++;
            if (occupancy >= 0.90) pressure90++;
            if (occupancy >= 1.00) pressure100++;
            if (pressure.MaximumByteRatio >= 0.50) bytePressure50++;
            if (pressure.MaximumByteRatio >= 0.75) bytePressure75++;
            if (pressure.MaximumByteRatio >= 0.90) bytePressure90++;
            if (pressure.MaximumByteRatio >= 1.00) bytePressure100++;
            if (_cadence is not null)
            {
                for (int index = 0; index < _activeBots; index++)
                    ownerStateAgeTicks.Add(Math.Max(
                        0d,
                        authoritySteps - (double)_bots[index].LastAppliedOwnerTick));
                if (recoveryTargetsCaptured && stallRecoveryWallTicks < 0)
                {
                    bool recovered = true;
                    for (int index = 0; index < _activeBots && recovered; index++)
                        recovered = _bots[index].HasAppliedAtLeast(recoveryTargets[index]);
                    if (recovered)
                        stallRecoveryWallTicks = checked(
                            tick - _cadence.Value.StallEndWallTick + 1);
                }
                if (tick > 0 && recoveryTargetsCaptured && stallRecoveryWallTicks < 0)
                {
                    recoveryBurstWireBytes = Math.Max(
                        recoveryBurstWireBytes, pressure.WireBytes - precedingWireBytes);
                    recoveryBurstRetransmissions = Math.Max(
                        recoveryBurstRetransmissions,
                        pressure.Retransmissions - precedingRetransmissions);
                }
                precedingWireBytes = pressure.WireBytes;
                precedingRetransmissions = pressure.Retransmissions;
            }
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

        ProtocolStageTimingSides protocolStageTiming = _profile.DeepTiming
            ? CaptureProtocolStageTiming()
            : default;
        if (_profile.DeepTiming) ResetProtocolStageTiming(enabled: false);

        // Match the V2 harness's untimed post-stage settle: no new intent is
        // created, but admitted input and current control state may converge.
        for (int settle = 0; settle < _scenario.SettleTicks; settle++)
        {
            if (_cadence is not null)
                for (int index = 0; index < _activeBots && _profile.SendsInput; index++)
                    _ = _bots[index].TryRequeueLatestInput();
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].ProcessServer(_scenario.TickHz);
            for (int index = 0; index < _bots.Length; index++)
                _bots[index].AdvanceAuthority(_scenario.TickHz);
            _observer?.UpdateChangedAuthority(
                checked(_scenario.StageTicks + settle + 1));
            for (int index = 0; index < _activeBots && _profile.SendsOwner; index++)
                if (!_scenario.ObserverState ||
                    _bots[index].ShouldQueueSparseOwner(
                        checked((ulong)_scenario.StageTicks + (ulong)settle + 1)))
                    _bots[index].QueueOwnerState(
                        checked((ulong)_scenario.StageTicks + (ulong)settle + 1));
            if (_scenario.ObserverState)
                for (int index = _activeBots; index < _bots.Length; index++)
                    if (_bots[index].ShouldQueueSparseOwner(
                            checked((ulong)_scenario.StageTicks + (ulong)settle + 1)))
                        _bots[index].QueueOwnerState(
                            checked((ulong)_scenario.StageTicks + (ulong)settle + 1));
            _observer?.Schedule(checked(_scenario.StageTicks + settle + 1));
            PumpAll(2);
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
            _observer?.ObserveCompactionProgress(
                checked(_scenario.StageTicks + settle + 1));
            _observer?.ScanFreshness(
                checked(_scenario.StageTicks + settle + 1), measure: true);
            PumpAll(1);
            if (_cadence is not null) WaitForRealtimeTick(ref nextDeadline);
        }
        if (_cadence is not null)
        {
            // Close the final endpoint-routing half-cycle without producing new
            // authority. This is application convergence, not measured work.
            PumpAll(1);
            for (int index = 0; index < _bots.Length; index++)
                ownerApplied += _bots[index].ProcessClient(pingMs, ackMs);
        }
        _observer?.DrainRemaining(
            checked(_scenario.StageTicks + _scenario.SettleTicks + 1));

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
        long retainedPayloadCapacity = 0;
        long retainedPayloadHighWater = 0;
        int largestRetainedMessage = 0;
        ulong retainedByteCapacityFailures = 0;
        ulong reliablePayloadCompactions = 0;
        ulong reliablePayloadCompactionBytesMoved = 0;
        int reliablePayloadMaximumBytesMovedPerCompaction = 0;
        ulong receivedPayloadCompactions = 0;
        ulong receivedPayloadCompactionBytesMoved = 0;
        int receivedPayloadMaximumBytesMovedPerCompaction = 0;
        UnreliableAggregationCensus unreliableAggregation = default;
        long reserved = 0;
        long serverReserved = 0;
        long clientReserved = 0;
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
            unreliableAggregation = unreliableAggregation
                .Add(snapshot.Client.UnreliableAggregation)
                .Add(snapshot.Server.UnreliableAggregation);
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
            retainedPayloadCapacity = checked(retainedPayloadCapacity +
                snapshot.Client.RetainedPayloads.TotalCapacityBytes +
                snapshot.Server.RetainedPayloads.TotalCapacityBytes);
            retainedPayloadHighWater = checked(retainedPayloadHighWater +
                snapshot.Client.RetainedPayloads.TotalHighWaterBytes +
                snapshot.Server.RetainedPayloads.TotalHighWaterBytes);
            largestRetainedMessage = Math.Max(largestRetainedMessage,
                Math.Max(snapshot.Client.RetainedPayloads.LargestRetainedMessageBytes,
                    snapshot.Server.RetainedPayloads.LargestRetainedMessageBytes));
            retainedByteCapacityFailures = SaturatingAdd(retainedByteCapacityFailures,
                SaturatingAdd(snapshot.Client.RetainedPayloads.ByteCapacityFailures,
                    snapshot.Server.RetainedPayloads.ByteCapacityFailures));
            reliablePayloadCompactions = SaturatingAdd(reliablePayloadCompactions,
                SaturatingAdd(snapshot.Client.RetainedPayloads.ReliableCompactions,
                    snapshot.Server.RetainedPayloads.ReliableCompactions));
            reliablePayloadCompactionBytesMoved = SaturatingAdd(
                reliablePayloadCompactionBytesMoved,
                SaturatingAdd(snapshot.Client.RetainedPayloads.ReliableCompactionBytesMoved,
                    snapshot.Server.RetainedPayloads.ReliableCompactionBytesMoved));
            reliablePayloadMaximumBytesMovedPerCompaction = Math.Max(
                reliablePayloadMaximumBytesMovedPerCompaction,
                Math.Max(snapshot.Client.RetainedPayloads.ReliableMaximumBytesMovedPerCompaction,
                    snapshot.Server.RetainedPayloads.ReliableMaximumBytesMovedPerCompaction));
            receivedPayloadCompactions = SaturatingAdd(receivedPayloadCompactions,
                SaturatingAdd(snapshot.Client.RetainedPayloads.ReceivedCompactions,
                    snapshot.Server.RetainedPayloads.ReceivedCompactions));
            receivedPayloadCompactionBytesMoved = SaturatingAdd(
                receivedPayloadCompactionBytesMoved,
                SaturatingAdd(snapshot.Client.RetainedPayloads.ReceivedCompactionBytesMoved,
                    snapshot.Server.RetainedPayloads.ReceivedCompactionBytesMoved));
            receivedPayloadMaximumBytesMovedPerCompaction = Math.Max(
                receivedPayloadMaximumBytesMovedPerCompaction,
                Math.Max(snapshot.Client.RetainedPayloads.ReceivedMaximumBytesMovedPerCompaction,
                    snapshot.Server.RetainedPayloads.ReceivedMaximumBytesMovedPerCompaction));
            capacityDrops = SaturatingAdd(capacityDrops,
                SaturatingAdd(snapshot.Client.CapacityDrops + snapshot.ClientDatagrams.CapacityDrops,
                    snapshot.Server.CapacityDrops + snapshot.ServerDatagrams.CapacityDrops));
            if (_sharedHost is null)
            {
                reserved = checked(reserved + snapshot.Client.ReservedBytes + snapshot.Server.ReservedBytes);
                clientReserved = checked(clientReserved + snapshot.Client.ReservedBytes);
                serverReserved = checked(serverReserved + snapshot.Server.ReservedBytes);
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
            serverReserved = sharedSnapshot.KnownServerReservedBytes;
            clientReserved = sharedSnapshot.KnownClientReservedBytes;
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
            unreliableAggregation = unreliableAggregation.Subtract(
                diagnosticBaseline.UnreliableAggregation);
        }
        if (!_unreliableFrameAggregation)
        {
            // Scalar controls intentionally do not execute aggregation telemetry in
            // the protocol hot path. Reconstruct the comparable one-frame sent
            // census from existing packet counters in this cold report layer.
            ulong scalarUnreliableDatagrams = dataPacketsSent >= reliableFragmentsSent
                ? dataPacketsSent - reliableFragmentsSent
                : 0;
            unreliableAggregation = new()
            {
                SentFrames = scalarUnreliableDatagrams,
                SentDatagrams = scalarUnreliableDatagrams,
                SentHistogramOne = scalarUnreliableDatagrams,
                MaximumSentFramesPerDatagram = scalarUnreliableDatagrams == 0 ? 0 : 1,
            };
        }
        peakManagedHeap = Math.Max(peakManagedHeap, GC.GetTotalMemory(false));
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
        peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
        IReadOnlyList<NetworkCanonicalOwnerState> canonicalStates = CaptureCanonicalStates();
        long benchmarkManifestDefaultReservedPerClient = checked(
            2 * NetworkProtocolLimits.Default.EstimateReservedPayloadBytes(
            new DeterministicDatagramLimits(UdpLimits.MaximumDatagramBytes, UdpLimits.MaximumDatagramBytes),
            _manifest));
        SparseObserverMetrics observerMetrics = _observer?.CaptureMetrics() ?? default;
        long normalizedLogicalBytes = checked(
            logicalInputs * 10 + ownerMessagesQueued * 30 + pingMs.Count * 4L +
            observerMetrics.SemanticBytesCopied);
        NetworkMetricSummary inputAckTicks = NetworkMetricSummary.From(
            ackMs.Select(value => value *
                (_cadence?.NominalAuthorityHz ?? _scenario.TickHz) / 1000d).ToArray());
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
                (_profile.IsComparisonDefault && _cadence is null
                    ? string.Empty
                    : _cadence is not null
                        ? $"-cadence-{_cadence.Value.Id}"
                        : $"-{_profile.Layer}-{_activeBots}") +
                (_unreliableFrameAggregation ? "-unreliable-multiframe" : string.Empty),
            Scenario = _scenario,
            EvidenceProfile = new(
                _scenario.ObserverState
                    ? _unreliableFrameAggregation
                        ? "network-bot-sparse-observer-movement-unreliable-multiframe-v1"
                        : "network-bot-sparse-observer-movement-v1"
                    : _profile.IsComparisonDefault && _cadence is null
                    ? "network-bot-owner-movement-v1"
                    : _cadence is not null
                        ? "network-v3-cadence-acceptance-v1"
                        : "network-v3-peer-cost-diagnostic-v1",
                _scenario.ObserverState
                    ? "Historical sparse workload: deterministic phased owner input, one observer latest-state family, three AOI rings, owner correction, and ping; chat and ordered Events disabled." +
                        (_unreliableFrameAggregation
                            ? " This run opts into bounded same-peer unreliable Data aggregation with a seven-frame cap; reliable Data remains single-frame."
                            : string.Empty)
                    : _profile.IsComparisonDefault && _cadence is null
                    ? "Deterministic phased owner input, authoritative latest control, owner correction, and ping; arrival-driven position remains diagnostic."
                    : _cadence is not null
                        ? _cadence.Value.DescribeSemantics()
                        : _profile.DescribeSemantics(),
                "workload bounds fixed; implementation reservations reported separately",
                _cadence is null
                    ? $"fixed {_scenario.TickHz} Hz; {_scenario.InputHz} Hz phased input; realtime"
                    : $"{_cadence.Value.DescribeCadence()}; 20 Hz wall-time input; realtime",
                _topology == V3UdpTopology.SharedListener
                    ? _sharedSocketIoMode is SharedUdpSocketIoMode.PlatformBatch or
                        SharedUdpSocketIoMode.PlatformSendReceiveBatch
                        ? $"one shared server UDP listener with bounded platform {(_sharedSocketIoMode == SharedUdpSocketIoMode.PlatformSendReceiveBatch ? "send/receive" : "send")} batching, one client socket and two secure protocol peers per bot"
                        : "one shared server UDP listener, one client socket and two secure protocol peers per bot"
                    : "one connected localhost UDP socket pair and two secure protocol peers per bot",
                "process-wide allocated bytes and GC collections; physical memory where exposed"),
            ReadyClients = _bots.Length,
            TickMilliseconds = NetworkMetricSummary.From(tickMs),
            ServerPumpMilliseconds = NetworkMetricSummary.From(serverMs),
            TickSamples = tickMs.ToArray(),
            ServerPumpSamples = serverMs.ToArray(),
            ClientPumpMilliseconds = NetworkMetricSummary.From(clientMs),
            PingMilliseconds = NetworkMetricSummary.From(pingMs),
            InputAcknowledgementMilliseconds = NetworkMetricSummary.From(ackMs),
            InputAcknowledgementTicks = inputAckTicks,
            PerClientInputAckP95Milliseconds = perClientInputAckP95,
            PerClientPingP95Milliseconds = perClientPingP95,
            HandshakeMilliseconds = NetworkMetricSummary.From(_handshakeMilliseconds),
            ImmediateWorldStateMilliseconds = observerMetrics.ImmediateMilliseconds,
            NearWorldStateMilliseconds = observerMetrics.NearMilliseconds,
            FarWorldStateMilliseconds = observerMetrics.FarMilliseconds,
            ImmediateWorldStateTicks = observerMetrics.ImmediateTicks,
            NearWorldStateTicks = observerMetrics.NearTicks,
            FarWorldStateTicks = observerMetrics.FarTicks,
            PerClientWorldStateP95Milliseconds = observerMetrics.PerClientP95Milliseconds,
            MetricCoverage = new(
                OwnerInputFreshness: _profile.SendsInput && _profile.SendsOwner,
                WorldStateFreshnessByTier: _scenario.ObserverState,
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
                InterestAmplification: _scenario.ObserverState,
                BaselineRecovery: false,
                DeepPhaseTiming: _profile.DeepTiming),
            TickDeadlineMisses = deadlineMisses,
            LongestDeadlineMissStreak = longestDeadlineStreak,
            LogicalInputs = logicalInputs,
            OwnerStatesApplied = ownerApplied,
            WorldStateRecordsApplied = observerMetrics.RecordsApplied,
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
                reliableOccupancy, _protocolLimits.MaximumReliableMessages),
            IndependentUnreliableOccupancy = NetworkOccupancySummary.From(
                independentOccupancy, _protocolLimits.MaximumIndependentUnreliableMessages),
            ReceivedOccupancy = NetworkOccupancySummary.From(
                receivedOccupancy, _protocolLimits.MaximumReceivedMessages),
            InFlightOccupancy = NetworkOccupancySummary.From(
                inFlightOccupancy, _protocolLimits.MaximumCongestionWindowPackets),
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
            CanonicalValidation = _scenario.ObserverState
                ? "Exact client-applied owner identity, acknowledged input sequence, and velocity. Observer convergence and freshness are validated separately; V2 exposes apply counts but no canonical observer digest."
                : _profile.IsComparisonDefault && _cadence is null
                ? "Exact client-applied owner identity, acknowledged input sequence, and velocity. Position remains diagnostic because V2 integrates arrival-relative input."
                : _cadence is not null
                    ? "V3 cadence acceptance: identical wall-time input samples must converge to the same owner identity, acknowledged input sequence, and velocity across cadence/stall schedules; position remains cadence-relative diagnostic state."
                    : "V3-only causal diagnostic; canonical state is retained for repeatability but no V2 semantic ratio is claimed.",
            Diagnostics = new Dictionary<string, double>
            {
                ["reserved_bytes_per_client"] = reserved / (double)_bots.Length,
                ["known_server_network_reserved_bytes"] = serverReserved,
                ["known_client_network_reserved_bytes"] = clientReserved,
                ["known_server_network_reserved_bytes_per_client"] =
                    serverReserved / (double)_bots.Length,
                ["known_client_network_reserved_bytes_per_client"] =
                    clientReserved / (double)_bots.Length,
                ["configured_listener_socket_buffer_bytes"] =
                    sharedSnapshot.ConfiguredListenerSocketBufferBytes,
                ["configured_client_socket_buffer_bytes"] =
                    sharedSnapshot.ConfiguredClientSocketBufferBytes,
                ["actual_listener_socket_buffer_bytes"] =
                    sharedSnapshot.Listener.ActualSocketBufferBytes,
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
                ["retained_payload_capacity_bytes"] = retainedPayloadCapacity,
                ["retained_payload_capacity_bytes_per_client"] =
                    retainedPayloadCapacity / (double)_bots.Length,
                ["retained_payload_category_high_water_sum_bytes"] = retainedPayloadHighWater,
                ["retained_payload_largest_message_bytes"] = largestRetainedMessage,
                ["retained_payload_byte_capacity_failures"] = retainedByteCapacityFailures,
                ["retained_reliable_payload_compactions"] = reliablePayloadCompactions,
                ["retained_reliable_compaction_bytes_moved"] =
                    reliablePayloadCompactionBytesMoved,
                ["retained_reliable_max_bytes_moved_one_compaction"] =
                    reliablePayloadMaximumBytesMovedPerCompaction,
                ["retained_received_payload_compactions"] = receivedPayloadCompactions,
                ["retained_received_compaction_bytes_moved"] =
                    receivedPayloadCompactionBytesMoved,
                ["retained_received_max_bytes_moved_one_compaction"] =
                    receivedPayloadMaximumBytesMovedPerCompaction,
                ["retained_payload_pressure_ticks_50"] = bytePressure50,
                ["retained_payload_pressure_ticks_75"] = bytePressure75,
                ["retained_payload_pressure_ticks_90"] = bytePressure90,
                ["retained_payload_pressure_ticks_100"] = bytePressure100,
                ["retained_reliable_bytes_mean"] = NetworkMetricSummary.From(reliableRetainedBytes).Mean,
                ["retained_reliable_bytes_p95"] = NetworkMetricSummary.From(reliableRetainedBytes).P95,
                ["retained_reliable_bytes_max"] = NetworkMetricSummary.From(reliableRetainedBytes).Max,
                ["retained_independent_bytes_mean"] = NetworkMetricSummary.From(independentRetainedBytes).Mean,
                ["retained_independent_bytes_p95"] = NetworkMetricSummary.From(independentRetainedBytes).P95,
                ["retained_independent_bytes_max"] = NetworkMetricSummary.From(independentRetainedBytes).Max,
                ["retained_received_bytes_mean"] = NetworkMetricSummary.From(receivedRetainedBytes).Mean,
                ["retained_received_bytes_p95"] = NetworkMetricSummary.From(receivedRetainedBytes).P95,
                ["retained_received_bytes_max"] = NetworkMetricSummary.From(receivedRetainedBytes).Max,
                ["retained_total_bytes_mean"] = NetworkMetricSummary.From(totalRetainedBytes).Mean,
                ["retained_total_bytes_p95"] = NetworkMetricSummary.From(totalRetainedBytes).P95,
                ["retained_total_bytes_max"] = NetworkMetricSummary.From(totalRetainedBytes).Max,
                ["worst_client_input_ack_p95_ms"] = _bots.Max(bot => bot.InputAcknowledgements.P95),
                ["worst_client_input_ack_max_ms"] = _bots.Max(bot => bot.InputAcknowledgements.Max),
                ["worst_client_ping_p95_ms"] = _bots.Max(bot => bot.PingAcknowledgements.P95),
                ["worst_client_ping_max_ms"] = _bots.Max(bot => bot.PingAcknowledgements.Max),
                ["data_packets_sent"] = dataPacketsSent,
                ["unreliable_frame_aggregation_enabled"] = _unreliableFrameAggregation ? 1 : 0,
                ["unreliable_frames_consumed"] = unreliableAggregation.SentFrames,
                ["unreliable_data_datagrams_sent"] = unreliableAggregation.SentDatagrams,
                ["unreliable_frames_received"] = unreliableAggregation.ReceivedFrames,
                ["unreliable_data_datagrams_received"] = unreliableAggregation.ReceivedDatagrams,
                ["unreliable_sent_datagrams_two_or_more_frames"] =
                    unreliableAggregation.SentDatagramsWithTwoOrMoreFrames,
                ["unreliable_received_datagrams_two_or_more_frames"] =
                    unreliableAggregation.ReceivedDatagramsWithTwoOrMoreFrames,
                ["unreliable_sent_histogram_1"] = unreliableAggregation.SentHistogramOne,
                ["unreliable_sent_histogram_2"] = unreliableAggregation.SentHistogramTwo,
                ["unreliable_sent_histogram_3"] = unreliableAggregation.SentHistogramThree,
                ["unreliable_sent_histogram_4"] = unreliableAggregation.SentHistogramFour,
                ["unreliable_sent_histogram_5"] = unreliableAggregation.SentHistogramFive,
                ["unreliable_sent_histogram_6"] = unreliableAggregation.SentHistogramSix,
                ["unreliable_sent_histogram_7"] = unreliableAggregation.SentHistogramSeven,
                ["unreliable_sent_histogram_8_plus"] = unreliableAggregation.SentHistogramEightOrMore,
                ["unreliable_received_histogram_1"] = unreliableAggregation.ReceivedHistogramOne,
                ["unreliable_received_histogram_2"] = unreliableAggregation.ReceivedHistogramTwo,
                ["unreliable_received_histogram_3"] = unreliableAggregation.ReceivedHistogramThree,
                ["unreliable_received_histogram_4"] = unreliableAggregation.ReceivedHistogramFour,
                ["unreliable_received_histogram_5"] = unreliableAggregation.ReceivedHistogramFive,
                ["unreliable_received_histogram_6"] = unreliableAggregation.ReceivedHistogramSix,
                ["unreliable_received_histogram_7"] = unreliableAggregation.ReceivedHistogramSeven,
                ["unreliable_received_histogram_8_plus"] = unreliableAggregation.ReceivedHistogramEightOrMore,
                ["unreliable_sent_plaintext_bytes"] = unreliableAggregation.SentPlaintextBytes,
                ["unreliable_received_plaintext_bytes"] = unreliableAggregation.ReceivedPlaintextBytes,
                ["unreliable_max_sent_frames_per_datagram"] =
                    unreliableAggregation.MaximumSentFramesPerDatagram,
                ["unreliable_max_received_frames_per_datagram"] =
                    unreliableAggregation.MaximumReceivedFramesPerDatagram,
                ["unreliable_mean_sent_datagram_fill_ratio"] = unreliableAggregation.SentDatagrams == 0
                    ? 0
                    : unreliableAggregation.SentPlaintextBytes / (double)unreliableAggregation.SentDatagrams /
                        UdpLimits.MaximumDatagramBytes,
                ["unreliable_mean_received_datagram_fill_ratio"] = unreliableAggregation.ReceivedDatagrams == 0
                    ? 0
                    : unreliableAggregation.ReceivedPlaintextBytes / (double)unreliableAggregation.ReceivedDatagrams /
                        UdpLimits.MaximumDatagramBytes,
                ["unreliable_frames_per_network_pump"] = networkPumps == 0
                    ? 0
                    : unreliableAggregation.SentFrames / (double)networkPumps,
                ["unreliable_receive_frames_per_network_pump"] = networkPumps == 0
                    ? 0
                    : unreliableAggregation.ReceivedFrames / (double)networkPumps,
                ["unreliable_configured_max_frames_per_datagram"] =
                    _protocolLimits.MaximumUnreliableFramesPerDatagram,
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
                ["benchmark_manifest_default_limits_reserved_bytes_per_client_estimate"] =
                    benchmarkManifestDefaultReservedPerClient,
                ["benchmark_manifest_default_limits_reserved_bytes_total_estimate"] =
                    checked(benchmarkManifestDefaultReservedPerClient * (long)_bots.Length),
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
        if (_observer is not null)
            _observer.AddDiagnostics((Dictionary<string, double>)result.Diagnostics);
        if (_cadence is not null)
        {
            var diagnostics = (Dictionary<string, double>)result.Diagnostics;
            double measuredSeconds = _scenario.StageTicks / (double)_scenario.TickHz;
            NetworkMetricSummary age = NetworkMetricSummary.From(ownerStateAgeTicks);
            NetworkMetricSummary freshnessMilliseconds = NetworkMetricSummary.From(
                _bots.SelectMany(bot => bot.OwnerFreshnessMilliseconds).ToArray());
            diagnostics["wall_clock_ticks"] = _scenario.StageTicks;
            diagnostics["wall_clock_hz"] = _scenario.TickHz;
            diagnostics["authoritative_steps"] = authoritySteps;
            diagnostics["network_pumps"] = networkPumps;
            diagnostics["authority_steps_per_wall_second"] =
                measuredSeconds == 0 ? 0 : authoritySteps / measuredSeconds;
            diagnostics["network_pumps_per_wall_second"] =
                measuredSeconds == 0 ? 0 : networkPumps / measuredSeconds;
            diagnostics["network_pumps_per_authoritative_step"] =
                authoritySteps == 0 ? 0 : networkPumps / (double)authoritySteps;
            diagnostics["owner_publications_per_authoritative_step_per_client"] =
                authoritySteps == 0 || _activeBots == 0
                    ? 0
                    : ownerMessagesQueued / (double)(authoritySteps * _activeBots);
            diagnostics["owner_state_age_mean_ticks"] = age.Mean;
            diagnostics["owner_state_age_p95_ticks"] = age.P95;
            diagnostics["owner_state_age_max_ticks"] = age.Max;
            diagnostics["owner_state_freshness_mean_ms"] = freshnessMilliseconds.Mean;
            diagnostics["owner_state_freshness_p95_ms"] = freshnessMilliseconds.P95;
            diagnostics["owner_state_freshness_max_ms"] = freshnessMilliseconds.Max;
            diagnostics["stall_recovery_observed"] = stallRecoveryWallTicks >= 0 ? 1 : 0;
            diagnostics["stall_recovery_wall_ticks"] = stallRecoveryWallTicks;
            diagnostics["stall_recovery_ms"] = stallRecoveryWallTicks < 0
                ? -1
                : stallRecoveryWallTicks * 1000d / _scenario.TickHz;
            diagnostics["stall_recovery_max_wire_bytes_one_wall_tick"] =
                recoveryBurstWireBytes;
            diagnostics["stall_recovery_max_retransmissions_one_wall_tick"] =
                recoveryBurstRetransmissions;
            diagnostics["minimum_latest_queued_input_sequence"] =
                _bots.Take(_activeBots).Min(bot => bot.LatestQueuedInput);
            diagnostics["minimum_server_input_sequence"] =
                _bots.Take(_activeBots).Min(bot => bot.ServerInput);
            diagnostics["minimum_owner_queued_input_sequence"] =
                _bots.Take(_activeBots).Min(bot => bot.LastQueuedOwnerInput);
            diagnostics["minimum_owner_received_input_sequence"] =
                _bots.Take(_activeBots).Min(bot => bot.LastReceivedOwnerInput);
            diagnostics["minimum_client_input_sequence"] =
                _bots.Take(_activeBots).Min(bot => bot.LastAppliedInput);
        }
        if (_profile.DeepTiming)
        {
            var diagnostics = (Dictionary<string, double>)result.Diagnostics;
            AddPhaseDiagnostics(diagnostics, "client_intent_queue", intentQueueMs!);
            AddPhaseDiagnostics(diagnostics, "server_application_ingress", serverIngressMs!);
            AddPhaseDiagnostics(diagnostics, "authority_advance", authorityMs!);
            AddPhaseDiagnostics(diagnostics, "interest_update", interestUpdateMs!);
            AddPhaseDiagnostics(diagnostics, "owner_state_queue", ownerQueueMs!);
            AddPhaseDiagnostics(diagnostics, "world_state_schedule", worldScheduleMs!);
            AddPhaseDiagnostics(diagnostics, "forward_protocol_pump", forwardPumpMs!);
            AddPhaseDiagnostics(diagnostics, "client_application_apply", clientApplyMs!);
            AddPhaseDiagnostics(diagnostics, "return_protocol_pump", returnPumpMs!);
            if (_cadence is null)
            {
                AddPhaseDiagnostics(diagnostics, "server_protocol_listener", serverProtocolMs!);
                AddPhaseDiagnostics(diagnostics, "client_protocol", clientProtocolMs!);
                AddPhaseDiagnostics(diagnostics, "server_role", serverRoleMs!);
                AddPhaseDiagnostics(diagnostics, "client_role", clientRoleMs!);
            }
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
            Add(prefix + "reliable_payload_compaction", value.ReliableCompactionTicks);
            Add(prefix + "received_payload_compaction", value.ReceivedCompactionTicks);
            Add(prefix + "inbound_residual", inboundResidual);
            diagnostics["protocol_stage_" + prefix + "outbound_unreliable_frames"] =
                value.OutboundUnreliableFrames;
            diagnostics["protocol_stage_" + prefix + "inbound_unreliable_frames"] =
                value.InboundUnreliableFrames;
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
        diagnostics["batch_same_lane_greedy_datagrams_observed"] =
            _batchCensus.SameLaneGreedyDatagrams;
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
        long reliableBytes = 0;
        long independentBytes = 0;
        long receivedBytes = 0;
        long totalBytes = 0;
        long totalByteCapacity = 0;
        ulong wireBytes = 0;
        ulong retransmissions = 0;
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
            AddPayload(snapshot.Client.RetainedPayloads);
            AddPayload(snapshot.Server.RetainedPayloads);
            wireBytes = SaturatingAdd(wireBytes,
                SaturatingAdd(snapshot.ClientDatagrams.SentBytes,
                    snapshot.ServerDatagrams.SentBytes));
            retransmissions = SaturatingAdd(retransmissions,
                SaturatingAdd(snapshot.Client.ReliableRetransmissions,
                    snapshot.Server.ReliableRetransmissions));
        }
        maxReliable = Math.Max(maxReliable, reliable);
        maxUnreliable = Math.Max(maxUnreliable, independent);
        maxReceived = Math.Max(maxReceived, received);
        maxInFlight = Math.Max(maxInFlight, inFlight);
        double maximum = Math.Max(
            Math.Max(reliable / (double)_protocolLimits.MaximumReliableMessages,
                independent / (double)_protocolLimits.MaximumIndependentUnreliableMessages),
            Math.Max(received / (double)_protocolLimits.MaximumReceivedMessages,
                inFlight / (double)_protocolLimits.MaximumCongestionWindowPackets));
        return new(reliable, independent, received, inFlight, maximum,
            reliableBytes, independentBytes, receivedBytes, totalBytes,
            totalByteCapacity == 0 ? 0 : totalBytes / (double)totalByteCapacity,
            wireBytes,
            retransmissions);

        void AddPayload(NetworkRetainedPayloadSnapshot payload)
        {
            reliableBytes = checked(reliableBytes + payload.ReliableBytes);
            independentBytes = checked(independentBytes + payload.IndependentBytes);
            receivedBytes = checked(receivedBytes + payload.ReceivedBytes);
            totalBytes = checked(totalBytes + payload.TotalBytes);
            totalByteCapacity = checked(totalByteCapacity + payload.TotalCapacityBytes);
        }
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

    private sealed class SparseObserverWorkload
    {
        private const int GlobalFreshnessBuckets = 16_384;
        private const int ClientFreshnessBuckets = 512;
        private readonly NetworkBotScenario _scenario;
        private readonly Bot[] _bots;
        private readonly Action<int> _pump;
        private readonly int _recordsPerBundle;
        private readonly int _byteBudget;
        private readonly GameInterestProfile _profile;
        private readonly GameInterestService _interest;
        private readonly SparseCapacityBounds _capacity;
        private readonly GameInterestAdmissionMode _admissionMode;
        private readonly SparseCompactionSettings _compactionSettings;
        private readonly RemoteWorldStateCompactionScheduler? _compactionScheduler;
        private readonly ReplicationSpatialPoint[] _positions;
        private readonly ulong[] _versions;
        private readonly long[] _createdAtByAuthorityTick;
        private readonly RemoteWorldEntitySnapshot[] _scratch;
        private readonly LatencyHistogram _immediateMs = new(1, GlobalFreshnessBuckets);
        private readonly LatencyHistogram _nearMs = new(1, GlobalFreshnessBuckets);
        private readonly LatencyHistogram _farMs = new(1, GlobalFreshnessBuckets);
        private readonly LatencyHistogram _immediateTicks =
            new(1, 2_048, percentileBucketOffset: 0);
        private readonly LatencyHistogram _nearTicks =
            new(1, 2_048, percentileBucketOffset: 0);
        private readonly LatencyHistogram _farTicks =
            new(1, 2_048, percentileBucketOffset: 0);
        private readonly LatencyHistogram _knownIdentityCounts;
        private readonly LatencyHistogram _compactionPressureToStartTicks =
            new(1, 4_096, percentileBucketOffset: 0);
        private readonly LatencyHistogram _compactionBaselineDurationTicks =
            new(1, 4_096, percentileBucketOffset: 0);
        private readonly LatencyHistogram[] _clientFreshness;
        private readonly ulong[] _lastCompactionRequestSequences;
        private readonly ulong[] _lastBaselinesStarted;
        private readonly ulong[] _lastBaselinesApplied;
        private readonly int[] _compactionRequestTicks;
        private readonly int[] _compactionStartTicks;
        private long _freshnessSamples;
        private int _maximumPending;
        private int _maximumOutstanding;
        private int _maximumRelations;
        private int _maximumKnownIdentities;
        private int _maximumActiveEntities;

        internal SparseObserverWorkload(
            NetworkBotScenario scenario,
            Bot[] bots,
            Action<int> pump,
            ulong applicationEpoch,
            int recordsPerBundle,
            int byteBudget,
            int regionEdgeMillimeters,
            SparseCapacityMode capacityMode,
            GameInterestAdmissionMode admissionMode,
            SparseCompactionSettings compactionSettings)
        {
            _scenario = scenario;
            _bots = bots;
            _pump = pump;
            _recordsPerBundle = recordsPerBundle;
            _byteBudget = byteBudget;
            _admissionMode = admissionMode;
            _compactionSettings = compactionSettings;
            _capacity = SparseCapacityBounds.Create(
                capacityMode, regionEdgeMillimeters, scenario, compactionSettings.Enabled);
            if (compactionSettings.Enabled)
                _compactionScheduler = new RemoteWorldStateCompactionScheduler(
                    scenario.Clients, compactionSettings.MaximumStartsPerTick);
            _knownIdentityCounts = new LatencyHistogram(
                1, checked(_capacity.MaximumKnownIdentities + 1), percentileBucketOffset: 0);
            _scratch = new RemoteWorldEntitySnapshot[_capacity.MaximumActiveEntities];
            _profile = new GameInterestProfile(
                RegionEdgeMillimeters: regionEdgeMillimeters,
                ImmediateEnterRadiusMillimeters: 7_500,
                ImmediateLeaveRadiusMillimeters: 7_500,
                NearRadiusMillimeters: 23_500,
                FarRadiusMillimeters: 39_500,
                NearIntervalMilliseconds: 32,
                FarIntervalMilliseconds: 48,
                ApplicationAckRetryMilliseconds: 100,
                ApplicationAckTimeoutMilliseconds: 2_000);
            int farCells = checked((_profile.FarRadiusMillimeters +
                _profile.RegionEdgeMillimeters - 1) / _profile.RegionEdgeMillimeters);
            int edgeWidth = checked(farCells * 2 + 1);
            // Rebuild adds one recipient's entering row/column before it removes
            // the exiting row/column. Preserve only that transactional headroom;
            // the admitted steady-state envelope remains edgeWidth squared.
            int maximumEdges = checked(
                scenario.Clients * edgeWidth * edgeWidth + edgeWidth * 2 - 1);
            _interest = new GameInterestService(
                new StateEpoch(1),
                new GameInterestLimits(
                    MaximumEntities: scenario.Clients,
                    MaximumRecipients: scenario.Clients,
                    MaximumRegions: checked(maximumEdges + scenario.Clients),
                    MaximumRegionInterestEdges: maximumEdges,
                    MaximumRecipientEntityRelations: checked(
                        scenario.Clients * _capacity.MaximumRelationsPerRecipient),
                    MaximumOutstandingBundles: checked(scenario.Clients * 8),
                    MaximumRecordsPerBundle: recordsPerBundle),
                _profile,
                _admissionMode);
            _positions = new ReplicationSpatialPoint[scenario.Clients];
            _versions = new ulong[scenario.Clients];
            _createdAtByAuthorityTick = new long[
                checked(scenario.StageTicks + scenario.SettleTicks + 2_049)];
            _clientFreshness = new LatencyHistogram[scenario.Clients];
            _lastCompactionRequestSequences = new ulong[scenario.Clients];
            _lastBaselinesStarted = new ulong[scenario.Clients];
            _lastBaselinesApplied = new ulong[scenario.Clients];
            _compactionRequestTicks = new int[scenario.Clients];
            _compactionStartTicks = new int[scenario.Clients];
            Array.Fill(_compactionRequestTicks, -1);
            Array.Fill(_compactionStartTicks, -1);

            for (int index = 0; index < bots.Length; index++)
            {
                _positions[index] = Position(bots[index]);
                _versions[index] = 1;
                // Per-client fairness needs a useful tail, not sub-millisecond
                // resolution.  Keeping this sketch compact prevents benchmark
                // telemetry from dominating the memory result at 4K clients.
                _clientFreshness[index] =
                    new LatencyHistogram(32, ClientFreshnessBuckets);
                Require(_interest.SetRecipientAnchor(
                        Participant(index), _positions[index], 0),
                    $"Sparse recipient {index} setup");
            }
            for (int index = 0; index < bots.Length; index++)
                Require(_interest.UpsertEntity(
                        Projected(index, 1, marker: 0, velocity: 0),
                        1,
                        _positions[index],
                        GameReplicationHorizon.Far,
                        0),
                    $"Sparse entity {index} setup");

            GameInterestSnapshot initial = _interest.GetSnapshot();
            _maximumRelations = initial.RelationCount;
            if (initial.RelationCount > checked(
                    scenario.Clients * _capacity.MaximumRelationsPerRecipient))
                throw new InvalidOperationException("Sparse relation estimate exceeded its admitted bound.");
            for (int index = 0; index < bots.Length; index++)
            {
                Bot bot = bots[index];
                bot.AttachWorldState(
                    new RemoteWorldStateServer(
                        bot.Server, _interest, Participant(index), applicationEpoch,
                        recordsPerBundle, _capacity.MaximumBaselineRecords,
                        compactionRequestSink: _compactionScheduler),
                    new RemoteWorldStateClient(
                        bot.Client, applicationEpoch, _interest.StateEpoch,
                        _capacity.MaximumKnownIdentities,
                        _capacity.MaximumActiveEntities,
                        recordsPerBundle, 64,
                        compactionPolicy: compactionSettings.Policy));
                if (_compactionScheduler is not null &&
                    !_compactionScheduler.TryRegister(Participant(index)))
                    throw new InvalidOperationException("Sparse compaction scheduler registration exceeded its fixed bound.");
            }
        }

        internal void DrainInitialState()
        {
            DrainCore(authorityTick: 0, initializeKnowledge: true);
            ScanFreshness(authorityTick: 0, measure: false);
        }

        internal void DrainRemaining(int authorityTick)
        {
            DrainCore(authorityTick, initializeKnowledge: false);
            ScanFreshness(authorityTick, measure: false);
            GameInterestSnapshot snapshot = _interest.GetSnapshot();
            if (snapshot.PendingRelationCount != 0 || snapshot.OutstandingBundleCount != 0 ||
                snapshot.NonviableRecipientCount != 0)
                throw new InvalidOperationException(
                    "Sparse observer state did not converge after the bounded drain.");
        }

        internal void ResetMeasurement()
        {
            _immediateMs.Clear();
            _nearMs.Clear();
            _farMs.Clear();
            _immediateTicks.Clear();
            _nearTicks.Clear();
            _farTicks.Clear();
            for (int index = 0; index < _clientFreshness.Length; index++)
                _clientFreshness[index].Clear();
            _freshnessSamples = 0;
            _maximumPending = 0;
            _maximumOutstanding = 0;
            _maximumRelations = _interest.GetSnapshot().RelationCount;
            _maximumKnownIdentities = 0;
            _maximumActiveEntities = 0;
            _knownIdentityCounts.Clear();
            _compactionPressureToStartTicks.Clear();
            _compactionBaselineDurationTicks.Clear();
            if (_compactionScheduler is not null)
            {
                for (int index = 0; index < _bots.Length; index++)
                {
                    RemoteWorldStateClientSnapshot client = _bots[index].WorldClient!.GetSnapshot();
                    RemoteWorldStateServerSnapshot server = _bots[index].WorldServer!.GetSnapshot();
                    _lastCompactionRequestSequences[index] = client.CompactionRequestSequence;
                    _lastBaselinesStarted[index] = server.BaselinesStarted;
                    _lastBaselinesApplied[index] = server.BaselinesApplied;
                    _compactionRequestTicks[index] = -1;
                    _compactionStartTicks[index] = -1;
                }
            }
        }

        internal void UpdateChangedAuthority(int authorityTick)
        {
            long now = SemanticMilliseconds(authorityTick);
            if ((uint)authorityTick >= (uint)_createdAtByAuthorityTick.Length)
                throw new InvalidOperationException("Sparse authority tick exceeded freshness history.");
            _createdAtByAuthorityTick[authorityTick] = Stopwatch.GetTimestamp();
            for (int index = 0; index < _bots.Length; index++)
            {
                Bot bot = _bots[index];
                if (!bot.ConsumeAuthorityChanged()) continue;
                ReplicationSpatialPoint point = Position(bot);
                _positions[index] = point;
                Require(_interest.SetRecipientAnchor(Participant(index), point, now),
                    $"Sparse recipient {index} movement");
                ulong version = checked(++_versions[index]);
                Require(_interest.UpsertEntity(
                        Projected(index, version, authorityTick,
                            checked((int)Math.Round(bot.ServerVelocityX * 100))),
                        version, point, GameReplicationHorizon.Far, now),
                    $"Sparse entity {index} movement");
            }
            _maximumRelations = Math.Max(
                _maximumRelations, _interest.GetSnapshot().RelationCount);
        }

        internal void Schedule(int authorityTick)
        {
            long now = SemanticMilliseconds(authorityTick);
            ObserveCompactionProgress(authorityTick);
            _compactionScheduler?.TryStartPending(authorityTick, TryStartCompaction);
            ObserveCompactionProgress(authorityTick);
            for (int index = 0; index < _bots.Length; index++)
            {
                RemoteWorldStateServer worldServer = _bots[index].WorldServer!;
                if (worldServer.GetSnapshot().BaselineInProgress)
                {
                    _ = worldServer.TryProgressBaseline();
                    continue;
                }
                for (int bundle = 0; bundle < 2; bundle++)
                {
                    GameWorldStateBuildStatus status = worldServer.TrySchedule(now, _byteBudget);
                    if (status == GameWorldStateBuildStatus.Success) continue;
                    if (status is GameWorldStateBuildStatus.NoData or
                        GameWorldStateBuildStatus.TransportNotAccepting or
                        GameWorldStateBuildStatus.OutstandingCapacityExceeded)
                        break;
                    throw new InvalidOperationException(
                        $"Sparse WorldState scheduling failed for recipient {index}: {status}.");
                }
            }
            GameInterestSnapshot snapshot = _interest.GetSnapshot();
            _maximumPending = Math.Max(_maximumPending, snapshot.PendingRelationCount);
            _maximumOutstanding = Math.Max(_maximumOutstanding, snapshot.OutstandingBundleCount);
            _maximumRelations = Math.Max(_maximumRelations, snapshot.RelationCount);
        }

        private RemoteWorldStateCompactionStartStatus TryStartCompaction(
            ParticipantId participant,
            in RemoteWorldStateCompactionRequestMessage request)
        {
            if (participant.Value == 0 || participant.Value > (ulong)_bots.Length)
                return RemoteWorldStateCompactionStartStatus.Reject;
            RemoteWorldStateServer server = _bots[checked((int)participant.Value - 1)].WorldServer!;
            if (!server.IsCurrentCompactionRequest(in request))
                return RemoteWorldStateCompactionStartStatus.Reject;
            RemoteWorldBaselineProgressStatus status = server.TryBeginCompactionBaseline(
                eventFloor: 0,
                recordsPerSegment: checked((ushort)_recordsPerBundle));
            return status switch
            {
                RemoteWorldBaselineProgressStatus.AwaitingClientPreparation or
                RemoteWorldBaselineProgressStatus.Progressed or
                RemoteWorldBaselineProgressStatus.CompleteAwaitingApplication =>
                    RemoteWorldStateCompactionStartStatus.Started,
                RemoteWorldBaselineProgressStatus.AlreadyActive or
                RemoteWorldBaselineProgressStatus.TransportBackpressure =>
                    RemoteWorldStateCompactionStartStatus.Retry,
                _ => RemoteWorldStateCompactionStartStatus.Reject,
            };
        }

        internal void ScanFreshness(int authorityTick, bool measure)
        {
            for (int client = 0; client < _bots.Length; client++)
            {
                RemoteWorldStateClient worldClient = _bots[client].WorldClient!;
                int count = worldClient.CopyActiveEntitiesUnordered(_scratch);
                RemoteWorldStateClientSnapshot clientSnapshot = worldClient.GetSnapshot();
                _maximumKnownIdentities = Math.Max(
                    _maximumKnownIdentities, clientSnapshot.KnownIdentityCount);
                _maximumActiveEntities = Math.Max(
                    _maximumActiveEntities, clientSnapshot.ActiveEntityCount);
                for (int row = 0; row < count; row++)
                {
                    ref readonly RemoteWorldEntitySnapshot state = ref _scratch[row];
                    if (!measure) continue;
                    _freshnessSamples++;
                    int entity = checked((int)state.Entity.Index);
                    int createdTick = state.TickMarker;
                    if ((uint)entity >= (uint)_versions.Length || createdTick <= 0 ||
                        (uint)createdTick >= (uint)_createdAtByAuthorityTick.Length ||
                        _createdAtByAuthorityTick[createdTick] == 0)
                        continue;
                    double milliseconds =
                        Stopwatch.GetElapsedTime(
                            _createdAtByAuthorityTick[createdTick]).TotalMilliseconds;
                    double ticks = Math.Max(0, authorityTick - createdTick);
                    _clientFreshness[client].Observe(milliseconds);
                    switch (Tier(_positions[client], new ReplicationSpatialPoint(
                        state.XMillimeters, state.ZMillimeters)))
                    {
                        case GameInterestTier.Immediate:
                            _immediateMs.Observe(milliseconds);
                            _immediateTicks.Observe(ticks);
                            break;
                        case GameInterestTier.Near:
                            _nearMs.Observe(milliseconds);
                            _nearTicks.Observe(ticks);
                            break;
                        default:
                            _farMs.Observe(milliseconds);
                            _farTicks.Observe(ticks);
                            break;
                    }
                }
            }
        }

        internal void ObserveCompactionProgress(int authorityTick)
        {
            if (_compactionScheduler is null) return;
            for (int index = 0; index < _bots.Length; index++)
            {
                RemoteWorldStateClientSnapshot client = _bots[index].WorldClient!.GetSnapshot();
                RemoteWorldStateServerSnapshot server = _bots[index].WorldServer!.GetSnapshot();
                _knownIdentityCounts.Observe(client.KnownIdentityCount);
                if (client.CompactionRequestOutstanding &&
                    client.CompactionRequestSequence != _lastCompactionRequestSequences[index])
                {
                    _lastCompactionRequestSequences[index] = client.CompactionRequestSequence;
                    _compactionRequestTicks[index] = authorityTick;
                }
                if (server.BaselinesStarted > _lastBaselinesStarted[index])
                {
                    _lastBaselinesStarted[index] = server.BaselinesStarted;
                    if (_compactionRequestTicks[index] >= 0)
                    {
                        _compactionPressureToStartTicks.Observe(
                            Math.Max(0, authorityTick - _compactionRequestTicks[index]));
                        _compactionStartTicks[index] = authorityTick;
                    }
                }
                if (server.BaselinesApplied > _lastBaselinesApplied[index])
                {
                    _lastBaselinesApplied[index] = server.BaselinesApplied;
                    if (_compactionStartTicks[index] >= 0)
                    {
                        _compactionBaselineDurationTicks.Observe(
                            Math.Max(0, authorityTick - _compactionStartTicks[index]));
                        _compactionStartTicks[index] = -1;
                    }
                }
            }
        }

        internal SparseObserverMetrics CaptureMetrics()
        {
            GameInterestSnapshot snapshot = _interest.GetSnapshot();
            return new(
                _immediateMs.Summary(),
                _nearMs.Summary(),
                _farMs.Summary(),
                _immediateTicks.Summary(),
                _nearTicks.Summary(),
                _farTicks.Summary(),
                NetworkMetricSummary.From(
                    _clientFreshness.Where(value => value.Count != 0)
                        .Select(value => value.Summary().P95).ToArray()),
                checked((long)snapshot.SemanticRecordCopies),
                _freshnessSamples,
                checked((long)snapshot.SemanticBytesCopied));
        }

        internal void AddDiagnostics(Dictionary<string, double> diagnostics)
        {
            GameInterestSnapshot interest = _interest.GetSnapshot();
            long adapterReserved = interest.ReservedBytes;
            ulong bundlesQueued = 0;
            ulong applicationAcks = 0;
            ulong baselinesStarted = 0;
            ulong baselinesApplied = 0;
            ulong compactionRequestsReceived = 0;
            ulong compactionRequestsRejected = 0;
            ulong compactionRequestsSent = 0;
            ulong baselineRecordsQueued = 0;
            ulong baselineWireBytesQueued = 0;
            ulong baselinesRejected = 0;
            ulong baselinesTimedOut = 0;
            ulong baselinesAbandoned = 0;
            int baselineInProgress = 0;
            for (int index = 0; index < _bots.Length; index++)
            {
                RemoteWorldStateServerSnapshot server = _bots[index].WorldServer!.GetSnapshot();
                RemoteWorldStateClientSnapshot client = _bots[index].WorldClient!.GetSnapshot();
                adapterReserved = checked(adapterReserved + server.ReservedBytes + client.ReservedBytes);
                bundlesQueued = checked(bundlesQueued + server.BundlesQueued);
                applicationAcks = checked(applicationAcks + server.ApplicationAcknowledgements);
                baselinesStarted = checked(baselinesStarted + server.BaselinesStarted);
                baselinesApplied = checked(baselinesApplied + server.BaselinesApplied);
                baselinesRejected = checked(baselinesRejected + server.BaselinesRejected);
                baselinesTimedOut = checked(baselinesTimedOut + server.BaselinesTimedOut);
                baselinesAbandoned = checked(baselinesAbandoned + server.BaselinesAbandoned);
                baselineRecordsQueued = checked(baselineRecordsQueued + server.BaselineRecordsQueued);
                baselineWireBytesQueued = checked(
                    baselineWireBytesQueued + server.BaselineWireBytesQueued);
                compactionRequestsReceived = checked(
                    compactionRequestsReceived + server.CompactionRequestsReceived);
                compactionRequestsRejected = checked(
                    compactionRequestsRejected + server.CompactionRequestsRejected);
                compactionRequestsSent = checked(
                    compactionRequestsSent + client.CompactionRequestsSent);
                if (server.BaselineInProgress) baselineInProgress++;
            }
            diagnostics["world_state_relations"] = interest.RelationCount;
            diagnostics["world_state_exact_admission_enabled"] =
                _admissionMode == GameInterestAdmissionMode.ExactDistance ? 1 : 0;
            diagnostics["world_state_relations_max"] = _maximumRelations;
            diagnostics["world_state_pending_relations_final"] = interest.PendingRelationCount;
            diagnostics["world_state_pending_relations_max"] = _maximumPending;
            diagnostics["world_state_outstanding_bundles_final"] = interest.OutstandingBundleCount;
            diagnostics["world_state_outstanding_bundles_max"] = _maximumOutstanding;
            diagnostics["world_state_nonviable_recipients"] = interest.NonviableRecipientCount;
            diagnostics["sparse_capacity_profile_current10"] =
                _capacity.Mode == SparseCapacityMode.Current10 ? 1 : 0;
            diagnostics["sparse_capacity_region_edge_mm"] = _profile.RegionEdgeMillimeters;
            diagnostics["sparse_capacity_steady_envelope_cells"] =
                _capacity.SteadyEnvelopeCells;
            diagnostics["sparse_capacity_boundary_union_cells"] =
                _capacity.BoundaryUnionCells;
            diagnostics["sparse_capacity_safety_slots"] = _capacity.SafetySlots;
            diagnostics["sparse_capacity_relations_per_recipient"] =
                _capacity.MaximumRelationsPerRecipient;
            diagnostics["sparse_capacity_known_identities_per_client"] =
                _capacity.MaximumKnownIdentities;
            diagnostics["sparse_capacity_active_entities_per_client"] =
                _capacity.MaximumActiveEntities;
            diagnostics["sparse_capacity_baseline_records_per_recipient"] =
                _capacity.MaximumBaselineRecords;
            diagnostics["sparse_capacity_known_identities_max_observed"] =
                _maximumKnownIdentities;
            diagnostics["sparse_capacity_active_entities_max_observed"] =
                _maximumActiveEntities;
            diagnostics["world_state_pending_writes"] = interest.PendingWrites;
            diagnostics["world_state_pending_obligations_created"] =
                interest.PendingObligationsCreated;
            diagnostics["world_state_pending_unsent_coalesces"] =
                interest.PendingUnsentCoalesces;
            diagnostics["world_state_pending_sent_supersessions"] =
                interest.PendingSentSupersessions;
            diagnostics["world_state_relation_sequence_allocations"] =
                interest.RelationSequenceAllocations;
            diagnostics["world_state_pending_list_relinks"] = interest.PendingListRelinks;
            diagnostics["world_state_eligibility_deadlines_preserved"] =
                interest.EligibilityDeadlinesPreserved;
            diagnostics["world_state_eligibility_deadlines_advanced_after_send"] =
                interest.EligibilityDeadlinesAdvancedAfterSend;
            diagnostics["world_state_same_tier_coalesces_moved_eligibility_later"] =
                interest.SameTierCoalescesMovedEligibilityLater;
            diagnostics["world_state_unique_semantic_constructions"] =
                interest.UniqueSemanticConstructions;
            diagnostics["world_state_semantic_record_copies"] = interest.SemanticRecordCopies;
            diagnostics["world_state_semantic_bytes_copied"] = interest.SemanticBytesCopied;
            diagnostics["world_state_bundles_built"] = interest.BundlesBuilt;
            diagnostics["world_state_bundles_applied"] = interest.BundlesApplied;
            diagnostics["world_state_adapter_bundles_queued"] = bundlesQueued;
            diagnostics["world_state_application_acknowledgements"] = applicationAcks;
            RemoteWorldStateCompactionSchedulerSnapshot compaction =
                _compactionScheduler?.GetSnapshot() ?? default;
            diagnostics["world_state_compaction_enabled"] = _compactionSettings.Enabled ? 1 : 0;
            diagnostics["world_state_compaction_headroom_identities"] =
                _compactionSettings.MaximumNewIdentitiesWhileWaiting;
            diagnostics["world_state_compaction_safety_margin_identities"] =
                _compactionSettings.SafetyMargin;
            diagnostics["world_state_compaction_maximum_starts_per_tick"] =
                compaction.MaximumStartsPerTick;
            diagnostics["world_state_compaction_minimum_ticks_between_starts"] =
                compaction.MinimumTicksBetweenStarts;
            diagnostics["world_state_compaction_scheduler_rate_limited"] =
                compaction.RequestsRateLimited;
            diagnostics["world_state_compaction_scheduler_pending"] = compaction.PendingRequests;
            diagnostics["world_state_compaction_scheduler_requests_accepted"] =
                compaction.RequestsAccepted;
            diagnostics["world_state_compaction_scheduler_duplicate_requests"] =
                compaction.DuplicateRequests;
            diagnostics["world_state_compaction_scheduler_requests_rejected"] =
                compaction.RequestsRejected;
            diagnostics["world_state_compaction_scheduler_started"] =
                compaction.CompactionsStarted;
            diagnostics["world_state_compaction_requests_received"] = compactionRequestsReceived;
            diagnostics["world_state_compaction_requests_rejected"] = compactionRequestsRejected;
            diagnostics["world_state_compaction_requests_sent"] = compactionRequestsSent;
            diagnostics["world_state_baselines_started"] = baselinesStarted;
            diagnostics["world_state_baselines_applied"] = baselinesApplied;
            diagnostics["world_state_baselines_rejected"] = baselinesRejected;
            diagnostics["world_state_baselines_timed_out"] = baselinesTimedOut;
            diagnostics["world_state_baselines_abandoned"] = baselinesAbandoned;
            diagnostics["world_state_baseline_records_queued"] = baselineRecordsQueued;
            diagnostics["world_state_baseline_wire_bytes_queued"] = baselineWireBytesQueued;
            diagnostics["world_state_compaction_baselines_in_progress"] = baselineInProgress;
            NetworkMetricSummary knownIdentitySummary = _knownIdentityCounts.Summary();
            NetworkMetricSummary pressureToStart = _compactionPressureToStartTicks.Summary();
            NetworkMetricSummary baselineDuration = _compactionBaselineDurationTicks.Summary();
            diagnostics["world_state_known_identities_p50"] = knownIdentitySummary.P50;
            diagnostics["world_state_known_identities_p95"] = knownIdentitySummary.P95;
            diagnostics["world_state_known_identities_max"] = knownIdentitySummary.Max;
            diagnostics["world_state_compaction_pressure_to_start_p50_ticks"] = pressureToStart.P50;
            diagnostics["world_state_compaction_pressure_to_start_p95_ticks"] = pressureToStart.P95;
            diagnostics["world_state_compaction_pressure_to_start_max_ticks"] = pressureToStart.Max;
            diagnostics["world_state_compaction_baseline_duration_p50_ticks"] = baselineDuration.P50;
            diagnostics["world_state_compaction_baseline_duration_p95_ticks"] = baselineDuration.P95;
            diagnostics["world_state_compaction_baseline_duration_max_ticks"] = baselineDuration.Max;
            diagnostics["game_replication_reserved_bytes"] = adapterReserved;
            diagnostics["game_replication_reserved_bytes_per_client"] =
                adapterReserved / (double)_bots.Length;
            diagnostics["world_state_records_applied"] = interest.SemanticRecordCopies;
            diagnostics["world_state_latest_freshness_samples"] = _freshnessSamples;
            diagnostics["world_state_freshness_sampling_ticks"] = _scenario.SettleTicks;
            diagnostics["world_state_freshness_histogram_payload_reserved_bytes"] = checked(
                (3L * GlobalFreshnessBuckets + 3L * 2_048 +
                 (long)_bots.Length * ClientFreshnessBuckets) * sizeof(long));
            diagnostics["world_state_recipient_copies_per_semantic_construction"] =
                interest.UniqueSemanticConstructions == 0 ? 0 :
                interest.SemanticRecordCopies / (double)interest.UniqueSemanticConstructions;
            diagnostics["world_state_chat_supported"] = 0;
            diagnostics["world_state_chat_messages"] = 0;
            AddExactScopeCensus(diagnostics, interest);
        }

        private void AddExactScopeCensus(
            Dictionary<string, double> diagnostics,
            GameInterestSnapshot interest)
        {
            // This is deliberately cold benchmark instrumentation. It audits the
            // production graph after convergence without changing its representation
            // or charging the O(recipients * entities) oracle to measured tick time.
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var relations = new GameRecipientEntityRelationSnapshot[interest.RelationCount];
            int relationCount = _interest.CopyRelations(relations);
            var maintainedScope = new int[_bots.Length];
            var exactScope = new int[_bots.Length];
            long exactPairCount = 0;
            for (int recipient = 0; recipient < _bots.Length; recipient++)
            {
                ReplicationSpatialPoint anchor = _positions[recipient];
                for (int entity = 0; entity < _positions.Length; entity++)
                {
                    if (ExactTier(anchor, _positions[entity]) == GameInterestTier.None) continue;
                    exactScope[recipient]++;
                    exactPairCount++;
                }
            }

            long maintainedPresent = 0;
            long maintainedOutsideExactFar = 0;
            long tierMismatches = 0;
            var tierMatrix = new long[3, 4];
            for (int index = 0; index < relationCount; index++)
            {
                ref readonly GameRecipientEntityRelationSnapshot relation = ref relations[index];
                if (!relation.DesiredPresent) continue;
                int recipient = checked((int)relation.Participant.Value - 1);
                int entity = checked((int)relation.Entity.Index);
                maintainedScope[recipient]++;
                maintainedPresent++;
                GameInterestTier exact = ExactTier(_positions[recipient], _positions[entity]);
                int exactIndex = exact switch
                {
                    GameInterestTier.Immediate => 0,
                    GameInterestTier.Near => 1,
                    GameInterestTier.Far => 2,
                    _ => 3,
                };
                int coarseIndex = relation.Tier switch
                {
                    GameInterestTier.Immediate => 0,
                    GameInterestTier.Near => 1,
                    GameInterestTier.Far => 2,
                    _ => throw new InvalidOperationException(
                        "A present maintained relation has no delivery tier."),
                };
                tierMatrix[coarseIndex, exactIndex]++;
                if (exact == GameInterestTier.None) maintainedOutsideExactFar++;
                if (relation.Tier != exact) tierMismatches++;
            }

            if (maintainedPresent != relationCount)
                throw new InvalidOperationException(
                    "The converged sparse graph retained an absent relation.");
            long maintainedInsideExactFar = maintainedPresent - maintainedOutsideExactFar;
            if (maintainedInsideExactFar != exactPairCount)
                throw new InvalidOperationException(
                    $"The coarse graph did not conservatively contain the exact Far scope: " +
                    $"graph={maintainedInsideExactFar}, oracle={exactPairCount}.");

            NetworkMetricSummary maintained = NetworkMetricSummary.From(
                maintainedScope.Select(static value => (double)value).ToArray());
            NetworkMetricSummary exactSummary = NetworkMetricSummary.From(
                exactScope.Select(static value => (double)value).ToArray());
            if (maintained.Max > _capacity.MaximumRelationsPerRecipient ||
                maintained.Max > _capacity.MaximumBaselineRecords)
                throw new InvalidOperationException(
                    $"The sparse converged scope ({maintained.Max:R}) exceeded its " +
                    $"relation/baseline bounds ({_capacity.MaximumRelationsPerRecipient}/" +
                    $"{_capacity.MaximumBaselineRecords}).");
            diagnostics["scope_census_measured"] = 1;
            diagnostics["scope_census_region_edge_mm"] = _profile.RegionEdgeMillimeters;
            int farCells = checked((_profile.FarRadiusMillimeters +
                _profile.RegionEdgeMillimeters - 1) / _profile.RegionEdgeMillimeters);
            int edgeWidth = checked(farCells * 2 + 1);
            diagnostics["scope_census_region_envelope_cells_per_recipient"] =
                checked(edgeWidth * edgeWidth);
            diagnostics["scope_census_region_edge_transaction_headroom"] =
                checked(edgeWidth * 2 - 1);
            diagnostics["scope_census_region_interest_edges"] = interest.RegionInterestEdgeCount;
            diagnostics["scope_census_maintained_relations"] = maintainedPresent;
            diagnostics["scope_census_exact_far_relations"] = exactPairCount;
            diagnostics["scope_census_coarse_only_relations"] = maintainedOutsideExactFar;
            diagnostics["scope_census_false_positive_ratio"] = maintainedPresent == 0
                ? 0
                : maintainedOutsideExactFar / (double)maintainedPresent;
            diagnostics["scope_census_tier_mismatches"] = tierMismatches;
            diagnostics["scope_census_exact_scope_p50"] = exactSummary.P50;
            diagnostics["scope_census_exact_scope_p95"] = exactSummary.P95;
            diagnostics["scope_census_exact_scope_max"] = exactSummary.Max;
            diagnostics["scope_census_maintained_scope_p50"] = maintained.P50;
            diagnostics["scope_census_maintained_scope_p95"] = maintained.P95;
            diagnostics["scope_census_maintained_scope_max"] = maintained.Max;
            diagnostics["scope_census_pending_and_emitted_exact_attribution_measured"] = 0;
            string[] coarseNames = ["immediate", "near", "far"];
            string[] exactNames = ["immediate", "near", "far", "outside"];
            for (int coarse = 0; coarse < coarseNames.Length; coarse++)
                for (int exact = 0; exact < exactNames.Length; exact++)
                    diagnostics[$"scope_census_coarse_{coarseNames[coarse]}_exact_{exactNames[exact]}"] =
                        tierMatrix[coarse, exact];
            diagnostics["scope_census_allocated_bytes"] =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        private void DrainCore(int authorityTick, bool initializeKnowledge)
        {
            for (int round = 0; round < 2_048; round++)
            {
                Schedule(checked(authorityTick + round));
                _pump(2);
                for (int index = 0; index < _bots.Length; index++)
                    _bots[index].ProcessServer(_scenario.TickHz);
                for (int index = 0; index < _bots.Length; index++)
                    _ = _bots[index].ProcessClient([], []);
                ObserveCompactionProgress(checked(authorityTick + round));
                _pump(1);
                GameInterestSnapshot snapshot = _interest.GetSnapshot();
                if (snapshot.PendingRelationCount == 0 && snapshot.OutstandingBundleCount == 0 &&
                    CompactionDrained())
                    return;
                if ((round & 15) == 15) Thread.Sleep(1);
            }
            throw new InvalidOperationException(
                initializeKnowledge
                    ? "Initial sparse observer state did not converge."
                    : "Sparse observer state did not converge after measurement.");
        }

        private bool CompactionDrained()
        {
            if (_compactionScheduler is not null &&
                _compactionScheduler.GetSnapshot().PendingRequests != 0)
                return false;
            for (int index = 0; index < _bots.Length; index++)
                if (_bots[index].WorldServer!.GetSnapshot().BaselineInProgress)
                    return false;
            return true;
        }

        private GameInterestTier Tier(
            ReplicationSpatialPoint recipient,
            ReplicationSpatialPoint entity)
        {
            long dx = (long)entity.XMillimeters - recipient.XMillimeters;
            long dz = (long)entity.ZMillimeters - recipient.ZMillimeters;
            long distanceSquared = checked(dx * dx + dz * dz);
            if (distanceSquared <=
                (long)_profile.ImmediateEnterRadiusMillimeters *
                _profile.ImmediateEnterRadiusMillimeters)
                return GameInterestTier.Immediate;
            if (distanceSquared <=
                (long)_profile.NearRadiusMillimeters * _profile.NearRadiusMillimeters)
                return GameInterestTier.Near;
            return GameInterestTier.Far;
        }

        private GameInterestTier ExactTier(
            ReplicationSpatialPoint recipient,
            ReplicationSpatialPoint entity)
        {
            long dx = (long)entity.XMillimeters - recipient.XMillimeters;
            long dz = (long)entity.ZMillimeters - recipient.ZMillimeters;
            long distanceSquared = checked(dx * dx + dz * dz);
            if (distanceSquared <=
                (long)_profile.ImmediateEnterRadiusMillimeters *
                _profile.ImmediateEnterRadiusMillimeters)
                return GameInterestTier.Immediate;
            if (distanceSquared <=
                (long)_profile.NearRadiusMillimeters * _profile.NearRadiusMillimeters)
                return GameInterestTier.Near;
            if (distanceSquared <=
                (long)_profile.FarRadiusMillimeters * _profile.FarRadiusMillimeters)
                return GameInterestTier.Far;
            return GameInterestTier.None;
        }

        private long SemanticMilliseconds(int tick) =>
            checked(tick * 1_000L / _scenario.TickHz);

        private static ParticipantId Participant(int index) =>
            new(checked((ulong)index + 1));

        private static ReplicationSpatialPoint Position(Bot bot) => new(
            checked((int)Math.Round(bot.ServerX * 1_000)),
            checked((int)Math.Round(bot.ServerY * 1_000)));

        private static GameProjectedEntity Projected(
            int index,
            ulong version,
            int marker,
            int velocity) => new(
                new ReplicationEntityId(checked((uint)index), 1),
                true,
                new GameSimulationState
                {
                    TickMarker = marker,
                    InputVelocity = velocity,
                });

        private static void Require(GameInterestUpdateStatus status, string operation)
        {
            if (status is not (GameInterestUpdateStatus.Success or
                GameInterestUpdateStatus.NoChange))
                throw new InvalidOperationException($"{operation} failed: {status}.");
        }
    }

    private readonly record struct SparseObserverMetrics(
        NetworkMetricSummary ImmediateMilliseconds,
        NetworkMetricSummary NearMilliseconds,
        NetworkMetricSummary FarMilliseconds,
        NetworkMetricSummary ImmediateTicks,
        NetworkMetricSummary NearTicks,
        NetworkMetricSummary FarTicks,
        NetworkMetricSummary PerClientP95Milliseconds,
        long RecordsApplied,
        long LatestFreshnessSamples,
        long SemanticBytesCopied);

    private sealed class Bot : IDisposable
    {
        private readonly long[] _inputSentAt = new long[256];
        private readonly ushort[] _inputSentSequence = new ushort[256];
        private readonly long[] _pingSentAt = new long[256];
        private readonly ushort[] _pingSentSequence = new ushort[256];
        private readonly long[] _ownerSentAt = new long[256];
        private readonly ulong[] _ownerSentTick = new ulong[256];
        private readonly byte[] _latestInputPayload = new byte[10];
        private readonly List<double> _inputAcknowledgements;
        private readonly List<double> _pingAcknowledgements;
        private readonly List<double> _ownerFreshnessMilliseconds;
        private ushort _nextInput = 1;
        private ushort _nextPing = 1;
        private ushort _serverInput;
        private float _serverX;
        private float _serverY;
        private float _serverVelocityX;
        private float _serverVelocityY;
        private bool _hasServerInput;
        private bool _authorityChanged;
        private float _lastSparseOwnerX;
        private float _lastSparseOwnerY;
        private float _lastSparseOwnerVelocityX;
        private float _lastSparseOwnerVelocityY;
        private ulong _lastSparseOwnerTick;

        internal Bot(
            int index,
            UdpProtocolSession? pairSession,
            SharedUdpProtocolSession? sharedSession,
            float x,
            float y,
            int expectedInputSamples,
            int expectedOwnerSamples)
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
            _ownerFreshnessMilliseconds = new List<double>(expectedOwnerSamples);
        }

        internal int Index { get; }
        internal UdpProtocolSession? PairSession { get; }
        internal SharedUdpProtocolSession? SharedSession { get; }
        internal NetworkProtocolPeer Client { get; }
        internal NetworkProtocolPeer Server { get; }
        internal RemoteWorldStateServer? WorldServer { get; private set; }
        internal RemoteWorldStateClient? WorldClient { get; private set; }
        internal ushort LatestQueuedInput { get; private set; }
        internal ushort ServerInput => _serverInput;
        internal ushort LastQueuedOwnerInput { get; private set; }
        internal ushort LastReceivedOwnerInput { get; private set; }
        internal ushort LastAppliedInput { get; private set; }
        internal ulong LastAppliedOwnerTick { get; private set; }
        internal float ClientX { get; private set; }
        internal float ClientY { get; private set; }
        internal float ClientVelocityX { get; private set; }
        internal float ClientVelocityY { get; private set; }
        internal float ServerX => _serverX;
        internal float ServerY => _serverY;
        internal float ServerVelocityX => _serverVelocityX;
        internal float ServerVelocityY => _serverVelocityY;
        internal NetworkMetricSummary InputAcknowledgements => NetworkMetricSummary.From(_inputAcknowledgements);
        internal NetworkMetricSummary PingAcknowledgements => NetworkMetricSummary.From(_pingAcknowledgements);
        internal IReadOnlyList<double> OwnerFreshnessMilliseconds => _ownerFreshnessMilliseconds;

        internal void ResetMeasurementHistory()
        {
            _inputAcknowledgements.Clear();
            _pingAcknowledgements.Clear();
            Array.Clear(_inputSentAt);
            Array.Clear(_inputSentSequence);
            Array.Clear(_pingSentAt);
            Array.Clear(_pingSentSequence);
            Array.Clear(_ownerSentAt);
            Array.Clear(_ownerSentTick);
            _ownerFreshnessMilliseconds.Clear();
            _lastSparseOwnerTick = 0;
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
            LatestQueuedInput = sequence;
            bytes.CopyTo(_latestInputPayload);
            _nextInput = sequence == ushort.MaxValue ? (ushort)1 : checked((ushort)(sequence + 1));
            return true;
        }

        internal bool TryRequeueLatestInput() => LatestQueuedInput != 0 &&
            Client.TryQueue(InputDescriptor.Id, _latestInputPayload) == NetworkQueueStatus.Accepted;

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

        internal void AttachWorldState(
            RemoteWorldStateServer server,
            RemoteWorldStateClient client)
        {
            WorldServer = server;
            WorldClient = client;
        }

        internal void ProcessServer(int tickHz)
        {
            while (Server.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (WorldServer?.TryProcess(in message, payload) == true)
                {
                }
                else if (message.MessageId == InputDescriptor.Id && payload.Length == 10)
                {
                    ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    if (SequenceNewer(sequence, _serverInput))
                    {
                        _serverInput = sequence;
                        _serverVelocityX = BinaryPrimitives.ReadInt16LittleEndian(payload[6..]) / 100f;
                        _serverVelocityY = BinaryPrimitives.ReadInt16LittleEndian(payload[8..]) / 100f;
                        _hasServerInput = true;
                        _authorityChanged = true;
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
            _authorityChanged = true;
        }

        internal bool ConsumeAuthorityChanged()
        {
            bool changed = _authorityChanged;
            _authorityChanged = false;
            return changed;
        }

        internal bool ShouldQueueSparseOwner(ulong serverTick)
        {
            if (_lastSparseOwnerTick == 0 || serverTick - _lastSparseOwnerTick >= 8)
                return true;
            float dx = _serverX - _lastSparseOwnerX;
            float dy = _serverY - _lastSparseOwnerY;
            return dx * dx + dy * dy > 0.01f;
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
            if (Server.TryQueue(OwnerDescriptor.Id, bytes) != NetworkQueueStatus.Accepted)
                return false;
            _lastSparseOwnerTick = serverTick;
            _lastSparseOwnerX = _serverX;
            _lastSparseOwnerY = _serverY;
            _lastSparseOwnerVelocityX = _serverVelocityX;
            _lastSparseOwnerVelocityY = _serverVelocityY;
            LastQueuedOwnerInput = _serverInput;
            int slot = checked((int)(serverTick & 255));
            _ownerSentTick[slot] = serverTick;
            _ownerSentAt[slot] = Stopwatch.GetTimestamp();
            return true;
        }

        internal int ProcessClient(List<double> pingMs, List<double> ackMs)
        {
            int applied = 0;
            while (Client.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (WorldClient?.TryProcess(in message, payload) == true)
                {
                }
                else if (message.MessageId == OwnerDescriptor.Id && payload.Length == 30)
                {
                    ulong ownerTick = BinaryPrimitives.ReadUInt64LittleEndian(payload[4..]);
                    ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
                    LastReceivedOwnerInput = sequence;
                    if (SequenceNewerOrEqual(sequence, LastAppliedInput))
                    {
                        LastAppliedInput = sequence;
                        LastAppliedOwnerTick = ownerTick;
                        int ownerSlot = checked((int)(ownerTick & 255));
                        if (_ownerSentTick[ownerSlot] == ownerTick && _ownerSentAt[ownerSlot] != 0)
                        {
                            _ownerFreshnessMilliseconds.Add(
                                Stopwatch.GetElapsedTime(_ownerSentAt[ownerSlot]).TotalMilliseconds);
                            _ownerSentAt[ownerSlot] = 0;
                        }
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
            _ = WorldClient?.TryFlushAcknowledgements();
            return applied;
        }

        internal bool HasAppliedAtLeast(ushort sequence) =>
            sequence == 0 || SequenceNewerOrEqual(LastAppliedInput, sequence);

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

internal enum V3CadenceStall : byte
{
    None = 0,
    Authority = 1,
    Network = 2,
}

internal readonly record struct V3CadenceProfile(
    string Id,
    int WallClockHz,
    int NetworkHz,
    int AuthorityHzFirst,
    int AuthorityHzMiddle,
    int AuthorityHzLast,
    int DurationSeconds,
    V3CadenceStall Stall)
{
    internal int NominalAuthorityHz => AuthorityHzFirst;
    internal bool HasStall => Stall != V3CadenceStall.None;
    internal int StallStartWallTick => HasStall ? WallClockHz : -1;
    internal int StallDurationWallTicks => HasStall ? checked(WallClockHz / 4) : 0;
    internal int StallEndWallTick => HasStall
        ? checked(StallStartWallTick + StallDurationWallTicks)
        : -1;

    internal static V3CadenceProfile Fixed(string id, int authorityHz, int networkHz)
    {
        int wall = LeastCommonMultiple(authorityHz, networkHz);
        return new(id, wall, networkHz, authorityHz, authorityHz, authorityHz, 3,
            V3CadenceStall.None);
    }

    internal static V3CadenceProfile Transition(
        string id,
        int outerAuthorityHz,
        int middleAuthorityHz,
        int networkHz) =>
        new(id, LeastCommonMultiple(outerAuthorityHz, networkHz), networkHz,
            outerAuthorityHz, middleAuthorityHz, outerAuthorityHz, 3,
            V3CadenceStall.None);

    internal static V3CadenceProfile Stalled(string id, V3CadenceStall stall) =>
        new(id, 120, 120, 60, 60, 60, 3, stall);

    internal int AuthorityHzAt(int wallTick, int totalWallTicks)
    {
        int third = totalWallTicks / 3;
        return wallTick < third
            ? AuthorityHzFirst
            : wallTick < checked(third * 2)
                ? AuthorityHzMiddle
                : AuthorityHzLast;
    }

    internal bool IsAuthorityStalled(int wallTick) =>
        Stall == V3CadenceStall.Authority &&
        wallTick >= StallStartWallTick && wallTick < StallEndWallTick;

    internal bool IsNetworkStalled(int wallTick) =>
        Stall == V3CadenceStall.Network &&
        wallTick >= StallStartWallTick && wallTick < StallEndWallTick;

    internal V3CadenceProfile Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Cadence ID is required.");
        if (WallClockHz <= 0 || NetworkHz <= 0 || AuthorityHzFirst <= 0 ||
            AuthorityHzMiddle <= 0 || AuthorityHzLast <= 0 || DurationSeconds != 3)
            throw new ArgumentOutOfRangeException(nameof(WallClockHz));
        if (NetworkHz > WallClockHz || AuthorityHzFirst > WallClockHz ||
            AuthorityHzMiddle > WallClockHz || AuthorityHzLast > WallClockHz ||
            WallClockHz % NetworkHz != 0 || WallClockHz % AuthorityHzFirst != 0 ||
            WallClockHz % AuthorityHzMiddle != 0 || WallClockHz % AuthorityHzLast != 0)
            throw new ArgumentException("Wall cadence must exactly represent every configured rate.");
        if (!Enum.IsDefined(Stall)) throw new ArgumentOutOfRangeException(nameof(Stall));
        return this;
    }

    internal string DescribeCadence() => AuthorityHzFirst == AuthorityHzMiddle &&
        AuthorityHzMiddle == AuthorityHzLast
        ? $"authority {AuthorityHzFirst} Hz; Network {NetworkHz} Hz; stall {Stall}"
        : $"authority {AuthorityHzFirst}->{AuthorityHzMiddle}->{AuthorityHzLast} Hz; Network {NetworkHz} Hz";

    internal string DescribeSemantics() =>
        $"V3 cadence correctness/liveness profile '{Id}': {DescribeCadence()}. " +
        "Input sampling, authority stepping, and Network pumping are independently accumulated from wall time; no adaptive cadence or V2 performance ratio is claimed.";

    private static int LeastCommonMultiple(int left, int right)
    {
        int a = left;
        int b = right;
        while (b != 0)
        {
            int remainder = a % b;
            a = b;
            b = remainder;
        }
        return checked(left / a * right);
    }
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

internal sealed record V3CadenceObservation(
    V3CadenceProfile Cadence,
    NetworkBotStageResult Result);

internal sealed record V3CadenceAcceptanceReport(
    string ProtocolVersion,
    DateTimeOffset TimestampUtc,
    V3UdpTopology Topology,
    SharedUdpSocketIoMode SharedSocketIoMode,
    string Method,
    bool CanonicalStateMatchesAcrossCases,
    string CanonicalStateHash,
    IReadOnlyList<V3CadenceObservation> Observations);

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
    double MaximumRatio,
    long ReliableRetainedBytes,
    long IndependentRetainedBytes,
    long ReceivedRetainedBytes,
    long TotalRetainedBytes,
    double MaximumByteRatio,
    ulong WireBytes,
    ulong Retransmissions);

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
    internal ulong SameLaneGreedyDatagrams;
    internal int MaximumReadyFrames;

    internal void Add(NetworkUnreliableBatchOpportunity opportunity)
    {
        Inspections = checked(Inspections + 1);
        if (opportunity.ReadyFrames == 0) return;
        WithReadyFrames = checked(WithReadyFrames + 1);
        ReadyFrames = checked(ReadyFrames + (ulong)opportunity.ReadyFrames);
        PlaintextBytes = checked(PlaintextBytes + (ulong)opportunity.PlaintextBytes);
        GreedyDatagrams = checked(GreedyDatagrams + (ulong)opportunity.GreedyDatagrams);
        SameLaneGreedyDatagrams = checked(
            SameLaneGreedyDatagrams + (ulong)opportunity.SameLaneGreedyDatagrams);
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

internal struct UnreliableAggregationCensus
{
    internal ulong SentFrames;
    internal ulong SentDatagrams;
    internal ulong ReceivedFrames;
    internal ulong ReceivedDatagrams;
    internal ulong SentDatagramsWithTwoOrMoreFrames;
    internal ulong ReceivedDatagramsWithTwoOrMoreFrames;
    internal ulong SentHistogramOne;
    internal ulong SentHistogramTwo;
    internal ulong SentHistogramThree;
    internal ulong SentHistogramFour;
    internal ulong SentHistogramFive;
    internal ulong SentHistogramSix;
    internal ulong SentHistogramSeven;
    internal ulong SentHistogramEightOrMore;
    internal ulong ReceivedHistogramOne;
    internal ulong ReceivedHistogramTwo;
    internal ulong ReceivedHistogramThree;
    internal ulong ReceivedHistogramFour;
    internal ulong ReceivedHistogramFive;
    internal ulong ReceivedHistogramSix;
    internal ulong ReceivedHistogramSeven;
    internal ulong ReceivedHistogramEightOrMore;
    internal ulong SentPlaintextBytes;
    internal ulong ReceivedPlaintextBytes;
    internal int MaximumSentFramesPerDatagram;
    internal int MaximumReceivedFramesPerDatagram;
    internal int MaximumSentPlaintextBytes;
    internal int MaximumReceivedPlaintextBytes;

    internal UnreliableAggregationCensus Add(NetworkUnreliableAggregationSnapshot value) => new()
    {
        SentFrames = checked(SentFrames + value.SentFrames),
        SentDatagrams = checked(SentDatagrams + value.SentDatagrams),
        ReceivedFrames = checked(ReceivedFrames + value.ReceivedFrames),
        ReceivedDatagrams = checked(ReceivedDatagrams + value.ReceivedDatagrams),
        SentDatagramsWithTwoOrMoreFrames = checked(
            SentDatagramsWithTwoOrMoreFrames + value.SentDatagramsWithTwoOrMoreFrames),
        ReceivedDatagramsWithTwoOrMoreFrames = checked(
            ReceivedDatagramsWithTwoOrMoreFrames + value.ReceivedDatagramsWithTwoOrMoreFrames),
        SentHistogramOne = checked(SentHistogramOne + value.SentHistogramOne),
        SentHistogramTwo = checked(SentHistogramTwo + value.SentHistogramTwo),
        SentHistogramThree = checked(SentHistogramThree + value.SentHistogramThree),
        SentHistogramFour = checked(SentHistogramFour + value.SentHistogramFour),
        SentHistogramFive = checked(SentHistogramFive + value.SentHistogramFive),
        SentHistogramSix = checked(SentHistogramSix + value.SentHistogramSix),
        SentHistogramSeven = checked(SentHistogramSeven + value.SentHistogramSeven),
        SentHistogramEightOrMore = checked(SentHistogramEightOrMore + value.SentHistogramEightOrMore),
        ReceivedHistogramOne = checked(ReceivedHistogramOne + value.ReceivedHistogramOne),
        ReceivedHistogramTwo = checked(ReceivedHistogramTwo + value.ReceivedHistogramTwo),
        ReceivedHistogramThree = checked(ReceivedHistogramThree + value.ReceivedHistogramThree),
        ReceivedHistogramFour = checked(ReceivedHistogramFour + value.ReceivedHistogramFour),
        ReceivedHistogramFive = checked(ReceivedHistogramFive + value.ReceivedHistogramFive),
        ReceivedHistogramSix = checked(ReceivedHistogramSix + value.ReceivedHistogramSix),
        ReceivedHistogramSeven = checked(ReceivedHistogramSeven + value.ReceivedHistogramSeven),
        ReceivedHistogramEightOrMore = checked(
            ReceivedHistogramEightOrMore + value.ReceivedHistogramEightOrMore),
        SentPlaintextBytes = checked(SentPlaintextBytes + value.SentPlaintextBytes),
        ReceivedPlaintextBytes = checked(ReceivedPlaintextBytes + value.ReceivedPlaintextBytes),
        MaximumSentFramesPerDatagram = Math.Max(MaximumSentFramesPerDatagram,
            value.MaximumSentFramesPerDatagram),
        MaximumReceivedFramesPerDatagram = Math.Max(MaximumReceivedFramesPerDatagram,
            value.MaximumReceivedFramesPerDatagram),
        MaximumSentPlaintextBytes = Math.Max(MaximumSentPlaintextBytes,
            value.MaximumSentPlaintextBytes),
        MaximumReceivedPlaintextBytes = Math.Max(MaximumReceivedPlaintextBytes,
            value.MaximumReceivedPlaintextBytes),
    };

    internal UnreliableAggregationCensus Subtract(UnreliableAggregationCensus baseline) => new()
    {
        SentFrames = checked(SentFrames - baseline.SentFrames),
        SentDatagrams = checked(SentDatagrams - baseline.SentDatagrams),
        ReceivedFrames = checked(ReceivedFrames - baseline.ReceivedFrames),
        ReceivedDatagrams = checked(ReceivedDatagrams - baseline.ReceivedDatagrams),
        SentDatagramsWithTwoOrMoreFrames = checked(
            SentDatagramsWithTwoOrMoreFrames - baseline.SentDatagramsWithTwoOrMoreFrames),
        ReceivedDatagramsWithTwoOrMoreFrames = checked(
            ReceivedDatagramsWithTwoOrMoreFrames - baseline.ReceivedDatagramsWithTwoOrMoreFrames),
        SentHistogramOne = checked(SentHistogramOne - baseline.SentHistogramOne),
        SentHistogramTwo = checked(SentHistogramTwo - baseline.SentHistogramTwo),
        SentHistogramThree = checked(SentHistogramThree - baseline.SentHistogramThree),
        SentHistogramFour = checked(SentHistogramFour - baseline.SentHistogramFour),
        SentHistogramFive = checked(SentHistogramFive - baseline.SentHistogramFive),
        SentHistogramSix = checked(SentHistogramSix - baseline.SentHistogramSix),
        SentHistogramSeven = checked(SentHistogramSeven - baseline.SentHistogramSeven),
        SentHistogramEightOrMore = checked(SentHistogramEightOrMore - baseline.SentHistogramEightOrMore),
        ReceivedHistogramOne = checked(ReceivedHistogramOne - baseline.ReceivedHistogramOne),
        ReceivedHistogramTwo = checked(ReceivedHistogramTwo - baseline.ReceivedHistogramTwo),
        ReceivedHistogramThree = checked(ReceivedHistogramThree - baseline.ReceivedHistogramThree),
        ReceivedHistogramFour = checked(ReceivedHistogramFour - baseline.ReceivedHistogramFour),
        ReceivedHistogramFive = checked(ReceivedHistogramFive - baseline.ReceivedHistogramFive),
        ReceivedHistogramSix = checked(ReceivedHistogramSix - baseline.ReceivedHistogramSix),
        ReceivedHistogramSeven = checked(ReceivedHistogramSeven - baseline.ReceivedHistogramSeven),
        ReceivedHistogramEightOrMore = checked(
            ReceivedHistogramEightOrMore - baseline.ReceivedHistogramEightOrMore),
        SentPlaintextBytes = checked(SentPlaintextBytes - baseline.SentPlaintextBytes),
        ReceivedPlaintextBytes = checked(ReceivedPlaintextBytes - baseline.ReceivedPlaintextBytes),
        MaximumSentFramesPerDatagram = MaximumSentFramesPerDatagram,
        MaximumReceivedFramesPerDatagram = MaximumReceivedFramesPerDatagram,
        MaximumSentPlaintextBytes = MaximumSentPlaintextBytes,
        MaximumReceivedPlaintextBytes = MaximumReceivedPlaintextBytes,
    };
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
    internal ulong ReliableCompactionTicks;
    internal ulong ReceivedCompactionTicks;
    internal ulong OutboundUnreliableFrames;
    internal ulong InboundUnreliableFrames;

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
        ReliableCompactionTicks = Add(ReliableCompactionTicks, value.ReliableCompactionTicks);
        ReceivedCompactionTicks = Add(ReceivedCompactionTicks, value.ReceivedCompactionTicks);
        OutboundUnreliableFrames = Add(OutboundUnreliableFrames, value.OutboundUnreliableFrames);
        InboundUnreliableFrames = Add(InboundUnreliableFrames, value.InboundUnreliableFrames);
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
        ReliableCompactionTicks = Add(ReliableCompactionTicks, value.ReliableCompactionTicks);
        ReceivedCompactionTicks = Add(ReceivedCompactionTicks, value.ReceivedCompactionTicks);
        OutboundUnreliableFrames = Add(OutboundUnreliableFrames, value.OutboundUnreliableFrames);
        InboundUnreliableFrames = Add(InboundUnreliableFrames, value.InboundUnreliableFrames);
    }

    internal readonly ProtocolStageTimingTotal Subtract(ProtocolStageTimingTotal baseline) => new()
    {
        EmitTicks = checked(EmitTicks - baseline.EmitTicks),
        SelectionTicks = checked(SelectionTicks - baseline.SelectionTicks),
        FrameEncodeTicks = checked(FrameEncodeTicks - baseline.FrameEncodeTicks),
        SecureSendTicks = checked(SecureSendTicks - baseline.SecureSendTicks),
        EndpointSendTicks = checked(EndpointSendTicks - baseline.EndpointSendTicks),
        InboundTicks = checked(InboundTicks - baseline.InboundTicks),
        EndpointReceiveTicks = checked(EndpointReceiveTicks - baseline.EndpointReceiveTicks),
        SecureReceiveTicks = checked(SecureReceiveTicks - baseline.SecureReceiveTicks),
        FrameAdmissionTicks = checked(FrameAdmissionTicks - baseline.FrameAdmissionTicks),
        ReliableCompactionTicks = checked(ReliableCompactionTicks - baseline.ReliableCompactionTicks),
        ReceivedCompactionTicks = checked(ReceivedCompactionTicks - baseline.ReceivedCompactionTicks),
        OutboundUnreliableFrames = checked(OutboundUnreliableFrames - baseline.OutboundUnreliableFrames),
        InboundUnreliableFrames = checked(InboundUnreliableFrames - baseline.InboundUnreliableFrames),
    };

    internal readonly double TotalMilliseconds =>
        (EmitTicks + InboundTicks) * 1000d / Stopwatch.Frequency;

    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal readonly record struct SharedListenerStageTiming(
    ulong SendSystemCallTicks,
    ulong ReceivePollTicks,
    ulong ReceiveSystemCallTicks)
{
    internal SharedListenerStageTiming Subtract(SharedListenerStageTiming baseline) => new(
        checked(SendSystemCallTicks - baseline.SendSystemCallTicks),
        checked(ReceivePollTicks - baseline.ReceivePollTicks),
        checked(ReceiveSystemCallTicks - baseline.ReceiveSystemCallTicks));

    internal double TotalMilliseconds =>
        (SendSystemCallTicks + ReceivePollTicks + ReceiveSystemCallTicks) *
        1000d / Stopwatch.Frequency;
}

internal readonly record struct ProtocolStageTimingSides(
    ProtocolStageTimingTotal Client,
    ProtocolStageTimingTotal Server,
    SharedListenerStageTiming Listener)
{
    internal ProtocolStageTimingSides Subtract(ProtocolStageTimingSides baseline) => new(
        Client.Subtract(baseline.Client),
        Server.Subtract(baseline.Server),
        Listener.Subtract(baseline.Listener));
}

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
    ulong ListenerNativeSentDatagrams,
    UnreliableAggregationCensus UnreliableAggregation)
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
        UnreliableAggregation = UnreliableAggregation
            .Add(value.Client.UnreliableAggregation)
            .Add(value.Server.UnreliableAggregation),
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
    int SpawnGridWidth,
    V3UdpTopology Topology,
    SharedUdpSocketIoMode SharedSocketIoMode,
    double ActiveRatio,
    V3DiagnosticLayer Layer,
    bool DeepTiming,
    bool DiagnosticSuite,
    bool CadenceSuite,
    string? CadenceCase,
    int SparseRegionEdgeMillimeters,
    SparseCapacityMode SparseCapacityMode,
    bool SparseExactAdmission,
    bool UnreliableFrameAggregation,
    int PooledTailWindows,
    int SparseCompactionHeadroom,
    int SparseCompactionSafetyMargin,
    int SparseCompactionStartsPerTick)
{
    internal SparseCompactionSettings CompactionSettings => new(
        SparseCompactionHeadroom,
        SparseCompactionSafetyMargin,
        SparseCompactionStartsPerTick);

    internal static AdapterOptions Parse(string[] args) => new(
        Get(args, "--output=") ?? "v3-network-bot-comparison.json",
        Get(args, "--scenario=") ?? "owner_movement",
        ParseInt(Get(args, "--clients="), 32),
        ParseInt(Get(args, "--tick-hz="), 60),
        ParseInt(Get(args, "--input-hz="), 20),
        ParseInt(Get(args, "--stage-ticks="), 120),
        ParseInt(Get(args, "--settle-ticks="), 30),
        ParseDouble(Get(args, "--spawn-spacing="), 12),
        ParseInt(Get(args, "--spawn-grid-width="), 32),
        ParseTopology(Get(args, "--topology=")),
        ParseSharedSocketIoMode(Get(args, "--shared-socket-io=")),
        ParseDouble(Get(args, "--active-ratio="), 1),
        ParseLayer(Get(args, "--diagnostic-layer=")),
        Has(args, "--deep-timing"),
        Has(args, "--diagnostic-suite"),
        Has(args, "--cadence-suite"),
        Get(args, "--cadence-case="),
        ParseInt(Get(args, "--sparse-region-edge-mm="), 10_000),
        ParseSparseCapacityMode(Get(args, "--sparse-capacity-profile=")),
        Has(args, "--sparse-exact-admission"),
        Has(args, "--unreliable-frame-aggregation"),
        ParseInt(Get(args, "--pooled-tail-windows="), 0),
        ParseInt(Get(args, "--sparse-compaction-headroom="), 0),
        ParseInt(Get(args, "--sparse-compaction-safety="), 0),
        ParseInt(Get(args, "--sparse-compaction-starts-per-tick="), 0));

    internal NetworkBotScenario CreateScenario() => new NetworkBotScenario(
        Scenario, Clients, TickHz, InputHz, StageTicks, SettleTicks,
        SpawnSpacing,
        ObserverState: StringComparer.Ordinal.Equals(Scenario, "sparse_observer_movement"),
        OrderedEvents: false,
        ActiveRatio,
        SpawnGridWidth).Validate();

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
            "platform-send-receive-batch" or "native-send-receive-batch" =>
                SharedUdpSocketIoMode.PlatformSendReceiveBatch,
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
    private static SparseCapacityMode ParseSparseCapacityMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "auto" => SparseCapacityMode.Auto,
            "current10" or "derived10" => SparseCapacityMode.Current10,
            "legacy128" => SparseCapacityMode.Legacy128,
            _ => throw new ArgumentException($"Unsupported sparse capacity profile '{value}'."),
        };
}

internal enum SparseCapacityMode : byte
{
    Auto = 0,
    Legacy128 = 1,
    Current10 = 2,
}

internal readonly record struct SparseCompactionSettings(
    int MaximumNewIdentitiesWhileWaiting,
    int SafetyMargin,
    int MaximumStartsPerTick)
{
    internal bool Enabled => MaximumNewIdentitiesWhileWaiting > 0 ||
        SafetyMargin > 0 || MaximumStartsPerTick > 0;

    internal RemoteWorldStateCompactionPolicy Policy => new(
        MaximumNewIdentitiesWhileWaiting, SafetyMargin);

    internal void Validate()
    {
        if (!Enabled) return;
        if (MaximumNewIdentitiesWhileWaiting <= 0 || SafetyMargin <= 0 ||
            MaximumStartsPerTick <= 0)
            throw new ArgumentOutOfRangeException(
                "Sparse compaction requires positive headroom, safety margin, and starts-per-tick.");
    }
}

internal readonly record struct SparseCapacityBounds(
    SparseCapacityMode Mode,
    int SteadyEnvelopeCells,
    int BoundaryUnionCells,
    int SafetySlots,
    int MaximumRelationsPerRecipient,
    int MaximumKnownIdentities,
    int MaximumActiveEntities,
    int MaximumBaselineRecords)
{
    internal static SparseCapacityBounds Create(
        SparseCapacityMode mode,
        int regionEdgeMillimeters,
        NetworkBotScenario scenario,
        bool allowLongLivedCompaction = false)
    {
        bool admitsCurrentWindow =
            regionEdgeMillimeters == 10_000 &&
            scenario.SpawnSpacingMeters >= 12 &&
            checked(scenario.StageTicks + scenario.SettleTicks) /
                (double)scenario.TickHz <= 6;
        if (mode == SparseCapacityMode.Auto)
            mode = (admitsCurrentWindow || allowLongLivedCompaction) &&
                regionEdgeMillimeters == 10_000 && scenario.SpawnSpacingMeters >= 12
                ? SparseCapacityMode.Current10
                : SparseCapacityMode.Legacy128;
        int farCells = checked((39_500 + regionEdgeMillimeters - 1) /
            regionEdgeMillimeters);
        int edgeWidth = checked(farCells * 2 + 1);
        int steadyCells = checked(edgeWidth * edgeWidth);
        int calculatedBoundaryUnionCells = checked(steadyCells + edgeWidth * 2 - 1);
        if (mode == SparseCapacityMode.Legacy128)
            return new(
                mode,
                steadyCells,
                calculatedBoundaryUnionCells,
                Math.Max(0, 128 - calculatedBoundaryUnionCells),
                128, 128, 128, 128);
        if (!admitsCurrentWindow && !allowLongLivedCompaction)
            throw new ArgumentException(
                "The current10 sparse capacity profile requires the 10 m broadphase, " +
                "at least 12 m spawn spacing, and a movement window no longer than six seconds.",
                nameof(regionEdgeMillimeters));

        // The sparse profile's 39.5 m Far radius occupies a steady 9x9 cell
        // envelope. A diagonal one-cell crossing temporarily owns the old
        // square plus one entering row and column: 81 + 17 = 98 cells. Fourteen
        // explicit slots preserve baseline/application overlap and measurement
        // variance without returning to the historical 128-slot bound.
        const int steadyEnvelopeCells = 81;
        const int boundaryUnionCells = 98;
        const int safetySlots = 14;
        const int transientCapacity = boundaryUnionCells + safetySlots;
        const int baselineCapacity = steadyEnvelopeCells + safetySlots;
        return new(
            mode,
            steadyEnvelopeCells,
            boundaryUnionCells,
            safetySlots,
            transientCapacity,
            transientCapacity,
            transientCapacity,
            baselineCapacity);
    }
}
