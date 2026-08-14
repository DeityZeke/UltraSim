using System.Diagnostics;
using UltraSim.ECS.Comparison;

RunnerOptions options = RunnerOptions.Parse(args);
string repositoryRoot = FindRepositoryRoot();
Directory.CreateDirectory(options.OutputDirectory);
string v2Path = options.V2Path ?? Path.Combine(options.OutputDirectory, "v2-ecs-comparison.json");
string v3Path = options.V3Path ?? Path.Combine(options.OutputDirectory, "v3-ecs-comparison.json");
string captureOrder = options.V3First ? "V3-then-V2" : "V2-then-V3";

if (!options.UseExisting)
{
    string v2Project = Path.Combine(repositoryRoot, "_Benchmarks", "UltraSim.ECS.V2ComparisonAdapter", "UltraSim.ECS.V2ComparisonAdapter.csproj");
    string v3Project = Path.Combine(repositoryRoot, "_Benchmarks", "UltraSim.ECS.V3ComparisonAdapter", "UltraSim.ECS.V3ComparisonAdapter.csproj");
    if (options.V3First)
    {
        RunAdapter(v3Project, v3Path, options);
        RunAdapter(v2Project, v2Path, options);
    }
    else
    {
        RunAdapter(v2Project, v2Path, options);
        RunAdapter(v3Project, v3Path, options);
    }
}

ComparisonRunReport v2 = ComparisonRunner.ReadJson(v2Path);
ComparisonRunReport v3 = ComparisonRunner.ReadJson(v3Path);
CrossGenerationReport joined = ComparisonJoiner.Join(v2, v3, captureOrder);
string jsonPath = Path.Combine(options.OutputDirectory, "ecs-v2-v3-comparison.json");
string markdownPath = Path.Combine(options.OutputDirectory, "ecs-v2-v3-comparison.md");
ComparisonJoiner.WriteJson(joined, jsonPath);
string markdown = ComparisonJoiner.BuildMarkdown(joined);
File.WriteAllText(markdownPath, markdown);

int comparable = joined.Results.Count(static result => result.Status == "comparable");
int mismatched = joined.Results.Count(static result => result.Status == "state-mismatch");
int unsupported = joined.Results.Count(static result => result.Status == "unsupported");
Console.WriteLine($"Cross-generation baseline: {comparable} comparable, {mismatched} state mismatches, {unsupported} unsupported");
Console.WriteLine(Path.GetFullPath(markdownPath));
if (mismatched != 0 || joined.Results.Any(static result => result.Status == "catalog-mismatch"))
    Environment.ExitCode = 2;

if (!string.IsNullOrWhiteSpace(options.BaselinePath))
{
    string baselinePath = Path.GetFullPath(options.BaselinePath);
    CrossGenerationReport baseline = ComparisonJoiner.ReadJson(baselinePath);
    BaselineRegressionReport regression = ComparisonJoiner.CompareAgainstBaseline(baseline, joined);
    string regressionJsonPath = Path.Combine(options.OutputDirectory, "ecs-v2-v3-regression.json");
    string regressionMarkdownPath = Path.Combine(options.OutputDirectory, "ecs-v2-v3-regression.md");
    ComparisonJoiner.WriteRegressionJson(regression, regressionJsonPath);
    File.WriteAllText(regressionMarkdownPath, ComparisonJoiner.BuildRegressionMarkdown(regression));
    int regressionComparable = regression.Results.Count(static result => result.Status == "comparable");
    int regressionMismatches = regression.Results.Count(static result =>
        result.Status is "state-mismatch" or "descriptor-mismatch" or "measurement-mismatch");
    int catalogChanges = regression.Results.Count(static result => result.Status == "catalog-mismatch");
    Console.WriteLine($"Saved-baseline regression: {regressionComparable} comparable, {regressionMismatches} mismatches, {catalogChanges} catalog changes");
    Console.WriteLine(Path.GetFullPath(regressionMarkdownPath));
    if (regressionMismatches != 0)
        Environment.ExitCode = 2;
}

if (!string.IsNullOrWhiteSpace(options.AcceptBaselinePath))
{
    string acceptedPath = Path.GetFullPath(options.AcceptBaselinePath);
    if (File.Exists(acceptedPath))
        throw new InvalidOperationException($"Refusing to overwrite accepted baseline '{acceptedPath}'. Choose a new path or remove it explicitly.");
    ComparisonJoiner.WriteJson(joined, acceptedPath);
    Console.WriteLine($"Accepted immutable baseline: {acceptedPath}");
}

static void RunAdapter(string project, string output, RunnerOptions options)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(project))!,
    };
    start.Environment["DOTNET_TieredCompilation"] = "0";
    start.Environment["DOTNET_ReadyToRun"] = "0";
    start.ArgumentList.Add("run");
    start.ArgumentList.Add("--project");
    start.ArgumentList.Add(project);
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("Release");
    if (options.NoBuild)
        start.ArgumentList.Add("--no-build");
    start.ArgumentList.Add("--");
    start.ArgumentList.Add($"--output={Path.GetFullPath(output)}");
    start.ArgumentList.Add($"--warmups={options.WarmupCount}");
    start.ArgumentList.Add($"--iterations={options.IterationCount}");
    if (!string.IsNullOrWhiteSpace(options.Filter))
        start.ArgumentList.Add($"--filter={options.Filter}");
    if (options.Scale.HasValue)
        start.ArgumentList.Add($"--scale={options.Scale.Value}");

    Console.WriteLine($"Running {Path.GetFileNameWithoutExtension(project)}...");
    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException($"Could not start adapter '{project}'.");
    string standardOutput = process.StandardOutput.ReadToEnd();
    string standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (!string.IsNullOrWhiteSpace(standardOutput))
        Console.Write(standardOutput);
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Adapter failed ({process.ExitCode}): {standardError}");
    if (!string.IsNullOrWhiteSpace(standardError))
        Console.Error.Write(standardError);
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
            File.Exists(Path.Combine(current.FullName, "Docs", "README.md")) &&
            Directory.Exists(Path.Combine(current.FullName, "Kernels", "ECS")))
        {
            return current.FullName;
        }
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the UltraSim repository root.");
}

internal sealed record RunnerOptions(
    string OutputDirectory,
    string? V2Path,
    string? V3Path,
    bool UseExisting,
    bool NoBuild,
    bool V3First,
    int WarmupCount,
    int IterationCount,
    string? Filter,
    int? Scale,
    string? BaselinePath,
    string? AcceptBaselinePath)
{
    internal static RunnerOptions Parse(string[] args)
    {
        string output = GetValue(args, "--output-dir=") ?? Path.Combine(
            Environment.CurrentDirectory,
            "_Benchmarks",
            "results",
            "ecs-v2-v3");
        return new RunnerOptions(
            Path.GetFullPath(output),
            GetValue(args, "--v2="),
            GetValue(args, "--v3="),
            Has(args, "--use-existing"),
            Has(args, "--no-build"),
            Has(args, "--v3-first"),
            ParseInt(GetValue(args, "--warmups="), 2),
            ParseInt(GetValue(args, "--iterations="), 9),
            GetValue(args, "--filter="),
            int.TryParse(GetValue(args, "--scale="), out int scale) ? scale : null,
            GetValue(args, "--baseline="),
            GetValue(args, "--accept-baseline="));
    }

    private static bool Has(string[] args, string value) =>
        args.Any(argument => string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out int parsed) ? parsed : fallback;
}
