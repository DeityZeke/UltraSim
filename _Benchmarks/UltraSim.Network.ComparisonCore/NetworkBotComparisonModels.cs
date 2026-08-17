using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraSim.Network.Comparison;

public sealed record NetworkBotScenario(
    string Id,
    int Clients,
    int TickHz,
    int InputHz,
    int StageTicks,
    int SettleTicks,
    double SpawnSpacingMeters,
    bool ObserverState,
    bool OrderedEvents,
    double ActiveRatio = 1,
    int SpawnGridWidth = 32)
{
    public NetworkBotScenario Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A scenario ID is required.", nameof(Id));
        if (!StringComparer.Ordinal.Equals(Id, "owner_movement") &&
            !StringComparer.Ordinal.Equals(Id, "sparse_observer_movement"))
            throw new NotSupportedException(
                $"The comparison profile '{Id}' is not implemented. N0.10 admits profiles only after both adapters own equivalent semantics.");
        if (Clients <= 0) throw new ArgumentOutOfRangeException(nameof(Clients));
        if (TickHz <= 0 || InputHz <= 0 || InputHz > TickHz)
            throw new ArgumentOutOfRangeException(nameof(InputHz));
        if (StageTicks <= 0 || SettleTicks < 0) throw new ArgumentOutOfRangeException(nameof(StageTicks));
        if (!double.IsFinite(SpawnSpacingMeters) || SpawnSpacingMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(SpawnSpacingMeters));
        if (!double.IsFinite(ActiveRatio) || ActiveRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ActiveRatio));
        if (SpawnGridWidth <= 0) throw new ArgumentOutOfRangeException(nameof(SpawnGridWidth));
        if (StringComparer.Ordinal.Equals(Id, "sparse_observer_movement") && !ObserverState)
            throw new ArgumentException("The sparse observer profile requires observer state.", nameof(ObserverState));
        return this;
    }
}

public readonly record struct NetworkMetricSummary(
    double Mean,
    double P50,
    double P95,
    double P99,
    double Max)
{
    public static NetworkMetricSummary From(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return default;
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        return new(sorted.Average(), Percentile(sorted, 0.50), Percentile(sorted, 0.95),
            Percentile(sorted, 0.99), sorted[^1]);
    }

    private static double Percentile(ReadOnlySpan<double> sorted, double percentile)
    {
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}

/// <summary>Occupancy distribution for one explicitly bounded surface.</summary>
public readonly record struct NetworkOccupancySummary(
    int Capacity,
    double Mean,
    double P95,
    int Max,
    double MaximumRatio,
    long TicksAtOrAbove50,
    long TicksAtOrAbove75,
    long TicksAtOrAbove90,
    long TicksAtOrAbove100)
{
    public static NetworkOccupancySummary From(IReadOnlyList<int> values, int capacity)
    {
        if (capacity <= 0 || values.Count == 0) return default;
        int[] sorted = values.ToArray();
        Array.Sort(sorted);
        long at50 = 0;
        long at75 = 0;
        long at90 = 0;
        long at100 = 0;
        long sum = 0;
        for (int index = 0; index < sorted.Length; index++)
        {
            int value = sorted[index];
            double ratio = value / (double)capacity;
            sum += value;
            if (ratio >= 0.50) at50++;
            if (ratio >= 0.75) at75++;
            if (ratio >= 0.90) at90++;
            if (ratio >= 1.00) at100++;
        }
        return new(capacity, sum / (double)sorted.Length,
            Percentile(sorted, 0.95), sorted[^1], sorted[^1] / (double)capacity,
            at50, at75, at90, at100);
    }

    private static double Percentile(ReadOnlySpan<int> sorted, double percentile)
    {
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}

/// <summary>
/// Explicit evidence coverage. A false value means unavailable for this workload,
/// not a measured zero.
/// </summary>
public readonly record struct NetworkBotMetricCoverage(
    bool OwnerInputFreshness,
    bool WorldStateFreshnessByTier,
    bool ApplicationEventLatency,
    bool PerClientFairness,
    bool TickDeadlineCompliance,
    bool BoundedOccupancy,
    bool WireBytes,
    bool SocketSyscalls,
    bool SecurityBytes,
    bool ProcessMemory,
    bool ManagedAllocation,
    bool ProcessCpu,
    bool Handshake,
    bool InterestAmplification,
    bool BaselineRecovery,
    bool DeepPhaseTiming);

/// <summary>Marginal p95 tick slope between adjacent population stages.</summary>
public readonly record struct NetworkBotScaleDelta(
    int FromClients,
    int ToClients,
    double V2MicrosecondsPerAddedClient,
    double V3MicrosecondsPerAddedClient);

public readonly record struct NetworkCanonicalOwnerState(
    int ClientIndex,
    uint NetworkEntityId,
    ushort LastProcessedInputSequence,
    uint PositionXBits,
    uint PositionYBits,
    uint VelocityXBits,
    uint VelocityYBits);

public static class NetworkBotCanonicalState
{
    /// <summary>
    /// Hashes the common owner-state contract. Position is deliberately excluded:
    /// frozen V2 integrates arrival-relative input while V3 integrates current
    /// control every authoritative step. Both remain useful diagnostic columns.
    /// </summary>
    public static string ComputeHash(IReadOnlyList<NetworkCanonicalOwnerState> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ulong hash = 14695981039346656037UL;
        Mix(ref hash, checked((ulong)rows.Count));
        for (int index = 0; index < rows.Count; index++)
        {
            NetworkCanonicalOwnerState row = rows[index];
            Mix(ref hash, checked((ulong)row.ClientIndex));
            Mix(ref hash, row.NetworkEntityId);
            Mix(ref hash, row.LastProcessedInputSequence);
            Mix(ref hash, row.VelocityXBits);
            Mix(ref hash, row.VelocityYBits);
        }
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
            hash = unchecked((hash ^ (byte)(value >> shift)) * 1099511628211UL);
    }
}

public sealed record NetworkBotStageResult
{
    public required string Generation { get; init; }
    public required NetworkBotScenario Scenario { get; init; }
    public required NetworkEvidenceProfile EvidenceProfile { get; init; }
    public required int ReadyClients { get; init; }
    public required NetworkMetricSummary TickMilliseconds { get; init; }
    public required NetworkMetricSummary ServerPumpMilliseconds { get; init; }
    public required NetworkMetricSummary ClientPumpMilliseconds { get; init; }
    public required NetworkMetricSummary PingMilliseconds { get; init; }
    public required NetworkMetricSummary InputAcknowledgementMilliseconds { get; init; }
    public required NetworkMetricSummary InputAcknowledgementTicks { get; init; }
    public required NetworkMetricSummary PerClientInputAckP95Milliseconds { get; init; }
    public required NetworkMetricSummary PerClientPingP95Milliseconds { get; init; }
    public required NetworkMetricSummary HandshakeMilliseconds { get; init; }
    public NetworkMetricSummary ImmediateWorldStateMilliseconds { get; init; }
    public NetworkMetricSummary NearWorldStateMilliseconds { get; init; }
    public NetworkMetricSummary FarWorldStateMilliseconds { get; init; }
    public NetworkMetricSummary ImmediateWorldStateTicks { get; init; }
    public NetworkMetricSummary NearWorldStateTicks { get; init; }
    public NetworkMetricSummary FarWorldStateTicks { get; init; }
    public NetworkMetricSummary PerClientWorldStateP95Milliseconds { get; init; }
    public NetworkMetricSummary ApplicationEventMilliseconds { get; init; }
    public NetworkMetricSummary ApplicationEventTicks { get; init; }
    public required NetworkBotMetricCoverage MetricCoverage { get; init; }
    public required long TickDeadlineMisses { get; init; }
    public required int LongestDeadlineMissStreak { get; init; }
    public required long LogicalInputs { get; init; }
    public required long OwnerStatesApplied { get; init; }
    public required long WorldStateRecordsApplied { get; init; }
    public required long ApplicationEventsApplied { get; init; }
    public required ulong WireDatagramsSent { get; init; }
    public required ulong WireBytesSent { get; init; }
    public required ulong RetransmittedPackets { get; init; }
    public required ulong RetransmittedWireBytes { get; init; }
    public required long ReservedNetworkBytes { get; init; }
    public required long ConfiguredSocketBufferBytes { get; init; }
    public required long ManagedHeapBytes { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateBytes { get; init; }
    public required long AllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required double ProcessCpuMilliseconds { get; init; }
    public required double ProcessCpuCoreUtilization { get; init; }
    public required int MaximumReliableQueued { get; init; }
    public required int MaximumUnreliableQueued { get; init; }
    public required int MaximumReceivedQueued { get; init; }
    public required int MaximumInFlightPackets { get; init; }
    public required NetworkOccupancySummary ReliableOccupancy { get; init; }
    public required NetworkOccupancySummary IndependentUnreliableOccupancy { get; init; }
    public required NetworkOccupancySummary ReceivedOccupancy { get; init; }
    public required NetworkOccupancySummary InFlightOccupancy { get; init; }
    public required long CapacityPressureTicks50 { get; init; }
    public required long CapacityPressureTicks75 { get; init; }
    public required long CapacityPressureTicks90 { get; init; }
    public required long CapacityPressureTicks100 { get; init; }
    public required ulong CapacityDrops { get; init; }
    public required long HandshakeAttempts { get; init; }
    public required long HandshakeSuccesses { get; init; }
    public required long HandshakeAllocatedBytes { get; init; }
    public required ulong HandshakeWireDatagrams { get; init; }
    public required ulong HandshakeWireBytes { get; init; }
    public required ulong SocketSendCalls { get; init; }
    public required ulong SocketReceivePollCalls { get; init; }
    public required ulong SocketReceiveCalls { get; init; }
    public required ulong SocketWouldBlockEvents { get; init; }
    public required int MaximumSentDatagramBytes { get; init; }
    public required string CanonicalOwnerStateHash { get; init; }
    public required IReadOnlyList<NetworkCanonicalOwnerState> CanonicalOwnerStates { get; init; }
    public required string CanonicalValidation { get; init; }
    public required IReadOnlyDictionary<string, double> Diagnostics { get; init; }
}

public sealed record NetworkBotAdapterReport
{
    public const string Protocol = "ultrasim-network-v2-v3-bot-comparison-v3";
    public required string ProtocolVersion { get; init; }
    public required string Generation { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string OSDescription { get; init; }
    public required int ProcessorCount { get; init; }
    public required string TieredCompilation { get; init; }
    public required string ReadyToRun { get; init; }
    public required NetworkBotStageResult Result { get; init; }

    public static NetworkBotAdapterReport Create(string generation, NetworkBotStageResult result) => new()
    {
        ProtocolVersion = Protocol,
        Generation = generation,
        TimestampUtc = DateTimeOffset.UtcNow,
        RuntimeVersion = Environment.Version.ToString(),
        OSDescription = RuntimeInformation.OSDescription,
        ProcessorCount = Environment.ProcessorCount,
        TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
        ReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
        Result = result,
    };
}

public sealed record NetworkBotJoinedReport(
    string ProtocolVersion,
    DateTimeOffset TimestampUtc,
    string CaptureOrder,
    NetworkComparisonDecision Decision,
    bool CanonicalStateMatches,
    string CanonicalStatus,
    NetworkBotAdapterReport V2,
    NetworkBotAdapterReport V3,
    double? V3ToV2TickP95Ratio,
    double? V3ToV2PingP95Ratio,
    double? V3ToV2WireBytesRatio,
    double? V3ToV2AllocatedBytesRatio,
    double? V3ToV2ReservedBytesRatio);

public sealed record NetworkBotMatrixReport(
    string ProtocolVersion,
    DateTimeOffset TimestampUtc,
    string Scenario,
    IReadOnlyList<int> ClientStages,
    bool BothProcessOrders,
    IReadOnlyList<NetworkBotJoinedReport> Runs,
    IReadOnlyList<NetworkBotScaleDelta> MarginalTickCosts,
    bool AllCanonicalStatesMatch);

public static class NetworkBotComparisonIo
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Write<T>(string path, T value)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(value, JsonOptions));
    }

    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize '{path}'.");
}
