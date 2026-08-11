using UltraSim.ECS.Comparison;

var tests = new (string Name, Action Execute)[]
{
    ("Canonical hash is deterministic", CanonicalHashIsDeterministic),
    ("Canonical hash detects value changes", CanonicalHashDetectsValueChanges),
    ("Canonical hash detects wide value changes", CanonicalHashDetectsWideValueChanges),
    ("Logical entity order is enforced", LogicalEntityOrderIsEnforced),
    ("Matching reports become comparable", MatchingReportsBecomeComparable),
    ("State mismatch suppresses trustworthy comparison", StateMismatchIsVisible),
    ("Unsupported parity remains visible", UnsupportedParityRemainsVisible),
    ("Descriptor mismatch is rejected", DescriptorMismatchIsRejected),
    ("Runtime mismatch is rejected", RuntimeMismatchIsRejected),
    ("Saved baseline reports V2 control and V3 movement", SavedBaselineReportsBothGenerations),
    ("Saved baseline permits an explicit V3 profile change", SavedBaselinePermitsV3ProfileChange),
    ("Saved baseline detects cross-time state drift", SavedBaselineDetectsStateDrift),
};

int failures = 0;
foreach ((string name, Action execute) in tests)
{
    try
    {
        execute();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void CanonicalHashIsDeterministic()
{
    CanonicalState first = MakeState(value: 10);
    CanonicalState second = MakeState(value: 10);
    Equal(first, second);
}

static void CanonicalHashDetectsValueChanges()
{
    CanonicalState first = MakeState(value: 10);
    CanonicalState second = MakeState(value: 11);
    True(first.Hash != second.Hash, "A component-value change did not change the canonical hash.");
}

static void CanonicalHashDetectsWideValueChanges()
{
    var first = new LogicalStateBuilder("wide", 1, 1, 1);
    first.AddEntity(
        0,
        true,
        0,
        LogicalComponents.Position | LogicalComponents.Wide,
        wide7: 41);
    first.AddArchetype(LogicalComponents.Position | LogicalComponents.Wide, 1);

    var second = new LogicalStateBuilder("wide", 1, 1, 1);
    second.AddEntity(
        0,
        true,
        0,
        LogicalComponents.Position | LogicalComponents.Wide,
        wide7: 42);
    second.AddArchetype(LogicalComponents.Position | LogicalComponents.Wide, 1);

    True(first.Finish().Hash != second.Finish().Hash,
        "A wide-component value change did not change the canonical hash.");
}

static void LogicalEntityOrderIsEnforced()
{
    var builder = new LogicalStateBuilder("test", 1, 1, 1);
    Throws<InvalidOperationException>(() => builder.AddEntity(1, true, 0, LogicalComponents.Position));
}

static void MatchingReportsBecomeComparable()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    CanonicalState state = MakeState(10);
    CrossGenerationReport report = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, state, 20, 100)),
        MakeReport("V3", MakeMeasured(descriptor, state, 10, 0)));
    CrossGenerationResult result = Single(report.Results);
    Equal("comparable", result.Status);
    Equal(0.5, result.V3ToV2MeanRatio);
    Equal(-100L, result.AllocationDeltaBytes);
}

static void StateMismatchIsVisible()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    CrossGenerationReport report = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, MakeState(10), 20, 0)),
        MakeReport("V3", MakeMeasured(descriptor, MakeState(11), 10, 0)));
    CrossGenerationResult result = Single(report.Results);
    Equal("state-mismatch", result.Status);
    True(!result.CanonicalStateMatches, "Mismatched states were marked equal.");
    Equal(0d, result.V3ToV2MeanRatio);
    Equal(0d, result.V3ToV2P95Ratio);
}

static void UnsupportedParityRemainsVisible()
{
    ScenarioDescriptor descriptor = MakeDescriptor() with { Strength = ComparisonStrength.Unsupported };
    ComparisonScenarioResult unsupported = new()
    {
        Descriptor = descriptor,
        Status = ComparisonScenarioStatus.Unsupported,
        UnsupportedReason = "not implemented",
    };
    CrossGenerationReport report = ComparisonJoiner.Join(
        MakeReport("V2", unsupported),
        MakeReport("V3", unsupported));
    Equal("unsupported", Single(report.Results).Status);
}

static void DescriptorMismatchIsRejected()
{
    ScenarioDescriptor left = MakeDescriptor();
    ScenarioDescriptor right = left with { SemanticContract = "different" };
    Throws<InvalidOperationException>(() => ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(left, MakeState(10), 20, 0)),
        MakeReport("V3", MakeMeasured(right, MakeState(10), 10, 0))));
}

static void RuntimeMismatchIsRejected()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    ComparisonRunReport v2 = MakeReport("V2", MakeMeasured(descriptor, MakeState(10), 20, 0));
    ComparisonRunReport v3 = MakeReport("V3", MakeMeasured(descriptor, MakeState(10), 10, 0), runtime: "different");
    Throws<InvalidOperationException>(() => ComparisonJoiner.Join(v2, v3));
}

static void SavedBaselineReportsBothGenerations()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    CanonicalState state = MakeState(10);
    CrossGenerationReport baseline = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, state, 20, 100)),
        MakeReport("V3", MakeMeasured(descriptor, state, 10, 0)));
    CrossGenerationReport current = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, state, 22, 120)),
        MakeReport("V3", MakeMeasured(descriptor, state, 8, 0)));

    BaselineRegressionResult result = Single(
        ComparisonJoiner.CompareAgainstBaseline(baseline, current).Results);
    Equal("comparable", result.Status);
    Equal(1.1, result.V2CurrentToBaselineMeanRatio);
    Equal(0.8, result.V3CurrentToBaselineMeanRatio);
    Equal(20d, result.V2BaselineMeanNanosecondsPerOperation);
    Equal(22d, result.V2CurrentMeanNanosecondsPerOperation);
    Equal(10d, result.V3BaselineMeanNanosecondsPerOperation);
    Equal(8d, result.V3CurrentMeanNanosecondsPerOperation);
    Equal(0.8 / 1.1, result.ControlNormalizedV3MeanRatio);
    Equal("material improvement", result.V3Signal);
    Equal(1.1, result.V2CurrentToBaselineP95Ratio);
    Equal(0.8, result.V3CurrentToBaselineP95Ratio);
    Equal(20L, result.V2AllocationDeltaBytes);
    Equal(0L, result.V3AllocationDeltaBytes);
    True(result.V2CanonicalStateMatches && result.V3CanonicalStateMatches, "Stable states did not match.");
    string markdown = ComparisonJoiner.BuildRegressionMarkdown(
        ComparisonJoiner.CompareAgainstBaseline(baseline, current));
    True(markdown.Contains("V2 p95/base", StringComparison.Ordinal), "Regression Markdown dropped p95 reporting.");
    True(markdown.Contains("V2 alloc delta", StringComparison.Ordinal), "Regression Markdown dropped V2 allocation reporting.");
}

static void SavedBaselineDetectsStateDrift()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    CrossGenerationReport baseline = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, MakeState(10), 20, 0)),
        MakeReport("V3", MakeMeasured(descriptor, MakeState(10), 10, 0)));
    CrossGenerationReport current = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, MakeState(11), 20, 0)),
        MakeReport("V3", MakeMeasured(descriptor, MakeState(11), 10, 0)));

    BaselineRegressionResult result = Single(
        ComparisonJoiner.CompareAgainstBaseline(baseline, current).Results);
    Equal("state-mismatch", result.Status);
    True(!result.V2CanonicalStateMatches && !result.V3CanonicalStateMatches, "Cross-time state drift was not detected.");
    Equal(0d, result.V2CurrentToBaselineMeanRatio);
    Equal(0d, result.V3CurrentToBaselineMeanRatio);
}

static void SavedBaselinePermitsV3ProfileChange()
{
    ScenarioDescriptor descriptor = MakeDescriptor();
    CanonicalState state = MakeState(10);
    CrossGenerationReport baseline = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, state, 20, 0), kernelProfile: "frozen-v2"),
        MakeReport("V3", MakeMeasured(descriptor, state, 10, 0), kernelProfile: "v30"));
    CrossGenerationReport current = ComparisonJoiner.Join(
        MakeReport("V2", MakeMeasured(descriptor, state, 20, 0), kernelProfile: "frozen-v2"),
        MakeReport("V3", MakeMeasured(descriptor, state, 9, 0), kernelProfile: "v31"));

    BaselineRegressionReport report = ComparisonJoiner.CompareAgainstBaseline(baseline, current);
    Equal("comparable", Single(report.Results).Status);
    Equal("v30", report.V3BaselineProfile);
    Equal("v31", report.V3CurrentProfile);
}

static CanonicalState MakeState(int value)
{
    var builder = new LogicalStateBuilder("test", 1, 1, 1);
    builder.AddEntity(0, true, 0, LogicalComponents.Position, value, value + 1);
    builder.AddArchetype(LogicalComponents.Position, 1);
    return builder.Finish();
}

static ScenarioDescriptor MakeDescriptor() => new(
    "test",
    "Test",
    "Test",
    MeasurementBoundary.HotLoop,
    ComparisonStrength.Direct,
    1,
    1,
    8,
    "same work",
    "same state");

static ComparisonScenarioResult MakeMeasured(
    ScenarioDescriptor descriptor,
    CanonicalState state,
    double nanoseconds,
    long allocated) => new()
{
    Descriptor = descriptor,
    Status = ComparisonScenarioStatus.Measured,
    WarmupCount = 1,
    IterationCount = 1,
    MeanNanoseconds = nanoseconds,
    P95Nanoseconds = nanoseconds,
    MeanNanosecondsPerOperation = nanoseconds,
    MeanAllocatedBytes = allocated,
    CanonicalState = state,
};

static ComparisonRunReport MakeReport(
    string generation,
    ComparisonScenarioResult result,
    string? runtime = null,
    string? kernelProfile = null) => new()
{
    ProtocolVersion = ComparisonRunner.ProtocolVersion,
    Generation = generation,
    KernelProfile = kernelProfile ?? generation,
    RuntimeVersion = runtime ?? Environment.Version.ToString(),
    FrameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    OSDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    Configuration = "Release",
    TieredCompilationSetting = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
    ReadyToRunSetting = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
    ServerGarbageCollection = System.Runtime.GCSettings.IsServerGC,
    ForcesFullCollectionBeforeMeasuredIteration = true,
    AllocationMeasurementScope = "GC.GetAllocatedBytesForCurrentThread; v1 measured scenarios are single-threaded",
    ProcessorCount = Environment.ProcessorCount,
    TimestampUtc = DateTimeOffset.UtcNow,
    Results = [result],
};

static T Single<T>(IReadOnlyList<T> values)
{
    if (values.Count != 1)
        throw new InvalidOperationException($"Expected one value; found {values.Count}.");
    return values[0];
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
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
