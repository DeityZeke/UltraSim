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
        Run(nameof(QueryFamilyMarker_EmitsDeterministicTwoReadOneWriteFamily), QueryFamilyMarker_EmitsDeterministicTwoReadOneWriteFamily);
        Run(nameof(QueryFamilyCatalog_EmitsTwoReadOnlyFamily), QueryFamilyCatalog_EmitsTwoReadOnlyFamily);
        Run(nameof(QueryFamilyCatalog_EmitsOneReadTwoWriteFamily), QueryFamilyCatalog_EmitsOneReadTwoWriteFamily);
        Run(nameof(QueryFamilyCatalog_EmitsRepresentativeWideFamilies), QueryFamilyCatalog_EmitsRepresentativeWideFamilies);
        Run(nameof(QueryFamilyCatalog_EncodesReadOnlyAndWritableSpans), QueryFamilyCatalog_EncodesReadOnlyAndWritableSpans);
        Run(nameof(QueryFamilyCatalog_EmitsTrackedWriteStampsOnlyForWritableFamilies), QueryFamilyCatalog_EmitsTrackedWriteStampsOnlyForWritableFamilies);
        Run(nameof(QueryFamilyCatalog_EmitsTwoReadTwoWatchedAnyAllFamily), QueryFamilyCatalog_EmitsTwoReadTwoWatchedAnyAllFamily);
        Run(nameof(QueryFamilyCatalog_EmitsPublicSerialJobsFromTheSameFamilySpec), QueryFamilyCatalog_EmitsPublicSerialJobsFromTheSameFamilySpec);
        Run(nameof(QueryFamilyCatalog_EmitsSchedulerAdaptersAndMissingFamilies), QueryFamilyCatalog_EmitsSchedulerAdaptersAndMissingFamilies);
        Run(nameof(QueryFamilyCatalog_EmitsExecutionGuidanceDocumentation), QueryFamilyCatalog_EmitsExecutionGuidanceDocumentation);
        Run(nameof(QueryFamilyCatalog_EmitsPublicTopologyFilterOverloads), QueryFamilyCatalog_EmitsPublicTopologyFilterOverloads);
        Run(nameof(QueryFamilyGeneration_IsExplicitlyOptIn), QueryFamilyGeneration_IsExplicitlyOptIn);

        Console.WriteLine($"All {_passed} ECS generator tests passed.");
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

    private static void QueryFamilyMarker_EmitsDeterministicTwoReadOneWriteFamily()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation first = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        Generation second = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(first);
        AssertNoGeneratorErrors(second);
        TestAssert.Equal(9, first.GeneratedSourceCount);
        TestAssert.Equal(9, second.GeneratedSourceCount);
        TestAssert.Equal(first.GeneratedSource, second.GeneratedSource);
        TestAssert.True(first.GeneratedSource.Contains(
            "IComponentChunkReadReadWriteVisitor<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(first.GeneratedSource.Contains(
            "ArchetypeReadReadWriteQueryPlan<TArena, TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(first.GeneratedSource.Contains(
            "PrepareReadReadWriteQuery<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(first.GeneratedSource.Contains(
            "ArchetypeQueryRowFilter.Bind(",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsExecutionGuidanceDocumentation()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.True(generation.GeneratedSource.Contains(
            "Use when ECS component composition selects the rows.",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "use ComponentLookup only for sparse referenced entities.",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "tracked registration for conditional/sparse actual writes.",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "Only mutations through tracked wrappers publish exact touched rows.",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsPublicTopologyFilterOverloads()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadWriteJob<TRead1, TRead2, TWrite>(in EcsQueryFilter filter)",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "filter.GetFilter(Id, nameof(filter))",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "including storage-free tags, are selection predicates",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "no membership predicate runs per row",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsTwoReadOnlyFamily()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "IComponentChunkReadReadVisitor<TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "ArchetypeReadReadQueryPlan<TArena, TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadQuery<TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "VisitReadReadChunks<TRead1, TRead2, TVisitor>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.ReadOnlySpan<TRead1> ReadComponents1 { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.ReadOnlySpan<TRead2> ReadComponents2 { get; }",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "global::System.Span<TRead1> ReadComponents1 { get; }",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "void SetRead1Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "void SetRead2Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyGeneration_IsExplicitlyOptIn()
    {
        Generation generation = Generate(
            "internal sealed class NoQueryFamilyMarker { }",
            new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(0, generation.GeneratedSourceCount);
        TestAssert.Equal(string.Empty, generation.GeneratedSource);
    }

    private static void QueryFamilyCatalog_EmitsOneReadTwoWriteFamily()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "IComponentChunkReadWriteWriteVisitor<TRead, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "ArchetypeReadWriteWriteQueryPlan<TArena, TRead, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadWriteWriteQuery<TRead, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "VisitReadWriteWriteChunks<TRead, TWrite1, TWrite2, TVisitor>",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EncodesReadOnlyAndWritableSpans()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.ReadOnlySpan<TRead> ReadComponents { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.Span<TWrite1> WriteComponents1 { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.Span<TWrite2> WriteComponents2 { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "void SetWrite1Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "void SetWrite2Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "void SetReadEnabled(int row, bool enabled)",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsRepresentativeWideFamilies()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "IComponentChunkReadReadReadWriteVisitor<TRead1, TRead2, TRead3, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "ArchetypeReadReadReadWriteQueryPlan<TArena, TRead1, TRead2, TRead3, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadReadWriteQuery<TRead1, TRead2, TRead3, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "IComponentChunkReadReadWriteWriteVisitor<TRead1, TRead2, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "ArchetypeReadReadWriteWriteQueryPlan<TArena, TRead1, TRead2, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadWriteWriteQuery<TRead1, TRead2, TWrite1, TWrite2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.ReadOnlySpan<TRead3> ReadComponents3 { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "global::System.Span<TWrite2> WriteComponents2 { get; }",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "void SetWrite2Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "void SetRead3Enabled(int row, bool enabled)",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsTrackedWriteStampsOnlyForWritableFamilies()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.True(generation.GeneratedSource.Contains(
            "ChangeVersion writeVersion = default;",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "store.StampDenseChangeVersion(",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "route.Storage.Store.StampFilteredChangeVersion(",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "route.Write1ColumnIndex,",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "route.Write2ColumnIndex,",
            StringComparison.Ordinal));

        int readOnlyStart = generation.GeneratedSource.IndexOf(
            "IComponentChunkReadReadVisitor<TRead1, TRead2>",
            StringComparison.Ordinal);
        int nextSource = generation.GeneratedSource.IndexOf(
            "// <auto-generated />",
            readOnlyStart + 1,
            StringComparison.Ordinal);
        TestAssert.True(readOnlyStart >= 0);
        TestAssert.True(nextSource > readOnlyStart);
        string readOnlyFamily = generation.GeneratedSource[readOnlyStart..nextSource];
        TestAssert.False(readOnlyFamily.Contains("ChangeVersion", StringComparison.Ordinal));
        TestAssert.False(readOnlyFamily.Contains("StampDenseChangeVersion", StringComparison.Ordinal));
        TestAssert.False(readOnlyFamily.Contains("StampFilteredChangeVersion", StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsTwoReadTwoWatchedAnyAllFamily()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "ArchetypeReadReadChangedQueryPlan<TArena, TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadChangedQuery<TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "PrepareReadReadChangedRowsQuery<TRead1, TRead2>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "VisitReadReadChangedChunks<TRead1, TRead2, TVisitor>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "VisitReadReadChangedRows<TRead1, TRead2, TVisitor>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "matchMode == ChangedMatchMode.Any",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "? first | second",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            ": first & second;",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "changed1ComponentId == changed2ComponentId",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "EnableSparseRowChangeTracking(changed1ColumnIndex)",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "EnableSparseRowChangeTracking(changed2ColumnIndex)",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsPublicSerialJobsFromTheSameFamilySpec()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.True(generation.GeneratedSource.Contains(
            "public interface IReadReadWriteChunkJob<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public interface ITrackedReadReadWriteChunkJob<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public readonly struct ReadReadWriteJobPlan<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public readonly ref struct ReadReadWriteComponentChunk<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public readonly ref struct TrackedReadReadWriteComponentChunk<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public int ExecuteJob<TRead1, TRead2, TWrite, TJob>(",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "public int ExecuteTrackedJob<TRead1, TRead2, TWrite, TJob>(",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "where TJob : struct, IReadReadWriteChunkJob<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "where TJob : struct, ITrackedReadReadWriteChunkJob<TRead1, TRead2, TWrite>",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "public NativeChunkArena",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "public ArchetypeChunkStore",
            StringComparison.Ordinal));
        TestAssert.False(generation.GeneratedSource.Contains(
            "public ChunkRow",
            StringComparison.Ordinal));
    }

    private static void QueryFamilyCatalog_EmitsSchedulerAdaptersAndMissingFamilies()
    {
        const string source = "[assembly: UltraSim.ECS.EcsQueryFamilies] internal sealed class Marker { }";
        Generation generation = Generate(source, new EcsQueryFamilyGenerator().AsSourceGenerator());
        AssertNoGeneratorErrors(generation);
        TestAssert.Equal(9, generation.GeneratedSourceCount);
        TestAssert.True(generation.GeneratedSource.Contains(
            "IScheduledReadChunkJob<TRead>", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "IScheduledWriteChunkJob<TWrite>", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "IScheduledReadReadReadChunkJob<TRead1, TRead2, TRead3>", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "RegisterReadSystem<TRead, TSystem, TJob>", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "RegisterWriteSystem<TWrite, TSystem, TJob>", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "RegisterReadReadReadSystem<TRead1, TRead2, TRead3, TSystem, TJob>",
            StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "SchedulerChunkPartition", StringComparison.Ordinal));
        TestAssert.True(generation.GeneratedSource.Contains(
            "SchedulerChangePublication", StringComparison.Ordinal));
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

    private static Generation Generate(string source) =>
        Generate(source, new EcsSchemaGenerator().AsSourceGenerator());

    private static Generation Generate(string source, ISourceGenerator generator)
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> driverDiagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();
        ImmutableArray<Diagnostic> diagnostics = result.Diagnostics.AddRange(driverDiagnostics).Distinct().ToImmutableArray();
        GeneratedSourceResult[] generatedSources = result.Results
            .SelectMany(static generatorResult => generatorResult.GeneratedSources)
            .OrderBy(static generated => generated.HintName, StringComparer.Ordinal)
            .ToArray();
        string generatedSource = string.Join(
            "\n",
            generatedSources.Select(static generated => generated.SourceText.ToString()));
        return new Generation(outputCompilation, diagnostics, generatedSource, generatedSources.Length);
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
            string generatedSource,
            int generatedSourceCount)
        {
            OutputCompilation = outputCompilation;
            Diagnostics = diagnostics;
            GeneratedSource = generatedSource;
            GeneratedSourceCount = generatedSourceCount;
        }

        internal Compilation OutputCompilation { get; }
        internal ImmutableArray<Diagnostic> Diagnostics { get; }
        internal string GeneratedSource { get; }
        internal int GeneratedSourceCount { get; }
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
