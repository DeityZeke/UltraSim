using UltraSim.Network.Comparison;

var tests = new (string Name, Action Body)[]
{
    ("CatalogAdmitsOnlyImplementedOwnerMovement", CatalogAdmitsOnlyImplementedOwnerMovement),
    ("CanonicalHashIgnoresDiagnosticPosition", CanonicalHashIgnoresDiagnosticPosition),
    ("CanonicalHashProtectsSemanticOwnerState", CanonicalHashProtectsSemanticOwnerState),
    ("ProvisionalComparisonPermitsOnlyTopologyDifference", ProvisionalComparisonPermitsOnlyTopologyDifference),
    ("OccupancySummaryPreservesCapacityPressure", OccupancySummaryPreservesCapacityPressure),
    ("MatrixEvidenceRoundTrips", MatrixEvidenceRoundTrips),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Passed {tests.Length} Network comparison tests.");

static void CatalogAdmitsOnlyImplementedOwnerMovement()
{
    Scenario("owner_movement").Validate();
    Throws<NotSupportedException>(() => Scenario("spread_mix").Validate());
}

static void CanonicalHashIgnoresDiagnosticPosition()
{
    NetworkCanonicalOwnerState first = State(positionX: 1, positionY: 2);
    NetworkCanonicalOwnerState second = State(positionX: 100, positionY: 200);
    Equal(NetworkBotCanonicalState.ComputeHash([first]), NetworkBotCanonicalState.ComputeHash([second]));
}

static void CanonicalHashProtectsSemanticOwnerState()
{
    string baseline = NetworkBotCanonicalState.ComputeHash([State()]);
    NotEqual(baseline, NetworkBotCanonicalState.ComputeHash([State(sequence: 8)]));
    NotEqual(baseline, NetworkBotCanonicalState.ComputeHash([State(velocityX: 99)]));
    NotEqual(baseline, NetworkBotCanonicalState.ComputeHash([State(entity: 2)]));
}

static void ProvisionalComparisonPermitsOnlyTopologyDifference()
{
    NetworkEvidenceProfile left = Profile("shared-server-socket", "same-capacity-contract");
    NetworkEvidenceProfile right = Profile("per-bot-connected-pair", "same-capacity-contract");
    NetworkComparisonDecision accepted = NetworkComparisonContract.Evaluate(
        left, right, NetworkComparisonClass.Provisional);
    Equal(NetworkComparisonClass.Provisional, accepted.Classification);
    True(accepted.MayReportHeadlineRatio);

    NetworkComparisonDecision rejected = NetworkComparisonContract.Evaluate(
        left, Profile("per-bot-connected-pair", "different-capacity-contract"),
        NetworkComparisonClass.Provisional);
    Equal(NetworkComparisonClass.Unsupported, rejected.Classification);
    False(rejected.MayReportHeadlineRatio);
}

static void OccupancySummaryPreservesCapacityPressure()
{
    NetworkOccupancySummary summary = NetworkOccupancySummary.From([0, 4, 6, 8], 8);
    Equal(8, summary.Capacity);
    Equal(8, summary.Max);
    Equal(1L, summary.TicksAtOrAbove100);
    Equal(3L, summary.TicksAtOrAbove50);
    True(summary.P95 > 7);
}

static void MatrixEvidenceRoundTrips()
{
    NetworkBotStageResult stage = Stage();
    NetworkBotAdapterReport v2 = NetworkBotAdapterReport.Create("V2", stage with { Generation = "V2" });
    NetworkBotAdapterReport v3 = NetworkBotAdapterReport.Create("V3", stage with { Generation = "V3" });
    NetworkComparisonDecision decision = NetworkComparisonContract.Evaluate(
        v2.Result.EvidenceProfile, v3.Result.EvidenceProfile, NetworkComparisonClass.Provisional);
    var joined = new NetworkBotJoinedReport(
        NetworkBotAdapterReport.Protocol, DateTimeOffset.UnixEpoch, "V2-then-V3", decision,
        true, "MATCH", v2, v3, 1, 1, null, null, null);
    var matrix = new NetworkBotMatrixReport(
        NetworkBotAdapterReport.Protocol, DateTimeOffset.UnixEpoch, "owner_movement",
        [4], false, [joined], [new(4, 8, 2, 3)], true);
    string path = Path.Combine(Path.GetTempPath(), $"ultrasim-network-comparison-{Guid.NewGuid():N}.json");
    try
    {
        NetworkBotComparisonIo.Write(path, matrix);
        NetworkBotMatrixReport restored = NetworkBotComparisonIo.Read<NetworkBotMatrixReport>(path);
        Equal(matrix.ProtocolVersion, restored.ProtocolVersion);
        Equal(1, restored.Runs.Count);
        Equal(1, restored.MarginalTickCosts.Count);
        Equal(3d, restored.MarginalTickCosts[0].V3MicrosecondsPerAddedClient);
        True(restored.AllCanonicalStatesMatch);
        Equal("owner_movement", restored.Runs[0].V3.Result.Scenario.Id);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static NetworkBotScenario Scenario(string id) => new(id, 4, 60, 20, 30, 10, 12, false, false);

static NetworkEvidenceProfile Profile(string topology, string capacity) => new(
    "network-bot-owner-movement-v1",
    "same semantics",
    capacity,
    "fixed 60 Hz",
    topology,
    "process-wide allocation");

static NetworkCanonicalOwnerState State(
    uint entity = 1,
    ushort sequence = 7,
    uint positionX = 10,
    uint positionY = 20,
    uint velocityX = 30,
    uint velocityY = 40) => new(0, entity, sequence, positionX, positionY, velocityX, velocityY);

static NetworkBotStageResult Stage()
{
    NetworkCanonicalOwnerState[] rows = [State()];
    return new()
    {
        Generation = "test",
        Scenario = Scenario("owner_movement"),
        EvidenceProfile = Profile("test", "same-capacity-contract"),
        ReadyClients = 1,
        TickMilliseconds = new(1, 1, 1, 1, 1),
        ServerPumpMilliseconds = default,
        ClientPumpMilliseconds = default,
        PingMilliseconds = default,
        InputAcknowledgementMilliseconds = default,
        InputAcknowledgementTicks = default,
        PerClientInputAckP95Milliseconds = default,
        PerClientPingP95Milliseconds = default,
        HandshakeMilliseconds = default,
        MetricCoverage = default,
        TickDeadlineMisses = 0,
        LongestDeadlineMissStreak = 0,
        LogicalInputs = 1,
        OwnerStatesApplied = 1,
        WorldStateRecordsApplied = 0,
        ApplicationEventsApplied = 0,
        WireDatagramsSent = 0,
        WireBytesSent = 0,
        RetransmittedPackets = 0,
        RetransmittedWireBytes = 0,
        ReservedNetworkBytes = 0,
        ConfiguredSocketBufferBytes = 0,
        ManagedHeapBytes = 0,
        WorkingSetBytes = 0,
        PrivateBytes = 0,
        AllocatedBytes = 0,
        Gen0Collections = 0,
        Gen1Collections = 0,
        Gen2Collections = 0,
        ProcessCpuMilliseconds = 0,
        ProcessCpuCoreUtilization = 0,
        MaximumReliableQueued = 0,
        MaximumUnreliableQueued = 0,
        MaximumReceivedQueued = 0,
        MaximumInFlightPackets = 0,
        ReliableOccupancy = default,
        IndependentUnreliableOccupancy = default,
        ReceivedOccupancy = default,
        InFlightOccupancy = default,
        CapacityPressureTicks50 = 0,
        CapacityPressureTicks75 = 0,
        CapacityPressureTicks90 = 0,
        CapacityPressureTicks100 = 0,
        CapacityDrops = 0,
        HandshakeAttempts = 0,
        HandshakeSuccesses = 0,
        HandshakeAllocatedBytes = 0,
        HandshakeWireDatagrams = 0,
        HandshakeWireBytes = 0,
        SocketSendCalls = 0,
        SocketReceivePollCalls = 0,
        SocketReceiveCalls = 0,
        SocketWouldBlockEvents = 0,
        MaximumSentDatagramBytes = 0,
        CanonicalOwnerStateHash = NetworkBotCanonicalState.ComputeHash(rows),
        CanonicalOwnerStates = rows,
        CanonicalValidation = "test",
        Diagnostics = new Dictionary<string, double>(),
    };
}

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("Expected true.");
}

static void False(bool condition) => True(!condition);

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void NotEqual<T>(T unexpected, T actual) where T : notnull
{
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        throw new InvalidOperationException($"Did not expect {unexpected}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
