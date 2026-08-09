using System.Text.Json;
using System.Text.Json.Serialization;
using UltraSim.ECS.BitmapExperiment;

ExperimentOptions options = ExperimentOptions.Parse(args);
IReadOnlyList<ExperimentScenarioDefinition> catalog = ExperimentCatalog.Build(options.Profile, options.Scale);
Console.WriteLine($"Bitmap block-pool experiment: {catalog.Count} cases, profile={options.Profile}, scale={options.Scale:N0}");
ExperimentReport report = ExperimentRunner.Run(catalog, options.Scale, options.Warmups, options.Iterations, options.Filter, options.OrderSeed);

string outputDirectory = Path.GetFullPath(options.OutputDirectory);
Directory.CreateDirectory(outputDirectory);
string stem = $"bitmap-block-pool-{options.Profile}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
string jsonPath = Path.Combine(outputDirectory, stem + ".json");
string markdownPath = Path.Combine(outputDirectory, stem + ".md");
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};
string json = JsonSerializer.Serialize(report, jsonOptions);
File.WriteAllText(jsonPath, json);
File.WriteAllText(markdownPath, ExperimentMarkdown.Build(report));
Console.WriteLine($"Validated {report.Results.Count} measured rows across {report.Results.Select(static result => result.LogicalCaseId).Distinct().Count()} logical cases.");
Console.WriteLine(jsonPath);
Console.WriteLine(markdownPath);
if (!string.IsNullOrWhiteSpace(options.BaselinePath))
{
    string baselinePath = Path.GetFullPath(options.BaselinePath);
    ExperimentReport baseline = JsonSerializer.Deserialize<ExperimentReport>(File.ReadAllText(baselinePath), jsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize prototype baseline '{baselinePath}'.");
    ExperimentRegressionReport regression = ExperimentRegression.Compare(baseline, report);
    string regressionJsonPath = Path.Combine(outputDirectory, stem + "-regression.json");
    string regressionMarkdownPath = Path.Combine(outputDirectory, stem + "-regression.md");
    File.WriteAllText(regressionJsonPath, JsonSerializer.Serialize(regression, jsonOptions));
    File.WriteAllText(regressionMarkdownPath, ExperimentRegression.BuildMarkdown(regression));
    Console.WriteLine(regressionJsonPath);
    Console.WriteLine(regressionMarkdownPath);
}
if (!string.IsNullOrWhiteSpace(options.AcceptBaselinePath))
{
    string acceptedPath = Path.GetFullPath(options.AcceptBaselinePath);
    if (File.Exists(acceptedPath))
        throw new InvalidOperationException($"Refusing to overwrite accepted baseline '{acceptedPath}'.");
    Directory.CreateDirectory(Path.GetDirectoryName(acceptedPath) ?? ".");
    File.WriteAllText(acceptedPath, json);
    Console.WriteLine($"Accepted immutable prototype baseline: {acceptedPath}");
}

internal sealed record ExperimentOptions(
    string Profile,
    int Scale,
    int Warmups,
    int Iterations,
    string? Filter,
    string OutputDirectory,
    string? AcceptBaselinePath,
    string? BaselinePath,
    uint OrderSeed)
{
    internal static ExperimentOptions Parse(string[] args) => new(
        Get(args, "--profile=") ?? "baseline",
        ParseInt(Get(args, "--scale="), 25_000),
        ParseInt(Get(args, "--warmups="), 2),
        ParseInt(Get(args, "--iterations="), 7),
        Get(args, "--filter="),
        Get(args, "--output-dir=") ?? Path.Combine(Environment.CurrentDirectory, "_Benchmarks", "results", "bitmap-block-pool"),
        Get(args, "--accept-baseline="),
        Get(args, "--baseline="),
        ParseUInt(Get(args, "--order-seed="), 0xB17B_00B5U));

    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out int result) ? result : fallback;

    private static uint ParseUInt(string? value, uint fallback) => uint.TryParse(value, out uint result) ? result : fallback;
}
