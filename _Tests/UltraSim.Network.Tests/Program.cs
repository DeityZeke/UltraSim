using System.Runtime.CompilerServices;
using UltraSim.Network;
using UltraSim.Network.Comparison;

var tests = new (string Name, Action Body)[]
{
    ("ConnectionIdentityIsGenerationalAndFixedWidth", ConnectionIdentityIsGenerationalAndFixedWidth),
    ("ReplicationIdentityIsConnectionIndependentAndFixedWidth", ReplicationIdentityIsConnectionIndependentAndFixedWidth),
    ("LaneTableIsExhaustiveAndSemanticallyDistinct", LaneTableIsExhaustiveAndSemanticallyDistinct),
    ("SemanticTraceIsBoundedTransactionalAndDeterministic", SemanticTraceIsBoundedTransactionalAndDeterministic),
    ("SemanticTraceDigestIsOrderSensitive", SemanticTraceDigestIsOrderSensitive),
    ("SemanticTraceWarmAppendAndDigestAllocateNothing", SemanticTraceWarmAppendAndDigestAllocateNothing),
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

Console.WriteLine($"All {tests.Length} Network N0.0 tests plus N0.2/N0.3 tests passed.");

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

static void ReplicationIdentityIsConnectionIndependentAndFixedWidth()
{
    Equal(8, Unsafe.SizeOf<ReplicationEntityId>());
    Equal(8, Unsafe.SizeOf<StateEpoch>());
    var canonical = new ReplicationEntityId(31, 4);
    var recipientA = canonical;
    var recipientB = canonical;
    Equal(recipientA, recipientB);
    False(new ReplicationEntityId(31, 5) == canonical);
    Throws<ArgumentOutOfRangeException>(() => _ = new ReplicationEntityId(0, 0));
    Throws<ArgumentOutOfRangeException>(() => _ = new StateEpoch(0));
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
    Equal(NetworkRetentionPolicy.LatestState, world.Retention);
    Equal(NetworkRetentionPolicy.ApplicationEvent, events.Retention);
    Equal(NetworkRetentionPolicy.SegmentedBulk, bulk.Retention);
    Equal(NetworkRetentionPolicy.Disposable, disposable.Retention);
    False(events.Retention == NetworkRetentionPolicy.LatestState);
    Throws<ArgumentOutOfRangeException>(() => NetworkLaneContracts.Get((NetworkLane)255));
}

static void SemanticTraceIsBoundedTransactionalAndDeterministic()
{
    var first = new NetworkSemanticTrace(2);
    var second = new NetworkSemanticTrace(2);
    NetworkSemanticTraceEntry a = Entry(NetworkSemanticTraceKind.ProjectionStatePublished, 4, 10, 20);
    NetworkSemanticTraceEntry b = Entry(NetworkSemanticTraceKind.ClientStateApplied, 4, 10, 20);
    NetworkSemanticTraceEntry rejected = Entry(NetworkSemanticTraceKind.ApplicationEventPublished, 5, 11, 21);

    True(first.TryAppend(a));
    True(first.TryAppend(b));
    True(second.TryAppend(a));
    True(second.TryAppend(b));
    False(first.TryAppend(rejected));
    Equal(2, first.Count);
    True(first.CanonicalEquals(second));
    Equal(first.ComputeDigest(), second.ComputeDigest());
    True(first.ReservedBytes >= first.Count * Unsafe.SizeOf<NetworkSemanticTraceEntry>());

    first.Clear();
    Equal(0, first.Count);
    Equal(2, first.Capacity);
    False(first.CanonicalEquals(second));
}

static void SemanticTraceDigestIsOrderSensitive()
{
    var forward = new NetworkSemanticTrace(2);
    var reverse = new NetworkSemanticTrace(2);
    NetworkSemanticTraceEntry a = Entry(NetworkSemanticTraceKind.AuthorityInputAccepted, 1, 2, 3);
    NetworkSemanticTraceEntry b = Entry(NetworkSemanticTraceKind.ProjectionStatePublished, 1, 2, 3);
    True(forward.TryAppend(a));
    True(forward.TryAppend(b));
    True(reverse.TryAppend(b));
    True(reverse.TryAppend(a));
    False(forward.CanonicalEquals(reverse));
    False(forward.ComputeDigest() == reverse.ComputeDigest());

    var maximumValues = new NetworkSemanticTrace(1);
    True(maximumValues.TryAppend(new(
        NetworkSemanticTraceKind.ClockAnchorApplied,
        new StateEpoch(ulong.MaxValue),
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue)));
    False(maximumValues.ComputeDigest() == default);
}

static void SemanticTraceWarmAppendAndDigestAllocateNothing()
{
    var trace = new NetworkSemanticTrace(128);
    for (int i = 0; i < 16; i++)
        True(trace.TryAppend(Entry(NetworkSemanticTraceKind.ProjectionStatePublished, (ulong)i, 1, 2)));
    _ = trace.ComputeDigest();
    trace.Clear();

    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 128; i++)
        True(trace.TryAppend(Entry(NetworkSemanticTraceKind.ProjectionStatePublished, (ulong)i, 1, 2)));
    _ = trace.ComputeDigest();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, allocated);
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

static NetworkSemanticTraceEntry Entry(NetworkSemanticTraceKind kind, ulong tick, ulong subject, ulong version) =>
    new(kind, new StateEpoch(9), tick, subject, version, valueHash: subject ^ version);

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
