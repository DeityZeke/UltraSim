using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UltraSim.ECS;
using UltraSim.ECS.Generators;

GeneratorTests.RunAll();

internal static class GeneratorTests
{
    private const string ValidSource = """
        using UltraSim.ECS;

        namespace Golden;

        [EcsComponent("core.position")]
        internal struct Position : IComponent<Position>
        {
            public float X;
            public float Y;
        }

        [EcsComponent("core.velocity", Version = 2, IsEnableable = true, PersistenceId = 10)]
        internal struct Velocity : IComponent<Velocity>
        {
            public double X;
            public double Y;
        }

        [EcsComponent("gameplay.disabled", IsTag = true)]
        internal struct DisabledTag : IComponent<DisabledTag>
        {
        }

        [EcsSchema(typeof(DisabledTag), typeof(Velocity), typeof(Position))]
        internal static partial class GameSchema
        {
        }
        """;

    private static int _passed;

    internal static void RunAll()
    {
        Run(nameof(ValidSchema_MatchesCheckedInGoldenAndCompiles), ValidSchema_MatchesCheckedInGoldenAndCompiles);
        Run(nameof(ComponentOrder_DoesNotChangeGeneratedSchema), ComponentOrder_DoesNotChangeGeneratedSchema);
        Run(nameof(ComponentVersion_ChangesSchemaFingerprint), ComponentVersion_ChangesSchemaFingerprint);
        Run(nameof(UniquePersistenceId_IsAccepted), UniquePersistenceId_IsAccepted);
        Run("InvalidSchemaDeclaration_ReportsECSGEN001", () => AssertDiagnostic(
            "[UltraSim.ECS.EcsSchema] internal partial class BadSchema { }",
            "ECSGEN001"));
        Run("ManagedComponent_ReportsECSGEN002", () => AssertDiagnostic(
            ComponentSource("bad.managed", "public string Value;") + SchemaSource("ManagedComponent"),
            "ECSGEN002"));
        Run(nameof(DuplicateCanonicalKey_ReportsECSGEN003), DuplicateCanonicalKey_ReportsECSGEN003);
        Run(nameof(DuplicatePersistenceId_ReportsECSGEN004), DuplicatePersistenceId_ReportsECSGEN004);
        Run("InvalidCanonicalKey_ReportsECSGEN005", () => AssertDiagnostic(
            ComponentSource("Bad Key", "public int Value;") + SchemaSource("ManagedComponent"),
            "ECSGEN005"));
        Run(nameof(VersionZero_ReportsECSGEN005), VersionZero_ReportsECSGEN005);
        Run(nameof(FieldBearingTag_ReportsECSGEN006), FieldBearingTag_ReportsECSGEN006);
        Run(nameof(ComponentLimit_ReservesInvalidIdAndReportsECSGEN007), ComponentLimit_ReservesInvalidIdAndReportsECSGEN007);
        Run(nameof(DuplicateComponentType_ReportsECSGEN008), DuplicateComponentType_ReportsECSGEN008);

        Console.WriteLine($"All {_passed} ECS schema-generator tests passed.");
    }

    private static void ValidSchema_MatchesCheckedInGoldenAndCompiles()
    {
        Generation generation = Generate(ValidSource);
        AssertNoGeneratorErrors(generation);
        Diagnostic[] compileErrors = generation.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (compileErrors.Length != 0)
            throw new InvalidOperationException("Generated compilation failed:\n" + string.Join("\n", compileErrors.AsEnumerable()));

        string goldenPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "Golden",
            "ValidSchema.g.cs"));
        string expected = Normalize(File.ReadAllText(goldenPath));
        string actual = Normalize(generation.GeneratedSource);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generated source differs from {goldenPath}.\n--- ACTUAL ---\n{actual}");
        }
    }

    private static void ComponentOrder_DoesNotChangeGeneratedSchema()
    {
        const string original = "typeof(DisabledTag), typeof(Velocity), typeof(Position)";
        const string reordered = "typeof(Position), typeof(DisabledTag), typeof(Velocity)";
        Generation first = Generate(ValidSource);
        Generation second = Generate(ValidSource.Replace(original, reordered, StringComparison.Ordinal));

        AssertNoGeneratorErrors(first);
        AssertNoGeneratorErrors(second);
        TestAssert.Equal(first.GeneratedSource, second.GeneratedSource);
    }

    private static void ComponentVersion_ChangesSchemaFingerprint()
    {
        Generation first = Generate(ValidSource);
        Generation second = Generate(ValidSource.Replace("Version = 2", "Version = 3", StringComparison.Ordinal));

        string firstId = ExtractSchemaIdLine(first.GeneratedSource);
        string secondId = ExtractSchemaIdLine(second.GeneratedSource);
        TestAssert.NotEqual(firstId, secondId);
    }

    private static void UniquePersistenceId_IsAccepted()
    {
        Generation generation = Generate(ValidSource);
        AssertNoGeneratorErrors(generation);
        TestAssert.False(generation.Diagnostics.Any(static diagnostic => diagnostic.Id == "ECSGEN004"));
        TestAssert.True(generation.GeneratedSource.Contains("10U", StringComparison.Ordinal));
    }

    private static void DuplicateCanonicalKey_ReportsECSGEN003()
    {
        string source = """
            using UltraSim.ECS;
            [EcsComponent("same.key")] internal struct A : IComponent<A> { public int Value; }
            [EcsComponent("same.key")] internal struct B : IComponent<B> { public int Value; }
            [EcsSchema(typeof(A), typeof(B))] internal static partial class Schema { }
            """;
        AssertDiagnostic(source, "ECSGEN003");
    }

    private static void DuplicatePersistenceId_ReportsECSGEN004()
    {
        string source = """
            using UltraSim.ECS;
            [EcsComponent("a", PersistenceId = 7)] internal struct A : IComponent<A> { public int Value; }
            [EcsComponent("b", PersistenceId = 7)] internal struct B : IComponent<B> { public int Value; }
            [EcsSchema(typeof(A), typeof(B))] internal static partial class Schema { }
            """;
        AssertDiagnostic(source, "ECSGEN004");
    }

    private static void VersionZero_ReportsECSGEN005()
    {
        string source = """
            using UltraSim.ECS;
            [EcsComponent("bad.version", Version = 0)] internal struct Versioned : IComponent<Versioned> { }
            [EcsSchema(typeof(Versioned))] internal static partial class Schema { }
            """;
        AssertDiagnostic(source, "ECSGEN005");
    }

    private static void FieldBearingTag_ReportsECSGEN006()
    {
        string source = """
            using UltraSim.ECS;
            [EcsComponent("bad.tag", IsTag = true)] internal struct Tag : IComponent<Tag> { public int Data; }
            [EcsSchema(typeof(Tag))] internal static partial class Schema { }
            """;
        AssertDiagnostic(source, "ECSGEN006");
    }

    private static void ComponentLimit_ReservesInvalidIdAndReportsECSGEN007()
    {
        var source = new StringBuilder(1_200_000);
        source.AppendLine("using UltraSim.ECS;")
            .AppendLine("[EcsComponent(\"one\")] internal struct C : IComponent<C> { }")
            .Append("[EcsSchema(");
        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            if (i != 0)
                source.Append(',');
            source.Append("typeof(C)");
        }
        source.AppendLine(")] internal static partial class TooLarge { }");

        AssertDiagnostic(source.ToString(), "ECSGEN007");
    }

    private static void DuplicateComponentType_ReportsECSGEN008()
    {
        string source = """
            using UltraSim.ECS;
            [EcsComponent("one")] internal struct C : IComponent<C> { }
            [EcsSchema(typeof(C), typeof(C))] internal static partial class Schema { }
            """;
        AssertDiagnostic(source, "ECSGEN008");
    }

    private static string ComponentSource(string key, string members) => $$"""
        using UltraSim.ECS;
        [EcsComponent("{{key}}")] internal struct ManagedComponent : IComponent<ManagedComponent> { {{members}} }
        """;

    private static string SchemaSource(string componentName) =>
        $"\n[EcsSchema(typeof({componentName}))] internal static partial class Schema {{ }}";

    private static string ExtractSchemaIdLine(string source) => source
        .Split('\n')
        .Single(static line => line.Contains("GeneratedSchemaId =>", StringComparison.Ordinal))
        .Trim();

    private static void AssertDiagnostic(string source, string expectedId)
    {
        Generation generation = Generate(source);
        if (!generation.Diagnostics.Any(diagnostic => diagnostic.Id == expectedId))
        {
            throw new InvalidOperationException(
                $"Expected {expectedId}, got: {string.Join(", ", generation.Diagnostics.Select(static diagnostic => diagnostic.ToString()))}");
        }
    }

    private static void AssertNoGeneratorErrors(Generation generation)
    {
        Diagnostic[] errors = generation.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("Generator failed:\n" + string.Join("\n", errors.AsEnumerable()));
    }

    private static Generation Generate(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Runtime reference list is unavailable."))
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(IComponent<>).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorTest_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ISourceGenerator generator = new EcsSchemaGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> driverDiagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();
        ImmutableArray<Diagnostic> diagnostics = result.Diagnostics.AddRange(driverDiagnostics).Distinct().ToImmutableArray();
        string generatedSource = result.Results
            .SelectMany(static generatorResult => generatorResult.GeneratedSources)
            .Select(static generated => generated.SourceText.ToString())
            .SingleOrDefault() ?? string.Empty;
        return new Generation(outputCompilation, diagnostics, generatedSource);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private readonly struct Generation
    {
        internal Generation(
            Compilation outputCompilation,
            ImmutableArray<Diagnostic> diagnostics,
            string generatedSource)
        {
            OutputCompilation = outputCompilation;
            Diagnostics = diagnostics;
            GeneratedSource = generatedSource;
        }

        internal Compilation OutputCompilation { get; }
        internal ImmutableArray<Diagnostic> Diagnostics { get; }
        internal string GeneratedSource { get; }
    }
}

internal static class TestAssert
{
    internal static void True(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Expected condition to be true.");
    }

    internal static void False(bool condition) => True(!condition);

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void NotEqual<T>(T unexpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"Did not expect {actual}.");
    }
}
