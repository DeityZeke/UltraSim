using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Server;

int operations = 10_000_000;
string outputPath = Path.Combine("_Benchmarks", "results", "random-benchmarks.json");
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
    {
        outputPath = args[++i];
    }
    else if (args[i] == "--operations" && i + 1 < args.Length &&
        int.TryParse(args[++i], out int parsedOperations) &&
        parsedOperations > 0)
    {
        operations = parsedOperations;
    }
}

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "Random",
    kernelName: "UltraSim.Server.Core",
    scenarios: CreateScenarios(operations));
MicroBenchmark.WriteReport(report, outputPath);
PrintSummary(report, outputPath);

static IEnumerable<BenchmarkScenario> CreateScenarios(int operations)
{
    yield return Create(
        "SystemRandom_Seeded_NextInt32",
        "Int32",
        operations,
        () => RunSystemRandomNextInt32(operations));

    yield return Create(
        "RandomShared_NextInt32",
        "Int32",
        operations,
        () => RunRandomSharedNextInt32(operations));

    yield return Create(
        "XoroshiroAdapter_NextInt32",
        "Int32",
        operations,
        () => RunXoroshiroAdapterNextInt32(operations));

    yield return Create(
        "XoroshiroStruct_NextInt32",
        "Int32",
        operations,
        () => RunXoroshiroStructNextInt32(operations));

    yield return Create(
        "SystemRandom_Seeded_NextBounded",
        "BoundedInt32",
        operations,
        () => RunSystemRandomNextBounded(operations));

    yield return Create(
        "XoroshiroAdapter_NextBounded",
        "BoundedInt32",
        operations,
        () => RunXoroshiroAdapterNextBounded(operations));

    yield return Create(
        "XoroshiroStruct_NextBounded",
        "BoundedInt32",
        operations,
        () => RunXoroshiroStructNextBounded(operations));

    yield return Create(
        "SystemRandom_Seeded_NextInt64",
        "Int64",
        operations,
        () => RunSystemRandomNextInt64(operations));

    yield return Create(
        "XoroshiroAdapter_NextInt64",
        "Int64",
        operations,
        () => RunXoroshiroAdapterNextInt64(operations));

    yield return Create(
        "XoroshiroStruct_NextUInt64",
        "Int64",
        operations,
        () => RunXoroshiroStructNextUInt64(operations));
}

static BenchmarkScenario Create(
    string name,
    string category,
    int operations,
    Func<ScenarioMeasurement> execute)
{
    return new BenchmarkScenario
    {
        Name = $"{name}_{operations}",
        Category = category,
        Comparable = true,
        Scale = operations,
        WarmupCount = 2,
        IterationCount = 7,
        Execute = execute,
    };
}

static ScenarioMeasurement RunSystemRandomNextInt32(int operations)
{
    var random = new Random(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.Next();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunRandomSharedNextInt32(int operations)
{
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += Random.Shared.Next();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroAdapterNextInt32(int operations)
{
    var random = new Xoroshiro256PlusPlusRandom(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.Next();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroStructNextInt32(int operations)
{
    var random = new Xoroshiro256PlusPlus(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.NextInt32();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunSystemRandomNextBounded(int operations)
{
    var random = new Random(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.Next(1, 10_001);

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroAdapterNextBounded(int operations)
{
    var random = new Xoroshiro256PlusPlusRandom(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.Next(1, 10_001);

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroStructNextBounded(int operations)
{
    var random = new Xoroshiro256PlusPlus(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.NextInt32(1, 10_001);

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunSystemRandomNextInt64(int operations)
{
    var random = new Random(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.NextInt64();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroAdapterNextInt64(int operations)
{
    var random = new Xoroshiro256PlusPlusRandom(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += random.NextInt64();

    return new ScenarioMeasurement(operations, checksum);
}

static ScenarioMeasurement RunXoroshiroStructNextUInt64(int operations)
{
    var random = new Xoroshiro256PlusPlus(12345);
    long checksum = 0;
    for (int i = 0; i < operations; i++)
        checksum += unchecked((long)random.NextUInt64());

    return new ScenarioMeasurement(operations, checksum);
}

static void PrintSummary(BenchmarkRunReport report, string outputPath)
{
    Console.WriteLine($"Wrote random benchmark report to {outputPath}");
    foreach (IGrouping<string, BenchmarkResult> group in report.Results.GroupBy(static result => result.Category))
    {
        BenchmarkResult baseline = group.First();
        Console.WriteLine(group.Key);
        foreach (BenchmarkResult result in group)
        {
            double speedup = result.MeanNanosecondsPerOperation > 0
                ? baseline.MeanNanosecondsPerOperation / result.MeanNanosecondsPerOperation
                : 0d;
            Console.WriteLine(
                $"  {result.Name}: {result.MeanNanosecondsPerOperation:0.###} ns/op ({speedup:0.##}x vs {baseline.Name})");
        }
    }
}
