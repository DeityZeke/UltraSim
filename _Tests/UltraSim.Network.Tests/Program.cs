using System.Runtime.CompilerServices;
using UltraSim.Network;
using UltraSim.Network.Comparison;

var tests = new (string Name, Action Body)[]
{
    ("ConnectionIdentityIsGenerationalAndFixedWidth", ConnectionIdentityIsGenerationalAndFixedWidth),
    ("LaneTableIsExhaustiveAndSemanticallyDistinct", LaneTableIsExhaustiveAndSemanticallyDistinct),
    ("ImpairmentIsStatelessDeterministicAndBounded", ImpairmentIsStatelessDeterministicAndBounded),
    ("ImpairmentProbabilityBoundariesAreExact", ImpairmentProbabilityBoundariesAreExact),
    ("AdmittedHotContractOperationsAllocateNothing", AdmittedHotContractOperationsAllocateNothing),
    ("ComparisonProfilesRefuseInvalidHeadlineRatios", ComparisonProfilesRefuseInvalidHeadlineRatios),
    ("DirectionalEvidenceNeverProducesHeadlineRatio", DirectionalEvidenceNeverProducesHeadlineRatio),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

N02FoundationTests.RunAll();
N03ProtocolTests.RunAll();
N05UdpTransportTests.RunAll();
N010SharedUdpListenerTests.RunAll();
H01HostedUdpAdmissionTests.RunAll();
H02HostedUdpPromotionTests.RunAll();
H03HostedUdpPathRebindingTests.RunAll();
H04HostedUdpOwnerShardingTests.RunAll();
H05HostedUdpCompositionTests.RunAll();

Console.WriteLine($"All {tests.Length} Network N0.0 tests plus N0.2/N0.3/N0.5/N0.10.1/H0.1-H0.5 tests passed.");

static void ConnectionIdentityIsGenerationalAndFixedWidth()
{
    Equal(8, Unsafe.SizeOf<ConnectionId>());
    False(default(ConnectionId).IsValid);
    var first = new ConnectionId(7, 1);
    var reused = new ConnectionId(7, 2);
    True(first.IsValid);
    False(first == reused);
    Equal("7:1", first.ToString());
    Throws<ArgumentOutOfRangeException>(() => _ = new ConnectionId(7, 0));
}

static void LaneTableIsExhaustiveAndSemanticallyDistinct()
{
    NetworkLane[] lanes = Enum.GetValues<NetworkLane>();
    Equal(NetworkLaneContracts.Count, lanes.Length);
    foreach (NetworkLane lane in lanes)
        Equal(lane, NetworkLaneContracts.Get(lane).Lane);
    ReadOnlySpan<NetworkLane> priority = NetworkLaneContracts.PriorityOrder;
    Equal(NetworkLaneContracts.Count, priority.Length);
    uint seen = 0;
    byte previousPriority = 0;
    for (int index = 0; index < priority.Length; index++)
    {
        uint bit = 1U << (int)priority[index];
        False((seen & bit) != 0);
        seen |= bit;
        byte currentPriority = NetworkLaneContracts.Get(priority[index]).Priority;
        if (index != 0) True(previousPriority <= currentPriority);
        previousPriority = currentPriority;
    }
    Equal((1U << NetworkLaneContracts.Count) - 1, seen);

    NetworkLaneContract control = NetworkLaneContracts.Get(NetworkLane.Control);
    NetworkLaneContract input = NetworkLaneContracts.Get(NetworkLane.Input);
    NetworkLaneContract owner = NetworkLaneContracts.Get(NetworkLane.OwnerState);
    NetworkLaneContract world = NetworkLaneContracts.Get(NetworkLane.WorldState);
    NetworkLaneContract events = NetworkLaneContracts.Get(NetworkLane.Event);
    NetworkLaneContract bulk = NetworkLaneContracts.Get(NetworkLane.Bulk);
    NetworkLaneContract disposable = NetworkLaneContracts.Get(NetworkLane.Disposable);

    Equal(NetworkDeliveryGuarantee.Reliable, control.Delivery);
    Equal(NetworkRetentionPolicy.ExpiringInput, input.Retention);
    Equal(NetworkRetentionPolicy.LatestState, owner.Retention);
    Equal(NetworkRetentionPolicy.IndependentState, world.Retention);
    Equal(NetworkDeliveryOrder.None, world.Order);
    Equal(NetworkRetentionPolicy.ApplicationEvent, events.Retention);
    Equal(NetworkRetentionPolicy.SegmentedBulk, bulk.Retention);
    Equal(NetworkRetentionPolicy.Disposable, disposable.Retention);
    False(events.Retention == NetworkRetentionPolicy.LatestState);
    Throws<ArgumentOutOfRangeException>(() => NetworkLaneContracts.Get((NetworkLane)255));
}

static void ImpairmentIsStatelessDeterministicAndBounded()
{
    var options = new NetworkImpairmentOptions(0xC0FFEE, 175, 225, 125, 9);
    var first = new DeterministicNetworkImpairment(options);
    var second = new DeterministicNetworkImpairment(options);
    bool dropped = false, duplicated = false, corrupted = false, delayed = false;
    for (ulong ordinal = 0; ordinal < 20_000; ordinal++)
    {
        NetworkImpairmentDecision a = first.Decide(ordinal);
        NetworkImpairmentDecision b = second.Decide(ordinal);
        Equal(a, b);
        Equal(a, first.Decide(ordinal));
        True(a.DelaySteps <= options.MaximumDelaySteps);
        False(a.Dropped && a.Duplicated);
        False(a.Dropped && a.Corrupted);
        dropped |= a.Dropped;
        duplicated |= a.Duplicated;
        corrupted |= a.Corrupted;
        delayed |= a.DelaySteps != 0;
    }
    True(dropped && duplicated && corrupted && delayed);
}

static void ImpairmentProbabilityBoundariesAreExact()
{
    var none = new DeterministicNetworkImpairment(new(1, 0, 0, 0, 0));
    var allLost = new DeterministicNetworkImpairment(new(1, 1000, 1000, 1000, 0));
    for (ulong ordinal = 0; ordinal < 1_000; ordinal++)
    {
        Equal(new NetworkImpairmentDecision(), none.Decide(ordinal));
        NetworkImpairmentDecision lost = allLost.Decide(ordinal);
        True(lost.Dropped);
        False(lost.Duplicated);
        False(lost.Corrupted);
    }
    Throws<ArgumentOutOfRangeException>(() => _ = new NetworkImpairmentOptions(0, 1001, 0, 0, 0));
}

static void AdmittedHotContractOperationsAllocateNothing()
{
    var impairment = new DeterministicNetworkImpairment(new(7, 100, 100, 100, 4));
    _ = impairment.Decide(0);
    _ = NetworkLaneContracts.Get(NetworkLane.Control);
    ulong checksum = 0;

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (ulong ordinal = 0; ordinal < 10_000; ordinal++)
    {
        NetworkImpairmentDecision decision = impairment.Decide(ordinal);
        NetworkLaneContract lane = NetworkLaneContracts.Get((NetworkLane)(ordinal % NetworkLaneContracts.Count));
        checksum += (ulong)decision.DelaySteps + lane.Priority + (decision.Dropped ? 1UL : 0UL);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
    True(checksum != 0);
}

static void ComparisonProfilesRefuseInvalidHeadlineRatios()
{
    NetworkEvidenceProfile baseline = Profile();
    NetworkComparisonDecision direct = NetworkComparisonContract.Evaluate(
        baseline, Profile(), NetworkComparisonClass.Direct);
    True(direct.SemanticallyComparable);
    True(direct.MayReportHeadlineRatio);

    NetworkEvidenceProfile differentClock = baseline with { ClockProfile = "30hz-fixed" };
    NetworkComparisonDecision rejected = NetworkComparisonContract.Evaluate(
        baseline, differentClock, NetworkComparisonClass.Direct);
    Equal(NetworkComparisonClass.Unsupported, rejected.Classification);
    False(rejected.SemanticallyComparable);
    False(rejected.MayReportHeadlineRatio);
    True(rejected.Reason.Contains("clock profile", StringComparison.Ordinal));
}

static void DirectionalEvidenceNeverProducesHeadlineRatio()
{
    NetworkComparisonDecision directional = NetworkComparisonContract.Evaluate(
        Profile(), Profile(), NetworkComparisonClass.Directional);
    True(directional.SemanticallyComparable);
    False(directional.MayReportHeadlineRatio);

    NetworkComparisonDecision unsupported = NetworkComparisonContract.Evaluate(
        Profile(), Profile(), NetworkComparisonClass.Unsupported);
    False(unsupported.SemanticallyComparable);
    False(unsupported.MayReportHeadlineRatio);
}

static NetworkEvidenceProfile Profile() => new(
    "network-n0-contract-v1",
    "same-normalized-semantic-trace",
    "connections=64;trace=4096",
    "60hz-fixed;1x",
    "protocol-loopback",
    "caller-thread-plus-explicit-worker-telemetry");

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("Expected true.");
}

static void False(bool condition) => True(!condition);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
