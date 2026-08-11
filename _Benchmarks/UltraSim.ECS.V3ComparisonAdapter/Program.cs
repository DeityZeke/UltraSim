using UltraSim.ECS.Comparison;
using UltraSim.ECS.V3ComparisonAdapter;

AdapterOptions options = AdapterOptions.Parse(args, "v3-ecs-comparison.json");
ComparisonRunReport report = ComparisonRunner.Run(
    generation: "V3",
    kernelProfile: "ecs-next-phase1-structural-consolidation-v31",
    scenarios: V3ScenarioFactory.CreateAll(),
    options: options.RunOptions);
ComparisonRunner.WriteJson(report, options.OutputPath);

int measured = report.Results.Count(static result => result.Status == ComparisonScenarioStatus.Measured);
int unsupported = report.Results.Count - measured;
Console.WriteLine($"V3 comparison adapter: {measured} measured, {unsupported} unsupported");
Console.WriteLine(Path.GetFullPath(options.OutputPath));

internal sealed record AdapterOptions(string OutputPath, ComparisonRunOptions RunOptions)
{
    internal static AdapterOptions Parse(string[] args, string defaultFileName)
    {
        string output = GetValue(args, "--output=") ?? Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "results", defaultFileName);
        int warmups = ParseInt(GetValue(args, "--warmups="), 2);
        int iterations = ParseInt(GetValue(args, "--iterations="), 9);
        int? scale = int.TryParse(GetValue(args, "--scale="), out int parsedScale) ? parsedScale : null;
        return new AdapterOptions(Path.GetFullPath(output), new ComparisonRunOptions
        {
            WarmupCount = warmups,
            IterationCount = iterations,
            ScenarioFilter = GetValue(args, "--filter="),
            ScaleFilter = scale,
        });
    }

    private static string? GetValue(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out int parsed) ? parsed : fallback;
}
