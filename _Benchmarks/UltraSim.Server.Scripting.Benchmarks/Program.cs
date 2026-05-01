using UltraSim.ECS.Benchmarks.Core;
using UltraSim.Server.Scripting;

string outputPath = Path.Combine("_Benchmarks", "results", "server-scripting-benchmarks.json");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
        outputPath = args[i + 1];
}

using var cacheFixture = ScriptBenchmarkFixture.Create();
cacheFixture.WriteScript("Commands/HelloModule.cs", HelloModuleScript());
var compiler = new UltraSimScriptCompiler();
UltraSimScriptCompileResult warm = compiler.Compile(new UltraSimScriptCompilerOptions(cacheFixture.Layout));
if (warm.Kind != UltraSimScriptCompileResultKind.Compiled)
    throw new InvalidOperationException($"Benchmark fixture compile failed: {warm.Kind}");

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "ServerScripting",
    kernelName: "UltraSim.Server.Scripting",
    scenarios: CreateScenarios(cacheFixture));
MicroBenchmark.WriteReport(report, outputPath);
Console.WriteLine($"Wrote server scripting benchmark report to {outputPath}");

static IEnumerable<BenchmarkScenario> CreateScenarios(ScriptBenchmarkFixture cacheFixture)
{
    yield return new BenchmarkScenario
    {
        Name = "ScriptCompiler_CompileOneModule",
        Category = "Compile",
        Comparable = false,
        Scale = 1,
        WarmupCount = 1,
        IterationCount = 3,
        Execute = RunCompileOneModule,
    };

    yield return new BenchmarkScenario
    {
        Name = "ScriptCompiler_CacheHit_100",
        Category = "Cache",
        Comparable = false,
        Scale = 100,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunCacheHit(cacheFixture, 100),
    };
}

static ScenarioMeasurement RunCompileOneModule()
{
    using var fixture = ScriptBenchmarkFixture.Create();
    fixture.WriteScript("Commands/HelloModule.cs", HelloModuleScript());
    var compiler = new UltraSimScriptCompiler();
    UltraSimScriptCompileResult result = compiler.Compile(new UltraSimScriptCompilerOptions(fixture.Layout));
    if (result.Kind != UltraSimScriptCompileResultKind.Compiled)
        throw new InvalidOperationException($"Expected compile, got {result.Kind}.");

    return new ScenarioMeasurement(1, result.CacheKey.Length + result.SourceCount);
}

static ScenarioMeasurement RunCacheHit(ScriptBenchmarkFixture fixture, int iterations)
{
    var compiler = new UltraSimScriptCompiler();
    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        UltraSimScriptCompileResult result = compiler.Compile(new UltraSimScriptCompilerOptions(fixture.Layout));
        if (result.Kind != UltraSimScriptCompileResultKind.CacheHit)
            throw new InvalidOperationException($"Expected cache hit, got {result.Kind}.");

        checksum += result.CacheKey.Length + result.SourceCount;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static string HelloModuleScript() =>
    """
    using UltraSim.Server.Scripting;

    public sealed class HelloModule : IUltraSimScriptModule
    {
        public void Configure(IUltraSimScriptContext context)
        {
            context.Commands.Register("hello", command => command.Reply("hello"));
        }
    }
    """;

internal sealed class ScriptBenchmarkFixture : IDisposable
{
    private ScriptBenchmarkFixture(string path)
    {
        Path = path;
        Layout = UltraSimRuntimeRootResolver.FromRoot(path);
        UltraSimRuntimeTemplate.Ensure(Layout);
    }

    public string Path { get; }
    public UltraSimRuntimeRootLayout Layout { get; }

    public static ScriptBenchmarkFixture Create()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ultrasim-scripting-benchmarks", Guid.NewGuid().ToString("N"));
        return new ScriptBenchmarkFixture(path);
    }

    public void WriteScript(string relativePath, string source)
    {
        string path = System.IO.Path.Combine(Layout.ScriptsPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}
