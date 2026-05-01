using UltraSim.ECS.Benchmarks.Core;
using UltraSim.ECS;
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

    yield return new BenchmarkScenario
    {
        Name = "ScriptDefinitions_BuildPrefabDescriptor_1000",
        Category = "Definitions",
        Comparable = false,
        Scale = 1000,
        WarmupCount = 2,
        IterationCount = 5,
        Execute = () => RunBuildPrefabDescriptor(1000),
    };

    yield return new BenchmarkScenario
    {
        Name = "ScriptDefinitions_BuildEcsPrefabCatalog_1000Prefabs",
        Category = "Definitions",
        Comparable = false,
        Scale = 1000,
        WarmupCount = 2,
        IterationCount = 5,
        Execute = () => RunBuildEcsPrefabCatalog(1000),
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

static ScenarioMeasurement RunBuildPrefabDescriptor(int iterations)
{
    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        PrefabDescriptor descriptor = new BenchmarkIronDagger().BuildDescriptor();
        checksum += descriptor.ComponentValues.Count + descriptor.ArchetypeKey.Length + descriptor.Key.Length;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunBuildEcsPrefabCatalog(int iterations)
{
    ArchetypeDescriptor[] archetypes = [new BenchmarkBaseBladedWeapon().BuildDescriptor()];
    PrefabDescriptor basePrefab = new BenchmarkIronDagger().BuildDescriptor();
    var prefabs = new PrefabDescriptor[iterations];
    for (int i = 0; i < iterations; i++)
    {
        prefabs[i] = basePrefab with
        {
            Key = $"items/weapons/bladed/iron_dagger_{i}",
        };
    }

    var schemaBuilder = new EcsSchemaBuilder();
    ScriptPrefabCatalogBuildResult result = ScriptPrefabCatalogBuilder.Build(
        archetypes,
        prefabs,
        schemaBuilder,
        new ScriptPrefabCatalogBuildOptions(new Random(7)));
    long checksum = result.Catalog.Count + result.ArchetypeSignatures.Count;
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

internal readonly record struct BenchmarkItemIdentity(int DefinitionId) : IComponent<BenchmarkItemIdentity>;
internal readonly record struct BenchmarkInventoryItem(int Width, int Height, int StackLimit) : IComponent<BenchmarkInventoryItem>;
internal readonly record struct BenchmarkWeaponData(int DamageMin, int DamageMax) : IComponent<BenchmarkWeaponData>;
internal readonly record struct BenchmarkBladedData(int BleedChance) : IComponent<BenchmarkBladedData>;

internal class BenchmarkBaseItem : ArchetypeShape
{
    public override string Key => "items/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        builder.AddComponent<BenchmarkItemIdentity>();
        builder.AddComponent<BenchmarkInventoryItem>();
    }
}

internal class BenchmarkBaseWeapon : BenchmarkBaseItem
{
    public override string Key => "items/weapons/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<BenchmarkWeaponData>();
    }
}

internal class BenchmarkBaseBladedWeapon : BenchmarkBaseWeapon
{
    public override string Key => "items/weapons/bladed/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<BenchmarkBladedData>();
    }
}

internal sealed class BenchmarkIronDagger : PrefabDefinition<BenchmarkBaseBladedWeapon>
{
    public override string Key => "items/weapons/bladed/iron_dagger";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new BenchmarkItemIdentity(1001));
        builder.Set(new BenchmarkInventoryItem(1, 2, 1));
        builder.Set(new BenchmarkWeaponData(3, 7));
        builder.Set(new BenchmarkBladedData(12));
    }
}
