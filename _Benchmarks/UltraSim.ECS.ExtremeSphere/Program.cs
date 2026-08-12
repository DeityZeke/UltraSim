using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using UltraSim.ECS.ExtremeSphere;

SuiteOptions options = SuiteOptions.Parse(args);
string repository = FindRepositoryRoot();
string outputDirectory = Path.GetFullPath(options.OutputDirectory, repository);
Directory.CreateDirectory(outputDirectory);

string v2Dll = Path.Combine(repository, "_Benchmarks", "UltraSim.ECS.ExtremeSphere.V2", "bin", "Release", "net8.0", "UltraSim.ECS.ExtremeSphere.V2.dll");
string v3Dll = Path.Combine(repository, "_Benchmarks", "UltraSim.ECS.ExtremeSphere.V3", "bin", "Release", "net8.0", "UltraSim.ECS.ExtremeSphere.V3.dll");
if (!File.Exists(v2Dll) || !File.Exists(v3Dll))
    throw new InvalidOperationException("Build both extreme-sphere adapters in Release before running the suite.");

var comparisons = new List<SphereComparison>();
foreach (ScaleProfile profile in options.Profiles)
{
    foreach (SphereExecutionMode mode in options.Modes)
    {
        string caseKey = $"{profile.Entities}-{mode.ToString().ToLowerInvariant()}";
        string v2Output = Path.Combine(outputDirectory, $"{caseKey}-v2.json");
        string v3Output = Path.Combine(outputDirectory, $"{caseKey}-v3.json");
        ExtremeSphereRunReport v2;
        ExtremeSphereRunReport v3;
        if (options.ReuseResults && File.Exists(v2Output) && File.Exists(v3Output))
        {
            v2 = ExtremeSphereJson.Read(v2Output);
            v3 = ExtremeSphereJson.Read(v3Output);
        }
        else if (options.V3First)
        {
            v3 = RunChild(v3Dll, "V3", profile, mode, options, v3Output);
            v2 = RunChild(v2Dll, "V2", profile, mode, options, v2Output);
        }
        else
        {
            v2 = RunChild(v2Dll, "V2", profile, mode, options, v2Output);
            v3 = RunChild(v3Dll, "V3", profile, mode, options, v3Output);
        }

        ValidateConfiguration(v2, "V2", profile, mode, options);
        ValidateConfiguration(v3, "V3", profile, mode, options);
        bool canonicalMatch = CanonicalMatch(v2.CanonicalState, v3.CanonicalState);
        if (!canonicalMatch)
        {
            throw new InvalidOperationException(
                $"Canonical mismatch for {caseKey}: V2={v2.CanonicalState.Hash}, V3={v3.CanonicalState.Hash}.");
        }
        comparisons.Add(SphereComparison.Create(v2, v3, canonicalMatch));
    }
}

var report = new ExtremeSphereSuiteReport
{
    ProtocolVersion = SphereWorkload.ProtocolVersion,
    TimestampUtc = DateTimeOffset.UtcNow,
    ProcessOrder = options.V3First ? "V3 then V2" : "V2 then V3",
    RuntimeControls = "Release child processes; DOTNET_TieredCompilation=0; DOTNET_ReadyToRun=0; DOTNET_GCServer=1",
    Comparisons = comparisons,
};
string suiteJson = Path.Combine(outputDirectory, "extreme-sphere-suite.json");
string suiteMarkdown = Path.Combine(outputDirectory, "extreme-sphere-suite.md");
File.WriteAllText(suiteJson, JsonSerializer.Serialize(report, ExtremeSphereJson.Options));
File.WriteAllText(suiteMarkdown, FormatMarkdown(report));
Console.WriteLine($"Suite JSON: {suiteJson}");
Console.WriteLine($"Suite report: {suiteMarkdown}");

static ExtremeSphereRunReport RunChild(
    string adapterDll,
    string generation,
    ScaleProfile profile,
    SphereExecutionMode mode,
    SuiteOptions suite,
    string output)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    start.ArgumentList.Add(adapterDll);
    Add(start, "--entities", profile.Entities);
    Add(start, "--warmup-ticks", profile.WarmupTicks);
    Add(start, "--ticks", profile.MeasuredTicks);
    Add(start, "--mode", mode.ToString().ToLowerInvariant());
    Add(start, "--workers", suite.Workers);
    Add(start, "--seed", suite.Seed);
    Add(start, "--radius", suite.Radius.ToString("R", CultureInfo.InvariantCulture));
    Add(start, "--delta", suite.Delta.ToString("R", CultureInfo.InvariantCulture));
    Add(start, "--chunk-kib", suite.ChunkKiB);
    Add(start, "--pipeline", "split3");
    Add(start, "--output", output);
    start.Environment["DOTNET_TieredCompilation"] = "0";
    start.Environment["DOTNET_ReadyToRun"] = "0";
    start.Environment["DOTNET_GCServer"] = "1";

    Console.WriteLine($"Running {generation} {mode} {profile.Entities:N0} entities ({profile.WarmupTicks} warmup, {profile.MeasuredTicks} measured)...");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start benchmark child.");
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (!string.IsNullOrWhiteSpace(stdout))
        Console.Write(stdout);
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{generation} child exited {process.ExitCode}:\n{stderr}");
    if (!string.IsNullOrWhiteSpace(stderr))
        Console.Error.Write(stderr);
    return ExtremeSphereJson.Read(output);
}

static void Add(ProcessStartInfo start, string key, object value) =>
    start.ArgumentList.Add($"{key}={value}");

static void ValidateConfiguration(
    ExtremeSphereRunReport report,
    string generation,
    ScaleProfile profile,
    SphereExecutionMode mode,
    SuiteOptions suite)
{
    var expected = new SphereRunConfiguration(
        profile.Entities,
        profile.WarmupTicks,
        profile.MeasuredTicks,
        mode,
        SpherePipeline.Split3,
        suite.Workers,
        suite.Seed,
        suite.Radius,
        suite.Delta,
        suite.ChunkKiB);
    if (!string.Equals(report.ProtocolVersion, SphereWorkload.ProtocolVersion, StringComparison.Ordinal) ||
        !string.Equals(report.Generation, generation, StringComparison.Ordinal) ||
        report.Configuration != expected)
    {
        throw new InvalidOperationException(
            $"Reused or executed {generation} result does not match the requested profile {profile.Entities}/{mode}.");
    }
}

static string FormatMarkdown(ExtremeSphereSuiteReport report)
{
    var text = new StringBuilder();
    text.AppendLine("# UltraSim extreme-sphere ECS comparison");
    text.AppendLine();
    text.AppendLine($"Generated: `{report.TimestampUtc:O}`  ");
    text.AppendLine($"Order: {report.ProcessOrder}  ");
    text.AppendLine($"Controls: {report.RuntimeControls}");
    text.AppendLine();
    text.AppendLine("All rows use the same deterministic 36-byte Position/Velocity/Pulse state and the same phase → velocity → movement pipeline. Positive speedup means V3 is faster.");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | V2 mean ms | V3 mean ms | V2 p95 ms | V3 p95 ms | V3 speedup | V2 ns/entity | V3 ns/entity | V2 alloc/tick | V3 alloc/tick | Hash |");
    text.AppendLine("|---:|:---|---:|---:|---:|---:|---:|---:|---:|---:|---:|:---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        ExtremeSphereRunReport v2 = comparison.V2;
        ExtremeSphereRunReport v3 = comparison.V3;
        text.AppendLine($"| {v2.Configuration.EntityCount:N0} | {v2.Configuration.Mode} | " +
            $"{v2.TickTiming.MeanMicroseconds / 1000d:F3} | {v3.TickTiming.MeanMicroseconds / 1000d:F3} | " +
            $"{v2.TickTiming.P95Microseconds / 1000d:F3} | {v3.TickTiming.P95Microseconds / 1000d:F3} | " +
            $"{comparison.V3TickSpeedup:F2}× | {v2.MeanNanosecondsPerEntityTick:F2} | {v3.MeanNanosecondsPerEntityTick:F2} | " +
            $"{v2.MeasurementAllocations.AllThreadsBytes / (double)v2.Configuration.MeasuredTicks:F0} B | " +
            $"{v3.MeasurementAllocations.AllThreadsBytes / (double)v3.Configuration.MeasuredTicks:F0} B | " +
            $"{(comparison.CanonicalMatch ? "match" : "FAIL")} |");
    }
    text.AppendLine();
    text.AppendLine("## Tail latency");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | V2 p50 ms | V3 p50 ms | V2 p99 ms | V3 p99 ms | V2 p99.9 ms | V3 p99.9 ms | V2 max ms | V3 max ms | V2 stddev ms | V3 stddev ms |");
    text.AppendLine("|---:|:---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        TimingDistribution v2 = comparison.V2.TickTiming;
        TimingDistribution v3 = comparison.V3.TickTiming;
        text.AppendLine($"| {comparison.V2.Configuration.EntityCount:N0} | {comparison.V2.Configuration.Mode} | " +
            $"{Ms(v2.P50Microseconds):F3} | {Ms(v3.P50Microseconds):F3} | " +
            $"{Ms(v2.P99Microseconds):F3} | {Ms(v3.P99Microseconds):F3} | " +
            $"{Ms(v2.P999Microseconds):F3} | {Ms(v3.P999Microseconds):F3} | " +
            $"{Ms(v2.MaxMicroseconds):F3} | {Ms(v3.MaxMicroseconds):F3} | " +
            $"{Ms(v2.StandardDeviationMicroseconds):F3} | {Ms(v3.StandardDeviationMicroseconds):F3} |");
    }
    text.AppendLine();
    text.AppendLine("## Sustained throughput");
    text.AppendLine();
    text.AppendLine("The effective bandwidth model counts 96 bytes/entity/tick: phase read+write (24 B), velocity reads+writes (36 B), and movement reads+writes (36 B).");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | V2 M entity-ticks/s | V3 M entity-ticks/s | V2 effective GiB/s | V3 effective GiB/s | V3 throughput speedup |");
    text.AppendLine("|---:|:---|---:|---:|---:|---:|---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        text.AppendLine($"| {comparison.V2.Configuration.EntityCount:N0} | {comparison.V2.Configuration.Mode} | " +
            $"{comparison.V2.EntityTicksPerSecond / 1_000_000d:F2} | {comparison.V3.EntityTicksPerSecond / 1_000_000d:F2} | " +
            $"{comparison.V2.EffectiveGiBPerSecond:F2} | {comparison.V3.EffectiveGiBPerSecond:F2} | {comparison.V3TickSpeedup:F2}× |");
    }
    text.AppendLine();
    text.AppendLine("## Per-system execution");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | System | V2 ns/row | V3 ns/row | V3 speedup | V2 effective GiB/s | V3 effective GiB/s |");
    text.AppendLine("|---:|:---|:---|---:|---:|---:|---:|---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        for (int index = 0; index < comparison.V2.Systems.Count; index++)
        {
            SphereSystemTiming v2 = comparison.V2.Systems[index];
            SphereSystemTiming v3 = comparison.V3.Systems[index];
            text.AppendLine($"| {comparison.V2.Configuration.EntityCount:N0} | {comparison.V2.Configuration.Mode} | {v2.Name} | " +
                $"{v2.MeanNanosecondsPerRow:F2} | {v3.MeanNanosecondsPerRow:F2} | " +
                $"{v2.MeanNanosecondsPerRow / v3.MeanNanosecondsPerRow:F2}× | " +
                $"{v2.EffectiveGiBPerSecond:F2} | {v3.EffectiveGiBPerSecond:F2} |");
        }
    }
    text.AppendLine();
    text.AppendLine("## Allocation and GC");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | V2 bytes/tick | V3 bytes/tick | V2 caller bytes | V3 caller bytes | V2 worker bytes | V3 worker bytes | V2 GC0/1/2 | V3 GC0/1/2 |");
    text.AppendLine("|---:|:---|---:|---:|---:|---:|---:|---:|:---:|:---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        ExtremeSphereRunReport v2 = comparison.V2;
        ExtremeSphereRunReport v3 = comparison.V3;
        text.AppendLine($"| {v2.Configuration.EntityCount:N0} | {v2.Configuration.Mode} | " +
            $"{v2.MeasurementAllocations.AllThreadsBytes / (double)v2.Configuration.MeasuredTicks:F0} | " +
            $"{v3.MeasurementAllocations.AllThreadsBytes / (double)v3.Configuration.MeasuredTicks:F0} | " +
            $"{v2.MeasurementAllocations.CallerThreadBytes:N0} | {v3.MeasurementAllocations.CallerThreadBytes:N0} | " +
            $"{v2.MeasurementAllocations.InstrumentedWorkerBytes:N0} | {v3.MeasurementAllocations.InstrumentedWorkerBytes:N0} | " +
            $"{v2.MeasurementAllocations.Gen0Collections}/{v2.MeasurementAllocations.Gen1Collections}/{v2.MeasurementAllocations.Gen2Collections} | " +
            $"{v3.MeasurementAllocations.Gen0Collections}/{v3.MeasurementAllocations.Gen1Collections}/{v3.MeasurementAllocations.Gen2Collections} |");
    }
    text.AppendLine();
    text.AppendLine("## Memory and creation");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | V2 spawn ns/entity | V3 spawn ns/entity | V2 working set MiB | V3 working set MiB | V2 private MiB | V3 private MiB | V3 known ECS MiB | V3 native chunks MiB |");
    text.AppendLine("|---:|:---|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        ExtremeSphereRunReport v2 = comparison.V2;
        ExtremeSphereRunReport v3 = comparison.V3;
        text.AppendLine($"| {v2.Configuration.EntityCount:N0} | {v2.Configuration.Mode} | {v2.Spawn.NanosecondsPerEntity:F1} | {v3.Spawn.NanosecondsPerEntity:F1} | " +
            $"{MiB(v2.AfterMeasurementMemory.WorkingSetBytes):F1} | {MiB(v3.AfterMeasurementMemory.WorkingSetBytes):F1} | " +
            $"{MiB(v2.AfterMeasurementMemory.PrivateBytes):F1} | {MiB(v3.AfterMeasurementMemory.PrivateBytes):F1} | " +
            $"{MiB(v3.AfterMeasurementMemory.ReservedEcsPayloadBytes):F1} | {MiB(v3.AfterMeasurementMemory.NativeChunkPayloadBytes):F1} |");
    }
    text.AppendLine();
    text.AppendLine("## Correctness and V3 scheduler counters");
    text.AppendLine();
    text.AppendLine("| Entities | Mode | Rows | Hash | Non-finite | Mean radius | Min radius | Max radius | V3 dispatches | V3 wakes | V3 parks |");
    text.AppendLine("|---:|:---|---:|:---|---:|---:|---:|---:|---:|---:|---:|");
    foreach (SphereComparison comparison in report.Comparisons)
    {
        ExtremeSphereRunReport v3 = comparison.V3;
        CanonicalSphereState state = v3.CanonicalState;
        text.AppendLine($"| {v3.Configuration.EntityCount:N0} | {v3.Configuration.Mode} | {state.Rows:N0} | " +
            $"{(comparison.CanonicalMatch ? "match" : "FAIL")} `{state.Hash}` | {state.NonFiniteValues:N0} | " +
            $"{state.MeanRadius:F6} | {state.MinRadius:F6} | {state.MaxRadius:F6} | " +
            $"{v3.SchedulerDispatches:N0} | {v3.SchedulerWorkerWakes:N0} | {v3.SchedulerWorkerParks:N0} |");
    }
    text.AppendLine();
    text.AppendLine("The JSON files retain exact raw reports, including before-world/after-seed/after-measurement memory snapshots, spawn allocations and collections, complete per-system timing distributions, checksums, and runtime metadata.");
    return text.ToString();
}

static double MiB(long bytes) => bytes / (1024d * 1024d);
static double Ms(double microseconds) => microseconds / 1_000d;

static bool CanonicalMatch(in CanonicalSphereState left, in CanonicalSphereState right) =>
    left.Hash == right.Hash &&
    left.Rows == right.Rows &&
    left.NonFiniteValues == right.NonFiniteValues &&
    left.FloatBitChecksum == right.FloatBitChecksum &&
    Math.Abs(left.MeanRadius - right.MeanRadius) <= 1e-9 &&
    Math.Abs(left.MinRadius - right.MinRadius) <= 1e-9 &&
    Math.Abs(left.MaxRadius - right.MaxRadius) <= 1e-9;

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "Kernels", "ECS")) &&
            Directory.Exists(Path.Combine(directory.FullName, "_V2", "Kernels", "ECS")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the UltraSim repository root.");
}

internal readonly record struct ScaleProfile(int Entities, int WarmupTicks, int MeasuredTicks);

internal sealed record SuiteOptions(
    IReadOnlyList<ScaleProfile> Profiles,
    IReadOnlyList<SphereExecutionMode> Modes,
    int Workers,
    uint Seed,
    float Radius,
    float Delta,
    int ChunkKiB,
    bool V3First,
    bool ReuseResults,
    string OutputDirectory)
{
    internal static SuiteOptions Parse(string[] args)
    {
        string? profilesText = Value(args, "--profiles=");
        IReadOnlyList<ScaleProfile> profiles = string.IsNullOrWhiteSpace(profilesText)
            ? [new(10_000, 120, 600), new(100_000, 120, 600), new(1_000_000, 60, 300)]
            : profilesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseProfile).ToArray();
        string modesText = Value(args, "--modes=") ?? "serial,parallel";
        SphereExecutionMode[] modes = modesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static mode => Enum.Parse<SphereExecutionMode>(mode, ignoreCase: true)).ToArray();
        return new SuiteOptions(
            profiles,
            modes,
            ParseInt(Value(args, "--workers="), Math.Max(1, Math.Min(7, Environment.ProcessorCount - 1))),
            uint.TryParse(Value(args, "--seed="), out uint seed) ? seed : 0xC0FFEEu,
            ParseFloat(Value(args, "--radius="), 50f),
            ParseFloat(Value(args, "--delta="), 1f / 60f),
            ParseInt(Value(args, "--chunk-kib="), 64),
            args.Any(static argument => argument.Equals("--v3-first", StringComparison.OrdinalIgnoreCase)),
            args.Any(static argument => argument.Equals("--reuse-results", StringComparison.OrdinalIgnoreCase)),
            Value(args, "--output=") ?? Path.Combine("_Benchmarks", "results", "extreme-sphere-current"));
    }

    private static ScaleProfile ParseProfile(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length != 3)
            throw new FormatException($"Profile '{text}' must be entities:warmup:measured.");
        return new ScaleProfile(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static string? Value(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out int parsed) ? parsed : fallback;
    private static float ParseFloat(string? value, float fallback) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
}

internal sealed class ExtremeSphereSuiteReport
{
    public required string ProtocolVersion { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string ProcessOrder { get; init; }
    public required string RuntimeControls { get; init; }
    public required IReadOnlyList<SphereComparison> Comparisons { get; init; }
}

internal sealed record SphereComparison(
    ExtremeSphereRunReport V2,
    ExtremeSphereRunReport V3,
    bool CanonicalMatch,
    double V3TickSpeedup,
    double V3P95Speedup,
    double V3SpawnSpeedup,
    double V3WorkingSetRatio,
    double V3PrivateMemoryRatio)
{
    internal static SphereComparison Create(ExtremeSphereRunReport v2, ExtremeSphereRunReport v3, bool match) =>
        new(
            v2,
            v3,
            match,
            v2.TickTiming.MeanMicroseconds / v3.TickTiming.MeanMicroseconds,
            v2.TickTiming.P95Microseconds / v3.TickTiming.P95Microseconds,
            v2.Spawn.NanosecondsPerEntity / v3.Spawn.NanosecondsPerEntity,
            v3.AfterMeasurementMemory.WorkingSetBytes / (double)v2.AfterMeasurementMemory.WorkingSetBytes,
            v3.AfterMeasurementMemory.PrivateBytes / (double)v2.AfterMeasurementMemory.PrivateBytes);
}
